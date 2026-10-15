using System;
using System.IO;
using UnityEngine;

namespace Edgewise.Game.Audio
{
    /// <summary>効果音の種類（DESIGN §8-1 サウンド）。</summary>
    public enum SfxKind
    {
        Cursor,
        Pick,
        Place,
        Flip,
        Same,
        Plus,
        Combo,
        Win,
        Lose,
        Draw,
        Button,
    }

    /// <summary>
    /// 効果音の合成（正弦波・三角波・ノイズ＋エンベロープ）。外部の音素材は使わない。
    /// 耳に刺さらないように、矩形波や高い倍音は使わず、最大でも 1.6 kHz 程度の正弦波・三角波にし、
    /// 立ち上がりと終わりを数 ms なめらかにしてクリックを防ぐ。ピークは 0.45 前後にそろえる（実際の音量は設定 × JuiceSettings）。
    /// Unity に依存しない計算（Mathf だけ）なので、Editor から WAV に書き出して確認できる。
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = 44100;
        const float TwoPi = Mathf.PI * 2f;

        public static readonly SfxKind[] All = (SfxKind[])Enum.GetValues(typeof(SfxKind));

        public static float[] Generate(SfxKind kind)
        {
            switch (kind)
            {
                case SfxKind.Cursor: return Cursor();
                case SfxKind.Pick: return Pick();
                case SfxKind.Place: return Place();
                case SfxKind.Flip: return Flip();
                case SfxKind.Same: return Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.06f, 0.24f, 0.75f, 0.45f);
                case SfxKind.Plus: return Arpeggio(new[] { 587.33f, 880f, 1174.66f }, 0.07f, 0.26f, 0.75f, 0.45f);
                case SfxKind.Combo: return Combo();
                case SfxKind.Win: return Fanfare(new[] { 261.63f, 329.63f, 392f, 523.25f }, 0.11f, 1.4f, 0.45f);
                case SfxKind.Lose: return Lose();
                case SfxKind.Draw: return Draw();
                case SfxKind.Button: return Button();
                default: return new float[1];
            }
        }

        // ------------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------------
        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

        static float Env(float t, float attack, float decay) => t < 0f ? 0f : t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static float Tri(float phase) => 2f / Mathf.PI * Mathf.Asin(Mathf.Sin(phase));

        /// <summary>一定の周波数の音を足す（正弦波＋少しの三角波）。</summary>
        static void AddTone(float[] b, float freq, float start, float attack, float decay, float gain, float triMix = 0.25f)
        {
            int s0 = Mathf.Max(0, (int)(start * SampleRate));
            for (int i = s0; i < b.Length; i++)
            {
                float t = (i - s0) / (float)SampleRate;
                float e = Env(t, attack, decay);
                if (t > attack && e < 0.0005f) break;
                float ph = TwoPi * freq * t;
                b[i] += gain * e * ((1f - triMix) * Mathf.Sin(ph) + triMix * Tri(ph));
            }
        }

        /// <summary>周波数が f0 から f1 へ指数的に変わる正弦波を足す。</summary>
        static void AddSweep(float[] b, float f0, float f1, float sweepTime, float start, float attack, float decay, float gain)
        {
            int s0 = Mathf.Max(0, (int)(start * SampleRate));
            float phase = 0f;
            for (int i = s0; i < b.Length; i++)
            {
                float t = (i - s0) / (float)SampleRate;
                float e = Env(t, attack, decay);
                if (t > attack && e < 0.0005f) break;
                float k = Mathf.Clamp01(t / sweepTime);
                float f = f0 * Mathf.Pow(f1 / f0, k);
                phase += TwoPi * f / SampleRate;
                b[i] += gain * e * Mathf.Sin(phase);
            }
        }

        /// <summary>ローパスをかけたノイズ（alpha が小さいほど柔らかい）を足す。乱数は固定 seed（毎回同じ音）。</summary>
        static void AddNoise(float[] b, float start, float attack, float decay, float gain, float alpha, uint seed)
        {
            int s0 = Mathf.Max(0, (int)(start * SampleRate));
            float lp = 0f;
            uint x = seed == 0 ? 1u : seed;
            for (int i = s0; i < b.Length; i++)
            {
                float t = (i - s0) / (float)SampleRate;
                float e = Env(t, attack, decay);
                if (t > attack && e < 0.0005f) break;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                float n = (x & 0xffff) / 32767.5f - 1f;
                lp += alpha * (n - lp);
                b[i] += gain * e * lp;
            }
        }

        /// <summary>ピークを target にそろえ、頭と尻をなめらかにする。</summary>
        static float[] Finish(float[] b, float target)
        {
            float peak = 0f;
            for (int i = 0; i < b.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(b[i]));
            float g = peak > 1e-6f ? target / peak : 0f;
            int fadeIn = (int)(0.002f * SampleRate);
            int fadeOut = (int)(0.012f * SampleRate);
            for (int i = 0; i < b.Length; i++)
            {
                float w = 1f;
                if (i < fadeIn) w = i / (float)fadeIn;
                int fromEnd = b.Length - 1 - i;
                if (fromEnd < fadeOut) w *= fromEnd / (float)fadeOut;
                b[i] *= g * w;
            }
            return b;
        }

        // ------------------------------------------------------------------
        // 各音
        // ------------------------------------------------------------------
        static float[] Cursor()
        {
            var b = Buffer(0.06f);
            AddTone(b, 659.25f, 0f, 0.002f, 0.014f, 1f, 0.5f);
            AddTone(b, 1318.5f, 0f, 0.002f, 0.008f, 0.12f, 0f);
            return Finish(b, 0.3f);
        }

        static float[] Pick()
        {
            var b = Buffer(0.12f);
            AddSweep(b, 330f, 560f, 0.06f, 0f, 0.004f, 0.03f, 1f);
            AddNoise(b, 0f, 0.002f, 0.015f, 0.25f, 0.08f, 11u);
            return Finish(b, 0.42f);
        }

        static float[] Place()
        {
            var b = Buffer(0.22f);
            AddSweep(b, 150f, 78f, 0.08f, 0f, 0.002f, 0.055f, 1f);
            AddNoise(b, 0f, 0.001f, 0.01f, 0.55f, 0.22f, 23u);
            AddTone(b, 415.3f, 0.002f, 0.002f, 0.028f, 0.28f, 0.6f);
            return Finish(b, 0.5f);
        }

        static float[] Flip()
        {
            var b = Buffer(0.3f);
            AddNoise(b, 0f, 0.03f, 0.04f, 0.35f, 0.12f, 37u);
            AddTone(b, 783.99f, 0.02f, 0.003f, 0.09f, 0.9f, 0.15f);
            AddTone(b, 1567.98f, 0.02f, 0.003f, 0.05f, 0.18f, 0f);
            AddTone(b, 392f, 0.02f, 0.003f, 0.07f, 0.3f, 0.5f);
            return Finish(b, 0.42f);
        }

        static float[] Arpeggio(float[] notes, float step, float decay, float length, float target)
        {
            var b = Buffer(length);
            for (int i = 0; i < notes.Length; i++)
            {
                AddTone(b, notes[i], i * step, 0.004f, decay, 1f, 0.3f);
                AddTone(b, notes[i] * 2f, i * step, 0.004f, decay * 0.4f, 0.12f, 0f);
            }
            return Finish(b, target);
        }

        static float[] Combo()
        {
            var b = Buffer(0.6f);
            AddSweep(b, 392f, 880f, 0.14f, 0f, 0.01f, 0.09f, 0.7f);
            AddTone(b, 783.99f, 0.14f, 0.004f, 0.28f, 0.8f, 0.3f);
            AddTone(b, 987.77f, 0.14f, 0.004f, 0.26f, 0.6f, 0.3f);
            AddTone(b, 1174.66f, 0.14f, 0.004f, 0.24f, 0.5f, 0.3f);
            return Finish(b, 0.45f);
        }

        static float[] Fanfare(float[] notes, float step, float length, float target)
        {
            var b = Buffer(length);
            for (int i = 0; i < notes.Length; i++) AddTone(b, notes[i], i * step, 0.006f, 0.18f, 0.8f, 0.4f);
            float chord = notes.Length * step;
            foreach (var f in notes) AddTone(b, f, chord, 0.02f, 0.45f, 0.5f, 0.35f);
            return Finish(b, target);
        }

        static float[] Lose()
        {
            var b = Buffer(1.3f);
            float[] notes = { 440f, 349.23f, 293.66f };
            for (int i = 0; i < notes.Length; i++) AddTone(b, notes[i], i * 0.22f, 0.01f, i == notes.Length - 1 ? 0.45f : 0.2f, 0.9f, 0.2f);
            AddTone(b, 146.83f, 0.44f, 0.03f, 0.5f, 0.35f, 0f);
            return Finish(b, 0.4f);
        }

        static float[] Draw()
        {
            var b = Buffer(0.95f);
            for (int k = 0; k < 2; k++)
            {
                AddTone(b, 392f, k * 0.3f, 0.008f, 0.22f, 0.8f, 0.3f);
                AddTone(b, 587.33f, k * 0.3f, 0.008f, 0.22f, 0.6f, 0.3f);
            }
            return Finish(b, 0.4f);
        }

        static float[] Button()
        {
            var b = Buffer(0.08f);
            AddTone(b, 523.25f, 0f, 0.002f, 0.02f, 1f, 0.3f);
            AddTone(b, 261.63f, 0f, 0.002f, 0.025f, 0.5f, 0f);
            return Finish(b, 0.32f);
        }

        // ------------------------------------------------------------------
        // WAV（確認用の書き出し）
        // ------------------------------------------------------------------

        /// <summary>音程を semitones 半音ずらした音（再生時の AudioSource.pitch と同じく、速さを変えて再標本化する）。</summary>
        public static float[] Repitch(float[] src, float semitones)
        {
            float r = Mathf.Pow(2f, semitones / 12f);
            int n = Mathf.Max(1, (int)(src.Length / r));
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                float p = i * r;
                int a = (int)p;
                float f = p - a;
                float x0 = a < src.Length ? src[a] : 0f;
                float x1 = a + 1 < src.Length ? src[a + 1] : 0f;
                dst[i] = x0 + (x1 - x0) * f;
            }
            return dst;
        }

        /// <summary>16bit PCM モノラルの WAV にする。</summary>
        public static byte[] ToWav(float[] samples, float gain = 1f)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(SampleRate);
                w.Write(SampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * gain * 32767f), -32768, 32767));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
