Shader "Blockcraft/VoxelWater"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} _Alpha ("Alpha", Range(0,1)) = 0.62 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Less
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex; sampler2D _VoxelLightmap;
            half _VoxelWorldLight; half4 _VoxelFogColor; float _VoxelFogNear; float _VoxelFogFar; float _VoxelHeldLight; half _Alpha;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float3 ssb:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; half2 uv:TEXCOORD0; half3 ssb:TEXCOORD1; float dist:TEXCOORD2; };
            v2f vert(appdata v) { v2f o; o.pos=mul(UNITY_MATRIX_VP,float4(v.vertex.xyz,1.0));o.uv=v.uv;o.dist=distance(v.vertex.xyz,_WorldSpaceCameraPos.xyz);o.ssb=v.ssb;o.ssb.z=max(v.ssb.z,(_VoxelHeldLight-o.dist)/15.0);return o; }
            half4 frag(v2f i):SV_Target { half4 tc=tex2D(_MainTex,i.uv); half2 lmuv=half2((saturate(i.ssb.z)*15.0+.5)/16.0,(saturate(i.ssb.y)*15.0+.5)/16.0); half3 lm=tex2D(_VoxelLightmap,lmuv).rgb*_VoxelWorldLight; half3 rgb=tc.rgb*i.ssb.x*lm; half fog=(half)saturate((i.dist-_VoxelFogNear)/max(.001,_VoxelFogFar-_VoxelFogNear));rgb=lerp(rgb,_VoxelFogColor.rgb,fog);return half4(rgb,tc.a*_Alpha); }
            ENDCG
        }
    }
}
