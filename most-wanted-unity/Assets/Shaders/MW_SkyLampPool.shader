// Most Wanted — sokak lambası ışık havuzu (zemine yatık dörtgen, toplamalı, GPU instancing). Gerçek nokta ışıkları
// yalnızca oyuncuya en yakın birkaç lambaya verilir; uzaktakiler bu ucuz havuzlarla görünür. Gece (_MW_Night) ile söner,
// ıslak zeminde (_MW_Wet) parlak yansıma gibi güçlenir; sisle birlikte kaybolur.
Shader "MW/SkyLampPool"
{
    Properties
    {
        _Color ("Renk", Color) = (1, 0.72, 0.42, 1)
        _Intensity ("Şiddet", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-90" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "LampPool"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Offset -2, -2
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _MW_Night;
            float _MW_Wet;
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;
                float fall = saturate(1.0 - r);
                fall = fall * fall * (3.0 - 2.0 * fall);
                half3 c = _Color.rgb * (half)(fall * _Intensity * saturate(_MW_Night) * lerp(1.0, 1.8, saturate(_MW_Wet)));
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
