using System;
using System.Text;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// クレジット画面（タイトルから開く。docs/CREDITS.md の要約）: フォント、カード絵のアイコン（50 枚の作者と CC BY 3.0）、
    /// BGM（曲名・作曲者・OpenTracks）、効果音、オマージュの表記。
    /// 本文は ScrollRect（マウスのホイール・タッチのドラッグ）、キーボードは ↑↓ / PageUp・PageDown でスクロール、Esc で閉じる。
    /// 文字はすべて UiText とカード名・ASCII から作る（フォントに焼いた文字だけで表示できるように）。
    /// </summary>
    public sealed class CreditsScreen : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] ScrollRect scroll;
        [SerializeField] TextMeshProUGUI body;
        [SerializeField] Button closeButton;

        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;
        public Button CloseButton => closeButton;
        public string Body => body.text;
        public ScrollRect Scroll => scroll;

        /// <summary>クレジットの本文（リッチテキスト）。カード → アイコン → 作者の表は CardArtTable の正本から作る。</summary>
        public static string Compose()
        {
            var sb = new StringBuilder();
            void Heading(string h) => sb.Append("<size=125%><color=#F5D17A>").Append(h).Append("</color></size>\n");
            Heading(UiText.CreditsFontHeading);
            sb.Append(UiText.CreditsFontBody).Append("\n\n");
            Heading(UiText.CreditsIconsHeading);
            sb.Append(UiText.CreditsIconsBody).Append("\n\n");
            sb.Append("<color=#A9B2D1>").Append(UiText.CreditsCardHeader).Append("</color>\n");
            foreach (var a in CardArtTable.Assignments)
            {
                var def = CardCatalog.ById[a.cardId];
                sb.Append(def.Name).Append(" — ").Append(a.icon).Append(" (").Append(CardArtTable.AuthorDisplayName(a.author)).Append(", ")
                    .Append(CardArtTable.License(a.author)).Append(")\n");
            }
            sb.Append('\n');
            Heading(UiText.CreditsBgmHeading);
            sb.Append(UiText.CreditsBgmBody).Append("\n\n");
            Heading(UiText.CreditsSfxHeading);
            sb.Append(UiText.CreditsSfxBody).Append("\n\n");
            Heading(UiText.CreditsHomageHeading);
            sb.Append(UiText.CreditsHomageBody).Append('\n');
            return sb.ToString();
        }

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            var p = panel.transform;
            title = UiKit.Label(p, "Title", a, UiText.CreditsTitle, 34, Theme.Gold);

            // スクロールする本文: Viewport（RectMask2D）→ Content（本文の高さに合わせる）
            var view = UiKit.Rect(p, "Viewport");
            var viewImg = view.gameObject.AddComponent<Image>();
            viewImg.color = new Color(0f, 0f, 0f, 0.001f); // ホイール・ドラッグを受けるため（見えない）
            view.gameObject.AddComponent<RectMask2D>();
            var content = UiKit.Rect(view, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 100f);
            body = UiKit.Label(content, "Body", a, Compose(), 20, Theme.Ink, TextAlignmentOptions.TopLeft);
            body.richText = true;
            body.lineSpacing = 6f;
            UiKit.Stretch(body.rectTransform, 4, 0, 16, 0);
            var fitter = content.gameObject.AddComponent<VerticalLayoutGroup>();
            fitter.childControlHeight = true;
            fitter.childControlWidth = true;
            fitter.childForceExpandHeight = false;
            fitter.padding = new RectOffset(4, 16, 4, 24);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = p.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.inertia = true;

            closeButton = UiKit.Button(p, "CreditsCloseButton", a, UiText.MenuClose, 26, out _, new Color(0.25f, 0.42f, 0.40f));
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            var size = portrait ? new Vector2(680f, 1180f) : new Vector2(1000f, 660f);
            UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, size);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(size.x - 40f, 50f));
            var view = (RectTransform)scroll.viewport;
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.pivot = new Vector2(0.5f, 0.5f);
            view.offsetMin = new Vector2(32f, portrait ? 120f : 92f);
            view.offsetMax = new Vector2(-24f, -80f);
            body.fontSize = portrait ? 24 : 20;
            UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(portrait ? 260f : 220f, portrait ? 80f : 60f));
        }

        public void Open()
        {
            gameObject.SetActive(true);
            scroll.verticalNormalizedPosition = 1f;
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
            AutomationLog.Buttons("Credits", transform);
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            float h = scroll.content.rect.height - scroll.viewport.rect.height;
            if (h <= 1f) return;
            float stepPx = 0f;
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) stepPx = 60f;
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) stepPx = -60f;
            if (kb.pageUpKey.wasPressedThisFrame) stepPx = scroll.viewport.rect.height * 0.9f;
            if (kb.pageDownKey.wasPressedThisFrame) stepPx = -scroll.viewport.rect.height * 0.9f;
            if (stepPx != 0f) scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + stepPx / h);
        }
    }
}
