// Most Wanted — lastik / jant (URP 17). Kauçuk: koyu, düşük pürüzsüzlükte geniş parlama, yanak yazılarında
// hafif mat toz; jant bölgeleri (açık renkli doku pikselleri) metalik ve parlak — tek malzemeli tekerlek modelleri için.
Shader "MW/Tire"
{
    Properties
    {
        _BaseColor ("Renk", Color) = (1, 1, 1, 1)
        _BaseMap ("Doku", 2D) = "white" {}
        _RubberColor ("Kauçuk Rengi", Color) = (0.035, 0.035, 0.038, 1)
        _RubberSmoothness ("Kauçuk Pürüzsüzlük", Range(0, 1)) = 0.32
        _RimMetallic ("Jant Metalik", Range(0, 1)) = 1
        _RimSmoothness ("Jant Pürüzsüzlük", Range(0, 1)) = 0.82
        _RimThreshold ("Jant Eşiği (parlaklık)", Range(0, 1)) = 0.28
        _Dust ("Toz", Range(0, 1)) = 0.25
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
            half4 _RubberColor;
            half _RubberSmoothness;
            half _RimMetallic;
            half _RimSmoothness;
            half _RimThreshold;
            half _Dust;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
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

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                InputData inputData = MWInputData(i, normalize(i.normalWS));
                half lum = dot(tex, half3(0.2126h, 0.7152h, 0.0722h));
                half rim = smoothstep(_RimThreshold, _RimThreshold + 0.12h, lum);   // açık pikseller = jant
                half dust = _Dust * (half)MWFbm(i.positionOS * 9.0) * (1.0h - rim * 0.6h);

                SurfaceData s = (SurfaceData)0;
                half3 rubber = lerp(_RubberColor.rgb * (0.6h + tex * 2.0h), half3(0.16h, 0.15h, 0.14h), dust);
                s.albedo = lerp(rubber, tex, rim);
                s.metallic = rim * _RimMetallic;
                s.specular = half3(0, 0, 0);
                s.smoothness = lerp(_RubberSmoothness * (1.0h - dust), _RimSmoothness, rim);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = 1;
                s.alpha = 1;
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
