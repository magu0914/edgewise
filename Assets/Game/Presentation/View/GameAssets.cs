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
    }
}
