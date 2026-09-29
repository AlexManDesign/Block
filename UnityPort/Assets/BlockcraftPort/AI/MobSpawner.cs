using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-style main.js fd()/D2()/eu()/Id()/Kf(): global caps/cadence, one-time per-chunk fauna,
    /// aquatic spawning and compact mob streaming persistence.
    /// </summary>
    public sealed class MobSpawner : MonoBehaviour
    {
        public static MobSpawner Instance { get; private set; }
        public VoxelWorld World; public Transform Player; public bool SharksEnabled=true;
        readonly List<MobAI> mobs=new List<MobAI>(96);
        readonly Dictionary<ChunkCoord,List<MobStoredState>> stored=new Dictionary<ChunkCoord,List<MobStoredState>>();
        readonly HashSet<ChunkCoord> seededChunks=new HashSet<ChunkCoord>();
        readonly List<int> landCandidates=new List<int>(48);
        readonly List<int> waterCandidates=new List<int>(32);
        float hostileT,passiveT,aquaticT,sharkT;int seq;
        int landAttempts,landSuccess,landRejectedUnavailable,landRejectedNoCandidate;
        int passiveAttempts,passiveSuccess,hostileAttempts,hostileSuccess;
        int seededChunkCalls,seededPassiveRolls,seededPassiveGroups;
        const int SourceWorldMin=-65536,SourceWorldMax=65536;
        static readonly MobKind[] HostileKinds={MobKind.Zombie,MobKind.Zombie,MobKind.Skeleton,MobKind.Creeper,MobKind.Spider,MobKind.Spider,MobKind.Enderman};
        static readonly MobKind[] PassiveKinds={MobKind.Pig,MobKind.Cow,MobKind.Sheep,MobKind.Chicken};
        void Awake(){Instance=this;}
        static float Rand()=>UnityEngine.Random.value;
        static int RandInt(int max)=>max<=1?0:Mathf.Min(max-1,Mathf.FloorToInt(Rand()*max));

        void Update()
        {
            if(World==null||Player==null)return;float dt=Time.deltaTime;
            // main.js main loop: ad(dt) advances the shared 20 Hz mob/projectile simulation before fd(dt) spawning.
            MobAI.AdvanceSourceFrame(dt);
            hostileT-=dt;passiveT-=dt;aquaticT-=dt;sharkT-=dt;
            for(int i=mobs.Count-1;i>=0;i--)if(mobs[i]==null)mobs.RemoveAt(i);
            CountKinds(out int hostile,out int passive,out int aquatic,out int hunters);
            int hcap=HostileCap(),pcap=PassiveCap();
            if(SharksEnabled)
            {
                // main.js fd(): Vf is reset to 12 even when the hunter cap is already full.
                if(sharkT<=0){sharkT=12f;if(hunters<2)for(int i=0;i<6;i++){Vector2Int p=Ring(18,48);if(TrySpawnShark(p.x,p.y))break;}}
            }
            else for(int i=mobs.Count-1;i>=0;i--)if(mobs[i]!=null&&mobs[i].Def.Hunter){MobAI m=mobs[i];mobs.RemoveAt(i);m.DespawnWithoutEffects();}
            if(aquaticT<=0){aquaticT=5f;for(int i=0;i<10&&aquatic<40;i++){Vector2Int p=Ring(10,40);aquatic+=SpawnSalmonGroup(p.x,p.y,40-aquatic);}}
            if(hostileT<=0&&hostile<hcap){Vector2Int p=SpawnRing();hostile+=SpawnLandGroup(MobKind.SlimeBig,true,p.x,p.y,Math.Min(2,hcap-hostile));}
            if(hostileT<=0)
            {
                hostileT=2f;
                for(int i=0;i<4&&hostile<hcap;i++){Vector2Int p=SpawnRing();hostile+=SpawnLandGroup(HostileKinds[RandInt(HostileKinds.Length)],true,p.x,p.y,hcap-hostile);}
            }
            if(passiveT<=0){passiveT=20f;if(DaytimeForPassive()&&passive<pcap){Vector2Int p=SpawnRing();SpawnLandGroup(PassiveKinds[RandInt(PassiveKinds.Length)],false,p.x,p.y,pcap-passive);}}
        }

        // BC_TEMP_DEBUG_BEGIN [F3_MOB_SPAWN_TELEMETRY] REMOVE BEFORE RELEASE
        public void GetDebugCounts(out int total,out int hostile,out int passive,out int fish,out int sharks,out int storedCount,out int hostileCap,out int passiveCap,out float spawnFar)
        {
            CountKinds(out hostile,out passive,out int aquatic,out sharks);
            fish=Mathf.Max(0,aquatic-sharks);total=hostile+passive+aquatic;storedCount=0;
            foreach(var kv in stored)storedCount+=kv.Value!=null?kv.Value.Count:0;
            spawnFar=SpawnFar();hostileCap=HostileCap();passiveCap=PassiveCap();
        }

        public void GetSpawnDebug(out int landTry,out int landOk,out int unavailable,out int noCandidate,
            out int passiveTry,out int passiveOk,out int hostileTry,out int hostileOk,
            out int seededCalls,out int passiveRolls,out int passiveGroups)
        {
            landTry=landAttempts;landOk=landSuccess;unavailable=landRejectedUnavailable;noCandidate=landRejectedNoCandidate;
            passiveTry=passiveAttempts;passiveOk=passiveSuccess;hostileTry=hostileAttempts;hostileOk=hostileSuccess;
            seededCalls=seededChunkCalls;passiveRolls=seededPassiveRolls;passiveGroups=seededPassiveGroups;
        }

        // BC_TEMP_DEBUG_END [F3_MOB_SPAWN_TELEMETRY]

        // main.js bp(): only non-persistent passive land mobs above hp=40 are trimmed, and only when
        // they are farther than Nh()=min(128, spawnFar+24). Natural spawning is capped much lower,
        // but this also covers manually spawned/passively restored crowds without changing source rules.
        void CountKinds(out int hostile,out int passive,out int aquatic,out int hunters)
        {
            hostile=passive=aquatic=hunters=0;
            for(int i=0;i<mobs.Count;i++)
            {
                MobAI m=mobs[i];if(m==null||m.IsDead)continue;var d=m.Def;
                if(d.Aquatic){aquatic++;if(d.Hunter)hunters++;}else if(d.Hostile)hostile++;else passive++;
            }
        }

        public bool StoreMob(MobAI mob)
        {
            if(mob==null||mob.IsDead)return false;ChunkCoord c=ChunkCoord.FromWorld(Mathf.FloorToInt(mob.transform.position.x),Mathf.FloorToInt(mob.transform.position.z));
            StoreState(c,mob.CaptureStoredState());mobs.Remove(mob);mob.MarkStoredForStreaming();Destroy(mob.gameObject);return true;
        }

        void StoreState(ChunkCoord c,MobStoredState state)
        {
            if(!stored.TryGetValue(c,out var list)){list=new List<MobStoredState>(8);stored[c]=list;}list.Add(state);
        }

        public void OnChunkUnloading(ChunkCoord c)
        {
            for(int i=mobs.Count-1;i>=0;i--)
            {
                MobAI m=mobs[i];if(m==null){mobs.RemoveAt(i);continue;}Vector3 p=m.transform.position;ChunkCoord mc=ChunkCoord.FromWorld(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.z));if(!mc.Equals(c))continue;
                StoreState(c,m.CaptureStoredState());mobs.RemoveAt(i);m.MarkStoredForStreaming();Destroy(m.gameObject);
            }
        }

        public void OnChunkLoaded(ChunkCoord c)
        {
            if(World==null||Player==null)return;
            seededChunkCalls++;
            if(stored.TryGetValue(c,out var list))
            {
                stored.Remove(c);for(int i=0;i<list.Count;i++)SpawnStored(list[i]);
            }
            if(!seededChunks.Add(c))return;
            // main.js eu(): each chunk gets one source-style initial fauna opportunity exactly once.
            int aquatic=0;for(int i=0;i<mobs.Count;i++)if(mobs[i]!=null&&!mobs[i].IsDead&&mobs[i].Def.Aquatic)aquatic++;
            int room=Mathf.Max(0,40-aquatic);
            for(int n=0;n<3&&room>0;n++)if(Rand()<.4f)
            {
                int x=c.X*16+RandInt(16),z=c.Z*16+RandInt(16);int added=SpawnSalmonGroup(x,z,room);room-=added;
            }
            // main.js eu(): this 10% initial passive attempt does not consult the global passive cap.
            seededPassiveRolls++;
            if(Rand()<.1f)
            {
                int x=c.X*16+RandInt(16),z=c.Z*16+RandInt(16);
                if(SpawnLandGroup(PassiveKinds[RandInt(PassiveKinds.Length)],false,x,z,4)>0)seededPassiveGroups++;
            }
        }

        static MainMobSaveData EncodePersistentMob(MobStoredState state)
        {
            Vector3 p=state.Position;
            return new MainMobSaveData{kind=(int)state.Kind,x=p.x,y=p.y,z=SourceCoords.UnityWorldZToSource(p.z),hp=state.HP,
                baby=state.Baby,sheared=state.Sheared,color=(int)state.SheepColor,persist=state.Persist};
        }

        public MainMobSaveData[] CapturePersistentMobsSource()
        {
            int count=0;for(int i=0;i<mobs.Count;i++)if(mobs[i]!=null&&!mobs[i].IsDead)count++;
            foreach(var kv in stored)count+=kv.Value.Count;
            var result=new MainMobSaveData[count];int o=0;
            for(int i=0;i<mobs.Count;i++)if(mobs[i]!=null&&!mobs[i].IsDead)result[o++]=EncodePersistentMob(mobs[i].CaptureStoredState());
            foreach(var kv in stored)for(int i=0;i<kv.Value.Count;i++)result[o++]=EncodePersistentMob(kv.Value[i]);
            return result;
        }

        public int[] CaptureSeededChunksSource()
        {
            // Save ordering is not semantic: restore inserts these pairs back into a HashSet. Avoid
            // copying + sorting every explored chunk during autosave capture; long worlds otherwise
            // grow an O(N log N) main-thread spike even after JSON/disk writing moves off-thread.
            int[] flat=new int[seededChunks.Count*2];int o=0;
            foreach(var c in seededChunks)
            {
                ChunkCoord sc=SourceCoords.UnityChunkToSource(c);flat[o++]=sc.X;flat[o++]=sc.Z;
            }
            return flat;
        }

        public void RestorePersistentState(MainMobSaveData[] savedMobs,int[] sourceSeededChunks)
        {
            stored.Clear();seededChunks.Clear();
            if(sourceSeededChunks!=null)for(int i=0;i+1<sourceSeededChunks.Length;i+=2)
                seededChunks.Add(SourceCoords.SourceChunkToUnity(new ChunkCoord(sourceSeededChunks[i],sourceSeededChunks[i+1])));
            if(savedMobs==null)return;
            for(int i=0;i<savedMobs.Length;i++)
            {
                MainMobSaveData d=savedMobs[i];if(d==null||d.kind<0||d.kind>(int)MobKind.SlimeSmall)continue;
                var kind=(MobKind)d.kind;var def=MobAI.Definition(kind);
                Vector3 pos=new Vector3(d.x,d.y,SourceCoords.SourceWorldZToUnity(d.z));
                var state=new MobStoredState(kind,pos,Mathf.Clamp(d.hp,1f,def.Hp),d.baby,d.sheared,
                    (MobSheepColor)Mathf.Clamp(d.color,(int)MobSheepColor.White,(int)MobSheepColor.Pink),d.persist);
                ChunkCoord c=ChunkCoord.FromWorld(Mathf.FloorToInt(pos.x),Mathf.FloorToInt(pos.z));StoreState(c,state);
            }
        }

        float SpawnFar()=>Mathf.Max(44f,Mathf.Min(128f,World.RenderDistance*16f-16f));
        float CapScale(){float f=SpawnFar();return (f*f-24f*24f)/(44f*44f-24f*24f);}
        int HostileCap()=>Mathf.Min(24,SourceMath.JsRound(12f*CapScale()));
        int PassiveCap()=>Mathf.Min(14,SourceMath.JsRound(8f*CapScale()));
        bool DaytimeForPassive()=>Mathf.Sin(DayNight.DayTime01*Mathf.PI*2f)>.05f;
        bool NightForSlime()=>Mathf.Sin(DayNight.DayTime01*Mathf.PI*2f)<-.17f;
        Vector2Int SpawnRing()=>Ring(24,SpawnFar());
        Vector2Int Ring(float min,float max){float a=Rand()*Mathf.PI*2f,r=min+Rand()*(max-min);return new Vector2Int(Mathf.FloorToInt(Player.position.x+Mathf.Cos(a)*r),Mathf.FloorToInt(Player.position.z+Mathf.Sin(a)*r));}

        int SpawnLandGroup(MobKind kind,bool hostile,int x,int z,int max)
        {
            int spawned=0,px=x,pz=z;
            for(int attempt=0;attempt<4&&spawned<max;attempt++)
            {
                px+=RandInt(6)-RandInt(6);pz+=RandInt(6)-RandInt(6);
                if(TrySpawnLand(kind,px,pz,hostile))spawned++;
            }
            return spawned;
        }

        bool TrySpawnLand(MobKind kind,int x,int z,bool hostile)
        {
            // main.js Xp(): gather valid Y levels first, then pick one with a single yn()/Math.random().
            landAttempts++;if(hostile)hostileAttempts++;else passiveAttempts++;
            if(x<SourceWorldMin+2||x>=SourceWorldMax-2||z<SourceWorldMin+2||z>=SourceWorldMax-2||!World.IsChunkLoadedAt(x,z))
            {
                landRejectedUnavailable++;return false;
            }
            MobDefinition def=MobAI.Definition(kind);int center=Mathf.FloorToInt(Player.position.y),hi=Math.Min(VoxelConstants.MaxY-3,center+20),lo=Math.Max(VoxelConstants.MinY+1,center-20);int need=def.Height>1.9f?3:2,air=0;
            bool slime=def.SlimeSize>0,slimeChunk=false,swamp=false;
            if(slime)
            {
                ChunkCoord unityChunk=new ChunkCoord(Mathf.FloorToInt(x/16f),Mathf.FloorToInt(z/16f));
                ChunkCoord sourceChunk=SourceCoords.UnityChunkToSource(unityChunk);
                slimeChunk=SlimeChunk(sourceChunk.X,sourceChunk.Z,World.Seed);
                BiomeId biome=World.GetBiome(x,z).Biome;
                swamp=biome==BiomeId.Swamp||biome==BiomeId.MangroveSwamp;
            }
            landCandidates.Clear();
            for(int y=hi+2;y>=lo;y--)
            {
                BlockId id=World.GetBlock(x,y,z);
                if(id==BlockId.Air){air++;continue;}
                if(air>=need&&SourcePointCollision.OrdinarySolidCube(World,x,y,z))
                {
                    int sy=y+1;bool ok=false;
                    if(slime)ok=(slimeChunk&&sy<40)||(swamp&&NightForSlime()&&sy>=50&&sy<=70);
                    else if(hostile){byte light=World.GetPackedLight(x,sy,z);int sky=light>>4,block=light&15;ok=block<=7&&(NightForSlime()||sky<=4);}
                    else ok=id==BlockId.Grass;
                    if(ok&&SpawnSpaceClear(def,x+.5f,sy,z+.5f))landCandidates.Add(sy);
                }
                air=0;
            }
            if(landCandidates.Count==0){landRejectedNoCandidate++;return false;}
            int spawnY=landCandidates[RandInt(landCandidates.Count)];Spawn(kind,new Vector3(x+.5f,spawnY,z+.5f));
            landSuccess++;if(hostile)hostileSuccess++;else passiveSuccess++;return true;
        }

        bool SpawnSpaceClear(MobDefinition d,float x,float y,float z)
        {
            // main.js gt() at spawn time: four body corners, three height samples, +.05 bottom epsilon.
            float r=d.CollisionWidth;
            for(int yi=0;yi<3;yi++)
            {
                float yo=yi==0?.05f:(yi==1?d.Height*.5f:d.Height-.1f);
                for(int xi=-1;xi<=1;xi+=2)for(int zi=-1;zi<=1;zi+=2)
                    if(SourcePointCollision.Contains(World,new Vector3(x+xi*r,y+yo,z+zi*r)))return false;
            }
            return true;
        }

        static bool SlimeChunk(int cx,int cz,int seed){unchecked{int t=cx*522133279^cz*668265261^seed;t=(t^(int)((uint)t>>15))*unchecked((int)2246822507u);t^=(int)((uint)t>>13);return((uint)t)%10==0;}}

        int SpawnSalmonGroup(int x,int z,int max)
        {
            int target=Math.Min(max,3+RandInt(3)),spawned=0;
            for(int i=0;i<target*4&&spawned<target;i++)
            {
                float a=Rand()*Mathf.PI*2f,r=2+Rand()*7f;
                int sx=SourceMath.JsRound(x+Mathf.Cos(a)*r),sz=SourceMath.JsRound(z+Mathf.Sin(a)*r);
                if(TrySpawnSalmon(sx,sz))spawned++;
            }
            return spawned;
        }
        bool TrySpawnSalmon(int x,int z)
        {
            if(x<SourceWorldMin+2||x>=SourceWorldMax-2||z<SourceWorldMin+2||z>=SourceWorldMax-2||!World.IsChunkLoadedAt(x,z))return false;
            waterCandidates.Clear();
            for(int y=Math.Min(VoxelConstants.MaxY-3,VoxelConstants.SeaLevel);y>=VoxelConstants.SeaLevel-24;y--)
                if(IsSwimmable(x,y,z)&&IsSwimmable(x,y+1,z))waterCandidates.Add(y);
            if(waterCandidates.Count==0)return false;
            int yy=waterCandidates[RandInt(waterCandidates.Count)];
            Spawn(MobKind.Salmon,new Vector3(x+.5f,yy+.15f+Rand()*.45f,z+.5f));return true;
        }
        bool TrySpawnShark(int x,int z)
        {
            if(x<SourceWorldMin+2||x>=SourceWorldMax-2||z<SourceWorldMin+2||z>=SourceWorldMax-2||!World.IsChunkLoadedAt(x,z)||!IsOcean(World.GetBiome(x,z).Biome))return false;
            int top=Math.Min(VoxelConstants.MaxY-3,VoxelConstants.SeaLevel),count=0;
            for(int y=top;y>=VoxelConstants.SeaLevel-24&&IsSwimmable(x,y,z);y--)count++;
            if(count<6)return false;
            int pick=Mathf.FloorToInt(count*(.4f+Rand()*.5f));if(pick>=count)pick=count-1;int yy=top-pick;
            if(!IsSwimmable(x,yy+1,z))return false;Spawn(MobKind.Shark,new Vector3(x+.5f,yy+.2f,z+.5f));return true;
        }
        bool IsSwimmable(int x,int y,int z){BlockId b=World.GetBlock(x,y,z);return b==BlockId.Water||BlockRegistry.IsAquatic(b);}
        static bool IsOcean(BiomeId b)=>b==BiomeId.Ocean||b==BiomeId.ColdOcean||b==BiomeId.LukewarmOcean||b==BiomeId.WarmOcean||b==BiomeId.FrozenOcean;

        MobAI Spawn(MobKind kind,Vector3 pos)
        {
            var go=new GameObject();go.transform.SetParent(transform,false);go.transform.position=pos;var m=go.AddComponent<MobAI>();m.Init(kind,World,Player,World.Seed+(++seq)*7919);mobs.Add(m);return m;
        }
        void SpawnStored(MobStoredState state){MobAI m=Spawn(state.Kind,state.Position);m.RestoreState(state);}
        /// <summary>main gC()/fa(): a spawn egg creates a persistent mob at the target cell.</summary>
        public static bool SpawnFromEgg(string sourceType,Vector3 pos)
        {
            var sp=Instance;if(sp==null||sp.World==null)return false;
            MobKind kind;
            switch(sourceType)
            {
                case "pig":kind=MobKind.Pig;break;case "cow":kind=MobKind.Cow;break;case "sheep":kind=MobKind.Sheep;break;
                case "chicken":kind=MobKind.Chicken;break;case "zombie":kind=MobKind.Zombie;break;case "skeleton":kind=MobKind.Skeleton;break;
                case "creeper":kind=MobKind.Creeper;break;case "spider":kind=MobKind.Spider;break;case "enderman":kind=MobKind.Enderman;break;
                case "salmon":kind=MobKind.Salmon;break;case "shark":kind=MobKind.Shark;break;case "slime_big":kind=MobKind.SlimeBig;break;
                default:return false;
            }
            MobAI m=sp.Spawn(kind,pos);m.MarkPersistent();return true;
        }
        public void SpawnSplit(MobKind kind,Vector3 pos,Vector3 velocity,float anger){MobAI m=Spawn(kind,pos);m.SetSplitLaunch(velocity,anger);}
        public void SpawnBaby(MobKind kind,Vector3 pos,MobSheepColor parentA,MobSheepColor parentB)
        {
            MobAI m=Spawn(kind,pos);
            MobSheepColor color=kind==MobKind.Sheep?(Rand()<.5f?parentA:parentB):MobSheepColor.White;
            m.MakeBaby(color);
        }
        void OnDestroy(){if(Instance==this)Instance=null;}
    }
}
