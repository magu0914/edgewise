using System;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEngine;

namespace Edgewise.Game.Match
{
    /// <summary>
    /// エンジンのイベント列を演出に変換して順番に再生する（DESIGN §8-1 アーキテクチャ）。
    /// 変換は PlaybackPlanner（純関数）、再生はここ。表示側が勝手にルールを判断しない:
    /// どのカードが・いつ・誰の色に裏返るかはすべてイベントに書いてある。
    /// 再生速度（×1 / ×2 / 即時）は timeFactor で一括して効かせる（即時 = 0 で、待ちなしで最後の状態にする）。
    /// </summary>
    public sealed class EventPlayer
    {
        readonly BoardView _board;
        readonly HandView[] _hands;
        readonly HudView _hud;
        readonly BannerView _banner;
        readonly JuiceSettings _juice;
        readonly Func<float> _timeFactor;

        public IMatchFx Fx { get; set; } = NullMatchFx.Instance;

        /// <summary>ワールド座標 → 画面上の点（文字演出をカードの上に出すのに使う）。null なら画面中央。</summary>
        public Func<Vector3, Vector2> WorldToScreen { get; set; }

        /// <summary>置いたカードの少し上（画面上の点）。</summary>
        Vector2? AboveCell(int cell, float extra = 0f)
        {
            if (WorldToScreen == null || cell < 0) return null;
            return WorldToScreen(_board.CellPosition(cell) + new Vector3(0f, CardView.Size.y * 0.5f + 0.18f + extra, 0f));
        }

        /// <summary>最後に再生したステップ（テスト・デバッグ用）。</summary>
        public IReadOnlyList<PlaybackStep> LastSteps { get; private set; } = new List<PlaybackStep>();

        public bool IsPlaying { get; private set; }

        public static readonly Color LinkColor = new Color(1f, 0.85f, 0.42f) * 2.2f;

        public EventPlayer(BoardView board, HandView[] hands, HudView hud, BannerView banner, JuiceSettings juice, Func<float> timeFactor)
        {
            _board = board;
            _hands = hands;
            _hud = hud;
            _banner = banner;
            _juice = juice;
            _timeFactor = timeFactor;
        }

        float Tf => Mathf.Max(0f, _timeFactor());

        /// <summary>1 手分のイベント列を再生する。before = 手を指す前の局面（カード定義・マス属性の参照に使う）。</summary>
        public async Awaitable PlayAsync(GameState before, Move move, IReadOnlyList<GameEvent> events, CancellationToken ct)
        {
            var steps = PlaybackPlanner.Build(events);
            LastSteps = steps;
            IsPlaying = true;
            int placedCell = move.Cell;
            var links = new List<MeshRenderer>();
            _banner.LocalScale = _juice.ruleTextScale;
            try
            {
                foreach (var step in steps)
                {
                    if (AutomationLog.Enabled) AutomationLog.Write("step " + step);
                    switch (step.Kind)
                    {
                        case StepKind.Place:
                            placedCell = step.Cell;
                            await PlaceAsync(before, move, step, ct);
                            break;
                        case StepKind.RuleLink:
                            await LinkAsync(placedCell, step, links, ct);
                            break;
                        case StepKind.RuleBanner:
                            Fx.OnRule(step.Rule);
                            await _banner.Show(RuleText(step.Rule), Theme.Gold, _juice.ruleBannerTime * Tf, ct, null, 1f, AboveCell(placedCell));
                            FadeLinks(links);
                            break;
                        case StepKind.ComboBanner:
                            await Tweens.Delay(_juice.comboStepWait * Tf, ct);
                            Fx.OnCombo(step.ComboDepth);
                            // 段が深いほど大きく
                            _banner.Play(ComboText(step.ComboDepth), new Color(1f, 0.62f, 0.85f), _juice.comboBannerTime * Tf, null,
                                1f + _juice.comboTextGrowth * (step.ComboDepth - 1), AboveCell(placedCell));
                            break;
                        case StepKind.FlipGroup:
                            await FlipGroupAsync(step, ct);
                            break;
                        case StepKind.SuddenDeath:
                            await _banner.Show(UiText.BannerSuddenDeath, new Color(1f, 0.5f, 0.45f), _juice.infoBannerTime * Tf, ct,
                                "ROUND " + (step.Round + 1));
                            break;
                        case StepKind.End:
                        case StepKind.Turn:
                            break;
                    }
                }
            }
            finally
            {
                foreach (var l in links)
                {
                    if (l != null) l.gameObject.SetActive(false);
                }
                IsPlaying = false;
            }
        }

        public static string RuleText(RuleHeading rule) =>
            rule == RuleHeading.Same ? UiText.BannerSame : rule == RuleHeading.Plus ? UiText.BannerPlus : UiText.BannerSameWall;

        /// <summary>連鎖の段 1 は「COMBO!」、段 n（2 以上）は「COMBO ×n」。</summary>
        public static string ComboText(int depth) => depth <= 1 ? UiText.BannerCombo : UiText.BannerComboTimes + depth;

        async Awaitable PlaceAsync(GameState before, Move move, PlaybackStep step, CancellationToken ct)
        {
            float tf = Tf;
            var hand = _hands[(int)step.Player];
            var card = hand.TakeAt(move.HandIndex);
            var def = before.Cards[step.CardId];
            bool reveal = card.FaceDown;
            bool spin = step.Rotation != 0 && card.Rotation != step.Rotation; // CPU が回して置くときは回転を見せる
            card.Bind(def, step.Player, step.Rotation, reveal);
            card.SetSelect(0f);
            card.SetGlow(0f, Color.white);
            card.SetAlpha(1f);
            card.SetOrder(60);
            card.SetTiltTarget(Vector2.zero);
            hand.Layout(true, tf);

            var from = card.transform.position;
            var to = _board.CellPosition(step.Cell);
            var fromScale = card.transform.localScale;
            var fromRot = card.transform.rotation;
            float startZ = spin ? 90f * step.Rotation : 0f;
            bool revealed = !reveal;
            await Tweens.Run(_juice.placeMoveTime * tf, k =>
            {
                var p = Vector3.LerpUnclamped(from, to, k);
                p.z = Mathf.Lerp(from.z, 0f, k) - 0.25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)) * (reveal ? 1f : 0f);
                card.transform.position = p;
                card.transform.localScale = Vector3.LerpUnclamped(fromScale, Vector3.one, k);
                var rot = Quaternion.SlerpUnclamped(fromRot, Quaternion.identity, Mathf.Clamp01(k));
                if (spin) rot = Quaternion.Euler(0f, 0f, Mathf.Lerp(startZ, 0f, k));
                if (reveal)
                {
                    // 伏せた CPU のカードは、運ぶ途中で表に返す（Y 軸回転。90° の時点で表にする）
                    float y = k < 0.5f ? 180f * k : -180f * (1f - k);
                    if (k >= 0.5f && !revealed)
                    {
                        revealed = true;
                        card.SetFaceDown(false);
                    }
                    rot = rot * Quaternion.Euler(0f, y, 0f);
                }
                card.transform.rotation = rot;
                card.SetLift(Mathf.Lerp(0.6f, 0f, k));
            }, ct, Ease.OutCubic, 0f, card, "move");
            if (!revealed) card.SetFaceDown(false);
            card.transform.SetPositionAndRotation(to, Quaternion.identity);
            card.BaseScale = 1f;
            card.SetLift(0f);
            card.ResetTilt(); // 盤上のカードは傾けない（盤面が揺れて見えないように）

            _board.Placed[step.Cell] = card;
            card.SetOrder(10 + step.Cell);
            var cellElem = before.CellElements[step.Cell];
            card.SetModifier(Board.ElementalModifier(before.Rules, cellElem, def));
            Fx.OnPlace(step.Cell);

            // 着地: 縦横に一瞬つぶれて戻る・光の輪・白い光
            var burst = _board.NextBurst(step.Cell);
            var color = Theme.OwnerColor(step.Player);
            Tweens.Play(_juice.landBurstTime * tf, k => _board.SetBurst(burst, color, k), Ease.OutQuad);
            Tweens.Play(0.18f * tf, k => card.SetFlash(1f - k), Ease.OutQuad, 0f, card, "flash");
            var sq = _juice.landSquash;
            await Tweens.Run(_juice.landSquashTime * tf, k =>
            {
                float sx = Mathf.LerpUnclamped(sq.x, 1f, k);
                float sy = Mathf.LerpUnclamped(sq.y, 1f, k);
                card.transform.localScale = new Vector3(sx, sy, 1f);
            }, ct, Ease.OutBack, 0f, card, "scale");
            card.transform.localScale = Vector3.one;
        }

        async Awaitable LinkAsync(int placedCell, PlaybackStep step, List<MeshRenderer> links, CancellationToken ct)
        {
            float tf = Tf;
            var created = new List<MeshRenderer>();
            for (int i = 0; i < step.Cells.Length && links.Count < 4; i++)
            {
                var r = _board.ShowLink(links.Count, placedCell, step.Cells[i], LinkColor);
                if (r == null) continue;
                links.Add(r);
                created.Add(r);
            }
            await Tweens.Run(_juice.ruleLinkTime * tf, k =>
            {
                foreach (var r in created) _board.SetLinkAlpha(r, LinkColor, Mathf.Clamp01(k * 1.4f));
            }, ct, Ease.OutQuad);
        }

        void FadeLinks(List<MeshRenderer> links)
        {
            var list = new List<MeshRenderer>(links);
            Tweens.Play(0.2f * Tf, k =>
            {
                foreach (var r in list)
                {
                    if (r != null && r.gameObject.activeSelf) _board.SetLinkAlpha(r, LinkColor, 1f - k, true);
                }
            }, Ease.Linear);
        }

        /// <summary>裏返った瞬間の光（白い光と光の輪）。連鎖の段が深いほど強く大きくする。</summary>
        void FlipLight(CardView card, FlipStep f, float tf)
        {
            if (tf <= 0f) return;
            float boost = 1f + _juice.comboLightPerDepth * f.ChainDepth;
            Tweens.Play(_juice.flipFlashTime * tf, k => card.SetFlash((1f - k) * Mathf.Min(1f, 0.6f * boost)), Ease.OutQuad, 0f, card, "flash");
            var burst = _board.NextBurst(f.Cell);
            var color = Theme.OwnerColor(f.To) * boost;
            float size = 1f + 0.25f * f.ChainDepth;
            Tweens.Play(_juice.landBurstTime * tf, k => _board.SetBurst(burst, color, k, size), Ease.OutQuad);
        }

        async Awaitable FlipGroupAsync(PlaybackStep step, CancellationToken ct)
        {
            float tf = Tf;
            var tweens = new List<Tween>();
            for (int i = 0; i < step.Flips.Length; i++)
            {
                var f = step.Flips[i];
                var card = _board.Placed[f.Cell];
                if (card == null) continue;
                var cellPos = _board.CellPosition(f.Cell);
                var fromPos = f.From >= 0 ? _board.CellPosition(f.From) : cellPos;
                var dir = cellPos - fromPos;
                dir.z = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
                dir.Normalize();
                // 回転軸は「取った側から取られた側への向き」に直交（押し倒されるように回る）
                var axis = Vector3.Cross(dir, Vector3.forward);
                bool swapped = false;
                var to = f.To;
                int order = 50 + i;
                tweens.Add(Tweens.Play(_juice.flipTime * tf, k =>
                {
                    float angle = 180f * k;
                    if (!swapped && angle >= 90f)
                    {
                        swapped = true;
                        card.SetOwner(to);
                        _hud.AddScore(to, +1, _juice, tf);
                        _hud.AddScore(to.Opponent(), -1, _juice, tf);
                        Fx.OnFlip(f.Cell, to, f.IndexInMove, f.ChainDepth);
                        FlipLight(card, f, tf);
                    }
                    if (k > 0f && k < 1f) card.SetOrder(order);
                    float a = swapped ? angle - 180f : angle;
                    card.transform.rotation = Quaternion.AngleAxis(a, axis);
                    var p = cellPos;
                    p.z = -_juice.flipLift * Mathf.Sin(Mathf.PI * k);
                    card.transform.position = p;
                    card.SetLift(0.5f * Mathf.Sin(Mathf.PI * k));
                }, Ease.InOutQuad, i * _juice.flipStagger * tf, card, "flip"));
            }
            await Tweens.AwaitAll(tweens, ct);
            foreach (var f in step.Flips)
            {
                var card = _board.Placed[f.Cell];
                if (card == null) continue;
                card.transform.SetPositionAndRotation(_board.CellPosition(f.Cell), Quaternion.identity);
                card.SetOrder(10 + f.Cell);
                card.SetLift(0f);
                if (card.Owner != f.To) card.SetOwner(f.To);
            }
        }
    }
}
