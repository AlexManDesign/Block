using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-aligned runtime for main.js Q6/P6/hc/BB/Os/QB/D6/v6/E7/u7/i7/c7.
    /// Horizontal physics is kept in main/source coordinates. Only ship Z is reflected at the Unity boundary.
    /// Geometry stays persistent in MainMovingShipRenderer; translation/turning only updates its draw matrix.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class MainShipRuntime : MonoBehaviour
    {
        const int MaxShipBlocks=3000;
        const int FloodLimit=3050;
        const float Pivot=.5f;
        const float HelmFallbackHeight=1.1f;
        const float Acc=7f,MaxSpeed=5f,Drag=1.3f,TurnAcc=1.1f,TurnDrag=2f,TurnMax=.65f,ReverseMax=.4f;
        const float Gravity=9.8f,VerticalDrag=2f,MaxVertical=4f;
        const float CarveInterval=.12f,CarveMoveThreshold=.35f,CarveYawThreshold=.06f,CarveLookAhead=.45f;
        const int FloodPerTick=3;
        static readonly float[] ForwardOffsets={Mathf.PI,-Mathf.PI*.5f,0f,Mathf.PI*.5f};
        static readonly Vector3Int[] N6={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};

        public struct ShipVoxelHit
        {
            public int Handle;
            public Vector3Int Block;
            public Vector3Int Normal;
            public BlockId Id;
            public byte Meta;
            public Vector3 Point;
            public float Distance;
        }

        sealed class WorldCarveState
        {
            public BlockId Id;
            public byte Meta;
            public int Owners;
        }

        sealed class MobRide
        {
            public Ship Ship;
            public Vector3 Local;
            public float LastYaw;
        }

        sealed class Ship
        {
            public int Handle;
            public int GeoVersion=1;
            public readonly List<MainMovingShipRenderer.ShipBlock> Blocks=new List<MainMovingShipRenderer.ShipBlock>(1024);
            public readonly List<Vector3Int> Perim=new List<Vector3Int>(512);
            public readonly List<Vector3Int> PerimV=new List<Vector3Int>(512);
            public readonly List<float> DisplacementY=new List<float>(2048);
            public readonly HashSet<long> Occupied=new HashSet<long>();
            public readonly Dictionary<long,int> BlockIndex=new Dictionary<long,int>(1024);
            public readonly HashSet<long> HoldCells=new HashSet<long>();
            public readonly HashSet<long> FloodCells=new HashSet<long>();
            public readonly HashSet<long> OutsideCells=new HashSet<long>();
            public readonly HashSet<long> LeakCells=new HashSet<long>();
            public readonly HashSet<long> ReservedWorld=new HashSet<long>();
            public float X,Y,Z,Yaw,FwdOff,VX,VZ,VY,VYaw;
            public float Mass,WaterY;
            public int MinLY,Height;
            public float HalfX,HalfZ;
            public Vector3Int WheelCell=Vector3Int.zero;
            public bool HasHelmSeat;
            public Vector3Int HelmSeat;
            public bool Docked;
            public float LastCarveAt,LastCarveX,LastCarveY,LastCarveZ,LastCarveYaw;
            public bool HasCarvePose;
        }

        static MainShipRuntime instance;
        static readonly Dictionary<long,WorldCarveState> carvedWorld=new Dictionary<long,WorldCarveState>(4096);
        static readonly Dictionary<long,int> reservedWorld=new Dictionary<long,int>(4096);
        readonly List<Ship> ships=new List<Ship>(8);
        readonly Dictionary<MobAI,MobRide> mobRides=new Dictionary<MobAI,MobRide>(64);
        VoxelWorld world;
        MainPlayerController player;
        Ship sailing;
        bool shiftLatch;
        float floodClock;

        public static MainShipRuntime EnsureInstance(VoxelWorld w,MainPlayerController p)
        {
            if(instance==null)
            {
                var go=new GameObject("MainShipRuntime");
                instance=go.AddComponent<MainShipRuntime>();
            }
            instance.world=w;instance.player=p;
            return instance;
        }

        public static bool IsReservedWorldCell(int x,int y,int z)
        {
            return reservedWorld.ContainsKey(Key(x,y,z));
        }

        public static bool TryGetCarvedWorldBlock(int x,int y,int z,out BlockId id)
        {
            if(carvedWorld.TryGetValue(Key(x,y,z),out var c)){id=c.Id;return true;}
            id=BlockId.Air;return false;
        }

        public static bool TryRaycastDocked(Vector3 origin,Vector3 direction,float maxDistance,out ShipVoxelHit hit)
        {
            hit=default(ShipVoxelHit);
            if(instance==null||instance.ships.Count==0||direction.sqrMagnitude<1e-8f)return false;
            direction.Normalize();bool found=false;float best=maxDistance;
            for(int i=0;i<instance.ships.Count;i++)
            {
                Ship s=instance.ships[i];if(!s.Docked)continue;
                if(instance.RaycastShip(s,origin,direction,best,out ShipVoxelHit q)&&q.Distance<best)
                {best=q.Distance;hit=q;found=true;}
            }
            return found;
        }

        public static bool OverlapsDockedBody(Vector3 feet,float radius,float height)
        {
            if(instance==null)return false;
            for(int i=0;i<instance.ships.Count;i++)
            {
                Ship s=instance.ships[i];if(!s.Docked)continue;
                float reach=Mathf.Max(s.HalfX,s.HalfZ)+2f;
                Vector3 rp=instance.RenderPosition(s);
                if(feet.x<rp.x-reach||feet.x>rp.x+reach||feet.z<rp.z-reach||feet.z>rp.z+reach||
                   feet.y+height<s.Y+s.MinLY-1f||feet.y>s.Y+s.MinLY+s.Height+2f)continue;
                Matrix4x4 inv=instance.DrawMatrix(s).inverse;Vector3 lf=inv.MultiplyPoint3x4(feet);
                float x0=lf.x-radius,x1=lf.x+radius,y0=lf.y,y1=lf.y+height,z0=lf.z-radius,z1=lf.z+radius;
                int ix0=Mathf.FloorToInt(x0),ix1=Mathf.FloorToInt(x1),iy0=Mathf.FloorToInt(y0)-1,iy1=Mathf.FloorToInt(y1-.001f),iz0=Mathf.FloorToInt(z0),iz1=Mathf.FloorToInt(z1);
                for(int x=ix0;x<=ix1;x++)for(int y=iy0;y<=iy1;y++)for(int z=iz0;z<=iz1;z++)
                {
                    BlockId id=instance.LocalBlock(s,x,y,z);if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id)||BlockRegistry.IsAquatic(id))continue;
                    byte meta=instance.LocalMeta(s,x,y,z);VoxelBox b=VoxelShapes.Selection(id,meta);
                    if(x1>x+b.X0&&x0<x+b.X1&&y1>y+b.Y0&&y0<y+b.Y1&&z1>z+b.Z0&&z0<z+b.Z1)return true;
                }
            }
            return false;
        }

        /// <summary>Source kT()/EE(): the nearest docked ship voxel must itself be the helm.
        /// A nearer hull block correctly prevents boarding through the ship.
        /// </summary>
        public static bool TryUseMovingHelm(Vector3 origin,Vector3 direction,float maxDistance)
        {
            if(instance==null||instance.player==null)return false;
            ShipVoxelHit hit;if(!TryRaycastDocked(origin,direction,maxDistance,out hit)||hit.Id!=BlockId.ShipWheel)return false;
            Ship s=instance.FindShip(hit.Handle);if(s==null)return false;instance.Board(s);return true;
        }

        /// <summary>Source E7(): detach the six-connected structure rooted at a static SHIP_WHEEL.</summary>
        public static bool TryTakeStaticHelm(Vector3Int wheel)
        {
            if(instance==null||instance.world==null||instance.player==null)return false;
            if(instance.world.GetBlock(wheel.x,wheel.y,wheel.z)!=BlockId.ShipWheel)return false;
            var cells=new List<Vector3Int>(1024);
            if(!instance.CollectConnected(wheel,cells,out bool tooBig)||cells.Count<2)return false;
            if(tooBig||cells.Count>MaxShipBlocks)
            {
                Debug.LogWarning("Ship is still anchored / exceeds source 3000-block sailing limit.");
                MainSourceObjectRenderer.ClearRedBoxes();
                MainSourceObjectRenderer.AddRedBox(wheel,.32f);
                return true;
            }

            byte unityWheelMeta=instance.world.GetMeta(wheel.x,wheel.y,wheel.z);
            byte sourceWheelMeta=SourceCoords.UnityMetaToSource(BlockId.ShipWheel,unityWheelMeta);
            var s=new Ship{X=wheel.x,Y=wheel.y,Z=SourceCoords.UnityBlockZToSource(wheel.z),Yaw=0f,FwdOff=ForwardOffsets[sourceWheelMeta&3],Docked=false};
            for(int i=0;i<cells.Count;i++)
            {
                Vector3Int c=cells[i];
                s.Blocks.Add(new MainMovingShipRenderer.ShipBlock(c.x-wheel.x,c.y-wheel.y,c.z-wheel.z,instance.world.GetBlock(c.x,c.y,c.z),instance.world.GetMeta(c.x,c.y,c.z)));
            }

            instance.CarveDetachedWorld(cells,wheel);
            instance.BuildMetrics(s);
            s.Handle=MainMovingShipRenderer.Create(s.Blocks,instance.RenderPosition(s),s.Yaw);
            if(s.Handle<0)return false;
            instance.ships.Add(s);
            MainSourceObjectRenderer.ClearRedBoxes();
            instance.Board(s);
            instance.world.MarkPersistentSaveDirty();
            return true;
        }

        public static void DismountForOtherVehicle()
        {
            if(instance!=null&&instance.sailing!=null)instance.Dismount(instance.sailing,false);
        }

        public static MainShipSaveData[] CapturePersistentShipsSource()
        {
            if(instance==null||instance.ships.Count==0)return Array.Empty<MainShipSaveData>();
            var data=new MainShipSaveData[instance.ships.Count];
            for(int i=0;i<instance.ships.Count;i++)
            {
                Ship s=instance.ships[i];int[] b=new int[s.Blocks.Count*5];
                for(int j=0,o=0;j<s.Blocks.Count;j++)
                {
                    var q=s.Blocks[j];byte sm=SourceCoords.UnityMetaToSource(q.Id,q.Meta);
                    b[o++]=q.X;b[o++]=q.Y;b[o++]=-q.Z;b[o++]=(int)q.Id;b[o++]=sm;
                }
                data[i]=new MainShipSaveData{x=s.X,y=s.Y,z=s.Z,yaw=s.Yaw,fwdOff=s.FwdOff,docked=s.Docked,riding=instance.sailing==s,blocks=b,holdCells=PackLocalSet(s.HoldCells),floodCells=PackLocalSet(s.FloodCells)};
            }
            return data;
        }

        public static void RestorePersistentShipsSource(MainShipSaveData[] data)
        {
            if(instance==null)return;
            for(int i=0;i<instance.ships.Count;i++)MainMovingShipRenderer.Remove(instance.ships[i].Handle);
            for(int i=0;i<instance.ships.Count;i++)instance.ReleaseCarve(instance.ships[i]);
            instance.ships.Clear();instance.mobRides.Clear();instance.sailing=null;instance.shiftLatch=false;
            if(data==null)return;Ship ride=null;
            for(int i=0;i<data.Length;i++)
            {
                MainShipSaveData d=data[i];if(d==null||d.blocks==null||d.blocks.Length<5)continue;
                var s=new Ship{X=d.x,Y=d.y,Z=d.z,Yaw=d.yaw,FwdOff=d.fwdOff,Docked=d.docked};
                for(int o=0;o+4<d.blocks.Length;o+=5)
                {
                    int id=d.blocks[o+3];if(id<=0||id>(int)BlockId.DarkOakSapling)continue;BlockId bid=(BlockId)id;
                    s.Blocks.Add(new MainMovingShipRenderer.ShipBlock(d.blocks[o],d.blocks[o+1],-d.blocks[o+2],bid,SourceCoords.SourceMetaToUnity(bid,(byte)d.blocks[o+4])));
                }
                if(s.Blocks.Count==0)continue;
                UnpackLocalSet(d.holdCells,s.HoldCells);UnpackLocalSet(d.floodCells,s.FloodCells);instance.BuildMetrics(s);
                s.Handle=MainMovingShipRenderer.Create(s.Blocks,instance.RenderPosition(s),s.Yaw);if(s.Handle<0)continue;
                instance.ships.Add(s);if(d.riding&&!d.docked)ride=s;
            }
            if(ride!=null)instance.Board(ride);
        }

        void Update()
        {
            if(world==null||player==null||ships.Count==0)return;
            bool shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            if(sailing!=null&&shift&&!shiftLatch)Dismount(sailing,true);
            shiftLatch=shift;

            float remaining=Mathf.Min(Time.deltaTime,.1f);
            while(remaining>0f)
            {
                float dt=Mathf.Min(.025f,remaining);
                if(sailing!=null)TickSailing(sailing,dt);
                for(int i=0;i<ships.Count;i++)TickBuoyancy(ships[i],dt);
                remaining-=dt;
            }
            floodClock+=Time.deltaTime;if(floodClock>=.2f){floodClock-=.2f;for(int i=0;i<ships.Count;i++)FloodTick(ships[i]);}
            for(int i=0;i<ships.Count;i++){MaybeRecarve(ships[i],false);MainMovingShipRenderer.SetPose(ships[i].Handle,RenderPosition(ships[i]),ships[i].Yaw);}
            CarryAttachedMobs();
            if(sailing!=null)player.ApplyVehicleRidePose(HelmPose(sailing));
        }

        void LateUpdate(){RefreshMobAttachments();}

        void Board(Ship s)
        {
            if(s==null||player==null)return;
            MainSourceObjectRenderer.CancelVehicleRide();
            if(sailing!=null&&sailing!=s)Dismount(sailing,false);
            sailing=s;s.Docked=false;s.VX=s.VZ=s.VYaw=0f;
            RefreshFloodTopology(s);RecomputeBuoyancy(s);MaybeRecarve(s,true);
            player.SetVehicleRiding(true);
            player.SetVehicleYaw(s.Yaw+s.FwdOff);
            player.ApplyVehicleRidePose(HelmPose(s));
        }

        void Dismount(Ship s,bool sourceShift)
        {
            if(s==null)return;
            if(sailing==s)sailing=null;
            s.Docked=true;s.VX=s.VZ=s.VYaw=0f;MaybeRecarve(s,true);
            if(player!=null)
            {
                Vector3 p=HelmPose(s)+Vector3.up*.05f;
                player.ApplyVehicleDismount(p,MainPlayerController.SourceStandingHeight);
            }
            world?.MarkPersistentSaveDirty();
        }

        void TickSailing(Ship s,float dt)
        {
            float steer=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1f:0f)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1f:0f);
            float throttle=(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1f:0f)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1f:0f);
            float oldYaw=s.Yaw,oldX=s.X,oldZ=s.Z;

            // main.js BB() exact tuning/order in source coordinates.
            if(Mathf.Abs(steer)>1e-7f)s.VYaw+=steer*TurnAcc*dt;
            s.VYaw=Mathf.Clamp(s.VYaw,-TurnMax,TurnMax);
            s.Yaw+=s.VYaw*dt;
            s.VYaw*=Mathf.Max(0f,1f-dt*TurnDrag);if(Mathf.Abs(s.VYaw)<.001f)s.VYaw=0f;
            float a=s.Yaw+s.FwdOff,fx=Mathf.Sin(a),fz=-Mathf.Cos(a),forward=s.VX*fx+s.VZ*fz;
            if(throttle>0f&&forward<MaxSpeed){s.VX+=fx*Acc*throttle*dt;s.VZ+=fz*Acc*throttle*dt;}
            else if(throttle<0f&&forward>-MaxSpeed*ReverseMax){s.VX+=fx*Acc*throttle*dt*.5f;s.VZ+=fz*Acc*throttle*dt*.5f;}
            s.VX*=Mathf.Max(0f,1f-dt*Drag);s.VZ*=Mathf.Max(0f,1f-dt*Drag);
            float speed=Mathf.Sqrt(s.VX*s.VX+s.VZ*s.VZ);if(speed>MaxSpeed){s.VX=s.VX/speed*MaxSpeed;s.VZ=s.VZ/speed*MaxSpeed;}
            s.X+=s.VX*dt;s.Z+=s.VZ*dt;

            // main c7(): reject yaw, then X, then Z independently against the world perimeter.
            if(Mathf.Abs(s.Yaw-oldYaw)>1e-8f&&Collides(s,oldX,oldZ,s.Yaw,s.Y,s.Perim))
            {s.Yaw=oldYaw;s.VYaw=0f;}
            if(Mathf.Abs(s.X-oldX)>1e-8f&&Collides(s,s.X,oldZ,s.Yaw,s.Y,s.Perim))
            {s.X=oldX;s.VX=0f;}
            if(Mathf.Abs(s.Z-oldZ)>1e-8f&&Collides(s,s.X,s.Z,s.Yaw,s.Y,s.Perim))
            {s.Z=oldZ;s.VZ=0f;}
            float dyaw=s.Yaw-oldYaw;if(Mathf.Abs(dyaw)>1e-8f)player.AddVehicleYaw(dyaw);
            if(Mathf.Abs(s.X-oldX)+Mathf.Abs(s.Z-oldZ)+Mathf.Abs(dyaw)>1e-7f)world.MarkPersistentSaveDirty();
        }

        void TickBuoyancy(Ship s,float dt)
        {
            if(s.DisplacementY.Count==0)return;
            float waterY=SampleWaterSurface(s),disp=0f;
            for(int i=0;i<s.DisplacementY.Count;i++)
            {
                float q=waterY-(s.Y+s.DisplacementY[i]);disp+=q<0f?0f:q>1f?1f:q;
            }
            float accel=Gravity*(disp-s.Mass)/Mathf.Max(1f,s.Mass);accel=Mathf.Clamp(accel,-20f,20f);
            s.VY+=accel*dt;s.VY*=Mathf.Max(0f,1f-dt*VerticalDrag);s.VY=Mathf.Clamp(s.VY,-MaxVertical,MaxVertical);
            if(Mathf.Abs(s.VY)<.02f&&Mathf.Abs(accel)<.05f)s.VY=0f;
            if(Mathf.Abs(s.VY)<1e-8f)return;
            float ny=s.Y+s.VY*dt;
            if(s.VY<0f&&BottomCollision(s,ny)){s.VY=0f;return;}
            if(Collides(s,s.X,s.Z,s.Yaw,ny,s.PerimV)&&!Collides(s,s.X,s.Z,s.Yaw,s.Y,s.PerimV)){s.VY=0f;return;}
            s.Y=ny;world.MarkPersistentSaveDirty();
        }

        bool CollectConnected(Vector3Int root,List<Vector3Int> cells,out bool tooBig)
        {
            tooBig=false;cells.Clear();
            if(!Structural(world.GetBlock(root.x,root.y,root.z)))return false;
            var seen=new HashSet<long>();var q=new Queue<Vector3Int>();seen.Add(Key(root.x,root.y,root.z));q.Enqueue(root);
            while(q.Count!=0)
            {
                Vector3Int c=q.Dequeue();cells.Add(c);
                for(int i=0;i<6;i++)
                {
                    Vector3Int n=c+N6[i];long k=Key(n.x,n.y,n.z);if(seen.Contains(k))continue;
                    if(!Structural(world.GetBlock(n.x,n.y,n.z)))continue;
                    seen.Add(k);q.Enqueue(n);if(seen.Count>FloodLimit){tooBig=true;return true;}
                }
            }
            return true;
        }

        static bool Structural(BlockId id)=>id!=BlockId.Air&&!BlockRegistry.IsWater(id)&&!BlockRegistry.IsAquatic(id);

        void CarveDetachedWorld(List<Vector3Int> cells,Vector3Int wheel)
        {
            int? surface=FindWaterSurface(wheel.x,wheel.z,wheel.y,40);
            int waterY=surface.HasValue?surface.Value:int.MinValue/2;
            var aquatic=new HashSet<long>();var aquaticCells=new List<Vector3Int>();
            for(int i=0;i<cells.Count;i++)
            {
                Vector3Int c=cells[i];
                for(int n=0;n<6;n++)
                {
                    Vector3Int p=c+N6[n];long k=Key(p.x,p.y,p.z);
                    if(!aquatic.Contains(k)&&BlockRegistry.IsAquatic(world.GetBlock(p.x,p.y,p.z))){aquatic.Add(k);aquaticCells.Add(p);}
                }
            }
            bool oldSuppress=false;MainSourceObjectRenderer.SetFallingHookSuppressed(true);oldSuppress=true;
            try
            {
                for(int i=0;i<cells.Count;i++){Vector3Int c=cells[i];world.SetBlockDeferredPersistent(c.x,c.y,c.z,c.y<waterY?BlockId.Water:BlockId.Air,0);}
                for(int i=0;i<aquaticCells.Count;i++){Vector3Int c=aquaticCells[i];world.SetBlockDeferredPersistent(c.x,c.y,c.z,c.y<waterY?BlockId.Water:BlockId.Air,0);}
            }
            finally{if(oldSuppress)MainSourceObjectRenderer.SetFallingHookSuppressed(false);}
        }

        BlockId LocalBlock(Ship s,int x,int y,int z)
        {
            return s.BlockIndex.TryGetValue(LocalKey(x,y,z),out int i)&&i>=0&&i<s.Blocks.Count?s.Blocks[i].Id:BlockId.Air;
        }

        byte LocalMeta(Ship s,int x,int y,int z)
        {
            return s.BlockIndex.TryGetValue(LocalKey(x,y,z),out int i)&&i>=0&&i<s.Blocks.Count?s.Blocks[i].Meta:(byte)0;
        }

        void ReindexBlocks(Ship s)
        {
            s.BlockIndex.Clear();s.Occupied.Clear();
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];if(b.Id==BlockId.Air)continue;
                s.BlockIndex[LocalKey(b.X,b.Y,b.Z)]=i;
                if(!BlockRegistry.IsWater(b.Id)&&!BlockRegistry.IsAquatic(b.Id))s.Occupied.Add(LocalKey(b.X,b.Y,b.Z));
            }
        }

        bool SetLocalBlock(Ship s,int x,int y,int z,BlockId id,byte meta=0)
        {
            long k=LocalKey(x,y,z);
            if(s.BlockIndex.TryGetValue(k,out int i))
            {
                if(id==BlockId.Air){s.Blocks.RemoveAt(i);ReindexBlocks(s);return true;}
                var b=s.Blocks[i];if(b.Id==id&&b.Meta==meta)return false;b.Id=id;b.Meta=meta;s.Blocks[i]=b;ReindexBlocks(s);return true;
            }
            if(id==BlockId.Air)return false;
            s.Blocks.Add(new MainMovingShipRenderer.ShipBlock(x,y,z,id,meta));ReindexBlocks(s);return true;
        }

        void GeometryChanged(Ship s,bool topology=true)
        {
            BuildMetrics(s);
            s.GeoVersion++;MainMovingShipRenderer.ReplaceGeometry(s.Handle,s.Blocks,s.GeoVersion);MaybeRecarve(s,true);world?.MarkPersistentSaveDirty();
        }

        public static bool TryFillBucketFromDockedShip(Vector3 origin,Vector3 direction,float maxDistance,out string filledItem)
        {
            filledItem=null;if(instance==null||direction.sqrMagnitude<1e-8f)return false;direction.Normalize();Ship bestShip=null;Vector3Int bestCell=default(Vector3Int);BlockId bestId=BlockId.Air;float best=maxDistance+.0001f;
            for(int si=0;si<instance.ships.Count;si++)
            {
                Ship s=instance.ships[si];if(!s.Docked)continue;Matrix4x4 inv=instance.DrawMatrix(s).inverse;Vector3 o=inv.MultiplyPoint3x4(origin),d=inv.MultiplyVector(direction).normalized;Vector3Int last=new Vector3Int(int.MinValue,int.MinValue,int.MinValue);
                for(float r=.3f;r<=maxDistance+.0001f&&r<best;r+=.1f)
                {
                    Vector3 p=o+d*r;Vector3Int c=new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z));if(c==last)continue;last=c;BlockId id=instance.LocalBlock(s,c.x,c.y,c.z);
                    if(id==BlockId.Water||id==BlockId.Lava){best=r;bestShip=s;bestCell=c;bestId=id;break;}
                    if(id!=BlockId.Air&&!BlockRegistry.IsFluid(id)&&!BlockRegistry.IsAquatic(id)&&!BucketPassThrough(id))break;
                }
            }
            if(bestShip==null)return false;instance.SetLocalBlock(bestShip,bestCell.x,bestCell.y,bestCell.z,BlockId.Air,0);instance.GeometryChanged(bestShip,true);filledItem=bestId==BlockId.Water?"item_water_bucket":"item_lava_bucket";return true;
        }

        public static bool TryPlaceBucketFluidOnDockedShip(ShipVoxelHit hit,string itemKey,out Vector3 worldCenter)
        {
            worldCenter=hit.Point;if(instance==null)return false;BlockId fluid=itemKey=="item_water_bucket"?BlockId.Water:itemKey=="item_lava_bucket"?BlockId.Lava:BlockId.Air;if(fluid==BlockId.Air)return false;
            Ship s=instance.FindShip(hit.Handle);if(s==null||!s.Docked)return false;Vector3Int p=hit.Block+hit.Normal;BlockId old=instance.LocalBlock(s,p.x,p.y,p.z);if(old!=BlockId.Air&&!BlockRegistry.IsFluid(old)&&!BlockRegistry.IsAquatic(old)&&!BucketPassThrough(old))return false;
            worldCenter=instance.DrawMatrix(s).MultiplyPoint3x4((Vector3)p+Vector3.one*.5f);if(instance.player!=null){Vector3 feet=instance.player.transform.position;if(Mathf.Abs(worldCenter.x-feet.x)<.8f&&Mathf.Abs(worldCenter.z-feet.z)<.8f&&worldCenter.y>feet.y-.5f&&worldCenter.y<feet.y+2.2f)return false;}
            if(!instance.SetLocalBlock(s,p.x,p.y,p.z,fluid,0))return false;instance.GeometryChanged(s,true);return true;
        }

        public static bool TryUseFlintAndSteelOnDockedShip(ShipVoxelHit hit)
        {
            if(instance==null)return false;Ship s=instance.FindShip(hit.Handle);if(s==null||!s.Docked)return false;BlockId id=instance.LocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z);
            if(id==BlockId.Tnt)
            {
                Vector3 center=instance.DrawMatrix(s).MultiplyPoint3x4((Vector3)hit.Block+Vector3.one*.5f);if(!instance.SetLocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z,BlockId.Air,0))return false;instance.GeometryChanged(s,true);MainSourceObjectRenderer.SpawnDetachedPrimedTnt(center,2f);return true;
            }
            if(id==BlockId.Fire)return false;Vector3Int p=hit.Block+hit.Normal;if(instance.LocalBlock(s,p.x,p.y,p.z)!=BlockId.Air)return false;if(!instance.SetLocalBlock(s,p.x,p.y,p.z,BlockId.Fire,0))return false;instance.GeometryChanged(s,true);return true;
        }

        static bool BucketPassThrough(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsAquatic(id))return true;BlockShape sh=BlockRegistry.Get(id).Shape;return sh==BlockShape.Cross||sh==BlockShape.TallPlant||id==BlockId.Vine||id==BlockId.GlowLichen||id==BlockId.Fire;
        }

        public struct ShipExplosionVoxel
        {
            public int Handle; public Vector3Int Block; public BlockId Id;
        }

        // main ta() bridge used by B2(): when a world ray is in AIR, query the moving vessel voxel.
        public static bool TryGetExplosionVoxel(Vector3 worldPoint,out ShipExplosionVoxel voxel)
        {
            voxel=default(ShipExplosionVoxel);if(instance==null)return false;
            for(int i=0;i<instance.ships.Count;i++)
            {
                Ship s=instance.ships[i];if(!instance.WithinShipEnvelope(s,worldPoint))continue;Vector3 lp=instance.DrawMatrix(s).inverse.MultiplyPoint3x4(worldPoint);Vector3Int c=new Vector3Int(Mathf.FloorToInt(lp.x),Mathf.FloorToInt(lp.y),Mathf.FloorToInt(lp.z));BlockId id=instance.LocalBlock(s,c.x,c.y,c.z);if(id==BlockId.Air)continue;
                voxel=new ShipExplosionVoxel{Handle=s.Handle,Block=c,Id=id};return true;
            }
            return false;
        }

        public static void ApplyExplosionCells(Dictionary<int,HashSet<Vector3Int>> cells)
        {
            if(instance==null||cells==null||cells.Count==0)return;
            foreach(var pair in cells)
            {
                Ship s=instance.FindShip(pair.Key);if(s==null)continue;bool changed=false;var primed=new List<Vector3>(4);
                foreach(Vector3Int c in pair.Value)
                {
                    BlockId id=instance.LocalBlock(s,c.x,c.y,c.z);if(id==BlockId.Air||id==BlockId.Bedrock||BlockRegistry.IsWater(id))continue;Vector3 center=instance.DrawMatrix(s).MultiplyPoint3x4((Vector3)c+Vector3.one*.5f);
                    if(id==BlockId.Tnt)primed.Add(center);
                    if(instance.SetLocalBlock(s,c.x,c.y,c.z,BlockId.Air,0)){changed=true;if(id!=BlockId.Tnt&&UnityEngine.Random.value<.12f)MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(center.x),Mathf.FloorToInt(center.y),Mathf.FloorToInt(center.z),id,2);}
                }
                if(changed)instance.GeometryChanged(s,true);for(int i=0;i<primed.Count;i++)MainSourceObjectRenderer.SpawnDetachedPrimedTnt(primed[i],.1f+UnityEngine.Random.value*.25f);
            }
        }

        public static bool TryBreakDockedBlock(ShipVoxelHit hit,out BlockId broken,out Vector3 worldCenter)
        {
            broken=BlockId.Air;worldCenter=hit.Point;if(instance==null)return false;
            Ship s=instance.FindShip(hit.Handle);if(s==null||!s.Docked)return false;
            broken=instance.LocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z);if(broken==BlockId.Air||broken==BlockId.Bedrock)return false;
            byte oldMeta=instance.LocalMeta(s,hit.Block.x,hit.Block.y,hit.Block.z);worldCenter=instance.DrawMatrix(s).MultiplyPoint3x4((Vector3)hit.Block+Vector3.one*.5f);
            if(!instance.SetLocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z,BlockId.Air,0))return false;
            // main eJ()/gT(): breaking either half of a door removes the paired half too.
            if(BlockRegistry.Get(broken).Shape==BlockShape.Door)
            {
                int py=((oldMeta>>2)&1)!=0?hit.Block.y-1:hit.Block.y+1;
                if(instance.LocalBlock(s,hit.Block.x,py,hit.Block.z)==broken)instance.SetLocalBlock(s,hit.Block.x,py,hit.Block.z,BlockId.Air,0);
            }
            instance.GeometryChanged(s,true);return true;
        }

        public static bool TryInteractDockedBlock(ShipVoxelHit hit)
        {
            if(instance==null)return false;Ship s=instance.FindShip(hit.Handle);if(s==null||!s.Docked)return false;
            BlockId id=instance.LocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z);if(id==BlockId.Air)return false;
            if(id==BlockId.ShipWheel){instance.Board(s);return true;}
            BlockShape shape=BlockRegistry.Get(id).Shape;if(shape!=BlockShape.Door&&shape!=BlockShape.Gate&&shape!=BlockShape.Trapdoor)return false;
            byte meta=instance.LocalMeta(s,hit.Block.x,hit.Block.y,hit.Block.z);byte toggled=(byte)(meta^8);
            instance.SetLocalBlock(s,hit.Block.x,hit.Block.y,hit.Block.z,id,toggled);
            // main yL()/ep(): both door halves share the open bit.
            if(shape==BlockShape.Door)
            {
                int py=((meta>>2)&1)!=0?hit.Block.y-1:hit.Block.y+1;
                if(instance.LocalBlock(s,hit.Block.x,py,hit.Block.z)==id)
                {byte pm=instance.LocalMeta(s,hit.Block.x,py,hit.Block.z);instance.SetLocalBlock(s,hit.Block.x,py,hit.Block.z,id,(byte)(pm^8));}
            }
            instance.GeometryChanged(s,false);return true;
        }

        public static bool TryPlaceDockedBlock(ShipVoxelHit hit,BlockId id,byte meta,out Vector3 worldCenter)
        {
            worldCenter=hit.Point;if(instance==null||id==BlockId.Air)return false;
            Ship s=instance.FindShip(hit.Handle);if(s==null||!s.Docked)return false;
            Vector3Int p=hit.Block+hit.Normal;if(instance.LocalBlock(s,p.x,p.y,p.z)!=BlockId.Air)return false;
            worldCenter=instance.DrawMatrix(s).MultiplyPoint3x4((Vector3)p+Vector3.one*.5f);
            if(instance.player!=null)
            {
                Vector3 feet=instance.player.transform.position;
                if(Mathf.Abs(worldCenter.x-feet.x)<.8f&&Mathf.Abs(worldCenter.z-feet.z)<.8f&&worldCenter.y>feet.y-.5f&&worldCenter.y<feet.y+2.2f)return false;
            }
            if(!instance.SetLocalBlock(s,p.x,p.y,p.z,id,meta))return false;
            instance.GeometryChanged(s,true);return true;
        }

        Ship FindShip(int handle){for(int i=0;i<ships.Count;i++)if(ships[i].Handle==handle)return ships[i];return null;}

        bool RaycastShip(Ship s,Vector3 origin,Vector3 direction,float maxDistance,out ShipVoxelHit hit)
        {
            hit=default(ShipVoxelHit);Matrix4x4 inv=DrawMatrix(s).inverse;
            Vector3 o=inv.MultiplyPoint3x4(origin),d=inv.MultiplyVector(direction).normalized;bool found=false;float best=maxDistance;
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];if(b.Id==BlockId.Air||BlockRegistry.IsWater(b.Id)||BlockRegistry.IsLava(b.Id)||BlockRegistry.IsAquatic(b.Id))continue;
                VoxelBox vb=VoxelShapes.Selection(b.Id,b.Meta);Vector3 mn=new Vector3(b.X+vb.X0,b.Y+vb.Y0,b.Z+vb.Z0),mx=new Vector3(b.X+vb.X1,b.Y+vb.Y1,b.Z+vb.Z1);
                if(!RayAabbNormal(o,d,mn,mx,best,out float t,out Vector3Int n))continue;
                if(t<0f||t>best)continue;best=t;found=true;
                Vector3 localPoint=o+d*t;Vector3 wp=DrawMatrix(s).MultiplyPoint3x4(localPoint);
                hit=new ShipVoxelHit{Handle=s.Handle,Block=new Vector3Int(b.X,b.Y,b.Z),Normal=n,Id=b.Id,Meta=b.Meta,Point=wp,Distance=Vector3.Distance(origin,wp)};
            }
            return found;
        }

        static bool RayAabbNormal(Vector3 o,Vector3 d,Vector3 mn,Vector3 mx,float max,out float hit,out Vector3Int normal)
        {
            float t0=0f,t1=max;normal=Vector3Int.zero;Vector3Int enter=Vector3Int.zero;
            if(!SlabNormal(o.x,d.x,mn.x,mx.x,Vector3Int.left,Vector3Int.right,ref t0,ref t1,ref enter) ||
               !SlabNormal(o.y,d.y,mn.y,mx.y,Vector3Int.down,Vector3Int.up,ref t0,ref t1,ref enter) ||
               !SlabNormal(o.z,d.z,mn.z,mx.z,new Vector3Int(0,0,-1),new Vector3Int(0,0,1),ref t0,ref t1,ref enter)){hit=0;return false;}
            hit=t0;normal=enter;return hit>=0f&&hit<=max;
        }

        static bool SlabNormal(float o,float d,float mn,float mx,Vector3Int nMin,Vector3Int nMax,ref float t0,ref float t1,ref Vector3Int enter)
        {
            if(Mathf.Abs(d)<1e-8f)return o>=mn&&o<=mx;
            float a=(mn-o)/d,b=(mx-o)/d;Vector3Int na=nMin,nb=nMax;
            if(a>b){float q=a;a=b;b=q;Vector3Int nq=na;na=nb;nb=nq;}
            if(a>t0){t0=a;enter=na;}if(b<t1)t1=b;return t0<=t1;
        }

        static int[] PackLocalSet(HashSet<long> set)
        {
            if(set==null||set.Count==0)return Array.Empty<int>();int[] a=new int[set.Count*3];int o=0;
            foreach(long k in set){DecodeLocalKey(k,out int x,out int y,out int z);a[o++]=x;a[o++]=y;a[o++]=-z;}return a;
        }
        static void UnpackLocalSet(int[] a,HashSet<long> set)
        {
            set.Clear();if(a==null)return;for(int i=0;i+2<a.Length;i+=3)set.Add(LocalKey(a[i],a[i+1],-a[i+2]));
        }
        static void DecodeLocalKey(long k,out int x,out int y,out int z)
        {z=(int)(k&0xFFFF)-8192;y=(int)((k>>16)&0xFFF)-512;x=(int)(k>>28)-8192;}

        void RefreshFloodTopology(Ship s)
        {
            s.WaterY=SampleWaterSurface(s);if(s.Blocks.Count==0)return;
            int minX=int.MaxValue,maxX=int.MinValue,minY=int.MaxValue,maxY=int.MinValue,minZ=int.MaxValue,maxZ=int.MinValue;
            var seal=new HashSet<long>();
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];minX=Mathf.Min(minX,b.X);maxX=Mathf.Max(maxX,b.X);minY=Mathf.Min(minY,b.Y);maxY=Mathf.Max(maxY,b.Y);minZ=Mathf.Min(minZ,b.Z);maxZ=Mathf.Max(maxZ,b.Z);
                if(!BlockRegistry.IsWater(b.Id)&&!BlockRegistry.IsAquatic(b.Id)&&SealsAir(b.Id))seal.Add(LocalKey(b.X,b.Y,b.Z));
            }
            int waterLocal=Mathf.CeilToInt(s.WaterY-1f-s.Y);int x0=minX-1,x1=maxX+1,y0=minY-1,y1=Mathf.Min(maxY+1,waterLocal),z0=minZ-1,z1=maxZ+1;
            if(y1<y0)return;long volume=(long)(x1-x0+1)*(y1-y0+1)*(z1-z0+1);if(volume>180000)return;
            s.LeakCells.Clear();s.OutsideCells.Clear();var q=new Queue<Vector3Int>();
            Action<int,int,int> seed=(x,y,z)=>{if(x<x0||x>x1||y<y0||y>y1||z<z0||z>z1)return;long k=LocalKey(x,y,z);if(seal.Contains(k)||!s.LeakCells.Add(k))return;q.Enqueue(new Vector3Int(x,y,z));};
            for(int y=y0;y<=y1;y++){for(int x=x0;x<=x1;x++){seed(x,y,z0);seed(x,y,z1);}for(int z=z0;z<=z1;z++){seed(x0,y,z);seed(x1,y,z);}}
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)seed(x,y0,z);
            while(q.Count>0){var c=q.Dequeue();for(int i=0;i<6;i++){var n=c+N6[i];seed(n.x,n.y,n.z);}}
            var prune=new List<long>();foreach(long k in s.HoldCells){DecodeLocalKey(k,out int x,out int y,out int z);if(x<minX||x>maxX||y<minY||y>y1||z<minZ||z>maxZ||seal.Contains(k))prune.Add(k);}for(int i=0;i<prune.Count;i++)s.HoldCells.Remove(prune[i]);
            // A7() has a second outside flood that treats retained holdCells as barriers. This is what lets a breached hull flood gradually instead of erasing all retained air in one topology pass.
            var oq=new Queue<Vector3Int>();
            Action<int,int,int> oseed=(x,y,z)=>{if(x<x0||x>x1||y<y0||y>y1||z<z0||z>z1)return;long k=LocalKey(x,y,z);if(seal.Contains(k)||s.HoldCells.Contains(k)||!s.OutsideCells.Add(k))return;oq.Enqueue(new Vector3Int(x,y,z));};
            for(int y=y0;y<=y1;y++){for(int x=x0;x<=x1;x++){oseed(x,y,z0);oseed(x,y,z1);}for(int z=z0;z<=z1;z++){oseed(x0,y,z);oseed(x1,y,z);}}
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)oseed(x,y0,z);
            while(oq.Count>0){var c=oq.Dequeue();for(int i=0;i<6;i++){var n=c+N6[i];oseed(n.x,n.y,n.z);}}
            for(int x=minX;x<=maxX;x++)for(int y=minY;y<=Mathf.Min(maxY,y1);y++)for(int z=minZ;z<=maxZ;z++)
            {
                long k=LocalKey(x,y,z);BlockId id=LocalBlock(s,x,y,z);
                if(!seal.Contains(k)&&!s.LeakCells.Contains(k)&&id==BlockId.Air)s.HoldCells.Add(k);
                if(!seal.Contains(k)&&!s.OutsideCells.Contains(k)&&!s.HoldCells.Contains(k)&&(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsAquatic(id)))s.HoldCells.Add(k);
            }
            prune.Clear();foreach(long k in s.FloodCells){DecodeLocalKey(k,out int x,out int y,out int z);if(!BlockRegistry.IsWater(LocalBlock(s,x,y,z)))prune.Add(k);}for(int i=0;i<prune.Count;i++)s.FloodCells.Remove(prune[i]);
        }

        void FloodTick(Ship s)
        {
            RefreshFloodTopology(s);int waterLocal=Mathf.CeilToInt(s.WaterY-1f-s.Y);var flood=new List<long>();var drain=new List<long>();
            foreach(long k in s.HoldCells){DecodeLocalKey(k,out int x,out int y,out int z);if(y<=waterLocal&&s.LeakCells.Contains(k)&&LocalBlock(s,x,y,z)==BlockId.Air)flood.Add(k);}
            foreach(long k in s.FloodCells)if(!s.LeakCells.Contains(k))drain.Add(k);
            flood.Sort((a,b)=>{DecodeLocalKey(a,out int ax,out int ay,out int az);DecodeLocalKey(b,out int bx,out int by,out int bz);return ay.CompareTo(by);});
            drain.Sort((a,b)=>{DecodeLocalKey(a,out int ax,out int ay,out int az);DecodeLocalKey(b,out int bx,out int by,out int bz);return by.CompareTo(ay);});
            int changed=0;
            for(int i=0;i<flood.Count&&changed<FloodPerTick;i++){long k=flood[i];DecodeLocalKey(k,out int x,out int y,out int z);if(SetLocalBlock(s,x,y,z,BlockId.Water,0)){s.HoldCells.Remove(k);s.FloodCells.Add(k);changed++;}}
            for(int i=0;i<drain.Count&&changed<FloodPerTick;i++){long k=drain[i];DecodeLocalKey(k,out int x,out int y,out int z);if(SetLocalBlock(s,x,y,z,BlockId.Air,0)){s.FloodCells.Remove(k);s.HoldCells.Add(k);changed++;}}
            if(changed>0){BuildMetrics(s);s.GeoVersion++;MainMovingShipRenderer.ReplaceGeometry(s.Handle,s.Blocks,s.GeoVersion);MaybeRecarve(s,true);world?.MarkPersistentSaveDirty();}
        }

        void MaybeRecarve(Ship s,bool force)
        {
            float now=Time.realtimeSinceStartup;if(!force&&now-s.LastCarveAt<CarveInterval)return;
            float move=s.HasCarvePose?Mathf.Abs(s.X-s.LastCarveX)+Mathf.Abs(s.Y-s.LastCarveY)+Mathf.Abs(s.Z-s.LastCarveZ):999f;
            float yaw=s.HasCarvePose?Mathf.Abs(Mathf.DeltaAngle(s.LastCarveYaw*Mathf.Rad2Deg,s.Yaw*Mathf.Rad2Deg))*Mathf.Deg2Rad:999f;
            if(!force&&move<=CarveMoveThreshold&&yaw<=CarveYawThreshold)return;Recarve(s);s.LastCarveAt=now;s.LastCarveX=s.X;s.LastCarveY=s.Y;s.LastCarveZ=s.Z;s.LastCarveYaw=s.Yaw;s.HasCarvePose=true;
        }

        void Recarve(Ship s)
        {
            if(world==null)return;var next=new HashSet<long>();Matrix4x4 m=DrawMatrix(s);Vector3 look=new Vector3(s.VX,0,-s.VZ)*CarveLookAhead;
            float[] f={.125f,.375f,.625f,.875f};float[] fy={.17f,.5f,.83f};
            Action<float,float,float> sample=(x,y,z)=>{Vector3 p=m.MultiplyPoint3x4(new Vector3(x,y,z))+look;int wx=Mathf.FloorToInt(p.x),wy=Mathf.FloorToInt(p.y),wz=Mathf.FloorToInt(p.z);if(wy>=VoxelConstants.MinY&&wy<=VoxelConstants.MaxY)next.Add(Key(wx,wy,wz));};
            for(int i=0;i<s.Blocks.Count;i++){var b=s.Blocks[i];if(b.Id==BlockId.Air)continue;for(int a=0;a<4;a++)for(int c=0;c<4;c++)for(int d=0;d<3;d++)sample(b.X+f[a],b.Y+fy[d],b.Z+f[c]);}
            foreach(long k in s.HoldCells){DecodeLocalKey(k,out int x,out int y,out int z);for(int a=0;a<4;a++)for(int c=0;c<4;c++)for(int d=0;d<3;d++)sample(x+f[a],y+fy[d],z+f[c]);}
            foreach(long k in s.ReservedWorld)if(!next.Contains(k))ReleaseReserved(k);
            foreach(long k in next)if(!s.ReservedWorld.Contains(k))AcquireReserved(s,k);
            s.ReservedWorld.Clear();foreach(long k in next)s.ReservedWorld.Add(k);
        }

        void AcquireReserved(Ship s,long k)
        {
            DecodeWorldKey(k,out int x,out int y,out int z);reservedWorld.TryGetValue(k,out int count);reservedWorld[k]=count+1;if(count>0)return;
            BlockId id=world.GetBlock(x,y,z);byte meta=world.GetMeta(x,y,z);
            // main Ns(): aquatic decoration is first normalized to WATER below gi(ship), AIR above it; only water is then carved/cached.
            if(BlockRegistry.IsAquatic(id))
            {
                BlockId q=y<Mathf.CeilToInt(s.WaterY)?BlockId.Water:BlockId.Air;world.SetBlock(x,y,z,q,0,false);id=q;meta=0;
            }
            if(!BlockRegistry.IsWater(id))return;
            carvedWorld[k]=new WorldCarveState{Id=id,Meta=meta,Owners=1};world.SetBlock(x,y,z,BlockId.Air,0,false);
        }
        void ReleaseReserved(long k)
        {
            if(!reservedWorld.TryGetValue(k,out int count))return;if(count>1){reservedWorld[k]=count-1;return;}reservedWorld.Remove(k);
            if(carvedWorld.TryGetValue(k,out var c)){DecodeWorldKey(k,out int x,out int y,out int z);if(world.GetBlock(x,y,z)==BlockId.Air)world.SetBlock(x,y,z,c.Id,c.Meta,false);carvedWorld.Remove(k);}
        }
        void ReleaseCarve(Ship s){if(s==null)return;var a=new List<long>(s.ReservedWorld);for(int i=0;i<a.Count;i++)ReleaseReserved(a[i]);s.ReservedWorld.Clear();s.HasCarvePose=false;}
        void OnDestroy(){for(int i=0;i<ships.Count;i++)ReleaseCarve(ships[i]);mobRides.Clear();if(instance==this)instance=null;}
        static void DecodeWorldKey(long k,out int x,out int y,out int z)
        {int yy=(int)(k&0x1FFFFF);long zz=(k>>21)&0x1FFFFF;long xx=(k>>42)&0x1FFFFF;x=(int)xx-1048576;z=(int)zz-1048576;y=yy+VoxelConstants.MinY;}

        void CarryAttachedMobs()
        {
            if(mobRides.Count==0)return;var dead=new List<MobAI>();
            foreach(var kv in mobRides)
            {
                MobAI mob=kv.Key;MobRide r=kv.Value;if(mob==null||mob.IsDead||r.Ship==null||!ships.Contains(r.Ship)){dead.Add(mob);continue;}
                Ship s=r.Ship;float dyaw=s.Yaw-r.LastYaw;mob.transform.position=DrawMatrix(s).MultiplyPoint3x4(r.Local);
                if(Mathf.Abs(dyaw)>1e-8f)mob.transform.rotation=Quaternion.AngleAxis(dyaw*Mathf.Rad2Deg,Vector3.up)*mob.transform.rotation;
                r.LastYaw=s.Yaw;
            }
            for(int i=0;i<dead.Count;i++)mobRides.Remove(dead[i]);
        }

        void RefreshMobAttachments()
        {
            if(ships.Count==0||MobAI.Active.Count==0){mobRides.Clear();return;}
            for(int mi=0;mi<MobAI.Active.Count;mi++)
            {
                MobAI mob=MobAI.Active[mi];if(mob==null||mob.IsDead||mob.Def.Aquatic){if(mob!=null)mobRides.Remove(mob);continue;}
                Ship support=FindMobSupport(mob.transform.position);
                if(support==null&&mobRides.TryGetValue(mob,out MobRide old)&&old.Ship!=null&&ships.Contains(old.Ship)&&WithinShipEnvelope(old.Ship,mob.transform.position))support=old.Ship;
                if(support==null){mobRides.Remove(mob);continue;}
                if(!mobRides.TryGetValue(mob,out MobRide ride)){ride=new MobRide();mobRides[mob]=ride;}
                ride.Ship=support;ride.Local=DrawMatrix(support).inverse.MultiplyPoint3x4(mob.transform.position);ride.LastYaw=support.Yaw;
            }
        }

        Ship FindMobSupport(Vector3 pos)
        {
            for(int i=0;i<ships.Count;i++)
            {
                Ship s=ships[i];if(!WithinShipEnvelope(s,pos))continue;Vector3 lp=DrawMatrix(s).inverse.MultiplyPoint3x4(pos);int x=Mathf.FloorToInt(lp.x),z=Mathf.FloorToInt(lp.z);
                for(int y=Mathf.FloorToInt(lp.y-.02f);y>=Mathf.FloorToInt(lp.y-.4f)-1;y--)
                {
                    BlockId id=LocalBlock(s,x,y,z);if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsAquatic(id)||BlockRegistry.IsLava(id))continue;
                    VoxelBox b=VoxelShapes.Selection(id,LocalMeta(s,x,y,z));float top=y+b.Y1;if(lp.y>=top-.18f&&lp.y<=top+.36f)return s;
                }
            }
            return null;
        }
        bool WithinShipEnvelope(Ship s,Vector3 p)
        {
            Vector3 rp=RenderPosition(s);float r=Mathf.Max(s.HalfX,s.HalfZ)+2f;
            return p.x>=rp.x-r&&p.x<=rp.x+r&&p.z>=rp.z-r&&p.z<=rp.z+r&&p.y>=s.Y+s.MinLY-2f&&p.y<=s.Y+s.MinLY+s.Height+4f;
        }

        int? FindWaterSurface(int x,int z,int startY,int depth)
        {
            int found=int.MinValue;
            for(int i=0;i<=depth;i++)
            {
                int y=startY-i;if(y<VoxelConstants.MinY)break;
                BlockId id=world.GetBlock(x,y,z);if(BlockRegistry.IsWater(id)||BlockRegistry.IsAquatic(id)){found=y;break;}
            }
            if(found==int.MinValue)return null;
            while(found<VoxelConstants.MaxY)
            {
                BlockId id=world.GetBlock(x,found+1,z);if(!BlockRegistry.IsWater(id)&&!BlockRegistry.IsAquatic(id))break;found++;
            }
            return found+1;
        }

        void BuildMetrics(Ship s)
        {
            ReindexBlocks(s);int minY=int.MaxValue,maxY=int.MinValue;float hx=1f,hz=1f;
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];minY=Mathf.Min(minY,b.Y);maxY=Mathf.Max(maxY,b.Y);hx=Mathf.Max(hx,Mathf.Abs(b.X)+1);hz=Mathf.Max(hz,Mathf.Abs(b.Z)+1);
            }
            s.MinLY=minY==int.MaxValue?0:minY;s.Height=maxY==int.MinValue?1:maxY-s.MinLY+1;s.HalfX=hx;s.HalfZ=hz;
            s.Perim.Clear();s.PerimV.Clear();
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];if(BlockRegistry.IsWater(b.Id))continue;int x=b.X,y=b.Y,z=b.Z;
                if(!s.Occupied.Contains(LocalKey(x+1,y,z))||!s.Occupied.Contains(LocalKey(x-1,y,z))||!s.Occupied.Contains(LocalKey(x,y,z+1))||!s.Occupied.Contains(LocalKey(x,y,z-1)))s.Perim.Add(new Vector3Int(x,y,z));
                if(!s.Occupied.Contains(LocalKey(x,y+1,z))||!s.Occupied.Contains(LocalKey(x,y-1,z)))s.PerimV.Add(new Vector3Int(x,y,z));
            }
            // ms(): wheel determines fwdOff and an empty cell immediately behind the helm is preferred as seat.
            s.HasHelmSeat=false;s.WheelCell=Vector3Int.zero;
            for(int i=0;i<s.Blocks.Count;i++)if(s.Blocks[i].Id==BlockId.ShipWheel)
            {
                var b=s.Blocks[i];s.WheelCell=new Vector3Int(b.X,b.Y,b.Z);byte sm=SourceCoords.UnityMetaToSource(BlockId.ShipWheel,b.Meta);s.FwdOff=ForwardOffsets[sm&3];
                int sx=b.X-Mathf.RoundToInt(Mathf.Sin(s.FwdOff));
                int sourceSeatZ=-b.Z+Mathf.RoundToInt(Mathf.Cos(s.FwdOff));int sz=-sourceSeatZ;
                if(!s.Occupied.Contains(LocalKey(sx,b.Y,sz))){s.HasHelmSeat=true;s.HelmSeat=new Vector3Int(sx,b.Y,sz);}break;
            }
            RefreshFloodTopology(s);RecomputeBuoyancy(s);
        }

        void RecomputeBuoyancy(Ship s)
        {
            s.Mass=0f;s.DisplacementY.Clear();
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];if(BlockRegistry.IsWater(b.Id)||BlockRegistry.IsAquatic(b.Id))continue;s.Mass+=BlockWeight(b.Id);s.DisplacementY.Add(b.Y);
            }
            foreach(long k in s.HoldCells){DecodeLocalKey(k,out int x,out int y,out int z);if(LocalBlock(s,x,y,z)==BlockId.Air)s.DisplacementY.Add(y);}
        }

        void AddEnclosedAirDisplacement(Ship s,float waterY)
        {
            if(s.Blocks.Count==0)return;
            int minX=int.MaxValue,maxX=int.MinValue,minY=int.MaxValue,maxY=int.MinValue,minZ=int.MaxValue,maxZ=int.MinValue;
            var seal=new HashSet<long>();
            for(int i=0;i<s.Blocks.Count;i++)
            {
                var b=s.Blocks[i];minX=Mathf.Min(minX,b.X);maxX=Mathf.Max(maxX,b.X);minY=Mathf.Min(minY,b.Y);maxY=Mathf.Max(maxY,b.Y);minZ=Mathf.Min(minZ,b.Z);maxZ=Mathf.Max(maxZ,b.Z);
                if(!BlockRegistry.IsWater(b.Id)&&SealsAir(b.Id))seal.Add(LocalKey(b.X,b.Y,b.Z));
            }
            int limit=Mathf.Min(maxY+1,Mathf.CeilToInt(waterY-1f-s.Y));int y0=minY-1;if(limit<y0)return;
            int x0=minX-1,x1=maxX+1,z0=minZ-1,z1=maxZ+1;
            long volume=(long)(x1-x0+1)*(limit-y0+1)*(z1-z0+1);if(volume>180000)return; // defensive only; source q4 hulls stay well below this.
            var outside=new HashSet<long>();var q=new Queue<Vector3Int>();
            Action<int,int,int> seed=(x,y,z)=>{if(x<x0||x>x1||y<y0||y>limit||z<z0||z>z1)return;long k=LocalKey(x,y,z);if(seal.Contains(k)||!outside.Add(k))return;q.Enqueue(new Vector3Int(x,y,z));};
            for(int y=y0;y<=limit;y++){for(int x=x0;x<=x1;x++){seed(x,y,z0);seed(x,y,z1);}for(int z=z0;z<=z1;z++){seed(x0,y,z);seed(x1,y,z);}}
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)seed(x,y0,z);
            while(q.Count!=0){Vector3Int c=q.Dequeue();for(int i=0;i<6;i++){Vector3Int n=c+N6[i];seed(n.x,n.y,n.z);}}
            for(int x=minX;x<=maxX;x++)for(int y=minY;y<=Mathf.Min(maxY,limit);y++)for(int z=minZ;z<=maxZ;z++)
            {long k=LocalKey(x,y,z);if(!seal.Contains(k)&&!outside.Contains(k))s.DisplacementY.Add(y);}
        }

        static bool SealsAir(BlockId id)
        {
            BlockShape sh=BlockRegistry.Get(id).Shape;
            return id!=BlockId.Torch&&sh!=BlockShape.Cross&&sh!=BlockShape.TallPlant;
        }

        float SampleWaterSurface(Ship s)
        {
            Vector3 pos=RenderPosition(s);Quaternion q=Quaternion.Euler(0,s.Yaw*Mathf.Rad2Deg,0);float x=s.HalfX-.5f,z=s.HalfZ-.5f;
            Vector2[] p={Vector2.zero,new Vector2(x,z),new Vector2(x,-z),new Vector2(-x,z),new Vector2(-x,-z)};int start=Mathf.FloorToInt(s.Y+s.MinLY);int? best=null;
            for(int i=0;i<p.Length;i++)
            {
                Vector3 d=q*new Vector3(p[i].x,0,p[i].y);int wx=Mathf.FloorToInt(pos.x+Pivot+d.x),wz=Mathf.FloorToInt(pos.z+Pivot+d.z);int? w=FindWaterSurface(wx,wz,start+1,28);
                if(w.HasValue&&(!best.HasValue||w.Value>best.Value))best=w;
            }
            return best.HasValue?best.Value:VoxelConstants.SeaLevel+1;
        }

        bool BottomCollision(Ship s,float newY)
        {
            Vector3 pos=RenderPosition(s);Quaternion q=Quaternion.Euler(0,s.Yaw*Mathf.Rad2Deg,0);float x=s.HalfX-.5f,z=s.HalfZ-.5f;Vector2[] p={Vector2.zero,new Vector2(x,z),new Vector2(x,-z),new Vector2(-x,z),new Vector2(-x,-z)};int y=Mathf.FloorToInt(newY+s.MinLY-.08f);
            for(int i=0;i<p.Length;i++){Vector3 d=q*new Vector3(p[i].x,0,p[i].y);if(SourceFullCollision(world.GetBlock(Mathf.FloorToInt(pos.x+Pivot+d.x),y,Mathf.FloorToInt(pos.z+Pivot+d.z))))return true;}return false;
        }

        bool Collides(Ship s,float sourceX,float sourceZ,float yaw,float y,List<Vector3Int> perimeter)
        {
            if(perimeter==null||perimeter.Count==0)return false;Vector3 pos=new Vector3(sourceX,y,-sourceZ-1f);Quaternion q=Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0);
            for(int i=0;i<perimeter.Count;i++)
            {
                Vector3Int b=perimeter[i];Vector3 c=pos+new Vector3(Pivot,b.y+.5f,Pivot)+q*new Vector3(b.x,0,b.z);
                if(SourceFullCollision(world.GetBlock(Mathf.FloorToInt(c.x),Mathf.FloorToInt(c.y),Mathf.FloorToInt(c.z))))return true;
            }
            return false;
        }

        static bool SourceFullCollision(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id))return false;
            BlockShape sh=BlockRegistry.Get(id).Shape;
            return sh==BlockShape.Cube;
        }

        Vector3 HelmPose(Ship s)
        {
            Vector3 local=s.HasHelmSeat?new Vector3(s.HelmSeat.x+Pivot,s.HelmSeat.y+.02f,s.HelmSeat.z+Pivot):new Vector3(s.WheelCell.x+Pivot,s.WheelCell.y+HelmFallbackHeight,s.WheelCell.z+Pivot);
            return DrawMatrix(s).MultiplyPoint3x4(local);
        }

        Matrix4x4 DrawMatrix(Ship s)
        {
            Vector3 p=RenderPosition(s);
            return Matrix4x4.Translate(p+new Vector3(Pivot,0,Pivot))*Matrix4x4.Rotate(Quaternion.Euler(0,s.Yaw*Mathf.Rad2Deg,0))*Matrix4x4.Translate(new Vector3(-Pivot,0,-Pivot));
        }
        Vector3 RenderPosition(Ship s)=>new Vector3(s.X,s.Y,-s.Z-1f);

        static float BlockWeight(BlockId id)
        {
            string n=id.ToString().ToUpperInvariant();if(n=="AIR"||n.Contains("WATER")||n.Contains("FLOW")||n.Contains("LAVA"))return 0f;float w;
            if(Has(n,"IRON","GOLD","DIAMOND","EMERALD","NETHERITE","COPPER","ANVIL","BELL"))w=7f;
            else if(Has(n,"STONE","COBBLE","DEEPSLATE","BRICK","ANDESITE","DIORITE","GRANITE","TUFF","BLACKSTONE","OBSIDIAN","SANDSTONE","TERRA","CONCRETE","QUARTZ","PRISMARINE","CALCITE","PURPUR","ENDSTONE","NETHERRACK","BEDROCK","ORE","MOSSY","CHISELED","SMOOTH","POLISHED","MAGMA","FURNACE"))w=2.6f;
            else if(Has(n,"DIRT","GRASS","GRAVEL","SAND","PODZOL","CLAY","MUD","SNOW"))w=1.6f;
            else if(Has(n,"LOG","PLANK","WOOD","FENCE","GATE","DOOR","TRAPDOOR","STAIRS","SLAB","LADDER","BOOKSHELF","CHEST","BARREL","SIGN","WHEEL","CRAFTING","LOOM","SCAFFOLD"))w=.6f;
            else if(Has(n,"WOOL","LEAVES","CARPET","FLOWER","SAPLING","FERN","BUSH","GRASS_","VINE","KELP","SEAGRASS","LILY","CACTUS","TORCH","GLASS","ICE","GLOW","LANTERN"))w=.25f;
            else w=1.5f;
            if(n.Contains("SLAB"))w*=.5f;else if(n.Contains("STAIRS"))w*=.75f;else if(n.Contains("FENCE")||n.Contains("GATE"))w*=.25f;else if(n.Contains("DOOR")||n.Contains("TRAPDOOR"))w*=.2f;else if(n.Contains("LADDER")||n.Contains("SIGN"))w*=.1f;
            return w;
        }
        static bool Has(string s,params string[] p){for(int i=0;i<p.Length;i++)if(s.Contains(p[i]))return true;return false;}
        static long LocalKey(int x,int y,int z)=>((long)(x+8192)<<28)^((long)(y+512)<<16)^(ushort)(z+8192);
        static long Key(int x,int y,int z)=>((long)(x+1048576)<<42)^((long)(z+1048576)<<21)^(uint)(y-VoxelConstants.MinY);

        static bool RayAabb(Vector3 o,Vector3 d,Vector3 mn,Vector3 mx,float max,out float hit)
        {
            float t0=0f,t1=max;if(!Slab(o.x,d.x,mn.x,mx.x,ref t0,ref t1)||!Slab(o.y,d.y,mn.y,mx.y,ref t0,ref t1)||!Slab(o.z,d.z,mn.z,mx.z,ref t0,ref t1)){hit=0;return false;}hit=t0;return hit>=0&&hit<=max;
        }
        static bool Slab(float o,float d,float mn,float mx,ref float t0,ref float t1)
        {
            if(Mathf.Abs(d)<1e-8f)return o>=mn&&o<=mx;float a=(mn-o)/d,b=(mx-o)/d;if(a>b){float q=a;a=b;b=q;}if(a>t0)t0=a;if(b<t1)t1=b;return t0<=t1;
        }
    }
}
