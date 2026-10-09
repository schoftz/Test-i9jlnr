// Most Wanted — gökyüzü (URP skybox). Ufuk rengi sisle birebir aynıdır (_MW_Horizon = RenderSettings.fogColor), böylece
// uzak binalar gökyüzüne dikişsiz karışır. Zenit gradyanı, Mie benzeri güneş halesi, HDR güneş diski (bloom/lens flare
// tetikler), gece yıldızlar ve ay diski. Güneş/ufuk değerleri Render/RenderSetup.cs tarafından global olarak verilir.
Shader "MW/Sky"
{
    Properties
    {
        _Exposure ("Pozlama", Range(0, 4)) = 1.2
        _SunSize ("Güneş Boyutu", Range(0, 0.2)) = 0.035
        _AtmosphereThickness ("Atmosfer Kalınlığı", Range(0, 3)) = 1
        _SkyTint ("Gök Tonu", Color) = (0.5, 0.5, 0.5, 1)
        _GroundColor ("Zemin", Color) = (0.25, 0.22, 0.2, 1)
        _ZenithDay ("Zenit (gündüz)", Color) = (0.16, 0.34, 0.70, 1)
        _ZenithGolden ("Zenit (altın saat)", Color) = (0.27, 0.38, 0.58, 1)
        _ZenithNight ("Zenit (gece)", Color) = (0.004, 0.007, 0.018, 1)
        _StarIntensity ("Yıldızlar", Range(0, 8)) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _MW_SunDir;     // xyz: ışığa doğru yön (gece ay), w: güneş yüksekliği (sin)
            float4 _MW_SunColor;   // rgb: renk*şiddet, a: gündüz oranı
            float4 _MW_Horizon;
            float _MW_Night;

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                half _SunSize;
                half _AtmosphereThickness;
                half4 _SkyTint;
                half4 _GroundColor;
                half4 _ZenithDay;
                half4 _ZenithGolden;
                half4 _ZenithNight;
                half _StarIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sd = normalize(_MW_SunDir.xyz + float3(0, 1e-5, 0));
                float elev = _MW_SunDir.w;
                float day = saturate(_MW_SunColor.a);
                float night = saturate(_MW_Night);
                half hi = saturate((elev - 0.04) / 0.5);

                half3 zenith = lerp(_ZenithGolden.rgb, _ZenithDay.rgb, hi) * (_SkyTint.rgb * 2.0);
                zenith = lerp(_ZenithNight.rgb, zenith, day);
                half3 horizon = _MW_Horizon.rgb;

                float y = d.y;
                // ufuk bandı kalınlığı atmosfer kalınlığıyla
                float t = pow(saturate(y), 0.38 * max(_AtmosphereThickness, 0.2));
                half3 col = lerp(horizon, zenith, t);

                float mu = dot(d, sd);
                float above = smoothstep(-0.02, 0.02, y);
                // güneş tarafında ufuk ısınır (geniş Mie), güneş etrafında hale
                half3 sunCol = _MW_SunColor.rgb;
                col += sunCol * (0.10 * pow(saturate(mu * 0.5 + 0.5), 6.0) * (1.0 - t * 0.6) + 0.30 * pow(saturate(mu), 90.0)) * day;

                // güneş diski (HDR) — açısal yarıçap ~ _SunSize * 0.5 rad
                float theta = sqrt(max(0.0, 2.0 * (1.0 - mu)));
                float r = max(_SunSize * 0.45, 1e-4);
                float disk = 1.0 - smoothstep(r * 0.75, r, theta);
                col += sunCol * disk * 30.0 * day * above;

                // gece: yıldızlar ve ay (ışık yönü gece ayı gösterir)
                if (night > 0.01)
                {
                    float3 cell = floor(d * 380.0);
                    float h = Hash31(cell);
                    float star = step(0.9975, h) * (0.4 + 0.6 * frac(h * 913.7));
                    col += star * _StarIntensity * night * saturate(y * 5.0);
                    float moon = 1.0 - smoothstep(r * 0.6, r * 0.8, theta);
                    col += half3(0.75, 0.8, 0.95) * (moon * 4.0 + 0.15 * pow(saturate(mu), 40.0)) * night * above;
                }

                // ufuk altı: zemin rengine (sis rengine yakın) geçiş
                half3 ground = lerp(horizon, _GroundColor.rgb * (0.3 + 0.7 * day), 0.6);
                col = lerp(col, ground, saturate(-y * 6.0));

                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
