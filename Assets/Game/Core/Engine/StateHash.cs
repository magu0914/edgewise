using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Edgewise.Core
{
    /// <summary>
    /// hashState: オンライン同期チェック用の決定論的ハッシュ。TS 版 hash.ts と同じ正規化文字列・同じ 64bit 値を返す。
    /// 置換表用の Zobrist ハッシュは Phase 2（AI/）で別途用意する。
    /// </summary>
    public static class StateHash
    {
        /// <summary>state の正規化文字列表現（同じ局面なら必ず同じ文字列）。</summary>
        public static string Serialize(GameState state)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(512);
            sb.Append(state.Rules.ToBitString()).Append(';');

            // JS の Array.prototype.sort() の既定（UTF-16 コード単位順）と同じ順序
            var ids = new List<string>(state.Cards.Keys);
            ids.Sort(string.CompareOrdinal);
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var c = state.Cards[ids[i]];
                sb.Append(ids[i]).Append('=').Append(c.Sides.ToString()).Append('.');
                if (c.Element.HasValue) sb.Append(c.Element.Value.ToId());
                // Resonance / Traits が ON のときだけ系統・Trait も同期チェックの対象にする（OFF の文字列は TS 版のまま）
                if (state.Rules.Resonance && c.Tribe.HasValue) sb.Append('#').Append(c.Tribe.Value.ToId());
                if (state.Rules.Traits && c.Trait.HasValue) sb.Append('@').Append(c.Trait.Value.ToId());
            }
            sb.Append(';');

            var board = state.BoardArray;
            for (int i = 0; i < board.Length; i++)
            {
                if (i > 0) sb.Append(',');
                if (!board[i].HasValue)
                {
                    sb.Append('_');
                    continue;
                }
                var pc = board[i].Value;
                sb.Append(pc.CardId).Append(':').Append(((int)pc.Owner).ToString(inv)).Append(pc.Rotation.ToString(inv));
                if (pc.GuardUsed) sb.Append('g'); // Guard を使い切った（Traits ON のときだけ起こる）
            }
            sb.Append(';');

            var elements = state.CellElementArray;
            for (int i = 0; i < elements.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(elements[i].HasValue ? elements[i].Value.ToId() : "_");
            }
            sb.Append(';');

            sb.Append(string.Join(",", state.HandArray(Player.P0))).Append('|').Append(string.Join(",", state.HandArray(Player.P1)));
            sb.Append(';').Append(((int)state.Turn).ToString(inv));
            sb.Append(';').Append(((int)state.FirstPlayer).ToString(inv));
            sb.Append(';').Append(state.GetPivotTokens(Player.P0).ToString(inv)).Append(',').Append(state.GetPivotTokens(Player.P1).ToString(inv));
            sb.Append(';').Append(state.RngState.ToString(inv));
            sb.Append(';').Append(state.Phase.ToId());
            sb.Append(';').Append(state.SuddenDeathRound.ToString(inv));
            return sb.ToString();
        }

        /// <summary>cyrb53 系の 2×32bit ハッシュ。上位 32bit = h2、下位 32bit = h1（TS 版の 16 桁 hex と同じ並び）。</summary>
        public static ulong Hash64(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            unchecked
            {
                uint h1 = 0xdeadbeefu;
                uint h2 = 0x41c6ce57u;
                for (int i = 0; i < str.Length; i++)
                {
                    uint ch = str[i]; // UTF-16 コード単位（JS の charCodeAt と同じ）
                    h1 = (h1 ^ ch) * 2654435761u;
                    h2 = (h2 ^ ch) * 1597334677u;
                }
                h1 = (h1 ^ (h1 >> 16)) * 2246822507u;
                h1 ^= (h2 ^ (h2 >> 13)) * 3266489909u;
                h2 = (h2 ^ (h2 >> 16)) * 2246822507u;
                h2 ^= (h1 ^ (h1 >> 13)) * 3266489909u;
                return ((ulong)h2 << 32) | h1;
            }
        }

        /// <summary>局面のハッシュ（16 桁 hex、小文字）。同じ seed と手順なら必ず一致する。</summary>
        public static string Hash(GameState state) => Hash64(Serialize(state)).ToString("x16", CultureInfo.InvariantCulture);
    }
}
