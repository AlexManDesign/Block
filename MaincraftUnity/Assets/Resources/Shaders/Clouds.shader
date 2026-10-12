// Clouds of the original (drawClouds): a depth-only pass, then the colour where the depth matches, so
// overlapping cloud faces blend once.
Shader "Maincraft/Clouds"
{
    Properties { [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Geometry+502" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        CGINCLUDE
        #include "Common.cginc"
        float3 _Mul, _FogColor;
        float2 _CloudFog;
        struct v2f { float4 pos : SV_POSITION; float4 col : COLOR; float fog : TEXCOORD0; };
        v2f vert(float4 v : POSITION, float4 col : COLOR)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v);
            o.col = col;
            o.fog = saturate((max(length(v.xz), abs(v.y) * 0.5) - _CloudFog.x) / (_CloudFog.y - _CloudFog.x));
            return o;
        }
        ENDCG
        Pass
        {
            Cull [_Cull] ZWrite On ZTest LEqual ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            float4 fragDepth(v2f i) : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Cull [_Cull] ZWrite Off ZTest Equal
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 c = i.col;
                c.rgb *= _Mul;
                return float4(OutCol(lerp(c.rgb, _FogColor, i.fog)), c.a);
            }
            ENDCG
        }
    }
}
