// Vinil / livery katmanı: araç gövde uzayında kutu (box) eşleme. Boya alt-mesh'inin üstüne ikinci katman olarak çizilir.
// _ObjToCar: renderer nesne uzayı → araç kökü uzayı (araç başına sabit). _BMin/_BSize: gövde sınırları (araç uzayı).
// Atlas: üst yarı = yan tasarım (u: uzunluk, v: yükseklik), alt yarı = kaput/tavan/ön/arka tasarımı.
Shader "MostWanted/Vinyl"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "black" {}
        _Alpha ("Alfa", Float) = 1
        _BMin ("Gövde Min", Vector) = (-1, 0, -2.2, 0)
        _BSize ("Gövde Boyut", Vector) = (2, 1.4, 4.4, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Alpha;
                float4 _BMin, _BSize;
                float4x4 _ObjToCar;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 carPos : TEXCOORD0; float3 carN : TEXCOORD1; float3 normalWS : TEXCOORD2; float fogFactor : TEXCOORD3; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.carPos = mul(_ObjToCar, float4(v.positionOS.xyz, 1)).xyz;
                o.carN = normalize(mul((float3x3)_ObjToCar, v.normalOS));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                if (_Alpha <= 0.001) discard;
                float3 q = (i.carPos - _BMin.xyz) / max(_BSize.xyz, 0.01);
                float3 n = abs(i.carN);
                float2 uv;
                if (n.x >= n.y && n.x >= n.z)
                {
                    uv = float2(q.z, 0.5 + 0.5 * saturate(q.y));   // iki yanda da ön = u 1 (yazılar Text3D ile)
                }
                else if (n.y >= n.z) uv = float2(q.z, 0.5 * saturate(q.x));
                else uv = float2(q.x, 0.5 * saturate(q.y));
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                Light l = GetMainLight();
                float ndl = saturate(dot(normalize(i.normalWS), l.direction));
                half3 lit = c.rgb * (SampleSH(normalize(i.normalWS)) + l.color * ndl * 0.85);
                lit = MixFog(lit, i.fogFactor);
                return half4(lit, c.a * _Alpha);
            }
            ENDHLSL
        }
    }
}
