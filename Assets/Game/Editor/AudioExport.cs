using System.IO;
using Edgewise.Game.Audio;
using UnityEditor;
using UnityEngine;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// 合成した効果音を WAV に書き出す（耳での確認用）。batchmode から:
    ///   tools/RunUnity.ps1 -LogName audio.log -Extra '-executeMethod','Edgewise.EditorTools.AudioExport.Run'
    /// 出力: Logs/shots/3b/audio/*.wav（16bit モノラル 44.1kHz）。flip_sequence.wav は 1 手で 8 枚裏返したときの音程の上がり方。
    /// 書き出す音は設定の音量 60% × JuiceSettings の種類ごとの音量を掛けた、実際に鳴る大きさ。
    /// </summary>
    public static class AudioExport
    {
        public const string OutDir = "Logs/shots/3b/audio";

        [MenuItem("Edgewise/Export Sounds (WAV)")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var juice = AssetDatabase.LoadAssetAtPath<Edgewise.Game.Juice.JuiceSettings>(ArtBuilder.JuicePath);
            if (juice == null) juice = Edgewise.Game.Juice.JuiceSettings.CreateDefault();
            foreach (var kind in SfxSynth.All)
            {
                var data = SfxSynth.Generate(kind);
                float gain = juice.defaultSfxVolume * SfxBank.KindVolume(juice, kind);
                File.WriteAllBytes(Path.Combine(OutDir, kind.ToString().ToLowerInvariant() + ".wav"), SfxSynth.ToWav(data, gain));
            }
            // 裏返しの音程: 同じ手の中で 1 枚ごとに半音上げる（0, 1, 2, ... 半音）
            var flip = SfxSynth.Generate(SfxKind.Flip);
            int step = (int)(0.13f * SfxSynth.SampleRate);
            var seq = new float[step * 8 + flip.Length];
            for (int i = 0; i < 8; i++)
            {
                var p = SfxSynth.Repitch(flip, Edgewise.Game.Juice.JuiceFx.FlipSemitones(i, juice));
                for (int k = 0; k < p.Length && i * step + k < seq.Length; k++) seq[i * step + k] += p[k];
            }
            File.WriteAllBytes(Path.Combine(OutDir, "flip_sequence.wav"), SfxSynth.ToWav(seq, juice.defaultSfxVolume * juice.volFlip));
            Debug.Log("[Edgewise] Exported " + (SfxSynth.All.Length + 1) + " sounds to " + Path.GetFullPath(OutDir));
        }
    }
}
