// Basit URP su: prosedürel kayan dalga normalleri, fresnel, güneş parlaması, sis.
Shader "MostWanted/Water"
{
    Properties
    {
        _ShallowColor ("Sığ Renk", Color) = (0.10, 0.45, 0.50, 0.75)
        _DeepColor ("Derin Renk", Color) = (0.02, 0.12, 0.22, 0.95)
        _SkyColor ("Gökyüzü Yansıma", Color) = (0.55, 0.70, 0.85, 1)
        _WaveScale ("Dalga Ölçeği", Float) = 0.035
        _WaveSpeed ("Dalga Hızı", Float) = 0.6
        _Gloss ("Parlaklık", Float) = 220
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _SkyColor;
                float _WaveScale, _WaveSpeed, _Gloss;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float3 WaveNormal(float2 xz, float t)
            {
                float2 d1 = float2(1.0, 0.3), d2 = float2(-0.4, 1.0), d3 = float2(0.7, -0.8);
                float s = _WaveScale;
                float2 g = 0;
                g += d1 * cos(dot(xz, d1) * s * 1.0 + t * 1.1) * 0.6;
                g += d2 * cos(dot(xz, d2) * s * 2.3 + t * 1.7) * 0.35;
                g += d3 * cos(dot(xz, d3) * s * 5.1 + t * 2.6) * 0.2;
                g += d1.yx * cos(dot(xz, d1.yx) * s * 11.0 + t * 3.3) * 0.1;
                return normalize(float3(-g.x * 0.25, 1.0, -g.y * 0.25));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float3 n = WaveNormal(i.positionWS.xz, t);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                Light sun = GetMainLight();
                float3 h = normalize(sun.direction + v);
                float spec = pow(saturate(dot(n, h)), _Gloss) * 3.0;
                half4 baseCol = lerp(_ShallowColor, _DeepColor, saturate(fres * 1.5 + 0.2));
                half3 col = lerp(baseCol.rgb * (0.35 + 0.65 * sun.color), _SkyColor.rgb * sun.color, fres * 0.8);
                col += spec * sun.color;
                col = MixFog(col, i.fogFactor);
                return half4(col, saturate(baseCol.a + fres * 0.3));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
