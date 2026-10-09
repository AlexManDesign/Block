// Voxel Forge — Unity port. Chunk GPU meshes, 2x2 render regions, water quad sorting and render lists.
// The reference keeps per-chunk CPU copies and batches clean 2x2 regions into one GPU mesh whose
// per-chunk index ranges are used when only part of a region is visible. Here a region is one
// UnityEngine.Mesh with overlapping submeshes: [all opaque][opaque per member][all cutout][cutout per member].
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelForge
{
    public sealed class GpuMesh
    {
        public Mesh mesh; public int submesh; public bool wide;
        public RenderRegion sharedRegion; public int regionGeneration;
        public int count; public int tris;
    }
    public sealed class RenderRegion
    {
        public string key; public int rx, rz;
        public Mesh mesh; public bool wide;
        public bool dirty = true; public int chunkCount; public double minY = double.NaN, maxY = double.NaN;
        public double lastBuild; public int generation = 1;
        public List<string> memberKeys = new List<string>(), cutMemberKeys = new List<string>();
        public GpuMesh opaqueAll, cutoutAll;
        public int _tag, _visibleOpaque, _emitTag, _cutTag, _cutVis, _cutEmit; public double _firstDist;
    }
    public sealed partial class Chunk
    {
        public GpuMesh opaque, cutout, water, trans;
        public GeoBucket opaqueCPU, cutoutCPU, waterCPU;
        public bool opaqueNeedsAlphaTest;
        public double meshMinY = double.NaN, meshMaxY = double.NaN;
        public readonly double[] bucketMinY = { double.NaN, double.NaN, double.NaN, double.NaN }, bucketMaxY = { double.NaN, double.NaN, double.NaN, double.NaN };
        public double opaqueMinY { get { return bucketMinY[0]; } }
        public double opaqueMaxY { get { return bucketMaxY[0]; } }
        // water sort state
        public bool hasFallingWater, waterSortDirty; public int waterQuadCount, waterSortRevision, waterSortedRevision = -1;
        public double waterSortEyeX = double.PositiveInfinity, waterSortEyeY = double.PositiveInfinity, waterSortEyeZ = double.PositiveInfinity;
        public double waterSortDirX = double.PositiveInfinity, waterSortDirY = double.PositiveInfinity, waterSortDirZ = double.PositiveInfinity;
        public float[] waterSortKeys; public int[] waterSortOrder; public int[] waterSortIndices;
        // per-frame render bookkeeping
        public int _opaqueVisTag, _opaqueRegionTag; public RenderRegion _opaqueRegionRef; public double _renderDist2, _waterViewDepth;
    }

    public static partial class VF
    {
        public const int RENDER_REGION_SIZE = 2;
        public static readonly Dictionary<string, RenderRegion> renderRegions = new Dictionary<string, RenderRegion>();
        public static readonly OrderedSet<string> dirtyRenderRegions = new OrderedSet<string>();
        public static int renderRegionTag = 0, cutoutBatchTag = 0, regionBuilds = 0, regionBatchedChunks = 0, regionArenaTakeovers = 0;
        public static double avgRegionBuildMs = 0;

        public static readonly VertexAttributeDescriptor[] TERRAIN_LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 2),
        };
        public const MeshUpdateFlags MUF = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        // ---------------- low level mesh upload ----------------
        static readonly Stack<Mesh> meshPool = new Stack<Mesh>();
        static ushort[] idx16Scratch = new ushort[4096];
        static Mesh allocMesh()
        {
            if (meshPool.Count > 0) return meshPool.Pop();
            var m = new Mesh { name = "vf-terrain" };
            m.MarkDynamic();
            return m;
        }
        static void recycleMesh(Mesh m)
        {
            if (m == null) return;
            if (meshPool.Count < 256) meshPool.Push(m); else UnityEngine.Object.Destroy(m);
        }
        static readonly List<SubMeshDescriptor> subScratch = new List<SubMeshDescriptor>();
        /// <summary>Uploads vertices + indices and the given submesh ranges. Returns true when 32-bit indices were used.</summary>
        public static bool writeTerrainMesh(Mesh mesh, TVert[] v, int vn, int[] idx, int inN, List<SubMeshDescriptor> subs, Bounds b)
        {
            mesh.SetVertexBufferParams(vn, TERRAIN_LAYOUT);
            mesh.SetVertexBufferData(v, 0, 0, vn, 0, MUF);
            bool wide = vn > 65535;
            mesh.SetIndexBufferParams(inN, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (wide) mesh.SetIndexBufferData(idx, 0, 0, inN, MUF);
            else
            {
                if (idx16Scratch.Length < inN) { int n = idx16Scratch.Length; while (n < inN) n *= 2; idx16Scratch = new ushort[n]; }
                var s = idx16Scratch; for (int i = 0; i < inN; i++) s[i] = (ushort)idx[i];
                mesh.SetIndexBufferData(s, 0, 0, inN, MUF);
            }
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) { var d = subs[i]; d.bounds = b; d.firstVertex = 0; d.vertexCount = vn; mesh.SetSubMesh(i, d, MUF); }
            mesh.bounds = b;
            return wide;
        }
        static Bounds chunkBounds(Chunk c, int bucket)
        {
            double y0 = bucket >= 0 ? c.bucketMinY[bucket] : c.meshMinY, y1 = bucket >= 0 ? c.bucketMaxY[bucket] : c.meshMaxY;
            if (double.IsNaN(y0) || double.IsNaN(y1)) { y0 = WORLD_MIN_Y; y1 = WORLD_MAX_Y; }
            float x0 = c.cx * CHUNK, z0 = c.cz * CHUNK;
            var mn = new Vector3(x0, (float)y0, z0); var mx = new Vector3(x0 + CHUNK, (float)y1, z0 + CHUNK);
            var bb = new Bounds(); bb.SetMinMax(mn, mx); return bb;
        }
        public static void freeMesh(GpuMesh m)
        {
            if (m == null || m.sharedRegion != null) return;
            recycleMesh(m.mesh); m.mesh = null;
        }
        static GpuMesh makeMesh(GeoBucket g, Bounds b)
        {
            var m = new GpuMesh { mesh = allocMesh() };
            subScratch.Clear(); subScratch.Add(new SubMeshDescriptor(0, g.ICount, MeshTopology.Triangles));
            m.wide = writeTerrainMesh(m.mesh, g.v, g.VCount, g.i, g.ICount, subScratch, b);
            m.count = g.ICount; m.tris = g.ICount / 3; m.submesh = 0;
            return m;
        }
        static GpuMesh updateMeshBuffers(GpuMesh mesh, GeoBucket g, Bounds b)
        {
            if (g == null || g.ICount == 0) { freeMesh(mesh); return null; }
            if (mesh != null && mesh.sharedRegion != null) mesh = null;
            if (mesh == null) return makeMesh(g, b);
            subScratch.Clear(); subScratch.Add(new SubMeshDescriptor(0, g.ICount, MeshTopology.Triangles));
            mesh.wide = writeTerrainMesh(mesh.mesh, g.v, g.VCount, g.i, g.ICount, subScratch, b);
            mesh.count = g.ICount; mesh.tris = g.ICount / 3; mesh.submesh = 0;
            return mesh;
        }

        // ---------------- chunk bucket upload ----------------
        public static void uploadChunkBuckets(Chunk c, GeoBucket o, GeoBucket a, GeoBucket w, GeoBucket t, bool oc, bool ac, bool wc, bool tc)
        {
            if (oc)
            {
                c.opaqueCPU = o != null && o.ICount > 0 ? o : null;
                c.opaqueNeedsAlphaTest = false;
                markRenderRegionDirty(c.cx, c.cz);
                c.opaque = updateMeshBuffers(c.opaque, c.opaqueCPU, chunkBounds(c, 0));
            }
            if (ac)
            {
                c.cutoutCPU = a != null && a.ICount > 0 ? a : null;
                c.cutout = updateMeshBuffers(c.cutout, c.cutoutCPU, chunkBounds(c, 1));
                markRenderRegionDirty(c.cx, c.cz);
            }
            if (wc)
            {
                if (w != null && w.ICount > 0) setChunkWaterCPU(c, w); else clearChunkWaterCPU(c);
                c.water = updateMeshBuffers(c.water, c.waterCPU, chunkBounds(c, 2));
            }
            if (tc) c.trans = updateMeshBuffers(c.trans, t, chunkBounds(c, 3));
            c.meshBuilt = true;
            c.tris = (c.opaque != null ? c.opaque.tris : 0) + (c.cutout != null ? c.cutout.tris : 0) + (c.water != null ? c.water.tris : 0) + (c.trans != null ? c.trans.tris : 0);
        }
        public static void releaseChunkGpu(Chunk c)
        {
            freeMesh(c.opaque); freeMesh(c.cutout); freeMesh(c.water); freeMesh(c.trans);
            c.opaque = c.cutout = c.water = c.trans = null;
            c.opaqueCPU = null; c.cutoutCPU = null; c.opaqueNeedsAlphaTest = false;
            clearChunkWaterCPU(c);
            markRenderRegionDirty(c.cx, c.cz);
        }

        // ---------------- water CPU copy + per-quad depth sort ----------------
        static void setChunkWaterCPU(Chunk c, GeoBucket w)
        {
            c.waterCPU = w;
            c.waterQuadCount = w.ICount / 6;
            c.waterSortRevision++;
            c.hasFallingWater = false;
            for (int k = 0; k < w.VCount; k++) if (w.v[k].sa >= 512) { c.hasFallingWater = true; break; }
            c.waterSortEyeX = c.waterSortEyeY = c.waterSortEyeZ = double.PositiveInfinity;
            c.waterSortedRevision = -1;
            c.waterSortDirty = true;
        }
        static void clearChunkWaterCPU(Chunk c)
        {
            c.waterCPU = null; c.waterQuadCount = 0; c.hasFallingWater = false;
            c.waterSortRevision++; c.waterSortedRevision = -1; c.waterSortDirty = false;
        }
        sealed class WaterKeyCmp : IComparer<int>
        {
            public float[] keys;
            public int Compare(int a, int b) { float d = keys[b] - keys[a]; return d < 0 ? -1 : d > 0 ? 1 : a - b; }
        }
        static readonly WaterKeyCmp waterCmp = new WaterKeyCmp();
        static ushort[] waterIdx16 = new ushort[0];
        public static bool ensureChunkWaterQuadSort(Chunk c, double ex, double ey, double ez, double fx, double fy, double fz)
        {
            var m = c.water; var g = c.waterCPU;
            if (m == null || g == null || g.ICount < 12 || g.ICount % 6 != 0) return false;
            double dx = c.waterSortEyeX - ex, dy = c.waterSortEyeY - ey, dz = c.waterSortEyeZ - ez, dfx = c.waterSortDirX - fx, dfy = c.waterSortDirY - fy, dfz = c.waterSortDirZ - fz;
            if (!c.waterSortDirty && c.waterSortedRevision == c.waterSortRevision && dx * dx + dy * dy + dz * dz < 0.04 && dfx * dfx + dfy * dfy + dfz * dfz < 1e-5) return false;
            int quadCount = g.ICount / 6;
            if (c.waterSortKeys == null || c.waterSortKeys.Length != quadCount) c.waterSortKeys = new float[quadCount];
            if (c.waterSortOrder == null || c.waterSortOrder.Length != quadCount) c.waterSortOrder = new int[quadCount];
            if (c.waterSortIndices == null || c.waterSortIndices.Length != g.ICount) c.waterSortIndices = new int[g.ICount];
            var keys = c.waterSortKeys; var order = c.waterSortOrder; var dst = c.waterSortIndices; var src = g.i; var v = g.v;
            float fex = (float)ex, fey = (float)ey, fez = (float)ez, ffx = (float)fx, ffy = (float)fy, ffz = (float)fz;
            for (int q = 0, off = 0; q < quadCount; q++, off += 6)
            {
                var A = v[src[off]]; var Bv = v[src[off + 1]]; var C = v[src[off + 2]]; var D = v[src[off + 5]];
                float cx = (A.x + Bv.x + C.x + D.x) * 0.25f, cy = (A.y + Bv.y + C.y + D.y) * 0.25f, cz = (A.z + Bv.z + C.z + D.z) * 0.25f;
                // Transparency must be sorted by camera-space depth, not radial distance.
                keys[q] = (cx - fex) * ffx + (cy - fey) * ffy + (cz - fez) * ffz;
                order[q] = q;
            }
            waterCmp.keys = keys;
            Array.Sort(order, waterCmp);
            for (int qi = 0, off = 0; qi < quadCount; qi++, off += 6)
            {
                int so = order[qi] * 6;
                dst[off] = src[so]; dst[off + 1] = src[so + 1]; dst[off + 2] = src[so + 2]; dst[off + 3] = src[so + 3]; dst[off + 4] = src[so + 4]; dst[off + 5] = src[so + 5];
            }
            if (m.wide) m.mesh.SetIndexBufferData(dst, 0, 0, dst.Length, MUF);
            else
            {
                if (waterIdx16.Length < dst.Length) waterIdx16 = new ushort[Math.Max(dst.Length, waterIdx16.Length * 2)];
                for (int i = 0; i < dst.Length; i++) waterIdx16[i] = (ushort)dst[i];
                m.mesh.SetIndexBufferData(waterIdx16, 0, 0, dst.Length, MUF);
            }
            c.waterSortEyeX = ex; c.waterSortEyeY = ey; c.waterSortEyeZ = ez; c.waterSortDirX = fx; c.waterSortDirY = fy; c.waterSortDirZ = fz;
            c.waterSortedRevision = c.waterSortRevision; c.waterSortDirty = false;
            return true;
        }

        // ---------------- render regions ----------------
        public static int renderRegionCoord(int v) { return JS.floor((double)v / RENDER_REGION_SIZE); }
        public static string renderRegionKey(int rx, int rz) { return rx + "," + rz; }
        public static string chunkRenderRegionKey(int cx, int cz) { return renderRegionKey(renderRegionCoord(cx), renderRegionCoord(cz)); }
        public static RenderRegion getRenderRegion(int rx, int rz, bool create = true)
        {
            var k = renderRegionKey(rx, rz); RenderRegion r;
            if (!renderRegions.TryGetValue(k, out r) && create)
            {
                r = new RenderRegion { key = k, rx = rx, rz = rz };
                renderRegions[k] = r;
                dirtyRenderRegions.Add(k);
            }
            return r;
        }
        public static void markRenderRegionDirty(int cx, int cz)
        {
            var r = getRenderRegion(renderRegionCoord(cx), renderRegionCoord(cz), true);
            r.dirty = true;
            dirtyRenderRegions.Add(r.key);
        }
        static GpuMesh standaloneOpaque(Chunk c) { return c.opaqueCPU != null && c.opaqueCPU.ICount > 0 ? makeMesh(c.opaqueCPU, chunkBounds(c, 0)) : null; }
        static GpuMesh standaloneCutout(Chunk c) { return c.cutoutCPU != null && c.cutoutCPU.ICount > 0 ? makeMesh(c.cutoutCPU, chunkBounds(c, 1)) : null; }
        static void detachRegionMembers(RenderRegion r, bool standalone = true)
        {
            foreach (var k in r.memberKeys)
            {
                var c = chunks.Get(k);
                if (c == null || c.opaque == null || c.opaque.sharedRegion != r) continue;
                c.opaque = standalone ? standaloneOpaque(c) : null;
            }
            r.memberKeys.Clear();
        }
        static void detachRegionCutout(RenderRegion r)
        {
            foreach (var k in r.cutMemberKeys)
            {
                var c = chunks.Get(k);
                if (c != null && c.cutout != null && c.cutout.sharedRegion == r) c.cutout = standaloneCutout(c);
            }
            r.cutMemberKeys.Clear();
            r.cutoutAll = null;
        }
        static void freeRegionMesh(RenderRegion r)
        {
            if (r.mesh != null) { recycleMesh(r.mesh); r.mesh = null; }
            r.opaqueAll = null; r.cutoutAll = null;
        }
        public static GpuMesh ensureChunkOpaqueViewCurrent(Chunk c)
        {
            var m = c != null ? c.opaque : null;
            if (m == null || m.sharedRegion == null) return m;
            var r = m.sharedRegion;
            if (m.regionGeneration == r.generation && r.mesh != null) return m;
            c.opaque = standaloneOpaque(c);
            integrityGuardFrames = Math.Max(integrityGuardFrames, 12);
            r.dirty = true; dirtyRenderRegions.Add(r.key);
            return c.opaque;
        }
        public static GpuMesh ensureChunkCutoutViewCurrent(Chunk c)
        {
            var m = c != null ? c.cutout : null;
            if (m == null || m.sharedRegion == null) return m;
            var r = m.sharedRegion;
            if (r.cutoutAll != null && r.mesh != null && m.regionGeneration == r.generation) return m;
            c.cutout = standaloneCutout(c);
            markRenderRegionDirty(c.cx, c.cz);
            return c.cutout;
        }

        static TVert[] regionScratchV = new TVert[4096];
        static int[] regionScratchI = new int[6144];
        static readonly List<Chunk> regionMembers = new List<Chunk>(4), regionCutMembers = new List<Chunk>(4);
        public static bool rebuildRenderRegion(RenderRegion r)
        {
            double t0 = JS.now();
            regionMembers.Clear(); regionCutMembers.Clear();
            int vn = 0, inN = 0; double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            int cx0 = r.rx * RENDER_REGION_SIZE, cz0 = r.rz * RENDER_REGION_SIZE;
            for (int dz = 0; dz < RENDER_REGION_SIZE; dz++)
                for (int dx = 0; dx < RENDER_REGION_SIZE; dx++)
                {
                    var c = chunkFastGet(cx0 + dx, cz0 + dz);
                    if (c == null) continue;
                    if (c.opaqueCPU != null && c.opaqueCPU.ICount > 0)
                    {
                        regionMembers.Add(c); vn += c.opaqueCPU.VCount; inN += c.opaqueCPU.ICount;
                        if (!double.IsNaN(c.opaqueMinY) && c.opaqueMinY < lo) lo = c.opaqueMinY;
                        if (!double.IsNaN(c.opaqueMaxY) && c.opaqueMaxY > hi) hi = c.opaqueMaxY;
                    }
                }
            r.chunkCount = regionMembers.Count;
            if (regionMembers.Count < 2 || inN == 0)
            {
                r.generation++;
                detachRegionMembers(r, true);
                detachRegionCutout(r);
                freeRegionMesh(r);
                r.minY = r.maxY = double.NaN;
                r.dirty = false;
                dirtyRenderRegions.Remove(r.key);
                r.lastBuild = JS.now();
                avgRegionBuildMs += (JS.now() - t0 - avgRegionBuildMs) * 0.12;
                return false;
            }
            for (int dz = 0; dz < RENDER_REGION_SIZE; dz++)
                for (int dx = 0; dx < RENDER_REGION_SIZE; dx++)
                {
                    var c = chunkFastGet(cx0 + dx, cz0 + dz);
                    if (c != null && c.cutoutCPU != null && c.cutoutCPU.ICount > 0) regionCutMembers.Add(c);
                }
            bool batchCut = regionCutMembers.Count >= 2;
            int cvn = 0, cin = 0;
            if (batchCut) foreach (var c in regionCutMembers) { cvn += c.cutoutCPU.VCount; cin += c.cutoutCPU.ICount; if (!double.IsNaN(c.bucketMinY[1]) && c.bucketMinY[1] < lo) lo = c.bucketMinY[1]; if (!double.IsNaN(c.bucketMaxY[1]) && c.bucketMaxY[1] > hi) hi = c.bucketMaxY[1]; }
            // members that left the region fall back to standalone meshes
            var newKeys = new HashSet<string>(); foreach (var c in regionMembers) newKeys.Add(c.key);
            foreach (var k in r.memberKeys)
            {
                if (newKeys.Contains(k)) continue;
                var c = chunks.Get(k);
                if (c != null && c.opaque != null && c.opaque.sharedRegion == r) { c.opaque = standaloneOpaque(c); integrityGuardFrames = Math.Max(integrityGuardFrames, 12); }
            }
            var newCut = new HashSet<string>(); if (batchCut) foreach (var c in regionCutMembers) newCut.Add(c.key);
            foreach (var k in r.cutMemberKeys)
            {
                if (newCut.Contains(k)) continue;
                var c = chunks.Get(k);
                if (c != null && c.cutout != null && c.cutout.sharedRegion == r) c.cutout = standaloneCutout(c);
            }
            int tvn = vn + cvn, tin = inN + cin;
            if (regionScratchV.Length < tvn) { int n = regionScratchV.Length; while (n < tvn) n *= 2; regionScratchV = new TVert[n]; }
            if (regionScratchI.Length < tin) { int n = regionScratchI.Length; while (n < tin) n *= 2; regionScratchI = new int[n]; }
            int vo = 0, io = 0;
            subScratch.Clear();
            subScratch.Add(new SubMeshDescriptor(0, inN, MeshTopology.Triangles));
            var opRanges = new int[regionMembers.Count * 2];
            for (int mi = 0; mi < regionMembers.Count; mi++)
            {
                var g = regionMembers[mi].opaqueCPU; int start = io, bse = vo;
                Array.Copy(g.v, 0, regionScratchV, vo, g.VCount);
                var gi = g.i; for (int j = 0; j < gi.Length; j++) regionScratchI[io + j] = gi[j] + bse;
                vo += g.VCount; io += gi.Length;
                opRanges[mi * 2] = start; opRanges[mi * 2 + 1] = gi.Length;
                subScratch.Add(new SubMeshDescriptor(start, gi.Length, MeshTopology.Triangles));
            }
            int cutAllSub = -1; int[] cutRanges = null;
            if (batchCut)
            {
                cutAllSub = subScratch.Count;
                subScratch.Add(new SubMeshDescriptor(inN, cin, MeshTopology.Triangles));
                cutRanges = new int[regionCutMembers.Count];
                for (int mi = 0; mi < regionCutMembers.Count; mi++)
                {
                    var g = regionCutMembers[mi].cutoutCPU; int start = io, bse = vo;
                    Array.Copy(g.v, 0, regionScratchV, vo, g.VCount);
                    var gi = g.i; for (int j = 0; j < gi.Length; j++) regionScratchI[io + j] = gi[j] + bse;
                    vo += g.VCount; io += gi.Length;
                    cutRanges[mi] = subScratch.Count;
                    subScratch.Add(new SubMeshDescriptor(start, gi.Length, MeshTopology.Triangles));
                }
            }
            if (double.IsInfinity(lo)) lo = WORLD_MIN_Y; if (double.IsInfinity(hi)) hi = WORLD_MAX_Y;
            var bb = new Bounds(); float bx = cx0 * CHUNK, bz = cz0 * CHUNK, bs = RENDER_REGION_SIZE * CHUNK;
            bb.SetMinMax(new Vector3(bx, (float)lo, bz), new Vector3(bx + bs, (float)hi, bz + bs));
            if (r.mesh == null) r.mesh = allocMesh();
            r.wide = writeTerrainMesh(r.mesh, regionScratchV, tvn, regionScratchI, tin, subScratch, bb);
            r.generation++;
            r.opaqueAll = new GpuMesh { mesh = r.mesh, submesh = 0, wide = r.wide, sharedRegion = r, regionGeneration = r.generation, count = inN, tris = inN / 3 };
            r.memberKeys.Clear();
            for (int mi = 0; mi < regionMembers.Count; mi++)
            {
                var c = regionMembers[mi];
                if (c.opaque != null && c.opaque.sharedRegion == null) freeMesh(c.opaque);
                c.opaque = new GpuMesh { mesh = r.mesh, submesh = 1 + mi, wide = r.wide, sharedRegion = r, regionGeneration = r.generation, count = opRanges[mi * 2 + 1], tris = opRanges[mi * 2 + 1] / 3 };
                r.memberKeys.Add(c.key);
                regionArenaTakeovers++;
            }
            r.cutMemberKeys.Clear();
            if (batchCut)
            {
                r.cutoutAll = new GpuMesh { mesh = r.mesh, submesh = cutAllSub, wide = r.wide, sharedRegion = r, regionGeneration = r.generation, count = cin, tris = cin / 3 };
                for (int mi = 0; mi < regionCutMembers.Count; mi++)
                {
                    var c = regionCutMembers[mi];
                    if (c.cutout != null && c.cutout.sharedRegion == null) freeMesh(c.cutout);
                    int cnt = c.cutoutCPU.ICount;
                    c.cutout = new GpuMesh { mesh = r.mesh, submesh = cutRanges[mi], wide = r.wide, sharedRegion = r, regionGeneration = r.generation, count = cnt, tris = cnt / 3 };
                    r.cutMemberKeys.Add(c.key);
                }
            }
            else r.cutoutAll = null;
            r.minY = lo; r.maxY = hi;
            r.dirty = false;
            r.lastBuild = JS.now();
            dirtyRenderRegions.Remove(r.key);
            regionBuilds++;
            avgRegionBuildMs += (JS.now() - t0 - avgRegionBuildMs) * 0.12;
            return true;
        }
        static readonly RenderRegion[] regionBuildPick = new RenderRegion[3];
        static readonly int[] regionBuildPickDist = new int[3];
        public static void pumpRenderRegionBuilds(double deadline, int maxBuilds = 3)
        {
            if (dirtyRenderRegions.Count == 0 || maxBuilds <= 0 || JS.now() >= deadline) return;
            int prx = renderRegionCoord(JS.floor(player.x / CHUNK)), prz = renderRegionCoord(JS.floor(player.z / CHUNK));
            int count = 0;
            List<string> stale = null;
            foreach (var k in dirtyRenderRegions)
            {
                RenderRegion r;
                if (!renderRegions.TryGetValue(k, out r)) { (stale ?? (stale = new List<string>())).Add(k); continue; }
                int d = Math.Max(Math.Abs(r.rx - prx), Math.Abs(r.rz - prz));
                int pos = count < 3 ? count : 2;
                if (count < 3) count++;
                else if (d >= regionBuildPickDist[2]) continue;
                while (pos > 0 && d < regionBuildPickDist[pos - 1])
                {
                    if (pos < 3) { regionBuildPick[pos] = regionBuildPick[pos - 1]; regionBuildPickDist[pos] = regionBuildPickDist[pos - 1]; }
                    pos--;
                }
                regionBuildPick[pos] = r; regionBuildPickDist[pos] = d;
            }
            if (stale != null) foreach (var k in stale) dirtyRenderRegions.Remove(k);
            int n = 0;
            for (int i = 0; i < count && n < maxBuilds; i++)
            {
                if (JS.now() + (n > 0 ? Math.Max(0.25, avgRegionBuildMs * 1.25) : 0) >= deadline) break;
                var r = regionBuildPick[i]; regionBuildPick[i] = null;
                if (r == null || !dirtyRenderRegions.Has(r.key)) continue;
                rebuildRenderRegion(r);
                n++;
            }
            for (int i = 0; i < 3; i++) regionBuildPick[i] = null;
        }
        public static void cleanupRenderRegions(int pcx, int pcz)
        {
            int prx = renderRegionCoord(pcx), prz = renderRegionCoord(pcz), keep = (int)Math.Ceiling((renderDistance + 6) / (double)RENDER_REGION_SIZE) + 1;
            List<string> drop = null;
            foreach (var kv in renderRegions)
            {
                var r = kv.Value;
                if (Math.Max(Math.Abs(r.rx - prx), Math.Abs(r.rz - prz)) <= keep) continue;
                detachRegionMembers(r, true);
                detachRegionCutout(r);
                freeRegionMesh(r);
                (drop ?? (drop = new List<string>())).Add(kv.Key);
            }
            if (drop != null) foreach (var k in drop) { renderRegions.Remove(k); dirtyRenderRegions.Remove(k); }
        }
        static bool renderRegionFullyInsideRD(RenderRegion r, int pcx, int pcz)
        {
            int x0 = r.rx * RENDER_REGION_SIZE, z0 = r.rz * RENDER_REGION_SIZE, x1 = x0 + RENDER_REGION_SIZE - 1, z1 = z0 + RENDER_REGION_SIZE - 1;
            return x0 >= pcx - renderDistance && x1 <= pcx + renderDistance && z0 >= pcz - renderDistance && z1 <= pcz + renderDistance;
        }

        // ---------------- render lists ----------------
        public struct OpaqueEntry { public GpuMesh m; public RenderRegion region; public Chunk chunk; public double dist2; }
        public static OpaqueEntry[] renderOpaqueList = new OpaqueEntry[256];
        public static int renderOpaqueCount = 0;
        static bool bucketVisible(Chunk c, int k)
        {
            double y0 = c.bucketMinY[k], y1 = c.bucketMaxY[k];
            if (double.IsNaN(y0) || double.IsNaN(y1)) return true;
            double x0 = c.cx * CHUNK, z0 = c.cz * CHUNK;
            return aabbVisible(x0, y0, z0, x0 + CHUNK, y1, z0 + CHUNK);
        }
        static bool opaqueBucketVisible(Chunk c) { var m = ensureChunkOpaqueViewCurrent(c); return m != null && bucketVisible(c, 0); }
        static void addOpaqueEntry(GpuMesh m, RenderRegion region, Chunk chunk, double dist2)
        {
            if (renderOpaqueCount >= renderOpaqueList.Length) Array.Resize(ref renderOpaqueList, renderOpaqueList.Length * 2);
            renderOpaqueList[renderOpaqueCount++] = new OpaqueEntry { m = m, region = region, chunk = chunk, dist2 = dist2 };
        }
        public static void buildOpaqueRenderList(List<Chunk> visible, int pcx, int pcz)
        {
            int tag = ++renderRegionTag;
            renderOpaqueCount = 0;
            foreach (var c in visible)
            {
                if (!opaqueBucketVisible(c)) { c._opaqueVisTag = 0; c._opaqueRegionTag = 0; continue; }
                c._opaqueVisTag = tag;
                RenderRegion r; renderRegions.TryGetValue(chunkRenderRegionKey(c.cx, c.cz), out r);
                if (r == null || r.dirty || r.opaqueAll == null || r.chunkCount < 2 || !renderRegionFullyInsideRD(r, pcx, pcz)) { c._opaqueRegionTag = 0; continue; }
                c._opaqueRegionTag = tag; c._opaqueRegionRef = r;
                if (r._tag != tag) { r._tag = tag; r._visibleOpaque = 0; r._firstDist = c._renderDist2; }
                r._visibleOpaque++;
            }
            regionBatchedChunks = 0;
            foreach (var c in visible)
            {
                if (c._opaqueVisTag != tag) continue;
                var r = c._opaqueRegionTag == tag ? c._opaqueRegionRef : null;
                if (r != null && r._tag == tag && r._visibleOpaque >= 2 && r._visibleOpaque == r.chunkCount)
                {
                    if (r._emitTag != tag) { r._emitTag = tag; addOpaqueEntry(r.opaqueAll, r, null, r._firstDist); regionBatchedChunks += r._visibleOpaque; }
                    continue;
                }
                addOpaqueEntry(c.opaque, null, c, c._renderDist2);
            }
        }
        public static GpuMesh[] cutoutRenderList = new GpuMesh[256];
        public static Chunk[] waterRenderList = new Chunk[256], transRenderList = new Chunk[256];
        public static int cutoutRenderCount = 0, waterRenderCount = 0, transRenderCount = 0;
        static readonly List<Chunk> cutoutChunkScratch = new List<Chunk>();
        public static void buildAlphaRenderLists(List<Chunk> visible)
        {
            cutoutRenderCount = 0; transRenderCount = 0; waterRenderCount = 0;
            int tag = ++cutoutBatchTag;
            cutoutChunkScratch.Clear();
            if (waterRenderList.Length < visible.Count) { waterRenderList = new Chunk[visible.Count * 2]; transRenderList = new Chunk[visible.Count * 2]; }
            if (cutoutRenderList.Length < visible.Count) cutoutRenderList = new GpuMesh[visible.Count * 2];
            for (int i = 0; i < visible.Count; i++)
            {
                var c = visible[i];
                ensureChunkCutoutViewCurrent(c);
                if (c.cutout != null && bucketVisible(c, 1))
                {
                    var r = c.cutout.sharedRegion;
                    if (r != null) { if (r._cutTag != tag) { r._cutTag = tag; r._cutVis = 0; } r._cutVis++; }
                    cutoutChunkScratch.Add(c);
                }
                if (c.water != null && bucketVisible(c, 2)) waterRenderList[waterRenderCount++] = c;
            }
            for (int i = visible.Count - 1; i >= 0; i--) { var c = visible[i]; if (c.trans != null && bucketVisible(c, 3)) transRenderList[transRenderCount++] = c; }
            foreach (var c in cutoutChunkScratch)
            {
                var r = c.cutout.sharedRegion;
                if (r != null && r.cutoutAll != null && r._cutTag == tag && r._cutVis == r.cutMemberKeys.Count)
                {
                    if (r._cutEmit != tag) { r._cutEmit = tag; cutoutRenderList[cutoutRenderCount++] = r.cutoutAll; }
                }
                else cutoutRenderList[cutoutRenderCount++] = c.cutout;
            }
        }
        sealed class WaterDepthCmp : IComparer<Chunk>
        {
            public int Compare(Chunk a, Chunk b)
            {
                double d = b._waterViewDepth - a._waterViewDepth;
                if (d != 0) return d < 0 ? -1 : 1;
                if (a.cx != b.cx) return a.cx - b.cx;
                return a.cz - b.cz;
            }
        }
        static readonly WaterDepthCmp waterDepthCmp = new WaterDepthCmp();
        static Chunk[] waterSortedList = new Chunk[256];
        public static int waterSortedCount = 0;
        /// <summary>Back-to-front water chunk order (drawWaterBucketBackToFront's list building + per-chunk quad sort).</summary>
        public static Chunk[] sortWaterBackToFront(double ex, double ey, double ez, double fx, double fy, double fz)
        {
            if (waterSortedList.Length < waterRenderCount) waterSortedList = new Chunk[waterRenderCount * 2];
            int wn = 0;
            for (int qi = 0; qi < waterRenderCount; qi++)
            {
                var c = waterRenderList[qi];
                if (c.water == null) continue;
                double y0 = c.bucketMinY[2], y1 = c.bucketMaxY[2];
                double cx = c.cx * CHUNK + CHUNK * 0.5, cy = ((double.IsNaN(y0) ? WORLD_MIN_Y : y0) + (double.IsNaN(y1) ? WORLD_MAX_Y : y1)) * 0.5, cz = c.cz * CHUNK + CHUNK * 0.5;
                c._waterViewDepth = (cx - ex) * fx + (cy - ey) * fy + (cz - ez) * fz;
                waterSortedList[wn++] = c;
            }
            Array.Sort(waterSortedList, 0, wn, waterDepthCmp);
            for (int i = 0; i < wn; i++) ensureChunkWaterQuadSort(waterSortedList[i], ex, ey, ez, fx, fy, fz);
            waterSortedCount = wn;
            return waterSortedList;
        }
    }
}
