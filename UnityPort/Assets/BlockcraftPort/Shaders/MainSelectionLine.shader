Shader "Blockcraft/MainSelectionLine"
{
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off
        ZTest Less
        ZWrite On
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float3 _Origin;
            float3 _Size;
            float _Visible;
            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 pos:SV_POSITION; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 world=_Origin+v.vertex.xyz*_Size;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1.0));
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                clip(_Visible-.5);
                return fixed4(0,0,0,.85);
            }
            ENDCG
        }
    }
}
