Shader "Blockcraft/EntityXray"
{
    Properties { _MainTex ("Entity Atlas", 2D) = "white" {} _AlphaCut ("Alpha Cut", Range(0,1)) = 0.5 }
    SubShader
    {
        Tags { "RenderType"="Cutout" "Queue"="AlphaTest+5" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex; half4 _VoxelFogColor; float _VoxelFogNear; float _VoxelFogFar; half _AlphaCut;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 ssb:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; half2 uv:TEXCOORD0; float dist:TEXCOORD1; half shade:TEXCOORD2; };
            v2f vert(appdata v){v2f o;o.pos=mul(UNITY_MATRIX_VP,float4(v.vertex.xyz,1.0));o.uv=v.uv;o.shade=v.ssb.x;o.dist=distance(v.vertex.xyz,_WorldSpaceCameraPos.xyz);return o;}
            half4 frag(v2f i):SV_Target
            {
                half4 tc=tex2D(_MainTex,i.uv); clip(tc.a-_AlphaCut); half3 rgb=tc.rgb*i.shade;
                half fog=(half)saturate((i.dist-_VoxelFogNear)/max(.001,_VoxelFogFar-_VoxelFogNear)); rgb=lerp(rgb,_VoxelFogColor.rgb,fog); return half4(rgb,1.0);
            }
            ENDCG
        }
    }
}
