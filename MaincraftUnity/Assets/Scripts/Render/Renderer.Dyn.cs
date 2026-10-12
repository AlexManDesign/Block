// Immediate-mode drawing of render.js for the game's own geometry (entities, particles, items, hand,
// outline, cracks, boats) and the vessels' section meshes.
//
// The original sets WebGL state (culling, blending, depth test / writes, colour mask, polygon offset)
// around its draw calls; the same state lives here as fields read by every draw, so the callers port
// one to one: gl.disable(gl.CULL_FACE) -> r.Cull = false. All vertices of a frame go into one GPU
// buffer, uploaded once in EndFrame; each draw reads its range from it.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maincraft
{
    /// <summary>
    /// A growable vertex list of 10 floats per vertex (x, y, z, u, v, layer, r, g, b, a; camera-relative),
    /// kept from frame to frame like the original's VBuf.
    /// </summary>
    public sealed class VBuf
    {
        public float[] a = new float[4096];
        public int n;                                 // floats written
        public int Length => n;
        public int Verts => n / 10;
        public void Clear() => n = 0;
        public void Push(double x, double y, double z, double u, double v, double l, double r, double g, double b, double al)
        {
            var A = a;
            if (n + 10 > A.Length) { Array.Resize(ref a, A.Length * 2); A = a; }
            A[n] = (float)x; A[n + 1] = (float)y; A[n + 2] = (float)z; A[n + 3] = (float)u; A[n + 4] = (float)v;
            A[n + 5] = (float)l; A[n + 6] = (float)r; A[n + 7] = (float)g; A[n + 8] = (float)b; A[n + 9] = (float)al;
            n += 10;
        }
        /// <summary>Room for k more floats; returns the (possibly new) array, to be written from n on.</summary>
        public float[] Reserve(int k)
        {
            if (n + k > a.Length)
            {
                int len = a.Length * 2;
                while (len < n + k) len *= 2;
                Array.Resize(ref a, len);
            }
            return a;
        }
    }

    /// <summary>A vessel's section mesh (ships.js buildMeshes): its quads and records in its own grid.</summary>
    public sealed class ShipMesh
    {
        public GraphicsBuffer buf;
        public int quads;
        public int[] counts;
        public int ox, oy, oz;
        public int[] recs; public int recN;
    }

    public sealed partial class Renderer
    {
        static readonly int ID_Dyn = Shader.PropertyToID("_Dyn"), ID_VBase = Shader.PropertyToID("_VBase"), ID_AlphaRef = Shader.PropertyToID("_AlphaRef"),
            ID_Color = Shader.PropertyToID("_Color"), ID_ZTest = Shader.PropertyToID("_ZTest"), ID_ZWrite = Shader.PropertyToID("_ZWrite"),
            ID_SrcBlend = Shader.PropertyToID("_SrcBlend"), ID_DstBlend = Shader.PropertyToID("_DstBlend"), ID_ColorMask = Shader.PropertyToID("_ColorMask"),
            ID_OffF = Shader.PropertyToID("_OffsetFactor"), ID_OffU = Shader.PropertyToID("_OffsetUnits");

        // ---------------------------------------------------------------- GL state of the original
        public bool DepthTest = true, DepthWrite = true, Blend, Cull = true, ColorWrite = true, PolyOffset;
        public float PolyFactor, PolyUnits;
        public void ResetState() { DepthTest = true; DepthWrite = true; Blend = false; Cull = true; ColorWrite = true; PolyOffset = false; PolyFactor = 0; PolyUnits = 0; }
        /// <summary>gl.clear(gl.DEPTH_BUFFER_BIT)</summary>
        public void ClearDepth() => cb.ClearRenderTarget(true, false, Color.black);

        Shader shArr, shEnt, shLine;
        readonly Dictionary<(Shader, int, float, float), Material> dynMats = new Dictionary<(Shader, int, float, float), Material>();
        readonly Func<string, byte[]> entityImage;
        readonly Dictionary<string, Texture2D> entTex = new Dictionary<string, Texture2D>();

        // the frame's dynamic vertices
        float[] dyn = new float[1 << 16];
        int dynN;                                     // vertices
        GraphicsBuffer dynBuf;

        void InitDyn()
        {
            shArr = Resources.Load<Shader>("Shaders/Arr"); shEnt = Resources.Load<Shader>("Shaders/Ent"); shLine = Resources.Load<Shader>("Shaders/Line");
            if (shArr == null || shEnt == null || shLine == null) throw new Exception("dynamic shaders not found");
            dynBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1 << 14, 40);
        }

        void DisposeDyn()
        {
            dynBuf?.Release();
            foreach (var t in entTex.Values) if (t != null) UnityEngine.Object.Destroy(t);
        }

        /// <summary>Copies count vertices (10 floats each) into the frame's buffer; returns the first vertex.</summary>
        int AppendDyn(float[] arr, int count)
        {
            int first = dynN;
            if ((dynN + count) * 10 > dyn.Length)
            {
                int len = dyn.Length * 2;
                while (len < (dynN + count) * 10) len *= 2;
                Array.Resize(ref dyn, len);
            }
            Array.Copy(arr, 0, dyn, dynN * 10, count * 10);
            dynN += count;
            return first;
        }

        void UploadDyn()
        {
            if (dynBuf.count < dynN)
            {
                dynBuf.Release();
                dynBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(dynN * 2, 1 << 14), 40);
            }
            if (dynN > 0) dynBuf.SetData(dyn, 0, 0, dynN * 10);
            Shader.SetGlobalBuffer(ID_Dyn, dynBuf);
        }

        /// <summary>A material of the shader with the current GL state.</summary>
        Material StateMat(Shader sh)
        {
            int flags = (DepthTest ? 1 : 0) | (DepthWrite ? 2 : 0) | (Blend ? 4 : 0) | (Cull ? 8 : 0) | (ColorWrite ? 16 : 0) | (PolyOffset ? 32 : 0);
            var key = (sh, flags, PolyOffset ? PolyFactor : 0f, PolyOffset ? PolyUnits : 0f);
            if (dynMats.TryGetValue(key, out var m)) return m;
            m = new Material(sh) { hideFlags = HideFlags.DontSave };
            m.SetFloat(ID_ZTest, DepthTest ? (float)CompareFunction.LessEqual : (float)CompareFunction.Always);
            m.SetFloat(ID_ZWrite, DepthWrite ? 1 : 0);
            m.SetFloat(ID_SrcBlend, Blend ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat(ID_DstBlend, Blend ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            // GL culls back faces (counter-clockwise front); Unity's front faces are clockwise
            m.SetFloat(ID_Cull, !Cull ? (float)CullMode.Off : cullFlip ? (float)CullMode.Back : (float)CullMode.Front);
            m.SetFloat(ID_ColorMask, ColorWrite ? 15 : 0);
            m.SetFloat(ID_OffF, PolyOffset ? PolyFactor : 0); m.SetFloat(ID_OffU, PolyOffset ? PolyUnits : 0);
            dynMats[key] = m;
            return m;
        }

        void Fog(double fogStart, double fogEnd, double[] fogColor)
        {
            mpb.SetVector(ID_Fog, new Vector4((float)fogStart, (float)fogEnd, 0, 0));
            if (fogColor != null) mpb.SetVector(ID_FogColor, new Vector4((float)fogColor[0], (float)fogColor[1], (float)fogColor[2], 1));
            else mpb.SetVector(ID_FogColor, Vector4.zero);
        }

        /// <summary>
        /// drawArr: triangles textured from the block atlas array. arr: [x,y,z,u,v,layer,r,g,b,a] * count,
        /// camera-relative; env null: no fog; vpOverride: another view-projection (the hand, screen overlays).
        /// </summary>
        public void DrawArr(float[] arr, int count, Env env, double alphaRef, double[] vpOverride = null)
        {
            if (count <= 0) return;
            int first = AppendDyn(arr, count);
            var mat = StateMat(shArr);
            mpb.Clear();
            mpb.SetFloat(ID_VBase, first);
            if (env != null) Fog(env.fogStart, env.fogEnd, env.fogColor); else Fog(1e9, 1e9 + 1, null);
            mpb.SetFloat(ID_AlphaRef, (float)alphaRef);
            mpb.SetVector(ID_Mul, new Vector4(1, 1, 1, 1));
            mpb.SetTexture(ID_Tex, texArr);
            if (vpOverride != null) cb.SetViewProjectionMatrices(Matrix4x4.identity, ToMatrix(vpOverride));
            cb.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, count, 1, mpb);
            if (vpOverride != null) cb.SetViewProjectionMatrices(Matrix4x4.identity, vpU);
            stats.draws++;
        }
        public void DrawArr(VBuf v, Env env, double alphaRef, double[] vpOverride = null) => DrawArr(v.a, v.Verts, env, alphaRef, vpOverride);

        /// <summary>drawEnt: triangles textured from an entity skin (alpha below 0.1 cut away).</summary>
        public void DrawEnt(float[] arr, int count, Texture tex, double fogStart, double fogEnd, double[] fogColor)
        {
            if (count <= 0 || tex == null) return;
            int first = AppendDyn(arr, count);
            var mat = StateMat(shEnt);
            mpb.Clear();
            mpb.SetFloat(ID_VBase, first);
            Fog(fogStart, fogEnd, fogColor);
            mpb.SetTexture(ID_MainTex, tex);
            cb.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, count, 1, mpb);
            stats.draws++;
        }
        public void DrawEnt(float[] arr, int count, Texture tex, Env env) => DrawEnt(arr, count, tex, env.fogStart, env.fogEnd, env.fogColor);
        public void DrawEnt(VBuf v, Texture tex, Env env) => DrawEnt(v.a, v.Verts, tex, env.fogStart, env.fogEnd, env.fogColor);

        float[] lineV = new float[600];
        /// <summary>drawLines: line segments (pairs of xyz points, nFloats floats) in one colour.</summary>
        public void DrawLines(float[] pts, int nFloats, float[] color)
        {
            int count = nFloats / 3;
            if (count <= 0) return;
            if (lineV.Length < count * 10) lineV = new float[count * 20];
            for (int i = 0; i < count; i++)
            {
                int o = i * 10, s = i * 3;
                lineV[o] = pts[s]; lineV[o + 1] = pts[s + 1]; lineV[o + 2] = pts[s + 2];
            }
            int first = AppendDyn(lineV, count);
            var mat = StateMat(shLine);
            mpb.Clear();
            mpb.SetFloat(ID_VBase, first);
            mpb.SetVector(ID_Color, new Vector4(color[0], color[1], color[2], color[3]));
            cb.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Lines, count, 1, mpb);
            stats.draws++;
        }

        /// <summary>entityTexture: an entity skin by name (spider / enderman eyes drawn over the skin); null if none.</summary>
        public Texture2D EntityTexture(string name)
        {
            if (entTex.TryGetValue(name, out var t)) return t;
            var png = entityImage?.Invoke(name);
            if (png != null)
            {
                var px = DecodePng(png, out int W, out int H);
                var eyes = entityImage(name + "_eyes");
                if (eyes != null)
                {
                    // source-over like a canvas drawImage
                    var ep = DecodePng(eyes, out int EW, out int EH);
                    if (EW == W && EH == H)
                        for (int i = 0; i < W * H * 4; i += 4)
                        {
                            int ea = ep[i + 3];
                            if (ea == 0) continue;
                            if (ea == 255) { px[i] = ep[i]; px[i + 1] = ep[i + 1]; px[i + 2] = ep[i + 2]; px[i + 3] = 255; continue; }
                            double a = ea / 255.0, ba = px[i + 3] / 255.0, oa = a + ba * (1 - a);
                            for (int k = 0; k < 3; k++) px[i + k] = (byte)Math.Round((ep[i + k] * a + px[i + k] * ba * (1 - a)) / oa);
                            px[i + 3] = (byte)Math.Round(oa * 255);
                        }
                }
                t = Tex2D(px, W, H);
            }
            entTex[name] = t;
            return t;
        }

        // ---------------------------------------------------------------- vessels (ships.js)
        /// <summary>A mesher result of a vessel section (its own grid, origin ox, oy, oz) on the GPU.</summary>
        public ShipMesh UploadShipMesh(MeshResult d, int ox, int oy, int oz)
        {
            int total = d.counts[0] + d.counts[1] + d.counts[2];
            if (total == 0) return null;
            var m = new ShipMesh { quads = total, counts = (int[])d.counts.Clone(), ox = ox, oy = oy, oz = oz };
            m.buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total * 12, 4);
            m.buf.SetData(d.data, 0, 0, total * 12);
            int chunks = (total + CHUNK - 1) / CHUNK;
            m.recs = new int[chunks * 4]; m.recN = chunks;
            for (int k = 0, c = 0; k < total; k += CHUNK, c++)
            {
                int cnt = Math.Min(CHUNK, total - k);
                m.recs[c * 4] = k | ((cnt - 1) << 26); m.recs[c * 4 + 1] = ox; m.recs[c * 4 + 2] = oy; m.recs[c * 4 + 3] = oz;
            }
            return m;
        }

        public void FreeShipMesh(ShipMesh m) { m?.buf?.Release(); if (m != null) m.buf = null; }

        /// <summary>
        /// drawShip: a vessel's sections turned by yaw about its wheel cell and moved there as one, alpha-tested
        /// (opaque and cut-out quads alike; its few translucent quads go in the same pass).
        /// </summary>
        public void DrawShip(IList<ShipMesh> meshes, double yaw, double posX, double posY, double posZ, double bobY, double camX, double camY, double camZ, Env env, double time, uint tick)
        {
            if (meshes == null || meshes.Count == 0) return;
            BindTerrain(env, camX, camY, camZ, time, tick, 0);
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            cb.SetGlobalVector(ID_ShipRot, new Vector4((float)c, (float)s, 0, 0));
            cb.SetGlobalVector(ID_ShipRel, new Vector4((float)(posX + 0.5 - camX), (float)(posY + bobY - camY), (float)(posZ + 0.5 - camZ), 0));
            foreach (var m in meshes)
            {
                if (m == null || m.buf == null) continue;
                int at = AddRecs(m.recs, m.recN);
                DrawRecords(matShip, m.buf, at, m.recN);
            }
        }
    }
}
