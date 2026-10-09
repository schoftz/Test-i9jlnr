// Bulut kubbesi: prosedürel gürültülü bulutlar, kayarak hareket eder.
Shader "MostWanted/Clouds"
{
    Properties
    {
        _Color ("Renk", Color) = (1, 1, 1, 1)
        _Cover ("Kapalılık", Range(0, 1)) = 0.45
        _Speed ("Hız", Float) = 0.004
        _Scale ("Ölçek", Float) = 3.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Cover, _Speed, _Scale;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = normalize(v.positionOS.xyz);
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 5; k++) { s += noise(p) * a; p *= 2.03; a *= 0.5; } return s; }
            half4 frag (Varyings i) : SV_Target
            {
                if (i.dir.y <= 0.02) return 0;
                float2 uv = i.dir.xz / (i.dir.y + 0.15) * _Scale + _Time.y * _Speed * float2(1, 0.4);
                float c = smoothstep(1.0 - _Cover, 1.0 - _Cover + 0.35, fbm(uv));
                Light sun = GetMainLight();
                half3 col = _Color.rgb * (0.55 + 0.45 * sun.color);
                return half4(col, c * saturate(i.dir.y * 6.0) * _Color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
