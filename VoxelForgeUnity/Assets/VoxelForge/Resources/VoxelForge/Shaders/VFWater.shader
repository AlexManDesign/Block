// Water colour pass (alpha from geometry only) and the depth-only pre-pass.
Shader "VoxelForge/Water"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _WaterPass ("Pass", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull Off
            ZTest [_ZTest]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #include "VFCommon.cginc"
            float _WaterPass;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 mat : TEXCOORD1;  // tile, shade, alpha, class
                float3 ld : TEXCOORD2;
            };
            v2f vert(VFTerrainIn v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv;
                float cls, shade, alpha, sky, block, dist;
                vfDecode(v.sa, v.tl.y, v.pos, cls, shade, alpha, sky, block, dist);
                o.mat = float4(v.tl.x, shade, alpha, cls);
                o.ld = float3(sky, block, dist);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_WaterPass > 0.5 && _WaterPass < 1.5 && i.mat.w > 0.5) discard;
                if (_WaterPass > 1.5 && i.mat.w < 0.5) discard;
                float2 q = i.uv;
                if (abs(i.mat.x - _VF_FlowTile) < 0.25) { q *= 0.5; q.y -= _VF_Time * 0.42; }
                float2 f = lerp(float2(0.035, 0.035), float2(0.965, 0.965), frac(q));
                float4 t = vfSampleAtlas(f, i.mat.x, vfAtlasLod(q));
                // Water opacity is controlled by the material alpha packed in geometry.
                float a = i.mat.z;
                if (a < 0.01) discard;
                float3 c = t.rgb * i.mat.y * vfLightmap(i.ld.y, i.ld.x);
                float fog = smoothstep(_VF_FogNear, _VF_FogFar, i.ld.z);
                return float4(lerp(c, _VF_Fog.rgb, fog), a);
            }
            ENDCG
        }
    }
}
