using UnityEngine;

namespace BlockcraftPort
{
    public sealed class PortBootstrap : MonoBehaviour
    {
        bool started;
        bool waitingForInitialWorld;
        VoxelWorld loadingWorld;
        MainPlayerController loadingPlayer;
        MobSpawner loadingSpawner;
        DayNight loadingDayNight;
        GUIStyle loadingTitleStyle,loadingPhaseStyle,loadingPercentStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            if (FindObjectOfType<PortBootstrap>() == null) new GameObject("BlockcraftPort").AddComponent<PortBootstrap>();
        }

        void Awake()
        {
            if (FindObjectsOfType<PortBootstrap>().Length > 1) { Destroy(gameObject); return; }

            // main.js creates WebGL with antialias:false; keep the Unity path equally lean.
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;
            // VSync is explicitly disabled above, so do not silently re-introduce a refresh-rate cap
            // through Application.targetFrameRate. R59-R64 did exactly that: on a 60 Hz desktop the
            // player was hard-limited to 60 FPS even though vSyncCount == 0. Keep desktop/Editor truly
            // uncapped; mobile still follows the display refresh to avoid runaway thermal load.
            if(Application.isMobilePlatform)
            {
#if UNITY_2022_2_OR_NEWER
                int refresh=Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
#else
                int refresh=Screen.currentResolution.refreshRate;
#endif
                Application.targetFrameRate=Mathf.Clamp(refresh>0?refresh:60,30,120);
            }
            else Application.targetFrameRate=-1;

            // main title/create-world flow asks for survival/creative. Do not create renderers or
            // stream chunks until the player has chosen, so the selector itself has zero game cost.
            var selectorGo=new GameObject("MainGameModeSelector");
            var selector=selectorGo.AddComponent<MainModeSelector>();
            selector.Bootstrap=this;
        }

        public void BeginGame(MainGameMode mode)
        {
            BeginGame(mode,null);
        }

        public void BeginGame(MainGameMode mode,MainWorldSaveData restore)
        {
            if(started)return;
            started=true;

            var atlas = Resources.Load<Texture2D>("Voxel/atlas");
            if (atlas == null) { Debug.LogError("Voxel atlas missing"); started=false; return; }
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.anisoLevel = 0;
            var itemAtlas = Resources.Load<Texture2D>("Voxel/main_items");
            if(itemAtlas!=null){itemAtlas.filterMode=FilterMode.Point;itemAtlas.wrapMode=TextureWrapMode.Clamp;itemAtlas.anisoLevel=0;}

            var opaque = Mat("Blockcraft/VoxelOpaque", atlas);
            var water = Mat("Blockcraft/VoxelWater", atlas);
            var transparent = Mat("Blockcraft/VoxelTransparent", atlas);
            var xray = Mat("Blockcraft/VoxelXray", atlas);

            var biomeTable = Resources.Load<TextAsset>("Voxel/biome_multinoise");
            if (biomeTable != null) MainBiomeLookup.Initialize(biomeTable.bytes);
            else { Debug.LogError("Packed main biome table missing; exact deepslate generation cannot start."); started=false; return; }

            var player = new GameObject("Player");
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(player.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = .1f;
            cam.farClipPlane = 420f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            // All game-world 3D is submitted by persistent CommandBuffers. Keep Unity's normal
            // scene Renderer culling path empty; IMGUI/command-buffer passes are unaffected.
            cam.cullingMask = 0;
            cam.useOcclusionCulling = false;
            cam.depthTextureMode = DepthTextureMode.None;
            cam.allowDynamicResolution = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.tag = "MainCamera";

            var pc = player.AddComponent<MainPlayerController>();
            pc.Cam = cam;
            pc.ConfigureMode(mode);
            if(restore!=null)
            {
                pc.RestorePersistentInventory(restore.inventory,restore.hotbarSel);
                pc.RestorePersistentSurvival(restore.surv);
            }
            // main.js does not make gameplay live until the startup generation/light/mesh phases finish.
            // Keep the controller disabled so Start()/input/cursor lock cannot run during preload.
            pc.enabled=false;

            // main.js first new world: FJ=56, SJ=[-1063,-2850], OJ=PI/2, genVersion="deepslate".
            // A saved world restores its source-space pose BEFORE VoxelWorld.Configure(), so R40's
            // startup square is centered on the actual resume position rather than the default spawn.
            int mainSeed=restore!=null?restore.seed:56;
            const int mainSpawnX = -1063, mainSpawnZ = -2850;
            Vector2Int sourceStartupAnchor=new Vector2Int(mainSpawnX,mainSpawnZ);
            if(restore!=null&&restore.pos!=null&&restore.pos.Length==3)
            {
                player.transform.position=new Vector3(restore.pos[0],restore.pos[1],SourceCoords.SourceWorldZToUnity(restore.pos[2]));
                pc.RestorePersistentPose(restore.yaw,restore.pitch,restore.fly);
                pc.RestorePersistentSpawnSource(restore.spawn);
            }
            else
            {
                // main startup: metadata SJ is only the generation anchor when be(SJ).h >= sea;
                // otherwise Bl() finds coarse land first. The final pg() dry-surface scan happens
                // after this generation square has finished, before initial lighting/meshing.
                var previewBiomes=new BiomeGenerator(mainSeed);
                sourceStartupAnchor=MainSpawnResolver.ResolveGenerationAnchor(previewBiomes,mainSpawnX,mainSpawnZ);
                var preview=previewBiomes.Sample(sourceStartupAnchor.x,sourceStartupAnchor.y);
                player.transform.position=new Vector3(sourceStartupAnchor.x+.5f,Mathf.Max(preview.Height,VoxelConstants.SeaLevel)+2f,SourceCoords.SourceWorldZToUnity(sourceStartupAnchor.y+.5f));
                pc.RestorePersistentPose(Mathf.PI*.5f,-.15f,false); // first-world OJ = PI/2
            }

            // Prewarm shared mob geometry before streaming threads start, so the first occurrence of a
            // species cannot cause a gameplay-frame allocation/geometry-build hitch.
            MainEntityModel.WarmupGeometry();

            var worldGo = new GameObject("VoxelWorld");
            var world = worldGo.AddComponent<VoxelWorld>();
            if(restore==null)
            {
                Vector2Int drySearchAnchor=sourceStartupAnchor;
                world.InitialGenerationCompleted=w=>
                {
                    Vector3 drySpawn=MainSpawnResolver.ResolveNewWorldSpawn(w,drySearchAnchor.x,drySearchAnchor.y);
                    player.transform.position=drySpawn;
                    pc.SetRespawnPoint(drySpawn,false);
                };
            }
            world.Configure(player.transform, opaque, water, transparent, xray, mainSeed, VoxelWorld.SavedRenderDistance(), restore!=null?restore.edits:null);

            // main's sun is not a scene light: voxel illumination comes from the 16x16 LUT. Keeping a
            // Unity Directional Light needlessly feeds the engine light-culling path for every renderer.
            // DayNight only maintains the source time/sky/LUT inputs.
            var dayNight=new GameObject("DayNight").AddComponent<DayNight>();
            dayNight.SetTime01(restore!=null?restore.dayT:.25f);
            dayNight.enabled=false;

            var env = new GameObject("VoxelRenderEnvironment").AddComponent<VoxelRenderEnvironment>();
            env.World = world;
            env.Cam = cam;
            env.Player = pc;

            // main.js draws the first-person hand/held block last (Z3/b3) after clearing only depth.
            // Keep it on the camera as a direct CommandBuffer pass; no MeshRenderer/MeshFilter is created.
            var firstPerson = camGo.AddComponent<MainFirstPersonRenderer>();
            firstPerson.World = world; firstPerson.Cam = cam; firstPerson.Controller = pc;

            var sky = new GameObject("MainSkyRenderer").AddComponent<MainSkyRenderer>();
            sky.World = world; sky.Player = player.transform; sky.Cam = cam;

            // Create the direct entity batch before the first camera submission. Mobs only append CPU
            // geometry later; no MeshRenderer/MeshFilter is registered with Unity's scene renderer.
            MainEntityBatchRenderer.EnsureInstance();
            // W7()/h7()/rP() dynamic world effects use persistent direct batches, matching main.js.
            MainTransientRenderer.EnsureInstance(world,cam,player.transform);
            // R60 main Eu()/hT()/gL()/dT(): event-driven leaf decay with source .25 s / 24 cap.
            MainLeafDecay.EnsureInstance(world);
            // R61 main qP()/tB()/oh()/Eh(): budget-friendly world lifecycle, independent from renderer hot paths.
            MainBlockLifecycle.EnsureInstance(world,restore);
            // kB()/pP()/nT()/yd()/$B() source object passes share the same direct-render architecture.
            MainSourceObjectRenderer.EnsureInstance(world,cam,player.transform);
            // R52: ordinary-world crafting/chest/furnace state. No moving-ship integration in this revision.
            MainWorldFunctionalBlocks.EnsureInstance(world,pc,restore!=null?restore.furnaces:null,restore!=null?restore.chests:null);
            MainShipRuntime.EnsureInstance(world,pc);
            if(restore!=null)MainShipRuntime.RestorePersistentShipsSource(restore.ships);
            var thirdPerson=player.AddComponent<MainThirdPersonPlayerRenderer>();
            thirdPerson.Controller=pc;thirdPerson.World=world;

            var outline = new GameObject("MainSelectionOutline").AddComponent<MainSelectionOutline>();
            outline.World = world; outline.Cam = cam; outline.Controller=pc;
            var spawner = new GameObject("MainMobSpawner").AddComponent<MobSpawner>();
            spawner.World = world;
            spawner.Player = player.transform;
            spawner.SharksEnabled = MainModSettings.SharksEnabled;
            if(restore!=null)spawner.RestorePersistentState(restore.mobs,restore.seededChunks);
            // OnChunkLoaded still performs source-style one-time fauna seeding during preload, but
            // the normal mob simulation/spawn cadence starts only after the world-live gate opens.
            spawner.enabled=false;

            // main.js HUD: hotbar, crosshair, hearts/food/armour/air.
            var hud = new GameObject("MainHud").AddComponent<MainHud>();
            hud.Player = pc;

            var perf = new GameObject("MainPerformanceHud").AddComponent<MainPerformanceHud>();
            perf.World=world;

            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            var persistence=new GameObject("MainWorldPersistence").AddComponent<MainWorldPersistence>();
            persistence.Configure(world,pc,dayNight,spawner,mainSeed,mode);

            loadingWorld=world;
            loadingPlayer=pc;
            loadingSpawner=spawner;
            loadingDayNight=dayNight;
            waitingForInitialWorld=true;
            Cursor.lockState=CursorLockMode.None;
            Cursor.visible=true;
            Debug.Log("Blockcraft mode: "+mode+(restore!=null?"; restoring saved world":"; new world")+"; preloading initial world before gameplay");
        }

        void Update()
        {
            if(!waitingForInitialWorld)return;
            if(loadingWorld==null)
            {
                waitingForInitialWorld=false;
                return;
            }
            if(!loadingWorld.InitialWorldReady)return;

            waitingForInitialWorld=false;
            if(loadingDayNight!=null)loadingDayNight.enabled=true;
            if(loadingSpawner!=null)loadingSpawner.enabled=true;
            // Enabling here causes MainPlayerController.Start() to run only after the complete
            // source-style startup gate, so cursor capture and input begin at the same moment.
            if(loadingPlayer!=null)loadingPlayer.enabled=true;
            Debug.Log("Gameplay live: initial world generation, lighting and meshes are ready");
        }

        void EnsureLoadingStyles()
        {
            if(loadingTitleStyle!=null)return;
            loadingTitleStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=26,fontStyle=FontStyle.Bold};
            loadingPhaseStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=14};
            loadingPercentStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=12,fontStyle=FontStyle.Bold};
        }

        void OnGUI()
        {
            if(!waitingForInitialWorld||loadingWorld==null)return;
            EnsureLoadingStyles();

            // Deliberately opaque: the player never observes chunks popping in underneath the loading UI.
            Color oldColor=GUI.color;
            GUI.color=Color.black;
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture,ScaleMode.StretchToFill);
            GUI.color=oldColor;
            float w=Mathf.Min(560f,Screen.width-40f);
            float x=(Screen.width-w)*.5f;
            float y=Screen.height*.5f-70f;
            GUI.Label(new Rect(x,y,w,42f),"Generating world... / Генерация мира...",loadingTitleStyle);
            GUI.Label(new Rect(x,y+42f,w,28f),loadingWorld.InitialLoadPhase,loadingPhaseStyle);

            float p=Mathf.Clamp01(loadingWorld.InitialLoadProgress);
            Rect outer=new Rect(x,y+82f,w,24f);
            GUI.Box(outer,GUIContent.none);
            Rect inner=new Rect(outer.x+3f,outer.y+3f,(outer.width-6f)*p,outer.height-6f);
            GUI.DrawTexture(inner,Texture2D.whiteTexture,ScaleMode.StretchToFill);
            GUI.Label(new Rect(x,y+108f,w,24f),Mathf.RoundToInt(p*100f)+"%",loadingPercentStyle);
        }

        Material Mat(string shaderName, Texture2D atlas)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) throw new System.InvalidOperationException("Required Blockcraft shader missing: " + shaderName);
            var m = new Material(sh) { mainTexture = atlas };
            if (shaderName == "Blockcraft/VoxelWater") m.SetFloat("_Alpha", .62f);
            else if (shaderName == "Blockcraft/VoxelXray") m.SetFloat("_WireAlpha", .5f);
            else if (shaderName == "Blockcraft/VoxelTransparent") m.SetFloat("_Alpha", 1f);
            return m;
        }
    }
}
