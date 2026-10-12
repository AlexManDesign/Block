// Port of noise.js: seeded simplex noise (2D/3D) + small hash helpers. Worker side.
// Integer hashing reproduces the original's 32-bit arithmetic exactly (unchecked int / uint ops),
// so a seed builds the same world as the browser version.
using System;

namespace Maincraft
{
    /// <summary>mulberry32 generator of the original (a closure there, a small class here).</summary>
    public sealed class Mulberry
    {
        int s;
        public Mulberry(uint seed) { s = unchecked((int)seed); }
        public Mulberry(int seed) { s = seed; }
        public double Next()
        {
            unchecked
            {
                s = s + 0x6D2B79F5;
                int t = (s ^ (int)((uint)s >> 15)) * (1 | s);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
    }

    public static class Hash
    {
        public static Mulberry Mulberry(int seed) => new Mulberry(seed);
        public static Mulberry Mulberry(uint seed) => new Mulberry(seed);

        public static uint Hash2i(int seed, int x, int z)
        {
            unchecked
            {
                int h = (x * 0x27d4eb2d) ^ (z * 0x165667b1) ^ (seed * (int)0x9E3779B1);
                h = (h ^ (int)((uint)h >> 15)) * (int)0x85ebca6b;
                h = (h ^ (int)((uint)h >> 13)) * (int)0xc2b2ae35;
                return (uint)(h ^ (int)((uint)h >> 16));
            }
        }
        public static double Hash2(int seed, int x, int z) => Hash2i(seed, x, z) / 4294967296.0;
        public static double Hash3(int seed, int x, int y, int z) => Hash2i(unchecked(seed + y * 0x3C6EF372), x, z) / 4294967296.0;
    }

    public sealed class Simplex
    {
        static readonly double F2 = 0.5 * (Math.Sqrt(3) - 1), G2 = (3 - Math.Sqrt(3)) / 6, F3 = 1.0 / 3, G3 = 1.0 / 6;
        static readonly float[] GRAD3 = { 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0, 1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
            0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1 };

        readonly byte[] perm = new byte[512], pm12 = new byte[512];
        public readonly double ox, oz, oy;

        public Simplex(uint seed)
        {
            var rnd = new Mulberry(seed);
            var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = (int)(rnd.Next() * (i + 1)); byte t = p[i]; p[i] = p[j]; p[j] = t; }
            for (int i = 0; i < 512; i++) { perm[i] = p[i & 255]; pm12[i] = (byte)((perm[i] % 12) * 3); }
            ox = rnd.Next() * 4096; oz = rnd.Next() * 4096; oy = rnd.Next() * 4096;
        }

        public double n2(double xin, double yin)
        {
            xin += ox; yin += oz;
            var perm = this.perm; var pm = pm12;
            double s = (xin + yin) * F2;
            double i = Math.Floor(xin + s), j = Math.Floor(yin + s);
            double t = (i + j) * G2;
            double x0 = xin - (i - t), y0 = yin - (j - t);
            int i1, j1; if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }
            double x1 = x0 - i1 + G2, y1 = y0 - j1 + G2, x2 = x0 - 1 + 2 * G2, y2 = y0 - 1 + 2 * G2;
            int ii = (int)i & 255, jj = (int)j & 255;
            double n = 0, tt; int g;
            tt = 0.5 - x0 * x0 - y0 * y0;
            if (tt > 0) { g = pm[ii + perm[jj]]; tt *= tt; n += tt * tt * (GRAD3[g] * x0 + GRAD3[g + 1] * y0); }
            tt = 0.5 - x1 * x1 - y1 * y1;
            if (tt > 0) { g = pm[ii + i1 + perm[jj + j1]]; tt *= tt; n += tt * tt * (GRAD3[g] * x1 + GRAD3[g + 1] * y1); }
            tt = 0.5 - x2 * x2 - y2 * y2;
            if (tt > 0) { g = pm[ii + 1 + perm[jj + 1]]; tt *= tt; n += tt * tt * (GRAD3[g] * x2 + GRAD3[g + 1] * y2); }
            return 70 * n;
        }

        public double n3(double xin, double yin, double zin)
        {
            xin += ox; yin += oy; zin += oz;
            var perm = this.perm; var pm = pm12;
            double s = (xin + yin + zin) * F3;
            double i = Math.Floor(xin + s), j = Math.Floor(yin + s), k = Math.Floor(zin + s);
            double t = (i + j + k) * G3;
            double x0 = xin - (i - t), y0 = yin - (j - t), z0 = zin - (k - t);
            int i1, j1, k1, i2, j2, k2;
            if (x0 >= y0)
            {
                if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
                else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
                else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
            }
            else
            {
                if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
                else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
                else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            }
            double x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
            double x2 = x0 - i2 + 2 * G3, y2 = y0 - j2 + 2 * G3, z2 = z0 - k2 + 2 * G3;
            double x3 = x0 - 1 + 3 * G3, y3 = y0 - 1 + 3 * G3, z3 = z0 - 1 + 3 * G3;
            int ii = (int)i & 255, jj = (int)j & 255, kk = (int)k & 255;
            double n = 0, tt; int g;
            tt = 0.6 - x0 * x0 - y0 * y0 - z0 * z0;
            if (tt > 0) { g = pm[ii + perm[jj + perm[kk]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x0 + GRAD3[g + 1] * y0 + GRAD3[g + 2] * z0); }
            tt = 0.6 - x1 * x1 - y1 * y1 - z1 * z1;
            if (tt > 0) { g = pm[ii + i1 + perm[jj + j1 + perm[kk + k1]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x1 + GRAD3[g + 1] * y1 + GRAD3[g + 2] * z1); }
            tt = 0.6 - x2 * x2 - y2 * y2 - z2 * z2;
            if (tt > 0) { g = pm[ii + i2 + perm[jj + j2 + perm[kk + k2]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x2 + GRAD3[g + 1] * y2 + GRAD3[g + 2] * z2); }
            tt = 0.6 - x3 * x3 - y3 * y3 - z3 * z3;
            if (tt > 0) { g = pm[ii + 1 + perm[jj + 1 + perm[kk + 1]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x3 + GRAD3[g + 1] * y3 + GRAD3[g + 2] * z3); }
            return 32 * n;
        }

        // fractal sums, normalised to roughly [-1,1]
        public double f2(double x, double z, int oct)
        {
            double s = 0, a = 1, n = 0;
            for (int o = 0; o < oct; o++) { s += n2(x, z) * a; n += a; a *= 0.5; x = x * 2.03 + 17.1; z = z * 2.03 - 9.3; }
            return s / n;
        }

        public double f3(double x, double y, double z, int oct)
        {
            double s = 0, a = 1, n = 0;
            for (int o = 0; o < oct; o++) { s += n3(x, y, z) * a; n += a; a *= 0.5; x = x * 2.01 + 7.7; y = y * 2.01 - 3.1; z = z * 2.01 + 11.3; }
            return s / n;
        }
    }

    // Value noise of the original maincraft generator (hash lattice + smoothstep, rotated octaves).
    // Its thresholds (coral reefs, kelp forests, icebergs, podzol / packed-ice patches, windswept
    // gravel) are tuned to this distribution, so it is ported as is rather than replaced by simplex.
    public sealed class ValueNoise
    {
        static readonly double[][] VN_ROT = { new[] { 1.0, 0 }, new[] { 0.866, 0.5 }, new[] { 0.5, 0.866 }, new[] { 0.0, 1 } };
        readonly int s;
        public ValueNoise(int seed) { s = seed; }

        // lattice hash: products summed in double precision as the original does, low 32 bits kept
        public double g(int a, int c)
        {
            unchecked
            {
                double wd = (double)(a + s) * 374761393 + (double)(c - s) * 668265263 + 527595518;
                int w = (int)(long)wd;
                w ^= (int)((uint)w >> 13); w *= 1274126177;
                return (uint)(w ^ (int)((uint)w >> 16)) / 4294967296.0;
            }
        }

        public double g3(int a, int c, int e)
        {
            unchecked
            {
                double qd = (double)(a + s) * 374761393 + (double)c * 668265263 + (double)(e - s) * 2147483423;
                int q = (int)(long)qd;
                q ^= (int)((uint)q >> 13); q *= 1274126177;
                return (uint)(q ^ (int)((uint)q >> 16)) / 4294967296.0;
            }
        }

        public double v2(double x, double z)
        {
            double fxi = Math.Floor(x), fzi = Math.Floor(z), fx = x - fxi, fz = z - fzi;
            int xi = (int)fxi, zi = (int)fzi;
            double a = g(xi, zi), b = g(xi + 1, zi), c = g(xi, zi + 1), d = g(xi + 1, zi + 1);
            double u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        // 4 octaves, each rotated 30 degrees further; result in [0, 1]
        public double f2(double x, double z)
        {
            double s = 0, amp = 0.5, fr = 1, tot = 0;
            for (int k = 0; k < 4; k++)
            {
                var r = VN_ROT[k];
                double u = x * r[0] - z * r[1], v = x * r[1] + z * r[0];
                s += v2(u * fr + k * 119.7, v * fr - k * 53.3) * amp; tot += amp; amp *= 0.5; fr *= 2.17;
            }
            return s / tot;
        }
    }
}
