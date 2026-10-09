// Voxel Forge — shared shader code (ports of the WebGL2 terrain/water GLSL).
#ifndef VF_COMMON_INCLUDED
#define VF_COMMON_INCLUDED
#include "UnityCG.cginc"

UNITY_DECLARE_TEX2DARRAY(_VF_Atlas);   // 16x16 tiles, 5 mips, point filtered
sampler2D _VF_Lightmap;                // 16x16, bilinear, clamp
float4 _VF_Cam;                        // eye position
float4 _VF_Fog;                        // fog colour
float _VF_FogNear, _VF_FogFar, _VF_Light, _VF_Hand, _VF_Time, _VF_FlowTile;

struct VFTerrainIn
{
    float3 pos : POSITION;
    float2 uv : TEXCOORD0;
    float sa : TEXCOORD1;   // shade + alpha8*2 + class*512
    float2 tl : TEXCOORD2;  // tile, light (sky4:block4)
};

// GL textureLod with NEAREST_MIPMAP_LINEAR: two point-sampled mips blended by frac(lod).
float4 vfSampleAtlas(float2 f, float tile, float lod)
{
    float l0 = floor(lod), l1 = min(l0 + 1.0, 4.0), fr = lod - l0;
    float4 a = UNITY_SAMPLE_TEX2DARRAY_LOD(_VF_Atlas, float3(f, tile), l0);
    if (fr <= 0.0) return a;
    float4 b = UNITY_SAMPLE_TEX2DARRAY_LOD(_VF_Atlas, float3(f, tile), l1);
    return lerp(a, b, fr);
}
float vfAtlasLod(float2 uv)
{
    float2 lgx = ddx(uv), lgy = ddy(uv);
    float ld2 = max(dot(lgx, lgx), 1e-12), ld3 = max(dot(lgy, lgy), 1e-12);
    return clamp(min(0.5 * log2(max(ld2, ld3)), 0.5 * log2(min(ld2, ld3)) + 1.0) + 4.0, 0.0, 4.0);
}
float3 vfLightmap(float block, float sky)
{
    return tex2D(_VF_Lightmap, float2((block * 15.0 + 0.5) / 16.0, (sky * 15.0 + 0.5) / 16.0)).rgb * _VF_Light;
}
void vfDecode(float sa, float lightPacked, float3 w, out float cls, out float shade, out float alpha, out float sky, out float block, out float dist)
{
    cls = floor(sa / 512.0);
    float q = sa - cls * 512.0;
    float a8 = floor(q * 0.5);
    shade = q - a8 * 2.0;
    alpha = a8 * (1.0 / 255.0);
    float sky4 = floor(lightPacked / 16.0), blk4 = lightPacked - sky4 * 16.0;
    dist = distance(w, _VF_Cam.xyz);
    sky = sky4 * (1.0 / 15.0);
    block = max(blk4 * (1.0 / 15.0), max(0.0, (_VF_Hand - dist) / 15.0));
}
float3 vfUnderwaterTint(float3 col, float3 w, float dist)
{
    if (_VF_FogFar > 30.0 && w.y < 63.5)
    {
        float depth = max(0.0, 63.5 - w.y);
        float wf = clamp(1.0 - exp(-(depth * 0.055 + max(dist - 6.0, 0.0) * 0.015)), 0.0, 0.72);
        col = lerp(col, float3(0.07, 0.24, 0.45), wf);
    }
    return col;
}
#endif
