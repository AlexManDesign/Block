// ENT_FS of the original: entity models textured from their skins, per-vertex light, fogged.
Shader "Maincraft/Ent"
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
            sampler2D _MainTex;
            float2 _Fog; float3 _FogColor;
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 col : COLOR; float fog : TEXCOORD1; };
            v2f vert(uint vid : SV_VertexID)
            {
                DynV v = DynVertex(vid);
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(v.pos, 1.0));
                o.uv = v.uv; o.col = v.col;
                o.fog = DynFog(v.pos, _Fog);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv.xy);
                if (c.a < 0.1) discard;
                c.rgb *= i.col.rgb;
                return float4(OutCol(lerp(c.rgb, _FogColor, i.fog)), c.a);
            }
            ENDCG
        }
    }
}
