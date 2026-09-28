using System;
using UnityEngine;

namespace BlockcraftPort
{
    // BC_TEMP_DEBUG_BEGIN [F3_PERFORMANCE_HUD] REMOVE BEFORE RELEASE
    // Entire component is investigation-only. Delete this class/component after the active render/streaming bugs are closed.
    /// <summary>
    /// F3 runtime counters analogous to main.js debug overlay. Keeps a fixed, allocation-free frame-time
    /// ring so short stalls remain visible instead of disappearing on the next OnGUI repaint.
    /// </summary>
    public sealed class MainPerformanceHud : MonoBehaviour
    {
        public VoxelWorld World;
        bool visible;
        float sampleStart;
        int frames;
        float fps,p95Ms,p99Ms,maxMs;
        int gc0,prevGc0,gc0Delta;
        int spikeCount;
        float lastSpikeMs;
        string lastSpikeHint="-";
        string cachedText="";
        float nextTextRefresh;
        GUIStyle style;
        VoxelRenderEnvironment renderEnvironment;
        readonly float[] frameRing=new float[240];
        readonly float[] sortScratch=new float[240];
        int frameWrite,frameCount;
        readonly FrameTiming[] frameTimings=new FrameTiming[1];
        double cpuFrameMs,gpuFrameMs;

        void Awake(){gc0=prevGc0=GC.CollectionCount(0);}

        void Update()
        {
            if(Input.GetKeyDown(KeyCode.F3))
            {
                visible=!visible;
                if(visible)ResetCapture();
            }
            if(visible)
            {
                FrameTimingManager.CaptureFrameTimings();
                uint timingCount=FrameTimingManager.GetLatestTimings(1,frameTimings);
                if(timingCount>0){cpuFrameMs=frameTimings[0].cpuFrameTime;gpuFrameMs=frameTimings[0].gpuFrameTime;}
            }
            float ms=Time.unscaledDeltaTime*1000f;
            frameRing[frameWrite]=ms;frameWrite=(frameWrite+1)%frameRing.Length;if(frameCount<frameRing.Length)frameCount++;
            if(ms>=25f)
            {
                spikeCount++;lastSpikeMs=ms;
                var w=World!=null?World:VoxelWorld.Instance;
                var save=MainWorldPersistence.Instance;
                if(save!=null&&Time.frameCount-save.LastSaveCaptureFrame<=1&&save.LastSaveCaptureMs>=3f)lastSpikeHint="save-capture "+save.LastSaveCaptureMs.ToString("0.0")+"ms";
                else if(Time.frameCount-MobAI.LastSourceStepFrame<=1&&MobAI.LastSourceStepMs>=2f)lastSpikeHint="ai "+MobAI.LastSourceStepMs.ToString("0.0")+"ms";
                else if(Time.frameCount-MainEntityBatchRenderer.LastBatchBuildFrame<=1&&MainEntityBatchRenderer.LastBatchBuildMs>=2f)lastSpikeHint="entity-batch "+MainEntityBatchRenderer.LastBatchBuildMs.ToString("0.0")+"ms";
                else if(w!=null&&w.LastWaterWorkMs>=.8f)lastSpikeHint="water "+w.LastWaterWorkMs.ToString("0.0")+"ms";
                else if(w!=null&&w.LastLavaWorkMs>=.8f)lastSpikeHint="lava "+w.LastLavaWorkMs.ToString("0.0")+"ms";
                else if(w!=null&&w.LastChunkLightWorkMs>=2f)lastSpikeHint="chunk-light "+w.LastChunkLightWorkMs.ToString("0.0")+"ms";
                else if(w!=null&&w.LastMeshUploadMs>=2f)lastSpikeHint="mesh-upload "+w.LastMeshUploadMs.ToString("0.0")+"ms";
                else if(w!=null&&w.LastStreamingWorkMs>=5f)lastSpikeHint="stream "+w.LastStreamingWorkMs.ToString("0.0")+"ms";
                else lastSpikeHint="render/other";
            }
            frames++;
            float now=Time.unscaledTime;
            if(sampleStart<=0f)sampleStart=now;
            float dt=now-sampleStart;
            if(dt>=.5f)
            {
                fps=frames/Mathf.Max(.001f,dt);frames=0;sampleStart=now;
                if(visible&&frameCount>0)
                {
                    // Sorting 240 samples is useful only while F3 is visible. Keep collecting the
                    // ring while hidden so the first visible sample still has recent frame history.
                    for(int i=0;i<frameCount;i++)sortScratch[i]=frameRing[i];
                    Array.Sort(sortScratch,0,frameCount);
                    p95Ms=sortScratch[Mathf.Clamp(Mathf.CeilToInt(frameCount*.95f)-1,0,frameCount-1)];
                    p99Ms=sortScratch[Mathf.Clamp(Mathf.CeilToInt(frameCount*.99f)-1,0,frameCount-1)];
                    maxMs=sortScratch[frameCount-1];
                }
                gc0=GC.CollectionCount(0);gc0Delta=gc0-prevGc0;prevGc0=gc0;
            }
            // OnGUI can run more than once per rendered frame. Keep the profiler overlay from becoming
            // its own GC/stutter source by formatting the diagnostic string only four times per second.
            if(visible&&now>=nextTextRefresh){nextTextRefresh=now+.25f;RebuildText();}
        }

        void ResetCapture()
        {
            spikeCount=0;lastSpikeMs=0f;lastSpikeHint="-";frameWrite=frameCount=0;
            p95Ms=p99Ms=maxMs=0f;nextTextRefresh=0f;
            var w=World!=null?World:VoxelWorld.Instance;if(w!=null)w.ResetPerformancePeaks();
            var save=MainWorldPersistence.Instance;if(save!=null)save.ResetPerformancePeaks();
            MobAI.ResetPerformancePeaks();
            MainEntityBatchRenderer.ResetPerformancePeaks();
        }

        void RebuildText()
        {
            var w=World!=null?World:VoxelWorld.Instance;
            string cap=Application.targetFrameRate<0?"uncapped":Application.targetFrameRate.ToString();
            string text="FPS "+fps.ToString("0.0")+"   p95 "+p95Ms.ToString("0.0")+" ms   p99 "+p99Ms.ToString("0.0")+" ms   max "+maxMs.ToString("0.0")+" ms   CPU/GPU "+cpuFrameMs.ToString("0.0")+"/"+gpuFrameMs.ToString("0.0")+" ms  cap "+cap+"  GC0 +"+gc0Delta+
                "   spikes>=25ms "+spikeCount+"  last "+lastSpikeMs.ToString("0.0")+"ms ["+lastSpikeHint+"]";
            if(w!=null)
            {
                w.RefreshVisualGapTelemetry();
                bool cave=false;
                if(w.Player!=null){Vector3 p=w.Player.position;byte li=w.GetPackedLight(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+1.4f),Mathf.FloorToInt(p.z));cave=(li>>4)<=1;}
                var save=MainWorldPersistence.Instance;
                if(renderEnvironment==null)renderEnvironment=FindObjectOfType<VoxelRenderEnvironment>();
                string fogState=renderEnvironment==null?"-":(renderEnvironment.EffectiveFogNear.ToString("0")+".."+renderEnvironment.EffectiveFogFar.ToString("0")+" SRC");
                int readyMeters=w.PublishedRenderRadius*VoxelConstants.ChunkSize;
                string posInfo="";
                if(w.Player!=null)
                {
                    Vector3 pp=w.Player.position;int px=Mathf.FloorToInt(pp.x),py=Mathf.FloorToInt(pp.y),pz=Mathf.FloorToInt(pp.z);
                    BlockId feet=w.GetBlock(px,py,pz),below=w.GetBlock(px,py-1,pz);
                    posInfo="  xyz "+pp.x.ToString("0.0")+"/"+pp.y.ToString("0.0")+"/"+pp.z.ToString("0.0")+" sea "+VoxelConstants.SeaLevel+" feet "+feet+" below "+below;
                }
                text+="\nrd target/gpu-ready "+w.RenderDistance+"/"+w.PublishedRenderRadius+"  fog "+fogState+"  ready~"+readyMeters+"m  workers g/m "+w.MaxGenerationJobs+"/"+w.MaxMeshJobs+" SOURCE  "+(cave?"CAVE":"open")+posInfo+"  -/[ lower  =/] higher  F fly"+
                    "\nTEMP gaps frontier data/mesh "+w.PublishedFrontierMissingData+"/"+w.PublishedFrontierMissingMesh+"  viewGap data/mesh "+w.ViewMissingDataChunks+"/"+w.ViewMissingMeshChunks+"   chunks "+w.LoadedChunks+" gpu "+w.RenderChunks+" visible "+w.LastVisibleChunks+" draws "+w.LastChunkDrawCalls+" tris "+(w.LastChunkTriangles/1000)+"k  gen "+w.PendingGeneration+" meshSec "+w.PendingMeshes+" upload "+w.PendingUploads+" light "+w.PendingLightEdits+" stitch "+w.PendingChunkLighting+" waterQ "+w.PendingWaterCells+" lavaQ "+w.PendingLavaCells+" unload "+w.PendingUnloads+" trim "+w.PendingCpuTrims+
                    "\nstream "+w.LastStreamingWorkMs.ToString("0.00")+" / "+w.LastStreamingBudgetMs.ToString("0.00")+" ms   water "+w.LastWaterWorkMs.ToString("0.00")+" max "+w.MaxWaterWorkMs.ToString("0.00")+" ticks "+w.LastWaterTicks+"   lava "+w.LastLavaWorkMs.ToString("0.00")+" max "+w.MaxLavaWorkMs.ToString("0.00")+" ticks "+w.LastLavaTicks+"  phases gen "+w.LastGenerationIntegrateMs.ToString("0.00")+" stitch "+w.LastChunkLightWorkMs.ToString("0.00")+" light "+w.LastLightWorkMs.ToString("0.00")+" mesh-int "+w.LastMeshIntegrateMs.ToString("0.00")+" sched "+w.LastMeshScheduleMs.ToString("0.00")+
                    "\nupload last "+w.LastMeshUploadMs.ToString("0.00")+" ms  ewma "+w.EstimatedMeshUploadMs.ToString("0.00")+"  max "+w.MaxMeshUploadMs.ToString("0.00")+"  "+(w.LastMeshUploadBytes/1024)+" KB  batch "+(w.LastMeshUploadBatchBytes/1024)+" KB  idx "+(w.LastMeshUploadUsed16BitIndices?"16":"32")+"  saved "+(w.LastMeshUploadIndexBytesSaved/1024)+" KB   idx16/32 "+w.MeshIndex16Uploads+"/"+w.MeshIndex32Uploads+"  total "+w.MeshUploads+
                    "\nsave capture "+(save!=null?save.LastSaveCaptureMs.ToString("0.00"):"-")+" ms  edit-journal "+w.PersistentJournalRecordCount+"  max "+(save!=null?save.MaxSaveCaptureMs.ToString("0.00"):"-")+"  bg-write "+(save!=null?save.LastSaveWriteMs.ToString("0.0"):"-")+" ms busy "+(save!=null&&save.SaveWriterBusy?"yes":"no")+"   ai "+MobAI.LastSourceStepMs.ToString("0.00")+" max "+MobAI.MaxSourceStepMs.ToString("0.00")+"ms ticks "+MobAI.LastSourceTicks+"   entity "+MainEntityBatchRenderer.LastBatchBuildMs.ToString("0.00")+" max "+MainEntityBatchRenderer.MaxBatchBuildMs.ToString("0.00")+"ms rebuild/skip "+MainEntityBatchRenderer.BatchRebuilds+"/"+MainEntityBatchRenderer.StableBatchSkips;
                var mobs=MobSpawner.Instance;
                if(mobs!=null)
                {
                    mobs.GetDebugCounts(out int total,out int hostile,out int passive,out int fish,out int sharks,out int stored,out int hcap,out int pcap,out float far);
                    text+="\nmobs "+total+"  hostile "+hostile+"/"+hcap+"  passive "+passive+"/"+pcap+"  fish "+fish+"  sharks "+sharks+"  stored "+stored+"  spawnFar "+far.ToString("0");
                    mobs.GetSpawnDebug(out int landTry,out int landOk,out int unavailable,out int noCandidate,
                        out int passiveTry,out int passiveOk,out int hostileTry,out int hostileOk,
                        out int seededCalls,out int passiveRolls,out int passiveGroups);
                    text+="\nspawn land "+landOk+"/"+landTry+"  P "+passiveOk+"/"+passiveTry+"  H "+hostileOk+"/"+hostileTry+
                        "  reject unavailable/noY "+unavailable+"/"+noCandidate+"  seeded chunks/rolls/groups "+seededCalls+"/"+passiveRolls+"/"+passiveGroups;
                }
            }
            cachedText=text;
        }

        void OnGUI()
        {
            if(!visible)return;
            if(style==null)style=new GUIStyle(GUI.skin.box){alignment=TextAnchor.UpperLeft,fontSize=12,padding=new RectOffset(8,8,6,6)};
            GUI.Box(new Rect(8,8,1320,190),cachedText,style);
        }
    }
    // BC_TEMP_DEBUG_END [F3_PERFORMANCE_HUD]

}
