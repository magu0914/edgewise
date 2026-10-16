using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Campaign;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 報酬（DESIGN §12-11 の最重要の演出）と、原作モードで取られる演出。カードはワールド空間の WorldCardStage、文字とボタンは uGUI。
    /// 報酬: 相手が使った 5 枚が裏向きで配られ、浮き沈みする。カーソルを乗せると傾いて浮く。選ぶと手前に出て、ふるえてからめくれ、
    /// 初めてのカードなら金の光と光の輪と「NEW!」（と効果音）、2 枚目以降なら「×2」。少し見せてから手持ちへ弧を描いて飛んでいく。
    /// 完勝なら 2 回選べる。選び終えたら、選ばなかったカードも表にして見せ、「次へ」。
    /// 取られる: 自分の 5 枚が並び、取られるカードが赤く光って相手の方へ去る（短く）。
    /// </summary>
    public sealed class RewardScreen : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] WorldCardStage stage;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI sub;
        [SerializeField] Image handIcon;
        [SerializeField] TextMeshProUGUI handText;
        [SerializeField] RawImage popGlow;
        [SerializeField] TextMeshProUGUI popLabel;
        [SerializeField] TextMeshProUGUI cardName;
        [SerializeField] PortraitView taker;
        [SerializeField] Button nextButton;
        [SerializeField] TextMeshProUGUI introText;
        [SerializeField] Button revealAllButton;

        readonly Queue<int> _requests = new Queue<int>();
        bool _next;
        bool _revealAll;
        int _handCount;
        bool _portrait;

        public bool IsOpen => gameObject.activeSelf;
        public WorldCardStage Stage => stage;
        public Button NextButton => nextButton;
        public string PopText => popLabel.gameObject.activeSelf ? popLabel.text : "";
        public string TitleText => title.text;
        public string SubText => sub.text;
        public int HandCount => _handCount;

        /// <summary>カードを選べる状態か（テスト用。RequestPick が受け付けられる）。</summary>
        public bool WaitingForPick { get; private set; }

        /// <summary>「次へ」を待っているか。</summary>
        public bool WaitingForNext { get; private set; }

        /// <summary>直近にめくったカードの結果（テスト用）。</summary>
        public RewardPick? LastPick { get; private set; }

        public void Build(GameAssets a, WorldCardStage worldStage)
        {
            stage = worldStage;
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            title = UiKit.Label(root, "Title", a, "", 40, Theme.Gold, TextAlignmentOptions.Center, true);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.fontStyle = FontStyles.Bold;
            sub = UiKit.Label(root, "Sub", a, "", 22, Theme.InkDim);
            handIcon = UiKit.Panel(root, "HandIcon", a, new Color(0.12f, 0.36f, 0.38f, 1f), false);
            var inner = UiKit.Panel(handIcon.transform, "Inner", a, new Color(0.05f, 0.14f, 0.18f, 1f), false);
            UiKit.Stretch((RectTransform)inner.transform, 5, 5, 5, 5);
            var mark = UiKit.Label(handIcon.transform, "Mark", a, Theme.MarkP0, 24, Theme.P0);
            UiKit.Stretch(mark.rectTransform);
            handText = UiKit.Label(root, "HandText", a, "", 26, Theme.Ink, TextAlignmentOptions.Left, true);
            handText.textWrappingMode = TextWrappingModes.NoWrap;
            popGlow = UiKit.Rect(root, "PopGlow").gameObject.AddComponent<RawImage>();
            popGlow.raycastTarget = false;
            popLabel = UiKit.Label(root, "Pop", a, "", 64, Theme.Gold, TextAlignmentOptions.Center, true);
            popLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            popLabel.textWrappingMode = TextWrappingModes.NoWrap;
            cardName = UiKit.Label(root, "CardName", a, "", 28, Theme.Ink, TextAlignmentOptions.Center, true);
            cardName.textWrappingMode = TextWrappingModes.NoWrap;
            taker = PortraitView.Create(root, "Taker", a);
            nextButton = UiKit.Button(root, "RewardNextButton", a, UiText.ResultNext, 28, out _, new Color(0.25f, 0.42f, 0.40f));
            introText = UiKit.Label(root, "IntroText", a, "", 26, Theme.Ink, TextAlignmentOptions.Top);
            revealAllButton = UiKit.Button(root, "RevealAllButton", a, UiText.IntroRevealAll, 24, out _);
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            if (popGlow != null) popGlow.texture = UiGlyphs.Rays;
            nextButton.onClick.AddListener(() => _next = true);
            revealAllButton.onClick.AddListener(() => _revealAll = true);
            if (stage != null) stage.Clicked += i => _requests.Enqueue(i);
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            var top = new Vector2(0.5f, 1f);
            UiKit.Place(title.rectTransform, top, new Vector2(0f, portrait ? -120f : -40f), new Vector2(portrait ? 700f : 1100f, 60f));
            title.fontSize = portrait ? 42 : 40;
            UiKit.Place(sub.rectTransform, top, new Vector2(0f, portrait ? -186f : -100f), new Vector2(680f, 34f));
            sub.fontSize = portrait ? 26 : 22;
            var bl = new Vector2(0f, 0f);
            UiKit.Place((RectTransform)handIcon.transform, bl, new Vector2(portrait ? 30f : 34f, portrait ? 40f : 30f), portrait ? new Vector2(64f, 80f) : new Vector2(52f, 66f));
            UiKit.Place(handText.rectTransform, bl, new Vector2(portrait ? 108f : 98f, portrait ? 58f : 46f), new Vector2(300f, 44f));
            handText.fontSize = portrait ? 32 : 26;
            UiKit.Place((RectTransform)nextButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, portrait ? 150f : 26f), portrait ? new Vector2(360f, 90f) : new Vector2(280f, 66f));
            nextButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 34 : 28;
            UiKit.Place(cardName.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, portrait ? 260f : 104f), new Vector2(700f, 44f));
            cardName.fontSize = portrait ? 34 : 28;
            UiKit.Place((RectTransform)taker.transform, top, new Vector2(0f, portrait ? -230f : -150f), portrait ? new Vector2(120f, 120f) : new Vector2(96f, 96f));
            popLabel.fontSize = portrait ? 70 : 64;
            UiKit.Place(introText.rectTransform, top, new Vector2(0f, portrait ? -130f : -28f), portrait ? new Vector2(680f, 220f) : new Vector2(1100f, 140f));
            introText.fontSize = portrait ? 26 : 26;
            UiKit.Place((RectTransform)revealAllButton.transform, new Vector2(1f, 0f), new Vector2(portrait ? -30f : -40f, portrait ? 40f : 28f),
                portrait ? new Vector2(280f, 84f) : new Vector2(230f, 60f));
            revealAllButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 24;
        }

        /// <summary>カードを選ぶ（クリックと同じ。テストから）。</summary>
        public void RequestPick(int index) => _requests.Enqueue(index);

        /// <summary>「まとめてめくる」（テストから）。</summary>
        public void RevealAll() => _revealAll = true;

        /// <summary>「次へ」を押す（テストから）。</summary>
        public void Continue() => _next = true;

        void ShowUi()
        {
            gameObject.SetActive(true);
            ApplyLayout(_portrait);
            _requests.Clear();
            _next = false;
            popLabel.gameObject.SetActive(false);
            popGlow.gameObject.SetActive(false);
            cardName.text = "";
            nextButton.gameObject.SetActive(false);
            introText.gameObject.SetActive(false);
            revealAllButton.gameObject.SetActive(false);
            _revealAll = false;
            title.color = Theme.Gold;
            title.gameObject.SetActive(true);
            sub.gameObject.SetActive(true);
            WaitingForNext = false;
            WaitingForPick = false;
            LastPick = null;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (stage != null) stage.Hide();
        }

        Vector3 HandWorld()
        {
            var rt = (RectTransform)handIcon.transform;
            var screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            return stage.Layout.ScreenToTable(screen);
        }

        void SetHand(int n, bool bump, float tf)
        {
            _handCount = n;
            handText.text = UiText.RewardHand + " " + n;
            if (!bump || tf <= 0f) return;
            var rt = (RectTransform)handIcon.transform;
            Tweens.Play(0.3f * tf, k => rt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, k), Ease.OutBack, 0f, rt, "bump");
            var trt = handText.rectTransform;
            Tweens.Play(0.3f * tf, k => trt.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, k), Ease.OutBack, 0f, trt, "bump");
        }

        static List<CardDef> Defs(IReadOnlyList<string> ids)
        {
            var list = new List<CardDef>();
            foreach (var id in ids) list.Add(CardCatalog.ById[id]);
            return list;
        }

        Vector3 DealFrom() => new Vector3(0f, stage.Layout.VisibleHeight * 0.75f, -0.5f);

        void LogCards()
        {
            if (!AutomationLog.Enabled) return;
            for (int i = 0; i < stage.Count; i++)
            {
                if (stage.IsDone(i)) continue;
                var s = stage.HomeScreenPos(i);
                AutomationLog.Write("reward card " + i + " " + F(s.x) + " " + F(Screen.height - s.y));
            }
        }

        static string F(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------
        // 報酬
        // ------------------------------------------------------------------

        /// <summary>報酬の演出を最後（「次へ」）まで。選んだカードは flow.Pick で RewardModel に記録する（手持ちへの反映は flow.FinishReward）。</summary>
        public async Awaitable PlayRewardAsync(CampaignFlow flow, float tf, CancellationToken ct)
        {
            var reward = flow.Reward;
            ShowUi();
            taker.Hide();
            title.text = reward.Count >= 2 ? UiText.RewardTitlePerfect : UiText.RewardTitle;
            sub.text = UiText.RewardSub;
            SetHand(flow.State.TotalCards, false, tf);
            var trt = title.rectTransform;
            Tweens.Play(0.35f * tf, k => trt.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, k), Ease.OutBack, 0f, trt, "pop");
            await stage.ShowAsync(Defs(reward.Cards), true, Player.P1, new[] { 5 }, new[] { 3, 2 }, DealFrom(), tf, ct);
            stage.ResetStats();
            if (AutomationLog.Enabled) AutomationLog.Write("reward shown " + reward.Count);

            while (!reward.IsDone)
            {
                stage.Interactive = true;
                WaitingForPick = true;
                LogCards();
                int index = -1;
                while (index < 0)
                {
                    while (_requests.Count > 0 && index < 0)
                    {
                        int r = _requests.Dequeue();
                        if (r >= 0 && r < reward.Cards.Count && !reward.IsPicked(r)) index = r;
                    }
                    if (index < 0) await Awaitable.NextFrameAsync(ct);
                }
                WaitingForPick = false;
                stage.Interactive = false;
                var pick = flow.Pick(index);
                if (!pick.HasValue) continue;
                var p = pick.Value;
                LastPick = p;
                var def = CardCatalog.ById[p.CardId];
                if (AutomationLog.Enabled) AutomationLog.Write("reward pick " + index + " " + p.CardId);
                SfxBank.Instance?.Play(SfxKind.Pick);
                await stage.FocusAsync(index, _portrait ? 1.6f : 1.45f, tf, ct);
                await stage.FlipAsync(index, def, Player.P0, tf, ct, () => Reveal(index, p, def, tf));
                if (AutomationLog.Enabled)
                {
                    var sp = stage.ScreenPos(index);
                    AutomationLog.Write("reward reveal " + p.CardId + (p.IsNew ? " new" : " x" + p.CopiesAfter) + " " + F(sp.x) + " " + F(Screen.height - sp.y));
                }
                await Tweens.Delay(1.1f * tf, ct);
                // 文字が消えて、手持ちへ飛んでいく
                var lrt = popLabel.rectTransform;
                Tweens.Play(0.2f * tf, k =>
                {
                    popLabel.alpha = 1f - k;
                    popGlow.color = new Color(popGlow.color.r, popGlow.color.g, popGlow.color.b, 0.8f * (1f - k));
                    cardName.alpha = 1f - k;
                    if (k >= 1f)
                    {
                        popLabel.gameObject.SetActive(false);
                        popGlow.gameObject.SetActive(false);
                    }
                }, Ease.Linear, 0f, lrt, "fade");
                await stage.FlyAsync(index, HandWorld(), tf, ct);
                SetHand(_handCount + 1, true, tf);
                SfxBank.Instance?.Play(SfxKind.Place, 5f);
                if (AutomationLog.Enabled) AutomationLog.Write("reward landed " + p.CardId + " hand " + _handCount);
                popLabel.gameObject.SetActive(false);
                popGlow.gameObject.SetActive(false);
                cardName.text = "";
                stage.Restore(index);
                if (!reward.IsDone)
                {
                    title.text = UiText.RewardMore;
                    Tweens.Play(0.3f * tf, k => trt.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, k), Ease.OutBack, 0f, trt, "pop");
                }
            }

            if (AutomationLog.Enabled) AutomationLog.Write("fps-reward " + stage.Stats());
            // 選ばなかったカードも表にして見せる
            title.text = UiText.RewardDone;
            sub.text = "";
            int n = 0;
            for (int i = 0; i < reward.Cards.Count; i++)
            {
                if (reward.IsPicked(i)) continue;
                if (n++ > 0) await Tweens.Delay(0.08f * tf, ct);
                await stage.FlipInPlaceAsync(i, CardCatalog.ById[reward.Cards[i]], Player.P1, tf * 0.7f, ct);
                stage.Card(i).SetAlpha(0.6f);
            }
            await WaitNextAsync(ct);
        }

        void Reveal(int index, RewardPick p, CardDef def, float tf)
        {
            var card = stage.Card(index);
            bool isNew = p.IsNew;
            var color = isNew ? Theme.Gold : new Color(0.55f, 0.9f, 1f);
            SfxBank.Instance?.Play(isNew ? SfxKind.RewardNew : SfxKind.RewardDup);
            stage.SetGlow(index, color, isNew ? 1f : 0.6f);
            stage.Burst(index, color, tf, isNew ? 1.15f : 0.8f);
            card.SetGlow(isNew ? 1f : 0.6f, color);
            // 「NEW!」/「×2」をカードの上に弾ませる
            var root = (RectTransform)transform;
            var top = stage.Layout.TableToScreen(card.transform.position + new Vector3(0f, CardView.Size.y * card.transform.localScale.y * 0.5f, 0f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, top, null, out var local);
            var lrt = popLabel.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(500f, 100f);
            lrt.anchoredPosition = local + new Vector2(0f, _portrait ? 40f : 26f);
            popLabel.text = isNew ? UiText.RewardNew : UiText.RewardCopies + p.CopiesAfter;
            popLabel.color = color;
            popLabel.alpha = 1f;
            popLabel.gameObject.SetActive(true);
            var grt = popGlow.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = lrt.anchoredPosition;
            grt.sizeDelta = new Vector2(isNew ? 360f : 240f, isNew ? 360f : 240f);
            popGlow.color = new Color(color.r, color.g, color.b, 0.8f);
            popGlow.gameObject.SetActive(true);
            cardName.text = UiText.DexLevel + def.Level + "  " + def.Name;
            cardName.alpha = 1f;
            if (tf > 0f)
            {
                Tweens.Play(0.45f * tf, k => lrt.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, k), Ease.OutBack, 0f, lrt, "pop");
                Tweens.Play(1.2f * tf, k =>
                {
                    grt.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.1f, Easing.Evaluate(Ease.OutCubic, k));
                    grt.localRotation = Quaternion.Euler(0f, 0f, 40f * k);
                }, Ease.Linear, 0f, grt, "spin");
            }
            else lrt.localScale = Vector3.one;
        }

        async Awaitable WaitNextAsync(CancellationToken ct)
        {
            nextButton.gameObject.SetActive(true);
            _next = false;
            WaitingForNext = true;
            EventSystem.current?.SetSelectedGameObject(nextButton.gameObject);
            AutomationLog.Buttons("Reward", transform);
            await Awaitable.NextFrameAsync(ct);
            while (!_next)
            {
                var kb = Keyboard.current;
                if (kb != null && !SettingsPanel.AnyOpen && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) &&
                    EventSystem.current?.currentSelectedGameObject != nextButton.gameObject) _next = true;
                if (!_next) await Awaitable.NextFrameAsync(ct);
            }
            WaitingForNext = false;
            nextButton.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 導入: はじめの 7 枚を受け取る
        // ------------------------------------------------------------------

        /// <summary>
        /// はじめて遊ぶとき（DESIGN §12-11）: 2〜3 行の導入 → はじめの 7 枚が裏向きで配られ、1 枚ずつタップでめくる
        /// （「まとめてめくる」もある）→ 7 枚が手持ちへ飛んでいく →「星図へ」。
        /// </summary>
        public async Awaitable PlayIntroAsync(IReadOnlyList<string> starter, float tf, CancellationToken ct)
        {
            ShowUi();
            taker.Hide();
            title.text = "";
            sub.text = "";
            SetHand(0, false, tf);
            introText.gameObject.SetActive(true);
            string[] lines = { UiText.IntroLine1, UiText.IntroLine2, UiText.IntroLine3 };
            introText.text = "";
            for (int i = 0; i < lines.Length; i++)
            {
                introText.text = string.Join("\n", lines, 0, i + 1);
                if (tf > 0f) await Tweens.Delay(0.45f * tf, ct);
            }
            if (AutomationLog.Enabled) AutomationLog.Write("intro text");
            await stage.ShowAsync(Defs(starter), true, Player.P0, new[] { 7 }, new[] { 4, 3 }, DealFrom(), tf, ct);
            stage.ResetStats();
            sub.text = UiText.IntroTap;
            sub.rectTransform.anchoredPosition = new Vector2(0f, _portrait ? -360f : -176f);
            revealAllButton.gameObject.SetActive(true);
            stage.Interactive = true;
            WaitingForPick = true;
            LogCards();
            AutomationLog.Buttons("Intro", transform);
            var revealed = new bool[starter.Count];
            int left = starter.Count;
            while (left > 0)
            {
                int index = -1;
                while (_requests.Count > 0 && index < 0)
                {
                    int r = _requests.Dequeue();
                    if (r >= 0 && r < revealed.Length && !revealed[r]) index = r;
                }
                if (index < 0 && _revealAll)
                {
                    for (int i = 0; i < revealed.Length; i++)
                    {
                        if (!revealed[i])
                        {
                            index = i;
                            break;
                        }
                    }
                }
                if (index < 0)
                {
                    await Awaitable.NextFrameAsync(ct);
                    continue;
                }
                revealed[index] = true;
                left--;
                var def = CardCatalog.ById[starter[index]];
                if (AutomationLog.Enabled) AutomationLog.Write("intro reveal " + index + " " + def.Id);
                int at = index;
                SfxBank.Instance?.Play(SfxKind.RewardDup, (starter.Count - left) * 0.7f);
                var flip = stage.FlipInPlaceAsync(index, def, Player.P0, tf, ct);
                if (!_revealAll) await flip;
                else await Tweens.Delay(0.12f * tf, ct);
                stage.Burst(at, Theme.Gold, tf, 0.7f);
                stage.SetGlow(at, Theme.Gold, 0.35f);
                if (left == 3 && AutomationLog.Enabled) AutomationLog.Write("intro midway");
            }
            await Tweens.Delay(0.5f * tf, ct);
            stage.Interactive = false;
            WaitingForPick = false;
            revealAllButton.gameObject.SetActive(false);
            title.text = UiText.IntroReceived;
            var trt = title.rectTransform;
            introText.gameObject.SetActive(false);
            sub.text = "";
            SfxBank.Instance?.Play(SfxKind.RewardNew);
            Tweens.Play(0.35f * tf, k => trt.localScale = Vector3.one * Mathf.LerpUnclamped(0.5f, 1f, k), Ease.OutBack, 0f, trt, "pop");
            if (AutomationLog.Enabled)
            {
                AutomationLog.Write("intro received");
                AutomationLog.Write("fps-intro " + stage.Stats());
            }
            await Tweens.Delay(0.7f * tf, ct);
            // 7 枚が手持ちへ
            var hand = HandWorld();
            var flights = new List<Awaitable>();
            for (int i = 0; i < starter.Count; i++)
            {
                stage.SetGlow(i, Theme.Gold, 0.5f);
                flights.Add(FlyOne(i, hand, tf, ct));
                if (tf > 0f) await Tweens.Delay(0.07f * tf, ct);
            }
            foreach (var f in flights) await f;
            nextButton.GetComponentInChildren<TextMeshProUGUI>().text = UiText.IntroToMap;
            await WaitNextAsync(ct);
            nextButton.GetComponentInChildren<TextMeshProUGUI>().text = UiText.ResultNext;
        }

        async Awaitable FlyOne(int i, Vector3 hand, float tf, CancellationToken ct)
        {
            await stage.FlyAsync(i, hand, tf * 0.8f, ct);
            SetHand(_handCount + 1, true, tf);
            SfxBank.Instance?.Play(SfxKind.Place, 2f + _handCount);
        }

        // ------------------------------------------------------------------
        // 原作モード: 取られる
        // ------------------------------------------------------------------

        /// <summary>原作モードで負けた: 取られる（または手持ちが少なくて守られる）演出を「次へ」まで。</summary>
        public async Awaitable PlayTakeAsync(CampaignFlow flow, float tf, CancellationToken ct)
        {
            var app = flow.Application;
            ShowUi();
            taker.Show(flow.Opponent);
            bool taken = app.TakenCard != null;
            title.text = taken ? UiText.TakeTitle : "";
            title.color = taken ? new Color(1f, 0.62f, 0.55f) : Theme.Gold;
            sub.text = "";
            SetHand(flow.PreMatchState.TotalCards, false, tf);
            await stage.ShowAsync(Defs(flow.PlayerDeck), false, Player.P0, new[] { 5 }, new[] { 3, 2 }, null, tf, ct);
            stage.Interactive = false;
            await Tweens.Delay(0.4f * tf, ct);
            string target = taken ? app.TakenCard : app.ProtectedCard;
            int index = -1;
            for (int i = 0; i < flow.PlayerDeck.Count; i++)
            {
                if (flow.PlayerDeck[i] == target) index = i;
            }
            if (index >= 0)
            {
                if (taken)
                {
                    SfxBank.Instance?.Play(SfxKind.Take);
                    await stage.TakeAwayAsync(index, tf, ct);
                    SetHand(flow.State.TotalCards, false, tf);
                }
                else await stage.ShakeKeepAsync(index, tf, ct);
            }
            sub.text = CampaignTexts.ClassicTakeMessage(app) + (taken ? "\n<size=80%>" + UiText.TakeHint + "</size>" : "");
            if (AutomationLog.Enabled) AutomationLog.Write("take " + (taken ? app.TakenCard : "protected " + app.ProtectedCard));
            await WaitNextAsync(ct);
            title.color = Theme.Gold;
        }
    }
}
