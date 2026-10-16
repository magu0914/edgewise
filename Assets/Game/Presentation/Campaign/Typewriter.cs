namespace Edgewise.Game.Campaign
{
    /// <summary>
    /// 会話の本文を 1 文字ずつ出す（表示に依存しない）。Advance で時間を進め、Visible 文字まで見せる。
    /// タップで全文（Complete）→ もう一度タップで次の行、の判断にも使う。
    /// </summary>
    public sealed class Typewriter
    {
        float _progress;

        /// <summary>1 秒あたりの文字数。</summary>
        public float CharsPerSecond { get; set; } = 36f;

        public string Text { get; private set; } = "";
        public int Length => Text.Length;
        public int Visible => _progress >= Length ? Length : (int)_progress;
        public bool IsComplete => Visible >= Length;

        public void Reset(string text)
        {
            Text = text ?? "";
            _progress = 0f;
        }

        /// <summary>dt 秒進める。timeFactor が 0（演出「即時」）なら全文を出す。増えた文字数を返す。</summary>
        public int Advance(float dt, float timeFactor = 1f)
        {
            int before = Visible;
            if (timeFactor <= 0f || CharsPerSecond <= 0f) _progress = Length;
            else _progress += dt * CharsPerSecond / timeFactor;
            if (_progress > Length) _progress = Length;
            return Visible - before;
        }

        public void Complete() => _progress = Length;
    }
}
