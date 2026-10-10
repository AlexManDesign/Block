// Voxel Forge — Unity port. Growable float batches and per-frame dynamic meshes (drops, entities, particles, lines).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelForge
{
    public sealed class FloatList
    {
        public float[] a; public int n;
        public FloatList(int cap = 1024) { a = new float[cap]; }
        public int Count { get { return n; } }
        public int length { get { return n; } }
        public void Clear() { n = 0; }
        public void Ensure(int add) { if (n + add > a.Length) { int c = a.Length * 2; while (c < n + add) c *= 2; Array.Resize(ref a, c); } }
        public void Add(float v) { if (n >= a.Length) Array.Resize(ref a, a.Length * 2); a[n++] = v; }
        public void Push7(double x, double y, double z, double u, double v, double t, double s)
        {
            Ensure(7); var q = a; int k = n;
            q[k] = (float)x; q[k + 1] = (float)y; q[k + 2] = (float)z; q[k + 3] = (float)u; q[k + 4] = (float)v; q[k + 5] = (float)t; q[k + 6] = (float)s; n = k + 7;
        }
    }

    /// <summary>A Unity mesh rebuilt every frame from a float batch. Quad meshes reuse a static quad index pattern.</summary>
    public sealed class DynMesh
    {
        public readonly Mesh mesh; readonly VertexAttributeDescriptor[] layout; readonly int floatsPerVert; readonly MeshTopology topo; readonly bool quads;
        int quadIndexCap = 0, vertCap = 0, idxCap = 0, idxUploaded = 0;
        int[] quadIdx = new int[0];
        public int vertexCount, indexCount;
        public DynMesh(string name, VertexAttributeDescriptor[] layout, int floatsPerVert, bool quads, MeshTopology topo = MeshTopology.Triangles)
        {
            mesh = new Mesh { name = name }; mesh.MarkDynamic();
            this.layout = layout; this.floatsPerVert = floatsPerVert; this.quads = quads; this.topo = topo;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }
        /// <summary>Grow-only GPU capacity (JS: grow-only bufferData + bufferSubData). Returns true when the index buffer was reallocated.</summary>
        bool ensureCapacity(int vn, int ic)
        {
            if (vn > vertCap) { int c = Math.Max(64, vertCap); while (c < vn) c *= 2; vertCap = c; mesh.SetVertexBufferParams(vertCap, layout); }
            if (ic > idxCap) { int c = Math.Max(96, idxCap); while (c < ic) c *= 2; idxCap = c; mesh.SetIndexBufferParams(idxCap, IndexFormat.UInt32); idxUploaded = 0; return true; }
            return false;
        }
        void setRange(int ic, int vn)
        {
            if (mesh.subMeshCount != 1) mesh.subMeshCount = 1;
            var d = new SubMeshDescriptor(0, ic, topo) { bounds = mesh.bounds, firstVertex = 0, vertexCount = vn };
            mesh.SetSubMesh(0, d, VF.MUF);
        }
        /// <summary>Upload n floats; quads: 4 vertices per quad (0,1,2 / 2,1,3), else sequential indices. The static index pattern is uploaded only when its capacity grows.</summary>
        public bool Upload(float[] data, int floatCount)
        {
            int vn = floatCount / floatsPerVert;
            vertexCount = vn;
            if (vn == 0) { indexCount = 0; return false; }
            int ic;
            if (quads)
            {
                int qn = vn / 4; ic = qn * 6;
                if (ic == 0) { indexCount = 0; return false; }
                if (qn > quadIndexCap)
                {
                    int cap = Math.Max(16, quadIndexCap); while (cap < qn) cap *= 2;
                    quadIdx = new int[cap * 6];
                    for (int q = 0, o = 0; q < cap; q++) { int b = q * 4; quadIdx[o++] = b; quadIdx[o++] = b + 1; quadIdx[o++] = b + 2; quadIdx[o++] = b + 2; quadIdx[o++] = b + 1; quadIdx[o++] = b + 3; }
                    quadIndexCap = cap;
                }
                // the whole pattern is resident, so the vertex buffer covers every vertex it references
                ensureCapacity(Math.Max(vn, quadIndexCap * 4), quadIndexCap * 6);
                if (idxUploaded < quadIndexCap * 6) { mesh.SetIndexBufferData(quadIdx, 0, 0, quadIndexCap * 6, VF.MUF); idxUploaded = quadIndexCap * 6; }
            }
            else
            {
                ic = vn;
                if (quadIdx.Length < ic) { int cap = Math.Max(64, quadIdx.Length); while (cap < ic) cap *= 2; quadIdx = new int[cap]; for (int i = 0; i < cap; i++) quadIdx[i] = i; }
                ensureCapacity(quadIdx.Length, quadIdx.Length);
                if (idxUploaded < quadIdx.Length) { mesh.SetIndexBufferData(quadIdx, 0, 0, quadIdx.Length, VF.MUF); idxUploaded = quadIdx.Length; }
            }
            mesh.SetVertexBufferData(data, 0, 0, vn * floatsPerVert, 0, VF.MUF);
            setRange(ic, vn);
            indexCount = ic;
            return true;
        }
        /// <summary>Upload with an explicit index list.</summary>
        public bool Upload(float[] data, int floatCount, int[] idx, int idxCount)
        {
            int vn = floatCount / floatsPerVert;
            vertexCount = vn; indexCount = idxCount;
            if (vn == 0 || idxCount == 0) return false;
            ensureCapacity(vn, idxCount);
            mesh.SetVertexBufferData(data, 0, 0, vn * floatsPerVert, 0, VF.MUF);
            mesh.SetIndexBufferData(idx, 0, 0, idxCount, VF.MUF);
            idxUploaded = 0; // the static pattern (if any) was overwritten
            setRange(idxCount, vn);
            return true;
        }
    }

    public static partial class VF
    {
        /// <summary>pos3, uv2, tile, shade — drops / entities / held sprites.</summary>
        public static readonly VertexAttributeDescriptor[] SPRITE_LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
        };
        /// <summary>pos3, uv2, tile, shade, light, tint3 — break particles (11 floats).</summary>
        public static readonly VertexAttributeDescriptor[] PARTICLE_LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 3),
        };
        /// <summary>pos3, color4 — lines / outlines.</summary>
        public static readonly VertexAttributeDescriptor[] LINE_LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };
        static readonly double[][] DROP_UV = { new double[] { 0, 0 }, new double[] { 1, 0 }, new double[] { 0, 1 }, new double[] { 1, 1 } };
        /// <summary>Pushes one textured quad (4 vertices, 7 floats each). The index list argument is kept for signature parity.</summary>
        public static void dropPushQuad(FloatList v, List<int> i, double[][] pts, int tile, double shade)
        {
            for (int k = 0; k < 4; k++) { var uv = DROP_UV[k]; v.Push7(pts[k][0], pts[k][1], pts[k][2], uv[0], uv[1], tile, shade); }
        }
    }
}
