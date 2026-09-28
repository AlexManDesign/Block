using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-order dynamic object passes missing from the original Unity port.
    /// Mirrors main.js aI(): kB() falling blocks -> pP() vehicles -> nT() fishing -> yd() pearls -> $B().
    /// Persistent direct meshes only; no per-object MeshRenderer/MeshFilter GameObjects.
    /// </summary>
    [DefaultExecutionOrder(146)]
    public sealed class MainSourceObjectRenderer : MonoBehaviour
    {
        struct FallingBlock { public int X,Z,Ry; public float Y,VY,Fuse; public BlockId Id; public bool Primed; }
        public enum VehicleKind : byte { Minecart, Boat }
        struct Vehicle
        {
            public VehicleKind Kind;
            public Vector3 Pos,Vel;
            public float Yaw,Pitch,VYaw,AmpL,AmpR,OarPhase;
            public int RailY;
        }
        struct Pearl { public Vector3 Pos,Vel; public float Age,Spin; }
        struct RedBox { public Vector3Int Cell; public float Alpha; }
        struct FishingBobber
        {
            public Vector3 Pos,Vel; public int State; // 0 fly, 1 float, 2 land
            public float SurfY,BobT,BiteT,BiteWin,LandT;
        }

        static MainSourceObjectRenderer instance;
        static readonly Matrix4x4 Identity=Matrix4x4.identity;
        static readonly VertexAttributeDescriptor[] Layout=
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,3,0)
        };
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
        static readonly MeshUpdateFlags BufferUploadFlags=UploadFlags|MeshUpdateFlags.DontNotifyMeshUsers;
        static readonly float[] FaceShade={.72f,.72f,1f,.55f,.82f,.82f};
        static readonly Vector3[,] CubeCorners=
        {
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,0,1),new Vector3(1,1,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,0,0),new Vector3(0,1,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(0,1,0),new Vector3(1,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(0,1,1),new Vector3(1,1,1)},
            {new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(1,1,0),new Vector3(0,1,0)}
        };
        static readonly Vector2[,] CubeUvs=
        {
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}
        };

        public VoxelWorld World;
        public Camera Cam;
        public Transform Player;

        readonly List<FallingBlock> falling=new List<FallingBlock>(64);
        readonly List<Vehicle> vehicles=new List<Vehicle>(32);
        readonly List<Pearl> pearls=new List<Pearl>(32);
        readonly List<RedBox> redBoxes=new List<RedBox>(64);
        readonly List<Vector3Int> explodingTnt=new List<Vector3Int>(16);
        int ridingVehicle=-1;
        FishingBobber fishing;
        bool fishingActive;
        bool fishingRodHeld;
        bool suppressFallHook;
        float pearlCooldown;
        float fishingUseCooldown;
        public MobItemId LastFishingCatch { get; private set; }

        readonly List<VoxelVertex> fallingV=new List<VoxelVertex>(4096); readonly List<int> fallingI=new List<int>(6144);
        readonly List<VoxelVertex> boatV=new List<VoxelVertex>(4096); readonly List<int> boatI=new List<int>(6144);
        readonly List<VoxelVertex> cartV=new List<VoxelVertex>(2048); readonly List<int> cartI=new List<int>(3072);
        readonly List<VoxelVertex> bobberV=new List<VoxelVertex>(64); readonly List<int> bobberI=new List<int>(96);
        readonly List<VoxelVertex> lineV=new List<VoxelVertex>(128); readonly List<int> lineI=new List<int>(192);
        readonly List<VoxelVertex> pearlV=new List<VoxelVertex>(256); readonly List<int> pearlI=new List<int>(384);
        readonly List<VoxelVertex> redV=new List<VoxelVertex>(2048); readonly List<int> redI=new List<int>(3072);

        Mesh fallingMesh,boatMesh,cartMesh,bobberMesh,lineMesh,pearlMesh,redMesh;
        Material atlasMat,ironMat,bobberMat,whiteMat,pearlMat,redMat,shipOpaqueMat,shipWaterMat;
        int fallingVC=4096,fallingIC=8192,boatVC=4096,boatIC=8192,cartVC=2048,cartIC=4096,bobberVC=64,bobberIC=128,lineVC=128,lineIC=256,pearlVC=256,pearlIC=512,redVC=2048,redIC=4096;
        int fallingLast=-1,boatLast=-1,cartLast=-1,bobberLast=-1,lineLast=-1,pearlLast=-1,redLast=-1;
        IndexFormat fallingFmt=IndexFormat.UInt16,boatFmt=IndexFormat.UInt16,cartFmt=IndexFormat.UInt16,bobberFmt=IndexFormat.UInt16,lineFmt=IndexFormat.UInt16,pearlFmt=IndexFormat.UInt16,redFmt=IndexFormat.UInt16;
        ushort[] falling16,boat16,cart16,bobber16,line16,pearl16,red16;
        bool fallingGeo,boatGeo,cartGeo,bobberGeo,lineGeo,pearlGeo,redGeo;
        bool fallingHad,vehiclesHad,fishingHad,pearlsHad,redDirty=true,vehiclesDirty=true;

        public static MainSourceObjectRenderer EnsureInstance(VoxelWorld world,Camera cam,Transform player)
        {
            if(instance==null)
            {
                var go=new GameObject("MainSourceObjectRenderer");
                instance=go.AddComponent<MainSourceObjectRenderer>();
            }
            instance.World=world;instance.Cam=cam;instance.Player=player;
            if(instance.shipOpaqueMat!=null&&instance.shipWaterMat!=null)MainMovingShipRenderer.EnsureInstance(world,instance.shipOpaqueMat,instance.shipWaterMat);
            if(world!=null)world.InvalidateDirectCommands();
            return instance;
        }

        public static void AppendFalling(CommandBuffer cb){if(cb!=null&&instance!=null&&instance.fallingGeo)cb.DrawMesh(instance.fallingMesh,Identity,instance.atlasMat,0,0);}
        public static void AppendVehicles(CommandBuffer cb)
        {
            if(cb==null||instance==null)return;
            if(instance.cartGeo)cb.DrawMesh(instance.cartMesh,Identity,instance.ironMat,0,0);
            if(instance.boatGeo)cb.DrawMesh(instance.boatMesh,Identity,instance.atlasMat,0,0);
        }
        // main.js ng()/tI(): moving vessels reuse voxel geometry and render as opaque-local then water.
        public static void AppendShipOpaque(CommandBuffer cb){MainMovingShipRenderer.AppendOpaque(cb);}
        public static void AppendShipWater(CommandBuffer cb){MainMovingShipRenderer.AppendWater(cb);}
        public static void AppendFishing(CommandBuffer cb)
        {
            if(cb==null||instance==null)return;
            if(instance.bobberGeo)cb.DrawMesh(instance.bobberMesh,Identity,instance.bobberMat,0,0);
            if(instance.lineGeo)cb.DrawMesh(instance.lineMesh,Identity,instance.whiteMat,0,0);
        }
        public static void AppendPearls(CommandBuffer cb){if(cb!=null&&instance!=null&&instance.pearlGeo)cb.DrawMesh(instance.pearlMesh,Identity,instance.pearlMat,0,0);}
        public static void AppendRedBoxes(CommandBuffer cb){if(cb!=null&&instance!=null&&instance.redGeo)cb.DrawMesh(instance.redMesh,Identity,instance.redMat,0,0);}

        // main.js block-update callback: rE(A,e,r), rE(A,e+1,r).
        public static void NotifyBlockChanged(int x,int y,int z)
        {
            if(instance==null||instance.suppressFallHook)return;
            instance.TryStartFalling(x,y,z);
            instance.TryStartFalling(x,y+1,z);
        }
        public static void SetFallingHookSuppressed(bool suppressed){if(instance!=null)instance.suppressFallHook=suppressed;}

        // main.js gs(): remove world TNT, precompute the fall target and start a 2s primed fuse.
        public static bool PrimeTnt(int x,int y,int z,float fuseSeconds=2f)
        {
            if(instance==null||instance.World==null||instance.World.GetBlock(x,y,z)!=BlockId.Tnt)return false;
            int ry=y;while(ry-1>=VoxelConstants.MinY&&instance.FallThrough(instance.World.GetBlock(x,ry-1,z)))ry--;
            bool oldSuppress=instance.suppressFallHook;instance.suppressFallHook=true;instance.World.SetBlock(x,y,z,BlockId.Air,0,true);instance.suppressFallHook=oldSuppress;
            instance.falling.Add(new FallingBlock{X=x,Z=z,Ry=ry,Y=y,VY=0f,Id=BlockId.Tnt,Primed=true,Fuse=Mathf.Max(.001f,fuseSeconds)});
            instance.fallingHad=true;instance.World.InvalidateDirectCommands();return true;
        }

        // main gs() in ship-local context: transform the TNT cell center, floor it back to world cells,
        // then detach the primed entity from vessel geometry without removing a static-world TNT block.
        public static bool SpawnDetachedPrimedTnt(Vector3 worldCenter,float fuseSeconds=2f)
        {
            if(instance==null||instance.World==null)return false;int x=Mathf.FloorToInt(worldCenter.x),y=Mathf.FloorToInt(worldCenter.y),z=Mathf.FloorToInt(worldCenter.z);
            int ry=y;while(ry-1>=VoxelConstants.MinY&&instance.FallThrough(instance.World.GetBlock(x,ry-1,z)))ry--;
            instance.falling.Add(new FallingBlock{X=x,Z=z,Ry=ry,Y=y,VY=0f,Id=BlockId.Tnt,Primed=true,Fuse=Mathf.Max(.001f,fuseSeconds)});instance.fallingHad=true;instance.World.InvalidateDirectCommands();return true;
        }

        public static int SpawnVehicle(VehicleKind kind,Vector3 pos,float yaw=0f,float pitch=0f)
        {
            if(instance==null)return -1;
            instance.vehicles.Add(new Vehicle{Kind=kind,Pos=pos,Yaw=yaw,Pitch=pitch,RailY=Mathf.FloorToInt(pos.y)});
            instance.vehiclesHad=true;instance.vehiclesDirty=true;instance.World?.InvalidateDirectCommands();return instance.vehicles.Count-1;
        }
        public static void SetVehiclePose(int index,Vector3 pos,float yaw,float pitch=0f,float ampL=0f,float ampR=0f,float oarPhase=0f)
        {
            if(instance==null||(uint)index>=(uint)instance.vehicles.Count)return;
            var v=instance.vehicles[index];v.Pos=pos;v.Yaw=yaw;v.Pitch=pitch;v.AmpL=ampL;v.AmpR=ampR;v.OarPhase=oarPhase;instance.vehicles[index]=v;instance.vehiclesDirty=true;
        }
        public static void ClearVehicles()
        {
            if(instance==null)return;
            if(instance.ridingVehicle>=0)instance.EndRide(false);
            instance.vehicles.Clear();instance.vehiclesHad=true;instance.vehiclesDirty=true;instance.World?.InvalidateDirectCommands();
        }

        struct RailInfo { public byte Kind; public int A,B; } // 0 straight, 1 slope, 2 curve
        struct BoatTuning
        {
            public float Acc,Max,Drag,IdleDrag,TurnAcc,TurnDrag,TurnMax;
            public BoatTuning(float acc,float max,float drag,float idle,float turnAcc,float turnDrag,float turnMax)
            {Acc=acc;Max=max;Drag=drag;IdleDrag=idle;TurnAcc=turnAcc;TurnDrag=turnDrag;TurnMax=turnMax;}
        }
        static readonly Vector2Int[] RailDirs={new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)};
        static readonly BoatTuning BoatWater=new BoatTuning(16f,8f,2.1f,2.1f,12f,5f,2.4f);
        static readonly BoatTuning BoatIce=new BoatTuning(16f,20f,.4f,.4f,9f,1.1f,2.8f);
        static readonly BoatTuning BoatLand=new BoatTuning(16f,1f,24f,10f,8f,8f,1.6f);

        public static bool TryRideVehicle(Vector3 origin,Vector3 direction)
        {
            if(instance==null||instance.Player==null)return false;
            int hit=instance.RaycastVehicle(origin,direction,6f,instance.ridingVehicle);if(hit<0)return false;
            var pc=instance.Player.GetComponent<MainPlayerController>();if(pc==null)return false;
            MainShipRuntime.DismountForOtherVehicle();
            var v=instance.vehicles[hit];if(v.Kind==VehicleKind.Boat){v.Yaw=pc.MainYawRadians;instance.vehicles[hit]=v;instance.vehiclesDirty=true;}
            instance.ridingVehicle=hit;pc.SetVehicleRiding(true);return true;
        }

        public static void CancelVehicleRide()
        {
            if(instance!=null&&instance.ridingVehicle>=0)instance.EndRide(false);
        }

        public static bool TryBreakVehicle(Vector3 origin,Vector3 direction)
        {
            if(instance==null)return false;int hit=instance.RaycastVehicle(origin,direction,6f,-1);if(hit<0)return false;
            Vehicle v=instance.vehicles[hit];
            if(instance.ridingVehicle==hit)instance.EndRide(false);
            else if(instance.ridingVehicle>hit)instance.ridingVehicle--;
            instance.vehicles.RemoveAt(hit);instance.vehiclesDirty=true;instance.vehiclesHad=true;instance.World?.InvalidateDirectCommands();
            MainTransientRenderer.SpawnDroppedItem(v.Pos.x,v.Pos.y+.3f,v.Pos.z,v.Kind==VehicleKind.Minecart?MobItemId.Minecart:MobItemId.Boat,1);
            return true;
        }

        public static bool PlaceMinecartFromAim(Vector3 origin,Vector3 direction,float playerYaw)
        {
            if(instance==null||instance.World==null)return false;VoxelHit hit;if(!VoxelRaycast.Cast(instance.World,origin,direction,6f,out hit))return false;
            Vector3 p;
            if(hit.Id==BlockId.Rail)p=new Vector3(hit.Block.x+.5f,hit.Block.y+.08f,hit.Block.z+.5f);
            else {Vector3Int b=hit.Block+hit.Normal;p=new Vector3(b.x+.5f,b.y+.02f,b.z+.5f);}
            float yaw=Mathf.Round(playerYaw/(Mathf.PI*.5f))*(Mathf.PI*.5f);SpawnVehicle(VehicleKind.Minecart,p,yaw);return true;
        }

        public static bool PlaceBoatFromAim(Vector3 origin,Vector3 direction,float playerYaw)
        {
            if(instance==null||instance.World==null)return false;direction.Normalize();
            Vector3Int last=new Vector3Int(int.MinValue,int.MinValue,int.MinValue);
            for(float d=0f;d<=6f;d+=.05f)
            {
                Vector3 q=origin+direction*d;var c=new Vector3Int(Mathf.FloorToInt(q.x),Mathf.FloorToInt(q.y),Mathf.FloorToInt(q.z));if(c==last)continue;last=c;
                BlockId id=instance.World.GetBlock(c.x,c.y,c.z);if(BlockRegistry.IsWater(id)||BlockRegistry.IsAquatic(id))
                {int top=c.y;while(top<VoxelConstants.MaxY&&(BlockRegistry.IsWater(instance.World.GetBlock(c.x,top+1,c.z))||BlockRegistry.IsAquatic(instance.World.GetBlock(c.x,top+1,c.z))))top++;SpawnVehicle(VehicleKind.Boat,new Vector3(c.x+.5f,top+.82f,c.z+.5f),playerYaw);return true;}
                if(BlockRegistry.IsSolid(id))break;
            }
            VoxelHit hit;if(!VoxelRaycast.Cast(instance.World,origin,direction,6f,out hit))return false;Vector3Int b=hit.Block+hit.Normal;
            SpawnVehicle(VehicleKind.Boat,new Vector3(b.x+.5f,b.y+.02f,b.z+.5f),playerYaw);return true;
        }

        int RaycastVehicle(Vector3 origin,Vector3 direction,float max,int exclude)
        {
            direction.Normalize();int best=-1;float nearest=float.MaxValue;
            for(int i=0;i<vehicles.Count;i++)
            {
                if(i==exclude)continue;Vehicle v=vehicles[i];float h=v.Kind==VehicleKind.Minecart?.6f:.8f,top=v.Kind==VehicleKind.Minecart?.7f:.6f;
                float t;if(RayAabb(origin,direction,new Vector3(v.Pos.x-h,v.Pos.y-.2f,v.Pos.z-h),new Vector3(v.Pos.x+h,v.Pos.y+top,v.Pos.z+h),max,out t)&&t<nearest){nearest=t;best=i;}
            }
            return best;
        }
        static bool RayAabb(Vector3 o,Vector3 d,Vector3 mn,Vector3 mx,float max,out float hit)
        {
            float t0=0f,t1=max;if(!RaySlab(o.x,d.x,mn.x,mx.x,ref t0,ref t1)||!RaySlab(o.y,d.y,mn.y,mx.y,ref t0,ref t1)||!RaySlab(o.z,d.z,mn.z,mx.z,ref t0,ref t1)){hit=0;return false;}hit=t0;return hit>=0&&hit<=max;
        }
        static bool RaySlab(float o,float d,float mn,float mx,ref float t0,ref float t1)
        {
            if(Mathf.Abs(d)<1e-8f)return o>=mn&&o<=mx;float a=(mn-o)/d,b=(mx-o)/d;if(a>b){float q=a;a=b;b=q;}if(a>t0)t0=a;if(b<t1)t1=b;return t0<=t1;
        }

        void UpdateVehicles(float dt)
        {
            if(vehicles.Count==0){if(ridingVehicle>=0)EndRide(false);return;}
            if(ridingVehicle>=vehicles.Count)EndRide(false);
            if(ridingVehicle>=0&&(Input.GetKeyDown(KeyCode.LeftShift)||Input.GetKeyDown(KeyCode.RightShift))){EndRide(true);}
            var pc=Player!=null?Player.GetComponent<MainPlayerController>():null;
            for(int i=0;i<vehicles.Count;i++)
            {
                Vehicle before=vehicles[i],v=before;bool ridden=i==ridingVehicle&&pc!=null;
                if(v.Kind==VehicleKind.Minecart)UpdateMinecart(ref v,dt,ridden,pc);else UpdateBoat(ref v,dt,ridden,pc);
                vehicles[i]=v;if((v.Pos-before.Pos).sqrMagnitude>1e-10f||Mathf.Abs(v.Yaw-before.Yaw)>1e-7f||Mathf.Abs(v.Pitch-before.Pitch)>1e-7f||Mathf.Abs(v.AmpL-before.AmpL)>1e-5f||Mathf.Abs(v.AmpR-before.AmpR)>1e-5f)vehiclesDirty=true;
                if(ridden)
                {
                    pc.ApplyVehicleRidePose(v.Pos+Vector3.up*(v.Kind==VehicleKind.Minecart?-.15f:-.45f));
                    if(v.Kind==VehicleKind.Boat)pc.ClampVehicleYaw(v.Yaw,Mathf.PI*.5f);
                }
            }
        }

        void EndRide(bool safeSide)
        {
            if(ridingVehicle<0){return;}int idx=ridingVehicle;ridingVehicle=-1;var pc=Player!=null?Player.GetComponent<MainPlayerController>():null;if(pc==null)return;pc.SetVehicleRiding(false);if(idx<0||idx>=vehicles.Count)return;
            Vehicle v=vehicles[idx];Vector3 feet;
            if(safeSide&&TrySafeDismount(v,out feet))pc.ApplyVehicleDismount(feet,MainPlayerController.SourceStandingHeight);
            else {float gy=GroundY(Mathf.FloorToInt(v.Pos.x),Mathf.FloorToInt(v.Pos.z),v.Pos.y+1.2f);feet=new Vector3(v.Pos.x,Mathf.Max(gy,v.Pos.y),v.Pos.z);pc.ApplyVehicleDismount(feet,MainPlayerController.SourceStandingHeight);}
        }
        bool TrySafeDismount(Vehicle v,out Vector3 feet)
        {
            var pc=Player!=null?Player.GetComponent<MainPlayerController>():null;Vector2[] c={new Vector2(v.Pos.x+.9f,v.Pos.z),new Vector2(v.Pos.x-.9f,v.Pos.z),new Vector2(v.Pos.x,v.Pos.z+.9f),new Vector2(v.Pos.x,v.Pos.z-.9f),new Vector2(v.Pos.x,v.Pos.z)};
            if(pc!=null)for(int i=0;i<c.Length;i++)
            {
                float g=GroundY(Mathf.FloorToInt(c[i].x),Mathf.FloorToInt(c[i].y),v.Pos.y+1.2f);if(g>v.Pos.y+1.5f||g<v.Pos.y-3f)g=v.Pos.y;Vector3 p=new Vector3(c[i].x,g+.01f,c[i].y);
                if(pc.CanOccupyFeet(p,MainPlayerController.SourceStandingHeight)){feet=p;return true;}if(pc.CanOccupyFeet(p,MainPlayerController.SourceSwimHeight)){feet=p;return true;}
            }
            feet=new Vector3(v.Pos.x,Mathf.Floor(v.Pos.y)+.01f,v.Pos.z);return false;
        }

        int? RailY(int x,int z,int y)
        {
            if(World.GetBlock(x,y,z)==BlockId.Rail)return y;if(World.GetBlock(x,y+1,z)==BlockId.Rail)return y+1;if(World.GetBlock(x,y-1,z)==BlockId.Rail)return y-1;return null;
        }
        float GroundY(int x,int z,float startY)
        {
            for(int y=Mathf.FloorToInt(startY);y>VoxelConstants.MinY;y--)if(BlockRegistry.IsSolid(World.GetBlock(x,y-1,z)))return y;return VoxelConstants.MinY;
        }
        int? WaterTop(int x,int z,float y)
        {
            int top=Mathf.FloorToInt(y+.5f);for(int q=top;q>top-4;q--){BlockId id=World.GetBlock(x,q,z);if(BlockRegistry.IsWater(id)||BlockRegistry.IsAquatic(id)){while(q<VoxelConstants.MaxY&&(BlockRegistry.IsWater(World.GetBlock(x,q+1,z))||BlockRegistry.IsAquatic(World.GetBlock(x,q+1,z))))q++;return q;}if(BlockRegistry.IsSolid(id))return null;}return null;
        }
        RailInfo GetRailShape(int x,int y,int z)
        {
            int mask=0,slope=-1;for(int i=0;i<4;i++){Vector2Int d=RailDirs[i];if(World.GetBlock(x+d.x,y,z+d.y)==BlockId.Rail)mask|=1<<i;else if(World.GetBlock(x+d.x,y+1,z+d.y)==BlockId.Rail){mask|=1<<i;if(slope<0)slope=i;}}
            if(slope>=0)return new RailInfo{Kind=1,A=slope};if((mask&1)!=0&&(mask&2)!=0)return new RailInfo{Kind=0,A=1};if((mask&4)!=0&&(mask&8)!=0)return new RailInfo{Kind=0,A=0};int xx=mask&3,zz=mask&12;if(xx!=0&&zz!=0)return new RailInfo{Kind=2,A=(mask&1)!=0?0:1,B=(mask&4)!=0?2:3};return new RailInfo{Kind=0,A=xx!=0?1:zz!=0?0:(World.GetMeta(x,y,z)&1)};
        }
        static int RailAxis(int dir)=>dir<=1?1:0;
        static int RailUpSign(int dir)=>(dir==0||dir==2)?1:-1;
        static float RailSlopeHeight(int dir,float x,float z,int bx,int bz)=>Mathf.Clamp01(dir==0?x-bx:dir==1?1f-(x-bx):dir==2?z-bz:1f-(z-bz));
        float PlayerAxisSign(int axis,MainPlayerController pc){float v=axis==1?Mathf.Sin(pc.MainYawRadians):Mathf.Cos(pc.MainYawRadians);return v>=0f?1f:-1f;}
        float StraightSpeed(float value,float dt,bool ridden,int axis,int upSign,MainPlayerController pc)
        {
            if(ridden){bool w=Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow),s=Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow);float sign=PlayerAxisSign(axis,pc);if(w)value+=sign*12f*dt;else if(s)value-=sign*12f*dt;else value*=Mathf.Pow(.35f,dt);}else value*=Mathf.Pow(.7f,dt);if(upSign!=0)value+=-upSign*10f*dt;return Mathf.Clamp(value,-8f,8f);
        }
        float CurveSpeed(float value,float dt,bool ridden)
        {
            if(ridden){bool w=Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow),s=Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow);if(w)value+=12f*dt;else if(s)value=Mathf.Max(0f,value-12f*dt);else value*=Mathf.Pow(.5f,dt);}else value*=Mathf.Pow(.7f,dt);return Mathf.Min(8f,value);
        }
        bool CartCornerCollision(float x,float y,float z)
        {
            const float h=.45f;for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)if(BlockRegistry.IsSolid(World.GetBlock(Mathf.FloorToInt(x+sx*h),Mathf.FloorToInt(y+.25f),Mathf.FloorToInt(z+sz*h))))return true;return false;
        }
        bool RailMoveAllowed(Vehicle v,float x,float z,int railY)
        {
            float dx=x-v.Pos.x,dz=z-v.Pos.z;if(Mathf.Abs(dx)<1e-8f&&Mathf.Abs(dz)<1e-8f)return true;float px=x+(dx>0?.45f:dx<0?-.45f:0),pz=z+(dz>0?.45f:dz<0?-.45f:0);int bx=Mathf.FloorToInt(px),bz=Mathf.FloorToInt(pz);if(World.GetBlock(bx,railY,bz)==BlockId.Rail||World.GetBlock(bx,railY+1,bz)==BlockId.Rail||World.GetBlock(bx,railY-1,bz)==BlockId.Rail)return true;return !BlockRegistry.IsSolid(World.GetBlock(bx,Mathf.FloorToInt(v.Pos.y+.25f),bz));
        }
        void FreeCart(ref Vehicle v,float dt)
        {
            v.Pitch=0;v.Vel.x*=Mathf.Max(0f,1f-dt*3f);v.Vel.z*=Mathf.Max(0f,1f-dt*3f);v.Vel.y-=24f*dt;v.Pos.y+=v.Vel.y*dt;float g=GroundY(Mathf.FloorToInt(v.Pos.x),Mathf.FloorToInt(v.Pos.z),v.Pos.y+.5f);if(v.Pos.y<=g){v.Pos.y=g;v.Vel.y=0;}float x=v.Pos.x+v.Vel.x*dt;if(CartCornerCollision(x,v.Pos.y,v.Pos.z))v.Vel.x=0;else v.Pos.x=x;float z=v.Pos.z+v.Vel.z*dt;if(CartCornerCollision(v.Pos.x,v.Pos.y,z))v.Vel.z=0;else v.Pos.z=z;
        }
        void UpdateMinecart(ref Vehicle v,float dt,bool ridden,MainPlayerController pc)
        {
            int bx=Mathf.FloorToInt(v.Pos.x),bz=Mathf.FloorToInt(v.Pos.z);int? ry=RailY(bx,bz,Mathf.FloorToInt(v.Pos.y));if(ry.HasValue&&v.Vel.y<-.5f&&v.Pos.y>ry.Value+.6f)ry=null;if(!ry.HasValue){FreeCart(ref v,dt);return;}int y=ry.Value;v.RailY=y;v.Vel.y=0;v.Pitch=0;RailInfo sh=GetRailShape(bx,y,bz);
            if(sh.Kind==2)
            {
                float cx=bx+(sh.A==0?1f:0f),cz=bz+(sh.B==2?1f:0f),dx=v.Pos.x-cx,dz=v.Pos.z-cz,rad=Mathf.Sqrt(dx*dx+dz*dz);if(rad<1e-6f)rad=1e-6f;float tx=-dz/rad,tz=dx/rad,curveSpeed=new Vector2(v.Vel.x,v.Vel.z).magnitude;
                if(curveSpeed>1e-4f){if(tx*v.Vel.x+tz*v.Vel.z<0){tx=-tx;tz=-tz;}}else if(pc!=null){float fx=Mathf.Sin(pc.MainYawRadians),fz=Mathf.Cos(pc.MainYawRadians);if(tx*fx+tz*fz<0){tx=-tx;tz=-tz;}}
                curveSpeed=CurveSpeed(curveSpeed,dt,ridden);v.Vel.x=tx*curveSpeed;v.Vel.z=tz*curveSpeed;if(!RailMoveAllowed(v,v.Pos.x+v.Vel.x*dt,v.Pos.z+v.Vel.z*dt,y)){v.Vel.x=v.Vel.z=0;curveSpeed=0;}v.Pos.x+=v.Vel.x*dt;v.Pos.z+=v.Vel.z*dt;dx=v.Pos.x-cx;dz=v.Pos.z-cz;rad=Mathf.Sqrt(dx*dx+dz*dz);if(rad<1e-6f)rad=1e-6f;v.Pos.x+=dx/rad*(.5f-rad);v.Pos.z+=dz/rad*(.5f-rad);v.Pos.y=y+.08f;if(curveSpeed>1e-4f)v.Yaw=Mathf.Atan2(v.Vel.x,v.Vel.z);return;
            }
            bool slope=sh.Kind==1;int axis=slope?RailAxis(sh.A):sh.A,up=slope?RailUpSign(sh.A):0;float speed=StraightSpeed(axis==1?v.Vel.x:v.Vel.z,dt,ridden,axis,up,pc);
            if(axis==1){v.Pos.z=bz+.5f;if(!RailMoveAllowed(v,v.Pos.x+speed*dt,v.Pos.z,y))speed=0;v.Vel.x=speed;v.Vel.z=0;v.Pos.x+=speed*dt;}else{v.Pos.x=bx+.5f;if(!RailMoveAllowed(v,v.Pos.x,v.Pos.z+speed*dt,y))speed=0;v.Vel.z=speed;v.Vel.x=0;v.Pos.z+=speed*dt;}
            if(slope){v.Pos.y=y+.08f+RailSlopeHeight(sh.A,v.Pos.x,v.Pos.z,bx,bz);v.Pitch=-up*(Mathf.PI*.25f);}else v.Pos.y=y+.08f;v.Yaw=axis==1?Mathf.PI*.5f:0f;
        }

        bool BoatCollision(Vehicle v,float x,float z,out Vector2 push)
        {
            float fx=Mathf.Sin(v.Yaw),fz=Mathf.Cos(v.Yaw),rx=Mathf.Cos(v.Yaw),rz=-Mathf.Sin(v.Yaw);float px=0,pz=0;bool hit=false;
            for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2){float cx=x+fx*.8f*a+rx*.5f*b,cz=z+fz*.8f*a+rz*.5f*b;if(BlockRegistry.IsSolid(World.GetBlock(Mathf.FloorToInt(cx),Mathf.FloorToInt(v.Pos.y+.05f),Mathf.FloorToInt(cz)))||BlockRegistry.IsSolid(World.GetBlock(Mathf.FloorToInt(cx),Mathf.FloorToInt(v.Pos.y+.25f),Mathf.FloorToInt(cz)))){hit=true;px-=cx-x;pz-=cz-z;}}
            push=new Vector2(px,pz);return hit;
        }
        void BoatAxisMove(ref Vehicle v,bool xAxis,float amount)
        {
            if(Mathf.Abs(amount)<1e-8f)return;float x=v.Pos.x,z=v.Pos.z;if(xAxis)x+=amount;else z+=amount;Vector2 q0,q1;if(BoatCollision(v,x,z,out q1)&&!BoatCollision(v,v.Pos.x,v.Pos.z,out q0)){if(xAxis)v.Vel.x=0;else v.Vel.z=0;return;}v.Pos.x=x;v.Pos.z=z;
        }
        void UpdateBoat(ref Vehicle v,float dt,bool ridden,MainPlayerController pc)
        {
            int bx=Mathf.FloorToInt(v.Pos.x),bz=Mathf.FloorToInt(v.Pos.z);int? water=WaterTop(bx,bz,v.Pos.y);BoatTuning tune=BoatLand;
            if(water.HasValue){v.Pos.y+=(water.Value+.82f-v.Pos.y)*Mathf.Min(1f,dt*8f);v.Vel.y=0;tune=BoatWater;}
            else {v.Vel.y-=24f*dt;v.Pos.y+=v.Vel.y*dt;float g=GroundY(bx,bz,v.Pos.y+.5f);if(v.Pos.y<=g){v.Pos.y=g;v.Vel.y=0;}BlockId under=World.GetBlock(bx,Mathf.FloorToInt(v.Pos.y-.05f),bz);if(under==BlockId.Ice||under==BlockId.PackedIce||under==BlockId.BlueIce)tune=BoatIce;}
            bool leftOar=false,rightOar=false;
            if(ridden&&pc!=null)
            {
                float steer=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1f:0f)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1f:0f),throttle=(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1f:0f)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1f:0f);if(throttle<0&&Mathf.Abs(steer)>.01f)throttle=0;if(Mathf.Abs(steer)>.001f)v.VYaw+=steer*tune.TurnAcc*dt;float fx=Mathf.Sin(v.Yaw),fz=Mathf.Cos(v.Yaw),forward=v.Vel.x*fx+v.Vel.z*fz,acc=tune.Acc*(throttle>=0?throttle:-throttle*.125f);if(throttle>0&&forward<tune.Max){v.Vel.x+=fx*acc*dt;v.Vel.z+=fz*acc*dt;}else if(throttle<0&&forward>-tune.Max*.125f){v.Vel.x-=fx*acc*dt;v.Vel.z-=fz*acc*dt;}bool active=Mathf.Abs(throttle)>.05f;leftOar=active||steer>.05f;rightOar=active||steer<-.05f;
            }
            v.AmpL+=((leftOar?1f:0f)-v.AmpL)*Mathf.Min(1f,dt*6f);v.AmpR+=((rightOar?1f:0f)-v.AmpR)*Mathf.Min(1f,dt*6f);if(v.AmpL>.02f||v.AmpR>.02f)v.OarPhase+=dt*7f;v.VYaw=Mathf.Clamp(v.VYaw,-tune.TurnMax,tune.TurnMax);
            if(Mathf.Abs(v.VYaw)>1e-8f){float dy=v.VYaw*dt;v.Yaw+=dy;if(ridden&&pc!=null)pc.AddVehicleYaw(dy);v.VYaw*=Mathf.Max(0f,1f-dt*tune.TurnDrag);if(Mathf.Abs(v.VYaw)<.01f)v.VYaw=0;}
            float drag=ridden?tune.Drag:tune.IdleDrag;v.Vel.x*=Mathf.Max(0f,1f-dt*drag);v.Vel.z*=Mathf.Max(0f,1f-dt*drag);float speed=Mathf.Sqrt(v.Vel.x*v.Vel.x+v.Vel.z*v.Vel.z);if(speed>tune.Max){v.Vel.x=v.Vel.x/speed*tune.Max;v.Vel.z=v.Vel.z/speed*tune.Max;}
            Vector2 push;if(BoatCollision(v,v.Pos.x,v.Pos.z,out push)){float m=push.magnitude;if(m>1e-4f){v.Pos.x+=push.x/m*dt*1.2f;v.Pos.z+=push.y/m*dt*1.2f;}}BoatAxisMove(ref v,true,v.Vel.x*dt);BoatAxisMove(ref v,false,v.Vel.z*dt);
        }

        public static void AddRedBox(Vector3Int cell,float alpha=.32f)
        {
            if(instance==null)return;instance.redBoxes.Add(new RedBox{Cell=cell,Alpha=Mathf.Clamp01(alpha)});instance.redDirty=true;
        }
        public static void ClearRedBoxes(){if(instance==null)return;instance.redBoxes.Clear();instance.redDirty=true;}

        // Source hL()/rT()/dL(): cast/reel toggle with the source 300 ms use guard.
        public static bool UseFishingRod()
        {
            if(instance==null||instance.fishingUseCooldown>0f)return false;instance.fishingUseCooldown=.3f;
            return instance.fishingActive?ReelFishing():CastFishing();
        }
        public static bool CastFishing()
        {
            if(instance==null||instance.Cam==null)return false;
            Vector3 eye=instance.Cam.transform.position,f=instance.Cam.transform.forward;
            instance.fishing=new FishingBobber{Pos=eye+f*.4f-Vector3.up*.1f,Vel=f*13f+Vector3.up*3.2f,State=0};
            instance.fishingActive=true;instance.fishingRodHeld=true;instance.LastFishingCatch=MobItemId.None;instance.World?.InvalidateDirectCommands();return true;
        }
        public static bool ReelFishing()
        {
            if(instance==null||!instance.fishingActive)return false;var b=instance.fishing;bool caught=b.State==1&&b.BiteWin>0f;
            if(caught)
            {
                MobItemId reward=instance.RollFishingReward();instance.LastFishingCatch=reward;
                if(reward!=MobItemId.None&&instance.Player!=null)
                {
                    Vector3 to=instance.Player.position+Vector3.up-b.Pos;float dist=to.magnitude;float u=Mathf.Clamp(.5f+dist*.06f,.7f,1.6f);
                    Vector3 v=new Vector3(to.x/u,to.y/u+9f*u,to.z/u);MainTransientRenderer.SpawnDroppedItem(b.Pos.x,b.Pos.y+.2f,b.Pos.z,reward,1,v);
                }
                if(instance.World!=null)MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(b.Pos.x),Mathf.FloorToInt(b.Pos.y),Mathf.FloorToInt(b.Pos.z),BlockId.Water,10);
                // main.js dL(): fishing rod loses one durability only on a successful bite/catch.
                if(instance.Player!=null){var pc=instance.Player.GetComponent<MainPlayerController>();if(pc!=null&&!pc.IsCreative)pc.DamageSelectedInventoryItem(1);}
            }
            instance.fishingActive=false;instance.fishingRodHeld=false;instance.World?.InvalidateDirectCommands();return caught;
        }
        public static void SetFishingRodHeld(bool held){if(instance==null)return;instance.fishingRodHeld=held;if(!held&&instance.fishingActive){instance.fishingActive=false;instance.World?.InvalidateDirectCommands();}}
        MobItemId RollFishingReward()
        {
            float a=UnityEngine.Random.value,e=UnityEngine.Random.value;if(a<.6f)return MobItemId.RawCod;if(a<.85f)return MobItemId.RawSalmon;
            if(a<.95f){MobItemId[] junk={MobItemId.Stick,MobItemId.Bone,MobItemId.ClayBall,MobItemId.Leather,MobItemId.String};return junk[Mathf.Min(junk.Length-1,Mathf.FloorToInt(e*junk.Length))];}
            MobItemId[] treasure={MobItemId.DiamondGem,MobItemId.GoldIngot,MobItemId.EmeraldGem};return treasure[Mathf.Min(treasure.Length-1,Mathf.FloorToInt(e*treasure.Length))];
        }

        // Source pearl constants qf=30,lR=12,QR=.818,BR=.0115,jf=1,Sd=20.
        public static bool ThrowEnderPearl(Vector3 inheritedVelocity,bool onGround)
        {
            if(instance==null||instance.Cam==null||instance.pearlCooldown>0f)return false;
            Vector3 f=instance.Cam.transform.forward;
            // main adds tiny triangular random inaccuracy PR=.0172275 before normalization.
            f+=new Vector3((UnityEngine.Random.value-UnityEngine.Random.value)*.0172275f,(UnityEngine.Random.value-UnityEngine.Random.value)*.0172275f,(UnityEngine.Random.value-UnityEngine.Random.value)*.0172275f);
            f.Normalize();Vector3 v=f*30f+new Vector3(inheritedVelocity.x,onGround?0f:inheritedVelocity.y,inheritedVelocity.z);
            instance.pearls.Add(new Pearl{Pos=instance.Cam.transform.position+instance.Cam.transform.forward*.4f-Vector3.up*.12f,Vel=v});
            instance.pearlCooldown=1f;instance.World?.InvalidateDirectCommands();return true;
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;
            fallingMesh=CreateMesh("main_kB_falling",fallingVC,fallingIC);boatMesh=CreateMesh("main_pP_boat",boatVC,boatIC);cartMesh=CreateMesh("main_pP_minecart",cartVC,cartIC);
            bobberMesh=CreateMesh("main_nT_bobber",bobberVC,bobberIC);lineMesh=CreateMesh("main_nT_line",lineVC,lineIC);pearlMesh=CreateMesh("main_yd_pearl",pearlVC,pearlIC);redMesh=CreateMesh("main_red_boxes",redVC,redIC);
            Shader opaque=Shader.Find("Blockcraft/VoxelOpaque"),transparent=Shader.Find("Blockcraft/VoxelTransparent"),shipOpaque=Shader.Find("Blockcraft/VoxelShipOpaque"),shipWater=Shader.Find("Blockcraft/VoxelShipWater");
            Texture2D atlas=LoadTex("Voxel/atlas"),iron=LoadTex("Voxel/SourceTransient/iron_block"),bob=LoadTex("Voxel/SourceTransient/bobber"),white=LoadTex("Voxel/SourceTransient/white"),pearl=LoadTex("Voxel/SourceTransient/ender_pearl"),red=LoadTex("Voxel/SourceTransient/red");
            if(opaque==null||transparent==null||shipOpaque==null||shipWater==null||atlas==null||iron==null||bob==null||white==null||pearl==null||red==null){Debug.LogError("MainSourceObjectRenderer: source textures/shaders missing");enabled=false;return;}
            atlasMat=NewMat(opaque,atlas,"main_source_atlas");ironMat=NewMat(opaque,iron,"main_source_iron");bobberMat=NewMat(opaque,bob,"main_source_bobber");whiteMat=NewMat(opaque,white,"main_source_white");pearlMat=NewMat(opaque,pearl,"main_source_pearl");
            redMat=NewMat(transparent,red,"main_source_red_box");redMat.SetFloat("_Alpha",.32f);
            shipOpaqueMat=NewMat(shipOpaque,atlas,"main_ship_opaque");shipWaterMat=NewMat(shipWater,atlas,"main_ship_water");shipWaterMat.SetFloat("_Alpha",.62f);
        }

        static Texture2D LoadTex(string path){var t=Resources.Load<Texture2D>(path);if(t!=null){t.filterMode=FilterMode.Point;t.wrapMode=TextureWrapMode.Clamp;t.anisoLevel=0;}return t;}
        static Material NewMat(Shader s,Texture2D t,string name)=>new Material(s){name=name,mainTexture=t};
        Mesh CreateMesh(string name,int vc,int ic)
        {
            var m=new Mesh{name=name,indexFormat=IndexFormat.UInt16};m.MarkDynamic();m.SetVertexBufferParams(vc,Layout);m.SetIndexBufferParams(ic,IndexFormat.UInt16);m.subMeshCount=1;m.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);m.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);return m;
        }

        void LateUpdate()
        {
            if(World==null)return;float dt=Mathf.Min(Time.deltaTime,.1f);pearlCooldown=Mathf.Max(0f,pearlCooldown-dt);fishingUseCooldown=Mathf.Max(0f,fishingUseCooldown-dt);
            UpdateFalling(dt);UpdateVehicles(dt);UpdateFishing(dt);UpdatePearls(dt);
            bool hf=falling.Count!=0;if(hf||fallingHad){BuildFalling();fallingHad=hf;}
            bool hv=vehicles.Count!=0;if(vehiclesDirty||hv!=vehiclesHad){BuildVehicles();vehiclesDirty=false;vehiclesHad=hv;}
            bool hb=fishingActive;if(hb||fishingHad){BuildFishing();fishingHad=hb;}
            bool hp=pearls.Count!=0;if(hp||pearlsHad){BuildPearls();pearlsHad=hp;}
            if(redDirty){BuildRedBoxes();redDirty=false;}
        }

        bool IsFalling(BlockId id)=>id==BlockId.Sand||id==BlockId.RedSand||id==BlockId.Gravel;
        bool FallThrough(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsWater(id))return true;BlockShape sh=BlockRegistry.Get(id).Shape;return sh==BlockShape.Cross||sh==BlockShape.TallPlant;
        }
        void TryStartFalling(int x,int y,int z)
        {
            if(y<=VoxelConstants.MinY||y>VoxelConstants.MaxY)return;BlockId id=World.GetBlock(x,y,z);if(!IsFalling(id)||!FallThrough(World.GetBlock(x,y-1,z)))return;
            for(int i=0;i<falling.Count;i++)if(falling[i].X==x&&falling[i].Z==z&&Mathf.Abs(falling[i].Y-y)<.01f)return;
            int ry=y;while(ry-1>=VoxelConstants.MinY&&FallThrough(World.GetBlock(x,ry-1,z)))ry--;
            suppressFallHook=true;World.SetBlock(x,y,z,BlockId.Air,0,false);suppressFallHook=false;
            falling.Add(new FallingBlock{X=x,Z=z,Ry=ry,Y=y,VY=0f,Id=id});
        }
        void UpdateFalling(float dt)
        {
            // main OB(): sort low -> high; gravity 28. Primed TNT shares the same list/render pass.
            falling.Sort((a,b)=>a.Y.CompareTo(b.Y));explodingTnt.Clear();
            for(int i=0;i<falling.Count;i++)
            {
                var f=falling[i];f.VY-=28f*dt;f.Y+=f.VY*dt;
                if(f.Primed)
                {
                    int y=Mathf.FloorToInt(f.Y);while(y>=VoxelConstants.MinY&&FallThrough(World.GetBlock(f.X,y,f.Z)))y--;int ground=y+1;
                    if(f.Y<=ground){f.Y=ground;f.VY=0f;}f.Fuse-=dt;if(f.Fuse<=0f){explodingTnt.Add(new Vector3Int(f.X,SourceMath.JsRound(f.Y),f.Z));falling.RemoveAt(i--);continue;}falling[i]=f;continue;
                }
                int s=f.Ry;while(s<VoxelConstants.MaxY-1&&!FallThrough(World.GetBlock(f.X,s,f.Z)))s++;
                if(f.Y<=s)
                {
                    f.Y=s;suppressFallHook=true;World.SetBlock(f.X,s,f.Z,f.Id,0,false);suppressFallHook=false;
                    falling.RemoveAt(i--);NotifyBlockChanged(f.X,s,f.Z);continue;
                }
                falling[i]=f;
            }
            for(int i=0;i<explodingTnt.Count;i++)SourceExplosion.Explode(World,Player!=null?Player.GetComponent<MainPlayerController>():null,explodingTnt[i].x,explodingTnt[i].y,explodingTnt[i].z,true,4f);
        }

        void BuildFalling()
        {
            fallingV.Clear();fallingI.Clear();
            for(int i=0;i<falling.Count;i++)
            {
                var f=falling[i];BlockDef d=BlockRegistry.Get(f.Id);byte l=World.GetPackedLight(f.X,Mathf.RoundToInt(f.Y)+1,f.Z);float sky=(l>>4)/15f,blk=(l&15)/15f;
                for(int face=0;face<6;face++)
                {
                    AtlasRect a=face==2?d.Top:face==3?d.Bottom:d.Side;int b=fallingV.Count;Vector3 ssb=new Vector3(FaceShade[face],sky,blk);
                    for(int k=0;k<4;k++)AddV(fallingV,new Vector3(f.X,f.Y,f.Z)+CubeCorners[face,k],UV(a,CubeUvs[face,k].x,CubeUvs[face,k].y),ssb);
                    Quad(fallingI,b);
                }
            }
            Upload(fallingMesh,fallingV,fallingI,ref fallingVC,ref fallingIC,ref fallingLast,ref fallingFmt,ref falling16);SetGeo(ref fallingGeo,fallingI.Count!=0);
        }

        void BuildVehicles()
        {
            boatV.Clear();boatI.Clear();cartV.Clear();cartI.Clear();
            AtlasRect plank=BlockRegistry.Get(BlockId.OakPlanks).Side;
            for(int i=0;i<vehicles.Count;i++)
            {
                Vehicle v=vehicles[i];byte l=World.GetPackedLight(Mathf.FloorToInt(v.Pos.x),Mathf.FloorToInt(v.Pos.y+.5f),Mathf.FloorToInt(v.Pos.z));float sky=(l>>4)/15f,blk=(l&15)/15f;
                if(v.Kind==VehicleKind.Minecart)
                {
                    AddVehicleBox(cartV,cartI,v,true,-.45f,0,-.62f,.45f,.12f,.62f,FullUV,sky,blk);
                    AddVehicleBox(cartV,cartI,v,true,-.45f,.12f,.52f,.45f,.5f,.62f,FullUV,sky,blk);
                    AddVehicleBox(cartV,cartI,v,true,-.45f,.12f,-.62f,.45f,.5f,-.52f,FullUV,sky,blk);
                    AddVehicleBox(cartV,cartI,v,true,.35f,.12f,-.62f,.45f,.5f,.62f,FullUV,sky,blk);
                    AddVehicleBox(cartV,cartI,v,true,-.45f,.12f,-.62f,-.35f,.5f,.62f,FullUV,sky,blk);
                }
                else
                {
                    AddVehicleBox(boatV,boatI,v,false,-.5625f,0,-.875f,.5625f,.1875f,.875f,plank,sky,blk);
                    AddVehicleBox(boatV,boatI,v,false,.4375f,.1875f,-.875f,.5625f,.5625f,.875f,plank,sky,blk);
                    AddVehicleBox(boatV,boatI,v,false,-.5625f,.1875f,-.875f,-.4375f,.5625f,.875f,plank,sky,blk);
                    AddVehicleBox(boatV,boatI,v,false,-.4375f,.1875f,.75f,.4375f,.5625f,.875f,plank,sky,blk);
                    AddVehicleBox(boatV,boatI,v,false,-.4375f,.1875f,-.875f,.4375f,.5625f,-.75f,plank,sky,blk);
                    AddOar(boatV,boatI,v,-1,v.AmpL,v.OarPhase,plank,sky,blk);AddOar(boatV,boatI,v,1,v.AmpR,v.OarPhase,plank,sky,blk);
                }
            }
            Upload(boatMesh,boatV,boatI,ref boatVC,ref boatIC,ref boatLast,ref boatFmt,ref boat16);Upload(cartMesh,cartV,cartI,ref cartVC,ref cartIC,ref cartLast,ref cartFmt,ref cart16);SetGeo(ref boatGeo,boatI.Count!=0);SetGeo(ref cartGeo,cartI.Count!=0);
        }

        static readonly AtlasRect FullUV=new AtlasRect(.00375f,.00375f,.99625f,.99625f);
        Vector3 TransformVehicle(Vehicle v,bool pitch,float x,float y,float z)
        {
            if(pitch&&Mathf.Abs(v.Pitch)>1e-7f){float c=Mathf.Cos(v.Pitch),s=Mathf.Sin(v.Pitch),ny=y*c-z*s,nz=y*s+z*c;y=ny;z=nz;}
            float cy=Mathf.Cos(v.Yaw),sy=Mathf.Sin(v.Yaw);
            // Source Z is reflected at the Unity world boundary. Boat/oar uses So(); minecart uses dE().
            float uz=pitch?(x*sy-z*cy):(-x*sy+z*cy);
            return new Vector3(v.Pos.x+x*cy+z*sy,v.Pos.y+y,v.Pos.z+uz);
        }
        void AddVehicleBox(List<VoxelVertex> vv,List<int> ii,Vehicle v,bool pitch,float x0,float y0,float z0,float x1,float y1,float z1,AtlasRect uv,float sky,float blk)
        {
            Vector3 l=TransformVehicle(v,pitch,x0,y0,z0),p=TransformVehicle(v,pitch,x1,y0,z0),c=TransformVehicle(v,pitch,x0,y0,z1),b=TransformVehicle(v,pitch,x1,y0,z1),d=TransformVehicle(v,pitch,x0,y1,z0),h=TransformVehicle(v,pitch,x1,y1,z0),I=TransformVehicle(v,pitch,x0,y1,z1),D=TransformVehicle(v,pitch,x1,y1,z1);
            Vector3[,] faces={{d,h,I,D},{l,p,c,b},{I,D,c,b},{h,d,p,l},{D,h,b,p},{d,I,l,c}};float[] shade={1,.5f,.85f,.85f,.7f,.7f};Vector2[] quv={new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
            for(int f=0;f<6;f++){int q=vv.Count;Vector3 ssb=new Vector3(shade[f],sky,blk);for(int k=0;k<4;k++)AddV(vv,faces[f,k],UV(uv,quv[k].x,quv[k].y),ssb);Quad(ii,q);}
        }
        void AddOar(List<VoxelVertex> vv,List<int> ii,Vehicle v,float side,float amp,float phase,AtlasRect uv,float sky,float blk)
        {
            Vector3 s=new Vector3(side*.56f,.52f,-.15f);float C=Mathf.Sin(phase)*amp*.55f*side;float f=side*.9f,w=-.3f,l=-.3f,P=Mathf.Cos(C),c=Mathf.Sin(C),bx=f*P-l*c,by=w,bz=f*c+l*P;Vector3 y=new Vector3(bx,by,bz).normalized;Vector3 b=new Vector3(-y.z,0,y.x).normalized;Vector3 ia=Vector3.Cross(y,b);const float Y=.8f,blade=.32f;Vector3 A=s+y*(Y/2);AddOrientedBox(vv,ii,v,A,y*(Y/2),b*.03f,ia*.03f,uv,sky,blk);Vector3 sA=s+y*(Y+blade/2);AddOrientedBox(vv,ii,v,sA,y*(blade/2),b*.13f,ia*.025f,uv,sky,blk);
        }
        void AddOrientedBox(List<VoxelVertex> vv,List<int> ii,Vehicle v,Vector3 center,Vector3 a,Vector3 b,Vector3 c,AtlasRect uv,float sky,float blk)
        {
            Vector3[] pt=new Vector3[8];for(int n=0;n<8;n++){float x=(n&4)!=0?1:-1,y=(n&2)!=0?1:-1,z=(n&1)!=0?1:-1;Vector3 q=center+a*x+b*y+c*z;pt[n]=TransformVehicle(v,false,q.x,q.y,q.z);}
            int[,] faces={{4,5,6,7},{0,1,2,3},{2,3,6,7},{0,1,4,5},{1,3,5,7},{0,2,4,6}};float[] shade={.8f,.6f,.95f,.7f,.75f,.7f};Vector2[] quv={new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
            for(int f=0;f<6;f++){int q=vv.Count;Vector3 ssb=new Vector3(shade[f],sky,blk);for(int k=0;k<4;k++)AddV(vv,pt[faces[f,k]],UV(uv,quv[k].x,quv[k].y),ssb);Quad(ii,q);}
        }

        void UpdateFishing(float dt)
        {
            if(!fishingActive)return;var b=fishing;BlockId here=World.GetBlock(Mathf.FloorToInt(b.Pos.x),Mathf.FloorToInt(b.Pos.y),Mathf.FloorToInt(b.Pos.z));
            if(!fishingRodHeld||(Player!=null&&(b.Pos-Player.position).sqrMagnitude>1024f)||b.Pos.y<VoxelConstants.MinY-8||BlockRegistry.IsLava(here)){fishingActive=false;return;}
            if(b.State==2){b.LandT-=dt;if(b.LandT<=0f){fishingActive=false;return;}fishing=b;return;}
            if(b.State==0)
            {
                b.Vel.y-=16f*dt;Vector3 n=b.Pos+b.Vel*dt;BlockId id=World.GetBlock(Mathf.FloorToInt(n.x),Mathf.FloorToInt(n.y),Mathf.FloorToInt(n.z));
                if(BlockRegistry.IsWater(id))
                {
                    int y=Mathf.FloorToInt(n.y);while(y<VoxelConstants.MaxY&&BlockRegistry.IsWater(World.GetBlock(Mathf.FloorToInt(n.x),y+1,Mathf.FloorToInt(n.z))))y++;
                    b.Pos=new Vector3(n.x,y+.85f,n.z);b.SurfY=y+.85f;b.State=1;b.Vel=Vector3.zero;b.BobT=0;b.BiteT=4f+UnityEngine.Random.value*10f;
                    MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(n.x),y,Mathf.FloorToInt(n.z),BlockId.Water,8);
                }
                else if(BlockRegistry.IsSolid(id))
                {
                    b.Vel.x=b.Vel.z=0;if(b.Vel.y>0)b.Vel.y=0;BlockId below=World.GetBlock(Mathf.FloorToInt(b.Pos.x),Mathf.FloorToInt(b.Pos.y-.1f),Mathf.FloorToInt(b.Pos.z));
                    if(BlockRegistry.IsSolid(below)){b.Pos.y=Mathf.Floor(b.Pos.y-.1f)+1.05f;b.State=2;b.LandT=.6f;}else b.Pos.y=n.y;
                }else b.Pos=n;
            }
            else
            {
                b.BobT+=dt;if(b.BiteWin>0){b.BiteWin-=dt;b.Pos.y=b.SurfY-.22f+Mathf.Sin(b.BobT*16f)*.03f;if(b.BiteWin<=0)b.BiteT=4f+UnityEngine.Random.value*10f;}
                else {b.Pos.y=b.SurfY+Mathf.Sin(b.BobT*2.3f)*.04f;b.BiteT-=dt;if(b.BiteT<=0){b.BiteWin=1.4f;MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(b.Pos.x),Mathf.FloorToInt(b.SurfY),Mathf.FloorToInt(b.Pos.z),BlockId.Water,12);}}
            }
            fishing=b;
        }

        void BuildFishing()
        {
            bobberV.Clear();bobberI.Clear();lineV.Clear();lineI.Clear();
            if(fishingActive)
            {
                Vector3 p=fishing.Pos;byte l=World.GetPackedLight(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y+.5f),Mathf.FloorToInt(p.z));float sky=(l>>4)/15f,blk=(l&15)/15f;const float u=.09f;Vector2[] axes={new Vector2(1,0),new Vector2(0,1)};
                for(int a=0;a<2;a++){float x=axes[a].x*u,z=axes[a].y*u;int q=bobberV.Count;Vector3 ssb=new Vector3(1,sky,blk);AddV(bobberV,new Vector3(p.x-x,p.y+2*u,p.z-z),FullUVAt(0,0),ssb);AddV(bobberV,new Vector3(p.x+x,p.y+2*u,p.z+z),FullUVAt(1,0),ssb);AddV(bobberV,new Vector3(p.x-x,p.y,p.z-z),FullUVAt(0,1),ssb);AddV(bobberV,new Vector3(p.x+x,p.y,p.z+z),FullUVAt(1,1),ssb);DoubleQuad(bobberI,q);}
                Vector3 from=FishingHand(),to=p+Vector3.up*(2*u);Vector3 d=to-from;float dist=d.magnitude,sag=Mathf.Min(.45f,dist*.14f)*(fishing.State==0 ? .25f : 1f);Vector3 prev=from,look=Cam!=null?Cam.transform.forward:Vector3.forward;
                for(int k=1;k<=10;k++){float t=k/10f;Vector3 cur=from+d*t+Vector3.down*(sag*4*t*(1-t));AddLineQuad(prev,cur,look,sky,blk);prev=cur;}
            }
            Upload(bobberMesh,bobberV,bobberI,ref bobberVC,ref bobberIC,ref bobberLast,ref bobberFmt,ref bobber16);Upload(lineMesh,lineV,lineI,ref lineVC,ref lineIC,ref lineLast,ref lineFmt,ref line16);SetGeo(ref bobberGeo,bobberI.Count!=0);SetGeo(ref lineGeo,lineI.Count!=0);
        }
        Vector3 FishingHand()
        {
            if(Cam==null)return Player!=null?Player.position+Vector3.up*1.5f:Vector3.zero;
            // Exact PL() in source coordinates, then the project's single source-Z reflection.
            Vector3 ue=Cam.transform.position,uf=Cam.transform.forward;Vector3 se=new Vector3(ue.x,ue.y,-ue.z),sf=new Vector3(uf.x,uf.y,-uf.z);
            float yaw=Player!=null?Player.eulerAngles.y*Mathf.Deg2Rad:Cam.transform.eulerAngles.y*Mathf.Deg2Rad;Vector3 r=new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw));Vector3 t=Vector3.Cross(r,sf);
            Vector3 sp=se+sf*.62f+r*.19f+t*(-.18f);return new Vector3(sp.x,sp.y,-sp.z);
        }
        void AddLineQuad(Vector3 a,Vector3 b,Vector3 look,float sky,float blk)
        {
            Vector3 d=b-a,c=Vector3.Cross(d,look);if(c.sqrMagnitude<1e-10f)c=Vector3.up;c=c.normalized*.012f;int q=lineV.Count;Vector3 ssb=new Vector3(.13f,sky,blk);AddV(lineV,a-c,FullUVAt(0,0),ssb);AddV(lineV,a+c,FullUVAt(1,0),ssb);AddV(lineV,b-c,FullUVAt(0,1),ssb);AddV(lineV,b+c,FullUVAt(1,1),ssb);DoubleQuad(lineI,q);
        }

        void UpdatePearls(float dt)
        {
            for(int i=pearls.Count-1;i>=0;i--)
            {
                Pearl p=pearls[i];p.Age+=dt;p.Spin+=dt*6f;if(p.Age>20f||p.Pos.y<VoxelConstants.MinY){pearls.RemoveAt(i);continue;}
                bool water=BlockRegistry.IsWater(World.GetBlock(Mathf.FloorToInt(p.Pos.x),Mathf.FloorToInt(p.Pos.y),Mathf.FloorToInt(p.Pos.z)));p.Vel.y-=12f*dt;float drag=Mathf.Pow(water ? .0115f : .818f,dt);p.Vel*=drag;
                float speed=p.Vel.magnitude;int steps=Mathf.Max(1,Mathf.CeilToInt(speed*dt/.3f));float sd=dt/steps;Vector3? hit=null;Vector3 dir=Vector3.forward;
                for(int step=0;step<steps&&!hit.HasValue;step++)
                {
                    Vector3 before=p.Pos;p.Pos+=p.Vel*sd;if(p.Pos.y<VoxelConstants.MinY)break;dir=p.Vel.sqrMagnitude>1e-8f?p.Vel.normalized:Vector3.forward;
                    if(SourcePointCollision.Contains(World,p.Pos))
                    {
                        float lo=0f,hi=1f;for(int b=0;b<7;b++){float m=(lo+hi)*.5f;Vector3 q=Vector3.Lerp(before,p.Pos,m);if(SourcePointCollision.Contains(World,q))hi=m;else lo=m;}hit=Vector3.Lerp(before,p.Pos,lo);break;
                    }
                    MobAI mob;if(MobAI.ProjectileHit(p.Pos,null,out mob)){hit=p.Pos;break;}
                }
                if(hit.HasValue)
                {
                    var pc=Player!=null?Player.GetComponent<MainPlayerController>():null;if(pc==null||!pc.Dead)TeleportNear(hit.Value,dir,pc);pearls.RemoveAt(i);continue;
                }
                pearls[i]=p;
            }
        }
        bool TeleportNear(Vector3 hit,Vector3 dir,MainPlayerController pc)
        {
            if(Player==null||pc==null)return false;float[] back={0,.15f,.35f,.6f,.9f};float[] up={0,.5f,1,-.5f,2,-1};
            for(int j=0;j<up.Length;j++)for(int i=0;i<back.Length;i++)
            {
                Vector3 q=hit-dir*back[i]+Vector3.up*up[j];bool stand=pc.CanOccupyFeet(q,MainPlayerController.SourceStandingHeight),swim=pc.CanOccupyFeet(q,MainPlayerController.SourceSwimHeight);if(!stand&&!swim)continue;
                float h=stand?MainPlayerController.SourceStandingHeight:MainPlayerController.SourceSwimHeight;pc.ApplyPearlTeleport(q,h);if(!pc.IsCreative)pc.TakeDirectDamage(5f);
                return true;
            }
            return false;
        }

        void BuildPearls()
        {
            pearlV.Clear();pearlI.Clear();const float n=.12f;for(int i=0;i<pearls.Count;i++){Pearl p=pearls[i];byte l=World.GetPackedLight(Mathf.FloorToInt(p.Pos.x),Mathf.FloorToInt(p.Pos.y),Mathf.FloorToInt(p.Pos.z));float sky=(l>>4)/15f,blk=(l&15)/15f,C=Mathf.Cos(p.Spin)*n,f=Mathf.Sin(p.Spin)*n;int q=pearlV.Count;Vector3 ssb=new Vector3(1,sky,blk);AddV(pearlV,new Vector3(p.Pos.x-C,p.Pos.y+n,p.Pos.z-f),FullUVAt(0,0),ssb);AddV(pearlV,new Vector3(p.Pos.x+C,p.Pos.y+n,p.Pos.z+f),FullUVAt(1,0),ssb);AddV(pearlV,new Vector3(p.Pos.x-C,p.Pos.y-n,p.Pos.z-f),FullUVAt(0,1),ssb);AddV(pearlV,new Vector3(p.Pos.x+C,p.Pos.y-n,p.Pos.z+f),FullUVAt(1,1),ssb);DoubleQuad(pearlI,q);}Upload(pearlMesh,pearlV,pearlI,ref pearlVC,ref pearlIC,ref pearlLast,ref pearlFmt,ref pearl16);SetGeo(ref pearlGeo,pearlI.Count!=0);
        }

        void BuildRedBoxes()
        {
            redV.Clear();redI.Clear();for(int i=0;i<redBoxes.Count;i++){Vector3 o=redBoxes[i].Cell;for(int f=0;f<6;f++){int q=redV.Count;Vector3 ssb=new Vector3(1,1,1);for(int k=0;k<4;k++)AddV(redV,o+CubeCorners[f,k],FullUVAt(CubeUvs[f,k].x,CubeUvs[f,k].y),ssb);Quad(redI,q);}}Upload(redMesh,redV,redI,ref redVC,ref redIC,ref redLast,ref redFmt,ref red16);SetGeo(ref redGeo,redI.Count!=0);
        }

        void SetGeo(ref bool field,bool value){if(field==value)return;field=value;World?.InvalidateDirectCommands();}
        static int GrowVertexCapacity(int current,int required)
        {
            if(required<=current)return current;
            int next=Mathf.NextPowerOfTwo(required);
            return required<=65535&&next>65535?65535:next;
        }
        static ushort[] EnsurePacked16(ref ushort[] buffer,int count)
        {
            if(buffer==null||buffer.Length<count)
            {
                if(buffer!=null)ArrayPool<ushort>.Shared.Return(buffer,false);
                buffer=ArrayPool<ushort>.Shared.Rent(Mathf.Max(1,count));
            }
            return buffer;
        }
        void Upload(Mesh mesh,List<VoxelVertex> v,List<int> i,ref int vc,ref int ic,ref int last,ref IndexFormat currentFormat,ref ushort[] packed16)
        {
            int nvc=GrowVertexCapacity(vc,v.Count),nic=ic;if(i.Count>nic)nic=Mathf.NextPowerOfTwo(i.Count);
            IndexFormat wanted=nvc<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            if(nvc!=vc||nic!=ic||wanted!=currentFormat)
            {
                vc=nvc;ic=nic;currentFormat=wanted;mesh.indexFormat=wanted;mesh.SetVertexBufferParams(vc,Layout);mesh.SetIndexBufferParams(ic,wanted);mesh.subMeshCount=1;last=-1;
            }
            if(v.Count>0)
            {
                mesh.SetVertexBufferData(v,0,0,v.Count,0,BufferUploadFlags);
                if(wanted==IndexFormat.UInt16)
                {
                    ushort[] dst=EnsurePacked16(ref packed16,i.Count);for(int n=0;n<i.Count;n++)dst[n]=(ushort)i[n];mesh.SetIndexBufferData(dst,0,0,i.Count,BufferUploadFlags);
                }
                else mesh.SetIndexBufferData(i,0,0,i.Count,BufferUploadFlags);
            }
            if(last!=i.Count){mesh.SetSubMesh(0,new SubMeshDescriptor(0,i.Count,MeshTopology.Triangles),UploadFlags);last=i.Count;}
        }
        static void AddV(List<VoxelVertex> v,Vector3 p,Vector2 uv,Vector3 ssb){v.Add(new VoxelVertex(p,uv,ssb));}
        static Vector2 UV(AtlasRect a,float u,float v)=>new Vector2(Mathf.Lerp(a.U0,a.U1,u),Mathf.Lerp(a.V0,a.V1,v));
        static Vector2 FullUVAt(float u,float v)=>UV(FullUV,u,v);
        static void Quad(List<int> i,int b){i.Add(b);i.Add(b+1);i.Add(b+2);i.Add(b+2);i.Add(b+1);i.Add(b+3);}
        static void DoubleQuad(List<int> i,int b){Quad(i,b);i.Add(b);i.Add(b+2);i.Add(b+1);i.Add(b+1);i.Add(b+2);i.Add(b+3);}
        static void Return16(ref ushort[] buffer){if(buffer==null)return;ArrayPool<ushort>.Shared.Return(buffer,false);buffer=null;}

        void OnDestroy()
        {
            Return16(ref falling16);Return16(ref boat16);Return16(ref cart16);Return16(ref bobber16);Return16(ref line16);Return16(ref pearl16);Return16(ref red16);
            if(shipOpaqueMat!=null)Destroy(shipOpaqueMat);if(shipWaterMat!=null)Destroy(shipWaterMat);
            if(instance==this)instance=null;
        }
    }
}
