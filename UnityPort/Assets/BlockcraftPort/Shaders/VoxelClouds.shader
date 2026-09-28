Shader "Blockcraft/VoxelClouds"
{
    Properties { _Alpha ("Alpha", Range(0,1)) = 0.45 }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }
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
            sampler2D _VoxelLightmap;
            float _VoxelWorldLight;
            float4 _VoxelFogColor;
            float _Alpha;
            float4 _CloudOffset;
            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 pos:SV_POSITION; float dist:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 world=v.vertex.xyz+_CloudOffset.xyz;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1.0));
                o.dist=distance(world,_WorldSpaceCameraPos.xyz);
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 lm=tex2D(_VoxelLightmap,float2(0.03125,0.96875)).rgb*_VoxelWorldLight;
                float3 rgb=fixed3(250.0/255.0,250.0/255.0,252.0/255.0)*lm;
                float f=saturate((i.dist-170.0)/90.0);rgb=lerp(rgb,_VoxelFogColor.rgb,f);
                return float4(rgb,_Alpha);
            }
            ENDCG
        }
    }
}
