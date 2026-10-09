// Voxel Forge — Unity port. Chunk-cached minimap rendered in software into a 144x144 texture.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        public const int MINIMAP_SIZE = 144;
        sealed class MinimapChunk { public int rev; public Color32[] px = new Color32[256]; }
        public static readonly Dictionary<string, object> minimapCache = new Dictionary<string, object>();
        static readonly Dictionary<int, double[]> minimapTileRGB = new Dictionary<int, double[]>();
        static double minimapLastSweep = 0, minimapLastX = double.PositiveInfinity, minimapLastZ = double.PositiveInfinity, minimapLastYaw = double.PositiveInfinity;
        static string minimapLastSpawn = "";
        public static Texture2D minimapTex;
        static Color32[] minimapBuf;
        public static bool minimapVisible = false;

        static void buildMinimapPalette()
        {
            if (atlasPixels == null) return;
            int W = atlasImg.width, H = atlasImg.height;
            for (int ti = 0; ti < TILE_NAMES.Count; ti++)
            {
                int col = ti % 32, row = ti / 32;
                if (row * 16 + 16 > H) continue;
                double r = 0, g = 0, b = 0; int n = 0;
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        // atlasPixels is bottom-up; canvas row (row*16 + y) from the top
                        var c = atlasPixels[(H - 1 - (row * 16 + y)) * W + col * 16 + x];
                        if (c.a > 48) { r += c.r; g += c.g; b += c.b; n++; }
                    }
                if (n > 0) minimapTileRGB[ti] = new[] { r / n, g / n, b / n };
            }
        }
        static void minimapTopCell(Chunk c, int lx, int lz, out int id, out int y)
        {
            for (int si = c.sections.Length - 1; si >= 0; si--)
            {
                var sec = c.sections[si];
                if (sec == null || sec.blocks == null) continue;
                for (int ly = 15; ly >= 0; ly--)
                {
                    int b = sec.blocks[(ly * CHUNK + lz) * CHUNK + lx];
                    if (b == B.AIR) continue;
                    var bd = bdef(b);
                    if ((bd != null && (bd.plant || bd.waterPlant)) || b == B.TORCH || b == B.FIRE) continue;
                    id = b; y = WORLD_MIN_Y + si * 16 + ly; return;
                }
            }
            id = B.AIR; y = WORLD_MIN_Y;
        }
        static Color32 minimapColor(int id, int y)
        {
            if (id == B.AIR) return new Color32(22, 30, 36, 255);
            var bd = bdef(id);
            int tile = bd != null && bd.top >= 0 ? bd.top : bd != null && bd.side >= 0 ? bd.side : T("stone");
            double[] bse;
            if (!minimapTileRGB.TryGetValue(tile, out bse))
            {
                if (isWater(id)) bse = new double[] { 45, 92, 148 };
                else if (id == B.GRASS) bse = new double[] { 85, 132, 64 };
                else if (id == B.SAND) bse = new double[] { 203, 190, 126 };
                else if (id == B.SNOW) bse = new double[] { 226, 235, 240 };
                else if (isTreeLeaves(id)) bse = new double[] { 60, 103, 51 };
                else bse = new double[] { 118, 112, 105 };
            }
            double shade = Math.Max(0.62, Math.Min(1.18, 0.86 + (y - SEA) * 0.006));
            Func<double, byte> cl = v => (byte)Math.Max(0, Math.Min(255, JS.round(v * shade)));
            return new Color32(cl(bse[0]), cl(bse[1]), cl(bse[2]), 255);
        }
        static void rebuildMinimapChunk(Chunk c)
        {
            var q = new MinimapChunk { rev = c.mapRev };
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int id, y; minimapTopCell(c, x, z, out id, out y);
                    q.px[z * 16 + x] = minimapColor(id, y);
                }
            minimapCache[c.key ?? ckey(c.cx, c.cz)] = q;
        }
        static void mmFill(int x0, int y0, int x1, int y1, Color32 col)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(MINIMAP_SIZE, x1); y1 = Math.Min(MINIMAP_SIZE, y1);
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) minimapBuf[(MINIMAP_SIZE - 1 - y) * MINIMAP_SIZE + x] = col;
        }
        static void mmPixel(int x, int y, Color32 c, float a = 1)
        {
            if (x < 0 || y < 0 || x >= MINIMAP_SIZE || y >= MINIMAP_SIZE) return;
            int i = (MINIMAP_SIZE - 1 - y) * MINIMAP_SIZE + x;
            if (a >= 1) { minimapBuf[i] = c; return; }
            var d = minimapBuf[i];
            minimapBuf[i] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a), (byte)(d.b + (c.b - d.b) * a), 255);
        }
        static bool pointInTri(double px, double py, double ax, double ay, double bx, double by, double cx, double cy)
        {
            double d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by), d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy), d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        static double segDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy, t = l2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0;
            double qx = ax + dx * t - px, qy = ay + dy * t - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }
        public static void updateMinimap(double now)
        {
            if (!modEnabled("minimap") || !started || !worldReady) { minimapVisible = false; return; }
            minimapVisible = true;
            if (minimapTex == null)
            {
                minimapTex = new Texture2D(MINIMAP_SIZE, MINIMAP_SIZE, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                minimapBuf = new Color32[MINIMAP_SIZE * MINIMAP_SIZE];
            }
            if (minimapTileRGB.Count == 0) buildMinimapPalette();
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
            double deadline = JS.now() + 1.5;
            bool changed = false, stop = false;
            for (int r = 0; r <= 5 && !stop; r++)
                for (int dz = -r; dz <= r && !stop; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        var c = chunkFastGet(pcx + dx, pcz + dz);
                        if (c == null) continue;
                        object old; minimapCache.TryGetValue(c.key ?? ckey(c.cx, c.cz), out old);
                        if (old == null || ((MinimapChunk)old).rev != c.mapRev)
                        {
                            rebuildMinimapChunk(c);
                            changed = true;
                            if (JS.now() >= deadline) { stop = true; break; }
                        }
                    }
            if (now - minimapLastSweep > 2000)
            {
                minimapLastSweep = now;
                var drop = new List<string>();
                foreach (var k in minimapCache.Keys) if (!chunks.Has(k)) drop.Add(k);
                foreach (var k in drop) { minimapCache.Remove(k); changed = true; }
            }
            string spawnKey = player.spawn != null ? string.Join(",", Array.ConvertAll(player.spawn, v => JS.ToStr(JS.round(v * 10) / 10))) : "";
            bool moved = Math.Abs(player.x - minimapLastX) > 0.2 || Math.Abs(player.z - minimapLastZ) > 0.2 || Math.Abs(player.yaw - minimapLastYaw) > 0.02 || spawnKey != minimapLastSpawn;
            if (!changed && !moved) return;
            minimapLastX = player.x; minimapLastZ = player.z; minimapLastYaw = player.yaw; minimapLastSpawn = spawnKey;
            int w = MINIMAP_SIZE, h = MINIMAP_SIZE; double scale = w <= 120 ? 1.15 : 1.35, cx = w / 2.0, cy = h / 2.0;
            mmFill(0, 0, w, h, new Color32(0x17, 0x21, 0x2a, 255));
            int mapRX = (int)Math.Ceiling(w / (2 * scale * CHUNK)) + 1, mapRZ = (int)Math.Ceiling(h / (2 * scale * CHUNK)) + 1;
            for (int dzc = -mapRZ; dzc <= mapRZ; dzc++)
                for (int dxc = -mapRX; dxc <= mapRX; dxc++)
                {
                    var c = chunkFastGet(pcx + dxc, pcz + dzc);
                    if (c == null) continue;
                    object qo; if (!minimapCache.TryGetValue(c.key ?? ckey(c.cx, c.cz), out qo)) continue;
                    var q = (MinimapChunk)qo;
                    double px = cx + (c.cx * CHUNK - player.x) * scale, py = cy + (c.cz * CHUNK - player.z) * scale, sz = CHUNK * scale;
                    if (px > w || py > h || px + sz < 0 || py + sz < 0) continue;
                    int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py), x1 = (int)Math.Ceiling(px + sz), y1 = (int)Math.Ceiling(py + sz);
                    for (int y = Math.Max(0, y0); y < Math.Min(h, y1); y++)
                    {
                        int ty = (int)Math.Floor((y + 0.5 - py) / scale); if (ty < 0 || ty > 15) continue;
                        for (int x = Math.Max(0, x0); x < Math.Min(w, x1); x++)
                        {
                            int tx = (int)Math.Floor((x + 0.5 - px) / scale); if (tx < 0 || tx > 15) continue;
                            minimapBuf[(h - 1 - y) * w + x] = q.px[ty * 16 + tx];
                        }
                    }
                }
            if (player.spawn != null)
            {
                double sx = cx + (player.spawn[0] - player.x) * scale, sy = cy + (player.spawn[2] - player.z) * scale;
                if (sx >= 0 && sx < w && sy >= 0 && sy < h) mmFill((int)Math.Round(sx - 2), (int)Math.Round(sy - 2), (int)Math.Round(sx + 2), (int)Math.Round(sy + 2), new Color32(0xff, 0xe3, 0x6a, 255));
            }
            // heading arrow (rotate(-yaw)): points (0,-7) (5,6) (0,3) (-5,6)
            double ca = Math.Cos(-player.yaw), sa = Math.Sin(-player.yaw);
            Func<double, double, double[]> rot = (x, y) => new[] { cx + x * ca - y * sa, cy + x * sa + y * ca };
            var P0 = rot(0, -7); var P1 = rot(5, 6); var P2 = rot(0, 3); var P3 = rot(-5, 6);
            var white = new Color32(255, 255, 255, 255); var dark = new Color32(0x11, 0x11, 0x11, 255);
            for (int y = (int)cy - 10; y <= (int)cy + 10; y++)
                for (int x = (int)cx - 10; x <= (int)cx + 10; x++)
                {
                    double qx = x + 0.5, qy = y + 0.5;
                    bool inside = pointInTri(qx, qy, P0[0], P0[1], P1[0], P1[1], P2[0], P2[1]) || pointInTri(qx, qy, P0[0], P0[1], P2[0], P2[1], P3[0], P3[1]);
                    double ed = Math.Min(Math.Min(segDist(qx, qy, P0[0], P0[1], P1[0], P1[1]), segDist(qx, qy, P1[0], P1[1], P2[0], P2[1])), Math.Min(segDist(qx, qy, P2[0], P2[1], P3[0], P3[1]), segDist(qx, qy, P3[0], P3[1], P0[0], P0[1])));
                    if (ed <= 0.75) mmPixel(x, y, dark);
                    else if (inside) mmPixel(x, y, white);
                }
            var border = new Color32(255, 255, 255, 255);
            for (int i = 0; i < w; i++) { mmPixel(i, 0, border, 0.4f); mmPixel(i, h - 1, border, 0.4f); mmPixel(0, i, border, 0.4f); mmPixel(w - 1, i, border, 0.4f); }
            minimapTex.SetPixels32(minimapBuf);
            minimapTex.Apply(false);
        }
    }
}
