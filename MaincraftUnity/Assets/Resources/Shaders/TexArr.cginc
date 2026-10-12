// The block texture array, sampled as GL's NEAREST_MIPMAP_LINEAR does (nearest texel in the two
// nearest mip levels, blended between them), which Unity's filter modes do not offer.
UNITY_DECLARE_TEX2DARRAY(_Tex);

float MipLod(float2 uv)
{
    float2 t = uv * 16.0;
    float2 dx = ddx(t), dy = ddy(t);
    return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-12));
}
float4 SampleArr(float3 uvl, float lod)
{
    if (lod <= 0.0) return UNITY_SAMPLE_TEX2DARRAY_LOD(_Tex, uvl, 0.0);
    lod = min(lod, 4.0);
    float l0 = floor(lod);
    float4 a = UNITY_SAMPLE_TEX2DARRAY_LOD(_Tex, uvl, l0);
    float4 b = UNITY_SAMPLE_TEX2DARRAY_LOD(_Tex, uvl, min(l0 + 1.0, 4.0));
    return lerp(a, b, lod - l0);
}
