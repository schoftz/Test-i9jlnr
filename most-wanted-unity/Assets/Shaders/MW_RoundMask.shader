// Yuvarlak minimap maskesi (IMGUI Graphics.DrawTexture ile kullanılır).
Shader "MostWanted/RoundMask"
{
    Properties
    {
        _MainTex ("Doku", 2D) = "white" {}
        _Edge ("Kenar Yumuşaklığı", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Edge;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                fixed4 c = tex2D(_MainTex, i.uv);
                c.a = 1.0 - smoothstep(1.0 - _Edge, 1.0, d);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
