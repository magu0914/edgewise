namespace Edgewise.Core.AI
{
    /// <summary>
    /// 置換表用の Zobrist ハッシュの乱数表。mulberry32 を固定 seed で回して作るので、どの環境でも同じ値になる（DESIGN §5-1）。
    /// カード番号は SearchBoard が局面（決定化の世界）ごとに振る番号なので、世界が変わったら置換表は使い回さない。
    /// </summary>
    internal static class Zobrist
    {
        const uint Seed = 0x2b0b1157u;
        const int PlaceCount = GameConstants.BoardSize * SearchBoard.MaxCards * 4;
        const int OwnerBase = PlaceCount;
        const int HandBase = OwnerBase + GameConstants.BoardSize;
        const int PivotBase = HandBase + 2 * SearchBoard.MaxSlots;
        const int TurnIndex = PivotBase + 2 * 4;
        const int Count = TurnIndex + 1;

        static readonly ulong[] Keys = Build();

        static ulong[] Build()
        {
            var keys = new ulong[Count];
            uint s = Rng.SeedToState(Seed);
            for (int i = 0; i < Count; i++)
            {
                ulong hi = Rng.NextUInt32(s, out s);
                ulong lo = Rng.NextUInt32(s, out s);
                keys[i] = (hi << 32) | lo;
            }
            return keys;
        }

        public static ulong Place(int cell, int card, int rot) => Keys[(cell * SearchBoard.MaxCards + card) * 4 + rot];

        public static ulong Owner(int cell) => Keys[OwnerBase + cell];

        public static ulong Hand(int player, int slot) => Keys[HandBase + player * SearchBoard.MaxSlots + slot];

        public static ulong PivotKey(int player, int tokens) => Keys[PivotBase + player * 4 + tokens];

        public static ulong TurnKey => Keys[TurnIndex];
    }

    /// <summary>
    /// 置換表（固定サイズ、エントリは key・値・深さ・境界の種類・最善手）。
    /// 世代番号（generation）で論理的に空にする（配列をクリアしないので速く、使い回しても結果は変わらない）。
    /// </summary>
    internal sealed class TranspositionTable
    {
        public const int FlagExact = 0;
        public const int FlagLower = 1;
        public const int FlagUpper = 2;

        ulong[] _keys = new ulong[0];
        int[] _data = new int[0];
        int[] _gen = new int[0];
        int _mask;
        int _generation;

        public bool Enabled => _mask >= 0 && _keys.Length > 0;

        /// <summary>2^bits エントリだけを使う（bits = 0 なら無効）。配列が足りなければ確保し直す。新しい世代を始める。</summary>
        public void Prepare(int bits)
        {
            if (bits <= 0)
            {
                _mask = -1;
                return;
            }
            int size = 1 << bits;
            if (_keys.Length < size)
            {
                _keys = new ulong[size];
                _data = new int[size];
                _gen = new int[size];
                _generation = 0;
            }
            _mask = size - 1;
            NewGeneration();
        }

        /// <summary>全エントリを論理的に無効にする。</summary>
        public void NewGeneration()
        {
            _generation++;
            if (_generation == int.MaxValue)
            {
                for (int i = 0; i < _gen.Length; i++) _gen[i] = 0;
                _generation = 1;
            }
        }

        // data のビット配置: value + 32768（16bit）| depth（4bit）<< 16 | flag（2bit）<< 20 | move + 1（8bit）<< 22
        public bool Probe(ulong key, out int value, out int depth, out int flag, out int move)
        {
            int i = (int)key & _mask;
            if (_gen[i] == _generation && _keys[i] == key)
            {
                int d = _data[i];
                value = (d & 0xffff) - 32768;
                depth = (d >> 16) & 15;
                flag = (d >> 20) & 3;
                move = ((d >> 22) & 255) - 1;
                return true;
            }
            value = 0;
            depth = 0;
            flag = 0;
            move = -1;
            return false;
        }

        public void Store(ulong key, int value, int depth, int flag, int move)
        {
            int i = (int)key & _mask;
            if (_gen[i] == _generation && _keys[i] != key)
            {
                // 別の局面が入っているときは、より深く読んだものを優先する
                int oldDepth = (_data[i] >> 16) & 15;
                if (depth < oldDepth) return;
            }
            _gen[i] = _generation;
            _keys[i] = key;
            _data[i] = (value + 32768) | (depth << 16) | (flag << 20) | ((move + 1) << 22);
        }
    }
}
