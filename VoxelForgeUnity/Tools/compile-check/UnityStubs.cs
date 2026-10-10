// Minimal UnityEngine API surface (signatures mirror Unity 2022.3) used only for an offline compile check
// of the runtime scripts with Mono mcs (see check.sh). Not part of the Unity project (outside Assets/).
using System;
using System.Collections.Generic;
#pragma warning disable 0067, 0649, 0169, 0414
namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object o) { }
        public static void Destroy(Object o, float t) { }
        public static void DestroyImmediate(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T FindObjectOfType<T>() where T : Object { return null; }
        public static T[] FindObjectsOfType<T>() where T : Object { return null; }
        public static T Instantiate<T>(T o) where T : Object { return o; }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
        public int GetInstanceID() { return 0; }
    }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61, DontSave = 52 }
    public class Component : Object
    {
        public GameObject gameObject { get { return null; } }
        public Transform transform { get { return null; } }
        public T GetComponent<T>() { return default(T); }
    }
    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public void SetParent(Transform p) { }
    }
    public class Behaviour : Component { public bool enabled { get; set; } public bool isActiveAndEnabled { get { return true; } } }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(System.Collections.IEnumerator e) { return null; }
        public void Invoke(string m, float t) { }
    }
    public class Coroutine { }
    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] comps) { }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
        public Transform transform { get { return null; } }
        public void SetActive(bool v) { }
    }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
        public static void LogException(Exception e) { }
    }
    public struct Mathf
    {
        public const float PI = 3.14159274f;
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Min(params float[] v) { return 0; }
        public static float Max(params float[] v) { return 0; }
        public static float Clamp(float v, float a, float b) { return v; }
        public static int Clamp(int v, int a, int b) { return v; }
        public static float Clamp01(float v) { return v; }
        public static float Lerp(float a, float b, float t) { return a; }
        public static float Abs(float v) { return v; }
        public static int Abs(int v) { return v; }
        public static float Floor(float v) { return v; }
        public static int FloorToInt(float v) { return 0; }
        public static int RoundToInt(float v) { return 0; }
        public static int CeilToInt(float v) { return 0; }
        public static float Round(float v) { return v; }
        public static float Sqrt(float v) { return v; }
        public static float Sin(float v) { return v; }
        public static float Cos(float v) { return v; }
        public static float Pow(float a, float b) { return a; }
        public static ushort FloatToHalf(float v) { return 0; }
        public static float HalfToFloat(ushort v) { return 0; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
        public float magnitude { get { return 0; } }
        public float sqrMagnitude { get { return 0; } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float b) { return a; }
        public static Vector2 operator /(Vector2 a, float b) { return a; }
        public static float Distance(Vector2 a, Vector2 b) { return 0; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(); } }
        public static Vector3 operator *(Vector3 a, float b) { return a; }
        public static Vector3 operator *(float b, Vector3 a) { return a; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public float this[int i] { get { return 0; } set { } }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4(float x, float y, float z) { this.x = x; this.y = y; this.z = z; w = 0; }
        public static Vector4 zero { get { return new Vector4(); } }
        public float this[int i] { get { return 0; } set { } }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public static Color white { get { return new Color(); } }
        public static Color black { get { return new Color(); } }
        public static Color clear { get { return new Color(); } }
        public static Color red { get { return new Color(); } }
        public static Color gray { get { return new Color(); } }
        public static Color yellow { get { return new Color(); } }
        public static Color operator *(Color a, float b) { return a; }
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static implicit operator Vector4(Color c) { return new Vector4(); }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) { return new Color(); }
        public static implicit operator Color32(Color c) { return new Color32(); }
    }
    public struct Rect
    {
        public Rect(float x, float y, float w, float h) : this() { this.x = x; this.y = y; width = w; height = h; }
        public Rect(Vector2 p, Vector2 s) : this() { x = p.x; y = p.y; width = s.x; height = s.y; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float xMin { get; set; }
        public float yMin { get; set; }
        public float xMax { get; set; }
        public float yMax { get; set; }
        public Vector2 center { get; set; }
        public Vector2 position { get; set; }
        public Vector2 size { get; set; }
        public bool Contains(Vector2 p) { return false; }
        public bool Contains(Vector3 p) { return false; }
        public bool Overlaps(Rect r) { return false; }
        public static Rect zero { get { return new Rect(); } }
    }
    public struct Bounds
    {
        public Bounds(Vector3 c, Vector3 s) : this() { center = c; size = s; extents = s; min = c; max = c; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
        public Vector3 extents { get; set; }
        public Vector3 min { get; set; }
        public Vector3 max { get; set; }
        public void SetMinMax(Vector3 a, Vector3 b) { }
    }
    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public float this[int r, int c] { get { return 0; } set { } }
        public float this[int i] { get { return 0; } set { } }
        public static Matrix4x4 identity { get { return new Matrix4x4(); } }
        public static Matrix4x4 Scale(Vector3 v) { return new Matrix4x4(); }
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) { return new Matrix4x4(); }
        public static Matrix4x4 Translate(Vector3 v) { return new Matrix4x4(); }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) { return a; }
        public Matrix4x4 inverse { get { return this; } }
    }

    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, R8 = 63, RG16 = 62, RHalf = 15, RGBAHalf = 17, RFloat = 18 }
    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Default = 7, R8 = 16 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
        public float mipMapBias { get; set; }
        public int mipmapCount { get { return 1; } }
    }
    public sealed class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) { }
        public Texture2D(int w, int h, TextureFormat f, int mipCount, bool linear) { }
        public static Texture2D whiteTexture { get { return null; } }
        public static Texture2D blackTexture { get { return null; } }
        public void SetPixels32(Color32[] c) { }
        public void SetPixels32(Color32[] c, int mip) { }
        public void SetPixels(Color[] c) { }
        public Color32[] GetPixels32() { return null; }
        public Color32[] GetPixels32(int mip) { return null; }
        public Color[] GetPixels() { return null; }
        public Color GetPixel(int x, int y) { return new Color(); }
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixelData<T>(T[] data, int mip, int sourceDataStartIndex = 0) { }
        public Unity.Collections.NativeArray<T> GetPixelData<T>(int mip) where T : struct { return default(Unity.Collections.NativeArray<T>); }
        public Unity.Collections.NativeArray<T> GetRawTextureData<T>() where T : struct { return default(Unity.Collections.NativeArray<T>); }
        public byte[] GetRawTextureData() { return null; }
        public void LoadRawTextureData(byte[] data) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public TextureFormat format { get { return TextureFormat.RGBA32; } }
    }
    public sealed class Texture2DArray : Texture
    {
        public Texture2DArray(int w, int h, int depth, TextureFormat f, bool mip) { }
        public Texture2DArray(int w, int h, int depth, TextureFormat f, bool mip, bool linear) { }
        public Texture2DArray(int w, int h, int depth, TextureFormat f, int mipCount, bool linear) { }
        public int depth { get { return 0; } }
        public void SetPixels32(Color32[] c, int arrayElement, int mip) { }
        public void SetPixels32(Color32[] c, int arrayElement) { }
        public void SetPixels(Color[] c, int arrayElement, int mip) { }
        public void SetPixelData<T>(T[] data, int mip, int element, int sourceDataStartIndex = 0) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public void Apply(bool updateMipmaps) { }
        public void Apply() { }
    }
    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int depth) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f, RenderTextureReadWrite rw) { }
        public int antiAliasing { get; set; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; }
        public bool Create() { return true; }
        public void Release() { }
        public bool IsCreated() { return true; }
        public static RenderTexture active { get; set; }
    }
    public static class ImageConversion
    {
        public static bool LoadImage(this Texture2D tex, byte[] data) { return true; }
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) { return true; }
        public static byte[] EncodeToPNG(this Texture2D tex) { return null; }
    }
    public class TextAsset : Object { public string text { get { return ""; } } public byte[] bytes { get { return null; } } }
    public static class Resources
    {
        public static Object Load(string path) { return null; }
        public static T Load<T>(string path) where T : Object { return null; }
        public static T[] LoadAll<T>(string path) where T : Object { return null; }
    }
    public sealed class Shader : Object
    {
        public static Shader Find(string name) { return null; }
        public static int PropertyToID(string name) { return 0; }
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
        public Material(Shader s) { }
        public Material(Material m) { }
        public Shader shader { get; set; }
        public Texture mainTexture { get; set; }
        public Color color { get; set; }
        public int renderQueue { get; set; }
        public bool enableInstancing { get; set; }
        public void SetFloat(string n, float v) { }
        public void SetFloat(int n, float v) { }
        public void SetInt(string n, int v) { }
        public void SetInteger(string n, int v) { }
        public void SetColor(string n, Color c) { }
        public void SetColor(int n, Color c) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetVector(int n, Vector4 v) { }
        public void SetMatrix(string n, Matrix4x4 m) { }
        public void SetTexture(string n, Texture t) { }
        public void SetTexture(int n, Texture t) { }
        public void EnableKeyword(string k) { }
        public void DisableKeyword(string k) { }
        public bool IsKeywordEnabled(string k) { return false; }
        public bool SetPass(int pass) { return true; }
    }
    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }
    public sealed class Mesh : Object
    {
        public Mesh() { }
        public Rendering.IndexFormat indexFormat { get; set; }
        public Vector3[] vertices { get; set; }
        public int[] triangles { get; set; }
        public Vector2[] uv { get; set; }
        public Color32[] colors32 { get; set; }
        public Bounds bounds { get; set; }
        public int vertexCount { get { return 0; } }
        public int subMeshCount { get; set; }
        public void Clear() { }
        public void Clear(bool keepVertexLayout) { }
        public void MarkDynamic() { }
        public void UploadMeshData(bool markNoLongerReadable) { }
        public void SetTriangles(int[] t, int submesh) { }
        public void SetTriangles(int[] t, int submesh, bool calculateBounds) { }
        public void SetTriangles(List<int> t, int submesh, bool calculateBounds) { }
        public void SetIndices(int[] idx, MeshTopology topo, int submesh) { }
        public void SetIndices(int[] idx, MeshTopology topo, int submesh, bool calculateBounds) { }
        public void SetVertices(List<Vector3> v) { }
        public void SetVertices(Vector3[] v) { }
        public void SetVertexBufferParams(int vertexCount, params Rendering.VertexAttributeDescriptor[] attributes) { }
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetVertexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetVertexBufferData<T>(Unity.Collections.NativeArray<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferParams(int indexCount, Rendering.IndexFormat format) { }
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferData<T>(Unity.Collections.NativeArray<T> data, int dataStart, int meshBufferStart, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetSubMesh(int index, Rendering.SubMeshDescriptor desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { }
        public void SetSubMeshes(Rendering.SubMeshDescriptor[] desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { }
        public void SetSubMeshes(Rendering.SubMeshDescriptor[] desc, int start, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { }
        public void SetSubMeshes(List<Rendering.SubMeshDescriptor> desc, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { }
        public void SetSubMeshes(List<Rendering.SubMeshDescriptor> desc, int start, int count, Rendering.MeshUpdateFlags flags = Rendering.MeshUpdateFlags.Default) { }
        public Rendering.SubMeshDescriptor GetSubMesh(int index) { return new Rendering.SubMeshDescriptor(); }
        public void RecalculateBounds() { }
    }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RenderingPath { UsePlayerSettings = -1, VertexLit = 0, Forward = 1, DeferredLighting = 2, DeferredShading = 3 }
    [Flags] public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }
    public sealed class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
        public static Camera[] allCameras { get { return null; } }
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
        public int pixelWidth { get { return 0; } }
        public int pixelHeight { get { return 0; } }
        public RenderTexture targetTexture { get; set; }
        public Matrix4x4 worldToCameraMatrix { get; set; }
        public Matrix4x4 projectionMatrix { get; set; }
        public void ResetWorldToCameraMatrix() { }
        public void ResetProjectionMatrix() { }
        public void AddCommandBuffer(Rendering.CameraEvent evt, Rendering.CommandBuffer cb) { }
        public void RemoveCommandBuffer(Rendering.CameraEvent evt, Rendering.CommandBuffer cb) { }
        public void RemoveAllCommandBuffers() { }
        public void Render() { }
    }
    public static class GL
    {
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) { return proj; }
        public static bool invertCulling { get; set; }
    }
    public static class Graphics
    {
        public static void ExecuteCommandBuffer(Rendering.CommandBuffer cb) { }
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
        public static int systemMemorySize { get { return 0; } }
        public static int graphicsMemorySize { get { return 0; } }
        public static int processorCount { get { return 1; } }
        public static bool supports2DArrayTextures { get { return true; } }
        public static string deviceModel { get { return ""; } }
        public static Rendering.GraphicsDeviceType graphicsDeviceType { get { return Rendering.GraphicsDeviceType.Direct3D11; } }
        public static bool usesReversedZBuffer { get { return true; } }
    }
    public enum RuntimePlatform { WindowsPlayer = 2, Android = 11, IPhonePlayer = 8, WebGLPlayer = 17 }
    public sealed class Application
    {
        public static bool isMobilePlatform { get { return false; } }
        public static bool isFocused { get { return true; } }
        public static bool isPlaying { get { return true; } }
        public static bool isEditor { get { return false; } }
        public static bool runInBackground { get; set; }
        public static int targetFrameRate { get; set; }
        public static string persistentDataPath { get { return ""; } }
        public static string version { get { return ""; } }
        public static RuntimePlatform platform { get { return RuntimePlatform.WindowsPlayer; } }
        public static void Quit() { }
        public static void OpenURL(string u) { }
    }
    public sealed class SleepTimeout { public const int NeverSleep = -1; public const int SystemSetting = -2; }
    public enum ScreenOrientation { Portrait = 1, LandscapeLeft = 3, AutoRotation = 5 }
    public sealed class Screen
    {
        public static int width { get { return 0; } }
        public static int height { get { return 0; } }
        public static float dpi { get { return 0; } }
        public static int sleepTimeout { get; set; }
        public static bool fullScreen { get; set; }
        public static Rect safeArea { get { return new Rect(); } }
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
        public static bool GetKey(KeyCode k) { return false; }
        public static bool GetKeyDown(KeyCode k) { return false; }
        public static bool GetKeyUp(KeyCode k) { return false; }
        public static bool GetMouseButton(int b) { return false; }
        public static bool GetMouseButtonDown(int b) { return false; }
        public static bool GetMouseButtonUp(int b) { return false; }
        public static float GetAxis(string n) { return 0; }
        public static float GetAxisRaw(string n) { return 0; }
        public static Vector3 mousePosition { get { return new Vector3(); } }
        public static Vector2 mouseScrollDelta { get { return new Vector2(); } }
        public static bool touchSupported { get { return false; } }
        public static int touchCount { get { return 0; } }
        public static Touch GetTouch(int i) { return new Touch(); }
        public static Touch[] touches { get { return null; } }
        public static bool multiTouchEnabled { get; set; }
        public static bool anyKeyDown { get { return false; } }
        public static string inputString { get { return ""; } }
    }
    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6, Repaint = 7, Layout = 8, Used = 12, ContextClick = 16 }
    public sealed class Event
    {
        public static Event current { get { return null; } }
        public EventType type { get; set; }
        public Vector2 mousePosition { get; set; }
        public Vector2 delta { get; set; }
        public int button { get; set; }
        public int clickCount { get; set; }
        public KeyCode keyCode { get; set; }
        public char character { get; set; }
        public bool shift { get; set; }
        public bool control { get; set; }
        public bool alt { get; set; }
        public bool isMouse { get { return false; } }
        public bool isKey { get { return false; } }
        public void Use() { }
        public EventType GetTypeForControl(int id) { return EventType.Layout; }
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum TextClipping { Overflow, Clip }
    public class GUIStyleState { public Color textColor { get; set; } public Texture2D background { get; set; } }
    public class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int l, int r, int t, int b) { }
        public int left { get; set; } public int right { get; set; } public int top { get; set; } public int bottom { get; set; }
    }
    public class Font : Object { }
    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public static GUIStyle none { get { return null; } }
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
        public Vector2 CalcSize(GUIContent c) { return new Vector2(); }
        public float CalcHeight(GUIContent c, float width) { return 0; }
    }
    public class GUIContent
    {
        public GUIContent() { }
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
        public GUIContent(Texture image) { }
        public string text { get; set; }
        public static GUIContent none { get { return null; } }
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
        public static GUISkin skin { get; set; }
        public static Color color { get; set; }
        public static Color backgroundColor { get; set; }
        public static Color contentColor { get; set; }
        public static bool enabled { get; set; }
        public static bool changed { get; set; }
        public static int depth { get; set; }
        public static Matrix4x4 matrix { get; set; }
        public static void Label(Rect r, string t) { }
        public static void Label(Rect r, string t, GUIStyle s) { }
        public static void Label(Rect r, GUIContent c, GUIStyle s) { }
        public static void Label(Rect r, Texture t) { }
        public static bool Button(Rect r, string t) { return false; }
        public static bool Button(Rect r, string t, GUIStyle s) { return false; }
        public static bool Button(Rect r, GUIContent c, GUIStyle s) { return false; }
        public static bool Button(Rect r, GUIContent c) { return false; }
        public static bool RepeatButton(Rect r, string t, GUIStyle s) { return false; }
        public static bool Toggle(Rect r, bool v, string t) { return v; }
        public static bool Toggle(Rect r, bool v, string t, GUIStyle s) { return v; }
        public static bool Toggle(Rect r, bool v, GUIContent c) { return v; }
        public static string TextField(Rect r, string t) { return t; }
        public static string TextField(Rect r, string t, int maxLength) { return t; }
        public static string TextField(Rect r, string t, GUIStyle s) { return t; }
        public static string TextField(Rect r, string t, int maxLength, GUIStyle s) { return t; }
        public static float HorizontalSlider(Rect r, float v, float a, float b) { return v; }
        public static float HorizontalSlider(Rect r, float v, float a, float b, GUIStyle s, GUIStyle t) { return v; }
        public static int SelectionGrid(Rect r, int sel, string[] texts, int xCount) { return sel; }
        public static int SelectionGrid(Rect r, int sel, string[] texts, int xCount, GUIStyle s) { return sel; }
        public static int Toolbar(Rect r, int sel, string[] texts) { return sel; }
        public static void Box(Rect r, string t) { }
        public static void Box(Rect r, string t, GUIStyle s) { }
        public static void Box(Rect r, GUIContent c, GUIStyle s) { }
        public static void DrawTexture(Rect r, Texture t) { }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m) { }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend) { }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend, float imageAspect) { }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alphaBlend, float imageAspect, Color color, float borderWidth, float borderRadius) { }
        public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect tc) { }
        public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect tc, bool alphaBlend) { }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view) { return scroll; }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view, bool alwaysH, bool alwaysV) { return scroll; }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view, GUIStyle h, GUIStyle v) { return scroll; }
        public static void EndScrollView() { }
        public static void EndScrollView(bool handleScrollWheel) { }
        public static void BeginGroup(Rect r) { }
        public static void EndGroup() { }
        public static void BeginClip(Rect r) { }
        public static void EndClip() { }
        public static void SetNextControlName(string n) { }
        public static string GetNameOfFocusedControl() { return ""; }
        public static void FocusControl(string n) { }
        public static void UnfocusWindow() { }
    }
    public class GUIUtility
    {
        public static int keyboardControl { get; set; }
        public static int hotControl { get; set; }
        public static Vector2 GUIToScreenPoint(Vector2 p) { return p; }
        public static Vector2 ScreenToGUIPoint(Vector2 p) { return p; }
        public static int GetControlID(FocusType f) { return 0; }
        public static void ExitGUI() { }
    }
    public enum FocusType { Passive, Keyboard }
    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) { return null; }
        public bool SetData(float[] data, int offsetSamples) { return true; }
        public float length { get { return 0; } }
        public int samples { get { return 0; } }
    }
    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float spatialBlend { get; set; }
        public float panStereo { get; set; }
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public bool isPlaying { get { return false; } }
        public int priority { get; set; }
        public void Play() { }
        public void Stop() { }
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
        public static double dspTime { get { return 0; } }
    }
    public sealed class Time
    {
        public static float deltaTime { get { return 0; } }
        public static float unscaledDeltaTime { get { return 0; } }
        public static float realtimeSinceStartup { get { return 0; } }
        public static double realtimeSinceStartupAsDouble { get { return 0; } }
        public static int frameCount { get { return 0; } }
    }
    public sealed class PlayerPrefs
    {
        public static string GetString(string k, string d) { return d; }
        public static string GetString(string k) { return ""; }
        public static void SetString(string k, string v) { }
        public static bool HasKey(string k) { return false; }
        public static void DeleteKey(string k) { }
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
        public RenderTargetIdentifier(BuiltinRenderTextureType t) { }
        public RenderTargetIdentifier(Texture t) { }
        public RenderTargetIdentifier(int nameId) { }
        public static implicit operator RenderTargetIdentifier(BuiltinRenderTextureType t) { return new RenderTargetIdentifier(t); }
        public static implicit operator RenderTargetIdentifier(Texture t) { return new RenderTargetIdentifier(t); }
        public static implicit operator RenderTargetIdentifier(int id) { return new RenderTargetIdentifier(id); }
    }
    public class CommandBuffer : IDisposable
    {
        public string name { get; set; }
        public int sizeInBytes { get { return 0; } }
        public void Clear() { }
        public void Dispose() { }
        public void Release() { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass, MaterialPropertyBlock properties) { }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) { }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, float depth) { }
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 proj) { }
        public void SetViewMatrix(Matrix4x4 view) { }
        public void SetProjectionMatrix(Matrix4x4 proj) { }
        public void SetGlobalVector(string n, Vector4 v) { }
        public void SetGlobalVector(int n, Vector4 v) { }
        public void SetGlobalFloat(string n, float v) { }
        public void SetGlobalFloat(int n, float v) { }
        public void SetGlobalColor(string n, Color c) { }
        public void SetGlobalMatrix(string n, Matrix4x4 m) { }
        public void SetGlobalTexture(string n, RenderTargetIdentifier t) { }
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
        public NativeArray(int length, Allocator a, NativeArrayOptions o = NativeArrayOptions.ClearMemory) : this() { Length = length; }
        public NativeArray(T[] array, Allocator a) : this() { Length = array.Length; }
        public int Length { get; private set; }
        public T this[int i] { get { return default(T); } set { } }
        public bool IsCreated { get { return true; } }
        public void Dispose() { }
        public void CopyFrom(T[] a) { }
        public T[] ToArray() { return null; }
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct { return default(NativeArray<U>); }
    }
}
