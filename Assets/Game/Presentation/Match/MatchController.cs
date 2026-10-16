using System;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.AI;
using Edgewise.Game.Audio;
using Edgewise.Game.App;
using Edgewise.Game.Controls;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEngine;

namespace Edgewise.Game.Match
{
    /// <summary>対戦の結果（席 0 から見た勝敗）。</summary>
    public sealed class MatchOutcome
    {
        public (int P0, int P1) Score;
        public GameResult Result;
        public bool Resigned;
        public int Moves;
    }

    /// <summary>
    /// 対戦の進行役（DESIGN §8-1 アーキテクチャ）。GameState を持つ唯一の場所。
    /// 手番ごとにプレイヤー（PlayerController）か CPU（AiDriver）から Move を受け取り、GameEngine.ApplyMove を呼んで、
    /// 返ったイベント列を EventPlayer に渡して再生させる。
    /// </summary>
    public sealed class MatchController : MonoBehaviour
    {
        [SerializeField] GameAssets assets;
        [SerializeField] BoardView board;
        [SerializeField] HandView hand0;
        [SerializeField] HandView hand1;
        [SerializeField] HudView hud;
        [SerializeField] BannerView banner;
        [SerializeField] TableLayout layout;
        [SerializeField] PlayerController player;
        [SerializeField] Transform cardRoot;
        [SerializeField] JuiceFx fx;

        readonly List<CardView> _pool = new List<CardView>();
        readonly AiDriver _ai = new AiDriver();
        EventPlayer _eventPlayer;
        uint _rng;
        bool _resignRequested;
        readonly HintEngine _hint = new HintEngine();
        bool _hintShown;
        int _tokensMax0 = GameConstants.PivotTokens;
        int _tokensMax1 = GameConstants.PivotTokens;

        public GameState State { get; private set; }
        public MatchSettings Settings { get; private set; }
        public EventPlayer EventPlayer => _eventPlayer;
        public PlayerController Controls => player;
        public BoardView Board => board;
        public HandView Hand(Player p) => p == Player.P0 ? hand0 : hand1;
        public bool IsRunning { get; private set; }
        public bool CpuThinking { get; private set; }
        public int MovesPlayed { get; private set; }
        public IMatchFx Fx => fx != null ? (IMatchFx)fx : NullMatchFx.Instance;
        public JuiceFx JuiceFx => fx;
        public HintEngine Hint => _hint;

        /// <summary>直近に表示したヒント（テスト用）。</summary>
        public HintResult? ShownHint { get; private set; }

        public void Wire(GameAssets a, BoardView b, HandView h0, HandView h1, HudView h, BannerView bn, TableLayout l, PlayerController p, Transform cards,
            JuiceFx juiceFx)
        {
            fx = juiceFx;
            assets = a;
            board = b;
            hand0 = h0;
            hand1 = h1;
            hud = h;
            banner = bn;
            layout = l;
            player = p;
            cardRoot = cards;
        }

        JuiceSettings Juice => assets.juice != null ? assets.juice : (_fallbackJuice ??= JuiceSettings.CreateDefault());
        JuiceSettings _fallbackJuice;

        static float Tf => UserPrefs.TimeFactor;

        void Awake()
        {
            player.Configure(board, hand0, layout, Juice, Fx);
            _eventPlayer = new EventPlayer(board, new[] { hand0, hand1 }, hud, banner, Juice, () => UserPrefs.TimeFactor)
            {
                Fx = Fx,
                WorldToScreen = w => layout.TableToScreen(w),
            };
        }

        void Update()
        {
            if (!_hint.IsRunning) return;
            if (State == null || !ReferenceEquals(_hint.State, State) || !player.IsAwaitingMove)
            {
                _hint.Cancel();
                return;
            }
            if (_hint.Tick(Juice.hintFrameBudgetMs)) ShowHint(_hint.Result);
        }

        // ------------------------------------------------------------------
        // ヒント（DESIGN §8-1 操作: ボタン 1 つで AnalyzeMoves を回し、最善手のカードとマスを光らせて「予想 6-4」）
        // ------------------------------------------------------------------

        /// <summary>プレイヤーの手番ならヒントの探索を始める（数フレームに分けて読む）。</summary>
        public bool RequestHint()
        {
            if (State == null || !player.IsAwaitingMove || _hint.IsRunning) return false;
            ClearHint();
            var camp = Settings?.campaign;
            // 星図巡り: 同じカードを両者が持ちうるので Shared。候補は相手の持ちカード（すべて見ていれば）か全カード
            if (camp != null) _hint.Begin(State, State.Turn, Juice.hintNodeBudget, 0x48494e54L + MovesPlayed, camp.HintPool, CardPoolModel.Shared);
            else _hint.Begin(State, State.Turn, Juice.hintNodeBudget, 0x48494e54L + MovesPlayed);
            hud.SetHintText(UiText.HintThinking);
            if (AutomationLog.Enabled) AutomationLog.Write("hint begin");
            return true;
        }

        void ShowHint(HintResult r)
        {
            ShownHint = r;
            _hintShown = true;
            var hand = Hand(State.Turn);
            if (r.Move.HandIndex < hand.Cards.Count) hand.Cards[r.Move.HandIndex].SetGlow(1f, Theme.HintGlow);
            board.ShowHint(r.Move.Cell, Theme.HintGlow);
            string text = UiText.HintForecast + " " + r.Mine + "-" + r.Theirs + (r.Exact ? UiText.HintExact : UiText.HintApprox);
            if (r.Move.Rotation != 0) text += "・" + (r.Move.Rotation * 90) + "°" + UiText.HintRotate;
            hud.SetHintText(text);
            if (AutomationLog.Enabled)
            {
                AutomationLog.Write("hint " + r.Move.HandIndex + ":" + r.Move.Cell + " r" + r.Move.Rotation + " " + r.Mine + "-" + r.Theirs + (r.Exact ? " exact" : " approx"));
                AutomationLog.Write("cell-hint " + r.Move.Cell);
            }
        }

        void ClearHint()
        {
            _hint.Cancel();
            hud.SetHintText("");
            if (!_hintShown) return;
            _hintShown = false;
            foreach (var h in new[] { hand0, hand1 })
                foreach (var card in h.Cards)
                    if (card != null) card.SetGlow(0f, Color.white);
            board.ShowHint(-1, Color.white);
        }

        /// <summary>降参する（CPU の手番なら、その手の再生後に終える）。</summary>
        public void Resign()
        {
            _resignRequested = true;
            player.Resign();
        }

        public async Awaitable<MatchOutcome> RunAsync(MatchSettings settings, CancellationToken ct)
        {
            Settings = settings;
            IsRunning = true;
            _resignRequested = false;
            MovesPlayed = 0;
            try
            {
                long seed;
                RuleSet rules;
                if (settings.campaign != null)
                {
                    // 星図巡り: 星域のルール・両者のデッキ・seed は CampaignRules.MatchConfig が決める
                    var cfg = settings.campaign.Config;
                    seed = cfg.Seed;
                    rules = cfg.Rules;
                    State = GameEngine.CreateGame(cfg);
                }
                else
                {
                    seed = settings.seed != 0 ? settings.seed : (DateTime.Now.Ticks & 0x7fffffffL) | 1L;
                    rules = settings.ToRuleSet();
                    var (d0, d1) = Dealer.Deal(settings.band, settings.random, seed);
                    // Pivot のトークンは先攻・後攻で非対称（Phase 3c の自己対戦で決めた値。docs/BALANCE.md）
                    var tokens = GameConstants.MatchPivotTokens(rules);
                    State = GameEngine.CreateGame(new GameConfig(rules, d0, d1, seed, CardCatalog.ById, tokens.First, tokens.Second));
                }
                _rng = Rng.SeedToState(seed ^ 0x2545F491L);
                _tokensMax0 = State.GetPivotTokens(Player.P0);
                _tokensMax1 = State.GetPivotTokens(Player.P1);
                if (AutomationLog.Enabled) AutomationLog.Write("match seed=" + seed + " rules=" + rules.ToBitString() + " first=" + (int)State.FirstPlayer);

                await SetupViewsAsync(ct);
                bool resigned = false;

                while (State.Phase == Phase.Playing)
                {
                    UpdateHud();
                    Move move;
                    var turn = State.Turn;
                    if (settings.Seat(turn) == SeatKind.Human)
                    {
                        hud.SetTurn(UiText.YourTurn, Theme.OwnerColor(turn));
                        hud.ShowHintButton(true);
                        AutomationLog.Buttons("Hud", hud.transform);
                        Move? r;
                        try
                        {
                            r = await player.WaitForMoveAsync(State, turn, ct);
                        }
                        finally
                        {
                            hud.ShowHintButton(false);
                            ClearHint();
                        }
                        if (r == null)
                        {
                            resigned = true;
                            break;
                        }
                        move = r.Value;
                    }
                    else
                    {
                        hud.SetTurn(UiText.CpuThinking, Theme.OwnerColor(turn));
                        move = await ThinkAsync(ct);
                        hud.SetTurn(UiText.CpuTurn, Theme.OwnerColor(turn));
                    }

                    var before = State;
                    var result = GameEngine.ApplyMove(before, move);
                    State = result.State;
                    MovesPlayed++;
                    await _eventPlayer.PlayAsync(before, move, result.Events, ct);
                    if (State.SuddenDeathRound != before.SuddenDeathRound) await RebuildForSuddenDeathAsync(ct);
                    SyncScores();
                    if (_resignRequested && State.Phase == Phase.Playing)
                    {
                        resigned = true;
                        break;
                    }
                }

                UpdateHud();
                hud.SetTurn(UiText.MatchOver, Theme.Ink);
                var score = GameEngine.Score(State);
                var outcome = new MatchOutcome
                {
                    Score = score,
                    Result = resigned ? (Settings.seat0 == SeatKind.Human ? GameResult.P1 : GameResult.P0) : GameEngine.ResultOfScore(score),
                    Resigned = resigned,
                    Moves = MovesPlayed,
                };
                if (AutomationLog.Enabled) AutomationLog.Write("end " + score.P0 + "-" + score.P1 + " " + outcome.Result.ToId() + (resigned ? " resigned" : ""));
                return outcome;
            }
            finally
            {
                IsRunning = false;
                CpuThinking = false;
            }
        }

        async Awaitable<Move> ThinkAsync(CancellationToken ct)
        {
            CpuThinking = true;
            try
            {
                long seed = Rng.NextUInt32(_rng, out _rng);
                // 星図巡りの相手は性格つきの AI。同じカードを両者が持ちうるので決定化は Shared（候補は全カード。DESIGN §12-6）
                if (Settings.campaign != null && State.Turn == Player.P1)
                    _ai.Begin(State, State.Turn, Settings.campaign.Profile, seed, CardCatalog.All, CardPoolModel.Shared);
                else _ai.Begin(State, State.Turn, Settings.ProfileFor(State.Turn), seed, CardCatalog.All);
                float start = Time.unscaledTime;
                float min = Juice.cpuMinThink * Tf;
                // 考えている間、手札の 1 枚がふわっと浮いたり戻ったりする（1 回ごとに別のカードへ）
                var hand = Hand(State.Turn);
                bool bob = Tf > 0f && hand.Cards.Count > 0;
                int bobIndex = -1;
                float bobT = 0f;
                uint bobRng = (uint)(MovesPlayed * 2654435761L + 12345);
                bool done = _ai.Tick();
                while (!done || Time.unscaledTime - start < min)
                {
                    if (bob)
                    {
                        float period = Mathf.Max(0.2f, Juice.cpuBobPeriod);
                        if (bobIndex < 0 || bobT >= period)
                        {
                            if (bobIndex >= 0) hand.Bob(bobIndex, 0f);
                            int n = hand.Cards.Count;
                            int next = Rng.NextInt(bobRng, n, out bobRng);
                            if (n > 1 && next == bobIndex) next = (next + 1) % n;
                            bobIndex = next;
                            bobT = 0f;
                        }
                        hand.Bob(bobIndex, Juice.cpuBobHeight * Mathf.Sin(Mathf.PI * bobT / period));
                        bobT += Time.unscaledDeltaTime;
                    }
                    await Awaitable.NextFrameAsync(ct);
                    if (!done) done = _ai.Tick();
                }
                if (bob)
                {
                    if (bobIndex >= 0) hand.Bob(bobIndex, 0f);
                    hand.Layout(true, Tf);
                }
                return _ai.BestMove;
            }
            finally
            {
                CpuThinking = false;
            }
        }

        /// <summary>
        /// 決着の演出: 盤上のカードが勝者の色に順に光る（引き分けは金色）。結果画面（カウントアップ → 勝敗の文字）の前に再生する。
        /// </summary>
        public async Awaitable PlayFinaleAsync(MatchOutcome outcome, CancellationToken ct)
        {
            float tf = Tf;
            bool draw = outcome.Result == GameResult.Draw;
            var winner = outcome.Result == GameResult.P1 ? Player.P1 : Player.P0;
            var color = draw ? Theme.Gold : Theme.OwnerColor(winner);
            int lit = 0;
            for (int c = 0; c < board.Placed.Length; c++)
            {
                var card = board.Placed[c];
                if (card == null || (!draw && card.Owner != winner)) continue;
                card.SetGlow(1f, color);
                if (tf > 0f)
                {
                    var cv = card;
                    Tweens.Play(0.25f * tf, k => cv.SetFlash((1f - k) * 0.8f), Ease.OutQuad, 0f, cv, "flash");
                    SfxBank.Instance?.Play(Edgewise.Game.Audio.SfxKind.Cursor, lit);
                    lit++;
                    await Tweens.Delay(Juice.finaleStep * tf, ct);
                }
            }
            await Tweens.Delay(Juice.finaleHold * tf, ct);
        }

        bool FaceDown(Player p) =>
            !State.Rules.Open && Settings.Seat(p) == SeatKind.Cpu && Settings.Seat(p.Opponent()) == SeatKind.Human;

        void EnsurePool(int n)
        {
            while (_pool.Count < n)
            {
                var card = Instantiate(assets.cardPrefab, cardRoot);
                card.name = "Card" + _pool.Count;
                _pool.Add(card);
            }
        }

        async Awaitable SetupViewsAsync(CancellationToken ct)
        {
            float tf = Tf;
            EnsurePool(GameConstants.HandSize * 2);
            board.ClearPlaced();
            board.HideLinks();
            board.HideBadges();
            board.ShowCursor(-1, Color.white, false);
            board.SetCellElements(State.CellElements);
            banner.Hide();
            hand0.Cards.Clear();
            hand1.Cards.Clear();
            hand0.Held = null;
            hand1.Held = null;
            int k = 0;
            var tweens = new List<Tween>();
            for (int p = 0; p < 2; p++)
            {
                var who = (Player)p;
                var hand = Hand(who);
                var ids = State.GetHand(who);
                for (int i = 0; i < ids.Count; i++)
                {
                    var card = _pool[k++];
                    card.gameObject.SetActive(true);
                    card.Bind(State.Cards[ids[i]], who, 0, FaceDown(who));
                    card.SetModifier(0);
                    card.SetGlow(0f, Color.white);
                    card.SetCaptureMark(Vector2.zero);
                    card.SetSelect(0f);
                    card.SetAlpha(1f);
                    card.SetLift(0f);
                    card.SetFlash(0f);
                    hand.Cards.Add(card);
                }
            }
            for (; k < _pool.Count; k++) _pool[k].gameObject.SetActive(false);

            layout.Apply();
            for (int p = 0; p < 2; p++)
            {
                var hand = Hand((Player)p);
                for (int i = 0; i < hand.Cards.Count; i++)
                {
                    var card = hand.Cards[i];
                    var target = hand.SlotPosition(i);
                    var from = target + (layout.Portrait ? new Vector3(0f, p == 0 ? -4f : 4f, 0f) : new Vector3(p == 0 ? -4f : 4f, 0f, 0f));
                    card.transform.SetPositionAndRotation(from, Quaternion.identity);
                    card.transform.localScale = Vector3.one * hand.CardScale;
                    card.SetOrder(20 + i);
                    var c = card;
                    tweens.Add(Tweens.Play(Juice.dealTime * tf, t => c.transform.position = Vector3.LerpUnclamped(from, target, t), Ease.OutCubic,
                        (i + p * 2) * Juice.dealStagger * tf, c, "move"));
                }
            }
            var opp = Settings.campaign?.Opponent;
            hud.SetNames(Settings.seat0 == SeatKind.Human ? UiText.You : UiText.Cpu, opp != null ? opp.Name : UiText.Cpu);
            hud.SetOpponent(opp);
            SyncScores();
            UpdateHud();
            await Tweens.AwaitAll(tweens, ct);
            hand0.Layout(false);
            hand1.Layout(false);
            bool youFirst = State.FirstPlayer == Player.P0;
            string first = Settings.seat0 == SeatKind.Human ? (youFirst ? UiText.YouFirst : UiText.CpuFirst) : "P" + (int)State.FirstPlayer + " FIRST";
            await banner.Show(first, Theme.OwnerColor(State.FirstPlayer), Juice.infoBannerTime * tf, ct, null, 0.6f);
        }

        async Awaitable RebuildForSuddenDeathAsync(CancellationToken ct)
        {
            float tf = Tf;
            var available = new List<CardView>();
            for (int c = 0; c < board.Placed.Length; c++)
            {
                if (board.Placed[c] != null) available.Add(board.Placed[c]);
            }
            available.AddRange(hand0.Cards);
            available.AddRange(hand1.Cards);
            board.ClearPlaced();
            hand0.Cards.Clear();
            hand1.Cards.Clear();
            for (int p = 0; p < 2; p++)
            {
                var who = (Player)p;
                var ids = State.GetHand(who);
                for (int i = 0; i < ids.Count; i++)
                {
                    int idx = available.FindIndex(cv => cv.CardId == ids[i]);
                    if (idx < 0) idx = 0;
                    var card = available[idx];
                    available.RemoveAt(idx);
                    card.Bind(State.Cards[ids[i]], who, 0, FaceDown(who));
                    card.SetModifier(0);
                    card.transform.rotation = Quaternion.identity;
                    Hand(who).Cards.Add(card);
                }
            }
            foreach (var extra in available) extra.gameObject.SetActive(false);
            hand0.Layout(true, tf);
            hand1.Layout(true, tf);
            await Tweens.Delay(Juice.dealTime * tf, ct);
        }

        void SyncScores()
        {
            var s = GameEngine.Score(State);
            hud.SetScores(s.P0, s.P1);
        }

        void UpdateHud()
        {
            hud.SetTokens(State.Rules.Pivot, State.GetPivotTokens(Player.P0), State.GetPivotTokens(Player.P1), _tokensMax0, _tokensMax1);
        }
    }
}
