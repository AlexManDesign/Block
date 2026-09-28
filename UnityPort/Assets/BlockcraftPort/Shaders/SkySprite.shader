Shader "Blockcraft/SkySprite"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Background+1" "RenderType"="TransparentCutout" }
        Cull Off
        ZWrite Off
        ZTest Less
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v){v2f o;o.pos=mul(UNITY_MATRIX_VP,float4(v.vertex.xyz,1.0));o.uv=v.uv;return o;}
            fixed4 frag(v2f i):SV_Target { fixed4 c=tex2D(_MainTex,i.uv); clip(c.a-.5); return fixed4(c.rgb,1.0); }
            ENDCG
        }
    }
}
