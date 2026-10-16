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
        [SerializeField] Button hintButton;
        [SerializeField] TextMeshProUGUI hintLabel;
        [SerializeField] PortraitView opponentPortrait;

        readonly int[] _score = new int[2];

        public Button MenuButton => menuButton;
        public Button RotateLeftButton => rotateLeft;
        public Button RotateRightButton => rotateRight;

        public event Action MenuPressed;
        public event Action<int> RotatePressed;
        public event Action HintPressed;

        public Button HintButton => hintButton;
        public string HintText => hintLabel != null ? hintLabel.text : "";
        public string TurnText => turnText != null ? turnText.text : "";

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
            hintButton = UiKit.Button(root, "HintButton", a, UiText.Hint, 22, out hintLabel, new Color(0.16f, 0.36f, 0.44f));
            hintLabel.enableAutoSizing = true;
            hintLabel.fontSizeMin = 14;
            hintLabel.fontSizeMax = 22;
            // 星図巡りの相手の小さな立ち絵（フリー対戦では出さない）
            opponentPortrait = PortraitView.Create(root, "OpponentPortrait", a);
            opponentPortrait.gameObject.SetActive(false);
            ApplyLayout(false);
        }

        void Awake()
        {
            menuButton.onClick.AddListener(() => MenuPressed?.Invoke());
            rotateLeft.onClick.AddListener(() => RotatePressed?.Invoke(-1));
            rotateRight.onClick.AddListener(() => RotatePressed?.Invoke(+1));
            hintButton.onClick.AddListener(() => HintPressed?.Invoke());
            ShowRotateButtons(false);
            ShowHintButton(false);
        }

        bool _portraitMode;

        public void ApplyLayout(bool portrait)
        {
            _portraitMode = portrait;
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
            if (opponentPortrait != null)
            {
                bool hasPortrait = opponentPortrait.gameObject.activeSelf;
                if (portrait)
                {
                    // 縦: 右端に小さな立ち絵、相手の欄はその左
                    UiKit.Place((RectTransform)opponentPortrait.transform, new Vector2(1f, 1f), new Vector2(-10f, -12f), new Vector2(54f, 54f));
                    if (hasPortrait) UiKit.Place(block1, new Vector2(1f, 1f), new Vector2(-70f, -4f), new Vector2(blockW, h - 4f));
                }
                else
                {
                    UiKit.Place((RectTransform)opponentPortrait.transform, new Vector2(1f, 1f), new Vector2(-150f - blockW - 8f, -6f), new Vector2(62f, 62f));
                }
            }

            if (portrait)
            {
                UiKit.Place((RectTransform)turnPlate.transform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(240f, 38f));
                UiKit.Place((RectTransform)menuButton.transform, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(180f, 40f));
                ((RectTransform)menuButton.transform).GetComponentInChildren<TextMeshProUGUI>().fontSize = 22;
                // 縦画面: ヒントは左下、回転ボタンは右下（重ならないように）
                UiKit.Place((RectTransform)rotateLeft.transform, new Vector2(1f, 0f), new Vector2(-176f, 18f), new Vector2(150f, 64f));
                UiKit.Place((RectTransform)rotateRight.transform, new Vector2(1f, 0f), new Vector2(-16f, 18f), new Vector2(150f, 64f));
                UiKit.Place((RectTransform)hintButton.transform, new Vector2(0f, 0f), new Vector2(16f, 18f), new Vector2(340f, 64f));
            }
            else
            {
                UiKit.Place((RectTransform)turnPlate.transform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(280f, 42f));
                UiKit.Place((RectTransform)menuButton.transform, new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(120f, 46f));
                ((RectTransform)menuButton.transform).GetComponentInChildren<TextMeshProUGUI>().fontSize = 20;
                UiKit.Place((RectTransform)rotateLeft.transform, new Vector2(0.5f, 0f), new Vector2(-90f, 6f), new Vector2(160f, 46f));
                UiKit.Place((RectTransform)rotateRight.transform, new Vector2(0.5f, 0f), new Vector2(90f, 6f), new Vector2(160f, 46f));
                UiKit.Place((RectTransform)hintButton.transform, new Vector2(0f, 0f), new Vector2(16f, 6f), new Vector2(300f, 46f));
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

        /// <summary>星図巡りの相手（上部に名前と小さな立ち絵）。null ならフリー対戦（立ち絵なし）。</summary>
        public void SetOpponent(Edgewise.Core.Campaign.Opponent opponent)
        {
            if (opponentPortrait == null) return;
            if (opponent == null) opponentPortrait.Hide();
            else opponentPortrait.Show(opponent);
            ApplyLayout(_portraitMode);
        }

        public bool OpponentPortraitShown => opponentPortrait != null && opponentPortrait.gameObject.activeSelf;

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

        /// <summary>Pivot トークンの残り（★）と使った分（☆）。max は対戦開始時の数（先攻・後攻で違うことがある）。</summary>
        public void SetTokens(bool pivotRule, int t0, int t1, int max0 = 2, int max1 = 2)
        {
            tokens0.gameObject.SetActive(pivotRule);
            tokens1.gameObject.SetActive(pivotRule);
            tokens0.text = UiText.PivotTokens + " " + Pips(t0, max0);
            tokens1.text = Pips(t1, max1) + " " + UiText.PivotTokens;
            TokensText0 = tokens0.text;
            TokensText1 = tokens1.text;
        }

        /// <summary>トークン表示の文字列（テスト用）。</summary>
        public string TokensText0 { get; private set; }
        public string TokensText1 { get; private set; }

        public static string Pips(int n, int max)
        {
            var s = "";
            for (int i = 0; i < n; i++) s += "★";
            for (int i = n; i < max; i++) s += "☆";
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

        /// <summary>ヒントボタン（プレイヤーの手番だけ出す）。</summary>
        public void ShowHintButton(bool show)
        {
            if (hintButton == null) return;
            hintButton.gameObject.SetActive(show);
            if (!show) SetHintText("");
        }

        /// <summary>ヒントボタンの文字（空ならボタン名、結果なら「予想 6-4（目安）」）。</summary>
        public void SetHintText(string text)
        {
            if (hintLabel == null) return;
            hintLabel.text = string.IsNullOrEmpty(text) ? UiText.Hint : text;
        }
    }
}
