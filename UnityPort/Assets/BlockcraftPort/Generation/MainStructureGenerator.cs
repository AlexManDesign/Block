using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-aligned world structures from genWorker D2()/i4().
    /// Algorithms are rewritten for the Unity dense chunk buffer: deterministic candidate caches,
    /// clipped writes and compact outpost template data avoid GameObjects and per-chunk template allocations.
    /// Includes source shipwreck templates and their coastal/submerged selection.
    /// </summary>
    internal sealed class MainStructureGenerator
    {
        const int S=16, H=384, MinY=-64, MaxY=319, Sea=63;
        const int MineSearchRadius=9, MineOriginRadius=7, MineMaxPieces=46;
        readonly int seed;
        readonly BiomeGenerator biomes;

        enum SurfaceKind:byte { Pyramid, Outpost, Portal, Ship }
        sealed class SurfaceSite
        {
            public bool Exists;
            public int Wx,Wz,BaseY,Rotation,ChunkX,ChunkZ;
            public bool Beached; public int Variant;
        }
        static readonly SurfaceSite NoSite=new SurfaceSite{Exists=false};
        readonly ConcurrentDictionary<long,SurfaceSite> pyramidSites=new ConcurrentDictionary<long,SurfaceSite>();
        readonly ConcurrentDictionary<long,SurfaceSite> outpostSites=new ConcurrentDictionary<long,SurfaceSite>();
        readonly ConcurrentDictionary<long,SurfaceSite> portalSites=new ConcurrentDictionary<long,SurfaceSite>();
        readonly ConcurrentDictionary<long,SurfaceSite> shipSites=new ConcurrentDictionary<long,SurfaceSite>();
        [ThreadStatic] static List<SurfaceSite> siteScratch;

        struct PaletteEntry
        {
            public BlockId Block; public byte Meta, RotateMode;
            public PaletteEntry(BlockId b,int m,int r){Block=b;Meta=(byte)m;RotateMode=(byte)r;}
        }
        static readonly PaletteEntry[] OutpostPalette=
        {
            new PaletteEntry(BlockId.DarkOakLog,0,2), new PaletteEntry(BlockId.Cobblestone,0,0),
            new PaletteEntry(BlockId.BirchPlanks,0,0), new PaletteEntry(BlockId.DarkOakPlanks,0,0),
            new PaletteEntry(BlockId.DarkOakLog,1,2), new PaletteEntry(BlockId.DarkOakLog,2,2),
            new PaletteEntry(BlockId.DarkOakSlab,1,0), new PaletteEntry(BlockId.CobblestoneStairs,3,1),
            new PaletteEntry(BlockId.CobblestoneStairs,0,1), new PaletteEntry(BlockId.DarkOakSlab,0,0),
            new PaletteEntry(BlockId.DarkOakFence,0,0), new PaletteEntry(BlockId.DarkOakStairs,6,1),
            new PaletteEntry(BlockId.DarkOakStairs,4,1), new PaletteEntry(BlockId.DarkOakStairs,5,1),
            new PaletteEntry(BlockId.DarkOakStairs,7,1), new PaletteEntry(BlockId.CobblestoneStairs,7,1),
            new PaletteEntry(BlockId.CobblestoneSlab,1,0), new PaletteEntry(BlockId.CobblestoneStairs,4,1),
            new PaletteEntry(BlockId.CobblestoneStairs,6,1), new PaletteEntry(BlockId.CobblestoneStairs,5,1),
            new PaletteEntry(BlockId.CobblestoneStairs,1,1), new PaletteEntry(BlockId.CobblestoneStairs,2,1),
            new PaletteEntry(BlockId.CobblestoneWall,0,0), new PaletteEntry(BlockId.Torch,0,0),
            new PaletteEntry(BlockId.Chest,1,1)
        };

        sealed class Rng
        {
            int state;
            public Rng(int state){this.state=state;}
            public double Next()
            {
                unchecked
                {
                    state+=1831565813;
                    int e=(state^(int)((uint)state>>15))*(1|state);
                    e=(e+(e^(int)((uint)e>>7))*(61|e))^e;
                    return (uint)(e^(int)((uint)e>>14))/4294967296.0;
                }
            }
        }

        enum MinePieceKind:byte { Room, Corridor, Crossing, Slope }
        struct MinePiece
        {
            public MinePieceKind Kind;
            public int Axis,Dir,Sx,Sz,Sy,Len,Cx,Cz,Y,YDelta,ChestI;
            public bool Rails,Webs;
        }
        struct MineBranch { public int X,Z,Y,Axis,Dir; public MineBranch(int x,int z,int y,int a,int d){X=x;Z=z;Y=y;Axis=a;Dir=d;} }
        sealed class MineFeature
        {
            public bool Exists;
            public readonly List<MinePiece> Pieces=new List<MinePiece>(48);
            public int MinX,MaxX,MinZ,MaxZ;
        }
        static readonly MineFeature NoMine=new MineFeature{Exists=false};
        readonly ConcurrentDictionary<long,MineFeature> mineCache=new ConcurrentDictionary<long,MineFeature>();

        internal struct DecorationExclusion
        {
            public int MinX,MaxX,MinZ,MaxZ;
            public DecorationExclusion(int minX,int maxX,int minZ,int maxZ){MinX=minX;MaxX=maxX;MinZ=minZ;MaxZ=maxZ;}
            public bool Contains(int x,int z)=>x>=MinX&&x<=MaxX&&z>=MinZ&&z<=MaxZ;
        }
        [ThreadStatic] static List<DecorationExclusion> decorationScratch;

        public MainStructureGenerator(int seed,BiomeGenerator biomes)
        {
            this.seed=seed; this.biomes=biomes;
        }

        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        static int FloorDiv(int a,int b){int q=a/b,r=a%b;return r!=0&&((r<0)!=(b<0))?q-1:q;}
        static int Idx(int x,int y,int z)=>((x<<4)+z)*H+(y-MinY);
        static int AxisX(int axis,int dir)=>axis==0?0:dir;
        static int AxisZ(int axis,int dir)=>axis==1?0:dir;

        double Hash2(double x,double z)
        {
            unchecked
            {
                // genWorker aQ(): the products are JavaScript Number operations, not Math.imul.
                // Preserve their IEEE-754 rounding before the first bitwise ToInt32 conversion.
                int ix=SourceMath.JsToInt32(x+seed), iz=SourceMath.JsToInt32(z-seed);
                double raw=(double)ix*374761393d;raw+=(double)iz*668265263d;raw+=527595518d;
                int r=SourceMath.JsToInt32(raw);r^=(int)((uint)r>>13);r*=1274126177;r^=(int)((uint)r>>16);
                return (uint)r/4294967296.0;
            }
        }
        double Hash3(double x,double y,double z)
        {
            unchecked
            {
                // genWorker r4(): same Number -> bitwise conversion sequence as aQ().
                int ix=SourceMath.JsToInt32(x+seed), iy=SourceMath.JsToInt32(y), iz=SourceMath.JsToInt32(z-seed);
                double raw=(double)ix*374761393d;raw+=(double)iy*668265263d;raw+=(double)iz*2147483423d;
                int r=SourceMath.JsToInt32(raw);r^=(int)((uint)r>>13);r*=1274126177;r^=(int)((uint)r>>16);
                return (uint)r/4294967296.0;
            }
        }
        Rng SurfaceRng(int rx,int rz,int salt)
        {
            double h=Hash2(rx*3.0+salt*17.0+.31,rz*5.0-salt*7.0+.77);
            int st=unchecked((int)(uint)(h*4294967296.0));
            return new Rng(st);
        }

        static bool OutpostBiome(BiomeId b)
        {
            switch(b)
            {
                case BiomeId.Plains: case BiomeId.Desert: case BiomeId.Savanna: case BiomeId.Taiga:
                case BiomeId.Snowy: case BiomeId.SnowyTaiga: case BiomeId.Meadow: case BiomeId.Grove:
                case BiomeId.SnowySlopes: case BiomeId.CherryGrove: case BiomeId.FrozenPeaks:
                case BiomeId.JaggedPeaks: case BiomeId.StonyPeaks: return true;
                default:return false;
            }
        }
        static bool ShipBiome(BiomeId b)
        {
            switch(b)
            {
                case BiomeId.Ocean: case BiomeId.ColdOcean: case BiomeId.LukewarmOcean:
                case BiomeId.WarmOcean: case BiomeId.FrozenOcean: case BiomeId.Beach:
                case BiomeId.SnowyBeach: return true;
                default:return false;
            }
        }

        // main t5()/o8()/n8(): mast variants are 5x weight, other beachable variants 2x, all others 1x.
        static int ShipVariant(bool beached,double roll)
        {
            int total=0;
            for(int i=0;i<MainShipwreckTemplates.All.Length;i++)
            {
                var t=MainShipwreckTemplates.All[i];
                if(beached&&!t.Beachable)continue;
                total+=t.Name.StartsWith("with_mast",StringComparison.Ordinal)?5:t.Beachable?2:1;
            }
            double pick=roll*total; int cumulative=0,last=0;
            for(int i=0;i<MainShipwreckTemplates.All.Length;i++)
            {
                var t=MainShipwreckTemplates.All[i];
                if(beached&&!t.Beachable)continue;
                last=i; cumulative+=t.Name.StartsWith("with_mast",StringComparison.Ordinal)?5:t.Beachable?2:1;
                if(pick<cumulative)return i;
            }
            return last;
        }

        SurfaceSite BuildSurfaceSite(SurfaceKind kind,int rx,int rz)
        {
            int salt,spacing,separation,flat,flatRadius,depth; double frequency=1.0; bool water=false;
            switch(kind)
            {
                case SurfaceKind.Pyramid: salt=14357617; spacing=32; separation=8; flat=8; flatRadius=10; depth=15; break;
                case SurfaceKind.Outpost: salt=165745296; spacing=32; separation=8; flat=6; flatRadius=7; depth=2; frequency=.2; break;
                case SurfaceKind.Portal: salt=34222645; spacing=40; separation=15; flat=0; flatRadius=5; depth=2; break;
                default: salt=165745295; spacing=24; separation=4; flat=6; flatRadius=4; depth=2; water=true; break;
            }
            Rng rng=SurfaceRng(rx,rz,salt);
            int range=spacing-separation;
            int chunkX=rx*spacing+(int)Math.Floor(rng.Next()*range);
            int chunkZ=rz*spacing+(int)Math.Floor(rng.Next()*range);
            double gate=rng.Next();
            int rotation=(int)Math.Floor(rng.Next()*4.0);
            double shipRoll=water?rng.Next():0.0;
            if(frequency<1.0 && gate>=frequency) return NoSite;
            int wx=chunkX*S+8,wz=chunkZ*S+8;
            BiomeId biome=biomes.Sample(wx,wz).Biome;
            if(kind==SurfaceKind.Pyramid && biome!=BiomeId.Desert) return NoSite;
            if(kind==SurfaceKind.Outpost && !OutpostBiome(biome)) return NoSite;
            if(kind==SurfaceKind.Ship && !ShipBiome(biome)) return NoSite;
            int baseY=biomes.DensitySurfaceY(wx,wz);
            bool beached=false; int variant=0;
            if(water)
            {
                beached=biome==BiomeId.Beach||biome==BiomeId.SnowyBeach;
                if(beached)
                {
                    if(baseY<Sea-1||baseY>Sea+3)return NoSite;
                }
                else
                {
                    int waterDepth=Sea-baseY; if(waterDepth<5||waterDepth>30)return NoSite;
                }
                variant=ShipVariant(beached,shipRoll);
            }
            else if(baseY<=Sea+1)return NoSite;
            if(baseY+24>=320 || baseY-depth-2<=MinY) return NoSite;
            if(flat>0)
            {
                int min=baseY,max=baseY,D=flatRadius;
                int h0=biomes.Sample(wx-D,wz-D).Height; if(h0<min)min=h0;if(h0>max)max=h0;
                int h1=biomes.Sample(wx+D,wz-D).Height; if(h1<min)min=h1;if(h1>max)max=h1;
                int h2=biomes.Sample(wx-D,wz+D).Height; if(h2<min)min=h2;if(h2>max)max=h2;
                int h3=biomes.Sample(wx+D,wz+D).Height; if(h3<min)min=h3;if(h3>max)max=h3;
                if(max-min>flat)return NoSite;
            }
            return new SurfaceSite{Exists=true,Wx=wx,Wz=wz,BaseY=baseY,Rotation=rotation,ChunkX=chunkX,ChunkZ=chunkZ,Beached=beached,Variant=variant};
        }

        ConcurrentDictionary<long,SurfaceSite> SiteCache(SurfaceKind kind)
        {
            return kind==SurfaceKind.Pyramid?pyramidSites:kind==SurfaceKind.Outpost?outpostSites:kind==SurfaceKind.Portal?portalSites:shipSites;
        }
        int Spacing(SurfaceKind kind)=>kind==SurfaceKind.Ship?24:kind==SurfaceKind.Portal?40:32;
        SurfaceSite GetSurfaceSite(SurfaceKind kind,int rx,int rz)
        {
            var cache=SiteCache(kind); long k=Key(rx,rz);
            SurfaceSite s;
            if(cache.TryGetValue(k,out s))return s.Exists?s:null;
            s=BuildSurfaceSite(kind,rx,rz);
            s=cache.GetOrAdd(k,s);
            return s.Exists?s:null;
        }
        List<SurfaceSite> SitesNearChunk(SurfaceKind kind,int cx,int cz)
        {
            var list=siteScratch??(siteScratch=new List<SurfaceSite>(8)); list.Clear();
            int spacing=Spacing(kind);
            int rx0=FloorDiv(cx-2,spacing),rx1=FloorDiv(cx+2,spacing),rz0=FloorDiv(cz-2,spacing),rz1=FloorDiv(cz+2,spacing);
            for(int rx=rx0;rx<=rx1;rx++)for(int rz=rz0;rz<=rz1;rz++)
            {
                var s=GetSurfaceSite(kind,rx,rz);
                if(s!=null&&Math.Abs(s.ChunkX-cx)<=2&&Math.Abs(s.ChunkZ-cz)<=2)list.Add(s);
            }
            return list;
        }

        // genWorker T2(): trees are suppressed around future structures even though the structure pass
        // runs later. main evaluates T2 for every candidate tree coordinate. The C# port precomputes
        // the same axis-aligned clear zones once for the chunk's expanded tree-sampling rectangle.
        internal List<DecorationExclusion> BuildTreeExclusions(int x0,int z0,int padding)
        {
            var list=decorationScratch??(decorationScratch=new List<DecorationExclusion>(12)); list.Clear();
            int minX=x0-padding,maxX=x0+S-1+padding,minZ=z0-padding,maxZ=z0+S-1+padding;
            AddSurfaceDecorationExclusions(SurfaceKind.Pyramid,11,minX,maxX,minZ,maxZ,list);
            AddSurfaceDecorationExclusions(SurfaceKind.Outpost,8,minX,maxX,minZ,maxZ,list);
            AddSurfaceDecorationExclusions(SurfaceKind.Portal,6,minX,maxX,minZ,maxZ,list);
            AddSurfaceDecorationExclusions(SurfaceKind.Ship,5,minX,maxX,minZ,maxZ,list);

            int cx0=FloorDiv(minX-3,S),cx1=FloorDiv(maxX+3,S),cz0=FloorDiv(minZ-3,S),cz1=FloorDiv(maxZ+3,S);
            for(int cx=cx0;cx<=cx1;cx++)for(int cz=cz0;cz<=cz1;cz++)
            {
                SurfaceSite well=DesertWell(cx,cz);
                if(well!=null&&RectIntersects(well.Wx-3,well.Wx+3,well.Wz-3,well.Wz+3,minX,maxX,minZ,maxZ))
                    list.Add(new DecorationExclusion(well.Wx-3,well.Wx+3,well.Wz-3,well.Wz+3));
            }
            return list;
        }
        void AddSurfaceDecorationExclusions(SurfaceKind kind,int clearHalf,int minX,int maxX,int minZ,int maxZ,List<DecorationExclusion> list)
        {
            int spacing=Spacing(kind);
            int cminX=FloorDiv(minX-clearHalf,S),cmaxX=FloorDiv(maxX+clearHalf,S);
            int cminZ=FloorDiv(minZ-clearHalf,S),cmaxZ=FloorDiv(maxZ+clearHalf,S);
            int rx0=FloorDiv(cminX-2,spacing),rx1=FloorDiv(cmaxX+2,spacing),rz0=FloorDiv(cminZ-2,spacing),rz1=FloorDiv(cmaxZ+2,spacing);
            for(int rx=rx0;rx<=rx1;rx++)for(int rz=rz0;rz<=rz1;rz++)
            {
                SurfaceSite site=GetSurfaceSite(kind,rx,rz); if(site==null)continue;
                int ax=site.Wx-clearHalf,bx=site.Wx+clearHalf,az=site.Wz-clearHalf,bz=site.Wz+clearHalf;
                if(RectIntersects(ax,bx,az,bz,minX,maxX,minZ,maxZ))list.Add(new DecorationExclusion(ax,bx,az,bz));
            }
        }
        static bool RectIntersects(int ax0,int ax1,int az0,int az1,int bx0,int bx1,int bz0,int bz1)
            =>ax1>=bx0&&ax0<=bx1&&az1>=bz0&&az0<=bz1;
        internal static bool DecorationBlocked(List<DecorationExclusion> exclusions,int x,int z)
        {
            if(exclusions==null)return false;
            for(int i=0;i<exclusions.Count;i++)if(exclusions[i].Contains(x,z))return true;
            return false;
        }

        static void RotateXZ(int x,int z,int r,out int rx,out int rz)
        {
            r&=3;
            if(r==1){rx=-z;rz=x;} else if(r==2){rx=-x;rz=-z;} else if(r==3){rx=z;rz=-x;} else {rx=x;rz=z;}
        }
        static byte RotateDir(byte meta,int r)=>(byte)((meta&~3)|(((meta&3)+r)&3));
        static byte RotateAxis(byte meta,int r)
        {
            if((r&1)!=0&&(meta&3)!=0)
            {
                int a=meta&3; a=a==1?2:a==2?1:a;
                return (byte)((meta&~3)|a);
            }
            return meta;
        }

        static bool InChunk(int wx,int wz,int x0,int z0)=>wx>=x0&&wx<x0+S&&wz>=z0&&wz<z0+S;
        static BlockId Get(ushort[] a,int x0,int z0,int wx,int y,int wz)
        {
            if(!InChunk(wx,wz,x0,z0)||y<MinY||y>MaxY)return BlockId.Air;
            return (BlockId)a[Idx(wx-x0,y,wz-z0)];
        }
        static void SetWorld(ushort[] a,byte[] meta,int x0,int z0,int wx,int y,int wz,BlockId id,byte m=0,bool onlyAir=false)
        {
            if(!InChunk(wx,wz,x0,z0)||y<MinY||y>MaxY)return;
            int i=Idx(wx-x0,y,wz-z0); if(onlyAir&&(BlockId)a[i]!=BlockId.Air)return;
            a[i]=(ushort)id; if(meta!=null)meta[i]=m;
        }
        static void SetLocal(ushort[] a,byte[] meta,int x0,int z0,SurfaceSite s,int lx,int yOff,int lz,BlockId id,byte m=0,bool onlyAir=false)
        {
            int rx,rz;RotateXZ(lx,lz,s.Rotation,out rx,out rz);
            SetWorld(a,meta,x0,z0,s.Wx+rx,s.BaseY+yOff,s.Wz+rz,id,m,onlyAir);
        }
        static void FoundationDown(ushort[] a,byte[] meta,int x0,int z0,SurfaceSite s,int lx,int lz,BlockId id,int count,int startOffset=-1)
        {
            int rx,rz;RotateXZ(lx,lz,s.Rotation,out rx,out rz); int wx=s.Wx+rx,wz=s.Wz+rz;
            if(!InChunk(wx,wz,x0,z0))return;
            for(int k=0;k<count;k++)
            {
                int y=s.BaseY+startOffset-k;if(y<=MinY)return;int i=Idx(wx-x0,y,wz-z0);BlockId cur=(BlockId)a[i];
                if(cur!=BlockId.Air&&cur!=BlockId.Water)return;a[i]=(ushort)id;if(meta!=null)meta[i]=0;
            }
        }

        public void ApplySurfaceStructures(ushort[] blocks,byte[] meta,int x0,int z0,int chunkX,int chunkZ)
        {
            // Source c8()/D2(): wells first, then pyramid -> outpost -> portal -> ship.
            for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
            {
                SurfaceSite well=DesertWell(chunkX+dx,chunkZ+dz); if(well!=null)BuildDesertWell(blocks,meta,x0,z0,well);
            }
            var sites=SitesNearChunk(SurfaceKind.Pyramid,chunkX,chunkZ);for(int i=0;i<sites.Count;i++)BuildPyramid(blocks,meta,x0,z0,sites[i]);
            sites=SitesNearChunk(SurfaceKind.Outpost,chunkX,chunkZ);for(int i=0;i<sites.Count;i++)BuildOutpost(blocks,meta,x0,z0,sites[i]);
            sites=SitesNearChunk(SurfaceKind.Portal,chunkX,chunkZ);for(int i=0;i<sites.Count;i++)BuildPortal(blocks,meta,x0,z0,sites[i]);
            sites=SitesNearChunk(SurfaceKind.Ship,chunkX,chunkZ);for(int i=0;i<sites.Count;i++)BuildShipwreck(blocks,meta,x0,z0,sites[i]);
        }

        static void BuildShipwreck(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s)
        {
            var t=MainShipwreckTemplates.All[s.Variant];
            var blocks=t.Blocks; var pal=t.PaletteData;
            for(int i=0;i<blocks.Length;i++)
            {
                uint q=blocks[i];
                int x=(int)(q&15), y=(int)((q>>4)&31), z=(int)((q>>9)&31), pi=(int)((q>>14)&31);
                var p=pal[pi]; byte mm=p.Meta;
                if(p.RotateMode==1)mm=RotateDir(mm,s.Rotation);
                else if(p.RotateMode==2&&(s.Rotation&1)!=0&&(mm&3)!=0)mm=RotateAxis(mm,s.Rotation);
                SetLocal(a,m,x0,z0,s,x-t.Axo,y,z-t.Azo,p.Block,mm);
            }
            // Source c8()/w4(): only submerged upright/beachable wrecks receive a helm.
            if(!s.Beached&&t.Beachable)
                SetLocal(a,m,x0,z0,s,t.WheelX-t.Axo,t.WheelY,t.WheelZ-t.Azo,BlockId.ShipWheel,RotateDir(2,s.Rotation));
        }

        SurfaceSite DesertWell(int cx,int cz)
        {
            if(Hash2(cx*13+917.3,cz*13-411.7)>=.001)return null;
            int wx=cx*S+4+(int)Math.Floor(Hash2(cx*5+77,cz*5-33)*8.0);
            int wz=cz*S+4+(int)Math.Floor(Hash2(cx*5-51,cz*5+29)*8.0);
            if(biomes.Sample(wx,wz).Biome!=BiomeId.Desert)return null;
            int y=biomes.DensitySurfaceY(wx,wz); if(y<=Sea+1||y+6>=320)return null;
            return new SurfaceSite{Exists=true,Wx=wx,Wz=wz,BaseY=y,Rotation=0,ChunkX=cx,ChunkZ=cz};
        }
        static void BuildDesertWell(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s)
        {
            for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
            {
                SetLocal(a,m,x0,z0,s,x,0,z,BlockId.Sandstone);FoundationDown(a,m,x0,z0,s,x,z,BlockId.Sandstone,4);
                int edge=Math.Max(Math.Abs(x),Math.Abs(z));
                SetLocal(a,m,x0,z0,s,x,1,z,edge==2?BlockId.SandstoneSlab:edge==1?BlockId.Sandstone:BlockId.Air);
            }
            SetLocal(a,m,x0,z0,s,0,0,0,BlockId.Water);SetLocal(a,m,x0,z0,s,0,-1,0,BlockId.Water);SetLocal(a,m,x0,z0,s,0,-2,0,BlockId.Sandstone);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2){SetLocal(a,m,x0,z0,s,x,2,z,BlockId.Sandstone);SetLocal(a,m,x0,z0,s,x,3,z,BlockId.Sandstone);}
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)SetLocal(a,m,x0,z0,s,x,4,z,BlockId.SandstoneSlab);
        }

        static void PyramidBox(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s,int xa,int ya,int za,int xb,int yb,int zb,BlockId shell,BlockId inside)
        {
            for(int x=xa;x<=xb;x++)for(int y=ya;y<=yb;y++)for(int z=za;z<=zb;z++)
            {
                bool edge=x==xa||x==xb||y==ya||y==yb||z==za||z==zb;
                SetLocal(a,m,x0,z0,s,x-10,y,z-10,edge?shell:inside);
            }
        }
        static void BuildPyramid(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s)
        {
            BlockId sand=BlockId.Sandstone,cut=BlockId.CutSandstone,chis=BlockId.ChiseledSandstone,orange=BlockId.OrangeTerracotta,air=BlockId.Air,stairs=BlockId.SandstoneStairs;
            byte n=RotateDir(2,s.Rotation),south=RotateDir(0,s.Rotation),west=RotateDir(3,s.Rotation),east=RotateDir(1,s.Rotation);
            Action<int,int,int,BlockId,byte> P=(x,y,z,b,mm)=>SetLocal(a,m,x0,z0,s,x-10,y,z-10,b,mm);
            Action<int,int,int,BlockId> P0=(x,y,z,b)=>SetLocal(a,m,x0,z0,s,x-10,y,z-10,b);
            PyramidBox(a,m,x0,z0,s,0,-4,0,20,0,20,sand,sand);
            for(int level=1;level<=9;level++)
            {
                PyramidBox(a,m,x0,z0,s,level,level,level,20-level,level,20-level,sand,sand);
                PyramidBox(a,m,x0,z0,s,level+1,level,level+1,19-level,level,19-level,air,air);
            }
            for(int x=0;x<=20;x++)for(int z=0;z<=20;z++)FoundationDown(a,m,x0,z0,s,x-10,z-10,sand,12,-5);
            PyramidBox(a,m,x0,z0,s,0,0,0,4,9,4,sand,air);PyramidBox(a,m,x0,z0,s,1,10,1,3,10,3,sand,sand);
            P(2,10,0,stairs,n);P(2,10,4,stairs,south);P(0,10,2,stairs,west);P(4,10,2,stairs,east);
            PyramidBox(a,m,x0,z0,s,16,0,0,20,9,4,sand,air);PyramidBox(a,m,x0,z0,s,17,10,1,19,10,3,sand,sand);
            P(18,10,0,stairs,n);P(18,10,4,stairs,south);P(16,10,2,stairs,west);P(20,10,2,stairs,east);
            PyramidBox(a,m,x0,z0,s,8,0,0,12,4,4,sand,air);PyramidBox(a,m,x0,z0,s,9,1,0,11,3,4,air,air);
            P0(9,1,1,cut);P0(9,2,1,cut);P0(9,3,1,cut);P0(10,3,1,cut);P0(11,3,1,cut);P0(11,2,1,cut);P0(11,1,1,cut);
            PyramidBox(a,m,x0,z0,s,4,1,1,8,3,3,sand,air);PyramidBox(a,m,x0,z0,s,4,1,2,8,2,2,air,air);
            PyramidBox(a,m,x0,z0,s,12,1,1,16,3,3,sand,air);PyramidBox(a,m,x0,z0,s,12,1,2,16,2,2,air,air);
            PyramidBox(a,m,x0,z0,s,5,4,5,15,4,15,sand,sand);PyramidBox(a,m,x0,z0,s,9,4,9,11,4,11,air,air);
            PyramidBox(a,m,x0,z0,s,8,1,8,8,3,8,cut,cut);PyramidBox(a,m,x0,z0,s,12,1,8,12,3,8,cut,cut);
            PyramidBox(a,m,x0,z0,s,8,1,12,8,3,12,cut,cut);PyramidBox(a,m,x0,z0,s,12,1,12,12,3,12,cut,cut);
            PyramidBox(a,m,x0,z0,s,1,1,5,4,4,11,sand,sand);PyramidBox(a,m,x0,z0,s,16,1,5,19,4,11,sand,sand);
            PyramidBox(a,m,x0,z0,s,6,7,9,6,7,11,sand,sand);PyramidBox(a,m,x0,z0,s,14,7,9,14,7,11,sand,sand);
            PyramidBox(a,m,x0,z0,s,5,5,9,5,7,11,cut,cut);PyramidBox(a,m,x0,z0,s,15,5,9,15,7,11,cut,cut);
            P0(5,5,10,air);P0(5,6,10,air);P0(6,6,10,air);P0(15,5,10,air);P0(15,6,10,air);P0(14,6,10,air);
            PyramidBox(a,m,x0,z0,s,2,4,4,2,6,4,air,air);PyramidBox(a,m,x0,z0,s,18,4,4,18,6,4,air,air);
            P(2,4,5,stairs,n);P(2,3,4,stairs,n);P(18,4,5,stairs,n);P(18,3,4,stairs,n);
            PyramidBox(a,m,x0,z0,s,1,1,3,2,2,3,sand,sand);PyramidBox(a,m,x0,z0,s,18,1,3,19,2,3,sand,sand);
            P0(1,1,2,sand);P0(19,1,2,sand);P0(1,2,2,BlockId.SandstoneSlab);P0(19,2,2,BlockId.SandstoneSlab);P(2,1,2,stairs,east);P(18,1,2,stairs,west);
            PyramidBox(a,m,x0,z0,s,4,3,5,4,3,17,sand,sand);PyramidBox(a,m,x0,z0,s,16,3,5,16,3,17,sand,sand);
            PyramidBox(a,m,x0,z0,s,3,1,5,4,2,16,air,air);PyramidBox(a,m,x0,z0,s,15,1,5,16,2,16,air,air);
            for(int z=5;z<=17;z+=2){P0(4,1,z,cut);P0(4,2,z,chis);P0(16,1,z,cut);P0(16,2,z,chis);}
            P0(10,0,7,orange);P0(10,0,8,orange);P0(9,0,9,orange);P0(11,0,9,orange);P0(8,0,10,orange);P0(12,0,10,orange);P0(7,0,10,orange);P0(13,0,10,orange);P0(9,0,11,orange);P0(11,0,11,orange);P0(10,0,12,orange);P0(10,0,13,orange);P0(10,0,10,BlockId.BlueTerracotta);
            for(int x=0;x<=20;x+=20)
            {
                P0(x,2,1,cut);P0(x,2,2,orange);P0(x,2,3,cut);P0(x,3,1,cut);P0(x,3,2,orange);P0(x,3,3,cut);P0(x,4,1,orange);P0(x,4,2,chis);P0(x,4,3,orange);P0(x,5,1,cut);P0(x,5,2,orange);P0(x,5,3,cut);P0(x,6,1,orange);P0(x,6,2,chis);P0(x,6,3,orange);P0(x,7,1,orange);P0(x,7,2,orange);P0(x,7,3,orange);P0(x,8,1,cut);P0(x,8,2,cut);P0(x,8,3,cut);
            }
            for(int x=2;x<=18;x+=16)
            {
                P0(x-1,2,0,cut);P0(x,2,0,orange);P0(x+1,2,0,cut);P0(x-1,3,0,cut);P0(x,3,0,orange);P0(x+1,3,0,cut);P0(x-1,4,0,orange);P0(x,4,0,chis);P0(x+1,4,0,orange);P0(x-1,5,0,cut);P0(x,5,0,orange);P0(x+1,5,0,cut);P0(x-1,6,0,orange);P0(x,6,0,chis);P0(x+1,6,0,orange);P0(x-1,7,0,orange);P0(x,7,0,orange);P0(x+1,7,0,orange);P0(x-1,8,0,cut);P0(x,8,0,cut);P0(x+1,8,0,cut);
            }
            PyramidBox(a,m,x0,z0,s,8,4,0,12,6,0,cut,cut);P0(8,6,0,air);P0(12,6,0,air);P0(9,5,0,orange);P0(10,5,0,chis);P0(11,5,0,orange);
            PyramidBox(a,m,x0,z0,s,8,-14,8,12,-11,12,cut,cut);PyramidBox(a,m,x0,z0,s,8,-10,8,12,-10,12,chis,chis);PyramidBox(a,m,x0,z0,s,8,-9,8,12,-9,12,cut,cut);PyramidBox(a,m,x0,z0,s,8,-8,8,12,-1,12,sand,sand);PyramidBox(a,m,x0,z0,s,9,-11,9,11,-1,11,air,air);
            P0(10,-11,10,BlockId.StonePressurePlate);PyramidBox(a,m,x0,z0,s,9,-13,9,11,-13,11,BlockId.Tnt,air);
            P0(8,-11,10,air);P0(8,-10,10,air);P0(7,-10,10,chis);P0(7,-11,10,cut);P0(12,-11,10,air);P0(12,-10,10,air);P0(13,-10,10,chis);P0(13,-11,10,cut);
            P0(10,-11,8,air);P0(10,-10,8,air);P0(10,-10,7,chis);P0(10,-11,7,cut);P0(10,-11,12,air);P0(10,-10,12,air);P0(10,-10,13,chis);P0(10,-11,13,cut);
            P(12,-11,10,BlockId.Chest,RotateDir(1,s.Rotation));P(8,-11,10,BlockId.Chest,RotateDir(3,s.Rotation));P(10,-11,12,BlockId.Chest,RotateDir(2,s.Rotation));P(10,-11,8,BlockId.Chest,RotateDir(0,s.Rotation));
        }

        static void BuildOutpost(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s)
        {
            for(int x=0;x<15;x++)for(int z=0;z<15;z++)for(int y=1;y<21;y++)SetLocal(a,m,x0,z0,s,x-7,y,z-7,BlockId.Air);
            uint[] data=MainStructureTemplates.Outpost;
            for(int i=0;i<data.Length;i++)
            {
                uint v=data[i];int x=(int)(v&15),y=(int)((v>>4)&31),z=(int)((v>>9)&15),pi=(int)((v>>13)&31);PaletteEntry p=OutpostPalette[pi];byte meta=p.Meta;
                if(p.RotateMode==1)meta=RotateDir(meta,s.Rotation);else if(p.RotateMode==2)meta=RotateAxis(meta,s.Rotation);
                SetLocal(a,m,x0,z0,s,x-7,y,z-7,p.Block,meta);
                if(y==0)FoundationDown(a,m,x0,z0,s,x-7,z-7,BlockId.Cobblestone,8);
            }
        }

        void BuildPortal(ushort[] a,byte[] m,int x0,int z0,SurfaceSite s)
        {
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)
            {
                double q=Hash3(s.Wx+x*7,11,s.Wz+z*7);if(Math.Abs(x)+Math.Abs(z)>4&&q>.5)continue;
                SetLocal(a,m,x0,z0,s,x,0,z,q<.15?BlockId.MagmaBlock:BlockId.Netherrack);FoundationDown(a,m,x0,z0,s,x,z,BlockId.Netherrack,3);
                for(int y=1;y<=6;y++)SetLocal(a,m,x0,z0,s,x,y,z,BlockId.Air);
            }
            for(int x=-2;x<=1;x++)for(int y=1;y<=5;y++)
            {
                bool frame=x==-2||x==1||y==1||y==5;if(!frame){SetLocal(a,m,x0,z0,s,x,y,0,BlockId.Air);continue;}
                bool present=y<=2||Hash3(s.Wx+x*13,y*5,s.Wz-x*3)<.6;if(!present)continue;
                bool crying=Hash3(s.Wx-x*5,y*9+4,s.Wz+x*11)<.25;SetLocal(a,m,x0,z0,s,x,y,0,crying?BlockId.CryingObsidian:BlockId.Obsidian);
            }
            SetLocal(a,m,x0,z0,s,-3,1,1,BlockId.GoldBlock);if(Hash3(s.Wx,3,s.Wz)<.5)SetLocal(a,m,x0,z0,s,2,1,-2,BlockId.GoldBlock);
            SetLocal(a,m,x0,z0,s,2,1,2,BlockId.Chest,RotateDir(0,s.Rotation));
        }

        // -------------------- deepslate mineshafts: genWorker IQ()/i4() --------------------
        Rng MineRng(int cx,int cz)
        {
            unchecked
            {
                int st=(cx+seed)*668265263 ^ (cz-seed)*374761393 ^ (int)2654435769u;
                return new Rng(st);
            }
        }
        double MineHash(int x,int y,int z)
        {
            unchecked
            {
                int q=(x+seed)*(int)2246822507u ^ (y-(int)1640531527u)*(int)3266489909u ^ (z+(int)668265263u)*(int)2654435761u;
                q^=(int)((uint)q>>15);q*=739982445;q^=(int)((uint)q>>13);return ((uint)q%100000)/100000.0;
            }
        }
        static void ExpandBounds(MineFeature f,int x,int z){if(x<f.MinX)f.MinX=x;if(x>f.MaxX)f.MaxX=x;if(z<f.MinZ)f.MinZ=z;if(z>f.MaxZ)f.MaxZ=z;}
        bool MineWithinOrigin(int x,int z,int cx,int cz)=>Math.Abs(FloorDiv(x,16)-cx)<=MineOriginRadius&&Math.Abs(FloorDiv(z,16)-cz)<=MineOriginRadius;
        bool MineFits(int x0,int z0,int x1,int z1,int topY,int surfaceBias)
        {
            int need=topY+surfaceBias;
            return biomes.Sample(x0,z0).Height>=need&&biomes.Sample(x1,z1).Height>=need&&biomes.Sample(x0,z1).Height>=need&&biomes.Sample(x1,z0).Height>=need&&biomes.Sample((x0+x1)>>1,(z0+z1)>>1).Height>=need;
        }
        MineFeature BuildMine(int cx,int cz)
        {
            Rng rng=MineRng(cx,cz);if(rng.Next()>=1.0/260.0)return NoMine;
            int x=cx*16+4+(int)Math.Floor(rng.Next()*8.0),z=cz*16+4+(int)Math.Floor(rng.Next()*8.0),surface=biomes.Sample(x,z).Height;
            bool nearSurface=rng.Next()<.22&&surface>Sea+5;int y;
            if(nearSurface)y=surface-6;
            else
            {
                int low=MinY+16,high=Sea-14;y=low+(int)Math.Floor(rng.Next()*(high-low));
                if(surface<y+12||biomes.Sample(x-4,z-4).Height<y+8||biomes.Sample(x+4,z+4).Height<y+8||biomes.Sample(x-4,z+4).Height<y+8||biomes.Sample(x+4,z-4).Height<y+8)return NoMine;
            }
            var f=new MineFeature{Exists=true,MinX=x,MaxX=x,MinZ=z,MaxZ=z};
            f.Pieces.Add(new MinePiece{Kind=MinePieceKind.Room,Cx=x,Cz=z,Y=y});ExpandBounds(f,x-3,z-3);ExpandBounds(f,x+3,z+3);
            var q=new List<MineBranch>(64);int[,] dirs={{0,1},{0,-1},{1,1},{1,-1}};
            for(int i=0;i<4;i++)if(rng.Next()<.82)
            {
                int ax=dirs[i,0],dir=dirs[i,1];q.Add(new MineBranch(x+AxisX(ax,dir)*4,z+AxisZ(ax,dir)*4,y,ax,dir));
            }
            int qi=0,loops=0,surfaceBias=nearSurface?-3:2;
            while(qi<q.Count&&f.Pieces.Count<MineMaxPieces&&loops++<500)
            {
                MineBranch b=q[qi++];if(!MineWithinOrigin(b.X,b.Z,cx,cz))continue;int dx=AxisX(b.Axis,b.Dir),dz=AxisZ(b.Axis,b.Dir);
                int len=5*(2+(int)Math.Floor(rng.Next()*3.0));int endX=b.X+dx*(len-1),endZ=b.Z+dz*(len-1);
                if(!MineWithinOrigin(endX,endZ,cx,cz)){len=Math.Max(6,len-5);endX=b.X+dx*(len-1);endZ=b.Z+dz*(len-1);}
                int minx=Math.Min(b.X,endX)-1,minz=Math.Min(b.Z,endZ)-1,maxx=Math.Max(b.X,endX)+1,maxz=Math.Max(b.Z,endZ)+1;
                if(!MineFits(minx,minz,maxx,maxz,b.Y+3,surfaceBias))continue;
                var p=new MinePiece{Kind=MinePieceKind.Corridor,Axis=b.Axis,Dir=b.Dir,Sx=b.X,Sz=b.Z,Sy=b.Y,Len=len,Rails=rng.Next()<.34,Webs=rng.Next()<.2,ChestI=-1};
                if(rng.Next()<.14)p.ChestI=2+(int)Math.Floor(rng.Next()*(len-4));
                f.Pieces.Add(p);ExpandBounds(f,b.X-1,b.Z-1);ExpandBounds(f,b.X+1,b.Z+1);ExpandBounds(f,endX-1,endZ-1);ExpandBounds(f,endX+1,endZ+1);
                int nextX=endX+dx,nextZ=endZ+dz;double eventRoll=rng.Next();if(eventRoll<.3)continue;
                if(eventRoll<.64)
                {
                    f.Pieces.Add(new MinePiece{Kind=MinePieceKind.Crossing,Cx=endX,Cz=endZ,Y=b.Y});ExpandBounds(f,endX-1,endZ-1);ExpandBounds(f,endX+1,endZ+1);
                    if(rng.Next()<.85)q.Add(new MineBranch(nextX,nextZ,b.Y,b.Axis,b.Dir));int sideAxis=b.Axis^1;
                    if(rng.Next()<.6)q.Add(new MineBranch(endX+AxisX(sideAxis,1)*2,endZ+AxisZ(sideAxis,1)*2,b.Y,sideAxis,1));
                    if(rng.Next()<.6)q.Add(new MineBranch(endX+AxisX(sideAxis,-1)*2,endZ+AxisZ(sideAxis,-1)*2,b.Y,sideAxis,-1));
                }
                else if(eventRoll<.82)
                {
                    int yd=rng.Next()<.5?1:-1,targetY=b.Y+yd*5;
                    if(targetY>=MinY+12&&targetY<=Sea+30)
                    {
                        f.Pieces.Add(new MinePiece{Kind=MinePieceKind.Slope,Axis=b.Axis,Dir=b.Dir,Sx=nextX,Sz=nextZ,Sy=b.Y,YDelta=yd,Len=5});
                        ExpandBounds(f,nextX+dx*4-1,nextZ+dz*4-1);ExpandBounds(f,nextX+dx*4+1,nextZ+dz*4+1);
                        q.Add(new MineBranch(nextX+dx*5,nextZ+dz*5,targetY,b.Axis,b.Dir));
                    }
                    else q.Add(new MineBranch(nextX,nextZ,b.Y,b.Axis,b.Dir));
                }
                else q.Add(new MineBranch(nextX,nextZ,b.Y,b.Axis,b.Dir));
            }
            return f;
        }
        MineFeature GetMine(int cx,int cz)
        {
            long k=Key(cx,cz);MineFeature f;if(mineCache.TryGetValue(k,out f))return f.Exists?f:null;
            if(mineCache.Count>20000)mineCache.Clear();f=BuildMine(cx,cz);f=mineCache.GetOrAdd(k,f);return f.Exists?f:null;
        }

        static bool Solid(BlockId id)=>id!=BlockId.Air&&id!=BlockId.Water;
        static void MineRemove(ushort[] a,int x0,int z0,int wx,int y,int wz)
        {
            if(!InChunk(wx,wz,x0,z0)||y<=MinY+1||y>MaxY)return;int i=Idx(wx-x0,y,wz-z0);BlockId b=(BlockId)a[i];if(b!=BlockId.Bedrock&&b!=BlockId.Air&&b!=BlockId.Water)a[i]=(ushort)BlockId.Air;
        }
        static void MineSet(ushort[] a,int x0,int z0,int wx,int y,int wz,BlockId id,bool onlyAir)
        {
            if(!InChunk(wx,wz,x0,z0)||y<=MinY||y>MaxY)return;int i=Idx(wx-x0,y,wz-z0);if(onlyAir&&(BlockId)a[i]!=BlockId.Air)return;a[i]=(ushort)id;
        }
        static BlockId MineGet(ushort[] a,int x0,int z0,int wx,int y,int wz)
        {
            if(!InChunk(wx,wz,x0,z0)||y<MinY||y>MaxY)return BlockId.Air;return (BlockId)a[Idx(wx-x0,y,wz-z0)];
        }
        static void MineFence(ushort[] a,int x0,int z0,int wx,int y0,int y1,int wz)
        {
            for(int y=y0;y<=y1;y++)MineSet(a,x0,z0,wx,y,wz,BlockId.OakFence,true);
            for(int d=1;d<=6;d++)
            {
                int y=y0-d;if(y<=MinY+1||!InChunk(wx,wz,x0,z0))break;BlockId b=MineGet(a,x0,z0,wx,y,wz);if(Solid(b))break;MineSet(a,x0,z0,wx,y,wz,BlockId.OakFence,false);
            }
        }
        void BuildMineRoom(ushort[] a,int x0,int z0,MinePiece p)
        {
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)
            {
                int wx=p.Cx+x,wz=p.Cz+z;for(int y=p.Y+1;y<=p.Y+3;y++)MineRemove(a,x0,z0,wx,y,wz);
                if(InChunk(wx,wz,x0,z0)){BlockId floor=MineGet(a,x0,z0,wx,p.Y,wz);if(floor==BlockId.Air||floor==BlockId.Water||floor==BlockId.Stone)MineSet(a,x0,z0,wx,p.Y,wz,BlockId.Dirt,false);}
            }
            MineFence(a,x0,z0,p.Cx-2,p.Y+1,p.Y+3,p.Cz-2);MineFence(a,x0,z0,p.Cx+2,p.Y+1,p.Y+3,p.Cz+2);MineSet(a,x0,z0,p.Cx,p.Y+1,p.Cz,BlockId.Chest,true);
        }
        void BuildMineCorridor(ushort[] a,int x0,int z0,MinePiece p)
        {
            int dx=AxisX(p.Axis,p.Dir),dz=AxisZ(p.Axis,p.Dir),y=p.Sy;
            for(int step=0;step<p.Len;step++)
            {
                int cx=p.Sx+dx*step,cz=p.Sz+dz*step;
                for(int side=-1;side<=1;side++)
                {
                    int wx=cx+(p.Axis==1?side:0),wz=cz+(p.Axis==0?side:0);MineRemove(a,x0,z0,wx,y+1,wz);MineRemove(a,x0,z0,wx,y+2,wz);MineRemove(a,x0,z0,wx,y+3,wz);
                    if(InChunk(wx,wz,x0,z0)){BlockId floor=MineGet(a,x0,z0,wx,y,wz);if((floor==BlockId.Air||floor==BlockId.Water)&&MineHash(wx,y,wz)<.72)MineSet(a,x0,z0,wx,y,wz,BlockId.OakPlanks,false);}
                }
                if(step%5==0&&MineHash(p.Sx*7+step,y,p.Sz*7)>.16)
                {
                    int ax=cx+(p.Axis==1?-1:0),az=cz+(p.Axis==0?-1:0),bx=cx+(p.Axis==1?1:0),bz=cz+(p.Axis==0?1:0);
                    MineFence(a,x0,z0,ax,y+1,y+2,az);MineFence(a,x0,z0,bx,y+1,y+2,bz);
                    for(int side=-1;side<=1;side++)MineSet(a,x0,z0,cx+(p.Axis==1?side:0),y+3,cz+(p.Axis==0?side:0),BlockId.OakPlanks,true);
                }
                if(p.Rails&&InChunk(cx,cz,x0,z0)&&Solid(MineGet(a,x0,z0,cx,y,cz))&&MineGet(a,x0,z0,cx,y+1,cz)==BlockId.Air)MineSet(a,x0,z0,cx,y+1,cz,BlockId.Rail,true);
                if(p.Webs)for(int side=-1;side<=1;side++)for(int yy=y+1;yy<=y+2;yy++)
                {
                    int wx=cx+(p.Axis==1?side:0),wz=cz+(p.Axis==0?side:0);if(MineHash(wx,yy,wz)<.03)MineSet(a,x0,z0,wx,yy,wz,BlockId.Cobweb,true);
                }
                if(step==p.ChestI&&InChunk(cx,cz,x0,z0)&&Solid(MineGet(a,x0,z0,cx,y,cz)))MineSet(a,x0,z0,cx,y+1,cz,BlockId.Chest,true);
            }
        }
        static void BuildMineCrossing(ushort[] a,int x0,int z0,MinePiece p)
        {
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {
                int wx=p.Cx+x,wz=p.Cz+z;MineRemove(a,x0,z0,wx,p.Y+1,wz);MineRemove(a,x0,z0,wx,p.Y+2,wz);MineRemove(a,x0,z0,wx,p.Y+3,wz);
                if(InChunk(wx,wz,x0,z0)){BlockId floor=MineGet(a,x0,z0,wx,p.Y,wz);if(floor==BlockId.Air||floor==BlockId.Water)MineSet(a,x0,z0,wx,p.Y,wz,BlockId.OakPlanks,false);}
                MineSet(a,x0,z0,wx,p.Y+3,wz,BlockId.OakPlanks,true);if(Math.Abs(x)==1&&Math.Abs(z)==1)MineFence(a,x0,z0,wx,p.Y+1,p.Y+2,wz);
            }
        }
        static void BuildMineSlope(ushort[] a,int x0,int z0,MinePiece p)
        {
            int dx=AxisX(p.Axis,p.Dir),dz=AxisZ(p.Axis,p.Dir);
            for(int step=0;step<p.Len;step++)
            {
                int cx=p.Sx+dx*step,cz=p.Sz+dz*step,y=p.Sy+p.YDelta*step;
                for(int side=-1;side<=1;side++)
                {
                    int wx=cx+(p.Axis==1?side:0),wz=cz+(p.Axis==0?side:0);MineRemove(a,x0,z0,wx,y+1,wz);MineRemove(a,x0,z0,wx,y+2,wz);MineRemove(a,x0,z0,wx,y+3,wz);MineRemove(a,x0,z0,wx,y+4,wz);MineSet(a,x0,z0,wx,y,wz,BlockId.OakPlanks,false);
                }
            }
        }

        public void ApplyMineshafts(ushort[] blocks,int x0,int z0,int chunkX,int chunkZ)
        {
            int maxX=x0+S+2,maxZ=z0+S+2;
            for(int ox=chunkX-MineSearchRadius;ox<=chunkX+MineSearchRadius;ox++)for(int oz=chunkZ-MineSearchRadius;oz<=chunkZ+MineSearchRadius;oz++)
            {
                MineFeature f=GetMine(ox,oz);if(f==null||f.MaxX<x0-2||f.MinX>maxX||f.MaxZ<z0-2||f.MinZ>maxZ)continue;
                for(int i=0;i<f.Pieces.Count;i++)
                {
                    MinePiece p=f.Pieces[i];if(p.Kind==MinePieceKind.Room)BuildMineRoom(blocks,x0,z0,p);else if(p.Kind==MinePieceKind.Corridor)BuildMineCorridor(blocks,x0,z0,p);else if(p.Kind==MinePieceKind.Crossing)BuildMineCrossing(blocks,x0,z0,p);else BuildMineSlope(blocks,x0,z0,p);
                }
            }
        }
    }
}
