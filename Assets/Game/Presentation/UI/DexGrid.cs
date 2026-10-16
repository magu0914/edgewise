using System;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// カード図鑑の一覧の並びと選択・スクロール（表示から切り離した計算だけ。EditMode でテストする）。
    /// 並びは左上から右へ、行は下へ。index はカード定義の順（Lv 順）。
    /// </summary>
    public sealed class DexGrid
    {
        public int Count { get; }
        public int Columns { get; private set; }

        /// <summary>選んでいるカード（キーボードの選択・拡大表示中のカード）。</summary>
        public int Selected { get; private set; }

        public DexGrid(int count, int columns)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
            SetColumns(columns);
        }

        public int Rows => (Count + Columns - 1) / Columns;
        public int RowOf(int index) => index / Columns;
        public int ColumnOf(int index) => index % Columns;

        public void SetColumns(int columns)
        {
            Columns = Math.Max(1, columns);
        }

        public void Select(int index)
        {
            Selected = Math.Max(0, Math.Min(Count - 1, index));
        }

        /// <summary>
        /// 矢印キーで選択を動かす。左右は並びの順に 1 つずつ（行の端では前後の行へ。最初と最後で止まる）、
        /// 上下は 1 行ずつ（列はそのまま。最後の行が短くて列が無いときは最後のカード）。動いたら true。
        /// </summary>
        public bool Move(int dx, int dy)
        {
            int target = Selected;
            if (dx != 0) target = Math.Max(0, Math.Min(Count - 1, Selected + Math.Sign(dx)));
            if (dy != 0)
            {
                int row = RowOf(Selected) + Math.Sign(dy);
                if (row < 0 || row >= Rows) return false;
                target = Math.Min(Count - 1, row * Columns + ColumnOf(Selected));
            }
            if (target == Selected) return false;
            Selected = target;
            return true;
        }

        /// <summary>拡大表示で前後のカードへ（端では反対の端へ回る）。</summary>
        public int Step(int dir)
        {
            Selected = ((Selected + Math.Sign(dir)) % Count + Count) % Count;
            return Selected;
        }

        /// <summary>スクロール量を 0..（中身の高さ − 見える高さ）に収める。中身が収まるなら 0。</summary>
        public static float ClampScroll(float scroll, float contentHeight, float viewHeight)
        {
            float max = Math.Max(0f, contentHeight - viewHeight);
            return Math.Max(0f, Math.Min(max, scroll));
        }

        /// <summary>選択中の行が見えるようにスクロール量を直す（必要なときだけ動かす）。</summary>
        public float ScrollToShow(float scroll, float rowHeight, float viewHeight)
        {
            float top = RowOf(Selected) * rowHeight;
            float bottom = top + rowHeight;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + viewHeight) scroll = bottom - viewHeight;
            return ClampScroll(scroll, Rows * rowHeight, viewHeight);
        }
    }
}
