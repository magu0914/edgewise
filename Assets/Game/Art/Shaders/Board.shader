// 盤面（DESIGN §8-1: 星図の羊皮紙風の 3×3 グリッド）。テクスチャを使わず手続き的に描く。
Shader "Edgewise/Board"
{
    Properties
    {
        _QuadSize ("Quad Size", Vector) = (3.5, 4.25, 0, 0)
        _Cell ("Cell Pitch", Vector) = (1.06, 1.31, 0, 0)
        _Paper ("Paper", Color) = (0.56, 0.48, 0.36, 1)
        _Ink ("Ink", Color) = (0.30, 0.20, 0.10, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
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
            float4 _Cell;
            float4 _Paper;
            float4 _Ink;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
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

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float fill(float d)
            {
                float w = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / w);
            }

            float lineAt(float d, float width)
            {
                float w = max(fwidth(d), 1e-4);
                return saturate(1.0 - (abs(d) - width) / w);
            }

            float4 over(float4 dst, float3 c, float a) { return float4(c * a, a) + dst * (1.0 - a); }

            // 定数色は sRGB で書き、リニア色空間に変換して使う
            float3 S(float3 c) { return pow(saturate(c), 2.2); }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float2 halfSize = _QuadSize.xy * 0.5;
                float d = sdRoundBox(p, halfSize - 0.02, 0.10);
                float inside = fill(d);

                float4 acc = float4(0, 0, 0, 0);
                // 盤の影
                float ds = sdRoundBox(p - float2(0.06, -0.10), halfSize, 0.14);
                acc = over(acc, float3(0, 0, 0), (1.0 - smoothstep(-0.05, 0.25, ds)) * 0.55 * (1.0 - inside));

                // 羊皮紙: むら・繊維・縁の焼け
                float n = noise(p * 3.0) * 0.6 + noise(p * 9.0) * 0.3 + noise(p * 31.0) * 0.1;
                float fiber = noise(float2(p.x * 40.0, p.y * 2.0)) * 0.05;
                float edge = smoothstep(-0.55, 0.0, d);
                float3 paper = _Paper.rgb * (0.82 + 0.28 * n + fiber) * (1.0 - 0.45 * edge);
                acc = over(acc, paper, inside);

                // 星図の飾り: 同心円と放射線（盤の左上の外にある中心から）
                float2 c = p - float2(-2.6, 3.2);
                float r = length(c);
                float rings = lineAt(frac(r * 1.25) - 0.5, 0.004);
                float ang = atan2(c.y, c.x);
                float rays = lineAt(frac(ang * 6.0 / 3.14159) - 0.5, 0.004) * step(1.0, r);
                acc = over(acc, _Ink.rgb, (rings * 0.16 + rays * 0.10) * inside);
                // 小さな星の点
                float2 g = floor(p * 5.0);
                float h = hash21(g);
                float2 sp = (frac(p * 5.0) - (0.2 + 0.6 * float2(hash21(g + 1.3), hash21(g + 2.7)))) / 5.0;
                float dots = step(0.82, h) * exp(-dot(sp, sp) * 9000.0);
                acc = over(acc, _Ink.rgb, saturate(dots) * 0.5 * inside);

                // マス: 少し沈んだくぼみと金のふち
                float2 cellHalf = _Cell.xy * 0.5 - 0.035;
                float slotD = 1e3;
                [unroll] for (int k = 0; k < 9; k++)
                {
                    float2 cc = float2((k % 3) - 1, 1 - (k / 3)) * _Cell.xy;
                    slotD = min(slotD, sdRoundBox(p - cc, cellHalf, 0.07));
                }
                acc = over(acc, _Ink.rgb * 0.5, fill(slotD) * 0.22 * inside);
                acc = over(acc, S(float3(0.62, 0.46, 0.22)), lineAt(slotD, 0.006) * 0.75 * inside);

                // 外枠（金の二重線）
                acc = over(acc, S(float3(0.80, 0.62, 0.30)), lineAt(d + 0.045, 0.012) * inside);
                acc = over(acc, S(float3(0.55, 0.40, 0.18)), lineAt(d + 0.085, 0.005) * inside);
                return acc;
            }
            ENDHLSL
        }
    }
}
