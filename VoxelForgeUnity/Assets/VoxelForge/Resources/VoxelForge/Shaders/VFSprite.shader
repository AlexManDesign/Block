// Drop / held-item / active-entity sprite shader (dropProg): grid atlas (terrain 32 or item 16), alpha test, fog.
Shader "VoxelForge/Sprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Grid ("Grid", Float) = 32
        _NoFog ("No fog", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Grid, _NoFog;
            float4 _VF_Cam, _VF_Fog;
            float _VF_FogNear, _VF_FogFar;
            struct appdata { float3 pos : POSITION; float2 uv : TEXCOORD0; float2 ts : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float tile : TEXCOORD1; float shade : TEXCOORD2; float dist : TEXCOORD3; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(float4(v.pos, 1.0));
                o.uv = v.uv; o.tile = v.ts.x; o.shade = v.ts.y;
                o.dist = distance(mul(unity_ObjectToWorld, float4(v.pos, 1.0)).xyz, _VF_Cam.xyz);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float col = fmod(i.tile, _Grid), row = floor(i.tile / _Grid);
                float2 f = lerp(float2(0.035, 0.035), float2(0.965, 0.965), saturate(i.uv));
                float2 uv = float2((col + f.x) / _Grid, 1.0 - (row + (1.0 - f.y)) / _Grid);
                float4 t = tex2Dlod(_MainTex, float4(uv, 0, 0));
                if (t.a < 0.12) discard;
                float3 c = t.rgb * i.shade;
                float fog = _NoFog > 0.5 ? 0.0 : smoothstep(_VF_FogNear, _VF_FogFar, i.dist);
                return float4(lerp(c, _VF_Fog.rgb, fog), t.a);
            }
            ENDCG
        }
    }
}
