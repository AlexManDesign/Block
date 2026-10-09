// Voxel Forge — Unity port. Block-break particles: fixed-capacity zero-GC pool.
using System;

namespace VoxelForge
{
    public static partial class VF
    {
        public const int BREAK_PARTICLE_MAX = 160, BREAK_PARTICLE_FLOATS_PER_VERTEX = 11, BREAK_PARTICLE_VERTS_PER_QUAD = 6;
        static readonly float[] breakPX = new float[BREAK_PARTICLE_MAX], breakPY = new float[BREAK_PARTICLE_MAX], breakPZ = new float[BREAK_PARTICLE_MAX],
            breakPVX = new float[BREAK_PARTICLE_MAX], breakPVY = new float[BREAK_PARTICLE_MAX], breakPVZ = new float[BREAK_PARTICLE_MAX],
            breakPU0 = new float[BREAK_PARTICLE_MAX], breakPV0 = new float[BREAK_PARTICLE_MAX], breakPU1 = new float[BREAK_PARTICLE_MAX], breakPV1 = new float[BREAK_PARTICLE_MAX],
            breakPSize = new float[BREAK_PARTICLE_MAX], breakPLife = new float[BREAK_PARTICLE_MAX], breakPShade = new float[BREAK_PARTICLE_MAX],
            breakPTintR = new float[BREAK_PARTICLE_MAX], breakPTintG = new float[BREAK_PARTICLE_MAX], breakPTintB = new float[BREAK_PARTICLE_MAX];
        static readonly ushort[] breakPTile = new ushort[BREAK_PARTICLE_MAX];
        static readonly byte[] breakPLight = new byte[BREAK_PARTICLE_MAX];
        public static int breakParticleCount = 0;
        static readonly sbyte[] BREAK_PARTICLE_SX = { -1, 1, -1, -1, 1, 1 }, BREAK_PARTICLE_SY = { -1, -1, 1, 1, -1, 1 };
        public static readonly float[] breakParticleVerts = new float[BREAK_PARTICLE_MAX * BREAK_PARTICLE_VERTS_PER_QUAD * BREAK_PARTICLE_FLOATS_PER_VERTEX];
        public static readonly double[] SHADE_POS = { 0.74, 1, 0.84 }, SHADE_NEG = { 0.66, 0.56, 0.78 };

        static bool validBreakParticleTile(int tile) { return tile >= 0 && tile < TILE_NAMES.Count; }
        /// <summary>[axis, sign] of the hit face or null; face name in <paramref name="face"/>.</summary>
        static int[] breakHitFace(Hit h, out string face)
        {
            face = null;
            var p = h != null ? h.prev : null;
            if (p == null) return null;
            int dx = p.x - h.x, dy = p.y - h.y, dz = p.z - h.z;
            if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) != 1) return null;
            if (dx != 0) { face = dx > 0 ? "east" : "west"; return new[] { 0, dx }; }
            if (dy != 0) { face = dy > 0 ? "up" : "down"; return new[] { 1, dy }; }
            face = dz > 0 ? "south" : "north"; return new[] { 2, dz };
        }
        static int firstValidBreakTile(params int[] a) { foreach (var t in a) if (validBreakParticleTile(t)) return t; return -1; }
        static int nativeBreakParticleTile(Hit h, int id, JObj m)
        {
            var b = bdef(id);
            if (b == null || id == B.AIR) return -1;
            string face; var f = breakHitFace(h, out face);
            if (f == null) return firstValidBreakTile(b.side, b.top, b.bottom, b.front);
            int axis = f[0], sign = f[1], tile;
            if (axisLogIdSync(id))
            {
                string logAxis = (m != null ? m.Str("axis") : null) ?? "y", faceAxis = axis == 0 ? "x" : axis == 1 ? "y" : "z";
                tile = faceAxis == logAxis ? b.top : b.side;
            }
            else if (b.front >= 0 && face == ((m != null ? m.Str("facing") : null) ?? "south"))
            {
                if (id == B.CHEST && m != null && m.Bool("chestDouble"))
                    tile = m.Bool("chestSide") != (face == "west" || face == "east") ? (b.frontL >= 0 ? b.frontL : b.front) : (b.frontR >= 0 ? b.frontR : b.front);
                else tile = b.front;
            }
            else tile = axis == 1 ? (sign > 0 ? b.top : b.bottom) : b.side;
            return firstValidBreakTile(tile, b.side, b.top, b.bottom, b.front);
        }
        static int virtualBreakParticleTile(Hit h, VirtualDef d, JObj m)
        {
            if (d == null) return -1;
            string face; var f = breakHitFace(h, out face); var sp = d.shape;
            bool top = m != null && m.Str("part") == "top";
            if ((sp == "door" || sp == "tallplant") && top && d.hasPartTop) return firstValidBreakTile(d.partTop, d.side, d.top, d.bottom);
            if ((sp == "door" || sp == "tallplant") && !top && d.hasPartBottom) return firstValidBreakTile(d.partBottom, d.side, d.top, d.bottom);
            if (f == null) return firstValidBreakTile(d.side, d.top, d.bottom);
            int axis = f[0], sign = f[1], tile;
            if (d.axislog)
            {
                string logAxis = (m != null ? m.Str("axis") : null) ?? "y", faceAxis = axis == 0 ? "x" : axis == 1 ? "y" : "z";
                tile = faceAxis == logAxis ? d.top : d.side;
            }
            else tile = axis == 1 ? (sign > 0 ? d.top : d.bottom) : d.side;
            return firstValidBreakTile(tile, d.side, d.top, d.bottom);
        }
        static double breakParticleFaceShade(Hit h)
        {
            string face; var f = breakHitFace(h, out face);
            if (f == null) return 0.76;
            double bse = f[1] > 0 ? SHADE_POS[f[0]] : SHADE_NEG[f[0]];
            return Math.Max(0.48, Math.Min(0.96, bse * 0.92));
        }
        static bool breakParticleTintableTile(int tile)
        {
            var n = tile >= 0 && tile < TILE_NAMES.Count ? TILE_NAMES[tile] : "";
            return n == "grass_top" || n == "short_grass" || n == "fern" || n == "large_fern_bottom" || n == "large_fern_top" || n.EndsWith("_leaves", StringComparison.Ordinal);
        }
        static void breakParticleBiomeTint(int tile, int x, int z, float[] outp, int i)
        {
            double r = 1, g = 1, b = 1;
            if (breakParticleTintableTile(tile))
            {
                var n = TILE_NAMES[tile]; bool leaf = n.EndsWith("_leaves", StringComparison.Ordinal); var bi = biomeAt(x, z);
                if (bi == "DESERT" || bi == "SAVANNA" || bi == "SAVANNA_PLATEAU" || bi == "WINDSWEPT_SAVANNA" || bi == "BADLANDS" || bi == "WOODED_BADLANDS" || bi == "ERODED_BADLANDS") { r = leaf ? 0.91 : 0.93; g = 0.98; b = leaf ? 0.8 : 0.76; }
                else if (bi == "SWAMP" || bi == "MANGROVE_SWAMP") { r = leaf ? 0.8 : 0.78; g = 0.93; b = leaf ? 0.74 : 0.7; }
                else if (bi == "JUNGLE" || bi == "SPARSE_JUNGLE" || bi == "BAMBOO_JUNGLE") { r = 0.84; g = 1; b = 0.82; }
                else if (bi == "TAIGA" || bi == "SNOWY_TAIGA" || bi == "OLD_GROWTH_PINE_TAIGA" || bi == "OLD_GROWTH_SPRUCE_TAIGA" || bi == "SNOWY_PLAINS" || bi == "ICE_SPIKES" || bi == "GROVE" || bi == "SNOWY_SLOPES" || bi == "FROZEN_PEAKS" || bi == "JAGGED_PEAKS") { r = 0.86; g = 0.96; b = 0.9; }
                else if (bi == "DARK_FOREST") { r = 0.84; g = 0.96; b = 0.79; }
                else if (bi == "CHERRY_GROVE") { r = 0.91; g = 1; b = 0.88; }
                else { r = 0.9; g = 1; b = 0.85; }
            }
            outp[i] = (float)r; outp[i + 1] = (float)g; outp[i + 2] = (float)b;
        }
        static readonly float[] breakSpawnTint = new float[3];
        static float rnd() { return (float)JS.random(); }
        public static void spawnBreakParticlesTile(int x, int y, int z, int tile, int count = 10, double shade = 0.76)
        {
            if (!validBreakParticleTile(tile)) return;
            int light = getPackedLightWorld(x, y, z);
            breakParticleBiomeTint(tile, x, z, breakSpawnTint, 0);
            float tr = breakSpawnTint[0], tg = breakSpawnTint[1], tb = breakSpawnTint[2];
            for (int n = 0; n < count && breakParticleCount < BREAK_PARTICLE_MAX; n++)
            {
                int i = breakParticleCount++;
                float px = 0.16f + rnd() * 0.68f, py = 0.16f + rnd() * 0.68f, pz = 0.16f + rnd() * 0.68f, u = rnd() * 0.72f, v = rnd() * 0.72f,
                    jx = (rnd() - 0.5f) * 0.26f, jy = rnd() * 0.35f, jz = (rnd() - 0.5f) * 0.26f;
                breakPX[i] = x + px; breakPY[i] = y + py; breakPZ[i] = z + pz;
                breakPVX[i] = (px - 0.5f) * 2.45f + jx; breakPVY[i] = (py - 0.5f) * 2.05f + 0.65f + jy; breakPVZ[i] = (pz - 0.5f) * 2.45f + jz;
                breakPU0[i] = u; breakPV0[i] = v; breakPU1[i] = u + 0.22f; breakPV1[i] = v + 0.22f;
                breakPSize[i] = 0.044f + rnd() * 0.046f; breakPLife[i] = 0.38f + rnd() * 0.3f;
                breakPTile[i] = (ushort)tile; breakPLight[i] = (byte)light; breakPShade[i] = (float)shade;
                breakPTintR[i] = tr; breakPTintG[i] = tg; breakPTintB[i] = tb;
            }
        }
        public static void spawnBreakParticles(Hit h, int id, int count = 10, JObj m = null)
        {
            int tile = nativeBreakParticleTile(h, id, m ?? getBlockMeta(h.x, h.y, h.z) ?? new JObj());
            if (tile >= 0) spawnBreakParticlesTile(h.x, h.y, h.z, tile, count, breakParticleFaceShade(h));
        }
        static void copyBreakParticle(int dst, int src)
        {
            breakPX[dst] = breakPX[src]; breakPY[dst] = breakPY[src]; breakPZ[dst] = breakPZ[src];
            breakPVX[dst] = breakPVX[src]; breakPVY[dst] = breakPVY[src]; breakPVZ[dst] = breakPVZ[src];
            breakPU0[dst] = breakPU0[src]; breakPV0[dst] = breakPV0[src]; breakPU1[dst] = breakPU1[src]; breakPV1[dst] = breakPV1[src];
            breakPSize[dst] = breakPSize[src]; breakPLife[dst] = breakPLife[src]; breakPTile[dst] = breakPTile[src]; breakPLight[dst] = breakPLight[src];
            breakPShade[dst] = breakPShade[src]; breakPTintR[dst] = breakPTintR[src]; breakPTintG[dst] = breakPTintG[src]; breakPTintB[dst] = breakPTintB[src];
        }
        public static void updateBreakParticles(double dt)
        {
            float drag = (float)Math.Pow(0.98, dt * 20), fdt = (float)dt;
            for (int i = breakParticleCount - 1; i >= 0; i--)
            {
                float life = breakPLife[i] - fdt;
                if (life <= 0) { int last = --breakParticleCount; if (i != last) copyBreakParticle(i, last); continue; }
                breakPLife[i] = life;
                float vx = breakPVX[i] * drag, vy = (breakPVY[i] - 16 * fdt) * drag, vz = breakPVZ[i] * drag, x = breakPX[i] + vx * fdt, y = breakPY[i] + vy * fdt, z = breakPZ[i] + vz * fdt;
                if (vy < 0)
                {
                    int by = JS.floor(y - breakPSize[i]), id = getBlock(JS.floor(x), by, JS.floor(z));
                    var bd = bdef(id);
                    if (bd != null && bd.solid) { vy = 0; vx *= 0.7f; vz *= 0.7f; y = Math.Max(y, by + 1 + breakPSize[i]); }
                }
                breakPVX[i] = vx; breakPVY[i] = vy; breakPVZ[i] = vz; breakPX[i] = x; breakPY[i] = y; breakPZ[i] = z;
            }
        }
        /// <summary>Builds camera-facing particle quads (CPU part of drawBreakParticles). Returns float count.</summary>
        public static int buildBreakParticleVerts(double ex, double ey, double ez, float rx, float ry, float rz, float ux, float uy, float uz)
        {
            if (breakParticleCount == 0) return 0;
            int o = 0; var V = breakParticleVerts;
            for (int i = 0; i < breakParticleCount; i++)
            {
                float x = breakPX[i], y = breakPY[i], z = breakPZ[i];
                double dx = x - ex, dy = y - ey, dz = z - ez;
                if (dx * dx + dy * dy + dz * dz < 0.085) continue;
                float sz = breakPSize[i], u0 = breakPU0[i], v0 = breakPV0[i], u1 = breakPU1[i], v1 = breakPV1[i], tile = breakPTile[i], light = breakPLight[i];
                float shade = breakPShade[i] != 0 ? breakPShade[i] : 0.76f, tr = breakPTintR[i] != 0 ? breakPTintR[i] : 1, tg = breakPTintG[i] != 0 ? breakPTintG[i] : 1, tb = breakPTintB[i] != 0 ? breakPTintB[i] : 1;
                for (int k = 0; k < 6; k++)
                {
                    int sx = BREAK_PARTICLE_SX[k], sy = BREAK_PARTICLE_SY[k];
                    V[o++] = x + (rx * sx + ux * sy) * sz; V[o++] = y + (ry * sx + uy * sy) * sz; V[o++] = z + (rz * sx + uz * sy) * sz;
                    V[o++] = sx < 0 ? u0 : u1; V[o++] = sy < 0 ? v1 : v0;
                    V[o++] = tile; V[o++] = shade; V[o++] = light; V[o++] = tr; V[o++] = tg; V[o++] = tb;
                }
            }
            return o;
        }
    }
}
