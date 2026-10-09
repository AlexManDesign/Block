// X-Ray terrain: mode 1 = wireframe-like edge lines of regular blocks, mode 2 = ores/special blocks only.
Shader "VoxelForge/TerrainXray"
{
    Properties
    {
        _XrayMode ("Mode", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" }
        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #include "VFCommon.cginc"
            float _XrayMode;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float tile : TEXCOORD1;
                float4 sad : TEXCOORD2;   // shade, alpha, dist, sky
                float2 bc : TEXCOORD3;    // block, class
                float3 world : TEXCOORD4;
            };
            v2f vert(VFTerrainIn v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv; o.tile = v.tl.x;
                float cls, shade, alpha, sky, block, dist;
                vfDecode(v.sa, v.tl.y, v.pos, cls, shade, alpha, sky, block, dist);
                o.sad = float4(shade, alpha, dist, sky);
                o.bc = float2(block, cls);
                o.world = v.pos;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 f = lerp(float2(0.035, 0.035), float2(0.965, 0.965), frac(i.uv));
                float4 t = vfSampleAtlas(f, i.tile, vfAtlasLod(i.uv));
                float a = t.a * i.sad.y;
                if (a < 0.12) discard;
                if (_XrayMode < 1.5)
                {
                    if (i.bc.y > 0.5) discard;
                    float3 ed = min(frac(i.world), 1.0 - frac(i.world));
                    float mn = min(ed.x, min(ed.y, ed.z)), mx = max(ed.x, max(ed.y, ed.z));
                    float mid = ed.x + ed.y + ed.z - mn - mx;
                    float w = fwidth(mid) + 0.012;
                    float line = 1.0 - smoothstep(0.0, w, mid);
                    if (line < 0.04) discard;
                    return float4(t.rgb * (0.5 + 0.5 * i.sad.x), line * 0.42);
                }
                if (i.bc.y < 0.5) discard;
                return float4(t.rgb, a);
            }
            ENDCG
        }
    }
}
