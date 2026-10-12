// The frame's dynamic vertices (Renderer.Dyn): position (camera-relative), uv + array layer, colour.
struct DynV { float3 pos; float3 uv; float4 col; };
StructuredBuffer<DynV> _Dyn;
float _VBase;
DynV DynVertex(uint vid) { return _Dyn[(uint)_VBase + vid]; }
float DynFog(float3 p, float2 fog) { return saturate((max(length(p.xz), abs(p.y) * 0.5) - fog.x) / (fog.y - fog.x)); }
