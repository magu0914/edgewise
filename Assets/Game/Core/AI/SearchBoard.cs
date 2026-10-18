using System;
using System.Collections.Generic;

namespace Edgewise.Core.AI
{
    /// <summary>
    /// 探索用の可変な局面（make / unmake 方式）。GameState と違いアロケーションせずに 1 手を進めて戻せる。
    /// 裏返しの判定は GameEngine.ApplyMoveFast と同じ順序・同じ規則（一致はテストで保証）。
    /// 1 ラウンドの終局（盤面が埋まる）までしか扱わない（Sudden Death の再戦は探索しない。DESIGN §5-1）。
    ///
    /// 手の表現: code = slot * 36 + cell * 4 + rotation（0..179）。slot は手札の「開始時の位置」で、取り除いても番号は詰めない。
    /// カードは局面ごとに 0..MaxCards-1 の番号を振り、辺の値は「カード × 回転 × マス × 方向」の表（Elemental 補正込み）で持つ。
    /// </summary>
    internal sealed class SearchBoard
    {
        public const int MaxCards = 24;
        public const int MaxSlots = GameConstants.HandSize;
        public const int MoveCodeCount = MaxSlots * 36;
        const int SideStride = 4 * 36;

        // ---- ルール ----
        bool _same;
        bool _sameWall;
        bool _plus;
        bool _combo;
        bool _pivotRule;
        bool _resonance;
        bool _traits;

        // ---- カード表 ----
        readonly int[] _side = new int[MaxCards * SideStride];
        readonly int[] _maxSide = new int[MaxCards * 36];
        readonly int[] _rawSum = new int[MaxCards];
        readonly int[] _rotMask = new int[MaxCards];
        readonly int[] _cardClass = new int[MaxCards];
        readonly CardDef[] _defs = new CardDef[MaxCards];
        readonly int[] _cardTribe = new int[MaxCards]; // 系統（Tribe）の番号。なし、または Resonance OFF なら -1
        readonly int[] _cardTrait = new int[MaxCards]; // Trait の番号。なし、または Traits OFF なら -1
        int _cardCount;

        // ---- 局面 ----
        readonly int[] _val = new int[36];
        readonly int[] _cellCard = new int[GameConstants.BoardSize];
        readonly int[] _cellRot = new int[GameConstants.BoardSize];
        readonly int[] _queue = new int[GameConstants.BoardSize];
        readonly int[] _tribeCells = new int[GameConstants.TribeCount]; // 系統ごとの、カードが置かれているマスのビットマスク
        int _rally;     // 置いたカードの Rally ボーナス（ResolveFlips の間だけ有効）
        int _rallyCell; // 置いたカードのマス

        public int Occupied;
        public int OwnerP1;

        /// <summary>Guard を使い切ったカードのマスのビットマスク（Traits ON のときだけ増える）。Zobrist にも入る。</summary>
        public int GuardUsedMask;

        public readonly int[] HandCard = new int[2 * MaxSlots];
        public readonly int[] HandMask = new int[2];
        public readonly int[] Pivot = new int[2];
        public int Turn;
        public ulong Hash;

        /// <summary>評価関数の重み（1/100 枚単位）。</summary>
        public EvalWeights Weights = EvalWeights.Default;

        public bool PivotRule => _pivotRule;

        public int CardCount => _cardCount;

        // ------------------------------------------------------------------
        // 構築
        // ------------------------------------------------------------------

        /// <summary>
        /// 局面を組み立てる。hands[p] は各プレイヤーの手札のカード定義（null 不可。Open OFF の相手手札は決定化で埋めたもの）。
        /// blind = true なら Same / Plus / Combo を無視する（Easy の「見落とし」）。
        /// </summary>
        public void Load(GameState state, IReadOnlyList<CardDef> hand0, IReadOnlyList<CardDef> hand1, bool blind)
        {
            var rules = state.Rules;
            _same = rules.Same && !blind;
            _sameWall = _same && rules.SameWall;
            _plus = rules.Plus && !blind;
            _combo = rules.Combo && !blind;
            _pivotRule = rules.Pivot;
            _resonance = rules.Resonance;
            _traits = rules.Traits;
            _cardCount = 0;
            GuardUsedMask = 0;
            for (int t = 0; t < _tribeCells.Length; t++) _tribeCells[t] = 0;

            var elements = state.CellElementArray;
            bool elemental = rules.Elemental;

            Occupied = 0;
            OwnerP1 = 0;
            Hash = 0;
            var board = state.BoardArray;
            for (int cell = 0; cell < GameConstants.BoardSize; cell++)
            {
                _cellCard[cell] = -1;
                _cellRot[cell] = 0;
                if (!board[cell].HasValue) continue;
                var pc = board[cell].Value;
                int card = AddCard(pc.Card, elements, elemental);
                int rot = pc.Rotation;
                _cellCard[cell] = card;
                _cellRot[cell] = rot;
                int baseIdx = card * SideStride + rot * 36 + cell * 4;
                for (int d = 0; d < 4; d++) _val[cell * 4 + d] = _side[baseIdx + d];
                Occupied |= 1 << cell;
                if (pc.Owner == Player.P1) OwnerP1 |= 1 << cell;
                Hash ^= Zobrist.Place(cell, card, rot);
                if (pc.Owner == Player.P1) Hash ^= Zobrist.Owner(cell);
                if (_cardTribe[card] >= 0) _tribeCells[_cardTribe[card]] |= 1 << cell;
                if (_traits && pc.GuardUsed)
                {
                    GuardUsedMask |= 1 << cell;
                    Hash ^= Zobrist.GuardUsed(cell);
                }
            }

            LoadHand(0, hand0, elements, elemental);
            LoadHand(1, hand1, elements, elemental);

            Turn = (int)state.Turn;
            if (Turn == 1) Hash ^= Zobrist.TurnKey;
            for (int p = 0; p < 2; p++)
            {
                // 先攻・後攻で数が違ってよい（Phase 3c）。Zobrist 表は 0..MaxPivotTokens の分だけある
                int t = state.GetPivotTokens((Player)p);
                if (t < 0) t = 0;
                if (t > GameConstants.MaxPivotTokens) t = GameConstants.MaxPivotTokens;
                Pivot[p] = t;
                Hash ^= Zobrist.PivotKey(p, t);
            }
        }

        void LoadHand(int p, IReadOnlyList<CardDef> hand, Element?[] elements, bool elemental)
        {
            if (hand.Count > MaxSlots) throw new InvalidOperationException("hand too large for search: " + hand.Count);
            HandMask[p] = 0;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                HandCard[p * MaxSlots + slot] = -1;
                if (slot >= hand.Count) continue;
                HandCard[p * MaxSlots + slot] = AddCard(hand[slot], elements, elemental);
                HandMask[p] |= 1 << slot;
                Hash ^= Zobrist.Hand(p, slot);
            }
        }

        int AddCard(CardDef def, Element?[] elements, bool elemental)
        {
            if (_cardCount >= MaxCards) throw new InvalidOperationException("too many cards for search");
            int card = _cardCount++;
            _defs[card] = def;
            _cardTribe[card] = _resonance && def.Tribe.HasValue ? (int)def.Tribe.Value : -1;
            _cardTrait[card] = _traits && def.Trait.HasValue ? (int)def.Trait.Value : -1;
            var s = def.Sides;
            _rawSum[card] = s.Sum;

            // 同じ定義（辺・属性が同じ）のカードは同じクラス（手の重複を省くため）
            _cardClass[card] = card;
            for (int other = 0; other < card; other++)
            {
                var o = _defs[other];
                if (o.Sides == s && o.Element == def.Element && _cardTribe[other] == _cardTribe[card] && _cardTrait[other] == _cardTrait[card])
                {
                    _cardClass[card] = _cardClass[other];
                    break;
                }
            }

            // 回転後の辺が回転なし（または先の回転）と同じになる回転は、トークンを無駄にするだけなので読まない
            int mask = 1;
            for (int r = 1; r < 4; r++)
            {
                var rs = Board.RotateSides(s, r);
                bool dup = false;
                for (int q = 0; q < r; q++)
                {
                    if ((mask & (1 << q)) != 0 && Board.RotateSides(s, q) == rs)
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup) mask |= 1 << r;
            }
            _rotMask[card] = mask;

            for (int rot = 0; rot < 4; rot++)
            {
                for (int cell = 0; cell < GameConstants.BoardSize; cell++)
                {
                    int mod = 0;
                    if (elemental)
                    {
                        var e = elements[cell];
                        if (e.HasValue) mod = def.Element == e.Value ? 1 : -1;
                    }
                    int baseIdx = card * SideStride + rot * 36 + cell * 4;
                    for (int d = 0; d < 4; d++) _side[baseIdx + d] = s[(d - rot + 4) & 3] + mod;
                }
            }
            for (int i = 0; i < 36; i++)
            {
                int m = _side[card * SideStride + i];
                for (int rot = 1; rot < 4; rot++)
                {
                    int v = _side[card * SideStride + rot * 36 + i];
                    if (v > m) m = v;
                }
                _maxSide[card * 36 + i] = m;
            }
            return card;
        }

        // ------------------------------------------------------------------
        // 手の生成
        // ------------------------------------------------------------------

        public static int Code(int slot, int cell, int rot) => slot * 36 + cell * 4 + rot;

        public static int SlotOf(int code) => code / 36;

        public static int CellOf(int code) => (code >> 2) % 9;

        public static int RotOf(int code) => code & 3;

        /// <summary>現在の手番の手を buffer[offset..] に書き、数を返す。同じ定義のカードの重複と、無駄な回転は除く。</summary>
        public int GenerateMoves(int[] buffer, int offset)
        {
            int p = Turn;
            int hand = HandMask[p];
            int empty = ~Occupied & 511;
            int seenClasses = 0;
            int n = 0;
            bool rotate = _pivotRule && Pivot[p] > 0;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if ((hand & (1 << slot)) == 0) continue;
                int card = HandCard[p * MaxSlots + slot];
                int cls = 1 << _cardClass[card];
                if ((seenClasses & cls) != 0) continue;
                seenClasses |= cls;
                int rmask = rotate ? _rotMask[card] : 1;
                for (int cell = 0; cell < GameConstants.BoardSize; cell++)
                {
                    if ((empty & (1 << cell)) == 0) continue;
                    for (int r = 0; r < 4; r++)
                    {
                        if ((rmask & (1 << r)) != 0) buffer[offset + n++] = slot * 36 + cell * 4 + r;
                    }
                }
            }
            return n;
        }

        /// <summary>同じ定義のカードなら同じ値（ルートで重複手をまとめるため）。</summary>
        public int CardClassOfSlot(int player, int slot) => _cardClass[HandCard[player * MaxSlots + slot]];

        /// <summary>この回転が「無駄な回転」（回転なし等と同じ辺になる）でないか。</summary>
        public bool IsUsefulRotation(int player, int slot, int rot) => (_rotMask[HandCard[player * MaxSlots + slot]] & (1 << rot)) != 0;

        // ------------------------------------------------------------------
        // make / unmake
        // ------------------------------------------------------------------

        /// <summary>手を適用し、裏返ったマスのビットマスクを返す（Unmake に渡す）。合法性は検査しない。</summary>
        public int Make(int code)
        {
            int slot = code / 36;
            int cell = (code >> 2) % 9;
            int rot = code & 3;
            int p = Turn;
            int card = HandCard[p * MaxSlots + slot];

            int baseIdx = card * SideStride + rot * 36 + cell * 4;
            int cb = cell * 4;
            _val[cb] = _side[baseIdx];
            _val[cb + 1] = _side[baseIdx + 1];
            _val[cb + 2] = _side[baseIdx + 2];
            _val[cb + 3] = _side[baseIdx + 3];
            _cellCard[cell] = card;
            _cellRot[cell] = rot;

            int bit = 1 << cell;
            if (_cardTribe[card] >= 0) _tribeCells[_cardTribe[card]] |= bit;
            Occupied |= bit;
            ulong h = Hash ^ Zobrist.Place(cell, card, rot) ^ Zobrist.Hand(p, slot) ^ Zobrist.TurnKey;
            if (p == 1)
            {
                OwnerP1 |= bit;
                h ^= Zobrist.Owner(cell);
            }
            HandMask[p] &= ~(1 << slot);
            if (rot != 0)
            {
                h ^= Zobrist.PivotKey(p, Pivot[p]);
                Pivot[p]--;
                h ^= Zobrist.PivotKey(p, Pivot[p]);
            }

            int flipped = ResolveFlips(cell, p, out int guarded);
            if (flipped != 0)
            {
                OwnerP1 ^= flipped;
                for (int c = 0; c < GameConstants.BoardSize; c++)
                {
                    if ((flipped & (1 << c)) != 0) h ^= Zobrist.Owner(c);
                }
            }
            if (guarded != 0)
            {
                // ResolveFlips が GuardUsedMask を更新済み。Zobrist だけここで反映する
                for (int c = 0; c < GameConstants.BoardSize; c++)
                {
                    if ((guarded & (1 << c)) != 0) h ^= Zobrist.GuardUsed(c);
                }
            }
            Hash = h;
            Turn = p ^ 1;
            return flipped | (guarded << 9);
        }

        /// <summary>Make の戻り値（下位 9 ビット = 裏返ったマス、その上の 9 ビット = この手で Guard を使い切ったマス）を渡して戻す。</summary>
        public void Unmake(int code, int made)
        {
            int flipped = made & 511;
            int guarded = made >> 9;
            int slot = code / 36;
            int cell = (code >> 2) % 9;
            int rot = code & 3;
            int p = Turn ^ 1;
            int card = HandCard[p * MaxSlots + slot];

            ulong h = Hash ^ Zobrist.TurnKey;
            if (guarded != 0)
            {
                GuardUsedMask &= ~guarded;
                for (int c = 0; c < GameConstants.BoardSize; c++)
                {
                    if ((guarded & (1 << c)) != 0) h ^= Zobrist.GuardUsed(c);
                }
            }
            if (flipped != 0)
            {
                OwnerP1 ^= flipped;
                for (int c = 0; c < GameConstants.BoardSize; c++)
                {
                    if ((flipped & (1 << c)) != 0) h ^= Zobrist.Owner(c);
                }
            }
            if (rot != 0)
            {
                h ^= Zobrist.PivotKey(p, Pivot[p]);
                Pivot[p]++;
                h ^= Zobrist.PivotKey(p, Pivot[p]);
            }
            HandMask[p] |= 1 << slot;
            int bit = 1 << cell;
            if (p == 1)
            {
                OwnerP1 &= ~bit;
                h ^= Zobrist.Owner(cell);
            }
            Occupied &= ~bit;
            if (_cardTribe[card] >= 0) _tribeCells[_cardTribe[card]] &= ~bit;
            _cellCard[cell] = -1;
            h ^= Zobrist.Place(cell, card, rot) ^ Zobrist.Hand(p, slot);
            Hash = h;
            Turn = p;
        }

        /// <summary>
        /// 比較の瞬間の辺の値。素の値（Elemental 込み）+ Resonance（共鳴していれば +1）+ Rally（置いたカードだけ）。
        /// opp は「いま相手（手番でない側）が所有しているマス」のマスクで、共鳴の判定（同じ所有者か）に使う。
        /// </summary>
        int SideNow(int cell, int dir, int opp)
        {
            int v = _val[cell * 4 + dir];
            if (_resonance && Resonant(cell, opp)) v++;
            if (cell == _rallyCell) v += _rally;
            return v;
        }

        /// <summary>cell のカードが共鳴しているか（同じ所有者・同じ系統のカードが縦横に隣接）。ownerMask はどちらか一方の側の所有マスク。</summary>
        bool Resonant(int cell, int ownerMask)
        {
            int t = _cardTribe[_cellCard[cell]];
            if (t < 0) return false;
            int group = (ownerMask & (1 << cell)) != 0 ? ownerMask : Occupied & ~ownerMask;
            return (NeighborMask[cell] & _tribeCells[t] & group) != 0;
        }

        /// <summary>
        /// Guard / Anchor が裏返しを防ぐか。Guard は最初の 1 回だけ（GuardUsedMask に入れ、guarded に記録）、Anchor は Combo のときだけ。
        /// </summary>
        bool Prevents(int n, bool isCombo, ref int guarded)
        {
            int trait = _cardTrait[_cellCard[n]];
            if (trait == (int)TraitId.Guard)
            {
                int bit = 1 << n;
                if ((GuardUsedMask & bit) == 0)
                {
                    GuardUsedMask |= bit;
                    guarded |= bit;
                    return true;
                }
                return false;
            }
            return isCombo && trait == (int)TraitId.Anchor;
        }

        /// <summary>
        /// 置いたカード（cell、所有者 p）による裏返しを計算し、裏返るマスのマスクを返す（OwnerP1 はまだ変更しない）。
        /// 手順は ApplyMoveFast と同じ: Same/Plus → 通常ルール → Pierce → Combo（Same/Plus で裏返ったカードを起点に FIFO）。
        /// Guard を使い切ったマスは guarded に返し、GuardUsedMask はここで更新する。
        /// </summary>
        int ResolveFlips(int cell, int p, out int guarded)
        {
            // owner(c) == p  ⇔  ((OwnerP1 >> c) & 1) == p。ここでは「p 以外の所有」を opp マスクで持つ
            int occ = Occupied & ~(1 << cell);
            int opp = p == 1 ? occ & ~OwnerP1 : occ & OwnerP1;
            int flipped = 0;
            int cb = cell * 4;
            int queueLength = 0;
            guarded = 0;

            // Rally: 置いた時点の盤面で、縦横に隣接する自分のカード 1 枚につき +1（最大 +2）
            _rallyCell = cell;
            _rally = 0;
            bool traits = _traits;
            int placedTrait = traits ? _cardTrait[_cellCard[cell]] : -1;
            if (placedTrait == (int)TraitId.Rally)
            {
                int mineAdj = NeighborMask[cell] & occ & ~opp;
                int count = PopCount(mineAdj);
                _rally = count > 2 ? 2 : count;
            }

            if (_same || _plus)
            {
                int sameCount = 0;
                int sameMask = 0;
                int s0 = -1, s1 = -1, s2 = -1, s3 = -1;
                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Neighbors[cb + dir];
                    int v = SideNow(cell, dir, opp);
                    if (n < 0)
                    {
                        if (_sameWall && v == GameConstants.WallRank) sameCount++;
                        continue;
                    }
                    if ((occ & (1 << n)) == 0) continue;
                    int t = SideNow(n, (dir + 2) & 3, opp);
                    if (_same && v == t)
                    {
                        sameCount++;
                        sameMask |= 1 << dir;
                    }
                    if (_plus)
                    {
                        int sum = v + t;
                        switch (dir)
                        {
                            case 0: s0 = sum; break;
                            case 1: s1 = sum; break;
                            case 2: s2 = sum; break;
                            default: s3 = sum; break;
                        }
                    }
                }
                int flipDirs = sameCount >= 2 ? sameMask : 0;
                if (_plus)
                {
                    if (s0 >= 0 && (s0 == s1 || s0 == s2 || s0 == s3)) flipDirs |= 1;
                    if (s1 >= 0 && (s1 == s0 || s1 == s2 || s1 == s3)) flipDirs |= 2;
                    if (s2 >= 0 && (s2 == s0 || s2 == s1 || s2 == s3)) flipDirs |= 4;
                    if (s3 >= 0 && (s3 == s0 || s3 == s1 || s3 == s2)) flipDirs |= 8;
                }
                if (flipDirs != 0)
                {
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if ((flipDirs & (1 << dir)) == 0) continue;
                        int n = Neighbors[cb + dir];
                        int nb = 1 << n;
                        if ((opp & nb) != 0)
                        {
                            if (traits && Prevents(n, false, ref guarded)) continue;
                            opp &= ~nb;
                            flipped |= nb;
                            _queue[queueLength++] = n;
                        }
                    }
                }
            }

            // 通常ルール
            for (int dir = 0; dir < 4; dir++)
            {
                int n = Neighbors[cb + dir];
                if (n < 0) continue;
                int nb = 1 << n;
                if ((opp & nb) == 0) continue;
                if (SideNow(cell, dir, opp) > SideNow(n, (dir + 2) & 3, opp))
                {
                    if (traits && Prevents(n, false, ref guarded)) continue;
                    opp &= ~nb;
                    flipped |= nb;
                }
            }

            // Pierce（斜め 4 方向。右上 → 右下 → 左下 → 左上）。Combo の起点にならない
            if (placedTrait == (int)TraitId.Pierce)
            {
                for (int k = 0; k < 4; k++)
                {
                    int n = Board.Diagonal(cell, k);
                    if (n < 0) continue;
                    int nb = 1 << n;
                    if ((opp & nb) == 0) continue;
                    int a = Board.PierceDirA[k];
                    int b = Board.PierceDirB[k];
                    int mineSum = SideNow(cell, a, opp) + SideNow(cell, b, opp);
                    int theirSum = SideNow(n, (a + 2) & 3, opp) + SideNow(n, (b + 2) & 3, opp);
                    if (mineSum > theirSum)
                    {
                        if (Prevents(n, false, ref guarded)) continue;
                        opp &= ~nb;
                        flipped |= nb;
                    }
                }
            }

            // Combo
            if (_combo)
            {
                for (int head = 0; head < queueLength; head++)
                {
                    int src = _queue[head];
                    int sb = src * 4;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int n = Neighbors[sb + dir];
                        if (n < 0) continue;
                        int nb = 1 << n;
                        if ((opp & nb) == 0) continue;
                        if (SideNow(src, dir, opp) > SideNow(n, (dir + 2) & 3, opp))
                        {
                            if (traits && Prevents(n, true, ref guarded)) continue;
                            opp &= ~nb;
                            flipped |= nb;
                            _queue[queueLength++] = n;
                        }
                    }
                }
            }
            return flipped;
        }

        // ------------------------------------------------------------------
        // 評価
        // ------------------------------------------------------------------

        public int EmptyCount => 9 - PopCount(Occupied);

        /// <summary>P0 のスコア − P1 のスコア（盤上の所有枚数 + 手札の枚数）。</summary>
        public int ScoreDiffP0()
        {
            int p1 = PopCount(OwnerP1);
            int p0 = PopCount(Occupied) - p1;
            return p0 + PopCount(HandMask[0]) - p1 - PopCount(HandMask[1]);
        }

        /// <summary>ラウンド終局時の評価値（手番側から見た値）。勝ち = 差×100 + WinBonus、引き分け = 0（Sudden Death の再戦も 0）。</summary>
        public int TerminalValue()
        {
            int diff = ScoreDiffP0();
            if (Turn == 1) diff = -diff;
            return TerminalValueOfDiff(diff);
        }

        public static int TerminalValueOfDiff(int diff)
        {
            if (diff > 0) return diff * 100 + SearchConstants.WinBonus;
            if (diff < 0) return diff * 100 - SearchConstants.WinBonus;
            return 0;
        }

        /// <summary>
        /// 途中局面の評価（手番側から見た値、1/100 枚単位）。
        /// 所有差（スコア差）＋ 手番ボーナス ＋ 手札の強さ ＋ 辺の露出リスク ＋ Pivot トークンの残り。
        /// </summary>
        public int Evaluate()
        {
            var w = Weights;
            int v = ScoreDiffP0() * 100;

            // 辺の露出リスク: 空きマスに面した辺について
            //  - 相手の手札（この世界で確定している手札）に、そのマスに置いてその辺に勝てるカードがあれば「狙われている」
            //  - 加えて辺の値そのものによる一般的なリスク（Same / Plus など大小比較以外の取られ方の目安）
            // カードごとに最も危ない露出辺で数える。
            int occ = Occupied;
            int empty = ~occ & 511;
            int risk0 = 0, risk1 = 0;
            int guard0 = 0, guard1 = 0;
            int turn = Turn;
            for (int c = 0; c < GameConstants.BoardSize; c++)
            {
                if ((occ & (1 << c)) == 0) continue;
                int owner = (OwnerP1 >> c) & 1;
                int attacker = owner ^ 1;
                int attackHand = HandMask[attacker];
                bool attackerRotates = _pivotRule && Pivot[attacker] > 0;
                int cb = c * 4;
                int worst = 0;
                int resBonus = _resonance && Resonant(c, OwnerP1) ? 1 : 0;
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbors[cb + d];
                    if (n < 0 || (empty & (1 << n)) == 0) continue;
                    int sv = _val[cb + d] + resBonus;
                    int r = sv <= 0 ? w.RiskTable[0] : sv >= RiskTableSize ? 0 : w.RiskTable[sv];
                    if (attackHand != 0)
                    {
                        int idx = n * 4 + ((d + 2) & 3);
                        for (int slot = 0; slot < MaxSlots; slot++)
                        {
                            if ((attackHand & (1 << slot)) == 0) continue;
                            int card = HandCard[attacker * MaxSlots + slot];
                            int atk = attackerRotates ? _maxSide[card * 36 + idx] : _side[card * SideStride + idx];
                            if (atk > sv)
                            {
                                r += attacker == turn ? w.ThreatToMove : w.Threat;
                                break;
                            }
                        }
                    }
                    if (r > worst) worst = r;
                }
                if (owner == 1) risk1 += worst;
                else risk0 += worst;

                // 未使用の Guard は 1 回ぶんの守りなので少し加点（持ち主側に）
                if (_traits && _cardTrait[_cellCard[c]] == (int)TraitId.Guard && (GuardUsedMask & (1 << c)) == 0)
                {
                    if (owner == 1) guard1 += w.GuardBonus;
                    else guard0 += w.GuardBonus;
                }
            }
            v += risk1 - risk0 + guard0 - guard1;

            // 手札の強さ（辺の合計）: 強いカードを後に残している方が有利
            int sum0 = 0, sum1 = 0;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if ((HandMask[0] & (1 << slot)) != 0) sum0 += _rawSum[HandCard[slot]];
                if ((HandMask[1] & (1 << slot)) != 0) sum1 += _rawSum[HandCard[MaxSlots + slot]];
            }
            v += (sum0 - sum1) * w.HandSidePoint;

            if (_pivotRule) v += (Pivot[0] - Pivot[1]) * w.PivotToken;

            if (Turn == 1) v = -v;
            return v + w.Tempo;
        }

        public const int RiskTableSize = 12;

        // ------------------------------------------------------------------
        // ユーティリティ
        // ------------------------------------------------------------------

        static readonly int[] Neighbors = BuildNeighbors();

        /// <summary>マスごとの、縦横の隣のマスのビットマスク。</summary>
        static readonly int[] NeighborMask = BuildNeighborMasks();

        static int[] BuildNeighborMasks()
        {
            var t = new int[GameConstants.BoardSize];
            for (int c = 0; c < GameConstants.BoardSize; c++)
                for (int d = 0; d < 4; d++)
                {
                    int n = Board.Neighbor(c, d);
                    if (n >= 0) t[c] |= 1 << n;
                }
            return t;
        }

        static int[] BuildNeighbors()
        {
            var t = new int[36];
            for (int c = 0; c < 9; c++)
                for (int d = 0; d < 4; d++)
                    t[c * 4 + d] = Board.Neighbor(c, d);
            return t;
        }

        static readonly byte[] PopTable = BuildPopTable();

        static byte[] BuildPopTable()
        {
            var t = new byte[512];
            for (int i = 1; i < 512; i++) t[i] = (byte)(t[i >> 1] + (i & 1));
            return t;
        }

        /// <summary>9 ビット以下のマスクのビット数。</summary>
        public static int PopCount(int mask) => PopTable[mask & 511];

        /// <summary>Hash を差分更新せずに一から計算する（テスト用）。</summary>
        public ulong ComputeHashSlow()
        {
            ulong h = 0;
            for (int c = 0; c < GameConstants.BoardSize; c++)
            {
                if ((Occupied & (1 << c)) == 0) continue;
                h ^= Zobrist.Place(c, _cellCard[c], _cellRot[c]);
                if ((OwnerP1 & (1 << c)) != 0) h ^= Zobrist.Owner(c);
                if ((GuardUsedMask & (1 << c)) != 0) h ^= Zobrist.GuardUsed(c);
            }
            for (int p = 0; p < 2; p++)
            {
                for (int slot = 0; slot < MaxSlots; slot++)
                {
                    if ((HandMask[p] & (1 << slot)) != 0) h ^= Zobrist.Hand(p, slot);
                }
                h ^= Zobrist.PivotKey(p, Pivot[p]);
            }
            if (Turn == 1) h ^= Zobrist.TurnKey;
            return h;
        }

        /// <summary>盤上の所有者（テスト用）。空きマスは -1。</summary>
        public int OwnerAt(int cell)
        {
            if ((Occupied & (1 << cell)) == 0) return -1;
            return (OwnerP1 >> cell) & 1;
        }
    }

    /// <summary>評価関数の重み（1/100 枚単位）。</summary>
    public sealed class EvalWeights
    {
        public static readonly EvalWeights Default = new EvalWeights(
            tempo: 50,
            handSidePoint: 3,
            pivotToken: 15,
            threat: 40,
            threatToMove: 60,
            riskTable: new[] { 20, 20, 18, 16, 14, 11, 8, 5, 3, 1, 0, 0 },
            guardBonus: 30);

        /// <summary>手番側のボーナス（次の 1 手で何かを取れる見込み）。</summary>
        public int Tempo { get; }

        /// <summary>相手の手札のどれかで大小比較で取られうるカード 1 枚あたりの減点（相手が手番でないとき）。</summary>
        public int Threat { get; }

        /// <summary>同上（相手が手番のとき。すぐに取られうる）。</summary>
        public int ThreatToMove { get; }

        /// <summary>手札に残ったカードの辺の合計 1 点あたりの価値。</summary>
        public int HandSidePoint { get; }

        /// <summary>Pivot トークン 1 個の価値（使い切りを避ける）。</summary>
        public int PivotToken { get; }

        /// <summary>盤上の未使用の Guard 1 枚の価値（Traits ON のときだけ評価に入る。1 回ぶんの守りなので小さく）。</summary>
        public int GuardBonus { get; }

        internal readonly int[] RiskTable;

        public EvalWeights(int tempo, int handSidePoint, int pivotToken, int threat, int threatToMove, int[] riskTable, int guardBonus = 30)
        {
            if (riskTable == null || riskTable.Length != SearchBoard.RiskTableSize) throw new ArgumentException("riskTable must have 12 entries");
            Tempo = tempo;
            HandSidePoint = handSidePoint;
            PivotToken = pivotToken;
            Threat = threat;
            ThreatToMove = threatToMove;
            GuardBonus = guardBonus;
            RiskTable = (int[])riskTable.Clone();
        }

        /// <summary>一般的なリスク表を factor/100 倍した重み（調整用）。</summary>
        public EvalWeights With(int? tempo = null, int? handSidePoint = null, int? pivotToken = null, int? threat = null,
            int? threatToMove = null, int? riskPercent = null)
        {
            var table = (int[])RiskTable.Clone();
            if (riskPercent.HasValue)
            {
                for (int i = 0; i < table.Length; i++) table[i] = table[i] * riskPercent.Value / 100;
            }
            return new EvalWeights(tempo ?? Tempo, handSidePoint ?? HandSidePoint, pivotToken ?? PivotToken, threat ?? Threat,
                threatToMove ?? ThreatToMove, table, GuardBonus);
        }

        public override string ToString() =>
            "tempo " + Tempo + ", hand " + HandSidePoint + ", pivot " + PivotToken + ", threat " + Threat + "/" + ThreatToMove
            + ", risk [" + string.Join(",", RiskTable) + "]";
    }
}
