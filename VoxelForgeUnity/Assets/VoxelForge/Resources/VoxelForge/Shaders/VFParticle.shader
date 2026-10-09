// Block-break particles (breakParticleProg): terrain atlas, tint * shade * lightmap, fog.
Shader "VoxelForge/Particle"
{
    SubShader
    {
        Tags { "Queue"="Geometry" }
        Pass
        {
            Cull Off
            ZTest Less
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #include "VFCommon.cginc"
            struct appdata { float3 pos : POSITION; float2 uv : TEXCOORD0; float3 tsl : TEXCOORD1; float3 tint : TEXCOORD2; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 ts : TEXCOORD1; nointerpolation float3 tint : TEXCOORD2; float3 ld : TEXCOORD3; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv; o.ts = v.tsl.xy; o.tint = v.tint;
                float sky4 = floor(v.tsl.z / 16.0), blk4 = v.tsl.z - sky4 * 16.0, dist = distance(v.pos, _VF_Cam.xyz);
                float block = max(blk4 * (1.0 / 15.0), max(0.0, (_VF_Hand - dist) / 15.0));
                o.ld = float3(sky4 * (1.0 / 15.0), block, dist);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 f = lerp(float2(0.035, 0.035), float2(0.965, 0.965), frac(i.uv));
                float4 t = vfSampleAtlas(f, i.ts.x, vfAtlasLod(i.uv));
                if (t.a < 0.12) discard;
                float3 c = t.rgb * i.tint * i.ts.y * vfLightmap(i.ld.y, i.ld.x);
                float fog = smoothstep(_VF_FogNear, _VF_FogFar, i.ld.z);
                return float4(lerp(c, _VF_Fog.rgb, fog), t.a);
            }
            ENDCG
        }
    }
}
