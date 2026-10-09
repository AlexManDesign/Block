// Voxel Forge — Unity port. Terrain atlas -> Texture2DArray (one layer per 16x16 tile) with CPU-built,
// alpha-coverage-preserving mipmaps; runtime flowing-water tile; entity/item atlases.
using System;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        public static Texture2D atlasImg;          // full 512x512 terrain atlas (point, readable) — also used for UI icons
        public static Texture2DArray atlasArray;   // 768 layers x 16x16, 5 mips
        public static Texture2D entityTex, itemTex;
        public static Color32[] atlasPixels;       // atlas pixels, Unity orientation (row 0 = bottom)
        public static Color32[] itemPixels;
        const int ATLAS_MIP_LEVELS = 5, ATLAS_ALPHA_CUT = 31;

        static Texture2D loadPng(string res, bool readable)
        {
            var ta = Resources.Load<TextAsset>("VoxelForge/" + res);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            t.LoadImage(ta.bytes, !readable);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.anisoLevel = 0;
            return t;
        }

        static void InitAtlases()
        {
            atlasImg = loadPng("terrain_atlas.png", true);
            atlasPixels = atlasImg.GetPixels32();
            entityTex = loadPng("entity_atlas.png", false);
            itemTex = loadPng("item_atlas.png", true);
            itemPixels = itemTex.GetPixels32();
            atlasArray = buildTerrainAtlasArray();
        }

        /// <summary>16x16 tile pixels of the atlas in layer orientation (row 0 = bottom of tile).</summary>
        public static byte[] atlasTileRGBA(int ti)
        {
            var o = new byte[16 * 16 * 4];
            int col = ti & 31, row = ti >> 5, x0 = col * 16, y0 = (31 - row) * 16, W = atlasImg.width;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    var c = atlasPixels[(y0 + y) * W + x0 + x]; int p = (y * 16 + x) * 4;
                    o[p] = c.r; o[p + 1] = c.g; o[p + 2] = c.b; o[p + 3] = c.a;
                }
            return o;
        }

        // Build a directional flow sprite from our own still-water art (top-down canvas orientation),
        // anisotropically blurred along the flow direction with no per-column phase offset.
        static byte[] buildRuntimeWaterFlowTile()
        {
            int ti = T("water");
            var layer = atlasTileRGBA(ti);
            // to canvas orientation (row 0 = top)
            var sp = new byte[16 * 16 * 4];
            for (int y = 0; y < 16; y++) Buffer.BlockCopy(layer, (15 - y) * 64, sp, y * 64, 64);
            var dp = new byte[16 * 16 * 4];
            int[] vw = { 1, 2, 3, 4, 3, 2, 1 }, hw = { 1, 2, 1 };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int sy = (y * 2) & 15, o = (y * 16 + x) * 4;
                    double r = 0, g = 0, b = 0, a = 0, tw = 0;
                    for (int ky = -3; ky <= 3; ky++)
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int w = vw[ky + 3] * hw[kx + 1], q = (((sy + ky) & 15) * 16 + ((x + kx) & 15)) * 4;
                            r += sp[q] * w; g += sp[q + 1] * w; b += sp[q + 2] * w; a += sp[q + 3] * w; tw += w;
                        }
                    int bs = (y * 16 + x) * 4;
                    dp[o] = (byte)Math.Max(0, Math.Min(255, (r / tw) * 0.8 + sp[bs] * 0.2));
                    dp[o + 1] = (byte)Math.Max(0, Math.Min(255, (g / tw) * 0.8 + sp[bs + 1] * 0.2));
                    dp[o + 2] = (byte)Math.Max(0, Math.Min(255, (b / tw) * 0.8 + sp[bs + 2] * 0.2));
                    dp[o + 3] = (byte)Math.Max(0, Math.Min(255, a / tw));
                }
            // back to layer orientation (FLIP_Y upload)
            var outp = new byte[16 * 16 * 4];
            for (int y = 0; y < 16; y++) Buffer.BlockCopy(dp, (15 - y) * 64, outp, y * 64, 64);
            return outp;
        }

        static float[] atlasMipDown(float[] src, int n)
        {
            int m = n >> 1; var d = new float[m * m * 4];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0, pr = 0, pg = 0, pb = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int o2 = ((y * 2 + (k >> 1)) * n + x * 2 + (k & 1)) * 4; float al = src[o2 + 3];
                        r += src[o2] * al; g += src[o2 + 1] * al; b += src[o2 + 2] * al; a += al;
                        pr += src[o2]; pg += src[o2 + 1]; pb += src[o2 + 2];
                    }
                    int o = (y * m + x) * 4;
                    if (a > 0) { d[o] = r / a; d[o + 1] = g / a; d[o + 2] = b / a; }
                    else { d[o] = pr / 4; d[o + 1] = pg / 4; d[o + 2] = pb / 4; }
                    d[o + 3] = a / 4;
                }
            return d;
        }
        static double atlasAlphaCoverage(float[] p, double s)
        {
            int c = 0, n = p.Length >> 2;
            for (int i = 3; i < p.Length; i += 4) if (Math.Min(255, p[i] * s) >= ATLAS_ALPHA_CUT) c++;
            return c / (double)n;
        }
        static byte[][] buildAtlasTileMips(byte[] tile0)
        {
            bool binary = true, holes = false;
            for (int i = 3; i < tile0.Length; i += 4)
            {
                int a = tile0[i];
                if (a > 8 && a < 247) { binary = false; break; }
                if (a <= 8) holes = true;
            }
            bool keepCov = binary && holes;
            var cur = new float[tile0.Length]; for (int i = 0; i < tile0.Length; i++) cur[i] = tile0[i];
            double cov0 = keepCov ? atlasAlphaCoverage(cur, 1) : 0;
            var outp = new byte[ATLAS_MIP_LEVELS - 1][];
            int n = 16;
            for (int l = 1; l < ATLAS_MIP_LEVELS; l++)
            {
                cur = atlasMipDown(cur, n); n >>= 1;
                double s = 1;
                if (keepCov)
                {
                    double lo = 0, hi = 64;
                    for (int it = 0; it < 18; it++) { double mid = (lo + hi) / 2; if (atlasAlphaCoverage(cur, mid) >= cov0) hi = mid; else lo = mid; }
                    s = hi;
                }
                var u = new byte[cur.Length];
                for (int i = 0; i < cur.Length; i++) u[i] = (byte)Math.Max(0, Math.Min(255, Math.Round((i & 3) == 3 ? cur[i] * s : cur[i], MidpointRounding.AwayFromZero)));
                outp[l - 1] = u;
            }
            return outp;
        }
        static Color32[] toColors(byte[] b)
        {
            var c = new Color32[b.Length / 4];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]);
            return c;
        }
        static Texture2DArray buildTerrainAtlasArray()
        {
            int layers = Math.Max(TILE_NAMES.Count, 1);
            var arr = new Texture2DArray(16, 16, layers, TextureFormat.RGBA32, true, false);
            arr.filterMode = FilterMode.Point;
            arr.wrapMode = TextureWrapMode.Clamp;
            arr.anisoLevel = 0;
            for (int ti = 0; ti < TILE_NAMES.Count; ti++)
            {
                byte[] t0 = ti == WATER_FLOW_TILE ? buildRuntimeWaterFlowTile() : atlasTileRGBA(ti);
                arr.SetPixels32(toColors(t0), ti, 0);
                var mips = buildAtlasTileMips(t0);
                for (int l = 0; l < mips.Length; l++) arr.SetPixels32(toColors(mips[l]), ti, l + 1);
            }
            arr.Apply(false, true);
            return arr;
        }
    }
}
