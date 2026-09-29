using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js Es()/t1()/AD()/Yu() lighting for the currently ported block set.
    /// Packed byte: high nibble = skylight, low nibble = block light.
    /// </summary>
    public static class VoxelLighting
    {
        internal struct LightNode
        {
            public int X, Y, Z;
            public LightNode(int x, int y, int z) { X=x; Y=y; Z=z; }
        }

        static readonly int[] DX = { 1,-1,0,0,0,0 };
        static readonly int[] DY = { 0,0,1,-1,0,0 };
        static readonly int[] DZ = { 0,0,0,0,1,-1 };
        [ThreadStatic] static Queue<LightNode> localSkyScratch;
        [ThreadStatic] static Queue<LightNode> localBlockScratch;
        [ThreadStatic] static Queue<LightNode> stitchSkyScratch;
        [ThreadStatic] static Queue<LightNode> stitchBlockScratch;

        /// <summary>
        /// Runtime cross-chunk lighting continuation. Streaming owns one of these jobs at a time and
        /// advances it against the shared frame deadline. The queues are persistent job state, so
        /// yielding changes only latency, never the source propagation order/result.
        /// </summary>
        public sealed class WorldStitchJob
        {
            internal ChunkCoord Chunk;
            internal int X0,Z0;
            internal int SeedPos;
            internal int SeedY=VoxelConstants.MinY;
            internal int SeedMaxY=int.MinValue;
            internal int Phase; // 0 border seed, 1 sky, 2 block, 3 done
            internal readonly Queue<LightNode> Sky=new Queue<LightNode>(2048);
            internal readonly Queue<LightNode> Block=new Queue<LightNode>(512);
            public readonly HashSet<ChunkCoord> DirtyChunks=new HashSet<ChunkCoord>();
            public bool Done=>Phase>=3;
        }

        public static WorldStitchJob BeginWorldStitch(ChunkCoord chunk)
        {
            return new WorldStitchJob{Chunk=chunk,X0=chunk.X*16,Z0=chunk.Z*16};
        }

        // main.js U4(): Yu() never scans the entire -64..319 column when stitching a new chunk.
        // It stops at the top of the highest allocated section in the surrounding 3x3 columns.
        static int SourceLoadedStitchMaxY(Dictionary<ChunkCoord,ChunkColumn> world,ChunkCoord center)
        {
            int highestSection=0;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                if(!world.TryGetValue(new ChunkCoord(center.X+dx,center.Z+dz),out var c))continue;
                for(int sec=VoxelConstants.SectionCount-1;sec>highestSection;sec--)
                    if(c.Sections[sec]!=null){highestSection=sec;break;}
            }
            return Math.Min(VoxelConstants.MaxY,VoxelConstants.MinY+(highestSection+1)*16);
        }

        /// <summary>
        /// Cooperative Yu() continuation used only by runtime streaming. It performs exactly the same
        /// border seeding and propagation as StitchNeighbors(), but may yield at a frame deadline and
        /// resume from its queues on the next frame. No fixed node count or quality reduction exists.
        /// </summary>
        public static bool StepWorldStitch(Dictionary<ChunkCoord,ChunkColumn> world,WorldStitchJob job,double deadlineMs)
        {
            if(job==null||job.Done)return true;
            int clockCheck=0;
            // The loaded-chunk set may change between cooperative steps, so the 3x3 cache is rebuilt per call.
            var cache=new StitchCache(world,job.Chunk);
            if(job.SeedMaxY==int.MinValue)job.SeedMaxY=SourceLoadedStitchMaxY(world,job.Chunk);
            if(job.Phase==0)
            {
                // Flatten the 18x18 one-block ring into a continuation cursor. Inner 16x16 cells are
                // skipped exactly like StitchNeighbors(); Y is persisted so even seed scanning yields.
                while(job.SeedPos<18*18)
                {
                    int ix=job.SeedPos/18-1, iz=job.SeedPos%18-1;
                    if(ix>=0&&ix<16&&iz>=0&&iz<16)
                    {
                        job.SeedPos++;job.SeedY=VoxelConstants.MinY;continue;
                    }
                    int wx=job.X0+ix,wz=job.Z0+iz;
                    ChunkColumn seedCol=cache.Column(wx,wz);
                    if(seedCol==null)
                    {
                        job.SeedPos++;job.SeedY=VoxelConstants.MinY;continue;
                    }
                    int slx=wx&15,slz=wz&15;
                    while(job.SeedY<=job.SeedMaxY)
                    {
                        int y=job.SeedY++;
                        byte packed=seedCol.GetLightLocal(slx,y,slz);
                        if((packed>>4)>1)job.Sky.Enqueue(new LightNode(wx,y,wz));
                        if((packed&15)>1)job.Block.Enqueue(new LightNode(wx,y,wz));
                        if((++clockCheck&31)==0&&Time.realtimeSinceStartupAsDouble*1000.0>=deadlineMs){cache.FlushDirty(job.DirtyChunks);return false;}
                    }
                    job.SeedPos++;job.SeedY=VoxelConstants.MinY;
                }
                job.Phase=1;
            }

            if(job.Phase==1)
            {
                while(job.Sky.Count>0)
                {
                    PropagateWorldNode(ref cache,job.Sky,true);
                    if((++clockCheck&31)==0&&Time.realtimeSinceStartupAsDouble*1000.0>=deadlineMs){cache.FlushDirty(job.DirtyChunks);return false;}
                }
                job.Phase=2;
            }
            if(job.Phase==2)
            {
                while(job.Block.Count>0)
                {
                    PropagateWorldNode(ref cache,job.Block,false);
                    if((++clockCheck&31)==0&&Time.realtimeSinceStartupAsDouble*1000.0>=deadlineMs){cache.FlushDirty(job.DirtyChunks);return false;}
                }
                job.Phase=3;
            }
            cache.FlushDirty(job.DirtyChunks);
            return true;
        }

        /// <summary>AD(): bake one generated chunk before it is attached to the loaded-world dictionary.</summary>
        public static void BakeColumn(ChunkColumn c)
        {
            ClearLight(c);
            for (int x=0;x<16;x++) for (int z=0;z<16;z++) BakeVerticalSky(c,x,z);

            var sky=localSkyScratch??(localSkyScratch=new Queue<LightNode>(4096));sky.Clear();
            var block=localBlockScratch??(localBlockScratch=new Queue<LightNode>(1024));block.Clear();
            SeedLocalSkyFrontier(c, sky);
            SeedEmissive(c, block);
            PropagateLocal(c, sky, true);
            PropagateLocal(c, block, false);
        }

        static void ClearLight(ChunkColumn c)
        {
            for (int s=0;s<c.Sections.Length;s++)
            {
                var sec=c.Sections[s];
                if (sec!=null) Array.Clear(sec.Light,0,sec.Light.Length);
            }
        }

        /// <summary>Es(): vertical direct sky column.</summary>
        static void BakeVerticalSky(ChunkColumn c,int x,int z)
        {
            int sky=15;
            int ceiling=VoxelConstants.MinY-1;
            for (int y=VoxelConstants.MaxY;y>=VoxelConstants.MinY;y--)
            {
                int yi=y-VoxelConstants.MinY;
                int sy=yi>>4;
                var sec=c.Sections[sy];
                BlockId id=sec==null?BlockId.Air:sec.Get(x,yi&15,z);

                if (sky>0 && !BlockRegistry.LightPasses(id))
                {
                    if (ceiling<VoxelConstants.MinY) ceiling=y;
                    sky=0;
                }
                else if (sky>0 && BlockRegistry.LightCost(id)>1)
                {
                    sky=Math.Max(0,sky-2);
                }

                if (sec!=null)
                {
                    int ly=yi&15;
                    byte old=sec.GetLight(x,ly,z);
                    sec.SetLight(x,ly,z,(byte)((sky<<4)|(old&15)));
                }
            }
            c.LightCeilings[x*16+z]=(short)ceiling;
        }

        /// <summary>
        /// AD() only queues skylight cells that border a lower-light cell. This avoids flooding every open-sky voxel.
        /// </summary>
        static void SeedLocalSkyFrontier(ChunkColumn c,Queue<LightNode> q)
        {
            for (int s=0;s<c.Sections.Length;s++)
            {
                var sec=c.Sections[s];
                if (sec==null) continue;
                int y0=VoxelConstants.MinY+s*16;
                for (int x=0;x<16;x++) for (int z=0;z<16;z++) for (int ly=0;ly<16;ly++)
                {
                    int sky=sec.GetLight(x,ly,z)>>4;
                    if (sky<=1) continue;
                    int y=y0+ly;
                    if (LocalSky(c,x+1,y,z)<sky || LocalSky(c,x-1,y,z)<sky ||
                        LocalSky(c,x,y,z+1)<sky || LocalSky(c,x,y,z-1)<sky ||
                        LocalSky(c,x,y-1,z)<sky)
                        q.Enqueue(new LightNode(x,y,z));
                }
            }
        }

        static int LocalSky(ChunkColumn c,int x,int y,int z)
        {
            if (y>VoxelConstants.MaxY) return 15;
            if (y<VoxelConstants.MinY) return 0;
            if ((uint)x>=16 || (uint)z>=16) return 15; // missing neighbour chunk has main's Ee() = 0xF0.
            return c.GetLightLocal(x,y,z)>>4;
        }

        static void SeedEmissive(ChunkColumn c,Queue<LightNode> q)
        {
            for (int s=0;s<c.Sections.Length;s++)
            {
                var sec=c.Sections[s];
                if (sec==null) continue;
                int y0=VoxelConstants.MinY+s*16;
                for (int x=0;x<16;x++) for (int z=0;z<16;z++) for (int ly=0;ly<16;ly++)
                {
                    BlockId id=sec.Get(x,ly,z);
                    byte meta=sec.GetMeta(x,ly,z);
                    byte emission=SourceEmission(c,x,y0+ly,z,id,meta);
                    if (emission==0) continue;
                    byte old=sec.GetLight(x,ly,z);
                    sec.SetLight(x,ly,z,(byte)((old&0xF0)|emission));
                    q.Enqueue(new LightNode(x,y0+ly,z));
                }
            }
        }


        // main di()/Hw(): SEA_PICKLE emits only while waterlogged (meta bit 4) or touching water/aquatic.
        static byte SourceEmission(ChunkColumn c,int x,int y,int z,BlockId id,byte meta)
        {
            if (id==BlockId.SeaPickle)
            {
                if ((meta&4)!=0 || WetLocal(c,x,y+1,z) || WetLocal(c,x+1,y,z) || WetLocal(c,x-1,y,z) ||
                    WetLocal(c,x,y,z+1) || WetLocal(c,x,y,z-1))
                    return (byte)(3+3*((meta&3)+1));
                return 0;
            }
            return BlockRegistry.Emission(id,meta);
        }

        static bool WetLocal(ChunkColumn c,int x,int y,int z)
        {
            if (y<VoxelConstants.MinY || y>VoxelConstants.MaxY || (uint)x>=16 || (uint)z>=16) return false;
            BlockId b=c.GetLocal(x,y,z);
            return BlockRegistry.IsWater(b)||BlockRegistry.IsAquatic(b);
        }

        static void PropagateLocal(ChunkColumn c,Queue<LightNode> q,bool skyMode)
        {
            while (q.Count>0)
            {
                LightNode p=q.Dequeue();
                byte packed=c.GetLightLocal(p.X,p.Y,p.Z);
                int current=skyMode?packed>>4:packed&15;
                if (current<=1) continue;

                for (int d=0;d<6;d++)
                {
                    int nx=p.X+DX[d], ny=p.Y+DY[d], nz=p.Z+DZ[d];
                    if ((uint)nx>=16 || (uint)nz>=16 || ny<VoxelConstants.MinY || ny>VoxelConstants.MaxY) continue;
                    BlockId target=c.GetLocal(nx,ny,nz);
                    if (!BlockRegistry.LightPasses(target)) continue;
                    int next=current-BlockRegistry.LightCost(target);
                    if (next<=0) continue;
                    byte old=c.GetLightLocal(nx,ny,nz);
                    int existing=skyMode?old>>4:old&15;
                    if (next<=existing) continue;
                    c.SetLightLocal(nx,ny,nz,skyMode?(byte)((old&15)|(next<<4)):(byte)((old&0xF0)|next));
                    if (next>1) q.Enqueue(new LightNode(nx,ny,nz));
                }
            }
        }

        /// <summary>
        /// Yu(): once a chunk is attached to the loaded world, seed the one-block ring around it from loaded neighbours
        /// and let both sky and block light cross chunk borders. Returns chunks whose light bytes changed.
        /// </summary>
        public static HashSet<ChunkCoord> StitchNeighbors(Dictionary<ChunkCoord,ChunkColumn> world,ChunkCoord chunk)
        {
            var dirty=new HashSet<ChunkCoord>();
            StitchNeighbors(world,chunk,dirty);
            return dirty;
        }

        public static void StitchNeighbors(Dictionary<ChunkCoord,ChunkColumn> world,ChunkCoord chunk,HashSet<ChunkCoord> dirty)
        {
            dirty.Clear();
            var sky=stitchSkyScratch??(stitchSkyScratch=new Queue<LightNode>(2048));sky.Clear();
            var block=stitchBlockScratch??(stitchBlockScratch=new Queue<LightNode>(512));block.Clear();
            int x0=chunk.X*16, z0=chunk.Z*16;
            var cache=new StitchCache(world,chunk);

            for (int wx=x0-1;wx<=x0+16;wx++) for (int wz=z0-1;wz<=z0+16;wz++)
            {
                if (wx>=x0 && wx<x0+16 && wz>=z0 && wz<z0+16) continue;
                ChunkColumn col=cache.Column(wx,wz);
                if (col==null) continue;
                int lx=wx&15,lz=wz&15;
                for (int y=VoxelConstants.MinY;y<=VoxelConstants.MaxY;y++)
                {
                    byte p=col.GetLightLocal(lx,y,lz);
                    if ((p>>4)>1) sky.Enqueue(new LightNode(wx,y,wz));
                    if ((p&15)>1) block.Enqueue(new LightNode(wx,y,wz));
                }
            }

            while(sky.Count>0)PropagateWorldNode(ref cache,sky,true);
            while(block.Count>0)PropagateWorldNode(ref cache,block,false);
            cache.FlushDirty(dirty);
        }

        /// <summary>
        /// Yu() propagation touches only the new chunk and its 8 neighbours (light <= 15 cannot travel
        /// further from the one-block seed ring). Resolving those columns through a 3x3 array instead of a
        /// Dictionary lookup per voxel access is the dominant cost saving; anything outside falls back to
        /// the dictionary so results stay identical.
        /// </summary>
        internal struct StitchCache
        {
            readonly Dictionary<ChunkCoord,ChunkColumn> world;
            readonly ChunkColumn[] cols;
            readonly int cx,cz;
            int dirtyMask;
            HashSet<ChunkCoord> farDirty;
            [ThreadStatic] static ChunkColumn[] scratchCols;

            public StitchCache(Dictionary<ChunkCoord,ChunkColumn> world,ChunkCoord center)
            {
                this.world=world;cx=center.X;cz=center.Z;dirtyMask=0;farDirty=null;
                cols=scratchCols??(scratchCols=new ChunkColumn[9]);
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {
                    world.TryGetValue(new ChunkCoord(cx+dx,cz+dz),out var c);
                    cols[(dx+1)+(dz+1)*3]=c;
                }
            }

            public ChunkColumn Column(int wx,int wz)
            {
                int ccx=wx>>4,ccz=wz>>4,ix=ccx-cx+1,iz=ccz-cz+1;
                if((uint)ix<3u&&(uint)iz<3u)return cols[ix+iz*3];
                world.TryGetValue(new ChunkCoord(ccx,ccz),out var c);
                return c;
            }

            public void MarkDirty(int wx,int wz)
            {
                int ccx=wx>>4,ccz=wz>>4,ix=ccx-cx+1,iz=ccz-cz+1;
                if((uint)ix<3u&&(uint)iz<3u){dirtyMask|=1<<(ix+iz*3);return;}
                (farDirty??(farDirty=new HashSet<ChunkCoord>())).Add(new ChunkCoord(ccx,ccz));
            }

            public void FlushDirty(HashSet<ChunkCoord> dirty)
            {
                for(int i=0;i<9;i++)if((dirtyMask&(1<<i))!=0)dirty.Add(new ChunkCoord(cx+(i%3)-1,cz+(i/3)-1));
                if(farDirty!=null)dirty.UnionWith(farDirty);
                dirtyMask=0;farDirty=null;
            }
        }

        static void PropagateWorldNode(ref StitchCache cache,Queue<LightNode> q,bool skyMode)
        {
            LightNode p=q.Dequeue();
            byte packed;
            if(p.Y>VoxelConstants.MaxY)packed=0xF0;
            else if(p.Y<VoxelConstants.MinY)packed=0;
            else{var pc=cache.Column(p.X,p.Z);packed=pc==null?(byte)0xF0:pc.GetLightLocal(p.X&15,p.Y,p.Z&15);}
            int current=skyMode?packed>>4:packed&15;
            if(current<=1)return;

            for(int d=0;d<6;d++)
            {
                int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY)continue;
                var nc=cache.Column(nx,nz);
                if(nc==null)continue; // main only writes loaded chunks.
                int lx=nx&15,lz=nz&15;
                BlockId target=nc.GetLocal(lx,ny,lz);
                if(!BlockRegistry.LightPasses(target))continue;
                int next=current-BlockRegistry.LightCost(target);
                if(next<=0)continue;
                byte old=nc.GetLightLocal(lx,ny,lz);
                int existing=skyMode?old>>4:old&15;
                if(next<=existing)continue;
                nc.SetLightLocal(lx,ny,lz,skyMode?(byte)((old&15)|(next<<4)):(byte)((old&0xF0)|next));
                cache.MarkDirty(nx,nz);
                if(next>1)q.Enqueue(new LightNode(nx,ny,nz));
            }
        }

        static byte GetWorldLight(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int y,int wz)
        {
            if (y>VoxelConstants.MaxY) return 0xF0;
            if (y<VoxelConstants.MinY) return 0;
            var cc=ChunkCoord.FromWorld(wx,wz);
            if (!world.TryGetValue(cc,out var c)) return 0xF0;
            return c.GetLightLocal(VoxelConstants.FloorMod(wx,16),y,VoxelConstants.FloorMod(wz,16));
        }

        /// <summary>
        /// Edit path: exact result for the currently loaded 3x3 neighbourhood. Light range is <=15 blocks horizontally,
        /// so a block edit cannot affect chunks beyond this ring. Each chunk is rebaked, then Yu()-style border propagation runs.
        /// </summary>
        public static HashSet<ChunkCoord> RelightNeighborhood(Dictionary<ChunkCoord,ChunkColumn> world,ChunkCoord center)
        {
            var affected=new HashSet<ChunkCoord>();
            for (int dz=-1;dz<=1;dz++) for (int dx=-1;dx<=1;dx++)
            {
                var c=new ChunkCoord(center.X+dx,center.Z+dz);
                if (!world.TryGetValue(c,out var col)) continue;
                BakeColumn(col);
                affected.Add(c);
            }
            // Re-stitch all loaded chunks in the ring; propagation can modify a neighbour in the same ring.
            var seeds=new List<ChunkCoord>(affected);
            for (int i=0;i<seeds.Count;i++) affected.UnionWith(StitchNeighbors(world,seeds[i]));
            return affected;
        }

        internal struct LightState
        {
            public int X,Y,Z,Level;
            public LightState(int x,int y,int z,int level){X=x;Y=y;Z=z;Level=level;}
        }

        [ThreadStatic] static Dictionary<long,byte[]> editSkyCacheScratch;
        [ThreadStatic] static List<byte[]> editRentedScratch;
        [ThreadStatic] static List<LightState> editRemovedScratch;
        [ThreadStatic] static List<LightNode> editSeedsScratch;

        static long ColumnKey(int x,int z)=>((long)x<<32)^(uint)z;

        static bool TryColumn(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int wz,out ChunkColumn c,out int lx,out int lz)
        {
            var cc=ChunkCoord.FromWorld(wx,wz);
            lx=VoxelConstants.FloorMod(wx,16);lz=VoxelConstants.FloorMod(wz,16);
            return world.TryGetValue(cc,out c);
        }

        static BlockId GetWorldBlock(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int y,int wz)
        {
            if(y<VoxelConstants.MinY)return BlockId.Bedrock;
            if(y>VoxelConstants.MaxY)return BlockId.Air;
            if(!TryColumn(world,wx,wz,out var c,out var lx,out var lz))return BlockId.Air;
            return c.GetLocal(lx,y,lz);
        }

        static byte GetWorldMeta(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int y,int wz)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return 0;
            if(!TryColumn(world,wx,wz,out var c,out var lx,out var lz))return 0;
            return c.GetMetaLocal(lx,y,lz);
        }

        static bool SetWorldLight(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int y,int wz,byte value,HashSet<SectionKey> dirtySections)
        {
            if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return false;
            var cc=ChunkCoord.FromWorld(wx,wz);
            if(!world.TryGetValue(cc,out var c))return false;
            int lx=VoxelConstants.FloorMod(wx,16),lz=VoxelConstants.FloorMod(wz,16);
            byte old=c.GetLightLocal(lx,y,lz);
            if(old==value)return false;
            c.SetLightLocal(lx,y,lz,value);
            MarkLightSampleSections(world,dirtySections,cc,lx,y,lz);
            return true;
        }

        // ChunkMesher samples face light and AO up to two voxels around a face corner. Mark only
        // sections whose vertex-light samples can actually observe this changed light cell. This
        // replaces the old Unity-port behavior that remeshed all 24 non-empty sections of every
        // touched chunk after one edit.
        static void MarkLightSampleSections(Dictionary<ChunkCoord,ChunkColumn> world,HashSet<SectionKey> dirtySections,ChunkCoord cc,int lx,int y,int lz)
        {
            if(dirtySections==null)return;
            int sec=(y-VoxelConstants.MinY)>>4;
            int ly=(y-VoxelConstants.MinY)&15;
            int[] ox=lx<=1?LightNegOffsets:(lx>=14?LightPosOffsets:LightZeroOffsets);
            int[] oz=lz<=1?LightNegOffsets:(lz>=14?LightPosOffsets:LightZeroOffsets);
            int[] os=ly<=1?LightNegOffsets:(ly>=14?LightPosOffsets:LightZeroOffsets);
            for(int zi=0;zi<oz.Length;zi++)for(int xi=0;xi<ox.Length;xi++)for(int yi=0;yi<os.Length;yi++)
            {
                int ss=sec+os[yi];
                if((uint)ss>=VoxelConstants.SectionCount)continue;
                var nc=new ChunkCoord(cc.X+ox[xi],cc.Z+oz[zi]);
                if(world.TryGetValue(nc,out var col)&&col.Sections[ss]!=null)dirtySections.Add(new SectionKey(nc,ss));
            }
        }

        static readonly int[] LightZeroOffsets={0};
        static readonly int[] LightNegOffsets={0,-1};
        static readonly int[] LightPosOffsets={0,1};

        // main.js F8()/is(): full cubes block light; special shapes are mostly transmissive.
        // Closed doors still pass skylight in source, while stairs pass only through the vertical axis.
        static bool CanLightPass(BlockId id,byte meta,int axis,bool sky)
        {
            if(BlockRegistry.IsFullOpaque(id))return false;
            var shape=BlockRegistry.Get(id).Shape;
            if(shape==BlockShape.Door)return ((meta>>3)&1)!=0||sky;
            if(shape==BlockShape.Stairs)return axis==1;
            return true;
        }

        static bool DirectSkyPasses(BlockId id)=>!BlockRegistry.IsFullOpaque(id);

        static bool WetWorld(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z)
        {
            BlockId b=GetWorldBlock(world,x,y,z);
            return BlockRegistry.IsWater(b)||BlockRegistry.IsAquatic(b);
        }

        static int SourceEmissionWorld(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z)
        {
            BlockId id=GetWorldBlock(world,x,y,z);byte meta=GetWorldMeta(world,x,y,z);
            if(id==BlockId.SeaPickle)
            {
                if((meta&4)!=0||WetWorld(world,x,y+1,z)||WetWorld(world,x+1,y,z)||WetWorld(world,x-1,y,z)||WetWorld(world,x,y,z+1)||WetWorld(world,x,y,z-1))
                    return 3+3*((meta&3)+1);
                return 0;
            }
            return BlockRegistry.Emission(id,meta);
        }

        static byte[] DirectSkyColumn(Dictionary<ChunkCoord,ChunkColumn> world,int wx,int wz,Dictionary<long,byte[]> cache,List<byte[]> rented)
        {
            long key=ColumnKey(wx,wz);
            if(cache.TryGetValue(key,out var values))return values;
            values=ArrayPool<byte>.Shared.Rent(VoxelConstants.WorldHeight);
            rented.Add(values);cache[key]=values;
            int sky=15,ceiling=VoxelConstants.MinY-1;
            bool hasColumn=TryColumn(world,wx,wz,out var col,out var lx,out var lz);
            for(int y=VoxelConstants.MaxY;y>=VoxelConstants.MinY;y--)
            {
                BlockId id=hasColumn?col.GetLocal(lx,y,lz):BlockId.Air;
                if(sky>0&&!DirectSkyPasses(id))
                {
                    if(ceiling<VoxelConstants.MinY)ceiling=y;
                    sky=0;
                }
                else if(sky>0&&BlockRegistry.LightCost(id)>1)sky=Math.Max(0,sky-2);
                values[y-VoxelConstants.MinY]=(byte)sky;
            }
            if(hasColumn)col.LightCeilings[lx*16+lz]=(short)ceiling;
            return values;
        }

        static void PropagateIncremental(Dictionary<ChunkCoord,ChunkColumn> world,List<LightNode> seeds,bool skyMode,HashSet<SectionKey> dirtySections)
        {
            for(int qi=0;qi<seeds.Count;qi++)PropagateIncrementalNode(world,seeds,qi,skyMode,dirtySections);
        }

        static void PropagateIncrementalNode(Dictionary<ChunkCoord,ChunkColumn> world,List<LightNode> seeds,int index,bool skyMode,HashSet<SectionKey> dirtySections)
        {
            LightNode p=seeds[index];
            byte packed=GetWorldLight(world,p.X,p.Y,p.Z);
            int current=skyMode?packed>>4:packed&15;
            if(current<=1)return;
            BlockId source=GetWorldBlock(world,p.X,p.Y,p.Z);
            byte sourceMeta=GetWorldMeta(world,p.X,p.Y,p.Z);
            bool sourceEmission=!skyMode&&SourceEmissionWorld(world,p.X,p.Y,p.Z)>0;
            for(int d=0;d<6;d++)
            {
                int axis=d<2?0:(d<4?1:2);
                if(!sourceEmission&&!CanLightPass(source,sourceMeta,axis,skyMode))continue;
                int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY)continue;
                var ncc=ChunkCoord.FromWorld(nx,nz);
                if(!world.ContainsKey(ncc))continue;
                BlockId target=GetWorldBlock(world,nx,ny,nz);
                byte targetMeta=GetWorldMeta(world,nx,ny,nz);
                if(!CanLightPass(target,targetMeta,axis,skyMode))continue;
                int next=current-BlockRegistry.LightCost(target);
                if(next<=0)continue;
                byte old=GetWorldLight(world,nx,ny,nz);
                int existing=skyMode?old>>4:old&15;
                if(next<=existing)continue;
                byte value=skyMode?(byte)((old&15)|(next<<4)):(byte)((old&0xF0)|next);
                SetWorldLight(world,nx,ny,nz,value,dirtySections);
                if(next>1)seeds.Add(new LightNode(nx,ny,nz));
            }
        }

        static void RelightBlockIncremental(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z,HashSet<SectionKey> dirtySections,List<LightState> removed,List<LightNode> seeds)
        {
            removed.Clear();seeds.Clear();
            byte start=GetWorldLight(world,x,y,z);int oldLevel=start&15;
            int emission=SourceEmissionWorld(world,x,y,z);
            SetWorldLight(world,x,y,z,(byte)(start&0xF0),dirtySections);
            removed.Add(new LightState(x,y,z,oldLevel));
            for(int qi=0;qi<removed.Count;qi++)
            {
                var p=removed[qi];
                for(int d=0;d<6;d++)
                {
                    int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                    if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                    byte light=GetWorldLight(world,nx,ny,nz);int level=light&15;
                    if(level==0)continue;
                    if(level<p.Level)
                    {
                        int own=SourceEmissionWorld(world,nx,ny,nz);
                        if(own>=level)seeds.Add(new LightNode(nx,ny,nz));
                        else
                        {
                            SetWorldLight(world,nx,ny,nz,(byte)((light&0xF0)|own),dirtySections);
                            removed.Add(new LightState(nx,ny,nz,level));
                            if(own>1)seeds.Add(new LightNode(nx,ny,nz));
                        }
                    }
                    else seeds.Add(new LightNode(nx,ny,nz));
                }
            }
            if(emission>0)
            {
                byte cur=GetWorldLight(world,x,y,z);
                SetWorldLight(world,x,y,z,(byte)((cur&0xF0)|emission),dirtySections);
                seeds.Add(new LightNode(x,y,z));
            }
            for(int d=0;d<6;d++)
            {
                int nx=x+DX[d],ny=y+DY[d],nz=z+DZ[d];
                if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                if((GetWorldLight(world,nx,ny,nz)&15)>1)seeds.Add(new LightNode(nx,ny,nz));
            }
            PropagateIncremental(world,seeds,false,dirtySections);
        }

        static void RelightSkyIncremental(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z,HashSet<SectionKey> dirtySections,Dictionary<long,byte[]> skyCache,List<byte[]> rented,List<LightState> removed,List<LightNode> seeds)
        {
            removed.Clear();seeds.Clear();
            byte[] direct=DirectSkyColumn(world,x,z,skyCache,rented);
            for(int wy=VoxelConstants.MinY;wy<=VoxelConstants.MaxY;wy++)
            {
                byte light=GetWorldLight(world,x,wy,z);int existing=light>>4,directLevel=direct[wy-VoxelConstants.MinY];
                if(existing>directLevel)
                {
                    SetWorldLight(world,x,wy,z,(byte)((light&15)|(directLevel<<4)),dirtySections);
                    removed.Add(new LightState(x,wy,z,existing));
                }
                else if(directLevel>existing)
                {
                    SetWorldLight(world,x,wy,z,(byte)((light&15)|(directLevel<<4)),dirtySections);
                    seeds.Add(new LightNode(x,wy,z));
                }
            }
            BlockId centerBlock=GetWorldBlock(world,x,y,z);byte centerMeta=GetWorldMeta(world,x,y,z);
            if(!CanLightPass(centerBlock,centerMeta,0,true)||!CanLightPass(centerBlock,centerMeta,2,true))
            {
                int level=GetWorldLight(world,x,y,z)>>4;
                if(level>1)removed.Add(new LightState(x,y,z,level));
            }
            for(int qi=0;qi<removed.Count;qi++)
            {
                var p=removed[qi];
                for(int d=0;d<6;d++)
                {
                    int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                    if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                    byte light=GetWorldLight(world,nx,ny,nz);int level=light>>4;
                    if(level==0)continue;
                    if(level<p.Level)
                    {
                        byte[] baseCol=DirectSkyColumn(world,nx,nz,skyCache,rented);
                        int baseline=baseCol[ny-VoxelConstants.MinY];
                        if(level>baseline)
                        {
                            SetWorldLight(world,nx,ny,nz,(byte)((light&15)|(baseline<<4)),dirtySections);
                            removed.Add(new LightState(nx,ny,nz,level));
                            if(baseline>1)seeds.Add(new LightNode(nx,ny,nz));
                        }
                        else seeds.Add(new LightNode(nx,ny,nz));
                    }
                    else seeds.Add(new LightNode(nx,ny,nz));
                }
            }
            for(int d=0;d<6;d++)
            {
                int nx=x+DX[d],ny=y+DY[d],nz=z+DZ[d];
                if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                if((GetWorldLight(world,nx,ny,nz)>>4)>1)seeds.Add(new LightNode(nx,ny,nz));
            }

            // R32: when an opaque block is removed, the nearest valid skylight source can be several
            // cells away around a wall/corner. main.js aD()/nD() keeps the full affected frontier; the
            // old Unity incremental port only seeded the six immediate neighbours, which could leave
            // a newly exposed side face black until another edit. Skylight attenuates to zero within
            // 15 steps, so seed the radius-16 shell from already-valid loaded light and propagate in.
            if(CanLightPass(centerBlock,centerMeta,0,true)||CanLightPass(centerBlock,centerMeta,2,true))
                SeedSkyFrontierShell(world,x,y,z,seeds);
            PropagateIncremental(world,seeds,true,dirtySections);
        }


        static void SeedSkyFrontierShell(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z,List<LightNode> seeds)
        {
            const int r=16;
            int minY=Math.Max(VoxelConstants.MinY,y-r),maxY=Math.Min(VoxelConstants.MaxY,y+r);
            for(int dx=-r;dx<=r;dx++)for(int dz=-r;dz<=r;dz++)
            {
                if(Math.Abs(dx)!=r&&Math.Abs(dz)!=r)continue;
                int wx=x+dx,wz=z+dz;
                if(!world.ContainsKey(ChunkCoord.FromWorld(wx,wz)))continue;
                for(int wy=minY;wy<=maxY;wy++)
                    if((GetWorldLight(world,wx,wy,wz)>>4)>1)seeds.Add(new LightNode(wx,wy,wz));
            }
            for(int dx=-r+1;dx<r;dx++)for(int dz=-r+1;dz<r;dz++)
            {
                int wx=x+dx,wz=z+dz;
                if(!world.ContainsKey(ChunkCoord.FromWorld(wx,wz)))continue;
                if((GetWorldLight(world,wx,minY,wz)>>4)>1)seeds.Add(new LightNode(wx,minY,wz));
                if(maxY!=minY&&(GetWorldLight(world,wx,maxY,wz)>>4)>1)seeds.Add(new LightNode(wx,maxY,wz));
            }
        }

        /// <summary>
        /// Continuation for deferred simulation relighting. Player clicks still use the synchronous
        /// one-edit path below for immediate feedback; water/TNT/background simulation advances this
        /// exact state machine under the shared streaming deadline, so one large cave skylight flood
        /// cannot monopolize a rendered frame.
        /// </summary>
        public sealed class DeferredRelightJob
        {
            internal int X,Y,Z,Phase,Index,BlockEmission,DirectY,ShellPos,ShellY,ShellMinY,ShellMaxY;
            internal bool NeedsSkyRelight,NeedsSkyFrontierShell;
            internal byte[] DirectColumn;
            internal readonly Dictionary<long,byte[]> SkyCache=new Dictionary<long,byte[]>(64);
            internal readonly List<byte[]> Rented=new List<byte[]>(64);
            internal readonly List<LightState> Removed=new List<LightState>(256);
            internal readonly List<LightNode> Seeds=new List<LightNode>(512);
            public readonly HashSet<SectionKey> DirtySections=new HashSet<SectionKey>();
            public bool Done=>Phase>=11;
        }

        public static bool RequiresSkyRelight(BlockId oldId,byte oldMeta,BlockId newId,byte newMeta)
        {
            if(DirectSkyPasses(oldId)!=DirectSkyPasses(newId))return true;
            if(BlockRegistry.LightCost(oldId)!=BlockRegistry.LightCost(newId))return true;
            for(int axis=0;axis<3;axis++)
                if(CanLightPass(oldId,oldMeta,axis,true)!=CanLightPass(newId,newMeta,axis,true))return true;
            return false;
        }

        public static bool RequiresSkyFrontierShell(BlockId oldId,byte oldMeta,BlockId newId,byte newMeta)
        {
            // R32's radius-16 frontier is only needed when an edit can expose *more* skylight.
            // Running it for AIR->WATER (or other transmission-decreasing simulation edits) is
            // both unnecessary and extremely expensive during a fluid cascade. The removal path
            // already carries reduced skylight outward from the changed direct-sky column.
            if(DirectSkyPasses(newId)&&!DirectSkyPasses(oldId))return true;
            if(BlockRegistry.LightCost(newId)<BlockRegistry.LightCost(oldId))return true;
            for(int axis=0;axis<3;axis++)
                if(CanLightPass(newId,newMeta,axis,true)&&!CanLightPass(oldId,oldMeta,axis,true))return true;
            return false;
        }

        public static DeferredRelightJob BeginDeferredRelight(DeferredRelightJob job,int x,int y,int z,bool needsSkyRelight=true,bool needsSkyFrontierShell=true)
        {
            if(job==null)job=new DeferredRelightJob();
            ReleaseDeferredRelightArrays(job);
            job.X=x;job.Y=y;job.Z=z;job.Phase=0;job.Index=0;job.BlockEmission=0;
            job.NeedsSkyRelight=needsSkyRelight;job.NeedsSkyFrontierShell=needsSkyFrontierShell;
            job.DirectColumn=null;job.DirectY=VoxelConstants.MinY;job.ShellPos=0;job.ShellY=0;
            job.SkyCache.Clear();job.Removed.Clear();job.Seeds.Clear();job.DirtySections.Clear();
            return job;
        }

        static void ReleaseDeferredRelightArrays(DeferredRelightJob job)
        {
            for(int i=0;i<job.Rented.Count;i++)ArrayPool<byte>.Shared.Return(job.Rented[i],false);
            job.Rented.Clear();job.SkyCache.Clear();job.DirectColumn=null;
        }

        public static void CancelDeferredRelight(DeferredRelightJob job)
        {
            if(job==null)return;
            ReleaseDeferredRelightArrays(job);
            job.Removed.Clear();job.Seeds.Clear();job.DirtySections.Clear();
            job.Phase=11;job.Index=0;
        }

        static bool RelightDeadline(double deadlineMs,ref int checks,int mask)
        {
            return ((++checks&mask)==0)&&Time.realtimeSinceStartupAsDouble*1000.0>=deadlineMs;
        }

        /// <summary>Returns true only when the exact edit relight transaction is complete.</summary>
        public static bool StepDeferredRelight(Dictionary<ChunkCoord,ChunkColumn> world,DeferredRelightJob job,double deadlineMs)
        {
            if(job==null||job.Done)return true;
            int checks=0;
            while(true)
            {
                if(job.Phase==0)
                {
                    job.Removed.Clear();job.Seeds.Clear();
                    byte start=GetWorldLight(world,job.X,job.Y,job.Z);int oldLevel=start&15;
                    job.BlockEmission=SourceEmissionWorld(world,job.X,job.Y,job.Z);
                    SetWorldLight(world,job.X,job.Y,job.Z,(byte)(start&0xF0),job.DirtySections);
                    job.Removed.Add(new LightState(job.X,job.Y,job.Z,oldLevel));job.Index=0;job.Phase=1;
                }
                if(job.Phase==1)
                {
                    while(job.Index<job.Removed.Count)
                    {
                        LightState p=job.Removed[job.Index++];
                        for(int d=0;d<6;d++)
                        {
                            int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                            if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                            byte light=GetWorldLight(world,nx,ny,nz);int level=light&15;
                            if(level==0)continue;
                            if(level<p.Level)
                            {
                                int own=SourceEmissionWorld(world,nx,ny,nz);
                                if(own>=level)job.Seeds.Add(new LightNode(nx,ny,nz));
                                else
                                {
                                    SetWorldLight(world,nx,ny,nz,(byte)((light&0xF0)|own),job.DirtySections);
                                    job.Removed.Add(new LightState(nx,ny,nz,level));
                                    if(own>1)job.Seeds.Add(new LightNode(nx,ny,nz));
                                }
                            }
                            else job.Seeds.Add(new LightNode(nx,ny,nz));
                        }
                        if(RelightDeadline(deadlineMs,ref checks,31))return false;
                    }
                    if(job.BlockEmission>0)
                    {
                        byte cur=GetWorldLight(world,job.X,job.Y,job.Z);
                        SetWorldLight(world,job.X,job.Y,job.Z,(byte)((cur&0xF0)|job.BlockEmission),job.DirtySections);
                        job.Seeds.Add(new LightNode(job.X,job.Y,job.Z));
                    }
                    for(int d=0;d<6;d++)
                    {
                        int nx=job.X+DX[d],ny=job.Y+DY[d],nz=job.Z+DZ[d];
                        if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                        if((GetWorldLight(world,nx,ny,nz)&15)>1)job.Seeds.Add(new LightNode(nx,ny,nz));
                    }
                    job.Index=0;job.Phase=2;
                }
                if(job.Phase==2)
                {
                    while(job.Index<job.Seeds.Count)
                    {
                        PropagateIncrementalNode(world,job.Seeds,job.Index++,false,job.DirtySections);
                        if(RelightDeadline(deadlineMs,ref checks,31))return false;
                    }
                    job.Removed.Clear();job.Seeds.Clear();job.Index=0;
                    if(!job.NeedsSkyRelight)
                    {
                        job.Phase=11;ReleaseDeferredRelightArrays(job);return true;
                    }
                    job.DirectColumn=DirectSkyColumn(world,job.X,job.Z,job.SkyCache,job.Rented);
                    job.DirectY=VoxelConstants.MinY;job.Phase=3;
                }
                if(job.Phase==3)
                {
                    while(job.DirectY<=VoxelConstants.MaxY)
                    {
                        int wy=job.DirectY++;
                        byte light=GetWorldLight(world,job.X,wy,job.Z);int existing=light>>4,directLevel=job.DirectColumn[wy-VoxelConstants.MinY];
                        if(existing>directLevel)
                        {
                            SetWorldLight(world,job.X,wy,job.Z,(byte)((light&15)|(directLevel<<4)),job.DirtySections);
                            job.Removed.Add(new LightState(job.X,wy,job.Z,existing));
                        }
                        else if(directLevel>existing)
                        {
                            SetWorldLight(world,job.X,wy,job.Z,(byte)((light&15)|(directLevel<<4)),job.DirtySections);
                            job.Seeds.Add(new LightNode(job.X,wy,job.Z));
                        }
                        if(RelightDeadline(deadlineMs,ref checks,63))return false;
                    }
                    BlockId centerBlock=GetWorldBlock(world,job.X,job.Y,job.Z);byte centerMeta=GetWorldMeta(world,job.X,job.Y,job.Z);
                    if(!CanLightPass(centerBlock,centerMeta,0,true)||!CanLightPass(centerBlock,centerMeta,2,true))
                    {
                        int level=GetWorldLight(world,job.X,job.Y,job.Z)>>4;
                        if(level>1)job.Removed.Add(new LightState(job.X,job.Y,job.Z,level));
                    }
                    job.Index=0;job.Phase=4;
                }
                if(job.Phase==4)
                {
                    while(job.Index<job.Removed.Count)
                    {
                        LightState p=job.Removed[job.Index++];
                        for(int d=0;d<6;d++)
                        {
                            int nx=p.X+DX[d],ny=p.Y+DY[d],nz=p.Z+DZ[d];
                            if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                            byte light=GetWorldLight(world,nx,ny,nz);int level=light>>4;
                            if(level==0)continue;
                            if(level<p.Level)
                            {
                                byte[] baseCol=DirectSkyColumn(world,nx,nz,job.SkyCache,job.Rented);
                                int baseline=baseCol[ny-VoxelConstants.MinY];
                                if(level>baseline)
                                {
                                    SetWorldLight(world,nx,ny,nz,(byte)((light&15)|(baseline<<4)),job.DirtySections);
                                    job.Removed.Add(new LightState(nx,ny,nz,level));
                                    if(baseline>1)job.Seeds.Add(new LightNode(nx,ny,nz));
                                }
                                else job.Seeds.Add(new LightNode(nx,ny,nz));
                            }
                            else job.Seeds.Add(new LightNode(nx,ny,nz));
                        }
                        if(RelightDeadline(deadlineMs,ref checks,31))return false;
                    }
                    for(int d=0;d<6;d++)
                    {
                        int nx=job.X+DX[d],ny=job.Y+DY[d],nz=job.Z+DZ[d];
                        if(ny<VoxelConstants.MinY||ny>VoxelConstants.MaxY||!world.ContainsKey(ChunkCoord.FromWorld(nx,nz)))continue;
                        if((GetWorldLight(world,nx,ny,nz)>>4)>1)job.Seeds.Add(new LightNode(nx,ny,nz));
                    }
                    BlockId centerBlock=GetWorldBlock(world,job.X,job.Y,job.Z);byte centerMeta=GetWorldMeta(world,job.X,job.Y,job.Z);
                    if(job.NeedsSkyFrontierShell&&(CanLightPass(centerBlock,centerMeta,0,true)||CanLightPass(centerBlock,centerMeta,2,true)))
                    {
                        job.ShellMinY=Math.Max(VoxelConstants.MinY,job.Y-16);job.ShellMaxY=Math.Min(VoxelConstants.MaxY,job.Y+16);
                        job.ShellPos=0;job.ShellY=job.ShellMinY;job.Phase=5;
                    }
                    else{job.Index=0;job.Phase=7;}
                }
                if(job.Phase==5)
                {
                    const int r=16,size=33;
                    while(job.ShellPos<size*size)
                    {
                        int dx=job.ShellPos/size-r,dz=job.ShellPos%size-r;
                        if(Math.Abs(dx)!=r&&Math.Abs(dz)!=r){job.ShellPos++;job.ShellY=job.ShellMinY;continue;}
                        int wx=job.X+dx,wz=job.Z+dz;
                        if(!world.ContainsKey(ChunkCoord.FromWorld(wx,wz))){job.ShellPos++;job.ShellY=job.ShellMinY;continue;}
                        while(job.ShellY<=job.ShellMaxY)
                        {
                            int wy=job.ShellY++;
                            if((GetWorldLight(world,wx,wy,wz)>>4)>1)job.Seeds.Add(new LightNode(wx,wy,wz));
                            if(RelightDeadline(deadlineMs,ref checks,63))return false;
                        }
                        job.ShellPos++;job.ShellY=job.ShellMinY;
                    }
                    job.ShellPos=0;job.Phase=6;
                }
                if(job.Phase==6)
                {
                    const int r=16,size=31;
                    while(job.ShellPos<size*size)
                    {
                        int dx=job.ShellPos/size-(r-1),dz=job.ShellPos%size-(r-1);job.ShellPos++;
                        int wx=job.X+dx,wz=job.Z+dz;
                        if(world.ContainsKey(ChunkCoord.FromWorld(wx,wz)))
                        {
                            if((GetWorldLight(world,wx,job.ShellMinY,wz)>>4)>1)job.Seeds.Add(new LightNode(wx,job.ShellMinY,wz));
                            if(job.ShellMaxY!=job.ShellMinY&&(GetWorldLight(world,wx,job.ShellMaxY,wz)>>4)>1)job.Seeds.Add(new LightNode(wx,job.ShellMaxY,wz));
                        }
                        if(RelightDeadline(deadlineMs,ref checks,31))return false;
                    }
                    job.Index=0;job.Phase=7;
                }
                if(job.Phase==7)
                {
                    while(job.Index<job.Seeds.Count)
                    {
                        PropagateIncrementalNode(world,job.Seeds,job.Index++,true,job.DirtySections);
                        if(RelightDeadline(deadlineMs,ref checks,31))return false;
                    }
                    job.Phase=11;ReleaseDeferredRelightArrays(job);return true;
                }
                if(job.Phase>=11)return true;
            }
        }

        /// <summary>
        /// main.js aD()/rD()/nD(): incremental light update for one edited block. Unlike the old
        /// Unity port this does not rebake nine complete 16x384x16 chunks for every mouse click.
        /// Player edits call this before their immediate mesh rebuild; simulation edits use the
        /// frame-budgeted queue in VoxelWorld.
        /// </summary>
        public static void RelightEditIncremental(Dictionary<ChunkCoord,ChunkColumn> world,int x,int y,int z,HashSet<SectionKey> dirtySections)
        {
            var skyCache=editSkyCacheScratch??(editSkyCacheScratch=new Dictionary<long,byte[]>(32));
            var rented=editRentedScratch??(editRentedScratch=new List<byte[]>(32));
            var removed=editRemovedScratch??(editRemovedScratch=new List<LightState>(128));
            var seeds=editSeedsScratch??(editSeedsScratch=new List<LightNode>(128));
            skyCache.Clear();rented.Clear();removed.Clear();seeds.Clear();
            if(dirtySections!=null)dirtySections.Clear();
            try
            {
                RelightBlockIncremental(world,x,y,z,dirtySections,removed,seeds);
                RelightSkyIncremental(world,x,y,z,dirtySections,skyCache,rented,removed,seeds);
            }
            finally
            {
                for(int i=0;i<rented.Count;i++)ArrayPool<byte>.Shared.Return(rented[i],false);
                rented.Clear();skyCache.Clear();
            }
        }

        /// <summary>
        /// R54 transaction path: apply a set of already-committed block edits through one shared
        /// incremental-light context. Direct-sky columns are computed once per affected X/Z column,
        /// scratch buffers are reused, and the mesh dirty set is unioned across the entire mutation batch.
        /// </summary>
        public static void RelightEditsIncremental(Dictionary<ChunkCoord,ChunkColumn> world,IList<Vector3Int> edits,HashSet<SectionKey> dirtySections)
        {
            if(edits==null||edits.Count==0){if(dirtySections!=null)dirtySections.Clear();return;}
            var skyCache=editSkyCacheScratch??(editSkyCacheScratch=new Dictionary<long,byte[]>(64));
            var rented=editRentedScratch??(editRentedScratch=new List<byte[]>(64));
            var removed=editRemovedScratch??(editRemovedScratch=new List<LightState>(256));
            var seeds=editSeedsScratch??(editSeedsScratch=new List<LightNode>(256));
            skyCache.Clear();rented.Clear();removed.Clear();seeds.Clear();
            if(dirtySections!=null)dirtySections.Clear();
            try
            {
                for(int i=0;i<edits.Count;i++)
                {
                    Vector3Int e=edits[i];
                    RelightBlockIncremental(world,e.x,e.y,e.z,dirtySections,removed,seeds);
                    RelightSkyIncremental(world,e.x,e.y,e.z,dirtySections,skyCache,rented,removed,seeds);
                }
            }
            finally
            {
                for(int i=0;i<rented.Count;i++)ArrayPool<byte>.Shared.Return(rented[i],false);
                rented.Clear();skyCache.Clear();
            }
        }

        // Compatibility helpers for older call sites; interactive edits use RelightEditIncremental().
        public static void RelightBlockLight(ChunkColumn c) { BakeColumn(c); }
        public static void RelightVerticalColumn(ChunkColumn c,int x,int z) { BakeColumn(c); }
    }
}
