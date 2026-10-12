// LINE_FS of the original: the block outline and debug lines, one colour.
Shader "Maincraft/Line"
{
    Properties
    {
        _ZTest ("ZTest", Float) = 4
        _ZWrite ("ZWrite", Float) = 1
        _SrcBlend ("SrcBlend", Float) = 1
        _DstBlend ("DstBlend", Float) = 0
        _Cull ("Cull", Float) = 1
        _ColorMask ("ColorMask", Float) = 15
        _OffsetFactor ("OffsetFactor", Float) = 0
        _OffsetUnits ("OffsetUnits", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            ZTest [_ZTest] ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend] Cull [_Cull] ColorMask [_ColorMask]
            Offset [_OffsetFactor], [_OffsetUnits]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Common.cginc"
            #include "Dyn.cginc"
            float4 _Color;
            float4 vert(uint vid : SV_VertexID) : SV_POSITION { return mul(UNITY_MATRIX_VP, float4(DynVertex(vid).pos, 1.0)); }
            float4 frag() : SV_Target { return float4(OutCol(_Color.rgb), _Color.a); }
            ENDCG
        }
    }
}
