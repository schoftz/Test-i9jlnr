// Most Wanted — tam ekran atmosfer geçişi (URP 17 Full Screen Pass Renderer Feature, BeforeRenderingPostProcessing,
// gereksinim: Depth). Üstel yükseklik sisi (yer pusu, güneş yönünde ısınan saçılma) + ekran-uzayı güneş huzmeleri
// (derinlik tamponundaki gökyüzü maskesinin güneşe doğru radyal örneklenmesi). Parametreler RenderSetup.cs'den global.
Shader "MW/SkyAtmosphere"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "MW_Atmosphere"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _MW_SunDir;
            float4 _MW_SunColor;
            float4 _MW_Horizon;
            float _MW_Night;
            float _MW_ShaftSamples;
            float _MW_ShaftIntensity;
            float _MW_FogHeightDensity;
            float _MW_FogHeightFalloff;
            float _MW_FogBaseY;
            float _MW_AtmosOn;

            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 1e-6;
            #else
                return rawDepth >= 1.0 - 1e-6;
            #endif
            }

            float IGN(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_MW_AtmosOn < 0.5) return src;

                float raw = SampleSceneDepth(uv);
                bool sky = IsSky(raw);
            #if UNITY_REVERSED_Z
                float depth = raw;
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
                float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = posWS - cam;
                float dist = length(ray);
                float3 v = ray / max(dist, 1e-4);

                // ---- üstel yükseklik sisi (analitik integral) ----
                float k = max(_MW_FogHeightFalloff, 1e-4);
                float h0 = cam.y - _MW_FogBaseY;
                float kdy = k * ray.y;
                float integral = abs(kdy) > 1e-3 ? (1.0 - exp(-kdy)) / kdy : 1.0;
                float optical = _MW_FogHeightDensity * exp(-k * h0) * dist * integral;
                float fogAmt = min(1.0 - exp(-max(optical, 0.0)), sky ? 0.25 : 0.55);

                float day = saturate(_MW_SunColor.a);
                float mu = dot(v, _MW_SunDir.xyz);
                half3 inscatter = _MW_Horizon.rgb + _MW_SunColor.rgb * (0.22 * pow(saturate(mu), 8.0)) * day;
                half3 col = lerp(src.rgb, inscatter, fogAmt);

                // ---- güneş huzmeleri ----
                if (day > 0.02 && _MW_ShaftIntensity > 0.0 && _MW_SunDir.w > -0.02)
                {
                    float3 sunPosWS = cam + _MW_SunDir.xyz * 1000.0;
                    float4 sunCS = mul(UNITY_MATRIX_VP, float4(sunPosWS, 1.0));
                    if (sunCS.w > 0.0)
                    {
                        float2 sunUV = ComputeNormalizedDeviceCoordinates(sunPosWS, UNITY_MATRIX_VP);
                        float2 delta = sunUV - uv;
                        float len = length(delta);
                        float att = saturate(1.0 - len * 0.85);
                        if (att > 0.0)
                        {
                            int n = (int)clamp(_MW_ShaftSamples, 4.0, 32.0);
                            float2 stepUV = delta * (0.85 / n);
                            float2 p = uv + stepUV * IGN(input.positionCS.xy);
                            float acc = 0.0, w = 1.0, wsum = 0.0;
                            [loop] for (int s = 0; s < n; s++)
                            {
                                p += stepUV;
                                float inside = (p.x >= 0.0 && p.x <= 1.0 && p.y >= 0.0 && p.y <= 1.0) ? 1.0 : 0.0;
                                float dS = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(p), 0).r;
                                acc += (IsSky(dS) ? 1.0 : 0.0) * inside * w;
                                wsum += w;
                                w *= 0.95;
                            }
                            acc /= max(wsum, 1e-4);
                            half3 shaft = _MW_SunColor.rgb * (acc * att * att * _MW_ShaftIntensity * day * (1.0 - _MW_Night));
                            col += shaft * (sky ? 0.35 : 1.0);
                        }
                    }
                }
                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
