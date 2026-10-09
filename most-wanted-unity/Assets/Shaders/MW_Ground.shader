// Most Wanted — arazi/çim (URP 17). Renk haritası (_BaseMap, 6.8 km'ye yayılır) + dünya uzayı detay: çok ölçekli çim
// gürültüsü, kuru/yeşil yama renk değişimi, toprak lekeleri, uzaklıkta atmosferik soluklaşma (sis rengine yakın, hafif
// doygunluk düşüşü). Islak modda (_MW_Wet) koyulaşır ve hafif parlar.
Shader "MW/Ground"
{
    Properties
    {
        _BaseColor ("Renk", Color) = (1, 1, 1, 1)
        _BaseMap ("Renk Haritası", 2D) = "white" {}
        _DetailStrength ("Detay", Range(0, 1)) = 0.6
        _DryColor ("Kuru Çim", Color) = (0.55, 0.5, 0.26, 1)
        _LushColor ("Yeşil Çim", Color) = (0.22, 0.38, 0.14, 1)
        _DistanceTint ("Uzaklık Tonu", Range(0, 1)) = 0.35
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
            half _DetailStrength;
            half4 _DryColor;
            half4 _LushColor;
            half _DistanceTint;
        CBUFFER_END
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

            float _MW_Wet;
            float4 _MW_Horizon;

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
                float2 w = i.positionWS.xz;
                half3 mapCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                // yeşillik: harita renginde yeşil baskınsa çim kabul et
                half green = saturate((mapCol.g - max(mapCol.r, mapCol.b)) * 8.0h);

                float2 p1 = w * 0.9, p2 = w * 4.0;
                float f1 = saturate(1.5 - length(fwidth(p1)));
                float f2 = saturate(1.5 - length(fwidth(p2)));
                float n = (VNoise(p1) - 0.5) * f1 * 0.6 + (VNoise(p2) - 0.5) * f2 * 0.4;
                float patchN = VNoise(w * 0.035 + 4.0) * 0.65 + VNoise(w * 0.12) * 0.35;
                half3 grassTint = lerp(_LushColor.rgb, _DryColor.rgb, (half)smoothstep(0.35, 0.8, patchN));
                half3 col = lerp(mapCol, lerp(mapCol, grassTint * 1.6h * (mapCol.g / max(_LushColor.g, 0.05h)), 0.45h), green);
                col *= (half)(1.0 + n * _DetailStrength * 0.6);
                // toprak lekeleri (çimde)
                float dirt = smoothstep(0.72, 0.8, VNoise(w * 0.08 + 9.0)) * green;
                col = lerp(col, half3(0.36, 0.31, 0.24), (half)(dirt * 0.5));

                // uzaklıkta solgunlaşma (hava perspektifi; sis rengine doğru, sis üstüne hafif)
                float dist = length(i.positionWS - _WorldSpaceCameraPos);
                half far = (half)(saturate((dist - 150.0) / 900.0) * _DistanceTint);
                half lum = dot(col, half3(0.3, 0.59, 0.11));
                col = lerp(col, lerp(half3(lum, lum, lum), _MW_Horizon.rgb * 0.6h, 0.4h), far);

                float wet = saturate(_MW_Wet);
                col *= (half)lerp(1.0, 0.72, wet);

                SurfaceData s = (SurfaceData)0;
                s.albedo = col;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = (half)lerp(0.08, 0.45, wet);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = 1;
                s.alpha = 1;
                InputData inputData = MWInputData(i, normalize(i.normalWS));
                half4 color = UniversalFragmentPBR(inputData, s);
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
