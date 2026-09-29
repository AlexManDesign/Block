using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace BlockcraftPort
{
    [DefaultExecutionOrder(100)]
    public sealed class VoxelWorld : MonoBehaviour
    {
        // Revision lineage kept for static regression validators: R32_WATER_LIGHT_FIX_2026-09-12.
        // Startup baseline retained: R40_SOURCE_STARTUP_PRELOAD_2026-09-13.
        // Save baseline retained: R41_SOURCE_WORLD_SAVE_PRELOAD_2026-09-13.
        // Render hot-path baseline retained: R42_RENDER_HOTPATH_SAFE_2026-09-13.
        // Index bandwidth baseline retained: R43_RENDER_INDEX_BANDWIDTH_2026-09-13.
        // Render pass baseline retained: R44_RENDER_PASS_SPLIT_2026-09-14.
        // Dynamic index baseline retained: R45_DYNAMIC_INDEX_BANDWIDTH_2026-09-14.
        // Architecture baseline retained: R54_ARCHITECTURE_PIPELINE_2026-09-19.
        // Spawn baseline retained: R55_DRY_SPAWN_PARITY_2026-09-19.
        // Mob lifecycle baseline retained: R56_MOB_LIFECYCLE_RENDER_PARITY_2026-09-19.
        // Stable fog/per-frame feed baseline retained: R58_FOG_STREAMING_SOURCE_PARITY_2026-09-19.
        public const string PortRevision = "R81_VISIBLE_MESH_PRIORITY_2026-09-21";
        public static VoxelWorld Instance { get; private set; }

        public int Seed = 56;
        public int RenderDistance = 6;
        public int GenerationDistance = 9;
        public int MaxGenerationJobs = 2; // worker concurrency, not a frame quota
        public int MaxMeshJobs = 2;       // worker concurrency, not a frame quota
        public int MaxPendingGeneration = 10;
        public int MaxSectionsPerMeshJob = VoxelConstants.SectionCount;
        public int SharedChunkSnapshotThreshold = 8;
        public int FarCpuGeometryRadius = 2;
        public int WritableMeshRadius = 3;

        // Exact render-distance choices exposed by main.js settings. The source also caps the
        // available choices by device memory/mobile class (8 / 12 / 20 chunks).
        static readonly int[] SourceRenderDistances={4,6,8,12,16,20,25,30,40,50};
        static readonly int[] EditZeroOffsets={0};
        static readonly int[] EditNegOffsets={0,-1};
        static readonly int[] EditPosOffsets={0,1};
        const string RenderDistancePrefKey="blockcraft.renderDist";

        public Transform Player { get; private set; }
        public ChunkGenerator Generator { get; private set; }
        public int LoadedChunks => chunks.Count;
        public int RenderChunks => renders.Count;
        // BC_TEMP_DEBUG_BEGIN [GPU_READY_RADIUS] REMOVE BEFORE RELEASE
        // Diagnostic only: contiguous GPU-ready radius around the current chunk. Rendering/fog must
        // never depend on this transient value; main.js fog depends solely on configured RENDER_R.
        public int PublishedRenderRadius { get { if(publishedRenderRadiusDirty)RecomputePublishedRenderRadius(); return publishedRenderRadius; } }
        // BC_TEMP_DEBUG_END [GPU_READY_RADIUS]
        public int PendingGeneration => generating.Count;
        public int PendingMeshes => pendingMeshTotal;
        public int ActiveGenerationJobs => Volatile.Read(ref activeGen);
        public int ActiveMeshJobs => Volatile.Read(ref activeMesh);
        public int PendingUploads => uploadQueue.Count;
        public int PendingLightEdits => lightEditQueue.Count + (deferredLightActive ? 1 : 0);
        public int PendingUnloads => unloadQueue.Count;
        public int PendingCpuTrims => cpuTrimQueue.Count + gpuDropQueue.Count;
        public int RenderRevision => renderRevision;
        public int RenderOverlayRevision => renderOverlayRevision;
        public int RenderCandidateRevision => renderCandidateRevision;
        public int WorldQueryRevision => worldQueryRevision;
        public int LastVisibleChunks { get; private set; }
        public int LastChunkDrawCalls { get; private set; }
        public int LastChunkTriangles { get; private set; }
        public int DirectCommandRebuilds { get; private set; }
        public int DirectStaticCommandRebuilds { get; private set; }
        public int DirectOverlayCommandRebuilds { get; private set; }
        public int DirectVisibilityScans { get; private set; }
        public int DirectCandidateRebuilds { get; private set; }
        // BC_TEMP_DEBUG_BEGIN [F3_STREAM_PERF_TELEMETRY] REMOVE BEFORE RELEASE
        // These public timing/counter surfaces exist only for the temporary F3 investigation HUD.
        public float LastStreamingWorkMs { get; private set; }
        public float LastStreamingBudgetMs { get; private set; }
        public float LastGenerationIntegrateMs { get; private set; }
        public float LastChunkLightWorkMs { get; private set; }
        public float LastLightWorkMs { get; private set; }
        public float LastMeshIntegrateMs { get; private set; }
        public float LastMeshScheduleMs { get; private set; }
        public float LastMeshUploadMs { get; private set; }
        public float MaxMeshUploadMs { get; private set; }
        public float EstimatedMeshUploadMs => (float)estimatedMeshUploadMs;
        public int LastMeshUploadBytes { get; private set; }
        public int LastMeshUploadBatchBytes { get; private set; }
        public bool LastMeshUploadUsed16BitIndices { get; private set; }
        public int LastMeshUploadIndexBytesSaved { get; private set; }
        public int MeshIndex16Uploads { get; private set; }
        public int MeshIndex32Uploads { get; private set; }
        public int MeshUploads { get; private set; }
        public int MeshReplacements { get; private set; }
        // BC_TEMP_DEBUG_END [F3_STREAM_PERF_TELEMETRY]

        // main.js DC(): gameplay is not live while the initial world square is being generated,
        // lighted and meshed. Unity keeps the same contract instead of merely freezing the player
        // over one loaded chunk while the surrounding world streams in.
        public bool InitialWorldReady { get; private set; }
        public float InitialLoadProgress { get; private set; }
        public string InitialLoadPhase { get; private set; } = "Preparing world";
        public int InitialGeneratedChunks { get; private set; }
        public int InitialGeneratedTarget { get; private set; }
        public int InitialMeshedChunks { get; private set; }
        public int InitialMeshedTarget { get; private set; }
        // main startup resolves pg() after the complete generation square exists but before the
        // initial lighting/mesh passes. PortBootstrap installs this one-shot barrier for a new world.
        public Action<VoxelWorld> InitialGenerationCompleted;

        // main.js stores only sparse world edits, never full generated chunks. Keys are local dense
        // cell indices inside a Unity chunk; values are Unity blockId | (meta << 16). The map is
        // shared with generation workers only through this lock, so saved edits can be applied before
        // the chunk enters lighting/meshing without racing new gameplay edits.
        readonly object persistentEditLock=new object();
        readonly Dictionary<ChunkCoord,Dictionary<int,int>> persistentEdits=new Dictionary<ChunkCoord,Dictionary<int,int>>();

        // R54 save architecture: gameplay writes a compact append-only edit journal. Autosave captures
        // only an O(1) immutable token on the main thread; deduplication/flattening happens on the
        // background writer. A successful save can atomically replace the journal with its compacted
        // form when no newer edit was made, preventing unbounded history growth.
        const int PersistentJournalSegmentSize=512;
        internal struct PersistentEditRecord
        {
            public int X,Y,SourceZ,Packed;
            public PersistentEditRecord(int x,int y,int sourceZ,int packed){X=x;Y=y;SourceZ=sourceZ;Packed=packed;}
        }
        internal sealed class PersistentEditJournalSegment
        {
            public readonly PersistentEditRecord[] Records=new PersistentEditRecord[PersistentJournalSegmentSize];
            public int Count;
            public PersistentEditJournalSegment Next;
        }
        public sealed class PersistentEditJournalSnapshot
        {
            internal PersistentEditJournalSegment Head,Tail;
            internal int RecordCount;
            internal long Version;
            public int Count=>RecordCount;
            public long SnapshotVersion=>Version;
        }
        PersistentEditJournalSegment persistentJournalHead,persistentJournalTail;
        int persistentJournalRecordCount;
        long persistentJournalVersion;
        public bool PersistentSaveDirty { get; private set; }
        public int PersistentEditCount { get; private set; }
        public int PersistentJournalRecordCount=>Volatile.Read(ref persistentJournalRecordCount);

        enum InitialLoadStage : byte { Generate, RebuildLight, StitchLight, Mesh, Ready }
        InitialLoadStage initialLoadStage;
        ChunkCoord initialCenter;
        readonly List<ChunkCoord> initialGenerationTargets = new List<ChunkCoord>(81);
        readonly List<ChunkCoord> initialMeshTargets = new List<ChunkCoord>(49);
        int initialLightIndex;
        bool initialGenerationQueued;
        bool initialGenerationCompletedInvoked;

        readonly Dictionary<ChunkCoord, ChunkColumn> chunks = new Dictionary<ChunkCoord, ChunkColumn>();
        readonly ConcurrentDictionary<ChunkCoord, byte> generating = new ConcurrentDictionary<ChunkCoord, byte>();
        readonly ConcurrentQueue<ChunkColumn> generated = new ConcurrentQueue<ChunkColumn>();
        readonly ConcurrentQueue<string> errors = new ConcurrentQueue<string>();
        // Runtime streaming is a staged pipeline: generation publishes a chunk, then cross-chunk
        // lighting is committed as its own main-thread stage before any mesh snapshot may observe it.
        readonly Queue<ChunkCoord> chunkLightQueue = new Queue<ChunkCoord>(64);
        readonly HashSet<ChunkCoord> chunkLightPending = new HashSet<ChunkCoord>();
        VoxelLighting.WorldStitchJob activeChunkLightJob;
        ChunkCoord activeChunkLightCoord;
        public int PendingChunkLighting=>chunkLightQueue.Count+(activeChunkLightJob!=null?1:0);

        readonly HashSet<SectionKey> meshQueued = new HashSet<SectionKey>();
        readonly Dictionary<ChunkCoord,uint> meshQueuedMasks = new Dictionary<ChunkCoord,uint>(2048);
        readonly List<ChunkCoord> meshMaskScratch = new List<ChunkCoord>(128);
        // main's dirty/far-mesh marker is per chunk. Off-screen edits/border changes collapse to
        // one chunk marker and expand to section jobs only when the chunk re-enters RENDER_R.
        readonly HashSet<ChunkCoord> deferredWholeChunks = new HashSet<ChunkCoord>();
        readonly HashSet<SectionKey> meshInFlight = new HashSet<SectionKey>();
        // O(1) pending test per chunk. main trims CPU geo periodically; scanning both mesh sets
        // for every render chunk would turn that cleanup into a visible frame spike.
        readonly Dictionary<ChunkCoord, int> pendingMeshByChunk = new Dictionary<ChunkCoord, int>();
        int pendingMeshTotal;
        readonly ConcurrentQueue<MeshBuildBatch> meshed = new ConcurrentQueue<MeshBuildBatch>();
        readonly Dictionary<SectionKey, int> sectionStamp = new Dictionary<SectionKey, int>();
        readonly Dictionary<SectionKey, int> sectionAppliedStamp = new Dictionary<SectionKey, int>();

        readonly Dictionary<ChunkCoord, ChunkRender> renders = new Dictionary<ChunkCoord, ChunkRender>();
        int publishedRenderRadius;
        bool publishedRenderRadiusDirty=true;
        readonly Queue<ChunkCoord> uploadQueue = new Queue<ChunkCoord>();
        readonly HashSet<ChunkCoord> uploadQueued = new HashSet<ChunkCoord>();
        readonly List<ChunkCoord> unloadScratch = new List<ChunkCoord>(64);
        readonly List<Vector2Int> generationOffsets = new List<Vector2Int>(4096);
        readonly Queue<ChunkCoord> unloadQueue = new Queue<ChunkCoord>(128);
        readonly HashSet<ChunkCoord> unloadQueued = new HashSet<ChunkCoord>();
        readonly Queue<ChunkCoord> cpuTrimQueue = new Queue<ChunkCoord>(128);
        readonly HashSet<ChunkCoord> cpuTrimQueued = new HashSet<ChunkCoord>();
        readonly Queue<ChunkCoord> gpuDropQueue = new Queue<ChunkCoord>(128);
        readonly HashSet<ChunkCoord> gpuDropQueued = new HashSet<ChunkCoord>();
        readonly List<ChunkCoord> renderDropScratch = new List<ChunkCoord>(64);
        // Only the source-style immediate player-edit rebuild uses a main-thread snapshot.
        // Streaming mesh jobs receive neighbour references and copy their immutable worker input off-thread.
        readonly ChunkColumn[] immediateCaptureNeighborhood = new ChunkColumn[9];
        readonly List<ChunkCoord> promoteChunkScratch = new List<ChunkCoord>(64);
        readonly List<SectionKey> stampRemoveScratch = new List<SectionKey>(64);
        readonly Dictionary<ChunkCoord, bool> meshReadyScratch = new Dictionary<ChunkCoord, bool>(256);
        readonly List<SectionKey> playerEditSectionsScratch = new List<SectionKey>(8);
        readonly List<ChunkCoord> playerEditChunksScratch = new List<ChunkCoord>(4);
        readonly HashSet<SectionKey> lightDirtySectionsScratch = new HashSet<SectionKey>();
        readonly HashSet<ChunkCoord> lightDirtyChunksScratch = new HashSet<ChunkCoord>();
        readonly List<ChunkRender> directCandidates = new List<ChunkRender>(2048);
        readonly List<ChunkRender> directVisibleScratch = new List<ChunkRender>(2048);
        readonly List<ChunkRender> directRecordedVisible = new List<ChunkRender>(2048);
        bool directCandidatesValid;
        readonly Vector4[] directFrustumPlanes = new Vector4[6];
        static readonly Matrix4x4 DirectIdentity = Matrix4x4.identity;

        struct LightEdit
        {
            public int X,Y,Z;
            public bool NeedsSkyRelight,NeedsSkyFrontierShell;
            public LightEdit(int x,int y,int z,BlockId oldId,byte oldMeta,BlockId newId,byte newMeta)
            {
                X=x;Y=y;Z=z;
                NeedsSkyRelight=VoxelLighting.RequiresSkyRelight(oldId,oldMeta,newId,newMeta);
                NeedsSkyFrontierShell=NeedsSkyRelight&&VoxelLighting.RequiresSkyFrontierShell(oldId,oldMeta,newId,newMeta);
            }
        }
        readonly Queue<LightEdit> lightEditQueue = new Queue<LightEdit>(128);
        // Deferred simulation lighting is a resumable transaction, not a per-frame quota.
        // The job owns every frontier/cursor needed to resume exact aD()/rD()/nD() propagation.
        VoxelLighting.DeferredRelightJob deferredLightJob;
        LightEdit deferredLightEdit;
        bool deferredLightActive;

        // main.js h8()/ya()/rJ()/oP()/fE()/sP(): source water simulation queues.
        // R54 keeps the fixed 200 ms simulation clock, but separates simulation from world commit:
        // one logical source tick is evaluated against a tiny overlay, then committed as one mutation
        // transaction. This removes N repeated relight/remesh callbacks without changing water rules.
        struct WaterCell
        {
            public int X,Y,Z;
            public WaterCell(int x,int y,int z){X=x;Y=y;Z=z;}
        }
        struct WaterMutation
        {
            public int X,Y,Z; public BlockId Id;
            public WaterMutation(int x,int y,int z,BlockId id){X=x;Y=y;Z=z;Id=id;}
        }
        readonly Queue<WaterCell> waterQueue=new Queue<WaterCell>(512);
        readonly HashSet<long> waterQueued=new HashSet<long>();
        readonly Queue<WaterCell> waterSeedQueue=new Queue<WaterCell>(128);
        readonly HashSet<long> waterSeedQueued=new HashSet<long>();
        readonly List<WaterCell> waterTickScratch=new List<WaterCell>(480);
        readonly List<WaterMutation> waterMutationScratch=new List<WaterMutation>(128);
        readonly Dictionary<long,int> waterMutationIndexScratch=new Dictionary<long,int>(128);
        readonly HashSet<SectionKey> waterDirtySectionsScratch=new HashSet<SectionKey>();
        float waterTickClock;
        bool waterTickActive;
        int waterTickIndex,waterTickChangesRemaining,waterTickWorldRevision;
        // BC_TEMP_DEBUG_BEGIN [F3_FLUID_TELEMETRY] REMOVE BEFORE RELEASE
        public float LastWaterWorkMs { get; private set; }
        public float MaxWaterWorkMs { get; private set; }
        public int LastWaterTicks { get; private set; }
        public int PendingWaterCells => waterQueue.Count + waterSeedQueue.Count + (waterTickActive ? Mathf.Max(0,waterTickScratch.Count-waterTickIndex) : 0);
        public bool WaterTickActive => waterTickActive;
        public void ResetPerformancePeaks(){MaxWaterWorkMs=0f;MaxLavaWorkMs=0f;MaxMeshUploadMs=0f;}
        const int WaterQueueLimit=20000;
        const float WaterFixedStep=.2f;
        static readonly int[] WaterDX={1,-1,0,0};
        static readonly int[] WaterDZ={0,0,1,-1};

        // main.js h8()/Ha()/tJ()/uP()/gc()/CP(): source lava simulation queues.
        // Uses the same atomic overlay/commit strategy as water so one bounded source tick observes
        // its own earlier mutations without paying per-voxel relight/remesh costs.
        readonly Queue<WaterCell> lavaQueue=new Queue<WaterCell>(320);
        readonly HashSet<long> lavaQueued=new HashSet<long>();
        readonly Queue<WaterCell> lavaSeedQueue=new Queue<WaterCell>(96);
        readonly HashSet<long> lavaSeedQueued=new HashSet<long>();
        readonly List<WaterCell> lavaTickScratch=new List<WaterCell>(280);
        readonly List<WaterMutation> lavaMutationScratch=new List<WaterMutation>(64);
        readonly Dictionary<long,int> lavaMutationIndexScratch=new Dictionary<long,int>(64);
        readonly HashSet<SectionKey> lavaDirtySectionsScratch=new HashSet<SectionKey>();
        float lavaTickClock;
        bool lavaTickActive;
        int lavaTickIndex,lavaTickChangesRemaining,lavaTickWorldRevision;
        public float LastLavaWorkMs { get; private set; }
        public float MaxLavaWorkMs { get; private set; }
        public int LastLavaTicks { get; private set; }
        public int PendingLavaCells => lavaQueue.Count + lavaSeedQueue.Count + (lavaTickActive ? Mathf.Max(0,lavaTickScratch.Count-lavaTickIndex) : 0);
        public bool LavaTickActive => lavaTickActive;
        // BC_TEMP_DEBUG_END [F3_FLUID_TELEMETRY]
        const int LavaQueueLimit=20000;
        const float LavaFixedStep=.5f;

        Material[] materials, xrayMaterials;
        bool xrayEnabled;
        int renderRevision;
        int renderOverlayRevision;
        int renderCandidateRevision;
        int worldQueryRevision;
        MainChunkDirectRenderer directRenderer;
        int activeGen, activeMesh;
        // main.js FI(): one shared deadline for chunk integration, lighting and mesh work.
        // Independent Unity budgets can all fire in the same frame and add up to a hitch; the source
        // instead stops background-result work when the current display-frame headroom is consumed.
        double sourceRefreshMs=16.6;
        // Upper bound for main-thread streaming work per frame (integration, light stitch, uploads).
        // Heavy work (generation, meshing) runs on worker threads and is not limited by this slice.
        public float MaxStreamingSliceMs = 6f;

        static double DisplayRefreshMs()
        {
#if UNITY_2022_2_OR_NEWER
            double hz=Screen.currentResolution.refreshRateRatio.value;
#else
            double hz=Screen.currentResolution.refreshRate;
#endif
            if(!(hz>=24.0&&hz<=500.0))hz=60.0;
            return 1000.0/hz;
        }
        double estimatedChunkIntegrateMs=.35;
        double estimatedMeshUploadMs=2.0;
        double estimatedUnloadMs=.35,estimatedGpuDropMs=.35,estimatedCpuTrimMs=.25;
        int maintenanceTurn;
        static readonly ProfilerMarker WaterMarker=new ProfilerMarker("Blockcraft.Stream.Water");
        static readonly ProfilerMarker LavaMarker=new ProfilerMarker("Blockcraft.Stream.Lava");
        static readonly ProfilerMarker IntegrateGeneratedMarker=new ProfilerMarker("Blockcraft.Stream.IntegrateGenerated");
        static readonly ProfilerMarker ChunkLightMarker=new ProfilerMarker("Blockcraft.Stream.ChunkLighting");
        static readonly ProfilerMarker LightMarker=new ProfilerMarker("Blockcraft.Stream.Lighting");
        static readonly ProfilerMarker MeshIntegrateMarker=new ProfilerMarker("Blockcraft.Stream.MeshIntegrateUpload");
        static readonly ProfilerMarker MeshUploadMarker=new ProfilerMarker("Blockcraft.Render.MeshUpload");
        static readonly ProfilerMarker MeshScheduleMarker=new ProfilerMarker("Blockcraft.Stream.MeshSchedule");
        static readonly ProfilerMarker MaintenanceMarker=new ProfilerMarker("Blockcraft.Stream.Maintenance");
        ChunkCoord center;
        ChunkCoord generationLiveCenter;
        MainPlayerController streamingPlayer;
        Camera streamingCamera;
        // R79: the world state may already exist while its GPU mesh is still in the worker pipeline.
        // Keep the producer camera-aware so chunks actually on screen win ties over equally-near
        // side/back chunks. This changes only work order: render distance, fog, topology and total work stay intact.
        volatile float streamingViewX=0f,streamingViewZ=1f;
        readonly Vector4[] streamingPriorityFrustumPlanes=new Vector4[6];
        bool streamingPriorityFrustumValid;
        // BC_TEMP_DEBUG_BEGIN [R79_VISUAL_GAP_TELEMETRY] REMOVE BEFORE RELEASE
        // Temporary counters used to prove data-present/mesh-missing visual holes.
        public int PublishedFrontierMissingData { get; private set; }
        public int PublishedFrontierMissingMesh { get; private set; }
        public int ViewMissingDataChunks { get; private set; }
        public int ViewMissingMeshChunks { get; private set; }
        // BC_TEMP_DEBUG_END [R79_VISUAL_GAP_TELEMETRY]

        // main.js keeps long-lived genWorker/meshWorker instances. Keep dedicated background
        // threads alive too instead of paying Task.Run/ThreadPool scheduling and losing worker-local
        // scratch affinity on every chunk. The semaphores count work items, so no wakeups are lost.
        // Source genWorker queue Cu is re-sorted around qo() via jw() while the player moves.
        // A FIFO queue keeps stale not-yet-started requests from an old center at the front and makes
        // fast flight render the wake behind the player before the new horizon. Keep a tiny bounded
        // pending set and choose the nearest item at worker dequeue time instead. This is O(10) on a
        // background worker (MaxPendingGeneration == source WT), allocation-free and always centered
        // on the current player chunk, matching main.js nearest-first scheduling.
        readonly List<ChunkCoord> generationWork = new List<ChunkCoord>(16);
        readonly object generationWorkLock = new object();
        ChunkCoord generationPriorityCenter;
        readonly ConcurrentQueue<MeshWorkerJob> meshWork = new ConcurrentQueue<MeshWorkerJob>();
        readonly SemaphoreSlim generationSignal = new SemaphoreSlim(0);
        readonly SemaphoreSlim meshSignal = new SemaphoreSlim(0);
        readonly ManualResetEventSlim generationBurstGate = new ManualResetEventSlim(false);
        readonly ManualResetEventSlim meshBurstGate = new ManualResetEventSlim(false);
        Thread[] generationThreads, meshThreads;
        volatile bool workersStopping;
        int baseGenerationJobs, baseMeshJobs, burstGenerationJobs, burstMeshJobs, backgroundWorkerBudget;
        float workerPressureCooldown;
        bool adaptiveWorkerBoost;
        public bool AdaptiveWorkerBoost => adaptiveWorkerBoost;
        public int BaseGenerationJobs => baseGenerationJobs;
        public int BaseMeshJobs => baseMeshJobs;

        public void Configure(Transform player, Material opaque, Material water, Material transparent, Material xrayTerrain, int seed, int renderDistance, int[] sourceEdits = null)
        {
            Instance = this;
            MainBlockUpdates.BindMainThread();
            Player = player;
            unchecked{worldQueryRevision++;}
            Seed = seed;
            RenderDistance = Mathf.Clamp(renderDistance, 2, DeviceRenderCap());
            GenerationDistance = RenderDistance + 1;
            BuildGenerationOffsets();
            // main.js uses the SAME GI width for genWorker and meshWorker pools:
            // clamp(hardwareConcurrency - 1, 2..4). Native Unity cannot copy that literally on a
            // low-core host because its main/render/driver threads share the same scheduler. Keep the
            // source's symmetric fixed pools and nearest-only queues, but reserve CPU on 4/6-thread
            // machines: 4 logical -> 2+2, 6 -> 3+3, 8+ -> 4+4. No runtime boost/steering.
            int cores=Mathf.Max(1,SystemInfo.processorCount);
            int sourceWorkers=Mathf.Clamp(cores-1,2,4);
            int unityWorkers=cores<=2?1:Mathf.Min(sourceWorkers,Mathf.Max(2,cores/2));
            // Generation is by far the most expensive stage (~10x a chunk mesh after the mesher/light
            // optimisations), so it gets every core not needed by the main/render threads. Mesh workers
            // keep the source-sized pool. Worker threads run BelowNormal, so they yield to the frame.
            int genWorkers=cores<=2?1:Mathf.Clamp(cores-2,unityWorkers,8);
            baseGenerationJobs=genWorkers;
            baseMeshJobs=unityWorkers;
            burstGenerationJobs=genWorkers;
            burstMeshJobs=unityWorkers;
            backgroundWorkerBudget=genWorkers+unityWorkers;
            MaxGenerationJobs=genWorkers;
            MaxMeshJobs=unityWorkers;
            // main.js keeps WT=10 requests in flight for its 2-4 workers; keep the same ~3 requests per
            // worker so a wider native pool is never starved by the request window.
            MaxPendingGeneration=Mathf.Max(10,genWorkers*3);
            adaptiveWorkerBoost=false;
            Generator = new ChunkGenerator(seed);
            LoadPersistentEditsSource(sourceEdits);
            materials = new[] { opaque, water, transparent }; // main GPU split: terrain(o), water(w), transparent(t)
            xrayMaterials = new[] { xrayTerrain, water, transparent }; // only terrain(o) switches; water/glass remain source-normal.
            center = player!=null
                ? ChunkCoord.FromWorld(Mathf.FloorToInt(player.position.x),Mathf.FloorToInt(player.position.z))
                : new ChunkCoord(0,0);
            streamingPlayer=player!=null?player.GetComponent<MainPlayerController>():null;
            streamingCamera=player!=null?player.GetComponentInChildren<Camera>():Camera.main;
            UpdateStreamingViewDirection();
            generationLiveCenter=center;
            lock(generationWorkLock)
            {
                generationPriorityCenter=center;
                generationLiveCenter=center;
            }
            publishedRenderRadius=0;publishedRenderRadiusDirty=true;
            initialCenter=center;
            InitialWorldReady=false;
            initialGenerationCompletedInvoked=false;
            InitialLoadProgress=0f;
            InitialLoadPhase="Generating world";
            BuildInitialLoadTargets();
            Camera directCam=streamingCamera!=null?streamingCamera:(player!=null?player.GetComponentInChildren<Camera>():Camera.main);
            if(directCam!=null)
            {
                directRenderer=directCam.GetComponent<MainChunkDirectRenderer>();
                if(directRenderer==null)directRenderer=directCam.gameObject.AddComponent<MainChunkDirectRenderer>();
                directRenderer.Initialize(this,directCam);
            }
            StartDedicatedWorkers(baseGenerationJobs,baseMeshJobs);
            QueueInitialGeneration();
            UpdateCameraRange();
            MarkRenderCommandsDirty(true);
        }

        void OnDestroy()
        {
            workersStopping=true;
            // Release every sleeping worker. Threads are background threads, so Editor/player shutdown
            // never waits on a long generation/meshing item.
            int gn=generationThreads==null?0:generationThreads.Length;
            int mn=meshThreads==null?0:meshThreads.Length;
            generationBurstGate.Set();meshBurstGate.Set();
            if(gn>0)generationSignal.Release(gn);
            if(mn>0)meshSignal.Release(mn);
            if(directRenderer!=null)directRenderer.ClearWorld(this);
            if (Instance == this) Instance = null;
            foreach (var r in renders.Values) r.Dispose();
            renders.Clear();
        }

        void StartDedicatedWorkers(int generationCount,int meshCount)
        {
            if(generationThreads!=null||meshThreads!=null)return;
            workersStopping=false;
            generationThreads=new Thread[Mathf.Max(1,generationCount)];
            meshThreads=new Thread[Mathf.Max(1,meshCount)];
            for(int i=0;i<generationThreads.Length;i++)
            {
                int workerIndex=i;
                generationThreads[i]=new Thread(()=>GenerationWorkerLoop(workerIndex))
                {
                    IsBackground=true,
                    Name="Blockcraft Gen "+i,
                    Priority=System.Threading.ThreadPriority.BelowNormal
                };
                generationThreads[i].Start();
            }
            for(int i=0;i<meshThreads.Length;i++)
            {
                int workerIndex=i;
                meshThreads[i]=new Thread(()=>MeshWorkerLoop(workerIndex))
                {
                    IsBackground=true,
                    Name="Blockcraft Mesh "+i,
                    Priority=System.Threading.ThreadPriority.BelowNormal
                };
                meshThreads[i].Start();
            }
        }

        bool TryTakeGenerationWork(out ChunkCoord c)
        {
            lock(generationWorkLock)
            {
                int count=generationWork.Count;
                if(count==0){c=default;return false;}
                ChunkCoord live=generationLiveCenter;
                int best=0;
                ChunkCoord q0=generationWork[0];
                int dx=q0.X-live.X,dz=q0.Z-live.Z;
                int bestTier=ViewTier(dx,dz),bestD=dx*dx+dz*dz;
                for(int i=1;i<count;i++)
                {
                    ChunkCoord q=generationWork[i];
                    dx=q.X-live.X;dz=q.Z-live.Z;int d=dx*dx+dz*dz,tier=ViewTier(dx,dz);
                    // Correctness under flight: generated-but-unmeshed world data is exactly what
                    // appears as a visual hole. Distance remains primary; camera direction only
                    // replaces source X/Z insertion order when two requests are equally near.
                    if(d<bestD||(d==bestD&&tier<bestTier)){bestTier=tier;bestD=d;best=i;}
                }
                c=generationWork[best];
                generationWork.RemoveAt(best);
                return true;
            }
        }

        void GenerationWorkerLoop(int workerIndex)
        {
            bool burstOnly=workerIndex>=baseGenerationJobs;
            while(true)
            {
                if(burstOnly)generationBurstGate.Wait();
                if(workersStopping)return;
                generationSignal.Wait();
                if(workersStopping)return;
                // The gate may have closed while this worker was sleeping on the work semaphore. Put
                // the token back so a baseline worker can consume it; the extra thread then parks.
                if(burstOnly&&!generationBurstGate.IsSet){generationSignal.Release();continue;}
                if(!TryTakeGenerationWork(out var c))continue;
                try
                {
                    ChunkColumn column=Generator.Generate(c);
                    if(ApplyPersistentEdits(column))
                    {
                        // ChunkGenerator bakes local light before returning. Source applies saved edits
                        // before its final lighting state, so rebake only edited chunks here. The R40
                        // startup pass will still do the full-square deterministic light rebuild later.
                        VoxelLighting.BakeColumn(column);
                        column.Revision=0;
                    }
                    generated.Enqueue(column);
                }
                catch(Exception ex){ errors.Enqueue("Generator: "+ex); }
                finally
                {
                    byte x; generating.TryRemove(c,out x);
                    Interlocked.Decrement(ref activeGen);
                }
            }
        }

        void MeshWorkerLoop(int workerIndex)
        {
            bool burstOnly=workerIndex>=baseMeshJobs;
            while(true)
            {
                if(burstOnly)meshBurstGate.Wait();
                if(workersStopping)return;
                meshSignal.Wait();
                if(workersStopping)return;
                if(burstOnly&&!meshBurstGate.IsSet){meshSignal.Release();continue;}
                if(!meshWork.TryDequeue(out var job))continue;
                ProcessMeshWorkerJob(job);
            }
        }

        void UpdateAdaptiveWorkerBudget(float frameMs)
        {
            // R70 source parity: worker pool width is fixed for the life of the world, exactly like
            // main.js WebWorker pools. Frame pacing is controlled by the shared per-frame deadline,
            // not by opening/closing worker threads based on transient frame time.
            adaptiveWorkerBoost=false;
            MaxGenerationJobs=baseGenerationJobs;
            MaxMeshJobs=baseMeshJobs;
            generationBurstGate.Reset();
            meshBurstGate.Reset();
        }

        void Update()
        {
            if (Player == null || Generator == null) return;
            // main et[] neighbour callbacks for fluid edits committed by last frame's simulation.
            MainBlockUpdates.Drain(this);
            UpdateStreamingViewDirection();
            if(!InitialWorldReady)
            {
                UpdateInitialPreload();
                return;
            }
            double workStartMs=NowMs();
            double frameMs=Time.unscaledDeltaTime*1000.0;
            UpdateAdaptiveWorkerBudget((float)frameMs);
            // main.js derives its deadline from the requestAnimationFrame interval, which the browser
            // locks to the display refresh. Unity runs uncapped (vSyncCount 0), so deriving the budget
            // from the *measured* frame time created a feedback loop: streaming work filled 75% of the
            // frame, the frame got longer, the budget grew with it and FPS settled at ~4x the base frame
            // cost whenever work was queued. Use the display interval instead, capped to a fixed slice.
            sourceRefreshMs=DisplayRefreshMs();
            double workBudgetMs=Math.Min(Math.Max(sourceRefreshMs*.75,sourceRefreshMs-5.0),MaxStreamingSliceMs);
            double deadlineMs=workStartMs+workBudgetMs;
            LastStreamingBudgetMs=(float)workBudgetMs;

            HandleRenderDistanceKeys();
            string err;
            while (errors.TryDequeue(out err)) UnityEngine.Debug.LogWarning(err);

            LastGenerationIntegrateMs=LastChunkLightWorkMs=LastLightWorkMs=LastMeshIntegrateMs=LastMeshScheduleMs=LastMeshUploadMs=0f;
            LastMeshUploadBytes=0;
            LastMeshUploadBatchBytes=0;
            // Fixed-step simulation, independent of render FPS. A water tick computes its complete
            // source batch into a local overlay and commits the final mutations as one transaction;
            // relight/remesh invalidation is deduplicated after the batch instead of per voxel write.
            double waterStart=NowMs();
            using(WaterMarker.Auto())AdvanceSourceWater(Time.unscaledDeltaTime,deadlineMs);
            LastWaterWorkMs=(float)(NowMs()-waterStart);
            if(LastWaterWorkMs>MaxWaterWorkMs)MaxWaterWorkMs=LastWaterWorkMs;
            double lavaStart=NowMs();
            using(LavaMarker.Auto())AdvanceSourceLava(Time.unscaledDeltaTime,deadlineMs);
            LastLavaWorkMs=(float)(NowMs()-lavaStart);
            if(LastLavaWorkMs>MaxLavaWorkMs)MaxLavaWorkMs=LastLavaWorkMs;
            double phaseStart=NowMs();
            using(IntegrateGeneratedMarker.Auto()) IntegrateGenerated(deadlineMs);
            LastGenerationIntegrateMs=(float)(NowMs()-phaseStart);
            // main.js chunk phase al(frameDeadline): publish completed generation above, then feed the
            // bounded nearest-first worker window before lighting/mesh so background generation can run
            // in parallel with the rest of this frame.
            Stream(deadlineMs);
            // Runtime streaming has an explicit publish -> cross-chunk-light -> mesh pipeline.
            // Generation integration only publishes the new column reference. Border propagation is
            // its own atomic stage, and mesh scheduling refuses to snapshot a neighbourhood while any
            // member is waiting for that stage. This prevents a large StitchNeighbors pass from being
            // hidden inside generation integration and gives stale worker meshes a clean version barrier.
            phaseStart=NowMs();
            using(ChunkLightMarker.Auto()) ProcessChunkLighting(deadlineMs);
            float chunkLightMs=(float)(NowMs()-phaseStart);
            LastChunkLightWorkMs=chunkLightMs;
            // main.js processes G4() lighting before ol() mesh integration/scheduling. If an old
            // worker result was built from pre-edit light, the light pass bumps its section stamp
            // before integration so that stale mesh can never flash for one frame.
            phaseStart=NowMs();
            using(LightMarker.Auto()) ProcessLightEdits(deadlineMs);
            LastLightWorkMs=(float)(NowMs()-phaseStart);
            using(MeshIntegrateMarker.Auto()) IntegrateMeshesAndUploads(deadlineMs);

            phaseStart=NowMs();
            using(MeshScheduleMarker.Auto()) ScheduleMeshes(deadlineMs);
            LastMeshScheduleMs=(float)(NowMs()-phaseStart);
            using(MaintenanceMarker.Auto()) ProcessDeferredMaintenance(deadlineMs);
            LastStreamingWorkMs=(float)(NowMs()-workStartMs);
        }

        void UpdateStreamingViewDirection()
        {
            // Unity objects are touched only on the main thread. Generation workers consume only the
            // two volatile horizontal direction scalars; mesh selection also uses a main-thread copy
            // of the exact camera frustum so already-loaded visible chunks can be published first.
            Camera cam=streamingCamera;
            if(cam==null){cam=Player!=null?Player.GetComponentInChildren<Camera>():Camera.main;streamingCamera=cam;}
            if(cam!=null)
            {
                ExtractSourceFrustum(cam.cullingMatrix,streamingPriorityFrustumPlanes);
                streamingPriorityFrustumValid=true;
            }
            else streamingPriorityFrustumValid=false;
            Vector3 f=cam!=null?cam.transform.forward:(Player!=null?Player.forward:Vector3.forward);
            float n=f.x*f.x+f.z*f.z;
            // Looking almost vertically should not randomize the horizontal generation cone. Mesh
            // priority remains correct in that case because it uses the full camera frustum above.
            if(n<.04f&&Player!=null){f=Player.forward;n=f.x*f.x+f.z*f.z;}
            if(n<1e-5f){streamingViewX=0f;streamingViewZ=1f;return;}
            float inv=1f/Mathf.Sqrt(n);streamingViewX=f.x*inv;streamingViewZ=f.z*inv;
        }

        int ViewTier(int dx,int dz)
        {
            int d2=dx*dx+dz*dz;
            // Generation keeps the R79/source-nearest contract: only equal-distance ties are biased
            // toward the forward cone. Mesh publication has a stronger visible-first tier below.
            if(d2<=4)return 0;
            float dot=dx*streamingViewX+dz*streamingViewZ;
            return dot>0f&&dot*dot>=d2*.18f?1:2;
        }

        int MeshViewTier(ChunkCoord c,int dx,int dz)
        {
            // R81 correctness/perception rule: the immediate 5x5 around the player is always first.
            // Outside it, a loaded chunk whose real non-air bounds intersect the current camera
            // frustum outranks off-screen chunks, regardless of squared distance. This does not add
            // work or fake geometry; it only stops existing block data from waiting behind meshes
            // that cannot currently be seen. main.js can rely on very cheap WebGL worker uploads;
            // Unity native Mesh uploads are costlier, so publication order must follow presentation.
            if(Math.Max(Math.Abs(dx),Math.Abs(dz))<=2)return 0;
            if(streamingPriorityFrustumValid&&chunks.TryGetValue(c,out var col)&&
               TryColumnRenderBounds(col,out Vector3 mn,out Vector3 mx)&&
               FastFrustumAabb(streamingPriorityFrustumPlanes,mn,mx))return 1;
            int d2=dx*dx+dz*dz;
            float dot=dx*streamingViewX+dz*streamingViewZ;
            return dot>0f&&dot*dot>=d2*.18f?2:3;
        }

        void BuildInitialLoadTargets()
        {
            initialGenerationTargets.Clear();
            initialMeshTargets.Clear();
            int genRadius=Math.Min(GenerationDistance,4); // main: min(GEN_R, 4)
            int meshRadius=Math.Min(RenderDistance,3);   // main: min(RENDER_R, 3)
            for(int i=0;i<generationOffsets.Count;i++)
            {
                Vector2Int o=generationOffsets[i];
                if(Math.Abs(o.x)<=genRadius&&Math.Abs(o.y)<=genRadius)
                    initialGenerationTargets.Add(new ChunkCoord(initialCenter.X+o.x,initialCenter.Z+o.y));
                if(Math.Abs(o.x)<=meshRadius&&Math.Abs(o.y)<=meshRadius)
                    initialMeshTargets.Add(new ChunkCoord(initialCenter.X+o.x,initialCenter.Z+o.y));
            }
            InitialGeneratedTarget=initialGenerationTargets.Count;
            InitialMeshedTarget=initialMeshTargets.Count;
            initialLoadStage=InitialLoadStage.Generate;
            initialLightIndex=0;
            initialGenerationQueued=false;
        }

        void RecenterInitialMeshTargetsToPlayer()
        {
            // Source startup generates around the provisional/coarse anchor, then pg() moves Q.pos,
            // and Ce() builds the first render square around the FINAL spawn. Keep those centers
            // separate: generation targets stay untouched; only mesh targets and runtime center move.
            if(Player==null)return;
            center=ChunkCoord.FromWorld(Mathf.FloorToInt(Player.position.x),Mathf.FloorToInt(Player.position.z));
            initialMeshTargets.Clear();
            int meshRadius=Math.Min(RenderDistance,3);
            for(int i=0;i<generationOffsets.Count;i++)
            {
                Vector2Int o=generationOffsets[i];
                if(Math.Abs(o.x)<=meshRadius&&Math.Abs(o.y)<=meshRadius)
                {
                    ChunkCoord target=new ChunkCoord(center.X+o.x,center.Z+o.y);
                    // main Ce() only submits initial mesh work for chunks already present in rA.chunks.
                    // If pg() moved near the outer edge of the generated square, steady-state streaming
                    // fills the remaining view after the live gate instead of deadlocking startup.
                    if(chunks.ContainsKey(target))initialMeshTargets.Add(target);
                }
            }
            InitialMeshedTarget=initialMeshTargets.Count;
            InitialMeshedChunks=0;
        }

        void QueueInitialGeneration()
        {
            if(initialGenerationQueued)return;
            initialGenerationQueued=true;
            // main submits the whole sorted startup square before gameplay becomes live. Dedicated
            // workers still bound actual CPU concurrency; queuing all targets removes the 120 ms
            // steady-state stream cadence from the loading path.
            for(int i=0;i<initialGenerationTargets.Count;i++)
            {
                ChunkCoord c=initialGenerationTargets[i];
                if(!chunks.ContainsKey(c)&&!generating.ContainsKey(c))StartGenerate(c);
            }
        }

        void UpdateInitialPreload()
        {
            string err;while(errors.TryDequeue(out err))UnityEngine.Debug.LogWarning(err);
            double start=NowMs();
            double deadline=start+(Application.isMobilePlatform?8.0:12.0);
            LastGenerationIntegrateMs=LastChunkLightWorkMs=LastLightWorkMs=LastMeshIntegrateMs=LastMeshScheduleMs=LastMeshUploadMs=0f;
            LastMeshUploadBytes=0;
            LastMeshUploadBatchBytes=0;

            if(initialLoadStage==InitialLoadStage.Generate)
            {
                QueueInitialGeneration();
                double t=NowMs();
                using(IntegrateGeneratedMarker.Auto())IntegrateGenerated(deadline);
                LastGenerationIntegrateMs=(float)(NowMs()-t);
                InitialGeneratedChunks=CountLoaded(initialGenerationTargets);
                InitialLoadProgress=InitialGeneratedTarget==0?.5f:.5f*InitialGeneratedChunks/InitialGeneratedTarget;
                InitialLoadPhase="Generating world";
                if(InitialGeneratedChunks>=InitialGeneratedTarget)
                {
                    if(!initialGenerationCompletedInvoked)
                    {
                        initialGenerationCompletedInvoked=true;
                        Action<VoxelWorld> resolveSpawn=InitialGenerationCompleted;
                        InitialGenerationCompleted=null;
                        if(resolveSpawn!=null)
                        {
                            try{resolveSpawn(this);}
                            catch(Exception ex){Debug.LogException(ex);}
                        }
                        RecenterInitialMeshTargetsToPlayer();
                    }
                    initialLoadStage=InitialLoadStage.RebuildLight;
                    initialLightIndex=0;
                    InitialLoadPhase="Lighting world";
                }
                LastStreamingWorkMs=(float)(NowMs()-start);
                return;
            }

            if(initialLoadStage==InitialLoadStage.RebuildLight)
            {
                // ChunkGenerator already bakes local light off-thread. Re-bake after the complete
                // startup square exists so the following border propagation starts from one clean,
                // deterministic state just like main's post-generation Es()/t1() phase.
                double t=NowMs();
                while(initialLightIndex<initialGenerationTargets.Count&&NowMs()<deadline-1.0)
                {
                    if(chunks.TryGetValue(initialGenerationTargets[initialLightIndex],out var c))VoxelLighting.BakeColumn(c);
                    initialLightIndex++;
                }
                LastLightWorkMs=(float)(NowMs()-t);
                float f=initialGenerationTargets.Count==0?1f:(float)initialLightIndex/initialGenerationTargets.Count;
                InitialLoadProgress=.5f+.075f*f;
                InitialLoadPhase="Lighting world";
                if(initialLightIndex>=initialGenerationTargets.Count)
                {
                    initialLoadStage=InitialLoadStage.StitchLight;
                    initialLightIndex=0;
                }
                LastStreamingWorkMs=(float)(NowMs()-start);
                return;
            }

            if(initialLoadStage==InitialLoadStage.StitchLight)
            {
                double t=NowMs();
                while(initialLightIndex<initialGenerationTargets.Count&&NowMs()<deadline-1.0)
                {
                    ChunkCoord c=initialGenerationTargets[initialLightIndex++];
                    if(chunks.ContainsKey(c))
                    {
                        VoxelLighting.StitchNeighbors(chunks,c,lightDirtyChunksScratch);
                    }
                }
                LastLightWorkMs=(float)(NowMs()-t);
                float f=initialGenerationTargets.Count==0?1f:(float)initialLightIndex/initialGenerationTargets.Count;
                InitialLoadProgress=.575f+.075f*f;
                InitialLoadPhase="Lighting world";
                if(initialLightIndex>=initialGenerationTargets.Count)
                {
                    // No mesh worker has been started during generation/light phases. Queue one
                    // final whole-chunk build from the completed light state for the source startup
                    // render square only. The outer generated ring exists solely as mesh neighbours.
                    for(int i=0;i<initialMeshTargets.Count;i++)DirtyWholeChunk(initialMeshTargets[i]);
                    initialLoadStage=InitialLoadStage.Mesh;
                    InitialLoadPhase="Building world";
                }
                LastStreamingWorkMs=(float)(NowMs()-start);
                return;
            }

            if(initialLoadStage==InitialLoadStage.Mesh)
            {
                double t=NowMs();
                using(MeshIntegrateMarker.Auto())IntegrateMeshesAndUploads(deadline);
                LastMeshIntegrateMs=(float)(NowMs()-t);
                t=NowMs();
                using(MeshScheduleMarker.Auto())ScheduleMeshes(deadline);
                LastMeshScheduleMs=(float)(NowMs()-t);
                InitialMeshedChunks=CountInitialMeshReady();
                InitialLoadProgress=.65f+(InitialMeshedTarget==0?.35f:.35f*InitialMeshedChunks/InitialMeshedTarget);
                InitialLoadPhase="Building world";
                if(InitialMeshedChunks>=InitialMeshedTarget)FinishInitialPreload();
                LastStreamingWorkMs=(float)(NowMs()-start);
            }
        }

        int CountLoaded(List<ChunkCoord> list)
        {
            int n=0;for(int i=0;i<list.Count;i++)if(chunks.ContainsKey(list[i]))n++;return n;
        }

        int CountInitialMeshReady()
        {
            int ready=0;
            for(int i=0;i<initialMeshTargets.Count;i++)
            {
                ChunkCoord c=initialMeshTargets[i];
                if(!chunks.TryGetValue(c,out var col)||HasPendingMesh(c))continue;
                bool hasGeometry=false;
                for(int s=0;s<VoxelConstants.SectionCount;s++)if(col.Sections[s]!=null&&col.Sections[s].NonAir>0){hasGeometry=true;break;}
                if(!hasGeometry){ready++;continue;}
                if(renders.TryGetValue(c,out var cr)&&!cr.NeedsUpload&&cr.Drawable)ready++;
            }
            return ready;
        }

        void FinishInitialPreload()
        {
            InitialWorldReady=true;
            initialLoadStage=InitialLoadStage.Ready;
            InitialLoadProgress=1f;
            InitialLoadPhase="Ready";
            // Expanding InRenderRadius after the gate opens promotes the already-generated radius-4
            // ring and normal streaming then continues toward the user's full render/generation distance.
            RepartitionMeshQueue();
            UpdateRenderVisibility();
            Debug.Log("Initial world ready: "+InitialGeneratedChunks+" generated, "+InitialMeshedChunks+" meshed");
        }

        static double NowMs()=>Time.realtimeSinceStartupAsDouble*1000.0;

        public void SetRenderDistance(int chunksDistance)
        {
            int next=Mathf.Clamp(chunksDistance,2,DeviceRenderCap());
            if(next==RenderDistance)return;
            int oldRenderDistance=RenderDistance;
            RenderDistance=next;
            publishedRenderRadiusDirty=true;
            GenerationDistance=RenderDistance+1;
            BuildGenerationOffsets();
            PlayerPrefs.SetInt(RenderDistancePrefKey,RenderDistance);
            UpdateCameraRange();
            QueueRenderMaintenanceForDistanceChange(oldRenderDistance,RenderDistance);
            RepartitionMeshQueue();
            UpdateRenderVisibility();
            Debug.Log("Render distance: "+RenderDistance+" chunks");
        }

        public static int SavedRenderDistance()
        {
            int value=PlayerPrefs.GetInt(RenderDistancePrefKey,6);
            int cap=DeviceRenderCap();
            for(int i=SourceRenderDistances.Length-1;i>=0;i--)
                if(SourceRenderDistances[i]<=cap&&SourceRenderDistances[i]<=value)return SourceRenderDistances[i];
            return Mathf.Min(4,cap);
        }

        void HandleRenderDistanceKeys()
        {
            // The WebGL build exposes the source preset list in Settings. For desktop validation in
            // Unity the same list is also keyboard-accessible: [ decreases, ] increases.
            if(Input.GetKeyDown(KeyCode.LeftBracket)||Input.GetKeyDown(KeyCode.Minus))StepRenderDistance(-1);
            else if(Input.GetKeyDown(KeyCode.RightBracket)||Input.GetKeyDown(KeyCode.Equals))StepRenderDistance(1);
        }

        public void StepRenderDistance(int direction)
        {
            if(direction==0)return;
            int cap=DeviceRenderCap();
            if(direction>0)
            {
                for(int i=0;i<SourceRenderDistances.Length;i++)
                    if(SourceRenderDistances[i]>RenderDistance&&SourceRenderDistances[i]<=cap)
                    {
                        SetRenderDistance(SourceRenderDistances[i]);
                        return;
                    }
            }
            else
            {
                for(int i=SourceRenderDistances.Length-1;i>=0;i--)
                    if(SourceRenderDistances[i]<RenderDistance&&SourceRenderDistances[i]<=cap)
                    {
                        SetRenderDistance(SourceRenderDistances[i]);
                        return;
                    }
            }
        }

        public void SetXray(bool enabled)
        {
            if(xrayEnabled==enabled)return;
            xrayEnabled=enabled;
            // X-Ray changes both chunk terrain passes and entity/held overlay materials.
            MarkRenderCommandsDirty();
            MarkRenderOverlayDirty();
        }

        public static int DeviceRenderCap()
        {
            int mb = SystemInfo.systemMemorySize;
            if (mb > 0 && mb <= 2048) return 8;
            if ((mb > 0 && mb <= 4096) || Application.isMobilePlatform) return 12;
            return 20;
        }

        void UpdateCameraRange()
        {
            Camera cam = Player != null ? Player.GetComponentInChildren<Camera>() : Camera.main;
            if (cam != null) cam.farClipPlane = Mathf.Max(420f, RenderDistance * 16f * 2f);
        }

        static int CompareGenerationOffset(Vector2Int a,Vector2Int b)
        {
            int da=a.x*a.x+a.y*a.y,db=b.x*b.x+b.y*b.y;
            if(da!=db)return da.CompareTo(db);
            // main al(): candidates are inserted with X as the outer loop and Z as the inner loop,
            // then stable-sorted by d only. Encode that stable tie order explicitly so Unity/.NET
            // collection details cannot bias one horizon side.
            if(a.x!=b.x)return a.x.CompareTo(b.x);
            return a.y.CompareTo(b.y);
        }

        void BuildGenerationOffsets()
        {
            generationOffsets.Clear();
            for(int dx=-GenerationDistance;dx<=GenerationDistance;dx++)for(int dz=-GenerationDistance;dz<=GenerationDistance;dz++)generationOffsets.Add(new Vector2Int(dx,dz));
            generationOffsets.Sort(CompareGenerationOffset);
        }

        int FeedGenerationWindow(ChunkCoord baseCenter,int slots,double deadlineMs)
        {
            // Preserve source squared-distance priority exactly. Only equal-distance candidates are
            // reordered toward the current camera view, replacing source's arbitrary X/Z tie order.
            // This cannot make a farther chunk jump over a nearer one and therefore keeps the loaded
            // collision world spatially coherent while publishing the visible half of each ring first.
            int oi=0;
            while(oi<generationOffsets.Count&&slots>0)
            {
                Vector2Int first=generationOffsets[oi];int groupD=first.x*first.x+first.y*first.y,end=oi+1;
                while(end<generationOffsets.Count)
                {
                    Vector2Int q=generationOffsets[end];if(q.x*q.x+q.y*q.y!=groupD)break;end++;
                }
                for(int tier=0;tier<3&&slots>0;tier++)
                {
                    for(int j=oi;j<end&&slots>0;j++)
                    {
                        if(NowMs()>deadlineMs-1.0)return slots;
                        Vector2Int o=generationOffsets[j];if(ViewTier(o.x,o.y)!=tier)continue;
                        var q=new ChunkCoord(baseCenter.X+o.x,baseCenter.Z+o.y);
                        if(chunks.ContainsKey(q)||generating.ContainsKey(q))continue;
                        StartGenerate(q);slots--;
                    }
                }
                oi=end;
            }
            return slots;
        }

        void Stream(double deadlineMs)
        {
            var c = ChunkCoord.FromWorld(Mathf.FloorToInt(Player.position.x), Mathf.FloorToInt(Player.position.z));
            ChunkCoord previousCenter=center;
            bool centerChanged = !c.Equals(center);
            center = c;
            if(centerChanged)
            {
                lock(generationWorkLock)
                {
                    generationPriorityCenter=c;
                    generationLiveCenter=c;
                }
                if(centerChanged)publishedRenderRadiusDirty=true;
            }

            if(centerChanged)
            {
                unloadScratch.Clear();
                // Distance cannot change while the player remains in the same chunk, so do this cache scan
                // only on a chunk transition rather than on every per-frame Stream() call. Actual destruction is deferred.
                int lim = RenderDistance + 8;
                foreach (var kv in chunks)
                {
                    int dx = Math.Abs(kv.Key.X - c.X), dz = Math.Abs(kv.Key.Z - c.Z);
                    if (dx > lim || dz > lim) unloadScratch.Add(kv.Key);
                }
                for (int i=0;i<unloadScratch.Count;i++)
                {
                    var old=unloadScratch[i];if(unloadQueued.Add(old))unloadQueue.Enqueue(old);
                }
            }

            if (centerChanged)
            {
                QueueRenderMaintenanceForCenterMove(previousCenter,c);
                UpdateRenderVisibility();
            }
            if(centerChanged)RepartitionMeshQueue();

            // Source al(deadline): feed the bounded generation request window every frame, but do not
            // spend the final 1 ms of the shared streaming deadline scanning for new requests. The
            // offsets are pre-sorted by squared distance, so this is allocation-free and equivalent
            // to main's nearest-first candidate sort while avoiding a per-frame temporary array.
            if(NowMs()>deadlineMs-1.0)return;
            int genSlots=Mathf.Max(0,MaxPendingGeneration-generating.Count);
            if(genSlots<=0)return;
            FeedGenerationWindow(c,genSlots,deadlineMs);
        }

        // BC_TEMP_DEBUG_BEGIN [GPU_READY_RADIUS_AND_FRONTIER_CLASSIFIER] REMOVE BEFORE RELEASE
        void RecomputePublishedRenderRadius()
        {
            publishedRenderRadiusDirty=false;
            PublishedFrontierMissingData=0;PublishedFrontierMissingMesh=0;
            int max=Mathf.Max(0,RenderDistance);
            int ready=-1;
            // Only scan Chebyshev rings when publication state changes. On the first incomplete ring,
            // also classify the cause: no world column yet vs world blocks present but no drawable GPU
            // mesh. This makes the exact class of a reported "visual hole" visible in F3.
            for(int r=0;r<=max;r++)
            {
                bool ringReady=true;
                if(r==0)
                {
                    bool data=chunks.ContainsKey(center),mesh=ChunkHasPublishedGpuMesh(center);
                    ringReady=mesh;if(!ringReady){if(!data)PublishedFrontierMissingData++;else PublishedFrontierMissingMesh++;}
                }
                else
                {
                    for(int d=-r;d<=r;d++)
                    {
                        CountPublicationGap(new ChunkCoord(center.X+d,center.Z-r),ref ringReady);
                        CountPublicationGap(new ChunkCoord(center.X+d,center.Z+r),ref ringReady);
                    }
                    for(int d=-r+1;d<=r-1;d++)
                    {
                        CountPublicationGap(new ChunkCoord(center.X-r,center.Z+d),ref ringReady);
                        CountPublicationGap(new ChunkCoord(center.X+r,center.Z+d),ref ringReady);
                    }
                }
                if(!ringReady)break;
                ready=r;
            }
            publishedRenderRadius=Mathf.Max(0,ready);
        }

        void CountPublicationGap(ChunkCoord c,ref bool ringReady)
        {
            if(ChunkHasPublishedGpuMesh(c))return;
            ringReady=false;
            if(chunks.ContainsKey(c))PublishedFrontierMissingMesh++;
            else PublishedFrontierMissingData++;
        }

        // BC_TEMP_DEBUG_END [GPU_READY_RADIUS_AND_FRONTIER_CLASSIFIER]

        bool ChunkHasPublishedGpuMesh(ChunkCoord c)
        {
            return renders.TryGetValue(c,out var cr)&&cr!=null&&cr.Drawable;
        }

        void UpdateRenderVisibility()
        {
            // Direct backend does not maintain thousands of Renderer visibility flags. Scan exactly
            // the source RENDER_R square and only wake pending GPU uploads that have become visible.
            int r=RenderDistance;
            for(int dx=-r;dx<=r;dx++)for(int dz=-r;dz<=r;dz++)
            {
                ChunkCoord cc=new ChunkCoord(center.X+dx,center.Z+dz);
                if(renders.TryGetValue(cc,out var cr)&&cr.NeedsUpload)QueueUpload(cc);
            }
            MarkRenderCommandsDirty(true);
        }

        void StartGenerate(ChunkCoord c)
        {
            if (!generating.TryAdd(c, 0)) return;
            Interlocked.Increment(ref activeGen);
            lock(generationWorkLock) generationWork.Add(c);
            generationSignal.Release();
        }

        void IntegrateGenerated(double deadlineMs)
        {
            ChunkColumn c;int integrated=0;
            while (NowMs()+estimatedChunkIntegrateMs<=deadlineMs)
            {
                // Publishing a generated column is intentionally cheap. Runtime cross-chunk lighting
                // is a separate pipeline stage below; startup has its own deterministic whole-square
                // lighting pass and therefore does not enqueue this runtime stage.
                if(integrated>0&&NowMs()+estimatedChunkIntegrateMs>deadlineMs)break;
                if(!generated.TryDequeue(out c))break;
                double t0=NowMs();
                integrated++;chunks[c.Coord]=c;unchecked{worldQueryRevision++;}
                // main.js steady-state integration does EP(chunk) immediately after publishing the
                // generated column. EP seeds exposed generated WATER/FLOW cells into waterSeedQueue,
                // so ordinary fE() water ticks can settle cave/ocean boundaries instead of leaving
                // permanent dry AIR pockets next to generated source water.
                // EP is a chunk-publication side effect, not a gameplay-ready side effect. Run it for
                // startup preload columns too; otherwise initial/saved chunks can enter the world with
                // exposed generated water but no seed in the source water queue.
                SeedGeneratedFluidEdges(c);
                // R76 deliberate improvement over main.js EP(chunk): EP only sees AIR inside the
                // freshly published chunk and therefore misses horizontal AIR/fluid contacts that
                // land exactly on a chunk seam. Scan only already-loaded neighbour seams and seed
                // the existing bounded source queues; the normal 200/500 ms simulators still own
                // all actual fluid mutations. This fixes persistent shoreline/cave seam pockets
                // without a flood-fill, missing-chunk-as-air rule, or render-path work.
                SeedLoadedNeighborFluidSeams(c);
                // main.js chunk integration calls eu(cx,cz) immediately after rA.chunks.set(...),
                // before cross-chunk lighting/meshing. Keep fauna seeding attached to the publication
                // event rather than to a later rendering stage, so lighting backlog cannot suppress it.
                if(MobSpawner.Instance!=null)MobSpawner.Instance.OnChunkLoaded(c.Coord);
                if(InitialWorldReady&&chunkLightPending.Add(c.Coord))chunkLightQueue.Enqueue(c.Coord);
                double cost=NowMs()-t0;estimatedChunkIntegrateMs+=(cost-estimatedChunkIntegrateMs)*.25;
                if(NowMs()>deadlineMs)break;
            }
        }

        void SeedGeneratedFluidEdges(ChunkColumn column)
        {
            // main.js EP(A). The source scans freshly integrated WATER/FLOW and LAVA/FLOW cells
            // and seed-queues exposed cells. Actual propagation remains on the normal 200/500 ms
            // fE()/gc() cadences; publication never performs a global runtime flood fill.
            int baseX=column.Coord.X*16,baseZ=column.Coord.Z*16;
            for(int sectionIndex=0;sectionIndex<column.Sections.Length;sectionIndex++)
            {
                ChunkSection section=column.Sections[sectionIndex];
                if(section==null)continue;
                ushort[] blocks=section.Blocks;
                ushort[] below=sectionIndex>0&&column.Sections[sectionIndex-1]!=null?column.Sections[sectionIndex-1].Blocks:null;
                for(int index=0;index<4096;index++)
                {
                    BlockId id=(BlockId)blocks[index];
                    bool seedWater=BlockRegistry.IsWater(id),seedLava=BlockRegistry.IsLava(id);
                    if(!seedWater&&!seedLava)continue;
                    int localY=index&15;
                    ushort belowId=localY>0?blocks[index-1]:(below!=null?below[index|15]:(ushort)0);
                    bool exposed=belowId==0||
                                 (index>=256&&blocks[index-256]==0)||
                                 (index<3840&&blocks[index+256]==0)||
                                 ((index&240)!=0&&blocks[index-16]==0)||
                                 ((index&240)!=240&&blocks[index+16]==0);
                    if(!exposed)continue;
                    int x=baseX+(index>>8);
                    int z=baseZ+((index>>4)&15);
                    int y=VoxelConstants.MinY+sectionIndex*16+localY;
                    if(seedWater)EnqueueWater(x,y,z,true);else EnqueueLava(x,y,z,true);
                }
            }
        }

        void SeedLoadedNeighborFluidSeams(ChunkColumn column)
        {
            // EP(chunk) intentionally mirrors source-local exposure checks. The source has one
            // blind spot: at local X/Z 0 or 15 it cannot see AIR in an adjacent chunk. When the
            // second half of a seam becomes loaded, inspect just that 16 x world-height plane.
            // Queue the fluid cell on either side if the opposite loaded cell is AIR. Missing
            // neighbours are never treated as AIR, so streaming order cannot make water leak into
            // not-yet-generated space. A seam is normally scanned once, when its second chunk is
            // published; HashSet-backed seed queues also make reload/revisit scans idempotent.
            SeedLoadedFluidSeam(column,-1,0);
            SeedLoadedFluidSeam(column, 1,0);
            SeedLoadedFluidSeam(column,0,-1);
            SeedLoadedFluidSeam(column,0, 1);
        }

        void SeedLoadedFluidSeam(ChunkColumn column,int dx,int dz)
        {
            ChunkCoord neighbourCoord=new ChunkCoord(column.Coord.X+dx,column.Coord.Z+dz);
            if(!chunks.TryGetValue(neighbourCoord,out ChunkColumn neighbour))return;

            int baseX=column.Coord.X*16,baseZ=column.Coord.Z*16;
            bool xSeam=dx!=0;
            int aEdge=xSeam?(dx<0?0:15):(dz<0?0:15);
            int bEdge=xSeam?(dx<0?15:0):(dz<0?15:0);

            for(int sectionIndex=0;sectionIndex<VoxelConstants.SectionCount;sectionIndex++)
            {
                ChunkSection aSection=column.Sections[sectionIndex];
                ChunkSection bSection=neighbour.Sections[sectionIndex];
                if(aSection==null&&bSection==null)continue;
                ushort[] a=aSection!=null?aSection.Blocks:null;
                ushort[] b=bSection!=null?bSection.Blocks:null;
                int yBase=VoxelConstants.MinY+(sectionIndex<<4);

                for(int lateral=0;lateral<16;lateral++)for(int localY=0;localY<16;localY++)
                {
                    int aIndex,bIndex,ax,az,bx,bz;
                    if(xSeam)
                    {
                        aIndex=(aEdge<<8)|(lateral<<4)|localY;
                        bIndex=(bEdge<<8)|(lateral<<4)|localY;
                        ax=baseX+aEdge; az=baseZ+lateral;
                        bx=ax+dx; bz=az;
                    }
                    else
                    {
                        aIndex=(lateral<<8)|(aEdge<<4)|localY;
                        bIndex=(lateral<<8)|(bEdge<<4)|localY;
                        ax=baseX+lateral; az=baseZ+aEdge;
                        bx=ax; bz=az+dz;
                    }

                    BlockId aId=a!=null?(BlockId)a[aIndex]:BlockId.Air;
                    BlockId bId=b!=null?(BlockId)b[bIndex]:BlockId.Air;
                    int y=yBase+localY;
                    if(aId==BlockId.Air)SeedFluidCellIfNeeded(bId,bx,y,bz);
                    else if(bId==BlockId.Air)SeedFluidCellIfNeeded(aId,ax,y,az);
                }
            }
        }

        void SeedFluidCellIfNeeded(BlockId id,int x,int y,int z)
        {
            if(BlockRegistry.IsWater(id))EnqueueWater(x,y,z,true);
            else if(BlockRegistry.IsLava(id))EnqueueLava(x,y,z,true);
        }

        void ProcessChunkLighting(double deadlineMs)
        {
            // Keep the exact Yu() result/order, but do not let one cross-chunk stitch consume the entire
            // streaming frame budget. R73 could spend ~9 ms here every frame on the user's capture.
            // This is cooperative scheduling only: job state persists and no nodes/quality are dropped.
            double lightDeadline=Math.Min(deadlineMs,NowMs()+(Application.isMobilePlatform?1.25:2.0));
            while(true)
            {
                if(activeChunkLightJob==null)
                {
                    if(chunkLightQueue.Count==0||NowMs()>=lightDeadline)return;
                    activeChunkLightCoord=chunkLightQueue.Dequeue();
                    if(!chunks.ContainsKey(activeChunkLightCoord))
                    {
                        chunkLightPending.Remove(activeChunkLightCoord);
                        continue;
                    }
                    activeChunkLightJob=VoxelLighting.BeginWorldStitch(activeChunkLightCoord);
                }

                if(!VoxelLighting.StepWorldStitch(chunks,activeChunkLightJob,lightDeadline))return;

                lightDirtyChunksScratch.Clear();
                foreach(var dc in activeChunkLightJob.DirtyChunks)lightDirtyChunksScratch.Add(dc);
                lightDirtyChunksScratch.Add(activeChunkLightCoord);
                foreach(var dc in lightDirtyChunksScratch)
                {
                    if(!chunks.TryGetValue(dc,out var litChunk))continue;
                    for(int sec=0;sec<VoxelConstants.SectionCount;sec++)
                        if(litChunk.Sections[sec]!=null&&litChunk.Sections[sec].NonAir>0)Dirty(new SectionKey(dc,sec));
                }
                MarkChunkEdgesDirty(activeChunkLightCoord);
                chunkLightPending.Remove(activeChunkLightCoord);
                activeChunkLightJob=null;
                if(NowMs()>=lightDeadline)return;
            }
        }

        void MarkChunkEdgesDirty(ChunkCoord c)
        {
            // Avoid a small managed array allocation for every integrated chunk.
            MarkChunkDirtyIfLoaded(new ChunkCoord(c.X + 1, c.Z));
            MarkChunkDirtyIfLoaded(new ChunkCoord(c.X - 1, c.Z));
            MarkChunkDirtyIfLoaded(new ChunkCoord(c.X, c.Z + 1));
            MarkChunkDirtyIfLoaded(new ChunkCoord(c.X, c.Z - 1));
        }

        void MarkChunkDirtyIfLoaded(ChunkCoord nc)
        {
            if (!chunks.TryGetValue(nc, out var ch)) return;
            for (int s = 0; s < VoxelConstants.SectionCount; s++)
                if (ch.Sections[s] != null && ch.Sections[s].NonAir > 0) Dirty(new SectionKey(nc, s));
        }

        void Dirty(SectionKey k)
        {
            // main.js drops CPU chunk.geo outside radius 2. If that happened here, a later edit/border
            // change must rebuild the full chunk before the next upload, not only one section.
            if (renders.TryGetValue(k.Chunk, out var cr) && !cr.HasCpuGeometry)
            {
                if (cr.FullCpuRebuildPending) DirtySingle(k);
                else DirtyWholeChunk(k.Chunk);
                return;
            }
            DirtySingle(k);
        }

        void DirtySingle(SectionKey k)
        {
            int s; sectionStamp.TryGetValue(k, out s); sectionStamp[k] = s + 1;
            if (InRenderRadius(k.Chunk)) EnqueueMesh(k);
            else deferredWholeChunks.Add(k.Chunk);
        }

        void EnqueueMesh(SectionKey k)
        {
            if((uint)k.Section>=VoxelConstants.SectionCount)return;
            if (meshQueued.Add(k))
            {
                meshQueuedMasks.TryGetValue(k.Chunk,out uint mask);meshQueuedMasks[k.Chunk]=mask|(1u<<k.Section);
                IncrementPendingMesh(k.Chunk);
            }
        }

        void RepartitionMeshQueue()
        {
            // Hot scheduling is tracked as one 24-bit section mask per chunk. Demote whole chunk masks
            // when they leave RENDER_R instead of compacting a potentially huge section list.
            meshMaskScratch.Clear();foreach(var kv in meshQueuedMasks)if(!InRenderRadius(kv.Key))meshMaskScratch.Add(kv.Key);
            for(int mi=0;mi<meshMaskScratch.Count;mi++)
            {
                ChunkCoord c=meshMaskScratch[mi];if(!meshQueuedMasks.TryGetValue(c,out uint mask))continue;meshQueuedMasks.Remove(c);
                for(int sec=0;sec<VoxelConstants.SectionCount;sec++)if((mask&(1u<<sec))!=0){var k=new SectionKey(c,sec);if(meshQueued.Remove(k))DecrementPendingMesh(c);}
                deferredWholeChunks.Add(c);
            }

            if(deferredWholeChunks.Count!=0)
            {
                promoteChunkScratch.Clear();
                foreach(var c in deferredWholeChunks)if(InRenderRadius(c))promoteChunkScratch.Add(c);
                for(int i=0;i<promoteChunkScratch.Count;i++)
                {
                    var c=promoteChunkScratch[i];
                    deferredWholeChunks.Remove(c);
                    DirtyWholeChunk(c);
                }
            }
        }

        void IncrementPendingMesh(ChunkCoord c)
        {
            pendingMeshByChunk.TryGetValue(c, out var n);
            pendingMeshByChunk[c] = n + 1;
            pendingMeshTotal++;
        }

        void DecrementPendingMesh(ChunkCoord c)
        {
            if (!pendingMeshByChunk.TryGetValue(c, out var n) || n <= 0) return;
            pendingMeshTotal = Math.Max(0, pendingMeshTotal - 1);
            if (n == 1) pendingMeshByChunk.Remove(c);
            else pendingMeshByChunk[c] = n - 1;
        }

        void DirtyWholeChunk(ChunkCoord c)
        {
            if (!chunks.TryGetValue(c, out var chunk)) return;
            if (renders.TryGetValue(c,out var cr)) cr.BeginFullCpuRebuild();
            for (int s=0;s<VoxelConstants.SectionCount;s++)
                if (chunk.Sections[s] != null && chunk.Sections[s].NonAir > 0) DirtySingle(new SectionKey(c,s));
        }

        bool InLiveRenderRadius(ChunkCoord c)
        {
            int r=InitialWorldReady?RenderDistance:Math.Min(RenderDistance,3);
            return Math.Abs(c.X-center.X)<=r&&Math.Abs(c.Z-center.Z)<=r;
        }

        bool TryFindBestMeshChunk(out ChunkCoord bestChunk)
        {
            bestChunk=default;int bestViewTier=int.MaxValue,bestDistance=int.MaxValue;bool found=false;meshMaskScratch.Clear();
            foreach(var kv in meshQueuedMasks)
            {
                ChunkCoord c=kv.Key;uint mask=kv.Value;
                if(!chunks.ContainsKey(c)){meshMaskScratch.Add(c);continue;}
                if(!InRenderRadius(c)||!NeighborhoodReady(c))continue;
                bool schedulable=false;
                for(int sec=0;sec<VoxelConstants.SectionCount;sec++)
                    if((mask&(1u<<sec))!=0&&!meshInFlight.Contains(new SectionKey(c,sec))){schedulable=true;break;}
                if(!schedulable)continue;

                int dx=c.X-center.X,dz=c.Z-center.Z,d=dx*dx+dz*dz,viewTier=MeshViewTier(c,dx,dz);
                // R81: publication priority is presentation-safe. The 5x5 safety core wins first,
                // then exact camera-frustum chunks, then the forward preload cone, then off-screen
                // work. Squared source distance remains the ordering key *inside* each tier. This is
                // the key difference from R79, where distance was primary and 138 GPU meshes could
                // still be spent around the player while visible loaded chunks remained black holes.
                if(!found||viewTier<bestViewTier||
                   (viewTier==bestViewTier&&(d<bestDistance||
                    (d==bestDistance&&(dx<bestChunk.X-center.X||
                     (dx==bestChunk.X-center.X&&dz<bestChunk.Z-center.Z))))))
                {
                    bestViewTier=viewTier;bestDistance=d;bestChunk=c;found=true;
                    if(viewTier==0&&d==0)break;
                }
            }
            for(int i=0;i<meshMaskScratch.Count;i++)meshQueuedMasks.Remove(meshMaskScratch[i]);
            return found;
        }

        bool InRenderRadius(ChunkCoord c)
        {
            return InLiveRenderRadius(c);
        }

        // main.js jL(): don't build boundary-dependent faces/AO until the complete 3x3
        // chunk neighbourhood exists. This avoids rebuilding the same chunk as neighbours stream in.
        bool NeighborhoodReady(ChunkCoord c)
        {
            for(int dz=-1;dz<=1;dz++) for(int dx=-1;dx<=1;dx++)
            {
                ChunkCoord n=new ChunkCoord(c.X+dx,c.Z+dz);
                if(!chunks.ContainsKey(n))return false;
                // A generated column is visible to queries as soon as it is published, but its mesh
                // may not snapshot the 3x3 neighbourhood until every member crossed the lighting
                // stage. This is the stage barrier that makes worker input immutable-by-version.
                if(InitialWorldReady&&chunkLightPending.Contains(n))return false;
            }
            return true;
        }

        int MeshPriority(SectionKey k, int playerSection)
        {
            bool live=InLiveRenderRadius(k.Chunk);
            int dx = k.Chunk.X - center.X, dz = k.Chunk.Z - center.Z, dy = k.Section - playerSection;
            return (live?0:1_000_000)+(dx * dx + dz * dz) * 64 + dy * dy;
        }

        struct MeshWorkInput
        {
            public SectionKey Key; public int Stamp; public SectionSnapshot Snapshot;
            public MeshWorkInput(SectionKey key,int stamp,SectionSnapshot snapshot){Key=key;Stamp=stamp;Snapshot=snapshot;}
        }

        struct MeshWorkerJob
        {
            public ChunkCoord Chunk;
            public MeshWorkInput[] Inputs;
            public ChunkColumn[] Neighborhood;
            public int Count;
            public bool BuildCombined;
        }

        void ProcessMeshWorkerJob(MeshWorkerJob job)
        {
            var inputs=job.Inputs;
            int workerCount=job.Count;
            var results=ArrayPool<MeshBuildResult>.Shared.Rent(workerCount);
            try
            {
                // R54: the main thread only publishes nine ChunkColumn references. The worker owns
                // all 18^3 / shared-column snapshot copying and mesh construction. Version stamps
                // are still checked during integration, so any concurrent edit invalidates this job.
                CaptureMeshInputsWorker(job.Chunk,inputs,workerCount,job.Neighborhood);
                for(int i=0;i<workerCount;i++)
                {
                    var input=inputs[i];
                    var snap=input.Snapshot;
                    if(!snap.Valid)
                    {
                        results[i]=EmptyResult(input.Key,input.Stamp);
                        continue;
                    }
                    try { results[i]=ChunkMesher.Build(snap); }
                    catch(Exception ex)
                    {
                        errors.Enqueue("Mesher "+snap.Key+": "+ex);
                        results[i]=EmptyResult(snap.Key,-1);
                    }
                    finally { snap.Release(); }
                }
                ChunkMeshPayload combined=job.BuildCombined?BuildCombinedPayload(results,workerCount):null;
                meshed.Enqueue(new MeshBuildBatch(job.Chunk,results,workerCount,combined));
                results=null;
            }
            finally
            {
                if(results!=null)ArrayPool<MeshBuildResult>.Shared.Return(results,true);
                ArrayPool<MeshWorkInput>.Shared.Return(inputs,true);
                if(job.Neighborhood!=null)ArrayPool<ChunkColumn>.Shared.Return(job.Neighborhood,true);
                Interlocked.Decrement(ref activeMesh);
            }
        }

        static ChunkMeshPayload BuildCombinedPayload(MeshBuildResult[] results,int count)
        {
            int totalVertices=0,totalIndices=0,opaque=0,water=0,transparent=0,xray=0,minSection=-1,maxSection=-1;
            int[] offsets=ArrayPool<int>.Shared.Rent(Math.Max(1,count));
            try
            {
                for(int i=0;i<count;i++)
                {
                    ref MeshBuildResult r=ref results[i];offsets[i]=totalVertices;if(r.Empty)continue;
                    totalVertices+=r.VertexCount;opaque+=r.OpaqueCount;water+=r.WaterCount;transparent+=r.TransparentCount;xray+=r.XrayHiddenCount;
                    int sec=r.Key.Section;if(minSection<0||sec<minSection)minSection=sec;if(sec>maxSection)maxSection=sec;
                }
                totalIndices=opaque+water+transparent+xray;if(totalVertices==0||totalIndices==0)return null;
                var p=ChunkMeshPayload.Rent();
                p.Vertices=ArrayPool<VoxelVertex>.Shared.Rent(totalVertices);p.VertexCount=totalVertices;
                bool use16=totalVertices<=65535; // UInt16 addresses vertices 0..65534 on the conservative cross-platform path.
                if(use16)p.Indices16=ArrayPool<ushort>.Shared.Rent(totalIndices);
                else p.Indices=ArrayPool<int>.Shared.Rent(totalIndices);
                p.IndexCount=totalIndices;
                p.OpaqueCount=opaque;p.WaterCount=water;p.TransparentCount=transparent;p.XrayCount=xray;p.MinSection=minSection;p.MaxSection=maxSection;
                int vw=0;for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];if(r.VertexCount==0)continue;Array.Copy(r.Vertices,0,p.Vertices,vw,r.VertexCount);vw+=r.VertexCount;}
                int iw=0;
                if(use16)
                {
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices16(r.Opaque,r.OpaqueCount,p.Indices16,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices16(r.Water,r.WaterCount,p.Indices16,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices16(r.Transparent,r.TransparentCount,p.Indices16,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices16(r.XrayHidden,r.XrayHiddenCount,p.Indices16,iw,offsets[i]);}
                }
                else
                {
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices(r.Opaque,r.OpaqueCount,p.Indices,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices(r.Water,r.WaterCount,p.Indices,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices(r.Transparent,r.TransparentCount,p.Indices,iw,offsets[i]);}
                    for(int i=0;i<count;i++){ref MeshBuildResult r=ref results[i];iw=CopyLayerIndices(r.XrayHidden,r.XrayHiddenCount,p.Indices,iw,offsets[i]);}
                }
                return p;
            }
            finally{ArrayPool<int>.Shared.Return(offsets,false);}
        }

        static int CopyLayerIndices(int[] src,int count,int[] dst,int write,int offset)
        {
            if(src==null||count<=0)return write;
            for(int i=0;i<count;i++)dst[write++]=src[i]+offset;
            return write;
        }

        static int CopyLayerIndices16(int[] src,int count,ushort[] dst,int write,int offset)
        {
            if(src==null||count<=0)return write;
            // BuildCombinedPayload only selects this path when totalVertices <= 65535, so every
            // source index plus section offset is representable without changing topology.
            for(int i=0;i<count;i++)dst[write++]=(ushort)(src[i]+offset);
            return write;
        }

        bool CanBuildCombinedPayload(ChunkCoord chunk,uint selectedMask)
        {
            if(!chunks.TryGetValue(chunk,out var c))return false;
            bool needsWhole=!renders.TryGetValue(chunk,out var cr)||!cr.HasCpuGeometry||cr.FullCpuRebuildPending;
            if(!needsWhole)return false;
            for(int sec=0;sec<VoxelConstants.SectionCount;sec++)
            {
                var s=c.Sections[sec];if(s==null||s.NonAir==0)continue;
                if((selectedMask&(1u<<sec))==0)return false;
            }
            return true;
        }

        void ScheduleMeshes(double deadlineMs)
        {
            // Deferred simulation lighting is an explicit stage barrier. Do not snapshot a mesh while
            // older water/block mutations are still waiting for their exact light propagation pass.
            if (meshQueuedMasks.Count == 0 || PendingLightEdits>0 || NowMs()>deadlineMs-2.0) return;
            int starts=0;
            // main.js ol(): at most three new mesh-worker chunk jobs are submitted per scheduler pass.
            while (meshQueuedMasks.Count>0 && Volatile.Read(ref activeMesh)<MaxMeshJobs && starts<3)
            {
                // Scheduling is intentionally lightweight: no voxel/light arrays are copied here.
                // Main publishes object references + version stamps; dedicated mesh threads perform
                // snapshot capture, meshing and whole-chunk merge before immutable results come back.
                if(NowMs()>deadlineMs-1.0)break;
                if(!TryFindBestMeshChunk(out ChunkCoord chunk))break;
                int capacity=Mathf.Max(1,Mathf.Min(MaxSectionsPerMeshJob,VoxelConstants.SectionCount));
                var inputs=ArrayPool<MeshWorkInput>.Shared.Rent(capacity);int inputCount=0;uint selectedMask=0;
                uint mask=meshQueuedMasks[chunk];
                for(int sec=0;sec<VoxelConstants.SectionCount&&inputCount<capacity;sec++)
                {
                    uint bit=1u<<sec;if((mask&bit)==0)continue;var k=new SectionKey(chunk,sec);if(meshInFlight.Contains(k))continue;
                    mask&=~bit;selectedMask|=bit;meshQueued.Remove(k);CaptureForMeshJob(k,inputs,ref inputCount);
                }
                if(mask==0)meshQueuedMasks.Remove(chunk);else meshQueuedMasks[chunk]=mask;
                if(inputCount==0){ArrayPool<MeshWorkInput>.Shared.Return(inputs,true);continue;}
                bool buildCombined=CanBuildCombinedPayload(chunk,selectedMask);
                var neighborhood=ArrayPool<ChunkColumn>.Shared.Rent(9);
                FillMeshNeighborhood(chunk,neighborhood);
                Interlocked.Increment(ref activeMesh);
                meshWork.Enqueue(new MeshWorkerJob{Chunk=chunk,Inputs=inputs,Neighborhood=neighborhood,Count=inputCount,BuildCombined=buildCombined});
                meshSignal.Release();starts++;
            }
        }

        void CaptureForMeshJob(SectionKey k,MeshWorkInput[] inputs,ref int inputCount)
        {
            int stamp = sectionStamp.TryGetValue(k, out var st) ? st : 0;
            // First collect all keys from the selected chunk. If this is a near-full rebuild we can
            // capture one 18x18x386 snapshot instead of 20-24 independent 18-cube snapshots.
            meshInFlight.Add(k);
            inputs[inputCount++]=new MeshWorkInput(k,stamp,default);
        }

        void FillMeshNeighborhood(ChunkCoord chunk,ChunkColumn[] neighborhood)
        {
            for(int cz=-1;cz<=1;cz++)for(int cx=-1;cx<=1;cx++)
            {
                chunks.TryGetValue(new ChunkCoord(chunk.X+cx,chunk.Z+cz),out var neighbour);
                neighborhood[(cx+1)+(cz+1)*3]=neighbour;
            }
        }

        void CaptureMeshInputsWorker(ChunkCoord chunk,MeshWorkInput[] inputs,int count,ChunkColumn[] neighborhood)
        {
            if(count>=SharedChunkSnapshotThreshold&&CaptureSharedChunkWorker(chunk,inputs,count,neighborhood))return;
            for(int i=0;i<count;i++)
                inputs[i].Snapshot=CaptureFromNeighborhood(inputs[i].Key,inputs[i].Stamp,neighborhood);
        }

        static bool CaptureSharedChunkWorker(ChunkCoord chunk,MeshWorkInput[] inputs,int count,ChunkColumn[] nb)
        {
            ChunkColumn centerChunk=nb!=null&&nb.Length>=9?nb[4]:null;
            if(centerChunk==null)return false;
            int references=0,minSection=VoxelConstants.SectionCount,maxSection=-1;
            for(int i=0;i<count;i++)
            {
                int si=inputs[i].Key.Section;
                var sec=(uint)si<(uint)VoxelConstants.SectionCount?centerChunk.Sections[si]:null;
                if(sec==null||sec.NonAir==0)continue;
                references++;if(si<minSection)minSection=si;if(si>maxSection)maxSection=si;
            }
            if(references==0)return true;

            const int S=18;
            // Only capture the selected vertical span + one-voxel halo. The old shared snapshot copied
            // the entire 384-block world height even for a shallow surface chunk. Values are identical
            // to independent 18^3 section snapshots, but main-thread memory bandwidth is much lower.
            int H=(maxSection-minSection+1)*16+2;
            int baseWorldY=VoxelConstants.MinY+minSection*16;
            int sampleCount=S*S*H;
            ushort[] voxels=ArrayPool<ushort>.Shared.Rent(sampleCount);
            byte[] meta=ArrayPool<byte>.Shared.Rent(sampleCount);
            byte[] lights=ArrayPool<byte>.Shared.Rent(sampleCount);

            for(int x=0;x<S;x++)
            {
                int cxOff=x==0?-1:(x==17?1:0);
                int lx=x==0?15:(x==17?0:x-1);
                for(int z=0;z<S;z++)
                {
                    int czOff=z==0?-1:(z==17?1:0);
                    int lz=z==0?15:(z==17?0:z-1);
                    ChunkColumn col=nb[(cxOff+1)+(czOff+1)*3];
                    int row=(x*S+z)*H;
                    for(int yy=0;yy<H;yy++)
                    {
                        int wy=baseWorldY+yy-1,idx=row+yy;
                        if(wy<VoxelConstants.MinY)
                        {
                            voxels[idx]=(ushort)BlockId.Bedrock;meta[idx]=0;lights[idx]=0;
                        }
                        else if(wy>VoxelConstants.MaxY)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else if(col==null)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else
                        {
                            int yo=wy-VoxelConstants.MinY,secIndex=yo>>4,ly=yo&15;
                            var sourceSec=col.Sections[secIndex];
                            if(sourceSec==null)
                            {
                                voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;
                                lights[idx]=wy>col.LightCeilings[lx*16+lz]?(byte)0xF0:(byte)0;
                            }
                            else
                            {
                                int si=((lx*16)+lz)*16+ly;
                                voxels[idx]=sourceSec.Blocks[si];
                                meta[idx]=sourceSec.Meta[si];
                                lights[idx]=sourceSec.Light[si];
                            }
                        }
                    }
                }
            }

            var shared=new SectionSnapshot.SharedBuffer(voxels,meta,lights,H,references);
            for(int i=0;i<count;i++)
            {
                int section=inputs[i].Key.Section;
                var sec=(uint)section<(uint)VoxelConstants.SectionCount?centerChunk.Sections[section]:null;
                if(sec==null||sec.NonAir==0)continue;
                inputs[i].Snapshot=new SectionSnapshot(inputs[i].Key,inputs[i].Stamp,shared,(section-minSection)*16);
            }
            return true;
        }

        static MeshBuildResult EmptyResult(SectionKey k, int stamp)
        {
            return new MeshBuildResult
            {
                Key = k, Stamp = stamp,
                Vertices = Array.Empty<VoxelVertex>(),
                Opaque = Array.Empty<int>(), Water = Array.Empty<int>(), Transparent = Array.Empty<int>()
            };
        }

        SectionSnapshot Capture(SectionKey k, int stamp)
        {
            // Immediate player edits intentionally keep source-style same-frame feedback. Streaming
            // never uses this path: its capture is fully worker-side.
            FillMeshNeighborhood(k.Chunk,immediateCaptureNeighborhood);
            return CaptureFromNeighborhood(k,stamp,immediateCaptureNeighborhood);
        }

        static SectionSnapshot CaptureFromNeighborhood(SectionKey k,int stamp,ChunkColumn[] nb)
        {
            ChunkColumn center=nb!=null&&nb.Length>=9?nb[4]:null;
            if(center==null)return default;
            var sec=center.Sections[k.Section];
            if(sec==null||sec.NonAir==0)return default;

            const int S=18;
            int sampleCount=S*S*S;
            ushort[] voxels=ArrayPool<ushort>.Shared.Rent(sampleCount);
            byte[] meta=ArrayPool<byte>.Shared.Rent(sampleCount);
            byte[] lights=ArrayPool<byte>.Shared.Rent(sampleCount);
            int baseY=VoxelConstants.MinY+k.Section*16;

            for(int x=0;x<S;x++)
            {
                int cxOff=x==0?-1:(x==17?1:0);
                int lx=x==0?15:(x==17?0:x-1);
                for(int z=0;z<S;z++)
                {
                    int czOff=z==0?-1:(z==17?1:0);
                    int lz=z==0?15:(z==17?0:z-1);
                    ChunkColumn col=nb[(cxOff+1)+(czOff+1)*3];
                    int row=(x*S+z)*S;
                    for(int y=0;y<S;y++)
                    {
                        int wy=baseY+y-1,idx=row+y;
                        if(wy<VoxelConstants.MinY)
                        {
                            voxels[idx]=(ushort)BlockId.Bedrock;meta[idx]=0;lights[idx]=0;
                        }
                        else if(wy>VoxelConstants.MaxY)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else if(col==null)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else
                        {
                            int yo=wy-VoxelConstants.MinY,secIndex=yo>>4,ly=yo&15;
                            var sourceSec=col.Sections[secIndex];
                            if(sourceSec==null)
                            {
                                voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;
                                lights[idx]=wy>col.LightCeilings[lx*16+lz]?(byte)0xF0:(byte)0;
                            }
                            else
                            {
                                int si=((lx*16)+lz)*16+ly;
                                voxels[idx]=sourceSec.Blocks[si];
                                meta[idx]=sourceSec.Meta[si];
                                lights[idx]=sourceSec.Light[si];
                            }
                        }
                    }
                }
            }
            return new SectionSnapshot(k,stamp,voxels,meta,lights);
        }

        void IntegrateMeshesAndUploads(double deadlineMs)
        {
            MeshBuildBatch batch;

            // main's limit of 6 is six completed chunk mesh-worker messages, not six 16-high sections.
            // Integrate one worker batch atomically so a full chunk can become upload-ready this frame.
            int integratedBatches=0;
            double meshIntegrateStart=NowMs();
            while (NowMs()<=deadlineMs-2.0 && meshed.TryDequeue(out batch))
            {
                integratedBatches++;
                bool chunkExists=chunks.ContainsKey(batch.Chunk);
                bool visible=chunkExists&&InRenderRadius(batch.Chunk);
                ChunkRender cr=null;
                bool touched=false;
                bool combinedUsable=batch.Combined!=null;

                try
                {
                    for(int bi=0;bi<batch.Count;bi++)
                    {
                        ref MeshBuildResult r=ref batch.Results[bi];
                        meshInFlight.Remove(r.Key);
                        DecrementPendingMesh(r.Key.Chunk);

                        if (!sectionStamp.TryGetValue(r.Key, out var stamp) || stamp != r.Stamp)
                        {
                            combinedUsable=false;
                            // A newer player edit may already have rebuilt this section synchronously.
                            // Do not bump the stamp again when an old worker result arrives: main.js simply
                            // ignores stale worker output and keeps the newer dirty/current geometry.
                            bool latestApplied=sectionAppliedStamp.TryGetValue(r.Key,out var applied)&&applied==stamp;
                            if (chunks.ContainsKey(r.Key.Chunk)&&!latestApplied&&!meshQueued.Contains(r.Key)&&InRenderRadius(r.Key.Chunk))
                                EnqueueMesh(r.Key);
                            continue;
                        }
                        if (!chunkExists){combinedUsable=false;continue;}
                        if(!visible)
                        {
                            combinedUsable=false;
                            // Work completed after this chunk left RENDER_R. Do not create/update a
                            // Unity Mesh off screen; rebuild from cached world data when it returns.
                            deferredWholeChunks.Add(batch.Chunk);
                            continue;
                        }

                        if (cr==null&&!renders.TryGetValue(batch.Chunk, out cr))
                        {
                            if (r.Empty) continue;
                            cr = new ChunkRender(batch.Chunk);
                            renders[batch.Chunk] = cr;
                        }
                        if(cr==null)continue;
                        cr.ApplySection(ref r);
                        sectionAppliedStamp[r.Key]=r.Stamp;
                        touched=true;
                    }

                    if(touched&&cr!=null)
                    {
                        if(combinedUsable&&batch.Combined!=null)cr.SetPremerged(batch.DetachCombined());
                        QueueUpload(batch.Chunk);
                    }
                }
                finally { batch.Release(); }
            }
            LastMeshIntegrateMs=(float)(NowMs()-meshIntegrateStart);

            // Then perform coalesced Unity Mesh uploads through the shared frame scheduler. Native
            // uploads must execute on Unity's main thread; R54 uses measured EWMA cost + frame headroom
            // instead of an arbitrary byte-per-frame throttle. Worker stages already own all CPU copies.
            int safety = uploadQueue.Count,uploads=0;
            int uploadedBytesThisFrame=0;
            while (safety-- > 0 && uploadQueue.Count > 0 && NowMs()<=deadlineMs-2.0)
            {
                // Unity Mesh uploads can synchronize with native/GPU memory and are less predictable
                // than WebGL bufferData. After one upload, use an EWMA to avoid knowingly crossing
                // the same shared deadline with a second large chunk.
                if(uploads>0&&NowMs()+estimatedMeshUploadMs>deadlineMs)break;
                // Peek first so an oversized next upload stays at the front instead of being moved to
                // the back every frame (which could starve a dense cave chunk during continuous streaming).
                ChunkCoord c = uploadQueue.Peek();
                if (!renders.TryGetValue(c, out var cr)||!cr.NeedsUpload||!InRenderRadius(c))
                {
                    uploadQueue.Dequeue();uploadQueued.Remove(c);continue;
                }
                if (HasPendingMesh(c))
                {
                    uploadQueue.Dequeue();uploadQueued.Remove(c);
                    if(InRenderRadius(c))QueueUpload(c);
                    continue;
                }
                uploadQueue.Dequeue();uploadQueued.Remove(c);
                if (cr.FullCpuRebuildPending) cr.CompleteFullCpuRebuild();
                double uploadStart=NowMs();
                bool structureChanged;
                using(MeshUploadMarker.Auto()) structureChanged=cr.Rebuild();
                publishedRenderRadiusDirty=true;
                if(structureChanged)MarkRenderCommandsDirty(true);
                double uploadCost=NowMs()-uploadStart;
                LastMeshUploadMs=(float)uploadCost;
                if(LastMeshUploadMs>MaxMeshUploadMs)MaxMeshUploadMs=LastMeshUploadMs;
                LastMeshUploadBytes=cr.LastUploadBytes;
                uploadedBytesThisFrame+=LastMeshUploadBytes;
                LastMeshUploadBatchBytes=uploadedBytesThisFrame;
                LastMeshUploadUsed16BitIndices=cr.LastUploadUsed16BitIndices;
                LastMeshUploadIndexBytesSaved=cr.LastUploadUsed16BitIndices?cr.LastUploadIndexCount*sizeof(ushort):0;
                if(cr.LastUploadUsed16BitIndices)MeshIndex16Uploads++;else MeshIndex32Uploads++;
                MeshUploads++;
                if(cr.LastUploadReplacedMesh)MeshReplacements++;
                estimatedMeshUploadMs+=(uploadCost-estimatedMeshUploadMs)*.25;
                uploads++;
                // CPU retention is event-driven: once a render has a current GPU copy, immediately
                // queue any far CPU staging data for deferred disposal. No periodic full-cache scan.
                QueueRenderMaintenance(c);
                // Existing direct DrawMesh commands reference the same Mesh object, so ordinary
                // content-only uploads need no CommandBuffer re-recording.
            }
        }

        void QueueUpload(ChunkCoord c)
        {
            if (uploadQueued.Add(c)) uploadQueue.Enqueue(c);
        }

        bool HasPendingMesh(ChunkCoord c)
        {
            return pendingMeshByChunk.TryGetValue(c, out var n) && n > 0;
        }

        void QueueRenderMaintenance(ChunkCoord c)
        {
            if(!renders.TryGetValue(c,out var cr))return;
            int d=Math.Max(Math.Abs(c.X-center.X),Math.Abs(c.Z-center.Z));
            int gpuKeepRadius=RenderDistance+3; // main: J = RENDER_R + 3
            if(d>gpuKeepRadius)
            {
                if(!cr.NeedsUpload&&!HasPendingMesh(c)&&gpuDropQueued.Add(c))gpuDropQueue.Enqueue(c);
            }
            else if(d>FarCpuGeometryRadius&&cr.HasCpuGeometry&&!cr.NeedsUpload&&!HasPendingMesh(c)&&cpuTrimQueued.Add(c))cpuTrimQueue.Enqueue(c);
        }

        void QueueRenderMaintenanceRing(ChunkCoord origin,int radius)
        {
            if(radius<0)return;
            if(radius==0){QueueRenderMaintenance(origin);return;}
            int x0=origin.X-radius,x1=origin.X+radius,z0=origin.Z-radius,z1=origin.Z+radius;
            for(int x=x0;x<=x1;x++)
            {
                QueueRenderMaintenance(new ChunkCoord(x,z0));
                QueueRenderMaintenance(new ChunkCoord(x,z1));
            }
            for(int z=z0+1;z<z1;z++)
            {
                QueueRenderMaintenance(new ChunkCoord(x0,z));
                QueueRenderMaintenance(new ChunkCoord(x1,z));
            }
        }

        void QueueRenderMaintenanceForCenterMove(ChunkCoord oldCenter,ChunkCoord newCenter)
        {
            int dx=Math.Abs(newCenter.X-oldCenter.X),dz=Math.Abs(newCenter.Z-oldCenter.Z);
            if(Math.Max(dx,dz)<=1)
            {
                // With a one-chunk move only the old threshold rings can cross a retention boundary.
                // Queueing those O(radius) cells replaces the old periodic O(all cached renders) scan.
                QueueRenderMaintenanceRing(oldCenter,FarCpuGeometryRadius);
                QueueRenderMaintenanceRing(oldCenter,RenderDistance+3);
                return;
            }

            // Teleports are rare and can cross many rings at once. Walk only the bounded old cache
            // square (not the unbounded render dictionary) so every newly-far render is considered.
            int radius=RenderDistance+3;
            for(int z=oldCenter.Z-radius;z<=oldCenter.Z+radius;z++)
                for(int x=oldCenter.X-radius;x<=oldCenter.X+radius;x++)
                    QueueRenderMaintenance(new ChunkCoord(x,z));
        }

        void QueueRenderMaintenanceForDistanceChange(int oldDistance,int newDistance)
        {
            if(newDistance>=oldDistance)return;
            int oldKeep=oldDistance+3,newKeep=newDistance+3;
            // Render-distance changes are explicit user actions. Queue only the cache rings that just
            // left the new retention radius; actual native disposal remains deferred by the scheduler.
            for(int r=newKeep+1;r<=oldKeep;r++)QueueRenderMaintenanceRing(center,r);
        }

        void ProcessDeferredMaintenance(double deadlineMs)
        {
            // Maintenance uses the same scheduler as streaming and is round-robin across resource
            // classes. There are no per-frame operation counts or private millisecond quotas: before
            // starting an atomic Unity/native operation we compare its measured EWMA with remaining
            // frame headroom, then update that estimate from the actual cost.
            if(NowMs()>=deadlineMs)return;
            int keep=RenderDistance+8,gpuKeep=RenderDistance+3;
            int emptyRounds=0;
            while(NowMs()<deadlineMs&&emptyRounds<3)
            {
                int turn=maintenanceTurn++%3;
                bool did=false;
                if(turn==0&&unloadQueue.Count>0&&NowMs()+estimatedUnloadMs<=deadlineMs)
                {
                    var c=unloadQueue.Dequeue();unloadQueued.Remove(c);
                    if(Math.Abs(c.X-center.X)>keep||Math.Abs(c.Z-center.Z)>keep)
                    {
                        double t=NowMs();if(chunks.ContainsKey(c))Unload(c);
                        double cost=NowMs()-t;estimatedUnloadMs+=(cost-estimatedUnloadMs)*.20;
                    }
                    did=true;
                }
                else if(turn==1&&gpuDropQueue.Count>0&&NowMs()+estimatedGpuDropMs<=deadlineMs)
                {
                    var c=gpuDropQueue.Dequeue();gpuDropQueued.Remove(c);did=true;
                    if(renders.TryGetValue(c,out var cr))
                    {
                        int d=Math.Max(Math.Abs(c.X-center.X),Math.Abs(c.Z-center.Z));
                        if(d>gpuKeep&&!cr.NeedsUpload&&!HasPendingMesh(c))
                        {
                            double t=NowMs();cr.Dispose();renders.Remove(c);publishedRenderRadiusDirty=true;MarkRenderCommandsDirty(true);
                            uploadQueued.Remove(c);deferredWholeChunks.Add(c);
                            double cost=NowMs()-t;estimatedGpuDropMs+=(cost-estimatedGpuDropMs)*.20;
                        }
                    }
                }
                else if(turn==2&&cpuTrimQueue.Count>0&&uploadQueue.Count==0&&meshed.IsEmpty&&generated.IsEmpty&&NowMs()+estimatedCpuTrimMs<=deadlineMs)
                {
                    var c=cpuTrimQueue.Dequeue();cpuTrimQueued.Remove(c);did=true;
                    if(renders.TryGetValue(c,out var cr))
                    {
                        int d=Math.Max(Math.Abs(c.X-center.X),Math.Abs(c.Z-center.Z));
                        if(d>FarCpuGeometryRadius&&d<=gpuKeep&&cr.HasCpuGeometry&&!cr.NeedsUpload&&!HasPendingMesh(c))
                        {
                            double t=NowMs();cr.DiscardCpuGeometry(d<=WritableMeshRadius);
                            double cost=NowMs()-t;estimatedCpuTrimMs+=(cost-estimatedCpuTrimMs)*.20;
                        }
                    }
                }
                if(did)emptyRounds=0;else emptyRounds++;
            }
        }

        void Unload(ChunkCoord c)
        {
            chunkLightPending.Remove(c);
            if(activeChunkLightJob!=null&&activeChunkLightCoord.Equals(c))activeChunkLightJob=null;
            if(deferredLightActive&&ChunkCoord.FromWorld(deferredLightEdit.X,deferredLightEdit.Z).Equals(c))
            {
                VoxelLighting.CancelDeferredRelight(deferredLightJob);
                deferredLightActive=false;
            }
            if(MobSpawner.Instance!=null)MobSpawner.Instance.OnChunkUnloading(c);
            if(chunks.Remove(c))unchecked{worldQueryRevision++;}
            unloadQueued.Remove(c);cpuTrimQueued.Remove(c);gpuDropQueued.Remove(c);meshQueuedMasks.Remove(c);
            if (renders.TryGetValue(c, out var render))
            {
                render.Dispose();renders.Remove(c);publishedRenderRadiusDirty=true;MarkRenderCommandsDirty(true);
            }
            uploadQueued.Remove(c);deferredWholeChunks.Remove(c);
            for(int sec=0;sec<VoxelConstants.SectionCount;sec++)
            {
                var k=new SectionKey(c,sec);sectionStamp.Remove(k);sectionAppliedStamp.Remove(k);
                if(meshQueued.Remove(k))DecrementPendingMesh(c);
                meshInFlight.Remove(k);
            }
            if (pendingMeshByChunk.TryGetValue(c, out var pending))
            {
                pendingMeshTotal = Math.Max(0, pendingMeshTotal - pending);pendingMeshByChunk.Remove(c);
            }
        }

        void MarkRenderCommandsDirty(bool candidatesDirty=false)
        {
            unchecked
            {
                renderRevision++;
                if(candidatesDirty)renderCandidateRevision++;
            }
            if(candidatesDirty)directCandidatesValid=false;
            if(directRenderer!=null)directRenderer.Invalidate();
        }

        void MarkRenderOverlayDirty()
        {
            unchecked{renderOverlayRevision++;}
        }

        // Dynamic world effects/entities/selection change only the small overlay buffers.
        // Chunk terrain/water command lists remain untouched unless their own revision changes.
        public void InvalidateDirectCommands()
        {
            MarkRenderOverlayDirty();
        }

        public void InvalidateAllDirectCommands()
        {
            MarkRenderCommandsDirty();
            MarkRenderOverlayDirty();
        }

        /// <summary>
        /// Rebuilds static native camera command buffers. This mirrors main.js' render loop:
        /// square RENDER_R scan -> AABB frustum test -> terrain, then water, then glass. Moving-ship
        /// water lives in a tiny dynamic buffer between static water/glass. X-Ray uses the exact
        /// global wire-all -> clear depth -> classified-solid-all order.
        /// </summary>
        internal bool RebuildDirectDrawCommands(Camera cam,CommandBuffer terrain,CommandBuffer water,CommandBuffer glass,bool forceRecord,bool rebuildCandidates)
        {
            directVisibleScratch.Clear();
            LastVisibleChunks=0;LastChunkDrawCalls=0;LastChunkTriangles=0;
            DirectVisibilityScans++;
            if(cam==null||materials==null||materials.Length<3||center.X==int.MaxValue)
            {
                if(forceRecord||directRecordedVisible.Count!=0)
                {
                    terrain.Clear();water.Clear();glass.Clear();directCandidates.Clear();directCandidatesValid=false;directRecordedVisible.Clear();DirectStaticCommandRebuilds++;DirectCommandRebuilds++;
                    return true;
                }
                return false;
            }

            if(rebuildCandidates||!directCandidatesValid)
            {
                directCandidates.Clear();
                DirectCandidateRebuilds++;
                int rr=RenderDistance;
                // main.js scans the square row by row. Here the same square is visited nearest ring
                // first: opaque chunks are then submitted front-to-back (early-Z rejects hidden
                // fragments of far chunks instead of shading them), and the alpha passes walk the
                // list backwards (far-to-near blending). Dictionary lookups still happen only when
                // the render structure/center changes; camera-only updates reuse these references.
                int[] order=NearFirstOffsets(rr);
                for(int k=0;k<order.Length;k+=2)
                {
                    ChunkCoord cc=new ChunkCoord(center.X+order[k],center.Z+order[k+1]);
                    if(renders.TryGetValue(cc,out var cr)&&cr.Drawable)directCandidates.Add(cr);
                }
                directCandidatesValid=true;
            }

            ExtractSourceFrustum(cam.cullingMatrix,directFrustumPlanes);
            for(int i=0;i<directCandidates.Count;i++)
            {
                var cr=directCandidates[i];
                if(!FastFrustumAabb(directFrustumPlanes,cr.BoundsMin,cr.BoundsMax))continue;
                directVisibleScratch.Add(cr);
                LastChunkTriangles+=cr.TriangleCount;
            }
            LastVisibleChunks=directVisibleScratch.Count;

            bool sameVisible=!forceRecord&&directRecordedVisible.Count==directVisibleScratch.Count;
            if(sameVisible)
            {
                for(int i=0;i<directVisibleScratch.Count;i++)
                    if(!ReferenceEquals(directVisibleScratch[i],directRecordedVisible[i])){sameVisible=false;break;}
            }
            if(sameVisible)
            {
                // Draw commands reference stable Mesh/Material objects. Camera matrices and dynamic
                // mesh contents are resolved when Unity executes the buffer, so re-recording the
                // same chunk list while walking/looking around only burns main-thread CPU.
                int draws=xrayEnabled?2:1;
                for(int i=0;i<directVisibleScratch.Count;i++)
                {
                    var cr=directVisibleScratch[i];
                    if(cr.HasOpaque)LastChunkDrawCalls+=draws;
                    if(cr.HasWater)LastChunkDrawCalls++;
                    if(cr.HasTransparent)LastChunkDrawCalls++;
                }
                return false;
            }

            terrain.Clear();
            water.Clear();
            glass.Clear();
            DirectStaticCommandRebuilds++;
            DirectCommandRebuilds++;
            directRecordedVisible.Clear();
            directRecordedVisible.AddRange(directVisibleScratch);

            // main.js draws sun/moon first with depth writes disabled, then terrain.
            MainSkyRenderer.AppendCelestial(terrain);

            if(xrayEnabled)
            {
                Material xm=xrayMaterials!=null&&xrayMaterials.Length>0?xrayMaterials[0]:materials[0];
                for(int i=0;i<directVisibleScratch.Count;i++)
                {
                    var cr=directVisibleScratch[i];if(!cr.HasOpaque)continue;
                    terrain.DrawMesh(cr.Mesh,DirectIdentity,xm,0,0);
                    LastChunkDrawCalls++;
                }
                // main: wire pass writes no depth, then gl.clear(DEPTH_BUFFER_BIT), then classified solids.
                terrain.ClearRenderTarget(true,false,Color.clear,1f);
                for(int i=0;i<directVisibleScratch.Count;i++)
                {
                    var cr=directVisibleScratch[i];if(!cr.HasOpaque)continue;
                    terrain.DrawMesh(cr.Mesh,DirectIdentity,xm,0,1);
                    LastChunkDrawCalls++;
                    // Ore faces hidden behind stone exist only for this pass (meshWorker draws them always).
                    if(cr.HasXrayHidden){terrain.DrawMesh(cr.Mesh,DirectIdentity,xm,3,1);LastChunkDrawCalls++;}
                }
            }
            else
            {
                Material om=materials[0];
                for(int i=0;i<directVisibleScratch.Count;i++)
                {
                    var cr=directVisibleScratch[i];if(!cr.HasOpaque)continue;
                    terrain.DrawMesh(cr.Mesh,DirectIdentity,om,0,0);
                    LastChunkDrawCalls++;
                }
            }

            Material wm=materials[1],tm=materials[2];
            // Source draws every world-water mesh first, then moving-ship water, then transparent/glass.
            // Visible chunks are near-first, so walk them backwards: far-to-near blending.
            for(int i=directVisibleScratch.Count-1;i>=0;i--)
            {
                var cr=directVisibleScratch[i];if(!cr.HasWater)continue;
                water.DrawMesh(cr.Mesh,DirectIdentity,wm,1,0);
                LastChunkDrawCalls++;
            }
            for(int i=directVisibleScratch.Count-1;i>=0;i--)
            {
                var cr=directVisibleScratch[i];if(!cr.HasTransparent)continue;
                glass.DrawMesh(cr.Mesh,DirectIdentity,tm,2,0);
                LastChunkDrawCalls++;
            }
            return true;
        }

        /// <summary>
        /// Re-record only the small dynamic overlays. They execute at AfterForwardOpaque/AfterForwardAlpha,
        /// preserving main.js order around the large static chunk buffers without rewriting chunk draws
        /// when particles, mobs, vehicles, mining cracks or the selection outline appear/disappear.
        /// </summary>
        internal void RebuildDirectOverlayCommands(CommandBuffer terrain,CommandBuffer shipWater,CommandBuffer alpha)
        {
            if(terrain==null||shipWater==null||alpha==null)return;
            terrain.Clear();
            shipWater.Clear();
            alpha.Clear();
            DirectOverlayCommandRebuilds++;
            DirectCommandRebuilds++;

            // Exact main.js aI() source order after terrain and before world water:
            // W7 particles -> kB falling/TNT -> h7 drops -> pP vehicles -> tI ship opaque
            // -> nT fishing -> yd pearls -> Bd entities -> rP crack -> $B red boxes.
            MainTransientRenderer.AppendParticles(terrain);
            MainSourceObjectRenderer.AppendFalling(terrain);
            MainTransientRenderer.AppendDrops(terrain);
            MainSourceObjectRenderer.AppendVehicles(terrain);
            MainSourceObjectRenderer.AppendShipOpaque(terrain);
            MainSourceObjectRenderer.AppendFishing(terrain);
            MainSourceObjectRenderer.AppendPearls(terrain);
            MainEntityBatchRenderer.AppendDirect(terrain,xrayEnabled);
            MainTransientRenderer.AppendCrack(terrain);
            MainSourceObjectRenderer.AppendRedBoxes(terrain);

            // Exact tI(true) slot: after every world-water draw and before transparent/glass.
            MainSourceObjectRenderer.AppendShipWater(shipWater);

            // main.js clouds are after world water + transparent/glass; selection/debug lines are last.
            MainSkyRenderer.AppendClouds(alpha);
            MainSelectionOutline.AppendDirect(alpha);
        }

        static int[] nearFirstOffsets=System.Array.Empty<int>();
        static int nearFirstRadius=-1;
        /// <summary>(dx,dz) pairs of the (2r+1)^2 square sorted by distance, ties in main.js scan order.</summary>
        static int[] NearFirstOffsets(int r)
        {
            if(r==nearFirstRadius)return nearFirstOffsets;
            int side=2*r+1,n=side*side;
            var keys=new long[n];var idx=new int[n];int k=0;
            for(int dx=-r;dx<=r;dx++)for(int dz=-r;dz<=r;dz++)
            {
                keys[k]=((long)(dx*dx+dz*dz)<<32)|(uint)k;idx[k]=k;k++;
            }
            System.Array.Sort(keys,idx);
            var o=new int[n*2];
            for(int i=0;i<n;i++){int s=idx[i];o[i*2]=s/side-r;o[i*2+1]=s%side-r;}
            nearFirstOffsets=o;nearFirstRadius=r;return o;
        }

        // main.js Rh()/h2(): extract row4 +/- row1/2/3 and test the positive AABB vertex.
        // Plane normalization is intentionally skipped: multiplying a plane by a positive scalar
        // cannot change the inside/outside sign, and normalization would add six square roots per scan.
        static void ExtractSourceFrustum(Matrix4x4 m,Vector4[] planes)
        {
            Vector4 r0=m.GetRow(0),r1=m.GetRow(1),r2=m.GetRow(2),r3=m.GetRow(3);
            planes[0]=r3+r0; planes[1]=r3-r0;
            planes[2]=r3+r1; planes[3]=r3-r1;
            planes[4]=r3+r2; planes[5]=r3-r2;
        }

        static bool FastFrustumAabb(Vector4[] planes,Vector3 mn,Vector3 mx)
        {
            for(int i=0;i<6;i++)
            {
                Vector4 p=planes[i];
                float x=p.x>0f?mx.x:mn.x;
                float y=p.y>0f?mx.y:mn.y;
                float z=p.z>0f?mx.z:mn.z;
                if(p.x*x+p.y*y+p.z*z+p.w<0f)return false;
            }
            return true;
        }

        static bool TryColumnRenderBounds(ChunkColumn col,out Vector3 mn,out Vector3 mx)
        {
            int min=-1,max=-1;
            for(int s=0;s<VoxelConstants.SectionCount;s++)
            {
                var sec=col.Sections[s];if(sec==null||sec.NonAir<=0)continue;
                if(min<0)min=s;max=s;
            }
            if(min<0){mn=mx=default;return false;}
            float x0=col.Coord.X*16f,z0=col.Coord.Z*16f;
            float y0=VoxelConstants.MinY+min*16f,y1=VoxelConstants.MinY+(max+1)*16f;
            mn=new Vector3(x0,y0,z0);mx=new Vector3(x0+16f,y1,z0+16f);return true;
        }

        // BC_TEMP_DEBUG_BEGIN [R79_VIEW_GAP_SCAN] REMOVE BEFORE RELEASE
        public void RefreshVisualGapTelemetry()
        {
            // F3-only diagnostic call (MainPerformanceHud refreshes four times per second). This is
            // deliberately outside the render hot path. A loaded/missing-mesh count > 0 proves the
            // black region is a publication gap rather than absent voxel data or a shader artifact.
            Camera cam=streamingCamera;
            if(cam==null){ViewMissingDataChunks=ViewMissingMeshChunks=0;return;}
            ExtractSourceFrustum(cam.cullingMatrix,directFrustumPlanes);
            int missingData=0,missingMesh=0,rr=RenderDistance;
            for(int dx=-rr;dx<=rr;dx++)for(int dz=-rr;dz<=rr;dz++)
            {
                ChunkCoord cc=new ChunkCoord(center.X+dx,center.Z+dz);
                if(renders.TryGetValue(cc,out var cr)&&cr.Drawable)continue;
                if(chunks.TryGetValue(cc,out var col))
                {
                    if(!TryColumnRenderBounds(col,out Vector3 mn,out Vector3 mx))continue;
                    if(FastFrustumAabb(directFrustumPlanes,mn,mx))missingMesh++;
                }
                else
                {
                    float x0=cc.X*16f,z0=cc.Z*16f;
                    Vector3 mn=new Vector3(x0,VoxelConstants.MinY,z0);
                    Vector3 mx=new Vector3(x0+16f,VoxelConstants.MaxY+1f,z0+16f);
                    if(FastFrustumAabb(directFrustumPlanes,mn,mx))missingData++;
                }
            }
            ViewMissingDataChunks=missingData;ViewMissingMeshChunks=missingMesh;
        }

        // BC_TEMP_DEBUG_END [R79_VIEW_GAP_SCAN]

        public bool IsChunkLoadedAt(int wx, int wz)
        {
            return chunks.ContainsKey(ChunkCoord.FromWorld(wx, wz));
        }

        public BlockId GetBlock(int wx, int y, int wz)
        {
            if (y < VoxelConstants.MinY) return BlockId.Bedrock;
            if (y > VoxelConstants.MaxY) return BlockId.Air;
            var cc = ChunkCoord.FromWorld(wx, wz);
            if (!chunks.TryGetValue(cc, out var c)) return BlockId.Air;
            return c.GetLocal(VoxelConstants.FloorMod(wx, 16), y, VoxelConstants.FloorMod(wz, 16));
        }

        public byte GetMeta(int wx, int y, int wz)
        {
            if (y < VoxelConstants.MinY || y > VoxelConstants.MaxY) return 0;
            var cc = ChunkCoord.FromWorld(wx, wz);
            if (!chunks.TryGetValue(cc, out var c)) return 0;
            return c.GetMetaLocal(VoxelConstants.FloorMod(wx, 16), y, VoxelConstants.FloorMod(wz, 16));
        }

        public byte GetPackedLight(int wx, int y, int wz)
        {
            // Same edge defaults as meshWorker.js: outside/missing world is open sky.
            if (y > VoxelConstants.MaxY) return 0xF0;
            if (y < VoxelConstants.MinY) return 0;
            var cc = ChunkCoord.FromWorld(wx, wz);
            if (!chunks.TryGetValue(cc, out var c)) return 0xF0;
            return c.GetLightLocal(VoxelConstants.FloorMod(wx, 16), y, VoxelConstants.FloorMod(wz, 16));
        }

        static long WaterKey(int x,int y,int z)
        {
            unchecked
            {
                // world bounds are far below these packed ranges; stable identity only, no ordering requirement.
                return ((long)(x+1048576)<<42)^((long)(z+1048576)<<21)^(uint)(y-VoxelConstants.MinY);
            }
        }

        void EnqueueWater(int x,int y,int z,bool seed=false)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;
            long k=WaterKey(x,y,z);
            if(seed)
            {
                if(waterQueued.Contains(k)||waterSeedQueued.Count>=WaterQueueLimit||!waterSeedQueued.Add(k))return;
                waterSeedQueue.Enqueue(new WaterCell(x,y,z));
            }
            else
            {
                if(waterQueued.Count>=WaterQueueLimit||!waterQueued.Add(k))return;
                waterQueue.Enqueue(new WaterCell(x,y,z));
                if(waterSeedQueued.Remove(k))RemoveSeedQueueStaleIsHarmless();
            }
        }

        // Queue entries removed from waterSeedQueued can remain physically in Queue; dequeue checks membership.
        static void RemoveSeedQueueStaleIsHarmless() { }

        void QueueWaterNeighborhood(int x,int y,int z)
        {
            EnqueueWater(x,y,z);EnqueueWater(x+1,y,z);EnqueueWater(x-1,y,z);
            EnqueueWater(x,y+1,z);EnqueueWater(x,y-1,z);EnqueueWater(x,y,z+1);EnqueueWater(x,y,z-1);
        }

        void EnqueueLava(int x,int y,int z,bool seed=false)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;
            long k=WaterKey(x,y,z);
            if(seed)
            {
                if(lavaQueued.Contains(k)||lavaSeedQueued.Count>=LavaQueueLimit||!lavaSeedQueued.Add(k))return;
                lavaSeedQueue.Enqueue(new WaterCell(x,y,z));
            }
            else
            {
                if(lavaQueued.Count>=LavaQueueLimit||!lavaQueued.Add(k))return;
                lavaQueue.Enqueue(new WaterCell(x,y,z));
            }
        }

        void QueueLavaNeighborhood(int x,int y,int z)
        {
            EnqueueLava(x,y,z);EnqueueLava(x+1,y,z);EnqueueLava(x-1,y,z);
            EnqueueLava(x,y+1,z);EnqueueLava(x,y-1,z);EnqueueLava(x,y,z+1);EnqueueLava(x,y,z-1);
        }

        BlockId ShipAwareWaterBlock(int x,int y,int z)
        {
            BlockId id=GetBlock(x,y,z);
            if(id==BlockId.Air&&MainShipRuntime.TryGetCarvedWorldBlock(x,y,z,out BlockId cached))return cached;
            return id;
        }
        bool SourceWaterCarrier(int x,int y,int z)
        {
            BlockId id=ShipAwareWaterBlock(x,y,z);
            if(id==BlockId.Water)return true;
            if(!BlockRegistry.IsAquatic(id))return false;
            return BlockRegistry.NeedsWater(id)||(GetMeta(x,y,z)&4)!=0;
        }
        bool SourceWater(int x,int y,int z)=>SourceWaterCarrier(x,y,z);
        bool WaterReplaceable(int x,int y,int z)
        {
            BlockId id=GetBlock(x,y,z);
            return !MainShipRuntime.IsReservedWorldCell(x,y,z)&&
                   (id==BlockId.Air||(BlockRegistry.IsSourceFluidReplaceableCross(id)&&!BlockRegistry.IsAquatic(id)));
        }
        static bool SourceWaterSupport(BlockId id)=>BlockRegistry.IsSourceFluidSupport(id);

        void TakeWaterCandidates(Queue<WaterCell> q,HashSet<long> membership,int limit)
        {
            while(limit>0&&q.Count>0)
            {
                var c=q.Dequeue();long k=WaterKey(c.X,c.Y,c.Z);
                if(!membership.Remove(k))continue;
                waterTickScratch.Add(c);limit--;
            }
        }

        void AdvanceSourceWater(float frameDt,double deadlineMs)
        {
            // main.js sP()/fE() is an atomic bounded tick: every 200 ms it takes at most 400 normal
            // + 80 seed candidates and applies at most 120 cell changes in the same JS turn.
            // R54-R72 tried to resume that logical tick across Unity frames under the streaming
            // deadline. That was incorrect: publishing any unrelated chunk changed worldQueryRevision,
            // aborted the pending water transaction, and under sustained streaming could starve water
            // indefinitely. Run the exact bounded batch atomically on the main thread instead.
            waterTickClock+=Mathf.Max(0f,frameDt);
            LastWaterTicks=0;
            if(waterTickClock<=WaterFixedStep)return;
            waterTickClock=0f;

            // A carried transaction can only exist after domain reload / revision upgrade from the old
            // cooperative implementation. Put its candidates back before starting a source-style tick.
            if(waterTickActive)
            {
                AbortSourceWaterFixedTick();
                waterTickClock=0f;
            }

            BeginSourceWaterFixedTick();
            if(waterTickActive)
            {
                // Infinite deadline is safe here because the source batch itself is hard-bounded
                // (<=480 candidates, <=120 mutations); no unbounded flood fill occurs in this path.
                StepSourceWaterFixedTick(double.PositiveInfinity);
            }
            LastWaterTicks=1;
        }

        BlockId WaterSimBlock(int x,int y,int z)
        {
            if(waterMutationIndexScratch.TryGetValue(WaterKey(x,y,z),out int index))return waterMutationScratch[index].Id;
            return ShipAwareWaterBlock(x,y,z);
        }

        int WaterSimLevel(int x,int y,int z)
        {
            BlockId id=WaterSimBlock(x,y,z);
            int level=BlockRegistry.WaterLevel(id);
            if(level>0)return level;
            if(!waterMutationIndexScratch.ContainsKey(WaterKey(x,y,z))&&SourceWaterCarrier(x,y,z))return 4;
            return 0;
        }
        bool SourceWaterSim(int x,int y,int z)
        {
            BlockId id=WaterSimBlock(x,y,z);
            if(id==BlockId.Water)return true;
            return !waterMutationIndexScratch.ContainsKey(WaterKey(x,y,z))&&SourceWaterCarrier(x,y,z);
        }
        bool WaterReplaceableSim(int x,int y,int z)
        {
            if(MainShipRuntime.IsReservedWorldCell(x,y,z))return false;
            BlockId id=WaterSimBlock(x,y,z);
            return id==BlockId.Air||(BlockRegistry.IsSourceFluidReplaceableCross(id)&&!BlockRegistry.IsAquatic(id));
        }

        void StageWaterMutation(int x,int y,int z,BlockId id)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;
            long key=WaterKey(x,y,z);
            if(waterMutationIndexScratch.TryGetValue(key,out int index))
            {
                WaterMutation m=waterMutationScratch[index];m.Id=id;waterMutationScratch[index]=m;
            }
            else
            {
                waterMutationIndexScratch.Add(key,waterMutationScratch.Count);
                waterMutationScratch.Add(new WaterMutation(x,y,z,id));
            }
            // Queue side-effects are committed with the mutation, not while the transactional
            // overlay is speculative. This makes an aborted/rescheduled tick side-effect free.
        }

        void BeginSourceWaterFixedTick()
        {
            waterTickScratch.Clear();waterMutationScratch.Clear();waterMutationIndexScratch.Clear();
            if(waterQueue.Count==0&&waterSeedQueue.Count==0){waterTickActive=false;return;}
            TakeWaterCandidates(waterQueue,waterQueued,400);
            TakeWaterCandidates(waterSeedQueue,waterSeedQueued,80);
            if(waterTickScratch.Count==0){waterTickActive=false;return;}
            waterTickIndex=0;waterTickChangesRemaining=120;waterTickWorldRevision=worldQueryRevision;waterTickActive=true;
        }

        void AbortSourceWaterFixedTick()
        {
            // The source tick is atomic with respect to JS gameplay. If Unity gameplay changed the
            // loaded world while this cooperative transaction was paused, discard the overlay and
            // retry its candidates from current world state instead of committing stale decisions.
            for(int i=0;i<waterTickScratch.Count;i++)
            {
                WaterCell c=waterTickScratch[i];EnqueueWater(c.X,c.Y,c.Z);
            }
            waterTickScratch.Clear();waterMutationScratch.Clear();waterMutationIndexScratch.Clear();
            waterTickIndex=0;waterTickChangesRemaining=0;waterTickActive=false;
            waterTickClock=WaterFixedStep+.0001f;
        }

        bool StepSourceWaterFixedTick(double deadlineMs)
        {
            if(!waterTickActive)return true;
            if(worldQueryRevision!=waterTickWorldRevision){AbortSourceWaterFixedTick();return true;}
            int checks=0;
            while(waterTickIndex<waterTickScratch.Count)
            {
                WaterCell c=waterTickScratch[waterTickIndex++];
                if(waterTickChangesRemaining<=0){EnqueueWater(c.X,c.Y,c.Z);continue;}
                if(Player!=null&&(Mathf.Abs(c.X-Player.position.x)>96f||Mathf.Abs(c.Z-Player.position.z)>96f))
                {EnqueueWater(c.X,c.Y,c.Z,true);continue;}

                BlockId cur=WaterSimBlock(c.X,c.Y,c.Z);int level=WaterSimLevel(c.X,c.Y,c.Z);bool source=SourceWaterSim(c.X,c.Y,c.Z);
                if(level==0)continue;
                waterTickChangesRemaining--;

                if(!source)
                {
                    int sourceCount=(SourceWaterSim(c.X+1,c.Y,c.Z)?1:0)+(SourceWaterSim(c.X-1,c.Y,c.Z)?1:0)+
                                    (SourceWaterSim(c.X,c.Y,c.Z+1)?1:0)+(SourceWaterSim(c.X,c.Y,c.Z-1)?1:0);
                    if(sourceCount>=2){StageWaterMutation(c.X,c.Y,c.Z,BlockId.Water);continue;}
                    bool supported=WaterSimLevel(c.X,c.Y+1,c.Z)>0;
                    for(int d=0;d<4&&!supported;d++)
                    {
                        int nx=c.X+WaterDX[d],nz=c.Z+WaterDZ[d];
                        if(WaterSimLevel(nx,c.Y,nz)>level||SourceWaterSim(nx,c.Y,nz))supported=true;
                    }
                    if(!supported)
                    {
                        int lower=level-1;
                        StageWaterMutation(c.X,c.Y,c.Z,BlockRegistry.WaterForLevel(lower));
                        EnqueueWater(c.X,c.Y,c.Z);
                        continue;
                    }
                }

                BlockId below=WaterSimBlock(c.X,c.Y-1,c.Z);
                if(WaterReplaceableSim(c.X,c.Y-1,c.Z))
                {
                    if(c.Y-1>=VoxelConstants.MinY)StageWaterMutation(c.X,c.Y-1,c.Z,BlockId.Flow3);
                }
                else if(level>1&&(SourceWaterSupport(below)||SourceWaterSim(c.X,c.Y-1,c.Z)))
                {
                    BlockId next=BlockRegistry.WaterForLevel(level-1);
                    for(int d=0;d<4;d++)
                    {
                        int nx=c.X+WaterDX[d],nz=c.Z+WaterDZ[d];
                        if(WaterReplaceableSim(nx,c.Y,nz))StageWaterMutation(nx,c.Y,nz,next);
                    }
                }

                if((++checks&7)==0&&NowMs()>=deadlineMs)return false;
            }

            // No world mutation happened while this transaction was paused, so its staged overlay is
            // still valid. Commit once, then defer exact lighting through the ordered relight pipeline.
            CommitWaterMutations();
            waterTickScratch.Clear();waterMutationScratch.Clear();waterMutationIndexScratch.Clear();
            waterTickIndex=0;waterTickChangesRemaining=0;waterTickActive=false;
            return true;
        }

        void CollectSimulationDirtySections(ChunkCoord cc,int lx,int y,int lz,HashSet<SectionKey> target)
        {
            int sec=(y-VoxelConstants.MinY)>>4,ly=(y-VoxelConstants.MinY)&15;
            int[] ex=lx<=1?EditNegOffsets:(lx>=14?EditPosOffsets:EditZeroOffsets);
            int[] ez=lz<=1?EditNegOffsets:(lz>=14?EditPosOffsets:EditZeroOffsets);
            int[] es=ly<=1?EditNegOffsets:(ly>=14?EditPosOffsets:EditZeroOffsets);
            for(int zi=0;zi<ez.Length;zi++)for(int xi=0;xi<ex.Length;xi++)for(int yi=0;yi<es.Length;yi++)
            {
                int ss=sec+es[yi];if((uint)ss>=VoxelConstants.SectionCount)continue;
                var nc=new ChunkCoord(cc.X+ex[xi],cc.Z+ez[zi]);
                if(chunks.ContainsKey(nc))target.Add(new SectionKey(nc,ss));
            }
        }

        void CommitWaterMutations()
        {
            if(waterMutationScratch.Count==0)return;
            waterDirtySectionsScratch.Clear();
            bool any=false;
            for(int i=0;i<waterMutationScratch.Count;i++)
            {
                WaterMutation m=waterMutationScratch[i];
                ChunkCoord cc=ChunkCoord.FromWorld(m.X,m.Z);
                if(!chunks.TryGetValue(cc,out var col))continue;
                int lx=VoxelConstants.FloorMod(m.X,16),lz=VoxelConstants.FloorMod(m.Z,16);
                BlockId old=col.GetLocal(lx,m.Y,lz);byte oldMeta=col.GetMetaLocal(lx,m.Y,lz);
                if(old==m.Id&&oldMeta==0)continue;
                col.SetLocal(lx,m.Y,lz,m.Id,0);any=true;
                QueueWaterNeighborhood(m.X,m.Y,m.Z);QueueLavaNeighborhood(m.X,m.Y,m.Z);
                MobProjectileSystem.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainLeafDecay.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainBlockLifecycle.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainSourceObjectRenderer.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainBlockUpdates.Enqueue(m.X,m.Y,m.Z,m.Id);
                if(!(BlockRegistry.IsWater(old)&&BlockRegistry.IsWater(m.Id)))
                    lightEditQueue.Enqueue(new LightEdit(m.X,m.Y,m.Z,old,oldMeta,m.Id,0));
                CollectSimulationDirtySections(cc,lx,m.Y,lz,waterDirtySectionsScratch);
            }
            if(!any)return;
            unchecked{worldQueryRevision++;}
            foreach(var sk in waterDirtySectionsScratch)Dirty(sk);
            waterDirtySectionsScratch.Clear();
        }

        void TakeLavaCandidates(Queue<WaterCell> q,HashSet<long> membership,int limit)
        {
            while(limit>0&&q.Count>0)
            {
                WaterCell c=q.Dequeue();long k=WaterKey(c.X,c.Y,c.Z);
                if(!membership.Remove(k))continue;
                lavaTickScratch.Add(c);limit--;
            }
        }

        void AdvanceSourceLava(float frameDt,double deadlineMs)
        {
            // main.js CP()/gc(): every 500 ms consume <=240 normal + <=40 seed candidates and
            // perform at most 60 source changes. As with water, this hard-bounded tick is atomic.
            lavaTickClock+=Mathf.Max(0f,frameDt);
            LastLavaTicks=0;
            if(lavaTickClock<=LavaFixedStep)return;
            lavaTickClock=0f;
            if(lavaTickActive)
            {
                AbortSourceLavaFixedTick();
                lavaTickClock=0f;
            }
            BeginSourceLavaFixedTick();
            if(lavaTickActive)StepSourceLavaFixedTick(double.PositiveInfinity);
            LastLavaTicks=1;
        }

        BlockId LavaSimBlock(int x,int y,int z)
        {
            if(lavaMutationIndexScratch.TryGetValue(WaterKey(x,y,z),out int index))return lavaMutationScratch[index].Id;
            return GetBlock(x,y,z);
        }

        int LavaSimLevel(int x,int y,int z)=>BlockRegistry.LavaLevel(LavaSimBlock(x,y,z));
        bool LavaReplaceableSim(int x,int y,int z)
        {
            BlockId id=LavaSimBlock(x,y,z);
            return id==BlockId.Air||(BlockRegistry.IsSourceFluidReplaceableCross(id)&&!BlockRegistry.IsAquatic(id));
        }
        bool SourceWaterContact(int x,int y,int z)
        {
            BlockId id=ShipAwareWaterBlock(x,y,z);
            if(BlockRegistry.IsWater(id))return true;
            return BlockRegistry.IsAquatic(id)&&(BlockRegistry.NeedsWater(id)||(GetMeta(x,y,z)&4)!=0);
        }
        bool LavaTouchesWaterSim(int x,int y,int z)
        {
            return SourceWaterContact(x,y+1,z)||SourceWaterContact(x,y-1,z)||
                   SourceWaterContact(x+1,y,z)||SourceWaterContact(x-1,y,z)||
                   SourceWaterContact(x,y,z+1)||SourceWaterContact(x,y,z-1);
        }

        void StageLavaMutation(int x,int y,int z,BlockId id)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;
            long key=WaterKey(x,y,z);
            if(lavaMutationIndexScratch.TryGetValue(key,out int index))
            {
                WaterMutation m=lavaMutationScratch[index];m.Id=id;lavaMutationScratch[index]=m;
            }
            else
            {
                lavaMutationIndexScratch.Add(key,lavaMutationScratch.Count);
                lavaMutationScratch.Add(new WaterMutation(x,y,z,id));
            }
        }

        void BeginSourceLavaFixedTick()
        {
            lavaTickScratch.Clear();lavaMutationScratch.Clear();lavaMutationIndexScratch.Clear();
            if(lavaQueue.Count==0&&lavaSeedQueue.Count==0){lavaTickActive=false;return;}
            TakeLavaCandidates(lavaQueue,lavaQueued,240);
            TakeLavaCandidates(lavaSeedQueue,lavaSeedQueued,40);
            if(lavaTickScratch.Count==0){lavaTickActive=false;return;}
            lavaTickIndex=0;lavaTickChangesRemaining=60;lavaTickWorldRevision=worldQueryRevision;lavaTickActive=true;
        }

        void AbortSourceLavaFixedTick()
        {
            for(int i=0;i<lavaTickScratch.Count;i++)
            {
                WaterCell c=lavaTickScratch[i];EnqueueLava(c.X,c.Y,c.Z);
            }
            lavaTickScratch.Clear();lavaMutationScratch.Clear();lavaMutationIndexScratch.Clear();
            lavaTickIndex=0;lavaTickChangesRemaining=0;lavaTickActive=false;
            lavaTickClock=LavaFixedStep+.0001f;
        }

        bool StepSourceLavaFixedTick(double deadlineMs)
        {
            if(!lavaTickActive)return true;
            if(worldQueryRevision!=lavaTickWorldRevision){AbortSourceLavaFixedTick();return true;}
            int checks=0;
            while(lavaTickIndex<lavaTickScratch.Count)
            {
                WaterCell c=lavaTickScratch[lavaTickIndex++];
                if(lavaTickChangesRemaining<=0){EnqueueLava(c.X,c.Y,c.Z);continue;}
                if(Player!=null&&(Mathf.Abs(c.X-Player.position.x)>96f||Mathf.Abs(c.Z-Player.position.z)>96f))
                {EnqueueLava(c.X,c.Y,c.Z,true);continue;}

                BlockId cur=LavaSimBlock(c.X,c.Y,c.Z);int level=BlockRegistry.LavaLevel(cur);
                if(level==0)continue;
                lavaTickChangesRemaining--;

                // main nJ(): any source water/flow or water-carrying aquatic neighbour solidifies lava.
                if(LavaTouchesWaterSim(c.X,c.Y,c.Z))
                {
                    StageLavaMutation(c.X,c.Y,c.Z,cur==BlockId.Lava?BlockId.Obsidian:BlockId.Cobblestone);
                    continue;
                }

                if(cur!=BlockId.Lava)
                {
                    bool supported=LavaSimLevel(c.X,c.Y+1,c.Z)>0||
                                   LavaSimLevel(c.X+1,c.Y,c.Z)>level||LavaSimLevel(c.X-1,c.Y,c.Z)>level||
                                   LavaSimLevel(c.X,c.Y,c.Z+1)>level||LavaSimLevel(c.X,c.Y,c.Z-1)>level;
                    if(!supported)
                    {
                        StageLavaMutation(c.X,c.Y,c.Z,BlockRegistry.LavaForLevel(level-1));
                        EnqueueLava(c.X,c.Y,c.Z);
                        continue;
                    }
                }

                BlockId below=LavaSimBlock(c.X,c.Y-1,c.Z);
                if(LavaReplaceableSim(c.X,c.Y-1,c.Z))
                {
                    if(c.Y-1>=VoxelConstants.MinY)StageLavaMutation(c.X,c.Y-1,c.Z,BlockId.LavaFlow2);
                }
                else if(level>1&&(BlockRegistry.IsSourceFluidSupport(below)||below==BlockId.Lava))
                {
                    BlockId next=BlockRegistry.LavaForLevel(level-1);
                    for(int d=0;d<4;d++)
                    {
                        int nx=c.X+WaterDX[d],nz=c.Z+WaterDZ[d];
                        if(LavaReplaceableSim(nx,c.Y,nz))StageLavaMutation(nx,c.Y,nz,next);
                    }
                }
                if((++checks&7)==0&&NowMs()>=deadlineMs)return false;
            }

            CommitLavaMutations();
            lavaTickScratch.Clear();lavaMutationScratch.Clear();lavaMutationIndexScratch.Clear();
            lavaTickIndex=0;lavaTickChangesRemaining=0;lavaTickActive=false;
            return true;
        }

        void CommitLavaMutations()
        {
            if(lavaMutationScratch.Count==0)return;
            lavaDirtySectionsScratch.Clear();bool any=false;
            for(int i=0;i<lavaMutationScratch.Count;i++)
            {
                WaterMutation m=lavaMutationScratch[i];ChunkCoord cc=ChunkCoord.FromWorld(m.X,m.Z);
                if(!chunks.TryGetValue(cc,out ChunkColumn col))continue;
                int lx=VoxelConstants.FloorMod(m.X,16),lz=VoxelConstants.FloorMod(m.Z,16);
                BlockId old=col.GetLocal(lx,m.Y,lz);byte oldMeta=col.GetMetaLocal(lx,m.Y,lz);
                if(old==m.Id&&oldMeta==0)continue;
                col.SetLocal(lx,m.Y,lz,m.Id,0);any=true;
                QueueWaterNeighborhood(m.X,m.Y,m.Z);QueueLavaNeighborhood(m.X,m.Y,m.Z);
                MobProjectileSystem.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainLeafDecay.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainBlockLifecycle.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainSourceObjectRenderer.NotifyBlockChanged(m.X,m.Y,m.Z);
                MainBlockUpdates.Enqueue(m.X,m.Y,m.Z,m.Id);
                lightEditQueue.Enqueue(new LightEdit(m.X,m.Y,m.Z,old,oldMeta,m.Id,0));
                CollectSimulationDirtySections(cc,lx,m.Y,lz,lavaDirtySectionsScratch);
            }
            if(!any)return;
            unchecked{worldQueryRevision++;}
            foreach(SectionKey sk in lavaDirtySectionsScratch)Dirty(sk);
            lavaDirtySectionsScratch.Clear();
        }

        static int PersistentLocalKey(int lx,int y,int lz)=>((lx*16+lz)*VoxelConstants.WorldHeight)+(y-VoxelConstants.MinY);

        static void DecodePersistentLocalKey(int key,out int lx,out int y,out int lz)
        {
            int col=key/VoxelConstants.WorldHeight;
            y=VoxelConstants.MinY+(key-col*VoxelConstants.WorldHeight);
            lx=col/16;lz=col-lx*16;
        }

        void ResetPersistentJournal()
        {
            persistentJournalHead=persistentJournalTail=null;
            Volatile.Write(ref persistentJournalRecordCount,0);
        }

        void AppendPersistentJournalRecord(PersistentEditRecord record,bool incrementVersion)
        {
            PersistentEditJournalSegment tail=persistentJournalTail;
            if(tail==null||Volatile.Read(ref tail.Count)>=PersistentJournalSegmentSize)
            {
                var next=new PersistentEditJournalSegment();
                if(tail==null)persistentJournalHead=next;
                else Volatile.Write(ref tail.Next,next);
                persistentJournalTail=next;tail=next;
            }
            int index=Volatile.Read(ref tail.Count);
            tail.Records[index]=record;
            Volatile.Write(ref tail.Count,index+1);
            Volatile.Write(ref persistentJournalRecordCount,Volatile.Read(ref persistentJournalRecordCount)+1);
            if(incrementVersion)persistentJournalVersion++;
        }

        void LoadPersistentEditsSource(int[] sourceEdits)
        {
            lock(persistentEditLock)
            {
                persistentEdits.Clear();PersistentEditCount=0;PersistentSaveDirty=false;
                ResetPersistentJournal();persistentJournalVersion=0;
                if(sourceEdits==null)return;
                for(int i=0;i+3<sourceEdits.Length;i+=4)
                {
                    int wx=sourceEdits[i],y=sourceEdits[i+1],sourceZ=sourceEdits[i+2],packed=sourceEdits[i+3];
                    if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)continue;
                    int wz=SourceCoords.SourceBlockZToUnity(sourceZ);
                    var cc=ChunkCoord.FromWorld(wx,wz);
                    int lx=VoxelConstants.FloorMod(wx,16),lz=VoxelConstants.FloorMod(wz,16);
                    int rawId=packed&0xFFFF;if(rawId<0||rawId>(int)BlockId.DarkOakSapling)continue;
                    BlockId id=(BlockId)rawId;
                    byte sourceMeta=(byte)((packed>>16)&255);
                    byte meta=SourceCoords.SourceMetaToUnity(id,sourceMeta);
                    int key=PersistentLocalKey(lx,y,lz),value=((int)id&0xFFFF)|(meta<<16);
                    if(!persistentEdits.TryGetValue(cc,out var map)){map=new Dictionary<int,int>();persistentEdits.Add(cc,map);}
                    if(!map.ContainsKey(key))PersistentEditCount++;
                    map[key]=value;
                    AppendPersistentJournalRecord(new PersistentEditRecord(wx,y,sourceZ,((int)id&0xFFFF)|(sourceMeta<<16)),false);
                }
            }
        }

        bool ApplyPersistentEdits(ChunkColumn column)
        {
            lock(persistentEditLock)
            {
                if(!persistentEdits.TryGetValue(column.Coord,out var map)||map.Count==0)return false;
                foreach(var kv in map)
                {
                    DecodePersistentLocalKey(kv.Key,out int lx,out int y,out int lz);
                    int packed=kv.Value;column.SetLocal(lx,y,lz,(BlockId)(packed&0xFFFF),(byte)((packed>>16)&255));
                    // R61 migration: old R60 saves have sparse sapling/crop/farmland/fire edits but no timer arrays.
                    MainBlockLifecycle.NotifyBlockChanged(column.Coord.X*16+lx,y,column.Coord.Z*16+lz);
                }
                return true;
            }
        }

        void RecordPersistentEdit(ChunkCoord cc,int lx,int y,int lz,BlockId id,byte meta)
        {
            lock(persistentEditLock)
            {
                if(!persistentEdits.TryGetValue(cc,out var map)){map=new Dictionary<int,int>();persistentEdits.Add(cc,map);}
                int key=PersistentLocalKey(lx,y,lz);
                if(!map.ContainsKey(key))PersistentEditCount++;
                map[key]=((int)id&0xFFFF)|(meta<<16);
                int wx=cc.X*16+lx,wz=cc.Z*16+lz;
                byte sourceMeta=SourceCoords.UnityMetaToSource(id,meta);
                AppendPersistentJournalRecord(new PersistentEditRecord(wx,y,SourceCoords.UnityBlockZToSource(wz),((int)id&0xFFFF)|(sourceMeta<<16)),true);
                PersistentSaveDirty=true;
            }
        }

        public PersistentEditJournalSnapshot CapturePersistentEditJournal()
        {
            // O(1) main-thread save capture: records are append-only and published with Volatile.Write.
            lock(persistentEditLock)
                return new PersistentEditJournalSnapshot{Head=persistentJournalHead,Tail=persistentJournalTail,RecordCount=persistentJournalRecordCount,Version=persistentJournalVersion};
        }

        static long PersistentWorldKey(int x,int y,int sourceZ)
        {
            unchecked{return((long)(x+1048576)<<42)^((long)(sourceZ+1048576)<<21)^(uint)(y-VoxelConstants.MinY);}
        }

        public static int[] MaterializePersistentEditsSource(PersistentEditJournalSnapshot snapshot)
        {
            if(snapshot==null||snapshot.Head==null||snapshot.RecordCount<=0)return Array.Empty<int>();
            var latest=new Dictionary<long,PersistentEditRecord>(Math.Max(16,snapshot.RecordCount));
            PersistentEditJournalSegment seg=snapshot.Head;int remaining=snapshot.RecordCount;
            while(seg!=null&&remaining>0)
            {
                int count=Math.Min(Volatile.Read(ref seg.Count),remaining);
                for(int i=0;i<count;i++)
                {
                    PersistentEditRecord r=seg.Records[i];
                    latest[PersistentWorldKey(r.X,r.Y,r.SourceZ)]=r;
                }
                remaining-=count;seg=Volatile.Read(ref seg.Next);
            }
            int[] flat=new int[latest.Count*4];int o=0;
            foreach(var kv in latest)
            {
                PersistentEditRecord r=kv.Value;
                flat[o++]=r.X;flat[o++]=r.Y;flat[o++]=r.SourceZ;flat[o++]=r.Packed;
            }
            return flat;
        }

        public static PersistentEditJournalSnapshot BuildCompactedPersistentJournal(int[] sourceEdits,long version)
        {
            var snap=new PersistentEditJournalSnapshot{Version=version};
            if(sourceEdits==null||sourceEdits.Length<4)return snap;
            PersistentEditJournalSegment head=null,tail=null;int records=0;
            for(int i=0;i+3<sourceEdits.Length;i+=4)
            {
                if(tail==null||tail.Count>=PersistentJournalSegmentSize)
                {
                    var n=new PersistentEditJournalSegment();if(head==null)head=n;else tail.Next=n;tail=n;
                }
                tail.Records[tail.Count++]=new PersistentEditRecord(sourceEdits[i],sourceEdits[i+1],sourceEdits[i+2],sourceEdits[i+3]);records++;
            }
            snap.Head=head;snap.Tail=tail;snap.RecordCount=records;return snap;
        }

        public bool TryInstallCompactedPersistentJournal(PersistentEditJournalSnapshot compacted,long expectedVersion)
        {
            if(compacted==null)return false;
            lock(persistentEditLock)
            {
                if(persistentJournalVersion!=expectedVersion)return false;
                persistentJournalHead=compacted.Head;persistentJournalTail=compacted.Tail;
                Volatile.Write(ref persistentJournalRecordCount,compacted.RecordCount);
                return true;
            }
        }

        public int[] CapturePersistentEditsSource()
        {
            // Compatibility/debug API. Runtime autosave uses CapturePersistentEditJournal() and does
            // the O(N) dedupe/flatten step on its background writer instead of the frame thread.
            return MaterializePersistentEditsSource(CapturePersistentEditJournal());
        }

        public void MarkPersistentSaveClean(){PersistentSaveDirty=false;}
        public void MarkPersistentSaveDirty(){PersistentSaveDirty=true;}

        // Large source operations such as P6() ship detachment must remain persistent without
        // invoking the synchronous single-click relight/remesh path once per hull block.
        public bool SetBlockDeferredPersistent(int wx,int y,int wz,BlockId id,byte meta=0)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return false;
            ChunkCoord cc=ChunkCoord.FromWorld(wx,wz);
            int lx=VoxelConstants.FloorMod(wx,16),lz=VoxelConstants.FloorMod(wz,16);
            bool ok=SetBlock(wx,y,wz,id,meta,false);
            if(ok)RecordPersistentEdit(cc,lx,y,lz,id,meta);
            return ok;
        }

        public bool SetBlock(int wx, int y, int wz, BlockId id, byte meta = 0, bool playerEdit = false)
        {
            if (y < VoxelConstants.MinY || y > VoxelConstants.MaxY) return false;
            var cc = ChunkCoord.FromWorld(wx, wz);
            if (!chunks.TryGetValue(cc, out var c)) return false;
            int lx = VoxelConstants.FloorMod(wx, 16), lz = VoxelConstants.FloorMod(wz, 16);
            BlockId old = c.GetLocal(lx, y, lz);
            byte oldMeta = c.GetMetaLocal(lx, y, lz);
            if (old == id && oldMeta == meta) return true;

            // R52 functional containers are intentionally static-world only. Observe ordinary player
            // edits before the voxel changes, but never hook deferred/ship/fluid mutations in this revision.
            // Same-block metadata edits (chest pairing/furnace state) preserve their inventories.
            if(playerEdit)MainWorldFunctionalBlocks.NotifyWorldBlockReplacing(wx,y,wz,old,oldMeta,id,meta);
            c.SetLocal(lx, y, lz, id, meta);
            unchecked{worldQueryRevision++;}
            if(playerEdit)RecordPersistentEdit(cc,lx,y,lz,id,meta);
            MobProjectileSystem.NotifyBlockChanged(wx,y,wz);
            // main et[] -> hT(): every ordinary-world edit schedules the six neighbouring leaves.
            MainLeafDecay.NotifyBlockChanged(wx,y,wz);
            // main ZP()/eh()/uh() lifecycle callbacks: dirt/farmland, crops, saplings and fire.
            MainBlockLifecycle.NotifyBlockChanged(wx,y,wz);
            // main.js block update callbacks run rE() on the edited cell and the cell above.
            MainSourceObjectRenderer.NotifyBlockChanged(wx,y,wz);
            QueueWaterNeighborhood(wx,y,wz); // main et[] -> oP() on every nA() edit.
            QueueLavaNeighborhood(wx,y,wz);  // main et[] -> uP() on every nA() edit.
            // main.js nA() queues N8() for every block/meta edit except water->water flow updates.
            // Metadata alone can change light passage (door/stairs), so comparing only opacity is wrong.
            bool needsLightEdit=!(BlockRegistry.IsWater(old)&&BlockRegistry.IsWater(id));

            // The old Unity path synchronously rebaked a complete loaded 3x3 neighbourhood here,
            // which could stall a click for tens/hundreds of milliseconds. main.js instead runs its
            // incremental G4()/aD() light update before the player-edit mesh stage. Do the same for
            // interactive edits so the newly exposed face never renders with stale dark light/AO.
            // Non-player edits remain frame-budgeted through the queue.
            lightDirtySectionsScratch.Clear();
            if (needsLightEdit)
            {
                if(playerEdit) VoxelLighting.RelightEditIncremental(chunks,wx,y,wz,lightDirtySectionsScratch);
                else lightEditQueue.Enqueue(new LightEdit(wx,y,wz,old,oldMeta,id,meta));
            }

            playerEditSectionsScratch.Clear();
            int sec = (y - VoxelConstants.MinY) >> 4;
            int ly=(y-VoxelConstants.MinY)&15;
            // ChunkMesher SampleCorner() reaches up to two voxels around a face corner. Include the
            // neighbouring section/chunk when an edit is within one voxel of a boundary, including
            // diagonal corner neighbours. This removes stale AO/light seams without broad remeshing.
            int[] ex=lx<=1?EditNegOffsets:(lx>=14?EditPosOffsets:EditZeroOffsets);
            int[] ez=lz<=1?EditNegOffsets:(lz>=14?EditPosOffsets:EditZeroOffsets);
            int[] es=ly<=1?EditNegOffsets:(ly>=14?EditPosOffsets:EditZeroOffsets);
            for(int zi=0;zi<ez.Length;zi++)for(int xi=0;xi<ex.Length;xi++)for(int yi=0;yi<es.Length;yi++)
            {
                int ss=sec+es[yi];
                if((uint)ss>=VoxelConstants.SectionCount)continue;
                AddEditedSection(new SectionKey(new ChunkCoord(cc.X+ex[xi],cc.Z+ez[zi]),ss),playerEdit);
            }

            if(playerEdit&&lightDirtySectionsScratch.Count>0)
            {
                foreach(var lk in lightDirtySectionsScratch)
                {
                    bool already=false;
                    for(int i=0;i<playerEditSectionsScratch.Count;i++)if(playerEditSectionsScratch[i].Equals(lk)){already=true;break;}
                    if(!already)DirtySingle(lk);
                }
            }

            // main.js dirtyEdit -> gB(): a player edit is meshed synchronously from only the touched
            // geometry sections, but now from already-corrected packed light. More distant sections
            // reached by light propagation are rebuilt asynchronously from the precise dirty set above.
            if(playerEdit) FastRebuildPlayerEditSections(playerEditSectionsScratch);
            // main nA() -> et[]: support loss (Qu), concrete powder, coral, cactus, sponge.
            MainBlockUpdates.Enqueue(wx,y,wz,id);
            MainBlockUpdates.Drain(this);
            return true;
        }

        void AddEditedSection(SectionKey k,bool collectPlayerEdit)
        {
            if(k.Section<0||k.Section>=VoxelConstants.SectionCount||!chunks.ContainsKey(k.Chunk))return;
            Dirty(k);
            if(!collectPlayerEdit)return;
            for(int i=0;i<playerEditSectionsScratch.Count;i++)if(playerEditSectionsScratch[i].Equals(k))return;
            playerEditSectionsScratch.Add(k);
        }

        void CancelQueuedMesh(SectionKey k)
        {
            if(!meshQueued.Remove(k))return;
            if(meshQueuedMasks.TryGetValue(k.Chunk,out uint mask))
            {
                mask&=~(1u<<k.Section);if(mask==0)meshQueuedMasks.Remove(k.Chunk);else meshQueuedMasks[k.Chunk]=mask;
            }
            DecrementPendingMesh(k.Chunk);
        }

        void FastRebuildPlayerEditSections(List<SectionKey> keys)
        {
            playerEditChunksScratch.Clear();
            for(int i=0;i<keys.Count;i++)
            {
                SectionKey k=keys[i];
                if(!InRenderRadius(k.Chunk)||!NeighborhoodReady(k.Chunk))continue;
                if(renders.TryGetValue(k.Chunk,out var existing)&&(!existing.HasCpuGeometry||existing.FullCpuRebuildPending))continue;

                int stamp=sectionStamp.TryGetValue(k,out var st)?st:0;
                MeshBuildResult result;
                SectionSnapshot snap=Capture(k,stamp);
                try
                {
                    result=!snap.Valid?EmptyResult(k,stamp):ChunkMesher.Build(snap);
                }
                catch(Exception ex)
                {
                    errors.Enqueue("Player edit mesher "+k+": "+ex);
                    continue;
                }
                finally
                {
                    if(snap.Valid)snap.Release();
                }

                // Remove the queued copy of this same latest section. An older in-flight job cannot
                // be cancelled, but its stale stamp is ignored on return without scheduling another rebuild.
                CancelQueuedMesh(k);
                sectionAppliedStamp[k]=stamp;

                if(!renders.TryGetValue(k.Chunk,out var cr))
                {
                    if(result.Empty)continue;
                    cr=new ChunkRender(k.Chunk);
                    renders[k.Chunk]=cr;
                }
                cr.ApplySection(ref result);
                bool seen=false;
                for(int q=0;q<playerEditChunksScratch.Count;q++)if(playerEditChunksScratch[q].Equals(k.Chunk)){seen=true;break;}
                if(!seen)playerEditChunksScratch.Add(k.Chunk);
            }

            for(int i=0;i<playerEditChunksScratch.Count;i++)
            {
                ChunkCoord c=playerEditChunksScratch[i];
                if(!renders.TryGetValue(c,out var cr)||!cr.NeedsUpload)continue;
                if(cr.Rebuild()){publishedRenderRadiusDirty=true;MarkRenderCommandsDirty(true);} // source dirtyEdit uploads immediately; structural changes may alter candidate membership/layers.
            }
        }

        void ProcessLightEdits(double deadlineMs)
        {
            // Deferred simulation light is one ordered transaction pipeline. The active edit keeps
            // its exact removal/seed/direct-sky/frontier cursors across frames and resumes until the
            // shared scheduler deadline. No node-count, edit-count or private time quota is used.
            // Mesh capture is blocked by PendingLightEdits until the transaction is fully committed.
            while(true)
            {
                if(!deferredLightActive)
                {
                    if(lightEditQueue.Count==0||NowMs()>=deadlineMs)return;
                    LightEdit e=lightEditQueue.Dequeue();
                    // The source only propagates through loaded chunks. If the edited center was
                    // unloaded before its deferred turn, there is no loaded light state left to fix.
                    if(!chunks.ContainsKey(ChunkCoord.FromWorld(e.X,e.Z)))continue;
                    deferredLightEdit=e;
                    deferredLightJob=VoxelLighting.BeginDeferredRelight(deferredLightJob,e.X,e.Y,e.Z,e.NeedsSkyRelight,e.NeedsSkyFrontierShell);
                    deferredLightActive=true;
                }

                if(!VoxelLighting.StepDeferredRelight(chunks,deferredLightJob,deadlineMs))return;

                foreach(var sk in deferredLightJob.DirtySections)DirtySingle(sk);
                deferredLightActive=false;
                if(NowMs()>=deadlineMs)return;
            }
        }


        public BiomeSample GetBiome(int wx, int wz) => Generator.Biomes.Sample(wx, SourceCoords.UnityBlockZToSource(wz));

        public int SurfaceY(int wx, int wz)
        {
            var cc = ChunkCoord.FromWorld(wx, wz);
            if (chunks.TryGetValue(cc, out var c))
            {
                int lx = VoxelConstants.FloorMod(wx, 16), lz = VoxelConstants.FloorMod(wz, 16);
                int guess = c.Heights[lx * 16 + lz];
                for (int y = Math.Min(VoxelConstants.MaxY, guess + 12); y >= VoxelConstants.MinY; y--)
                    if (BlockRegistry.IsSolid(GetBlock(wx, y, wz))) return y;
            }
            return Generator.Biomes.Sample(wx, SourceCoords.UnityBlockZToSource(wz)).Height;
        }

        public bool IsWater(int wx, int y, int wz)
        {
            var b = GetBlock(wx, y, wz); return b == BlockId.Water;
        }

        public bool IsWalkable(int wx, int wz, int currentY, out int y)
        {
            y = SurfaceY(wx, wz) + 1;
            if (Math.Abs(y - currentY) > 2) return false;
            return GetBlock(wx, y, wz) == BlockId.Air && GetBlock(wx, y + 1, wz) == BlockId.Air;
        }
    }
}
