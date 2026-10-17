using System.Text;
using System.Threading;
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
    /// エンドロール（DESIGN §14-4。Phase 8a）: エンディングの文の後に自動で流す。
    /// 中央揃え・役割ごとの見出し（制作 / カード絵 / 音楽 / フォント / 謝辞）・大きめの文字。背景には星座（点と線）がゆっくり昇り、
    /// 左右をカードが流れていく（後ろでは星空のシェーダーとタイトルの扇も動いている）。
    /// 最後の「ありがとう」の行が画面の中央に来たら止まり、少し待って閉じる。「スキップ」・Esc / Enter / Space で飛ばせる。
    /// 演出速度の設定にかかわらず同じ速さで流す（読む速さの問題なので）。通常のクレジット画面（タイトルから開く）は一覧のまま（CreditsScreen）。
    /// </summary>
    public sealed class EndRollScreen : MonoBehaviour, IResponsiveUi
    {
        /// <summary>流れる速さ（キャンバス上の px / 秒）。</summary>
        public const float RollSpeed = 72f;

        /// <summary>背景を流れるカード（Lv の高い順に見栄えのするもの）。</summary>
        public static readonly string[] DriftCardIds =
        {
            "andromede", "leomane", "pegasion", "cygnwraith", "ophiwyrm", "aquilaburst", "draconyx", "orionmane",
        };

        const int StarCount = 34;

        [SerializeField] Image dim;
        [SerializeField] RectTransform sky;
        [SerializeField] Image[] stars = new Image[StarCount];
        [SerializeField] MiniCardView[] cards = new MiniCardView[8];
        [SerializeField] Image veil;
        [SerializeField] RectTransform content;
        [SerializeField] TextMeshProUGUI body;
        [SerializeField] Button skipButton;

        bool _rolling;
        bool _skip;
        bool _portrait;

        /// <summary>星座の層の最初の位置（下へずらしておき、本文の 3 割の速さで昇らせる）。</summary>
        const float SkyStart = -1400f;

        public bool IsOpen => gameObject.activeSelf;
        public bool IsRolling => _rolling;

        /// <summary>流した割合（0〜1。テスト・ログ用）。</summary>
        public float RollProgress { get; private set; }

        public Button SkipButton => skipButton;
        public string Body => body.text;
        public int DriftCardCount => cards.Length;

        /// <summary>エンドロールの本文（リッチテキスト・中央揃え）。カード → アイコン → 作者の表は CardArtTable の正本から作る。</summary>
        public static string Compose()
        {
            var sb = new StringBuilder();
            const string gap = "\n\n\n";
            void Heading(string h) => sb.Append("<size=150%><color=#F5D17A>").Append(h).Append("</color></size>\n\n");
            void Role(string r) => sb.Append("<size=78%><color=#A9B2D1>").Append(r).Append("</color></size>\n");
            sb.Append("<size=230%><color=#F5D17A>").Append(UiText.Logo).Append("</color></size>\n");
            sb.Append(UiText.Subtitle).Append(gap).Append('\n');

            Heading(UiText.EndRollProduction);
            Role(UiText.EndRollPlanningRole);
            sb.Append("<size=120%>").Append(UiText.EndRollPlanningName).Append("</size>\n\n");
            Role(UiText.EndRollDevRole);
            sb.Append(UiText.EndRollDevName).Append(gap);

            Heading(UiText.EndRollArt);
            sb.Append(UiText.EndRollArtBody).Append("\n\n");
            sb.Append("<size=66%>");
            Role(UiText.CreditsCardHeader);
            foreach (var a in CardArtTable.Assignments)
            {
                var def = CardCatalog.ById[a.cardId];
                sb.Append(def.Name).Append(" — ").Append(a.icon).Append(" (").Append(CardArtTable.AuthorDisplayName(a.author)).Append(")\n");
            }
            sb.Append('\n');
            Role(UiText.CreditsPortraitHeader);
            foreach (var a in CardArtTable.PortraitAssignments)
            {
                var o = Edgewise.Core.Campaign.CampaignData.OpponentById(a.cardId);
                sb.Append(o.Title).Append(' ').Append(o.Name).Append(" — ").Append(a.icon).Append(" (").Append(CardArtTable.AuthorDisplayName(a.author)).Append(")\n");
            }
            sb.Append("</size>").Append(gap);

            Heading(UiText.EndRollMusic);
            Role(UiText.EndRollMusicSource);
            sb.Append('\n');
            void Song(string scene, string song) => sb.Append("<size=78%><color=#A9B2D1>").Append(scene).Append("</color></size>\n").Append(song).Append("\n\n");
            Song(UiText.EndRollMusicTitle, "「星空」 Heitaro Ashibe");
            Song(UiText.EndRollMusicMatch, "「ギザなビート」 マニーラ");
            Song(UiText.EndRollMusicVictory, "「明日へのファンファーレ」 のる");
            Song(UiText.EndRollMusicDefeat, "「Too late...」 t.tam");
            sb.Append("<size=78%>https://opentracks.com/</size>\n\n");
            sb.Append("<size=78%>").Append(UiText.EndRollSfx).Append("</size>").Append(gap);

            Heading(UiText.EndRollFont);
            sb.Append("Noto Sans JP\n<size=78%>Copyright 2014-2021 Adobe\nSIL Open Font License, Version 1.1</size>").Append(gap);

            Heading(UiText.EndRollThanksHeading);
            sb.Append(UiText.EndRollThanksBody).Append(gap).Append("\n\n");

            sb.Append("<size=115%><color=#F5D17A>").Append(UiText.CreditsThanks).Append("</color></size>");
            return sb.ToString();
        }

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            // 後ろの星空（ワールドのシェーダー）を少し暗くする
            dim = UiKit.Fill(root, "EndRollDim", new Color(0.004f, 0.006f, 0.025f, 0.58f), false);

            // 星座: 点を散らし、近い点どうしを細い線で結ぶ（位置は固定の乱数で決める。シーンを作り直しても同じ）
            sky = UiKit.Rect(root, "Sky");
            UiKit.Place(sky, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1400f, 3000f));
            var rng = new System.Random(20261018);
            var pos = new Vector2[StarCount];
            for (int i = 0; i < StarCount; i++)
                pos[i] = new Vector2((float)(rng.NextDouble() * 1300.0 - 650.0), (float)(i * (2900.0 / StarCount) + rng.NextDouble() * 60.0));
            for (int i = 1; i < StarCount; i++)
            {
                // 少し前の点のうち最も近いものと結ぶ（星座らしい折れ線になる）
                int best = i - 1;
                for (int j = Mathf.Max(0, i - 4); j < i; j++)
                    if ((pos[j] - pos[i]).sqrMagnitude < (pos[best] - pos[i]).sqrMagnitude) best = j;
                if ((pos[best] - pos[i]).magnitude > 520f || i % 7 == 0) continue; // ところどころ切って、いくつかの星座に分ける
                var d = pos[i] - pos[best];
                var line = UiKit.Panel(sky, "Line" + i, a, new Color(0.96f, 0.82f, 0.48f, 0.16f), false);
                line.sprite = null;
                line.type = Image.Type.Simple;
                UiKit.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), (pos[i] + pos[best]) * 0.5f, new Vector2(d.magnitude, 2f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            stars = new Image[StarCount];
            for (int i = 0; i < StarCount; i++)
            {
                float s = 5f + (i * 7 % 5);
                stars[i] = UiKit.Panel(sky, "Star" + i, a, new Color(1f, 0.93f, 0.75f, 0.85f), false);
                UiKit.Place(stars[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), pos[i], new Vector2(s, s));
            }

            // 左右を流れるカード
            cards = new MiniCardView[DriftCardIds.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = MiniCardView.Create(root, "DriftCard" + i, a, false);
                var g = cards[i].GetComponent<CanvasGroup>();
                g.blocksRaycasts = false;
                g.interactable = false;
            }

            // カードの上に薄い幕（カード絵のシェーダーは CanvasGroup の透明度を使わないので、幕で沈める）。星座は幕の上に出す
            veil = UiKit.Fill(root, "CardVeil", new Color(0.004f, 0.006f, 0.025f, 0.45f), false);
            sky.SetAsLastSibling();

            // 本文（中央揃え）。上端を基準に置き、上へ動かす
            content = UiKit.Rect(root, "Content");
            body = UiKit.Label(content, "Body", a, Compose(), 30, Theme.Ink, TextAlignmentOptions.Top, true);
            body.richText = true;
            body.lineSpacing = 8f;
            UiKit.Stretch(body.rectTransform);

            skipButton = UiKit.Button(root, "EndRollSkipButton", a, UiText.CreditsRollSkip, 22, out _, new Color(0.18f, 0.22f, 0.36f, 0.85f));
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            skipButton.onClick.AddListener(Skip);
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            float w = portrait ? 680f : 940f;
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 1f);
            body.fontSize = portrait ? 32 : 30;
            // 本文の高さは実行中だけ測る（シーンの生成では固定の値にして、作り直しても差分が出ないようにする）
            content.sizeDelta = new Vector2(w, Application.isPlaying ? body.GetPreferredValues(body.text, w, 0f).y + 40f : 100f);
            UiKit.Place((RectTransform)skipButton.transform, new Vector2(1f, 0f), new Vector2(portrait ? -24f : -28f, portrait ? 28f : 22f),
                portrait ? new Vector2(200f, 72f) : new Vector2(170f, 54f));
            for (int i = 0; i < cards.Length; i++)
            {
                var rt = (RectTransform)cards[i].transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = portrait ? new Vector2(112f, 150f) : new Vector2(120f, 162f);
                cards[i].GetComponent<CanvasGroup>().alpha = 1f;
                if (!Application.isPlaying) rt.anchoredPosition = new Vector2((i % 2 == 0 ? -1f : 1f) * 400f, -200f);
            }
            // 縦は本文の後ろをカードが通るので、幕を濃くして読みやすさを保つ
            veil.color = new Color(0.004f, 0.006f, 0.025f, portrait ? 0.74f : 0.5f);
            if (Application.isPlaying) PlaceCards(0f);
        }

        /// <summary>
        /// カードの位置（t 秒後）。左右 4 枚ずつ、等間隔で下から上へ流れて折り返す。同じ列は同じ速さにして重ならないようにし、
        /// 左右で速さと横の位置を少し変えて単調にならないようにする。
        /// </summary>
        void PlaceCards(float t)
        {
            var rootRt = (RectTransform)transform;
            float h = Mathf.Max(720f, rootRt.rect.height);
            float span = h + 260f;
            for (int i = 0; i < cards.Length; i++)
            {
                bool left = i % 2 == 0;
                int k = i / 2;
                float x = (left ? -1f : 1f) * (_portrait ? 250f + (k % 2) * 40f : 520f + (k % 2) * 64f);
                float speed = left ? 34f : 42f;
                float y = Mathf.Repeat(k * span / 4f + (left ? 0f : span / 8f) + t * speed, span) - 130f;
                var rt = (RectTransform)cards[i].transform;
                rt.anchoredPosition = new Vector2(x, y);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.4f + i) * 6f);
            }
        }

        /// <summary>スキップ（ボタン・キー・テスト）。</summary>
        public void Skip()
        {
            if (_rolling) _skip = true;
        }

        /// <summary>エンドロールを流し、流し終えるかスキップされたら閉じて返る。</summary>
        public async Awaitable PlayAsync(CancellationToken ct)
        {
            gameObject.SetActive(true);
            _rolling = true;
            _skip = false;
            RollProgress = 0f;
            for (int i = 0; i < cards.Length; i++)
                cards[i].Show(CardCatalog.ById.TryGetValue(DriftCardIds[i], out var def) ? def : CardCatalog.All[i]);
            ApplyLayout(_portrait);
            EventSystem.current?.SetSelectedGameObject(skipButton.gameObject);
            if (AutomationLog.Enabled) AutomationLog.Write("credits roll begin");
            AutomationLog.Buttons("EndRoll", transform);
            try
            {
                await Awaitable.NextFrameAsync(ct);
                Canvas.ForceUpdateCanvases();
                var rootRt = (RectTransform)transform;
                float half = rootRt.rect.height * 0.5f;
                float h = content.sizeDelta.y;
                // 上端が画面の下端 → 最後の行（ありがとう）が中央
                float y0 = -half - 10f;
                float y1 = h - 40f - 30f;
                float y = y0;
                content.anchoredPosition = new Vector2(0f, y0);
                float t = 0f;
                Animate(0f);
                bool logged = false;
                while (y < y1 && !_skip)
                {
                    float dt = Time.unscaledDeltaTime;
                    t += dt;
                    // 最初の 1 秒は少しずつ速くなる
                    float speed = RollSpeed * Mathf.Clamp01(t / 1f + 0.15f);
                    y = Mathf.Min(y1, y + speed * dt);
                    content.anchoredPosition = new Vector2(0f, y);
                    RollProgress = Mathf.InverseLerp(y0, y1, y);
                    Animate(t);
                    if (!logged && RollProgress >= 0.25f && AutomationLog.Enabled)
                    {
                        logged = true;
                        AutomationLog.Write("credits rolling");
                    }
                    await Awaitable.NextFrameAsync(ct);
                }
                RollProgress = 1f;
                if (AutomationLog.Enabled && !_skip) AutomationLog.Write("credits roll thanks");
                float hold = 0f;
                while (hold < 3.5f && !_skip)
                {
                    hold += Time.unscaledDeltaTime;
                    t += Time.unscaledDeltaTime;
                    Animate(t);
                    await Awaitable.NextFrameAsync(ct);
                }
            }
            finally
            {
                if (AutomationLog.Enabled) AutomationLog.Write("credits roll end" + (_skip ? " skipped" : ""));
                _rolling = false;
                if (this != null) gameObject.SetActive(false);
            }
        }

        /// <summary>背景: 星座が本文より遅く昇り（奥行き）、星がまたたき、カードが流れる。</summary>
        void Animate(float t)
        {
            sky.anchoredPosition = new Vector2(0f, SkyStart + t * RollSpeed * 0.3f);
            for (int i = 0; i < stars.Length; i++)
            {
                var c = stars[i].color;
                c.a = 0.45f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * (1.3f + (i % 5) * 0.37f) + i * 1.7f));
                stars[i].color = c;
            }
            PlaceCards(t);
        }

        void Update()
        {
            if (!_rolling) return;
            var kb = Keyboard.current;
            if (kb != null && !SettingsPanel.AnyOpen
                && (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                _skip = true;
        }
    }
}
