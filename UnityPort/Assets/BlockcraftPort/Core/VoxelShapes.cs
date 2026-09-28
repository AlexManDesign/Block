using UnityEngine;

namespace BlockcraftPort
{
    public readonly struct VoxelBox
    {
        public readonly float X0, Y0, Z0, X1, Y1, Z1;
        public VoxelBox(float x0,float y0,float z0,float x1,float y1,float z1)
        { X0=x0; Y0=y0; Z0=z0; X1=x1; Y1=y1; Z1=z1; }
        public Vector3 Min => new Vector3(X0,Y0,Z0);
        public Vector3 Size => new Vector3(X1-X0,Y1-Y0,Z1-Z0);
    }

    /// <summary>Selection envelope used by the current controller. Render meshes may contain several source boxes.</summary>
    public static class VoxelShapes
    {
        public static readonly VoxelBox Full = new VoxelBox(0,0,0,1,1,1);

        public static VoxelBox Selection(BlockId id, byte meta=0)
        {
            switch(BlockRegistry.Get(id).Shape)
            {
                case BlockShape.TallPlant: return new VoxelBox(.1f,0,.1f,.9f,1,.9f);
                case BlockShape.LilyPad: return new VoxelBox(0,0,0,1,.0625f,1);
                case BlockShape.SeaPickle: return new VoxelBox(.15f,0,.15f,.9f,.45f,.9f);
                case BlockShape.Bamboo: return new VoxelBox(.4375f,0,.4375f,.5625f,1,.5625f);
                case BlockShape.Carpet: return new VoxelBox(0,0,0,1,.0625f,1);
                case BlockShape.Slab: return (meta&1)!=0?new VoxelBox(0,.5f,0,1,1,1):new VoxelBox(0,0,0,1,.5f,1);
                case BlockShape.Fence: return new VoxelBox(.25f,0,.25f,.75f,1,.75f);
                case BlockShape.Gate: return new VoxelBox(0,.3125f,0,1,1,1);
                case BlockShape.Wall: return new VoxelBox(.25f,0,.25f,.75f,1,.75f);
                case BlockShape.Pane: return new VoxelBox(.4375f,0,.4375f,.5625f,1,.5625f);
                case BlockShape.Plate: return new VoxelBox(.0625f,0,.0625f,.9375f,.0625f,.9375f);
                case BlockShape.Button: return new VoxelBox(.25f,.25f,.25f,.75f,.75f,.75f);
                case BlockShape.Trapdoor:
                    if((meta&8)!=0)return Full; // open envelope depends on facing; safe current selection envelope.
                    return (meta&4)!=0?new VoxelBox(0,.8125f,0,1,1,1):new VoxelBox(0,0,0,1,.1875f,1);
                case BlockShape.Ladder: return new VoxelBox(0,0,.875f,1,1,.9375f);
                case BlockShape.Torch: return new VoxelBox(.35f,0,.35f,.65f,.85f,.65f);
                case BlockShape.Rail: return new VoxelBox(0,0,0,1,.125f,1);
                case BlockShape.FlatFaces: return Full;
                case BlockShape.Pot: return new VoxelBox(.3125f,0,.3125f,.6875f,.375f,.6875f);
                case BlockShape.ShipWheel: return new VoxelBox(.25f,0,.25f,.75f,1,.75f);
                case BlockShape.Bed: return (meta&1)!=0?new VoxelBox(0,.5f,0,1,1,1):new VoxelBox(0,0,0,1,.5f,1);
                case BlockShape.Door:
                    {
                        int facing=meta&3; bool open=(meta&8)!=0, hinge=(meta&16)!=0;
                        VoxelBox b=open?(hinge?new VoxelBox(.8125f,0,0,1,1,1):new VoxelBox(0,0,0,.1875f,1,1)):new VoxelBox(0,0,0,1,1,.1875f);
                        if(facing==0)return b;
                        Vector3 lo=new Vector3(b.X0,b.Y0,b.Z0), hi=new Vector3(b.X1,b.Y1,b.Z1);
                        Vector3[] r=RotateY(lo,hi,facing);return new VoxelBox(r[0].x,r[0].y,r[0].z,r[1].x,r[1].y,r[1].z);
                    }
                case BlockShape.Chest: return Full;
                default: return Full;
            }
        }

        static Vector3[] RotateY(Vector3 lo,Vector3 hi,int rot)
        {
            rot=((rot%4)+4)%4;if(rot==0)return new[]{lo,hi};
            float x0,z0,x1,z1;
            if(rot==1){x0=1-hi.z;x1=1-lo.z;z0=lo.x;z1=hi.x;}
            else if(rot==2){x0=1-hi.x;x1=1-lo.x;z0=1-hi.z;z1=1-lo.z;}
            else{x0=lo.z;x1=hi.z;z0=1-hi.x;z1=1-lo.x;}
            return new[]{new Vector3(x0,lo.y,z0),new Vector3(x1,hi.y,z1)};
        }
    }
}
