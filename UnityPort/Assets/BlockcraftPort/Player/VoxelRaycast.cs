using UnityEngine;
namespace BlockcraftPort
{
    public struct VoxelHit{public Vector3Int Block;public Vector3Int Normal;public BlockId Id; public Vector3 Point;}
    public static class VoxelRaycast
    {
        public static bool Cast(VoxelWorld world,Vector3 origin,Vector3 dir,float max,out VoxelHit hit)
        {
            hit=default;dir.Normalize();int x=Mathf.FloorToInt(origin.x),y=Mathf.FloorToInt(origin.y),z=Mathf.FloorToInt(origin.z);
            int sx=dir.x>=0?1:-1,sy=dir.y>=0?1:-1,sz=dir.z>=0?1:-1;
            float dx=Mathf.Abs(dir.x)<1e-6f?float.PositiveInfinity:Mathf.Abs(1f/dir.x),dy=Mathf.Abs(dir.y)<1e-6f?float.PositiveInfinity:Mathf.Abs(1f/dir.y),dz=Mathf.Abs(dir.z)<1e-6f?float.PositiveInfinity:Mathf.Abs(1f/dir.z);
            float tx=Next(origin.x,x,sx,dir.x),ty=Next(origin.y,y,sy,dir.y),tz=Next(origin.z,z,sz,dir.z);float t=0;
            while(t<=max)
            {
                BlockId id=world.GetBlock(x,y,z);
                // main's normal target ray skips fluids, but plants/aquatic geometry remains targetable.
                if(id!=BlockId.Air&&!BlockRegistry.IsFluid(id))
                {
                    VoxelBox b=VoxelShapes.Selection(id,world.GetMeta(x,y,z));
                    float ht; Vector3Int normal;
                    if(RayBox(origin,dir,new Vector3(x+b.X0,y+b.Y0,z+b.Z0),new Vector3(x+b.X1,y+b.Y1,z+b.Z1),max,out ht,out normal))
                    {
                        hit=new VoxelHit{Block=new Vector3Int(x,y,z),Normal=normal,Id=id,Point=origin+dir*ht};return true;
                    }
                }
                if(tx<ty&&tx<tz){x+=sx;t=tx;tx+=dx;}else if(ty<tz){y+=sy;t=ty;ty+=dy;}else{z+=sz;t=tz;tz+=dz;}
            }return false;
        }
        static float Next(float o,int cell,int step,float d){if(Mathf.Abs(d)<1e-6f)return float.PositiveInfinity;float plane=step>0?cell+1:cell;return (plane-o)/d;}

        static bool RayBox(Vector3 o,Vector3 d,Vector3 mn,Vector3 mx,float max,out float tHit,out Vector3Int normal)
        {
            float t0=0f,t1=max; normal=Vector3Int.zero; Vector3Int enter=Vector3Int.zero;
            if(!Slab(o.x,d.x,mn.x,mx.x,Vector3Int.left,Vector3Int.right,ref t0,ref t1,ref enter) ||
               !Slab(o.y,d.y,mn.y,mx.y,Vector3Int.down,Vector3Int.up,ref t0,ref t1,ref enter) ||
               !Slab(o.z,d.z,mn.z,mx.z,new Vector3Int(0,0,-1),new Vector3Int(0,0,1),ref t0,ref t1,ref enter))
            { tHit=0; return false; }
            tHit=t0; normal=enter; return tHit>=0f&&tHit<=max;
        }
        static bool Slab(float o,float d,float mn,float mx,Vector3Int nMin,Vector3Int nMax,ref float t0,ref float t1,ref Vector3Int enter)
        {
            if(Mathf.Abs(d)<1e-7f) return o>mn&&o<mx;
            float a=(mn-o)/d,b=(mx-o)/d; Vector3Int na=nMin,nb=nMax;
            if(a>b){float q=a;a=b;b=q;var nq=na;na=nb;nb=nq;}
            if(a>t0){t0=a;enter=na;} if(b<t1)t1=b; return t0<=t1;
        }
    }
}
