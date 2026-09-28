using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js aR()/Qd() third-person player path. Geometry is the exact Oh[] player model,
    /// including 0.25px second skin layers. It appends world-space vertices into the same direct
    /// dynamic batches as mobs; no MeshRenderer/MeshFilter is created.
    /// </summary>
    public sealed class MainThirdPersonPlayerRenderer : MonoBehaviour
    {
        struct PartSpec
        {
            public Vector3 Size, TexBox, At, Pivot;
            public Vector2 UV;
            public string Anim;
            public bool HasPivot;
            public PartSpec(Vector3 size,Vector3 texBox,Vector3 at,Vector2 uv,string anim=null,Vector3? pivot=null)
            {Size=size;TexBox=texBox;At=at;UV=uv;Anim=anim;HasPivot=pivot.HasValue;Pivot=pivot??Vector3.zero;}
        }
        struct Geometry
        {
            public Vector3[] V;
            public Vector2[] UV;
            public float[] Shade;
            public int[] I;
        }
        struct RuntimePart { public PartSpec Spec; public Geometry Geo; }

        static MainThirdPersonPlayerRenderer instance;
        static readonly int[] HeldFlatQuad={0,1,2,2,1,3,0,2,1,1,2,3};
        RuntimePart[] parts;
        float walkPhase;
        double lastTime;
        double swimUntil;
        bool swimSmoothValid;
        float smoothSwimYaw,smoothSwimPitch;

        public MainPlayerController Controller;
        public VoxelWorld World;

        public static MainThirdPersonPlayerRenderer Instance => instance;
        public bool Visible => Controller!=null&&Controller.CameraMode!=0;

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(this);return;}
            instance=this;
            BuildParts();
        }
        void OnDestroy(){if(instance==this)instance=null;}

        void BuildParts()
        {
            // main Oh[] + RE(): layer thickness ip=.25, b grows by .5 and at.y moves down .25;
            // UV box dimensions stay the original tb dimensions.
            var s=new[]
            {
                P(4,12,4, 4,12,4,  2,0,0, 16,48,"leg1"),
                P(4.5f,12.5f,4.5f, 4,12,4,  2,-.25f,0, 0,48,"leg1"),
                P(4,12,4, 4,12,4, -2,0,0, 0,16,"leg0"),
                P(4.5f,12.5f,4.5f, 4,12,4, -2,-.25f,0, 0,32,"leg0"),
                P(8,12,4, 8,12,4, 0,12,0, 16,16),
                P(8.5f,12.5f,4.5f, 8,12,4, 0,11.75f,0, 16,32),
                P(4,12,4, 4,12,4, 6,12,0, 32,48,"armL"),
                P(4.5f,12.5f,4.5f, 4,12,4, 6,11.75f,0, 48,48,"armL"),
                P(4,12,4, 4,12,4, -6,12,0, 40,16,"armR"),
                P(4.5f,12.5f,4.5f, 4,12,4, -6,11.75f,0, 40,32,"armR"),
                new PartSpec(new Vector3(8,8,8),new Vector3(8,8,8),new Vector3(0,24,0),new Vector2(0,0),"head",new Vector3(0,24,0)),
                new PartSpec(new Vector3(9,9,9),new Vector3(8,8,8),new Vector3(0,23.5f,0),new Vector2(32,0),"head",new Vector3(0,24,0))
            };
            parts=new RuntimePart[s.Length];
            for(int i=0;i<s.Length;i++)parts[i]=new RuntimePart{Spec=s[i],Geo=BuildBox(s[i])};
        }
        static PartSpec P(float sx,float sy,float sz,float tx,float ty,float tz,float ax,float ay,float az,float u,float v,string anim=null)
            =>new PartSpec(new Vector3(sx,sy,sz),new Vector3(tx,ty,tz),new Vector3(ax,ay,az),new Vector2(u,v),anim);

        static Geometry BuildBox(PartSpec s)
        {
            float J=s.Size.x,O=s.Size.y,G=s.Size.z,N=s.UV.x,Y=s.UV.y;
            float TJ=s.TexBox.x,TO=s.TexBox.y,TG=s.TexBox.z;
            float x0=s.At.x-J*.5f,x1=s.At.x+J*.5f,y0=s.At.y,y1=s.At.y+O,z0=s.At.z-G*.5f,z1=s.At.z+G*.5f;
            var v=new List<Vector3>(24);var uv=new List<Vector2>(24);var sh=new List<float>(24);var ind=new List<int>(36);
            Vector2[] top=Rect(N+TG,Y,TJ,TG),bottom=Rect(N+TG+TJ,Y,TJ,TG),front=Rect(N+TG,Y+TG,TJ,TO),back=Rect(N+2*TG+TJ,Y+TG,TJ,TO),right=Rect(N,Y+TG,TG,TO),left=Rect(N+TG+TJ,Y+TG,TG,TO);
            Face(v,uv,sh,ind,new[]{new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x0,y1,z0),new Vector3(x1,y1,z0)},top,1f);
            Face(v,uv,sh,ind,new[]{new Vector3(x0,y0,z0),new Vector3(x1,y0,z0),new Vector3(x0,y0,z1),new Vector3(x1,y0,z1)},bottom,.55f);
            Face(v,uv,sh,ind,new[]{new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x0,y0,z1),new Vector3(x1,y0,z1)},front,.85f);
            Face(v,uv,sh,ind,new[]{new Vector3(x1,y1,z0),new Vector3(x0,y1,z0),new Vector3(x1,y0,z0),new Vector3(x0,y0,z0)},back,.85f);
            Face(v,uv,sh,ind,new[]{new Vector3(x1,y1,z1),new Vector3(x1,y1,z0),new Vector3(x1,y0,z1),new Vector3(x1,y0,z0)},left,.72f);
            Face(v,uv,sh,ind,new[]{new Vector3(x0,y1,z0),new Vector3(x0,y1,z1),new Vector3(x0,y0,z0),new Vector3(x0,y0,z1)},right,.72f);
            return new Geometry{V=v.ToArray(),UV=uv.ToArray(),Shade=sh.ToArray(),I=ind.ToArray()};
        }
        static Vector2[] Rect(float x,float y,float w,float h)=>new[]{new Vector2(x,y),new Vector2(x+w,y),new Vector2(x,y+h),new Vector2(x+w,y+h)};
        static void Face(List<Vector3> v,List<Vector2> uv,List<float> sh,List<int> ind,Vector3[] q,Vector2[] t,float shade)
        {int n=v.Count;for(int i=0;i<4;i++){v.Add(q[i]);uv.Add(t[i]);sh.Add(shade);}ind.Add(n);ind.Add(n+1);ind.Add(n+2);ind.Add(n+2);ind.Add(n+1);ind.Add(n+3);}

        internal void AppendToBatch(List<VoxelVertex> entityV,List<int> entityI,List<VoxelVertex> heldV,List<int> heldI)
        {
            if(!Visible||World==null||parts==null)return;
            double now=Time.realtimeSinceStartupAsDouble;
            float dt=lastTime>0?Mathf.Min(.1f,(float)(now-lastTime)):0f;lastTime=now;
            Vector3 vel=Controller.Velocity;float speed=new Vector2(vel.x,vel.z).magnitude;
            float yaw=Mathf.PI-Controller.MainYawRadians;
            float bodyPitch=0f,yOffset=0f;
            bool moving=false;
            float leg0=0,leg1=0,armL=0,armR=0;

            if(Controller.Swimming)swimUntil=now+.350;
            bool swimPose=Controller.InWater&&!Controller.Flying&&now<swimUntil;
            bool crawlPose=!Controller.InWater&&Controller.CurrentHeight<=.9f;
            if(swimPose||crawlPose)
            {
                float targetPitch=Mathf.PI*.5f-.15f,targetYaw=yaw;
                if(swimPose)
                {
                    if(speed>.5f)targetYaw=Mathf.PI-Mathf.Atan2(vel.x,vel.z); // reflected source: -sourceVz == unityVz
                    targetPitch=Mathf.PI*.5f+Mathf.Atan2(-vel.y,Mathf.Max(speed,.8f));
                    if(!swimSmoothValid){smoothSwimYaw=targetYaw;smoothSwimPitch=targetPitch;swimSmoothValid=true;}
                    float delta=Mathf.DeltaAngle(smoothSwimYaw*Mathf.Rad2Deg,targetYaw*Mathf.Rad2Deg)*Mathf.Deg2Rad;
                    float k=Mathf.Min(1f,dt*7f);smoothSwimYaw+=delta*k;smoothSwimPitch+=(targetPitch-smoothSwimPitch)*k;
                    yaw=smoothSwimYaw;bodyPitch=smoothSwimPitch;
                }
                else bodyPitch=Mathf.PI*.5f;
                walkPhase+=dt*(swimPose?Mathf.Max(speed,2.5f)*1.1f:Mathf.Max(speed,1.2f)*1.6f);
                float h=Mathf.Sin(walkPhase*3f)*(swimPose?.9f:.45f);
                armL=h;armR=-h;leg0=-h*.6f;leg1=h*.6f;yOffset=-.3f;
            }
            else if(Controller.InWater&&!Controller.OnGround)
            {
                walkPhase+=dt*2.2f;float h=Mathf.Sin(walkPhase*3f)*.25f;
                armL=h;armR=-h;leg0=-h;leg1=h;
            }
            else
            {
                moving=speed>.12f&&Controller.OnGround;walkPhase+=dt*speed*1.6f;
                if(Controller.Sneaking){bodyPitch=.22f;yOffset=-.12f;}
            }
            if(!swimPose)swimSmoothValid=false;
            float charge=Controller.FirstPersonBowCharge,swing=Controller.FirstPersonSwing;
            if(charge>0f){armR=-1.3f;armL=-.9f;}else if(swing>.01f)armR-=swing*1.7f;

            Vector3 basePos=Controller.transform.position+Vector3.up*yOffset;
            int bx=Mathf.FloorToInt(basePos.x),by=Mathf.FloorToInt(basePos.y+1f),bz=Mathf.FloorToInt(basePos.z);
            byte packed=World.GetPackedLight(bx,by,bz);float sky=(packed>>4)/15f,block=(packed&15)/15f;
            float walk=moving?Mathf.Sin(walkPhase*3f)*.6f:0f;
            Vector3 handPoint=Vector3.zero;bool haveHand=false;

            for(int pi=0;pi<parts.Length;pi++)
            {
                RuntimePart rp=parts[pi];PartSpec ps=rp.Spec;float rx=0f;
                if(ps.Anim=="leg0")rx=walk+leg0;else if(ps.Anim=="leg1")rx=-walk+leg1;
                else if(ps.Anim=="armR")rx=walk+armR;else if(ps.Anim=="armL")rx=-walk+armL;
                else if(ps.Anim=="head")rx=-Controller.MainPitchRadians-bodyPitch;
                Vector3 pivot=ps.HasPivot?ps.Pivot:new Vector3(ps.At.x,ps.At.y+ps.Size.y,ps.At.z);
                int start=entityV.Count;
                for(int vi=0;vi<rp.Geo.V.Length;vi++)
                {
                    Vector3 p=rp.Geo.V[vi];p=TransformPoint(p,pivot,rx,bodyPitch,yaw,basePos);
                    entityV.Add(new VoxelVertex(p,EntityAtlasLayout.UV("entity_player",rp.Geo.UV[vi]),new Vector3(rp.Geo.Shade[vi],sky,block)));
                }
                for(int ii=0;ii<rp.Geo.I.Length;ii++)entityI.Add(start+rp.Geo.I[ii]);
                if(ps.Anim=="armR")
                {
                    // Qd(): c=pA(T.at[0], y+1, T.at[2]); the layer arm is last and intentionally wins.
                    handPoint=TransformPoint(new Vector3(ps.At.x,ps.At.y+1f,ps.At.z),pivot,rx,bodyPitch,yaw,basePos);haveHand=true;
                }
            }

            // main aR(): only block IDs with M[id] become Yt.handItem. Non-block item IDs are not rendered here.
            if(haveHand&&string.IsNullOrEmpty(Controller.FirstPersonPrimaryItemTexture)&&Controller.SelectedBlock!=BlockId.Air)
                AppendHeldBlock(Controller.SelectedBlock,handPoint,yaw,sky,block,heldV,heldI);
        }

        static Vector3 TransformPoint(Vector3 p,Vector3 pivot,float rx,float bodyPitch,float yaw,Vector3 basePos)
        {
            p-=pivot;float cx=Mathf.Cos(rx),sx=Mathf.Sin(rx),t=p.y*cx-p.z*sx;p.z=p.y*sx+p.z*cx;p.y=t;p+=pivot;
            if(bodyPitch!=0f){float cp=Mathf.Cos(bodyPitch),sp=Mathf.Sin(bodyPitch),yy=p.y-12f;t=yy*cp-p.z*sp;p.z=yy*sp+p.z*cp;p.y=t+12f;}
            float c=Mathf.Cos(yaw),s=Mathf.Sin(yaw);float sourceX=p.x*c+p.z*s,sourceZ=-p.x*s+p.z*c;
            // SourceCoords reflection: source world Z -> Unity -Z.
            return basePos+new Vector3(sourceX,p.y,-sourceZ)*(1f/16f);
        }

        static void AppendHeldBlock(BlockId id,Vector3 hand,float yaw,float sky,float block,List<VoxelVertex> v,List<int> ind)
        {
            BlockDef d=BlockRegistry.Get(id);float sy=Mathf.Sin(yaw),cy=Mathf.Cos(yaw);
            // Qd(): _e=c.x+sin(yaw)*.1, wr=c.y-.06, Gt=c.z+cos(yaw)*.1 in source coordinates.
            // Z is reflected for Unity, hence -cos(yaw)*.1.
            Vector3 center=hand+new Vector3(sy*.1f,-.06f,-cy*.1f);
            // main Qd(): Ot(item) is impossible here because aR only forwards block IDs; the flat block path is bA(cross) + torch + rail.
            bool flat=d.Shape==BlockShape.Cross||d.Shape==BlockShape.TallPlant||d.Shape==BlockShape.Torch||d.Shape==BlockShape.Rail;
            if(flat){AppendFlat(d.Side,center,yaw,sky,block,v,ind);return;}
            const float pn=.11f;
            for(int f=0;f<6;f++)
            {
                FaceDir fd=(FaceDir)f;AtlasRect tex=d.Face(fd);int start=v.Count;
                for(int q=0;q<4;q++)
                {
                    Vector3 c=HeldCorners[f,q];Vector2 uv=HeldUV[f,q];float lx=(c.x-.5f)*2f*pn,ly=c.y*2f*pn-pn,lz=(c.z-.5f)*2f*pn;
                    // source: m1=Ft*cos(yaw)-Er*sin(yaw); nn=Ft*sin(yaw)+Er*cos(yaw), then reflect source Z.
                    float wx=lx*cy-lz*sy,wz=-(lx*sy+lz*cy);
                    v.Add(new VoxelVertex(center+new Vector3(wx,ly,wz),Atlas(tex,uv),new Vector3(HeldShade[f],sky,block)));
                }
                for(int k=0;k<6;k++)ind.Add(start+HeldQuad[k]);
            }
        }
        static void AppendFlat(AtlasRect tex,Vector3 center,float yaw,float sky,float block,List<VoxelVertex> v,List<int> ind)
        {
            const float he=.26f;float k=Mathf.Cos(-.785f),l=Mathf.Sin(-.785f),sy=Mathf.Sin(yaw),cy=Mathf.Cos(yaw);int s=v.Count;
            for(int y=0;y<2;y++)for(int x=0;x<2;x++)
            {
                float a=(x-.5f)*2f*he,b=(.5f-y)*2f*he,rx=a*k-b*l,ry=a*l+b*k;
                v.Add(new VoxelVertex(center+new Vector3(sy*rx,ry+he,-cy*rx),Atlas(tex,new Vector2(x,y)),new Vector3(.92f,sky,block)));
            }
            // source writes both windings.
            for(int i=0;i<HeldFlatQuad.Length;i++)ind.Add(s+HeldFlatQuad[i]);
        }
        static Vector2 Atlas(AtlasRect r,Vector2 uv)=>new Vector2(Mathf.Lerp(r.U0,r.U1,uv.x),Mathf.Lerp(r.V1,r.V0,uv.y));

        static readonly Vector3[,] HeldCorners={
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,0,1),new Vector3(1,1,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,0,0),new Vector3(0,1,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(0,1,0),new Vector3(1,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(0,1,1),new Vector3(1,1,1)},
            {new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(1,1,0),new Vector3(0,1,0)}};
        static readonly Vector2[,] HeldUV={
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}};
        static readonly float[] HeldShade={.72f,.72f,1f,.55f,.82f,.82f};
        static readonly int[] HeldQuad={0,1,2,2,1,3};
    }
}
