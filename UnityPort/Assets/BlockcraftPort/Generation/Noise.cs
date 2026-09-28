using System;
namespace BlockcraftPort
{
    public static class Noise
    {
        static uint H(int x,int y,int z,int seed)
        {
            unchecked
            {
                uint h=(uint)seed; h^=(uint)x*0x9E3779B1u; h=(h<<13)|(h>>19);
                h^=(uint)y*0x85EBCA77u; h=(h<<11)|(h>>21); h^=(uint)z*0xC2B2AE3Du;
                h^=h>>16; h*=0x7FEB352Du; h^=h>>15; h*=0x846CA68Bu; h^=h>>16; return h;
            }
        }
        public static float Hash01(int x,int z,int seed)=> (H(x,0,z,seed)&0x00FFFFFFu)/16777215f;
        static float Fade(float t)=>t*t*t*(t*(t*6f-15f)+10f);
        static float Lerp(float a,float b,float t)=>a+(b-a)*t;
        static float Grad2(uint h,float x,float z)
        {
            switch(h&7u){case 0:return x+z;case 1:return x-z;case 2:return -x+z;case 3:return -x-z;case 4:return x;case 5:return -x;case 6:return z;default:return -z;}
        }
        static float Grad3(uint h,float x,float y,float z)
        {
            uint b=h%12u;
            switch(b){case 0:return x+y;case 1:return -x+y;case 2:return x-y;case 3:return -x-y;case 4:return x+z;case 5:return -x+z;case 6:return x-z;case 7:return -x-z;case 8:return y+z;case 9:return -y+z;case 10:return y-z;default:return -y-z;}
        }
        public static float Perlin2(float x,float z,int seed)
        {
            int x0=(int)Math.Floor(x),z0=(int)Math.Floor(z); int x1=x0+1,z1=z0+1;
            float tx=x-x0,tz=z-z0,u=Fade(tx),v=Fade(tz);
            float a=Grad2(H(x0,0,z0,seed),tx,tz), b=Grad2(H(x1,0,z0,seed),tx-1,tz);
            float c=Grad2(H(x0,0,z1,seed),tx,tz-1), d=Grad2(H(x1,0,z1,seed),tx-1,tz-1);
            return Lerp(Lerp(a,b,u),Lerp(c,d,u),v)*0.70710678f;
        }
        public static float Perlin3(float x,float y,float z,int seed)
        {
            int x0=(int)Math.Floor(x),y0=(int)Math.Floor(y),z0=(int)Math.Floor(z); int x1=x0+1,y1=y0+1,z1=z0+1;
            float tx=x-x0,ty=y-y0,tz=z-z0,u=Fade(tx),v=Fade(ty),w=Fade(tz);
            float c000=Grad3(H(x0,y0,z0,seed),tx,ty,tz), c100=Grad3(H(x1,y0,z0,seed),tx-1,ty,tz);
            float c010=Grad3(H(x0,y1,z0,seed),tx,ty-1,tz), c110=Grad3(H(x1,y1,z0,seed),tx-1,ty-1,tz);
            float c001=Grad3(H(x0,y0,z1,seed),tx,ty,tz-1), c101=Grad3(H(x1,y0,z1,seed),tx-1,ty,tz-1);
            float c011=Grad3(H(x0,y1,z1,seed),tx,ty-1,tz-1), c111=Grad3(H(x1,y1,z1,seed),tx-1,ty-1,tz-1);
            float x00=Lerp(c000,c100,u),x10=Lerp(c010,c110,u),x01=Lerp(c001,c101,u),x11=Lerp(c011,c111,u);
            return Lerp(Lerp(x00,x10,v),Lerp(x01,x11,v),w)*0.87f;
        }
        public static float Fbm2(float x,float z,int seed,int oct=5,float lac=2f,float gain=.5f)
        {
            float sum=0,amp=1,norm=0; for(int i=0;i<oct;i++){sum+=Perlin2(x,z,seed+i*1013)*amp;norm+=amp;x*=lac;z*=lac;amp*=gain;} return sum/norm;
        }
        public static float Fbm3(float x,float y,float z,int seed,int oct=3,float lac=2f,float gain=.5f)
        {
            float sum=0,amp=1,norm=0; for(int i=0;i<oct;i++){sum+=Perlin3(x,y,z,seed+i*1619)*amp;norm+=amp;x*=lac;y*=lac;z*=lac;amp*=gain;} return sum/norm;
        }
        public static float Ridge2(float x,float z,int seed,int oct=4)
        { float n=Fbm2(x,z,seed,oct); return 1f-Math.Abs(n); }
        public static float Clamp01(float v)=>v<0?0:v>1?1:v;
    }
}
