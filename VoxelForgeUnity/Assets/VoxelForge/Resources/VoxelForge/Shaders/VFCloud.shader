// Flat cloud layer (cloudProg).
Shader "VoxelForge/Cloud"
{
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull Off
            ZTest LEqual
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _VF_Cam, _VF_Fog, _VF_CloudOff;
            struct v2f { float4 pos : SV_POSITION; float dist : TEXCOORD0; };
            v2f vert(float3 pos : POSITION)
            {
                v2f o; float3 w = pos + _VF_CloudOff.xyz;
                o.pos = UnityObjectToClipPos(float4(w, 1.0));
                o.dist = distance(w, _VF_Cam.xyz);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float f = smoothstep(170.0, 260.0, i.dist);
                return float4(lerp(float3(0.98, 0.98, 0.98), _VF_Fog.rgb, f), 0.45 * (1.0 - f));
            }
            ENDCG
        }
    }
}
