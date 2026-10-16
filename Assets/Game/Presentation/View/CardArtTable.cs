using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// カード ID → シルエットのアイコン（game-icons.net）と個別調整（DESIGN §7-1「カードとアイコンの対応」）。
    /// 正本はこのファイルの <see cref="Assignments"/>。Editor の CardArtPipeline がこれを ScriptableObject に書き出し、
    /// 同じ値で SVG → マスク → SDF アトラス（<see cref="CardArtAtlas"/>）を作る。選んだ理由は docs/ART.md、クレジットは docs/CREDITS.md。
    /// </summary>
    [CreateAssetMenu(menuName = "Edgewise/Card Art Table", fileName = "CardArtTable")]
    public sealed class CardArtTable : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string cardId;

            /// <summary>game-icons.net の作者フォルダ名（例: lorc）。</summary>
            public string author;

            /// <summary>アイコン名（拡張子なし。例: wyvern）。</summary>
            public string icon;

            /// <summary>自動の大きさ合わせ（外接矩形をセルに収める）に掛ける倍率。</summary>
            public float scale = 1f;

            /// <summary>セル内の位置のずらし（セルの幅を 1 とする。+x 右、+y 上）。</summary>
            public Vector2 offset;

            /// <summary>左右反転。</summary>
            public bool flipX;

            public string IconPath => author + "/" + icon;

            public Entry() { }

            public Entry(string cardId, string author, string icon, float scale = 1f, float offsetX = 0f, float offsetY = 0f, bool flipX = false)
            {
                this.cardId = cardId;
                this.author = author;
                this.icon = icon;
                this.scale = scale;
                offset = new Vector2(offsetX, offsetY);
                this.flipX = flipX;
            }
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>カードとアイコンの対応の正本（カード定義の順 = Lv 順）。</summary>
        public static IReadOnlyList<Entry> Assignments => _assignments;

        static readonly Entry[] _assignments =
        {
            // ---- Lv1 ----
            new Entry("glimmouse", "lorc", "mouse"),
            new Entry("dustwisp", "lorc", "ghost"),
            new Entry("coglet", "delapouite", "mono-wheel-robot", 0.92f),
            new Entry("emberkit", "caro-asercion", "fox"),
            new Entry("scalefry", "caro-asercion", "axolotl"),
            // ---- Lv2 ----
            new Entry("pyxmoth", "lorc", "fairy"),
            new Entry("bolthare", "delapouite", "rabbit"),
            new Entry("rivetcrab", "lorc", "sad-crab"),
            new Entry("frostnewt", "lorc", "gecko"),
            new Entry("mirelamp", "delapouite", "elysium-shade", 0.92f),
            // ---- Lv3 ----
            new Entry("lynxlight", "lorc", "cat"),
            new Entry("gearowl", "lorc", "owl", 0.95f),
            new Entry("cinderdrake", "delapouite", "dimetrodon"),
            new Entry("hollowshade", "lorc", "spectre"),
            new Entry("stonetoad", "lorc", "frog"),
            // ---- Lv4 ----
            new Entry("velahound", "delapouite", "sitting-dog"),
            new Entry("clockwyrm", "lorc", "centipede"),
            new Entry("tidewraith", "lorc", "jellyfish"),
            new Entry("marshwyvern", "lorc", "wyvern"),
            new Entry("voltsparrow", "delapouite", "swallow"),
            // ---- Lv5 ----
            new Entry("lupusfang", "lorc", "werewolf"),
            new Entry("orreryx", "lorc", "scarab-beetle", 0.95f),
            new Entry("gloamveil", "lorc", "wing-cloak"),
            new Entry("emberscale", "delapouite", "velociraptor"),
            new Entry("corvusarc", "lorc", "raven"),
            // ---- Lv6 ----
            new Entry("aquilaburst", "lorc", "eagle-emblem"),
            new Entry("ironcetus", "delapouite", "dolphin"),
            new Entry("lanternmare", "caro-asercion", "cloaked-figure-on-horseback"),
            new Entry("serpenscoil", "delapouite", "cobra"),
            new Entry("rimeloom", "delapouite", "djinn"),
            // ---- Lv7 ----
            new Entry("leomane", "lorc", "lion"),
            new Entry("forgecolossus", "lorc", "robot-golem"),
            new Entry("auroraveil", "delapouite", "fairy"),
            new Entry("draconyx", "lorc", "hydra"),
            new Entry("stormgear", "lorc", "missile-mech"),
            // ---- Lv8 ----
            new Entry("cygnwraith", "lorc", "swan"),
            new Entry("taurocrag", "delapouite", "bison"),
            new Entry("gravelith", "lorc", "trilobite", 0.95f),
            new Entry("voltwyrm", "lorc", "sea-serpent"),
            new Entry("nocturnis", "delapouite", "bat"),
            // ---- Lv9 ----
            new Entry("orionmane", "delapouite", "tiger"),
            new Entry("astrolabion", "delapouite", "praying-mantis"),
            new Entry("cassiveil", "delapouite", "mermaid"),
            new Entry("scorpidrake", "lorc", "scorpion"),
            new Entry("polarion", "delapouite", "pterodactylus"),
            // ---- Lv10 ----
            new Entry("andromede", "lorc", "angel-outfit"),
            new Entry("pegasion", "skoll", "pegasus"),
            new Entry("ophiwyrm", "lorc", "dragon-spiral"),
            new Entry("sextantor", "lorc", "megabot"),
            new Entry("ursamaw", "cathelineau", "polar-bear"),
        };

        /// <summary>立ち絵のアトラス上の ID（カード ID と区別するため "@" + 相手の ID）。</summary>
        public static string PortraitId(string opponentId) => "@" + opponentId;

        public static bool IsPortraitId(string id) => !string.IsNullOrEmpty(id) && id[0] == '@';

        /// <summary>
        /// 対戦相手 12 人の立ち絵（DESIGN §12-7）の正本。cardId には相手の ID を書き、パイプラインは <see cref="PortraitId"/> でアトラスに入れる。
        /// 星図巡りの順（星域 1 → 4、主は各星域の最後）。選んだ理由は docs/ART.md「立ち絵」。
        /// </summary>
        public static IReadOnlyList<Entry> PortraitAssignments => _portraits;

        static readonly Entry[] _portraits =
        {
            // ---- 1. 灯の港 ----
            new Entry("harbor-apprentice", "delapouite", "figurehead", 0.98f),
            new Entry("harbor-keeper", "delapouite", "monk-face", 0.94f),
            new Entry("harbor-master", "delapouite", "lucky-fisherman", 0.98f),
            // ---- 2. 鏡の書庫 ----
            new Entry("library-scribe", "skoll", "read", 0.96f),
            new Entry("library-twins", "delapouite", "face-to-face", 1.0f),
            new Entry("library-curator", "caro-asercion", "philosopher-bust", 0.98f),
            // ---- 3. 霧の観測所 ----
            new Entry("observatory-forecaster", "lorc", "person-in-blizzard", 1.0f),
            new Entry("observatory-seer", "cathelineau", "witch-face", 0.96f),
            new Entry("observatory-director", "lorc", "doctor-face", 0.9f),
            // ---- 4. 星界の門 ----
            new Entry("gate-traveler", "delapouite", "hiking", 0.98f),
            new Entry("gate-knight", "delapouite", "black-knight-helm", 0.88f),
            new Entry("gate-elder", "delapouite", "sun-priest", 1.0f),
        };

        /// <summary>作者フォルダ名 → 表示名（クレジット用。game-icons.net の license.txt の表記）。</summary>
        public static string AuthorDisplayName(string author)
        {
            switch (author)
            {
                case "lorc": return "Lorc";
                case "delapouite": return "Delapouite";
                case "caro-asercion": return "Caro Asercion";
                case "skoll": return "Skoll";
                case "cathelineau": return "Cathelineau";
                default: return author;
            }
        }

        /// <summary>作者ごとのライセンス（game-icons.net の license.txt。Viscious Speed と Zeromancer だけ CC0）。</summary>
        public static string License(string author) =>
            author == "viscious-speed" || author == "zeromancer" ? "CC0" : "CC BY 3.0";

        Dictionary<string, Entry> _byId;

        public Entry Find(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_byId == null || _byId.Count != entries.Count)
            {
                _byId = new Dictionary<string, Entry>();
                foreach (var e in entries) _byId[e.cardId] = e;
            }
            return _byId.TryGetValue(cardId, out var hit) ? hit : null;
        }

        /// <summary>図鑑・クレジット画面に出す 1 行（4b で使う）。</summary>
        public string CreditLine(string cardId)
        {
            var e = Find(cardId);
            return e == null ? "" : e.icon + " by " + AuthorDisplayName(e.author) + " (game-icons.net, " + License(e.author) + ")";
        }
    }
}
