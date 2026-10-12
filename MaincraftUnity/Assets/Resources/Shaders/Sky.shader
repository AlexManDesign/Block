// Sky of the original (SKY_FS): zenith to horizon gradient, sunset glow, stars. Drawn as a cube round
// the camera after the opaque terrain, on the far plane, so it only fills what the terrain left empty.
Shader "Maincraft/Sky"
{
    SubShader
    {
        Tags { "Queue"="Geometry+500" "RenderType"="Background" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Common.cginc"
            float3 _Zenith, _Horizon, _SunDir;
            float4 _Sunset;
            float _Stars, _Void;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.pos = AtFar(UnityObjectToClipPos(v));
                o.dir = v.xyz;
                return o;
            }
            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float t = pow(saturate(1.0 - max(h, 0.0)), 5.0);
                float3 col = lerp(_Zenith, _Horizon, t);
                if (h < 0.0) col = lerp(_Horizon, _Horizon * _Void, saturate(-h * 3.0));
                // sunrise / sunset glow
                float sd = max(dot(d, normalize(float3(_SunDir.x, 0.0, _SunDir.z))), 0.0);
                float band = exp(-abs(h - 0.02) * 7.0) * pow(sd, 3.0);
                col = lerp(col, _Sunset.rgb, saturate(band * _Sunset.a));
                // stars
                if (_Stars > 0.0 && h > 0.0)
                {
                    float3 q = floor(d * 380.0);
                    float s = hash(q);
                    if (s > 0.9975) col += (_Stars * (s - 0.9975) * 400.0 * saturate(h * 4.0)).xxx;
                }
                return float4(OutCol(col), 1.0);
            }
            ENDCG
        }
    }
}
