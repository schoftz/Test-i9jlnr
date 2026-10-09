// Most Wanted — araç gölgelendiricileri ortak kodu (MW/CarPaint, MW/Glass, MW/Tire).
// Kullanım: Core.hlsl + UnityPerMaterial CBUFFER (_BaseMap_ST dahil) tanımlandıktan sonra include edilir.
// Forward geçişte Lighting.hlsl include edilmiş olmalı (MW_FORWARD tanımlanır).
#ifndef MW_CARPAINT_COMMON_INCLUDED
#define MW_CARPAINT_COMMON_INCLUDED

struct MWAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct MWVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3  normalWS   : TEXCOORD2;
    half4  tangentWS  : TEXCOORD3;   // w: bitanjant işareti
    float3 positionOS : TEXCOORD4;
    half4  fogVL      : TEXCOORD5;   // x: sis faktörü, yzw: köşe ışıkları
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// ---- gürültü yardımcıları ----
float3 MWHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float MWValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = MWHash33(i).x;
    float n100 = MWHash33(i + float3(1, 0, 0)).x;
    float n010 = MWHash33(i + float3(0, 1, 0)).x;
    float n110 = MWHash33(i + float3(1, 1, 0)).x;
    float n001 = MWHash33(i + float3(0, 0, 1)).x;
    float n101 = MWHash33(i + float3(1, 0, 1)).x;
    float n011 = MWHash33(i + float3(0, 1, 1)).x;
    float n111 = MWHash33(i + float3(1, 1, 1)).x;
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

float MWFbm(float3 p)
{
    return MWValueNoise(p) * 0.55 + MWValueNoise(p * 2.13) * 0.3 + MWValueNoise(p * 4.37) * 0.15;
}

MWVaryings MWVert(MWAttributes v)
{
    MWVaryings o = (MWVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
    o.positionCS = p.positionCS;
    o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
    o.positionOS = v.positionOS.xyz;
#ifdef MW_FORWARD
    o.fogVL = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
#endif
    return o;
}

#ifdef MW_FORWARD
InputData MWInputData(MWVaryings i, half3 normalWS)
{
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.positionCS = i.positionCS;
    d.normalWS = NormalizeNormalPerPixel(normalWS);
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    d.fogCoord = i.fogVL.x;
    d.vertexLighting = i.fogVL.yzw;
    d.bakedGI = SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    return d;
}
#endif

// ---------------------------------------------------------------- gölge / derinlik geçişleri
#ifdef MW_SHADOWCASTER
float3 _LightDirection;
float3 _LightPosition;

float4 MWShadowVert(MWAttributes v) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(v);
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDir = normalize(_LightPosition - positionWS);
#else
    float3 lightDir = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

half4 MWShadowFrag() : SV_Target { return 0; }
#endif

#ifdef MW_DEPTHONLY
float4 MWDepthVert(MWAttributes v) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(v);
    return TransformObjectToHClip(v.positionOS.xyz);
}
half4 MWDepthFrag() : SV_Target { return 0; }
#endif

#ifdef MW_DEPTHNORMALS
struct MWDNVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
MWDNVaryings MWDepthNormalsVert(MWAttributes v)
{
    MWDNVaryings o = (MWDNVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}
half4 MWDepthNormalsFrag(MWDNVaryings i) : SV_Target
{
    half3 n = NormalizeNormalPerPixel(i.normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 oct = PackNormalOctQuadEncode(n);
    float2 remapped = saturate(oct * 0.5 + 0.5);
    return half4(PackFloat2To888(remapped), 0.0);
#else
    return half4(n, 0.0);
#endif
}
#endif

#endif // MW_CARPAINT_COMMON_INCLUDED
