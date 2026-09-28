Shader "Blockcraft/VoxelXray"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} _WireAlpha ("Wire Alpha", Range(0,1)) = 0.5 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off

        // main.js uXray==1: non-classified terrain is a depth-independent textured wireframe.
        Pass
        {
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vertWire
            #pragma fragment fragWire
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _WireAlpha;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 ssb:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; half2 uv:TEXCOORD0; half shade:TEXCOORD1; half cls:TEXCOORD2; float3 world:TEXCOORD3; };
            v2f vertWire(appdata v)
            {
                v2f o; o.pos=mul(UNITY_MATRIX_VP,float4(v.vertex.xyz,1.0)); o.uv=v.uv; o.world=v.vertex.xyz;
                float sh=v.ssb.x; o.cls=0.0; if(sh>2.0){o.cls=1.0;sh-=4.0;} o.shade=sh; return o;
            }
            half4 fragWire(v2f i):SV_Target
            {
                clip(.5-i.cls);
                float3 ed=min(frac(i.world),1.0-frac(i.world));
                float mn=min(ed.x,min(ed.y,ed.z)); float mx=max(ed.x,max(ed.y,ed.z));
                float mid=ed.x+ed.y+ed.z-mn-mx; float w=fwidth(mid)+.012;
                float line=1.0-smoothstep(0.0,w,mid); clip(line-.04);
                return half4(tex2D(_MainTex,i.uv).rgb*(.5+.5*i.shade),(half)(line*_WireAlpha));
            }
            ENDCG
        }

        // main.js uXray==2: classified blocks are solid, unlit, alpha-tested and depth-test only
        // against other classified geometry. The wire pass above writes no depth, matching that ordering.
        Pass
        {
            ZWrite On
            ZTest Less
            Blend Off
            CGPROGRAM
            #pragma vertex vertClass
            #pragma fragment fragClass
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 ssb:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; half2 uv:TEXCOORD0; half cls:TEXCOORD1; };
            v2f vertClass(appdata v){v2f o;o.pos=mul(UNITY_MATRIX_VP,float4(v.vertex.xyz,1.0));o.uv=v.uv;o.cls=v.ssb.x>2.0?1.0:0.0;return o;}
            half4 fragClass(v2f i):SV_Target { clip(i.cls-.5); half4 tc=tex2D(_MainTex,i.uv); clip(tc.a-.5); return tc; }
            ENDCG
        }
    }
}
