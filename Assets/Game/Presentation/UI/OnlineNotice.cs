using System;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 対戦中の画面上部の帯（オンライン）。「再接続を待っています（残り 25 秒）」と「部屋を出る」ボタン。
    /// 盤の操作は止めない（切れている間に指した手は、つながり直したときに送り直す）。
    /// </summary>
    public sealed class OnlineNotice : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Image plate;
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] Button leaveButton;

        public event Action LeavePressed;

        public bool IsShown => gameObject.activeSelf;
        public string Text => label != null ? label.text : "";
        public Button LeaveButton => leaveButton;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            plate = UiKit.Panel(root, "Plate", a, new Color(0.30f, 0.14f, 0.12f, 0.94f), false);
            label = UiKit.Label(plate.transform, "Label", a, "", 24, Theme.Ink);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14;
            label.fontSizeMax = 24;
            leaveButton = UiKit.Button(plate.transform, "OnlineNoticeLeave", a, UiText.OnlineLeave, 22, out _, new Color(0.45f, 0.18f, 0.18f));
            ApplyLayout(false);
        }

        void Awake()
        {
            leaveButton.onClick.AddListener(() => LeavePressed?.Invoke());
        }

        public void ApplyLayout(bool portrait)
        {
            var top = new Vector2(0.5f, 1f);
            if (portrait)
            {
                UiKit.Place((RectTransform)plate.transform, top, new Vector2(0f, -106f), new Vector2(680f, 110f));
                UiKit.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(640f, 44f));
                UiKit.Place((RectTransform)leaveButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(260f, 48f));
                label.fontSizeMax = 26;
            }
            else
            {
                UiKit.Place((RectTransform)plate.transform, top, new Vector2(0f, -86f), new Vector2(720f, 56f));
                UiKit.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(480f, 44f));
                UiKit.Place((RectTransform)leaveButton.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(190f, 42f));
                label.fontSizeMax = 24;
            }
        }

        public void Show(string text)
        {
            gameObject.SetActive(true);
            if (label.text != text) label.text = text;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
