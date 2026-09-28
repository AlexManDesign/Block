using System;

namespace BlockcraftPort
{
    /// <summary>Small helpers matching JavaScript number semantics used by main/genWorker.</summary>
    public static class SourceMath
    {
        // JavaScript Math.round(x): ties go toward +infinity, unlike C# banker's rounding.
        public static int JsRound(double x) => (int)Math.Floor(x + .5d);
        public static int JsRound(float x) => (int)Math.Floor(x + .5f);
        // JavaScript bitwise operators first apply ToInt32 to the IEEE-754 Number. This matters when
        // a preceding Number multiplication is larger than 2^53 (structure salts deliberately do this).
        public static int JsToInt32(double x)
        {
            if(double.IsNaN(x)||double.IsInfinity(x)||x==0d)return 0;
            double t=Math.Truncate(x)%4294967296d;if(t<0d)t+=4294967296d;
            return t>=2147483648d?(int)(t-4294967296d):(int)t;
        }
        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float ClampSigned(float x) => x < -1f ? -1f : x > 1f ? 1f : x;
        public static float Smooth01(float x) => x * x * (3f - 2f * x);
        public static float SignedValue(float x) => (x * 2f - 1f) * 2.2f;
    }
}
