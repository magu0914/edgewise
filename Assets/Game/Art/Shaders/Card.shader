// カード 1 枚（表・裏・枠・カード絵・影・予告の光）を 1 枚のクアッドで描く。
// クアッドはカード本体（1 x 1.25）より一回り大きく（_QuadSize）、外側に影と光を描く余白を持つ。
// 値は CardView が MaterialPropertyBlock で渡す。
// カード表面の絵（DESIGN §7-1。下から順に重ねる）:
//   1. 背景: 属性色の星雲（ノイズがゆっくり流れる）＋周辺減光
//   2. シルエット: SDF アトラス（CardArtAtlas）から塗る。系統ごとのグラデーション、内側の縁の光、外側のぼんやりした光、内側の細かな星
//   3. 星座: 輪郭上の星と、それを結ぶ細い線。星はまたたく
//   4. レベルごとの加工（_LevelFx。4b で実装。いまは口だけ）
//   5. 傾きと立体感（_Tilt。層ごとのずれ量は _TiltDepth。いまは 0 を渡している）
// ランクのプレートと数字は、どの絵の上でも一番読みやすい要素であり続けること（プレートは最後に濃く重ねる）。
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
        _ArtTex ("Card Art SDF Atlas (R8)", 2D) = "black" {}
        _ArtSpread ("SDF Spread (cell units)", Float) = 0.09375
        _ArtRect ("Art Cell UV (scale xy, offset zw)", Vector) = (0, 0, 0, 0)
        _ArtCount ("Art Counts (stars, lines, has art)", Vector) = (0, 0, 0, 0)
        _Tribe ("Tribe (-1 none, 0 beast, 1 machine, 2 spirit, 3 dragon)", Float) = -1
        _LevelFx ("Level Effect Strength (4b)", Float) = 0
        _Tilt ("Tilt (xy, -1..1)", Vector) = (0, 0, 0, 0)
        _TiltDepth ("Tilt Depth per Layer (bg, silhouette, stars)", Vector) = (0.012, 0.03, 0.05, 0)
        _EmblemRot ("Emblem Rotation (rad)", Float) = 0
        _Level ("Level", Float) = 1
        _Glow ("Preview Glow", Float) = 0
        _Select ("Select", Float) = 0
        _Alpha ("Alpha", Float) = 1
        _FaceDown ("Face Down", Float) = 0
        _Unknown ("Unknown (dark silhouette only)", Float) = 0
        _Lift ("Lift", Float) = 0
        _Flash ("Flash", Float) = 0
        _Pivot ("Pivot Turns (0..3)", Float) = 0
        _MarkDir ("Capture Mark Direction (xy, 0 = none)", Vector) = (0, 0, 0, 0)
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
            TEXTURE2D(_ArtTex);
            SAMPLER(sampler_ArtTex);
            float _ArtSpread;
            float4 _ArtRect;
            float4 _ArtCount;
            float _Tribe;
            float _LevelFx;
            float4 _Tilt;
            float4 _TiltDepth;
            float _EmblemRot;
            float _Level;
            float _Glow;
            float _Select;
            float _Alpha;
            float _FaceDown;
            float _Unknown;
            float _Lift;
            float _Flash;
            float _Pivot;
            float4 _MarkDir;
            float4 _QuadSize;
            float4 _ArtStars[14];
            float4 _ArtLines[14];

            // カード絵の置き場所（カードの座標。カードは 1 x 1.25、中心が原点）。中央からやや下（DESIGN §7-1）
            static const float2 ART_C = float2(0.035, -0.07);
            // セル（128px）1 辺がカード上で占める大きさ
            static const float ART_S = 0.80;

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

            float hash21(float2 q)
            {
                q = frac(q * float2(123.34, 456.21));
                q += dot(q, q + 45.32);
                return frac(q.x * q.y);
            }

            float vnoise(float2 q)
            {
                float2 i = floor(q);
                float2 f = frac(q);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float e = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, e, u.x), u.y);
            }

            float fbm3(float2 q)
            {
                float s = 0.5 * vnoise(q);
                q = q * 2.03 + 17.1;
                s += 0.25 * vnoise(q);
                q = q * 2.01 + 31.7;
                s += 0.125 * vnoise(q);
                return s / 0.875;
            }

            // 系統ごとのシルエットのグラデーション（上の色・下の色。sRGB で書く）
            void tribeColors(out float3 top, out float3 bottom)
            {
                if (_Tribe < -0.5) { top = float3(0.92, 0.93, 0.98); bottom = float3(0.52, 0.55, 0.66); }
                else if (_Tribe < 0.5) { top = float3(1.00, 0.93, 0.74); bottom = float3(0.88, 0.56, 0.28); }      // 獣: 琥珀
                else if (_Tribe < 1.5) { top = float3(0.88, 0.95, 1.00); bottom = float3(0.40, 0.52, 0.72); }      // 機: 鋼
                else if (_Tribe < 2.5) { top = float3(0.95, 0.88, 1.00); bottom = float3(0.56, 0.44, 0.94); }      // 霊: 菫
                else { top = float3(0.80, 1.00, 0.86); bottom = float3(0.24, 0.64, 0.50); }                        // 竜: 翡翠
            }

            // 1. 背景: 属性色の星雲（ゆっくり流れる）＋かすかな星＋周辺減光
            float4 nebula(float2 p, float4 acc, float inside)
            {
                float3 owner = _OwnerColor.rgb;
                float3 elem = _ElemColor.rgb;
                float t = saturate(p.y / 1.25 + 0.5);
                float3 bg = lerp(S(float3(0.035, 0.045, 0.11)), S(float3(0.09, 0.11, 0.23)), t) + owner * 0.035;
                float2 seed = _ArtRect.zw * 41.0;
                float2 bp = p + _Tilt.xy * _TiltDepth.x;
                float time = _Time.y;
                float n1 = fbm3(bp * 3.1 + seed + float2(time * 0.045, time * 0.028));
                float n2 = fbm3(bp * 5.7 - seed.yx + float2(-time * 0.03, time * 0.05));
                float cloud = smoothstep(0.38, 0.86, n1 * 0.7 + n2 * 0.45);
                float wisp = smoothstep(0.55, 0.95, n2);
                bg += elem * (0.015 + 0.11 * cloud) + lerp(elem, float3(1, 1, 1), 0.3) * wisp * 0.03;
                // 遠くの小さな星
                float2 g = bp * 34.0 + seed;
                float2 gi = floor(g);
                float h = hash21(gi);
                float2 off = float2(hash21(gi + 7.3), hash21(gi + 3.1)) - 0.5;
                float sd = length(frac(g) - 0.5 - off * 0.6);
                float tw = 0.6 + 0.4 * sin(time * (1.3 + h * 2.0) + h * 40.0);
                bg += step(0.9, h) * exp(-sd * sd * 140.0) * tw * 0.35;
                // 周辺減光
                float vig = smoothstep(0.30, 0.78, length(p * float2(1.0, 0.82)));
                bg *= 1.0 - 0.55 * vig;
                return over(acc, bg, inside);
            }

            // ---- 4. レベルごとの加工（_LevelFx: 0 素朴 / 1 金の縁 / 2 ホロ / 3 オーロラ。CardLevelFx.TierOf）----

            float3 hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 0.6666667, 0.3333333)) * 6.0 - 3.0) - 1.0);
            }

            // ランクのプレートと名前の帯から離れた所だけ 1（光沢・粒をかけない範囲）
            float fxMaskAt(float2 p, float plate)
            {
                return smoothstep(0.0, 0.04, plate) * smoothstep(-0.43, -0.39, p.y);
            }

            // ホロ: 斜めの虹色の帯が、傾きに連動して流れる（傾けないときもゆっくり動く）。シルエットに強く、背景に弱く
            float4 holo(float2 p, float4 acc, float mask, float sil)
            {
                float time = _Time.y;
                float2 tl = _Tilt.xy;
                float phase = dot(p, float2(0.9, 1.4)) * 1.6 + tl.x * 0.9 - tl.y * 0.7 + time * 0.035;
                float3 rainbow = S(hue(frac(phase)));
                float wave = (p.x * 1.6 - p.y * 1.0) * 7.0 + tl.x * 6.0 + tl.y * 4.5 + time * 0.35;
                float band = pow(0.5 + 0.5 * sin(wave), 4.0);
                float strength = (0.05 + 0.30 * band) * (0.75 + 0.9 * saturate(length(tl)));
                float k = lerp(0.35, 1.0, sil);
                acc.rgb += rainbow * strength * k * mask * acc.a;
                // 光沢の筋（白いハイライト）
                acc.rgb += float3(1, 1, 1) * pow(band, 6.0) * 0.22 * k * mask * acc.a;
                return acc;
            }

            // オーロラ（背景の上、シルエットの下）: 揺れるカーテン
            float4 aurora(float2 p, float4 acc, float mask)
            {
                float time = _Time.y;
                float2 ap = p + _Tilt.xy * _TiltDepth.x * 1.5;
                float n = fbm3(float2(ap.x * 2.0 + time * 0.07, time * 0.045 + 3.0));
                float centerY = 0.20 + (n - 0.5) * 0.34 + sin(ap.x * 3.0 + time * 0.25) * 0.06;
                float dy = ap.y - centerY;
                float curtain = exp(-dy * dy / 0.028) * smoothstep(-0.30, 0.10, dy + 0.16);
                float rays = 0.55 + 0.45 * sin(ap.x * 38.0 + n * 9.0 + time * 0.9);
                float3 c1 = S(float3(0.30, 1.00, 0.70));
                float3 c2 = S(float3(0.62, 0.42, 1.00));
                float3 col = lerp(c1, c2, saturate(ap.x * 0.9 + 0.5 + (n - 0.5)));
                acc.rgb += col * curtain * rays * 0.85 * mask * acc.a;
                return acc;
            }

            // きらめきの粒: ゆっくり昇る小さな光（十字のきらめきつき）
            float4 sparkles(float2 p, float4 acc, float mask)
            {
                float time = _Time.y;
                float2 g = float2(p.x * 9.0, p.y * 7.0 - time * 0.22) + _Tilt.xy * 0.35;
                float2 gi = floor(g);
                float h = hash21(gi + 11.0);
                float2 off = float2(hash21(gi + 2.7), hash21(gi + 8.3)) - 0.5;
                float2 dv = (frac(g) - 0.5 - off * 0.6) / float2(9.0, 7.0);
                float tw = pow(0.5 + 0.5 * sin(time * (2.2 + h * 3.0) + h * 30.0), 3.0);
                float core = exp(-dot(dv, dv) / 0.000018);
                float glint4 = exp(-abs(dv.x) * 900.0) * exp(-abs(dv.y) * 140.0) + exp(-abs(dv.y) * 900.0) * exp(-abs(dv.x) * 140.0);
                float s = step(0.62, h) * (core + glint4 * 0.8) * tw;
                acc.rgb += float3(1.0, 0.95, 0.80) * s * 1.6 * mask * acc.a;
                return acc;
            }

            // 金の縁（Lv4 以上）: 枠の内側の細い金の線と、四隅の菱形
            float4 goldTrim(float2 p, float4 acc, float inside, float d)
            {
                float3 gold = S(float3(1.0, 0.84, 0.46)) * 1.35;
                float line1 = saturate(1.0 - abs(d + 0.064) / 0.0055);
                acc = over(acc, gold, line1 * inside * 0.95);
                float2 c = abs(p) - float2(0.5 - 0.064, 0.625 - 0.064);
                float diamond = abs(c.x) + abs(c.y) - 0.026;
                acc = over(acc, gold * 1.2, fill(diamond) * inside);
                return acc;
            }

            // カード絵のセル内の座標（0..1）。Pivot の回転と、傾きの層ずれを含める
            float2 artUv(float2 p, float depth)
            {
                // 浮いている層ほどカーソルの方（傾いて奥へ沈む側）へずれて見える
                float2 q = p - ART_C - _Tilt.xy * depth;
                q = rot(q, _EmblemRot);
                return q / ART_S + 0.5;
            }

            // 2. シルエット ＋ 3. 星座
            float4 cardArt(float2 p, float4 acc, float inside, float fxMask)
            {
                if (_ArtCount.z < 0.5) return acc;
                float time = _Time.y;
                float3 elem = _ElemColor.rgb;
                float spreadCard = _ArtSpread * ART_S;

                // ---- 2. シルエット ----
                float2 cuv = artUv(p, _TiltDepth.y);
                float2 auv = saturate(cuv) * _ArtRect.xy + _ArtRect.zw;
                float sdf = SAMPLE_TEXTURE2D(_ArtTex, sampler_ArtTex, auv).r;
                float inCell = step(0.0, cuv.x) * step(cuv.x, 1.0) * step(0.0, cuv.y) * step(cuv.y, 1.0);
                // カードの単位での符号つき距離（+ が外側）。セルの外は「遠い外側」
                float dc = lerp(spreadCard, (0.5 - sdf) * 2.0 * spreadCard, inCell);

                float3 top, bottom;
                tribeColors(top, bottom);
                top = S(top);
                bottom = S(bottom);

                // 外側のぼんやりした光（SDF の届く範囲の端で 0 に落とす）
                float glow = exp(-max(dc, 0.0) / 0.022) * (1.0 - smoothstep(spreadCard * 0.55, spreadCard * 0.95, dc));
                float3 glowCol = lerp(bottom, elem, 0.5) * 1.15;
                acc = over(acc, glowCol, glow * 0.42 * step(0.0, dc) * inside);

                // 本体: 系統のグラデーション（上ほど明るい）。中心に向かって少し沈め、縁を光らせる
                float sil = fill(dc) * inside;
                float gy = saturate(cuv.y * 1.15 - 0.1);
                float3 body = lerp(bottom, top, gy);
                body = lerp(body, body * elem * 1.3, 0.18);
                float depthIn = saturate(-dc / 0.055);
                body *= lerp(0.98, 0.74, depthIn);
                float rim = exp(min(dc, 0.0) / 0.010);
                body = lerp(body, top * 1.45, rim * 0.55);

                // 図形の内側にちりばめた細かな星（星図の世界観）
                float2 sg = cuv * 30.0 + _ArtRect.zw * 13.0;
                float2 si = floor(sg);
                float sh = hash21(si);
                float2 so = float2(hash21(si + 5.1), hash21(si + 9.7)) - 0.5;
                float sdist = length(frac(sg) - 0.5 - so * 0.55);
                float stw = 0.55 + 0.45 * sin(time * (1.7 + sh * 2.3) + sh * 50.0);
                float sprinkle = step(0.80, sh) * exp(-sdist * sdist * 70.0) * stw * saturate(-dc / 0.012);
                body += float3(1.0, 0.98, 0.92) * sprinkle * 0.9;

                acc = over(acc, body, sil * 0.94);

                // 4. レベルごとの加工: Lv7〜9 のホロ（シルエットに強く、背景に弱く。ランクのプレートと名前の帯にはかけない）
                if (_LevelFx > 1.5 && _LevelFx < 2.5) acc = holo(p, acc, inside * fxMask, sil);

                // ---- 3. 星座 ----
                float2 suv = artUv(p, _TiltDepth.z);
                int ns = (int)_ArtCount.x;
                int nl = (int)_ArtCount.y;
                float lineV = 0.0;
                [loop] for (int l = 0; l < 14; l++)
                {
                    if (l < nl)
                    {
                        float dl = sdSegment(suv, _ArtLines[l].xy, _ArtLines[l].zw) * ART_S;
                        lineV = max(lineV, saturate(1.0 - dl / 0.0042));
                    }
                }
                float3 lineCol = lerp(elem, float3(1, 1, 1), 0.55);
                acc = over(acc, lineCol * 1.1, lineV * 0.55 * inside);

                float starV = 0.0;
                [loop] for (int k = 0; k < 14; k++)
                {
                    if (k < ns)
                    {
                        float4 st = _ArtStars[k];
                        float2 dv = (suv - st.xy) * ART_S;
                        float r = length(dv);
                        float sz = 0.0085 * st.z;
                        float tw = 0.72 + 0.28 * sin(time * (2.0 + st.w * 1.5) + st.w * 6.2831853);
                        float core = exp(-r * r / (sz * sz));
                        float halo = 0.35 * exp(-r / (sz * 1.6));
                        float glint = (exp(-abs(dv.x) * 260.0) * exp(-abs(dv.y) * 55.0) + exp(-abs(dv.y) * 260.0) * exp(-abs(dv.x) * 55.0))
                            * saturate(st.z - 1.0);
                        starV += (core + halo + glint * 0.8) * tw;
                    }
                }
                float3 starCol = lerp(elem, float3(1, 1, 1), 0.6) * 1.9;
                acc = over(acc, starCol, saturate(starV) * inside);
                return acc;
            }

            float4 face(float2 p, float4 acc, float inside, float d)
            {
                float3 owner = _OwnerColor.rgb;
                float plate = sdRoundBox(p - float2(-0.235, 0.32), float2(0.205, 0.228), 0.07);
                float fxMask = fxMaskAt(p, plate);
                acc = nebula(p, acc, inside);
                if (_LevelFx > 2.5) acc = aurora(p, acc, inside * fxMask);
                acc = cardArt(p, acc, inside, fxMask);

                // 名前の帯
                float band = fill(p.y + 0.43) * inside;
                acc = over(acc, S(float3(0.02, 0.025, 0.06)), band * 0.72);

                // 枠: プレイヤーは実線（内側にもう 1 本）、CPU は斜線模様
                float frameW = 0.052;
                float inFrame = inside * (1.0 - fill(d + frameW));
                float stripes = step(0.5, frac((p.x - p.y) * 8.0));
                float3 hatch = lerp(owner * 0.10, owner, stripes);
                float3 frameCol = lerp(owner, hatch, _Pattern);
                acc = over(acc, frameCol, inFrame);
                float innerLine = (1.0 - _Pattern) * inside * saturate(1.0 - abs(d + 0.082) / 0.009);
                acc = over(acc, owner, innerLine * 0.9);
                if (_LevelFx > 0.5) acc = goldTrim(p, acc, inside, d);

                // 属性・系統のバッジ（丸）
                float de = length(p - float2(0.335, 0.47)) - 0.092;
                acc = over(acc, _ElemColor.rgb * 0.16, fill(de) * _ElemOn);
                acc = over(acc, _ElemColor.rgb, saturate(1.0 - abs(de) / 0.012) * _ElemOn);
                float dt2 = length(p - float2(0.335, 0.255)) - 0.078;
                acc = over(acc, S(float3(0.12, 0.13, 0.20)), fill(dt2) * _TribeOn);
                acc = over(acc, S(float3(0.62, 0.66, 0.80)), saturate(1.0 - abs(dt2) / 0.010) * _TribeOn);

                // Pivot で回したカードは、回した角度ぶんの金の弧（上から時計回り）と先端の点を出す
                if (_Pivot > 0.5)
                {
                    float2 q = p - ART_C;
                    float R = 0.335;
                    float ang = atan2(q.y, q.x);
                    float tcw = fmod(1.5707963 - ang + 6.2831853, 6.2831853);
                    float sweep = _Pivot * 1.5707963;
                    float arc = saturate(1.0 - abs(length(q) - R) / 0.014) * step(tcw, sweep);
                    float e = 1.5707963 - sweep;
                    float2 tipPos = R * float2(cos(e), sin(e));
                    float tip = 1.0 - smoothstep(0.022, 0.034, length(q - tipPos));
                    acc = over(acc, S(float3(1.0, 0.85, 0.45)) * 1.7, saturate(arc + tip) * inside);
                }

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

                // ランクの下地: 濃いプレート（絵の上でも数字が一番読めるように、まわりを少し暗くしてから濃く塗る。縁だけ所有者の色）
                if (_LevelFx > 2.5) acc = sparkles(p, acc, inside * fxMask);
                acc = over(acc, float3(0, 0, 0), exp(-max(plate, 0.0) / 0.025) * 0.45 * step(0.0, plate) * inside);
                acc = over(acc, S(float3(0.012, 0.014, 0.03)), fill(plate) * 0.95);
                acc = over(acc, owner * 0.75, saturate(1.0 - abs(plate) / 0.007) * 0.55);
                return acc;
            }

            // 未入手のカード（図鑑の収集表示。5b-2）: 暗い背景に、暗いシルエットだけ。数字・名前・星座・レベル加工は出さない
            float4 unknownFace(float2 p, float4 acc, float inside, float d)
            {
                float t = saturate(p.y / 1.25 + 0.5);
                float3 bg = lerp(S(float3(0.025, 0.03, 0.06)), S(float3(0.05, 0.06, 0.11)), t);
                bg *= 1.0 - 0.5 * smoothstep(0.30, 0.78, length(p * float2(1.0, 0.82)));
                acc = over(acc, bg, inside);
                if (_ArtCount.z > 0.5)
                {
                    float spreadCard = _ArtSpread * ART_S;
                    float2 cuv = artUv(p, _TiltDepth.y);
                    float2 auv = saturate(cuv) * _ArtRect.xy + _ArtRect.zw;
                    float sdf = SAMPLE_TEXTURE2D(_ArtTex, sampler_ArtTex, auv).r;
                    float inCell = step(0.0, cuv.x) * step(cuv.x, 1.0) * step(0.0, cuv.y) * step(cuv.y, 1.0);
                    float dc = lerp(spreadCard, (0.5 - sdf) * 2.0 * spreadCard, inCell);
                    float sil = fill(dc) * inside;
                    float3 body = lerp(S(float3(0.10, 0.11, 0.17)), S(float3(0.17, 0.19, 0.27)), saturate(cuv.y));
                    acc = over(acc, body, sil);
                    float rim = saturate(1.0 - abs(dc) / 0.006) * inside;
                    acc = over(acc, S(float3(0.30, 0.33, 0.45)), rim * 0.55);
                }
                float inFrame = inside * (1.0 - fill(d + 0.04));
                acc = over(acc, S(float3(0.20, 0.22, 0.30)), inFrame);
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
                else if (_Unknown > 0.5) acc = unknownFace(p, acc, inside, d);
                else acc = face(p, acc, inside, d);

                // 予告: 取れる予定のカードには「取れる」印（金の円の中に、置く位置からの向きの矢印）。所有者の色は変えない
                if (dot(_MarkDir.xy, _MarkDir.xy) > 0.01)
                {
                    float2 mc = float2(0.04, -0.035);
                    float2 m = p - mc;
                    float disc = length(m) - 0.15;
                    acc = over(acc, S(float3(0.03, 0.03, 0.06)), fill(disc) * 0.85);
                    acc = over(acc, S(float3(1.0, 0.85, 0.45)) * (1.2 + 0.8 * pulse), saturate(1.0 - abs(disc) / 0.012));
                    float2 dir = normalize(_MarkDir.xy);
                    float2 lp = float2(dot(m, dir), dot(m, float2(-dir.y, dir.x)));
                    float arrow = sdSegment(lp, float2(-0.075, 0.0), float2(0.07, 0.0));
                    arrow = min(arrow, sdSegment(lp, float2(0.08, 0.0), float2(0.02, 0.055)));
                    arrow = min(arrow, sdSegment(lp, float2(0.08, 0.0), float2(0.02, -0.055)));
                    acc = over(acc, S(float3(1.0, 0.9, 0.55)) * (1.4 + 0.6 * pulse), fill(arrow - 0.017));
                }
                // 着地・裏返しの一瞬の白い光
                acc = over(acc, float3(1.6, 1.6, 1.6), inside * saturate(_Flash) * 0.6);

                return acc * saturate(_Alpha);
            }
            ENDHLSL
        }
    }
}
