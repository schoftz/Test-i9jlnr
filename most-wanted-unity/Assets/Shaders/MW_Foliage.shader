// Most Wanted — ağaç / bitki (URP 17, GPU instancing: Graphics.RenderMeshInstanced). Örnek başına (nesne konumu
// hash'i) renk ve parlaklık değişimi, yaprak kümesi gürültüsü, taç altı karanlığı, arkadan ışık geçirgenliği,
// hafif rüzgâr salınımı (tüm geçişlerde aynı → gölge uyumlu). Gövde malzemesinde _Leaf = 0.
Shader "MW/Foliage"
{
    Properties
    {
        _BaseColor ("Renk", Color) = (0.2, 0.38, 0.14, 1)
        _BaseMap ("Doku", 2D) = "white" {}
        _Leaf ("Yaprak (1) / Gövde (0)", Float) = 1
        _Variation ("Renk Değişimi", Range(0, 1)) = 0.6
        _Wind ("Rüzgâr", Range(0, 1)) = 0.35
        _Translucency ("Geçirgenlik", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Leaf;
            half _Variation;
            half _Wind;
            half _Translucency;
        CBUFFER_END

        // rüzgâr: yükseklikle artan, örnek başına faz kaymalı salınım (nesne uzayında birkaç cm)
        #define MW_VERTEX_MODIFY 1
        float3 MWModifyVertex(float3 p)
        {
            float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
            float phase = dot(origin.xz, float2(0.13, 0.17));
            float hgt = saturate(p.y / 10.0);
            float s = sin(_Time.y * 1.3 + phase) * 0.6 + sin(_Time.y * 2.9 + phase * 1.7) * 0.25;
            p.xz += float2(s, s * 0.6) * (hgt * hgt) * 0.12 * _Wind * _Leaf;
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #define MW_FORWARD 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "MW_CarPaintCommon.hlsl"

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
                float h = Hash21(origin.xz * 0.37 + 1.3);
                float h2 = Hash21(origin.xz * 0.71 + 8.1);
                half3 col = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // örnek başına ton: sarımsı / koyu yeşil / mavimsi, parlaklık ±
                half3 tintA = half3(1.18, 1.08, 0.72), tintB = half3(0.78, 0.92, 0.85);
                half3 tint = lerp(tintB, tintA, (half)h);
                col = lerp(col, col * tint * (half)lerp(0.8, 1.15, h2), _Variation);
                half3 n = normalize(i.normalWS);
                half occ = 1;
                if (_Leaf > 0.5)
                {
                    // yaprak kümeleri ve taç altı karanlığı
                    float clump = MWFbm(i.positionOS * 1.6 + h * 10.0);
                    col *= (half)(0.7 + 0.55 * clump);
                    occ = (half)lerp(0.45, 1.0, saturate(dot(n, half3(0, 1, 0)) * 0.5 + 0.55)) * (half)(0.75 + 0.25 * clump);
                    // küme normal pürüzü
                    n = normalize(n + (half3)(MWHash33(floor(i.positionOS * 3.0 + h * 7.0)) - 0.5) * 0.6h);
                }
                else
                {
                    col *= (half)(0.8 + 0.3 * VNoise(float2(i.positionOS.y * 6.0, atan2(i.positionOS.z, i.positionOS.x) * 3.0)));
                }

                SurfaceData s = (SurfaceData)0;
                s.albedo = col;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = _Leaf > 0.5 ? 0.28h : 0.1h;
                s.normalTS = half3(0, 0, 1);
                s.occlusion = occ;
                s.alpha = 1;
                InputData inputData = MWInputData(i, n);
                half4 color = UniversalFragmentPBR(inputData, s);
                if (_Leaf > 0.5)
                {
                    Light ml = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                    half back = pow(saturate(dot(inputData.viewDirectionWS, -ml.direction)), 4.0h);
                    color.rgb += col * ml.color * (back * _Translucency * ml.shadowAttenuation);
                }
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWShadowVert
            #pragma fragment MWShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #define MW_SHADOWCASTER 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "MW_CarPaintCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWDepthVert
            #pragma fragment MWDepthFrag
            #pragma multi_compile_instancing
            #define MW_DEPTHONLY 1
            #include "MW_CarPaintCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWDepthNormalsVert
            #pragma fragment MWDepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #define MW_DEPTHNORMALS 1
            #include "MW_CarPaintCommon.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
