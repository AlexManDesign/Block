// Voxel Forge — Unity port. Bootstrap MonoBehaviour: data init, camera + command buffer setup and the
// per-frame driver equivalent to requestAnimationFrame(render). Created automatically on scene load,
// so the game runs in an empty scene without any manual setup.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelForge
{
    public static partial class VF
    {
        static bool booted = false;

        /// <summary>Top-level script initialisation of the reference page, in dependency order.</summary>
        public static void bootstrap()
        {
            if (booted) return;
            booted = true;
            InitData();
            InitSettings();
            renderDistance = Math.Min(gameSettings.renderDist != 0 ? gameSettings.renderDist : DEFAULT_RD, DEVICE_RD_CAP);
            simulationDistance = Math.Min(gameSettings.simDist != 0 ? gameSettings.simDist : 6, DEVICE_RD_CAP);
            InitBlocks();
            InitItemDefs();
            InitCrops();
            InitTreeSets();
            InitSaplingSpecies();
            InitBlockSymbols();
            InitRecipes();
            InitLoot();
            InitSmelt();
            InitCreativeIds();
            InitEntityModels();
            InitAtlases();
            GenData.Ensure();
            initRenderer();
            ensureLegacyWorldIndex();
            updateModHUD();
            updateVitals();
            drawHotbar();
        }
    }

    [DefaultExecutionOrder(-1000)]
    public sealed class VoxelForgeGame : MonoBehaviour
    {
        static VoxelForgeGame instance;
        Camera cam, blitCam;
        RenderTexture scaledRT;
        CommandBuffer blitCB;
        bool cbAttached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (instance != null || FindObjectOfType<VoxelForgeGame>() != null) return;
            new GameObject("Voxel Forge").AddComponent<VoxelForgeGame>();
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            QualitySettings.antiAliasing = 0;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = true;

            // Only this camera draws the world; scene cameras would just add an extra clear.
            foreach (var c in Camera.allCameras) c.enabled = false;
            foreach (var l in FindObjectsOfType<AudioListener>()) l.enabled = false;

            cam = gameObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.depthTextureMode = DepthTextureMode.None;
            cam.nearClipPlane = 0.06f;
            cam.farClipPlane = 5000f;
            cam.depth = 0;
            gameObject.AddComponent<AudioListener>();

            // Upscale pass for dynamic resolution (the reference lowers the canvas backing size and lets CSS stretch it).
            var bgo = new GameObject("Voxel Forge Upscale");
            bgo.transform.SetParent(transform, false);
            blitCam = bgo.AddComponent<Camera>();
            blitCam.clearFlags = CameraClearFlags.Nothing;
            blitCam.cullingMask = 0;
            blitCam.allowHDR = false;
            blitCam.allowMSAA = false;
            blitCam.depth = 10;
            blitCam.enabled = false;
            blitCB = new CommandBuffer { name = "Voxel Forge upscale" };
            blitCam.AddCommandBuffer(CameraEvent.AfterEverything, blitCB);

            try { VF.bootstrap(); }
            catch (Exception e) { Debug.LogException(e); }
            if (VF.frameCB != null) { cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, VF.frameCB); cbAttached = true; }
        }

        void Update()
        {
            double now = JS.now();
            try
            {
                Timers.Tick();
                VF.tickPeriodicSave();
                VF.drainWorkerMessages();
                VF.pollInput(VF.uiTextFocused);
                double dt;
                double frameStart = VF.frameSimulate(now, out dt);
                int w, h;
                configureTarget(out w, out h);
                VF.renderWorld(cam, now, dt, w, h);
                VF.frameFinish(now, frameStart);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>resize(): backing size = screen × min(dpr, DPR_CAP)/dpr × renderScale.</summary>
        void configureTarget(out int w, out int h)
        {
            double dpr = Application.isMobilePlatform && Screen.dpi > 0 ? Screen.dpi / 160.0 : 1;
            double d = (dpr > VF.DPR_CAP ? VF.DPR_CAP / dpr : 1) * VF.renderScale;
            int sw = Math.Max(1, Screen.width), sh = Math.Max(1, Screen.height);
            if (d >= 0.999)
            {
                if (scaledRT != null) { cam.targetTexture = null; scaledRT.Release(); Destroy(scaledRT); scaledRT = null; blitCam.enabled = false; }
                w = sw; h = sh;
                return;
            }
            w = Math.Max(1, (int)Math.Floor(sw * d)); h = Math.Max(1, (int)Math.Floor(sh * d));
            if (scaledRT == null || scaledRT.width != w || scaledRT.height != h)
            {
                cam.targetTexture = null;
                if (scaledRT != null) { scaledRT.Release(); Destroy(scaledRT); }
                scaledRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "vf-scaled", antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
                };
                scaledRT.Create();
                cam.targetTexture = scaledRT;
                blitCB.Clear();
                blitCB.Blit(scaledRT, BuiltinRenderTextureType.CameraTarget);
                blitCam.enabled = true;
            }
        }

        void OnGUI()
        {
            try { VF.drawUI(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // window "blur" -> clearInputState; the browser also drops pointer lock, which raises pointerlockchange.
        void OnApplicationFocus(bool focus)
        {
            if (!focus) { VF.onFocusLost(); if (VF.locked) VF.releasePointerLock(); }
            VF.syncAudioState();
        }

        // Mobile background == "pagehide" + "visibilitychange".
        void OnApplicationPause(bool paused)
        {
            if (paused && VF.started && VF.worldReady) { try { VF.saveGameNow(); } catch (Exception e) { Debug.LogException(e); } }
            VF.syncAudioState();
        }

        void OnApplicationQuit()
        {
            try { if (VF.started && VF.worldReady) VF.saveGameNow(); } catch (Exception e) { Debug.LogException(e); }
            VF.shutdownWorkerEngine();
        }

        void OnDestroy()
        {
            if (instance != this) return;
            if (cbAttached && cam != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, VF.frameCB);
            if (blitCam != null && blitCB != null) blitCam.RemoveCommandBuffer(CameraEvent.AfterEverything, blitCB);
            if (scaledRT != null) { scaledRT.Release(); Destroy(scaledRT); }
            instance = null;
        }
    }
}
