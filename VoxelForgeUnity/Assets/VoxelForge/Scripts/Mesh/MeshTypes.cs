// Voxel Forge — Unity port. Mesh data types shared by mesher threads and the renderer.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoxelForge
{
    /// <summary>Terrain GPU vertex, 24 bytes (reference packs the same data into 24 bytes; Unity requires the
    /// VertexAttribute enum order): Position f32x3 @0, TexCoord0 uv half2 @12, TexCoord1 shadeAlpha f32 @16,
    /// TexCoord2 (tile, light) half2 @20.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TVert
    {
        public float x, y, z;
        public ushort u, v;
        public float sa;
        public ushort tile, light;
        public int Tile { get { return (int)Half.ToFloat(tile); } }
    }

    [StructLayout(LayoutKind.Explicit)] struct FloatBits { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }
    public static class Half
    {
        // f32 -> f16 with the same rounding as the reference packer (round-half-up on bit 12).
        public static ushort FromFloat(float f)
        {
            var cv = new FloatBits { f = f }; uint x = cv.u;
            uint s = (x >> 16) & 32768; int e = (int)((x >> 23) & 255) - 112; uint m = x & 8388607;
            if (e <= 0)
            {
                if (e < -10) return (ushort)s;
                return (ushort)(s | ((((m | 8388608) >> (1 - e)) + 4096) >> 13));
            }
            if (e >= 31) return (ushort)(s | 31744);
            return (ushort)(s | (uint)((e << 10) + ((m + 4096) >> 13)));
        }
        public static float ToFloat(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
            float v;
            if (e == 0) v = m * (1f / 16777216f);
            else if (e == 31) v = m != 0 ? float.NaN : float.PositiveInfinity;
            else v = (1 + m / 1024f) * (float)Math.Pow(2, e - 15);
            return s != 0 ? -v : v;
        }
        static readonly ushort[] small = BuildSmall();
        static ushort[] BuildSmall() { var a = new ushort[2048]; for (int i = 0; i < 2048; i++) a[i] = FromFloat(i); return a; }
        public static ushort FromInt(int i) { return i >= 0 && i < 2048 ? small[i] : FromFloat(i); }
    }

    public sealed class VBuf
    {
        public TVert[] a; public int n;
        public VBuf(int cap = 1024) { a = new TVert[cap]; }
        public void Clear() { n = 0; }
        public void Ensure(int add) { if (n + add > a.Length) { int c = a.Length * 2; while (c < n + add) c *= 2; Array.Resize(ref a, c); } }
        public TVert[] ToArray(int from = 0) { var r = new TVert[n - from]; Array.Copy(a, from, r, 0, n - from); return r; }
    }
    public sealed class IBuf
    {
        public int[] a; public int n;
        public IBuf(int cap = 1536) { a = new int[cap]; }
        public void Clear() { n = 0; }
        public void Ensure(int add) { if (n + add > a.Length) { int c = a.Length * 2; while (c < n + add) c *= 2; Array.Resize(ref a, c); } }
        public void Add6(int p0, int p1, int p2, int p3, int p4, int p5) { Ensure(6); var q = a; int k = n; q[k] = p0; q[k + 1] = p1; q[k + 2] = p2; q[k + 3] = p3; q[k + 4] = p4; q[k + 5] = p5; n = k + 6; }
        public int[] ToArray(int from = 0) { var r = new int[n - from]; Array.Copy(a, from, r, 0, n - from); return r; }
    }

    /// <summary>One CPU geometry bucket (vertices + indices). Indices are relative to the bucket's own vertex array.</summary>
    public sealed class GeoBucket
    {
        public TVert[] v; public int[] i;
        public static readonly GeoBucket Empty = new GeoBucket { v = new TVert[0], i = new int[0] };
        public int VCount { get { return v != null ? v.Length : 0; } }
        public int ICount { get { return i != null ? i.Length : 0; } }
    }
    /// <summary>Per-section geometry in four buckets: opaque, cutout, water, transparent.</summary>
    public sealed class SectionGeo
    {
        public GeoBucket o, c, w, t;
        public GeoBucket Get(int k) { return k == 0 ? o : k == 1 ? c : k == 2 ? w : t; }
        public bool Any { get { return o.ICount > 0 || c.ICount > 0 || w.ICount > 0 || t.ICount > 0; } }
    }

    /// <summary>Unified block descriptor for the mesher (native workerBlockCfg or virtual catalog entry).</summary>
    public sealed class MDef
    {
        public int side = -1, top = -1, bottom = -1, front = -1, frontL = -1, frontR = -1, topL = -1, topR = -1, corner = -1, partTop = -1, partBottom = -1;
        public bool solid, transparent, cutout, plant, waterPlant, needsWater, axislog, isVirtual;
        public double alpha = 1;
        public string special, shape;
        public int light;
        /// <summary>b.shape || b.special</summary>
        public string sp { get { return shape ?? special; } }
    }

    public sealed class SecPayload { public int dx, dz, si; public byte[] blocks, light; }
    public struct MetaEntry { public int x, iy, z; public JObj m; public MetaEntry(int a, int b, int c, JObj o) { x = a; iy = b; z = c; m = o; } }

    public sealed class MeshJob
    {
        public int id, cx, cz, rev; public string key;
        public List<SecPayload> sections; public List<MetaEntry> meta; public int[] sis; public bool full;
        public int[] ctx; public int geoEpoch, chunkToken; public bool initial;
    }
    public sealed class MeshResult
    {
        public int id, cx, cz, rev; public double ms; public bool full; public int[] sis;
        public SectionGeo[] full24;               // full: section geometry for all 24 sections (null = empty)
        public GeoBucket aggO, aggC, aggW, aggT;  // full: concatenated chunk buckets (built on the mesher thread)
        public List<KeyValuePair<int, SectionGeo>> parts; // partial: per requested section
        public int[] ctx; public int geoEpoch, chunkToken; public bool initial;
    }
}
