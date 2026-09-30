using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BlockcraftPort
{
    /// <summary>
    /// Direct deepslate V2() world-generation path from genWorker. Terrain, surfaces, vegetation,
    /// caves, aquifers, ores, cave biomes and the structure/mineshaft passes follow the source
    /// stage order and constants, including the source shipwreck pass.
    /// </summary>
    public sealed class ChunkGenerator
    {
        const int S=16, H=384, MinY=-64, MaxY=319, Sea=63;
        readonly int seed; readonly MainTerrainNoise n; public readonly BiomeGenerator Biomes; readonly MainStructureGenerator structures;
        readonly ConcurrentDictionary<long,Tunnel> tunnelCache=new ConcurrentDictionary<long,Tunnel>();
        enum TreeKind:byte{None,Cactus,Oak,Birch,FancyOak,DarkOak,Jungle,Spruce,TallSpruce,Acacia,Cherry,GiantRedMushroom,GiantBrownMushroom,IceSpike}
        enum CaveBiome:byte{None,Lush,Dripstone,DeepDark}
        sealed class Tunnel{public readonly List<Node> Nodes=new List<Node>();public int Depth;public float WaterFrac;public double MinX=double.PositiveInfinity,MaxX=double.NegativeInfinity,MinZ=double.PositiveInfinity,MaxZ=double.NegativeInfinity;public bool Exists=true;}
        [ThreadStatic] static List<Tunnel> nearbyScratch;
        [ThreadStatic] static Stack<int> floodStackScratch;
        [ThreadStatic] static short[] aquiferTopsScratch;
        static readonly Tunnel NoTunnel=new Tunnel{Exists=false};
        struct Node{public double X,Z,R;public Node(double x,double z,double r){X=x;Z=z;R=r;}}
        struct Ore{public BlockId O,D;public int Min,Peak,Max,Attempts,MinSize,MaxSize,Seed;public bool Mountain;public Ore(BlockId o,BlockId d,int min,int peak,int max,int attempts,int minS,int maxS,int sd,bool m){O=o;D=d;Min=min;Peak=peak;Max=max;Attempts=attempts;MinSize=minS;MaxSize=maxS;Seed=sd;Mountain=m;}}
        static readonly Ore[] Ores={
            new Ore(BlockId.CoalOre,BlockId.DeepslateCoalOre,0,48,132,16,8,16,0,false),new Ore(BlockId.CopperOre,BlockId.DeepslateCopperOre,-16,48,112,18,5,10,1,false),
            new Ore(BlockId.IronOre,BlockId.DeepslateIronOre,-24,16,80,10,4,9,2,false),new Ore(BlockId.IronOre,BlockId.DeepslateIronOre,-64,8,72,8,2,4,12,false),
            new Ore(BlockId.LapisOre,BlockId.DeepslateLapisOre,-32,4,40,2,3,6,3,false),new Ore(BlockId.GoldOre,BlockId.DeepslateGoldOre,-64,-16,32,2,3,7,4,false),
            new Ore(BlockId.RedstoneOre,BlockId.DeepslateRedstoneOre,-64,-55,16,5,4,8,5,false),new Ore(BlockId.DiamondOre,BlockId.DeepslateDiamondOre,-64,-55,16,1,3,5,6,false),
            new Ore(BlockId.EmeraldOre,BlockId.DeepslateEmeraldOre,60,110,200,3,1,1,7,true)};
        static readonly BlockId[] BadlandsBands={BlockId.OrangeTerracotta,BlockId.Terracotta,BlockId.OrangeTerracotta,BlockId.RedSand,BlockId.WhiteTerracotta,BlockId.Terracotta,BlockId.OrangeTerracotta,BlockId.Terracotta};
        static readonly BlockId[] CoralFans={BlockId.TubeCoralFan,BlockId.BrainCoralFan,BlockId.BubbleCoralFan,BlockId.FireCoralFan,BlockId.HornCoralFan};
        static readonly BlockId[] CoralBlocks={BlockId.TubeCoralBlock,BlockId.BrainCoralBlock,BlockId.BubbleCoralBlock,BlockId.FireCoralBlock,BlockId.HornCoralBlock};
        static readonly BlockId[] PlainsFlowers={BlockId.Dandelion,BlockId.Poppy,BlockId.Cornflower,BlockId.OxeyeDaisy,BlockId.AzureBluet};
        static readonly BlockId[] MeadowFlowers={BlockId.Dandelion,BlockId.Cornflower,BlockId.OxeyeDaisy,BlockId.Poppy};
        static readonly BlockId[] FlowerForestFlowers={BlockId.Dandelion,BlockId.Poppy,BlockId.Allium,BlockId.Cornflower,BlockId.OxeyeDaisy,BlockId.AzureBluet,BlockId.LilyOfTheValley,BlockId.OrangeTulip,BlockId.PinkTulip,BlockId.RedTulip,BlockId.WhiteTulip};
        static readonly BlockId[] CherryFlowers={BlockId.PinkTulip,BlockId.Allium,BlockId.OxeyeDaisy,BlockId.Dandelion,BlockId.PinkTulip};
        static readonly int[] TreeDirX={1,-1,0,0},TreeDirZ={0,0,1,-1};
        static readonly int[] FancyOakRadii={2,3,3,2,1};
        static readonly int[] CherryRadii={2,3,2};

        public ChunkGenerator(int seed){this.seed=seed;n=new MainTerrainNoise(seed);Biomes=new BiomeGenerator(seed);structures=new MainStructureGenerator(seed,Biomes);}
        static int Idx(int x,int y,int z)=>((x<<4)+z)*H+(y-MinY);
        static int Col(int x,int z)=>((x<<4)+z)*H;
        static bool SurfaceReplace(BlockId b)=>b==BlockId.Stone||b==BlockId.Granite||b==BlockId.Diorite||b==BlockId.Andesite;
        static bool CaveReplace(BlockId b)=>b==BlockId.Stone||b==BlockId.Dirt||b==BlockId.Grass||b==BlockId.Sand||b==BlockId.RedSand||b==BlockId.Sandstone||b==BlockId.Gravel||b==BlockId.Snow||b==BlockId.Podzol||b==BlockId.Terracotta||b==BlockId.OrangeTerracotta||b==BlockId.WhiteTerracotta||b==BlockId.Mycelium||b==BlockId.Mud||b==BlockId.PackedIce;
        static bool CaveRock(BlockId b)=>b==BlockId.Stone||b==BlockId.Deepslate||b==BlockId.Tuff||b==BlockId.Granite||b==BlockId.Diorite||b==BlockId.Andesite||b==BlockId.Dirt||b==BlockId.Gravel;
        static bool OreReplace(BlockId b)=>b==BlockId.Stone||b==BlockId.Granite||b==BlockId.Diorite||b==BlockId.Andesite||b==BlockId.Deepslate||b==BlockId.Tuff;
        static bool Mountain(BiomeId b)=>b==BiomeId.Mountain||b==BiomeId.Peaks||b==BiomeId.Meadow||b==BiomeId.Grove||b==BiomeId.SnowySlopes||b==BiomeId.JaggedPeaks||b==BiomeId.StonyPeaks||b==BiomeId.Windswept;
        float G(int x,int z)=>n.Hash(x,z); float G3(int x,int y,int z)=>n.Hash3(x,y,z); float DA(float x,float z)=>n.ClassicValue(x,z); float EA(float x,float y,float z)=>n.Value3(x,y,z); static float Signed(float v)=>v*2f-1f;

#if PARITY_HARNESS
        // Offline profiling only: per-stage wall time of GenerateSource (single-threaded harness use).
        public static readonly double[] StageMs=new double[14];
        public static readonly string[] StageNames={"biome columns","density lattice","interpolate","surface+plants","trees","caves/rock","aquifers","sea flood","ores","cave deco","structures","mineshafts","to unity column","light bake"};
        [ThreadStatic] static System.Diagnostics.Stopwatch stageSw;[ThreadStatic] static double stageLast;
        static void StageStart(){if(stageSw==null)stageSw=System.Diagnostics.Stopwatch.StartNew();stageLast=stageSw.Elapsed.TotalMilliseconds;}
        static void StageMark(int i){double t=stageSw.Elapsed.TotalMilliseconds;StageMs[i]+=t-stageLast;stageLast=t;}
#endif
        public ChunkColumn Generate(ChunkCoord unityCoord)
        {
            // Generate the exact source chunk, then reflect its Z cells into Unity.  The -1 in the
            // chunk conversion is required because blocks are unit intervals, not points.
            ChunkCoord sourceCoord = SourceCoords.UnityChunkToSource(unityCoord);
            return GenerateSource(sourceCoord,unityCoord);
        }

        ChunkColumn GenerateSource(ChunkCoord cc,ChunkCoord unityCoord)
        {
            int denseCount=S*S*H;
            ushort[] blocks=ArrayPool<ushort>.Shared.Rent(denseCount);
            byte[] meta=ArrayPool<byte>.Shared.Rent(denseCount);
            Array.Clear(blocks,0,denseCount);Array.Clear(meta,0,denseCount);
            // V2 density lattice: 5 x 5 x 49, then 4 x 8 x 4 interpolation.
            const int stepXZ=4,stepY=8; int gy=H/stepY+1,gxz=S/stepXZ+1,densityCount=gxz*gxz*gy;
            double[] density=ArrayPool<double>.Shared.Rent(densityCount);
            BiomeSample[] columnBiomes=ArrayPool<BiomeSample>.Shared.Rent(S*S);
            TreeKind[] columnTrees=ArrayPool<TreeKind>.Shared.Rent(S*S);
            byte[] columnAquifers=ArrayPool<byte>.Shared.Rent(S*S);
            int wx0=cc.X*S,wz0=cc.Z*S;
            try
            {
#if PARITY_HARNESS
            StageStart();
#endif
            // Cache the 16x16 source-column biome samples once for every stage that uses the same
            // chunk columns (surface, sea freezing and exported biome/height arrays).
            for(int lx=0;lx<S;lx++)for(int lz=0;lz<S;lz++)
            {
                int ci=lx*S+lz,wx=wx0+lx,wz=wz0+lz;
                BiomeSample bs=Biomes.Sample(wx,wz);columnBiomes[ci]=bs;
                bool aq=IsAquiferXZ(wx,wz);columnAquifers[ci]=(byte)(aq?1:0);
                columnTrees[ci]=aq?TreeKind.None:TreeAt(wx,wz,bs,false);
            }
#if PARITY_HARNESS
            StageMark(0);
#endif
            for(int gx=0;gx<gxz;gx++)for(int gz=0;gz<gxz;gz++){int wx=wx0+gx*stepXZ,wz=wz0+gz*stepXZ,o=(gx*gxz+gz)*gy;var columnBiome=gx<gxz-1&&gz<gxz-1?columnBiomes[(gx*stepXZ)*S+gz*stepXZ]:Biomes.Sample(wx,wz);for(int yy=0;yy<gy;yy++)density[o+yy]=Biomes.TerrainDensity(columnBiome,wx,MinY+yy*stepY,wz);}
#if PARITY_HARNESS
            StageMark(1);
#endif
            for(int cx=0;cx<S/stepXZ;cx++)for(int cz=0;cz<S/stepXZ;cz++)for(int cy=0;cy<H/stepY;cy++)
            {
                int q=(cx*gxz+cz)*gy+cy;double a=density[q],b=density[q+1],c=density[q+gy],d=density[q+gy+1],e=density[q+gxz*gy],f=density[q+gxz*gy+1],g=density[q+gxz*gy+gy],h=density[q+gxz*gy+gy+1];int baseY=MinY+cy*stepY;
                for(int dx=0;dx<stepXZ;dx++){double tx=dx/(double)stepXZ;double a0=a+(e-a)*tx,b0=b+(f-b)*tx,c0=c+(g-c)*tx,d0=d+(h-d)*tx;for(int dz=0;dz<stepXZ;dz++){double tz=dz/(double)stepXZ;double lo=a0+(c0-a0)*tz,hi=b0+(d0-b0)*tz;int lx=cx*stepXZ+dx,lz=cz*stepXZ+dz;for(int dy=0;dy<stepY;dy++){int y=baseY+dy;double den=lo+(hi-lo)*(dy/(double)stepY);blocks[Idx(lx,y,lz)]=(ushort)(den>0?BlockId.Stone:y<=Sea?BlockId.Water:BlockId.Air);}}}
            }
#if PARITY_HARNESS
            StageMark(2);
#endif
            // Surface/subsurface + water/land decoration.
            for(int lx=0;lx<S;lx++)for(int lz=0;lz<S;lz++)
            {
                int wx=wx0+lx,wz=wz0+lz,baseIdx=Col(lx,lz); blocks[Idx(lx,MinY,lz)]=(ushort)BlockId.Bedrock; var bs=columnBiomes[lx*S+lz];int top=MinY;
                for(int y=MaxY;y>MinY;y--){var id=(BlockId)blocks[Idx(lx,y,lz)];if(id!=BlockId.Air&&id!=BlockId.Water){top=y;break;}}
                if(top>MinY&&SurfaceReplace((BlockId)blocks[Idx(lx,top,lz)]))
                {
                    BlockId surf=Surface(bs.Biome,wx,wz,top,bs.Height);blocks[Idx(lx,top,lz)]=(ushort)surf;
                    for(int dep=1;dep<=3;dep++){int y=top-dep;if(y<=MinY||!SurfaceReplace((BlockId)blocks[Idx(lx,y,lz)]))break;blocks[Idx(lx,y,lz)]=(ushort)SubSurface(bs.Biome,surf,y);}
                }
                if(top>MinY&&top<Sea)Underwater(blocks,meta,baseIdx,wx,wz,top,bs.Biome,bs.TemperatureBucket);
                else if(top>=Sea&&top+1<=MaxY&&(BlockId)blocks[Idx(lx,top+1,lz)]==BlockId.Air)
                {
                    BlockId ground=(BlockId)blocks[Idx(lx,top,lz)]; bool jungle=bs.Biome==BiomeId.Jungle||bs.Biome==BiomeId.SparseJungle||bs.Biome==BiomeId.BambooJungle;
                    if(ground==BlockId.Grass&&G(wx*17+101,wz*17-59)<.00013f)blocks[Idx(lx,top+1,lz)]=(ushort)BlockId.Pumpkin;
                    else if(ground==BlockId.Grass&&jungle&&G(wx*19-71,wz*19+43)<.0016f)blocks[Idx(lx,top+1,lz)]=(ushort)BlockId.Melon;
                    else PlacePlantDense(blocks,meta,wx0,wz0,wx,wz,top,bs,columnAquifers[lx*S+lz]!=0,columnTrees[lx*S+lz]);
                }
            }
#if PARITY_HARNESS
            StageMark(3);
#endif
            // Trees sample beyond chunk borders, exactly C2's source range. genWorker T2() suppresses
            // roots inside future structure clear-zones; precompute those zones once per chunk.
            var treeExclusions=structures.BuildTreeExclusions(wx0,wz0,5);
            for(int wx=wx0-5;wx<wx0+S+5;wx++)for(int wz=wz0-5;wz<wz0+S+5;wz++)
            {
                int lx=wx-wx0,lz=wz-wz0;
                TreeKind kind=(uint)lx<S&&(uint)lz<S?columnTrees[lx*S+lz]:TreeAt(wx,wz);
                if(kind!=TreeKind.None&&!MainStructureGenerator.DecorationBlocked(treeExclusions,wx,wz))
                    PlaceTreeDense(blocks,wx0,wz0,wx,wz,kind);
            }
#if PARITY_HARNESS
            StageMark(4);
#endif
            // Cave/rock pass. Noise-cell caches are per thread; reset them so a different seed/world
            // on the same worker thread can never reuse corner values.
            System.Array.Clear(CaveCells,0,CaveCells.Length);
            for(int lx=0;lx<S;lx++)for(int lz=0;lz<S;lz++)
            {
                int wx=wx0+lx,wz=wz0+lz,top=MinY;for(int y=MaxY;y>MinY;y--)if(CaveReplace((BlockId)blocks[Idx(lx,y,lz)])){top=y;break;}
                for(int y=MinY;y<=top;y++){int ii=Idx(lx,y,lz);BlockId old=(BlockId)blocks[ii];if(!CaveReplace(old))continue;if(BedrockNoise(wx,y,wz)){blocks[ii]=(ushort)BlockId.Bedrock;continue;}int cave=Cave(wx,y,wz,top);if(cave==1){blocks[ii]=0;continue;}if(cave==2||cave==3){BlockId fluid=cave==2?BlockId.Water:BlockId.Lava;blocks[ii]=(ushort)fluid;for(int q=ii-1;q>Col(lx,lz)&&blocks[q]==0;q--)blocks[q]=(ushort)fluid;continue;}if(old==BlockId.Stone)blocks[ii]=(ushort)RockVariant(wx,y,wz);}
            }
#if PARITY_HARNESS
            StageMark(5);
#endif
            CarveAquifers(blocks,wx0,wz0,cc.X,cc.Z);
#if PARITY_HARNESS
            StageMark(6);
#endif
            SeaFloodAndFreeze(blocks,columnBiomes);
#if PARITY_HARNESS
            StageMark(7);
#endif
            GenerateOres(blocks,wx0,wz0,cc.X,cc.Z);
#if PARITY_HARNESS
            StageMark(8);
#endif
            DecorateCaves(blocks,meta,wx0,wz0);
#if PARITY_HARNESS
            StageMark(9);
#endif
            // genWorker D2() then i4(): surface structures (including shipwrecks) followed by deepslate mineshafts.
            structures.ApplySurfaceStructures(blocks,meta,wx0,wz0,cc.X,cc.Z);
#if PARITY_HARNESS
            StageMark(10);
#endif
            structures.ApplyMineshafts(blocks,wx0,wz0,cc.X,cc.Z);

#if PARITY_HARNESS
            StageMark(11);
#endif
            // Write the generated source chunk straight into the reflected Unity column. The previous
            // port first materialized a complete source ChunkColumn and immediately copied it, doubling
            // transient section allocations for every streamed chunk. This is byte-for-byte equivalent.
            var dst=new ChunkColumn(unityCoord);
            for(int lx=0;lx<S;lx++)for(int uz=0;uz<S;uz++)
            {
                int sz=15-uz;var bs=columnBiomes[lx*S+sz];int di=lx*16+uz;dst.Biomes[di]=(byte)bs.Biome;dst.Heights[di]=(short)bs.Height;
            }
            for(int sy=0;sy<VoxelConstants.SectionCount;sy++)
            {
                int y0=MinY+sy*16;bool any=false;
                for(int lx=0;lx<16&&!any;lx++)for(int sz=0;sz<16&&!any;sz++)for(int ly=0;ly<16;ly++){int si=Idx(lx,y0+ly,sz);if(blocks[si]!=0||meta[si]!=0){any=true;break;}}
                if(!any)continue;var d=dst.EnsureSection(sy);
                for(int lx=0;lx<16;lx++)for(int uz=0;uz<16;uz++){int sz=15-uz;for(int ly=0;ly<16;ly++){int si=Idx(lx,y0+ly,sz),di=((lx*16)+uz)*16+ly;ushort b=blocks[si];d.Blocks[di]=b;byte sm=meta[si];d.Meta[di]=sm!=0||SourceCoords.HasDirectionalMeta((BlockId)b)?SourceCoords.SourceMetaToUnity((BlockId)b,sm):(byte)0;if(b!=0){d.NonAir++;if(BlockRegistry.IsFluid((BlockId)b))d.Fluid++;}}}
            }
#if PARITY_HARNESS
            StageMark(12);
#endif
            VoxelLighting.BakeColumn(dst);dst.Revision=0;
#if PARITY_HARNESS
            StageMark(13);
#endif
            return dst;
            }
            finally
            {
                ArrayPool<ushort>.Shared.Return(blocks,false);ArrayPool<byte>.Shared.Return(meta,false);ArrayPool<double>.Shared.Return(density,false);ArrayPool<BiomeSample>.Shared.Return(columnBiomes,false);ArrayPool<TreeKind>.Shared.Return(columnTrees,false);ArrayPool<byte>.Shared.Return(columnAquifers,false);
            }
        }

        BlockId Surface(BiomeId b,int x,int z,int top,int biomeHeight)
        {
            BlockId r;switch(b){case BiomeId.Desert:case BiomeId.Beach:r=BlockId.Sand;break;case BiomeId.SnowyBeach:r=BlockId.Snow;break;case BiomeId.Badlands:r=BlockId.RedSand;break;case BiomeId.WoodedBadlands:r=biomeHeight>=Sea+10?BlockId.Grass:BlockId.RedSand;break;case BiomeId.MushroomFields:r=BlockId.Mycelium;break;case BiomeId.MangroveSwamp:r=BlockId.Mud;break;case BiomeId.StonyShore:r=BlockId.Stone;break;case BiomeId.WarmOcean:case BiomeId.LukewarmOcean:r=BlockId.Sand;break;case BiomeId.ColdOcean:case BiomeId.FrozenOcean:r=BlockId.Gravel;break;case BiomeId.FrozenRiver:r=BlockId.Snow;break;case BiomeId.Ocean:case BiomeId.River:{int d=Sea-top;if(d<1){r=BlockId.Grass;break;}if(DA(x*.11f+311.2f,z*.11f-177.6f)>.7f){r=BlockId.Clay;break;}if(d>=3){float v=DA(x*.05f+777.7f,z*.05f-333.3f);r=v>.66f?BlockId.Gravel:v<.3f?BlockId.Dirt:BlockId.Sand;}else r=BlockId.Sand;break;}case BiomeId.Snowy:case BiomeId.Peaks:case BiomeId.SnowyTaiga:case BiomeId.SnowySlopes:case BiomeId.JaggedPeaks:case BiomeId.Grove:case BiomeId.IceSpikes:r=BlockId.Snow;break;case BiomeId.FrozenPeaks:r=DA(x*.1f+3,z*.1f-3)<.46f?BlockId.PackedIce:BlockId.Snow;break;case BiomeId.StonyPeaks:r=BlockId.Stone;break;case BiomeId.Windswept:{float q=DA(x*.11f+21,z*.11f-21)+(top-Sea)*.004f;r=q<.55f?BlockId.Grass:q<.68f?BlockId.Stone:BlockId.Gravel;break;}case BiomeId.OldGrowthTaiga:r=DA(x*.09f-13,z*.09f+13)<.58f?BlockId.Podzol:BlockId.Grass;break;case BiomeId.BambooJungle:r=DA(x*.12f-3,z*.12f+7)<.4f?BlockId.Podzol:BlockId.Grass;break;default:r=BlockId.Grass;break;}
            if(top<Sea&&(r==BlockId.Grass||r==BlockId.Podzol||r==BlockId.Mycelium)){int d=Sea-top;if(DA(x*.11f+311.2f,z*.11f-177.6f)>.7f)r=BlockId.Clay;else if(d>=3){float v=DA(x*.05f+777.7f,z*.05f-333.3f);r=v>.66f?BlockId.Gravel:v<.3f?BlockId.Dirt:BlockId.Sand;}else r=BlockId.Sand;}return r;
        }
        BlockId SubSurface(BiomeId b,BlockId surf,int y){if(b==BiomeId.Badlands)return BadlandsBand(y);if(b==BiomeId.WoodedBadlands)return surf==BlockId.Grass?BlockId.Dirt:BadlandsBand(y);if(b==BiomeId.Desert)return BlockId.Sandstone;if(b==BiomeId.MangroveSwamp)return BlockId.Mud;if(b==BiomeId.Beach||b==BiomeId.SnowyBeach||b==BiomeId.Ocean||b==BiomeId.River||b==BiomeId.WarmOcean||b==BiomeId.LukewarmOcean)return BlockId.Sand;if(b==BiomeId.StonyPeaks||b==BiomeId.JaggedPeaks||b==BiomeId.SnowySlopes||b==BiomeId.FrozenPeaks||b==BiomeId.StonyShore||b==BiomeId.ColdOcean||b==BiomeId.FrozenOcean||b==BiomeId.FrozenRiver)return BlockId.Stone;return BlockId.Dirt;}
        BlockId BadlandsBand(int y)=>y<57?BlockId.Stone:BadlandsBands[(y+(int)Math.Floor(G(0,y>>3)*3))%BadlandsBands.Length];

        void Underwater(ushort[] a,byte[] m,int col,int x,int z,int top,BiomeId b,byte temp)
        {
            int next=col+top+1-MinY,sea=col+Sea-MinY,depth=Sea-top;float r=G(x*5+41,z*5-23),q=G(x*9-17,z*9+31);
            if(b==BiomeId.FrozenOcean){float v=DA(x*.02f+911.1f,z*.02f-333.3f);if(v>.72f&&depth>=3){float f=Math.Min(1,(v-.72f)/.28f);int hi=Sea+SourceMath.JsRound(f*6),lo=Sea-1-SourceMath.JsRound(f*5);if(lo<top+1)lo=top+1;for(int y=lo;y<=hi&&y<MaxY;y++)a[col+y-MinY]=(ushort)BlockId.PackedIce;if(hi>Sea+2&&a[col+hi+1-MinY]==0)a[col+hi+1-MinY]=(ushort)BlockId.Snow;}else if((BlockId)a[sea]==BlockId.Water)a[sea]=(ushort)BlockId.Ice;return;}
            if(b==BiomeId.FrozenRiver){if((BlockId)a[sea]==BlockId.Water)a[sea]=(ushort)BlockId.Ice;return;}
            if(b==BiomeId.WarmOcean){if((BlockId)a[next]!=BlockId.Water)return;float v=DA(x*.03f+123.4f,z*.03f-567.8f);if(depth>=4&&v>.72f){int len=1+(int)Math.Floor((v-.72f)*14);if(len>3)len=3;int k=0;for(;k<len&&(BlockId)a[next+k]==BlockId.Water&&top+2+k<Sea;k++)a[next+k]=(ushort)CoralBlocks[(int)Math.Floor(G(x*3+k*7,z*3-k*5)*CoralBlocks.Length)%CoralBlocks.Length];if((BlockId)a[next+k]==BlockId.Water){if(q<.4f)a[next+k]=(ushort)CoralFans[(int)Math.Floor(q*12)%CoralFans.Length];else if(q<.55f){a[next+k]=(ushort)BlockId.SeaPickle;m[next+k]=(byte)((int)Math.Floor(r*4)&3);}}return;}if(r<.06f){a[next]=(ushort)BlockId.SeaPickle;m[next]=(byte)((int)Math.Floor(q*4)&3);}else if(r<.17f)a[next]=(ushort)CoralFans[(int)Math.Floor(q*CoralFans.Length)%CoralFans.Length];else if(r<.34f)a[next]=(ushort)BlockId.Seagrass;return;}
            if(b==BiomeId.ColdOcean){if((BlockId)a[next]!=BlockId.Water||depth>=3&&Kelp(a,col,x,z,top))return;if(r<.14f)a[next]=(ushort)BlockId.Seagrass;return;}
            if(b==BiomeId.Ocean||b==BiomeId.LukewarmOcean){if((BlockId)a[next]!=BlockId.Water||depth>=3&&Kelp(a,col,x,z,top))return;if(r<(b==BiomeId.LukewarmOcean?.3f:.24f))a[next]=(ushort)BlockId.Seagrass;return;}
            if(b==BiomeId.River){if((BlockId)a[next]==BlockId.Water&&r<.12f)a[next]=(ushort)BlockId.Seagrass;return;}
            if(b==BiomeId.Swamp||b==BiomeId.MangroveSwamp){if((BlockId)a[next]==BlockId.Water&&r<.1f)a[next]=(ushort)BlockId.Seagrass;int aboveSea=col+Sea+1-MinY;if(Sea+1<=MaxY&&(BlockId)a[sea]==BlockId.Water&&a[aboveSea]==0&&G(x*7-9,z*7+19)<.05f)a[aboveSea]=(ushort)BlockId.LilyPad;}
        }
        bool Kelp(ushort[] a,int col,int x,int z,int top){float f=DA(x*.025f+313.7f,z*.025f-191.3f);if(f<.6f)return false;float b=(f-.6f)/.4f,t=G(x*5+41,z*5-23);if(t>.3f+b*.55f)return false;int max=Sea-2-top;if(max>25)max=25;if(max<1)return false;int len=1+(int)Math.Floor(G(x*9-3,z*9+7)*max),end=Math.Min(Sea-1,top+len);for(int y=top+1;y<=end;y++)a[col+y-MinY]=(ushort)BlockId.Kelp;return true;}

        void PlacePlantDense(ushort[] a,byte[] meta,int wx0,int wz0,int x,int z,int top,BiomeSample sample,bool aquifer,TreeKind tree)
        {
            BlockId p=PlantAt(x,z,sample,aquifer,tree);if(p==BlockId.Air)return;int lx=x-wx0,lz=z-wz0;if((uint)lx>=16||(uint)lz>=16)return;int ground=Idx(lx,top,lz),dst=Idx(lx,top+1,lz);BlockId g=(BlockId)a[ground];bool mush=p==BlockId.BrownMushroom||p==BlockId.RedMushroom;bool ok=g==BlockId.Grass||g==BlockId.Podzol||(g==BlockId.Sand||g==BlockId.RedSand)&&p==BlockId.DeadBush||mush&&(g==BlockId.Mycelium||g==BlockId.Podzol||g==BlockId.Mud)||(p==BlockId.TallGrass||p==BlockId.Fern)&&g==BlockId.Mud||p==BlockId.BambooPlant&&(g==BlockId.Grass||g==BlockId.Podzol||g==BlockId.Dirt)||p==BlockId.SugarCane&&(g==BlockId.Sand||g==BlockId.RedSand);if(!ok)return;a[dst]=(ushort)p;
            if(IsTallPlant(p)&&top+2<=MaxY&&a[Idx(lx,top+2,lz)]==0){a[Idx(lx,top+2,lz)]=(ushort)p;meta[Idx(lx,top+2,lz)]=4;}
            else if(p==BlockId.SugarCane){int h=2+(G(x*13+5,z*13-5)<.4f?1:0);for(int i=1;i<h&&top+1+i<=MaxY&&a[Idx(lx,top+1+i,lz)]==0;i++)a[Idx(lx,top+1+i,lz)]=(ushort)p;}
            else if(p==BlockId.BambooPlant){int h=3+(int)Math.Floor(G(x*11-4,z*11+6)*4);for(int i=1;i<h&&top+1+i<=MaxY&&a[Idx(lx,top+1+i,lz)]==0;i++)a[Idx(lx,top+1+i,lz)]=(ushort)p;}
        }
        static bool IsTallPlant(BlockId p)=>p==BlockId.Sunflower||p==BlockId.Lilac||p==BlockId.RoseBush||p==BlockId.Peony||p==BlockId.LargeFern;
        BlockId PlantAt(int x,int z,BiomeSample s,bool aquifer,TreeKind tree)
        {
            // $4() rejects aquifers and tree roots. Both are cached once per interior chunk column;
            // neighbour samples remain exact for the rare sugar-cane shoreline test.
if(s.Height<=Sea||s.Height>=MaxY-3||aquifer||tree!=TreeKind.None)return BlockId.Air;float r=G(x*7+3,z*7-5),q=G(x*11-7,z*11+13);BlockId Pick(BlockId[] list)=>list[(int)Math.Floor(q*list.Length)%list.Length];
            if(s.Height<=Sea+2&&r<.32f&&s.Biome!=BiomeId.Desert&&s.Biome!=BiomeId.Badlands&&(Biomes.Sample(x+1,z).Height<=Sea||Biomes.Sample(x-1,z).Height<=Sea||Biomes.Sample(x,z+1).Height<=Sea||Biomes.Sample(x,z-1).Height<=Sea))return BlockId.SugarCane;
            switch(s.Biome){case BiomeId.Plains:if(r<.1f)return BlockId.TallGrass;if(r<.14f)return Pick(PlainsFlowers);break;case BiomeId.Meadow:if(r<.14f)return BlockId.TallGrass;if(r<.205f)return Pick(MeadowFlowers);if(r<.21f)return BlockId.Sunflower;break;case BiomeId.FlowerForest:if(r<.07f)return BlockId.TallGrass;if(r<.26f)return Pick(FlowerForestFlowers);if(r<.275f)return q<.5f?BlockId.Lilac:BlockId.RoseBush;break;case BiomeId.Forest:if(r<.06f)return BlockId.TallGrass;if(r<.08f)return BlockId.Fern;if(r<.092f)return q<.5f?BlockId.Dandelion:BlockId.Poppy;if(r<.098f)return q<1f/3f?BlockId.Lilac:q<2f/3f?BlockId.RoseBush:BlockId.Peony;break;case BiomeId.BirchForest:if(r<.06f)return BlockId.TallGrass;if(r<.086f)return q<.5f?BlockId.LilyOfTheValley:BlockId.Dandelion;if(r<.093f)return q<.5f?BlockId.Lilac:BlockId.Peony;break;case BiomeId.DarkForest:if(r<.05f)return BlockId.TallGrass;if(r<.075f)return q<.5f?BlockId.BrownMushroom:BlockId.RedMushroom;if(r<.09f)return BlockId.LargeFern;if(r<.097f)return q<.5f?BlockId.RoseBush:BlockId.Peony;break;case BiomeId.Jungle:if(r<.12f)return BlockId.TallGrass;if(r<.22f)return BlockId.Fern;break;case BiomeId.SparseJungle:if(r<.14f)return BlockId.TallGrass;if(r<.2f)return BlockId.Fern;break;case BiomeId.Taiga:case BiomeId.OldGrowthTaiga:case BiomeId.SnowyTaiga:case BiomeId.Grove:if(r<.08f)return BlockId.Fern;if(r<.12f)return q<.5f?BlockId.TallGrass:BlockId.LargeFern;if(r<.135f)return BlockId.SweetBerryBush;break;case BiomeId.Savanna:if(r<.25f)return BlockId.TallGrass;break;case BiomeId.Swamp:if(r<.1f)return BlockId.TallGrass;if(r<.13f)return BlockId.Fern;if(r<.145f)return BlockId.BlueOrchid;break;case BiomeId.Windswept:if(r<.05f)return BlockId.TallGrass;break;case BiomeId.Desert:if(r<.016f)return BlockId.DeadBush;break;case BiomeId.Badlands:if(r<.02f)return BlockId.DeadBush;break;case BiomeId.WoodedBadlands:if(r<.04f)return BlockId.DeadBush;if(r<.08f)return BlockId.TallGrass;break;case BiomeId.CherryGrove:if(r<.14f)return BlockId.TallGrass;if(r<.34f)return Pick(CherryFlowers);break;case BiomeId.BambooJungle:if(r<.32f)return BlockId.BambooPlant;if(r<.42f)return BlockId.Fern;if(r<.48f)return BlockId.TallGrass;break;case BiomeId.MushroomFields:if(r<.22f)return q<.5f?BlockId.BrownMushroom:BlockId.RedMushroom;break;case BiomeId.MangroveSwamp:if(r<.08f)return BlockId.TallGrass;if(r<.12f)return BlockId.Fern;break;}return BlockId.Air;
        }

        TreeKind TreeAt(int x,int z) => TreeAt(x,z,Biomes.Sample(x,z),true);
        TreeKind TreeAt(int x,int z,BiomeSample s,bool checkAquifer)
        {
            if(s.Height<=Sea||s.Height>=MaxY-13||(checkAquifer&&IsAquiferXZ(x,z)))return TreeKind.None;if(s.Biome==BiomeId.Desert)return G(x*3+11,z*3-7)<.006f?TreeKind.Cactus:TreeKind.None;if(s.Biome==BiomeId.Badlands||s.Biome==BiomeId.WoodedBadlands&&s.Height<Sea+10||s.Biome==BiomeId.Windswept&&DA(x*.11f+21,z*.11f-21)+(s.Height-Sea)*.004f>=.53f)return TreeKind.None;if(s.Biome==BiomeId.MushroomFields){float r=G(x*3+11,z*3-7);return r<.004f?TreeKind.GiantRedMushroom:r<.008f?TreeKind.GiantBrownMushroom:TreeKind.None;}if(s.Biome==BiomeId.IceSpikes)return G(x*3+11,z*3-7)<.01f?TreeKind.IceSpike:TreeKind.None;
            int cell;switch(s.Biome){case BiomeId.Forest:case BiomeId.BirchForest:case BiomeId.DarkForest:case BiomeId.Taiga:case BiomeId.OldGrowthTaiga:case BiomeId.BambooJungle:cell=6;break;case BiomeId.FlowerForest:case BiomeId.Swamp:case BiomeId.CherryGrove:cell=9;break;case BiomeId.Jungle:cell=5;break;case BiomeId.SparseJungle:cell=11;break;case BiomeId.SnowyTaiga:cell=8;break;case BiomeId.Snowy:case BiomeId.Plains:cell=18;break;case BiomeId.Savanna:cell=13;break;case BiomeId.Meadow:cell=26;break;case BiomeId.Grove:case BiomeId.MangroveSwamp:cell=7;break;case BiomeId.Windswept:cell=15;break;case BiomeId.WoodedBadlands:cell=10;break;default:return TreeKind.None;}
            int cx=FloorDiv(x,cell),cz=FloorDiv(z,cell),tx=cx*cell+(int)Math.Floor(G(cx*17+3,cz*13-5)*cell),tz=cz*cell+(int)Math.Floor(G(cx*7-11,cz*23+9)*cell);if(x!=tx||z!=tz)return TreeKind.None;float q=G(cx*31+1,cz*37-1);switch(s.Biome){case BiomeId.Forest:return q<.5f?TreeKind.Oak:q<.75f?TreeKind.Birch:q<.87f?TreeKind.FancyOak:TreeKind.None;case BiomeId.BirchForest:return q<.78f?TreeKind.Birch:q<.9f?TreeKind.Oak:TreeKind.None;case BiomeId.FlowerForest:return q<.45f?TreeKind.Oak:q<.7f?TreeKind.Birch:TreeKind.None;case BiomeId.DarkForest:return q<.62f?TreeKind.DarkOak:q<.82f?TreeKind.Oak:q<.9f?TreeKind.Birch:TreeKind.None;case BiomeId.Jungle:return q<.72f?TreeKind.Jungle:q<.85f?TreeKind.Oak:TreeKind.None;case BiomeId.SparseJungle:return q<.5f?TreeKind.Jungle:q<.65f?TreeKind.Oak:TreeKind.None;case BiomeId.Taiga:return q<.85f?TreeKind.Spruce:TreeKind.None;case BiomeId.OldGrowthTaiga:return q<.55f?TreeKind.TallSpruce:q<.85f?TreeKind.Spruce:TreeKind.None;case BiomeId.SnowyTaiga:return q<.7f?TreeKind.Spruce:TreeKind.None;case BiomeId.Snowy:return q<.4f?TreeKind.Spruce:TreeKind.None;case BiomeId.Plains:return q<.3f?TreeKind.Oak:q<.42f?TreeKind.FancyOak:TreeKind.None;case BiomeId.Savanna:return q<.55f?TreeKind.Acacia:TreeKind.None;case BiomeId.Meadow:return q<.25f?TreeKind.Oak:TreeKind.None;case BiomeId.Grove:return q<.75f?TreeKind.Spruce:TreeKind.None;case BiomeId.Swamp:return q<.55f?TreeKind.Oak:TreeKind.None;case BiomeId.Windswept:return q<.25f?TreeKind.Oak:q<.4f?TreeKind.Spruce:TreeKind.None;case BiomeId.CherryGrove:return q<.4f?TreeKind.Cherry:TreeKind.None;case BiomeId.BambooJungle:return q<.5f?TreeKind.Jungle:q<.65f?TreeKind.Oak:TreeKind.None;case BiomeId.WoodedBadlands:return q<.35f?TreeKind.Oak:TreeKind.None;case BiomeId.MangroveSwamp:return q<.45f?TreeKind.Oak:q<.65f?TreeKind.DarkOak:TreeKind.None;default:return TreeKind.None;}
        }
        static int FloorDiv(int a,int b){int q=a/b,r=a%b;return r!=0&&((r<0)!=(b<0))?q-1:q;}
        void PlaceTreeDense(ushort[] a,int wx0,int wz0,int x,int z,TreeKind kind)
        {
            int y=Biomes.DensitySurfaceY(x,z),h=4+(int)Math.Floor(G(x+77,z-77)*3);void Hard(int xx,int yy,int zz,BlockId id){int lx=xx-wx0,lz=zz-wz0;if((uint)lx<16&&(uint)lz<16&&yy>=MinY&&yy<=MaxY)a[Idx(lx,yy,lz)]=(ushort)id;}void Leaf(int xx,int yy,int zz,BlockId id){int lx=xx-wx0,lz=zz-wz0;if((uint)lx<16&&(uint)lz<16&&yy>=MinY&&yy<=MaxY){int i=Idx(lx,yy,lz);BlockId old=(BlockId)a[i];if(old==BlockId.Air||BlockRegistry.Get(old).Shape==BlockShape.Cross)a[i]=(ushort)id;}}void Disc(int xx,int yy,int zz,int rad,BlockId id,bool corners){for(int dx=-rad;dx<=rad;dx++)for(int dz=-rad;dz<=rad;dz++){int ax=Math.Abs(dx),az=Math.Abs(dz);if(rad>=2&&ax==rad&&az==rad&&(!corners||G3(xx+dx,yy,zz+dz)<.6f)||rad>=3&&ax+az>rad+1)continue;Leaf(xx+dx,yy,zz+dz,id);}}
            if(kind==TreeKind.Cactus){int c=2+(int)Math.Floor(G(x+33,z-33)*2);for(int yy=y+1;yy<=y+c;yy++)Hard(x,yy,z,BlockId.Cactus);return;}if(kind==TreeKind.Spruce||kind==TreeKind.TallSpruce){bool tall=kind==TreeKind.TallSpruce;h=tall?10+(int)Math.Floor(G(x+7,z)*4):h+2;int top=y+h;Leaf(x,top+1,z,BlockId.SpruceLeaves);int layers=tall?8:5;for(int u=0;u<layers;u++){int yy=top-u;if(yy<=y+2)break;Disc(x,yy,z,u==0||u%2==1?1:2,BlockId.SpruceLeaves,false);}for(int yy=y+1;yy<=top;yy++)Hard(x,yy,z,BlockId.SpruceLog);return;}if(kind==TreeKind.DarkOak){h=7+(int)Math.Floor(G(x+3,z-3)*3);int top=y+h;for(int u=-2;u<=1;u++){int yy=top+u;if(yy>y)Disc(x,yy,z,u<=0?3:2,BlockId.DarkOakLeaves,true);}for(int yy=y+1;yy<=top;yy++){Hard(x,yy,z,BlockId.DarkOakLog);Hard(x+1,yy,z,BlockId.DarkOakLog);Hard(x,yy,z+1,BlockId.DarkOakLog);Hard(x+1,yy,z+1,BlockId.DarkOakLog);}return;}if(kind==TreeKind.Jungle){h=8+(int)Math.Floor(G(x-9,z+9)*5);int top=y+h;Disc(x,top+1,z,1,BlockId.JungleLeaves,false);Disc(x,top,z,2,BlockId.JungleLeaves,true);Disc(x,top-1,z,2,BlockId.JungleLeaves,true);for(int yy=y+1;yy<=top;yy++)Hard(x,yy,z,BlockId.JungleLog);return;}if(kind==TreeKind.Acacia){h=5+(int)Math.Floor(G(x+1,z+1)*2);int d=(int)Math.Floor(G(x-5,z+5)*4),dx=TreeDirX[d],dz=TreeDirZ[d],cx=x,cz=z;for(int yy=y+1;yy<=y+h;yy++){if(yy>y+2&&(yy-y)%2==1){cx+=dx;cz+=dz;}Hard(cx,yy,cz,BlockId.AcaciaLog);}Disc(cx,y+h,cz,3,BlockId.AcaciaLeaves,false);Disc(cx,y+h+1,cz,2,BlockId.AcaciaLeaves,true);return;}if(kind==TreeKind.FancyOak){h=7+(int)Math.Floor(G(x+19,z-19)*3);int top=y+h;for(int u=0;u<FancyOakRadii.Length;u++){int yy=top-3+u;if(yy>y)Disc(x,yy,z,FancyOakRadii[u],BlockId.OakLeaves,true);}int d=(int)Math.Floor(G(x,z+77)*4),bx=x+TreeDirX[d]*2,bz=z+TreeDirZ[d]*2;Disc(bx,top-3,bz,1,BlockId.OakLeaves,false);for(int yy=y+1;yy<=top;yy++)Hard(x,yy,z,BlockId.OakLog);return;}if(kind==TreeKind.Cherry){h=4+(int)Math.Floor(G(x+5,z-5)*2);int top=y+h;Disc(x,top+1,z,1,BlockId.FloweringAzaleaLeaves,false);for(int u=0;u<CherryRadii.Length;u++){int yy=top-u;if(yy<=y+1)break;Disc(x,yy,z,CherryRadii[u],G3(x,yy,z)<.25f?BlockId.AzaleaLeaves:BlockId.FloweringAzaleaLeaves,true);}for(int yy=y+1;yy<=top;yy++)Hard(x,yy,z,BlockId.OakLog);return;}if(kind==TreeKind.GiantRedMushroom){h=4+(int)Math.Floor(G(x,z)*3);for(int yy=y+1;yy<=y+h;yy++)Hard(x,yy,z,BlockId.MushroomStem);Disc(x,y+h+1,z,1,BlockId.RedMushroomBlock,false);Disc(x,y+h,z,2,BlockId.RedMushroomBlock,true);return;}if(kind==TreeKind.GiantBrownMushroom){h=3+(int)Math.Floor(G(x+1,z-1)*2);for(int yy=y+1;yy<=y+h;yy++)Hard(x,yy,z,BlockId.MushroomStem);Disc(x,y+h+1,z,3,BlockId.BrownMushroomBlock,false);return;}if(kind==TreeKind.IceSpike){h=4+(int)Math.Floor(G(x+2,z-2)*7);for(int yy=y+1;yy<=y+h;yy++)Hard(x,yy,z,BlockId.PackedIce);if(h>5){Hard(x+1,y+1,z,BlockId.PackedIce);Hard(x,y+1,z+1,BlockId.PackedIce);}return;}BlockId log=kind==TreeKind.Birch?BlockId.BirchLog:BlockId.OakLog,leaf=kind==TreeKind.Birch?BlockId.BirchLeaves:BlockId.OakLeaves;if(kind==TreeKind.Birch)h++;int crown=y+h;for(int u=-2;u<=1;u++){int yy=crown+u;if(yy>y&&yy<=MaxY)Disc(x,yy,z,u<=-1?2:1,leaf,true);}for(int yy=y+1;yy<=crown;yy++)Hard(x,yy,z,log);
        }

        bool BedrockNoise(int x,int y,int z)=>y<=MinY||y<MinY+5&&G3(x,y,z)<(MinY+5-y)/5f;
        BlockId RockVariant(int x,int y,int z){var C=CaveCells;if(y<=0)return n.Value3(ref C[13],x*.07f+71.7f,y*.07f-51.5f,z*.07f+31.3f)>.86f?BlockId.Tuff:BlockId.Deepslate;if(y<8&&G3(x+7,y-3,z+5)<(8-y)/8f)return BlockId.Deepslate;if(y<84){if(n.Value3(ref C[14],x*.085f+5.1f,y*.085f-3.3f,z*.085f+9.7f)>.85f)return BlockId.Granite;if(n.Value3(ref C[15],x*.085f-8.8f,y*.085f+6.6f,z*.085f-2.2f)>.85f)return BlockId.Diorite;if(n.Value3(ref C[16],x*.085f+14.4f,y*.085f+11.1f,z*.085f+4.4f)>.85f)return BlockId.Andesite;}return BlockId.Stone;}
        // Per-thread lattice-cell caches for the Value3 call sites of Cave()/RockVariant(). The cave pass
        // walks each column upwards, so consecutive voxels mostly stay in one noise cell per site.
        [ThreadStatic] static MainTerrainNoise.Cell3[] caveCells;
        static MainTerrainNoise.Cell3[] CaveCells=>caveCells??(caveCells=new MainTerrainNoise.Cell3[17]);
        int Cave(int x,int y,int z,int top){if(y<=MinY+5||y>top)return 0;if(y==top)return 0;int depth=top-y;float fade=depth<8?depth/8f:1;if(y<MinY+14)fade=Math.Min(fade,(y-(MinY+5))/9f);if(fade<=0)return 0;var C=CaveCells;float amp=1+Signed(n.Value3(ref C[0],x*.09f+2.2f,y*.09f-4.4f,z*.09f+6.6f))*.85f;bool cave=false;if(y<110){float f=Signed(n.Value3(ref C[1],x*.012f+11.1f,y*.018f-7.7f,z*.012f+4.4f));if(f*f<.026f*.026f*fade*fade*amp)cave=true;}if(!cave){const float w=.045f;float xx=x+Signed(n.Value3(ref C[2],x*w+61.1f,y*w-22.2f,z*w+13.3f))*15,yy=y+Signed(n.Value3(ref C[3],x*w+9.9f,y*w+2.2f,z*w-51.1f))*10,zz=z+Signed(n.Value3(ref C[4],x*w-31.7f,y*w+44.4f,z*w-8.8f))*15,d=n.Value3(ref C[5],x*.02f+5.5f,y*.02f-3.3f,z*.02f+7.7f),lim=(.3f+1.8f*d*d)*amp;if(y<90){float a=Signed(n.Value3(ref C[6],xx*.022f+12.3f,yy*.022f-7.7f,zz*.022f+4.1f)),b=Signed(n.Value3(ref C[7],xx*.022f-41.9f,yy*.022f+23.3f,zz*.022f-9.5f));if(a*a+b*b<.016f*fade*lim)cave=true;}if(!cave&&n.Value3(ref C[8],x*.006f+90.1f,y*.006f+5.5f,z*.006f-30.2f)>.5f){float a=Signed(n.Value3(ref C[9],xx*.04f+5.1f,yy*.04f-8.2f,zz*.04f+3.3f)),b=Signed(n.Value3(ref C[10],xx*.04f-7.7f,yy*.04f+9.9f,zz*.04f-2.1f));if(a*a+b*b<.009f*fade*lim)cave=true;}}if(!cave)return 0;if(y<=MinY+12)return 3;if(depth>12&&n.Value3(ref C[11],x*.012f+401.1f,y*.005f-77.7f,z*.012f+220.2f)>.52f){float q=n.Value3(ref C[12],x*.013f+11.3f,.5f,z*.013f-9.9f);int wl=(int)Math.Floor(-14+(q-.5f)*44);if(wl>Sea)wl=Sea;if(y<=wl)return 2;}return 1;}

        struct Mulberry{int s;public Mulberry(int v){s=v;}public double Next(){unchecked{s+=1831565813;int e=(s^(int)((uint)s>>15))*(1|s);e=(e+(e^(int)((uint)e>>7))*(61|e))^e;return (uint)(e^(int)((uint)e>>14))/4294967296.0;}}}
        Tunnel Feature(int cx,int cz){long key=((long)cx<<32)^(uint)cz;if(!tunnelCache.TryGetValue(key,out var t)){var built=BuildTunnel(cx,cz)??NoTunnel;t=tunnelCache.GetOrAdd(key,built);}return t.Exists?t:null;}
        Tunnel BuildTunnel(int cx,int cz){unchecked{int st=(cx+seed)*461845907^(cz-seed)*unchecked((int)3432918353u)^1597334677;var r=new Mulberry(st);if(r.Next()>=1.0/110.0)return null;var t=new Tunnel();double x=cx*16+r.Next()*16,z=cz*16+r.Next()*16,ang=r.Next()*Math.PI*2;int len=44+(int)Math.Floor(r.Next()*40);double radius=3.6+r.Next()*4.4;t.Depth=18+(int)Math.Floor(r.Next()*46);t.WaterFrac=(float)(r.Next()*r.Next()*.72);for(int i=0;i<len;i++){x+=Math.Cos(ang)*1.7;z+=Math.Sin(ang)*1.7;ang+=(r.Next()-.5)*.55;double u=len>1?i/(double)(len-1):.5,rr=radius*Math.Sin(u*Math.PI)+.5;t.Nodes.Add(new Node(x,z,rr));t.MinX=Math.Min(t.MinX,x-rr);t.MaxX=Math.Max(t.MaxX,x+rr);t.MinZ=Math.Min(t.MinZ,z-rr);t.MaxZ=Math.Max(t.MaxZ,z+rr);}return t;}}
        List<Tunnel> Nearby(int cx,int cz){var list=nearbyScratch??(nearbyScratch=new List<Tunnel>(24));list.Clear();double x0=cx*16,z0=cz*16;for(int dx=-6;dx<=6;dx++)for(int dz=-6;dz<=6;dz++){var t=Feature(cx+dx,cz+dz);if(t!=null&&!(t.MaxX<x0-6||t.MinX>x0+22||t.MaxZ<z0-6||t.MinZ>z0+22))list.Add(t);}return list;}
        bool IsAquiferXZ(int x,int z){var list=Nearby(FloorDiv(x,16),FloorDiv(z,16));for(int i=0;i<list.Count;i++)for(int j=0;j<list[i].Nodes.Count;j++){var q=list[i].Nodes[j];double dx=x+.5-q.X,dz=z+.5-q.Z;if(dx*dx+dz*dz<q.R*q.R)return true;}return false;}
        void CarveAquifers(ushort[] a,int wx0,int wz0,int cx,int cz){var list=Nearby(cx,cz);if(list.Count==0)return;var tops=aquiferTopsScratch??(aquiferTopsScratch=new short[256]);for(int x=0;x<16;x++)for(int z=0;z<16;z++){int top=MinY;for(int y=MaxY;y>MinY;y--){BlockId id=(BlockId)a[Idx(x,y,z)];if(id!=BlockId.Air&&id!=BlockId.Water){top=y;break;}}tops[x*16+z]=(short)top;}foreach(var t in list){bool wet=false;foreach(var wetNode in t.Nodes)if(Biomes.Sample(SourceMath.JsRound(wetNode.X),SourceMath.JsRound(wetNode.Z)).Height<=Sea){wet=true;break;}foreach(var q in t.Nodes){double rr=q.R,r2=rr*rr;int x0=Math.Max(0,(int)Math.Floor(q.X-rr)-wx0),x1=Math.Min(15,(int)Math.Ceiling(q.X+rr)-wx0),z0=Math.Max(0,(int)Math.Floor(q.Z-rr)-wz0),z1=Math.Min(15,(int)Math.Ceiling(q.Z+rr)-wz0);for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++){double dx=wx0+x+.5-q.X,dz=wz0+z+.5-q.Z,dist=dx*dx+dz*dz;if(dist>=r2)continue;int top=tops[x*16+z];if(top<=MinY+10)continue;int low=top-t.Depth;if(low<MinY+9)low=MinY+9;int span=top-low;if(span<1)continue;int water=wet?Sea-1:low+SourceMath.JsRound(t.WaterFrac*span);if(water>top-2)water=top-2;for(int y=low;y<=top;y++){int ii=Idx(x,y,z);BlockId old=(BlockId)a[ii];if(old==BlockId.Bedrock||old==BlockId.Air)continue;float u=(y-low)/(float)span,profile=u<.4f?.22f+.78f*(u/.4f):u>.85f?1-.35f*((u-.85f)/.15f):1;profile*=(float)rr;if(dist>=profile*profile)continue;a[ii]=(ushort)(y<=MinY+12?BlockId.Lava:y<=water?BlockId.Water:BlockId.Air);}}}}}
        void SeaFloodAndFreeze(ushort[] a,BiomeSample[] columnBiomes){var stack=floodStackScratch??(floodStackScratch=new Stack<int>(2048));stack.Clear();int maxLocal=Sea-1-MinY,row=H,plane=H*16;for(int x=0;x<16;x++)for(int z=0;z<16;z++){int col=Col(x,z);bool low=columnBiomes[x*S+z].Height<=Sea;for(int y=MinY+1;y<Sea;y++){int ii=col+y-MinY;if((BlockId)a[ii]==BlockId.Water)stack.Push(ii);else if(low&&a[ii]==0){a[ii]=(ushort)BlockId.Water;stack.Push(ii);}}}while(stack.Count>0){int ii=stack.Pop(),ly=ii%H,p=(ii-ly)/H,z=p&15,x=p>>4;if(ly<maxLocal&&a[ii+1]==0){a[ii+1]=(ushort)BlockId.Water;stack.Push(ii+1);}if(ly>1&&a[ii-1]==0){a[ii-1]=(ushort)BlockId.Water;stack.Push(ii-1);}if(z<15&&a[ii+row]==0){a[ii+row]=(ushort)BlockId.Water;stack.Push(ii+row);}if(z>0&&a[ii-row]==0){a[ii-row]=(ushort)BlockId.Water;stack.Push(ii-row);}if(x<15&&a[ii+plane]==0){a[ii+plane]=(ushort)BlockId.Water;stack.Push(ii+plane);}if(x>0&&a[ii-plane]==0){a[ii-plane]=(ushort)BlockId.Water;stack.Push(ii-plane);}}for(int x=0;x<16;x++)for(int z=0;z<16;z++)if(columnBiomes[x*S+z].TemperatureBucket==0){for(int y=Sea;y>=Sea-2;y--){int ii=Idx(x,y,z);if((BlockId)a[ii]==BlockId.Water&&a[ii+1]==0){a[ii]=(ushort)BlockId.Ice;break;}}}}

        struct OreRng{int s;public OreRng(int s){this.s=s;}public double Next(){unchecked{s|=0;s+=1831565813;int e=(s^(int)((uint)s>>15))*(1|s);e=(e+(e^(int)((uint)e>>7))*(61|e))^e;return (uint)(e^(int)((uint)e>>14))/4294967296.0;}}}
        OreRng OreRandom(int cx,int cz,int os){unchecked{uint bits=n.HashBitsTrunc(cx*3.0+os*17.0+.31,cz*5.0-os*7.0+.77);return new OreRng((int)bits);}}
        // Source Yw(c,...) consumes the shared closure RNG: the state must advance for the caller too.
        int OreY(ref OreRng r,int min,int peak,int max){bool left=r.Next()*(max-min)<peak-min;double d=1-Math.Sqrt(r.Next());return SourceMath.JsRound(left?peak-d*(peak-min):peak+d*(max-peak));}
        void GenerateOres(ushort[] a,int wx0,int wz0,int cx,int cz){for(int ncx=cx-1;ncx<=cx+1;ncx++)for(int ncz=cz-1;ncz<=cz+1;ncz++)foreach(var o in Ores){var r=OreRandom(ncx,ncz,o.Seed);for(int at=0;at<o.Attempts;at++){int x=ncx*16+(int)Math.Floor(r.Next()*16),z=ncz*16+(int)Math.Floor(r.Next()*16),y=OreY(ref r,o.Min,o.Peak,o.Max),len=o.MinSize+(int)Math.Floor(r.Next()*(o.MaxSize-o.MinSize+1));if(o.Mountain&&!Mountain(Biomes.Sample(x,z).Biome))continue;for(int i=0;i<len;i++){SetOre(a,wx0,wz0,x,y,z,o);if(r.Next()<.35)SetOre(a,wx0,wz0,x+(r.Next()<.5?1:-1),y,z,o);double q=r.Next();if(q<.4)x+=r.Next()<.5?1:-1;else if(q<.8)z+=r.Next()<.5?1:-1;else y+=r.Next()<.5?1:-1;}}}}
        void SetOre(ushort[] a,int wx0,int wz0,int x,int y,int z,Ore o){if(x<wx0||x>=wx0+16||z<wz0||z>=wz0+16||y<=MinY||y>=320)return;int ii=Idx(x-wx0,y,z-wz0);if(OreReplace((BlockId)a[ii]))a[ii]=(ushort)(y<0?o.D:o.O);}
        CaveBiome CaveAt(int x,int y,int z,int top){float d=(top-y)/128f;if(d<.2f)return CaveBiome.None;if(d>.9f&&EA(x*.018f+303.3f,y*.02f-44.4f,z*.018f-101.1f)<.4f)return CaveBiome.DeepDark;if(d<=.96f){float l=EA(x*.011f+700.5f,y*.012f+55.5f,z*.011f-200.2f),r=EA(x*.011f-410.7f,y*.012f-66.6f,z*.011f+330.3f);if(l>.64f&&l>=r)return CaveBiome.Lush;if(r>.64f)return CaveBiome.Dripstone;}return CaveBiome.None;}
        void DecorateCaves(ushort[] a,byte[] m,int wx0,int wz0){for(int x=0;x<16;x++)for(int z=0;z<16;z++){int wx=wx0+x,wz=wz0+z,top=MinY;for(int y=MaxY;y>MinY;y--)if(CaveReplace((BlockId)a[Idx(x,y,z)])){top=y;break;}if(top-MinY<33)continue;for(int y=top-2;y>MinY+6;y--){int ii=Idx(x,y,z);if(a[ii]!=0)continue;var cb=CaveAt(wx,y,wz,top);if(cb==CaveBiome.None)continue;int below=ii-1,above=ii+1;bool floor=CaveRock((BlockId)a[below]),ceil=CaveRock((BlockId)a[above]);if(!floor&&!ceil)continue;float q=G3(wx,y,wz);if(cb==CaveBiome.Lush){if(floor){if(q<.72f)a[below]=(ushort)(q<.04f?BlockId.Clay:BlockId.Moss);if(q<.06f)a[ii]=(ushort)BlockId.FloweringAzaleaLeaves;else if(q<.16f)a[ii]=(ushort)BlockId.MossCarpet;else if(q<.3f)a[ii]=(ushort)(q<.23f?BlockId.Fern:BlockId.TallGrass);}if(ceil){if(q<.6f)a[above]=(ushort)BlockId.Moss;if(q<.22f&&a[ii]==0){int len=2+(int)Math.Floor(G(wx*3+1,wz*3-2)*4);for(int k=0;k<len;k++){int yy=y-k;if(yy<=MinY+6)break;int dst=Idx(x,yy,z);if(a[dst]!=0)break;a[dst]=(ushort)BlockId.CaveVines;if(G3(wx,yy,wz+7)<.13f)m[dst]=1;}}}}else if(cb==CaveBiome.Dripstone){if(floor){if(q<.05f)a[below]=(ushort)BlockId.Calcite;else if(q<.4f)a[below]=(ushort)BlockId.Dripstone;if(q<.06f){int len=1+(int)Math.Floor(G(wx+4,wz-9)*3);for(int k=0;k<len;k++){int yy=y+k;if(yy>=top)break;int dst=Idx(x,yy,z);if(a[dst]!=0)break;a[dst]=(ushort)BlockId.Dripstone;}}}if(ceil){if(q<.4f)a[above]=(ushort)BlockId.Dripstone;if(q<.06f){int len=1+(int)Math.Floor(G(wx-6,wz+3)*3);for(int k=0;k<len;k++){int yy=y-k;if(yy<=MinY+6)break;int dst=Idx(x,yy,z);if(a[dst]!=0)break;a[dst]=(ushort)BlockId.Dripstone;}}}}else if(cb==CaveBiome.DeepDark&&floor){if(q<.02f)a[below]=(ushort)BlockId.BuddingAmethyst;else if(q<.05f)a[below]=(ushort)BlockId.Amethyst;}}}}
    }
}
