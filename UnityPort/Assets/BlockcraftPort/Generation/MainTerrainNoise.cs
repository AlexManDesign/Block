using System;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-aligned noise family from main/genWorker: six seeded Perlin fields plus the value-noise detail field.
    /// Keeping these scales separate is what gives main its broad continents, erosion bands and peak chains.
    /// </summary>
    public sealed class MainTerrainNoise
    {
        readonly int seed;
        readonly byte[] continental, erosion, weirdness, detail, temperature, humidity;
        static readonly int[,] Grad = { {1,1},{-1,1},{1,-1},{-1,-1},{1,0},{-1,0},{0,1},{0,-1} };
        static readonly float[] ClassicRx={1f,.866f,.5f,0f};
        static readonly float[] ClassicRz={0f,.5f,.866f,1f};
        static readonly double[] ClassicRxExact={1d,.866d,.5d,0d};
        static readonly double[] ClassicRzExact={0d,.5d,.866d,1d};

        public MainTerrainNoise(int seed)
        {
            this.seed = seed;
            continental = Perm(seed, 1); erosion = Perm(seed, 2); weirdness = Perm(seed, 3);
            detail = Perm(seed, 4); temperature = Perm(seed, 5); humidity = Perm(seed, 6);
        }

        static int Imul(int a, int b) { unchecked { return a * b; } }
        static uint UR(int v, int bits) { return ((uint)v) >> bits; }

        sealed class Rng
        {
            int s;
            public Rng(int seed) { s = seed; }
            public double Next()
            {
                unchecked
                {
                    s += 1831565813;
                    int e = Imul(s ^ (int)UR(s, 15), 1 | s);
                    e = (e + Imul(e ^ (int)UR(e, 7), 61 | e)) ^ e;
                    return (uint)(e ^ (int)UR(e, 14)) / 4294967296.0;
                }
            }
        }

        static byte[] Perm(int seed, int channel)
        {
            int mixed = seed ^ Imul(channel, unchecked((int)2654435761u));
            var r = new Rng(mixed); var p = new int[256]; for (int i=0;i<256;i++) p[i]=i;
            for (int i=255;i>0;i--) { int j=(int)Math.Floor(r.Next()*(i+1)); int t=p[i];p[i]=p[j];p[j]=t; }
            var d = new byte[512]; for (int i=0;i<512;i++) d[i]=(byte)p[i&255]; return d;
        }

        static double Fade(double x) { return x*x*x*(x*(x*6.0-15.0)+10.0); }
        static double Lerp(double a,double b,double t) { return a+(b-a)*t; }
        static double Perlin(byte[] p,double x,double z)
        {
            int ix=(int)Math.Floor(x), iz=(int)Math.Floor(z); double fx=x-ix, fz=z-iz;
            int X=ix&255,Z=iz&255; double u=Fade(fx),v=Fade(fz);
            double Dot(int h,double dx,double dz) { int g=h&7; return Grad[g,0]*dx+Grad[g,1]*dz; }
            int aa=p[p[X]+Z], ab=p[p[X]+Z+1], ba=p[p[X+1]+Z], bb=p[p[X+1]+Z+1];
            return Lerp(Lerp(Dot(aa,fx,fz),Dot(ba,fx-1,fz),u),Lerp(Dot(ab,fx,fz-1),Dot(bb,fx-1,fz-1),u),v);
        }
        static double Fbm(byte[] p,double x,double z,double scale,int oct)
        {
            double sum=0,norm=0,amp=1,f=scale;
            for(int i=0;i<oct;i++){sum+=Perlin(p,x*f+i*131.7,z*f-i*57.3)*amp;norm+=amp;amp*=.5;f*=2;}
            return sum/norm;
        }
        static float Clamp(float v){return v < -1f ? -1f : v > 1f ? 1f : v;}
        static double Clamp(double v){return v < -1d ? -1d : v > 1d ? 1d : v;}

        // Source-precision climate path. JavaScript Number is IEEE-754 double, so biome/height
        // decisions must not be made from values that have already been rounded to float.
        public double ContinentalExact(int x,int z)=>Fbm(continental,x,z,1.0/2200.0,4)*2.3+.24;
        public double ErosionExact(int x,int z)=>Fbm(erosion,x,z,1.0/1300.0,4)*2.1;
        public double WeirdnessExact(int x,int z)=>Fbm(weirdness,x,z,1.0/850.0,5)*2.2;
        public double FineDetailExact(int x,int z)=>Fbm(detail,x,z,1.0/22.0,3);
        public double TemperatureExact(double x,double z)=>Clamp(Fbm(temperature,x,z,1.0/1600.0,3)*2.4);
        public double HumidityExact(double x,double z)=>Clamp(Fbm(humidity,x,z,1.0/1500.0,3)*2.1);

        public float Continental(int x,int z)=> (float)(Fbm(continental,x,z,1.0/2200.0,4)*2.3+.24);
        public float Erosion(int x,int z)=> (float)(Fbm(erosion,x,z,1.0/1300.0,4)*2.1);
        public float Weirdness(int x,int z)=> (float)(Fbm(weirdness,x,z,1.0/850.0,5)*2.2);
        public float FineDetail(int x,int z)=> (float)Fbm(detail,x,z,1.0/22.0,3);
        public float Temperature(float x,float z)=> Clamp((float)(Fbm(temperature,x,z,1.0/1600.0,3)*2.4));
        public float Humidity(float x,float z)=> Clamp((float)(Fbm(humidity,x,z,1.0/1500.0,3)*2.1));

        static float Smooth(float x){return x*x*(3f-2f*x);}
        static uint HashInt(int x,int z)
        {
            unchecked
            {
                int r = x*374761393 + z*668265263 + 527595518;
                r = r ^ (int)(((uint)r)>>13); r *= 1274126177;
                return (uint)(r ^ (int)(((uint)r)>>16));
            }
        }
        float Lattice(int x,int z) => HashInt(unchecked(x+seed),unchecked(z-seed))/4294967296f;
        double LatticeExact(int x,int z) => HashBitsTrunc(x,z)/4294967296.0;
        // Raw g(A,C) bits for algorithms whose RNG seed depends on all 32 hash bits.
        // JavaScript first converts the shifted coordinates with |0, then performs the integer hash.
        public uint HashBitsTrunc(double x,double z)
        {
            int ix=SourceMath.JsToInt32(x+seed), iz=SourceMath.JsToInt32(z-seed);
            int r=SourceMath.JsToInt32((double)ix*374761393d+(double)iz*668265263d+527595518d);
            unchecked
            {
                r^=(int)((uint)r>>13);
                r*=1274126177;
                return (uint)(r^(int)((uint)r>>16));
            }
        }
        // Exact g(A,C) / AA(A,C,w) style hashes used by main for deterministic decoration placement.
        public float Hash(int x,int z) => Lattice(x,z);
        public float Hash3(int x,int y,int z)
        {
            unchecked
            {
                int xx=x+seed, zz=z-seed;
                int r=xx*374761393 + y*668265263 + zz*2147483423;
                r ^= (int)((uint)r >> 13); r *= 1274126177; r ^= (int)((uint)r >> 16);
                return (uint)r / 4294967296f;
            }
        }
        public float Value(float x,float z)
        {
            int ix=(int)Math.Floor(x),iz=(int)Math.Floor(z); float fx=x-ix,fz=z-iz,u=Smooth(fx),v=Smooth(fz);
            float a=Lattice(ix,iz),b=Lattice(ix+1,iz),c=Lattice(ix,iz+1),d=Lattice(ix+1,iz+1);
            return (a+(b-a)*u) + ((c+(d-c)*u)-(a+(b-a)*u))*v;
        }

        static double Smooth(double x){return x*x*(3d-2d*x);}
        public double ValueExact(double x,double z)
        {
            int ix=(int)Math.Floor(x),iz=(int)Math.Floor(z); double fx=x-ix,fz=z-iz,u=Smooth(fx),v=Smooth(fz);
            double a=LatticeExact(ix,iz),b=LatticeExact(ix+1,iz),c=LatticeExact(ix,iz+1),d=LatticeExact(ix+1,iz+1);
            return a+(b-a)*u+(c-a)*v+(a-b-c+d)*u*v;
        }

        uint Hash3BitsTrunc(double x,double y,double z)
        {
            int ix=SourceMath.JsToInt32(x+seed), iy=SourceMath.JsToInt32(y), iz=SourceMath.JsToInt32(z-seed);
            int r=SourceMath.JsToInt32((double)ix*374761393d+(double)iy*668265263d+(double)iz*2147483423d);
            unchecked
            {
                r^=(int)((uint)r>>13); r*=1274126177;
                return (uint)(r^(int)((uint)r>>16));
            }
        }
        double Hash3Exact(int x,int y,int z)=>Hash3BitsTrunc(x,y,z)/4294967296.0;

        /// <summary>floor() for values well inside the int range, without the double round trip.</summary>
        static int FastFloor(float v){int i=(int)v;return v<i?i-1:i;}
        const int HX=374761393, HY=668265263, HZ=2147483423;
        static float Mix3(int r)
        {
            unchecked { r ^= (int)((uint)r >> 13); r *= 1274126177; r ^= (int)((uint)r >> 16); return (uint)r / 4294967296f; }
        }

        public float Value3(float x,float y,float z)
        {
            // Hot path of cave carving (up to ten calls per voxel). Same arithmetic as Hash3() on the
            // eight corners: the corner hashes are the base hash plus the per-axis multipliers
            // (wrapping int arithmetic), so each corner costs one add instead of three multiplies.
            int ix=FastFloor(x), iy=FastFloor(y), iz=FastFloor(z);
            float fx=x-ix, fy=y-iy, fz=z-iz;
            float ux=Smooth(fx), uy=Smooth(fy), uz=Smooth(fz);
            int r0; unchecked { r0=(ix+seed)*HX + iy*HY + (iz-seed)*HZ; }
            float a,b,c,d,e,f,g,h;
            unchecked
            {
                a=Mix3(r0); b=Mix3(r0+HX); c=Mix3(r0+HY); d=Mix3(r0+HX+HY);
                e=Mix3(r0+HZ); f=Mix3(r0+HX+HZ); g=Mix3(r0+HY+HZ); h=Mix3(r0+HX+HY+HZ);
            }
            float l0=a+(b-a)*ux, l1=c+(d-c)*ux, l2=e+(f-e)*ux, l3=g+(h-g)*ux;
            float q0=l0+(l1-l0)*uy, q1=l2+(l3-l2)*uy;
            return q0+(q1-q0)*uz;
        }

        /// <summary>The eight corner values of the last lattice cell one Value3 call site used.</summary>
        public struct Cell3 { public int X, Y, Z; public bool Valid; public float A, B, C, D, E, F, G, H; }

        /// <summary>
        /// Value3() for a call site that is evaluated at consecutive voxels (e.g. walking a column
        /// upwards): the corner hashes are reused while the point stays in the same lattice cell.
        /// Bit-identical to Value3().
        /// </summary>
        public float Value3(ref Cell3 c,float x,float y,float z)
        {
            int ix=FastFloor(x), iy=FastFloor(y), iz=FastFloor(z);
            if(!c.Valid||ix!=c.X||iy!=c.Y||iz!=c.Z)
            {
                int r0; unchecked { r0=(ix+seed)*HX + iy*HY + (iz-seed)*HZ;
                c.A=Mix3(r0); c.B=Mix3(r0+HX); c.C=Mix3(r0+HY); c.D=Mix3(r0+HX+HY);
                c.E=Mix3(r0+HZ); c.F=Mix3(r0+HX+HZ); c.G=Mix3(r0+HY+HZ); c.H=Mix3(r0+HX+HY+HZ); }
                c.X=ix; c.Y=iy; c.Z=iz; c.Valid=true;
            }
            float fx=x-ix, fy=y-iy, fz=z-iz;
            float ux=Smooth(fx), uy=Smooth(fy), uz=Smooth(fz);
            float l0=c.A+(c.B-c.A)*ux, l1=c.C+(c.D-c.C)*ux, l2=c.E+(c.F-c.E)*ux, l3=c.G+(c.H-c.G)*ux;
            float q0=l0+(l1-l0)*uy, q1=l2+(l3-l2)*uy;
            return q0+(q1-q0)*uz;
        }

        public double Value3Exact(double x,double y,double z)
        {
            int ix=(int)Math.Floor(x), iy=(int)Math.Floor(y), iz=(int)Math.Floor(z);
            double fx=x-ix, fy=y-iy, fz=z-iz;
            double ux=Smooth(fx), uy=Smooth(fy), uz=Smooth(fz);
            double a=Hash3Exact(ix,iy,iz), b=Hash3Exact(ix+1,iy,iz), c=Hash3Exact(ix,iy+1,iz), d=Hash3Exact(ix+1,iy+1,iz);
            double e=Hash3Exact(ix,iy,iz+1), f=Hash3Exact(ix+1,iy,iz+1), g=Hash3Exact(ix,iy+1,iz+1), h=Hash3Exact(ix+1,iy+1,iz+1);
            double l0=a+(b-a)*ux, l1=c+(d-c)*ux, l2=e+(f-e)*ux, l3=g+(h-g)*ux;
            double q0=l0+(l1-l0)*uy, q1=l2+(l3-l2)*uy;
            return q0+(q1-q0)*uz;
        }

        public double TerrainFbm3Exact(double x,double y,double z)
        {
            double sum=0d, amp=.5d, freq=1d, norm=0d;
            for(int i=0;i<4;i++)
            {
                sum+=Value3Exact(x*freq+i*71.3,y*freq-i*113.1,z*freq+i*191.7)*amp;
                norm+=amp;amp*=.5;freq*=2;
            }
            return sum/norm;
        }

        // Exact t4() four-octave 3-D value-noise field used by T1().
        public float TerrainFbm3(float x,float y,float z)
        {
            float sum=0f, amp=.5f, freq=1f, norm=0f;
            for(int i=0;i<4;i++)
            {
                sum += Value3(x*freq+i*71.3f, y*freq-i*113.1f, z*freq+i*191.7f) * amp;
                norm += amp; amp*=.5f; freq*=2f;
            }
            return sum/norm;
        }
        public double ClassicValueExact(double x,double z)
        {
            double sum=0d,norm=0d,amp=.5d,freq=1d;
            for(int i=0;i<4;i++)
            {
                double ex=x*ClassicRxExact[i]-z*ClassicRzExact[i], ez=x*ClassicRzExact[i]+z*ClassicRxExact[i];
                sum+=ValueExact(ex*freq+i*119.7,ez*freq-i*53.3)*amp;norm+=amp;amp*=.5;freq*=2.17;
            }
            return norm>0d?sum/norm:.5d;
        }

        public float ClassicValue(float x,float z)
        {
            float sum=0,norm=0,amp=.5f,f=1f;
            for(int i=0;i<4;i++)
            {
                float ex=x*ClassicRx[i]-z*ClassicRz[i], ez=x*ClassicRz[i]+z*ClassicRx[i];
                sum+=Value(ex*f+i*119.7f,ez*f-i*53.3f)*amp; norm+=amp; amp*=.5f; f*=2.17f;
            }
            return norm>0?sum/norm:.5f;
        }
    }
}
