using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    public enum MobKind
    {
        Pig,Cow,Sheep,Chicken,
        Zombie,Skeleton,Creeper,Spider,Enderman,
        Salmon,Shark,
        SlimeBig,SlimeMedium,SlimeSmall
    }

    public enum MobSheepColor { White=0, Black=1, Gray=2, LightGray=3, Brown=4, Pink=5 }

    public readonly struct MobStoredState
    {
        public readonly MobKind Kind;
        public readonly Vector3 Position;
        public readonly float HP;
        public readonly bool Baby,Sheared,Persist;
        public readonly MobSheepColor SheepColor;
        public MobStoredState(MobKind kind,Vector3 position,float hp,bool baby,bool sheared,MobSheepColor color,bool persist)
        {Kind=kind;Position=position;HP=hp;Baby=baby;Sheared=sheared;SheepColor=color;Persist=persist;}
    }

    public readonly struct MobDefinition
    {
        public readonly float Hp,Speed,Width,Height,Melee,Detect,ModelScale,CollisionWidth;
        public readonly bool Hostile,Aquatic,Hunter,Despawn,Climber,Jumper,Neutral;
        public readonly bool Ranged,Creeper,Leaper,Teleports,HatesWater,Burns;
        public readonly int NeutralLight,SlimeSize;
        public readonly MobKind SplitKind;
        public readonly bool Splits;

        public MobDefinition(float hp,float speed,float width,float height,float melee=0,float detect=16,float modelScale=1,
            bool hostile=false,bool aquatic=false,bool hunter=false,bool despawn=false,bool climber=false,bool jumper=false,bool neutral=false,
            bool ranged=false,bool creeper=false,bool leaper=false,bool teleports=false,bool hatesWater=false,bool burns=false,
            int neutralLight=0,int slimeSize=0,bool splits=false,MobKind splitKind=MobKind.SlimeSmall,float collisionWidth=0)
        {
            Hp=hp;Speed=speed;Width=width;Height=height;Melee=melee;Detect=detect;ModelScale=modelScale;
            Hostile=hostile;Aquatic=aquatic;Hunter=hunter;Despawn=despawn;Climber=climber;Jumper=jumper;Neutral=neutral;
            Ranged=ranged;Creeper=creeper;Leaper=leaper;Teleports=teleports;HatesWater=hatesWater;Burns=burns;
            NeutralLight=neutralLight;SlimeSize=slimeSize;Splits=splits;SplitKind=splitKind;CollisionWidth=collisionWidth>0?collisionWidth:width;
        }
    }

    /// <summary>
    /// Source-oriented port of main.js bp()/yp()/Dp()/pf(). Simulation cadence is 20 Hz and rendering is
    /// interpolated between source ticks. The per-type state machine (melee/ranged/creeper/leaper/jumper/
    /// teleport/aquatic hunter), path cadence, water/ledge avoidance and source despawn rules live here.
    /// </summary>
    public sealed class MobAI : MonoBehaviour
    {
        // Previous movement baseline retained for regression validators: R30_MOB_MOVEMENT_SOURCE_EXACT_2026-09-12
        public const string PortRevision = "R47_MOB_AI_DEATH_BURN_PARITY_2026-09-17";
        public const string BehaviorBaseline = "R31_MOB_BEHAVIOR_SOURCE_EXACT_2026-09-12";
        public static readonly List<MobAI> Active = new List<MobAI>(96);
        static float sourceAccumulator,sourceAlpha=1f;
        // BC_TEMP_DEBUG_BEGIN [F3_MOB_AI_PERF] REMOVE BEFORE RELEASE
        public static float LastSourceStepMs { get; private set; }
        public static float MaxSourceStepMs { get; private set; }
        public static int LastSourceStepFrame { get; private set; }=-1000000;
        public static int LastSourceTicks { get; private set; }
        public static void ResetPerformancePeaks(){MaxSourceStepMs=0f;}
        // BC_TEMP_DEBUG_END [F3_MOB_AI_PERF]
        static int sourcePassiveExcess;
        static Vector3 sourcePlayerPosition;
        static bool sourcePlayerSurvivalActive,sourcePlayerInWater;
        public static float SourceInterpolationAlpha => sourceAlpha;
        internal static Vector3 SourceTickPlayerPosition => sourcePlayerPosition;
        internal static bool SourceTickPlayerSurvivalActive => sourcePlayerSurvivalActive;

        public MobKind Kind { get; private set; }
        public MobDefinition Def { get; private set; }
        public float HP { get; private set; }
        public bool InWater { get; private set; }
        public bool IsDead => dead;
        public float AngryTime => angryT;
        public float SourceAge => age;
        public bool IsBaby => baby;
        public bool IsSheared => sheared;
        public bool Persist => persist;
        public MobSheepColor SheepColor => sheepColor;
        public float BodyWidth => Def.Width*(baby?.5f:1f);
        public float BodyHeight => Def.Height*(baby?.5f:1f);
        public float BodyCollisionWidth => Def.CollisionWidth*(baby?.5f:1f);

        VoxelWorld world; Transform player; MainPlayerController playerController; MainEntityModel visual;
        float age,aiT,attackT,shootT,jumpT,pathT,dryT,hopT,walkPhase,pitch,leapT,squashY=1f,kbT,growlT;
        float fleeT,stuckT,detourT,tpT,stareT,burnT,lavaT,swimT,escT,huntT,angryT,hurtT,fuse=-1f;
        float loveT,breedCd,growT,woolT,eggT;
        float pathTx,pathTz,detX,detZ,escX,escY,escZ,escDX,escDZ,wanderX,wanderZ;
        int pathI;
        List<Vector3> path;
        static readonly int[] PathDX={1,-1,0,0,1,1,-1,-1};
        static readonly int[] PathDZ={0,0,1,-1,1,-1,1,-1};
        static readonly float[] radii={.3f,.6f,.9f,1.3f,1.8f,2.4f,3.2f};
        [ThreadStatic] static Dictionary<long,int> pathMapScratch;
        [ThreadStatic] static Dictionary<long,bool> pathClearScratch;
        [ThreadStatic] static Dictionary<long,bool> pathStandScratch;
        [ThreadStatic] static List<PathNode> pathNodesScratch;
        [ThreadStatic] static List<int> pathOpenScratch;
        [ThreadStatic] static List<Vector3> pathReverseScratch;
        Vector3 velocity,wish,previousSimPosition;
        float yaw,tgtYaw,previousSimYaw;
        bool onGround,fleeing,hasEscapeTarget,simPoseReady,dead,baby,sheared,persist,lastHitByPlayer,moving;
        MobSheepColor sheepColor=MobSheepColor.White;
        MobAI prey;
        bool preyPlayer;

        public void Init(MobKind kind,VoxelWorld w,Transform p,int salt)
        {
            Kind=kind;Def=Definition(kind);HP=Def.Hp;world=w;player=p;playerController=p!=null?p.GetComponent<MainPlayerController>():null;
            name="mob_"+kind;wanderX=transform.position.x;wanderZ=transform.position.z;path=new List<Vector3>(64);
            // main.js fa() object-literal evaluation order: yaw, growlT, leapT, hopT, eggT, then sheep up().
            yaw=Rand()*Mathf.PI*2f;growlT=2f+Rand()*4f;leapT=1.5f+Rand()*1.5f;hopT=Rand()*.8f;eggT=300f+Rand()*300f;
            aiT=0f;swimT=0f;if(kind==MobKind.Sheep)sheepColor=RandomSheepColor();
            tgtYaw=yaw;ApplyYaw();previousSimPosition=transform.position;previousSimYaw=0f;simPoseReady=true;
            // main.js fa() has no inWater property initially; yp() therefore enters its water-side branch once,
            // and pf() computes the real value at the end of that first tick. true reproduces that ===false test.
            InWater=Def.Aquatic;
            visual=MainEntityModel.Build(gameObject,kind,Def.ModelScale);UpdateVisualState();Active.Add(this);
        }

        public static MobDefinition Definition(MobKind k)
        {
            switch(k)
            {
                case MobKind.Pig:return new MobDefinition(10,2.6f,.45f,.95f);
                case MobKind.Cow:return new MobDefinition(10,2.1f,.45f,1.4f);
                case MobKind.Sheep:return new MobDefinition(8,2.4f,.45f,1.3f);
                case MobKind.Chicken:return new MobDefinition(4,2.5f,.25f,.7f);
                case MobKind.Zombie:return new MobDefinition(20,2.4f,.3f,1.95f,3,16,1,hostile:true,burns:true);
                case MobKind.Skeleton:return new MobDefinition(20,2.6f,.3f,1.95f,0,16,1,hostile:true,ranged:true,burns:true);
                case MobKind.Creeper:return new MobDefinition(20,2.7f,.3f,1.6f,0,16,1,hostile:true,creeper:true);
                case MobKind.Spider:return new MobDefinition(16,3.2f,.7f,.9f,2,16,1,hostile:true,climber:true,leaper:true,neutralLight:12,collisionWidth:.45f);
                case MobKind.Enderman:return new MobDefinition(40,3.2f,.3f,2.9f,7,64,2.9f/(50f/16f),hostile:true,neutral:true,teleports:true,hatesWater:true);
                case MobKind.Salmon:return new MobDefinition(3,1.6f,.35f,.4f,0,16,1,aquatic:true,despawn:true);
                case MobKind.Shark:return new MobDefinition(24,4.2f,.45f,.8f,6,20,1.25f,hostile:true,aquatic:true,hunter:true,despawn:true);
                case MobKind.SlimeBig:return new MobDefinition(16,2.1f,1.02f,2.04f,4,16,4,hostile:true,jumper:true,slimeSize:4,splits:true,splitKind:MobKind.SlimeMedium);
                case MobKind.SlimeMedium:return new MobDefinition(4,2.7f,.51f,1.02f,2,16,2,hostile:true,jumper:true,slimeSize:2,splits:true,splitKind:MobKind.SlimeSmall);
                default:return new MobDefinition(1,3.2f,.255f,.51f,0,16,1,hostile:true,jumper:true,slimeSize:1);
            }
        }

        // main.js uses one global Math.random() stream for all mob/spawn AI decisions.
        static float Rand()=>UnityEngine.Random.value;
        static int RandInt(int max)=>max<=1?0:Mathf.Min(max-1,Mathf.FloorToInt(Rand()*max));
        MobSheepColor RandomSheepColor()
        {
            float r=Rand();if(r<.05f)return MobSheepColor.Black;if(r<.10f)return MobSheepColor.Gray;if(r<.15f)return MobSheepColor.LightGray;if(r<.18f)return MobSheepColor.Brown;if(r<.1816f)return MobSheepColor.Pink;return MobSheepColor.White;
        }
        void UpdateVisualState(){if(visual!=null)visual.SetMobState(baby,sheared,sheepColor);}

        public MobStoredState CaptureStoredState()
        {
            Vector3 p=transform.position;p.x=SourceMath.JsRound(p.x*100f)/100f;p.y=SourceMath.JsRound(p.y*100f)/100f;p.z=SourceMath.JsRound(p.z*100f)/100f;
            return new MobStoredState(Kind,p,Mathf.Clamp(HP,1f,Def.Hp),baby,sheared,sheepColor,persist);
        }
        public void RestoreState(MobStoredState state)
        {
            HP=Mathf.Clamp(Mathf.Floor(state.HP),1f,Def.Hp);baby=state.Baby;growT=baby?90f:0f;sheared=state.Sheared;woolT=sheared?90f:0f;sheepColor=state.SheepColor;persist=state.Persist;
            transform.position=state.Position;previousSimPosition=state.Position;UpdateVisualState();
        }
        public void MarkStoredForStreaming(){dead=true;}

        public static void AdvanceSourceFrame(float frameDt)
        {
            long perfStart=System.Diagnostics.Stopwatch.GetTimestamp();
            // main.js ad(): one shared Xi/Ka clock for every mob and arrow. Hp() snapshots all poses
            // before each 20 Hz bp() tick, then bp() advances mobs first and arrows last.
            sourceAccumulator+=frameDt;if(sourceAccumulator>.3f)sourceAccumulator=.3f;
            int loops=0;
            while(sourceAccumulator>=.05f&&loops<3)
            {
                MobSpawner sp=MobSpawner.Instance;
                if(sp!=null&&sp.Player!=null)
                {
                    sourcePlayerPosition=sp.Player.position;
                    MainPlayerController pc=sp.Player.GetComponent<MainPlayerController>();
                    sourcePlayerSurvivalActive=pc!=null&&!pc.IsCreative&&!pc.Dead;
                    sourcePlayerInWater=pc!=null&&pc.InWater;
                }
                for(int i=0;i<Active.Count;i++){MobAI m=Active[i];if(m!=null&&!m.dead)m.CapturePreviousSourcePose();}
                MobProjectileSystem.CapturePreviousSourcePoses();
                int ordinaryPassive=0;
                for(int i=0;i<Active.Count;i++){MobAI m=Active[i];if(m!=null&&!m.dead&&!m.persist&&!m.Def.Hostile&&!m.Def.Aquatic)ordinaryPassive++;}
                sourcePassiveExcess=ordinaryPassive-40;
                for(int i=Active.Count-1;i>=0;i--)
                {
                    if(i>=Active.Count)continue;MobAI m=Active[i];if(m==null||m.dead||m.world==null||m.player==null)continue;m.Think(.05f);
                }
                MobProjectileSystem.SourceStep(.05f);
                sourceAccumulator-=.05f;loops++;
            }
            sourceAlpha=Mathf.Min(1f,sourceAccumulator/.05f);
            LastSourceTicks=loops;
            if(loops>0)
            {
                LastSourceStepMs=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-perfStart)*1000.0/System.Diagnostics.Stopwatch.Frequency);
                LastSourceStepFrame=Time.frameCount;if(LastSourceStepMs>MaxSourceStepMs)MaxSourceStepMs=LastSourceStepMs;
            }
            else LastSourceStepMs=0f;
        }

        void CapturePreviousSourcePose(){previousSimPosition=transform.position;previousSimYaw=yaw;simPoseReady=true;}

        void LateUpdate()
        {
            if(dead||world==null||player==null)return;
            byte light=world.GetPackedLight(Mathf.FloorToInt(transform.position.x),Mathf.FloorToInt(transform.position.y+BodyHeight*.5f),Mathf.FloorToInt(transform.position.z));
            if(visual!=null)
            {
                float alpha=sourceAlpha;Vector3 rp=simPoseReady?Vector3.Lerp(previousSimPosition,transform.position,alpha):transform.position;
                float ry=simPoseReady?LerpAngleRad(previousSimYaw,yaw,alpha):yaw;
                visual.SetRenderPose(rp,ry);visual.SetPackedLight(light);visual.SetSquashY(squashY);
                visual.SetHurtScale(hurtT>0f?.55f:1f);visual.SetFuse(fuse);visual.Animate(walkPhase,moving?.31f:0f,Def.Aquatic,pitch);
            }
        }

        static float LerpAngleRad(float a,float b,float t)
        {
            float d=b-a;while(d>Mathf.PI)d-=Mathf.PI*2f;while(d<-Mathf.PI)d+=Mathf.PI*2f;return a+d*t;
        }

        void Think(float dt)
        {
            // main.js bp(): keep the source state/update ordering because it affects edge cases at
            // despawn, chunk streaming and timer boundaries.
            age+=dt;
            if(hurtT>0)hurtT-=dt;
            if(breedCd>0)breedCd-=dt;
            if(baby&&growT>0){growT-=dt;if(growT<=0){baby=false;growT=0;UpdateVisualState();}}
            if(sheared&&woolT>0){woolT-=dt;if(woolT<=0){sheared=false;woolT=0;UpdateVisualState();}}

            Vector3 pos=transform.position;
            if(lavaT>0f)lavaT-=dt;
            if(lavaT<=0f)
            {
                BlockId feet=world.GetBlock(Mathf.FloorToInt(pos.x),Mathf.FloorToInt(pos.y+.3f),Mathf.FloorToInt(pos.z));
                if(BlockRegistry.IsLava(feet)||feet==BlockId.Fire){lavaT=.7f;Damage(BlockRegistry.IsLava(feet)?4f:1f,null,false,false);if(dead)return;}
            }

            float dist=DistanceToPlayer();
            float far=Mathf.Min(128f,Mathf.Clamp(world.RenderDistance*16f-16f,44f,128f)+24f);
            if(!persist&&(Def.Hostile||Def.Despawn)&&dist>far){Kill(false,false);return;}
            if(sourcePassiveExcess>0&&!persist&&!Def.Hostile&&!Def.Aquatic&&dist>far){sourcePassiveExcess--;Kill(false,false);return;}
            if(!persist&&(Def.Hostile||Def.Despawn)&&dist>32f&&Rand()<dt/25f){Kill(false,false);return;}

            if(!world.IsChunkLoadedAt(Mathf.FloorToInt(pos.x),Mathf.FloorToInt(pos.z)))
            {
                if(MobSpawner.Instance!=null&&MobSpawner.Instance.StoreMob(this))return;
                velocity=Vector3.zero;wish=Vector3.zero;return;
            }

            if(Def.Aquatic)
            {
                ThinkAquatic(dt,dist);
                if(!dead&&transform.position.y<VoxelConstants.MinY-8f)Kill(false,false);
                return;
            }

            if(angryT>0)angryT-=dt;
            if(Def.Teleports&&tpT>0)tpT-=dt;
            if(Def.Teleports)UpdateEndermanNeutral(dt,dist);
            if(dead)return;
            pos=transform.position;

            // main.js bp(): the entire land-AI/movement branch is inside !(burns && day && sky>=14).
            // While a zombie/skeleton is exposed to direct sun it only advances burnT / takes periodic damage;
            // chase, wander, growl and Dp() movement are skipped for that source tick. Source uses def.h here,
            // not baby-scaled G1(), so keep the skylight probe at the full definition height.
            if(Def.Burns&&IsDay()&&SkyLightAt(Mathf.FloorToInt(pos.x),Mathf.FloorToInt(pos.y+Def.Height),Mathf.FloorToInt(pos.z))>=14)
            {
                burnT+=dt;if(burnT>=1.2f){burnT=0f;Damage(2f,null,false,false);if(dead)return;}
                return;
            }

            if(Kind==MobKind.Chicken&&!baby)
            {
                eggT-=dt;if(eggT<=0f){eggT=300f+Rand()*300f;MainTransientRenderer.SpawnDroppedItem(pos.x,pos.y+.3f,pos.z,MobItemId.Egg,1);}
            }
            if(Kind==MobKind.Zombie)
            {
                growlT-=dt;if(growlT<=0f)growlT=3.5f+Rand()*5f; // source random cadence; audio bridge is separate
            }

            ThinkLand(dt,dist);
            if(!dead&&transform.position.y<VoxelConstants.MinY-8f)Kill(false,false);
        }

        bool SurvivalPlayerActive()=>playerController!=null&&!playerController.IsCreative&&!playerController.Dead;
        bool IsDay()=>Mathf.Sin(DayNight.DayTime01*Mathf.PI*2f)>.05f;
        bool IsNight()=>Mathf.Sin(DayNight.DayTime01*Mathf.PI*2f)<-.17f;
        int SkyLightAt(int x,int y,int z)=>world.GetPackedLight(x,y,z)>>4;
        int CombinedNeutralLight(int x,int y,int z){byte p=world.GetPackedLight(x,y,z);return Mathf.Max(p&15,IsNight()?0:p>>4);}
        bool IsSwimmable(float x,float y,float z){BlockId b=world.GetBlock(Mathf.FloorToInt(x),Mathf.FloorToInt(y),Mathf.FloorToInt(z));return b==BlockId.Water||BlockRegistry.IsAquatic(b);}
        float DistanceToPlayer()
        {
            Vector3 p=transform.position;float my=Def.Aquatic?p.y+.2f:p.y+BodyHeight*.5f;Vector3 q=sourcePlayerPosition+Vector3.up*.9f;float dx=q.x-p.x,dy=q.y-my,dz=q.z-p.z;return Mathf.Sqrt(dx*dx+dy*dy+dz*dz);
        }

        void UpdateEndermanNeutral(float dt,float dist)
        {
            if(Def.HatesWater)
            {
                Vector3 p=transform.position;if(IsSwimmable(p.x,p.y+Def.Height*.5f,p.z))
                {
                    dryT-=dt;if(dryT<=0){dryT=.5f;Damage(1f,null,false,false);if(dead)return;}
                    if(tpT<=0&&TeleportAround(p,24f)){tpT=1f;}
                }
            }
            if(angryT<=0&&sourcePlayerSurvivalActive)
            {
                if(PlayerStaring(dist)){stareT+=dt;if(stareT>.25f){angryT=30f;stareT=0;}}
                else stareT=0;
            }
        }

        bool PlayerStaring(float dist)
        {
            if(playerController==null||playerController.EndermanPumpkinVision||dist>64f||dist<.5f)return false;
            Vector3 eye=playerController.AimOrigin,dir=playerController.AimDirection.normalized;
            Vector3 to=transform.position+Vector3.up*(BodyHeight*.9f)-eye;float len=to.magnitude;if(len<.001f)return true;to/=len;
            float r=BodyWidth*1.2f/len;float threshold=Mathf.Min(.999f,1f-r*r*.5f);if(Vector3.Dot(to,dir)<=threshold)return false;
            return HasLineOfSight(eye,transform.position+Vector3.up*(BodyHeight*.9f),.4f);
        }

        bool HasLineOfSight(Vector3 a,Vector3 b,float step)
        {
            // main.js Np(): sample <=160 points every ~0.4 block and only Ae(block)&&!ii(block)
            // blocks the stare ray. Glass/glassy blocks therefore remain transparent to this test.
            Vector3 d=b-a;float len=d.magnitude;int n=Mathf.Min(160,Mathf.CeilToInt(len/Mathf.Max(.1f,step)));
            for(int i=1;i<n;i++)
            {
                Vector3 p=a+d*(i/(float)n);int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
                if(SourcePointCollision.BlocksMobSight(world,x,y,z))return false;
            }
            return true;
        }

        void ThinkAquatic(float dt,float dist)
        {
            Vector3 pos=transform.position;
            if(!InWater)
            {
                dryT+=dt;if(dryT>=1f){dryT-=1f;Damage(1f,null,false,false);if(dead)return;}
                hopT-=dt;if(onGround&&hopT<=0){hopT=.5f+Rand()*.5f;float a=Rand()*Mathf.PI*2;velocity.y=2.6f;velocity.x=Mathf.Cos(a)*.7f;velocity.z=Mathf.Sin(a)*.7f;yaw=a;ApplyYaw();}
                wish=Vector3.zero;MoveAquatic(dt);return;
            }
            dryT=0;if(fleeT>0)fleeT-=dt;swimT-=dt;
            if(Def.Hunter)
            {
                // main.js yp()/Mp(): prey is cached for .4 s. If "player" was selected, the shark
                // keeps that target until the next hunt refresh even if the player leaves water meanwhile.
                attackT+=dt;huntT-=dt;if(huntT<=0){huntT=.4f;AcquireAquaticPrey(dist);}
                if(!preyPlayer&&prey!=null&&(prey.dead||!Active.Contains(prey)))prey=null;
                if(preyPlayer||prey!=null)
                {
                    bool playerTarget=preyPlayer;Vector3 pp=playerTarget?sourcePlayerPosition+Vector3.up*.4f:prey.transform.position;
                    Vector3 d=pp-pos;float len=d.magnitude;if(len<.001f)len=1;Vector3 nd=d/len;
                    wish=new Vector3(nd.x*Def.Speed,nd.y*Def.Speed*.6f,nd.z*Def.Speed);fleeing=false;swimT=.5f;
                    if(len<BodyWidth+(playerTarget?1.1f:.9f)&&attackT>1.2f)
                    {attackT=0;if(playerTarget)DamagePlayer(Def.Melee);else prey.Damage(Def.Melee,null,false,true);}
                    if(!IsSwimmable(pos.x,pos.y+.9f,pos.z))wish.y=Mathf.Min(wish.y,-.15f);
                    MoveAquatic(dt);return;
                }
            }

            if(!Def.Hunter&&(dist<8f||fleeT>0))UpdateFishEscape(pos,dt);
            else if(fleeing||hasEscapeTarget){fleeing=false;hasEscapeTarget=false;escT=0;escDX=escDZ=0;swimT=0;}

            if(!fleeing&&swimT<=0)
            {
                escDX=escDZ=0;swimT=2.5f+Rand()*2.5f;
                if(Rand()<.35f)
                {
                    float h=new Vector2(wish.x,wish.z).magnitude;float a=h>.01f?Mathf.Atan2(wish.x,wish.z):Rand()*Mathf.PI*2;
                    wish=new Vector3(Mathf.Sin(a)*Def.Speed*.2f,0,Mathf.Cos(a)*Def.Speed*.2f);
                }
                else
                {
                    float a=Rand()*Mathf.PI*2;wish=new Vector3(Mathf.Cos(a)*Def.Speed,(Rand()-.5f)*Def.Speed*.2f,Mathf.Sin(a)*Def.Speed);
                    ApplySchooling(ref wish);
                }
            }
            if(IsSwimmable(pos.x,pos.y+.9f,pos.z)){if(!IsSwimmable(pos.x,pos.y-.6f,pos.z))wish.y=Mathf.Max(wish.y,.15f);}
            else wish.y=Mathf.Min(wish.y,-.15f);
            MoveAquatic(dt);
        }

        bool CanHuntPlayer(float dist)=>sourcePlayerSurvivalActive&&sourcePlayerInWater&&dist<Def.Detect;

        void AcquireAquaticPrey(float playerDist)
        {
            // main.js Mp(): player has priority when alive/survival/in water and within detect range;
            // otherwise choose the nearest living non-hunter aquatic mob.
            prey=null;preyPlayer=false;
            if(CanHuntPlayer(playerDist)){preyPlayer=true;return;}
            float dBest=Def.Detect;
            for(int i=0;i<Active.Count;i++)
            {
                MobAI m=Active[i];if(m==null||m==this||m.dead||!m.Def.Aquatic||m.Def.Hunter||m.HP<=0)continue;
                float d=Vector3.Distance(transform.position,m.transform.position);if(d<dBest){dBest=d;prey=m;}
            }
        }

        void UpdateFishEscape(Vector3 pos,float dt)
        {
            // main.js yp(): keep an 8 s escape target, but invalidate it if it stopped increasing
            // horizontal distance from the player or now points back toward the threat.
            escT-=dt;
            bool refresh=!hasEscapeTarget||escT<=0f||Vector2.Distance(new Vector2(escX,escZ),new Vector2(pos.x,pos.z))<1.5f;
            if(!refresh&&hasEscapeTarget)
            {
                Vector2 away=new Vector2(pos.x-sourcePlayerPosition.x,pos.z-sourcePlayerPosition.z);float awayLen=away.magnitude;if(awayLen<.001f)awayLen=1f;away/=awayLen;
                float currentDist=Vector2.Distance(new Vector2(pos.x,pos.z),new Vector2(sourcePlayerPosition.x,sourcePlayerPosition.z));
                float targetDist=Vector2.Distance(new Vector2(escX,escZ),new Vector2(sourcePlayerPosition.x,sourcePlayerPosition.z));
                Vector2 toward=new Vector2(escX-pos.x,escZ-pos.z);float towardLen=toward.magnitude;if(towardLen<.001f)towardLen=1f;toward/=towardLen;
                if(targetDist<currentDist||Vector2.Dot(toward,away)<-.15f)refresh=true;
            }
            if(refresh)
            {
                Vector3? p=FindWaterEscape(pos,sourcePlayerPosition);
                if(p.HasValue)
                {
                    Vector3 e=p.Value;Vector2 d=new Vector2(e.x-pos.x,e.z-pos.z);float len=d.magnitude;if(len<.001f)len=1f;d/=len;
                    escDX=d.x;escDZ=d.y;escX=e.x;escY=e.y;escZ=e.z;escT=8f;hasEscapeTarget=true;
                }
                else{hasEscapeTarget=false;escT=.6f;}
            }
            if(hasEscapeTarget)
            {
                float speed=Def.Speed*(fleeT>0?3.8f:3.4f);Vector3 d=new Vector3(escX-pos.x,escY-pos.y,escZ-pos.z);float len=d.magnitude;
                if(len>.001f){d/=len;wish=new Vector3(d.x*speed,d.y*speed*.5f,d.z*speed);fleeing=true;swimT=.5f;return;}
            }
            fleeing=false;
        }

        Vector3? FindWaterEscape(Vector3 pos,Vector3 threat)
        {
            float old=Vector2.Distance(new Vector2(pos.x,pos.z),new Vector2(threat.x,threat.z));Vector3 best=Vector3.zero;float score=0;
            for(int pass=0;pass<2;pass++)
            {
                float lo=pass==0?6f:2f,hi=pass==0?16f:6f;
                for(int i=0;i<12;i++)
                {
                    float a=Rand()*Mathf.PI*2,r=lo+Rand()*(hi-lo);Vector3 q=new Vector3(pos.x+Mathf.Cos(a)*r,pos.y+(Rand()-.5f)*4f,pos.z+Mathf.Sin(a)*r);
                    if(!IsSwimmable(q.x,q.y,q.z))continue;float gain=Vector2.Distance(new Vector2(q.x,q.z),new Vector2(threat.x,threat.z))-old;if(gain<=0)continue;
                    Vector2 moveAway=new Vector2(q.x-pos.x,q.z-pos.z);float moveLen=moveAway.magnitude;if(moveLen<.001f)continue;moveAway/=moveLen;
                    Vector2 awayThreat=new Vector2(pos.x-threat.x,pos.z-threat.z);float awayLen=awayThreat.magnitude;if(awayLen<.001f)awayLen=1f;awayThreat/=awayLen;
                    if(Vector2.Dot(moveAway,awayThreat)<-.15f||!WaterLine(new Vector3(pos.x,pos.y+.2f,pos.z),q))continue;int near=(IsSwimmable(q.x+2,q.y,q.z)?1:0)+(IsSwimmable(q.x-2,q.y,q.z)?1:0)+(IsSwimmable(q.x,q.y,q.z+2)?1:0)+(IsSwimmable(q.x,q.y,q.z-2)?1:0);
                    Vector2 move=new Vector2(q.x-pos.x,q.z-pos.z);float dir=move.sqrMagnitude>.001f?Vector2.Dot(move.normalized,new Vector2(escDX,escDZ)):0;float s=gain+near*1.5f+dir*4f;if(s>score){score=s;best=q;}
                }
                if(score>0)break;
            }
            if(score>0)return best;
            // main.js Yp() fallback: scan 16 radial water corridors up to 8 blocks.
            Vector3 fallback=Vector3.zero;float fallbackScore=0f;Vector2 awayNow=new Vector2(pos.x-threat.x,pos.z-threat.z);if(awayNow.sqrMagnitude>.001f)awayNow.Normalize();
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8f;Vector2 dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));float reach=0f;for(int step=1;step<=8&&IsSwimmable(pos.x+dir.x*step,pos.y+.2f,pos.z+dir.y*step);step++)reach=step;if(reach<=0||Vector2.Dot(dir,awayNow)<-.15f)continue;Vector3 q=new Vector3(pos.x+dir.x*reach,pos.y,pos.z+dir.y*reach);float s=reach+(Vector2.Distance(new Vector2(q.x,q.z),new Vector2(threat.x,threat.z))-old)*2f+Vector2.Dot(dir,new Vector2(escDX,escDZ))*4f;if(s>fallbackScore){fallbackScore=s;fallback=q;}}
            return fallbackScore>0?fallback:(Vector3?)null;
        }

        bool WaterLine(Vector3 a,Vector3 b)
        {
            float len=Vector3.Distance(a,b);int n=Mathf.Max(2,Mathf.CeilToInt(len/.8f));for(int i=1;i<=n;i++){Vector3 p=Vector3.Lerp(a,b,i/(float)n);if(!IsSwimmable(p.x,p.y,p.z))return false;}return true;
        }

        void ApplySchooling(ref Vector3 w)
        {
            float sx=0,sz=0,sepx=0,sepz=0;int count=0;Vector3 p=transform.position;
            for(int i=0;i<Active.Count;i++)
            {
                MobAI m=Active[i];if(m==null||m==this||m.dead||m.Kind!=Kind)continue;Vector3 q=m.transform.position;float d=Vector3.Distance(p,q);if(d>8||d<.001f)continue;
                sx+=q.x;sz+=q.z;count++;if(d<1.8f){sepx+=(p.x-q.x)/d;sepz+=(p.z-q.z)/d;}
            }
            if(count>0){Vector2 coh=new Vector2(sx/count-p.x,sz/count-p.z);if(coh.magnitude>4f){coh.Normalize();w.x+=coh.x*Def.Speed*.4f;w.z+=coh.y*Def.Speed*.4f;}}
            Vector2 sep=new Vector2(sepx,sepz);if(sep.sqrMagnitude>0){sep.Normalize();w.x+=sep.x*Def.Speed*.8f;w.z+=sep.y*Def.Speed*.8f;}
        }

        void MoveAquatic(float dt)
        {
            // main.js pf(): aquatic entities first try vertical recovery if their current body overlaps
            // collision, then recompute water state before applying swimming/air physics.
            AquaticUnstuck();
            InWater=IsSwimmable(transform.position.x,transform.position.y+.05f,transform.position.z);
            if(InWater)
            {
                float accel=fleeing?10f:4f;
                velocity.x+=(wish.x-velocity.x)*Mathf.Min(1,dt*accel);
                velocity.y+=(wish.y-velocity.y)*Mathf.Min(1,dt*accel);
                velocity.z+=(wish.z-velocity.z)*Mathf.Min(1,dt*accel);
            }
            else
            {
                velocity.y-=23f*dt;velocity.x*=Mathf.Max(0,1-dt*4);velocity.z*=Mathf.Max(0,1-dt*4);
            }
            float speed=velocity.magnitude;
            if(speed>.15f)
            {
                tgtYaw=Mathf.Atan2(velocity.x,velocity.z);
                float targetPitch=-Mathf.Atan2(velocity.y,Mathf.Max(.001f,new Vector2(velocity.x,velocity.z).magnitude));pitch+=(targetPitch-pitch)*Mathf.Min(1,dt*4);
            }
            ApproachYaw(dt*4f);
            Vector3 p=transform.position;bool requireWater=InWater;
            Vector3 q=new Vector3(p.x+velocity.x*dt,p.y,p.z);
            if(CanAquatic(q,requireWater))p=q;else{velocity.x=0;wish.x=-wish.x;}
            q=new Vector3(p.x,p.y,p.z+velocity.z*dt);
            if(CanAquatic(q,requireWater))p=q;else{velocity.z=0;wish.z=-wish.z;}
            float nextY=p.y+velocity.y*dt;q=new Vector3(p.x,nextY,p.z);
            if(CanAquatic(q,requireWater)){p=q;onGround=false;}
            else if(velocity.y<=0){p.y=SourceFloorY(nextY,p.x,p.z);velocity.y=0;onGround=true;}
            else velocity.y=0;
            transform.position=p;moving=true;walkPhase+=dt*(.6f+speed*.5f);
        }

        void AquaticUnstuck()
        {
            if(CanOccupy(transform.position,true))return;
            float baseY=Mathf.Floor(transform.position.y);
            for(int i=0;i<=6;i++)
            {
                float off=i<=3?i:3-i;float y=baseY+off+.3f;
                Vector3 q=new Vector3(transform.position.x,y,transform.position.z);
                if(CanOccupy(q,true)&&IsSwimmable(q.x,y+Def.Height*.5f,q.z)){transform.position=q;break;}
            }
            velocity=Vector3.zero;
        }

        bool CanAquatic(Vector3 p,bool requireWater)
        {
            return CanOccupy(p,true)&&(!requireWater||IsSwimmable(p.x,p.y+.05f,p.z));
        }

        void ThinkLand(float dt,float dist)
        {
            Vector3 pos=transform.position;bool playerActive=sourcePlayerSurvivalActive;bool aggressive=Def.Hostile&&playerActive&&dist<Def.Detect&&IsAggressiveByState();
            
            if(detourT>0&&!(Def.Creeper&&fuse>=0))
            {
                detourT-=dt;if(fleeT>0)fleeT-=dt;MoveToward(detX,detZ,Def.Speed*(fleeT>0?2f:1f));
            }
            else if(fleeT>0)
            {
                fleeT-=dt;Vector3 away=pos+(pos-sourcePlayerPosition);MoveToward(away.x,away.z,Def.Speed*2f);
            }
            else if(loveT>0f&&HandleLove(pos,dt))
            {
                // main.js love state temporarily owns movement until the pair meets or loveT expires.
            }
            else if(Def.Creeper&&fuse>=0)
            {
                fuse+=dt;StopHorizontal();if(dist>5f)fuse=-1f;else if(fuse>1.5f){ExplodeCreeper();return;}
            }
            else if(aggressive)
            {
                ThinkHostile(dt,dist,pos);
            }
            else ThinkWander(dt,pos);

            if(onGround&&WouldWalkOffLedge())StartDetour();
            Vector3 currentPos=transform.position;
            if((!Def.Hostile||Def.HatesWater)&&!IsSwimmable(currentPos.x,currentPos.y+.3f,currentPos.z)&&WaterAhead()){StopHorizontal();detourT=0;wanderX=currentPos.x;wanderZ=currentPos.z;aiT=Mathf.Min(aiT,.5f);}

            float intended=new Vector2(wish.x,wish.z).magnitude;Vector3 before=transform.position;bool beforeGround=onGround;
            MoveLand(dt);
            if(Def.Jumper){if(!beforeGround&&onGround&&squashY>.95f)squashY=.72f;squashY+=(1f-squashY)*Mathf.Min(1,dt*6f);}
            if(intended>.5f)
            {
                float moved=Vector2.Distance(new Vector2(before.x,before.z),new Vector2(transform.position.x,transform.position.z));
                if(moved<intended*dt*.3f)stuckT+=dt;else stuckT=Mathf.Max(0,stuckT-dt*2);
                if(stuckT>.5f&&detourT<=0){stuckT=0;detourT=.7f;float sign=Rand()<.5f?1:-1;float m=Mathf.Max(.001f,intended);detX=transform.position.x+wish.z/m*5*sign;detZ=transform.position.z-wish.x/m*5*sign;}
            }else stuckT=0;
        }

        bool IsAggressiveByState()
        {
            if(angryT>0)return true;if(Def.NeutralLight>0){Vector3 p=transform.position;return CombinedNeutralLight(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+.5f),Mathf.FloorToInt(p.z))<Def.NeutralLight;}
            return !Def.Neutral;
        }

        void ThinkHostile(float dt,float dist,Vector3 pos)
        {
            if(Def.Leaper)
            {
                PathToPlayer(Def.Speed,dt);leapT-=dt;if(onGround&&leapT<=0&&dist>2&&dist<8){leapT=1.5f+Rand()*1.5f;Vector3 d=sourcePlayerPosition-pos;d.y=0;if(d.sqrMagnitude>.001f){d.Normalize();velocity.x=d.x*7;velocity.z=d.z*7;velocity.y=6.5f;kbT=.45f;}}
                attackT+=dt;if(dist<1.6f&&Mathf.Abs(sourcePlayerPosition.y-pos.y)<2&&attackT>1f){attackT=0;DamagePlayer(Def.Melee);}return;
            }
            if(Def.Jumper)
            {
                hopT-=dt;if(onGround){if(hopT<=0){hopT=.6f+Rand()*.6f;Vector3 d=sourcePlayerPosition-pos;d.y=0;float len=d.magnitude;if(len<.001f)len=1;d/=len;tgtYaw=Mathf.Atan2(d.x,d.z);wish=d*Def.Speed;velocity.x=wish.x;velocity.z=wish.z;velocity.y=7.4f;kbT=.4f;squashY=1.25f;}else StopHorizontal();}
                attackT+=dt;if(Def.Melee>0&&dist<BodyWidth+1.1f&&Mathf.Abs(sourcePlayerPosition.y-pos.y)<BodyHeight+.6f&&attackT>.5f){attackT=0;DamagePlayer(Def.Melee);}return;
            }
            if(Def.Teleports)
            {
                if(dist>3)PathToPlayer(Def.Speed*1.25f,dt);else{StopHorizontal();FacePlayer();}
                if(tpT<=0&&dist>6&&dist<32){tpT=2+Rand()*2;float a=Rand()*Mathf.PI*2;TeleportNear(new Vector3(sourcePlayerPosition.x+Mathf.Cos(a)*3,sourcePlayerPosition.y,sourcePlayerPosition.z+Mathf.Sin(a)*3),2f);}
                attackT+=dt;if(dist<2.2f&&Mathf.Abs(sourcePlayerPosition.y-transform.position.y)<3&&attackT>1){attackT=0;DamagePlayer(Def.Melee);}return;
            }
            if(Def.Ranged)
            {
                if(dist>9)PathToPlayer(Def.Speed,dt);else if(dist<5){Vector3 away=pos+(pos-sourcePlayerPosition);MoveToward(away.x,away.z,Def.Speed);}else{StopHorizontal();FacePlayer();}
                shootT+=dt;if(shootT>2&&dist<12){shootT=0;Vector3 origin=SkeletonArrowOrigin();Vector3 tgt=sourcePlayerPosition+Vector3.up*1.2f;MobProjectileSystem.SpawnArrow(world,playerController,origin,tgt,this);}return;
            }
            if(Def.Creeper)
            {
                PathToPlayer(Def.Speed,dt);if(dist<2.2f)fuse=0;return;
            }
            PathToPlayer(Def.Speed,dt);attackT+=dt;if(dist<1.4f&&Mathf.Abs(sourcePlayerPosition.y-pos.y)<2&&attackT>1){attackT=0;DamagePlayer(Def.Melee);}
        }


        Vector3 SkeletonArrowOrigin()
        {
            float moving=new Vector2(velocity.x,velocity.z).magnitude>.3f?1f:0f;float walk=moving>0?Mathf.Sin(walkPhase*3f)*.6f:0f;float arm=-1.25f+walk*.08f;
            float d=13f-24f,h=d*Mathf.Cos(arm)+24f,side=d*Mathf.Sin(arm),sourceYaw=yaw;float cy=Mathf.Cos(sourceYaw),sy=Mathf.Sin(sourceYaw);
            float rx=5f*cy+side*sy,rz=-5f*sy+side*cy;return transform.position+new Vector3(rx/16f+sy*.1f,h/16f,rz/16f+cy*.1f);
        }

        void ThinkWander(float dt,Vector3 pos)
        {
            aiT-=dt;if(aiT<=0){aiT=3f+Rand()*5f;if(Rand()<.6f){wanderX=pos.x+(Rand()-.5f)*12f;wanderZ=pos.z+(Rand()-.5f)*12f;}else{wanderX=pos.x;wanderZ=pos.z;}}
            if(Def.Jumper)
            {
                hopT-=dt;if(onGround){if(hopT<=0){hopT=.8f+Rand()*1.2f;float dx=wanderX-pos.x,dz=wanderZ-pos.z,len=Mathf.Sqrt(dx*dx+dz*dz);if(len>.5f){tgtYaw=Mathf.Atan2(dx,dz);wish.x=dx/len*Def.Speed*.8f;wish.z=dz/len*Def.Speed*.8f;velocity.x=wish.x;velocity.z=wish.z;velocity.y=7.4f;kbT=.4f;squashY=1.25f;}}else StopHorizontal();}
            }
            else if(wanderX!=pos.x||wanderZ!=pos.z)
            {
                if(PathToward(wanderX,wanderZ,Def.Speed*.85f,dt)){wanderX=pos.x;wanderZ=pos.z;}
            }
            else StopHorizontal();
        }

        bool HandleLove(Vector3 pos,float dt)
        {
            // main.js decrements loveT after entering the love branch, then still performs this tick's mate search.
            loveT-=dt;
            MobAI mate=null;float best=12f;
            for(int i=0;i<Active.Count;i++)
            {
                MobAI m=Active[i];if(m==null||m==this||m.dead||m.Kind!=Kind||m.baby||m.loveT<=0f)continue;
                float d=Vector2.Distance(new Vector2(pos.x,pos.z),new Vector2(m.transform.position.x,m.transform.position.z));if(d<best){best=d;mate=m;}
            }
            if(mate==null){StopHorizontal();return true;}
            MoveToward(mate.transform.position.x,mate.transform.position.z,Def.Speed);
            if(best<1.4f)
            {
                loveT=0f;mate.loveT=0f;breedCd=120f;mate.breedCd=120f;
                if(MobSpawner.Instance!=null)
                {
                    // Source order is important for the shared random stream: fa() fully initializes
                    // the child first (including the sheep up() roll), then the inherited parent colour
                    // consumes its own Math.random(). Do not choose the parent before SpawnBaby().
                    Vector3 a=transform.position,b=mate.transform.position;Vector3 born=new Vector3((a.x+b.x)*.5f,Mathf.Max(a.y,b.y),(a.z+b.z)*.5f);
                    MobSpawner.Instance.SpawnBaby(Kind,born,sheepColor,mate.sheepColor);
                    MainTransientRenderer.SpawnSourceParticles(Mathf.FloorToInt(a.x),Mathf.FloorToInt(a.y+1f),Mathf.FloorToInt(a.z),AtlasLayout.RedWool,8);
                }
            }
            return true;
        }

        public bool TryInteract(MainPlayerController pc)
        {
            if(dead||pc==null)return false;MobItemId held=MobItemCatalog.FromTextureKey(pc.FirstPersonPrimaryItemTexture);
            if(Kind==MobKind.Sheep&&held==MobItemId.Shears&&!baby&&!sheared)
            {
                sheared=true;woolT=90f+Rand()*60f;UpdateVisualState();
                MainTransientRenderer.SpawnDroppedItem(transform.position.x,transform.position.y+.6f,transform.position.z,MobItemCatalog.WoolFor(sheepColor),1+RandInt(3));
                if(!pc.IsCreative)pc.DamageSelectedInventoryItem(1);
                return true;
            }
            MobItemId feed=MobItemCatalog.FeedFor(Kind);
            if(feed!=MobItemId.None&&held==feed&&!baby&&loveT<=0f&&breedCd<=0f)
            {
                if(!pc.IsCreative&&!pc.ConsumeSelectedInventory(1))return false;
                loveT=30f;persist=true;
                Vector3 p=transform.position;MainTransientRenderer.SpawnSourceParticles(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+1f),Mathf.FloorToInt(p.z),AtlasLayout.RedWool,5);
                return true;
            }
            return false;
        }

        public void MakeBaby(MobSheepColor color)
        {
            baby=true;growT=120f;persist=true;if(Kind==MobKind.Sheep)sheepColor=color;UpdateVisualState();
        }

        void PathToPlayer(float speed,float dt){PathToward(sourcePlayerPosition.x,sourcePlayerPosition.z,speed,dt);}
        bool PathToward(float x,float z,float speed,float dt)
        {
            // Literal main.js Ki(): age/rebuild the cached A* path first, even if No() will
            // immediately stop because the final target is already within 0.4 blocks.
            pathT-=dt;
            bool changed=Mathf.Abs(x-pathTx)+Mathf.Abs(z-pathTz)>2f;
            if(path==null||path.Count==0||pathT<=0f||changed)
            {
                pathT=.6f;pathTx=x;pathTz=z;BuildPath(transform.position,x,z);pathI=0;
            }
            if(path==null||pathI>=path.Count)return MoveToward(x,z,speed);
            Vector3 wp=path[pathI];
            if(Vector2.Distance(new Vector2(wp.x,wp.z),new Vector2(transform.position.x,transform.position.z))<.55f)
            {
                pathI++;if(pathI>=path.Count)return MoveToward(x,z,speed);wp=path[pathI];
            }
            MoveToward(wp.x,wp.z,speed);return false;
        }
        bool MoveToward(float x,float z,float speed)
        {
            Vector3 d=new Vector3(x-transform.position.x,0,z-transform.position.z);float len=d.magnitude;if(len<.4f){StopHorizontal();return true;}wish=d/len*speed;return false;
        }
        void StopHorizontal(){wish.x=wish.z=0;}
        void FacePlayer(){Vector3 d=sourcePlayerPosition-transform.position;tgtYaw=Mathf.Atan2(d.x,d.z);}

        void StartDetour()
        {
            float m=new Vector2(wish.x,wish.z).magnitude;if(m<.001f)return;float sign=Rand()<.5f?1:-1;detourT=.5f;detX=transform.position.x+wish.z/m*3*sign;detZ=transform.position.z-wish.x/m*3*sign;path?.Clear();stuckT=0;StopHorizontal();
        }

        bool WouldWalkOffLedge()
        {
            // main.js Gp(): test the collision point at the projected cell center, then up to td=3 below it.
            float m=new Vector2(wish.x,wish.z).magnitude;if(m<.2f)return false;
            int x=Mathf.FloorToInt(transform.position.x+wish.x/m*.8f),z=Mathf.FloorToInt(transform.position.z+wish.z/m*.8f),y=Mathf.FloorToInt(transform.position.y+.1f);
            if(SourcePointCollision.Contains(world,new Vector3(x+.5f,y-.2f,z+.5f)))return false;
            for(int d=1;d<=3;d++)if(SourcePointCollision.Contains(world,new Vector3(x+.5f,y-d-.2f,z+.5f)))return false;
            return true;
        }

        bool WaterAhead()
        {
            // main.js Vp(): water/aquatic in the forward cell, or an AIR cell with water/aquatic one/two below.
            float m=new Vector2(wish.x,wish.z).magnitude;if(m<.2f)return false;
            int x=Mathf.FloorToInt(transform.position.x+wish.x/m*.9f),z=Mathf.FloorToInt(transform.position.z+wish.z/m*.9f),y=Mathf.FloorToInt(transform.position.y+.3f);
            BlockId here=world.GetBlock(x,y,z);return SourceWaterLike(here)||(here==BlockId.Air&&(SourceWaterLike(world.GetBlock(x,y-1,z))||SourceWaterLike(world.GetBlock(x,y-2,z))));
        }
        static bool SourceWaterLike(BlockId id)=>id==BlockId.Water||BlockRegistry.IsAquatic(id);

        struct PathNode{public int x,y,z,from;public float g,f;public bool done;}


        void BuildPath(Vector3 start,float gx,float gz)
        {
            if(path==null)path=new List<Vector3>(64);else path.Clear();
            int sx=Mathf.FloorToInt(start.x),sy=Mathf.FloorToInt(start.y+.1f),sz=Mathf.FloorToInt(start.z),tx=Mathf.FloorToInt(gx),tz=Mathf.FloorToInt(gz);if(sx==tx&&sz==tz)return;
            var map=pathMapScratch??(pathMapScratch=new Dictionary<long,int>(384));map.Clear();
            var clearCache=pathClearScratch??(pathClearScratch=new Dictionary<long,bool>(768));clearCache.Clear();
            var standCache=pathStandScratch??(pathStandScratch=new Dictionary<long,bool>(1024));standCache.Clear();
            var nodes=pathNodesScratch??(pathNodesScratch=new List<PathNode>(384));nodes.Clear();
            var open=pathOpenScratch??(pathOpenScratch=new List<int>(128));open.Clear();
            var rev=pathReverseScratch??(pathReverseScratch=new List<Vector3>(64));rev.Clear();
            var first=new PathNode{x=sx,y=sy,z=sz,g=0,f=PathHeuristic(sx-tx,sz-tz),from=-1};nodes.Add(first);map[PathXZKey(sx,sz)]=0;HeapPush(open,nodes,0);int best=0;float bestH=first.f;int expanded=0;
            while(open.Count>0&&expanded<320)
            {
                int curIndex=HeapPop(open,nodes);PathNode cur=nodes[curIndex];if(cur.done)continue;cur.done=true;nodes[curIndex]=cur;expanded++;if(cur.x==tx&&cur.z==tz){best=curIndex;break;}
                for(int i=0;i<8;i++)
                {
                    int nx=cur.x+PathDX[i],nz=cur.z+PathDZ[i];if(Mathf.Abs(nx-sx)>22||Mathf.Abs(nz-sz)>22)continue;if(!Standable(nx,nz,cur.y,out int ny))continue;bool diag=PathDX[i]!=0&&PathDZ[i]!=0;
                    if(diag&&(ny!=cur.y||!PathClearCached(cur.x+PathDX[i],cur.y,cur.z)||!PathClearCached(cur.x,cur.y,cur.z+PathDZ[i])))continue;
                    float ng=cur.g+(diag?1.41421356f:1f);long key=PathXZKey(nx,nz);
                    if(map.TryGetValue(key,out int ni)){PathNode n=nodes[ni];if(n.done||ng>=n.g)continue;n.g=ng;n.y=ny;n.from=curIndex;n.f=ng+PathHeuristic(nx-tx,nz-tz);nodes[ni]=n;HeapPush(open,nodes,ni);}
                    else{float h=PathHeuristic(nx-tx,nz-tz);var n=new PathNode{x=nx,y=ny,z=nz,g=ng,f=ng+h,from=curIndex};int newIndex=nodes.Count;nodes.Add(n);map[key]=newIndex;HeapPush(open,nodes,newIndex);if(h<bestH){bestH=h;best=newIndex;}}
                }
            }
            if(best==0)return;for(int ni=best;ni>=0;){PathNode n=nodes[ni];if(n.from<0)break;rev.Add(new Vector3(n.x+.5f,n.y,n.z+.5f));ni=n.from;}for(int i=rev.Count-1;i>=0;i--)path.Add(rev[i]);
        }
        static long PathXZKey(int x,int z)=>((long)(uint)x<<32)|(uint)z;
        static long PathXYZKey(int x,int y,int z)=>((long)(uint)x<<32)|((long)(ushort)y<<16)|(ushort)z;
        bool PathClear(int x,int y,int z)=>!SourcePointCollision.Contains(world,new Vector3(x+.5f,y+.4f,z+.5f))&&!SourcePointCollision.Contains(world,new Vector3(x+.5f,y+1.4f,z+.5f));
        bool PathClearCached(int x,int y,int z)
        {
            long k=PathXYZKey(x,y,z);var cache=pathClearScratch;
            if(cache!=null&&cache.TryGetValue(k,out bool value))return value;
            value=PathClear(x,y,z);if(cache!=null)cache[k]=value;return value;
        }
        bool IsStandCellCached(int x,int y,int z)
        {
            long k=PathXYZKey(x,y,z);var cache=pathStandScratch;
            if(cache!=null&&cache.TryGetValue(k,out bool value))return value;
            value=SourcePointCollision.Contains(world,new Vector3(x+.5f,y-.2f,z+.5f))&&PathClearCached(x,y,z);
            if(cache!=null)cache[k]=value;return value;
        }
        static float PathHeuristic(int dx,int dz){dx=Math.Abs(dx);dz=Math.Abs(dz);int mn=Math.Min(dx,dz);return Math.Abs(dx-dz)+1.41421356f*mn;}
        static void HeapPush(List<int> h,List<PathNode> nodes,int nodeIndex){h.Add(nodeIndex);for(int i=h.Count-1;i>0;){int p=(i-1)>>1;if(nodes[h[p]].f<=nodes[h[i]].f)break;int t=h[p];h[p]=h[i];h[i]=t;i=p;}}
        static int HeapPop(List<int> h,List<PathNode> nodes){int r=h[0],last=h[h.Count-1];h.RemoveAt(h.Count-1);if(h.Count==0)return r;h[0]=last;for(int i=0;;){int a=i*2+1,b=a+1,m=i;if(a<h.Count&&nodes[h[a]].f<nodes[h[m]].f)m=a;if(b<h.Count&&nodes[h[b]].f<nodes[h[m]].f)m=b;if(m==i)break;int t=h[m];h[m]=h[i];h[i]=t;i=m;}return r;}

        bool Standable(int x,int z,int currentY,out int y)
        {
            // main.js Lp()/Rp(): search +1 .. -3, require support at y-.2 and free points at y+.4/y+1.4.
            for(int dy=1;dy>=-3;dy--){int candidate=currentY+dy;if(IsStandCellCached(x,candidate,z)){y=candidate;return true;}}y=currentY;return false;
        }
        bool IsStandCell(int x,int y,int z)
        {
            return SourcePointCollision.Contains(world,new Vector3(x+.5f,y-.2f,z+.5f))&&PathClear(x,y,z);
        }

        float SourceFloorY(float candidateY,float x,float z)
        {
            // Static-world part of main.js rd(). Moving-ship CE() support remains a ship-system dependency.
            float r=BodyCollisionWidth,best=-1e9f;int by=Mathf.FloorToInt(candidateY);
            for(int xi=-1;xi<=1;xi+=2)for(int zi=-1;zi<=1;zi+=2)
            {
                int bx=Mathf.FloorToInt(x+xi*r),bz=Mathf.FloorToInt(z+zi*r);
                if(SourcePointCollision.OrdinarySolidCube(world,bx,by,bz)&&by+1>best)best=by+1;
            }
            return best>-1e8f?best:Mathf.Floor(candidateY)+1f;
        }

        void MoveLand(float dt)
        {
            if(jumpT>0f)jumpT-=dt;
            if(TryUnstuck(dt))return;
            BlockId medium=world.GetBlock(Mathf.FloorToInt(transform.position.x),Mathf.FloorToInt(transform.position.y+.3f),Mathf.FloorToInt(transform.position.z));bool swim=SourceWaterLike(medium);
            if(swim)velocity.y+=(2.5f-velocity.y)*Mathf.Min(1,dt*3);else{velocity.y-=23f*dt;if(velocity.y<-40)velocity.y=-40;}
            if(kbT>0f)kbT-=dt;float accel=kbT>0f?1.5f:(onGround||swim?10f:2.5f);velocity.x+=(wish.x-velocity.x)*Mathf.Min(1,dt*accel);velocity.z+=(wish.z-velocity.z)*Mathf.Min(1,dt*accel);
            if(new Vector2(wish.x,wish.z).magnitude>.2f)tgtYaw=Mathf.Atan2(wish.x,wish.z);
            ApproachYaw(dt*8f);
            Vector3 p=transform.position;bool blocked=false;float nextY=p.y+velocity.y*dt;Vector3 q=new Vector3(p.x,nextY,p.z);
            if(velocity.y<=0f?CanOccupy(q,true):CanOccupy(q,false)){p=q;onGround=false;}
            else if(velocity.y<=0f){p.y=SourceFloorY(nextY,p.x,p.z);velocity.y=0;onGround=true;}
            else{velocity.y=0;onGround=false;}
            q=new Vector3(p.x+velocity.x*dt,p.y,p.z);
            if(CanOccupy(q,false))p=q;else if(onGround&&jumpT<=0f&&CanOccupy(q+Vector3.up,false)){velocity.y=8.2f;jumpT=.5f;}else blocked=true;
            q=new Vector3(p.x,p.y,p.z+velocity.z*dt);
            if(CanOccupy(q,false))p=q;else if(onGround&&velocity.y<=0f&&jumpT<=0f&&CanOccupy(q+Vector3.up,false)){velocity.y=8.2f;jumpT=.5f;}else blocked=true;
            if(Def.Climber&&blocked&&new Vector2(wish.x,wish.z).magnitude>.3f&&!swim)
            {
                Vector3 up=p+Vector3.up*(3.4f*dt);if(CanOccupy(up,true)){p=up;velocity.y=0;onGround=false;}
            }
            transform.position=p;float hs=new Vector2(velocity.x,velocity.z).magnitude;moving=hs>.3f;if(moving)walkPhase+=dt*hs*1.6f;
        }

        bool TryUnstuck(float dt)
        {
            if(CanOccupy(transform.position,false))return false;
            for(int ri=0;ri<radii.Length;ri++){float r=radii[ri];for(int i=0;i<8;i++){float a=i*Mathf.PI/4f;Vector3 off=new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);if(CanOccupy(transform.position+off,false)){float len=Mathf.Min(r,8f*dt);transform.position+=off.normalized*len;velocity=Vector3.zero;path?.Clear();return true;}}
                Vector3 up=transform.position+Vector3.up*r;if(CanOccupy(up,false)){transform.position+=Vector3.up*Mathf.Min(r,8f*dt);velocity=Vector3.zero;path?.Clear();return true;}Vector3 dn=transform.position-Vector3.up*r;if(CanOccupy(dn,false)){transform.position-=Vector3.up*Mathf.Min(r,8f*dt);velocity=Vector3.zero;path?.Clear();return true;}}
            // main.js vp(): if no escape exists, return true so Dp() skips the rest of this tick.
            return true;
        }

        bool CanOccupy(Vector3 p,bool exactBottom=false)
        {
            // main.js gt(): four corners at three body heights, using An()/W1() point collision.
            float r=BodyCollisionWidth,h=BodyHeight;float bottom=exactBottom?0f:.05f;
            float y0=p.y+bottom,y1=p.y+h*.5f,y2=p.y+h-.1f;
            for(int xi=-1;xi<=1;xi+=2)for(int zi=-1;zi<=1;zi+=2)
            {
                float x=p.x+xi*r,z=p.z+zi*r;
                if(SourcePointCollision.Contains(world,new Vector3(x,y0,z))||SourcePointCollision.Contains(world,new Vector3(x,y1,z))||SourcePointCollision.Contains(world,new Vector3(x,y2,z)))return false;
            }
            return true;
        }

        void ApproachYaw(float maxStep)
        {
            float d=tgtYaw-yaw;while(d>Mathf.PI)d-=Mathf.PI*2f;while(d<-Mathf.PI)d+=Mathf.PI*2f;yaw+=Mathf.Clamp(d,-maxStep,maxStep);ApplyYaw();
        }
        void ApplyYaw(){transform.rotation=Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0);}

        bool TeleportAround(Vector3 center,float radius)
        {
            for(int n=0;n<32;n++)
            {
                float x=center.x+(Rand()-.5f)*2*radius,z=center.z+(Rand()-.5f)*2*radius;float fx=Mathf.Floor(x)+.5f,fz=Mathf.Floor(z)+.5f;
                for(int s=8;s>=-16;s--)
                {
                    float y=Mathf.Floor(transform.position.y)+s;if(!IsTeleportSpot(new Vector3(fx,y,fz)))continue;
                    // main.js v2(): successful Enderman teleport emits Yr(..., OBSIDIAN, 8)
                    // at the old position before moving, then again at the destination.
                    Vector3 old=transform.position;
                    MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(old.x),Mathf.FloorToInt(old.y+1f),Mathf.FloorToInt(old.z),BlockId.Obsidian,8);
                    transform.position=new Vector3(fx,y,fz);velocity=Vector3.zero;path?.Clear();
                    MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(fx),Mathf.FloorToInt(y+1f),Mathf.FloorToInt(fz),BlockId.Obsidian,8);
                    return true;
                }
            }
            return false;
        }
        bool TeleportNear(Vector3 center,float radius)=>TeleportAround(center,radius);
        bool IsTeleportSpot(Vector3 p)
        {
            // main.js Op(): support is An(x,y-.2,z), body is gt(...,0), and water/aquatic/lava are rejected.
            if(p.y<VoxelConstants.MinY+2||p.y>VoxelConstants.MaxY-2)return false;
            if(!SourcePointCollision.Contains(world,new Vector3(p.x,p.y-.2f,p.z))||!CanOccupy(p,true))return false;
            BlockId b=world.GetBlock(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+.5f),Mathf.FloorToInt(p.z));return !SourceWaterLike(b)&&!BlockRegistry.IsLava(b);
        }

        void DamagePlayer(float amount){if(amount<=0||playerController==null)return;playerController.TakeMobDamage(amount);}

        public void Damage(float amount,Vector3? hitOrigin,bool byPlayer,bool silent=false)
        {
            if(dead||amount<=0)return;lastHitByPlayer=byPlayer;HP-=amount;hurtT=.4f;if(hitOrigin.HasValue){Vector3 away=transform.position-hitOrigin.Value;away.y=0;if(away.sqrMagnitude<.001f)away=Vector3.forward;away.Normalize();velocity.x+=away.x*6;velocity.z+=away.z*6;velocity.y=4.5f;kbT=.3f;}
            if(!Def.Hostile)fleeT=4f;if(Def.Aquatic){escT=0;hasEscapeTarget=false;}if(HP>0&&byPlayer&&(Def.Neutral||Def.NeutralLight>0))angryT=30f;
            if(HP>0&&Def.Teleports&&Rand()<.5f&&TeleportAround(transform.position,24f))tpT=1f;if(HP<=0)Kill(true,byPlayer);
        }

        void Kill(bool effects,bool byPlayer)
        {
            if(dead)return;dead=true;
            if(effects)
            {
                Vector3 p=transform.position;MainTransientRenderer.SpawnSourceParticles(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+.5f),Mathf.FloorToInt(p.z),AtlasLayout.Bricks,8);
            }
            if(effects&&byPlayer)lastHitByPlayer=true;
            // main.js Kp(): split children are rolled/spawned before def.drops() is evaluated.
            // Keep this order because both paths consume the shared random stream.
            if(effects&&Def.Splits&&MobSpawner.Instance!=null)
            {
                int count=2+RandInt(3);for(int i=0;i<count;i++){float a=i/(float)count*Mathf.PI*2+Rand()*.5f;Vector3 v=new Vector3(Mathf.Cos(a)*2.5f,3,Mathf.Sin(a)*2.5f);MobSpawner.Instance.SpawnSplit(Def.SplitKind,transform.position+new Vector3(Mathf.Cos(a)*BodyWidth*.8f,.1f,Mathf.Sin(a)*BodyWidth*.8f),v,angryT);}
            }
            if(effects&&lastHitByPlayer&&playerController!=null&&!playerController.IsCreative)DropOnDeath();
            Destroy(gameObject);
        }

        void DropOnDeath()
        {
            // main.js Kp(): def.drops(A) is evaluated completely first; only afterwards does Xe()
            // create item entities. Xe() consumes another random value for launch direction, so emitting
            // an item between loot rolls changes later loot decisions. Roll first, emit second.
            Vector3 p=transform.position+Vector3.up*.5f;
            MobItemId first=MobItemId.None,second=MobItemId.None;int firstCount=0,secondCount=0;
            switch(Kind)
            {
                case MobKind.Pig:
                    first=MobItemId.Porkchop;firstCount=1+(Rand()<.5f?1:0);break;
                case MobKind.Cow:
                    first=MobItemId.Beef;firstCount=1+(Rand()<.5f?1:0);if(Rand()<.7f){second=MobItemId.Leather;secondCount=1;}break;
                case MobKind.Sheep:
                    first=MobItemId.Mutton;firstCount=1;if(!sheared){second=MobItemCatalog.WoolFor(sheepColor);secondCount=1;}break;
                case MobKind.Chicken:
                    first=MobItemId.Chicken;firstCount=1;if(Rand()<.6f){second=MobItemId.Feather;secondCount=1+(Rand()<.4f?1:0);}break;
                case MobKind.Zombie:
                {
                    first=MobItemId.RottenFlesh;firstCount=1+(Rand()<.5f?1:0);float zr=Rand();if(zr<.05f){second=MobItemId.Carrot;secondCount=1;}else if(zr<.10f){second=MobItemId.Potato;secondCount=1;}break;
                }
                case MobKind.Skeleton:
                    first=MobItemId.Bone;firstCount=1+(Rand()<.5f?1:0);if(Rand()<.5f){second=MobItemId.Arrow;secondCount=1+RandInt(2);}break;
                case MobKind.Creeper:
                    first=MobItemId.Gunpowder;firstCount=1+RandInt(2);break;
                case MobKind.Spider:
                {
                    // Source VE.spider returns count=floor(random*3), then Xe() applies count||1.
                    // Therefore the zero roll still produces one string.
                    int strings=RandInt(3);first=MobItemId.String;firstCount=strings==0?1:strings;if(Rand()<.33f){second=MobItemId.SpiderEye;secondCount=1;}break;
                }
                case MobKind.Enderman:
                {int pearls=RandInt(2);if(pearls>0){first=MobItemId.EnderPearl;firstCount=pearls;}break;}
                case MobKind.Salmon:
                    first=IsBurningMedium()?MobItemId.CookedSalmon:MobItemId.RawSalmon;firstCount=1;break;
                case MobKind.Shark:
                    first=IsBurningMedium()?MobItemId.CookedCod:MobItemId.RawCod;firstCount=1+RandInt(2);if(Rand()<.35f){second=MobItemId.Bone;secondCount=1;}break;
                case MobKind.SlimeSmall:
                {int balls=RandInt(3);if(balls>0){first=MobItemId.SlimeBall;firstCount=balls;}break;}
            }
            Drop(first,firstCount,p);Drop(second,secondCount,p);
        }
        bool IsBurningMedium()=>burnT>0f||lavaT>0f;
        static void Drop(MobItemId id,int count,Vector3 p){if(id!=MobItemId.None&&count>0)MainTransientRenderer.SpawnDroppedItem(p.x,p.y,p.z,id,count);}

        void ExplodeCreeper()
        {
            int ex=SourceMath.JsRound(transform.position.x),ey=SourceMath.JsRound(transform.position.y+.5f),ez=SourceMath.JsRound(transform.position.z);
            Vector3 damageCenter=new Vector3(ex,ey,ez),c=new Vector3(ex+.5f,ey+.5f,ez+.5f);Kill(false,false);const float blockRadius=3f,entityRadius=8f;
            // main.js B2() clears the rounded explosion center before tracing rays.
            if(world.GetBlock(ex,ey,ez)!=BlockId.Air)world.SetBlock(ex,ey,ez,BlockId.Air,0,true);
            // main.js x7(): only the survival player receives the separate 8-block radial damage.
            if(playerController!=null&&!playerController.IsCreative){float d=Vector3.Distance(player.position+Vector3.up*.9f,damageCenter);if(d<entityRadius)playerController.TakeMobDamage(SourceMath.JsRound(20f*(1f-d/entityRadius)));}
            // main.js Pd()->Th(oR): explosions also damage every active mob in an 8-block radius.
            // oR() uses def.h*.5 (not baby-scaled G1()) and passes t!==false as the player-owned flag;
            // Creeper B2(..., false, 3) therefore damages/knocks back mobs without crediting the player.
            for(int mi=Active.Count-1;mi>=0;mi--)
            {
                if(mi>=Active.Count)continue;MobAI m=Active[mi];if(m==null||m==this||m.dead)continue;
                Vector3 mp=m.transform.position;float dx=mp.x-ex,dy=mp.y+m.Def.Height*.5f-ey,dz=mp.z-ez;float md=Mathf.Sqrt(dx*dx+dy*dy+dz*dz);
                if(md<entityRadius)m.Damage(SourceMath.JsRound(20f*(1f-md/entityRadius)),damageCenter,false,false);
            }
            var hit=new HashSet<long>();
            var hitOrder=new List<long>(96);
            // main.js B2(): cast rays from the surface of a 16^3 direction cube, advance .3 blocks,
            // attenuate by block resistance and .225 per step. BlastResistance() mirrors main.js Ap()/$J()
            // for every block currently present in the Unity BlockId catalog.
            for(int ix=0;ix<16;ix++)for(int iy=0;iy<16;iy++)for(int iz=0;iz<16;iz++)
            {
                if(ix!=0&&ix!=15&&iy!=0&&iy!=15&&iz!=0&&iz!=15)continue;
                Vector3 dir=new Vector3(ix/15f*2f-1f,iy/15f*2f-1f,iz/15f*2f-1f).normalized;float power=blockRadius*(.7f+Rand()*.6f);Vector3 p=c;
                while(power>0f)
                {
                    int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)break;BlockId id=world.GetBlock(x,y,z);
                    if(id!=BlockId.Air)
                    {
                        float resistance=BlastResistance(id);power-=(resistance+.3f)*.3f;
                        if(power>0f&&id!=BlockId.Bedrock&&id!=BlockId.Water){long key=PackBlock(x,y,z);if(hit.Add(key))hitOrder.Add(key);}
                    }
                    power-=.225f;p+=dir*.3f;
                }
            }
            for(int hi=0;hi<hitOrder.Count;hi++)
            {
                long key=hitOrder[hi];
                UnpackBlock(key,out int x,out int y,out int z);BlockId id=world.GetBlock(x,y,z);if(id==BlockId.Air||id==BlockId.Bedrock||id==BlockId.Water)continue;
                if(Rand()<.12f)MainTransientRenderer.SpawnBreakParticles(x,y,z,id,2);world.SetBlock(x,y,z,BlockId.Air,0,true);
            }
        }
        static readonly float[] SourceBlastResistance=BuildSourceBlastResistance();
        static float BlastResistance(BlockId id)
        {
            int i=(int)id;return i>=0&&i<SourceBlastResistance.Length?SourceBlastResistance[i]:3f;
        }
        static float[] BuildSourceBlastResistance()
        {
            Array values=Enum.GetValues(typeof(BlockId));int max=0;foreach(BlockId id in values)if((int)id>max)max=(int)id;
            var table=new float[max+1];foreach(BlockId id in values)table[(int)id]=SourceBlastResistanceForKey(SourceKey(id.ToString()));return table;
        }
        static float SourceBlastResistanceForKey(string n)
        {
            // main.js $J(): same ordered key-pattern rules for the currently ported BlockId catalog.
            if(n=="AIR")return 0f;
            if(n.Contains("BEDROCK")||n.Contains("OBSIDIAN"))return 1200f;
            if(n.Contains("WATER")||n.Contains("FLOW")||n.Contains("LAVA"))return 100f;
            if(n=="TNT")return 0f;
            if(n.Contains("END_STONE"))return 9f;
            if(n.Contains("SANDSTONE"))return .8f;
            if(HasAny(n,"IRON","GOLD","DIAMOND","EMERALD","NETHERITE","ANVIL")||
               HasAny(n,"STONE","COBBLE","DEEPSLATE","BRICK","ANDESITE","DIORITE","GRANITE","TUFF","BLACKSTONE","TERRA","CONCRETE","QUARTZ","PRISMARINE","CALCITE","PURPUR","NETHERRACK","ORE","FURNACE","MAGMA"))return 6f;
            if(HasAny(n,"GLASS","ICE","LEAVES","WOOL","CARPET","FLOWER","SAPLING","FERN","BUSH","GRASS_","VINE","KELP","SEAGRASS","LILY","TORCH","SNOW"))return .3f;
            if(HasAny(n,"DIRT","GRASS","GRAVEL","SAND","PODZOL","CLAY","MUD"))return .5f;
            return 3f;
        }
        static bool HasAny(string s,params string[] parts){for(int i=0;i<parts.Length;i++)if(s.Contains(parts[i]))return true;return false;}
        static string SourceKey(string pascal)
        {
            if(string.IsNullOrEmpty(pascal))return string.Empty;var b=new System.Text.StringBuilder(pascal.Length+8);
            for(int i=0;i<pascal.Length;i++){char c=pascal[i];if(i>0&&char.IsUpper(c)&&!char.IsUpper(pascal[i-1]))b.Append('_');b.Append(char.ToUpperInvariant(c));}return b.ToString();
        }
        static long PackBlock(int x,int y,int z){unchecked{return ((long)(x+1048576)<<42)^((long)(z+1048576)<<21)^(uint)(y-VoxelConstants.MinY);}}
        static void UnpackBlock(long k,out int x,out int y,out int z){x=(int)((k>>42)&0x1fffff)-1048576;z=(int)((k>>21)&0x1fffff)-1048576;y=(int)(k&0x1fffff)+VoxelConstants.MinY;}

        public void SetSplitLaunch(Vector3 v,float inheritedAnger){velocity=v;angryT=inheritedAnger;onGround=false;}
        public void DespawnWithoutEffects(){Kill(false,false);}


        public void ReceiveArrow(float damage,Vector3 hitPosition,bool ownedByPlayer=false)
        {
            if(dead)return;if(Def.Teleports){if(ownedByPlayer)angryT=30f;TeleportAround(transform.position,24f);tpT=1f;return;}Damage(damage,hitPosition,ownedByPlayer);
        }

        public static bool ProjectileHit(Vector3 point,MobAI source,out MobAI hit)
        {
            hit=null;for(int i=0;i<Active.Count;i++){MobAI m=Active[i];if(m==null||m==source||m.dead)continue;float r=m.BodyWidth+.15f;Vector3 p=m.transform.position;if(Mathf.Abs(point.x-p.x)<r&&Mathf.Abs(point.z-p.z)<r&&point.y>p.y-.15f&&point.y<p.y+m.BodyHeight+.15f){hit=m;return true;}}return false;
        }

        public static bool Raycast(Vector3 origin,Vector3 dir,float maxDistance,out MobAI mob,out float distance)
        {
            mob=null;distance=maxDistance;dir.Normalize();for(int i=0;i<Active.Count;i++){MobAI m=Active[i];if(m==null||m.dead)continue;float t=RayBox(m,origin,dir,maxDistance);if(t>=0&&t<distance){distance=t;mob=m;}}return mob!=null;
        }
        static float RayBox(MobAI m,Vector3 o,Vector3 d,float max)
        {
            float r=m.BodyWidth;Vector3 p=m.transform.position;Vector3 mn=new Vector3(p.x-r,p.y,p.z-r),mx=new Vector3(p.x+r,p.y+m.BodyHeight,p.z+r);float lo=0,hi=max;
            for(int a=0;a<3;a++){float ov=a==0?o.x:a==1?o.y:o.z,dv=a==0?d.x:a==1?d.y:d.z,a0=a==0?mn.x:a==1?mn.y:mn.z,a1=a==0?mx.x:a==1?mx.y:mx.z;if(Mathf.Abs(dv)<1e-8f){if(ov<a0||ov>a1)return-1;continue;}float t0=(a0-ov)/dv,t1=(a1-ov)/dv;if(t0>t1){float q=t0;t0=t1;t1=q;}if(t0>lo)lo=t0;if(t1<hi)hi=t1;if(lo>hi)return-1;}return lo;
        }

        void OnDestroy(){Active.Remove(this);}
    }
}
