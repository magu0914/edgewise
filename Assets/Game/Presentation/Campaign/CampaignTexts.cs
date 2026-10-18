using System.Collections.Generic;
using System.Text;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.UI;
using UnityEngine;

namespace Edgewise.Game.Campaign
{
    /// <summary>星域の画面の「持っているカード」の 1 枠。</summary>
    public readonly struct OpponentCardSlot
    {
        /// <summary>見たことのあるカードの ID。まだ見ていなければ null（「？」で表示）。</summary>
        public readonly string CardId;

        /// <summary>プレイヤーも持っている（手に入れた印）。</summary>
        public readonly bool Owned;

        /// <summary>原作モードで相手に取られたカード（勝てば取り返せる）。</summary>
        public readonly bool Captured;

        public OpponentCardSlot(string cardId, bool owned, bool captured)
        {
            CardId = cardId;
            Owned = owned;
            Captured = captured;
        }

        public bool Seen => CardId != null;
    }

    /// <summary>
    /// 星図巡りの画面に出す文字と、表示用の小さな計算（表示に依存しない。EditMode でテストする）。
    /// 文字そのものは UiText に集める（フォントの焼き込み対象）。
    /// </summary>
    public static class CampaignTexts
    {
        /// <summary>今いる星域の番号（解放済みの最も奥の星域）。</summary>
        public static int FrontierRegion(CampaignState state)
        {
            int n = 1;
            foreach (var r in CampaignData.Regions)
            {
                if (state.IsUnlocked(r.Id) && r.Number > n) n = r.Number;
            }
            return n;
        }

        /// <summary>集めたカードの種類数。</summary>
        public static int CollectedKinds(CampaignState state)
        {
            int n = 0;
            foreach (var kv in state.Collection)
            {
                if (kv.Value > 0) n++;
            }
            return n;
        }

        /// <summary>「収集 18/50」。</summary>
        public static string Collection(CampaignState state) =>
            UiText.ProgressCollect + CollectedKinds(state) + "/" + CardCatalog.All.Count;

        /// <summary>タイトルの進み具合（例: 「星域 2・収集 18/50」）。</summary>
        public static string TitleProgress(CampaignState state) =>
            UiText.ProgressRegion + FrontierRegion(state) + UiText.ProgressSep + Collection(state);

        /// <summary>相手の強さ（★1〜5）。AI の段階の表示名から決める。</summary>
        public static int Stars(Opponent opponent)
        {
            switch (opponent?.StrengthLabel)
            {
                case "Easy": return 1;
                case "Easy+": return 2;
                case "Normal": return 3;
                case "Normal+": return 4;
                case "Hard-": return 4;
                case "Hard": return 5;
                default: return 3;
            }
        }

        public static string StarText(int stars)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++) sb.Append(i < stars ? "★" : "☆");
            return sb.ToString();
        }

        /// <summary>「2勝 1敗 0分」。</summary>
        public static string Record(OpponentRecord r) =>
            r.Wins + UiText.RecordWin + " " + r.Losses + UiText.RecordLoss + " " + r.Draws + UiText.RecordDraw;

        /// <summary>星図の進み具合: 勝った相手を ●、未勝利を ○（星図の順）。</summary>
        public static string RegionProgress(CampaignState state, Region region)
        {
            var sb = new StringBuilder();
            foreach (var o in region.Opponents) sb.Append(state.Record(o.Id).Wins > 0 ? "●" : "○");
            return sb.ToString();
        }

        /// <summary>星域の主に勝ったことがあるか（巡り終えたか）。</summary>
        public static bool IsCleared(CampaignState state, Region region) => state.Record(region.Boss.Id).Wins > 0;

        /// <summary>ルールの説明（1 行ずつ: 名前と短い説明）。Open OFF も 1 行として出す。基本だけなら「基本」の 1 行。</summary>
        public static List<KeyValuePair<string, string>> RuleLines(RuleSet r)
        {
            var lines = new List<KeyValuePair<string, string>>();
            void Line(bool on, string name, string desc)
            {
                if (on) lines.Add(new KeyValuePair<string, string>(name, desc));
            }
            Line(r.Open, UiText.RuleOpen, UiText.RuleOpenDesc);
            Line(!r.Open, UiText.RuleOpenOffName, UiText.RuleOpenOffDesc);
            Line(r.Random, UiText.RuleRandom, UiText.RuleRandomDesc);
            Line(r.Same, UiText.RuleSame, UiText.RuleSameDesc);
            Line(r.SameWall, UiText.RuleSameWall, UiText.RuleSameWallDesc);
            Line(r.Plus, UiText.RulePlus, UiText.RulePlusDesc);
            Line(r.Combo, UiText.RuleCombo, UiText.RuleComboDesc);
            Line(r.Elemental, UiText.RuleElemental, UiText.RuleElementalDesc);
            Line(r.SuddenDeath, UiText.RuleSuddenDeath, UiText.RuleSuddenDeathDesc);
            Line(r.Pivot, UiText.RulePivot, UiText.PivotDesc(r));
            bool special = r.Same || r.Plus || r.Combo || r.Elemental || r.SuddenDeath || r.Pivot || r.Random || !r.Open;
            if (!special) lines.Add(new KeyValuePair<string, string>(UiText.RuleBasicName, UiText.RuleBasicDesc));
            return lines;
        }

        /// <summary>星図の印に並べるルールの小さな札（例: Open / Same / Plus）。</summary>
        public static List<string> RuleChips(RuleSet r)
        {
            var chips = new List<string> { r.Open ? UiText.ChipOpen : UiText.ChipClosed };
            if (r.Same) chips.Add(UiText.ChipSame);
            if (r.SameWall) chips.Add(UiText.ChipWall);
            if (r.Plus) chips.Add(UiText.ChipPlus);
            if (r.Combo) chips.Add(UiText.ChipCombo);
            if (r.Elemental) chips.Add(UiText.ChipElemental);
            if (r.SuddenDeath) chips.Add(UiText.ChipSudden);
            if (r.Pivot) chips.Add(UiText.ChipPivot);
            return chips;
        }

        /// <summary>
        /// 星域の画面の「持っているカード」: 相手の今の持ちカード（原作モードで取ったカードを含む）。
        /// 見たことのあるカードは ID、未見は null。プレイヤーも持っていれば Owned、相手に取られたカードなら Captured。
        /// </summary>
        public static List<OpponentCardSlot> OpponentCards(CampaignState state, Opponent opponent)
        {
            var visible = CampaignRules.VisibleOpponentCards(state, opponent);
            var pool = CampaignRules.OpponentPool(state, opponent);
            var captured = state.Record(opponent.Id).Captured;
            var list = new List<OpponentCardSlot>();
            for (int i = 0; i < visible.Length; i++)
            {
                var id = visible[i];
                bool cap = captured.ContainsKey(pool[i]);
                // 取られたカードは自分のカードなので、まだ「見て」いなくても表で出す
                if (id == null && cap) id = pool[i];
                list.Add(new OpponentCardSlot(id, id != null && state.Count(id) > 0, cap));
            }
            return list;
        }

        /// <summary>
        /// ヒントの決定化の候補（申し送り）: 相手の持ちカードをすべて見ていればその持ちカード、そうでなければ全カード。
        /// </summary>
        public static IReadOnlyList<CardDef> HintPool(CampaignState state, Opponent opponent)
        {
            var pool = CampaignRules.OpponentPool(state, opponent);
            var seen = state.Record(opponent.Id).Seen;
            var defs = new List<CardDef>();
            foreach (var id in pool)
            {
                bool known = false;
                foreach (var s in seen)
                {
                    if (s == id)
                    {
                        known = true;
                        break;
                    }
                }
                if (!known) return CardCatalog.All;
                defs.Add(CardCatalog.ById[id]);
            }
            return defs;
        }

        /// <summary>
        /// 原作モードの取られ方の表示: 取られたら「〈名前〉 を取られた」、手持ちが少なくて守られたら「手持ちが少ないので、〈名前〉 は取られなかった」。
        /// どちらでもなければ null（原作モード OFF・負けていない）。
        /// </summary>
        public static string ClassicTakeMessage(MatchApplication application)
        {
            if (application == null) return null;
            if (application.TakenCard != null) return CardName(application.TakenCard) + UiText.TakeSuffix;
            if (application.ProtectedCard != null)
                return UiText.TakeProtectedPrefix + CardName(application.ProtectedCard) + UiText.TakeProtectedSuffix;
            return null;
        }

        public static string CardName(string id) => id != null && CardCatalog.ById.TryGetValue(id, out var d) ? d.Name : id;

        /// <summary>「〈星域名〉 を巡り終えた」。</summary>
        public static string Cleared(Region region) => region.Name + UiText.ClearedSuffix;

        /// <summary>立ち絵の仮表示に使う頭文字（名前の 1 文字目）。</summary>
        public static string Initial(Opponent opponent) =>
            string.IsNullOrEmpty(opponent?.Name) ? "?" : opponent.Name.Substring(0, 1);

        /// <summary>デッキの目盛りの文字「合計 Lv 13 / 15」。</summary>
        public static string DeckTotal(int total, int cap) => UiText.DeckTotal + total + UiText.DeckSlash + cap;

        /// <summary>デッキの状態の文字（上限超え・あと何枚・準備OK）。</summary>
        public static string DeckStatus(DeckEditModel deck)
        {
            if (deck.OverCap) return UiText.DeckOver;
            int need = CampaignRules.DeckSize - deck.Selected.Count;
            if (need > 0) return UiText.DeckNeedPrefix + need + UiText.DeckNeedSuffix;
            return UiText.DeckReady;
        }
    }

    /// <summary>星域ごとの色（立ち絵の仮表示・星図の印）。</summary>
    public static class CampaignTheme
    {
        public static Color RegionColor(string regionId)
        {
            switch (regionId)
            {
                case "harbor": return new Color(0.98f, 0.66f, 0.30f); // 灯の港: 灯火の橙
                case "library": return new Color(0.46f, 0.80f, 0.92f); // 鏡の書庫: 鏡の水色
                case "observatory": return new Color(0.70f, 0.62f, 0.96f); // 霧の観測所: 霧の紫
                case "gate": return new Color(0.98f, 0.86f, 0.46f); // 星界の門: 星の金
                default: return new Color(0.8f, 0.82f, 0.92f);
            }
        }

        public static Color RegionColor(Region r) => RegionColor(r?.Id);
    }
}
