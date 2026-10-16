namespace Edgewise.Game.UI
{
    /// <summary>
    /// 画面に出す文字列をすべてここに集める。
    /// フォントアセットは「ここにある文字＋ASCII＋カード名」に絞って生成する（Editor の FontBuilder がこのクラスを読む）。
    /// 文字列を足したら ProjectSetup を実行し直すこと。
    /// </summary>
    public static class UiText
    {
        public const string Logo = "EDGEWISE";
        public const string Subtitle = "星図カードバトル（仮題）";
        public const string Start = "はじめる";
        public const string Homage = "本作は Triple Triad（© SQUARE ENIX）へのオマージュ作品であり、公式とは無関係です。";
        public const string TitleHint = "マウス・タッチ・キーボード（矢印・Enter・Esc）で遊べます";

        public const string SetupTitle = "対戦設定";
        public const string DifficultyLabel = "CPU の強さ";
        public const string BandLabel = "カードの強さ";
        public const string RulesLabel = "ルール";
        public const string Easy = "Easy";
        public const string Normal = "Normal";
        public const string Hard = "Hard";
        public const string BandBeginner = "初級 Lv1〜3";
        public const string BandIntermediate = "中級 Lv3〜6";
        public const string BandAdvanced = "上級 Lv6〜10";
        public const string BeginMatch = "対戦開始";
        public const string Back = "戻る";
        public const string On = "ON";
        public const string Off = "OFF";

        public const string RuleOpen = "Open";
        public const string RuleOpenDesc = "相手の手札が見える";
        public const string RuleRandom = "Random";
        public const string RuleRandomDesc = "手札をデッキから無作為に 5 枚選ぶ";
        public const string RuleSame = "Same";
        public const string RuleSameDesc = "2 辺以上が隣と同じ数なら、まとめて裏返す";
        public const string RuleSameWall = "Same Wall";
        public const string RuleSameWallDesc = "Same で盤の外の壁を A として数える";
        public const string RulePlus = "Plus";
        public const string RulePlusDesc = "2 辺以上で隣との和が等しければ裏返す";
        public const string RuleCombo = "Combo";
        public const string RuleComboDesc = "Same / Plus で取ったカードから連鎖する";
        public const string RuleElemental = "Elemental";
        public const string RuleElementalDesc = "属性マスで一致 +1、不一致 −1";
        public const string RuleSuddenDeath = "Sudden Death";
        public const string RuleSuddenDeathDesc = "引き分けなら持ちカードで再戦（5 回まで）";
        public const string RulePivot = "Pivot";
        // トークン数は GameConstants.MatchPivotTokens（先攻 1・後攻 2、Sudden Death ON は 1・1）。変えたらここも直す（テストで照合）
        public const string RulePivotDesc = "カードを 90° 回して置ける（先攻 1 回・後攻 2 回）";
        public const string RulePivotDescSuddenDeath = "カードを 90° 回して置ける（Sudden Death ON は 1 回ずつ）";

        /// <summary>Pivot の説明（Sudden Death の ON/OFF でトークン数が変わる）。</summary>
        public static string PivotDesc(bool suddenDeath) => suddenDeath ? RulePivotDescSuddenDeath : RulePivotDesc;

        public const string You = "あなた";
        public const string Cpu = "CPU";
        public const string YourTurn = "あなたの番";
        public const string CpuTurn = "CPU の番";
        public const string CpuThinking = "CPU 思考中…";
        public const string YouFirst = "あなたの先攻";
        public const string CpuFirst = "CPU の先攻";
        public const string MatchOver = "対戦終了";
        public const string Hint = "ヒント";
        public const string HintThinking = "ヒントを考え中…";
        public const string HintForecast = "予想";
        public const string HintExact = "（読み切り）";
        public const string HintApprox = "（目安）";
        public const string HintRotate = "回して置く";
        public const string MenuSettings = "設定";
        public const string SettingsTitle = "設定";
        public const string SettingsSpeed = "演出の速度";
        public const string SettingsMaster = "全体の音量";
        public const string SettingsBgm = "BGM";
        public const string SettingsSfx = "効果音";
        public const string SettingsMuteAll = "ミュート";
        public const string SettingsMuteAllNote = "ON ですべての音を止める";
        public const string SettingsBackgroundMute = "裏では消音";
        public const string SettingsBackgroundMuteNote = "タブが裏に回ったら音を止める";
        public const string SettingsShake = "画面の揺れ";
        public const string SettingsShakeNote = "動きを減らしたいときは OFF";
        public const string Percent = "%";
        public const string PivotTokens = "回転";
        public const string RotateLeft = "◀ 回転";
        public const string RotateRight = "回転 ▶";
        public const string Menu = "メニュー";

        public const string MenuTitle = "メニュー";
        public const string MenuRules = "ルール確認";
        public const string MenuSpeed = "速度";
        public const string SpeedNormal = "×1";
        public const string SpeedDouble = "×2";
        public const string SpeedInstant = "即時";
        public const string MenuMute = "ミュート";
        public const string MenuResign = "降参する";
        public const string MenuClose = "閉じる";
        public const string RulesInUse = "このルールで対戦中";
        public const string NoRules = "基本ルールのみ";
        public const string BasicRule = "置いたカードの辺が隣の相手カードの辺より大きければ裏返す。盤上と手札の枚数が多い方の勝ち。";

        public const string BannerSame = "SAME!";
        public const string BannerPlus = "PLUS!";
        public const string BannerSameWall = "SAME WALL!";
        public const string BannerCombo = "COMBO!";
        public const string BannerComboTimes = "COMBO ×";
        public const string BannerSuddenDeath = "SUDDEN DEATH";
        public const string BadgeSame = "SAME";
        public const string BadgePlus = "PLUS";

        public const string Win = "WIN";
        public const string Lose = "LOSE";
        public const string Draw = "DRAW";
        public const string Resigned = "降参しました";
        public const string Again = "もう一度";
        public const string ChangeSettings = "設定を変える";

        // ---- タイトルのボタン（4b） ----
        public const string TitleDex = "カード図鑑";
        public const string TitleCredits = "クレジット";

        // ---- カード図鑑（DESIGN §7-1 見せ場） ----
        public const string DexTitle = "カード図鑑";
        public const string DexCountPrefix = "全 ";
        public const string DexCountSuffix = " 枚";
        public const string DexHintList = "クリック・タップで拡大　矢印で選んで Enter　Esc で戻る";
        public const string DexHintListTouch = "タップで拡大　上下にドラッグで移動";
        public const string DexHintZoom = "カードはカーソルの方へ傾きます　← → で前後のカード　Esc で一覧へ";
        public const string DexPrev = "◀ 前";
        public const string DexNext = "次 ▶";
        public const string DexBackToList = "一覧へ";
        public const string DexShowAll = "すべて見る";
        public const string DexShowCollection = "収集状況";
        public const string DexUnknownName = "？？？";
        public const string DexNotOwned = "まだ持っていないカード（星図巡りで手に入る）";
        public const string DexOwned = "持っている枚数 ";
        public const string DexElement = "属性";
        public const string DexTribe = "系統";
        public const string DexNone = "なし";
        public const string DexIcon = "アイコン";
        public const string DexLevel = "Lv";
        public const string TribeBeastName = "獣";
        public const string TribeMachineName = "機械";
        public const string TribeSpiritName = "霊";
        public const string TribeDragonName = "竜";
        public const string ElementFireName = "火";
        public const string ElementIceName = "氷";
        public const string ElementThunderName = "雷";
        public const string ElementEarthName = "土";
        public const string ElementWaterName = "水";
        public const string ElementWindName = "風";
        public const string ElementHolyName = "聖";
        public const string ElementDarkName = "闇";

        // ---- クレジット画面（docs/CREDITS.md の要約） ----
        public const string CreditsTitle = "クレジット";
        public const string CreditsFontHeading = "フォント";
        public const string CreditsFontBody = "Noto Sans JP（Copyright 2014-2021 Adobe）\nSIL Open Font License, Version 1.1";
        public const string CreditsIconsHeading = "カード絵のアイコン";
        public const string CreditsIconsBody = "カード絵と対戦相手の立ち絵の土台に game-icons.net のアイコンを使い、SDF に変換して色・光・星座を加えています（改変あり）。\n"
            + "ライセンス: Creative Commons Attribution 3.0（CC BY 3.0）\n"
            + "Icons made by Lorc, Delapouite, Caro Asercion, Skoll and Cathelineau, available on https://game-icons.net";
        public const string CreditsCardHeader = "カード — アイコン（作者）";
        public const string CreditsPortraitHeader = "立ち絵 — アイコン（作者）";
        public const string CreditsBgmHeading = "BGM（OpenTracks）";
        public const string CreditsBgmBody = "タイトル「星空」Heitaro Ashibe\n対戦「ギザなビート」マニーラ\n勝利「明日へのファンファーレ」のる\n敗北「Too late...」t.tam\n"
            + "https://opentracks.com/ のフリー素材を音源利用ライセンスに従って使用しています。素敵な楽曲をありがとうございます。";
        public const string CreditsSfxHeading = "効果音";
        public const string CreditsSfxBody = "効果音はすべてプログラムで合成した音です。";
        public const string CreditsHomageHeading = "オマージュの表記";
        public const string CreditsHomageBody = "本作は Triple Triad（© SQUARE ENIX）へのオマージュ作品であり、公式とは無関係です。ルールの仕組みのみを参考にし、カード名・キャラクター・絵・音・ロゴ・名称は使っていません。";

        // ---- 星図巡り（Phase 5b-1。DESIGN §12-11） ----
        public const string TitleCampaign = "星図巡り";
        public const string TitleFree = "フリー対戦";
        public const string ProgressRegion = "星域 ";
        public const string ProgressCollect = "収集 ";
        public const string ProgressSep = "・";
        public const string NewJourney = "はじめから";

        public const string IntroLine1 = "星図を巡り、星の名を持つカードを集める旅へ。";
        public const string IntroLine2 = "各地の札使いに勝つと、相手のカードを 1 枚もらえます。";
        public const string IntroLine3 = "まずは、はじめの 7 枚を受け取ろう。";
        public const string IntroTap = "カードをタップしてめくろう";
        public const string IntroRevealAll = "まとめてめくる";
        public const string IntroReceived = "7 枚を受け取った！";
        public const string IntroToMap = "星図へ";

        public const string MapTitle = "星図";
        public const string MapHint = "星域を選んでください";
        public const string MapLocked = "未解放";
        public const string MapToTitle = "タイトルへ";
        public const string MapDex = "図鑑";
        public const string MapUnlocked = "新しい星域が開いた！";

        public const string RegionRules = "この星域のルール";
        public const string RegionCap = "デッキの上限  合計 Lv ";
        public const string RegionBossCap = "主のデッキは +5";
        public const string RegionBossNote = "主との対戦は決着がつくまで（Sudden Death）";
        public const string RegionChallenge = "挑む";
        public const string RegionCards = "持っているカード";
        public const string RegionBoss = "主";
        public const string RegionBack = "星図へ";
        public const string RecordWin = "勝";
        public const string RecordLoss = "敗";
        public const string RecordDraw = "分";
        public const string RuleBasicName = "基本";
        public const string RuleBasicDesc = "大きい数字で隣の相手カードを裏返す";
        public const string RuleOpenOffName = "Open OFF";
        public const string RuleOpenOffDesc = "相手の手札が伏せられる";
        public const string ChipOpen = "Open";
        public const string ChipClosed = "伏せ";
        public const string ChipSame = "Same";
        public const string ChipWall = "Wall";
        public const string ChipPlus = "Plus";
        public const string ChipCombo = "Combo";
        public const string ChipElemental = "Elem";
        public const string ChipSudden = "SD";
        public const string ChipPivot = "Pivot";
        public const string MarkOwned = "◆";
        public const string MarkCaptured = "取";
        public const string Unknown = "？";

        public const string DeckTitle = "デッキ編集";
        public const string DeckTotal = "合計 Lv ";
        public const string DeckSlash = " / ";
        public const string DeckAuto = "おまかせ";
        public const string DeckBegin = "この 5 枚で挑む";
        public const string DeckOver = "上限を超えています";
        public const string DeckNeedPrefix = "あと ";
        public const string DeckNeedSuffix = " 枚選んでください";
        public const string DeckReady = "準備OK";
        public const string DeckAll = "すべて";
        public const string DeckHint = "タップで入れる・外す";
        public const string DeckOpponent = "対戦相手";
        public const string DeckSeen = "見えている持ちカード";
        public const string DeckCount = "×";

        public const string DialogueSkip = "スキップ";
        public const string DialogueNext = "▼";

        public const string ResultNext = "次へ";
        public const string ResultPerfect = "完勝！";

        public const string RewardTitle = "カードを 1 枚選ぼう";
        public const string RewardTitlePerfect = "完勝！ カードを 2 枚選べる";
        public const string RewardSub = "相手が使った 5 枚から選べます";
        public const string RewardMore = "もう 1 枚選べます";
        public const string RewardNew = "NEW!";
        public const string RewardCopies = "×";
        public const string RewardHand = "手持ち";
        public const string RewardDone = "カードを手に入れた！";

        public const string TakeTitle = "カードを取られた…";
        public const string TakeSuffix = " を取られた";
        public const string TakeProtectedPrefix = "手持ちが少ないので、";
        public const string TakeProtectedSuffix = " は取られなかった";
        public const string TakeHint = "勝てば取り返せます";

        public const string ClearedSuffix = " を巡り終えた";
        public const string ClearedAll = "星図をすべて巡り終えた！";

        public const string SettingsTabBasic = "基本";
        public const string SettingsTabCampaign = "星図巡り";
        public const string SettingsClassic = "原作モード";
        public const string SettingsClassicNote = "負けると相手にカードを 1 枚取られる";
        public const string SettingsClassicDetail = "取られるのは使った 5 枚のうち一番強いカード。その相手に勝てば取り返せます。手持ちが 5 種類を切るときは取られません。";
        public const string SettingsErase = "データを消す";
        public const string SettingsEraseNote = "星図巡りの進み具合と手持ちを消す";
        public const string SettingsEraseButton = "消す";
        public const string EraseConfirm1 = "星図巡りのデータを消しますか？";
        public const string EraseConfirm2 = "本当に消しますか？ 元に戻せません。";
        public const string EraseYes = "消す";
        public const string EraseNo = "やめる";
        public const string EraseDone = "データを消しました";
        public const string NoSaveYet = "まだデータはありません";

        public const string SaveTooNew = "新しい版のセーブがあるため、この進み具合は保存されません";
        public const string SaveCorrupt = "セーブが読めなかったので、はじめから始めます（元のデータは退避しました）";

        /// <summary>フォントに必ず入れる追加の記号。</summary>
        public const string ExtraGlyphs = "◆●◇○×★☆・…「」（）〜ー－−＋！？：　0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            + "火氷雷土水風聖闇獣機霊竜—▶◀▼";
    }
}
