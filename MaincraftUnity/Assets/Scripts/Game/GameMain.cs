// Port of main.js (15730-15759): the bootstrap, and the browser's frame loop and window events as the
// one MonoBehaviour of the game. Created in any scene ([RuntimeInitializeOnLoadMethod]), kept across scenes.
//
// Boot runs what the page did before and in boot(), in this order:
//   1. the module-level initialisation the JS ran at script load, in file order (blocks.js, entities.js,
//      interact.js, craft.js, game.js RT), and the static page of game.html (UI.BuildDom: #boot and #fatal
//      must exist before the Game is constructed);
//   2. main.js boot: Settings.Load, render distance capped at 24, the assets (loadImage: the PNGs stay
//      encoded as Resources TextAssets *.png.bytes and the Renderer decodes them), resolveBlockTextures,
//      new Game (an exception shows #fatal 'WebGL2: ' + message and rethrows), assets.atlasPixels from the
//      renderer, Icons.Init, window.game (GameMain.game), UI.Init, UI.ShowTitle, #boot hidden.
// Update is requestAnimationFrame: UI.Tick (the document-level handlers: input events reach the page
// before the animation frame) then Game.Loop with performance.now() (Renderer.Now). The Game constructor's
// requestAnimationFrame and window resize listener need nothing more (Loop resizes every frame).
// OnApplicationFocus(false) is the window blur event, OnApplicationPause(true) visibilitychange to hidden,
// OnApplicationQuit pagehide. navigator.storage.persist has no counterpart (files are not evicted).
//
// The free-camera world viewer this file held before the gameplay port (its stream / meshPass /
// uploadMeshes / environment are C# renditions of game.js) is in the git history, commit 0d3f04b.
//
// Complete as written (owner game-a); the functions it calls are filled in by their owners.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maincraft
{
    public sealed class GameMain : MonoBehaviour
    {
        /// <summary>window.game: the one Game (null until Boot constructed it).</summary>
        public static Game game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindObjectOfType<GameMain>() != null) return;
            var go = new GameObject("Maincraft");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMain>();
        }

        // boot() is async in the JS: a failure is logged (an unhandled rejection) and the page stays on #boot
        void Start()
        {
            try { Boot(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // main.js boot (15737-15758), after the module initialisation the JS did at script load
        void Boot()
        {
            // script load: the modules' tables, in file order
            Blocks.Init();
            StructData.Load(Text("Data/struct_tpl"), Text("Data/jig_str"), Text("Data/jig_pools"), Text("Data/jig_tpl"));
            EntityData.Init();
            Interact.Init();
            Craft.Init();
            Game.InitStatics();
            UI.BuildDom();

            // boot()
            Settings.Load();
            if (Settings.renderDist > 24) Settings.renderDist = 24;
            string metaJson = Text("Data/atlas_meta");
            var meta = (Dictionary<string, object>)MiniJson.Parse(metaJson);
            var layers = new Dictionary<string, int>();
            foreach (var kv in (Dictionary<string, object>)meta["layers"]) layers[kv.Key] = MiniJson.I(kv.Value);
            var assets = new GameAssets
            {
                atlasPng = Bytes("Textures/atlas.png"), sunPng = Bytes("Textures/sun.png"), moonPng = Bytes("Textures/moon.png"),
                atlasMetaJson = metaJson, entityImage = EntityImage, layers = layers,
                count = MiniJson.I(meta["count"]), cols = MiniJson.I(meta["cols"]),
            };
            Blocks.ResolveBlockTextures(layers);
            Mesher.InitMesherTextures(layers);     // the JS workers resolved theirs from assets.layers
            var cam = SetupCamera();
            try { game = new Game(assets, cam); }
            catch (Exception e)
            {
                if (UI.ById("fatal") is TextElement f) f.text = "WebGL2: " + e.Message;
                UI.Show("fatal", true);
                throw;
            }
            assets.atlasPixels = game.r.atlasPixels; assets.atlasW = game.r.atlasW;
            Icons.Init(assets);
            UI.Init(game);
            UI.ShowTitle();
            UI.Show("boot", false);
        }

        // requestAnimationFrame
        void Update()
        {
            if (game == null) return;
            UI.Tick();
            game.Loop(Renderer.Now);
        }

        // window blur
        void OnApplicationFocus(bool focus) { if (!focus && game != null) game.OnBlur(); }
        // visibilitychange (document.hidden)
        void OnApplicationPause(bool paused) { if (paused && game != null) game.OnPageHide(); }
        // pagehide
        void OnApplicationQuit() { game?.OnPageHide(); }

        // the page's end: worker threads and GPU buffers are released (the browser did this itself)
        void OnDestroy()
        {
            game?.world?.Dispose();
            game?.r?.Dispose();
        }

        /// <summary>The canvas #gl: the main camera, cleared and drawn only by the Renderer's command buffer.</summary>
        static Camera SetupCamera()
        {
            Application.targetFrameRate = -1;
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Camera"); cam = go.AddComponent<Camera>(); go.tag = "MainCamera"; }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.depthTextureMode = DepthTextureMode.None;
            foreach (var l in FindObjectsOfType<Light>()) l.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.ambientLight = Color.black;
            return cam;
        }

        /// <summary>ASSETS.entities[name]: an entity skin PNG (null when there is none).</summary>
        static byte[] EntityImage(string name) => Resources.Load<TextAsset>("Textures/Entities/" + name + ".png")?.bytes;

        static string Text(string path)
        {
            var t = Resources.Load<TextAsset>(path);
            if (t == null) throw new Exception("missing resource " + path);
            return t.text;
        }
        static byte[] Bytes(string path)
        {
            var t = Resources.Load<TextAsset>(path);
            if (t == null) throw new Exception("missing resource " + path);
            return t.bytes;
        }
    }
}
