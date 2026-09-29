using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// meshWorker-oriented face mesher. Vertex payload is exactly pos3+uv2+shade/sky/block.
    /// Cube AO/light follows O2(); special block boxes follow SE()/HC() in main meshWorker.
    /// </summary>
    public static class ChunkMesher
    {
        // meshWorker.js keeps its scratch JS arrays inside long-lived workers. Do the same here:
        // one reusable scratch set per ThreadPool worker avoids six managed List allocations per section.
        [ThreadStatic] static List<Vector3> scratchV;
        [ThreadStatic] static List<Vector2> scratchUv;
        [ThreadStatic] static List<Vector3> scratchLight;
        [ThreadStatic] static List<int> scratchOpaque;
        [ThreadStatic] static List<int> scratchWater;
        [ThreadStatic] static List<int> scratchTransparent;
        [ThreadStatic] static List<int> scratchXray;

        static List<T> Scratch<T>(ref List<T> list, int capacity)
        {
            if (list == null) list = new List<T>(capacity);
            else list.Clear();
            return list;
        }
        static readonly Vector3Int[] N={new Vector3Int(1,0,0),new Vector3Int(-1,0,0),new Vector3Int(0,1,0),new Vector3Int(0,-1,0),new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
        // meshWorker GA: metadata horizontal direction order +Z,-X,-Z,+X.
        static readonly Vector3Int[] HDir={new Vector3Int(0,0,1),new Vector3Int(-1,0,0),new Vector3Int(0,0,-1),new Vector3Int(1,0,0)};
        // rail neighbour order +X,-X,+Z,-Z.
        static readonly Vector2Int[] RailDir={new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)};
        static readonly Vector3[,] C={
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,0,1),new Vector3(1,1,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,0,0),new Vector3(0,1,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(0,1,0),new Vector3(1,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(0,1,1),new Vector3(1,1,1)},
            {new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(1,1,0),new Vector3(0,1,0)}};
        static readonly Vector2[,] FUV={
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)}, {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}, {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}, {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}};
        static readonly Vector2[,] RailUV={
            {new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)},
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(1,1),new Vector2(0,1),new Vector2(1,0),new Vector2(0,0)},
            {new Vector2(1,0),new Vector2(1,1),new Vector2(0,0),new Vector2(0,1)}};
        static readonly float[] Shade={.72f,.72f,1f,.55f,.82f,.82f};
        static readonly Vector3[] Pickles={new Vector3(.375f,.375f,.375f),new Vector3(.18f,.22f,.3125f),new Vector3(.6f,.5f,.4375f),new Vector3(.32f,.62f,.3125f)};
        static readonly Vector2[] TorchCorners={new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1)};
        static readonly int[] ChestFaceFacing={3,1,-1,-1,0,2};
        static readonly Vector4[][] WheelRects=BuildWheelRects();
        // meshWorker h2: side texture UV rotation count for pillar axis meta 0(Y),1(X),2(Z).
        static readonly int[,] AxisUvRot={{0,0,0,0,0,0},{0,0,1,1,1,1},{1,1,0,0,0,0}};

        public static MeshBuildResult Build(SectionSnapshot s)
        {
            var v=Scratch(ref scratchV,6144); var uv=Scratch(ref scratchUv,6144); var sl=Scratch(ref scratchLight,6144);
            var terrain=Scratch(ref scratchOpaque,8192); var water=Scratch(ref scratchWater,2048); var transparent=Scratch(ref scratchTransparent,2048); var xray=Scratch(ref scratchXray,512); int sy=s.Key.Section*16;
            for(int x=0;x<16;x++)for(int z=0;z<16;z++)for(int y=0;y<16;y++)
            {
                BlockId id=s.Get(x+1,y+1,z+1);if(id==BlockId.Air)continue;
                BlockDef def=BlockRegistry.Get(id);if(def.Layer==RenderLayer.None)continue;
                byte meta=s.GetMeta(x+1,y+1,z+1);
                List<int> dst=Indices(def.Layer,terrain,water,transparent);

                if(BlockRegistry.IsAquatic(id)&&(BlockRegistry.NeedsWater(id)||(meta&4)!=0||HasWaterAround(s,x+1,y+1,z+1)))
                    AddWaterShell(v,uv,sl,water,s,x,y+sy,z,y);

                if(id==BlockId.Fire){AddFire(v,uv,sl,dst,s,def.Side,x,y+sy,z,y);continue;}
                switch(def.Shape)
                {
                    case BlockShape.Cross: AddCross(v,uv,sl,dst,s,def.Side,x,y+sy,z,y,.95f,false,BlockRegistry.XrayClassified(id)?4f:0f); continue;
                    case BlockShape.TallPlant: AddCross(v,uv,sl,dst,s,(meta&4)!=0?def.Top:def.Bottom,x,y+sy,z,y,.95f,true); continue;
                    case BlockShape.LilyPad: AddLilyPad(v,uv,sl,dst,s,def.Top,x,y+sy,z,y); continue;
                    case BlockShape.Bamboo: AddBox(v,uv,sl,dst,s,def,x,y+sy,z,y,new Vector3(.4375f,0,.4375f),new Vector3(.5625f,1,.5625f)); continue;
                    case BlockShape.Carpet: AddBox(v,uv,sl,dst,s,def,x,y+sy,z,y,Vector3.zero,new Vector3(1,.0625f,1)); continue;
                    case BlockShape.SeaPickle:
                        int count=(meta&3)+1;for(int q=0;q<count;q++){var p=Pickles[q];AddBox(v,uv,sl,dst,s,def,x,y+sy,z,y,new Vector3(p.x,0,p.y),new Vector3(p.x+.25f,p.z,p.y+.25f));}continue;
                    case BlockShape.Slab: AddSlab(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Stairs: AddStairs(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Fence: AddFence(v,uv,sl,dst,s,def,x,y+sy,z,y,BlockRegistry.XrayClassified(id)?4f:0f); continue;
                    case BlockShape.Gate: AddGate(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Wall: AddWall(v,uv,sl,dst,s,def,x,y+sy,z,y); continue;
                    case BlockShape.Pane: AddPane(v,uv,sl,dst,s,def,x,y+sy,z,y); continue;
                    case BlockShape.Plate: AddBox(v,uv,sl,dst,s,def,x,y+sy,z,y,new Vector3(.0625f,0,.0625f),new Vector3(.9375f,.0625f,.9375f)); continue;
                    case BlockShape.Button: AddButton(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Trapdoor: AddTrapdoor(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Ladder: AddLadder(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Torch: AddTorch(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    // meshWorker F2() writes a fixed 0.96 shade without the V1[] class offset.
                    case BlockShape.Rail: AddRail(v,uv,sl,dst,s,def,x,y+sy,z,y,meta,0f); continue;
                    case BlockShape.FlatFaces: AddFlatFaces(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Pot: AddPot(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.ShipWheel: AddShipWheel(v,uv,sl,dst,s,x,y+sy,z,y,meta); continue;
                    case BlockShape.Door: AddDoor(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Bed: AddBed(v,uv,sl,dst,s,def,x,y+sy,z,y,meta); continue;
                    case BlockShape.Chest: break; // cube AO path below, with source front/top texture selection in AddFace().
                }

                for(int f=0;f<6;f++)
                {
                    var nn=N[f];BlockId other=s.Get(x+1+nn.x,y+1+nn.y,z+1+nn.z);
                    if(BlockRegistry.IsWater(id))
                    {
                        if(!WaterFaceVisible(s,id,other,(FaceDir)f,x,y,z))continue;
                    }
                    else if(!BlockRegistry.FaceVisible(id,other))
                    {
                        // meshWorker MC (V1[]==2): ores emit all six faces so the X-Ray pass can show them
                        // through stone. The hidden ones go to a separate list that is only drawn in X-Ray
                        // mode, instead of adding ~40% invisible triangles to every underground chunk.
                        if(BlockRegistry.IsXrayOre(id))AddFace(v,uv,sl,xray,s,id,def,(FaceDir)f,x,y+sy,z,y,meta);
                        continue;
                    }
                    AddFace(v,uv,sl,dst,s,id,def,(FaceDir)f,x,y+sy,z,y,meta);
                }
            }
            // meshWorker emits vertex positions in WORLD coordinates (cx*16+x, MIN_Y+section*16+y, cz*16+z).
            // R4 kept positions chunk-local and applied a Transform offset in ChunkRender.  That is mathematically
            // similar, but it introduces a second coordinate transform at chunk borders.  Match WebGL2 exactly:
            // bake the integral chunk/world offset into every vertex and keep the renderer transform at identity.
            Vector3 worldOffset=new Vector3(s.Key.Chunk.X*16,VoxelConstants.MinY,s.Key.Chunk.Z*16);
            int vertexCount=v.Count;
            if(vertexCount==0)
                return new MeshBuildResult
                {
                    Key=s.Key,Stamp=s.Stamp,Vertices=Array.Empty<VoxelVertex>(),VertexCount=0,
                    Opaque=Array.Empty<int>(),Water=Array.Empty<int>(),Transparent=Array.Empty<int>(),XrayHidden=Array.Empty<int>()
                };

            VoxelVertex[] packed=ArrayPool<VoxelVertex>.Shared.Rent(vertexCount);
            // Vertices were built in the source frame (local z in [0,16]); reflect Z into Unity space.
            for(int i=0;i<vertexCount;i++){Vector3 p=v[i];packed[i]=new VoxelVertex(new Vector3(p.x+worldOffset.x,p.y+worldOffset.y,16f-p.z+worldOffset.z),uv[i],sl[i]);}
            int[] opaque=RentCopy(terrain),waterIndices=RentCopy(water),transparentIndices=RentCopy(transparent),xrayIndices=RentCopy(xray);
            return new MeshBuildResult
            {
                Key=s.Key,Stamp=s.Stamp,Vertices=packed,VertexCount=vertexCount,
                Opaque=opaque,OpaqueCount=terrain.Count,Water=waterIndices,WaterCount=water.Count,
                Transparent=transparentIndices,TransparentCount=transparent.Count,
                XrayHidden=xrayIndices,XrayHiddenCount=xray.Count
            };
        }

        static int[] RentCopy(List<int> source)
        {
            int count=source.Count;
            if(count==0)return Array.Empty<int>();
            int[] result=ArrayPool<int>.Shared.Rent(count);
            source.CopyTo(result,0);
            return result;
        }

        static List<int> Indices(RenderLayer layer,List<int> terrain,List<int> water,List<int> transparent)
        {if(layer==RenderLayer.Water)return water;if(layer==RenderLayer.Transparent)return transparent;return terrain;}

        static BlockId B(SectionSnapshot s,int x,int y,int z)=>s.Get(x+1,y+1,z+1);
        static byte M(SectionSnapshot s,int x,int y,int z)=>s.GetMeta(x+1,y+1,z+1);
        static bool HasWaterAround(SectionSnapshot s,int x,int y,int z)=>Wet(s.Get(x,y+1,z))||Wet(s.Get(x+1,y,z))||Wet(s.Get(x-1,y,z))||Wet(s.Get(x,y,z+1))||Wet(s.Get(x,y,z-1));
        static bool Wet(BlockId b)=>BlockRegistry.IsWater(b)||BlockRegistry.IsAquatic(b);
        static Vector2 MainUV(AtlasRect r,float u,float vv)=>new Vector2(Mathf.Lerp(r.U0,r.U1,u),Mathf.Lerp(r.V1,r.V0,vv));
        static Vector3 LightVec(byte p,float shade)=>new Vector3(shade,(p>>4)/15f,(p&15)/15f);
        static byte MaxLight6(SectionSnapshot s,int x,int y,int z){byte p=s.GetLight(x,y,z);int sky=p>>4,blk=p&15;for(int i=0;i<6;i++){var n=N[i];byte q=s.GetLight(x+n.x,y+n.y,z+n.z);sky=Mathf.Max(sky,q>>4);blk=Mathf.Max(blk,q&15);}return(byte)((sky<<4)|blk);}

        static void AddCross(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,AtlasRect tex,int x,int my,int z,int ly,float shade,bool neighborLight,float classAdd=0f)
        {byte p=neighborLight?MaxLight6(s,x+1,ly+1,z+1):s.GetLight(x+1,ly+1,z+1);AddCrossQuad(v,uv,sl,ind,new Vector3(x+.15f,my,z+.15f),new Vector3(x+.85f,my,z+.85f),tex,LightVec(p,shade+classAdd));AddCrossQuad(v,uv,sl,ind,new Vector3(x+.85f,my,z+.15f),new Vector3(x+.15f,my,z+.85f),tex,LightVec(p,shade+classAdd));}
        static void AddCrossQuad(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,Vector3 a,Vector3 b,AtlasRect r,Vector3 l){int n=v.Count;v.Add(a);v.Add(b);v.Add(a+Vector3.up);v.Add(b+Vector3.up);uv.Add(MainUV(r,0,1));uv.Add(MainUV(r,1,1));uv.Add(MainUV(r,0,0));uv.Add(MainUV(r,1,0));for(int i=0;i<4;i++)sl.Add(l);AddQuad(ind,n);}
        static void AddLilyPad(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,AtlasRect r,int x,int my,int z,int ly){byte p=MaxLight6(s,x+1,ly+1,z+1);Vector3 q=LightVec(p,.96f);float yy=my+.0625f;int n=v.Count;v.Add(new Vector3(x,yy,z));v.Add(new Vector3(x+1,yy,z));v.Add(new Vector3(x,yy,z+1));v.Add(new Vector3(x+1,yy,z+1));uv.Add(MainUV(r,0,0));uv.Add(MainUV(r,1,0));uv.Add(MainUV(r,0,1));uv.Add(MainUV(r,1,1));for(int i=0;i<4;i++)sl.Add(q);AddQuad(ind,n);}
        // meshWorker g2(): fire. Crossed planes only when isolated; otherwise leaning planes toward
        // flammable neighbours (or all four when standing on a solid/flammable block), full block light.
        static void AddFire(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,AtlasRect tex,int x,int my,int z,int ly)
        {
            float sky=(s.GetLight(x+1,ly+1,z+1)>>4)/15f;
            BlockId below=s.Get(x+1,ly,z+1);
            bool sup=BlockRegistry.IsSolidRender(below)||BlockRegistry.FeedsFire(below);
            bool c=BlockRegistry.FeedsFire(s.Get(x+1,ly+1,z)),i=BlockRegistry.FeedsFire(s.Get(x+1,ly+1,z+2));
            bool g=BlockRegistry.FeedsFire(s.Get(x,ly+1,z+1)),dd=BlockRegistry.FeedsFire(s.Get(x+2,ly+1,z+1)),f=BlockRegistry.FeedsFire(s.Get(x+1,ly+2,z+1));
            Vector3 l=new Vector3(1f,sky,1f);
            void V(float g0,float z0,float g1,float z1,float jx,float jz)
            {
                int n=v.Count;
                v.Add(new Vector3(x+g0,my,z+z0));v.Add(new Vector3(x+g1,my,z+z1));
                v.Add(new Vector3(x+g0+jx,my+1,z+z0+jz));v.Add(new Vector3(x+g1+jx,my+1,z+z1+jz));
                uv.Add(MainUV(tex,0,1));uv.Add(MainUV(tex,1,1));uv.Add(MainUV(tex,0,0));uv.Add(MainUV(tex,1,0));
                sl.Add(l);sl.Add(l);sl.Add(l);sl.Add(l);AddQuad(ind,n);
            }
            const float T=.32f;
            if(sup){V(.15f,.15f,.85f,.85f,0,0);V(.85f,.15f,.15f,.85f,0,0);}
            if(sup||c)V(.02f,.02f,.98f,.02f,0,T);
            if(sup||i)V(.98f,.98f,.02f,.98f,0,-T);
            if(sup||g)V(.02f,.98f,.02f,.02f,T,0);
            if(sup||dd)V(.98f,.02f,.98f,.98f,-T,0);
            if(!sup&&f&&!c&&!i&&!g&&!dd)
            {
                int n=v.Count;
                v.Add(new Vector3(x+.02f,my+.98f,z+.02f));v.Add(new Vector3(x+.98f,my+.98f,z+.02f));v.Add(new Vector3(x+.02f,my+.98f,z+.98f));v.Add(new Vector3(x+.98f,my+.98f,z+.98f));
                uv.Add(MainUV(tex,0,1));uv.Add(MainUV(tex,1,1));uv.Add(MainUV(tex,0,0));uv.Add(MainUV(tex,1,0));sl.Add(l);sl.Add(l);sl.Add(l);sl.Add(l);AddQuad(ind,n);
                n=v.Count;
                v.Add(new Vector3(x+.98f,my+.88f,z+.02f));v.Add(new Vector3(x+.02f,my+.88f,z+.02f));v.Add(new Vector3(x+.98f,my+.88f,z+.98f));v.Add(new Vector3(x+.02f,my+.88f,z+.98f));
                uv.Add(MainUV(tex,0,1));uv.Add(MainUV(tex,1,1));uv.Add(MainUV(tex,0,0));uv.Add(MainUV(tex,1,0));sl.Add(l);sl.Add(l);sl.Add(l);sl.Add(l);AddQuad(ind,n);
            }
            if(!sup&&!f&&!c&&!i&&!g&&!dd){V(.15f,.15f,.85f,.85f,0,0);V(.85f,.15f,.15f,.85f,0,0);}
        }

        static void AddQuad(List<int> ind,int n){ind.Add(n);ind.Add(n+1);ind.Add(n+2);ind.Add(n+2);ind.Add(n+1);ind.Add(n+3);}

        // meshWorker HC(): special boxes use neighbour-max light and cropped UVs, without corner AO.
        static void AddBox(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,Vector3 lo,Vector3 hi,bool fullUV=false,bool flat=false,int rotTop=0,float classAdd=0f)
        {
            byte own=s.GetLight(x+1,ly+1,z+1);
            for(int f=0;f<6;f++)
            {
                var nn=N[f];bool touches=f==0&&hi.x==1||f==1&&lo.x==0||f==2&&hi.y==1||f==3&&lo.y==0||f==4&&hi.z==1||f==5&&lo.z==0;
                if(touches&&!flat&&BlockRegistry.IsFullOpaque(s.Get(x+1+nn.x,ly+1+nn.y,z+1+nn.z)))continue;
                byte nb=s.GetLight(x+1+nn.x,ly+1+nn.y,z+1+nn.z);byte packed=(byte)((Mathf.Max(own>>4,nb>>4)<<4)|Mathf.Max(own&15,nb&15));
                AtlasRect r=f==2?d.Top:f==3?d.Bottom:d.Side;int b=v.Count;
                for(int i=0;i<4;i++)
                {
                    Vector3 c=C[f,i];float px=c.x>.5f?hi.x:lo.x,py=c.y>.5f?hi.y:lo.y,pz=c.z>.5f?hi.z:lo.z;
                    v.Add(new Vector3(x+px,my+py,z+pz));float u,t;
                    if(fullUV){Vector2 q=FUV[f,i];u=q.x;t=q.y;}
                    else if(f==0){u=pz;t=1-py;}else if(f==1){u=1-pz;t=1-py;}else if(f==2){u=px;t=pz;}else if(f==3){u=px;t=1-pz;}else if(f==4){u=px;t=1-py;}else{u=1-px;t=1-py;}
                    if(f==2)for(int rr=0;rr<rotTop;rr++){float old=u;u=1-t;t=old;}
                    uv.Add(MainUV(r,u,t));float block=(packed&15)/15f;if(d.Glow)block=Mathf.Max(block,.85f);sl.Add(new Vector3(Shade[f]+classAdd,(packed>>4)/15f,block));
                }
                AddQuad(ind,b);
            }
        }

        static void AddSlab(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {Vector3 lo=(meta&1)!=0?new Vector3(0,.5f,0):Vector3.zero;Vector3 hi=(meta&1)!=0?Vector3.one:new Vector3(1,.5f,1);AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi);}

        static void RotateBox(Vector3 lo,Vector3 hi,int rot,out Vector3 rlo,out Vector3 rhi)
        {
            rot=((rot%4)+4)%4;if(rot==0){rlo=lo;rhi=hi;return;}
            float x0,z0,x1,z1;
            if(rot==1){x0=1-hi.z;x1=1-lo.z;z0=lo.x;z1=hi.x;}
            else if(rot==2){x0=1-hi.x;x1=1-lo.x;z0=1-hi.z;z1=1-lo.z;}
            else{x0=lo.z;x1=hi.z;z0=1-hi.x;z1=1-lo.x;}
            rlo=new Vector3(x0,lo.y,z0);rhi=new Vector3(x1,hi.y,z1);
        }
        static void AddRotatedBox(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,Vector3 lo,Vector3 hi,int rot,bool fullUV=false,bool flat=false)
        {RotateBox(lo,hi,rot,out var rlo,out var rhi);AddBox(v,uv,sl,ind,s,d,x,my,z,ly,rlo,rhi,fullUV,flat);}

        static bool StairMismatch(SectionSnapshot s,int facing,int half,int dir,int x,int y,int z)
        {var d=HDir[dir];BlockId id=B(s,x+d.x,y,z+d.z);byte m=M(s,x+d.x,y,z+d.z);return!BlockRegistry.IsStairs(id)||(m&3)!=facing||((m>>2)&1)!=half;}
        static int StairCorner(SectionSnapshot s,int facing,int half,int x,int y,int z,out bool left)
        {
            int right=(facing+1)%4;var front=HDir[facing];BlockId a=B(s,x+front.x,y,z+front.z);byte am=M(s,x+front.x,y,z+front.z);
            if(BlockRegistry.IsStairs(a)&&((am>>2)&1)==half){int af=am&3;if((af&1)!=(facing&1)&&StairMismatch(s,facing,half,(af+2)%4,x,y,z)){left=af==right;return 1;}}
            var back=HDir[(facing+2)%4];BlockId b=B(s,x+back.x,y,z+back.z);byte bm=M(s,x+back.x,y,z+back.z);
            if(BlockRegistry.IsStairs(b)&&((bm>>2)&1)==half){int bf=bm&3;if((bf&1)!=(facing&1)&&StairMismatch(s,facing,half,bf,x,y,z)){left=bf==right;return 2;}}
            left=false;return 0;
        }
        static void AddStairs(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            int facing=meta&3,half=(meta>>2)&1;Vector3 baseLo=half!=0?new Vector3(0,.5f,0):Vector3.zero,baseHi=half!=0?Vector3.one:new Vector3(1,.5f,1);AddBox(v,uv,sl,ind,s,d,x,my,z,ly,baseLo,baseHi);
            float y0=half!=0?0f:.5f,y1=half!=0?.5f:1f;bool left;int kind=StairCorner(s,facing,half,x,ly,z,out left);
            if(kind==1){Vector3 lo=left?new Vector3(0,y0,.5f):new Vector3(.5f,y0,.5f);Vector3 hi=left?new Vector3(.5f,y1,1):new Vector3(1,y1,1);AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi,facing);}
            else if(kind==2){AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(0,y0,.5f),new Vector3(1,y1,1),facing);Vector3 lo=left?new Vector3(0,y0,0):new Vector3(.5f,y0,0);Vector3 hi=left?new Vector3(.5f,y1,.5f):new Vector3(1,y1,.5f);AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi,facing);}
            else AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(0,y0,.5f),new Vector3(1,y1,1),facing);
        }

        static bool IsCrossLike(BlockId id){var q=BlockRegistry.Get(id).Shape;return q==BlockShape.Cross||q==BlockShape.TallPlant||q==BlockShape.Bamboo||q==BlockShape.LilyPad||q==BlockShape.SeaPickle||q==BlockShape.Rail;}
        // meshWorker B2(): plain blocks, fences, and gates whose axis faces this arm.
        static bool FenceConnects(SectionSnapshot s,int x,int y,int z,int dir)
        {BlockId id=B(s,x,y,z);if(BlockRegistry.ConnectsPlain(id)||BlockRegistry.IsFence(id))return true;if(BlockRegistry.IsGate(id)){byte m=M(s,x,y,z);return(m&1)==0?(dir==0||dir==2):(dir==1||dir==3);}return false;}
        static void AddFenceArm(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,int dir,float y0,float y1,float classAdd)
        {var q=HDir[dir];Vector3 lo,hi;if(q.x>0){lo=new Vector3(.625f,y0,.4375f);hi=new Vector3(1,y1,.5625f);}else if(q.x<0){lo=new Vector3(0,y0,.4375f);hi=new Vector3(.375f,y1,.5625f);}else if(q.z>0){lo=new Vector3(.4375f,y0,.625f);hi=new Vector3(.5625f,y1,1);}else{lo=new Vector3(.4375f,y0,0);hi=new Vector3(.5625f,y1,.375f);}AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi,false,false,0,classAdd);}
        static void AddFence(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,float classAdd)
        {AddBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.375f,0,.375f),new Vector3(.625f,1,.625f),false,false,0,classAdd);for(int dir=0;dir<4;dir++){var q=HDir[dir];if(!FenceConnects(s,x+q.x,ly,z+q.z,dir))continue;AddFenceArm(v,uv,sl,ind,s,d,x,my,z,ly,dir,.375f,.5625f,classAdd);AddFenceArm(v,uv,sl,ind,s,d,x,my,z,ly,dir,.75f,.9375f,classAdd);}}

        static void AddGate(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            // Source n2(): quarter-turn 1 for the alternate gate axis (the mesher works in the source frame).
            int rot=(meta&1)!=0?1:0;bool open=((meta>>3)&1)!=0;
            AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.3125f,0),new Vector3(.5625f,1,.125f),rot);
            AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.3125f,.875f),new Vector3(.5625f,1,1),rot);
            if(open)
            {
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.5625f,.375f,0),new Vector3(.8125f,.5625f,.125f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.5625f,.75f,0),new Vector3(.8125f,.9375f,.125f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.5625f,.375f,.875f),new Vector3(.8125f,.5625f,1),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.5625f,.75f,.875f),new Vector3(.8125f,.9375f,1),rot);
            }
            else
            {
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.375f,.375f),new Vector3(.5625f,.9375f,.625f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.375f,.125f),new Vector3(.5625f,.5625f,.375f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.375f,.625f),new Vector3(.5625f,.5625f,.875f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.75f,.125f),new Vector3(.5625f,.9375f,.375f),rot);
                AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,.75f,.625f),new Vector3(.5625f,.9375f,.875f),rot);
            }
        }

        // meshWorker a2().
        static bool WallConnects(BlockId id)=>BlockRegistry.ConnectsPlain(id)||BlockRegistry.IsWall(id);
        static void AddWall(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly)
        {AddBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.25f,0,.25f),new Vector3(.75f,1,.75f));for(int dir=0;dir<4;dir++){var q=HDir[dir];if(!WallConnects(B(s,x+q.x,ly,z+q.z)))continue;Vector3 lo,hi;if(q.x>0){lo=new Vector3(.6875f,0,.3125f);hi=new Vector3(1,1,.6875f);}else if(q.x<0){lo=new Vector3(0,0,.3125f);hi=new Vector3(.3125f,1,.6875f);}else if(q.z>0){lo=new Vector3(.3125f,0,.6875f);hi=new Vector3(.6875f,1,1);}else{lo=new Vector3(.3125f,0,0);hi=new Vector3(.6875f,1,.3125f);}AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi);}}

        // meshWorker i2().
        static bool PaneConnects(BlockId id)=>BlockRegistry.ConnectsPlain(id)||BlockRegistry.IsPane(id);
        static void AddPane(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly)
        {AddBox(v,uv,sl,ind,s,d,x,my,z,ly,new Vector3(.4375f,0,.4375f),new Vector3(.5625f,1,.5625f));for(int dir=0;dir<4;dir++){var q=HDir[dir];if(!PaneConnects(B(s,x+q.x,ly,z+q.z)))continue;Vector3 lo,hi;if(q.x>0){lo=new Vector3(.5f,0,.4375f);hi=new Vector3(1,1,.5625f);}else if(q.x<0){lo=new Vector3(0,0,.4375f);hi=new Vector3(.5f,1,.5625f);}else if(q.z>0){lo=new Vector3(.4375f,0,.5f);hi=new Vector3(.5625f,1,1);}else{lo=new Vector3(.4375f,0,0);hi=new Vector3(.5625f,1,.5f);}AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi);}}

        static void AddButton(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {Vector3 lo,hi;if(meta==0){lo=new Vector3(.3125f,0,.375f);hi=new Vector3(.6875f,.125f,.625f);}else if(meta==5){lo=new Vector3(.3125f,.875f,.375f);hi=new Vector3(.6875f,1,.625f);}else{int k=Mathf.Clamp(meta-1,0,3);var q=HDir[k];if(q.x>0){lo=new Vector3(.875f,.375f,.3125f);hi=new Vector3(1,.625f,.6875f);}else if(q.x<0){lo=new Vector3(0,.375f,.3125f);hi=new Vector3(.125f,.625f,.6875f);}else if(q.z>0){lo=new Vector3(.3125f,.375f,.875f);hi=new Vector3(.6875f,.625f,1);}else{lo=new Vector3(.3125f,.375f,0);hi=new Vector3(.6875f,.625f,.125f);}}AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi);}

        static void AddTrapdoor(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {int rot=meta&3;bool top=((meta>>2)&1)!=0,open=((meta>>3)&1)!=0;if(open)AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,Vector3.zero,new Vector3(1,1,.1875f),rot);else AddBox(v,uv,sl,ind,s,d,x,my,z,ly,top?new Vector3(0,.8125f,0):Vector3.zero,top?Vector3.one:new Vector3(1,.1875f,1));}
        // meshWorker d2(): ladder is one inset cutout quad, not a thin cube.
        static void AddLadder(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            const float inset=.05f;int f=meta&3;Vector3 p0,p1,p2,p3;
            if(f==0){p0=new Vector3(0,0,1-inset);p1=new Vector3(1,0,1-inset);p2=new Vector3(0,1,1-inset);p3=new Vector3(1,1,1-inset);}
            else if(f==2){p0=new Vector3(1,0,inset);p1=new Vector3(0,0,inset);p2=new Vector3(1,1,inset);p3=new Vector3(0,1,inset);}
            else if(f==1){p0=new Vector3(inset,0,1);p1=new Vector3(inset,0,0);p2=new Vector3(inset,1,1);p3=new Vector3(inset,1,0);}
            else{p0=new Vector3(1-inset,0,0);p1=new Vector3(1-inset,0,1);p2=new Vector3(1-inset,1,0);p3=new Vector3(1-inset,1,1);}
            byte packed=MaxLight6(s,x+1,ly+1,z+1);Vector3 light=LightVec(packed,.92f),o=new Vector3(x,my,z);int b=v.Count;
            v.Add(o+p0);v.Add(o+p1);v.Add(o+p2);v.Add(o+p3);
            uv.Add(MainUV(d.Side,0,1));uv.Add(MainUV(d.Side,1,1));uv.Add(MainUV(d.Side,0,0));uv.Add(MainUV(d.Side,1,0));
            sl.Add(light);sl.Add(light);sl.Add(light);sl.Add(light);AddQuad(ind,b);
        }

        // meshWorker G2(): exact freestanding/wall torch prism. Wall torches are tilted around a line basis.
        static void AddTorch(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            const float radius=.0625f;Vector3 a,b;
            if(meta>=1)
            {
                Vector3Int dir=HDir[Mathf.Clamp(meta-1,0,3)];const float length=.625f,lean=.414f;
                a=new Vector3(.5f+dir.x*.56f,.22f,.5f+dir.z*.56f);Vector3 dline=new Vector3(-dir.x*lean,1f,-dir.z*lean).normalized;b=a+dline*length;
            }
            else{a=new Vector3(.5f,0,.5f);b=new Vector3(.5f,.625f,.5f);}
            Vector3 axis=(b-a).normalized,up=Mathf.Abs(axis.y)>.92f?Vector3.right:Vector3.up;
            Vector3 side1=Vector3.Cross(axis,up).normalized,side2=Vector3.Cross(axis,side1).normalized;

            byte packed=MaxLight6(s,x+1,ly+1,z+1);Vector3 sideLight=LightVec(packed,.85f),capLight=LightVec(packed,1f);AtlasRect tex=d.Side;
            Vector3 P(bool end,Vector2 q)=>(end?b:a)+radius*(q.x*side1+q.y*side2);
            for(int face=0;face<4;face++)
            {
                Vector2 c0=TorchCorners[face],c1=TorchCorners[(face+1)&3];int n=v.Count;
                Vector3 p0=P(false,c0),p1=P(false,c1),p2=P(true,c0),p3=P(true,c1);
                v.Add(new Vector3(x,my,z)+p0);v.Add(new Vector3(x,my,z)+p1);v.Add(new Vector3(x,my,z)+p2);v.Add(new Vector3(x,my,z)+p3);
                uv.Add(MainUV(tex,.4375f,1f));uv.Add(MainUV(tex,.5625f,1f));uv.Add(MainUV(tex,.4375f,.375f));uv.Add(MainUV(tex,.5625f,.375f));
                sl.Add(sideLight);sl.Add(sideLight);sl.Add(sideLight);sl.Add(sideLight);AddQuad(ind,n);
            }
            {
                int n=v.Count;Vector3 p0=P(true,new Vector2(-1,-1)),p1=P(true,new Vector2(-1,1)),p2=P(true,new Vector2(1,-1)),p3=P(true,new Vector2(1,1));
                v.Add(new Vector3(x,my,z)+p0);v.Add(new Vector3(x,my,z)+p1);v.Add(new Vector3(x,my,z)+p2);v.Add(new Vector3(x,my,z)+p3);
                uv.Add(MainUV(tex,.4375f,.5f));uv.Add(MainUV(tex,.4375f,.3125f));uv.Add(MainUV(tex,.5625f,.5f));uv.Add(MainUV(tex,.5625f,.3125f));
                sl.Add(capLight);sl.Add(capLight);sl.Add(capLight);sl.Add(capLight);AddQuad(ind,n);
            }
        }


        // meshWorker L2(): vine/lichen metadata is a six-bit face mask. Emit directly; this path
        // can run thousands of times in vine-heavy chunks, so never allocate per block.
        static void AddFlatFaces(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            byte packed=MaxLight6(s,x+1,ly+1,z+1);Vector3 light=LightVec(packed,.92f);
            int mask=meta&63;if(mask==0)mask=1;
            if((mask&1)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(0,0,.95f),new Vector3(1,0,.95f),new Vector3(0,1,.95f),new Vector3(1,1,.95f),light);
            if((mask&2)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(.05f,0,1),new Vector3(.05f,0,0),new Vector3(.05f,1,1),new Vector3(.05f,1,0),light);
            if((mask&4)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(1,0,.05f),new Vector3(0,0,.05f),new Vector3(1,1,.05f),new Vector3(0,1,.05f),light);
            if((mask&8)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(.95f,0,0),new Vector3(.95f,0,1),new Vector3(.95f,1,0),new Vector3(.95f,1,1),light);
            if((mask&16)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(0,.95f,0),new Vector3(1,.95f,0),new Vector3(0,.95f,1),new Vector3(1,.95f,1),light);
            if((mask&32)!=0)AddFlatFace(v,uv,sl,ind,d.Side,x,my,z,new Vector3(0,.05f,1),new Vector3(1,.05f,1),new Vector3(0,.05f,0),new Vector3(1,.05f,0),light);
        }

        static void AddFlatFace(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,AtlasRect tex,int x,int my,int z,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 light)
        {
            Vector3 o=new Vector3(x,my,z);int n=v.Count;v.Add(o+a);v.Add(o+b);v.Add(o+c);v.Add(o+d);
            uv.Add(MainUV(tex,0,1));uv.Add(MainUV(tex,1,1));uv.Add(MainUV(tex,0,0));uv.Add(MainUV(tex,1,0));
            sl.Add(light);sl.Add(light);sl.Add(light);sl.Add(light);AddQuad(ind,n);
        }

        // meshWorker SC / pC(): (meta&255)-1 indexes this list of potted plants.
        static readonly BlockId[] PotPlants={BlockId.Dandelion,BlockId.Poppy,BlockId.Fern,BlockId.DeadBush,BlockId.Allium,BlockId.AzureBluet,
            BlockId.BlueOrchid,BlockId.Cornflower,BlockId.LilyOfTheValley,BlockId.OxeyeDaisy,BlockId.OrangeTulip,BlockId.PinkTulip,
            BlockId.RedTulip,BlockId.WhiteTulip,BlockId.WitherRose,BlockId.BrownMushroom,BlockId.RedMushroom,BlockId.CrimsonRoots,
            BlockId.WarpedRoots,BlockId.CrimsonFungus,BlockId.WarpedFungus,BlockId.OakSapling,BlockId.SpruceSapling,BlockId.BirchSapling,
            BlockId.JungleSapling,BlockId.AcaciaSapling,BlockId.DarkOakSapling};
        static BlockId PotPlant(byte meta)
        {
            int i=(meta&255)-1;
            return (uint)i<(uint)PotPlants.Length?PotPlants[i]:BlockId.Air;
        }
        // meshWorker pot branch: 3/8-high pot box plus a compact crossed plant when metadata contains one.
        static void AddPot(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            BlockId plant=PotPlant(meta);AtlasRect top=plant!=BlockId.Air?BlockRegistry.Get(BlockId.Dirt).Top:d.Top;
            BlockDef pot=new BlockDef(d.Layer,false,false,d.Side,top,d.Bottom);
            AddBox(v,uv,sl,ind,s,pot,x,my,z,ly,new Vector3(.3125f,0,.3125f),new Vector3(.6875f,.375f,.6875f));
            if(plant==BlockId.Air)return;AtlasRect tex=BlockRegistry.Get(plant).Side;byte packed=MaxLight6(s,x+1,ly+1,z+1);Vector3 light=LightVec(packed,.95f);
            AddPottedPlantQuad(v,uv,sl,ind,x,my,z,.32f,.32f,.68f,.68f,tex,light);
            AddPottedPlantQuad(v,uv,sl,ind,x,my,z,.68f,.32f,.32f,.68f,tex,light);
        }

        static void AddPottedPlantQuad(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,int x,int my,int z,float x0,float z0,float x1,float z1,AtlasRect tex,Vector3 light)
        {
            int n=v.Count;v.Add(new Vector3(x+x0,my+.28f,z+z0));v.Add(new Vector3(x+x1,my+.28f,z+z1));v.Add(new Vector3(x+x0,my+1f,z+z0));v.Add(new Vector3(x+x1,my+1f,z+z1));
            uv.Add(MainUV(tex,0,1));uv.Add(MainUV(tex,1,1));uv.Add(MainUV(tex,0,0));uv.Add(MainUV(tex,1,0));sl.Add(light);sl.Add(light);sl.Add(light);sl.Add(light);AddQuad(ind,n);
        }


        // meshWorker t2(): ship wheel is assembled from source rectangle masks, not a sprite/cube.
        static bool WheelSpoke(float x,float y)
        {
            const float width=1.5f;
            for(int q=0;q<8;q++){float a=q*Mathf.PI/4f,c=Mathf.Cos(a),sn=Mathf.Sin(a);if(x*c+y*sn>0f&&Mathf.Abs(y*c-x*sn)<=width)return true;}
            return false;
        }
        static List<Vector4> MergeWheelMask(bool[,] mask)
        {
            int n=mask.GetLength(0),center=(n-1)/2;bool[,] used=new bool[n,n];var rects=new List<Vector4>();
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                if(!mask[y,x]||used[y,x])continue;int right=x;while(right+1<n&&mask[y,right+1]&&!used[y,right+1])right++;int bottom=y;
                for(int yy=y+1;yy<n;yy++){bool ok=true;for(int xx=x;xx<=right;xx++)if(!mask[yy,xx]||used[yy,xx]){ok=false;break;}if(!ok)break;bottom=yy;}
                for(int yy=y;yy<=bottom;yy++)for(int xx=x;xx<=right;xx++)used[yy,xx]=true;
                rects.Add(new Vector4(x-center-.5f,y-center-.5f,right-center+.5f,bottom-center+.5f));
            }
            return rects;
        }
        static Vector4[] CopyRects(List<Vector4> source){var result=new Vector4[source.Count];source.CopyTo(result);return result;}
        static Vector4[][] BuildWheelRects()
        {
            const int size=31;const float center=15f;bool[,] hub=new bool[size,size],disc=new bool[size,size],grip=new bool[size,size];
            for(int yy=0;yy<size;yy++)for(int xx=0;xx<size;xx++)
            {float rx=xx-center,ry=yy-center,ax=Mathf.Abs(rx),ay=Mathf.Abs(ry),r=Mathf.Sqrt(rx*rx+ry*ry);bool spoke=WheelSpoke(rx,ry);hub[yy,xx]=ax<=3f&&ay<=3f;disc[yy,xx]=(r>=8.4f&&r<=11.6f)||(r>3f&&r<8.8f&&spoke);grip[yy,xx]=r>11.6f&&r<=15.5f&&spoke;}
            return new[]{CopyRects(MergeWheelMask(hub)),CopyRects(MergeWheelMask(disc)),CopyRects(MergeWheelMask(grip))};
        }
        static void AddWheelPiece(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,int x,int my,int z,int ly,Vector3 lo,Vector3 hi,int rot,AtlasRect tex)
        {var d=new BlockDef(RenderLayer.Opaque,true,false,tex,tex,tex);AddRotatedBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi,rot);}
        static void AddShipWheel(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,int x,int my,int z,int ly,byte meta)
        {
            int rot=meta&3;AtlasRect spruce=AtlasLayout.SprucePlanks,oak=AtlasLayout.OakPlanks,birch=AtlasLayout.BirchPlanks;
            AddWheelPiece(v,uv,sl,ind,s,x,my,z,ly,new Vector3(.3f,0,.3f),new Vector3(.7f,.085f,.7f),rot,spruce);
            AddWheelPiece(v,uv,sl,ind,s,x,my,z,ly,new Vector3(.35f,.085f,.35f),new Vector3(.65f,.15f,.65f),rot,spruce);
            AddWheelPiece(v,uv,sl,ind,s,x,my,z,ly,new Vector3(.4f,.15f,.445f),new Vector3(.6f,.42f,.555f),rot,spruce);
            const float u=.379f/15.5f,cy=.615f;
            for(int k=0;k<3;k++)
            {
                float z0=k==0?.38f:k==1?.42f:.4f,z1=k==0?.62f:k==1?.58f:.6f;var rects=WheelRects[k];
                for(int ri=0;ri<rects.Length;ri++){Vector4 r=rects[ri];AddWheelPiece(v,uv,sl,ind,s,x,my,z,ly,new Vector3(.5f+r.x*u,cy+r.y*u,z0),new Vector3(.5f+r.z*u,cy+r.w*u,z1),rot,oak);}
            }
            AddWheelPiece(v,uv,sl,ind,s,x,my,z,ly,new Vector3(.5f-2.5f*u,cy-2.5f*u,.37f),new Vector3(.5f+2.5f*u,cy+2.5f*u,.63f),rot,birch);
        }

        // meshWorker SE(): beds are half slabs; their top UV rotates from direction bits 1..2.
        static void AddBed(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            Vector3 lo=(meta&1)!=0?new Vector3(0,.5f,0):Vector3.zero;
            Vector3 hi=(meta&1)!=0?Vector3.one:new Vector3(1,.5f,1);
            int rot=(2-((meta>>1)&3))&3;
            AddBox(v,uv,sl,ind,s,d,x,my,z,ly,lo,hi,false,false,rot);
        }

        static Vector2 Rotate01(Vector2 q,int rot)
        {
            for(int i=0;i<(rot&3);i++){float old=q.x;q.x=1f-q.y;q.y=old;}
            return q;
        }
        static Vector2 RotatePoint01(Vector2 q,int rot)
        {
            rot=((rot%4)+4)%4;
            if(rot==0)return q;if(rot==1)return new Vector2(1f-q.y,q.x);if(rot==2)return new Vector2(1f-q.x,1f-q.y);return new Vector2(q.y,1f-q.x);
        }

        // Exact meshWorker c2()/door SE() geometry: 3/16 panel, facing/open/hinge metadata,
        // thin edge UV and side mirroring preserved.
        static void AddDoor(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta)
        {
            int facing=meta&3; bool upper=((meta>>2)&1)!=0, open=((meta>>3)&1)!=0, hinge=((meta>>4)&1)!=0;
            Vector3 lo,hi;
            if(open)
            {
                if(hinge){lo=new Vector3(.8125f,0,0);hi=Vector3.one;}
                else{lo=Vector3.zero;hi=new Vector3(.1875f,1,1);}
            }
            else{lo=Vector3.zero;hi=new Vector3(1,1,.1875f);}
            RotateBox(lo,hi,facing,out var rlo,out var rhi);lo=rlo;hi=rhi;
            AtlasRect tex=upper?d.Top:d.Bottom;
            Vector2 hingePoint=RotatePoint01(hinge?new Vector2(1,0):Vector2.zero,facing);
            int flipMask=0,edgeMask=63;
            if(hi.x-lo.x<hi.z-lo.z)
            {
                if(hingePoint.y==1f)flipMask|=1; if(hingePoint.y==0f)flipMask|=2; edgeMask&=~3;
            }
            else
            {
                if(hingePoint.x==1f)flipMask|=1<<4; if(hingePoint.x==0f)flipMask|=1<<5; edgeMask&=~((1<<4)|(1<<5));
            }
            AddDoorBox(v,uv,sl,ind,s,tex,x,my,z,ly,lo,hi,flipMask,edgeMask);
        }

        static void AddDoorBox(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,AtlasRect tex,int x,int my,int z,int ly,Vector3 lo,Vector3 hi,int flipMask,int edgeMask)
        {
            byte own=s.GetLight(x+1,ly+1,z+1);
            for(int f=0;f<6;f++)
            {
                var nn=N[f];bool touches=f==0&&hi.x==1||f==1&&lo.x==0||f==2&&hi.y==1||f==3&&lo.y==0||f==4&&hi.z==1||f==5&&lo.z==0;
                if(touches&&BlockRegistry.IsFullOpaque(s.Get(x+1+nn.x,ly+1+nn.y,z+1+nn.z)))continue;
                byte nb=s.GetLight(x+1+nn.x,ly+1+nn.y,z+1+nn.z);byte packed=(byte)((Mathf.Max(own>>4,nb>>4)<<4)|Mathf.Max(own&15,nb&15));
                int b=v.Count;
                for(int i=0;i<4;i++)
                {
                    Vector3 c=C[f,i];float px=c.x>.5f?hi.x:lo.x,py=c.y>.5f?hi.y:lo.y,pz=c.z>.5f?hi.z:lo.z;
                    float u,t;if(f==0){u=pz;t=1-py;}else if(f==1){u=1-pz;t=1-py;}else if(f==2){u=px;t=pz;}else if(f==3){u=px;t=1-pz;}else if(f==4){u=px;t=1-py;}else{u=1-px;t=1-py;}
                    if((edgeMask&(1<<f))!=0)u=.04f;if((flipMask&(1<<f))!=0)u=1f-u;
                    v.Add(new Vector3(x+px,my+py,z+pz));uv.Add(MainUV(tex,u,t));sl.Add(new Vector3(Shade[f],(packed>>4)/15f,(packed&15)/15f));
                }
                AddQuad(ind,b);
            }
        }

        static int RailShape(SectionSnapshot s,int x,int y,int z,byte meta,out int a,out int b)
        {
            int mask=0,slope=-1;for(int i=0;i<4;i++){var q=RailDir[i];if(B(s,x+q.x,y,z+q.y)==BlockId.Rail)mask|=1<<i;else if(B(s,x+q.x,y+1,z+q.y)==BlockId.Rail){mask|=1<<i;if(slope<0)slope=i;}}
            if(slope>=0){a=slope;b=0;return 1;}if((mask&1)!=0&&(mask&2)!=0){a=1;b=0;return 0;}if((mask&4)!=0&&(mask&8)!=0){a=0;b=0;return 0;}
            int xx=mask&3,zz=mask&12;if(xx!=0&&zz!=0){a=(mask&1)!=0?0:1;b=(mask&4)!=0?2:3;return 2;}a=xx!=0?1:zz!=0?0:(meta&1);b=0;return 0;
        }
        static int RailCurveRotation(int xd,int zd){return xd==0&&zd==2?0:xd==0&&zd==3?3:xd==1&&zd==2?1:2;}
        static void AddRail(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockDef d,int x,int my,int z,int ly,byte meta,float classAdd=0f)
        {
            int a,b;int kind=RailShape(s,x,ly,z,meta,out a,out b);AtlasRect r=kind==2?AtlasLayout.RailCorner:d.Top;int uvRot=kind==2?RailCurveRotation(a,b):(kind==1?(a==0||a==1?1:0):a==1?1:0);
            float y0=.0625f;float H(float px,float pz){if(kind!=1)return y0;return a==0?(px>.5f?1:y0):a==1?(px<.5f?1:y0):a==2?(pz>.5f?1:y0):(pz<.5f?1:y0);}
            byte l=MaxLight6(s,x+1,ly+1,z+1);Vector3 lv=LightVec(l,.96f+classAdd),o=new Vector3(x,my,z);int n=v.Count;
            v.Add(o+new Vector3(0,H(0,0),0));v.Add(o+new Vector3(1,H(1,0),0));v.Add(o+new Vector3(0,H(0,1),1));v.Add(o+new Vector3(1,H(1,1),1));
            for(int i=0;i<4;i++){Vector2 q=RailUV[uvRot,i];uv.Add(MainUV(r,q.x,q.y));sl.Add(lv);}AddQuad(ind,n);
        }

        static float WaterHeight(SectionSnapshot s,BlockId id,int x,int ly,int z)
        {
            if(!BlockRegistry.IsWater(id))return 0f;
            return BlockRegistry.IsWater(s.Get(x+1,ly+2,z+1))?1f:BlockRegistry.WaterSurfaceHeight(id);
        }

        static bool WaterFaceVisible(SectionSnapshot s,BlockId self,BlockId other,FaceDir face,int x,int ly,int z)
        {
            if(!BlockRegistry.IsWater(other))return BlockRegistry.FaceVisible(self,other);
            if(face==FaceDir.PosY||face==FaceDir.NegY)return false;
            var n=N[(int)face];
            float a=WaterHeight(s,self,x,ly,z);
            float b=WaterHeight(s,other,x+n.x,ly+n.y,z+n.z);
            // meshWorker also skips this face when the current water is full height (water above it),
            // leaving a see-through seam above the lower neighbour's surface. The port deliberately closes
            // that seam; it is the only intentional mesher difference (tools/parity reports it on real chunks).
            return a>b+.001f;
        }

        static void AddWaterShell(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,int x,int my,int z,int ly)
        {BlockDef water=BlockRegistry.Get(BlockId.Water);BlockId above=s.Get(x+1,ly+2,z+1);float top=Wet(above)?1f:.875f;for(int f=0;f<6;f++){var nn=N[f];BlockId other=s.Get(x+1+nn.x,ly+1+nn.y,z+1+nn.z);if(!BlockRegistry.FaceVisible(BlockId.Water,other))continue;byte packed=s.GetLight(x+1+nn.x,ly+1+nn.y,z+1+nn.z);AtlasRect r=water.Face((FaceDir)f);int b=v.Count;for(int i=0;i<4;i++){Vector3 c=C[f,i];if(c.y>.5f)c.y=top;v.Add(new Vector3(x,my,z)+c);Vector2 q=FUV[f,i];uv.Add(MainUV(r,q.x,q.y));sl.Add(LightVec(packed,Shade[f]));}AddQuad(ind,b);}}

        static void AxisTexture(BlockId id,BlockDef d,byte meta,FaceDir face,out AtlasRect r,out int rot)
        {
            rot=0;if(!BlockRegistry.IsAxisBlock(id)){r=d.Face(face);return;}int axis=meta&3;if(axis>2)axis=0;int plus=axis==1?0:axis==2?4:2,minus=axis==1?1:axis==2?5:3;int f=(int)face;
            if(f==plus)r=d.Top;else if(f==minus)r=d.Bottom;else{r=d.Side;rot=AxisUvRot[axis,f];}
        }
        static void AddFace(List<Vector3> v,List<Vector2> uv,List<Vector3> sl,List<int> ind,SectionSnapshot s,BlockId id,BlockDef d,FaceDir face,int x,int my,int z,int ly,byte meta)
        {
            int b=v.Count;AtlasRect r;int uvRot;AxisTexture(id,d,meta,face,out r,out uvRot);
            if(id==BlockId.Chest)
            {
                int f=(int)face,dir=meta&3;bool dbl=(meta&4)!=0,rightHalf=(meta&8)!=0;

                if(f==2&&dbl){r=rightHalf?AtlasLayout.ChestTopL:AtlasLayout.ChestTopR;uvRot=(2-dir)&3;}
                if(ChestFaceFacing[f]==dir)
                {
                    r=AtlasLayout.ChestFront;
                    if(dbl){bool sideAxis=dir==1||dir==3;r=(rightHalf!=sideAxis)?AtlasLayout.ChestFrontL:AtlasLayout.ChestFrontR;}
                }
            }
            bool waterBlock=BlockRegistry.IsWater(id);bool lavaBlock=BlockRegistry.IsLava(id);bool fluid=waterBlock||lavaBlock;
            float top=waterBlock?WaterHeight(s,id,x,ly,z):1f;
            if(lavaBlock)top=BlockRegistry.IsLava(s.Get(x+1,ly+2,z+1))?1f:BlockRegistry.LavaSurfaceHeight(id);
            int level0=3,level1=3,level2=3,level3=3;
            for(int i=0;i<4;i++)
            {
                var fn=N[(int)face];BlockId faceNeighbor=s.Get(x+1+fn.x,ly+1+fn.y,z+1+fn.z);
                Vector3 p=C[(int)face,i];
                if(waterBlock)
                {
                    float low=(face!=FaceDir.PosY&&face!=FaceDir.NegY&&BlockRegistry.IsWater(faceNeighbor))?WaterHeight(s,faceNeighbor,x+fn.x,ly+fn.y,z+fn.z):0f;
                    p.y=p.y>.5f?top:low;
                }
                else if(fluid&&p.y>.5f)p.y=top;
                float push=(fluid&&faceNeighbor!=BlockId.Air&&!BlockRegistry.IsFluid(faceNeighbor))?.004f:0f;
                v.Add(new Vector3(x,my,z)+p-new Vector3(fn.x,fn.y,fn.z)*push);Vector2 q=FUV[(int)face,i];for(int rr=0;rr<uvRot;rr++){float old=q.x;q.x=1-q.y;q.y=old;}uv.Add(MainUV(r,q.x,q.y));
                float shade=Shade[(int)face],sky,blk;if(waterBlock){byte l=s.GetLight(x+1+fn.x,ly+1+fn.y,z+1+fn.z);sky=(l>>4)/15f;blk=(l&15)/15f;}else{SampleCorner(s,face,i,x,ly,z,out int ao,out sky,out blk);if(i==0)level0=ao;else if(i==1)level1=ao;else if(i==2)level2=ao;else level3=ao;shade*=.62f+ao*.1267f;}if(d.Glow)blk=Mathf.Max(blk,.85f);if(BlockRegistry.XrayClassified(id))shade+=4f;sl.Add(new Vector3(shade,sky,blk));
            }
            if(!waterBlock&&level0+level3>level1+level2){ind.Add(b);ind.Add(b+1);ind.Add(b+3);ind.Add(b);ind.Add(b+3);ind.Add(b+2);}else AddQuad(ind,b);
        }
        static void SampleCorner(SectionSnapshot s,FaceDir face,int corner,int x,int y,int z,out int ao,out float sky,out float blk)
        {Vector3 p=C[(int)face,corner];int a1x=0,a1y=0,a1z=0,a2x=0,a2y=0,a2z=0;switch(face){case FaceDir.PosX:case FaceDir.NegX:a1y=p.y>.5f?1:-1;a2z=p.z>.5f?1:-1;break;case FaceDir.PosY:case FaceDir.NegY:a1x=p.x>.5f?1:-1;a2z=p.z>.5f?1:-1;break;default:a1x=p.x>.5f?1:-1;a2y=p.y>.5f?1:-1;break;}var n=N[(int)face];int px=x+1+n.x,py=y+1+n.y,pz=z+1+n.z;bool o1=BlockRegistry.OccludesAO(s.Get(px+a1x,py+a1y,pz+a1z)),o2=BlockRegistry.OccludesAO(s.Get(px+a2x,py+a2y,pz+a2z)),od=BlockRegistry.OccludesAO(s.Get(px+a1x+a2x,py+a1y+a2y,pz+a1z+a2z));ao=o1&&o2?0:3-(o1?1:0)-(o2?1:0)-(od?1:0);byte baseL=s.GetLight(px,py,pz);int ss=baseL>>4,bb=baseL&15,count=1;if(!o1)AddLight(s.GetLight(px+a1x,py+a1y,pz+a1z),ref ss,ref bb,ref count);if(!o2)AddLight(s.GetLight(px+a2x,py+a2y,pz+a2z),ref ss,ref bb,ref count);if(!od)AddLight(s.GetLight(px+a1x+a2x,py+a1y+a2y,pz+a1z+a2z),ref ss,ref bb,ref count);sky=ss/(count*15f);blk=bb/(count*15f);}
        static void AddLight(byte p,ref int sky,ref int blk,ref int count){sky+=p>>4;blk+=p&15;count++;}
    }
}
