// Sun and moon billboards (SPRITE_FS with uKeyBlack): black keyed out, added onto the sky, hidden
// below the sea horizon.
Shader "Maincraft/Celestial"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Geometry+501" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Common.cginc"
            sampler2D _MainTex;
            float _KeyBlack;
            float2 _SeaClip;   // eye height over the sea, drawn distance
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 p : TEXCOORD1; };
            v2f vert(float4 v : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = AtFar(UnityObjectToClipPos(v));
                o.uv = uv;
                o.p = v.xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                // the sun or moon below the horizon is behind the sea: where the way to it meets the sea level
                // within the drawn distance
                if (_SeaClip.x > 0.0) { float3 d = normalize(i.p); if (d.y < 0.0 && -d.y * _SeaClip.y > _SeaClip.x * length(d.xz)) discard; }
                float4 c = tex2D(_MainTex, i.uv);
                if (_KeyBlack > 0.5) { float m = max(c.r, max(c.g, c.b)); c.a = min(c.a, m * 2.0); }
                if (c.a < 0.01) discard;
                return float4(OutCol(c.rgb), c.a);
            }
            ENDCG
        }
    }
}
