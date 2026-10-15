// カード 1 枚（表・裏・枠・星座エンブレム・影・予告の光）を 1 枚のクアッドで描く。画像素材は使わない。
// クアッドはカード本体（1 x 1.25）より一回り大きく（_QuadSize）、外側に影と光を描く余白を持つ。
// 値は CardView が MaterialPropertyBlock で渡す。
Shader "Edgewise/Card"
{
    Properties
    {
        _OwnerColor ("Owner Color", Color) = (0.22, 0.82, 0.78, 1)
        _ElemColor ("Element Color", Color) = (0.8, 0.82, 0.92, 1)
        _GlowColor ("Glow Color", Color) = (1, 1, 1, 1)
        _Pattern ("Frame Pattern (0 solid / 1 hatched)", Float) = 0
        _ElemOn ("Element Badge", Float) = 0
        _TribeOn ("Tribe Badge", Float) = 0
        _StarCount ("Star Count", Float) = 0
        _Branch ("Branch (from, to)", Vector) = (-1, -1, 0, 0)
        _EmblemRot ("Emblem Rotation (rad)", Float) = 0
        _Level ("Level", Float) = 1
        _Glow ("Preview Glow", Float) = 0
        _Select ("Select", Float) = 0
        _Alpha ("Alpha", Float) = 1
        _FaceDown ("Face Down", Float) = 0
        _Lift ("Lift", Float) = 0
        _Flash ("Flash", Float) = 0
        _Pivot ("Pivot Turns (0..3)", Float) = 0
        _QuadSize ("Quad Size", Vector) = (1.24, 1.49, 0, 0)
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

            float4 _OwnerColor;
            float4 _ElemColor;
            float4 _GlowColor;
            float _Pattern;
            float _ElemOn;
            float _TribeOn;
            float _StarCount;
            float4 _Branch;
            float _EmblemRot;
            float _Level;
            float _Glow;
            float _Select;
            float _Alpha;
            float _FaceDown;
            float _Lift;
            float _Flash;
            float _Pivot;
            float4 _QuadSize;
            float4 _Stars[7];

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            // d < 0 の内側で 1。境界は画面のピクセル幅でなめらかにする
            float fill(float d)
            {
                float w = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / w);
            }

            // 前乗算アルファで上に重ねる
            float4 over(float4 dst, float3 c, float a)
            {
                return float4(c * a, a) + dst * (1.0 - a);
            }

            // シェーダー内の定数色は sRGB で書き、リニア色空間に変換して使う
            float3 S(float3 c) { return pow(saturate(c), 2.2); }

            float2 rot(float2 v, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            float4 face(float2 p, float4 acc, float inside, float d)
            {
                float3 owner = _OwnerColor.rgb;
                float t = saturate(p.y / 1.25 + 0.5);
                float3 bg = lerp(S(float3(0.05, 0.065, 0.15)), S(float3(0.12, 0.15, 0.29)), t) + owner * 0.05;
                acc = over(acc, bg, inside);

                // 名前の帯
                float band = fill(p.y + 0.43) * inside;
                acc = over(acc, S(float3(0.02, 0.025, 0.06)), band * 0.6);

                // 枠: プレイヤーは実線（内側にもう 1 本）、CPU は斜線模様
                float frameW = 0.052;
                float inFrame = inside * (1.0 - fill(d + frameW));
                float stripes = step(0.5, frac((p.x - p.y) * 8.0));
                float3 hatch = lerp(owner * 0.10, owner, stripes);
                float3 frameCol = lerp(owner, hatch, _Pattern);
                acc = over(acc, frameCol, inFrame);
                float innerLine = (1.0 - _Pattern) * inside * saturate(1.0 - abs(d + 0.082) / 0.009);
                acc = over(acc, owner, innerLine * 0.9);

                // ランクの菱形の下地
                float2 rc = p - float2(-0.24, 0.355);
                float dd = (abs(rc.x) / 0.215 + abs(rc.y) / 0.24 - 1.0) * 0.16;
                acc = over(acc, S(float3(0.015, 0.02, 0.05)), fill(dd) * 0.78);
                float diaLine = saturate(1.0 - abs(dd) / 0.006);
                acc = over(acc, owner * 0.8, diaLine * 0.7);

                // 属性・系統のバッジ（丸）
                float de = length(p - float2(0.335, 0.47)) - 0.092;
                acc = over(acc, _ElemColor.rgb * 0.16, fill(de) * _ElemOn);
                acc = over(acc, _ElemColor.rgb, saturate(1.0 - abs(de) / 0.012) * _ElemOn);
                float dt2 = length(p - float2(0.335, 0.255)) - 0.078;
                acc = over(acc, S(float3(0.12, 0.13, 0.20)), fill(dt2) * _TribeOn);
                acc = over(acc, S(float3(0.62, 0.66, 0.80)), saturate(1.0 - abs(dt2) / 0.010) * _TribeOn);

                // 星座エンブレム（カード ID から生成した星の点と線。属性の色味）
                float2 ec = float2(0.04, -0.035);
                float scale = 0.29;
                float2 q = p - ec;
                float ring = saturate(1.0 - abs(length(q) - scale * 1.05) / 0.006);
                acc = over(acc, _ElemColor.rgb * 0.3, ring * 0.6);
                // Pivot で回したカードは、回した角度ぶんの金の弧（上から時計回り）と先端の点を出す
                if (_Pivot > 0.5)
                {
                    float R = scale * 1.05;
                    float ang = atan2(q.y, q.x);
                    float tcw = fmod(1.5707963 - ang + 6.2831853, 6.2831853);
                    float sweep = _Pivot * 1.5707963;
                    float arc = saturate(1.0 - abs(length(q) - R) / 0.014) * step(tcw, sweep);
                    float e = 1.5707963 - sweep;
                    float2 tipPos = R * float2(cos(e), sin(e));
                    float tip = 1.0 - smoothstep(0.022, 0.034, length(q - tipPos));
                    acc = over(acc, S(float3(1.0, 0.85, 0.45)) * 1.7, saturate(arc + tip) * inside);
                }
                int n = (int)_StarCount;
                float lineV = 0.0;
                float starV = 0.0;
                float2 prev = float2(0, 0);
                [unroll] for (int k = 0; k < 7; k++)
                {
                    if (k < n)
                    {
                        float2 s = rot(_Stars[k].xy, _EmblemRot) * scale;
                        float dist = length(q - s);
                        float tw = 0.85 + 0.15 * sin(_Time.y * 2.3 + k * 1.7);
                        float sz = _Stars[k].z * scale;
                        starV += (exp(-dist * dist / (sz * sz * 0.10)) + 0.35 * exp(-dist / (sz * 0.9))) * _Stars[k].w * tw;
                        if (k > 0) lineV = max(lineV, saturate(1.0 - sdSegment(q, prev, s) / 0.0075));
                        prev = s;
                    }
                }
                if (_Branch.x >= 0.0)
                {
                    int a = (int)_Branch.x;
                    int b = (int)_Branch.y;
                    float2 sa = float2(0, 0);
                    float2 sb = float2(0, 0);
                    [unroll] for (int j = 0; j < 7; j++)
                    {
                        if (j == a) sa = rot(_Stars[j].xy, _EmblemRot) * scale;
                        if (j == b) sb = rot(_Stars[j].xy, _EmblemRot) * scale;
                    }
                    lineV = max(lineV, saturate(1.0 - sdSegment(q, sa, sb) / 0.0075) * 0.8);
                }
                acc = over(acc, _ElemColor.rgb * 0.9, lineV * 0.55 * inside);
                float3 starCol = lerp(_ElemColor.rgb, float3(1, 1, 1), 0.45) * 1.9;
                acc = over(acc, starCol, saturate(starV) * inside);

                // レベル（星の数）
                int lv = (int)_Level;
                float spacing = 0.068;
                float x0 = -(lv - 1) * spacing * 0.5;
                float sparkle = 0.0;
                [unroll] for (int m = 0; m < 10; m++)
                {
                    if (m < lv)
                    {
                        float2 sp = p - float2(x0 + m * spacing, -0.375);
                        float r = length(sp);
                        float glint = exp(-abs(sp.x) * 90.0) * exp(-abs(sp.y) * 30.0) + exp(-abs(sp.y) * 90.0) * exp(-abs(sp.x) * 30.0);
                        sparkle += exp(-r * r * 3500.0) + glint * 0.6;
                    }
                }
                acc = over(acc, S(float3(1.0, 0.86, 0.52)) * 1.3, saturate(sparkle) * inside);
                return acc;
            }

            float4 back(float2 p, float4 acc, float inside, float d)
            {
                float t = saturate(p.y / 1.25 + 0.5);
                float3 bg = lerp(S(float3(0.06, 0.06, 0.14)), S(float3(0.14, 0.11, 0.27)), t);
                acc = over(acc, bg, inside);
                float lattice = step(0.92, frac((p.x + p.y) * 7.0)) + step(0.92, frac((p.x - p.y) * 7.0));
                acc = over(acc, S(float3(0.28, 0.24, 0.48)), saturate(lattice) * inside * 0.5);
                float3 gold = S(float3(0.95, 0.78, 0.42));
                float inFrame = inside * (1.0 - fill(d + 0.045));
                acc = over(acc, gold * 0.85, inFrame);
                float r = length(p);
                acc = over(acc, gold * 0.7, saturate(1.0 - abs(r - 0.30) / 0.008) * inside);
                acc = over(acc, gold * 0.5, saturate(1.0 - abs(r - 0.36) / 0.005) * inside);
                float ang = atan2(p.y, p.x);
                float star = r - 0.20 * (0.45 + 0.55 * pow(abs(cos(ang * 4.0)), 6.0));
                acc = over(acc, gold * 1.4, fill(star) * inside);
                return acc;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _QuadSize.xy;
                float2 halfSize = float2(0.5, 0.625);
                float d = sdRoundBox(p, halfSize, 0.07);
                float inside = fill(d);

                float4 acc = float4(0, 0, 0, 0);

                // 影（持ち上げると伸びて薄くなる）
                float2 so = float2(0.05, -0.08) * (0.35 + 1.4 * _Lift);
                float ds = sdRoundBox(p - so, halfSize, 0.09);
                float shadow = (1.0 - smoothstep(-0.03, 0.05 + 0.12 * _Lift, ds)) * (0.42 - 0.12 * _Lift);
                acc = over(acc, float3(0, 0, 0), shadow * (1.0 - inside));

                // 外側の光（予告の脈打つ光・選択中の光）
                float outside = step(0.0, d);
                float pulse = 0.55 + 0.45 * sin(_Time.y * 7.0);
                float glowA = exp(-max(d, 0.0) * 26.0) * outside;
                acc = over(acc, _GlowColor.rgb * 2.2, glowA * saturate(_Glow * pulse));
                acc = over(acc, S(float3(1.0, 0.85, 0.45)) * 1.6, glowA * saturate(_Select) * 0.85);

                if (_FaceDown > 0.5) acc = back(p, acc, inside, d);
                else acc = face(p, acc, inside, d);

                // 予告: 裏返る予定のカードの面もうっすら光らせる
                acc = over(acc, _GlowColor.rgb * 1.4, inside * saturate(_Glow * pulse) * 0.28);
                // 着地・裏返しの一瞬の白い光
                acc = over(acc, float3(1.6, 1.6, 1.6), inside * saturate(_Flash) * 0.6);

                return acc * saturate(_Alpha);
            }
            ENDHLSL
        }
    }
}
