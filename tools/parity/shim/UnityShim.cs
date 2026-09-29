// Minimal managed re-implementation of the UnityEngine math types used by the port's pure-logic
// code (generation, lighting, meshing). Semantics follow UnityCsReference so results match Unity.
// Used ONLY by the offline parity/perf harness; never shipped in the Unity project.
using System;
using System.Diagnostics;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Infinity = float.PositiveInfinity;
        public const float Deg2Rad = PI * 2f / 360f;
        public const float Rad2Deg = 1f / Deg2Rad;
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int v) => Math.Abs(v);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float min, float max) { if (v < min) v = min; else if (v > max) v = max; return v; }
        public static int Clamp(int v, int min, int max) { if (v < min) v = min; else if (v > max) v = max; return v; }
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public override string ToString() => $"({x:F2}, {y:F2})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) { float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z; return dx * dx + dy * dy + dz * dz < 9.99999944E-11f; }
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && x == v.x && y == v.y && z == v.z;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Distance(Vector3 a, Vector3 b) { float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1E-05f ? this / m : zero; } }
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Vector2Int v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
    }

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int x, y, z;
        public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3Int zero => new Vector3Int(0, 0, 0);
        public static Vector3Int one => new Vector3Int(1, 1, 1);
        public static Vector3Int up => new Vector3Int(0, 1, 0);
        public static Vector3Int down => new Vector3Int(0, -1, 0);
        public static Vector3Int left => new Vector3Int(-1, 0, 0);
        public static Vector3Int right => new Vector3Int(1, 0, 0);
        public static Vector3Int forward => new Vector3Int(0, 0, 1);
        public static Vector3Int back => new Vector3Int(0, 0, -1);
        public static Vector3Int operator +(Vector3Int a, Vector3Int b) => new Vector3Int(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3Int operator -(Vector3Int a, Vector3Int b) => new Vector3Int(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3Int operator *(Vector3Int a, int d) => new Vector3Int(a.x * d, a.y * d, a.z * d);
        public static implicit operator Vector3(Vector3Int v) => new Vector3(v.x, v.y, v.z);
        public bool Equals(Vector3Int o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object o) => o is Vector3Int v && Equals(v);
        public override int GetHashCode() { int yh = y.GetHashCode(), zh = z.GetHashCode(); return x.GetHashCode() ^ (yh << 4) ^ (yh >> 28) ^ (zh >> 4) ^ (zh << 28); }
        public static bool operator ==(Vector3Int a, Vector3Int b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3Int a, Vector3Int b) => !(a == b);
    }

    public static class Time
    {
        static readonly Stopwatch sw = Stopwatch.StartNew();
        public static double realtimeSinceStartupAsDouble => sw.Elapsed.TotalSeconds;
        public static float realtimeSinceStartup => (float)sw.Elapsed.TotalSeconds;
    }

    public static class Debug
    {
        public static void Log(object o) => Console.Error.WriteLine(o);
        public static void LogWarning(object o) => Console.Error.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.Error.WriteLine("ERROR " + o);
    }

    public class Transform { }

    public sealed class ThreadStaticAttributeShim { }
}
