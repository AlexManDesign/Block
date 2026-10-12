// Shared helpers of the sky, sprite and cloud shaders.
#include "UnityCG.cginc"

// the original works on sRGB values as they are; in a linear-space project the result is converted
float3 OutCol(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return float3(GammaToLinearSpaceExact(c.r), GammaToLinearSpaceExact(c.g), GammaToLinearSpaceExact(c.b));
#endif
}

// a position on the far plane (the original's depthRange(1, 1)): passes LEqual only where nothing was drawn
float4 AtFar(float4 p)
{
#if defined(UNITY_REVERSED_Z)
    p.z = p.w * 1e-6;
#else
    p.z = p.w * (1.0 - 1e-6);
#endif
    return p;
}
