using System.Collections.Generic;
using System.Threading;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
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
    /// エンディングの文（DESIGN §12-7-1。5c）: 最後の星域の主に初めて勝ったとき、帯の後に出す。
    /// 星空を少し暗くして、見出し「星図巡り」の下に 3〜4 行（Core の CampaignData.Ending）を 1 行ずつ浮かび上がらせる。
    /// タップ（Enter / Space）で残りを一度に出し、出し終えたらもう一度タップで次へ（クレジット）。読む速さは人によるので自動では進めない。
    /// </summary>
    public sealed class EndingScreen : MonoBehaviour, IResponsiveUi
    {
        public const int MaxLines = 4;

        [SerializeField] Button tapArea;
        [SerializeField] Image dim;
        [SerializeField] TextMeshProUGUI heading;
        [SerializeField] Image rule;
        [SerializeField] TextMeshProUGUI[] lines = new TextMeshProUGUI[MaxLines];
        [SerializeField] TextMeshProUGUI next;

        bool _tapped;
        bool _portrait;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>すべての行を出し終え、次へを待っているか。</summary>
        public bool WaitingForNext { get; private set; }

        public int LineCount { get; private set; }

        public string LineText(int i) => lines[i].text;

        public float LineAlpha(int i) => lines[i].alpha;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            dim = UiKit.Fill(root, "EndingTap", new Color(0.005f, 0.008f, 0.03f, 0.78f));
            tapArea = dim.gameObject.AddComponent<Button>();
            tapArea.targetGraphic = dim;
            tapArea.transition = Selectable.Transition.None;
            var nav = tapArea.navigation;
            nav.mode = Navigation.Mode.None;
            tapArea.navigation = nav;
            heading = UiKit.Label(root, "Heading", a, UiText.EndingHeading, 30, Theme.Gold, TextAlignmentOptions.Center, true);
            heading.characterSpacing = 24f;
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            rule = UiKit.Fill(root, "Rule", Theme.Gold, false);
            lines = new TextMeshProUGUI[MaxLines];
            for (int i = 0; i < MaxLines; i++)
            {
                lines[i] = UiKit.Label(root, "Line" + i, a, "", 32, Theme.Ink, TextAlignmentOptions.Center, true);
                lines[i].textWrappingMode = TextWrappingModes.Normal;
            }
            next = UiKit.Label(root, "Next", a, UiText.DialogueNext + "  " + UiText.EndingNext, 20, Theme.InkDim, TextAlignmentOptions.Center);
            next.textWrappingMode = TextWrappingModes.NoWrap;
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            tapArea.onClick.AddListener(() => _tapped = true);
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            var c = new Vector2(0.5f, 0.5f);
            float w = portrait ? 660f : 1100f;
            float lineH = portrait ? 108f : 64f;
            float top = portrait ? 330f : 190f;
            UiKit.Place(heading.rectTransform, c, new Vector2(0f, top), new Vector2(w, 50f));
            heading.fontSize = portrait ? 36 : 30;
            UiKit.Place(rule.rectTransform, c, new Vector2(0f, top - 40f), new Vector2(portrait ? 260f : 320f, 2f));
            for (int i = 0; i < MaxLines; i++)
            {
                UiKit.Place(lines[i].rectTransform, c, new Vector2(0f, top - 110f - i * lineH), new Vector2(w, lineH));
                lines[i].fontSize = portrait ? 34 : 32;
            }
            UiKit.Place(next.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, portrait ? 70f : 44f), new Vector2(w, 36f));
            next.fontSize = portrait ? 26 : 20;
        }

        /// <summary>タップと同じ（残りを全部出す → 次へ）。テスト用。</summary>
        public void Advance() => _tapped = true;

        /// <summary>文を 1 行ずつ出し、次へを押されたら閉じて返る。tf が 0（即時）なら一度に出す。</summary>
        public async Awaitable PlayAsync(IReadOnlyList<string> text, float tf, CancellationToken ct)
        {
            gameObject.SetActive(true);
            _tapped = false;
            WaitingForNext = false;
            LineCount = Mathf.Min(MaxLines, text.Count);
            EventSystem.current?.SetSelectedGameObject(null);
            for (int i = 0; i < MaxLines; i++)
            {
                lines[i].text = i < LineCount ? text[i] : "";
                lines[i].alpha = 0f;
                lines[i].gameObject.SetActive(i < LineCount);
            }
            heading.alpha = 0f;
            rule.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0f);
            next.alpha = 0f;
            if (AutomationLog.Enabled) AutomationLog.Write("ending begin " + LineCount);
            try
            {
                // 見出しと線
                await Tweens.Run(0.8f * tf, k =>
                {
                    heading.alpha = k;
                    rule.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.8f * k);
                    rule.rectTransform.localScale = new Vector3(k, 1f, 1f);
                }, ct, Ease.OutCubic);
                // 1 行ずつ、下から少し浮かび上がる
                for (int i = 0; i < LineCount; i++)
                {
                    if (_tapped || ReadKeys()) break;
                    var lt = lines[i];
                    var rt = lt.rectTransform;
                    var basePos = rt.anchoredPosition;
                    SfxBank.Instance?.Play(SfxKind.Cursor, 5f + i * 2f);
                    float d = 1.1f * tf, t = 0f;
                    while (t < d && !_tapped)
                    {
                        t += Time.unscaledDeltaTime;
                        float k = Easing.Evaluate(Ease.OutCubic, Mathf.Clamp01(t / d));
                        lt.alpha = k;
                        rt.anchoredPosition = basePos + new Vector2(0f, (1f - k) * -14f);
                        if (ReadKeys()) _tapped = true;
                        await Awaitable.NextFrameAsync(ct);
                    }
                    lt.alpha = 1f;
                    rt.anchoredPosition = basePos;
                    // 行と行の間（読む間）
                    t = 0f;
                    while (t < 0.6f * tf && !_tapped)
                    {
                        t += Time.unscaledDeltaTime;
                        if (ReadKeys()) _tapped = true;
                        await Awaitable.NextFrameAsync(ct);
                    }
                }
                // 残りを一度に
                heading.alpha = 1f;
                rule.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.8f);
                rule.rectTransform.localScale = Vector3.one;
                for (int i = 0; i < LineCount; i++) lines[i].alpha = 1f;
                ApplyLayout(_portrait);
                _tapped = false;
                WaitingForNext = true;
                if (AutomationLog.Enabled)
                {
                    AutomationLog.Write("ending shown " + LineCount);
                    AutomationLog.Buttons("Ending", transform);
                }
                await Awaitable.NextFrameAsync(ct);
                while (!_tapped && !ReadKeys())
                {
                    next.alpha = 0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
                    await Awaitable.NextFrameAsync(ct);
                }
                WaitingForNext = false;
                SfxBank.Instance?.Play(SfxKind.Cursor);
                await Tweens.Run(0.5f * tf, k =>
                {
                    float a = 1f - k;
                    heading.alpha = a;
                    next.alpha = 0f;
                    for (int i = 0; i < LineCount; i++) lines[i].alpha = a;
                    rule.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.8f * a);
                }, ct, Ease.InCubic);
            }
            finally
            {
                WaitingForNext = false;
                if (this != null) gameObject.SetActive(false);
                if (AutomationLog.Enabled) AutomationLog.Write("ending end");
            }
        }

        static bool ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || SettingsPanel.AnyOpen) return false;
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame
                || kb.escapeKey.wasPressedThisFrame;
        }
    }
}
