// Terrain: opaque (fast, no alpha), cutout (alpha test) and translucent (blend) variants via render-state properties.
Shader "VoxelForge/Terrain"
{
    Properties
    {
        [Toggle(VF_ALPHA)] _VFAlpha ("Alpha test", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
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
            #pragma multi_compile_local __ VF_ALPHA
            #include "VFCommon.cginc"
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float3 mat : TEXCOORD1;
                float3 ld : TEXCOORD2;
                float3 world : TEXCOORD3;
            };
            v2f vert(VFTerrainIn v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv;
                float cls, shade, alpha, sky, block, dist;
                vfDecode(v.sa, v.tl.y, v.pos, cls, shade, alpha, sky, block, dist);
#ifdef VF_ALPHA
                o.mat = float3(v.tl.x, shade, alpha);
#else
                o.mat = float3(v.tl.x, fmod(v.sa, 2.0), 1.0);
#endif
                o.ld = float3(sky, block, dist);
                o.world = v.pos;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 f = lerp(float2(0.035, 0.035), float2(0.965, 0.965), frac(i.uv));
                float4 t = vfSampleAtlas(f, i.mat.x, vfAtlasLod(i.uv));
#ifdef VF_ALPHA
                float a = t.a * i.mat.z;
                if (a < 0.12) discard;
#else
                float a = 1.0;
#endif
                float3 c = t.rgb * i.mat.y * vfLightmap(i.ld.y, i.ld.x);
                float fog = smoothstep(_VF_FogNear, _VF_FogFar, i.ld.z);
                float3 col = lerp(c, _VF_Fog.rgb, fog);
                col = vfUnderwaterTint(col, i.world, i.ld.z);
                return float4(col, a);
            }
            ENDCG
        }
    }
}
