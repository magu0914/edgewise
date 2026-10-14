// オリジナルカード 50 枚（Lv1〜10 各 5 枚）。仮テーマ: 星座と幻獣をモチーフにした架空の生き物「星図カード」。
// 名前はすべて造語。既存作品の固有名詞は使わない。TS 版 src/data/cards.ts をそのまま移植したもの。
// Sides は (上, 右, 下, 左)、10 = A。強さの予算は Budget.cs の LevelBudgets を参照。

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Edgewise.Core.Data
{
    /// <summary>カードのルール上のデータ（辺の値・属性・系統）の正本。</summary>
    public static class CardCatalog
    {
        static readonly CardDef[] Definitions =
        {
            // ---- Lv1 ----
            new CardDef("glimmouse", "Glimmouse", 1, new Sides(1, 4, 1, 5), null, Tribe.Beast),
            new CardDef("dustwisp", "Dustwisp", 1, new Sides(5, 1, 3, 2), Element.Wind, Tribe.Spirit),
            new CardDef("coglet", "Coglet", 1, new Sides(2, 3, 3, 4), null, Tribe.Machine),
            new CardDef("emberkit", "Emberkit", 1, new Sides(4, 2, 1, 5), Element.Fire, Tribe.Beast),
            new CardDef("scalefry", "Scalefry", 1, new Sides(6, 1, 2, 4), Element.Water, Tribe.Dragon),

            // ---- Lv2 ----
            new CardDef("pyxmoth", "Pyxmoth", 2, new Sides(2, 5, 1, 5), Element.Holy, Tribe.Spirit),
            new CardDef("bolthare", "Bolthare", 2, new Sides(6, 2, 4, 1), Element.Thunder, Tribe.Beast),
            new CardDef("rivetcrab", "Rivetcrab", 2, new Sides(3, 3, 5, 3), Element.Water, Tribe.Machine),
            new CardDef("frostnewt", "Frostnewt", 2, new Sides(5, 4, 1, 4), Element.Ice, Tribe.Dragon),
            new CardDef("mirelamp", "Mirelamp", 2, new Sides(1, 6, 2, 6), Element.Earth, Tribe.Spirit),

            // ---- Lv3 ----
            new CardDef("lynxlight", "Lynxlight", 3, new Sides(6, 3, 2, 5), Element.Holy, Tribe.Beast),
            new CardDef("gearowl", "Gearowl", 3, new Sides(4, 4, 4, 3), null, Tribe.Machine),
            new CardDef("cinderdrake", "Cinderdrake", 3, new Sides(2, 7, 3, 3), Element.Fire, Tribe.Dragon),
            new CardDef("hollowshade", "Hollowshade", 3, new Sides(7, 1, 6, 3), Element.Dark, Tribe.Spirit),
            new CardDef("stonetoad", "Stonetoad", 3, new Sides(3, 5, 6, 2), Element.Earth, Tribe.Beast),

            // ---- Lv4 ----
            new CardDef("velahound", "Velahound", 4, new Sides(7, 2, 5, 4), null, Tribe.Beast),
            new CardDef("clockwyrm", "Clockwyrm", 4, new Sides(5, 5, 3, 5), Element.Thunder, Tribe.Machine),
            new CardDef("tidewraith", "Tidewraith", 4, new Sides(2, 7, 4, 6), Element.Water, Tribe.Spirit),
            new CardDef("marshwyvern", "Marshwyvern", 4, new Sides(6, 3, 7, 2), Element.Earth, Tribe.Dragon),
            new CardDef("voltsparrow", "Voltsparrow", 4, new Sides(4, 6, 2, 7), Element.Thunder, Tribe.Machine),

            // ---- Lv5 ----
            new CardDef("lupusfang", "Lupusfang", 5, new Sides(7, 5, 2, 6), Element.Ice, Tribe.Beast),
            new CardDef("orreryx", "Orreryx", 5, new Sides(5, 6, 5, 4), Element.Holy, Tribe.Machine),
            new CardDef("gloamveil", "Gloamveil", 5, new Sides(3, 8, 2, 7), Element.Dark, Tribe.Spirit),
            new CardDef("emberscale", "Emberscale", 5, new Sides(8, 3, 6, 4), Element.Fire, Tribe.Dragon),
            new CardDef("corvusarc", "Corvusarc", 5, new Sides(6, 2, 8, 5), Element.Dark, Tribe.Beast),

            // ---- Lv6 ----
            new CardDef("aquilaburst", "Aquilaburst", 6, new Sides(8, 6, 3, 5), Element.Wind, Tribe.Beast),
            new CardDef("ironcetus", "Ironcetus", 6, new Sides(4, 7, 7, 4), Element.Water, Tribe.Machine),
            new CardDef("lanternmare", "Lanternmare", 6, new Sides(2, 8, 8, 4), Element.Fire, Tribe.Spirit),
            new CardDef("serpenscoil", "Serpenscoil", 6, new Sides(7, 4, 8, 3), Element.Earth, Tribe.Dragon),
            new CardDef("rimeloom", "Rimeloom", 6, new Sides(6, 6, 5, 6), Element.Ice, Tribe.Spirit),

            // ---- Lv7 ----
            new CardDef("leomane", "Leomane", 7, new Sides(9, 4, 6, 5), Element.Holy, Tribe.Beast),
            new CardDef("forgecolossus", "Forgecolossus", 7, new Sides(6, 8, 4, 7), Element.Fire, Tribe.Machine),
            new CardDef("auroraveil", "Auroraveil", 7, new Sides(3, 9, 5, 7), Element.Ice, Tribe.Spirit),
            new CardDef("draconyx", "Draconyx", 7, new Sides(8, 7, 8, 2), Element.Dark, Tribe.Dragon),
            new CardDef("stormgear", "Stormgear", 7, new Sides(5, 6, 9, 4), Element.Thunder, Tribe.Machine),

            // ---- Lv8 ----
            new CardDef("cygnwraith", "Cygnwraith", 8, new Sides(10, 3, 8, 6), Element.Wind, Tribe.Spirit),
            new CardDef("taurocrag", "Taurocrag", 8, new Sides(8, 8, 4, 8), Element.Earth, Tribe.Beast),
            new CardDef("gravelith", "Gravelith", 8, new Sides(4, 9, 9, 5), Element.Earth, Tribe.Machine),
            new CardDef("voltwyrm", "Voltwyrm", 8, new Sides(9, 5, 3, 10), Element.Thunder, Tribe.Dragon),
            new CardDef("nocturnis", "Nocturnis", 8, new Sides(6, 10, 2, 9), Element.Dark, Tribe.Spirit),

            // ---- Lv9 ----
            new CardDef("orionmane", "Orionmane", 9, new Sides(10, 7, 4, 9), Element.Fire, Tribe.Beast),
            new CardDef("astrolabion", "Astrolabion", 9, new Sides(7, 8, 8, 7), Element.Holy, Tribe.Machine),
            new CardDef("cassiveil", "Cassiveil", 9, new Sides(3, 10, 9, 8), Element.Water, Tribe.Spirit),
            new CardDef("scorpidrake", "Scorpidrake", 9, new Sides(9, 4, 10, 6), Element.Dark, Tribe.Dragon),
            new CardDef("polarion", "Polarion", 9, new Sides(8, 9, 6, 7), Element.Ice, Tribe.Dragon),

            // ---- Lv10 ----
            new CardDef("andromede", "Andromede", 10, new Sides(10, 8, 4, 10), Element.Holy, Tribe.Spirit),
            new CardDef("pegasion", "Pegasion", 10, new Sides(9, 10, 6, 7), Element.Wind, Tribe.Beast),
            new CardDef("ophiwyrm", "Ophiwyrm", 10, new Sides(10, 4, 10, 8), Element.Earth, Tribe.Dragon),
            new CardDef("sextantor", "Sextantor", 10, new Sides(8, 8, 8, 8), Element.Thunder, Tribe.Machine),
            new CardDef("ursamaw", "Ursamaw", 10, new Sides(6, 10, 10, 5), Element.Ice, Tribe.Beast),
        };

        /// <summary>全カード定義（定義順 = Lv 順）。</summary>
        public static readonly ReadOnlyCollection<CardDef> All = new ReadOnlyCollection<CardDef>(Definitions);

        /// <summary>id → カード定義。</summary>
        public static readonly IReadOnlyDictionary<string, CardDef> ById = BuildIndex();

        static IReadOnlyDictionary<string, CardDef> BuildIndex()
        {
            var db = new Dictionary<string, CardDef>();
            foreach (var c in Definitions) db[c.Id] = c;
            return new ReadOnlyDictionary<string, CardDef>(db);
        }
    }
}
