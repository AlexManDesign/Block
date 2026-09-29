using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Point collision used by main.js An()/W1() for projectiles.
    /// This intentionally differs from selection envelopes: several decorative/special
    /// blocks have no projectile collision in W1(), while slabs/stairs/fences/walls/panes/
    /// doors/trapdoors/gates/ship wheels use their source collision boxes.
    /// Metadata is already mirrored at the source->Unity world boundary, so the source
    /// direction order (0=+Z,1=-X,2=-Z,3=+X) remains valid on Unity-side metadata.
    /// </summary>
    public static class SourcePointCollision
    {
        static readonly Vector3Int[] Dir =
        {
            new Vector3Int(0,0,1), new Vector3Int(-1,0,0),
            new Vector3Int(0,0,-1), new Vector3Int(1,0,0)
        };

        // main.js Ae(): ordinary solid cube. Fluids, cross/decorative and oA special shapes are excluded.
        public static bool OrdinarySolidCube(VoxelWorld world,int x,int y,int z)
        {
            if(world==null)return false;
            BlockId id=world.GetBlock(x,y,z);
            if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id)||BlockRegistry.IsAquatic(id))return false;
            BlockDef def=BlockRegistry.Get(id);
            return def.Shape==BlockShape.Cube;
        }

        // main.js Np(): sight is blocked by Ae(block) except source v0[] glass/glassy entries.
        public static bool BlocksMobSight(VoxelWorld world,int x,int y,int z)
        {
            if(!OrdinarySolidCube(world,x,y,z))return false;
            BlockId id=world.GetBlock(x,y,z);
            return id!=BlockId.Glass&&!BlockRegistry.IsGlassy(id);
        }

        // main.js Ke(): player body AABB against the same source W1() collision-box set.
        // Unlike VoxelShapes.Selection this keeps disconnected arms/panes/doors as separate boxes.
        public static bool OverlapsBody(VoxelWorld world,Vector3 feet,float radius,float height)
        {
            if(world==null)return true;
            float ax0=feet.x-radius,ax1=feet.x+radius,ay0=feet.y,ay1=feet.y+height,az0=feet.z-radius,az1=feet.z+radius;
            int x0=Mathf.FloorToInt(ax0),x1=Mathf.FloorToInt(ax1),y0=Mathf.FloorToInt(ay0)-1,y1=Mathf.FloorToInt(ay1-.001f),z0=Mathf.FloorToInt(az0),z1=Mathf.FloorToInt(az1);
            for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)
            {
                BlockId id=world.GetBlock(x,y,z);
                if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id)||BlockRegistry.IsAquatic(id))continue;
                BlockDef def=BlockRegistry.Get(id);float qx0=ax0-x,qx1=ax1-x,qy0=ay0-y,qy1=ay1-y,qz0=az0-z,qz1=az1-z;
                switch(def.Shape)
                {
                    case BlockShape.Cube: if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,1,1))return true; continue;
                    case BlockShape.Cross: case BlockShape.TallPlant: case BlockShape.Bamboo: case BlockShape.SeaPickle:
                    case BlockShape.Plate: case BlockShape.Button: case BlockShape.Ladder: case BlockShape.Torch:
                    case BlockShape.Rail: case BlockShape.FlatFaces: continue;
                }
                byte meta=world.GetMeta(x,y,z);
                switch(def.Shape)
                {
                    case BlockShape.Slab:
                        if((meta&1)!=0?Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,.5f,0,1,1,1):Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,.5f,1))return true;break;
                    case BlockShape.Stairs:
                        if(OverlapsSourceStair(qx0,qy0,qz0,qx1,qy1,qz1,meta))return true;break;
                    case BlockShape.Fence:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,.375f,0,.375f,.625f,1.5f,.625f))return true;
                        for(int d=0;d<4;d++)if(FenceConnects(world,x,y,z,d)&&OverlapFenceArm(qx0,qy0,qz0,qx1,qy1,qz1,d))return true;break;
                    case BlockShape.Gate:
                        if(((meta>>3)&1)==0&&OverlapRotY(qx0,qy0,qz0,qx1,qy1,qz1,.375f,0,0,.625f,1.5f,1,(meta&1)!=0?1:0))return true;break;
                    case BlockShape.Wall:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,.25f,0,.25f,.75f,1.5f,.75f))return true;
                        for(int d=0;d<4;d++)if(WallConnects(world,x+Dir[d].x,y,z+Dir[d].z)&&OverlapWallArm(qx0,qy0,qz0,qx1,qy1,qz1,d,1.5f))return true;break;
                    case BlockShape.Pane:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,.4375f,0,.4375f,.5625f,1,.5625f))return true;
                        for(int d=0;d<4;d++)if(PaneConnects(world,x+Dir[d].x,y,z+Dir[d].z)&&OverlapPaneArm(qx0,qy0,qz0,qx1,qy1,qz1,d))return true;break;
                    case BlockShape.Carpet: case BlockShape.LilyPad:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,.0625f,1))return true;break;
                    case BlockShape.Pot:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,.3125f,0,.3125f,.6875f,.375f,.6875f))return true;break;
                    case BlockShape.ShipWheel:
                        if(OverlapRotY(qx0,qy0,qz0,qx1,qy1,qz1,.125f,0,.1875f,.875f,.62f,.8125f,meta&3)||OverlapRotY(qx0,qy0,qz0,qx1,qy1,qz1,.14f,.62f,.4f,.86f,.98f,.6f,meta&3))return true;break;
                    case BlockShape.Door:
                        if(OverlapsDoor(qx0,qy0,qz0,qx1,qy1,qz1,meta))return true;break;
                    case BlockShape.Trapdoor:
                        if(((meta>>3)&1)!=0)
                        {if(OverlapRotY(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,1,.1875f,meta&3))return true;}
                        else if(((meta>>2)&1)!=0?Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,.8125f,0,1,1,1):Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,.1875f,1))return true;
                        break;
                    default:
                        if(Overlap(qx0,qy0,qz0,qx1,qy1,qz1,0,0,0,1,1,1))return true;break;
                }
            }
            if(MainShipRuntime.OverlapsDockedBody(feet,radius,height))return true;
            return false;
        }

        static bool OverlapsSourceStair(float x0,float y0,float z0,float x1,float y1,float z1,byte meta)
        {
            int dir=meta&3;bool upside=((meta>>2)&1)!=0;
            if(upside){if(Overlap(x0,y0,z0,x1,y1,z1,0,.5f,0,1,1,1))return true;}
            else if(Overlap(x0,y0,z0,x1,y1,z1,0,0,0,1,.5f,1))return true;
            float by0=upside?0f:.5f,by1=upside?.5f:1f;
            return OverlapRotY(x0,y0,z0,x1,y1,z1,0,by0,.5f,1,by1,1,dir);
        }
        static bool OverlapsDoor(float x0,float y0,float z0,float x1,float y1,float z1,byte meta)
        {
            bool open=((meta>>3)&1)!=0,hinge=((meta>>4)&1)!=0;
            if(!open)return OverlapRotY(x0,y0,z0,x1,y1,z1,0,0,0,1,1,.1875f,meta&3);
            return hinge?OverlapRotY(x0,y0,z0,x1,y1,z1,.8125f,0,0,1,1,1,meta&3):OverlapRotY(x0,y0,z0,x1,y1,z1,0,0,0,.1875f,1,1,meta&3);
        }
        static bool OverlapFenceArm(float x0,float y0,float z0,float x1,float y1,float z1,int d)
        {
            if(d==0)return Overlap(x0,y0,z0,x1,y1,z1,.375f,0,.625f,.625f,1.5f,1);
            if(d==1)return Overlap(x0,y0,z0,x1,y1,z1,0,0,.375f,.375f,1.5f,.625f);
            if(d==2)return Overlap(x0,y0,z0,x1,y1,z1,.375f,0,0,.625f,1.5f,.375f);
            return Overlap(x0,y0,z0,x1,y1,z1,.625f,0,.375f,1,1.5f,.625f);
        }
        static bool OverlapWallArm(float x0,float y0,float z0,float x1,float y1,float z1,int d,float h)
        {
            if(d==0)return Overlap(x0,y0,z0,x1,y1,z1,.3125f,0,.6875f,.6875f,h,1);
            if(d==1)return Overlap(x0,y0,z0,x1,y1,z1,0,0,.3125f,.3125f,h,.6875f);
            if(d==2)return Overlap(x0,y0,z0,x1,y1,z1,.3125f,0,0,.6875f,h,.3125f);
            return Overlap(x0,y0,z0,x1,y1,z1,.6875f,0,.3125f,1,h,.6875f);
        }
        static bool OverlapPaneArm(float x0,float y0,float z0,float x1,float y1,float z1,int d)
        {
            if(d==0)return Overlap(x0,y0,z0,x1,y1,z1,.4375f,0,.5f,.5625f,1,1);
            if(d==1)return Overlap(x0,y0,z0,x1,y1,z1,0,0,.4375f,.5f,1,.5625f);
            if(d==2)return Overlap(x0,y0,z0,x1,y1,z1,.4375f,0,0,.5625f,1,.5f);
            return Overlap(x0,y0,z0,x1,y1,z1,.5f,0,.4375f,1,1,.5625f);
        }
        static bool OverlapRotY(float x0,float y0,float z0,float x1,float y1,float z1,float bx0,float by0,float bz0,float bx1,float by1,float bz1,int rot)
        {
            rot=((rot%4)+4)%4;float rx0=x0,rx1=x1,rz0=z0,rz1=z1;
            if(rot==1){rx0=z0;rx1=z1;rz0=1-x1;rz1=1-x0;}
            else if(rot==2){rx0=1-x1;rx1=1-x0;rz0=1-z1;rz1=1-z0;}
            else if(rot==3){rx0=1-z1;rx1=1-z0;rz0=x0;rz1=x1;}
            return Overlap(rx0,y0,rz0,rx1,y1,rz1,bx0,by0,bz0,bx1,by1,bz1);
        }
        static bool Overlap(float ax0,float ay0,float az0,float ax1,float ay1,float az1,float bx0,float by0,float bz0,float bx1,float by1,float bz1)
        {return ax1>bx0&&ax0<bx1&&ay1>by0&&ay0<by1&&az1>bz0&&az0<bz1;}

        public static bool Contains(VoxelWorld world, Vector3 p)
        {
            if(world==null)return true;
            int x=Mathf.FloorToInt(p.x), y=Mathf.FloorToInt(p.y), z=Mathf.FloorToInt(p.z);
            BlockId id=world.GetBlock(x,y,z);
            if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id)||BlockRegistry.IsAquatic(id))return false;
            BlockDef def=BlockRegistry.Get(id);
            float lx=p.x-x, ly=p.y-y, lz=p.z-z;

            // main.js Ae(): non-fluid, non-cross, non-special blocks collide as a full cube.
            switch(def.Shape)
            {
                case BlockShape.Cube:
                    return In(lx,ly,lz,0,0,0,1,1,1);
                case BlockShape.Cross:
                case BlockShape.TallPlant:
                case BlockShape.Bamboo:
                case BlockShape.SeaPickle:
                case BlockShape.Plate:
                case BlockShape.Button:
                case BlockShape.Ladder:
                case BlockShape.Torch:
                case BlockShape.Rail:
                case BlockShape.FlatFaces:
                    return false;
            }

            byte meta=world.GetMeta(x,y,z);
            switch(def.Shape)
            {
                case BlockShape.Slab:
                    return (meta&1)!=0 ? In(lx,ly,lz,0,.5f,0,1,1,1) : In(lx,ly,lz,0,0,0,1,.5f,1);

                case BlockShape.Stairs:
                    return InSourceStair(lx,ly,lz,meta);

                case BlockShape.Fence:
                    if(In(lx,ly,lz,.375f,0,.375f,.625f,1.5f,.625f))return true;
                    for(int d=0;d<4;d++)if(FenceConnects(world,x,y,z,d))
                    {
                        if(InFenceArm(lx,ly,lz,d))return true;
                    }
                    return false;

                case BlockShape.Gate:
                    if(((meta>>3)&1)!=0)return false;
                    return InRotY(lx,ly,lz,.375f,0,0,.625f,1.5f,1,(meta&1)!=0?1:0);

                case BlockShape.Wall:
                    if(In(lx,ly,lz,.25f,0,.25f,.75f,1.5f,.75f))return true;
                    for(int d=0;d<4;d++)if(WallConnects(world,x+Dir[d].x,y,z+Dir[d].z))
                    {
                        if(InWallArm(lx,ly,lz,d,1.5f))return true;
                    }
                    return false;

                case BlockShape.Pane:
                    if(In(lx,ly,lz,.4375f,0,.4375f,.5625f,1,.5625f))return true;
                    for(int d=0;d<4;d++)if(PaneConnects(world,x+Dir[d].x,y,z+Dir[d].z))
                    {
                        if(InPaneArm(lx,ly,lz,d))return true;
                    }
                    return false;

                case BlockShape.Carpet:
                    return In(lx,ly,lz,0,0,0,1,.0625f,1);

                case BlockShape.LilyPad:
                    return In(lx,ly,lz,0,0,0,1,.0625f,1);

                case BlockShape.Pot:
                    return In(lx,ly,lz,.3125f,0,.3125f,.6875f,.375f,.6875f);

                case BlockShape.ShipWheel:
                    return InRotY(lx,ly,lz,.125f,0,.1875f,.875f,.62f,.8125f,meta&3)
                        || InRotY(lx,ly,lz,.14f,.62f,.4f,.86f,.98f,.6f,meta&3);

                case BlockShape.Door:
                    return InDoor(lx,ly,lz,meta);

                case BlockShape.Trapdoor:
                    if(((meta>>3)&1)!=0)
                        return InRotY(lx,ly,lz,0,0,0,1,1,.1875f,meta&3);
                    return ((meta>>2)&1)!=0
                        ? In(lx,ly,lz,0,.8125f,0,1,1,1)
                        : In(lx,ly,lz,0,0,0,1,.1875f,1);

                // main W1() falls back to d4 (full cube) for other registered shapes.
                default:
                    return In(lx,ly,lz,0,0,0,1,1,1);
            }
        }

        // main.js W1() calls J4(meta) without world coordinates. Therefore An()/mob/projectile
        // collision uses the straight stair shape only; W5()/D8() corner detection is not reached here.
        static bool InSourceStair(float x,float y,float z,byte meta)
        {
            int dir=meta&3;bool upside=((meta>>2)&1)!=0;
            if(upside){if(In(x,y,z,0,.5f,0,1,1,1))return true;}
            else if(In(x,y,z,0,0,0,1,.5f,1))return true;
            float y0=upside?0f:.5f,y1=upside?.5f:1f;
            return InRotY(x,y,z,0,y0,.5f,1,y1,1,dir);
        }

        static bool InDoor(float x,float y,float z,byte meta)
        {
            bool open=((meta>>3)&1)!=0, hinge=((meta>>4)&1)!=0;
            if(!open)return InRotY(x,y,z,0,0,0,1,1,.1875f,meta&3);
            return hinge
                ? InRotY(x,y,z,.8125f,0,0,1,1,1,meta&3)
                : InRotY(x,y,z,0,0,0,.1875f,1,1,meta&3);
        }

        // Same connection rules as the mesher (meshWorker B2()/a2()/i2()), so collision matches what is drawn.
        static bool FenceConnects(VoxelWorld world,int x,int y,int z,int dir)
        {
            Vector3Int q=Dir[dir]; BlockId n=world.GetBlock(x+q.x,y,z+q.z);
            if(BlockRegistry.ConnectsPlain(n)||BlockRegistry.IsFence(n))return true;
            if(BlockRegistry.IsGate(n))
            {
                byte m=world.GetMeta(x+q.x,y,z+q.z);
                return (m&1)==0 ? dir==0||dir==2 : dir==1||dir==3;
            }
            return false;
        }

        static bool WallConnects(VoxelWorld world,int x,int y,int z)
        {
            BlockId n=world.GetBlock(x,y,z);return BlockRegistry.ConnectsPlain(n)||BlockRegistry.IsWall(n);
        }

        static bool PaneConnects(VoxelWorld world,int x,int y,int z)
        {
            BlockId n=world.GetBlock(x,y,z);return BlockRegistry.ConnectsPlain(n)||BlockRegistry.IsPane(n);
        }

        static bool InFenceArm(float x,float y,float z,int d)
        {
            if(d==0)return In(x,y,z,.375f,0,.625f,.625f,1.5f,1);
            if(d==1)return In(x,y,z,0,0,.375f,.375f,1.5f,.625f);
            if(d==2)return In(x,y,z,.375f,0,0,.625f,1.5f,.375f);
            return In(x,y,z,.625f,0,.375f,1,1.5f,.625f);
        }

        static bool InWallArm(float x,float y,float z,int d,float h)
        {
            if(d==0)return In(x,y,z,.3125f,0,.6875f,.6875f,h,1);
            if(d==1)return In(x,y,z,0,0,.3125f,.3125f,h,.6875f);
            if(d==2)return In(x,y,z,.3125f,0,0,.6875f,h,.3125f);
            return In(x,y,z,.6875f,0,.3125f,1,h,.6875f);
        }

        static bool InPaneArm(float x,float y,float z,int d)
        {
            if(d==0)return In(x,y,z,.4375f,0,.5f,.5625f,1,1);
            if(d==1)return In(x,y,z,0,0,.4375f,.5f,1,.5625f);
            if(d==2)return In(x,y,z,.4375f,0,0,.5625f,1,.5f);
            return In(x,y,z,.5f,0,.4375f,1,1,.5625f);
        }

        static bool InRotY(float x,float y,float z,float x0,float y0,float z0,float x1,float y1,float z1,int rot)
        {
            rot=((rot%4)+4)%4;
            if(rot==0)return In(x,y,z,x0,y0,z0,x1,y1,z1);
            float rx=x,rz=z;
            // Inverse-transform the point instead of allocating a rotated box.
            if(rot==1){rx=z;rz=1-x;}
            else if(rot==2){rx=1-x;rz=1-z;}
            else {rx=1-z;rz=x;}
            return In(rx,y,rz,x0,y0,z0,x1,y1,z1);
        }

        static bool In(float x,float y,float z,float x0,float y0,float z0,float x1,float y1,float z1)
        { return x>=x0&&x<=x1&&y>=y0&&y<=y1&&z>=z0&&z<=z1; }
    }
}
