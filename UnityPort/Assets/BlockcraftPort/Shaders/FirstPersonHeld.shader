Shader "Blockcraft/FirstPersonHeld"
{
    Properties { _MainTex("Atlas",2D)="white"{} _HandTex("Hand",2D)="white"{} _ItemTex("Items",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off
        ZWrite On
        ZTest Less
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex; sampler2D _HandTex; sampler2D _ItemTex; sampler2D _VoxelLightmap;
            float4x4 _HeldMVP; half _HeldSky; half _HeldBlock; half _TextureMode; half4 _Tint;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 ssb:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; half2 uv:TEXCOORD0; half shade:TEXCOORD1; };
            v2f vert(appdata v){ v2f o; o.pos=mul(_HeldMVP,v.vertex); o.uv=v.uv; o.shade=v.ssb.x; return o; }
            half4 frag(v2f i):SV_Target
            {
                half4 tc;
                if(_TextureMode<.5) tc=tex2D(_MainTex,i.uv);
                else if(_TextureMode<1.5) tc=tex2D(_HandTex,i.uv);
                else tc=tex2D(_ItemTex,i.uv)*_Tint;
                clip(tc.a-.5);
                half2 lmuv=half2((saturate(_HeldBlock)*15.0+.5)/16.0,(saturate(_HeldSky)*15.0+.5)/16.0);
                half3 lm=tex2D(_VoxelLightmap,lmuv).rgb;
                return half4(tc.rgb*i.shade*lm,1.0);
            }
            ENDCG
        }
    }
}
