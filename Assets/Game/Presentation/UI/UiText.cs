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
        public const string RulePivotDesc = "2 回だけ、カードを 90° 単位で回して置ける";

        public const string You = "あなた";
        public const string Cpu = "CPU";
        public const string YourTurn = "あなたの番";
        public const string CpuTurn = "CPU の番";
        public const string CpuThinking = "CPU 思考中…";
        public const string YouFirst = "あなたの先攻";
        public const string CpuFirst = "CPU の先攻";
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

        /// <summary>フォントに必ず入れる追加の記号。</summary>
        public const string ExtraGlyphs = "◆●◇○×★☆・…「」（）〜ー－−＋！？：　0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            + "火氷雷土水風聖闇獣機霊竜";
    }
}
