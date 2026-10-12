using System;

namespace Maincraft
{
    /// <summary>
    /// The few places where JavaScript number semantics differ from C# and the original relies on them:
    /// Math.round rounds halves up (C# Math.Round rounds to even), `x | 0` / `x >>> 0` wrap doubles to 32 bits.
    /// Everything else uses System.Math directly.
    /// </summary>
    public static class JsMath
    {
        /// <summary>JS Math.round: halves round toward +infinity.</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);

        /// <summary>JS ToInt32 (the `x | 0` of a double).</summary>
        public static int ToInt32(double x)
        {
            if (x >= -2147483648.0 && x < 2147483648.0) return (int)x;
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x) % 4294967296.0;
            if (t < 0) t += 4294967296.0;
            return unchecked((int)(uint)t);
        }

        /// <summary>JS ToUint32 (the `x >>> 0` of a double).</summary>
        public static uint ToUint32(double x) => unchecked((uint)ToInt32(x));

        /// <summary>JS Math.sign.</summary>
        public static double Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : x;

        /// <summary>JS `a % b` for doubles (sign of the dividend) is C#'s % as well; kept for readability.</summary>
        public static double Mod(double a, double b) => a % b;

        /// <summary>Clamp helpers used all over the original.</summary>
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
