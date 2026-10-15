// 汎用の図形（角丸の面・枠・光の輪・光の線）。マスのカーソル、Same/Plus の光の線、着地の光の粒、属性マスの印に使う。
// _Mode: 0 = 角丸の面（_Softness でぼかす）、1 = 角丸の枠（_Thickness）、2 = 光の輪（半径 _Radius の円周）、3 = 光の点
Shader "Edgewise/Shape"
{
    Properties
    {
        _Color ("Color (HDR)", Color) = (1, 1, 1, 1)
        _Mode ("Mode", Float) = 0
        _Size ("Quad Size (world)", Vector) = (1, 1, 0, 0)
        _Corner ("Corner Radius", Float) = 0.08
        _Softness ("Softness", Float) = 0.02
        _Thickness ("Thickness", Float) = 0.03
        _Radius ("Ring Radius (0..0.5 of size)", Float) = 0.4
        _Pulse ("Pulse", Float) = 0
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

            float4 _Color;
            float _Mode;
            float4 _Size;
            float _Corner;
            float _Softness;
            float _Thickness;
            float _Radius;
            float _Pulse;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = v.positionOS.xy * _Size.xy;
                return o;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 halfSize = _Size.xy * 0.5;
                float a = 0.0;
                float soft = max(_Softness, 1e-4);
                if (_Mode < 0.5)
                {
                    float d = sdRoundBox(i.p, halfSize - soft, _Corner);
                    a = 1.0 - smoothstep(-soft, soft, d);
                }
                else if (_Mode < 1.5)
                {
                    float d = sdRoundBox(i.p, halfSize - soft - _Thickness, _Corner);
                    a = 1.0 - smoothstep(_Thickness - soft, _Thickness + soft, abs(d));
                }
                else if (_Mode < 2.5)
                {
                    float r = length(i.p / halfSize) * 0.5;
                    a = exp(-pow((r - _Radius) / max(_Thickness, 1e-3), 2.0));
                }
                else
                {
                    float r = length(i.p / halfSize);
                    a = exp(-r * r * 6.0);
                }
                float pulse = lerp(1.0, 0.6 + 0.4 * sin(_Time.y * 7.0), saturate(_Pulse));
                a = saturate(a * _Color.a * pulse);
                return float4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
