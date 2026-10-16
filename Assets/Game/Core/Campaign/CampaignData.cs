// 星図巡りのデータ（DESIGN §12-2 / §12-3 / §12-5 / §12-7）。
// 名前・肩書き・台詞は仮（5c で推敲）。持ちカードと AI の強さ・性格は Phase 5a の自己対戦で調整した値（docs/BALANCE.md）。
// ID は表示名ではなく役割で付ける（名前を変えてもセーブが壊れないように）。

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Edgewise.Core.AI;

namespace Edgewise.Core.Campaign
{
    /// <summary>
    /// 星図巡りで使う AI の強さの段階（DESIGN §12-2 の「Easy+」「Normal+」「Hard−」）。Phase 5a の自己対戦で調整した値（docs/BALANCE.md）。
    /// 星域 1・2（基本ルールと Same/Plus）は、強さの近い同士だと引き分けが 5 割を超え、カードが増えずに進行が止まって感じる（§12-9 の追加目標）。
    /// そこで星域 1・2 の相手は手の選び方の揺れ（ノイズ）を大きくしてある: 読みの深さは段階どおりだが、ときどき最善でない手を選ぶので、
    /// プレイヤーが引き分けに持ち込まれず勝ち切れる。
    /// </summary>
    public static class CampaignAi
    {
        /// <summary>フリー対戦の Easy と同じ（1 手先、ノイズ ±150、Same/Plus/Combo の見落とし 40%）。</summary>
        public static readonly AiProfile Easy = AiProfile.Easy;

        /// <summary>1 手先、ノイズ ±80、見落としなし。</summary>
        public static readonly AiProfile EasyPlus = AiProfile.Easy.WithNoise(80, 0).WithName("Easy+");

        /// <summary>
        /// 星域 1・2 の Normal: 3 手先まで読むが、ノイズ（揺れ）を大きくして引き分けを減らす（相手ごとに幅を変える）。
        /// 主は対戦に Sudden Death が入る（CampaignRules.MatchRules）ので引き分けで止まらず、ノイズは通常の相手より小さい。
        /// </summary>
        public static AiProfile LooseNormal(int noise) => AiProfile.Normal.WithNoise(noise, 0);

        /// <summary>フリー対戦の Normal と同じ。</summary>
        public static readonly AiProfile Normal = AiProfile.Normal;

        /// <summary>
        /// Normal からノイズを除き、決定化の世界を 1 つ増やしたもの。
        /// （深さ 4 にすると Normal に対して 8 割以上勝ち、段階として大きすぎた。）
        /// </summary>
        public static readonly AiProfile NormalPlus = AiProfile.Normal.WithNoise(0, 0).WithWorlds(4).WithName("Normal+");

        /// <summary>Hard の予算を減らし、ノイズを足したもの。性格はボーナスとして評価に足す（Hard のような同点だけの好みではない）。</summary>
        public static readonly AiProfile HardMinus = AiProfile.Hard.WithMaxNodes(120000).WithWorlds(6).WithNoise(30, 0).WithName("Hard-")
            .WithPersonality(null, false);

        /// <summary>フリー対戦の Hard と同じ（読み切り・ノイズなし・40 万ノード）。</summary>
        public static readonly AiProfile Hard = AiProfile.Hard;

        /// <summary>
        /// Hard の読み方（読み切り・ノイズなし）のまま予算を 25,000 ノード・世界 4 に絞ったもの（星界の門の騎士）。
        /// 全予算の Hard は、Pivot＋Sudden Death＋Open OFF でプレイヤー役の Normal に 55% 以上勝ち、持ちカードを弱くしても下がらなかった。
        /// </summary>
        public static readonly AiProfile HardLight = AiProfile.Hard.WithMaxNodes(25000).WithWorlds(4);
    }

    /// <summary>星図巡りの相手の性格（DESIGN §12-5）。値の単位は評価値と同じ 1/100 枚。</summary>
    public static class CampaignPersonalities
    {
        /// <summary>素直: 今すぐ取れる枚数だけを見る。</summary>
        public static readonly AiPersonality Greedy = new AiPersonality("greedy", capture: 45);

        /// <summary>角と端を好む守り型。</summary>
        public static readonly AiPersonality CornerGuard = new AiPersonality("corner-guard", corner: 60, edge: 20, defense: 6);

        /// <summary>攻め気: 取れるなら取りにいく。強い辺を外に向ける。</summary>
        public static readonly AiPersonality Aggressive = new AiPersonality("aggressive", capture: 35, defense: 3);

        /// <summary>Same を狙う。</summary>
        public static readonly AiPersonality SameHunter = new AiPersonality("same-hunter", same: 120);

        /// <summary>Plus を狙う。</summary>
        public static readonly AiPersonality PlusHunter = new AiPersonality("plus-hunter", plus: 120);

        /// <summary>
        /// 連鎖重視。星域 2 は Combo ルールがないので、Same / Plus で一度に複数枚を取る手を好む形で表れる（Combo があれば連鎖も好む）。
        /// </summary>
        public static readonly AiPersonality ChainSeeker = new AiPersonality("chain-seeker", capture: 20, same: 60, plus: 60, combo: 40);

        /// <summary>属性マスを重視する。</summary>
        public static readonly AiPersonality ElementReader = new AiPersonality("element-reader", element: 80);

        /// <summary>読みにくい（揺らぎ大）。</summary>
        public static readonly AiPersonality Fickle = new AiPersonality("fickle", volatility: 120);

        /// <summary>Pivot を早めに切る。</summary>
        public static readonly AiPersonality EarlyPivot = new AiPersonality("early-pivot", pivot: 80);

        /// <summary>守り型（Hard では同点の手の間の好み）。</summary>
        public static readonly AiPersonality Guardian = new AiPersonality("guardian", corner: 20, defense: 8);
    }

    /// <summary>星図巡りの星域・相手・はじめの手持ち。</summary>
    public static class CampaignData
    {
        /// <summary>はじめの手持ち（Lv1〜2 の 7 枚。DESIGN §12-3）。</summary>
        public static readonly ReadOnlyCollection<string> StarterCards = new ReadOnlyCollection<string>(new[]
        {
            "glimmouse", "dustwisp", "coglet", "scalefry", "pyxmoth", "rivetcrab", "mirelamp",
        });

        /// <summary>主の上限は星域の上限 +5（DESIGN §12-2）。</summary>
        public const int BossCapBonus = 5;

        public static readonly ReadOnlyCollection<Region> Regions = new ReadOnlyCollection<Region>(new[]
        {
            new Region("harbor", 1, "灯の港", new RuleSet(open: true), 15, new[]
            {
                new Opponent("harbor-apprentice", "ミラ", "見習い航海士", "harbor", false, 15, "Easy", "素直（取れる枚数だけ見る）",
                    CampaignAi.Easy.WithPersonality(CampaignPersonalities.Greedy),
                    new[] { "bolthare", "frostnewt", "rivetcrab", "pyxmoth", "mirelamp", "gearowl", "lynxlight" }, 1,
                    new Dialogue(
                        new[] { "はじめまして！ 見習い航海士のミラです。", "取れるだけ取る、それがわたしの作戦です！" },
                        new[] { "あれ？ ちゃんと数えてたのに、気づいたらひっくり返されてた……" },
                        new[] { "やった！ いっぱい取れました！" },
                        new[] { "約束どおり、好きなカードをどうぞ。大事にしてくださいね。" },
                        new[] { "ごめんなさい、決まりなので……このカード、預かりますね。" })),
                new Opponent("harbor-keeper", "オルド", "灯台守", "harbor", false, 15, "Easy+", "角と端を好む守り型",
                    CampaignAi.EasyPlus.WithPersonality(CampaignPersonalities.CornerGuard),
                    new[] { "glimmouse", "dustwisp", "emberkit", "stonetoad", "hollowshade", "cinderdrake", "velahound" }, 1,
                    new Dialogue(
                        new[] { "灯台は岬の端に立つものだ。", "わしの札も、隅から崩れることはない。" },
                        new[] { "……隅を固めても、波は真ん中から来るか。" },
                        new[] { "守りは退屈に見えて、最後に効くのさ。" },
                        new[] { "持っていけ。灯りの番の合間に磨いた札だ。" },
                        new[] { "この札はしばらく灯台で預かる。取り返しに来るといい。" })),
                new Opponent("harbor-master", "マーロウ", "港の親方", "harbor", true, 15 + BossCapBonus, "Normal", "攻め気（強い辺で押す）",
                    CampaignAi.LooseNormal(70).WithPersonality(CampaignPersonalities.Aggressive),
                    new[] { "glimmouse", "dustwisp", "scalefry", "emberkit", "bolthare", "hollowshade", "voltsparrow" }, 3,
                    new Dialogue(
                        new[] { "港の親方マーロウだ！", "強い辺で正面から押す、それだけさ。かかってきな！" },
                        new[] { "ははっ、押し負けたか！ いい腕してるじゃないか。" },
                        new[] { "押して押して押し切る！ これが港の流儀だよ。" },
                        new[] { "勝った者には報酬だ。好きなのを持っていきな。" },
                        new[] { "負けは負けだ。こいつはもらっていくよ。" },
                        new[] { "この港じゃもう敵なしだね。", "次は鏡の書庫だ。あそこの連中は数字にうるさいよ、気をつけな！" })),
            }),
            new Region("library", 2, "鏡の書庫", new RuleSet(open: true, same: true, sameWall: true, plus: true), 25, new[]
            {
                new Opponent("library-scribe", "リーフ", "写本係", "library", false, 25, "Normal", "Same を狙う",
                    CampaignAi.LooseNormal(180).WithPersonality(CampaignPersonalities.SameHunter),
                    new[] { "glimmouse", "coglet", "scalefry", "frostnewt", "rivetcrab", "gearowl", "orreryx" }, 3,
                    new Dialogue(
                        new[] { "写本は一字一句、同じに写すのが仕事です。", "盤の上でも、同じ数字が並ぶ瞬間は見逃しません。" },
                        new[] { "写し間違い……いえ、読み違いでした。" },
                        new[] { "ほら、同じ数字がふたつ。きれいに揃いました。" },
                        new[] { "この札をどうぞ。写しではなく、本物ですよ。" },
                        new[] { "この札は書庫で保管します。閲覧はいつでもどうぞ。" })),
                new Opponent("library-twins", "ユンとシア", "双子の司書", "library", false, 25, "Normal", "Plus を狙う",
                    CampaignAi.LooseNormal(180).WithPersonality(CampaignPersonalities.PlusHunter),
                    new[] { "dustwisp", "emberkit", "pyxmoth", "mirelamp", "cinderdrake", "clockwyrm", "gloamveil" }, 3,
                    new Dialogue(
                        new[] { "ユンと！", "シアです！ 足して同じ数になったら、ふたりでまとめてひっくり返すよ！" },
                        new[] { "足し算は合ってたのに……", "ねえユン、どこで間違えたんだろう？" },
                        new[] { "ぴったり同じ数！ わたしたちの勝ち！", "ね、シア！" },
                        new[] { "はい、どうぞ。ふたりで選んだカードだよ。", "大事にしてね！" },
                        new[] { "このカード、書庫で預かってもいい？", "取り返しに来てね！" })),
                new Opponent("library-curator", "セレスト", "館長", "library", true, 25 + BossCapBonus, "Normal+", "連鎖重視",
                    CampaignAi.NormalPlus.WithNoise(50, 0).WithPersonality(CampaignPersonalities.ChainSeeker),
                    new[] { "frostnewt", "bolthare", "gearowl", "stonetoad", "hollowshade", "tidewraith", "aquilaburst" }, 3,
                    new Dialogue(
                        new[] { "書庫の本は、一冊が次の一冊を呼ぶように並んでいます。", "一枚の裏返しが次を呼ぶ。その連なりをお見せしましょう。" },
                        new[] { "見事です。連なりを断たれたのは久しぶりですね。" },
                        new[] { "一手が次の一手を呼ぶ。美しかったでしょう？" },
                        new[] { "蔵書から一枚、お貸しします。返却は不要です。" },
                        new[] { "この札は書庫に収めます。目録にも載せておきますね。" },
                        new[] { "鏡の書庫の扉は開かれました。", "霧の観測所では、相手の札の顔さえ見えませんよ。" })),
            }),
            new Region("observatory", 3, "霧の観測所", new RuleSet(open: false, same: true, plus: true, combo: true, elemental: true), 35, new[]
            {
                new Opponent("observatory-forecaster", "ノア", "気象官", "observatory", false, 35, "Normal+", "属性マスを重視",
                    CampaignAi.NormalPlus.WithPersonality(CampaignPersonalities.ElementReader),
                    new[] { "orreryx", "gloamveil", "corvusarc", "rimeloom", "aquilaburst", "auroraveil", "leomane" }, 3,
                    new Dialogue(
                        new[] { "本日の盤面予報。ところにより炎、のち氷。", "空模様に合った札を置く。それがいちばん確かな手です。" },
                        new[] { "予報が外れました。記録しておきます。" },
                        new[] { "予報どおりです。風向きが味方しました。" },
                        new[] { "観測の記念にどうぞ。湿気には気をつけて。" },
                        new[] { "この札は観測資料として預かります。" })),
                new Opponent("observatory-seer", "ヴェイル", "霧の占い師", "observatory", false, 35, "Hard-", "読みにくい（揺らぎ大）",
                    CampaignAi.HardMinus.WithPersonality(CampaignPersonalities.Fickle),
                    new[] { "marshwyvern", "lupusfang", "emberscale", "corvusarc", "ironcetus", "lanternmare", "stormgear", "nocturnis" }, 5,
                    new Dialogue(
                        new[] { "ふふ……次の一手は、わたしにも見えないの。", "霧の中では、占いも気まぐれよ。" },
                        new[] { "あら。今日の霧は、あなたに味方したみたい。" },
                        new[] { "見えないものほど、怖いでしょう？" },
                        new[] { "霧のかけらをひとつ、あげる。" },
                        new[] { "その札、霧の向こうへ連れていくわね。" })),
                new Opponent("observatory-director", "アストラ", "観測所長", "observatory", true, 35 + BossCapBonus, "Hard", "冷静",
                    CampaignAi.Hard,
                    new[] { "serpenscoil", "rimeloom", "leomane", "draconyx", "cygnwraith", "taurocrag", "gravelith", "astrolabion" }, 3,
                    new Dialogue(
                        new[] { "観測所長のアストラです。感情は観測の邪魔になります。", "最善の一手だけを、静かに置きましょう。" },
                        new[] { "……想定外です。あなたの手を記録し直します。" },
                        new[] { "観測どおりの結果です。" },
                        new[] { "規定により、一枚お渡しします。" },
                        new[] { "規定により、一枚お預かりします。" },
                        new[] { "霧は晴れました。", "星界の門の先は、星が回り、決着がつくまで終わらない場所です。" })),
            }),
            new Region("gate", 4, "星界の門",
                new RuleSet(open: false, same: true, sameWall: true, plus: true, combo: true, suddenDeath: true, pivot: true), 45, new[]
            {
                new Opponent("gate-traveler", "テオ", "回転の旅人", "gate", false, 45, "Hard-", "Pivot を早めに切る",
                    CampaignAi.HardMinus.WithPersonality(CampaignPersonalities.EarlyPivot),
                    new[] { "forgecolossus", "auroraveil", "voltwyrm", "nocturnis", "orionmane", "cassiveil", "pegasion", "ophiwyrm" }, 5,
                    new Dialogue(
                        new[] { "やあ、旅人のテオだ。札は回してなんぼさ！", "使える手は早めに使う。旅も勝負も、出し惜しみは損だよ。" },
                        new[] { "回しすぎたかな？ まあ、それも旅の味さ。" },
                        new[] { "くるっと回して、ほら逆転！" },
                        new[] { "旅の土産だ。持っていきな。" },
                        new[] { "この札、次の旅に連れていくよ。" })),
                new Opponent("gate-knight", "ローレル", "星図の騎士", "gate", false, 45, "Hard", "守り型",
                    CampaignAi.HardLight.WithPersonality(CampaignPersonalities.Guardian),
                    new[] { "stormgear", "taurocrag", "gravelith", "scorpidrake", "polarion", "astrolabion", "ursamaw" }, 3,
                    new Dialogue(
                        new[] { "星図の騎士ローレル。この門を守るのが我が務め。", "隙は見せぬ。崩せるものなら崩してみよ。" },
                        new[] { "見事。我が守りを越えたこと、誇るがいい。" },
                        new[] { "守りを固めた者が、最後に立つ。" },
                        new[] { "受け取るがいい。騎士の誓いの証だ。" },
                        new[] { "この札は門の守りに加えさせてもらう。" })),
                new Opponent("gate-elder", "イオ", "星詠みの長", "gate", true, 45 + BossCapBonus, "Hard", "最強",
                    CampaignAi.Hard,
                    new[] { "cygnwraith", "voltwyrm", "orionmane", "scorpidrake", "polarion", "andromede", "sextantor" }, 1,
                    new Dialogue(
                        new[] { "よく来ましたね。星はあなたの訪れを告げていました。", "ここまでの旅で育てた札を、すべて見せてください。" },
                        new[] { "星の読みを越えましたか……", "あなたの札は、もう星図そのものです。" },
                        new[] { "まだ星は満ちていません。また来なさい。" },
                        new[] { "星詠みの札を一枚、あなたに。" },
                        new[] { "この札は星に返しましょう。取り戻したければ、また挑みなさい。" },
                        new[] { "星図はすべて巡られました。", "これからは、あなたが星を詠む番です。" })),
            }),
        });

        /// <summary>全相手（星図の順）。</summary>
        public static readonly ReadOnlyCollection<Opponent> Opponents = BuildOpponents();

        static ReadOnlyCollection<Opponent> BuildOpponents()
        {
            var list = new List<Opponent>();
            foreach (var r in Regions) list.AddRange(r.Opponents);
            return new ReadOnlyCollection<Opponent>(list);
        }

        public static Region RegionById(string id)
        {
            foreach (var r in Regions)
            {
                if (r.Id == id) return r;
            }
            return null;
        }

        public static Opponent OpponentById(string id)
        {
            foreach (var o in Opponents)
            {
                if (o.Id == id) return o;
            }
            return null;
        }

        public static Region RegionOf(Opponent opponent) => opponent == null ? null : RegionById(opponent.RegionId);

        /// <summary>number の次の星域（なければ null）。</summary>
        public static Region NextRegion(Region region)
        {
            if (region == null) return null;
            foreach (var r in Regions)
            {
                if (r.Number == region.Number + 1) return r;
            }
            return null;
        }
    }
}
