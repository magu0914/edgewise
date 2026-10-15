// 背景の星空（DESIGN §8-1: 濃紺の星空、ゆっくり瞬く星）。テクスチャを使わずハッシュで星を置く。
Shader "Edgewise/Starfield"
{
    Properties
    {
        _QuadSize ("Quad Size (world)", Vector) = (40, 24, 0, 0)
        _Top ("Top Color", Color) = (0.05, 0.06, 0.16, 1)
        _Bottom ("Bottom Color", Color) = (0.015, 0.02, 0.055, 1)
        _Density ("Star Density", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _QuadSize;
            float4 _Top;
            float4 _Bottom;
            float _Density;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 p : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.p = v.positionOS.xy * _QuadSize.xy;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    v += a * noise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            float starLayer(float2 p, float cellSize, float threshold, float time, float seed)
            {
                float2 g = p / cellSize;
                float2 cell = floor(g);
                float2 f = frac(g);
                float h = hash21(cell + seed);
                if (h < threshold) return 0.0;
                float2 pos = float2(hash21(cell + seed + 3.1), hash21(cell + seed + 7.7)) * 0.7 + 0.15;
                float d = length((f - pos) * cellSize);
                float size = lerp(0.012, 0.035, hash21(cell + seed + 11.3));
                float twinkle = 0.55 + 0.45 * sin(time * (0.6 + 2.2 * hash21(cell + 5.0)) + h * 40.0);
                float core = exp(-d * d / (size * size));
                // 光のにじみはマスの中で 0 まで落とす（マスの境目で切れて四角く見えないように）
                float halo = exp(-d / (size * 3.0)) * 0.25 * saturate(1.0 - d / (cellSize * 0.14));
                return (core + halo) * twinkle * (0.5 + h);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.p;
                float3 col = lerp(_Bottom.rgb, _Top.rgb, saturate(i.uv.y));

                // 星雲（ゆっくり流れる）
                float n = fbm(p * 0.12 + float2(t * 0.004, t * 0.002));
                float n2 = fbm(p * 0.21 - float2(t * 0.003, 0.0) + 9.0);
                col += float3(0.10, 0.05, 0.18) * smoothstep(0.45, 0.85, n) * 0.55;
                col += float3(0.02, 0.10, 0.14) * smoothstep(0.5, 0.9, n2) * 0.5;

                // 星（3 層。遠い層ほど細かくゆっくり流れる）
                float s = 0.0;
                s += starLayer(p + float2(t * 0.010, 0.0), 0.55, 1.0 - 0.30 * _Density, t, 1.0) * 0.55;
                s += starLayer(p + float2(t * 0.020, 0.0), 1.10, 1.0 - 0.35 * _Density, t, 2.0) * 0.85;
                s += starLayer(p + float2(t * 0.035, 0.0), 2.30, 1.0 - 0.45 * _Density, t, 3.0) * 1.3;
                col += float3(0.85, 0.90, 1.0) * s;

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
