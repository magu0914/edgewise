using UnityEditor;
using UnityEngine;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// Assets/Audio/Bgm/ に置いた曲に、WebGL 向けのインポート設定を自動で付ける（DESIGN §8-2: BGM 込みで圧縮後 16MB 以下）。
    /// - 圧縮したまま持つ（Compressed In Memory）。裏で読み込む（Load In Background）ので、最初の場面で待たせない。
    /// - 品質（quality）0.35。WebGL は Unity が曲を AAC に変換し、ビットレートはこの品質で決まる。
    ///   この版の Unity では WebGL のプラットフォーム別の上書き（SetOverrideSampleSettings("WebGL")）が受け付けられない
    ///   （3c で確認。どの圧縮形式でも false）ので、既定の設定に入れる。
    /// - ステレオのまま（モノラルにしない）。サンプルレートは元のまま。
    /// 最初の取り込み時だけ設定する（userData に印を付ける）。その後インスペクターで変えた設定は上書きしない。
    /// 設定し直したいときは、メニュー「Edgewise/BGM のインポート設定をやり直す」を使う（docs/AUDIO.md）。
    /// </summary>
    public sealed class BgmImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Audio/Bgm/";
        public const string Marker = "edgewise-bgm-v1";
        public const float Quality = 0.35f;

        public static bool IsBgmPath(string path) =>
            path != null && path.Replace('\\', '/').StartsWith(Folder, System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessAudio()
        {
            if (!IsBgmPath(assetPath)) return;
            var importer = (AudioImporter)assetImporter;
            if (importer.userData != null && importer.userData.Contains(Marker)) return;
            Apply(importer);
            Debug.Log("[Edgewise] BGM import settings applied: " + assetPath);
        }

        /// <summary>BGM 用の設定を importer に書く（保存・再インポートは呼び出し側）。</summary>
        public static void Apply(AudioImporter importer)
        {
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var def = importer.defaultSampleSettings;
            def.loadType = AudioClipLoadType.CompressedInMemory;
            def.compressionFormat = AudioCompressionFormat.Vorbis;
            def.quality = Quality;
            def.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            def.preloadAudioData = true;
            importer.defaultSampleSettings = def;
            importer.userData = Marker;
        }

        [MenuItem("Edgewise/BGM のインポート設定をやり直す")]
        public static void ReapplyAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                Apply(importer);
                importer.SaveAndReimport();
                Debug.Log("[Edgewise] BGM import settings re-applied: " + path);
            }
        }
    }
}
