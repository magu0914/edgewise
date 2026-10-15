using Edgewise.Game.Juice;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 実行時に使うアセットの参照をまとめたもの。Editor の ProjectSetup が生成・更新する（手で編集しない）。
    /// </summary>
    [CreateAssetMenu(menuName = "Edgewise/Game Assets", fileName = "GameAssets")]
    public sealed class GameAssets : ScriptableObject
    {
        public JuiceSettings juice;

        /// <summary>場面ごとの BGM の割り当て（3c）。曲が未設定の場面は無音。</summary>
        public Edgewise.Game.Audio.BgmLibrary bgm;
        public CardView cardPrefab;
        public Material cardMaterial;
        public Material shapeMaterial;
        public Material boardMaterial;
        public Material starfieldMaterial;
        public TMP_FontAsset font;

        /// <summary>縁取りつき（カードのランク・大きな文字）。</summary>
        public Material fontOutline;

        /// <summary>uGUI の角丸（9 スライス）。</summary>
        public Sprite roundRect;

        public Mesh quad;

        /// <summary>カード絵の SDF アトラスと星座（Phase 4。CardArtPipeline が生成）。</summary>
        public CardArtAtlas cardArt;

        /// <summary>カード → アイコン（作者・名前）の対応。クレジット表示に使う。</summary>
        public CardArtTable cardArtTable;
    }
}
