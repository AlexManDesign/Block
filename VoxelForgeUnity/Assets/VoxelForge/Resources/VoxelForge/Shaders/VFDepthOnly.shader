// Depth-only pass (water depth pre-pass).
Shader "VoxelForge/DepthOnly"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZTest Less
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float3 pos : POSITION) : SV_POSITION { return UnityObjectToClipPos(float4(pos, 1.0)); }
            float4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
