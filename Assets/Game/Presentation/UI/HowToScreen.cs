using System;
using Edgewise.Core;
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
    /// 遊び方（DESIGN §14-4。Phase 8a）: タイトルの「遊び方」から開く 1 画面。基本ルールを盤と矢印の図で 3 コマ、オリジナルの Pivot を 1 コマ。
    /// 図は uGUI の四角と文字だけで組み、ゆっくり動かす（置く・裏返る）。各ルール修飾は対戦中のメニューの「ルール確認」へ誘導する。
    /// 横は 4 コマを 1 列、縦は 2×2。Esc / Backspace / 閉じるボタンで閉じる。
    /// </summary>
    public sealed class HowToScreen : MonoBehaviour, IResponsiveUi
    {
        public const int PanelCount = 4;

        /// <summary>図の中の小さなカード（枠は所有者の色、4 辺のランクは菱形、右下に ◆ / ●）。</summary>
        [Serializable]
        public sealed class DiagramCard
        {
            public RectTransform root;
            public Image border;
            public Image fill;
            public TextMeshProUGUI[] ranks;
            public TextMeshProUGUI mark;

            public void SetOwner(Player p)
            {
                var c = Theme.OwnerColor(p);
                border.color = c;
                fill.color = Color.Lerp(new Color(0.05f, 0.06f, 0.13f), c, 0.22f);
                mark.text = Theme.Mark(p);
                mark.color = c;
            }
        }

        [SerializeField] Image dim;
        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] Image[] frames = new Image[PanelCount];
        [SerializeField] TextMeshProUGUI[] frameTitles = new TextMeshProUGUI[PanelCount];
        [SerializeField] RectTransform[] diagrams = new RectTransform[PanelCount];
        [SerializeField] TextMeshProUGUI[] captions = new TextMeshProUGUI[PanelCount];
        [SerializeField] TextMeshProUGUI rulesNote;
        [SerializeField] TextMeshProUGUI controls;
        [SerializeField] Button closeButton;
        // 1. 置く: 手札のカードが盤の空きマスへ動く
        [SerializeField] DiagramCard placeCard;
        [SerializeField] RectTransform placeTarget;
        [SerializeField] RectTransform placeArrow;
        // 2. 裏返す: 下に置いた自分のカード（上の辺 7）が、上の相手のカード（下の辺 3）を裏返す
        [SerializeField] DiagramCard flipMine;
        [SerializeField] DiagramCard flipTheirs;
        [SerializeField] TextMeshProUGUI compare;
        // 4. Pivot: 回す前と後
        [SerializeField] DiagramCard pivotBefore;
        [SerializeField] DiagramCard pivotAfter;

        float _openedAt;
        Vector2 _placeFrom;

        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;
        public Button CloseButton => closeButton;
        public string FrameTitle(int i) => frameTitles[i].text;
        public string Caption(int i) => captions[i].text;
        public string RulesNote => rulesNote.text;

        /// <summary>裏返しの図で、相手のカードが今プレイヤーの色か（テスト用）。</summary>
        public bool FlipShowsMine => flipTheirs.mark.text == Theme.MarkP0;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            dim = UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            var p = panel.transform;
            title = UiKit.Label(p, "Title", a, UiText.HowToTitle, 34, Theme.Gold);
            string[] titles = { UiText.HowTo1Title, UiText.HowTo2Title, UiText.HowTo3Title, UiText.HowTo4Title };
            string[] bodies = { UiText.HowTo1Body, UiText.HowTo2Body, UiText.HowTo3Body, UiText.HowTo4Body };
            frames = new Image[PanelCount];
            frameTitles = new TextMeshProUGUI[PanelCount];
            diagrams = new RectTransform[PanelCount];
            captions = new TextMeshProUGUI[PanelCount];
            for (int i = 0; i < PanelCount; i++)
            {
                frames[i] = UiKit.Panel(p, "Frame" + i, a, new Color(0.03f, 0.04f, 0.10f, 0.92f), false);
                var f = frames[i].transform;
                frameTitles[i] = UiKit.Label(f, "FrameTitle", a, titles[i], 22, i == 3 ? Theme.P0 : Theme.Gold);
                frameTitles[i].textWrappingMode = TextWrappingModes.NoWrap;
                diagrams[i] = UiKit.Rect(f, "Diagram");
                UiKit.Place(diagrams[i], new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(240f, 200f));
                captions[i] = UiKit.Label(f, "Caption", a, bodies[i], 17, Theme.Ink, TextAlignmentOptions.Top);
            }
            BuildPlace(a, diagrams[0]);
            BuildFlip(a, diagrams[1]);
            BuildScore(a, diagrams[2]);
            BuildPivot(a, diagrams[3]);
            rulesNote = UiKit.Label(p, "RulesNote", a, UiText.HowToRulesNote, 18, Theme.Gold);
            controls = UiKit.Label(p, "Controls", a, UiText.HowToControls, 16, Theme.InkDim);
            closeButton = UiKit.Button(p, "HowToCloseButton", a, UiText.MenuClose, 26, out _, new Color(0.25f, 0.42f, 0.40f));
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        // ---------------- 図の部品 ----------------

        static DiagramCard Card(Transform parent, string name, GameAssets a, Vector2 pos, Vector2 size, int[] ranks, Player owner, float rankSize)
        {
            var c = new DiagramCard();
            c.border = UiKit.Panel(parent, name, a, Theme.OwnerColor(owner), false);
            c.root = c.border.rectTransform;
            UiKit.Place(c.root, new Vector2(0.5f, 0.5f), pos, size);
            c.fill = UiKit.Panel(c.root, "Fill", a, Color.black, false);
            UiKit.Stretch(c.fill.rectTransform, 3, 3, 3, 3);
            c.ranks = new TextMeshProUGUI[4];
            // 上・右・下・左（カードの上 6 割に菱形）
            float dx = size.x * 0.24f, dy = size.y * 0.2f;
            var center = new Vector2(0f, size.y * 0.08f);
            Vector2[] at = { center + new Vector2(0f, dy), center + new Vector2(dx, 0f), center + new Vector2(0f, -dy), center + new Vector2(-dx, 0f) };
            string[] names = { "RankTop", "RankRight", "RankBottom", "RankLeft" };
            for (int i = 0; i < 4; i++)
            {
                var t = UiKit.Label(c.root, names[i], a, ranks != null ? Theme.Rank(ranks[i]) : "", rankSize, Color.white, TextAlignmentOptions.Center, true);
                t.fontStyle = FontStyles.Bold;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                UiKit.Place(t.rectTransform, new Vector2(0.5f, 0.5f), at[i], new Vector2(rankSize * 1.4f, rankSize * 1.3f));
                c.ranks[i] = t;
            }
            c.mark = UiKit.Label(c.root, "Mark", a, "", rankSize * 0.8f, Color.white, TextAlignmentOptions.Center, true);
            UiKit.Place(c.mark.rectTransform, new Vector2(1f, 0f), new Vector2(-size.x * 0.17f, size.y * 0.14f), new Vector2(rankSize, rankSize));
            c.SetOwner(owner);
            return c;
        }

        /// <summary>盤の 3×3 のマス（cell の大きさ、間隔 4）。</summary>
        static RectTransform[] Board(Transform parent, GameAssets a, Vector2 center, float cell)
        {
            var cells = new RectTransform[9];
            for (int i = 0; i < 9; i++)
            {
                int r = i / 3, col = i % 3;
                var img = UiKit.Panel(parent, "Cell" + i, a, new Color(0.16f, 0.17f, 0.30f, 0.9f), false);
                UiKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), center + new Vector2((col - 1) * (cell + 4f), (1 - r) * (cell + 4f)),
                    new Vector2(cell, cell));
                cells[i] = img.rectTransform;
            }
            return cells;
        }

        /// <summary>盤に置いたカードの小さな印（所有者の色の四角と ◆ / ●）。</summary>
        static void Tile(RectTransform cell, GameAssets a, Player owner)
        {
            var t = UiKit.Panel(cell, "Tile", a, Color.Lerp(new Color(0.05f, 0.06f, 0.13f), Theme.OwnerColor(owner), 0.35f), false);
            UiKit.Stretch(t.rectTransform, 3, 3, 3, 3);
            var edge = UiKit.Panel(t.rectTransform, "Edge", a, Theme.OwnerColor(owner), false);
            UiKit.Stretch(edge.rectTransform);
            edge.color = new Color(Theme.OwnerColor(owner).r, Theme.OwnerColor(owner).g, Theme.OwnerColor(owner).b, 0.35f);
            var m = UiKit.Label(t.rectTransform, "Mark", a, Theme.Mark(owner), cell.sizeDelta.x * 0.42f, Theme.OwnerColor(owner), TextAlignmentOptions.Center, true);
            UiKit.Stretch(m.rectTransform);
        }

        /// <summary>矢印（細い棒と ▶）。from → to（図の座標）。</summary>
        static RectTransform Arrow(Transform parent, string name, GameAssets a, Vector2 from, Vector2 to, Color color)
        {
            var root = UiKit.Rect(parent, name);
            var d = to - from;
            UiKit.Place(root, new Vector2(0.5f, 0.5f), (from + to) * 0.5f, new Vector2(d.magnitude, 18f));
            root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var bar = UiKit.Panel(root, "Bar", a, color, false);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(Mathf.Max(0f, d.magnitude - 10f), 4f));
            var head = UiKit.Label(root, "Head", a, "▶", 20, color, TextAlignmentOptions.Center);
            UiKit.Place(head.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(4f, 0f), new Vector2(22f, 22f));
            return root;
        }

        void BuildPlace(GameAssets a, RectTransform d)
        {
            // 盤は右寄せ、手札のカードは左。左の列の真ん中（マス 3）へ置く
            var cells = Board(d, a, new Vector2(54f, -4f), 44f);
            Tile(cells[0], a, Player.P1);
            Tile(cells[4], a, Player.P0);
            Tile(cells[8], a, Player.P1);
            placeTarget = cells[3];
            var hand = new Vector2(-90f, -4f);
            placeArrow = Arrow(d, "Arrow", a, hand + new Vector2(32f, 0f), new Vector2(54f - 48f - 24f, -4f), Theme.Gold);
            placeCard = Card(d, "HandCard", a, hand, new Vector2(56f, 70f), new[] { 6, 4, 2, 5 }, Player.P0, 17f);
            _placeFrom = hand;
        }

        void BuildFlip(GameAssets a, RectTransform d)
        {
            var size = new Vector2(68f, 84f);
            flipTheirs = Card(d, "Theirs", a, new Vector2(-34f, 46f), size, new[] { 5, 2, 3, 4 }, Player.P1, 20f);
            flipMine = Card(d, "Mine", a, new Vector2(-34f, -46f), size, new[] { 7, 3, 1, 6 }, Player.P0, 20f);
            // 比べる辺（相手の下 3 と自分の上 7）を金色に
            flipTheirs.ranks[2].color = Theme.Gold;
            flipMine.ranks[0].color = Theme.Gold;
            compare = UiKit.Label(d, "Compare", a, UiText.HowTo2Compare, 28, Theme.Gold, TextAlignmentOptions.Center, true);
            compare.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Place(compare.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(76f, 0f), new Vector2(92f, 44f));
            // 下の自分のカードが上の相手のカードを押し返す向き
            Arrow(d, "Arrow", a, new Vector2(16f, -34f), new Vector2(16f, 34f), Theme.Gold);
        }

        void BuildScore(GameAssets a, RectTransform d)
        {
            var cells = Board(d, a, new Vector2(-26f, 22f), 42f);
            int[] mine = { 0, 1, 3, 4, 6, 8 };
            for (int i = 0; i < 9; i++) Tile(cells[i], a, Array.IndexOf(mine, i) >= 0 ? Player.P0 : Player.P1);
            // 相手の手札に 1 枚残っている
            var handLabel = UiKit.Label(d, "HandLabel", a, UiText.HowTo3Hand, 15, Theme.InkDim);
            UiKit.Place(handLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(86f, 58f), new Vector2(70f, 24f));
            var handCell = UiKit.Rect(d, "HandCell");
            UiKit.Place(handCell, new Vector2(0.5f, 0.5f), new Vector2(86f, 22f), new Vector2(42f, 42f));
            Tile(handCell, a, Player.P1);
            var score = UiKit.Label(d, "Score", a, Theme.MarkP0 + " 6  －  4 " + Theme.MarkP1 + "  " + UiText.HowTo3Score, 19, Theme.Ink, TextAlignmentOptions.Center, true);
            score.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Place(score.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(240f, 30f));
        }

        void BuildPivot(GameAssets a, RectTransform d)
        {
            var size = new Vector2(70f, 86f);
            // 回す前: 右の辺が 9。90° 回すと 9 が上に来る（数字は正立のまま、並びだけが回る）
            pivotBefore = Card(d, "Before", a, new Vector2(-70f, 6f), size, new[] { 2, 9, 3, 4 }, Player.P0, 20f);
            pivotAfter = Card(d, "After", a, new Vector2(70f, 6f), size, new[] { 9, 3, 4, 2 }, Player.P0, 20f);
            pivotBefore.ranks[1].color = Theme.Gold;
            pivotAfter.ranks[0].color = Theme.Gold;
            Arrow(d, "Arrow", a, new Vector2(-26f, 6f), new Vector2(26f, 6f), Theme.P0);
            var deg = UiKit.Label(d, "Degrees", a, UiText.HowToRotateLabel, 18, Theme.P0, TextAlignmentOptions.Center, true);
            UiKit.Place(deg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(60f, 26f));
            var keys = UiKit.Label(d, "Keys", a, "Q / E", 16, Theme.InkDim);
            UiKit.Place(keys.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -66f), new Vector2(200f, 24f));
        }

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            var size = portrait ? new Vector2(690f, 1160f) : new Vector2(1220f, 680f);
            UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, size);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(size.x - 40f, 50f));
            Vector2 frameSize = portrait ? new Vector2(330f, 420f) : new Vector2(284f, 420f);
            for (int i = 0; i < PanelCount; i++)
            {
                Vector2 pos = portrait
                    ? new Vector2((i % 2 == 0 ? -1f : 1f) * 172f, 295f - (i / 2) * 432f)
                    : new Vector2((i - 1.5f) * 296f, 52f);
                var rt = frames[i].rectTransform;
                UiKit.Place(rt, c, pos, frameSize);
                UiKit.Place(frameTitles[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(frameSize.x - 16f, 36f));
                frameTitles[i].fontSize = portrait ? 25 : 21;
                float k = portrait ? 1.12f : 1.05f;
                diagrams[i].localScale = new Vector3(k, k, 1f);
                UiKit.Place(diagrams[i], new Vector2(0.5f, 1f), new Vector2(0f, portrait ? -58f : -54f), new Vector2(240f, 200f));
                float capTop = portrait ? -58f - 200f * k - 12f : -54f - 200f * k - 12f;
                UiKit.Place(captions[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, capTop), new Vector2(frameSize.x - 28f, portrait ? 118f : 120f));
                captions[i].fontSize = portrait ? 19 : 17;
            }
            if (portrait)
            {
                UiKit.Place(rulesNote.rectTransform, c, new Vector2(0f, -392f), new Vector2(640f, 76f));
                rulesNote.fontSize = 20;
                UiKit.Place(controls.rectTransform, c, new Vector2(0f, -456f), new Vector2(640f, 40f));
                controls.fontSize = 18;
                UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(280f, 76f));
            }
            else
            {
                UiKit.Place(rulesNote.rectTransform, c, new Vector2(0f, -192f), new Vector2(1160f, 46f));
                rulesNote.fontSize = 18;
                UiKit.Place(controls.rectTransform, c, new Vector2(0f, -238f), new Vector2(1160f, 28f));
                controls.fontSize = 16;
                UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(220f, 56f));
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            _openedAt = Time.unscaledTime;
            Animate(0f);
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
            if (AutomationLog.Enabled) AutomationLog.Write("howto open");
            AutomationLog.Buttons("HowTo", transform);
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
            if (kb != null && !SettingsPanel.AnyOpen && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame))
            {
                Close();
                return;
            }
            Animate(Time.unscaledTime - _openedAt);
        }

        /// <summary>図を動かす: 1 は手札のカードがマスへ動いて置かれる、2 は相手のカードが裏返る（どちらも 3.2 秒で繰り返す）。</summary>
        void Animate(float t)
        {
            const float loop = 3.2f;
            float u = t % loop;
            // 1. 置く: 0.4〜1.2 秒で動く → 2.6 秒まで置いたまま → 消えて戻る
            if (placeCard != null && placeCard.root != null)
            {
                var to = placeTarget.anchoredPosition;
                float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1.2f, u));
                placeCard.root.anchoredPosition = Vector2.Lerp(_placeFrom, to, m);
                float s = Mathf.Lerp(1f, 44f / 70f, m);
                if (u > 1.1f && u < 1.3f) s *= 1f - 0.08f * Mathf.Sin((u - 1.1f) / 0.2f * Mathf.PI); // 着地で一瞬沈む
                placeCard.root.localScale = new Vector3(s, s, 1f);
                float alpha = u > 2.8f ? Mathf.InverseLerp(3.2f, 2.8f, u) : 1f;
                SetAlpha(placeCard, alpha);
                SetGraphicAlpha(placeArrow, 1f - 0.7f * m);
            }
            // 2. 裏返す: 1.0〜1.3 秒で横につぶれて色が変わり、戻る
            if (flipTheirs != null && flipTheirs.root != null)
            {
                float f = Mathf.InverseLerp(1.0f, 1.3f, u);
                bool mine = f >= 0.5f && u < 3.0f;
                flipTheirs.SetOwner(mine ? Player.P0 : Player.P1);
                float sx = u >= 1.0f && u <= 1.3f ? Mathf.Abs(Mathf.Cos(f * Mathf.PI)) : 1f;
                flipTheirs.root.localScale = new Vector3(Mathf.Max(0.02f, sx), 1f, 1f);
                compare.alpha = 0.55f + 0.45f * Mathf.Clamp01(Mathf.Sin(u / loop * Mathf.PI * 2f) * 0.5f + 0.5f);
            }
        }

        static void SetAlpha(DiagramCard c, float alpha)
        {
            foreach (var g in c.root.GetComponentsInChildren<Graphic>(true))
            {
                var col = g.color;
                col.a = alpha;
                g.color = col;
            }
        }

        static void SetGraphicAlpha(RectTransform rt, float alpha)
        {
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true))
            {
                var col = g.color;
                col.a = alpha;
                g.color = col;
            }
        }
    }
}
