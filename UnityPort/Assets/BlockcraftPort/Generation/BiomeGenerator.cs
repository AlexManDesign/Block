using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BlockcraftPort
{
    /// <summary>
    /// Direct C# port of genWorker iA()/pQ() for the deepslate generator.
    /// This is intentionally formula-for-formula rather than a biome approximation.
    /// </summary>
    public sealed class BiomeGenerator
    {
        readonly MainTerrainNoise noise;
        readonly ConcurrentDictionary<long, BiomeSample> cache = new ConcurrentDictionary<long, BiomeSample>();

        // Source iA() is called repeatedly for the same X/Z while T1() walks the vertical
        // density lattice. ConcurrentDictionary is useful as the long-lived shared cache, but
        // paying its synchronization/hash cost for every Y sample is unnecessary. A tiny
        // thread-local direct-mapped front cache keeps the result bit-identical while making
        // the hot path allocation-free and lock-free. Owner prevents cross-world seed reuse.
        const int HotCacheSize = 2048;
        const int HotCacheMask = HotCacheSize - 1;
        static int nextOwner;
        readonly int cacheOwner;
        struct HotEntry
        {
            public long Key;
            public int Owner;
            public BiomeSample Sample;
            public bool Valid;
        }
        [ThreadStatic] static HotEntry[] hotCache;

        static readonly double[,] HeightCurve =
        {
            {-1d,5d},{-.6d,12d},{-.42d,20d},{-.3d,30d},{-.2d,40d},{-.1d,48d},
            {0d,55d},{.12d,61d},{.24d,64d},{.4d,71d},{.6d,84d},{1d,104d}
        };
        static readonly double[,] ErosionCurve =
        {
            {-1d,1d},{-.58d,.88d},{-.35d,.62d},{-.18d,.36d},{.05d,.16d},{.4d,.06d},{1d,0d}
        };
        // genWorker Ew: T1() vertical density factor derived from erosion.
        static readonly double[,] FactorCurve =
        {
            {-1d,1.3d},{-.5d,2.2d},{-.2d,3.6d},{.05d,5d},{.45d,6.5d},{1d,7.5d}
        };
        static readonly BiomeId[] Oceans =
        {
            BiomeId.FrozenOcean, BiomeId.ColdOcean, BiomeId.Ocean, BiomeId.LukewarmOcean, BiomeId.WarmOcean
        };

        struct RiverOffset
        {
            public int X,Z;
            public double Weight;
            public RiverOffset(int x,int z,double w){X=x;Z=z;Weight=w;}
        }
        static readonly RiverOffset[] RiverOffsets = BuildRiverOffsets();

        public BiomeGenerator(int seed)
        {
            noise = new MainTerrainNoise(seed);
            cacheOwner = Interlocked.Increment(ref nextOwner);
        }
        static long Key(int x,int z) => ((long)x << 32) ^ (uint)z;
        static int HotIndex(long key)
        {
            unchecked
            {
                ulong x=(ulong)key;
                x^=x>>33; x*=0xff51afd7ed558ccdUL; x^=x>>33;
                return (int)x & HotCacheMask;
            }
        }
        void StoreHot(long key, BiomeSample sample)
        {
            var h=hotCache ?? (hotCache=new HotEntry[HotCacheSize]);
            int i=HotIndex(key); h[i].Key=key; h[i].Owner=cacheOwner; h[i].Sample=sample; h[i].Valid=true;
        }
        static double Clamp01(double x) => x < 0d ? 0d : x > 1d ? 1d : x;
        static double Ridge(double x) => 1d - Math.Abs(3d * Math.Abs(x) - 2d);

        static RiverOffset[] BuildRiverOffsets()
        {
            // genWorker v4: direction outer loop, radii 6/13/20 inner loop. Weight is based on
            // the original radius, before Math.round changes diagonal integer offsets.
            var a = new RiverOffset[24]; int k=0;
            for(int dir=0;dir<8;dir++)
            {
                double ang=dir*Math.PI/4.0;
                for(int radius=6;radius<=20;radius+=7)
                {
                    int ox=SourceMath.JsRound(Math.Cos(ang)*radius);
                    int oz=SourceMath.JsRound(Math.Sin(ang)*radius);
                    a[k++]=new RiverOffset(ox,oz,1d-(radius-6)/20d);
                }
            }
            return a;
        }

        static double Curve(double[,] c,double x)
        {
            if(x<=c[0,0]) return c[0,1];
            int n=c.GetLength(0);
            for(int i=1;i<n;i++) if(x<=c[i,0])
            {
                double t=(x-c[i-1,0])/(c[i,0]-c[i-1,0]);
                return c[i-1,1]+(c[i,1]-c[i-1,1])*t;
            }
            return c[n-1,1];
        }

        public BiomeSample Sample(int x,int z)
        {
            long key=Key(x,z);
            var h=hotCache ?? (hotCache=new HotEntry[HotCacheSize]);
            int hi=HotIndex(key);
            HotEntry e=h[hi];
            if(e.Valid && e.Owner==cacheOwner && e.Key==key) return e.Sample;
            if(cache.TryGetValue(key,out var cached))
            {
                StoreHot(key,cached);
                return cached;
            }
            BiomeSample s=Compute(x,z);
            if(cache.Count>400000) cache.Clear(); // same order-of-magnitude cap as genWorker UE.
            cache.TryAdd(key,s);
            StoreHot(key,s);
            return s;
        }

        BiomeSample Compute(int x,int z)
        {
            // genWorker iA() is Number/double all the way through its height and climate decisions.
            // Keep that precision until the final compact BiomeSample fields are written.
            double cont=noise.ContinentalExact(x,z);
            double erosion=noise.ErosionExact(x,z);
            double weird=noise.WeirdnessExact(x,z);
            double ridge=Ridge(weird);

            double baseHeight=Curve(HeightCurve,cont);
            double land=Clamp01((cont+.25d)/.4d);
            double mAmp=Curve(ErosionCurve,erosion)*land;
            double peak=Math.Pow(ridge*.5d+.5d,1.4d);
            double h=baseHeight+Math.Pow(mAmp,1.15d)*peak*235d;

            if(h>VoxelConstants.SeaLevel-3)
            {
                h+=noise.FineDetailExact(x,z)*(9d+mAmp*15d)*Clamp01((h-(VoxelConstants.SeaLevel-2))/11d);
            }
            else if(h<VoxelConstants.SeaLevel-4)
            {
                double c=Clamp01((VoxelConstants.SeaLevel-4-h)/22d);
                h+=((noise.ClassicValueExact(x*.018d+88.8d,z*.018d-44.4d)*2d-1d)*2.2d)*(2.5d+c*8d);
            }

            if(cont>-.2d)
            {
                double nearest=0d;
                for(int i=0;i<RiverOffsets.Length;i++)
                {
                    RiverOffset o=RiverOffsets[i];
                    if(Ridge(noise.WeirdnessExact(x+o.X,z+o.Z))<-.35d && o.Weight>nearest) nearest=o.Weight;
                }
                if(nearest>0d)
                {
                    double w=nearest*nearest*(3d-2d*nearest);
                    double target=VoxelConstants.SeaLevel+4;
                    if(h>target) h+=(target-h)*w*.85d;
                }
            }

            if(cont>-.2d && ridge<-.35d && h>VoxelConstants.SeaLevel)
            {
                double n=Clamp01((-.35d-ridge)/.35d); n=n*n*(3d-2d*n);
                double target=VoxelConstants.SeaLevel+3;
                if(h>target) h+=(target-h)*n*.9d;
                double deep=Clamp01((-.83d-ridge)/.17d); deep=deep*deep*(3d-2d*deep);
                if(deep>0d && h>VoxelConstants.SeaLevel-2) h+=(VoxelConstants.SeaLevel-2-h)*deep;
            }

            double mushroom=0d;
            if(cont<-.85d)
            {
                double n=noise.ValueExact(x*.0011d+912.3d,z*.0011d-377.1d);
                if(n>.9d)
                {
                    double a=(n-.9d)/.1d; if(a>1d)a=1d;
                    mushroom=a*a*(3d-2d*a);
                    double coast=Clamp01((-.85d-cont)/.12d);
                    mushroom*=coast*coast*(3d-2d*coast);
                }
            }
            if(mushroom>0d)
            {
                double island=VoxelConstants.SeaLevel-14+mushroom*26d+(noise.ClassicValueExact(x*.02d+51.5d,z*.02d-42.2d)-.5d)*6d;
                if(island>h) h=island;
            }

            bool invalidHeight=double.IsNaN(h);
            // Source can rarely leave h=NaN when the fractional peak power receives a negative
            // base. Keep a compact integer fallback for storage, but preserve the NaN semantics
            // separately so pQ()/T1()/S1() behave like JavaScript.
            int ih=invalidHeight?VoxelConstants.MinY+4:SourceMath.JsRound(h);
            if(!invalidHeight)
            {
                if(ih<VoxelConstants.MinY+4) ih=VoxelConstants.MinY+4;
                if(ih>VoxelConstants.MaxY-3) ih=VoxelConstants.MaxY-3;
            }

            double factor=Curve(FactorCurve,erosion);
            double warpA=noise.ValueExact(x*.008d+431.7d,z*.008d-271.3d);
            double warpB=noise.ValueExact(x*.008d-118.4d,z*.008d+542.9d);
            // deepslate BA=16 and PA=0: the .055 detail warp is dead arithmetic in source.
            double wx=x+(warpA-.5d)*16d;
            double wz=z+(warpB-.5d)*16d;
            double temp=noise.TemperatureExact(wx,wz);
            double hum=noise.HumidityExact(wx,wz);

            BiomeId biome=mushroom>.25d && ih>VoxelConstants.SeaLevel
                ? BiomeId.MushroomFields
                : Classify(ih,invalidHeight,cont,temp,hum,erosion,weird,x,z);

            return new BiomeSample(ih,biome,temp,hum,cont,erosion,weird,factor,mAmp,invalidHeight);
        }

        static int TempBucket(double x)=>x<-.45d?0:x<-.15d?1:x<.2d?2:x<.55d?3:4;

        BiomeId Classify(int height,bool invalidHeight,double cont,double temp,double hum,double erosion,double weird,int x,int z)
        {
            int tb=TempBucket(temp);
            if(!invalidHeight && height<=VoxelConstants.SeaLevel)
                return cont>-.11d && height>VoxelConstants.SeaLevel-5 && Math.Abs(weird)<.065d
                    ? (tb==0?BiomeId.FrozenRiver:BiomeId.River)
                    : Oceans[tb];

            if(!invalidHeight && height<=VoxelConstants.SeaLevel+2 && cont<-.04d)
            {
                if(tb==0) return BiomeId.SnowyBeach;
                if(tb==4) return BiomeId.Desert;
                if(erosion<-.18d) return BiomeId.StonyShore;
                if(noise.ValueExact(x*.011d+401.7d,z*.011d-233.1d)>.52d) return BiomeId.Beach;
            }

            if(!MainBiomeLookup.IsReady)
                throw new InvalidOperationException("Packed main biome table is required for exact deepslate biome generation.");
            return MainBiomeLookup.Lookup(temp,hum,cont,erosion,weird);
        }

        // T1(A,C,w): keep source double precision through the density zero-crossing.
        public double TerrainDensity(int x,int y,int z) => TerrainDensity(Sample(x,z),x,y,z);

        public double TerrainDensity(BiomeSample b,int x,int y,int z)
        {
            if(b.SourceHeightInvalid) return double.NaN;
            double density=(b.Height-y)/24d;
            double detail=noise.TerrainFbm3Exact(x*.016d,y*.011d,z*.016d)*2d-1d;
            double coast=1d;
            if(b.Height>VoxelConstants.SeaLevel-7 && b.Height<VoxelConstants.SeaLevel+9)
                coast=.35d+.65d*Clamp01(Math.Abs(b.Height-VoxelConstants.SeaLevel)/9d);
            return density*b.SourceFactor + detail*(.3d+b.SourceMountainAmplitude*.9d)*coast;
        }

        // S1(A,C): source top-solid scan of the density field, used by tree placement.
        public int DensitySurfaceY(int x,int z)
        {
            BiomeSample b=Sample(x,z);
            if(b.SourceHeightInvalid) return VoxelConstants.MinY;
            int max=b.Height+28; if(max>VoxelConstants.MaxY)max=VoxelConstants.MaxY;
            int x0=(int)Math.Floor(x/4.0)*4, z0=(int)Math.Floor(z/4.0)*4;
            double fx=(x-x0)/4d, fz=(z-z0)/4d;
            int cell=(int)Math.Floor((max-VoxelConstants.MinY)/8d);
            // The four lattice columns are invariant for the whole vertical scan. Source S1()
            // repeatedly reaches them through T1()->iA(); pre-sampling them avoids cache traffic
            // without changing any density arithmetic.
            BiomeSample b00=Sample(x0,z0), b10=Sample(x0+4,z0), b01=Sample(x0,z0+4), b11=Sample(x0+4,z0+4);
            double? prev=null;
            for(int u=cell;u>=0;u--)
            {
                int y0=VoxelConstants.MinY+u*8, y1=y0+8;
                double Eval(int yy)
                {
                    double a=TerrainDensity(b00,x0,yy,z0), bx=TerrainDensity(b10,x0+4,yy,z0);
                    double c=TerrainDensity(b01,x0,yy,z0+4), d=TerrainDensity(b11,x0+4,yy,z0+4);
                    double p=a+(bx-a)*fx, q=c+(d-c)*fx; return p+(q-p)*fz;
                }
                double high=prev ?? Eval(y1), low=Eval(y0); prev=low;
                if(low<=0d && high<=0d) continue;
                for(int dy=7;dy>=0;dy--)
                {
                    int yy=y0+dy; if(yy>max)continue;
                    double v=low+(high-low)*(dy/8d); if(v>0d)return yy;
                }
            }
            return VoxelConstants.MinY;
        }
    }
}
