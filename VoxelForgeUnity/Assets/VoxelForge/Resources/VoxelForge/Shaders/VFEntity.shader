// Entity models (entityProg): 4x7 grid of 64px skins, hand light, fog.
Shader "VoxelForge/Entity"
{
    Properties
    {
        _MainTex ("Entity atlas", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Geometry" }
        Pass
        {
            Cull Off
            ZTest [_ZTest]
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _VF_Cam, _VF_Fog;
            float _VF_FogNear, _VF_FogFar, _VF_Hand;
            struct appdata { float3 pos : POSITION; float2 uv : TEXCOORD0; float2 ts : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float tile : TEXCOORD1; float shade : TEXCOORD2; float dist : TEXCOORD3; float hand : TEXCOORD4; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv; o.tile = v.ts.x; o.shade = v.ts.y;
                o.dist = distance(v.pos, _VF_Cam.xyz);
                o.hand = max(0.0, (_VF_Hand - o.dist) / 15.0);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float col = fmod(i.tile, 4.0), row = floor(i.tile / 4.0);
                float2 uv = float2((col + clamp(i.uv.x, 0.001, 0.999)) / 4.0, 1.0 - (row + clamp(i.uv.y, 0.001, 0.999)) / 7.0);
                float4 t = tex2Dlod(_MainTex, float4(uv, 0, 0));
                if (t.a < 0.08) discard;
                float3 c = t.rgb * max(i.shade, 0.22 + i.hand * 0.78);
                float f = smoothstep(_VF_FogNear, _VF_FogFar, i.dist);
                return float4(lerp(c, _VF_Fog.rgb, f), t.a);
            }
            ENDCG
        }
    }
}
