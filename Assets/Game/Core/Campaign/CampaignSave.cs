using System;
using System.Collections.Generic;
using Edgewise.Core.Data;

namespace Edgewise.Core.Campaign
{
    /// <summary>読み込みの結果の種類。</summary>
    public enum SaveLoadStatus
    {
        /// <summary>そのまま読めた。</summary>
        Ok = 0,

        /// <summary>セーブがない（空文字・null）。新しく始めた。</summary>
        Empty = 1,

        /// <summary>古い形式から移行して読めた。</summary>
        Migrated = 2,

        /// <summary>読めたが、知らないカード・相手などを捨てて直した（Warnings に内容）。</summary>
        Repaired = 3,

        /// <summary>壊れていて読めなかった。新しく始めた（Message に理由）。元のデータは上書きする前に退避を勧める。</summary>
        Corrupt = 4,

        /// <summary>このゲームより新しい形式で読めなかった。新しく始めた（元のデータは上書きしないこと）。</summary>
        TooNew = 5,
    }

    /// <summary>CampaignSave.Load の結果。State は常に使える進行（失敗時は新しい進行）。</summary>
    public sealed class SaveLoadResult
    {
        public CampaignState State { get; }
        public SaveLoadStatus Status { get; }

        /// <summary>失敗の理由（Corrupt / TooNew のとき）。</summary>
        public string Message { get; }

        /// <summary>直した点（Repaired / Migrated のとき）。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>読んだデータの形式のバージョン（読めなかったときは -1）。</summary>
        public int SourceVersion { get; }

        /// <summary>読めずに新しく始めたか（UI はこのとき「データが壊れていたので新しく始めます」を知らせる）。</summary>
        public bool StartedOver => Status == SaveLoadStatus.Empty || Status == SaveLoadStatus.Corrupt || Status == SaveLoadStatus.TooNew;

        public SaveLoadResult(CampaignState state, SaveLoadStatus status, string message, IReadOnlyList<string> warnings, int sourceVersion)
        {
            State = state;
            Status = status;
            Message = message;
            Warnings = warnings ?? Array.Empty<string>();
            SourceVersion = sourceVersion;
        }
    }

    /// <summary>
    /// 星図巡りのセーブ（DESIGN §12-8）: CampaignState ⇔ JSON。1 スロット。PlayerPrefs への出し入れは Presentation 側で行う。
    /// <para>形式（version 1）:</para>
    /// <code>
    /// {"version":1,"seed":0,"classic":false,
    ///  "collection":{"glimmouse":2,...},          // カード ID → 枚数（カタログ順）
    ///  "unlocked":["harbor",...],                 // 解放済みの星域（星図順）
    ///  "opponents":{"harbor-apprentice":{"wins":1,"losses":0,"draws":0,"captured":{"x":1},"seen":["a",...]},...},
    ///  "lastDecks":{"harbor":["a","b","c","d","e"],...}}
    /// </code>
    /// 移行の口: Load は "version" を見て、古い形式なら MigrateStep を 1 版ずつ適用してから読む。
    /// version のないデータは version 0（5a の設計段階の仮形式: 手持ちを ID の配列で持ち、解放を星域の数で持つ）として扱う。
    /// </summary>
    public static class CampaignSave
    {
        /// <summary>今の形式のバージョン。形式を変えたら上げて、MigrateStep に 1 つ前からの移行を書く。</summary>
        public const int CurrentVersion = 1;

        /// <summary>1 枚のカードを持てる最大枚数（壊れたデータで巨大な数が入っても困らないように）。</summary>
        public const int MaxCopies = 99;

        /// <summary>勝敗数の上限。</summary>
        public const int MaxCount = 999999;

        // ------------------------------------------------------------------
        // 書き出し
        // ------------------------------------------------------------------

        public static string ToJson(CampaignState state) => MiniJson.Write(ToJsonValue(state));

        public static JsonValue ToJsonValue(CampaignState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var root = JsonValue.NewObject();
            root.Set("version", JsonValue.Integer(CurrentVersion));
            root.Set("seed", JsonValue.Integer(state.Seed));
            root.Set("classic", JsonValue.Bool(state.ClassicMode));

            var col = JsonValue.NewObject();
            foreach (var id in CampaignUtil.SortedKeys(state.Collection)) col.Set(id, JsonValue.Integer(state.Collection[id]));
            root.Set("collection", col);

            var unlocked = JsonValue.NewArray();
            foreach (var id in state.UnlockedRegions) unlocked.Add(JsonValue.String(id));
            root.Set("unlocked", unlocked);

            var opps = JsonValue.NewObject();
            foreach (var o in CampaignData.Opponents)
            {
                if (!state.Records.TryGetValue(o.Id, out var r)) continue;
                var ro = JsonValue.NewObject();
                ro.Set("wins", JsonValue.Integer(r.Wins));
                ro.Set("losses", JsonValue.Integer(r.Losses));
                ro.Set("draws", JsonValue.Integer(r.Draws));
                var cap = JsonValue.NewObject();
                foreach (var id in CampaignUtil.SortedKeys(r.Captured)) cap.Set(id, JsonValue.Integer(r.Captured[id]));
                ro.Set("captured", cap);
                var seen = JsonValue.NewArray();
                foreach (var id in r.Seen) seen.Add(JsonValue.String(id));
                ro.Set("seen", seen);
                opps.Set(o.Id, ro);
            }
            root.Set("opponents", opps);

            var decks = JsonValue.NewObject();
            foreach (var reg in CampaignData.Regions)
            {
                if (!state.LastDecks.TryGetValue(reg.Id, out var d)) continue;
                var arr = JsonValue.NewArray();
                foreach (var id in d) arr.Add(JsonValue.String(id));
                decks.Set(reg.Id, arr);
            }
            root.Set("lastDecks", decks);
            return root;
        }

        // ------------------------------------------------------------------
        // 読み込み
        // ------------------------------------------------------------------

        /// <summary>
        /// JSON から進行を読む。例外は投げない: 空なら新しい進行（Empty）、壊れていれば新しい進行（Corrupt）、
        /// 新しすぎる形式も新しい進行（TooNew）。知らないカード・相手・星域は捨てて読む（Repaired）。
        /// fallbackSeed は新しく始めるときの Seed。
        /// </summary>
        public static SaveLoadResult Load(string json, long fallbackSeed = 0)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new SaveLoadResult(CampaignRules.NewGame(fallbackSeed), SaveLoadStatus.Empty, null, null, -1);
            JsonValue root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (JsonException e)
            {
                return Fail(SaveLoadStatus.Corrupt, "not valid JSON: " + e.Message, fallbackSeed);
            }
            try
            {
                return FromJsonValue(root, fallbackSeed);
            }
            catch (JsonException e)
            {
                return Fail(SaveLoadStatus.Corrupt, "unexpected shape: " + e.Message, fallbackSeed);
            }
            catch (ArgumentException e)
            {
                return Fail(SaveLoadStatus.Corrupt, "invalid data: " + e.Message, fallbackSeed);
            }
            catch (InvalidOperationException e)
            {
                return Fail(SaveLoadStatus.Corrupt, "invalid data: " + e.Message, fallbackSeed);
            }
            catch (OverflowException e)
            {
                return Fail(SaveLoadStatus.Corrupt, "number out of range: " + e.Message, fallbackSeed);
            }
        }

        static SaveLoadResult Fail(SaveLoadStatus status, string message, long fallbackSeed) =>
            new SaveLoadResult(CampaignRules.NewGame(fallbackSeed), status, message, null, -1);

        static SaveLoadResult FromJsonValue(JsonValue root, long fallbackSeed)
        {
            if (root.Kind != JsonKind.Object) throw new JsonException("the root is not an object");
            var warnings = new List<string>();
            var versionValue = root.Get("version");
            int version = versionValue == null ? 0 : ToInt(versionValue, "version");
            if (version < 0) throw new JsonException("negative version");
            if (version > CurrentVersion)
                return new SaveLoadResult(CampaignRules.NewGame(fallbackSeed), SaveLoadStatus.TooNew,
                    "save version " + version + " is newer than " + CurrentVersion, null, version);

            int source = version;
            while (version < CurrentVersion)
            {
                root = MigrateStep(root, version, warnings);
                version++;
            }

            long seed = root.Get("seed") is JsonValue sv && sv.Kind == JsonKind.Integer ? sv.AsLong : fallbackSeed;
            bool classic = root.Get("classic") is JsonValue cv && cv.Kind == JsonKind.Bool && cv.AsBool;

            var collection = ReadCounts(Require(root, "collection", JsonKind.Object), "collection", warnings);

            var unlocked = new List<string>();
            var ul = root.Get("unlocked");
            if (ul != null && ul.Kind == JsonKind.Array)
            {
                foreach (var v in ul.Items)
                {
                    string id = v.Kind == JsonKind.String ? v.AsString : null;
                    if (id != null && CampaignData.RegionById(id) != null) unlocked.Add(id);
                    else warnings.Add("unknown region dropped: " + Describe(v));
                }
            }
            else if (ul != null) warnings.Add("unlocked is not an array");
            if (!unlocked.Contains(CampaignData.Regions[0].Id))
            {
                unlocked.Insert(0, CampaignData.Regions[0].Id);
                if (ul != null) warnings.Add("the first region was not unlocked");
            }

            var records = new Dictionary<string, OpponentRecord>();
            var opps = root.Get("opponents");
            if (opps != null && opps.Kind == JsonKind.Object)
            {
                foreach (var kv in opps.Members)
                {
                    if (CampaignData.OpponentById(kv.Key) == null)
                    {
                        warnings.Add("unknown opponent dropped: " + kv.Key);
                        continue;
                    }
                    var o = kv.Value;
                    if (o.Kind != JsonKind.Object)
                    {
                        warnings.Add("opponent record is not an object: " + kv.Key);
                        continue;
                    }
                    int wins = ReadCount(o.Get("wins"), kv.Key + ".wins", warnings);
                    int losses = ReadCount(o.Get("losses"), kv.Key + ".losses", warnings);
                    int draws = ReadCount(o.Get("draws"), kv.Key + ".draws", warnings);
                    var cap = o.Get("captured");
                    var captured = cap != null && cap.Kind == JsonKind.Object
                        ? ReadCounts(cap, kv.Key + ".captured", warnings)
                        : new Dictionary<string, int>();
                    var seen = ReadIds(o.Get("seen"), kv.Key + ".seen", warnings);
                    records[kv.Key] = new OpponentRecord(wins, losses, draws, captured, seen);
                }
            }
            else if (opps != null) warnings.Add("opponents is not an object");

            var decks = new Dictionary<string, IReadOnlyList<string>>();
            var ld = root.Get("lastDecks");
            if (ld != null && ld.Kind == JsonKind.Object)
            {
                foreach (var kv in ld.Members)
                {
                    if (CampaignData.RegionById(kv.Key) == null)
                    {
                        warnings.Add("unknown region deck dropped: " + kv.Key);
                        continue;
                    }
                    var ids = ReadIds(kv.Value, "lastDecks." + kv.Key, warnings);
                    // 最後のデッキは「覚えておくだけ」。今の手持ちで組めなくなっていても捨てず、UI がデッキ編集で直す
                    if (ids.Count == CampaignRules.DeckSize) decks[kv.Key] = ids;
                    else warnings.Add("deck with " + ids.Count + " cards dropped: " + kv.Key);
                }
            }
            else if (ld != null) warnings.Add("lastDecks is not an object");

            // 5 種類未満ではデッキが組めず進めなくなる。壊れたデータとして扱う
            int distinct = 0;
            foreach (var kv in collection)
            {
                if (kv.Value > 0) distinct++;
            }
            if (distinct < CampaignRules.DeckSize)
                return Fail(SaveLoadStatus.Corrupt, "the collection has only " + distinct + " kinds of cards", fallbackSeed);

            var state = new CampaignState(seed, classic, collection, unlocked, records, decks);
            var status = source < CurrentVersion ? SaveLoadStatus.Migrated : warnings.Count > 0 ? SaveLoadStatus.Repaired : SaveLoadStatus.Ok;
            return new SaveLoadResult(state, status, null, warnings, source);
        }

        /// <summary>
        /// 形式を 1 版進める（from → from+1）。新しい形式を足すときはここに case を足す。
        /// v0 → v1: 手持ち "cards"（ID の配列。同じ ID を並べて枚数を表す）を "collection"（ID → 枚数）に、
        /// 解放 "regions"（開いた星域の数）を "unlocked"（星域 ID の配列）にする。
        /// </summary>
        static JsonValue MigrateStep(JsonValue root, int from, List<string> warnings)
        {
            switch (from)
            {
                case 0:
                {
                    var next = JsonValue.NewObject();
                    foreach (var kv in root.Members)
                    {
                        if (kv.Key == "cards" || kv.Key == "regions") continue;
                        next.Set(kv.Key, kv.Value);
                    }
                    var col = JsonValue.NewObject();
                    var cards = root.Get("cards");
                    if (cards == null || cards.Kind != JsonKind.Array) throw new JsonException("v0 save needs a cards array");
                    var counts = new Dictionary<string, int>();
                    var order = new List<string>();
                    foreach (var v in cards.Items)
                    {
                        if (v.Kind != JsonKind.String) throw new JsonException("v0 cards must be strings");
                        string id = v.AsString;
                        if (!counts.ContainsKey(id))
                        {
                            counts[id] = 0;
                            order.Add(id);
                        }
                        counts[id]++;
                    }
                    foreach (var id in order) col.Set(id, JsonValue.Integer(counts[id]));
                    next.Set("collection", col);

                    int regions = root.Get("regions") is JsonValue rv && rv.Kind == JsonKind.Integer ? (int)Clamp(rv.AsLong, 1, CampaignData.Regions.Count) : 1;
                    var unlocked = JsonValue.NewArray();
                    for (int i = 0; i < regions; i++) unlocked.Add(JsonValue.String(CampaignData.Regions[i].Id));
                    next.Set("unlocked", unlocked);
                    next.Set("version", JsonValue.Integer(1));
                    warnings.Add("migrated from version 0");
                    return next;
                }
                default:
                    throw new JsonException("no migration from version " + from);
            }
        }

        // ------------------------------------------------------------------
        // 読み込みの部品
        // ------------------------------------------------------------------

        static JsonValue Require(JsonValue obj, string key, JsonKind kind)
        {
            var v = obj.Get(key);
            if (v == null) throw new JsonException("missing " + key);
            if (v.Kind != kind) throw new JsonException(key + " must be " + kind);
            return v;
        }

        static int ToInt(JsonValue v, string what)
        {
            if (v.Kind != JsonKind.Integer) throw new JsonException(what + " must be an integer");
            long x = v.AsLong;
            if (x < int.MinValue || x > int.MaxValue) throw new JsonException(what + " out of range");
            return (int)x;
        }

        static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;

        static int ReadCount(JsonValue v, string what, List<string> warnings)
        {
            if (v == null) return 0;
            if (v.Kind != JsonKind.Integer)
            {
                warnings.Add(what + " is not an integer");
                return 0;
            }
            long x = v.AsLong;
            if (x < 0 || x > MaxCount)
            {
                warnings.Add(what + " out of range: " + x);
                return (int)Clamp(x, 0, MaxCount);
            }
            return (int)x;
        }

        static Dictionary<string, int> ReadCounts(JsonValue obj, string what, List<string> warnings)
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in obj.Members)
            {
                if (!CardCatalog.ById.ContainsKey(kv.Key))
                {
                    warnings.Add("unknown card dropped from " + what + ": " + kv.Key);
                    continue;
                }
                if (kv.Value.Kind != JsonKind.Integer)
                {
                    warnings.Add(what + "." + kv.Key + " is not an integer");
                    continue;
                }
                long n = kv.Value.AsLong;
                if (n <= 0)
                {
                    if (n < 0) warnings.Add(what + "." + kv.Key + " is negative");
                    continue;
                }
                if (n > MaxCopies)
                {
                    warnings.Add(what + "." + kv.Key + " clamped to " + MaxCopies);
                    n = MaxCopies;
                }
                result[kv.Key] = (int)n;
            }
            return result;
        }

        static List<string> ReadIds(JsonValue arr, string what, List<string> warnings)
        {
            var result = new List<string>();
            if (arr == null) return result;
            if (arr.Kind != JsonKind.Array)
            {
                warnings.Add(what + " is not an array");
                return result;
            }
            foreach (var v in arr.Items)
            {
                if (v.Kind == JsonKind.String && CardCatalog.ById.ContainsKey(v.AsString)) result.Add(v.AsString);
                else warnings.Add("unknown card dropped from " + what + ": " + Describe(v));
            }
            return result;
        }

        static string Describe(JsonValue v)
        {
            string s = MiniJson.Write(v);
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }
    }
}
