// uGUI で使うカード絵・立ち絵（5b-2）。ArtGraphic が 1 つのメッシュに「本体」「星」「線」の四角形を並べ、頂点の値で描き分ける
// （同じマテリアル・同じテクスチャなので、小さなカードが何十枚あっても 1 回の描画にまとまる）。
// テクスチャ（_MainTex）はカード絵の SDF アトラス（CardArtAtlas。R8。値 0.5 が輪郭、大きいほど内側）。
// 頂点の値:
//   uv0.xy = 四角形の中の位置（0..1）
//   uv1    = アトラスのセル（xy = 大きさ、zw = 左下）
//   uv2    = x: 種類（0 本体 / 1 星 / 2 線）、y: 配色（-1 なし、0..3 系統、4 = 色の明暗（立ち絵））、z: 形（0 角丸 / 1 円 / 2 柔らかい光）、w: 模様の種
//   uv3    = 本体: xy = 絵の中心（四角形の 0..1）、z = セルの大きさ（四角形の高さに対する比）、w = 縦横比（幅 / 高さ）
//            星: x = またたきの位相、y = 大きさの倍率
//   color  = 星雲と光の色（カード = 属性色、立ち絵 = 星域の色）。a は全体の不透明度（CanvasGroup も含む）
// UI/Default と同じく、ステンシル（Mask）と RectMask2D の切り抜き（UNITY_UI_CLIP_RECT）に対応する。
Shader "Edgewise/UIArt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card Art SDF Atlas (R8)", 2D) = "black" {}
        _ArtSpread ("SDF Spread (cell units)", Float) = 0.09375
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            sampler2D _MainTex;
            float _ArtSpread;
            float4 _ClipRect;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.uv2 = v.uv2;
                o.uv3 = v.uv3;
                return o;
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

            float4 over(float4 dst, float3 c, float a)
            {
                return float4(c * a, a) + dst * (1.0 - a);
            }

            float3 S(float3 c) { return pow(saturate(c), 2.2); }

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

            float fbm2(float2 q)
            {
                float s = 0.5 * vnoise(q);
                q = q * 2.03 + 17.1;
                s += 0.25 * vnoise(q);
                return s / 0.75;
            }

            // 系統（Card.shader と同じ色）と、立ち絵の「色の明暗」
            void palette(float id, float3 tint, out float3 top, out float3 bottom)
            {
                if (id < -0.5) { top = S(float3(0.92, 0.93, 0.98)); bottom = S(float3(0.52, 0.55, 0.66)); }
                else if (id < 0.5) { top = S(float3(1.00, 0.93, 0.74)); bottom = S(float3(0.88, 0.56, 0.28)); }
                else if (id < 1.5) { top = S(float3(0.88, 0.95, 1.00)); bottom = S(float3(0.40, 0.52, 0.72)); }
                else if (id < 2.5) { top = S(float3(0.95, 0.88, 1.00)); bottom = S(float3(0.56, 0.44, 0.94)); }
                else if (id < 3.5) { top = S(float3(0.80, 1.00, 0.86)); bottom = S(float3(0.24, 0.64, 0.50)); }
                else { top = lerp(tint, float3(1, 1, 1), 0.72); bottom = tint * 0.62; }
            }

            float4 body(v2f i)
            {
                float2 uv = i.uv0.xy;
                float aspect = max(i.uv3.w, 0.01);
                float2 p = float2((uv.x - 0.5) * aspect, uv.y - 0.5);
                float shape = i.uv2.z;
                float3 tint = i.color.rgb;
                float seed = i.uv2.w;
                float time = _Time.y;

                // 形: 0 角丸の四角 / 1 円 / 2 柔らかい光（大きな立ち絵。背景の星雲は中心から薄れる）
                float inside;
                float bgA = 1.0;
                if (shape < 0.5) inside = fill(sdRoundBox(p, float2(0.5 * aspect, 0.5), 0.07));
                else if (shape < 1.5) inside = fill(length(p) - 0.5);
                else
                {
                    inside = 1.0;
                    // 四角形の辺に届く前に消える（四角い縁が見えないように）
                    float rr = length(p);
                    bgA = smoothstep(0.49, 0.08, rr) * 0.9;
                }

                // 背景: 星雲（ゆっくり流れる）＋小さな星＋周辺減光
                float t = saturate(uv.y);
                float3 bg = lerp(S(float3(0.035, 0.045, 0.11)), S(float3(0.09, 0.11, 0.23)), t);
                float2 sp = p * 2.6 + seed * 13.7;
                float n1 = fbm2(sp + float2(time * 0.04, time * 0.025));
                float n2 = fbm2(sp * 1.8 - seed + float2(-time * 0.03, time * 0.045));
                float cloud = smoothstep(0.38, 0.86, n1 * 0.7 + n2 * 0.45);
                bg += tint * (0.02 + 0.16 * cloud) + lerp(tint, float3(1, 1, 1), 0.3) * smoothstep(0.55, 0.95, n2) * 0.04;
                float2 g = p * 26.0 + seed * 7.0;
                float2 gi = floor(g);
                float h = hash21(gi);
                float2 off = float2(hash21(gi + 7.3), hash21(gi + 3.1)) - 0.5;
                float sd = length(frac(g) - 0.5 - off * 0.6);
                float tw = 0.6 + 0.4 * sin(time * (1.3 + h * 2.0) + h * 40.0);
                bg += step(0.9, h) * exp(-sd * sd * 140.0) * tw * 0.35;
                bg *= 1.0 - 0.5 * smoothstep(0.32, 0.8, length(p / float2(max(aspect, 1.0), 1.0)));
                float4 acc = float4(0, 0, 0, 0);
                acc = over(acc, bg, inside * bgA);

                // シルエット（SDF）。中心と大きさは四角形の高さを 1 とした単位
                float scale = max(i.uv3.z, 0.01);
                float2 cuv = float2((uv.x - i.uv3.x) * aspect, uv.y - i.uv3.y) / scale + 0.5;
                float2 auv = saturate(cuv) * i.uv1.xy + i.uv1.zw;
                float sdf = tex2D(_MainTex, auv).r;
                float inCell = step(0.0, cuv.x) * step(cuv.x, 1.0) * step(0.0, cuv.y) * step(cuv.y, 1.0);
                float spread = _ArtSpread * scale;
                float dc = lerp(spread, (0.5 - sdf) * 2.0 * spread, inCell);
                float3 top, bottom;
                palette(i.uv2.y, tint, top, bottom);
                float glow = exp(-max(dc, 0.0) / (0.03 * scale + 0.004)) * (1.0 - smoothstep(spread * 0.55, spread * 0.95, dc));
                acc = over(acc, lerp(bottom, tint, 0.5) * 1.15, glow * 0.45 * step(0.0, dc) * inside);
                float sil = fill(dc) * inside;
                float3 col = lerp(bottom, top, saturate(cuv.y * 1.15 - 0.1));
                col = lerp(col, col * tint * 1.3, 0.15);
                col *= lerp(0.98, 0.76, saturate(-dc / (0.07 * scale)));
                float rim = exp(min(dc, 0.0) / (0.012 * scale + 0.002));
                col = lerp(col, top * 1.4, rim * 0.5);
                // 図形の内側の細かな星
                float2 sg = cuv * 30.0 + seed * 5.0;
                float2 si = floor(sg);
                float sh = hash21(si);
                float2 so = float2(hash21(si + 5.1), hash21(si + 9.7)) - 0.5;
                float sdist = length(frac(sg) - 0.5 - so * 0.55);
                float stw = 0.55 + 0.45 * sin(time * (1.7 + sh * 2.3) + sh * 50.0);
                col += float3(1.0, 0.98, 0.92) * step(0.80, sh) * exp(-sdist * sdist * 70.0) * stw * saturate(-dc / (0.014 * scale));
                acc = over(acc, col, sil * 0.95);
                return acc;
            }

            float4 star(v2f i)
            {
                float2 q = i.uv0.xy - 0.5;
                float r = length(q);
                float tw = 0.7 + 0.3 * sin(_Time.y * (2.0 + i.uv3.x * 1.5) + i.uv3.x * 6.2831853);
                float core = exp(-r * r / 0.006);
                float halo = 0.3 * exp(-r / 0.09);
                float glint = (exp(-abs(q.x) * 60.0) * exp(-abs(q.y) * 9.0) + exp(-abs(q.y) * 60.0) * exp(-abs(q.x) * 9.0)) * saturate(i.uv3.y - 0.9);
                float v = saturate((core + halo + glint * 0.7) * tw) * smoothstep(0.5, 0.35, r);
                float3 c = lerp(i.color.rgb, float3(1, 1, 1), 0.65);
                return float4(c * v, v);
            }

            float4 segment(v2f i)
            {
                float across = (i.uv0.y - 0.5) * 2.0;
                float a = exp(-across * across * 5.0) * 0.55;
                float3 c = lerp(i.color.rgb, float3(1, 1, 1), 0.55);
                return float4(c * a, a);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 acc;
                if (i.uv2.x < 0.5) acc = body(i);
                else if (i.uv2.x < 1.5) acc = star(i);
                else acc = segment(i);
                acc *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                acc *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return acc;
            }
            ENDCG
        }
    }
}
