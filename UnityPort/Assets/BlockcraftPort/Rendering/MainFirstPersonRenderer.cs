using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js Z3()/b3()/fL()/sL()/W3()/cL() first-person renderer.
    /// Direct camera CommandBuffer pass: depth-only clear, primary hand/item, optional offhand.
    /// No MeshRenderer/MeshFilter is created.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public sealed class MainFirstPersonRenderer : MonoBehaviour
    {
        const int VertexCapacity=4096;
        const int IndexCapacity=8192;
        const float Pixel=1f/16f;
        static readonly Vector3[,] Corners={
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,0,1),new Vector3(1,1,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,0,0),new Vector3(0,1,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(0,1,0),new Vector3(1,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(0,1,1),new Vector3(1,1,1)},
            {new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(1,1,0),new Vector3(0,1,0)}};
        static readonly Vector2[,] FaceUv={
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}};
        static readonly float[] Shade={.72f,.72f,1f,.55f,.82f,.82f};
        static readonly int[] Quad={0,1,2,2,1,3};
        static readonly VertexAttributeDescriptor[] VertexLayout={
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,3,0)};
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices|MeshUpdateFlags.DontNotifyMeshUsers;

        struct BowInfo
        {
            public bool Valid;
            public Vector2 TipA,TipB;
            public float StringOffset;
        }

        struct VisualState
        {
            public bool IsItem,IsEmpty,IsBow,IsFishingRod;
            public BlockId Block;
            public string ItemTexture;
            public Color Tint;
            public int Identity;
        }

        public Camera Cam;
        public MainPlayerController Controller;
        public VoxelWorld World;

        Mesh primaryMesh,primaryAuxMesh,offhandMesh,offhandAuxMesh;
        Material primaryMaterial,offhandMaterial;
        Texture2D handTexture,itemAtlas;
        CommandBuffer command;
        bool attached;
        int builtPrimaryIdentity=int.MinValue,builtOffhandIdentity=int.MinValue;
        Bounds primaryBounds,offhandBounds;
        BowInfo primaryBow,offhandBow;
        float primaryEquip,offhandEquip;
        bool selectionInitialized,offhandInitialized;
        int lastPrimaryIdentity=int.MinValue,lastOffhandIdentity=int.MinValue;
        float lastPrimaryBowCharge=-1f,lastOffhandBowCharge=-1f;
        readonly List<VoxelVertex> bowAuxVertices=new List<VoxelVertex>(96);
        readonly List<int> bowAuxIndices=new List<int>(192);
        readonly List<VoxelVertex> visualVertices=new List<VoxelVertex>(384);
        readonly List<int> visualIndices=new List<int>(768);
        readonly ushort[] packedIndices=new ushort[IndexCapacity];
        bool lightStateInitialized,primaryMvpInitialized,offhandMvpInitialized;
        byte lastPackedLight;
        Matrix4x4 lastPrimaryMvp,lastOffhandMvp;

        void Awake()
        {
            if(Cam==null)Cam=GetComponent<Camera>();
            Shader sh=Shader.Find("Blockcraft/FirstPersonHeld");
            if(sh==null){Debug.LogError("Required Blockcraft first-person shader missing");enabled=false;return;}
            Texture2D atlas=Resources.Load<Texture2D>("Voxel/atlas");
            itemAtlas=Resources.Load<Texture2D>("Voxel/main_items");
            if(atlas==null||itemAtlas==null){Debug.LogError("Voxel/item atlas missing for first-person renderer");enabled=false;return;}
            atlas.filterMode=FilterMode.Point;atlas.wrapMode=TextureWrapMode.Clamp;atlas.anisoLevel=0;
            itemAtlas.filterMode=FilterMode.Point;itemAtlas.wrapMode=TextureWrapMode.Clamp;itemAtlas.anisoLevel=0;
            handTexture=BuildMainHandTexture();
            primaryMaterial=CreateMaterial(sh,atlas,itemAtlas,"main_first_person_primary_mat");
            offhandMaterial=CreateMaterial(sh,atlas,itemAtlas,"main_first_person_offhand_mat");
            primaryMesh=CreateMesh("main_first_person_primary");
            primaryAuxMesh=CreateMesh("main_first_person_primary_aux");
            offhandMesh=CreateMesh("main_first_person_offhand");
            offhandAuxMesh=CreateMesh("main_first_person_offhand_aux");
            command=new CommandBuffer{name="Blockcraft First Person Held"};
            command.ClearRenderTarget(true,false,Color.clear,1f);
            command.DrawMesh(primaryMesh,Matrix4x4.identity,primaryMaterial,0,0);
            command.DrawMesh(primaryAuxMesh,Matrix4x4.identity,primaryMaterial,0,0);
            command.DrawMesh(offhandMesh,Matrix4x4.identity,offhandMaterial,0,0);
            command.DrawMesh(offhandAuxMesh,Matrix4x4.identity,offhandMaterial,0,0);
            Attach();
        }

        static Material CreateMaterial(Shader sh,Texture2D atlas,Texture2D items,string name)
        {
            var m=new Material(sh){name=name};
            m.SetTexture("_MainTex",atlas);m.SetTexture("_HandTex",null);m.SetTexture("_ItemTex",items);m.SetColor("_Tint",Color.white);
            return m;
        }

        static Mesh CreateMesh(string name)
        {
            var m=new Mesh{name=name,indexFormat=IndexFormat.UInt16};
            m.MarkDynamic();
            m.SetVertexBufferParams(VertexCapacity,VertexLayout);
            m.SetIndexBufferParams(IndexCapacity,IndexFormat.UInt16);
            m.subMeshCount=1;
            m.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);
            m.bounds=new Bounds(Vector3.zero,Vector3.one*8f);
            return m;
        }

        void Start()
        {
            if(primaryMaterial!=null)primaryMaterial.SetTexture("_HandTex",handTexture);
            if(offhandMaterial!=null)offhandMaterial.SetTexture("_HandTex",handTexture);
        }
        void OnEnable(){Attach();}
        void OnDisable(){Detach();}
        void OnDestroy()
        {
            Detach();
            if(command!=null){command.Release();command=null;}
            DestroySafe(primaryMesh);DestroySafe(primaryAuxMesh);DestroySafe(offhandMesh);DestroySafe(offhandAuxMesh);
            DestroySafe(primaryMaterial);DestroySafe(offhandMaterial);DestroySafe(handTexture);
        }
        static void DestroySafe(UnityEngine.Object o){if(o!=null)UnityEngine.Object.Destroy(o);}

        void Attach()
        {
            if(attached||command==null)return;
            if(Cam==null)Cam=GetComponent<Camera>();
            if(Cam==null)return;
            Cam.AddCommandBuffer(CameraEvent.AfterEverything,command);attached=true;
        }
        void Detach()
        {
            if(!attached||Cam==null||command==null)return;
            Cam.RemoveCommandBuffer(CameraEvent.AfterEverything,command);attached=false;
        }

        void LateUpdate()
        {
            if(Cam==null)Cam=Camera.main;
            if(Controller==null&&Cam!=null)Controller=Cam.GetComponentInParent<MainPlayerController>();
            if(World==null)World=VoxelWorld.Instance;
            if(Cam==null||Controller==null||World==null||primaryMesh==null||primaryMaterial==null)return;
            // main aI(): Z3() is skipped whenever p.camMode != 0.
            if(Controller.CameraMode!=0){Detach();return;}
            Attach();

            VisualState primary=GetPrimaryVisual();
            VisualState offhand=GetOffhandVisual();
            UpdateEquip(primary.Identity,ref selectionInitialized,ref lastPrimaryIdentity,ref primaryEquip);
            UpdateEquip(offhand.Identity,ref offhandInitialized,ref lastOffhandIdentity,ref offhandEquip);

            if(builtPrimaryIdentity!=primary.Identity){BuildVisual(primaryMesh,primaryAuxMesh,primary,ref primaryBounds,ref primaryBow,primaryMaterial);builtPrimaryIdentity=primary.Identity;lastPrimaryBowCharge=-1f;}
            if(builtOffhandIdentity!=offhand.Identity){BuildVisual(offhandMesh,offhandAuxMesh,offhand,ref offhandBounds,ref offhandBow,offhandMaterial);builtOffhandIdentity=offhand.Identity;lastOffhandBowCharge=-1f;}

            Vector3 eye=Cam.transform.position;
            byte packed=World.GetPackedLight(Mathf.FloorToInt(eye.x),Mathf.FloorToInt(eye.y),Mathf.FloorToInt(eye.z));
            if(!lightStateInitialized||packed!=lastPackedLight)
            {
                lastPackedLight=packed;lightStateInitialized=true;
                float sky=(packed>>4)/15f, block=(packed&15)/15f;
                SetLight(primaryMaterial,sky,block);SetLight(offhandMaterial,sky,block);
            }

            float bobX=Controller.FirstPersonBobX,bobY=Controller.FirstPersonBobY,swing=Controller.FirstPersonSwing;
            float primaryCharge=primary.IsBow?Controller.FirstPersonBowCharge:0f;
            Matrix4x4 primaryModel=BuildSourceModel(primary,false,bobX,bobY,swing,primaryEquip,primaryCharge,primaryBounds);
            Matrix4x4 offhandModel=BuildSourceModel(offhand,true,bobX,bobY,0f,offhandEquip,0f,offhandBounds);
            Matrix4x4 sourceToUnityView=Matrix4x4.Scale(new Vector3(1f,1f,-1f));
            Matrix4x4 projection=Matrix4x4.Perspective(1.1f*Mathf.Rad2Deg,Cam.aspect,.05f,8f);
            Matrix4x4 gpuProjection=GL.GetGPUProjectionMatrix(projection,false);
            Matrix4x4 primaryMvp=gpuProjection*sourceToUnityView*primaryModel;
            Matrix4x4 offhandMvp=gpuProjection*sourceToUnityView*offhandModel;
            SetMatrixIfChanged(primaryMaterial,"_HeldMVP",primaryMvp,ref primaryMvpInitialized,ref lastPrimaryMvp);
            SetMatrixIfChanged(offhandMaterial,"_HeldMVP",offhandMvp,ref offhandMvpInitialized,ref lastOffhandMvp);

            if(primary.IsBow&&Mathf.Abs(primaryCharge-lastPrimaryBowCharge)>.0005f){BuildBowAux(primaryAuxMesh,primaryBow,primaryCharge);lastPrimaryBowCharge=primaryCharge;}
            if(offhand.IsBow&&Mathf.Abs(lastOffhandBowCharge)>.0005f){BuildBowAux(offhandAuxMesh,offhandBow,0f);lastOffhandBowCharge=0f;}
        }

        void UpdateEquip(int identity,ref bool initialized,ref int last,ref float equip)
        {
            if(!initialized){initialized=true;last=identity;}
            else if(identity!=last){equip=1f;last=identity;}
            equip=Mathf.Max(0f,equip-Time.unscaledDeltaTime*5f); // main Z2/q2 -= dt*5
        }

        VisualState GetPrimaryVisual()
        {
            string item=Controller.FirstPersonPrimaryItemTexture;
            if(!string.IsNullOrEmpty(item))return ItemVisual(item,Controller.FirstPersonPrimaryItemTint);
            BlockId b=Controller.SelectedBlock;
            if(b==BlockId.Air)return new VisualState{IsEmpty=true,Tint=Color.white,Identity=-1};
            return new VisualState{Block=b,Tint=Color.white,Identity=0x10000+(int)b};
        }
        VisualState GetOffhandVisual()
        {
            string item=Controller.FirstPersonOffhandItemTexture;
            if(!string.IsNullOrEmpty(item))return ItemVisual(item,Controller.FirstPersonOffhandItemTint);
            BlockId b=Controller.FirstPersonOffhandBlock;
            if(b!=BlockId.Air)return new VisualState{Block=b,Tint=Color.white,Identity=0x10000+(int)b};
            return new VisualState{Tint=Color.white,Identity=-2};
        }
        static VisualState ItemVisual(string item,Color tint)
        {
            Color32 c=tint;int h;unchecked{h=item.GetHashCode();h=h*31+c.r;h=h*31+c.g;h=h*31+c.b;h=h*31+c.a;h^=0x40000000;}
            return new VisualState{IsItem=true,IsBow=MainItemVisualCatalog.IsBow(item),IsFishingRod=MainItemVisualCatalog.IsFishingRod(item),ItemTexture=item,Tint=tint,Identity=h};
        }

        static void SetLight(Material m,float sky,float block)
        {if(m==null)return;m.SetFloat("_HeldSky",sky);m.SetFloat("_HeldBlock",block);}

        static void SetMatrixIfChanged(Material m,string property,Matrix4x4 value,ref bool initialized,ref Matrix4x4 last)
        {
            if(m==null)return;
            if(initialized&&MatrixExact(last,value))return;
            last=value;initialized=true;m.SetMatrix(property,value);
        }

        static bool MatrixExact(Matrix4x4 a,Matrix4x4 b)
        {for(int i=0;i<16;i++)if(a[i]!=b[i])return false;return true;}

        void BuildVisual(Mesh baseMesh,Mesh auxMesh,VisualState visual,ref Bounds bounds,ref BowInfo bow,Material mat)
        {
            bow=default(BowInfo);ClearMesh(auxMesh);mat.SetColor("_Tint",visual.Tint);
            var verts=visualVertices;var inds=visualIndices;verts.Clear();inds.Clear();
            int mode=0;
            if(visual.IsEmpty)
            {
                mode=1;AddBox(verts,inds,new Vector3(-.14f,-1f,-.14f),new Vector3(.14f,.05f,.14f),default(AtlasRect),true);
            }
            else if(visual.IsItem)
            {
                mode=2;
                MainItemVisualDef def;
                if(!MainItemVisualCatalog.TryGet(visual.ItemTexture,out def))
                {
                    Debug.LogWarning("Unknown main item texture: "+visual.ItemTexture);ClearMesh(baseMesh);bounds=new Bounds(Vector3.zero,Vector3.one);return;
                }
                if(visual.IsBow)BuildBowBase(def,verts,inds,out bow);else BuildExtrudedItem(def,verts,inds);
            }
            else if(visual.Block!=BlockId.Air)
            {
                BlockDef d=BlockRegistry.Get(visual.Block);
                BuildHeldBlock(d,verts,inds);
            }
            mat.SetFloat("_TextureMode",mode);
            Upload(baseMesh,verts,inds,out bounds);
            if(visual.IsBow)BuildBowAux(auxMesh,bow,0f);
        }

        static void BuildHeldBlock(BlockDef d,List<VoxelVertex> verts,List<int> inds)
        {
            switch(d.Shape)
            {
                case BlockShape.Slab: AddBoxDef(verts,inds,d,Vector3.zero,new Vector3(1,.5f,1));break;
                case BlockShape.Plate: AddBoxDef(verts,inds,d,new Vector3(.0625f,0,.0625f),new Vector3(.9375f,.0625f,.9375f));break;
                case BlockShape.Carpet: AddBoxDef(verts,inds,d,Vector3.zero,new Vector3(1,.0625f,1));break;
                case BlockShape.Bamboo: AddBoxDef(verts,inds,d,new Vector3(.4375f,0,.4375f),new Vector3(.5625f,1,.5625f));break;
                case BlockShape.Torch: BuildHeldTorch(d,verts,inds);break;
                default: for(int f=0;f<6;f++)AddFace(verts,inds,(FaceDir)f,d.Face((FaceDir)f),Vector3.zero,Vector3.one,false);break;
            }
        }
        static void AddBoxDef(List<VoxelVertex> v,List<int> i,BlockDef d,Vector3 lo,Vector3 hi)
        {for(int f=0;f<6;f++)AddFace(v,i,(FaceDir)f,d.Face((FaceDir)f),lo,hi,false);}
        static void BuildHeldTorch(BlockDef d,List<VoxelVertex> v,List<int> i)
        {
            // main CL(): upright inventory/held torch, exact .0625 shaft and .625 height.
            float a=.0625f,x0=.5f-a,x1=.5f+a,z0=.5f-a,z1=.5f+a,y1=.625f;
            AddBoxDef(v,i,d,new Vector3(x0,0,z0),new Vector3(x1,y1,z1));
        }

        static Matrix4x4 BuildSourceModel(VisualState v,bool offhand,float bobX,float bobY,float swing,float equip,float charge,Bounds b)
        {
            if(offhand&&v.Identity==-2)return Matrix4x4.zero;
            float l=offhand?-1f:1f,p=-swing*.38f-equip*.65f,c=-swing*.16f*l;
            Vector3 center=b.center;float D=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));if(D<=0f)D=1f;
            Matrix4x4 T;
            if(v.IsEmpty)
            {
                T=Matrix4x4.Translate(new Vector3(l*(.5f+bobX*.5f)+c,-.35f+bobY*.5f+p,-.7f));
                if(offhand)T*=Matrix4x4.Scale(new Vector3(-1,1,1));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(-.4f*Mathf.Rad2Deg,Vector3.forward));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis((.15f-swing*1.1f)*Mathf.Rad2Deg,Vector3.right));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(.4f*Mathf.Rad2Deg,Vector3.up));
                return T;
            }
            if(v.IsBow)
            {
                double ms=Time.realtimeSinceStartupAsDouble*1000.0;
                float j=charge>=1f?Mathf.Sin((float)(ms*.035))*.005f:0f;
                float o=charge>=1f?Mathf.Cos((float)(ms*.029))*.005f:0f;
                T=Matrix4x4.Translate(new Vector3(l*(.4f-charge*.08f+bobX*.5f)+c+j,-.34f+bobY*.5f+p+o,-.7f+charge*.06f-swing*.06f));
                if(offhand)T*=Matrix4x4.Scale(new Vector3(-1,1,1));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis((.08f-swing*1.1f)*Mathf.Rad2Deg,Vector3.right));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(1.25f*Mathf.Rad2Deg,Vector3.up));
                T*=Matrix4x4.Scale(Vector3.one*(.95f/D));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(-2.356f*Mathf.Rad2Deg,Vector3.forward));
                T*=Matrix4x4.Translate(-center);
                return T;
            }
            if(v.IsItem)
            {
                T=Matrix4x4.Translate(new Vector3(l*(.5f+bobX*.5f)+c,-.42f+bobY*.5f+p,-.78f-swing*.06f));
                if(offhand)T*=Matrix4x4.Scale(new Vector3(-1,1,1));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(-swing*1.15f*Mathf.Rad2Deg,Vector3.right));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(-.55f*Mathf.Rad2Deg,Vector3.up));
                T*=Matrix4x4.Rotate(Quaternion.AngleAxis(.35f*Mathf.Rad2Deg,Vector3.forward));
                T*=Matrix4x4.Scale(Vector3.one*(.62f/D));
                if(v.IsFishingRod)T*=Matrix4x4.Scale(new Vector3(-1,1,1));
                T*=Matrix4x4.Translate(new Vector3(-center.x,-center.y+D*.12f,-center.z));
                return T;
            }
            T=Matrix4x4.Translate(new Vector3(l*(.58f+bobX*.5f)+c,-.44f+bobY*.5f+p,-.85f-swing*.06f));
            if(offhand)T*=Matrix4x4.Scale(new Vector3(-1,1,1));
            T*=Matrix4x4.Rotate(Quaternion.AngleAxis(-swing*1.15f*Mathf.Rad2Deg,Vector3.right));
            T*=Matrix4x4.Rotate(Quaternion.AngleAxis(.78f*Mathf.Rad2Deg,Vector3.up));
            T*=Matrix4x4.Scale(Vector3.one*(.4f/D));
            T*=Matrix4x4.Translate(-center);
            return T;
        }

        static void BuildExtrudedItem(MainItemVisualDef d,List<VoxelVertex> v,List<int> ind)
        {
            float t=.5f/16f;
            AddItemQuad(v,ind,new Vector3(-.5f,.5f,t),new Vector3(.5f,.5f,t),new Vector3(-.5f,-.5f,t),new Vector3(.5f,-.5f,t),d.UV,0,0,1,1,1f,false);
            AddItemQuad(v,ind,new Vector3(.5f,.5f,-t),new Vector3(-.5f,.5f,-t),new Vector3(.5f,-.5f,-t),new Vector3(-.5f,-.5f,-t),d.UV,1,0,0,1,.72f,false);
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(d.Opaque(x,y))
            {
                float f=x/16f-.5f,w=(x+1)/16f-.5f,top=.5f-y/16f,bot=.5f-(y+1)/16f,u=(x+.5f)/16f,q=(y+.5f)/16f;
                if(!d.Opaque(x-1,y))AddItemQuad(v,ind,new Vector3(f,top,t),new Vector3(f,top,-t),new Vector3(f,bot,t),new Vector3(f,bot,-t),d.UV,u,q,u,q,.62f,true);
                if(!d.Opaque(x+1,y))AddItemQuad(v,ind,new Vector3(w,top,-t),new Vector3(w,top,t),new Vector3(w,bot,-t),new Vector3(w,bot,t),d.UV,u,q,u,q,.62f,true);
                if(!d.Opaque(x,y-1))AddItemQuad(v,ind,new Vector3(f,top,-t),new Vector3(w,top,-t),new Vector3(f,top,t),new Vector3(w,top,t),d.UV,u,q,u,q,.88f,true);
                if(!d.Opaque(x,y+1))AddItemQuad(v,ind,new Vector3(f,bot,t),new Vector3(w,bot,t),new Vector3(f,bot,-t),new Vector3(w,bot,-t),d.UV,u,q,u,q,.55f,true);
            }
        }

        static void BuildBowBase(MainItemVisualDef d,List<VoxelVertex> v,List<int> ind,out BowInfo info)
        {
            ushort[] stringBits=new ushort[16];var stringPixels=new List<Vector2Int>(32);
            Vector2 tipA=new Vector2(15,0),tipB=new Vector2(0,15);float max=-1e9f,min=1e9f;
            for(int y=0;y<16;y++)
            {
                var runs=new List<Vector2Int>(4);int start=-1;
                for(int x=0;x<=16;x++)
                {
                    bool on=x<16&&d.Opaque(x,y);
                    if(on&&start<0)start=x;
                    else if(!on&&start>=0){runs.Add(new Vector2Int(start,x-1));start=-1;}
                }
                if(runs.Count>=2)
                {
                    Vector2Int r=runs[runs.Count-1];
                    for(int x=r.x;x<=r.y;x++){stringBits[y]|=(ushort)(1<<x);stringPixels.Add(new Vector2Int(x,y));}
                }
                for(int x=0;x<16;x++)if(d.Opaque(x,y))
                {
                    float diag=x-y;if(diag>max){max=diag;tipA=new Vector2(x,y);}if(diag<min){min=diag;tipB=new Vector2(x,y);}
                }
            }
            float t=.5f/16f;
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            {
                bool h=d.Opaque(x,y)&&(stringBits[y]&(1<<x))==0;if(!h)continue;
                float f=x/16f-.5f,w=(x+1)/16f-.5f,top=.5f-y/16f,bot=.5f-(y+1)/16f,u=(x+.5f)/16f,q=(y+.5f)/16f;
                AddItemQuad(v,ind,new Vector3(f,top,t),new Vector3(w,top,t),new Vector3(f,bot,t),new Vector3(w,bot,t),d.UV,u,q,u,q,1f,true);
                AddItemQuad(v,ind,new Vector3(w,top,-t),new Vector3(f,top,-t),new Vector3(w,bot,-t),new Vector3(f,bot,-t),d.UV,u,q,u,q,.72f,true);
                if(!IsBowBody(d,stringBits,x-1,y))AddItemQuad(v,ind,new Vector3(f,top,t),new Vector3(f,top,-t),new Vector3(f,bot,t),new Vector3(f,bot,-t),d.UV,u,q,u,q,.62f,true);
                if(!IsBowBody(d,stringBits,x+1,y))AddItemQuad(v,ind,new Vector3(w,top,-t),new Vector3(w,top,t),new Vector3(w,bot,-t),new Vector3(w,bot,t),d.UV,u,q,u,q,.62f,true);
                if(!IsBowBody(d,stringBits,x,y-1))AddItemQuad(v,ind,new Vector3(f,top,-t),new Vector3(w,top,-t),new Vector3(f,top,t),new Vector3(w,top,t),d.UV,u,q,u,q,.88f,true);
                if(!IsBowBody(d,stringBits,x,y+1))AddItemQuad(v,ind,new Vector3(f,bot,t),new Vector3(w,bot,t),new Vector3(f,bot,-t),new Vector3(w,bot,-t),d.UV,u,q,u,q,.55f,true);
            }
            Vector2 A=PixelCenter(tipA),B=PixelCenter(tipB),dir=B-A;float len=dir.magnitude;if(len<1e-6f)len=1f;dir/=len;
            var offs=new List<float>(stringPixels.Count);
            for(int n=0;n<stringPixels.Count;n++)
            {
                Vector2 q=PixelCenter(stringPixels[n]),w=q-A;float along=Vector2.Dot(w,dir);Vector2 perp=w-dir*along;
                offs.Add(perp.x*.7071f+perp.y*-.7071f);
            }
            offs.Sort();float median=offs.Count>0?offs[offs.Count>>1]:.17f;
            info=new BowInfo{Valid=true,TipA=A,TipB=B,StringOffset=median+.03f};
        }
        static bool IsBowBody(MainItemVisualDef d,ushort[] stringBits,int x,int y)
        {return x>=0&&x<16&&y>=0&&y<16&&d.Opaque(x,y)&&(stringBits[y]&(1<<x))==0;}
        static Vector2 PixelCenter(Vector2 p)=>new Vector2((p.x+.5f)/16f-.5f,.5f-(p.y+.5f)/16f);

        void BuildBowAux(Mesh mesh,BowInfo info,float charge)
        {
            if(mesh==null||!info.Valid){ClearMesh(mesh);return;}
            MainItemVisualDef white,arrow;if(!MainItemVisualCatalog.TryGet("__white",out white)||!MainItemVisualCatalog.TryGet("item_arrow",out arrow)){ClearMesh(mesh);return;}
            var v=bowAuxVertices;var ind=bowAuxIndices;v.Clear();ind.Clear();
            float i=.7071f,u=-.7071f,E=info.StringOffset;
            Vector3 s=new Vector3(info.TipA.x+i*E,info.TipA.y+u*E,0),c=new Vector3(info.TipB.x+i*E,info.TipB.y+u*E,0);
            float f=charge*.3f;Vector3 w=(s+c)*.5f+new Vector3(i,u,0)*f;
            AddRibbonPair(v,ind,s,w,.012f,white.UV,.22f);AddRibbonPair(v,ind,w,c,.012f,white.UV,.22f);
            if(charge>0f)AddBowArrow(v,ind,w,arrow.UV);
            Bounds dummy;Upload(mesh,v,ind,out dummy);
        }
        static void AddRibbonPair(List<VoxelVertex> v,List<int> ind,Vector3 a,Vector3 b,float half,AtlasRect uv,float shade)
        {
            Vector3 dir=b-a;float len=dir.magnitude;if(len<1e-6f)return;dir/=len;
            Vector3 side=new Vector3(-dir.y,dir.x,0);float sl=side.magnitude;if(sl<1e-6f)sl=1;side=side/sl*half;
            Vector3 cross=Vector3.Cross(dir,side);float cl=cross.magnitude;if(cl<1e-6f)cl=1;cross=cross/cl*half;
            AddRibbon(v,ind,a,b,side,uv,shade);AddRibbon(v,ind,a,b,cross,uv,shade);
        }
        static void AddRibbon(List<VoxelVertex> v,List<int> ind,Vector3 a,Vector3 b,Vector3 d,AtlasRect uv,float shade)
        {
            int n=v.Count;Vector3 shadeVec=new Vector3(shade,1,1);
            v.Add(new VoxelVertex(a-d,MainItemUV(uv,0,0),shadeVec));v.Add(new VoxelVertex(a+d,MainItemUV(uv,1,0),shadeVec));
            v.Add(new VoxelVertex(b-d,MainItemUV(uv,0,1),shadeVec));v.Add(new VoxelVertex(b+d,MainItemUV(uv,1,1),shadeVec));
            AddDoubleQuad(ind,n);
        }
        static void AddBowArrow(List<VoxelVertex> v,List<int> ind,Vector3 w,AtlasRect uv)
        {
            float i=.7071f,u=-.7071f,c=-i,B=-u,d=.26f;
            float X=w.x+c*d*1.4142f,Y=w.y+B*d*1.4142f,Z=0;
            for(int r=0;r<2;r++)
            {
                int n=v.Count;Vector3 J=r==0?new Vector3(-B,c,0):new Vector3(0,0,1);
                for(int g=0;g<2;g++)for(int q=0;q<2;q++)
                {
                    float yy=(q-.5f)*2*d,xx=(.5f-g)*2*d,k=(yy+xx)*.7071f,z=(xx-yy)*.7071f;
                    Vector3 p=new Vector3(X+k*c+z*J.x,Y+k*B+z*J.y,Z+z*J.z);
                    v.Add(new VoxelVertex(p,MainItemUV(uv,q,g),new Vector3(1,1,1)));
                }
                AddDoubleQuad(ind,n);
            }
        }

        static void AddItemQuad(List<VoxelVertex> v,List<int> ind,Vector3 a,Vector3 b,Vector3 c,Vector3 d,AtlasRect tex,float u0,float v0,float u1,float v1,float shade,bool constantUv)
        {
            int n=v.Count;
            if(constantUv)
            {
                Vector2 uv=MainItemUV(tex,u0,v0);v.Add(new VoxelVertex(a,uv,new Vector3(shade,1,1)));v.Add(new VoxelVertex(b,uv,new Vector3(shade,1,1)));v.Add(new VoxelVertex(c,uv,new Vector3(shade,1,1)));v.Add(new VoxelVertex(d,uv,new Vector3(shade,1,1)));
            }
            else
            {
                v.Add(new VoxelVertex(a,MainItemUV(tex,u0,v0),new Vector3(shade,1,1)));
                v.Add(new VoxelVertex(b,MainItemUV(tex,u1,v0),new Vector3(shade,1,1)));
                v.Add(new VoxelVertex(c,MainItemUV(tex,u0,v1),new Vector3(shade,1,1)));
                v.Add(new VoxelVertex(d,MainItemUV(tex,u1,v1),new Vector3(shade,1,1)));
            }
            for(int k=0;k<6;k++)ind.Add(n+Quad[k]);
        }
        static void AddDoubleQuad(List<int> ind,int n)
        {ind.Add(n);ind.Add(n+1);ind.Add(n+2);ind.Add(n+2);ind.Add(n+1);ind.Add(n+3);ind.Add(n);ind.Add(n+2);ind.Add(n+1);ind.Add(n+1);ind.Add(n+2);ind.Add(n+3);}

        void Upload(Mesh mesh,List<VoxelVertex> verts,List<int> inds,out Bounds bounds)
        {
            bounds=BoundsFrom(verts);if(mesh==null)return;
            if(verts.Count>VertexCapacity||inds.Count>IndexCapacity){Debug.LogError("First-person mesh capacity exceeded: "+verts.Count+"/"+inds.Count);ClearMesh(mesh);return;}
            if(verts.Count>0)mesh.SetVertexBufferData(verts,0,0,verts.Count,0,UploadFlags);
            if(inds.Count>0)
            {
                for(int i=0;i<inds.Count;i++)packedIndices[i]=(ushort)inds[i];
                mesh.SetIndexBufferData(packedIndices,0,0,inds.Count,UploadFlags);
            }
            mesh.SetSubMesh(0,new SubMeshDescriptor(0,inds.Count,MeshTopology.Triangles),UploadFlags);mesh.bounds=bounds;
        }
        static void ClearMesh(Mesh mesh){if(mesh!=null)mesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);}
        static Bounds BoundsFrom(List<VoxelVertex> v)
        {
            if(v==null||v.Count==0)return new Bounds(Vector3.zero,Vector3.one);
            Vector3 lo=v[0].Position,hi=lo;for(int i=1;i<v.Count;i++){lo=Vector3.Min(lo,v[i].Position);hi=Vector3.Max(hi,v[i].Position);}return new Bounds((lo+hi)*.5f,hi-lo);
        }

        static void AddBox(List<VoxelVertex> verts,List<int> inds,Vector3 lo,Vector3 hi,AtlasRect tex,bool hand)
        {for(int f=0;f<6;f++)AddFace(verts,inds,(FaceDir)f,tex,lo,hi,hand);}
        static void AddFace(List<VoxelVertex> verts,List<int> inds,FaceDir face,AtlasRect tex,Vector3 lo,Vector3 hi,bool hand)
        {
            int f=(int)face,b=verts.Count;
            for(int i=0;i<4;i++)
            {
                Vector3 c=Corners[f,i];Vector3 p=new Vector3(c.x>.5f?hi.x:lo.x,c.y>.5f?hi.y:lo.y,c.z>.5f?hi.z:lo.z);Vector2 q=FaceUv[f,i];
                Vector2 uv=hand?new Vector2(q.x,1f-q.y):MainUV(tex,q.x,q.y);verts.Add(new VoxelVertex(p,uv,new Vector3(Shade[f],1f,1f)));
            }
            for(int i=0;i<6;i++)inds.Add(b+Quad[i]);
        }
        static Vector2 MainUV(AtlasRect r,float u,float v)=>new Vector2(Mathf.Lerp(r.U0,r.U1,u),Mathf.Lerp(r.V1,r.V0,v));
        static Vector2 MainItemUV(AtlasRect r,float u,float v)=>new Vector2(Mathf.Lerp(r.U0,r.U1,u),Mathf.Lerp(r.V1,r.V0,v));

        static Texture2D BuildMainHandTexture()
        {
            var tex=new Texture2D(16,16,TextureFormat.RGBA32,false,true){name="main_hand_Uu",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,anisoLevel=0};var px=new Color32[256];
            for(int g=0;g<16;g++)for(int x=0;x<16;x++){float h=1f+(Hash01(x,g,57)-.5f)*.12f;byte r=(byte)Mathf.Clamp(Mathf.RoundToInt((g<5?72f:199f)*h),0,255);byte gg=(byte)Mathf.Clamp(Mathf.RoundToInt((g<5?116f:146f)*h),0,255);byte b=(byte)Mathf.Clamp(Mathf.RoundToInt((g<5?186f:109f)*h),0,255);px[(15-g)*16+x]=new Color32(r,gg,b,255);}
            tex.SetPixels32(px);tex.Apply(false,true);return tex;
        }
        static float Hash01(int x,int y,int seed)
        {
            unchecked{int t=x*374761393+y*668265263+seed*2147483423;t^=(int)((uint)t>>13);t*=1274126177;uint u=(uint)(t^(int)((uint)t>>16));return u/4294967296f;}
        }
    }
}
