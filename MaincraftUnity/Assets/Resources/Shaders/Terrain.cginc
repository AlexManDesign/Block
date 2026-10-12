// Terrain shader of the original (TERRAIN_VS / TERRAIN_FS), reading the mesher's packed vertices
// straight from GPU buffers. One instance = one record = up to 64 quads of one section:
//   record: x = first quad | (quads - 1) << 26, yzw = section origin minus the camera block (blocks),
//           or for a vessel its section's origin in the vessel's grid
//   vertex: 3 uints  w0 = x | y << 10 | z << 20 (1/32 block) | sway << 30
//                    w1 = layer | u << 11 | v << 16 (1/16) | shade << 21 | overlay << 29 | uvRot << 30
//                    w2 = sky light | block light << 8 | tint (RGB565) << 16
#include "Common.cginc"
#include "TexArr.cginc"

StructuredBuffer<uint> _Data;
StructuredBuffer<int4> _Rec;
StructuredBuffer<uint> _Anim;      // per layer: frames | ticks << 8
float _RecBase;
float3 _CamI;                      // camera block (whole numbers)
float3 _CamF;                      // camera position inside it
float _MTime, _MTick, _Sway, _AlphaMul, _Overlay;
float2 _Fog;
float3 _FogColor;
float4 _ShipRot;                   // cos, sin of a vessel's yaw
float3 _ShipRel;                   // its wheel cell's centre relative to the camera
sampler2D _LM;

struct v2f
{
    float4 pos : SV_POSITION;
    float3 uv : TEXCOORD0;
    float3 lit : TEXCOORD1;
    float fog : TEXCOORD2;
    nointerpolation float3 tint : TEXCOORD3;
    nointerpolation float ovl : TEXCOORD4;
};

v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    v2f o = (v2f)0;
    int4 r = _Rec[(uint)_RecBase + iid];
    uint rx = (uint)r.x;
    uint q = vid >> 2;
    if (q > (rx >> 26)) { o.pos = float4(2.0, 2.0, 2.0, 1.0); return o; }   // past the record's quads: outside the view
    uint b = ((((rx & 0x3FFFFFFu) + q) << 2) + (vid & 3u)) * 3u;
    uint w0 = _Data[b], w1 = _Data[b + 1], w2 = _Data[b + 2];
    float3 p = float3(w0 & 1023u, (w0 >> 10) & 1023u, (w0 >> 20) & 1023u) * 0.03125;
    float3 org = float3(r.yzw);
#ifdef SHIP
    // a vessel: its section origin in its own grid, turned about the wheel cell and moved there
    float3 sq = org + p - float3(0.5, 0.0, 0.5);
    float3 wp = float3(_ShipRot.x * sq.x - _ShipRot.y * sq.z, sq.y, _ShipRot.y * sq.x + _ShipRot.x * sq.z) + _ShipRel;
#else
    // camera-relative: integer parts first, so the result is exact far from the world origin
    float3 wp = org - _CamF + p;
#endif
    uint fl = w0 >> 30;
    if (fl != 0u && _Sway > 0.0)
    {
        float3 a = org + _CamI + p;
        float t = _MTime;
        if (fl == 1u) { wp.x += sin(t * 1.7 + a.x * 0.6 + a.z * 0.35) * 0.065 * _Sway; wp.z += cos(t * 1.3 + a.z * 0.5 + a.x * 0.2) * 0.05 * _Sway; }
        else if (fl == 2u) { wp.x += sin(t * 1.1 + a.x * 0.5 + a.y * 0.4 + a.z * 0.3) * 0.022 * _Sway; wp.y += cos(t * 1.3 + a.z * 0.45 + a.x * 0.25) * 0.012 * _Sway; }
    }
    o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
    uint layer = w1 & 2047u;
    uint an = _Anim[layer];
    uint frames = an & 255u;
    if (frames > 1u) layer += ((uint)_MTick / max(an >> 8, 1u)) % frames;
    o.uv = float3(((w1 >> 11) & 31u) * 0.0625, ((w1 >> 16) & 31u) * 0.0625, (float)layer);
    // a flowing liquid running diagonally: its tile turned by 45 degrees about the window's centre
    if (((w1 >> 30) & 1u) == 1u) { float2 d = o.uv.xy - 0.5; o.uv.xy = 0.5 + float2(d.x + d.y, d.y - d.x) * 0.70710678; }
    // light colour per vertex like Minecraft's terrain shaders: lightmap(sky, block) * face shade / AO
    float2 li = float2(w2 & 255u, (w2 >> 8) & 255u) / 240.0;
    o.lit = tex2Dlod(_LM, float4(li.y * 0.9375 + 0.03125, li.x * 0.9375 + 0.03125, 0, 0)).rgb * (((w1 >> 21) & 255u) / 255.0);
    uint tn = w2 >> 16;
    o.tint = float3(tn >> 11, (tn >> 5) & 63u, tn & 31u) * float3(1.0 / 31.0, 1.0 / 63.0, 1.0 / 31.0);
    o.ovl = (float)((w1 >> 29) & 1u);
    float d = max(length(wp.xz), abs(wp.y) * 0.5);
    o.fog = saturate((d - _Fog.x) / (_Fog.y - _Fog.x));
    return o;
}

float4 frag(v2f i) : SV_Target
{
    float lod = MipLod(i.uv.xy);
    float4 c = SampleArr(i.uv, lod);
#ifdef CUTOUT
    if (c.a < 0.5) discard;
#endif
    // biome colour; the grass block side lays its tinted grass overlay over the dirt base
    if (i.ovl > 0.5) { float4 ov = SampleArr(float3(i.uv.xy, _Overlay), lod); c.rgb = lerp(c.rgb, ov.rgb * i.tint, ov.a); }
    else c.rgb *= i.tint;
    float3 rgb = c.rgb * i.lit;
    rgb = lerp(rgb, _FogColor, i.fog);
#ifdef TRANS
    return float4(OutCol(rgb), c.a * _AlphaMul);
#else
    return float4(OutCol(rgb), 1.0);
#endif
}
