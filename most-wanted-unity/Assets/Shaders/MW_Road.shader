// Most Wanted — yol yüzeyleri (URP 17). Modlar: 0 şeritli yol (doku: çizgiler, u: enine 0..1), 1 düz asfalt,
// 2 kaldırım taşı, 3 beton/bordür. Prosedürel asfalt (dünya uzayı): agrega gürültüsü, ton değişimi, yamalar, çatlaklar,
// şerit ortasında yağ lekesi / teker izlerinde cilalanma. Islak mod (global _MW_Wet): gözenekli yüzey koyulaşır,
// pürüzsüzlük artar, su birikintileri ayna gibi (prob/gökyüzü yansıması + sokak lambası parlamaları).
Shader "MW/Road"
{
    Properties
    {
        _BaseColor ("Renk", Color) = (1, 1, 1, 1)
        _BaseMap ("Doku (şerit çizgileri / taş)", 2D) = "white" {}
        _Mode ("Mod (0 yol, 1 asfalt, 2 kaldırım, 3 beton)", Float) = 0
        _RoadWidth ("Yol Genişliği (m)", Float) = 0
        _LaneW ("Şerit Genişliği (m)", Float) = 3.5
        _Smoothness ("Pürüzsüzlük", Range(0, 1)) = 0.18
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
            float _Mode;
            float _RoadWidth;
            float _LaneW;
            half _Smoothness;
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

            // ince detayı piksel altına inince sönümle (titreşim olmasın)
            float Fade(float2 p) { return saturate(1.5 - length(fwidth(p))); }

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                int mode = (int)round(_Mode);
                float2 w = i.positionWS.xz;
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float wet = saturate(_MW_Wet);

                half3 alb;
                half smooth = _Smoothness;
                float porous = 0.5;     // ıslakken koyulaşma
                float puddleAllowed = 0;

                if (mode <= 1)
                {
                    float2 pA = w * 7.0, pB = w * 23.0;
                    float agg = (VNoise(pA) - 0.5) * 0.6 * Fade(pA) + (VNoise(pB) - 0.5) * 0.4 * Fade(pB);
                    float tone = VNoise(w * 0.05) * 0.05 + VNoise(w * 0.4) * 0.025;
                    float base = 0.155 + tone + agg * 0.06;
                    // yamalar: daha yeni (koyu, pürüzsüz) asfalt
                    float patchN = VNoise(w * 0.07 + 11.0);
                    float patchFw = max(fwidth(patchN), 1e-4);
                    float patch = saturate((patchN - 0.7) / patchFw + 0.5);
                    base = lerp(base, 0.11, patch * 0.8);
                    // çatlaklar: sırt gürültüsü, yalnız yıpranmış bölgelerde
                    float cn = abs(VNoise(w * 0.55 + 3.0) * 2.0 - 1.0);
                    float cfw = max(fwidth(cn), 1e-4);
                    float crack = saturate(1.0 - (cn - 0.03) / cfw) * smoothstep(0.5, 0.65, VNoise(w * 0.11 + 7.0)) * (1.0 - patch);
                    base = lerp(base, 0.06, crack * 0.85);

                    float oil = 0, tracks = 0;
                    if (mode == 0 && _RoadWidth > 0.5)
                    {
                        float laneF = frac(i.uv.x * _RoadWidth / max(_LaneW, 1.0));
                        float dl = laneF - 0.5;
                        oil = exp(-dl * dl / 0.008) * (0.6 + 0.4 * VNoise(w * 0.3));
                        float dt = abs(dl) - 0.26;
                        tracks = exp(-dt * dt / 0.004);
                    }
                    base = base * (1.0 - oil * 0.22) + tracks * 0.018;
                    alb = half3(base, base, base * 1.03) * _BaseColor.rgb;
                    smooth = _Smoothness + tracks * 0.12 + patch * 0.06 + oil * 0.08;

                    // şerit boyası (dokudan): aşınmış, hafif parlak
                    if (mode == 0)
                    {
                        half lum = dot(tex.rgb, half3(0.3, 0.6, 0.1));
                        float paint = saturate((lum - 0.4) * 6.0) * lerp(0.75, 1.0, VNoise(w * 3.0));
                        alb = lerp(alb, tex.rgb * 0.95h, (half)paint);
                        smooth = lerp(smooth, 0.35h, (half)paint);
                        porous = lerp(0.5, 0.8, paint);
                    }
                    puddleAllowed = 1;
                }
                else if (mode == 2)
                {
                    float stain = VNoise(w * 0.45 + 5.0);
                    alb = tex.rgb * _BaseColor.rgb * (half)(0.9 + 0.12 * VNoise(w * 2.2)) * (half)lerp(1.0, 0.78, smoothstep(0.6, 0.85, stain));
                    porous = 0.65;
                    puddleAllowed = 0.35;
                }
                else
                {
                    alb = _BaseColor.rgb * (half)(0.88 + 0.16 * VNoise(i.positionWS.xz * 1.3 + i.positionWS.y * 2.1));
                    porous = 0.7;
                }

                // ---- ıslak zemin ----
                float puddleN = VNoise(w * 0.11 + 7.0) * 0.7 + VNoise(w * 0.5 + 2.0) * 0.3;
                float puddle = smoothstep(0.56, 0.6, puddleN) * puddleAllowed * wet;
                alb *= (half)lerp(1.0, porous, wet);
                smooth = lerp(smooth, 0.8h, (half)wet);
                alb *= (half)lerp(1.0, 0.75, puddle);
                smooth = lerp(smooth, 0.98h, (half)puddle);

                SurfaceData s = (SurfaceData)0;
                s.albedo = alb;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = smooth;
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
