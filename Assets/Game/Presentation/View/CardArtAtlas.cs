using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// カード絵の SDF アトラスと星座（DESIGN §7-1 パイプライン 2・3）。Editor の CardArtPipeline が生成する（手で編集しない）。
    /// アトラスは R8 の 1 枚（1 セル = 1 アイコン、<see cref="cellSize"/> px）。値 0.5 が輪郭、大きいほど内側。
    /// 距離 1.0（値 0 から 1 まで）が <see cref="spread"/>×2 セル px にあたる。
    /// 星と線の座標はセル内の 0..1（y は上向き）。描画は Card シェーダーが行う。
    /// </summary>
    public sealed class CardArtAtlas : ScriptableObject
    {
        public const int MaxStars = 14;
        public const int MaxLines = 14;

        [Serializable]
        public sealed class Entry
        {
            public string cardId;

            /// <summary>アトラス上のセル番号（左下から右へ、行は上へ）。</summary>
            public int cell;

            /// <summary>セルの UV（xy = 大きさ、zw = 左下）。</summary>
            public Vector4 uvRect;

            /// <summary>星: xy = セル内の位置、z = 大きさの倍率、w = またたきの位相（0..1）。</summary>
            public Vector4[] stars = new Vector4[0];

            /// <summary>線: xy = 始点、zw = 終点（セル内の位置）。</summary>
            public Vector4[] lines = new Vector4[0];
        }

        public Texture2D atlas;
        public int cellSize = 128;
        public int columns = 8;
        public int rows = 7;

        /// <summary>SDF の片側の幅（セルの px）。値 0..1 が距離 +spread..−spread に対応する。</summary>
        public float spread = 12f;

        public List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> _byId;

        public Entry Find(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_byId == null || _byId.Count != entries.Count)
            {
                _byId = new Dictionary<string, Entry>();
                foreach (var e in entries) _byId[e.cardId] = e;
            }
            return _byId.TryGetValue(cardId, out var hit) ? hit : null;
        }

        /// <summary>シェーダーに渡す固定長の配列に詰める（足りない分はゼロ）。</summary>
        public static void Fill(Vector4[] src, Vector4[] dst)
        {
            int n = src == null ? 0 : Mathf.Min(src.Length, dst.Length);
            for (int i = 0; i < dst.Length; i++) dst[i] = i < n ? src[i] : Vector4.zero;
        }
    }
}
