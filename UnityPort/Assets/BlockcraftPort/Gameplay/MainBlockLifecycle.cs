using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace BlockcraftPort
{
    [Serializable]
    public sealed class MainTimedCellSaveData
    {
        // Source-space z. Value is elapsed crop/sapling time or fire age depending on array.
        public int x,y,z;
        public float value;
    }

    /// <summary>
    /// main.js static-world lifecycle port for ZP()/qP(), y4()/tB(), zJ()/oh()/nh()/Ah() and XJ()/Eh().
    /// It is deliberately independent of rendering: edits enter VoxelWorld's existing deferred
    /// persistent-light/mesh path, so timers cannot create a second renderer or a synchronous rebuild loop.
    /// </summary>
    // R63_SAVED_WORLD_LIFECYCLE_THREADSAFE_2026-09-20
    public sealed class MainBlockLifecycle : MonoBehaviour
    {
        sealed class CropEntry { public float T; }
        sealed class FarmEntry { public float DryT; }
        sealed class SaplingEntry { public float T,Delay; }
        sealed class FireEntry { public float T,Next; public int Age; }
        readonly struct PendingBlockChange { public readonly int X,Y,Z; public PendingBlockChange(int x,int y,int z){X=x;Y=y;Z=z;} }

        public VoxelWorld World;
        static MainBlockLifecycle instance;
        static int mainThreadId;
        readonly ConcurrentQueue<PendingBlockChange> pendingBlockChanges=new ConcurrentQueue<PendingBlockChange>();
        public static MainBlockLifecycle Instance=>instance;

        readonly Dictionary<Vector3Int,CropEntry> crops=new Dictionary<Vector3Int,CropEntry>(128);
        readonly Dictionary<Vector3Int,FarmEntry> farmland=new Dictionary<Vector3Int,FarmEntry>(128);
        readonly Dictionary<Vector3Int,float> grassSpread=new Dictionary<Vector3Int,float>(256);
        readonly Dictionary<Vector3Int,SaplingEntry> saplings=new Dictionary<Vector3Int,SaplingEntry>(128);
        readonly Dictionary<Vector3Int,FireEntry> fires=new Dictionary<Vector3Int,FireEntry>(128);
        readonly List<Vector3Int> keys=new List<Vector3Int>(512);

        float cropClock,groundClock,saplingClock;
        const float CropStageSeconds=35f;
        const int MaxTrackedFire=300;
        static readonly Vector3Int[] Six={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};

        public static MainBlockLifecycle EnsureInstance(VoxelWorld world,MainWorldSaveData restore=null)
        {
            if(instance==null)
            {
                var go=new GameObject("MainBlockLifecycle");
                instance=go.AddComponent<MainBlockLifecycle>();
            }
            instance.World=world;
            if(restore!=null)instance.Restore(restore);
            return instance;
        }

        public static void NotifyBlockChanged(int x,int y,int z)
        {
            // ApplyPersistentEdits() runs on generation workers. Never touch engine RNG,
            // lifecycle dictionaries or world queries from those threads: only enqueue managed data.
            MainBlockLifecycle target=instance;
            if(ReferenceEquals(target,null)||ReferenceEquals(target.World,null))return;
            if(Thread.CurrentThread.ManagedThreadId==Volatile.Read(ref mainThreadId))target.OnBlockChanged(new Vector3Int(x,y,z));
            else target.pendingBlockChanges.Enqueue(new PendingBlockChange(x,y,z));
        }

        void Awake(){if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;Volatile.Write(ref mainThreadId,Thread.CurrentThread.ManagedThreadId);}
        void OnDestroy(){if(instance==this)instance=null;}

        void OnBlockChanged(Vector3Int p)
        {
            BlockId id=World.GetBlock(p.x,p.y,p.z);
            if(IsCrop(id))
            {
                if(!crops.ContainsKey(p))crops.Add(p,new CropEntry());
            }
            else crops.Remove(p);

            if(id==BlockId.Farmland||id==BlockId.FarmlandMoist)
            {
                if(!farmland.ContainsKey(p))farmland.Add(p,new FarmEntry());
            }
            else farmland.Remove(p);

            if(IsSapling(id))
            {
                if(!saplings.ContainsKey(p))saplings.Add(p,new SaplingEntry{T=0f,Delay=90f+UnityEngine.Random.value*90f});
            }
            else saplings.Remove(p);

            if(id==BlockId.Fire)
            {
                if(fires.Count<MaxTrackedFire&&!fires.ContainsKey(p))fires.Add(p,new FireEntry{T=0f,Next=.7f+UnityEngine.Random.value*.6f,Age=0});
            }
            else fires.Remove(p);

            // ZP(): every edit schedules self + six neighbours when the cell is DIRT.
            ScheduleGrassCandidate(p);
            for(int i=0;i<Six.Length;i++)ScheduleGrassCandidate(p+Six[i]);
        }

        void ScheduleGrassCandidate(Vector3Int p)
        {
            if(p.y<VoxelConstants.MinY||p.y>VoxelConstants.MaxY)return;
            if(World.GetBlock(p.x,p.y,p.z)!=BlockId.Dirt||grassSpread.ContainsKey(p))return;
            grassSpread.Add(p,60f+UnityEngine.Random.value*120f);
        }

        public bool PlantWheat(int x,int y,int z)=>PlantCrop(x,y,z,BlockId.Wheat0);

        /// <summary>main y4(): plant stage 0 of a crop on farmland with air above and start its timer.</summary>
        public bool PlantCrop(int x,int y,int z,BlockId stage0)
        {
            if(World==null||World.GetBlock(x,y,z)!=BlockId.Air)return false;
            BlockId soil=World.GetBlock(x,y-1,z);if(soil!=BlockId.Farmland&&soil!=BlockId.FarmlandMoist)return false;
            if(!World.SetBlock(x,y,z,stage0,0,true))return false;
            crops[new Vector3Int(x,y,z)]=new CropEntry();
            return true;
        }

        /// <summary>
        /// main ML() bone meal on the aimed block: crops advance 1-2 stages (rB), saplings grow with
        /// 45% chance (ah/nh), grass sprouts tall grass and flowers around (24 tries in 7x7).
        /// Returns true when the bone meal is used up.
        /// </summary>
        public bool BoneMeal(Vector3Int p)
        {
            if(World==null)return false;
            BlockId id=World.GetBlock(p.x,p.y,p.z);
            if(CropStage(id)>=0)
            {
                BlockId[] stages=CropStages(id);int idx=CropStage(id);
                if(idx>=stages.Length-1)return false;
                int a=Mathf.Min(stages.Length-1,idx+1+UnityEngine.Random.Range(0,2));
                World.SetBlock(p.x,p.y,p.z,stages[a],0,true);
                if((a<stages.Length-1||CropFruit(id)!=BlockId.Air)&&!crops.ContainsKey(p))crops[p]=new CropEntry();
                return true;
            }
            if(IsSapling(id)){if(UnityEngine.Random.value<.45f)TryGrowSapling(p);return true;}
            if(id==BlockId.Grass)
            {
                bool any=false;
                for(int r=0;r<24;r++)
                {
                    int t=p.x+Mathf.FloorToInt(UnityEngine.Random.value*7)-3,n=p.z+Mathf.FloorToInt(UnityEngine.Random.value*7)-3;
                    for(int a=p.y+1;a>=p.y-1;a--)
                    {
                        if(World.GetBlock(t,a,n)!=BlockId.Grass||World.GetBlock(t,a+1,n)!=BlockId.Air)continue;
                        float i=UnityEngine.Random.value;
                        World.SetBlock(t,a+1,n,i<.875f?BlockId.TallGrass:i<.9375f?BlockId.Dandelion:BlockId.Poppy,0,true);any=true;break;
                    }
                }
                return any;
            }
            return false;
        }

        /// <summary>main ID(): a ripe stem places its fruit on a free neighbour cell with soil below.</summary>
        void GrowFruit(Vector3Int p,BlockId fruit)
        {
            for(int n=0;n<4;n++){Vector3Int q=p+FruitDirs[n];if(World.GetBlock(q.x,q.y,q.z)==fruit)return;}
            int start=UnityEngine.Random.Range(0,4);
            for(int n=0;n<4;n++)
            {
                Vector3Int q=p+FruitDirs[(start+n)&3];
                BlockId below=World.GetBlock(q.x,q.y-1,q.z);
                if(World.GetBlock(q.x,q.y,q.z)==BlockId.Air&&(below==BlockId.Grass||below==BlockId.Dirt||below==BlockId.Podzol||below==BlockId.CoarseDirt||below==BlockId.Farmland||below==BlockId.FarmlandMoist))
                {World.SetBlockDeferredPersistent(q.x,q.y,q.z,fruit,0);return;}
            }
        }

        void Update()
        {
            if(World==null)return;
            DrainPendingBlockChanges();
            float dt=Time.deltaTime;
            AdvanceFire(dt);
            cropClock+=dt;if(cropClock>=1f){float e=cropClock;cropClock=0f;AdvanceCrops(e);}
            saplingClock+=dt;if(saplingClock>=1f){float e=saplingClock;saplingClock=0f;AdvanceSaplings(e);}
            groundClock+=dt;if(groundClock>=2f){float e=groundClock;groundClock=0f;AdvanceGround(e);}
        }


        void DrainPendingBlockChanges()
        {
            // Saved-world edits are applied before their generated column is published into World.chunks.
            // Process only the queue entries that existed at frame start; unloaded columns go back to the
            // tail instead of being queried early or spinning repeatedly in the same frame.
            int budget=World!=null&&!World.InitialWorldReady?4096:1024;
            int count=Math.Min(budget,pendingBlockChanges.Count);
            for(int i=0;i<count;i++)
            {
                if(!pendingBlockChanges.TryDequeue(out PendingBlockChange p))break;
                if(!World.IsChunkLoadedAt(p.X,p.Z)){pendingBlockChanges.Enqueue(p);continue;}
                OnBlockChanged(new Vector3Int(p.X,p.Y,p.Z));
            }
        }

        void AdvanceCrops(float elapsed)
        {
            if(crops.Count==0)return;keys.Clear();foreach(var kv in crops)keys.Add(kv.Key);
            for(int k=0;k<keys.Count;k++)
            {
                Vector3Int p=keys[k];if(!crops.TryGetValue(p,out CropEntry e)||!World.IsChunkLoadedAt(p.x,p.z))continue;
                BlockId id=World.GetBlock(p.x,p.y,p.z);int stage=CropStage(id);
                if(stage<0){crops.Remove(p);continue;}
                BlockId soil=World.GetBlock(p.x,p.y-1,p.z);
                if(soil!=BlockId.Farmland&&soil!=BlockId.FarmlandMoist)
                {
                    World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0);crops.Remove(p);continue;
                }
                // main tB(): ripe wheat/carrots/potatoes stop; ripe stems keep a timer to grow fruit (ID()).
                BlockId[] stages=CropStages(id);BlockId fruit=CropFruit(id);
                if(stage>=stages.Length-1&&fruit==BlockId.Air){crops.Remove(p);continue;}
                e.T+=soil==BlockId.FarmlandMoist?elapsed:elapsed*.5f;
                if(stage>=stages.Length-1)
                {
                    if(e.T>=FruitSeconds){e.T=0f;GrowFruit(p,fruit);}
                }
                else if(e.T>=CropStageSeconds)
                {
                    e.T=0f;World.SetBlockDeferredPersistent(p.x,p.y,p.z,stages[stage+1],0);
                }
            }
        }

        void AdvanceGround(float elapsed)
        {
            if(grassSpread.Count>0)
            {
                keys.Clear();foreach(var kv in grassSpread)keys.Add(kv.Key);
                for(int k=0;k<keys.Count;k++)
                {
                    Vector3Int p=keys[k];float left=grassSpread[p]-elapsed;
                    if(left>0f){grassSpread[p]=left;continue;}
                    grassSpread.Remove(p);
                    if(!World.IsChunkLoadedAt(p.x,p.z)||World.GetBlock(p.x,p.y,p.z)!=BlockId.Dirt||!ReplaceablePlantSpace(World.GetBlock(p.x,p.y+1,p.z))||!GrassNearby(p))continue;
                    World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Grass,0);
                }
            }

            if(farmland.Count==0)return;keys.Clear();foreach(var kv in farmland)keys.Add(kv.Key);
            for(int k=0;k<keys.Count;k++)
            {
                Vector3Int p=keys[k];if(!farmland.TryGetValue(p,out FarmEntry e)||!World.IsChunkLoadedAt(p.x,p.z))continue;
                BlockId id=World.GetBlock(p.x,p.y,p.z);
                if(id!=BlockId.Farmland&&id!=BlockId.FarmlandMoist){farmland.Remove(p);continue;}
                bool wet=HasWaterForFarmland(p);
                if(wet&&id==BlockId.Farmland)World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.FarmlandMoist,0);
                else if(!wet&&id==BlockId.FarmlandMoist)World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Farmland,0);
                if(!wet&&!IsCrop(World.GetBlock(p.x,p.y+1,p.z)))
                {
                    e.DryT+=elapsed;if(e.DryT>=90f){World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Dirt,0);farmland.Remove(p);}
                }
                else e.DryT=0f;
            }
        }

        bool GrassNearby(Vector3Int p)
        {
            for(int dy=-1;dy<=3;dy++)for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
            {
                if(dx==0&&dy==0&&dz==0)continue;
                if(World.GetBlock(p.x+dx,p.y+dy,p.z+dz)==BlockId.Grass)return true;
            }
            return false;
        }

        bool HasWaterForFarmland(Vector3Int p)
        {
            for(int dy=0;dy<=1;dy++)for(int dx=-4;dx<=4;dx++)for(int dz=-4;dz<=4;dz++)
            {
                int x=p.x+dx,y=p.y+dy,z=p.z+dz;BlockId id=World.GetBlock(x,y,z);
                if(BlockRegistry.IsWater(id))return true;
                if(BlockRegistry.IsAquatic(id)&&(BlockRegistry.NeedsWater(id)||(World.GetMeta(x,y,z)&4)!=0))return true;
            }
            return false;
        }

        void AdvanceSaplings(float elapsed)
        {
            if(saplings.Count==0)return;keys.Clear();foreach(var kv in saplings)keys.Add(kv.Key);
            for(int k=0;k<keys.Count;k++)
            {
                Vector3Int p=keys[k];if(!saplings.TryGetValue(p,out SaplingEntry e)||!World.IsChunkLoadedAt(p.x,p.z))continue;
                if(!IsSapling(World.GetBlock(p.x,p.y,p.z))){saplings.Remove(p);continue;}
                e.T+=elapsed;if(e.T<e.Delay)continue;
                TryGrowSapling(p);
            }
        }

        bool TryGrowSapling(Vector3Int p)
        {
            BlockId sap=World.GetBlock(p.x,p.y,p.z);if(!IsSapling(sap))return false;
            if(sap==BlockId.DarkOakSapling)
            {
                for(int ox=-1;ox<=0;ox++)for(int oz=-1;oz<=0;oz++)
                {
                    int bx=p.x+ox,bz=p.z+oz;bool all=true;
                    for(int x=0;x<=1&&all;x++)for(int z=0;z<=1;z++)if(World.GetBlock(bx+x,p.y,bz+z)!=BlockId.DarkOakSapling){all=false;break;}
                    if(!all)continue;
                    for(int x=0;x<=1;x++)for(int z=0;z<=1;z++){Vector3Int q=new Vector3Int(bx+x,p.y,bz+z);World.SetBlockDeferredPersistent(q.x,q.y,q.z,BlockId.Air,0);saplings.Remove(q);}
                    if(GrowTree(BlockId.DarkOakSapling,bx,p.y,bz))return true;
                    Vector3Int restore=new Vector3Int(bx,p.y,bz);
                    World.SetBlockDeferredPersistent(restore.x,restore.y,restore.z,BlockId.DarkOakSapling,0);
                    saplings[restore]=new SaplingEntry{T=0f,Delay=30f};return false;
                }
                if(saplings.TryGetValue(p,out SaplingEntry retry)){retry.T=0f;retry.Delay=30f;}
                return false;
            }
            World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0);saplings.Remove(p);
            if(GrowTree(sap,p.x,p.y,p.z))return true;
            World.SetBlockDeferredPersistent(p.x,p.y,p.z,sap,0);
            saplings[p]=new SaplingEntry{T=0f,Delay=30f};return false;
        }

        bool GrowTree(BlockId sap,int x,int y,int z)
        {
            int n,yy;
            if(sap==BlockId.DarkOakSapling)
            {
                n=5+UnityEngine.Random.Range(0,2);
                for(int dx=0;dx<=1;dx++)for(int dz=0;dz<=1;dz++)if(!ColumnReplaceable(x+dx,y,z+dz,n))return false;
                for(int a=-2;a<=1;a++){yy=y+n+a;if(yy>y){Canopy(x,yy,z,a<=0?3:2,BlockId.DarkOakLeaves);Canopy(x+1,yy,z+1,a<=0?3:2,BlockId.DarkOakLeaves);}}
                for(int a=0;a<n;a++){Place(x,y+a,z,BlockId.DarkOakLog);Place(x+1,y+a,z,BlockId.DarkOakLog);Place(x,y+a,z+1,BlockId.DarkOakLog);Place(x+1,y+a,z+1,BlockId.DarkOakLog);}return true;
            }
            if(sap==BlockId.SpruceSapling)
            {
                bool tall=UnityEngine.Random.value<.25f;n=tall?10+UnityEngine.Random.Range(0,4):6+UnityEngine.Random.Range(0,3);if(!ColumnReplaceable(x,y,z,n))return false;
                PlaceReplaceable(x,y+n+1,z,BlockId.SpruceLeaves);int c=tall?8:6;
                for(int a=0;a<c&&(yy=y+n-a)>y+2;a++)Canopy(x,yy,z,a==0||a%2==1?1:2,BlockId.SpruceLeaves);
                for(int a=0;a<n;a++)Place(x,y+a,z,BlockId.SpruceLog);return true;
            }
            if(sap==BlockId.JungleSapling)
            {
                n=8+UnityEngine.Random.Range(0,4);if(!ColumnReplaceable(x,y,z,n))return false;
                Canopy(x,y+n+1,z,1,BlockId.JungleLeaves);Canopy(x,y+n,z,2,BlockId.JungleLeaves);Canopy(x,y+n-1,z,2,BlockId.JungleLeaves);
                for(int a=0;a<n;a++)Place(x,y+a,z,BlockId.JungleLog);return true;
            }
            if(sap==BlockId.AcaciaSapling)
            {
                n=5+UnityEngine.Random.Range(0,2);if(!ColumnReplaceable(x,y,z,n))return false;
                int dir=UnityEngine.Random.Range(0,4),dx=dir==0?1:dir==1?-1:0,dz=dir==2?1:dir==3?-1:0,cx=x,cz=z;
                for(int a=1;a<=n;a++){if(a>2&&a%2==1){cx+=dx;cz+=dz;}Place(cx,y+a-1,cz,BlockId.AcaciaLog);}Canopy(cx,y+n,cz,3,BlockId.AcaciaLeaves);Canopy(cx,y+n+1,cz,2,BlockId.AcaciaLeaves);return true;
            }
            if(sap==BlockId.OakSapling&&UnityEngine.Random.value<.3f)
            {
                n=7+UnityEngine.Random.Range(0,3);if(!ColumnReplaceable(x,y,z,n))return false;int top=y+n;int[] rad={2,3,3,2,1};
                for(int a=0;a<rad.Length;a++){yy=top-3+a;if(yy>y)Canopy(x,yy,z,rad[a],BlockId.OakLeaves);}int dir=UnityEngine.Random.Range(0,4);int bx=x+(dir==0?2:dir==1?-2:0),bz=z+(dir==2?2:dir==3?-2:0);Canopy(bx,top-3,bz,1,BlockId.OakLeaves);
                for(int a=0;a<n;a++)Place(x,y+a,z,BlockId.OakLog);return true;
            }
            bool birch=sap==BlockId.BirchSapling;n=(birch?5:4)+UnityEngine.Random.Range(0,3);if(!ColumnReplaceable(x,y,z,n))return false;
            BlockId log=birch?BlockId.BirchLog:BlockId.OakLog,leaf=birch?BlockId.BirchLeaves:BlockId.OakLeaves;
            for(int a=-2;a<=1;a++){yy=y+n+a;if(yy>y)Canopy(x,yy,z,a<=-1?2:1,leaf);}for(int a=0;a<n;a++)Place(x,y+a,z,log);return true;
        }

        bool ColumnReplaceable(int x,int y,int z,int height)
        {
            for(int i=0;i<height;i++)if(!ReplaceableTreeSpace(World.GetBlock(x,y+i,z)))return false;return true;
        }
        static bool ReplaceableTreeSpace(BlockId id)=>id==BlockId.Air||BlockRegistry.Get(id).Shape==BlockShape.Cross;
        static bool ReplaceablePlantSpace(BlockId id)=>id==BlockId.Air||BlockRegistry.Get(id).Shape==BlockShape.Cross;
        void PlaceReplaceable(int x,int y,int z,BlockId id){if(y<1||y>VoxelConstants.MaxY||!ReplaceableTreeSpace(World.GetBlock(x,y,z)))return;Place(x,y,z,id);}
        void Place(int x,int y,int z,BlockId id){if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;World.SetBlockDeferredPersistent(x,y,z,id,0);}
        void Canopy(int x,int y,int z,int radius,BlockId leaf)
        {
            for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++)
            {
                if(radius>=2&&Mathf.Abs(dx)==radius&&Mathf.Abs(dz)==radius&&UnityEngine.Random.value<.6f)continue;
                PlaceReplaceable(x+dx,y,z+dz,leaf);
            }
        }

        void AdvanceFire(float elapsed)
        {
            if(fires.Count==0)return;keys.Clear();foreach(var kv in fires)keys.Add(kv.Key);
            for(int k=0;k<keys.Count;k++)
            {
                Vector3Int p=keys[k];if(!fires.TryGetValue(p,out FireEntry e)||!World.IsChunkLoadedAt(p.x,p.z))continue;
                if(World.GetBlock(p.x,p.y,p.z)!=BlockId.Fire){fires.Remove(p);continue;}
                e.T+=elapsed;if(e.T<e.Next)continue;e.T=0f;e.Next=.7f+UnityEngine.Random.value*.6f;e.Age=Math.Min(15,e.Age+1);
                bool fuel=false,removed=false;
                for(int i=0;i<Six.Length;i++)
                {
                    BlockId n=World.GetBlock(p.x+Six[i].x,p.y+Six[i].y,p.z+Six[i].z);
                    if(BlockRegistry.IsWater(n)){World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0);fires.Remove(p);removed=true;break;}
                    if(IsFlammable(n))fuel=true;
                }
                if(removed)continue;
                if(fuel)
                {
                    if(e.Age>=15&&UnityEngine.Random.value<.2f){World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0);fires.Remove(p);continue;}
                }
                else if(!SupportsFire(World.GetBlock(p.x,p.y-1,p.z))||(e.Age>=4&&UnityEngine.Random.value<.35f))
                {World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0);fires.Remove(p);continue;}

                for(int i=0;i<Six.Length;i++)
                {
                    Vector3Int q=p+Six[i];if(q.y<VoxelConstants.MinY||q.y>VoxelConstants.MaxY)continue;BlockId id=World.GetBlock(q.x,q.y,q.z);if(!IsFlammable(id))continue;
                    float chance=Six[i].y==1?.9f:.7f;if(UnityEngine.Random.value<chance)World.SetBlockDeferredPersistent(q.x,q.y,q.z,UnityEngine.Random.value<.8f?BlockId.Fire:BlockId.Air,0);
                }
                for(int n=0;n<3;n++)
                {
                    Vector3Int q=new Vector3Int(p.x+UnityEngine.Random.Range(-1,2),p.y+UnityEngine.Random.Range(-1,2),p.z+UnityEngine.Random.Range(-1,2));
                    if(q.y<VoxelConstants.MinY||q.y>VoxelConstants.MaxY||q==p||World.GetBlock(q.x,q.y,q.z)!=BlockId.Air)continue;bool nearFuel=false;
                    for(int i=0;i<Six.Length;i++)if(IsFlammable(World.GetBlock(q.x+Six[i].x,q.y+Six[i].y,q.z+Six[i].z))){nearFuel=true;break;}
                    if(nearFuel&&UnityEngine.Random.value<.35f)World.SetBlockDeferredPersistent(q.x,q.y,q.z,BlockId.Fire,0);
                }
            }
        }

        static bool SupportsFire(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsFluid(id))return false;
            return BlockRegistry.Get(id).Shape==BlockShape.Cube;
        }

        static bool IsFlammable(BlockId id)
        {
            if(id==BlockId.Air||id==BlockId.Fire)return false;
            if(BlockRegistry.IsLeaf(id)||IsSapling(id))return true;
            if(BlockRegistry.Get(id).Shape==BlockShape.Cross)return true;
            switch(id)
            {
                case BlockId.OakLog: case BlockId.BirchLog: case BlockId.SpruceLog: case BlockId.JungleLog: case BlockId.AcaciaLog: case BlockId.DarkOakLog:
                case BlockId.OakPlanks: case BlockId.SprucePlanks: case BlockId.BirchPlanks: case BlockId.DarkOakPlanks:
                case BlockId.OakFence: case BlockId.SpruceFence: case BlockId.DarkOakFence: case BlockId.OakFenceGate:
                case BlockId.CraftingTable:return true;
                default:return false;
            }
        }

        // main eB(): crop families and their stages; M4: stems that grow a fruit block.
        static readonly BlockId[] WheatStages={BlockId.Wheat0,BlockId.Wheat1,BlockId.Wheat2,BlockId.Wheat3};
        static readonly BlockId[] CarrotStages={BlockId.Carrots0,BlockId.Carrots1,BlockId.Carrots2,BlockId.Carrots3};
        static readonly BlockId[] PotatoStages={BlockId.Potatoes0,BlockId.Potatoes1,BlockId.Potatoes2,BlockId.Potatoes3};
        static readonly BlockId[] PumpkinStages={BlockId.PumpkinStem0,BlockId.PumpkinStem1,BlockId.PumpkinStem2,BlockId.PumpkinStem3};
        static readonly BlockId[] MelonStages={BlockId.MelonStem0,BlockId.MelonStem1,BlockId.MelonStem2,BlockId.MelonStem3};
        static readonly BlockId[][] CropFamilies={WheatStages,CarrotStages,PotatoStages,PumpkinStages,MelonStages};
        // main AB in source z; Unity z is mirrored.
        static readonly Vector3Int[] FruitDirs={new Vector3Int(1,0,0),new Vector3Int(-1,0,0),new Vector3Int(0,0,-1),new Vector3Int(0,0,1)};
        const float FruitSeconds=45f; // main dD
        static BlockId[] CropStages(BlockId id){foreach(var f in CropFamilies)if(Array.IndexOf(f,id)>=0)return f;return null;}
        static BlockId CropFruit(BlockId id)=>Array.IndexOf(PumpkinStages,id)>=0?BlockId.Pumpkin:Array.IndexOf(MelonStages,id)>=0?BlockId.Melon:BlockId.Air;
        static bool IsCrop(BlockId id)=>CropStage(id)>=0;
        static int CropStage(BlockId id){foreach(var f in CropFamilies){int i=Array.IndexOf(f,id);if(i>=0)return i;}return -1;}
        static bool IsSapling(BlockId id)=>id==BlockId.OakSapling||id==BlockId.BirchSapling||id==BlockId.SpruceSapling||id==BlockId.JungleSapling||id==BlockId.AcaciaSapling||id==BlockId.DarkOakSapling;

        public MainTimedCellSaveData[] CaptureCropsSource(){return CaptureFloatMap(crops,kv=>kv.Value.T);}
        public MainTimedCellSaveData[] CaptureSaplingsSource(){return CaptureFloatMap(saplings,kv=>kv.Value.T);}
        public MainTimedCellSaveData[] CaptureFiresSource(){return CaptureFloatMap(fires,kv=>kv.Value.Age);}
        public MainTimedCellSaveData[] CaptureFarmlandSource()
        {
            var a=new MainTimedCellSaveData[farmland.Count];int i=0;foreach(var kv in farmland){Vector3Int p=kv.Key;a[i++]=new MainTimedCellSaveData{x=p.x,y=p.y,z=SourceCoords.UnityBlockZToSource(p.z),value=0f};}return a;
        }
        delegate float ValueSelector<T>(KeyValuePair<Vector3Int,T> kv);
        static MainTimedCellSaveData[] CaptureFloatMap<T>(Dictionary<Vector3Int,T> map,ValueSelector<T> sel)
        {
            var a=new MainTimedCellSaveData[map.Count];int i=0;foreach(var kv in map){Vector3Int p=kv.Key;a[i++]=new MainTimedCellSaveData{x=p.x,y=p.y,z=SourceCoords.UnityBlockZToSource(p.z),value=Mathf.Floor(sel(kv)+.5f)};}return a;
        }

        void Restore(MainWorldSaveData d)
        {
            crops.Clear();farmland.Clear();grassSpread.Clear();saplings.Clear();fires.Clear();
            if(d.crops!=null)for(int i=0;i<d.crops.Length;i++){var v=d.crops[i];if(v==null)continue;Vector3Int p=LoadPos(v);crops[p]=new CropEntry{T=Mathf.Max(0f,v.value)};}
            if(d.farmlandTimers!=null)for(int i=0;i<d.farmlandTimers.Length;i++){var v=d.farmlandTimers[i];if(v==null)continue;farmland[LoadPos(v)]=new FarmEntry();}
            if(d.saplingTimers!=null)for(int i=0;i<d.saplingTimers.Length;i++){var v=d.saplingTimers[i];if(v==null)continue;Vector3Int p=LoadPos(v);saplings[p]=new SaplingEntry{T=Mathf.Max(0f,v.value),Delay=90f+UnityEngine.Random.value*90f};}
            if(d.fireTimers!=null)for(int i=0;i<d.fireTimers.Length&&fires.Count<MaxTrackedFire;i++){var v=d.fireTimers[i];if(v==null)continue;Vector3Int p=LoadPos(v);fires[p]=new FireEntry{T=0f,Next=1.2f+UnityEngine.Random.value,Age=Mathf.Clamp(Mathf.RoundToInt(v.value),0,15)};}
        }
        static Vector3Int LoadPos(MainTimedCellSaveData v)=>new Vector3Int(v.x,v.y,SourceCoords.SourceBlockZToUnity(v.z));
    }
}
