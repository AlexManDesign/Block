// Solid-colour lines (lineProg): selection outline, projectiles, fishing line, primed TNT outline.
Shader "VoxelForge/Line"
{
    Properties { _Color ("Color", Color) = (0,0,0,0.85) }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull Off
            ZTest Less
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 vert(float3 pos : POSITION) : SV_POSITION { return UnityObjectToClipPos(float4(pos, 1.0)); }
            float4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
