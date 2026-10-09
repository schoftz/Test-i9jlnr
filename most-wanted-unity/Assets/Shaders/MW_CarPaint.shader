// Most Wanted — araç boyası (URP 17).
// Taban renk + metalik pul parıltısı (nesne uzayı gürültü, ekran türevine göre sönümlenir) + vernik (clear coat, URP'nin ikinci
// speküler lob'u ve çevre yansıması) + Fresnel ortam yansıması + kir/toz maskesi (_Dirt, hasar için de kullanılabilir).
// Özellik adları URP/Lit ile uyumludur (_BaseColor, _BaseMap, _Metallic, _Smoothness, _BumpMap, _EmissionColor) —
// U.ApplyPaint ve mevcut boya kodu değişmeden çalışır.
Shader "MW/CarPaint"
{
    Properties
    {
        _BaseColor ("Renk", Color) = (0.7, 0.05, 0.05, 1)
        _BaseMap ("Doku", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Ölçek", Float) = 1
        _Metallic ("Metalik", Range(0, 1)) = 0.6
        _Smoothness ("Taban Pürüzsüzlük", Range(0, 1)) = 0.7
        _ClearCoat ("Vernik", Range(0, 1)) = 1
        _ClearCoatSmoothness ("Vernik Pürüzsüzlük", Range(0, 1)) = 0.94
        _FlakeScale ("Pul Ölçeği (1/m)", Float) = 420
        _FlakeDensity ("Pul Yoğunluğu", Range(0, 1)) = 0.35
        _FlakeIntensity ("Pul Parlaklığı", Range(0, 4)) = 1.2
        _Dirt ("Kir", Range(0, 1)) = 0
        _DirtColor ("Kir Rengi", Color) = (0.30, 0.26, 0.21, 1)
        [HDR] _EmissionColor ("Işıma", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            half _ClearCoat;
            half _ClearCoatSmoothness;
            float _FlakeScale;
            half _FlakeDensity;
            half _FlakeIntensity;
            half _Dirt;
            half4 _DirtColor;
            half4 _EmissionColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWVert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
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

            // URP'nin ikinci (vernik) speküler lob'u ve vernik çevre yansıması
            #define _CLEARCOAT 1
            #define MW_FORWARD 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "MW_CarPaintCommon.hlsl"

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                half3 nWS = normalize(i.normalWS);
            #ifdef _NORMALMAP
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                nWS = TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS));
            #endif

                InputData inputData = MWInputData(i, nWS);

                // ---- kir / toz: aşağı ve yana bakan yüzeylerde, nesne uzayı fbm ile lekeli ----
                half dirtNoise = (half)MWFbm(i.positionOS * 3.1);
                half lower = saturate(0.45 - inputData.normalWS.y);      // alt ve yan paneller
                half dirt = saturate(_Dirt * (lower * 1.4 + 0.35) * smoothstep(0.25, 0.75, dirtNoise + _Dirt * 0.35));

                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(albedo, _DirtColor.rgb, dirt);
                s.metallic = lerp(_Metallic, 0.0h, dirt);
                s.specular = half3(0, 0, 0);
                s.smoothness = lerp(_Smoothness, 0.2h, dirt);
                s.normalTS = half3(0, 0, 1);
                s.emission = _EmissionColor.rgb;
                s.occlusion = 1;
                s.alpha = 1;
                s.clearCoatMask = _ClearCoat * (1.0h - dirt);
                s.clearCoatSmoothness = _ClearCoatSmoothness;

                half4 color = UniversalFragmentPBR(inputData, s);

                // ---- metalik pullar: hücre başına rastgele eğik mikro normal, ana ışıktan keskin parıltı ----
                float3 fp = i.positionOS * _FlakeScale;
                float3 cell = floor(fp);
                float3 h = MWHash33(cell);
                // hücre pikselden küçükse parıltıyı söndür (titreşim/aliasing olmasın)
                float cellsPerPixel = length(fwidth(fp));
                half flakeVis = (half)saturate(1.5 - cellsPerPixel);
                half isFlake = step(1.0 - _FlakeDensity, h.x);
                half3 flakeN = normalize(inputData.normalWS + (half3)(MWHash33(cell + 17.0) - 0.5) * 0.9h);
                Light ml = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half3 H = SafeNormalize(ml.direction + inputData.viewDirectionWS);
                half nl = saturate(dot(inputData.normalWS, ml.direction));
                half sparkle = pow(saturate(dot(flakeN, H)), 280.0h) * isFlake * flakeVis;
                // uzakta pulların ortalaması: hafif geniş metalik parlama
                half sheen = pow(saturate(dot(inputData.normalWS, H)), 24.0h) * 0.06h * (1.0h - flakeVis);
                half3 flakeTint = lerp(s.albedo, half3(1, 1, 1), 0.45h);
                color.rgb += ml.color * (ml.shadowAttenuation * ml.distanceAttenuation * nl) * (sparkle * 6.0h + sheen) * _FlakeIntensity * _Metallic * flakeTint * (1.0h - dirt);

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
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
            ZWrite On
            ColorMask R
            Cull Back

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
            Cull Back

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
