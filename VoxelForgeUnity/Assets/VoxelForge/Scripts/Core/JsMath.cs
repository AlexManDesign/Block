// Voxel Forge — Unity port. Bit-exact V8 (Node 22 / V8 12.4, src/base/ieee754.cc = fdlibm) transcendental math.
// System.Math defers to the platform libm (glibc / MSVC CRT / bionic / Apple), whose last-ulp results differ per platform,
// so worldgen heights / ravines near rounding edges would differ by device. These are pure managed ports of the exact
// routines V8 executes for Math.pow/sin/cos/tan/log/log10/atan/atan2 (Node is built without v8_use_libm_trig_functions,
// so sin/cos are fdlibm too) and of the Torque Math.hypot builtin. Allocation-free, thread-safe, no unsafe code.
// Verified bit-for-bit against Node 22 x64 by Tools/parity-math (millions of samples per function).
using System;
using System.Runtime.CompilerServices;

namespace VoxelForge
{
    public static class JsMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static int HI(double d) { return (int)(BitConverter.DoubleToInt64Bits(d) >> 32); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static uint LO(double d) { return (uint)BitConverter.DoubleToInt64Bits(d); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static double MK(int hi, uint lo) { return BitConverter.Int64BitsToDouble(((long)hi << 32) | lo); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static double SETLO(double d, uint lo) { return BitConverter.Int64BitsToDouble((BitConverter.DoubleToInt64Bits(d) & unchecked((long)0xFFFFFFFF00000000UL)) | lo); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static double SETHI(double d, int hi) { return BitConverter.Int64BitsToDouble((BitConverter.DoubleToInt64Bits(d) & 0xFFFFFFFFL) | ((long)hi << 32)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static double CHOPLO(double d) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(d) & unchecked((long)0xFFFFFFFF00000000UL)); }

        // fdlibm s_scalbn.c (exact / correctly rounded, like the C library scalbn V8 links)
        static double scalbn(double x, int n)
        {
            const double two54 = 1.80143985094819840000e+16, twom54 = 5.55111512312578270212e-17, huge = 1.0e+300, tiny = 1.0e-300;
            int hx = HI(x); uint lx = LO(x); int k = (hx & 0x7ff00000) >> 20;
            if (k == 0) { if ((lx | (uint)(hx & 0x7fffffff)) == 0) return x; x *= two54; hx = HI(x); k = ((hx & 0x7ff00000) >> 20) - 54; if (n < -50000) return tiny * x; }
            if (k == 0x7ff) return x + x;
            k = k + n;
            if (k > 0x7fe) return huge * (x < 0 ? -huge : huge);
            if (k > 0) return SETHI(x, (hx & unchecked((int)0x800fffff)) | (k << 20));
            if (k <= -54) { if (n > 50000) return huge * (x < 0 ? -huge : huge); return tiny * (x < 0 ? -tiny : tiny); }
            k += 54;
            return SETHI(x, (hx & unchecked((int)0x800fffff)) | (k << 20)) * twom54;
        }

        static readonly int[] two_over_pi = {
            0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C, 0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A,
            0x424DD2, 0xE00649, 0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44, 0x84E99C, 0x7026B4, 0x5F7E41,
            0x3991D6, 0x398353, 0x39F49C, 0x845F8B, 0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D, 0x367ECF,
            0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5, 0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08,
            0x560330, 0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E, 0xE61B08, 0x659985, 0x5F14A0, 0x68408D, 0xFFD880,
            0x4D7327, 0x310606, 0x1556CA, 0x73A8C9, 0x60E27B, 0xC08C6B,
        };
        static readonly int[] npio2_hw = {
            0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C,
            0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C,
            0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB,
        };
        static readonly double[] PIo2 = {
            1.57079625129699707031e+00, 7.54978941586159635335e-08, 5.39030252995776476554e-15, 3.28200341580791294123e-22,
            1.27065575308067607349e-29, 1.22933308981111328932e-36, 2.73370053816464559624e-44, 2.16741683877804819444e-51,
        };
        // per-thread scratch for the (rare, |x| > 2^19*pi/2) Payne-Hanek path: worldgen runs on several threads
        sealed class RemScratch { public readonly double[] tx = new double[3], f = new double[20], fq = new double[20], q = new double[20]; public readonly int[] iq = new int[20]; }
        [ThreadStatic] static RemScratch remScratch;

        // __ieee754_rem_pio2: x rem pi/2 in y0+y1, returns n
        static int remPio2(double x, out double y0, out double y1)
        {
            const double half = 5.00000000000000000000e-01, two24 = 1.67772160000000000000e+07, invpio2 = 6.36619772367581382433e-01,
                pio2_1 = 1.57079632673412561417e+00, pio2_1t = 6.07710050650619224932e-11, pio2_2 = 6.07710050630396597660e-11,
                pio2_2t = 2.02226624879595063154e-21, pio2_3 = 2.02226624871116645580e-21, pio2_3t = 8.47842766036889956997e-32;
            double z = 0, w, t, r, fn; int e0, i, j, nx, n, ix, hx;
            hx = HI(x); ix = hx & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) { y0 = x; y1 = 0; return 0; }
            if (ix < 0x4002D97C)
            {
                if (hx > 0)
                {
                    z = x - pio2_1;
                    if (ix != 0x3FF921FB) { y0 = z - pio2_1t; y1 = (z - y0) - pio2_1t; }
                    else { z -= pio2_2; y0 = z - pio2_2t; y1 = (z - y0) - pio2_2t; }
                    return 1;
                }
                z = x + pio2_1;
                if (ix != 0x3FF921FB) { y0 = z + pio2_1t; y1 = (z - y0) + pio2_1t; }
                else { z += pio2_2; y0 = z + pio2_2t; y1 = (z - y0) + pio2_2t; }
                return -1;
            }
            if (ix <= 0x413921FB)
            {
                t = Math.Abs(x); n = (int)(t * invpio2 + half); fn = n;
                r = t - fn * pio2_1; w = fn * pio2_1t;
                if (n < 32 && ix != npio2_hw[n - 1]) y0 = r - w;
                else
                {
                    j = ix >> 20; y0 = r - w; i = j - ((HI(y0) >> 20) & 0x7FF);
                    if (i > 16)
                    {
                        t = r; w = fn * pio2_2; r = t - w; w = fn * pio2_2t - ((t - r) - w); y0 = r - w; i = j - ((HI(y0) >> 20) & 0x7FF);
                        if (i > 49) { t = r; w = fn * pio2_3; r = t - w; w = fn * pio2_3t - ((t - r) - w); y0 = r - w; }
                    }
                }
                y1 = (r - y0) - w;
                if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
                return n;
            }
            if (ix >= 0x7FF00000) { y0 = y1 = x - x; return 0; }
            var S = remScratch; if (S == null) remScratch = S = new RemScratch();
            double[] tx = S.tx;
            z = SETLO(z, LO(x)); e0 = (ix >> 20) - 1046; z = SETHI(z, ix - (int)((uint)e0 << 20));
            for (i = 0; i < 2; i++) { tx[i] = (int)z; z = (z - tx[i]) * two24; }
            tx[2] = z; nx = 3;
            while (tx[nx - 1] == 0) nx--;
            n = kernelRemPio2(S, tx, out y0, out y1, e0, nx);
            if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
            return n;
        }

        // __kernel_rem_pio2 with prec = 2 (the only precision __ieee754_rem_pio2 uses)
        static int kernelRemPio2(RemScratch S, double[] x, out double y0, out double y1, int e0, int nx)
        {
            const double two24 = 1.67772160000000000000e+07, twon24 = 5.96046447753906250000e-08;
            int[] ipio2 = two_over_pi, iq = S.iq; double[] f = S.f, fq = S.fq, q = S.q;
            int jz, jx, jv, jp, jk, carry, n, i, j, k, m, q0, ih; double z, fw;
            jk = 4; jp = jk;
            jx = nx - 1; jv = (e0 - 3) / 24; if (jv < 0) jv = 0; q0 = e0 - 24 * (jv + 1);
            j = jv - jx; m = jx + jk;
            for (i = 0; i <= m; i++, j++) f[i] = j < 0 ? 0.0 : (double)ipio2[j];
            for (i = 0; i <= jk; i++) { for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j]; q[i] = fw; }
            jz = jk;
        recompute:
            for (i = 0, j = jz, z = q[jz]; j > 0; i++, j--) { fw = (int)(twon24 * z); iq[i] = (int)(z - two24 * fw); z = q[j - 1] + fw; }
            z = scalbn(z, q0); z -= 8.0 * Math.Floor(z * 0.125); n = (int)z; z -= n; ih = 0;
            if (q0 > 0) { i = iq[jz - 1] >> (24 - q0); n += i; iq[jz - 1] -= i << (24 - q0); ih = iq[jz - 1] >> (23 - q0); }
            else if (q0 == 0) ih = iq[jz - 1] >> 23;
            else if (z >= 0.5) ih = 2;
            if (ih > 0)
            {
                n += 1; carry = 0;
                for (i = 0; i < jz; i++) { j = iq[i]; if (carry == 0) { if (j != 0) { carry = 1; iq[i] = 0x1000000 - j; } } else iq[i] = 0xFFFFFF - j; }
                if (q0 > 0) { if (q0 == 1) iq[jz - 1] &= 0x7FFFFF; else if (q0 == 2) iq[jz - 1] &= 0x3FFFFF; }
                if (ih == 2) { z = 1.0 - z; if (carry != 0) z -= scalbn(1.0, q0); }
            }
            if (z == 0)
            {
                j = 0; for (i = jz - 1; i >= jk; i--) j |= iq[i];
                if (j == 0)
                {
                    for (k = 1; jk >= k && iq[jk - k] == 0; k++) { }
                    for (i = jz + 1; i <= jz + k; i++) { f[jx + i] = ipio2[jv + i]; for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j]; q[i] = fw; }
                    jz += k;
                    goto recompute;
                }
            }
            if (z == 0.0) { jz -= 1; q0 -= 24; while (iq[jz] == 0) { jz--; q0 -= 24; } }
            else
            {
                z = scalbn(z, -q0);
                if (z >= two24) { fw = (int)(twon24 * z); iq[jz] = (int)(z - two24 * fw); jz += 1; q0 += 24; iq[jz] = (int)fw; }
                else iq[jz] = (int)z;
            }
            fw = scalbn(1.0, q0);
            for (i = jz; i >= 0; i--) { q[i] = fw * iq[i]; fw *= twon24; }
            for (i = jz; i >= 0; i--) { for (fw = 0.0, k = 0; k <= jp && k <= jz - i; k++) fw += PIo2[k] * q[i + k]; fq[jz - i] = fw; }
            fw = 0.0; for (i = jz; i >= 0; i--) fw += fq[i];
            y0 = ih == 0 ? fw : -fw;
            fw = fq[0] - fw; for (i = 1; i <= jz; i++) fw += fq[i];
            y1 = ih == 0 ? fw : -fw;
            return n & 7;
        }

        static double kernelCos(double x, double y)
        {
            const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
                C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
            int ix = HI(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000) { if ((int)x == 0) return 1.0; }
            double z = x * x, r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6))))), qx;
            if (ix < 0x3FD33333) return 1.0 - (0.5 * z - (z * r - x * y));
            if (ix > 0x3FE90000) qx = 0.28125; else qx = MK(ix - 0x00200000, 0);
            double iz = 0.5 * z - qx, a = 1.0 - qx;
            return a - (iz - (z * r - x * y));
        }
        static double kernelSin(double x, double y, int iy)
        {
            const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
                S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
            int ix = HI(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000) { if ((int)x == 0) return x; }
            double z = x * x, v = z * x, r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            if (iy == 0) return x + v * (S1 + z * r);
            return x - ((z * (0.5 * y - v * r) - y) - v * S1);
        }
        static double kernelTan(double x, double y, int iy)
        {
            const double T0 = 3.33333333333334091986e-01, T1 = 1.33333333333201242699e-01, T2 = 5.39682539762260521377e-02,
                T3 = 2.18694882948595424599e-02, T4 = 8.86323982359930005737e-03, T5 = 3.59207910759131235356e-03, T6 = 1.45620945432529025516e-03,
                T7 = 5.88041240820264096874e-04, T8 = 2.46463134818469906812e-04, T9 = 7.81794442939557092300e-05, T10 = 7.14072491382608190305e-05,
                T11 = -1.85586374855275456654e-05, T12 = 2.59073051863633712884e-05, pio4 = 7.85398163397448278999e-01, pio4lo = 3.06161699786838301793e-17;
            double z, r, v, w, s, a, t; int hx = HI(x), ix = hx & 0x7FFFFFFF;
            if (ix < 0x3E300000)
            {
                if ((int)x == 0)
                {
                    if (((uint)ix | LO(x) | (uint)(iy + 1)) == 0) return 1.0 / Math.Abs(x);
                    if (iy == 1) return x;
                    z = w = x + y; z = CHOPLO(z); v = y - (z - x); t = a = -1.0 / w; t = CHOPLO(t); s = 1.0 + t * z;
                    return t + a * (s + t * v);
                }
            }
            if (ix >= 0x3FE59428) { if (hx < 0) { x = -x; y = -y; } z = pio4 - x; w = pio4lo - y; x = z + w; y = 0.0; }
            z = x * x; w = z * z;
            r = T1 + w * (T3 + w * (T5 + w * (T7 + w * (T9 + w * T11))));
            v = z * (T2 + w * (T4 + w * (T6 + w * (T8 + w * (T10 + w * T12)))));
            s = z * x; r = y + z * (s * (r + v) + y); r += T0 * s; w = x + r;
            if (ix >= 0x3FE59428) { v = iy; return (1 - ((hx >> 30) & 2)) * (v - 2.0 * (x - (w * w / (w + v) - r))); }
            if (iy == 1) return w;
            z = CHOPLO(w); v = r - (z - x); t = a = -1.0 / w; t = CHOPLO(t); s = 1.0 + t * z;
            return t + a * (s + t * v);
        }

        /// <summary>JS Math.sin (V8 fdlibm_sin).</summary>
        public static double sin(double x)
        {
            int ix = HI(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return kernelSin(x, 0.0, 0);
            if (ix >= 0x7FF00000) return x - x;
            double y0, y1; int n = remPio2(x, out y0, out y1);
            switch (n & 3) { case 0: return kernelSin(y0, y1, 1); case 1: return kernelCos(y0, y1); case 2: return -kernelSin(y0, y1, 1); default: return -kernelCos(y0, y1); }
        }
        /// <summary>JS Math.cos (V8 fdlibm_cos).</summary>
        public static double cos(double x)
        {
            int ix = HI(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return kernelCos(x, 0.0);
            if (ix >= 0x7FF00000) return x - x;
            double y0, y1; int n = remPio2(x, out y0, out y1);
            switch (n & 3) { case 0: return kernelCos(y0, y1); case 1: return -kernelSin(y0, y1, 1); case 2: return -kernelCos(y0, y1); default: return kernelSin(y0, y1, 1); }
        }
        /// <summary>JS Math.tan (V8 fdlibm tan).</summary>
        public static double tan(double x)
        {
            int ix = HI(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return kernelTan(x, 0.0, 1);
            if (ix >= 0x7FF00000) return x - x;
            double y0, y1; int n = remPio2(x, out y0, out y1);
            return kernelTan(y0, y1, 1 - ((n & 1) << 1));
        }

        /// <summary>JS Math.log (V8 fdlibm e_log.c).</summary>
        public static double log(double x)
        {
            const double ln2_hi = 6.93147180369123816490e-01, ln2_lo = 1.90821492927058770002e-10, two54 = 1.80143985094819840000e+16,
                Lg1 = 6.666666666666735130e-01, Lg2 = 3.999999999940941908e-01, Lg3 = 2.857142874366239149e-01, Lg4 = 2.222219843214978396e-01,
                Lg5 = 1.818357216161805012e-01, Lg6 = 1.531383769920937332e-01, Lg7 = 1.479819860511658591e-01;
            double hfsq, f, s, z, R, w, t1, t2, dk; int k, hx, i, j; uint lx;
            hx = HI(x); lx = LO(x); k = 0;
            if (hx < 0x00100000)
            {
                if ((((uint)hx & 0x7FFFFFFF) | lx) == 0) return double.NegativeInfinity;
                if (hx < 0) return double.NaN;
                k -= 54; x *= two54; hx = HI(x);
            }
            if (hx >= 0x7FF00000) return x + x;
            k += (hx >> 20) - 1023; hx &= 0x000FFFFF; i = (hx + 0x95F64) & 0x100000;
            x = SETHI(x, hx | (i ^ 0x3FF00000)); k += i >> 20; f = x - 1.0;
            if ((0x000FFFFF & (2 + hx)) < 3)
            {
                if (f == 0) { if (k == 0) return 0; dk = k; return dk * ln2_hi + dk * ln2_lo; }
                R = f * f * (0.5 - 0.33333333333333333 * f);
                if (k == 0) return f - R;
                dk = k; return dk * ln2_hi - ((R - dk * ln2_lo) - f);
            }
            s = f / (2.0 + f); dk = k; z = s * s; i = hx - 0x6147A; w = z * z; j = 0x6B851 - hx;
            t1 = w * (Lg2 + w * (Lg4 + w * Lg6)); t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7))); i |= j; R = t2 + t1;
            if (i > 0)
            {
                hfsq = 0.5 * f * f;
                if (k == 0) return f - (hfsq - s * (hfsq + R));
                return dk * ln2_hi - ((hfsq - (s * (hfsq + R) + dk * ln2_lo)) - f);
            }
            if (k == 0) return f - s * (f - R);
            return dk * ln2_hi - ((s * (f - R) - dk * ln2_lo) - f);
        }
        /// <summary>JS Math.log10 (V8 fdlibm e_log10.c on top of log).</summary>
        public static double log10(double x)
        {
            const double two54 = 1.80143985094819840000e+16, ivln10 = 4.34294481903251816668e-01,
                log10_2hi = 3.01029995663611771306e-01, log10_2lo = 3.69423907715893078616e-13;
            int i, k, hx = HI(x); uint lx = LO(x); k = 0;
            if (hx < 0x00100000)
            {
                if ((((uint)hx & 0x7FFFFFFF) | lx) == 0) return double.NegativeInfinity;
                if (hx < 0) return double.NaN;
                k -= 54; x *= two54; hx = HI(x); lx = LO(x);
            }
            if (hx >= 0x7FF00000) return x + x;
            if (hx == 0x3FF00000 && lx == 0) return 0.0;
            k += (hx >> 20) - 1023;
            i = (int)(((uint)k & 0x80000000) >> 31);
            hx = (hx & 0x000FFFFF) | ((0x3FF - i) << 20);
            double y = k + i;
            x = MK(hx, lx);
            double z = y * log10_2lo + ivln10 * log(x);
            return z + y * log10_2hi;
        }

        /// <summary>JS Math.pow / ** (V8 ieee754::pow = fdlibm e_pow.c with JS special cases).</summary>
        public static double pow(double x, double y)
        {
            const double dp_h1 = 5.84962487220764160156e-01, dp_l1 = 1.35003920212974897128e-08, two53 = 9007199254740992.0,
                huge = 1.0e300, tiny = 1.0e-300,
                L1 = 5.99999999999994648725e-01, L2 = 4.28571428578550184252e-01, L3 = 3.33333329818377432918e-01,
                L4 = 2.72728123808534006489e-01, L5 = 2.30660745775561754067e-01, L6 = 2.06975017800338417784e-01,
                P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03, P3 = 6.61375632143793436117e-05,
                P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08,
                lg2 = 6.93147180559945286227e-01, lg2_h = 6.93147182464599609375e-01, lg2_l = -1.90465429995776804525e-09,
                ovt = 8.0085662595372944372e-0017, cp = 9.61796693925975554329e-01, cp_h = 9.61796700954437255859e-01,
                cp_l = -7.02846165095275826516e-09, ivln2 = 1.44269504088896338700e+00, ivln2_h = 1.44269502162933349609e+00,
                ivln2_l = 1.92596299112661746887e-08;
            double z, ax, z_h, z_l, p_h, p_l, y1, t1, t2, r, s, t, u, v, w;
            int i, j, k, yisint, n, hx, hy, ix, iy; uint lx, ly;
            long bx = BitConverter.DoubleToInt64Bits(x), by = BitConverter.DoubleToInt64Bits(y);
            hx = (int)(bx >> 32); lx = (uint)bx; hy = (int)(by >> 32); ly = (uint)by;
            ix = hx & 0x7fffffff; iy = hy & 0x7fffffff;
            if (((uint)iy | ly) == 0) return 1.0;
            if (ix > 0x7ff00000 || (ix == 0x7ff00000 && lx != 0) || iy > 0x7ff00000 || (iy == 0x7ff00000 && ly != 0)) return x + y;
            yisint = 0;
            if (hx < 0)
            {
                if (iy >= 0x43400000) yisint = 2;
                else if (iy >= 0x3ff00000)
                {
                    k = (iy >> 20) - 0x3ff;
                    if (k > 20) { j = (int)(ly >> (52 - k)); if ((j << (52 - k)) == (int)ly) yisint = 2 - (j & 1); }
                    else if (ly == 0) { j = iy >> (20 - k); if ((j << (20 - k)) == iy) yisint = 2 - (j & 1); }
                }
            }
            if (ly == 0)
            {
                if (iy == 0x7ff00000)
                {
                    if (((ix - 0x3ff00000) | (int)lx) == 0) return y - y;
                    if (ix >= 0x3ff00000) return hy >= 0 ? y : 0.0;
                    return hy < 0 ? -y : 0.0;
                }
                if (iy == 0x3ff00000) return hy < 0 ? 1.0 / x : x;
                if (hy == 0x40000000) return x * x;
                if (hy == 0x3fe00000 && hx >= 0) return Math.Sqrt(x);
            }
            ax = Math.Abs(x);
            if (lx == 0)
            {
                if (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000)
                {
                    z = ax;
                    if (hy < 0) z = 1.0 / z;
                    if (hx < 0) { if (((ix - 0x3ff00000) | yisint) == 0) z = double.NaN; else if (yisint == 1) z = -z; }
                    return z;
                }
            }
            n = (hx >> 31) + 1;
            if ((n | yisint) == 0) return double.NaN;
            s = 1.0;
            if ((n | (yisint - 1)) == 0) s = -1.0;
            if (iy > 0x41e00000)
            {
                if (iy > 0x43f00000)
                {
                    if (ix <= 0x3fefffff) return hy < 0 ? huge * huge : tiny * tiny;
                    if (ix >= 0x3ff00000) return hy > 0 ? huge * huge : tiny * tiny;
                }
                if (ix < 0x3fefffff) return hy < 0 ? s * huge * huge : s * tiny * tiny;
                if (ix > 0x3ff00000) return hy > 0 ? s * huge * huge : s * tiny * tiny;
                t = ax - 1.0;
                w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
                u = ivln2_h * t; v = t * ivln2_l - w * ivln2;
                t1 = CHOPLO(u + v); t2 = v - (t1 - u);
            }
            else
            {
                double ss, s2, s_h, s_l, t_h, t_l, bpk, dphk, dplk;
                n = 0;
                if (ix < 0x00100000) { ax *= two53; n -= 53; ix = HI(ax); }
                n += (ix >> 20) - 0x3ff; j = ix & 0x000fffff; ix = j | 0x3ff00000;
                if (j <= 0x3988E) k = 0; else if (j < 0xBB67A) k = 1; else { k = 0; n += 1; ix -= 0x00100000; }
                ax = SETHI(ax, ix);
                if (k == 0) { bpk = 1.0; dphk = 0.0; dplk = 0.0; } else { bpk = 1.5; dphk = dp_h1; dplk = dp_l1; }
                u = ax - bpk; v = 1.0 / (ax + bpk); ss = u * v; s_h = CHOPLO(ss);
                t_h = MK(((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18), 0);
                t_l = ax - (t_h - bpk);
                s_l = v * ((u - s_h * t_h) - s_h * t_l);
                s2 = ss * ss;
                r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
                r += s_l * (s_h + ss);
                s2 = s_h * s_h;
                t_h = CHOPLO(3.0 + s2 + r);
                t_l = r - ((t_h - 3.0) - s2);
                u = s_h * t_h; v = s_l * t_h + t_l * ss;
                p_h = CHOPLO(u + v); p_l = v - (p_h - u);
                z_h = cp_h * p_h; z_l = cp_l * p_h + p_l * cp + dplk;
                t = n;
                t1 = CHOPLO(((z_h + z_l) + dphk) + t);
                t2 = z_l - (((t1 - t) - dphk) - z_h);
            }
            y1 = CHOPLO(y);
            p_l = (y - y1) * t1 + y * t2; p_h = y1 * t1; z = p_l + p_h;
            long bz = BitConverter.DoubleToInt64Bits(z); j = (int)(bz >> 32); i = (int)(uint)bz;
            if (j >= 0x40900000)
            {
                if (((j - 0x40900000) | i) != 0) return s * huge * huge;
                if (p_l + ovt > z - p_h) return s * huge * huge;
            }
            else if ((j & 0x7fffffff) >= 0x4090cc00)
            {
                if (((j - unchecked((int)0xc090cc00)) | i) != 0) return s * tiny * tiny;
                if (p_l <= z - p_h) return s * tiny * tiny;
            }
            i = j & 0x7fffffff; k = (i >> 20) - 0x3ff; n = 0;
            if (i > 0x3fe00000)
            {
                n = j + (0x00100000 >> (k + 1));
                k = ((n & 0x7fffffff) >> 20) - 0x3ff;
                t = MK(n & ~(0x000fffff >> k), 0);
                n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
                if (j < 0) n = -n;
                p_h -= t;
            }
            t = CHOPLO(p_l + p_h);
            u = t * lg2_h; v = (p_l - (t - p_h)) * lg2 + t * lg2_l;
            z = u + v; w = v - (z - u);
            t = z * z;
            t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            r = (z * t1) / ((t1 - 2.0) - (w + z * w));
            z = 1.0 - (r - z);
            j = HI(z); j += (int)((uint)n << 20);
            if ((j >> 20) <= 0) z = scalbn(z, n);
            else z = SETHI(z, HI(z) + (int)((uint)n << 20));
            return s * z;
        }

        /// <summary>JS Math.atan (V8 fdlibm s_atan.c).</summary>
        public static double atan(double x)
        {
            const double aT0 = 3.33333333333329318027e-01, aT1 = -1.99999999998764832476e-01, aT2 = 1.42857142725034663711e-01,
                aT3 = -1.11111104054623557880e-01, aT4 = 9.09088713343650656196e-02, aT5 = -7.69187620504482999495e-02,
                aT6 = 6.66107313738753120669e-02, aT7 = -5.83357013379057348645e-02, aT8 = 4.97687799461593236017e-02,
                aT9 = -3.65315727442169155270e-02, aT10 = 1.62858201153657823623e-02, huge = 1.0e300;
            double w, s1, s2, z, hi, lo; int ix, hx = HI(x), id; ix = hx & 0x7FFFFFFF;
            if (ix >= 0x44100000)
            {
                if (ix > 0x7FF00000 || (ix == 0x7FF00000 && LO(x) != 0)) return x + x;
                return hx > 0 ? 1.57079632679489655800e+00 + 6.12323399573676603587e-17 : -1.57079632679489655800e+00 - 6.12323399573676603587e-17;
            }
            if (ix < 0x3FDC0000) { if (ix < 0x3E400000) { if (huge + x > 1.0) return x; } id = -1; }
            else
            {
                x = Math.Abs(x);
                if (ix < 0x3FF30000)
                {
                    if (ix < 0x3FE60000) { id = 0; x = (2.0 * x - 1.0) / (2.0 + x); }
                    else { id = 1; x = (x - 1.0) / (x + 1.0); }
                }
                else if (ix < 0x40038000) { id = 2; x = (x - 1.5) / (1.0 + 1.5 * x); }
                else { id = 3; x = -1.0 / x; }
            }
            z = x * x; w = z * z;
            s1 = z * (aT0 + w * (aT2 + w * (aT4 + w * (aT6 + w * (aT8 + w * aT10)))));
            s2 = w * (aT1 + w * (aT3 + w * (aT5 + w * (aT7 + w * aT9))));
            if (id < 0) return x - x * (s1 + s2);
            switch (id)
            {
                case 0: hi = 4.63647609000806093515e-01; lo = 2.26987774529616870924e-17; break;
                case 1: hi = 7.85398163397448278999e-01; lo = 3.06161699786838301793e-17; break;
                case 2: hi = 9.82793723247329054082e-01; lo = 1.39033110312309984516e-17; break;
                default: hi = 1.57079632679489655800e+00; lo = 6.12323399573676603587e-17; break;
            }
            z = hi - ((x * (s1 + s2) - lo) - x);
            return hx < 0 ? -z : z;
        }
        /// <summary>JS Math.atan2 (V8 fdlibm e_atan2.c).</summary>
        public static double atan2(double y, double x)
        {
            const double tiny = 1.0e-300, pi_o_4 = 7.8539816339744827900E-01, pi_o_2 = 1.5707963267948965580E+00,
                pi = 3.1415926535897931160E+00, pi_lo = 1.2246467991473531772E-16;
            double z; int k, m, hx = HI(x), hy = HI(y), ix, iy; uint lx = LO(x), ly = LO(y);
            ix = hx & 0x7FFFFFFF; iy = hy & 0x7FFFFFFF;
            if (((uint)ix | ((lx | (uint)(-(int)lx)) >> 31)) > 0x7FF00000 || ((uint)iy | ((ly | (uint)(-(int)ly)) >> 31)) > 0x7FF00000) return x + y;
            if (((hx - 0x3FF00000) | (int)lx) == 0) return atan(y);
            m = ((hy >> 31) & 1) | ((hx >> 30) & 2);
            if (((uint)iy | ly) == 0)
            {
                switch (m) { case 0: case 1: return y; case 2: return pi + tiny; default: return -pi - tiny; }
            }
            if (((uint)ix | lx) == 0) return hy < 0 ? -pi_o_2 - tiny : pi_o_2 + tiny;
            if (ix == 0x7FF00000)
            {
                if (iy == 0x7FF00000)
                {
                    switch (m) { case 0: return pi_o_4 + tiny; case 1: return -pi_o_4 - tiny; case 2: return 3.0 * pi_o_4 + tiny; default: return -3.0 * pi_o_4 - tiny; }
                }
                switch (m) { case 0: return 0.0; case 1: return -0.0; case 2: return pi + tiny; default: return -pi - tiny; }
            }
            if (iy == 0x7FF00000) return hy < 0 ? -pi_o_2 - tiny : pi_o_2 + tiny;
            k = (iy - ix) >> 20;
            if (k > 60) { z = pi_o_2 + 0.5 * pi_lo; m &= 1; }
            else if (hx < 0 && k < -60) z = 0.0;
            else z = atan(Math.Abs(y / x));
            switch (m) { case 0: return z; case 1: return -z; case 2: return pi - (z - pi_lo); default: return (z - pi_lo) - pi; }
        }

        /// <summary>JS Math.round (V8 Float64Round: ceil, step back if ceil - 0.5 > x). Unlike floor(x + 0.5) it is exact for
        /// 0.49999999999999994 and |x| >= 2^52, and keeps -0 for x in [-0.5, -0].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double round(double x) { double r = Math.Ceiling(x); return r - 0.5 > x ? r - 1.0 : r; }

        /// <summary>JS Math.hypot(a, b) (V8 Torque MathHypot: max-normalised Kahan sum).</summary>
        public static double hypot(double a, double b)
        {
            bool nan = false; double max = 0, aa = 0, ab = 0;
            if (double.IsNaN(a)) nan = true; else { aa = Math.Abs(a); if (aa > max) max = aa; }
            if (double.IsNaN(b)) nan = true; else { ab = Math.Abs(b); if (ab > max) max = ab; }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0, comp = 0, n, summand, pre;
            n = aa / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
            n = ab / max; summand = n * n - comp; pre = sum + summand; sum = pre;
            return Math.Sqrt(sum) * max;
        }
        /// <summary>JS Math.hypot(a, b, c).</summary>
        public static double hypot(double a, double b, double c)
        {
            bool nan = false; double max = 0, aa = 0, ab = 0, ac = 0;
            if (double.IsNaN(a)) nan = true; else { aa = Math.Abs(a); if (aa > max) max = aa; }
            if (double.IsNaN(b)) nan = true; else { ab = Math.Abs(b); if (ab > max) max = ab; }
            if (double.IsNaN(c)) nan = true; else { ac = Math.Abs(c); if (ac > max) max = ac; }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0, comp = 0, n, summand, pre;
            n = aa / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
            n = ab / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
            n = ac / max; summand = n * n - comp; pre = sum + summand; sum = pre;
            return Math.Sqrt(sum) * max;
        }
    }
}
