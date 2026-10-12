// Sky, sun, moon and clouds of render.js.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed partial class Renderer
    {
        Mesh skyCube, cloudMesh;
        readonly double[] seaClip = new double[2];

        void InitSky()
        {
            // a cube round the camera: each fragment's direction is its position (the camera sits at the origin)
            skyCube = new Mesh();
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1);
            skyCube.vertices = v;
            skyCube.triangles = new[] { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            skyCube.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            cloudMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            cloudMesh.MarkDynamic();
        }

        // ---------------------------------------------------------------- sky, sun, moon
        // Drawn after opaque terrain, on the far plane, so it only fills what the terrain left empty.
        void DrawSky(Env env, double camY)
        {
            // the sea surface (top water block at SEA, 8/9 full) out to the edge of the drawn world: what the
            // sun and moon sink behind (rays meeting it there would show them through the water)
            seaClip[0] = camY - (SEA + 8.0 / 9); seaClip[1] = (env.renderDist + 0.5) * 16;
            mpb.Clear();
            mpb.SetVector(ID_Zenith, V3(env.zenith));
            mpb.SetVector(ID_Horizon, V3(env.fogColor));
            mpb.SetVector(ID_SunDir, V3(env.sunDir));
            mpb.SetVector(ID_Sunset, new Vector4((float)env.sunsetColor[0], (float)env.sunsetColor[1], (float)env.sunsetColor[2], (float)env.sunset));
            mpb.SetFloat(ID_Stars, (float)env.stars);
            mpb.SetFloat(ID_Void, 1);
            cb.DrawMesh(skyCube, Matrix4x4.identity, matSky, 0, 0, mpb);
            // sun & moon as billboards at distance 100 along their directions
            var s = env.sunDir;
            DrawCelestial(sunTex, s[0], s[1], s[2], 16, 0, 0, 1, 1);
            int ph = env.moonPhase;
            double u0 = (ph % 4) / 4.0, v0 = (ph / 4) / 2.0;
            DrawCelestial(moonTex, -s[0], -s[1], -s[2], 11, u0, v0, u0 + 0.25, v0 + 0.5);
        }

        static Vector4 V3(double[] a) => new Vector4((float)a[0], (float)a[1], (float)a[2], 0);

        readonly float[] spriteV = new float[60];

        void DrawCelestial(Texture tex, double dx, double dy, double dz, double size, double ua, double va, double ub, double vb)
        {
            const double D = 100;
            double cx = dx * D, cy = dy * D, cz = dz * D;
            // basis perpendicular to dir: axis a = z (sun path is in x-y plane), b = dir x a
            double ax = 0, ay = 0, az = 1;
            double bx = dy * az - dz * ay, by = dz * ax - dx * az, bz = dx * ay - dy * ax;
            double bl = Math.Sqrt(bx * bx + by * by + bz * bz); if (bl == 0) bl = 1;
            bx /= bl; by /= bl; bz /= bl;
            // re-orthogonalise a
            ax = by * dz - bz * dy; ay = bz * dx - bx * dz; az = bx * dy - by * dx;
            double h = size / 2;
            var v = spriteV; int n = 0;
            void Corner(double sa, double sb, double u, double w)
            {
                v[n] = (float)(cx + (ax * sa + bx * sb) * h); v[n + 1] = (float)(cy + (ay * sa + by * sb) * h); v[n + 2] = (float)(cz + (az * sa + bz * sb) * h);
                v[n + 3] = (float)u; v[n + 4] = (float)w; v[n + 5] = 0; v[n + 6] = 1; v[n + 7] = 1; v[n + 8] = 1; v[n + 9] = 1; n += 10;
            }
            Corner(-1, -1, ua, vb); Corner(1, -1, ub, vb); Corner(1, 1, ub, va);
            Corner(-1, -1, ua, vb); Corner(1, 1, ub, va); Corner(-1, 1, ua, va);
            int first = AppendDyn(v, 6);
            mpb.Clear();
            mpb.SetFloat(ID_VBase, first);
            mpb.SetTexture(ID_MainTex, tex);
            mpb.SetFloat(ID_KeyBlack, 1);
            mpb.SetVector(ID_SeaClip, new Vector4((float)Math.Max(0, seaClip[0]), (float)seaClip[1], 0, 0));
            cb.DrawProcedural(Matrix4x4.identity, matSun, 0, MeshTopology.Triangles, 6, 1, mpb);
        }

        // ---------------------------------------------------------------- clouds (MC-like 12x12x4 cells at y=192)
        byte[] cloudGrid; int cloudN;
        int cgx0 = int.MinValue, cgz0, cRR;
        readonly List<Vector3> cv = new List<Vector3>(); readonly List<Color> ccol = new List<Color>(); readonly List<int> cidx = new List<int>();

        public void BuildClouds(int seed)
        {
            const int N = 256; var g = new byte[N * N];
            double Rnd(int x, int z)
            {
                unchecked
                {
                    int hh = (x * 374761393) ^ (z * 668265263) ^ seed;
                    hh = (hh ^ (int)((uint)hh >> 13)) * 1274126177;
                    return (uint)(hh ^ (int)((uint)hh >> 16)) / 4294967296.0;
                }
            }
            double Val(int x, int z, int s)
            {
                int x0 = (int)Math.Floor((double)x / s), z0 = (int)Math.Floor((double)z / s); double fx = (double)x / s - x0, fz = (double)z / s - z0;
                int M = N / s - 1;
                double a = Rnd(x0 & M, z0 & M), b = Rnd((x0 + 1) & M, z0 & M);
                double c = Rnd(x0 & M, (z0 + 1) & M), d = Rnd((x0 + 1) & M, (z0 + 1) & M);
                double sx = fx * fx * (3 - 2 * fx), sz = fz * fz * (3 - 2 * fz);
                return a + (b - a) * sx + (c - a) * sz + (a - b - c + d) * sx * sz;
            }
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                double v = Val(x, z, 16) * 0.6 + Val(x, z, 8) * 0.3 + Val(x, z, 4) * 0.1;
                g[z * N + x] = (byte)(v > 0.56 ? 1 : 0);
            }
            cloudGrid = g; cloudN = N; cgx0 = int.MinValue;
        }

        /// <summary>The cloud layer (drawClouds): a depth-only pass, then colour where the depth matches.</summary>
        public void DrawClouds(double camX, double camY, double camZ, Env env, double time)
        {
            if (cloudGrid == null) return;
            int N = cloudN; var g = cloudGrid;
            const int S = 12, H = 4; const double Y = 192.33;
            double drift = time * 0.6;
            int R = Math.Min(40, (int)Math.Ceiling(env.far / S) + 2);
            double ox = camX + drift, oz = camZ;
            // the mesh covers 8 cells more than is visible around an anchor that moves in 8-cell steps, so
            // it is rebuilt only every ~100 blocks of travel; the colour is a uniform, not baked in
            const int M = 8; int RR = R + M;
            int gx0 = (int)Math.Floor(Math.Floor(ox / S) / M + 0.5) * M, gz0 = (int)Math.Floor(Math.Floor(oz / S) / M + 0.5) * M;
            if (gx0 != cgx0 || gz0 != cgz0 || RR != cRR)
            {
                cgx0 = gx0; cgz0 = gz0; cRR = RR;
                int At(int x, int z) => g[((z % N + N) % N) * N + ((x % N + N) % N)];
                cv.Clear(); ccol.Clear(); cidx.Clear();
                void Quad(double ax, double ay, double az, double bx, double by, double bz, double qx, double qy, double qz, double dx, double dy, double dz, float sh)
                {
                    int b0 = cv.Count;
                    cv.Add(new Vector3((float)ax, (float)ay, (float)az)); cv.Add(new Vector3((float)bx, (float)by, (float)bz));
                    cv.Add(new Vector3((float)qx, (float)qy, (float)qz)); cv.Add(new Vector3((float)dx, (float)dy, (float)dz));
                    var col = new Color(sh, sh, sh, 0.8f);
                    ccol.Add(col); ccol.Add(col); ccol.Add(col); ccol.Add(col);
                    cidx.Add(b0); cidx.Add(b0 + 1); cidx.Add(b0 + 2); cidx.Add(b0); cidx.Add(b0 + 2); cidx.Add(b0 + 3);
                }
                for (int dz = -RR; dz <= RR; dz++) for (int dx = -RR; dx <= RR; dx++)
                {
                    if (dx * dx + dz * dz > RR * RR) continue;
                    int cx = gx0 + dx, cz = gz0 + dz;
                    if (At(cx, cz) == 0) continue;
                    double x0 = dx * S, x1 = x0 + S, z0 = dz * S, z1 = z0 + S, y0 = 0, y1 = H;
                    Quad(x0, y1, z1, x1, y1, z1, x1, y1, z0, x0, y1, z0, 1.0f);
                    Quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1, 0.7f);
                    if (At(cx + 1, cz) == 0) Quad(x1, y0, z1, x1, y0, z0, x1, y1, z0, x1, y1, z1, 0.9f);
                    if (At(cx - 1, cz) == 0) Quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0, 0.9f);
                    if (At(cx, cz + 1) == 0) Quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1, 0.8f);
                    if (At(cx, cz - 1) == 0) Quad(x1, y0, z0, x0, y0, z0, x0, y1, z0, x1, y1, z0, 0.8f);
                }
                cloudMesh.Clear();
                cloudMesh.SetVertices(cv); cloudMesh.SetColors(ccol); cloudMesh.SetTriangles(cidx, 0, false);
                cloudMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
            if (cv.Count == 0) return;
            // grid-space mesh moved to camera-relative space
            double tx = gx0 * S - drift - camX, ty = Y - camY, tz = gz0 * S - camZ;
            var col2 = env.cloudColor;
            mpb.Clear();
            mpb.SetVector(ID_Mul, new Vector4((float)col2[0], (float)col2[1], (float)col2[2], 1));
            // fog distance is measured in mesh space; approximate by far distances
            mpb.SetVector(ID_CloudFog, new Vector4((float)(env.far * 0.9 + 400), (float)(env.far * 2 + 600), 0, 0));
            mpb.SetVector(ID_FogColor, new Vector4((float)env.fogColor[0], (float)env.fogColor[1], (float)env.fogColor[2], 1));
            var T = Matrix4x4.Translate(new Vector3((float)tx, (float)ty, (float)tz));
            cb.DrawMesh(cloudMesh, T, matClouds, 0, 0, mpb);
            cb.DrawMesh(cloudMesh, T, matClouds, 0, 1, mpb);
        }
    }
}
