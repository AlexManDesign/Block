using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-data entity model for the main.js Qd() renderer. Geometry/UVs remain in source texture
    /// pixels and are transformed in source X->Y->Z, model-pitch, yaw order before being appended to
    /// MainEntityBatchRenderer.
    /// </summary>
    public sealed class MainEntityModel : MonoBehaviour
    {
        struct Geometry
        {
            public Vector3[] VerticesPx;
            public Vector2[] UvPx;
            public float[] Shade;
            public int[] Triangles;
        }

        struct RuntimePart
        {
            public PartSpec Spec;
            public Geometry Geometry;
        }

        public struct PartSpec
        {
            public Vector3 SizePx, AtPx, UvPx, RotRad;
            public string Anim;
            public Vector3? PivotPx;
            public bool RotUV;
            public int SpiderLeg;
            public PartSpec(Vector3 size,Vector3 at,Vector2 uv,string anim=null,Vector3? rotRad=null,Vector3? pivotPx=null,bool rotUV=false,int spiderLeg=-1)
            {SizePx=size;AtPx=at;UvPx=new Vector3(uv.x,uv.y,0);Anim=anim;RotRad=rotRad??Vector3.zero;PivotPx=pivotPx;RotUV=rotUV;SpiderLeg=spiderLeg;}
        }

        static readonly Dictionary<string,Geometry> GeometryCache=new Dictionary<string,Geometry>();
        struct AtlasUvKey : IEquatable<AtlasUvKey>
        {
            public readonly MobKind Kind;public readonly int Part;public readonly string Texture;
            public AtlasUvKey(MobKind kind,int part,string texture){Kind=kind;Part=part;Texture=texture;}
            public bool Equals(AtlasUvKey other)=>Kind==other.Kind&&Part==other.Part&&Texture==other.Texture;
            public override bool Equals(object obj)=>obj is AtlasUvKey other&&Equals(other);
            public override int GetHashCode(){unchecked{return ((int)Kind*397^Part)*397+(Texture!=null?Texture.GetHashCode():0);}}
        }
        static readonly Dictionary<AtlasUvKey,Vector2[]> AtlasUvCache=new Dictionary<AtlasUvKey,Vector2[]>();
        static readonly Dictionary<MobKind,PartSpec[]> SpecCache=new Dictionary<MobKind,PartSpec[]>();
        static readonly MobKind[] AllKinds={MobKind.Pig,MobKind.Cow,MobKind.Sheep,MobKind.Chicken,MobKind.Zombie,MobKind.Skeleton,MobKind.Creeper,MobKind.Spider,MobKind.Enderman,MobKind.Salmon,MobKind.Shark,MobKind.SlimeBig,MobKind.SlimeMedium,MobKind.SlimeSmall};
        RuntimePart[] parts;
        MobKind kind;
        string textureName;
        float modelScale=1f,pitchYPx,pitchRad,walkPhase,squashY=1f,fuse=-1f;
        bool moving,hasRenderPose,baby,sheared;
        MobSheepColor sheepColor=MobSheepColor.White;
        Vector3 renderPosition;
        float renderYawRad;
        byte packedLight;
        float hurtScale=1f;
        Bounds sourceBounds;
        float sourceCullRadiusPx;
        uint renderVersion;

        public Bounds WorldBounds
        {
            get
            {
                GetWorldBoundsMinMax(out Vector3 mn,out Vector3 mx);
                return new Bounds((mn+mx)*.5f,mx-mn);
            }
        }

        public uint RenderVersion => renderVersion;

        public void GetWorldBoundsMinMax(out Vector3 mn,out Vector3 mx)
        {
            float scale=modelScale/16f*(baby?.5f:1f),r=sourceCullRadiusPx*scale+.5f;
            Vector3 basePos=hasRenderPose?renderPosition:transform.position;
            Vector3 c=basePos+Vector3.up*(sourceBounds.center.y*scale);
            Vector3 e=new Vector3(r,r,r);mn=c-e;mx=c+e;
        }

        void TouchRender(){unchecked{renderVersion++;}MainEntityBatchRenderer.NotifyModelDirty();}

        public static MainEntityModel Build(GameObject root,MobKind kind,float mscale=1f)
        {
            var model=root.AddComponent<MainEntityModel>();model.kind=kind;model.modelScale=mscale;model.BuildInternal();MainEntityBatchRenderer.Register(model);return model;
        }

        static PartSpec[] CachedSpecs(MobKind k)
        {
            if(!SpecCache.TryGetValue(k,out var specs)){specs=Specs(k);SpecCache[k]=specs;}return specs;
        }

        public static void WarmupGeometry()
        {
            for(int ki=0;ki<AllKinds.Length;ki++)
            {
                MobKind k=AllKinds[ki];PartSpec[] specs=CachedSpecs(k);string texture=TextureName(k);
                for(int i=0;i<specs.Length;i++)
                {
                    string key=k+":"+i+":"+texture;
                    if(!GeometryCache.TryGetValue(key,out Geometry g)){g=BuildBoxGeometry(specs[i]);GeometryCache[key]=g;}
                    // Move immutable atlas-UV allocation out of gameplay frames.
                    CachedAtlasUvs(k,i,texture,g.UvPx);
                }
            }
        }

        void BuildInternal()
        {
            textureName=TextureName(kind);pitchYPx=PitchY(kind);
            PartSpec[] specs=CachedSpecs(kind);parts=new RuntimePart[specs.Length];
            Vector3 min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<specs.Length;i++)
            {
                string key=kind+":"+i+":"+textureName;
                if(!GeometryCache.TryGetValue(key,out Geometry g))
                {
                    g=BuildBoxGeometry(specs[i]);GeometryCache[key]=g;
                }
                parts[i]=new RuntimePart{Spec=specs[i],Geometry=g};
                Vector3 half=new Vector3(specs[i].SizePx.x*.5f,0,specs[i].SizePx.z*.5f);
                Vector3 lo=specs[i].AtPx-half,hi=specs[i].AtPx+half+Vector3.up*specs[i].SizePx.y;
                min=Vector3.Min(min,lo);max=Vector3.Max(max,hi);
            }
            if(float.IsInfinity(min.x)){min=Vector3.zero;max=Vector3.one;}
            sourceBounds=new Bounds((min+max)*.5f,max-min);
            sourceCullRadiusPx=sourceBounds.extents.magnitude;
        }

        static Geometry BuildBoxGeometry(PartSpec s)
        {
            float J=s.SizePx.x,O=s.SizePx.y,G=s.SizePx.z,N=s.UvPx.x,Y=s.UvPx.y;
            float x0=s.AtPx.x-J*.5f,x1=s.AtPx.x+J*.5f,y0=s.AtPx.y,y1=s.AtPx.y+O,z0=s.AtPx.z-G*.5f,z1=s.AtPx.z+G*.5f;
            var verts=new List<Vector3>(24);var uv=new List<Vector2>(24);var shade=new List<float>(24);var tri=new List<int>(36);
            Vector2[] top,bottom,front,back,left,right;
            if(s.RotUV)
            {
                top=RectUV(N+2*O+J,Y+O,J,G);
                bottom=new[]{new Vector2(N+O,Y+O+G),new Vector2(N+O+J,Y+O+G),new Vector2(N+O,Y+O),new Vector2(N+O+J,Y+O)};
                front=RectUV(N+O,Y,J,O);back=RectUV(N+O+J,Y,J,O);
                left=new[]{new Vector2(N+O,Y+O),new Vector2(N+O,Y+O+G),new Vector2(N,Y+O),new Vector2(N,Y+O+G)};
                right=new[]{new Vector2(N+2*O+J,Y+O+G),new Vector2(N+2*O+J,Y+O),new Vector2(N+O+J,Y+O+G),new Vector2(N+O+J,Y+O)};
            }
            else
            {
                top=RectUV(N+G,Y,J,G);bottom=RectUV(N+G+J,Y,J,G);
                front=RectUV(N+G,Y+G,J,O);back=RectUV(N+2*G+J,Y+G,J,O);
                right=RectUV(N,Y+G,G,O);left=RectUV(N+G+J,Y+G,G,O);
            }
            // Exact Qd() vertex order and per-face shading.
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x0,y1,z0),new Vector3(x1,y1,z0)},top,1f);
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x0,y0,z0),new Vector3(x1,y0,z0),new Vector3(x0,y0,z1),new Vector3(x1,y0,z1)},bottom,.55f);
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x0,y0,z1),new Vector3(x1,y0,z1)},front,.85f);
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x1,y1,z0),new Vector3(x0,y1,z0),new Vector3(x1,y0,z0),new Vector3(x0,y0,z0)},back,.85f);
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x1,y1,z1),new Vector3(x1,y1,z0),new Vector3(x1,y0,z1),new Vector3(x1,y0,z0)},left,.72f);
            AddFace(verts,uv,shade,tri,new[]{new Vector3(x0,y1,z0),new Vector3(x0,y1,z1),new Vector3(x0,y0,z0),new Vector3(x0,y0,z1)},right,.72f);
            return new Geometry{VerticesPx=verts.ToArray(),UvPx=uv.ToArray(),Shade=shade.ToArray(),Triangles=tri.ToArray()};
        }

        static Vector2[] RectUV(float x,float y,float w,float h)=>new[]{new Vector2(x,y),new Vector2(x+w,y),new Vector2(x,y+h),new Vector2(x+w,y+h)};
        static void AddFace(List<Vector3> verts,List<Vector2> uv,List<float> shade,List<int> tri,Vector3[] q,Vector2[] tuv,float faceShade)
        {
            int n=verts.Count;for(int i=0;i<4;i++){verts.Add(q[i]);uv.Add(tuv[i]);shade.Add(faceShade);}tri.Add(n);tri.Add(n+1);tri.Add(n+2);tri.Add(n+2);tri.Add(n+1);tri.Add(n+3);
        }

        public void SetPackedLight(byte packed)
        {
            if(packedLight==packed)return;packedLight=packed;TouchRender();
        }
        public void SetRenderPose(Vector3 position,float yawRad)
        {
            bool same=hasRenderPose&&renderPosition.x==position.x&&renderPosition.y==position.y&&renderPosition.z==position.z&&renderYawRad==yawRad;
            if(same)return;renderPosition=position;renderYawRad=yawRad;hasRenderPose=true;TouchRender();
        }
        public void SetHurtScale(float value)
        {
            value=Mathf.Clamp01(value);if(hurtScale==value)return;hurtScale=value;TouchRender();
        }
        public void SetSquashY(float value)
        {
            value=Mathf.Max(.01f,value);if(squashY==value)return;squashY=value;TouchRender();
        }
        public void SetFuse(float value)
        {
            if(fuse==value)return;fuse=value;TouchRender();
        }
        public void SetMobState(bool isBaby,bool isSheared,MobSheepColor color)
        {
            if(baby==isBaby&&sheared==isSheared&&sheepColor==color)return;baby=isBaby;sheared=isSheared;sheepColor=color;TouchRender();
        }
        public void Animate(float phase,float speed,bool aquatic,float sourcePitchRad)
        {
            bool nextMoving=speed>.3f;float nextPitch=aquatic?sourcePitchRad:0f;
            if(walkPhase==phase&&moving==nextMoving&&pitchRad==nextPitch)return;
            walkPhase=phase;moving=nextMoving;pitchRad=nextPitch;TouchRender();
        }

        internal void AppendToBatch(List<VoxelVertex> vertices,List<int> indices)=>AppendToBatch(vertices,indices,true);
        internal void AppendToBatch(List<VoxelVertex> vertices,List<int> indices,bool appendIndices)
        {
            if(parts==null)return;
            Vector3 basePos=hasRenderPose?renderPosition:transform.position;float yaw=hasRenderPose?renderYawRad:transform.eulerAngles.y*Mathf.Deg2Rad;
            float cy=Mathf.Cos(yaw),sy=Mathf.Sin(yaw),cp=Mathf.Cos(pitchRad),sp=Mathf.Sin(pitchRad);
            float fuseMul=fuse>=0f?1f+fuse*.15f+.06f*Mathf.Sin(fuse*25f):1f;
            float scale=modelScale/16f*fuseMul*(baby?.5f:1f),walk=moving?Mathf.Sin(walkPhase*3f)*.6f:0f,tail=Mathf.Sin(walkPhase*6f)*.45f;
            string activeTexture=CurrentTextureName();
            float syScale=squashY,xzScale=1f/Mathf.Sqrt(syScale);
            float block=(packedLight&15)/15f,sky=(packedLight>>4)/15f;
            for(int pi=0;pi<parts.Length;pi++)
            {
                RuntimePart rp=parts[pi];PartSpec s=rp.Spec;
                float rx=s.RotRad.x,ry=s.RotRad.y,rz=s.RotRad.z;
                if(kind==MobKind.Skeleton&&(s.Anim=="armL"||s.Anim=="armR"))rx=-1.25f+walk*.08f+s.RotRad.x;
                else if(s.Anim=="leg0"||s.Anim=="leg3"||s.Anim=="armR")rx+=walk;
                else if(s.Anim=="leg1"||s.Anim=="leg2"||s.Anim=="armL")rx-=walk;
                if(s.SpiderLeg>=0)
                {
                    float sign=(s.SpiderLeg&1)==0?-1f:1f,a=walkPhase*3f+(s.SpiderLeg>>1)*1.57f,m=moving?1f:0f;
                    ry+=Mathf.Sin(a)*.38f*m*sign;rz+=Mathf.Abs(Mathf.Cos(a))*.32f*m*sign;
                }
                if(s.Anim=="tail")ry+=tail;
                Vector3 pivot=s.PivotPx??new Vector3(s.AtPx.x,s.AtPx.y+s.SizePx.y,s.AtPx.z);
                float cx=Mathf.Cos(rx),sx=Mathf.Sin(rx),cyr=Mathf.Cos(ry),syr=Mathf.Sin(ry),cz=Mathf.Cos(rz),sz=Mathf.Sin(rz);
                int start=vertices.Count;
                Vector2[] atlasUvs=CachedAtlasUvs(kind,pi,activeTexture,rp.Geometry.UvPx);
                for(int vi=0;vi<rp.Geometry.VerticesPx.Length;vi++)
                {
                    Vector3 p=rp.Geometry.VerticesPx[vi]-pivot;float t;
                    if(rx!=0){t=p.y*cx-p.z*sx;p.z=p.y*sx+p.z*cx;p.y=t;}
                    if(ry!=0){t=p.x*cyr+p.z*syr;p.z=-p.x*syr+p.z*cyr;p.x=t;}
                    if(rz!=0){t=p.x*cz-p.y*sz;p.y=p.x*sz+p.y*cz;p.x=t;}
                    p+=pivot;
                    if(pitchRad!=0){float yy=p.y-pitchYPx;t=yy*cp-p.z*sp;p.z=yy*sp+p.z*cp;p.y=t+pitchYPx;}
                    float wx=p.x*cy+p.z*sy,wz=-p.x*sy+p.z*cy;
                    Vector3 world=basePos+new Vector3(wx*xzScale,p.y*syScale,wz*xzScale)*scale;
                    Vector2 atlasUv=atlasUvs[vi];
                    // Exact source order after UV: shade, sky, block. Hurt mobs multiply face shade by .55 in main.js.
                    vertices.Add(new VoxelVertex(world,atlasUv,new Vector3(rp.Geometry.Shade[vi]*hurtScale,sky,block)));
                }
                if(appendIndices){int[] tr=rp.Geometry.Triangles;for(int ti=0;ti<tr.Length;ti++)indices.Add(start+tr[ti]);}
            }
        }

        /// <summary>
        /// main.js Qd(): skeletons render the bow as a separate item-atlas quad anchored to ld().
        /// It intentionally lives outside the entity skin atlas, so the shared entity batch stays unchanged.
        /// </summary>
        internal void AppendHeldItemToBatch(List<VoxelVertex> vertices,List<int> indices)=>AppendHeldItemToBatch(vertices,indices,true);
        internal void AppendHeldItemToBatch(List<VoxelVertex> vertices,List<int> indices,bool appendIndices)
        {
            if(kind!=MobKind.Skeleton||parts==null||!MainItemVisualCatalog.TryGet("item_bow",out MainItemVisualDef bow))return;
            Vector3 basePos=hasRenderPose?renderPosition:transform.position;
            float yaw=hasRenderPose?renderYawRad:transform.eulerAngles.y*Mathf.Deg2Rad;
            float walk=moving?Mathf.Sin(walkPhase*3f)*.6f:0f;
            float arm=-1.25f+walk*.08f;
            // Exact ld(r,i,u,E,s): note that source bow/arrow hand origin uses mf and baby scale,
            // not the optional entity model mscale.
            float sourceScale=(baby?.5f:1f)/16f;
            float d=13f-24f,h=d*Mathf.Cos(arm)+24f,side=d*Mathf.Sin(arm);
            float cy=Mathf.Cos(yaw),sy=Mathf.Sin(yaw);
            float rx=5f*cy+side*sy,rz=-5f*sy+side*cy;
            Vector3 origin=basePos+new Vector3(rx*sourceScale+sy*.1f,h*sourceScale,rz*sourceScale+cy*.1f);

            float axisX=.95f*sy+.31f*cy,axisZ=.95f*cy-.31f*sy;
            const float half=.34f,rot=-2.356f;float cr=Mathf.Cos(rot),sr=Mathf.Sin(rot);
            byte light=packedLight;float sky=(light>>4)/15f,block=(light&15)/15f;
            Vector3 ssb=new Vector3(.9f,sky,block);int b=vertices.Count;
            for(int row=0;row<2;row++)for(int col=0;col<2;col++)
            {
                float cx=(col-.5f)*2f*half,cy2=(.5f-row)*2f*half;
                float along=cx*cr-cy2*sr,up=cx*sr+cy2*cr;
                Vector3 pos=origin+new Vector3(axisX*along,up,axisZ*along);
                Vector2 uv=new Vector2(Mathf.Lerp(bow.UV.U0,bow.UV.U1,col),Mathf.Lerp(bow.UV.V0,bow.UV.V1,row));
                vertices.Add(new VoxelVertex(pos,uv,ssb));
            }
            if(appendIndices)
            {
                indices.Add(b);indices.Add(b+1);indices.Add(b+2);indices.Add(b+2);indices.Add(b+1);indices.Add(b+3);
                // Source emits a second reversed winding because Qd's held-item pass is explicitly double-sided.
                indices.Add(b);indices.Add(b+2);indices.Add(b+1);indices.Add(b+1);indices.Add(b+2);indices.Add(b+3);
            }
        }

        static Vector2[] CachedAtlasUvs(MobKind k,int partIndex,string activeTexture,Vector2[] sourcePixels)
        {
            var key=new AtlasUvKey(k,partIndex,activeTexture);
            if(AtlasUvCache.TryGetValue(key,out Vector2[] cached))return cached;
            cached=new Vector2[sourcePixels.Length];
            for(int i=0;i<cached.Length;i++)cached[i]=EntityAtlasLayout.UV(activeTexture,sourcePixels[i]);
            AtlasUvCache[key]=cached;return cached;
        }

        void OnEnable(){MainEntityBatchRenderer.NotifyModelDirty();}
        void OnDisable(){MainEntityBatchRenderer.NotifyModelDirty();}
        void OnDestroy(){MainEntityBatchRenderer.Unregister(this);}

        static float PitchY(MobKind k){return k==MobKind.Salmon?2.5f:k==MobKind.Shark?5.5f:0f;}
        string CurrentTextureName()
        {
            if(kind!=MobKind.Sheep)return textureName;
            if(sheepColor==MobSheepColor.White)return sheared?"entity_sheep_sheared":"entity_sheep_combined";
            string suffix=sheepColor==MobSheepColor.LightGray?"light_gray":sheepColor.ToString().ToLowerInvariant();
            return sheared?"entity_sheep_sheared_"+suffix:"entity_sheep_"+suffix;
        }
        static string TextureName(MobKind k)
        {
            switch(k){case MobKind.Pig:return "entity_pig";case MobKind.Cow:return "entity_cow";case MobKind.Sheep:return "entity_sheep_combined";case MobKind.Chicken:return "entity_chicken";case MobKind.Zombie:return "entity_zombie";case MobKind.Skeleton:return "entity_skeleton";case MobKind.Creeper:return "entity_creeper";case MobKind.Spider:return "entity_spider_combined";case MobKind.Enderman:return "entity_enderman_combined";case MobKind.Salmon:return "entity_salmon";case MobKind.Shark:return "entity_shark";default:return "entity_slime";}
        }

        static PartSpec[] Specs(MobKind k)
        {
            switch(k)
            {
                case MobKind.Pig:return Quadruped(6,new Vector3(10,8,16),6,new Vector3(8,8,8),7,11,new Vector2(0,0),new Vector2(28,8),new Vector2(0,16));
                case MobKind.Cow:return Quadruped(12,new Vector3(12,10,18),12,new Vector3(8,8,6),14,12,new Vector2(0,0),new Vector2(18,4),new Vector2(0,16));
                case MobKind.Sheep:return Quadruped(12,new Vector3(8,6,16),12,new Vector3(6,6,8),14,11,new Vector2(0,0),new Vector2(28,8),new Vector2(0,16));
                case MobKind.Chicken:return new[]{new PartSpec(new Vector3(3,5,3),new Vector3(-1.5f,0,0),new Vector2(26,0),"leg0"),new PartSpec(new Vector3(3,5,3),new Vector3(1.5f,0,0),new Vector2(26,0),"leg1"),new PartSpec(new Vector3(6,6,8),new Vector3(0,4,0),new Vector2(0,9),rotUV:true),new PartSpec(new Vector3(4,6,3),new Vector3(0,8,4),new Vector2(0,0),"head"),new PartSpec(new Vector3(4,2,2),new Vector3(0,10,6.5f),new Vector2(14,0)),new PartSpec(new Vector3(1,4,6),new Vector3(-3.5f,6,0),new Vector2(24,13)),new PartSpec(new Vector3(1,4,6),new Vector3(3.5f,6,0),new Vector2(24,13))};
                case MobKind.Zombie:return Humanoid(false);case MobKind.Skeleton:return Humanoid(true);
                case MobKind.Creeper:return new[]{new PartSpec(new Vector3(4,6,4),new Vector3(-2,0,3),new Vector2(0,16),"leg0"),new PartSpec(new Vector3(4,6,4),new Vector3(2,0,3),new Vector2(0,16),"leg1"),new PartSpec(new Vector3(4,6,4),new Vector3(-2,0,-3),new Vector2(0,16),"leg2"),new PartSpec(new Vector3(4,6,4),new Vector3(2,0,-3),new Vector2(0,16),"leg3"),new PartSpec(new Vector3(8,12,4),new Vector3(0,6,0),new Vector2(16,16)),new PartSpec(new Vector3(8,8,8),new Vector3(0,18,0),new Vector2(0,0),"head")};
                case MobKind.Spider:return Spider();
                case MobKind.Enderman:return new[]{new PartSpec(new Vector3(2,30,2),new Vector3(-2,0,0),new Vector2(56,0),"leg0"),new PartSpec(new Vector3(2,30,2),new Vector3(2,0,0),new Vector2(56,0),"leg1"),new PartSpec(new Vector3(8,12,4),new Vector3(0,30,0),new Vector2(32,16)),new PartSpec(new Vector3(2,30,2),new Vector3(-5,12,0),new Vector2(56,0),"armL"),new PartSpec(new Vector3(2,30,2),new Vector3(5,12,0),new Vector2(56,0),"armR"),new PartSpec(new Vector3(8,8,8),new Vector3(0,42,0),new Vector2(0,0),"head")};
                case MobKind.Salmon:return new[]{new PartSpec(new Vector3(2,4,3),new Vector3(0,.5f,1.5f),new Vector2(22,0)),new PartSpec(new Vector3(3,5,8),new Vector3(0,0,-4),new Vector2(0,0)),new PartSpec(new Vector3(3,5,8),new Vector3(0,0,-12),new Vector2(0,13),"tail",pivotPx:new Vector3(0,2.5f,-8)),new PartSpec(new Vector3(0,5,6),new Vector3(0,0,-19),new Vector2(20,10),"tail",pivotPx:new Vector3(0,2.5f,-8))};
                case MobKind.Shark:return new[]{new PartSpec(new Vector3(8,7,13),new Vector3(0,2,-2),new Vector2(0,0)),new PartSpec(new Vector3(7,6,6),new Vector3(0,2.5f,6),new Vector2(30,20)),new PartSpec(new Vector3(4,3,4),new Vector3(0,3.5f,10.5f),new Vector2(0,36)),new PartSpec(new Vector3(1,5,5),new Vector3(0,9,-1),new Vector2(16,36)),new PartSpec(new Vector3(5,1,4),new Vector3(-5.5f,3,2),new Vector2(42,36),rotRad:new Vector3(0,0,-.25f)),new PartSpec(new Vector3(5,1,4),new Vector3(5.5f,3,2),new Vector2(42,41),rotRad:new Vector3(0,0,.25f)),new PartSpec(new Vector3(4,5,11),new Vector3(0,3.5f,-13),new Vector2(0,20),"tail",pivotPx:new Vector3(0,5.5f,-8)),new PartSpec(new Vector3(1,10,6),new Vector3(0,1,-21),new Vector2(28,36),"tail",pivotPx:new Vector3(0,5.5f,-8))};
                default:return new[]{new PartSpec(new Vector3(8,8,8),Vector3.zero,new Vector2(0,0)),new PartSpec(new Vector3(2,2,2),new Vector3(-2.25f,4,3.1f),new Vector2(32,0)),new PartSpec(new Vector3(2,2,2),new Vector3(2.25f,4,3.1f),new Vector2(32,4)),new PartSpec(new Vector3(2,1,1),new Vector3(0,2,3.6f),new Vector2(32,8))};
            }
        }

        static PartSpec[] Humanoid(bool skeleton)
        {
            float limb=skeleton?2:4,armX=skeleton?5:6;return new[]{new PartSpec(new Vector3(limb,12,limb),new Vector3(-2,0,0),new Vector2(0,16),"leg0"),new PartSpec(new Vector3(limb,12,limb),new Vector3(2,0,0),new Vector2(0,16),"leg1"),new PartSpec(new Vector3(8,12,4),new Vector3(0,12,0),new Vector2(16,16)),new PartSpec(new Vector3(limb,12,limb),new Vector3(-armX,12,0),new Vector2(40,16),"armL"),new PartSpec(new Vector3(limb,12,limb),new Vector3(armX,12,0),new Vector2(40,16),"armR"),new PartSpec(new Vector3(8,8,8),new Vector3(0,24,0),new Vector2(0,0),"head")};
        }

        static PartSpec[] Quadruped(float legH,Vector3 body,float bodyY,Vector3 head,float headY,float headZ,Vector2 headUv,Vector2 bodyUv,Vector2 legUv)
        {
            float bx=body.x/2f-2,bz=body.z/2f-2;return new[]{new PartSpec(new Vector3(4,legH,4),new Vector3(-bx,0,bz),legUv,"leg0"),new PartSpec(new Vector3(4,legH,4),new Vector3(bx,0,bz),legUv,"leg1"),new PartSpec(new Vector3(4,legH,4),new Vector3(-bx,0,-bz),legUv,"leg2"),new PartSpec(new Vector3(4,legH,4),new Vector3(bx,0,-bz),legUv,"leg3"),new PartSpec(body,new Vector3(0,bodyY,0),bodyUv,rotUV:true),new PartSpec(head,new Vector3(0,headY,headZ),headUv,"head")};
        }

        static PartSpec[] Spider()
        {
            var a=new List<PartSpec>{new PartSpec(new Vector3(8,8,8),new Vector3(0,5,7),new Vector2(32,4),"head"),new PartSpec(new Vector3(6,6,6),new Vector3(0,6,0),new Vector2(0,0)),new PartSpec(new Vector3(10,8,12),new Vector3(0,5,-9),new Vector2(0,12))};
            float[] z={1,0,-1,-2},yaw={.7854f,.5812f,-.5812f,-.7854f};
            for(int i=0;i<8;i++){int pair=i>>1;float side=(i&1)==0?-1f:1f;a.Add(new PartSpec(new Vector3(16,3,1),new Vector3(side*11,7.5f,z[pair]),new Vector2(18,0),rotRad:new Vector3(0,yaw[pair]*-side,.7854f*-side),pivotPx:new Vector3(side*4,9,z[pair]),spiderLeg:i));}return a.ToArray();
        }
    }
}
