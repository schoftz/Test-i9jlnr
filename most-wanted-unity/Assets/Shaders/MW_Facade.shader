// Most Wanted — bina cephesi (URP 17). UV metre cinsinden (u: duvar boyunca, v: yükseklik; MeshKit.Box).
// Prosedürel pencere ızgarası (pencere başına perde/stor tonu, çerçeve, denizlik), kat bantları, stil başına duvar
// (cam giydirme / beton panel / tuğla / sıva / ev / depo / otopark / dükkan vitrini + ışıklı tabela),
// kir/akıntı lekeleri, camda URP PBR yansıması (prob + gökyüzü, Fresnel), gece pencere ışıkları (global _MW_Night),
// ıslak zemin modunda (global _MW_Wet) hafif koyulaşma/parlama. Tüm desenler fwidth ile kenar yumuşatmalı (uzakta titreşmez).
Shader "MW/Facade"
{
    Properties
    {
        _BaseColor ("Duvar Rengi", Color) = (0.75, 0.72, 0.68, 1)
        _BaseMap ("(kullanılmaz)", 2D) = "white" {}
        _GlassColor ("Cam Rengi", Color) = (0.10, 0.13, 0.16, 1)
        _FrameColor ("Çerçeve Rengi", Color) = (0.85, 0.85, 0.83, 1)
        _Style ("Stil (0 cam,1 beton,2 tuğla,3 sıva,4 ev,5 depo,6 otopark,7 dükkan)", Float) = 1
        _FloorH ("Kat Yüksekliği (m)", Float) = 3.4
        _WinW ("Pencere Aralığı (m)", Float) = 3.0
        _WinSize ("Pencere (genişlik, yükseklik, merkez y, -)", Vector) = (0.5, 0.5, 0.55, 0)
        _LitRatio ("Gece Yanan Oranı", Range(0, 1)) = 0.5
        _Dirt ("Kir", Range(0, 1)) = 0.35
        _WallSmoothness ("Duvar Pürüzsüzlük", Range(0, 1)) = 0.2
        _Seed ("Tohum", Float) = 0
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
            half4 _GlassColor;
            half4 _FrameColor;
            float _Style;
            float _FloorH;
            float _WinW;
            float4 _WinSize;
            half _LitRatio;
            half _Dirt;
            half _WallSmoothness;
            float _Seed;
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

            float _MW_Night;
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

            // d: kutu kenarına işaretli mesafe (içeride < 0), fw: piksel başına birim → kenar yumuşatmalı maske
            float BoxAA(float2 d, float2 fw)
            {
                float2 m = saturate(0.5 - d / fw);
                return m.x * m.y;
            }

            half3 Palette(float h)
            {
                // perde/stor tonları: bej, beyaz, gri-mavi, sıcak kırmızı, yeşil
                half3 c = half3(0.78, 0.70, 0.58);
                c = h > 0.25 ? half3(0.86, 0.86, 0.84) : c;
                c = h > 0.50 ? half3(0.45, 0.52, 0.60) : c;
                c = h > 0.70 ? half3(0.62, 0.30, 0.24) : c;
                c = h > 0.88 ? half3(0.40, 0.50, 0.36) : c;
                return c;
            }

            half3 Neon(float h)
            {
                half3 c = half3(1.0, 0.25, 0.35);
                c = h > 0.2 ? half3(0.2, 0.7, 1.0) : c;
                c = h > 0.4 ? half3(1.0, 0.65, 0.15) : c;
                c = h > 0.6 ? half3(0.35, 1.0, 0.45) : c;
                c = h > 0.8 ? half3(0.85, 0.35, 1.0) : c;
                return c;
            }

            half4 Frag(MWVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 uv = i.uv;                       // metre
                int style = (int)round(_Style);
                bool shop = style == 7;
                float night = saturate(_MW_Night);
                float wet = saturate(_MW_Wet);

                float2 cs = float2(max(_WinW, 0.5), max(_FloorH, 0.5));
                float2 g = uv / cs;
                float2 cell = floor(g);
                float2 f = frac(g);
                float2 fw = max(fwidth(g), 1e-4);
                float h1 = Hash21(cell + _Seed * 17.13);
                float h2 = Hash21(cell.yx * 1.37 + 5.3 + _Seed);

                // ---- pencere, çerçeve, denizlik ----
                float2 hs = _WinSize.xy * 0.5;
                float2 wc = float2(0.5, _WinSize.z);
                float2 dWin = abs(f - wc) - hs;
                float win = BoxAA(dWin, fw);
                float2 frameW = float2(0.09 / cs.x, 0.09 / cs.y);
                float frame = saturate(BoxAA(dWin - frameW, fw) - win);
                float2 sillC = float2(0.5, wc.y - hs.y - frameW.y - 0.03 / cs.y);
                float sill = BoxAA(abs(f - sillC) - float2(hs.x + frameW.x + 0.04 / cs.x, 0.035 / cs.y), fw);
                // zemin kotu (temel) penceresiz; dükkan katında vitrin
                float plinth = saturate((uv.y - 0.9) / max(fwidth(uv.y), 1e-3));
                if (!shop) { win *= plinth; frame *= plinth; sill *= plinth; }
                // kat bandı
                float band = (style == 1 || style == 3 || style == 0) ? BoxAA(float2(0, abs(f.y - 0.02) - 0.035), fw) : 0.0;

                // ---- duvar ----
                half3 wall = _BaseColor.rgb;
                half wallSmooth = _WallSmoothness;
                half wallMetal = 0;
                if (style == 2)
                {
                    float2 bp = uv / float2(0.25, 0.075);
                    float row = floor(bp.y);
                    bp.x += 0.5 * fmod(abs(row), 2.0);
                    float2 bf = frac(bp);
                    float brick = BoxAA(abs(bf - 0.5) - float2(0.45, 0.38), max(fwidth(bp), 1e-4));
                    float bh = Hash21(floor(bp) + _Seed);
                    half3 bc = wall * lerp(0.78h, 1.12h, (half)bh);
                    wall = lerp(half3(0.62, 0.6, 0.56), bc, brick);
                }
                else if (style == 1 || style == 6)
                {
                    // beton panel derzleri (her pencere hücresinde) + leke
                    float joint = 1.0 - BoxAA(abs(f - 0.5) - (0.5 - 0.012), fw);
                    wall *= (half)(1.0 - joint * 0.25) * lerp(0.9h, 1.06h, (half)VNoise(uv * 0.35 + _Seed));
                }
                else if (style == 0)
                {
                    wall = _FrameColor.rgb;           // alüminyum kayıt
                    wallSmooth = 0.55;
                    wallMetal = 0.8;
                }
                else
                {
                    wall *= lerp(0.92h, 1.05h, (half)VNoise(uv * 0.6 + _Seed));
                }
                wall = lerp(wall, wall * 0.82h, (half)band);

                // ---- kir / akıntı lekeleri (pencere altından aşağı) ----
                float streak = VNoise(float2(uv.x * 2.7, uv.y * 0.12 + _Seed)) * VNoise(float2(uv.x * 9.1, uv.y * 0.5));
                float grime = saturate(1.0 - uv.y / 2.2) * 0.6;                    // zemin hizası
                float dirt = saturate(_Dirt * (streak * 1.3 + grime + 0.25 * VNoise(uv * 0.15)));
                wall *= (half)(1.0 - dirt * 0.38);
                wallSmooth *= (half)(1.0 - dirt * 0.5);

                // ---- cam: koyu iç + perde/stor; yansıma PBR'dan (pürüzsüz dielektrik) ----
                float curtain = step(0.45, h2);
                half3 curtainCol = Palette(Hash21(cell * 3.1 + 7.7 + _Seed));
                half3 glass = lerp(_GlassColor.rgb, curtainCol * 0.35h, (half)(curtain * 0.6));
                // storlar: yatay şerit
                float blinds = curtain * step(0.8, h1) * step(0.5, frac(f.y * cs.y * 6.0));
                glass = lerp(glass, curtainCol * 0.5h, (half)(blinds * 0.5));

                // ---- dükkan katı ----
                half3 emission = 0;
                float lit = step(h1, _LitRatio);
                half3 warm = lerp(half3(1.0, 0.78, 0.48), half3(0.82, 0.9, 1.0), (half)step(0.7, h2));
                if (style == 0) warm = lerp(half3(0.9, 0.95, 1.0), half3(1.0, 0.85, 0.6), (half)step(0.8, h2));
                emission += warm * (half)(win * lit * night) * (half)(curtain > 0.5 ? 0.9 : 1.6);
                if (shop)
                {
                    // vitrin: 0.2–3.3 m cam, 3.45–4.05 m tabela bandı
                    float sx = uv.x / 4.5;
                    float sc = floor(sx);
                    float sf = frac(sx);
                    float sfw = max(fwidth(sx), 1e-4);
                    float vy = uv.y;
                    float vfw = max(fwidth(vy), 1e-4);
                    float vitrine = saturate(0.5 - (abs(sf - 0.5) - 0.44) / sfw) * saturate(0.5 - (abs(vy - 1.75) - 1.55) / vfw);
                    float signMask = saturate(0.5 - (abs(sf - 0.5) - 0.40) / sfw) * saturate(0.5 - (abs(vy - 3.75) - 0.3) / vfw);
                    float shopH = Hash21(float2(sc, _Seed + 3.0));
                    win = vitrine;
                    frame = saturate(saturate(0.5 - (abs(sf - 0.5) - 0.47) / sfw) * saturate(0.5 - (abs(vy - 1.75) - 1.6) / vfw) - vitrine);
                    sill = 0;
                    glass = half3(0.08, 0.09, 0.1);
                    wall = lerp(wall * 0.5h, _BaseColor.rgb * 0.35h, 0.5h);
                    half3 neon = Neon(shopH);
                    wall = lerp(wall, neon * 0.6h, (half)signMask);
                    // vitrin içi her zaman biraz aydınlık, gece daha çok; tabela neon gece parlar
                    emission += half3(1.0, 0.86, 0.66) * (half)(vitrine * (0.25 + 1.4 * night) * step(0.15, shopH));
                    emission += neon * (half)(signMask * (0.4 + 3.2 * night));
                }

                SurfaceData s = (SurfaceData)0;
                half3 alb = wall;
                alb = lerp(alb, half3(0.82, 0.8, 0.77), (half)sill);
                alb = lerp(alb, _FrameColor.rgb, (half)frame);
                alb = lerp(alb, glass, (half)win);
                half smooth = lerp(wallSmooth, 0.45h, (half)saturate(frame + sill));
                smooth = lerp(smooth, 0.94h, (half)win);
                half metal = lerp(wallMetal, style == 0 ? 0.8h : 0.1h, (half)frame);
                metal = lerp(metal, 0.0h, (half)win);
                // ıslak: duvar koyulaşır, parlar
                alb *= (half)lerp(1.0, lerp(0.8, 1.0, win), wet);
                smooth = lerp(smooth, max(smooth, 0.55h), (half)wet * (half)(1.0 - win));

                s.albedo = alb;
                s.metallic = metal;
                s.specular = half3(0, 0, 0);
                s.smoothness = smooth;
                s.normalTS = half3(0, 0, 1);
                s.emission = emission;
                s.occlusion = (half)(1.0 - frame * 0.25);
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
