using System;
using Edgewise.Core;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 対戦中の上部の表示（スコア・手番・Pivot トークン・メニューボタン）と、持っている間の回転ボタン。表示だけを担当。
    /// </summary>
    public sealed class HudView : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] RectTransform bar;
        [SerializeField] RectTransform block0;
        [SerializeField] RectTransform block1;
        [SerializeField] TextMeshProUGUI name0;
        [SerializeField] TextMeshProUGUI name1;
        [SerializeField] TextMeshProUGUI score0;
        [SerializeField] TextMeshProUGUI score1;
        [SerializeField] TextMeshProUGUI tokens0;
        [SerializeField] TextMeshProUGUI tokens1;
        [SerializeField] TextMeshProUGUI turnText;
        [SerializeField] Image turnPlate;
        [SerializeField] Button menuButton;
        [SerializeField] Button rotateLeft;
        [SerializeField] Button rotateRight;

        readonly int[] _score = new int[2];

        public Button MenuButton => menuButton;
        public Button RotateLeftButton => rotateLeft;
        public Button RotateRightButton => rotateRight;

        public event Action MenuPressed;
        public event Action<int> RotatePressed;

        public int Score(Player p) => _score[(int)p];

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);

            var barImg = UiKit.Fill(root, "Bar", new Color(0.02f, 0.03f, 0.08f, 0.55f), false);
            bar = barImg.rectTransform;

            block0 = UiKit.Rect(root, "Player0");
            name0 = UiKit.Label(block0, "Name", a, Theme.MarkP0 + " " + UiText.You, 24, Theme.P0, TextAlignmentOptions.Left);
            score0 = UiKit.Label(block0, "Score", a, "5", 46, Theme.P0, TextAlignmentOptions.Center, true);
            tokens0 = UiKit.Label(block0, "Tokens", a, "", 18, Theme.InkDim, TextAlignmentOptions.Left);

            block1 = UiKit.Rect(root, "Player1");
            name1 = UiKit.Label(block1, "Name", a, UiText.Cpu + " " + Theme.MarkP1, 24, Theme.P1, TextAlignmentOptions.Right);
            score1 = UiKit.Label(block1, "Score", a, "5", 46, Theme.P1, TextAlignmentOptions.Center, true);
            tokens1 = UiKit.Label(block1, "Tokens", a, "", 18, Theme.InkDim, TextAlignmentOptions.Right);

            turnPlate = UiKit.Panel(root, "TurnPlate", a, new Color(0.08f, 0.10f, 0.22f, 0.9f), false);
            turnText = UiKit.Label(turnPlate.transform, "Turn", a, UiText.YourTurn, 24, Theme.Ink);
            UiKit.Stretch(turnText.rectTransform);

            menuButton = UiKit.Button(root, "MenuButton", a, UiText.Menu, 20, out _);
            rotateLeft = UiKit.Button(root, "RotateLeft", a, UiText.RotateLeft, 22, out _);
            rotateRight = UiKit.Button(root, "RotateRight", a, UiText.RotateRight, 22, out _);
            ApplyLayout(false);
        }

        void Awake()
        {
            menuButton.onClick.AddListener(() => MenuPressed?.Invoke());
            rotateLeft.onClick.AddListener(() => RotatePressed?.Invoke(-1));
            rotateRight.onClick.AddListener(() => RotatePressed?.Invoke(+1));
            ShowRotateButtons(false);
        }

        public void ApplyLayout(bool portrait)
        {
            float h = portrait ? 92f : 74f;
            UiKit.Place(bar, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, h));
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, h);

            float blockW = portrait ? 200f : 260f;
            UiKit.Place(block0, new Vector2(0f, 1f), new Vector2(16f, -4f), new Vector2(blockW, h - 4f));
            UiKit.Place(block1, new Vector2(1f, 1f), new Vector2(portrait ? -16f : -150f, -4f), new Vector2(blockW, h - 4f));
            LayoutBlock(name0, score0, tokens0, blockW, h, false);
            LayoutBlock(name1, score1, tokens1, blockW, h, true);

            if (portrait)
            {
                UiKit.Place((RectTransform)turnPlate.transform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(240f, 38f));
                UiKit.Place((RectTransform)menuButton.transform, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(180f, 40f));
                ((RectTransform)menuButton.transform).GetComponentInChildren<TextMeshProUGUI>().fontSize = 22;
                UiKit.Place((RectTransform)rotateLeft.transform, new Vector2(0.5f, 0f), new Vector2(-95f, 18f), new Vector2(170f, 60f));
                UiKit.Place((RectTransform)rotateRight.transform, new Vector2(0.5f, 0f), new Vector2(95f, 18f), new Vector2(170f, 60f));
            }
            else
            {
                UiKit.Place((RectTransform)turnPlate.transform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(280f, 42f));
                UiKit.Place((RectTransform)menuButton.transform, new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(120f, 46f));
                ((RectTransform)menuButton.transform).GetComponentInChildren<TextMeshProUGUI>().fontSize = 20;
                UiKit.Place((RectTransform)rotateLeft.transform, new Vector2(0.5f, 0f), new Vector2(-90f, 6f), new Vector2(160f, 46f));
                UiKit.Place((RectTransform)rotateRight.transform, new Vector2(0.5f, 0f), new Vector2(90f, 6f), new Vector2(160f, 46f));
            }
        }

        static void LayoutBlock(TextMeshProUGUI name, TextMeshProUGUI score, TextMeshProUGUI tokens, float w, float h, bool right)
        {
            float scoreW = 64f;
            var anchor = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            UiKit.Place(score.rectTransform, anchor, new Vector2(0f, 0f), new Vector2(scoreW, h - 4f));
            float textX = right ? -scoreW - 6f : scoreW + 6f;
            UiKit.Place(name.rectTransform, anchor, new Vector2(textX, -6f), new Vector2(w - scoreW - 6f, 30f));
            UiKit.Place(tokens.rectTransform, anchor, new Vector2(textX, -38f), new Vector2(w - scoreW - 6f, 24f));
        }

        public void SetNames(string p0, string p1)
        {
            name0.text = Theme.MarkP0 + " " + p0;
            name1.text = p1 + " " + Theme.MarkP1;
        }

        public void SetScores(int s0, int s1)
        {
            _score[0] = s0;
            _score[1] = s1;
            score0.text = s0.ToString();
            score1.text = s1.ToString();
        }

        /// <summary>スコアを 1 つ動かして弾ませる。</summary>
        public void AddScore(Player p, int delta, JuiceSettings juice, float timeFactor)
        {
            int i = (int)p;
            _score[i] += delta;
            var t = i == 0 ? score0 : score1;
            t.text = _score[i].ToString();
            if (delta > 0 && timeFactor > 0f)
            {
                var rt = t.rectTransform;
                float s = juice.scorePopScale;
                Tweens.Play(juice.scorePopTime * timeFactor, k => rt.localScale = Vector3.one * Mathf.Lerp(1f, s, Easing.Evaluate(Ease.Bump, k)),
                    Ease.Linear, 0f, rt, "pop");
            }
        }

        public void SetTokens(bool pivotRule, int t0, int t1)
        {
            tokens0.gameObject.SetActive(pivotRule);
            tokens1.gameObject.SetActive(pivotRule);
            tokens0.text = UiText.PivotTokens + " " + Pips(t0);
            tokens1.text = Pips(t1) + " " + UiText.PivotTokens;
        }

        static string Pips(int n)
        {
            var s = "";
            for (int i = 0; i < n; i++) s += "★";
            for (int i = n; i < 2; i++) s += "☆";
            return s;
        }

        public void SetTurn(string text, Color color)
        {
            turnText.text = text;
            turnText.color = color;
        }

        public void ShowRotateButtons(bool show)
        {
            rotateLeft.gameObject.SetActive(show);
            rotateRight.gameObject.SetActive(show);
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
