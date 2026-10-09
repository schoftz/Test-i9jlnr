// Most Wanted — araç altı temas gölgesi (yumuşak, çarpmalı). Aracı zemine oturtur; gölge haritasının çözemediği
// tekerlek/gövde altı kararmasını verir. Uzakta sisle birlikte söner.
Shader "MW/CarPaintShadow"
{
    Properties
    {
        _Strength ("Güç", Range(0, 1)) = 0.75
        _Core ("Çekirdek", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                half _Core;
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
                float2 p = abs(i.uv - 0.5) * 2.0;                    // 0 merkez .. 1 kenar
                float2 q = max(p - _Core, 0.0) / max(1.0 - _Core, 1e-3);
                float e = length(q);                                 // yuvarlatılmış dikdörtgen mesafe
                half shadow = (1.0 - smoothstep(0.0, 1.0, e)) * _Strength;
                shadow *= 0.75 + 0.25 * (1.0 - saturate(length(p)));  // merkeze doğru biraz daha koyu
                half3 c = 1.0 - shadow;
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                c = lerp(half3(1, 1, 1), c, ComputeFogIntensity(i.fog));
            #endif
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
