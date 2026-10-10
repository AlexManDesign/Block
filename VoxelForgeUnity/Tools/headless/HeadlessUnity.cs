// Behaving UnityEngine API surface for the headless runtime smoke test (see README.md).
// Signatures mirror Tools/compile-check/UnityStubs.cs (= real Unity 2022.3); the implementations actually work:
// resources are read from Assets/VoxelForge/Resources, textures keep pixels, PNGs are decoded, meshes keep and
// validate their buffers like the native side does, IMGUI controls react to scripted mouse events and Input
// returns values scripted by the harness. Main-thread-only Unity APIs throw UnityException off the main thread.
// UnityEngine.HeadlessHost is the harness-side control surface (not a Unity API; never used by game code).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
#pragma warning disable 0067, 0649, 0169, 0414, 0660, 0661
namespace UnityEngine
{
    public class UnityException : Exception { public UnityException(string m) : base(m) { } }
    public class MissingReferenceException : Exception { public MissingReferenceException(string m) : base(m) { } }
    public sealed class ExitGUIException : Exception { }

    /// <summary>Harness control surface: player loop, scripted input, problem log.</summary>
    public static class HeadlessHost
    {
        public static Thread mainThread = Thread.CurrentThread;
        public static string resourcesRoot = "", persistentDataPath = Path.GetTempPath();
        public static int screenW = 1280, screenH = 720;
        public static readonly object logLock = new object();
        public static readonly List<string> exceptions = new List<string>(), errors = new List<string>(), problems = new List<string>(), warnings = new List<string>();
        public static bool verboseLog = false;
        public static TextWriter log = Console.Out;
        public static void Problem(string msg)
        {
            lock (logLock)
            {
                if (problems.Count < 5000) problems.Add("[frame " + Time.frameCount + "] " + msg);
                if (problems.Count <= 60) log.WriteLine("PROBLEM: " + msg);
            }
        }
        public static void MainOnly(string api)
        {
            if (Thread.CurrentThread != mainThread)
            {
                Problem(api + " called from a background thread (" + Thread.CurrentThread.Name + ")\n" + Environment.StackTrace);
                throw new UnityException(api + " can only be called from the main thread.");
            }
        }
        // ---- scene ----
        internal static readonly List<Component> components = new List<Component>();
        internal static readonly List<Object> pendingDestroy = new List<Object>();
        internal static readonly HashSet<MonoBehaviour> started = new HashSet<MonoBehaviour>();
        internal static int nextInstanceId = 1;
        // ---- input ----
        internal static readonly HashSet<KeyCode> held = new HashSet<KeyCode>(), down = new HashSet<KeyCode>(), up = new HashSet<KeyCode>();
        static readonly HashSet<KeyCode> qDown = new HashSet<KeyCode>(), qUp = new HashSet<KeyCode>();
        internal static readonly bool[] mHeld = new bool[3], mDown = new bool[3], mUp = new bool[3];
        static readonly bool[] qmDown = new bool[3], qmUp = new bool[3];
        internal static float axisMouseX, axisMouseY, qAxisX, qAxisY; internal static Vector2 scroll, qScroll;
        public static Vector2 mouseGUI = new Vector2(640, 360); // GUI space (y down)
        public static readonly List<Event> guiEvents = new List<Event>();
        public static string typedText = null;
        public static void PressKey(KeyCode k) { qDown.Add(k); }
        public static void ReleaseKey(KeyCode k) { qUp.Add(k); }
        public static void PressMouse(int b) { qmDown[b] = true; }
        public static void ReleaseMouse(int b) { qmUp[b] = true; }
        public static void MouseMove(float dx, float dy) { qAxisX += dx; qAxisY += dy; }
        public static void Scroll(float dy) { qScroll = new Vector2(0, dy); }
        /// <summary>Queues an IMGUI click (MouseDown + MouseUp) at a GUI-space point.</summary>
        public static void Click(Vector2 p, int button = 0)
        {
            guiEvents.Add(new Event { type = EventType.MouseDown, rawPos = p, button = button, clickCount = 1 });
            guiEvents.Add(new Event { type = EventType.MouseUp, rawPos = p, button = button, clickCount = 1 });
        }
        public struct ButtonInfo { public string text; public Rect screen; public bool enabled; }
        public static readonly List<ButtonInfo> buttons = new List<ButtonInfo>(), buttonsPrev = new List<ButtonInfo>();
        internal static readonly Dictionary<string, int> namedControls = new Dictionary<string, int>();
        internal static string nextControlName = null; internal static int controlCounter = 0;
        // ---- frame stats ----
        public static int frameDraws, frameTris, lastDraws, lastTris;
        public static double frameStartTime;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        internal static double realtime { get { return clock.Elapsed.TotalSeconds; } }
        internal static int frameCount = 0; internal static float deltaTime = 0.016f; static double lastFrameT = 0;

        static void Invoke(Component c, string m)
        {
            var mi = c.GetType().GetMethod(m, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (mi == null) return;
            try { mi.Invoke(c, null); }
            catch (System.Reflection.TargetInvocationException e)
            {
                if (e.InnerException is ExitGUIException) return;
                Debug.LogException(e.InnerException ?? e);
            }
        }
        internal static void AwakeComponent(Component c) { if (c is MonoBehaviour) { Invoke(c, "Awake"); Invoke(c, "OnEnable"); } }
        /// <summary>One player-loop iteration: input -> Start/Update -> render (validation) -> OnGUI passes -> end-of-frame destroy.</summary>
        public static void Frame()
        {
            frameCount++;
            double t = realtime; deltaTime = (float)Math.Max(1e-4, t - lastFrameT); lastFrameT = t;
            down.Clear(); up.Clear();
            foreach (var k in qDown) { if (!held.Contains(k)) down.Add(k); held.Add(k); }
            foreach (var k in qUp) { if (held.Contains(k)) up.Add(k); held.Remove(k); }
            qDown.Clear(); qUp.Clear();
            for (int b = 0; b < 3; b++)
            {
                mDown[b] = qmDown[b] && !mHeld[b]; if (qmDown[b]) mHeld[b] = true;
                mUp[b] = qmUp[b] && mHeld[b]; if (qmUp[b]) mHeld[b] = false;
                qmDown[b] = qmUp[b] = false;
            }
            axisMouseX = qAxisX; axisMouseY = qAxisY; qAxisX = qAxisY = 0; scroll = qScroll; qScroll = Vector2.zero;
            var beh = new List<MonoBehaviour>();
            foreach (var c in components.ToArray()) { var mb = c as MonoBehaviour; if (mb != null && !mb.__destroyed && mb.enabled) beh.Add(mb); }
            foreach (var mb in beh) if (started.Add(mb)) Invoke(mb, "Start");
            foreach (var mb in beh) Invoke(mb, "Update");
            foreach (var mb in beh) Invoke(mb, "LateUpdate");
            // "render": validate every command buffer attached to an enabled camera
            frameDraws = frameTris = 0;
            foreach (var c in components.ToArray())
            {
                var cam = c as Camera; if (cam == null || cam.__destroyed || !cam.enabled) continue;
                foreach (var cb in cam.buffers) cb.Execute();
            }
            lastDraws = frameDraws; lastTris = frameTris;
            // OnGUI: Layout + event for every queued input event, then Layout + Repaint
            var evs = guiEvents.ToArray(); guiEvents.Clear();
            buttonsPrev.Clear(); buttonsPrev.AddRange(buttons);
            foreach (var ev in evs) { GuiPass(beh, new Event { type = EventType.Layout, rawPos = ev.rawPos }); mouseGUI = ev.rawPos; GuiPass(beh, ev); }
            buttons.Clear();
            GuiPass(beh, new Event { type = EventType.Layout, rawPos = mouseGUI });
            GuiPass(beh, new Event { type = EventType.Repaint, rawPos = mouseGUI }, true);
            typedText = null;
            foreach (var o in pendingDestroy.ToArray()) DestroyNow(o);
            pendingDestroy.Clear();
        }
        static bool recordButtons;
        internal static bool RecordButtons { get { return recordButtons; } }
        static void GuiPass(List<MonoBehaviour> beh, Event e, bool repaint = false)
        {
            recordButtons = repaint;
            foreach (var mb in beh)
            {
                Event.s_current = e; GUI.ResetPass(); controlCounter = 0;
                Invoke(mb, "OnGUI");
                GUI.CheckBalanced();
            }
            Event.s_current = null; recordButtons = false;
        }
        internal static void DestroyNow(Object o)
        {
            if (o == null || o.__destroyed) return;
            var go = o as GameObject;
            if (go != null) { foreach (var c in go.comps.ToArray()) DestroyNow(c); }
            var comp = o as Component;
            if (comp != null) { if (comp is MonoBehaviour) Invoke(comp, "OnDestroy"); components.Remove(comp); if (comp.go != null) comp.go.comps.Remove(comp); }
            o.__destroyed = true;
            o.OnDestroyed();
        }
        public static void Quit()
        {
            foreach (var c in components.ToArray()) if (c is MonoBehaviour) Invoke(c, "OnApplicationQuit");
            foreach (var c in components.ToArray()) DestroyNow(c);
        }
        public static void Focus(bool f) { foreach (var c in components.ToArray()) if (c is MonoBehaviour) { var mi = c.GetType().GetMethod("OnApplicationFocus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); if (mi != null) mi.Invoke(c, new object[] { f }); } }
        public static void InvokeStatic(Type t, string m)
        {
            var mi = t.GetMethod(m, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            mi.Invoke(null, null);
        }
    }

    public class Object
    {
        internal bool __destroyed; readonly int __id;
        string _name = "";
        public Object() { __id = Interlocked.Increment(ref HeadlessHost.nextInstanceId); }
        public string name { get { return _name; } set { _name = value; } }
        public HideFlags hideFlags { get; set; }
        internal virtual void OnDestroyed() { }
        internal void Alive(string api)
        {
            if (__destroyed) { HeadlessHost.Problem("use of destroyed " + GetType().Name + " '" + _name + "' in " + api + "\n" + Environment.StackTrace); throw new MissingReferenceException("The object of type '" + GetType().Name + "' has been destroyed but you are still trying to access it."); }
        }
        public static void Destroy(Object o) { HeadlessHost.MainOnly("Destroy"); if (!ReferenceEquals(o, null) && !o.__destroyed) HeadlessHost.pendingDestroy.Add(o); }
        public static void Destroy(Object o, float t) { Destroy(o); }
        public static void DestroyImmediate(Object o) { HeadlessHost.MainOnly("DestroyImmediate"); HeadlessHost.DestroyNow(o); }
        public static void DontDestroyOnLoad(Object o) { }
        public static T FindObjectOfType<T>() where T : Object { foreach (var c in HeadlessHost.components) if (c is T && !c.__destroyed) return (T)(Object)c; return null; }
        public static T[] FindObjectsOfType<T>() where T : Object { var l = new List<T>(); foreach (var c in HeadlessHost.components) if (c is T && !c.__destroyed) l.Add((T)(Object)c); return l.ToArray(); }
        public static T Instantiate<T>(T o) where T : Object { return o; }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null) && !o.__destroyed; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.__destroyed, bn = ReferenceEquals(b, null) || b.__destroyed;
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { var u = o as Object; return u == this; }
        public override int GetHashCode() { return __id; }
        public int GetInstanceID() { return __id; }
    }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61, DontSave = 52 }
    public class Component : Object
    {
        internal GameObject go;
        public GameObject gameObject { get { return go; } }
        public Transform transform { get { return go != null ? go.transform : null; } }
        public T GetComponent<T>() { return go != null ? go.GetComponent<T>() : default(T); }
    }
    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Transform parent { get; private set; }
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; }
        public void SetParent(Transform p) { parent = p; }
    }
    public class Behaviour : Component { bool _en = true; public bool enabled { get { return _en; } set { _en = value; } } public bool isActiveAndEnabled { get { return _en && !__destroyed; } } }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(System.Collections.IEnumerator e) { return null; }
        public void Invoke(string m, float t) { }
    }
    public class Coroutine { }
    public sealed class GameObject : Object
    {
        internal readonly List<Component> comps = new List<Component>();
        readonly Transform tr;
        public GameObject() : this("New Game Object") { }
        public GameObject(string name)
        {
            HeadlessHost.MainOnly("GameObject..ctor");
            this.name = name;
            tr = new Transform(); tr.go = this; comps.Add(tr);
        }
        public GameObject(string name, params Type[] comps) : this(name) { }
        public T AddComponent<T>() where T : Component
        {
            HeadlessHost.MainOnly("AddComponent");
            Alive("AddComponent");
            var c = (T)Activator.CreateInstance(typeof(T), true);
            c.go = this; comps.Add(c); HeadlessHost.components.Add(c);
            HeadlessHost.AwakeComponent(c);
            return c;
        }
        public T GetComponent<T>() { foreach (var c in comps) if (c is T && !c.__destroyed) return (T)(object)c; return default(T); }
        public Transform transform { get { return tr; } }
        public bool activeSelf { get; private set; }
        public void SetActive(bool v) { activeSelf = v; }
    }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }

    public static class Debug
    {
        static string Tag() { return "[frame " + HeadlessHost.frameCount + (Thread.CurrentThread != HeadlessHost.mainThread ? " thread " + Thread.CurrentThread.Name : "") + "] "; }
        public static void Log(object o) { lock (HeadlessHost.logLock) if (HeadlessHost.verboseLog) HeadlessHost.log.WriteLine("LOG " + Tag() + o); }
        public static void LogWarning(object o) { lock (HeadlessHost.logLock) { HeadlessHost.warnings.Add(Tag() + o); HeadlessHost.log.WriteLine("WARN " + Tag() + o); } }
        public static void LogError(object o) { lock (HeadlessHost.logLock) { HeadlessHost.errors.Add(Tag() + o + "\n" + Environment.StackTrace); HeadlessHost.log.WriteLine("ERROR " + Tag() + o + "\n" + Environment.StackTrace); } }
        public static void LogException(Exception e) { lock (HeadlessHost.logLock) { HeadlessHost.exceptions.Add(Tag() + e); HeadlessHost.log.WriteLine("EXCEPTION " + Tag() + e); } }
    }
    public struct Mathf
    {
        public const float PI = 3.14159274f;
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Min(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static float Max(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
        public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float Abs(float v) { return Math.Abs(v); }
        public static int Abs(int v) { return Math.Abs(v); }
        public static float Floor(float v) { return (float)Math.Floor(v); }
        public static int FloorToInt(float v) { return (int)Math.Floor(v); }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
        public static int CeilToInt(float v) { return (int)Math.Ceiling(v); }
        public static float Round(float v) { return (float)Math.Round(v); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static ushort FloatToHalf(float v)
        {
            uint x = BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
            uint sign = (x >> 16) & 0x8000; int exp = (int)((x >> 23) & 0xff) - 127 + 15; uint mant = x & 0x7fffff;
            if (((x >> 23) & 0xff) == 0xff) return (ushort)(sign | 0x7c00 | (mant != 0 ? 0x200u : 0));
            if (exp >= 31) return (ushort)(sign | 0x7c00);
            if (exp <= 0) { if (exp < -10) return (ushort)sign; mant |= 0x800000; int sh = 14 - exp; uint r = mant >> sh; if (((mant >> (sh - 1)) & 1) != 0 && ((mant & ((1u << (sh - 1)) - 1)) != 0 || (r & 1) != 0)) r++; return (ushort)(sign | r); }
            uint h = sign | ((uint)exp << 10) | (mant >> 13);
            if ((mant & 0x1000) != 0 && ((mant & 0x2fff) != 0)) h++;
            return (ushort)h;
        }
        public static float HalfToFloat(ushort v)
        {
            int s = (v >> 15) & 1, e = (v >> 10) & 31, m = v & 1023; double r;
            if (e == 0) r = m * Math.Pow(2, -24); else if (e == 31) r = m != 0 ? double.NaN : double.PositiveInfinity; else r = (1 + m / 1024.0) * Math.Pow(2, e - 15);
            return (float)(s != 0 ? -r : r);
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.x * b, a.y * b); }
        public static Vector2 operator /(Vector2 a, float b) { return new Vector2(a.x / b, a.y / b); }
        public static float Distance(Vector2 a, Vector2 b) { return (a - b).magnitude; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator *(float b, Vector3 a) { return a * b; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public float this[int i] { get { return i == 0 ? x : i == 1 ? y : i == 2 ? z : Bad(i); } set { if (i == 0) x = value; else if (i == 1) y = value; else if (i == 2) z = value; else Bad(i); } }
        static float Bad(int i) { throw new IndexOutOfRangeException("Invalid Vector3 index!"); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4(float x, float y, float z) { this.x = x; this.y = y; this.z = z; w = 0; }
        public static Vector4 zero { get { return new Vector4(); } }
        public float this[int i] { get { switch (i) { case 0: return x; case 1: return y; case 2: return z; case 3: return w; } throw new IndexOutOfRangeException("Invalid Vector4 index!"); } set { switch (i) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; case 3: w = value; break; default: throw new IndexOutOfRangeException("Invalid Vector4 index!"); } } }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0, 0, 0, 1); } }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
        public static Color red { get { return new Color(1, 0, 0, 1); } }
        public static Color gray { get { return new Color(0.5f, 0.5f, 0.5f, 1); } }
        public static Color yellow { get { return new Color(1, 0.92156863f, 0.015686275f, 1); } }
        public static Color operator *(Color a, float b) { return new Color(a.r * b, a.g * b, a.b * b, a.a * b); }
        public static Color Lerp(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
        public static implicit operator Vector4(Color c) { return new Vector4(c.r, c.g, c.b, c.a); }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) { return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }
        static byte B(float v) { return (byte)Math.Round(Mathf.Clamp01(v) * 255f); }
        public static implicit operator Color32(Color c) { return new Color32(B(c.r), B(c.g), B(c.b), B(c.a)); }
    }
    public struct Rect
    {
        float m_X, m_Y, m_W, m_H;
        public Rect(float x, float y, float w, float h) { m_X = x; m_Y = y; m_W = w; m_H = h; }
        public Rect(Vector2 p, Vector2 s) { m_X = p.x; m_Y = p.y; m_W = s.x; m_H = s.y; }
        public float x { get { return m_X; } set { m_X = value; } }
        public float y { get { return m_Y; } set { m_Y = value; } }
        public float width { get { return m_W; } set { m_W = value; } }
        public float height { get { return m_H; } set { m_H = value; } }
        public float xMin { get { return m_X; } set { float o = xMax; m_X = value; m_W = o - m_X; } }
        public float yMin { get { return m_Y; } set { float o = yMax; m_Y = value; m_H = o - m_Y; } }
        public float xMax { get { return m_W + m_X; } set { m_W = value - m_X; } }
        public float yMax { get { return m_H + m_Y; } set { m_H = value - m_Y; } }
        public Vector2 center { get { return new Vector2(x + m_W / 2f, y + m_H / 2f); } set { m_X = value.x - m_W / 2f; m_Y = value.y - m_H / 2f; } }
        public Vector2 position { get { return new Vector2(m_X, m_Y); } set { m_X = value.x; m_Y = value.y; } }
        public Vector2 size { get { return new Vector2(m_W, m_H); } set { m_W = value.x; m_H = value.y; } }
        public bool Contains(Vector2 p) { return p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax; }
        public bool Contains(Vector3 p) { return Contains(new Vector2(p.x, p.y)); }
        public bool Overlaps(Rect r) { return r.xMax > xMin && r.xMin < xMax && r.yMax > yMin && r.yMin < yMax; }
        public static Rect zero { get { return new Rect(); } }
        public override string ToString() { return "(x:" + x + ", y:" + y + ", width:" + width + ", height:" + height + ")"; }
    }
    public struct Bounds
    {
        Vector3 m_C, m_E;
        public Bounds(Vector3 c, Vector3 s) { m_C = c; m_E = s * 0.5f; }
        public Vector3 center { get { return m_C; } set { m_C = value; } }
        public Vector3 size { get { return m_E * 2f; } set { m_E = value * 0.5f; } }
        public Vector3 extents { get { return m_E; } set { m_E = value; } }
        public Vector3 min { get { return m_C - m_E; } set { SetMinMax(value, max); } }
        public Vector3 max { get { return m_C + m_E; } set { SetMinMax(min, value); } }
        public void SetMinMax(Vector3 a, Vector3 b) { m_E = (b - a) * 0.5f; m_C = a + m_E; }
    }
    public struct Matrix4x4
    {
        public float m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33;
        public float this[int r, int c] { get { return this[r + c * 4]; } set { this[r + c * 4] = value; } }
        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return m00; case 1: return m10; case 2: return m20; case 3: return m30; case 4: return m01; case 5: return m11; case 6: return m21; case 7: return m31;
                    case 8: return m02; case 9: return m12; case 10: return m22; case 11: return m32; case 12: return m03; case 13: return m13; case 14: return m23; case 15: return m33;
                }
                throw new IndexOutOfRangeException("Invalid matrix index!");
            }
            set
            {
                switch (i)
                {
                    case 0: m00 = value; break; case 1: m10 = value; break; case 2: m20 = value; break; case 3: m30 = value; break;
                    case 4: m01 = value; break; case 5: m11 = value; break; case 6: m21 = value; break; case 7: m31 = value; break;
                    case 8: m02 = value; break; case 9: m12 = value; break; case 10: m22 = value; break; case 11: m32 = value; break;
                    case 12: m03 = value; break; case 13: m13 = value; break; case 14: m23 = value; break; case 15: m33 = value; break;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
        }
        public static Matrix4x4 identity { get { var m = new Matrix4x4(); m.m00 = m.m11 = m.m22 = m.m33 = 1; return m; } }
        public static Matrix4x4 Scale(Vector3 v) { var m = new Matrix4x4(); m.m00 = v.x; m.m11 = v.y; m.m22 = v.z; m.m33 = 1; return m; }
        public static Matrix4x4 Translate(Vector3 v) { var m = identity; m.m03 = v.x; m.m13 = v.y; m.m23 = v.z; return m; }
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s)
        {
            float x = q.x * 2, y = q.y * 2, z = q.z * 2, xx = q.x * x, yy = q.y * y, zz = q.z * z, xy = q.x * y, xz = q.x * z, yz = q.y * z, wx = q.w * x, wy = q.w * y, wz = q.w * z;
            var m = new Matrix4x4();
            m.m00 = (1 - (yy + zz)) * s.x; m.m10 = (xy + wz) * s.x; m.m20 = (xz - wy) * s.x;
            m.m01 = (xy - wz) * s.y; m.m11 = (1 - (xx + zz)) * s.y; m.m21 = (yz + wx) * s.y;
            m.m02 = (xz + wy) * s.z; m.m12 = (yz - wx) * s.z; m.m22 = (1 - (xx + yy)) * s.z;
            m.m03 = p.x; m.m13 = p.y; m.m23 = p.z; m.m33 = 1;
            return m;
        }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var m = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) { float s = 0; for (int k = 0; k < 4; k++) s += a[r, k] * b[k, c]; m[r, c] = s; }
            return m;
        }
        public Matrix4x4 inverse
        {
            get
            {
                var a = new double[16]; for (int i = 0; i < 16; i++) a[i] = this[i];
                var inv = new double[16];
                inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
                inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
                inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
                inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];
                inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
                inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
                inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
                inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
                inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
                inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
                inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
                inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
                inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
                inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
                inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
                inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];
                double det = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];
                var m = new Matrix4x4(); if (det == 0) return m;
                for (int i = 0; i < 16; i++) m[i] = (float)(inv[i] / det);
                return m;
            }
        }
        internal bool HasNaN() { for (int i = 0; i < 16; i++) if (float.IsNaN(this[i]) || float.IsInfinity(this[i])) return true; return false; }
    }

    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, R8 = 63, RG16 = 62, RHalf = 15, RGBAHalf = 17, RFloat = 18 }
    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Default = 7, R8 = 16 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public class Texture : Object
    {
        internal int w, h, mips = 1;
        public int width { get { return w; } set { w = value; } }
        public int height { get { return h; } set { h = value; } }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
        public float mipMapBias { get; set; }
        public int mipmapCount { get { return mips; } }
        internal static int MipCount(int w, int h, bool mip) { if (!mip) return 1; int n = 1, s = Math.Max(w, h); while (s > 1) { s >>= 1; n++; } return n; }
        internal static int BPP(TextureFormat f) { switch (f) { case TextureFormat.Alpha8: case TextureFormat.R8: return 1; case TextureFormat.RG16: case TextureFormat.RHalf: return 2; case TextureFormat.RGB24: return 3; case TextureFormat.RGBAHalf: return 8; default: return 4; } }
    }
    public sealed class Texture2D : Texture
    {
        internal Color32[] px; TextureFormat fmt = TextureFormat.RGBA32; internal bool readable = true;
        public Texture2D(int w, int h) : this(w, h, TextureFormat.RGBA32, true) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip) : this(w, h, f, mip, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) { Init(w, h, f, MipCount(w, h, mip)); }
        public Texture2D(int w, int h, TextureFormat f, int mipCount, bool linear) { Init(w, h, f, mipCount < 0 ? MipCount(w, h, true) : Math.Max(1, mipCount)); }
        void Init(int w, int h, TextureFormat f, int m)
        {
            HeadlessHost.MainOnly("Texture2D..ctor");
            if (w <= 0 || h <= 0) throw new UnityException("Failed to create texture because of invalid parameters.");
            this.w = w; this.h = h; fmt = f; mips = m; px = new Color32[w * h];
        }
        static Texture2D _white;
        public static Texture2D whiteTexture { get { if (_white == null) { _white = new Texture2D(4, 4, TextureFormat.RGBA32, false); for (int i = 0; i < 16; i++) _white.px[i] = new Color32(255, 255, 255, 255); } return _white; } }
        public static Texture2D blackTexture { get { return new Texture2D(4, 4, TextureFormat.RGBA32, false); } }
        public bool isReadable { get { return readable; } }
        void R(string api) { HeadlessHost.MainOnly(api); Alive(api); if (!readable) throw new UnityException("Texture '" + name + "' is not readable, the texture memory can not be accessed from scripts. (" + api + ")"); }
        int MipW(int mip) { return Math.Max(1, w >> mip); }
        int MipH(int mip) { return Math.Max(1, h >> mip); }
        public void SetPixels32(Color32[] c) { SetPixels32(c, 0); }
        public void SetPixels32(Color32[] c, int mip)
        {
            R("SetPixels32");
            if (mip < 0 || mip >= mips) throw new ArgumentException("Invalid mip level " + mip);
            if (c == null || c.Length != MipW(mip) * MipH(mip)) throw new ArgumentException("Array size must be at least width*height");
            if (mip == 0) Array.Copy(c, px, px.Length);
        }
        public void SetPixels(Color[] c)
        {
            R("SetPixels");
            if (c == null || c.Length != w * h) throw new ArgumentException("Array size must be at least width*height");
            for (int i = 0; i < px.Length; i++) px[i] = c[i];
        }
        public Color32[] GetPixels32() { return GetPixels32(0); }
        public Color32[] GetPixels32(int mip) { R("GetPixels32"); if (mip != 0) return new Color32[MipW(mip) * MipH(mip)]; return (Color32[])px.Clone(); }
        public Color[] GetPixels() { R("GetPixels"); var o = new Color[px.Length]; for (int i = 0; i < o.Length; i++) o[i] = px[i]; return o; }
        public Color GetPixel(int x, int y) { R("GetPixel"); x = Math.Max(0, Math.Min(w - 1, x)); y = Math.Max(0, Math.Min(h - 1, y)); return px[y * w + x]; }
        public void SetPixel(int x, int y, Color c) { R("SetPixel"); if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c; }
        public void SetPixelData<T>(T[] data, int mip, int sourceDataStartIndex = 0)
        {
            R("SetPixelData");
            if (data == null) throw new ArgumentNullException("data");
            if (mip < 0 || mip >= mips) throw new ArgumentException("SetPixelData: mip level out of bounds");
            int es = Marshal.SizeOf(typeof(T)), need = MipW(mip) * MipH(mip) * BPP(fmt);
            if (sourceDataStartIndex < 0 || (long)(data.Length - sourceDataStartIndex) * es < need) throw new ArgumentException("SetPixelData: size of data to be filled was larger than the size of data available in the source array.");
            if (mip != 0 || BPP(fmt) != 4) return;
            var bytes = HeadlessMesh.ToBytes(data, sourceDataStartIndex, need / es + (need % es != 0 ? 1 : 0));
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
        }
        public Unity.Collections.NativeArray<T> GetPixelData<T>(int mip) where T : struct { R("GetPixelData"); return new Unity.Collections.NativeArray<T>(MipW(mip) * MipH(mip) * BPP(fmt) / Marshal.SizeOf(typeof(T)), Unity.Collections.Allocator.None); }
        public Unity.Collections.NativeArray<T> GetRawTextureData<T>() where T : struct { R("GetRawTextureData"); return new Unity.Collections.NativeArray<T>(w * h * BPP(fmt) / Marshal.SizeOf(typeof(T)), Unity.Collections.Allocator.None); }
        public byte[] GetRawTextureData() { R("GetRawTextureData"); var b = new byte[px.Length * 4]; for (int i = 0; i < px.Length; i++) { b[i * 4] = px[i].r; b[i * 4 + 1] = px[i].g; b[i * 4 + 2] = px[i].b; b[i * 4 + 3] = px[i].a; } return b; }
        public void LoadRawTextureData(byte[] data) { R("LoadRawTextureData"); if (data.Length < w * h * BPP(fmt)) throw new UnityException("LoadRawTextureData: not enough data provided (will result in overread)."); if (BPP(fmt) == 4) for (int i = 0; i < px.Length; i++) px[i] = new Color32(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]); }
        public void Apply() { Apply(true, false); }
        public void Apply(bool updateMipmaps) { Apply(updateMipmaps, false); }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { R("Apply"); if (makeNoLongerReadable) readable = false; }
        public TextureFormat format { get { return fmt; } }
        internal void Replace(int nw, int nh, Color32[] p, bool markNonReadable) { w = nw; h = nh; px = p; fmt = TextureFormat.RGBA32; mips = mips > 1 ? MipCount(nw, nh, true) : 1; readable = !markNonReadable; }
    }
    public sealed class Texture2DArray : Texture
    {
        int d; TextureFormat fmt; internal Color32[][][] layers; bool readable = true;
        public Texture2DArray(int w, int h, int depth, TextureFormat f, bool mip) : this(w, h, depth, f, mip ? -1 : 1, false) { }
        public Texture2DArray(int w, int h, int depth, TextureFormat f, bool mip, bool linear) : this(w, h, depth, f, mip ? -1 : 1, linear) { }
        public Texture2DArray(int w, int h, int depth, TextureFormat f, int mipCount, bool linear)
        {
            HeadlessHost.MainOnly("Texture2DArray..ctor");
            if (w <= 0 || h <= 0 || depth <= 0) throw new UnityException("Failed to create 2D array texture because of invalid parameters.");
            if (depth > 2048) throw new UnityException("Texture2DArray depth " + depth + " exceeds the GPU limit (2048)");
            this.w = w; this.h = h; d = depth; fmt = f; mips = mipCount < 0 ? MipCount(w, h, true) : Math.Max(1, mipCount);
            layers = new Color32[depth][][];
        }
        public int depth { get { return d; } }
        void Chk(Color32[] c, int el, int mip, string api)
        {
            HeadlessHost.MainOnly(api); Alive(api);
            if (!readable) throw new UnityException("Texture2DArray is not readable (" + api + ")");
            if (el < 0 || el >= d) throw new ArgumentException(api + ": array element " + el + " out of range (depth " + d + ")");
            if (mip < 0 || mip >= mips) throw new ArgumentException(api + ": mip level " + mip + " out of range (" + mips + ")");
            int n = Math.Max(1, w >> mip) * Math.Max(1, h >> mip);
            if (c == null || c.Length != n) throw new ArgumentException(api + ": array size " + (c == null ? -1 : c.Length) + " must be width*height (" + n + ") of mip " + mip);
            if (layers[el] == null) layers[el] = new Color32[mips][];
            layers[el][mip] = (Color32[])c.Clone();
        }
        public void SetPixels32(Color32[] c, int arrayElement, int mip) { Chk(c, arrayElement, mip, "SetPixels32"); }
        public void SetPixels32(Color32[] c, int arrayElement) { Chk(c, arrayElement, 0, "SetPixels32"); }
        public void SetPixels(Color[] c, int arrayElement, int mip) { var cc = new Color32[c.Length]; for (int i = 0; i < c.Length; i++) cc[i] = c[i]; Chk(cc, arrayElement, mip, "SetPixels"); }
        public void SetPixelData<T>(T[] data, int mip, int element, int sourceDataStartIndex = 0)
        {
            int n = Math.Max(1, w >> mip) * Math.Max(1, h >> mip);
            var b = HeadlessMesh.ToBytes(data, sourceDataStartIndex, data.Length - sourceDataStartIndex);
            if (b.Length < n * 4) throw new ArgumentException("SetPixelData: not enough data");
            var c = new Color32[n]; for (int i = 0; i < n; i++) c[i] = new Color32(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]);
            Chk(c, element, mip, "SetPixelData");
        }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            HeadlessHost.MainOnly("Texture2DArray.Apply");
            if (!updateMipmaps) for (int e = 0; e < d; e++) for (int m = 0; m < mips; m++) if (layers[e] == null || layers[e][m] == null) { HeadlessHost.Problem("Texture2DArray '" + name + "' element " + e + " mip " + m + " never uploaded"); e = d; break; }
            if (makeNoLongerReadable) readable = false;
        }
        public void Apply(bool updateMipmaps) { Apply(updateMipmaps, false); }
        public void Apply() { Apply(true, false); }
    }
    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int depth) { HeadlessHost.MainOnly("RenderTexture..ctor"); this.w = w; this.h = h; }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f) : this(w, h, depth) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f, RenderTextureReadWrite rw) : this(w, h, depth) { }
        public int antiAliasing { get; set; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; }
        bool created;
        public bool Create() { Alive("RenderTexture.Create"); created = true; return true; }
        public void Release() { created = false; }
        public bool IsCreated() { return created; }
        public static RenderTexture active { get; set; }
    }
    public static class ImageConversion
    {
        public static bool LoadImage(this Texture2D tex, byte[] data) { return LoadImage(tex, data, false); }
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable)
        {
            HeadlessHost.MainOnly("LoadImage");
            if (data == null) throw new ArgumentNullException("data");
            int w, h; Color32[] px;
            if (!HeadlessPng.Decode(data, out w, out h, out px)) { tex.Replace(8, 8, new Color32[64], markNonReadable); return false; }
            tex.Replace(w, h, px, markNonReadable);
            return true;
        }
        public static byte[] EncodeToPNG(this Texture2D tex) { return new byte[0]; }
    }
    public class TextAsset : Object
    {
        internal byte[] data;
        public string text
        {
            get
            {
                var b = data; int o = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
                return Encoding.UTF8.GetString(b, o, b.Length - o);
            }
        }
        public byte[] bytes { get { return (byte[])data.Clone(); } }
    }
    public static class Resources
    {
        static readonly string[] TEXT_EXT = { ".txt", ".html", ".htm", ".xml", ".bytes", ".json", ".csv", ".yaml", ".fnt" };
        static readonly Dictionary<string, TextAsset> cache = new Dictionary<string, TextAsset>();
        public static Object Load(string path) { return Load<TextAsset>(path); }
        public static T Load<T>(string path) where T : Object
        {
            HeadlessHost.MainOnly("Resources.Load");
            if (typeof(T) != typeof(TextAsset) && typeof(T) != typeof(Object)) return null;
            TextAsset ta;
            if (cache.TryGetValue(path, out ta)) return (T)(Object)ta;
            foreach (var ext in TEXT_EXT)
            {
                var f = Path.Combine(HeadlessHost.resourcesRoot, path + ext);
                if (File.Exists(f)) { ta = new TextAsset { data = File.ReadAllBytes(f), name = Path.GetFileName(path) }; cache[path] = ta; return (T)(Object)ta; }
            }
            HeadlessHost.Problem("Resources.Load<" + typeof(T).Name + ">(\"" + path + "\") returned null");
            return null;
        }
        public static T[] LoadAll<T>(string path) where T : Object { return new T[0]; }
    }
    public sealed class Shader : Object
    {
        static HashSet<string> known;
        static readonly Dictionary<string, Shader> found = new Dictionary<string, Shader>();
        static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        static readonly string[] BUILTIN = { "Hidden/InternalErrorShader", "Unlit/Texture", "Unlit/Color", "Sprites/Default", "Standard", "Hidden/Internal-Colored", "UI/Default" };
        public static Shader Find(string name)
        {
            HeadlessHost.MainOnly("Shader.Find");
            if (known == null)
            {
                known = new HashSet<string>(BUILTIN);
                var dir = Path.Combine(HeadlessHost.resourcesRoot, "VoxelForge/Shaders");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.shader"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f), "Shader\\s+\"([^\"]+)\"");
                        if (m.Success) known.Add(m.Groups[1].Value);
                    }
            }
            if (name == null || !known.Contains(name)) return null;
            Shader s; if (!found.TryGetValue(name, out s)) found[name] = s = new Shader { name = name };
            return s;
        }
        public static int PropertyToID(string name) { lock (ids) { int v; if (!ids.TryGetValue(name, out v)) ids[name] = v = ids.Count + 1; return v; } }
        public static void SetGlobalTexture(string n, Texture t) { }
        public static void SetGlobalTexture(int n, Texture t) { }
        public static void SetGlobalVector(string n, Vector4 v) { }
        public static void SetGlobalVector(int n, Vector4 v) { }
        public static void SetGlobalFloat(string n, float v) { }
        public static void SetGlobalFloat(int n, float v) { }
        public static void SetGlobalMatrix(string n, Matrix4x4 m) { }
        public static void SetGlobalColor(string n, Color c) { }
        public bool isSupported { get { return true; } }
    }
    public class Material : Object
    {
        internal readonly Dictionary<string, object> props = new Dictionary<string, object>(); internal readonly HashSet<string> keywords = new HashSet<string>();
        Shader sh;
        public Material(Shader s) { HeadlessHost.MainOnly("Material..ctor"); if (s == null) throw new ArgumentNullException("shader", "Value cannot be null."); sh = s; }
        public Material(Material m) { HeadlessHost.MainOnly("Material..ctor"); sh = m.sh; foreach (var kv in m.props) props[kv.Key] = kv.Value; keywords.UnionWith(m.keywords); }
        public Shader shader { get { return sh; } set { sh = value; } }
        public Texture mainTexture { get { object o; return props.TryGetValue("_MainTex", out o) ? (Texture)o : null; } set { P("_MainTex", value); } }
        public Color color { get { object o; return props.TryGetValue("_Color", out o) ? (Color)o : Color.white; } set { P("_Color", value); } }
        public int renderQueue { get; set; }
        public bool enableInstancing { get; set; }
        void P(string n, object v)
        {
            HeadlessHost.MainOnly("Material.Set"); Alive("Material.Set " + n);
            if (v is float && (float.IsNaN((float)v) || float.IsInfinity((float)v))) HeadlessHost.Problem("Material '" + name + "' " + n + " = " + v);
            props[n] = v;
        }
        public void SetFloat(string n, float v) { P(n, v); }
        public void SetFloat(int n, float v) { P("#" + n, v); }
        public void SetInt(string n, int v) { P(n, (float)v); }
        public void SetInteger(string n, int v) { P(n, v); }
        public void SetColor(string n, Color c) { P(n, c); }
        public void SetColor(int n, Color c) { P("#" + n, c); }
        public void SetVector(string n, Vector4 v) { P(n, v); }
        public void SetVector(int n, Vector4 v) { P("#" + n, v); }
        public void SetMatrix(string n, Matrix4x4 m) { P(n, m); }
        public void SetTexture(string n, Texture t) { P(n, t); }
        public void SetTexture(int n, Texture t) { P("#" + n, t); }
        public void EnableKeyword(string k) { keywords.Add(k); }
        public void DisableKeyword(string k) { keywords.Remove(k); }
        public bool IsKeywordEnabled(string k) { return keywords.Contains(k); }
        public bool SetPass(int pass) { return true; }
    }
    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }

    /// <summary>Byte helpers for mesh/texture buffers.</summary>
    public static class HeadlessMesh
    {
        public static byte[] ToBytes<T>(T[] data, int start, int count)
        {
            int es = Marshal.SizeOf(typeof(T)); var b = new byte[count * es];
            if (count == 0) return b;
            var hnd = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { Marshal.Copy(new IntPtr(hnd.AddrOfPinnedObject().ToInt64() + (long)start * es), b, 0, b.Length); } finally { hnd.Free(); }
            return b;
        }
        public static byte[] ToBytes<T>(List<T> data, int start, int count) { return ToBytes(data.ToArray(), start, count); }
        public static int FormatSize(Rendering.VertexAttributeFormat f)
        {
            switch (f) { case Rendering.VertexAttributeFormat.Float32: case Rendering.VertexAttributeFormat.UInt32: case Rendering.VertexAttributeFormat.SInt32: return 4; case Rendering.VertexAttributeFormat.Float16: case Rendering.VertexAttributeFormat.UNorm16: case Rendering.VertexAttributeFormat.SNorm16: case Rendering.VertexAttributeFormat.UInt16: case Rendering.VertexAttributeFormat.SInt16: return 2; default: return 1; }
        }
        public static long totalUploads, totalVertexBytes;
    }
    public sealed class Mesh : Object
    {
        // vertex streams
        internal Rendering.VertexAttributeDescriptor[] layout = new Rendering.VertexAttributeDescriptor[0];
        internal int vcount; internal int[] strides = new int[4]; internal byte[][] vdata = new byte[4][];
        internal Rendering.IndexFormat ifmt = Rendering.IndexFormat.UInt16; internal int icount; internal byte[] idata = new byte[0];
        internal List<Rendering.SubMeshDescriptor> subs = new List<Rendering.SubMeshDescriptor>();
        Vector3[] legacyVerts;
        public Mesh() { HeadlessHost.MainOnly("Mesh..ctor"); }
        void M(string api) { HeadlessHost.MainOnly("Mesh." + api); Alive("Mesh." + api); }
        public Rendering.IndexFormat indexFormat { get { return ifmt; } set { M("indexFormat"); ifmt = value; } }
        public Vector3[] vertices
        {
            get { M("vertices"); return legacyVerts != null ? (Vector3[])legacyVerts.Clone() : new Vector3[0]; }
            set
            {
                M("vertices");
                legacyVerts = (Vector3[])value.Clone();
                SetVertexBufferParams(value.Length, new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.Position, Rendering.VertexAttributeFormat.Float32, 3));
                vdata[0] = HeadlessMesh.ToBytes(value, 0, value.Length);
                CheckPositions("vertices");
            }
        }
        public int[] triangles { get { M("triangles"); return GetIdx(0, icount); } set { SetTriangles(value, 0, true); } }
        public Vector2[] uv { get; set; }
        public Color32[] colors32 { get; set; }
        Bounds _b;
        public Bounds bounds { get { return _b; } set { M("bounds"); _b = value; if (float.IsNaN(value.center.x + value.center.y + value.center.z + value.size.x + value.size.y + value.size.z)) HeadlessHost.Problem("Mesh '" + name + "' NaN bounds"); } }
        public int vertexCount { get { M("vertexCount"); return vcount; } }
        public int subMeshCount
        {
            get { return subs.Count; }
            set
            {
                M("subMeshCount");
                if (value < 0) throw new ArgumentException("subMeshCount can't be set to negative value");
                while (subs.Count > value) subs.RemoveAt(subs.Count - 1);
                while (subs.Count < value) subs.Add(new Rendering.SubMeshDescriptor(0, 0));
            }
        }
        internal override void OnDestroyed() { vdata = new byte[4][]; idata = new byte[0]; subs.Clear(); }
        public void Clear() { Clear(true); }
        public void Clear(bool keepVertexLayout)
        {
            M("Clear");
            vcount = 0; vdata = new byte[4][]; icount = 0; idata = new byte[0]; subs.Clear(); subs.Add(new Rendering.SubMeshDescriptor(0, 0)); legacyVerts = null;
            if (!keepVertexLayout) { layout = new Rendering.VertexAttributeDescriptor[0]; strides = new int[4]; }
        }
        public void MarkDynamic() { M("MarkDynamic"); }
        public void UploadMeshData(bool markNoLongerReadable) { M("UploadMeshData"); }
        int[] GetIdx(int start, int count)
        {
            var r = new int[count];
            for (int i = 0; i < count; i++) r[i] = ifmt == Rendering.IndexFormat.UInt16 ? BitConverter.ToUInt16(idata, (start + i) * 2) : BitConverter.ToInt32(idata, (start + i) * 4);
            return r;
        }
        public void SetTriangles(int[] t, int submesh) { SetTriangles(t, submesh, true); }
        public void SetTriangles(int[] t, int submesh, bool calculateBounds) { SetIndices(t, MeshTopology.Triangles, submesh, calculateBounds); }
        public void SetTriangles(List<int> t, int submesh, bool calculateBounds) { SetIndices(t.ToArray(), MeshTopology.Triangles, submesh, calculateBounds); }
        public void SetIndices(int[] idx, MeshTopology topo, int submesh) { SetIndices(idx, topo, submesh, true); }
        public void SetIndices(int[] idx, MeshTopology topo, int submesh, bool calculateBounds)
        {
            M("SetIndices");
            if (submesh < 0 || submesh >= Math.Max(1, subs.Count)) { Debug.LogError("Failed setting triangles. Submesh index is out of bounds."); return; }
            foreach (var i in idx) if (i < 0 || i >= vcount) { Debug.LogError("Failed setting triangles. Some indices are referencing out of bounds vertices. IndexCount: " + idx.Length + ", VertexCount: " + vcount); return; }
            if (subs.Count == 0) subs.Add(new Rendering.SubMeshDescriptor(0, 0));
            // legacy API: only single-submesh meshes are used by the game; rebuild the index buffer for that case
            int max = 0; foreach (var i in idx) max = Math.Max(max, i);
            if (max > 65535 && ifmt == Rendering.IndexFormat.UInt16) { Debug.LogError("Mesh '" + name + "': indices > 65535 with a 16-bit index buffer"); return; }
            icount = idx.Length;
            idata = ifmt == Rendering.IndexFormat.UInt16 ? HeadlessMesh.ToBytes(Array.ConvertAll(idx, v => (ushort)v), 0, idx.Length) : HeadlessMesh.ToBytes(idx, 0, idx.Length);
            subs[submesh] = new Rendering.SubMeshDescriptor(0, idx.Length, topo) { vertexCount = vcount };
        }
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetVertices(Vector3[] v) { vertices = v; }
        public void SetVertexBufferParams(int vertexCount, params Rendering.VertexAttributeDescriptor[] attributes)
        {
            M("SetVertexBufferParams");
            if (vertexCount < 0) throw new ArgumentOutOfRangeException("vertexCount");
            var st = new int[4]; var last = new int[4] { -1, -1, -1, -1 }; var seen = new HashSet<Rendering.VertexAttribute>();
            foreach (var a in attributes)
            {
                if (a.stream < 0 || a.stream > 3) throw new ArgumentException("Invalid vertex attribute stream " + a.stream);
                if (a.dimension < 1 || a.dimension > 4) throw new ArgumentException("Invalid vertex attribute dimension " + a.dimension + " for " + a.attribute);
                int sz = HeadlessMesh.FormatSize(a.format) * a.dimension;
                if (sz % 4 != 0) throw new ArgumentException("Invalid vertex attribute format+dimension value (" + a.format + " x " + a.dimension + ", data size must be multiple of 4)");
                if (!seen.Add(a.attribute)) throw new ArgumentException("Duplicate vertex attribute " + a.attribute);
                if ((int)a.attribute < last[a.stream]) throw new ArgumentException("Vertex attributes must be specified in VertexAttribute order (" + a.attribute + " after a later attribute)");
                last[a.stream] = (int)a.attribute;
                st[a.stream] += sz;
            }
            if (!seen.Contains(Rendering.VertexAttribute.Position)) throw new ArgumentException("Mesh vertex layout must contain a Position attribute");
            layout = (Rendering.VertexAttributeDescriptor[])attributes.Clone(); strides = st; vcount = vertexCount;
            vdata = new byte[4][]; for (int s = 0; s < 4; s++) if (st[s] > 0) vdata[s] = new byte[st[s] * vertexCount];
            legacyVerts = null;
        }
        void SetV(byte[] bytes, int esize, int meshBufferStart, int count, int stream, string api)
        {
            if (stream < 0 || stream > 3 || strides[stream] == 0) throw new ArgumentOutOfRangeException("stream", api + ": stream " + stream + " is not part of the vertex layout");
            long end = ((long)meshBufferStart + count) * esize, size = (long)strides[stream] * vcount;
            if (meshBufferStart < 0 || end > size) throw new ArgumentOutOfRangeException("meshBufferStart", "Mesh '" + name + "' " + api + ": data range (" + meshBufferStart + "+" + count + ")*" + esize + " bytes exceeds the vertex buffer size " + size);
            Buffer.BlockCopy(bytes, 0, vdata[stream], meshBufferStart * esize, bytes.Length);
            HeadlessMesh.totalUploads++; HeadlessMesh.totalVertexBytes += bytes.Length;
            if (stream == 0) CheckPositions(api);
        }
        void CheckPositions(string api)
        {
            if (layout.Length == 0 || layout[0].attribute != Rendering.VertexAttribute.Position || layout[0].format != Rendering.VertexAttributeFormat.Float32 || layout[0].stream != 0) return;
            var b = vdata[0]; int st = strides[0];
            for (int v = 0; v < vcount; v++)
                for (int k = 0; k < layout[0].dimension; k++)
                {
                    float f = BitConverter.ToSingle(b, v * st + k * 4);
                    if (float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) > 1e7f) { HeadlessHost.Problem("Mesh '" + name + "' " + api + ": bad vertex position component " + f + " at vertex " + v + "/" + vcount); return; }
                }
        }
        static void RangeArgs(int len, int dataStart, int count)
        {
            if (dataStart < 0 || count < 0 || (long)dataStart + count > len) throw new ArgumentOutOfRangeException("dataStart", "Bad start/count arguments (dataStart:" + dataStart + " count:" + count + " length:" + len + ")");
        }
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { M("SetVertexBufferData"); if (data == null) throw new ArgumentNullException("data"); RangeArgs(data.Length, dataStart, count); SetV(HeadlessMesh.ToBytes(data, dataStart, count), Marshal.SizeOf(typeof(T)), meshBufferStart, count, stream, "SetVertexBufferData"); }
        public void SetVertexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { SetVertexBufferData(data.ToArray(), dataStart, meshBufferStart, count, stream, flags); }
        public void SetVertexBufferData<T>(Unity.Collections.NativeArray<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { SetVertexBufferData(data.ToArray(), dataStart, meshBufferStart, count, stream, flags); }
        public void SetIndexBufferParams(int indexCount, Rendering.IndexFormat format)
        {
            M("SetIndexBufferParams");
            if (indexCount < 0) throw new ArgumentOutOfRangeException("indexCount");
            icount = indexCount; ifmt = format; idata = new byte[indexCount * (format == Rendering.IndexFormat.UInt16 ? 2 : 4)];
        }
        void SetI(byte[] bytes, int esize, int meshBufferStart, int count)
        {
            long end = ((long)meshBufferStart + count) * esize;
            if (meshBufferStart < 0 || end > idata.Length) throw new ArgumentOutOfRangeException("meshBufferStart", "Mesh '" + name + "' SetIndexBufferData: data range (" + meshBufferStart + "+" + count + ")*" + esize + " bytes exceeds the index buffer size " + idata.Length + " (" + ifmt + ")");
            Buffer.BlockCopy(bytes, 0, idata, meshBufferStart * esize, bytes.Length);
        }
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { M("SetIndexBufferData"); if (data == null) throw new ArgumentNullException("data"); RangeArgs(data.Length, dataStart, count); SetI(HeadlessMesh.ToBytes(data, dataStart, count), Marshal.SizeOf(typeof(T)), meshBufferStart, count); }
        public void SetIndexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { SetIndexBufferData(data.ToArray(), dataStart, meshBufferStart, count, flags); }
        public void SetIndexBufferData<T>(Unity.Collections.NativeArray<T> data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct
        { SetIndexBufferData(data.ToArray(), dataStart, meshBufferStart, count, flags); }
        public void SetSubMesh(int index, Rendering.SubMeshDescriptor desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default)
        {
            M("SetSubMesh");
            if (index < 0 || index >= subs.Count) throw new ArgumentOutOfRangeException("index", "Mesh '" + name + "': specified sub mesh " + index + " is out of range. Must be less than subMeshCount (" + subs.Count + ").");
            if (desc.indexStart < 0 || desc.indexCount < 0 || (long)desc.indexStart + desc.indexCount > icount) throw new ArgumentException("Mesh '" + name + "': bad submesh index range (" + desc.indexStart + "+" + desc.indexCount + " > index count " + icount + ")");
            {
                // native side only validates without DontValidateIndices; the harness always checks (out-of-range indices = GPU garbage)
                var ix = GetIdx(desc.indexStart, desc.indexCount);
                foreach (var i in ix)
                    if (i + desc.baseVertex < 0 || i + desc.baseVertex >= vcount)
                    {
                        string msg = "Mesh '" + name + "': submesh " + index + " index " + i + " (+base " + desc.baseVertex + ") out of range of vertex count " + vcount;
                        if ((flags & Rendering.MeshUpdateFlags.DontValidateIndices) == 0) throw new ArgumentException(msg);
                        HeadlessHost.Problem(msg); break;
                    }
                int mod = desc.topology == MeshTopology.Triangles ? 3 : desc.topology == MeshTopology.Lines ? 2 : desc.topology == MeshTopology.Quads ? 4 : 1;
                if (desc.indexCount % mod != 0) HeadlessHost.Problem("Mesh '" + name + "': submesh " + index + " index count " + desc.indexCount + " not a multiple of " + mod + " for " + desc.topology);
            }
            subs[index] = desc;
        }
        public void SetSubMeshes(Rendering.SubMeshDescriptor[] desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { subMeshCount = desc.Length; for (int i = 0; i < desc.Length; i++) SetSubMesh(i, desc[i], flags); }
        public void SetSubMeshes(Rendering.SubMeshDescriptor[] desc, int start, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { subMeshCount = count; for (int i = 0; i < count; i++) SetSubMesh(i, desc[start + i], flags); }
        public void SetSubMeshes(List<Rendering.SubMeshDescriptor> desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { SetSubMeshes(desc.ToArray(), flags); }
        public void SetSubMeshes(List<Rendering.SubMeshDescriptor> desc, int start, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { SetSubMeshes(desc.ToArray(), start, count, flags); }
        public Rendering.SubMeshDescriptor GetSubMesh(int index) { M("GetSubMesh"); if (index < 0 || index >= subs.Count) throw new IndexOutOfRangeException("Specified sub mesh is out of range. Must be less than subMeshCount."); return subs[index]; }
        public void RecalculateBounds() { M("RecalculateBounds"); }
        /// <summary>Harness access: float component k of vertex v at byte offset off in stream 0.</summary>
        public float ReadFloat(int v, int off) { return BitConverter.ToSingle(vdata[0], v * strides[0] + off); }
        public int VertexStride { get { return strides[0]; } }
    }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RenderingPath { UsePlayerSettings = -1, VertexLit = 0, Forward = 1, DeferredLighting = 2, DeferredShading = 3 }
    [Flags] public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }
    public sealed class Camera : Behaviour
    {
        internal readonly List<Rendering.CommandBuffer> buffers = new List<Rendering.CommandBuffer>();
        public static Camera main { get { return null; } }
        public static Camera[] allCameras { get { var l = new List<Camera>(); foreach (var c in HeadlessHost.components) { var k = c as Camera; if (k != null && !k.__destroyed && k.enabled) l.Add(k); } return l.ToArray(); } }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public int cullingMask { get; set; }
        public RenderingPath renderingPath { get; set; }
        public bool allowHDR { get; set; }
        public bool allowMSAA { get; set; }
        public bool useOcclusionCulling { get; set; }
        public DepthTextureMode depthTextureMode { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public float fieldOfView { get; set; }
        public float depth { get; set; }
        public float aspect { get; set; }
        public Rect rect { get; set; }
        public int pixelWidth { get { return targetTexture != null ? targetTexture.width : Screen.width; } }
        public int pixelHeight { get { return targetTexture != null ? targetTexture.height : Screen.height; } }
        public RenderTexture targetTexture { get; set; }
        Matrix4x4 w2c = Matrix4x4.identity, proj = Matrix4x4.identity;
        public Matrix4x4 worldToCameraMatrix { get { return w2c; } set { if (value.HasNaN()) HeadlessHost.Problem("Camera.worldToCameraMatrix contains NaN/Inf"); w2c = value; } }
        public Matrix4x4 projectionMatrix { get { return proj; } set { if (value.HasNaN()) HeadlessHost.Problem("Camera.projectionMatrix contains NaN/Inf"); proj = value; } }
        public void ResetWorldToCameraMatrix() { }
        public void ResetProjectionMatrix() { }
        public void AddCommandBuffer(Rendering.CameraEvent evt, Rendering.CommandBuffer cb) { if (cb == null) throw new ArgumentNullException("buffer"); buffers.Add(cb); }
        public void RemoveCommandBuffer(Rendering.CameraEvent evt, Rendering.CommandBuffer cb) { buffers.Remove(cb); }
        public void RemoveAllCommandBuffers() { buffers.Clear(); }
        public void Render() { }
    }
    public static class GL
    {
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) { return proj; }
        public static bool invertCulling { get; set; }
    }
    public static class Graphics
    {
        public static void ExecuteCommandBuffer(Rendering.CommandBuffer cb) { cb.Execute(); }
        public static void Blit(Texture src, RenderTexture dst) { }
        public static void DrawMeshNow(Mesh m, Matrix4x4 mat, int submesh) { }
    }
    public sealed class QualitySettings
    {
        public static int antiAliasing { get; set; }
        public static int vSyncCount { get; set; }
        public static int masterTextureLimit { get; set; }
    }
    public sealed class SystemInfo
    {
        public static int systemMemorySize { get { return 16384; } }
        public static int graphicsMemorySize { get { return 4096; } }
        public static int processorCount { get { return Environment.ProcessorCount; } }
        public static bool supports2DArrayTextures { get { return true; } }
        public static string deviceModel { get { return "headless"; } }
        public static Rendering.GraphicsDeviceType graphicsDeviceType { get { return Rendering.GraphicsDeviceType.Direct3D11; } }
        public static bool usesReversedZBuffer { get { return true; } }
    }
    public enum RuntimePlatform { WindowsPlayer = 2, Android = 11, IPhonePlayer = 8, WebGLPlayer = 17 }
    public sealed class Application
    {
        public static bool focused = true;
        public static bool isMobilePlatform { get { return false; } }
        public static bool isFocused { get { return focused; } }
        public static bool isPlaying { get { return true; } }
        public static bool isEditor { get { return false; } }
        public static bool runInBackground { get; set; }
        public static int targetFrameRate { get; set; }
        public static string persistentDataPath { get { HeadlessHost.MainOnly("Application.persistentDataPath"); return HeadlessHost.persistentDataPath; } }
        public static string version { get { return "headless"; } }
        public static RuntimePlatform platform { get { return RuntimePlatform.WindowsPlayer; } }
        public static void Quit() { }
        public static void OpenURL(string u) { }
    }
    public sealed class SleepTimeout { public const int NeverSleep = -1; public const int SystemSetting = -2; }
    public enum ScreenOrientation { Portrait = 1, LandscapeLeft = 3, AutoRotation = 5 }
    public sealed class Screen
    {
        public static int width { get { HeadlessHost.MainOnly("Screen.width"); return HeadlessHost.screenW; } }
        public static int height { get { HeadlessHost.MainOnly("Screen.height"); return HeadlessHost.screenH; } }
        public static float dpi { get { return 96; } }
        public static int sleepTimeout { get; set; }
        public static bool fullScreen { get; set; }
        public static Rect safeArea { get { return new Rect(0, 0, HeadlessHost.screenW, HeadlessHost.screenH); } }
        public static ScreenOrientation orientation { get; set; }
    }
    public enum CursorLockMode { None, Locked, Confined }
    public class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch
    {
        public int fingerId { get; set; }
        public Vector2 position { get; set; }
        public Vector2 deltaPosition { get; set; }
        public TouchPhase phase { get; set; }
        public int tapCount { get; set; }
    }
    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32,
        Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        LeftBracket = 91, Backslash = 92, RightBracket = 93,
        A = 97, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Delete = 127, UpArrow = 273, DownArrow, RightArrow, LeftArrow,
        F1 = 282, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306, RightAlt = 307, LeftAlt = 308,
        Mouse0 = 323, Mouse1, Mouse2,
    }
    public class Input
    {
        static void Chk() { HeadlessHost.MainOnly("Input"); }
        public static bool GetKey(KeyCode k) { Chk(); return HeadlessHost.held.Contains(k); }
        public static bool GetKeyDown(KeyCode k) { Chk(); return HeadlessHost.down.Contains(k); }
        public static bool GetKeyUp(KeyCode k) { Chk(); return HeadlessHost.up.Contains(k); }
        public static bool GetMouseButton(int b) { Chk(); return HeadlessHost.mHeld[b]; }
        public static bool GetMouseButtonDown(int b) { Chk(); return HeadlessHost.mDown[b]; }
        public static bool GetMouseButtonUp(int b) { Chk(); return HeadlessHost.mUp[b]; }
        public static float GetAxis(string n) { return GetAxisRaw(n); }
        public static float GetAxisRaw(string n)
        {
            Chk();
            if (n == "Mouse X") return HeadlessHost.axisMouseX;
            if (n == "Mouse Y") return HeadlessHost.axisMouseY;
            if (n == "Horizontal" || n == "Vertical" || n == "Mouse ScrollWheel") return 0;
            throw new ArgumentException("Input Axis " + n + " is not setup.");
        }
        public static Vector3 mousePosition { get { return new Vector3(HeadlessHost.mouseGUI.x, HeadlessHost.screenH - HeadlessHost.mouseGUI.y, 0); } }
        public static Vector2 mouseScrollDelta { get { return HeadlessHost.scroll; } }
        public static bool touchSupported { get { return false; } }
        public static int touchCount { get { return 0; } }
        public static Touch GetTouch(int i) { throw new ArgumentException("Index out of bounds."); }
        public static Touch[] touches { get { return new Touch[0]; } }
        public static bool multiTouchEnabled { get; set; }
        public static bool anyKeyDown { get { return HeadlessHost.down.Count > 0; } }
        public static string inputString { get { return ""; } }
    }
    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6, Repaint = 7, Layout = 8, Used = 12, ContextClick = 16 }
    public sealed class Event
    {
        internal static Event s_current;
        internal Vector2 rawPos;
        public static Event current { get { return s_current; } }
        public EventType type { get; set; }
        /// <summary>Like IMGUI: in the space of the current GUI.matrix and clip/scroll/group stack.</summary>
        public Vector2 mousePosition { get { return GUI.ToLocal(rawPos); } set { rawPos = GUI.ToScreen(value); } }
        public Vector2 delta { get; set; }
        public int button { get; set; }
        public int clickCount { get; set; }
        public KeyCode keyCode { get; set; }
        public char character { get; set; }
        public bool shift { get; set; }
        public bool control { get; set; }
        public bool alt { get; set; }
        public bool isMouse { get { return type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag || type == EventType.MouseMove; } }
        public bool isKey { get { return type == EventType.KeyDown || type == EventType.KeyUp; } }
        public void Use()
        {
            if (type == EventType.Repaint || type == EventType.Layout) { Debug.LogError("Event.Use() should not be called for events of type " + type); return; }
            type = EventType.Used;
        }
        public EventType GetTypeForControl(int id) { return type; }
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum TextClipping { Overflow, Clip }
    public class GUIStyleState { public Color textColor { get; set; } public Texture2D background { get; set; } internal GUIStyleState Copy() { return new GUIStyleState { textColor = textColor, background = background }; } }
    public class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; }
        public int left { get; set; } public int right { get; set; } public int top { get; set; } public int bottom { get; set; }
        internal RectOffset Copy() { return new RectOffset(left, right, top, bottom); }
    }
    public class Font : Object { }
    public sealed class GUIStyle
    {
        public GUIStyle() { normal = new GUIStyleState(); hover = new GUIStyleState(); active = new GUIStyleState(); focused = new GUIStyleState(); onNormal = new GUIStyleState(); onHover = new GUIStyleState(); onActive = new GUIStyleState(); padding = new RectOffset(); margin = new RectOffset(); border = new RectOffset(); }
        public GUIStyle(GUIStyle o)
        {
            if (o == null) throw new NullReferenceException("GUIStyle copy constructor: other is null");
            fontSize = o.fontSize; fontStyle = o.fontStyle; alignment = o.alignment; richText = o.richText; wordWrap = o.wordWrap; clipping = o.clipping; font = o.font;
            normal = o.normal.Copy(); hover = o.hover.Copy(); active = o.active.Copy(); focused = o.focused.Copy(); onNormal = o.onNormal.Copy(); onHover = o.onHover.Copy(); onActive = o.onActive.Copy();
            padding = o.padding.Copy(); margin = o.margin.Copy(); border = o.border.Copy(); fixedHeight = o.fixedHeight; fixedWidth = o.fixedWidth;
        }
        static GUIStyle _none;
        public static GUIStyle none { get { return _none ?? (_none = new GUIStyle()); } }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool richText { get; set; }
        public bool wordWrap { get; set; }
        public TextClipping clipping { get; set; }
        public Font font { get; set; }
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public GUIStyleState focused { get; set; }
        public GUIStyleState onNormal { get; set; }
        public GUIStyleState onHover { get; set; }
        public GUIStyleState onActive { get; set; }
        public RectOffset padding { get; set; }
        public RectOffset margin { get; set; }
        public RectOffset border { get; set; }
        public float fixedHeight { get; set; }
        public float fixedWidth { get; set; }
        static string Strip(string t) { return t == null ? "" : System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", ""); }
        public Vector2 CalcSize(GUIContent c) { int fs = fontSize > 0 ? fontSize : 13; var t = Strip(c != null ? c.text : ""); int lines = 1; foreach (var ch in t) if (ch == '\n') lines++; return new Vector2(t.Length * fs * 0.55f + padding.left + padding.right, lines * fs * 1.3f + padding.top + padding.bottom); }
        public float CalcHeight(GUIContent c, float width) { int fs = fontSize > 0 ? fontSize : 13; var w = CalcSize(c).x; int lines = wordWrap && width > 0 ? Math.Max(1, (int)Math.Ceiling(w / width)) : 1; return lines * fs * 1.3f; }
    }
    public class GUIContent
    {
        public GUIContent() { text = ""; }
        public GUIContent(string text) { this.text = text; }
        public GUIContent(string text, string tooltip) { this.text = text; }
        public GUIContent(Texture image) { text = ""; }
        public string text { get; set; }
        public static GUIContent none { get { return new GUIContent(); } }
    }
    public sealed class GUISkin : Object
    {
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
        public GUIStyle box { get; set; }
        public GUIStyle textField { get; set; }
        public GUIStyle toggle { get; set; }
        public GUIStyle window { get; set; }
        public GUIStyle horizontalSlider { get; set; }
        public GUIStyle horizontalSliderThumb { get; set; }
        public GUIStyle verticalScrollbar { get; set; }
        public GUIStyle scrollView { get; set; }
        public Font font { get; set; }
    }
    public class GUI
    {
        static GUISkin _skin;
        static GUISkin DefaultSkin()
        {
            Func<int, GUIStyle> st = fs => new GUIStyle { fontSize = fs, normal = new GUIStyleState { textColor = new Color(0.9f, 0.9f, 0.9f, 1) } };
            return new GUISkin { label = st(13), button = st(13), box = st(13), textField = st(13), toggle = st(13), window = st(13), horizontalSlider = st(0), horizontalSliderThumb = st(0), verticalScrollbar = st(0), scrollView = st(0) };
        }
        static void G(string api) { HeadlessHost.MainOnly("GUI." + api); if (Event.s_current == null) throw new ArgumentException("You can only call GUI functions from inside OnGUI."); }
        public static GUISkin skin { get { G("skin"); return _skin ?? (_skin = DefaultSkin()); } set { G("skin"); _skin = value; } }
        public static Color color { get; set; }
        public static Color backgroundColor { get; set; }
        public static Color contentColor { get; set; }
        public static bool enabled { get; set; }
        public static bool changed { get; set; }
        public static int depth { get; set; }
        static Matrix4x4 _m = Matrix4x4.identity;
        public static Matrix4x4 matrix { get { return _m; } set { if (value.HasNaN()) HeadlessHost.Problem("GUI.matrix NaN"); _m = value; } }
        // clip stack (scroll views / groups): offset of the local origin in pre-matrix GUI space
        struct Clip { public Vector2 off; public Rect rect; public bool scroll; }
        static readonly List<Clip> clips = new List<Clip>();
        internal static void ResetPass() { clips.Clear(); _m = Matrix4x4.identity; enabled = true; color = Color.white; changed = false; }
        internal static void CheckBalanced() { if (clips.Count != 0) { HeadlessHost.Problem("GUI clip stack unbalanced at end of OnGUI (" + clips.Count + " open BeginScrollView/BeginGroup)"); clips.Clear(); } }
        static Vector2 Off() { var o = Vector2.zero; foreach (var c in clips) o += c.off; return o; }
        internal static Vector2 ToLocal(Vector2 screen)
        {
            float sx = _m.m00 != 0 ? _m.m00 : 1, sy = _m.m11 != 0 ? _m.m11 : 1;
            var p = new Vector2((screen.x - _m.m03) / sx, (screen.y - _m.m13) / sy);
            return p - Off();
        }
        internal static Vector2 ToScreen(Vector2 local) { var p = local + Off(); return new Vector2(p.x * (_m.m00 != 0 ? _m.m00 : 1) + _m.m03, p.y * (_m.m11 != 0 ? _m.m11 : 1) + _m.m13); }
        static Rect ScreenRect(Rect r) { var a = ToScreen(r.position); var b = ToScreen(new Vector2(r.xMax, r.yMax)); return new Rect(a.x, a.y, b.x - a.x, b.y - a.y); }
        static bool InClip(Vector2 local)
        {
            // a point is only interactive when inside every enclosing scroll view / group (in that clip's local space)
            var o = Vector2.zero;
            foreach (var c in clips) { if (!c.rect.Contains(local + Off() - o)) return false; o += c.off; }
            return true;
        }
        static Rect NaNCheck(Rect r, string api) { if (float.IsNaN(r.x + r.y + r.width + r.height)) HeadlessHost.Problem("GUI." + api + " with NaN rect"); return r; }
        static int NextId() { int id = ++HeadlessHost.controlCounter; if (HeadlessHost.nextControlName != null) { HeadlessHost.namedControls[HeadlessHost.nextControlName] = id; HeadlessHost.nextControlName = null; } return id; }
        static bool Hit(Rect r, EventType t)
        {
            var e = Event.s_current;
            if (e == null || e.type != t || !enabled) return false;
            var lp = e.mousePosition;
            return r.Contains(lp) && InClip(lp);
        }
        static bool Clicked(Rect r, string text)
        {
            G("Button"); NaNCheck(r, "Button"); NextId();
            if (HeadlessHost.RecordButtons) HeadlessHost.buttons.Add(new HeadlessHost.ButtonInfo { text = text, screen = ScreenRect(r), enabled = enabled });
            if (Hit(r, EventType.MouseDown)) { Event.s_current.Use(); return false; }
            if (Hit(r, EventType.MouseUp)) { Event.s_current.Use(); changed = true; return true; }
            return false;
        }
        public static void Label(Rect r, string t) { G("Label"); NaNCheck(r, "Label"); }
        public static void Label(Rect r, string t, GUIStyle s) { G("Label"); NaNCheck(r, "Label"); if (s == null) throw new ArgumentNullException("style"); }
        public static void Label(Rect r, GUIContent c, GUIStyle s) { G("Label"); NaNCheck(r, "Label"); }
        public static void Label(Rect r, Texture t) { G("Label"); }
        public static bool Button(Rect r, string t) { return Clicked(r, t); }
        public static bool Button(Rect r, string t, GUIStyle s) { if (s == null) throw new ArgumentNullException("style"); return Clicked(r, t); }
        public static bool Button(Rect r, GUIContent c, GUIStyle s) { return Clicked(r, c != null ? c.text : ""); }
        public static bool Button(Rect r, GUIContent c) { return Clicked(r, c != null ? c.text : ""); }
        public static bool RepeatButton(Rect r, string t, GUIStyle s) { G("RepeatButton"); NextId(); return HeadlessHost.mHeld[0] && enabled && r.Contains(Event.s_current.mousePosition); }
        public static bool Toggle(Rect r, bool v, string t) { return Clicked(r, "[toggle] " + t) ? !v : v; }
        public static bool Toggle(Rect r, bool v, string t, GUIStyle s) { return Clicked(r, "[toggle] " + t) ? !v : v; }
        public static bool Toggle(Rect r, bool v, GUIContent c) { return Clicked(r, "[toggle] " + (c != null ? c.text : "")) ? !v : v; }
        static string Field(Rect r, string t, int max)
        {
            G("TextField"); NaNCheck(r, "TextField"); int id = NextId();
            if (t == null) throw new ArgumentNullException("text");
            if (HeadlessHost.RecordButtons) HeadlessHost.buttons.Add(new HeadlessHost.ButtonInfo { text = "[textfield]", screen = ScreenRect(r), enabled = enabled });
            if (Hit(r, EventType.MouseDown)) { GUIUtility.keyboardControl = id; Event.s_current.Use(); }
            if (GUIUtility.keyboardControl == id && HeadlessHost.typedText != null && Event.s_current.type == EventType.Layout) { t += HeadlessHost.typedText; HeadlessHost.typedText = null; changed = true; }
            if (max > 0 && t.Length > max) t = t.Substring(0, max);
            return t;
        }
        public static string TextField(Rect r, string t) { return Field(r, t, -1); }
        public static string TextField(Rect r, string t, int maxLength) { return Field(r, t, maxLength); }
        public static string TextField(Rect r, string t, GUIStyle s) { return Field(r, t, -1); }
        public static string TextField(Rect r, string t, int maxLength, GUIStyle s) { return Field(r, t, maxLength); }
        public static float HorizontalSlider(Rect r, float v, float a, float b) { return Slider(r, v, a, b); }
        public static float HorizontalSlider(Rect r, float v, float a, float b, GUIStyle s, GUIStyle t) { return Slider(r, v, a, b); }
        static float Slider(Rect r, float v, float a, float b)
        {
            G("HorizontalSlider"); NextId();
            if (HeadlessHost.RecordButtons) HeadlessHost.buttons.Add(new HeadlessHost.ButtonInfo { text = "[slider]", screen = ScreenRect(r), enabled = enabled });
            if (Hit(r, EventType.MouseDown) || Hit(r, EventType.MouseDrag)) { float f = (Event.s_current.mousePosition.x - r.x) / Math.Max(1, r.width); Event.s_current.Use(); changed = true; return a + (b - a) * Mathf.Clamp01(f); }
            return v;
        }
        public static int SelectionGrid(Rect r, int sel, string[] texts, int xCount) { return Grid(r, sel, texts, xCount); }
        public static int SelectionGrid(Rect r, int sel, string[] texts, int xCount, GUIStyle s) { return Grid(r, sel, texts, xCount); }
        public static int Toolbar(Rect r, int sel, string[] texts) { return Grid(r, sel, texts, texts.Length); }
        static int Grid(Rect r, int sel, string[] texts, int xCount)
        {
            G("SelectionGrid"); NextId();
            if (texts.Length == 0) return sel;
            xCount = Math.Max(1, xCount); int rows = (texts.Length + xCount - 1) / xCount; float cw = r.width / xCount, chh = r.height / rows;
            for (int i = 0; i < texts.Length; i++)
            {
                var cr = new Rect(r.x + (i % xCount) * cw, r.y + (i / xCount) * chh, cw, chh);
                if (HeadlessHost.RecordButtons) HeadlessHost.buttons.Add(new HeadlessHost.ButtonInfo { text = "[grid] " + texts[i], screen = ScreenRect(cr), enabled = enabled });
                if (Hit(cr, EventType.MouseUp)) { Event.s_current.Use(); changed = true; return i; }
                if (Hit(cr, EventType.MouseDown)) Event.s_current.Use();
            }
            return sel;
        }
        public static void Box(Rect r, string t) { G("Box"); }
        public static void Box(Rect r, string t, GUIStyle s) { G("Box"); }
        public static void Box(Rect r, GUIContent c, GUIStyle s) { G("Box"); }
        static void Tex(Rect r, Texture t, string api) { G(api); NaNCheck(r, api); if (t == null) Debug.LogError("GUI." + api + ": texture is null"); else t.Alive("GUI." + api); }
        public static void DrawTexture(Rect r, Texture t) { Tex(r, t, "DrawTexture"); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m) { Tex(r, t, "DrawTexture"); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend) { Tex(r, t, "DrawTexture"); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend, float imageAspect) { Tex(r, t, "DrawTexture"); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend, float imageAspect, Color color, float borderWidth, float borderRadius) { Tex(r, t, "DrawTexture"); }
        public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect tc) { Tex(r, t, "DrawTextureWithTexCoords"); }
        public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect tc, bool alphaBlend) { Tex(r, t, "DrawTextureWithTexCoords"); }
        static Vector2 Scroll(Rect pos, Vector2 scroll, Rect view)
        {
            G("BeginScrollView"); NextId();
            float maxX = Math.Max(0, view.width - pos.width), maxY = Math.Max(0, view.height - pos.height);
            var e = Event.s_current;
            if (e.type == EventType.ScrollWheel && pos.Contains(e.mousePosition)) { scroll.y += e.delta.y * 20; e.Use(); }
            scroll = new Vector2(Mathf.Clamp(scroll.x, 0, maxX), Mathf.Clamp(scroll.y, 0, maxY));
            clips.Add(new Clip { off = new Vector2(pos.x - scroll.x, pos.y - scroll.y), rect = pos, scroll = true });
            return scroll;
        }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view) { return Scroll(pos, scroll, view); }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view, bool alwaysH, bool alwaysV) { return Scroll(pos, scroll, view); }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view, GUIStyle h, GUIStyle v) { return Scroll(pos, scroll, view); }
        static void Pop(string api) { G(api); if (clips.Count == 0) { Debug.LogError(api + ": pushing more GUIClips than you are popping. Make sure they are balanced."); return; } clips.RemoveAt(clips.Count - 1); }
        public static void EndScrollView() { Pop("EndScrollView"); }
        public static void EndScrollView(bool handleScrollWheel) { Pop("EndScrollView"); }
        public static void BeginGroup(Rect r) { G("BeginGroup"); clips.Add(new Clip { off = r.position, rect = r }); }
        public static void EndGroup() { Pop("EndGroup"); }
        public static void BeginClip(Rect r) { G("BeginClip"); clips.Add(new Clip { off = r.position, rect = r }); }
        public static void EndClip() { Pop("EndClip"); }
        public static void SetNextControlName(string n) { HeadlessHost.nextControlName = n; }
        public static string GetNameOfFocusedControl() { foreach (var kv in HeadlessHost.namedControls) if (kv.Value == GUIUtility.keyboardControl && kv.Value != 0) return kv.Key; return ""; }
        public static void FocusControl(string n) { int id; GUIUtility.keyboardControl = n != null && HeadlessHost.namedControls.TryGetValue(n, out id) ? id : 0; }
        public static void UnfocusWindow() { }
    }
    public class GUIUtility
    {
        public static int keyboardControl { get; set; }
        public static int hotControl { get; set; }
        public static Vector2 GUIToScreenPoint(Vector2 p) { return GUI.ToScreen(p); }
        public static Vector2 ScreenToGUIPoint(Vector2 p) { return GUI.ToLocal(p); }
        public static int GetControlID(FocusType f) { return ++HeadlessHost.controlCounter; }
        public static void ExitGUI() { throw new ExitGUIException(); }
    }
    public enum FocusType { Passive, Keyboard }
    public sealed class AudioClip : Object
    {
        int n, ch, freq; float[] data;
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
        {
            HeadlessHost.MainOnly("AudioClip.Create");
            if (lengthSamples <= 0) throw new ArgumentException("Length of created clip must be larger than 0");
            if (channels <= 0) throw new ArgumentException("Number of channels in created clip must be greater than 0");
            if (frequency <= 0) throw new ArgumentException("Frequency in created clip must be greater than 0");
            return new AudioClip { name = name, n = lengthSamples, ch = channels, freq = frequency, data = new float[lengthSamples * channels] };
        }
        public bool SetData(float[] d, int offsetSamples)
        {
            Alive("AudioClip.SetData");
            if (d == null) throw new ArgumentNullException("data");
            foreach (var f in d) if (float.IsNaN(f) || float.IsInfinity(f)) { HeadlessHost.Problem("AudioClip '" + name + "' NaN sample"); break; }
            Array.Copy(d, 0, data, Math.Min(offsetSamples * ch, data.Length), Math.Min(d.Length, data.Length - Math.Min(offsetSamples * ch, data.Length)));
            return true;
        }
        public float length { get { return n / (float)freq; } }
        public int samples { get { return n; } }
    }
    public sealed class AudioSource : Behaviour
    {
        AudioClip _clip; double endAt;
        public AudioClip clip { get { return _clip; } set { _clip = value; } }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float spatialBlend { get; set; }
        public float panStereo { get; set; }
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public bool isPlaying { get { return _clip != null && HeadlessHost.realtime < endAt; } }
        public int priority { get; set; }
        public void Play() { Alive("AudioSource.Play"); if (_clip != null) { _clip.Alive("AudioSource.Play clip"); endAt = HeadlessHost.realtime + _clip.length; } }
        public void Stop() { endAt = 0; }
        public void PlayOneShot(AudioClip c) { }
        public void PlayOneShot(AudioClip c, float vol) { }
    }
    public sealed class AudioListener : Behaviour
    {
        public static bool pause { get; set; }
        public static float volume { get; set; }
    }
    public sealed class AudioSettings
    {
        public static int outputSampleRate { get { return 48000; } }
        public static double dspTime { get { return HeadlessHost.realtime; } }
    }
    public sealed class Time
    {
        public static float deltaTime { get { return HeadlessHost.deltaTime; } }
        public static float unscaledDeltaTime { get { return HeadlessHost.deltaTime; } }
        public static float realtimeSinceStartup { get { return (float)HeadlessHost.realtime; } }
        public static double realtimeSinceStartupAsDouble { get { return HeadlessHost.realtime; } }
        public static int frameCount { get { return HeadlessHost.frameCount; } }
    }
    public sealed class PlayerPrefs
    {
        static readonly Dictionary<string, string> d = new Dictionary<string, string>();
        public static string GetString(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
        public static string GetString(string k) { return GetString(k, ""); }
        public static void SetString(string k, string v) { d[k] = v; }
        public static bool HasKey(string k) { return d.ContainsKey(k); }
        public static void DeleteKey(string k) { d.Remove(k); }
        public static void Save() { }
    }
    public sealed class MaterialPropertyBlock
    {
        public void SetFloat(string n, float v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetColor(string n, Color c) { }
        public void SetTexture(string n, Texture t) { }
        public void SetMatrix(string n, Matrix4x4 m) { }
        public void Clear() { }
    }

    /// <summary>Minimal PNG decoder (8-bit gray/RGB/RGBA/gray+alpha, 1-8 bit palette, no interlace). Output rows bottom-up like Unity.</summary>
    public static class HeadlessPng
    {
        static int BE(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }
        public static bool Decode(byte[] f, out int w, out int h, out Color32[] px)
        {
            w = h = 0; px = null;
            if (f.Length < 8 || f[0] != 137 || f[1] != 80 || f[2] != 78 || f[3] != 71) return false;
            int depth = 0, ctype = 0, interlace = 0; byte[] pal = null, trns = null; var idat = new MemoryStream();
            for (int p = 8; p + 8 <= f.Length;)
            {
                int len = BE(f, p); string type = Encoding.ASCII.GetString(f, p + 4, 4); int d0 = p + 8;
                if (type == "IHDR") { w = BE(f, d0); h = BE(f, d0 + 4); depth = f[d0 + 8]; ctype = f[d0 + 9]; interlace = f[d0 + 12]; }
                else if (type == "PLTE") { pal = new byte[len]; Array.Copy(f, d0, pal, 0, len); }
                else if (type == "tRNS") { trns = new byte[len]; Array.Copy(f, d0, trns, 0, len); }
                else if (type == "IDAT") idat.Write(f, d0, len);
                else if (type == "IEND") break;
                p = d0 + len + 4;
            }
            if (w <= 0 || h <= 0 || interlace != 0) return false;
            int chans = ctype == 0 ? 1 : ctype == 2 ? 3 : ctype == 3 ? 1 : ctype == 4 ? 2 : ctype == 6 ? 4 : 0;
            if (chans == 0 || (depth != 8 && !(ctype == 3 && depth <= 8))) return false;
            int bpp = Math.Max(1, chans * depth / 8), stride = (w * chans * depth + 7) / 8;
            var z = idat.ToArray(); var raw = new byte[(stride + 1) * h];
            using (var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress))
            {
                int got = 0; while (got < raw.Length) { int n = ds.Read(raw, got, raw.Length - got); if (n <= 0) break; got += n; }
                if (got < raw.Length) return false;
            }
            var cur = new byte[stride]; var prev = new byte[stride];
            px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int ft = raw[y * (stride + 1)], ro = y * (stride + 1) + 1;
                for (int i = 0; i < stride; i++)
                {
                    int x = raw[ro + i], a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0, v;
                    switch (ft)
                    {
                        case 0: v = x; break;
                        case 1: v = x + a; break;
                        case 2: v = x + b; break;
                        case 3: v = x + ((a + b) >> 1); break;
                        case 4: { int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c); v = x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c); break; }
                        default: return false;
                    }
                    cur[i] = (byte)v;
                }
                int orow = (h - 1 - y) * w;
                for (int xx = 0; xx < w; xx++)
                {
                    Color32 col;
                    switch (ctype)
                    {
                        case 6: col = new Color32(cur[xx * 4], cur[xx * 4 + 1], cur[xx * 4 + 2], cur[xx * 4 + 3]); break;
                        case 2: col = new Color32(cur[xx * 3], cur[xx * 3 + 1], cur[xx * 3 + 2], 255); break;
                        case 0: col = new Color32(cur[xx], cur[xx], cur[xx], 255); break;
                        case 4: col = new Color32(cur[xx * 2], cur[xx * 2], cur[xx * 2], cur[xx * 2 + 1]); break;
                        default:
                            {
                                int bit = xx * depth, idx = (cur[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                                col = pal != null && idx * 3 + 2 < pal.Length ? new Color32(pal[idx * 3], pal[idx * 3 + 1], pal[idx * 3 + 2], trns != null && idx < trns.Length ? trns[idx] : (byte)255) : new Color32(0, 0, 0, 255);
                                break;
                            }
                    }
                    px[orow + xx] = col;
                }
                var t = prev; prev = cur; cur = t;
            }
            return true;
        }
    }
}
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    [Flags] public enum MeshUpdateFlags { Default = 0, DontValidateIndices = 1, DontResetBoneBounds = 2, DontNotifyMeshUsers = 4, DontRecalculateBounds = 8 }
    public enum VertexAttribute { Position = 0, Normal, Tangent, Color, TexCoord0, TexCoord1, TexCoord2, TexCoord3, TexCoord4, TexCoord5, TexCoord6, TexCoord7, BlendWeight, BlendIndices }
    public enum VertexAttributeFormat { Float32 = 0, Float16 = 1, UNorm8 = 2, SNorm8 = 3, UNorm16 = 4, SNorm16 = 5, UInt8 = 6, SInt8 = 7, UInt16 = 8, SInt16 = 9, UInt32 = 10, SInt32 = 11 }
    public struct VertexAttributeDescriptor
    {
        public VertexAttributeDescriptor(VertexAttribute attribute = VertexAttribute.Position, VertexAttributeFormat format = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0) : this() { this.attribute = attribute; this.format = format; this.dimension = dimension; this.stream = stream; }
        public VertexAttribute attribute { get; set; }
        public VertexAttributeFormat format { get; set; }
        public int dimension { get; set; }
        public int stream { get; set; }
    }
    public struct SubMeshDescriptor
    {
        public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles) : this() { this.indexStart = indexStart; this.indexCount = indexCount; this.topology = topology; baseVertex = 0; firstVertex = 0; vertexCount = 0; bounds = new Bounds(); }
        public int indexStart { get; set; }
        public int indexCount { get; set; }
        public MeshTopology topology { get; set; }
        public int baseVertex { get; set; }
        public int firstVertex { get; set; }
        public int vertexCount { get; set; }
        public Bounds bounds { get; set; }
    }
    public enum BlendMode { Zero = 0, One = 1, DstColor = 2, SrcColor = 3, OneMinusDstColor = 4, SrcAlpha = 5, OneMinusSrcColor = 6, DstAlpha = 7, OneMinusDstAlpha = 8, SrcAlphaSaturate = 9, OneMinusSrcAlpha = 10 }
    public enum CompareFunction { Disabled = 0, Never = 1, Less = 2, Equal = 3, LessEqual = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8 }
    public enum CullMode { Off = 0, Front = 1, Back = 2 }
    public enum CameraEvent { BeforeDepthTexture = 0, AfterDepthTexture, BeforeDepthNormalsTexture, AfterDepthNormalsTexture, BeforeGBuffer, AfterGBuffer, BeforeLighting, AfterLighting, BeforeFinalPass, AfterFinalPass, BeforeForwardOpaque, AfterForwardOpaque, BeforeImageEffectsOpaque, AfterImageEffectsOpaque, BeforeSkybox, AfterSkybox, BeforeForwardAlpha, AfterForwardAlpha, BeforeImageEffects, AfterImageEffects, AfterEverything }
    public enum BuiltinRenderTextureType { CameraTarget = 2, CurrentActive = 1 }
    public enum GraphicsDeviceType { OpenGLES3 = 11, Direct3D11 = 2, Metal = 16, Vulkan = 21, OpenGLCore = 17 }
    public struct RenderTargetIdentifier
    {
        internal Texture tex; internal int id; internal BuiltinRenderTextureType bt;
        public RenderTargetIdentifier(BuiltinRenderTextureType t) : this() { bt = t; }
        public RenderTargetIdentifier(Texture t) : this() { tex = t; }
        public RenderTargetIdentifier(int nameId) : this() { id = nameId; }
        public static implicit operator RenderTargetIdentifier(BuiltinRenderTextureType t) { return new RenderTargetIdentifier(t); }
        public static implicit operator RenderTargetIdentifier(Texture t) { return new RenderTargetIdentifier(t); }
        public static implicit operator RenderTargetIdentifier(int id) { return new RenderTargetIdentifier(id); }
    }
    /// <summary>Records draws; Execute() validates them against the meshes' current state (what the GPU would draw).</summary>
    public class CommandBuffer : IDisposable
    {
        struct Draw { public Mesh mesh; public int sub; public Material mat; public Matrix4x4 m; }
        readonly List<Draw> draws = new List<Draw>();
        public string name { get; set; }
        public int sizeInBytes { get { return draws.Count * 64; } }
        public int DrawCount { get { return draws.Count; } }
        public void Clear() { draws.Clear(); }
        public void Dispose() { draws.Clear(); }
        public void Release() { draws.Clear(); }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material) { DrawMesh(mesh, matrix, material, 0, -1); }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex) { DrawMesh(mesh, matrix, material, submeshIndex, -1); }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass) { DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, null); }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass, MaterialPropertyBlock properties)
        {
            if (mesh == null) throw new ArgumentNullException("mesh");
            if (material == null) throw new ArgumentNullException("material");
            if (submeshIndex < 0 || submeshIndex >= mesh.subMeshCount) { Debug.LogError("CommandBuffer.DrawMesh: submesh index " + submeshIndex + " out of range for mesh '" + mesh.name + "' (" + mesh.subMeshCount + " submeshes)"); return; }
            if (matrix.HasNaN()) HeadlessHost.Problem("CommandBuffer.DrawMesh: NaN matrix for mesh '" + mesh.name + "'");
            draws.Add(new Draw { mesh = mesh, sub = submeshIndex, mat = material, m = matrix });
        }
        internal void Execute()
        {
            foreach (var d in draws)
            {
                if (d.mesh.__destroyed) { HeadlessHost.Problem("draw of destroyed mesh '" + d.mesh.name + "' recorded in '" + name + "'"); continue; }
                if (d.sub >= d.mesh.subs.Count) { HeadlessHost.Problem("draw of mesh '" + d.mesh.name + "' submesh " + d.sub + " that no longer exists"); continue; }
                var s = d.mesh.subs[d.sub];
                if (s.indexCount > 0 && d.mesh.vcount == 0) HeadlessHost.Problem("draw of mesh '" + d.mesh.name + "' with indices but no vertices");
                HeadlessHost.frameDraws++; HeadlessHost.frameTris += s.topology == MeshTopology.Triangles ? s.indexCount / 3 : 0;
            }
        }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) { }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, float depth) { }
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 proj) { if (view.HasNaN() || proj.HasNaN()) HeadlessHost.Problem("SetViewProjectionMatrices NaN"); }
        public void SetViewMatrix(Matrix4x4 view) { }
        public void SetProjectionMatrix(Matrix4x4 proj) { }
        static void F(string n, float v) { if (float.IsNaN(v) || float.IsInfinity(v)) HeadlessHost.Problem("CommandBuffer global " + n + " = " + v); }
        public void SetGlobalVector(string n, Vector4 v) { F(n, v.x + v.y + v.z + v.w); }
        public void SetGlobalVector(int n, Vector4 v) { F("#" + n, v.x + v.y + v.z + v.w); }
        public void SetGlobalFloat(string n, float v) { F(n, v); }
        public void SetGlobalFloat(int n, float v) { F("#" + n, v); }
        public void SetGlobalColor(string n, Color c) { }
        public void SetGlobalMatrix(string n, Matrix4x4 m) { if (m.HasNaN()) HeadlessHost.Problem("CommandBuffer global matrix " + n + " NaN"); }
        public void SetGlobalTexture(string n, RenderTargetIdentifier t) { if (t.tex != null) t.tex.Alive("SetGlobalTexture " + n); }
        public void SetGlobalTexture(int n, RenderTargetIdentifier t) { }
        public void SetRenderTarget(RenderTargetIdentifier rt) { }
        public void Blit(Texture source, RenderTargetIdentifier dest) { }
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest) { }
        public void Blit(Texture source, RenderTargetIdentifier dest, Material mat) { }
        public void SetInvertCulling(bool invert) { }
        public void EnableShaderKeyword(string k) { }
        public void DisableShaderKeyword(string k) { }
    }
}
namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }
    public enum NativeArrayOptions { UninitializedMemory, ClearMemory }
    public struct NativeArray<T> : IDisposable where T : struct
    {
        T[] a;
        public NativeArray(int length, Allocator al, NativeArrayOptions o = NativeArrayOptions.ClearMemory) : this() { a = new T[length]; }
        public NativeArray(T[] array, Allocator al) : this() { a = (T[])array.Clone(); }
        public int Length { get { return a != null ? a.Length : 0; } }
        public T this[int i] { get { return a[i]; } set { a[i] = value; } }
        public bool IsCreated { get { return a != null; } }
        public void Dispose() { a = null; }
        public void CopyFrom(T[] src) { if (src.Length != a.Length) throw new ArgumentException("source and destination length must be the same"); Array.Copy(src, a, a.Length); }
        public T[] ToArray() { return a != null ? (T[])a.Clone() : new T[0]; }
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct
        {
            var b = UnityEngine.HeadlessMesh.ToBytes(a, 0, a.Length); int us = Marshal.SizeOf(typeof(U)); var r = new U[b.Length / us];
            var h = GCHandle.Alloc(r, GCHandleType.Pinned); try { Marshal.Copy(b, 0, h.AddrOfPinnedObject(), r.Length * us); } finally { h.Free(); }
            var n = new NativeArray<U>(); n.a = r; return n;
        }
    }
}
