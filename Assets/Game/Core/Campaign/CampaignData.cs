// 星図巡りのデータ（DESIGN §12-2 / §12-3 / §12-5 / §12-7）。
// 名前・肩書きは仮。台詞は 5c で推敲（§12-7-1）。持ちカードと AI の強さ・性格は Phase 5a の自己対戦で調整した値（docs/BALANCE.md）。
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

        // 台詞（5c。DESIGN §12-7-1）: Dialogue(登場・初回, 登場・再戦, プレイヤーが勝った, プレイヤーが負けた, カードを渡す, カードを取る[, 主のみ: 星域を抜けた])。
        // 初回の登場では、担当のルールに 1 行だけ触れる（星域の新しいルールの要点が台詞だけでわかるように）。
        public static readonly ReadOnlyCollection<Region> Regions = new ReadOnlyCollection<Region>(new[]
        {
            new Region("harbor", 1, "灯の港", new RuleSet(open: true), 15, new[]
            {
                // ミラ: 元気で丁寧、少しドジ。担当は基本（大きい数字で隣を裏返す）と「迷ったらヒント」
                new Opponent("harbor-apprentice", "ミラ", "見習い航海士", "harbor", false, 15, "Easy", "素直（取れる枚数だけ見る）",
                    CampaignAi.Easy.WithPersonality(CampaignPersonalities.Greedy),
                    new[] { "bolthare", "frostnewt", "rivetcrab", "pyxmoth", "mirelamp", "gearowl", "lynxlight" }, 1,
                    new Dialogue(
                        new[] { "はじめまして！ 見習い航海士のミラです。", "大きい数字で隣を裏返す。迷ったらヒント。……メモより！", "あっ、メモ逆さまでした。でも合ってます！" },
                        new[] { "また来てくれたんですね！ 今日のわたしは一味違います。", "……昨日より、半歩くらいですけど！" },
                        new[] { "あれ？ ちゃんと数えてたのに、盤がそっちの色です……", "航海日誌に「完敗」って書いておきます。" },
                        new[] { "やった！ 取れるだけ取ったら、勝っちゃいました！", "ヒント、押してもよかったんですよ？" },
                        new[] { "約束です。好きな札をどうぞ！", "あっ、それお気に入り……いえ、どうぞどうぞ！" },
                        new[] { "ごめんなさい、決まりなので……この札、預かりますね。", "ちゃんと磨いておきますから！" })),
                // オルド: 寡黙な老人。担当は角と端（守りの考え方）
                new Opponent("harbor-keeper", "オルド", "灯台守", "harbor", false, 15, "Easy+", "角と端を好む守り型",
                    CampaignAi.EasyPlus.WithPersonality(CampaignPersonalities.CornerGuard),
                    new[] { "glimmouse", "dustwisp", "emberkit", "stonetoad", "hollowshade", "cinderdrake", "velahound" }, 1,
                    new Dialogue(
                        new[] { "……灯台守のオルドだ。", "角は二方、端は三方からしか攻められん。", "灯台が岬の端に立つのも、そういうわけだ。" },
                        new[] { "また来たか。……茶ぐらいは出そう。", "隅は、今日もわしがもらうぞ。" },
                        new[] { "……隅を固めても、波は真ん中から来るか。", "いい腕だ。灯りの番より、よほど目が覚めた。" },
                        new[] { "守りは退屈に見えて、最後に効く。", "急がんことだ。夜は長い。" },
                        new[] { "持っていけ。灯りの番の合間に磨いた札だ。" },
                        new[] { "この札は灯台で預かる。取り返しに来い。", "……灯りは、つけておく。" })),
                // マーロウ（主）: 豪快で面倒見のよい親方。担当は Sudden Death（主とは決着がつくまで）
                new Opponent("harbor-master", "マーロウ", "港の親方", "harbor", true, 15 + BossCapBonus, "Normal", "攻め気（強い辺で押す）",
                    CampaignAi.LooseNormal(70).WithPersonality(CampaignPersonalities.Aggressive),
                    new[] { "glimmouse", "dustwisp", "scalefry", "emberkit", "bolthare", "hollowshade", "voltsparrow" }, 3,
                    new Dialogue(
                        new[] { "おう、港の親方マーロウだ！ よく来たな！", "引き分けなら、自分の色の札でもう一勝負。決着がつくまでだ！", "飯は食ってきたか？ 長い勝負になるぞ！" },
                        new[] { "また来たな！ いい顔になってきたじゃないか！", "今日も決着がつくまでだ。覚悟しな！" },
                        new[] { "ははっ、押し負けた！ いい腕してるじゃないか！", "港じゅうに言いふらしてやるからな！" },
                        new[] { "押して押して押し切る！ これが港の流儀だ！", "腹ごしらえして、また来な！" },
                        new[] { "勝った者には報酬だ。好きなのを持っていきな！" },
                        new[] { "負けは負けだ。こいつは預かっとく。", "取り返しに来いよ。待ってるからな！" },
                        new[] { "この港じゃもう、教えることはないな！", "次は鏡の書庫だ。あそこの連中は数字にうるさいぞ！", "達者でな！ 風邪ひくなよ！" })),
            }),
            new Region("library", 2, "鏡の書庫", new RuleSet(open: true, same: true, sameWall: true, plus: true), 25, new[]
            {
                // リーフ: 几帳面、写本の比喩。担当は Same（2 辺以上が同じ数）と Same Wall（盤の外の壁は A）
                new Opponent("library-scribe", "リーフ", "写本係", "library", false, 25, "Normal", "Same を狙う",
                    CampaignAi.LooseNormal(180).WithPersonality(CampaignPersonalities.SameHunter),
                    new[] { "glimmouse", "coglet", "scalefry", "frostnewt", "rivetcrab", "gearowl", "orreryx" }, 3,
                    new Dialogue(
                        new[] { "写本係のリーフです。写し間違いは一字も許しません。", "隣と同じ数の辺が二つ以上で裏返る。盤の外の壁は A です。", "余白まで写してこその写本ですから。" },
                        new[] { "再読ですね。前回の対局は清書して保管してあります。", "今回は、注釈つきでお相手します。" },
                        new[] { "写し間違い……いえ、読み違いでした。", "この対局は、正本として残しておきます。" },
                        new[] { "ほら、同じ数がふたつ。きれいに揃いました。", "数字は、写すより読むほうが難しいのです。" },
                        new[] { "この札をどうぞ。写しではなく、原本ですよ。" },
                        new[] { "この札は書庫で保管します。閲覧はいつでもどうぞ。", "貸し出しには、勝利が必要ですけれど。" })),
                // ユンとシア: 交互に話す、明るい掛け合い。担当は Plus（2 辺以上の和が同じ）
                new Opponent("library-twins", "ユンとシア", "双子の司書", "library", false, 25, "Normal", "Plus を狙う",
                    CampaignAi.LooseNormal(180).WithPersonality(CampaignPersonalities.PlusHunter),
                    new[] { "dustwisp", "emberkit", "pyxmoth", "mirelamp", "cinderdrake", "clockwyrm", "gloamveil" }, 3,
                    new Dialogue(
                        new[] { "ユン「双子の司書、ユンと！」シア「シアです！」", "ユン「辺どうしの和が、二か所以上で同じなら裏返る！」", "シア「……つまり足し算だよ。ユン、説明が硬い！」" },
                        new[] { "ユン「また来た！」シア「今日はわたしが計算係！」", "ユン「えー、昨日もシアだったよ？」" },
                        new[] { "ユン「足し算は合ってたのに……」", "シア「ユンが繰り上がりを忘れたんだよ！」", "ユン「繰り上がりなんてないよ！」" },
                        new[] { "シア「ぴったり同じ数！ わたしたちの勝ち！」", "ユン「ね、計算って楽しいでしょ？」" },
                        new[] { "ユン「はい、ふたりで選んだ札だよ！」", "シア「選んだのは、ほとんどわたしだけどね。」" },
                        new[] { "シア「この札、書庫で預かってもいい？」", "ユン「延滞料はとらないから、取り返しに来てね！」" })),
                // セレスト（主）: 落ち着いた丁寧語、本の比喩。担当は Same と Plus で一度に多く裏返す（鏡の書庫に Combo はないので「連鎖」とは言わない）
                new Opponent("library-curator", "セレスト", "館長", "library", true, 25 + BossCapBonus, "Normal+", "Same・Plus で一度に多く取る",
                    CampaignAi.NormalPlus.WithNoise(50, 0).WithPersonality(CampaignPersonalities.ChainSeeker),
                    new[] { "frostnewt", "bolthare", "gearowl", "stonetoad", "hollowshade", "tidewraith", "aquilaburst" }, 3,
                    new Dialogue(
                        new[] { "館長のセレストです。ようこそ、鏡の書庫へ。", "Same や Plus がきまれば、何枚も一度に裏返ります。", "棚の一段を、まるごと入れ替えるように。" },
                        new[] { "おかえりなさい。しおりは挟んだままにしてあります。", "続きのページから始めましょう。" },
                        new[] { "見事です。この一局、蔵書に加えさせてください。", "久しぶりに、結末の読めない本でした。" },
                        new[] { "一手で棚が入れ替わる。美しかったでしょう？", "次は、あなたの一冊を読ませてください。" },
                        new[] { "蔵書から一枚、お貸しします。返却は不要です。" },
                        new[] { "この札は書庫に収めます。目録にも載せておきますね。" },
                        new[] { "鏡の書庫の扉は開かれました。", "次は霧の観測所。相手の手札の顔さえ、見えませんよ。" })),
            }),
            new Region("observatory", 3, "霧の観測所", new RuleSet(open: false, same: true, plus: true, combo: true, elemental: true), 35, new[]
            {
                // ノア: 天気予報の口調、淡々として少しユーモア。担当は属性マス（合えば +1、合わなければ −1）
                new Opponent("observatory-forecaster", "ノア", "気象官", "observatory", false, 35, "Normal+", "属性マスを重視",
                    CampaignAi.NormalPlus.WithPersonality(CampaignPersonalities.ElementReader),
                    new[] { "orreryx", "gloamveil", "corvusarc", "rimeloom", "aquilaburst", "auroraveil", "leomane" }, 3,
                    new Dialogue(
                        new[] { "気象官のノアです。本日の盤面予報をお伝えします。", "所により属性マス。合えば各辺 +1、合わなければ −1 でしょう。", "なお、降水確率は勝負に関係ありません。" },
                        new[] { "前回の観測から、気圧の変化はありません。", "本日も、所により逆転があるでしょう。" },
                        new[] { "予報が外れました。訂正してお詫びします。", "明日は、晴れのちリベンジでしょう。" },
                        new[] { "予報どおりです。風向きが味方しました。", "次回は、属性マスの雲行きにご注意ください。" },
                        new[] { "観測の記念にどうぞ。湿気には気をつけて。" },
                        new[] { "この札は観測資料としてお預かりします。", "返却予定日は、未定です。" })),
                // ヴェイル: 気だるく謎めく。担当は Open OFF（相手の手札が伏せられている）
                new Opponent("observatory-seer", "ヴェイル", "霧の占い師", "observatory", false, 35, "Hard-", "読みにくい（揺らぎ大）",
                    CampaignAi.HardMinus.WithPersonality(CampaignPersonalities.Fickle),
                    new[] { "marshwyvern", "lupusfang", "emberscale", "corvusarc", "ironcetus", "lanternmare", "stormgear", "nocturnis" }, 5,
                    new Dialogue(
                        new[] { "霧の占い師、ヴェイル。……ふわぁ、よく来たわね。", "わたしの手札は霧の中。あなたからは見えないわ。", "……わたしにも、ときどき見えないけれど。" },
                        new[] { "また来たのね。……ええ、来ると思っていたわ。", "嘘よ。占いなんて、たいてい外れるもの。" },
                        new[] { "あら。今日の霧は、あなたに味方したみたい。" },
                        new[] { "見えないものほど、怖いでしょう？", "……わたしも、ちょっと怖かったわ。" },
                        new[] { "霧のかけらをひとつ、あげるわ。" },
                        new[] { "その札、霧の向こうへ連れていくわね。", "……迎えに来るなら、灯りを持ってきて。" })),
                // アストラ（主）: 感情を抑えた報告口調。担当は Combo（Same / Plus で取ったカードがさらに隣を裏返す）
                new Opponent("observatory-director", "アストラ", "観測所長", "observatory", true, 35 + BossCapBonus, "Hard", "冷静",
                    CampaignAi.Hard,
                    new[] { "serpenscoil", "rimeloom", "leomane", "draconyx", "cygnwraith", "taurocrag", "gravelith", "astrolabion" }, 3,
                    new Dialogue(
                        new[] { "観測所長のアストラです。対局の記録を開始します。", "Same・Plus で取った札は、さらに隣を裏返す。Combo です。", "感情は観測の誤差になります。以上。" },
                        new[] { "再観測を開始します。前回との差分に注目します。" },
                        new[] { "……想定外です。記録を修正します。", "訂正。あなたは、想定以上でした。" },
                        new[] { "観測どおりの結果です。", "連鎖の起点を、もう少し観察してください。" },
                        new[] { "規定により、一枚お渡しします。", "……個人的にも、そうしたいと思いました。" },
                        new[] { "規定により、一枚お預かりします。" },
                        new[] { "報告。霧は晴れました。", "星界の門では札が回り、決着がつくまで終わりません。", "……健闘を祈ります。これは記録外です。" })),
            }),
            new Region("gate", 4, "星界の門",
                new RuleSet(open: false, same: true, sameWall: true, plus: true, combo: true, suddenDeath: true, pivot: true), 45, new[]
            {
                // テオ: 気さくな旅人、軽口。担当は Pivot（置くときに回せる。回数に限りがある）
                new Opponent("gate-traveler", "テオ", "回転の旅人", "gate", false, 45, "Hard-", "Pivot を早めに切る",
                    CampaignAi.HardMinus.WithPersonality(CampaignPersonalities.EarlyPivot),
                    new[] { "forgecolossus", "auroraveil", "voltwyrm", "nocturnis", "orionmane", "cassiveil", "pegasion", "ophiwyrm" }, 5,
                    new Dialogue(
                        new[] { "やあ、旅人のテオだ。君も星を巡ってるのかい？", "置くときに札をくるっと回せる。ただし回数には限りがあるよ。", "使える手は早めに使う。旅も勝負も、出し惜しみは損さ。" },
                        new[] { "おっ、また会ったね。旅の縁ってやつかな。", "今日も早めに回すよ。先に言っとく！" },
                        new[] { "回しすぎたかな？ まあ、それも旅の味さ。" },
                        new[] { "くるっと回して、ほら逆転！", "回せるうちに回さなきゃ、もったいないよ？" },
                        new[] { "旅の土産だ。持っていきなよ。" },
                        new[] { "この札、次の旅に連れていくよ。", "絵はがきは送るからさ。" })),
                // ローレル: 騎士の古風な言い回し。担当は守り
                new Opponent("gate-knight", "ローレル", "星図の騎士", "gate", false, 45, "Hard", "守り型",
                    CampaignAi.HardLight.WithPersonality(CampaignPersonalities.Guardian),
                    new[] { "stormgear", "taurocrag", "gravelith", "scorpidrake", "polarion", "astrolabion", "ursamaw" }, 3,
                    new Dialogue(
                        new[] { "星図の騎士ローレル。この門を守るが我が務め。", "弱き辺は壁に向けよ。隙を見せねば、崩れはせぬ。", "いざ、参られよ。" },
                        new[] { "再び来たか。その意気や良し。", "我が守り、前より固いと知れ。" },
                        new[] { "見事。我が守りを越えたこと、誇るがよい。" },
                        new[] { "守りを固めた者が、最後に立つ。", "焦るでない。隙は、待てば生まれる。" },
                        new[] { "受け取るがよい。騎士の誓いの証だ。" },
                        new[] { "この札、門の守りに加えさせてもらう。", "取り戻したくば、剣ならぬ札で来い。" })),
                // イオ（主）: 穏やかで威厳がある。旅の総決算（ルールの説明はしない）
                new Opponent("gate-elder", "イオ", "星詠みの長", "gate", true, 45 + BossCapBonus, "Hard", "最強",
                    CampaignAi.Hard,
                    new[] { "cygnwraith", "voltwyrm", "orionmane", "scorpidrake", "polarion", "andromede", "sextantor" }, 1,
                    new Dialogue(
                        new[] { "よく来ましたね。星はあなたの訪れを告げていました。", "港の灯、書庫の鏡、観測所の霧。すべて覚えていますか。", "その旅で育てた札を、すべて見せなさい。" },
                        new[] { "また来たのですね。星は何度でも巡ります。", "さあ、座りなさい。" },
                        new[] { "星の読みを越えましたか……", "見事です。あなたの札は、もう星図そのものです。" },
                        new[] { "まだ星は満ちていません。また来なさい。", "旅の途中で拾った札を、もう一度見直しなさい。" },
                        new[] { "星詠みの札を一枚、あなたに託します。" },
                        new[] { "この札は星に返しましょう。取り戻したければ、また挑みなさい。" },
                        new[] { "星図は、これですべて巡られました。", "これからは、あなたが星を詠む番です。" })),
            }),
        });

        /// <summary>
        /// エンディングの文（DESIGN §12-7-1。3〜4 行）: イオに初めて勝ったとき、主の「星域を抜けた」台詞と帯の後に出し、クレジットを流す。
        /// 旅を振り返り、星図を巡り終えたことを告げる。
        /// </summary>
        public static readonly ReadOnlyCollection<string> Ending = new ReadOnlyCollection<string>(new[]
        {
            "灯の港で数字を比べ、鏡の書庫で数を揃えた。",
            "霧の観測所では見えない札を読み、星界の門で札を回した。",
            "七枚から始まった手札は、いま星図そのものになった。",
            "星図巡りは、ここでおしまい。けれど星は、また巡る。",
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
