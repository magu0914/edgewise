using Edgewise.Core;

namespace Edgewise.Game.Match
{
    /// <summary>
    /// 演出の「味付け」の差し込み口（Phase 3b: 音・画面の揺れ・ヒットストップ・光）。
    /// EventPlayer は見た目の基本の動きを再生し、節目ごとにここを呼ぶ。3a では何もしない実装（NullMatchFx）を使う。
    /// </summary>
    public interface IMatchFx
    {
        void OnPick();
        void OnCursorMove();
        void OnPlace(int cell);
        /// <summary>indexInMove: 1 手の中で何枚目の裏返しか（音程を半音ずつ上げる用）。chainDepth: 連鎖の段。</summary>
        void OnFlip(int cell, Player to, int indexInMove, int chainDepth);
        void OnRule(RuleHeading rule);
        void OnCombo(int depth);
        /// <summary>Guard が裏返しを防いだ（盾が光る）。</summary>
        void OnGuard(int cell);
        void OnEnd(GameResult resultForSeat0);
    }

    public sealed class NullMatchFx : IMatchFx
    {
        public static readonly NullMatchFx Instance = new NullMatchFx();
        public void OnPick() { }
        public void OnCursorMove() { }
        public void OnPlace(int cell) { }
        public void OnFlip(int cell, Player to, int indexInMove, int chainDepth) { }
        public void OnRule(RuleHeading rule) { }
        public void OnCombo(int depth) { }
        public void OnGuard(int cell) { }
        public void OnEnd(GameResult resultForSeat0) { }
    }
}
