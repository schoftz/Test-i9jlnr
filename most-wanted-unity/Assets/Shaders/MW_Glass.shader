// Most Wanted — araç camı (URP 17). Renkli, yarı saydam; yansıma Fresnel ile kenarlarda güçlenir ve saydamlıktan
// etkilenmez (önçarpımlı alfa: One / OneMinusSrcAlpha). Gölge düşürmez (iç mekan kararmasın).
Shader "MW/Glass"
{
    Properties
    {
        _BaseColor ("Renk (A: opaklık)", Color) = (0.06, 0.08, 0.09, 0.45)
        _BaseMap ("Doku", 2D) = "white" {}
        _Smoothness ("Pürüzsüzlük", Range(0, 1)) = 0.96
        _FresnelPower ("Fresnel Üssü", Range(1, 8)) = 4
        _EdgeOpacity ("Kenar Opaklığı", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _FresnelPower;
            half _EdgeOpacity;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MWVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // diffuse alfa ile çarpılır, speküler/yansıma çarpılmaz → gerçek cam gibi
            #define _ALPHAPREMULTIPLY_ON 1
            #define _SURFACE_TYPE_TRANSPARENT 1
            #define MW_FORWARD 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "MW_CarPaintCommon.hlsl"

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                InputData inputData = MWInputData(i, normalize(i.normalWS));
                half nv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half fres = pow(1.0h - nv, _FresnelPower);
                half alpha = saturate(lerp(_BaseColor.a * tex.a, _EdgeOpacity, fres));

                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb * tex.rgb;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = _Smoothness;
                s.normalTS = half3(0, 0, 1);
                s.occlusion = 1;
                s.alpha = alpha;
                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFogColor(color.rgb, unity_FogColor.rgb * alpha, inputData.fogCoord);
                return half4(color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    // Fallback yok: Lit fallback'i ShadowCaster geçişi ekleyip cam gölgesi düşürürdü (C# tarafı shader yoksa Lit'e döner)
    FallBack Off
}
