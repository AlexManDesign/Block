using System;
using System.IO;
using System.Threading;
using UnityEngine;
using Unity.Profiling;

namespace BlockcraftPort
{
    [Serializable]
    public sealed class MainMobSaveData
    {
        public int kind;
        public float x,y,z,hp;
        public bool baby,sheared,persist;
        public int color;
    }

    [Serializable]
    public sealed class MainShipSaveData
    {
        // Source-space vessel pose and local blocks: [x,y,z,id,meta] repeated.
        public float x,y,z,yaw,fwdOff;
        public bool docked,riding;
        public int[] blocks=Array.Empty<int>();
        // Source w7()/Lc(): persistent sealed-air/flood state, encoded as local [x,y,z,...].
        public int[] holdCells=Array.Empty<int>();
        public int[] floodCells=Array.Empty<int>();
    }

    [Serializable]
    public sealed class MainSurvivalSaveData
    {
        // main.js p.surv persisted subset: exhaustion/timers are intentionally runtime-only.
        public float hp=20f;
        public int hunger=20;
        public float air=10f;
        public bool dead;
    }

    [Serializable]
    public sealed class MainWorldSaveData
    {
        public int version=1;
        public int seed=56;
        public string genVersion="deepslate";
        public float dayT=.25f;
        // Source-space pose: X/Y are shared, Z is reflected at the Unity boundary.
        public float[] pos=new float[3];
        // main.js p.spawnPos: persistent respawn point; absent in old Unity saves -> current pose.
        public float[] spawn;
        public float yaw=Mathf.PI*.5f;
        public float pitch=-.15f;
        public bool fly;
        public int mode=(int)MainGameMode.Creative;
        // main.js sparse edit ABI: [worldX, worldY, worldZ, blockId | (meta << 16), ...].
        public int[] edits=Array.Empty<int>();
        public MainMobSaveData[] mobs=Array.Empty<MainMobSaveData>();
        // Source chunk coordinates [cx,cz,...] that already received eu() initial fauna.
        public int[] seededChunks=Array.Empty<int>();
        // main.js p.survInv: exactly 36 nullable stacks plus the selected hotbar slot.
        public MainInventoryStackData[] inventory=Array.Empty<MainInventoryStackData>();
        public int hotbarSel;
        public MainSurvivalSaveData surv=new MainSurvivalSaveData();
        // R52 static-world functional blocks. These are deliberately separate from moving ships.
        public MainFurnaceSaveData[] furnaces=Array.Empty<MainFurnaceSaveData>();
        public MainChestSaveData[] chests=Array.Empty<MainChestSaveData>();
        // main nB()/jP()/ih()/sh(): static-world lifecycle timer persistence.
        public MainTimedCellSaveData[] crops=Array.Empty<MainTimedCellSaveData>();
        public MainTimedCellSaveData[] farmlandTimers=Array.Empty<MainTimedCellSaveData>();
        public MainTimedCellSaveData[] saplingTimers=Array.Empty<MainTimedCellSaveData>();
        public MainTimedCellSaveData[] fireTimers=Array.Empty<MainTimedCellSaveData>();
        public MainShipSaveData[] ships=Array.Empty<MainShipSaveData>();
    }

    public static class MainWorldSave
    {
        const string FileName="blockcraft_main_world.json";
        public static string SavePath=>Path.Combine(Application.persistentDataPath,FileName);

        public static bool Exists=>File.Exists(SavePath);

        public static bool TryLoad(out MainWorldSaveData data)
        {
            data=null;
            try
            {
                string path=SavePath;
                if(!File.Exists(path))return false;
                string json=File.ReadAllText(path);
                var d=JsonUtility.FromJson<MainWorldSaveData>(json);
                if(d==null||d.version<1||d.genVersion!="deepslate"||d.pos==null||d.pos.Length!=3)return false;
                if(d.spawn!=null&&d.spawn.Length!=3)d.spawn=null;
                if(d.edits==null)d.edits=Array.Empty<int>();
                if(d.mobs==null)d.mobs=Array.Empty<MainMobSaveData>();
                if(d.seededChunks==null)d.seededChunks=Array.Empty<int>();
                if(d.inventory==null)d.inventory=Array.Empty<MainInventoryStackData>();
                if(d.surv==null)d.surv=new MainSurvivalSaveData();
                if(d.furnaces==null)d.furnaces=Array.Empty<MainFurnaceSaveData>();
                if(d.chests==null)d.chests=Array.Empty<MainChestSaveData>();
                if(d.crops==null)d.crops=Array.Empty<MainTimedCellSaveData>();
                if(d.farmlandTimers==null)d.farmlandTimers=Array.Empty<MainTimedCellSaveData>();
                if(d.saplingTimers==null)d.saplingTimers=Array.Empty<MainTimedCellSaveData>();
                if(d.fireTimers==null)d.fireTimers=Array.Empty<MainTimedCellSaveData>();
                if(d.ships==null)d.ships=Array.Empty<MainShipSaveData>();
                if((d.edits.Length&3)!=0||(d.seededChunks.Length&1)!=0)return false;
                if(d.mode!=(int)MainGameMode.Survival&&d.mode!=(int)MainGameMode.Creative)return false;
                data=d;return true;
            }
            catch(Exception ex)
            {
                Debug.LogWarning("World save load failed: "+ex.Message);
                data=null;return false;
            }
        }

        public static bool Write(MainWorldSaveData data)
        {
            bool ok=WriteToPath(data,SavePath,out string error);
            if(!ok)Debug.LogWarning("World save write failed: "+error);
            return ok;
        }

        // JsonUtility's plain-data ToJson path is safe to call from a background thread. Capture the
        // persistentDataPath string on the main thread and pass it here so no Unity API lookup is made
        // by the writer. This removes JSON serialization + disk I/O from the 4-second gameplay cadence.
        public static bool WriteToPath(MainWorldSaveData data,string path,out string error)
        {
            error=null;if(data==null||string.IsNullOrEmpty(path)){error="invalid save request";return false;}
            try
            {
                string tmp=path+".tmp";
                string dir=Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(dir))Directory.CreateDirectory(dir);
                File.WriteAllText(tmp,JsonUtility.ToJson(data,false));
                if(File.Exists(path))File.Delete(path);
                File.Move(tmp,path);
                return true;
            }
            catch(Exception ex){error=ex.Message;return false;}
        }

        public static void Delete()
        {
            try{if(File.Exists(SavePath))File.Delete(SavePath);}catch(Exception ex){Debug.LogWarning("World save delete failed: "+ex.Message);}
        }
    }

    /// <summary>
    /// Source-style lightweight persistence. Full generated chunks are never serialized: only the
    /// sparse edit overlay and the small world/player header are written. Autosave follows main.js:
    /// dirty state at roughly four-second cadence, plus unconditional focus-loss/pause/quit saves.
    /// </summary>
    public sealed class MainWorldPersistence : MonoBehaviour
    {
        public VoxelWorld World;
        public MainPlayerController Player;
        public DayNight DayNight;
        public MobSpawner Spawner;
        public int Seed=56;
        public MainGameMode Mode=MainGameMode.Creative;
        float saveClock;
        bool initialSnapshotWritten;

        static readonly ProfilerMarker SaveCaptureMarker=new ProfilerMarker("Blockcraft.Save.Capture");
        sealed class SaveRequest
        {
            public MainWorldSaveData Data;
            public VoxelWorld.PersistentEditJournalSnapshot EditJournal;
        }
        readonly object writeLock=new object();
        readonly AutoResetEvent writerSignal=new AutoResetEvent(false);
        SaveRequest pendingWrite;
        string savePath;
        Thread writerThread;
        volatile bool writerStopping;
        bool writerRunning;
        volatile bool writerCompleted;
        volatile bool writerLastOk=true;
        string writerLastError;
        volatile float writerLastMs;
        VoxelWorld.PersistentEditJournalSnapshot writerCompactedJournal;
        long writerCompactedVersion;
        public static MainWorldPersistence Instance { get; private set; }
        // BC_TEMP_DEBUG_BEGIN [F3_SAVE_PERF] REMOVE BEFORE RELEASE
        public float LastSaveCaptureMs { get; private set; }
        public float MaxSaveCaptureMs { get; private set; }
        public int LastSaveCaptureFrame { get; private set; }=-1000000;
        public float LastSaveWriteMs => writerLastMs;
        public bool SaveWriterBusy { get { lock(writeLock)return writerRunning||pendingWrite!=null; } }
        public void ResetPerformancePeaks(){MaxSaveCaptureMs=0f;}
        // BC_TEMP_DEBUG_END [F3_SAVE_PERF]

        void Awake(){Instance=this;EnsureWriterThread();}
        void OnDestroy()
        {
            writerStopping=true;writerSignal.Set();
            if(writerThread!=null&&writerThread.IsAlive)writerThread.Join(50);
            if(Instance==this)Instance=null;
        }

        void EnsureWriterThread()
        {
            if(writerThread!=null)return;
            writerStopping=false;
            writerThread=new Thread(WriterLoop){IsBackground=true,Name="Blockcraft Save Writer",Priority=System.Threading.ThreadPriority.BelowNormal};
            writerThread.Start();
        }

        public void Configure(VoxelWorld world,MainPlayerController player,DayNight dayNight,MobSpawner spawner,int seed,MainGameMode mode)
        {
            World=world;Player=player;DayNight=dayNight;Spawner=spawner;Seed=seed;Mode=mode;saveClock=0f;initialSnapshotWritten=false;
            savePath=MainWorldSave.SavePath;
        }

        void Update()
        {
            PollWriterCompletion();
            if(World==null||Player==null||!World.InitialWorldReady)return;
            // The browser creates the world record before play. Unity has no separate world-list DB,
            // so commit one compact baseline snapshot as soon as the R40 startup gate is complete.
            if(!initialSnapshotWritten){initialSnapshotWritten=true;saveClock=0f;SaveNow(false);return;}
            saveClock+=Time.unscaledDeltaTime;
            if(saveClock>=4f&&World.PersistentSaveDirty){saveClock=0f;SaveNow(false);}
        }

        SaveRequest CaptureSnapshot()
        {
            Vector3 p=Player.transform.position;
            // Persistent voxel edits are not flattened here. CapturePersistentEditJournal() is O(1)
            // and immutable up to its record count; background save materializes/deduplicates it.
            var data=new MainWorldSaveData
            {
                version=1,seed=Seed,genVersion="deepslate",dayT=DayNight.DayTime01,
                pos=new[]{p.x,p.y,SourceCoords.UnityWorldZToSource(p.z)},
                spawn=Player.CapturePersistentSpawnSource(),
                yaw=Player.MainYawRadians,pitch=Player.MainPitchRadians,
                fly=Player.IsCreative&&Player.Flying,mode=(int)Player.Mode,
                edits=Array.Empty<int>(),
                mobs=Spawner!=null?Spawner.CapturePersistentMobsSource():Array.Empty<MainMobSaveData>(),
                seededChunks=Spawner!=null?Spawner.CaptureSeededChunksSource():Array.Empty<int>(),
                inventory=Player.CapturePersistentInventory(),hotbarSel=Player.SurvivalHotbarSelected,
                surv=Player.CapturePersistentSurvival(),
                furnaces=MainWorldFunctionalBlocks.Instance!=null?MainWorldFunctionalBlocks.Instance.CaptureFurnacesSource():Array.Empty<MainFurnaceSaveData>(),
                chests=MainWorldFunctionalBlocks.Instance!=null?MainWorldFunctionalBlocks.Instance.CaptureChestsSource():Array.Empty<MainChestSaveData>(),
                crops=MainBlockLifecycle.Instance!=null?MainBlockLifecycle.Instance.CaptureCropsSource():Array.Empty<MainTimedCellSaveData>(),
                farmlandTimers=MainBlockLifecycle.Instance!=null?MainBlockLifecycle.Instance.CaptureFarmlandSource():Array.Empty<MainTimedCellSaveData>(),
                saplingTimers=MainBlockLifecycle.Instance!=null?MainBlockLifecycle.Instance.CaptureSaplingsSource():Array.Empty<MainTimedCellSaveData>(),
                fireTimers=MainBlockLifecycle.Instance!=null?MainBlockLifecycle.Instance.CaptureFiresSource():Array.Empty<MainTimedCellSaveData>(),
                ships=MainShipRuntime.CapturePersistentShipsSource()
            };
            return new SaveRequest{Data=data,EditJournal=World.CapturePersistentEditJournal()};
        }

        public bool SaveNow(bool forceSynchronous=false)
        {
            if(World==null||Player==null||!World.InitialWorldReady)return false;
            double t0=Time.realtimeSinceStartupAsDouble;
            SaveRequest request;using(SaveCaptureMarker.Auto())request=CaptureSnapshot();
            LastSaveCaptureMs=(float)((Time.realtimeSinceStartupAsDouble-t0)*1000.0);
            LastSaveCaptureFrame=Time.frameCount;
            if(LastSaveCaptureMs>MaxSaveCaptureMs)MaxSaveCaptureMs=LastSaveCaptureMs;

            if(forceSynchronous)
            {
                FlushPendingWriter();
                request.Data.edits=VoxelWorld.MaterializePersistentEditsSource(request.EditJournal);
                bool ok=MainWorldSave.WriteToPath(request.Data,savePath??MainWorldSave.SavePath,out string error);
                if(!ok)Debug.LogWarning("World save write failed: "+error);
                if(ok)World.MarkPersistentSaveClean();
                return ok;
            }

            QueueAsyncWrite(request);
            // The snapshot contains every edit visible at this point. New edits after this call set the
            // dirty bit again; coalescing only replaces an older pending snapshot with a newer one.
            World.MarkPersistentSaveClean();
            return true;
        }

        void QueueAsyncWrite(SaveRequest request)
        {
            EnsureWriterThread();
            lock(writeLock)pendingWrite=request; // coalesce: newest complete snapshot wins
            writerSignal.Set();
        }

        void WriterLoop()
        {
            while(!writerStopping)
            {
                writerSignal.WaitOne();
                if(writerStopping)break;
                while(!writerStopping)
                {
                    SaveRequest request;
                    lock(writeLock)
                    {
                        request=pendingWrite;pendingWrite=null;
                        if(request==null){writerRunning=false;break;}
                        writerRunning=true;
                    }
                    var sw=System.Diagnostics.Stopwatch.StartNew();
                    int[] edits=VoxelWorld.MaterializePersistentEditsSource(request.EditJournal);
                    request.Data.edits=edits;
                    var compacted=VoxelWorld.BuildCompactedPersistentJournal(edits,request.EditJournal.SnapshotVersion);
                    bool ok=MainWorldSave.WriteToPath(request.Data,savePath,out string error);
                    sw.Stop();
                    writerLastMs=(float)sw.Elapsed.TotalMilliseconds;writerLastOk=ok;writerLastError=error;
                    writerCompactedJournal=ok?compacted:null;writerCompactedVersion=request.EditJournal.SnapshotVersion;
                    writerCompleted=true;
                    // If gameplay published a newer request while I/O was running, the inner loop
                    // immediately consumes that coalesced snapshot; otherwise this thread sleeps.
                }
            }
            lock(writeLock)writerRunning=false;
        }

        void PollWriterCompletion()
        {
            if(!writerCompleted)return;writerCompleted=false;
            if(!writerLastOk)
            {
                if(World!=null)World.MarkPersistentSaveDirty();
                if(!string.IsNullOrEmpty(writerLastError))Debug.LogWarning("World save write failed: "+writerLastError);
                return;
            }
            // Compaction was built on the writer thread. Installation is O(1) and only succeeds if
            // no persistent edit was appended after this exact snapshot.
            if(World!=null&&writerCompactedJournal!=null)World.TryInstallCompactedPersistentJournal(writerCompactedJournal,writerCompactedVersion);
            writerCompactedJournal=null;
        }

        void FlushPendingWriter()
        {
            // Pause/quit saves must be durable before the application loses its process. Wait only for
            // an already-running background write; the final snapshot itself is then written synchronously.
            while(true)
            {
                bool busy;lock(writeLock)busy=writerRunning||pendingWrite!=null;
                if(!busy)break;Thread.Sleep(1);
            }
        }

        void OnApplicationPause(bool paused){if(paused)SaveNow(true);}
        void OnApplicationFocus(bool focused){if(!focused)SaveNow(true);}
        void OnApplicationQuit(){SaveNow(true);}
    }

}
