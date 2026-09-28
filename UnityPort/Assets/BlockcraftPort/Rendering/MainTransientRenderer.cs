using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// Dynamic world-effect batch placed in the same main.js aI() slot as W7()/h7()/rP():
    /// terrain -> break particles/dropped blocks -> entities -> mining crack -> water -> glass.
    /// Uses persistent meshes + direct CommandBuffer submission; no per-effect GameObjects/Renderers.
    /// </summary>
    [DefaultExecutionOrder(145)]
    public sealed class MainTransientRenderer : MonoBehaviour
    {
        struct Particle
        {
            public Vector3 Pos,Vel; public float U0,V0,U1,V1,Size,Life;
        }
        struct Drop
        {
            public Vector3 Pos,Vel; public BlockId Id; public int Count; public float Age,PickAfter;
        }
        struct ItemDrop
        {
            public Vector3 Pos,Vel; public MobItemId Id; public int Count; public float Age,PickAfter;
        }
        struct GenericItemDrop
        {
            public Vector3 Pos,Vel; public string Key; public int Count,Dur; public float Age,PickAfter;
        }

        static MainTransientRenderer instance;
        static readonly Matrix4x4 Identity=Matrix4x4.identity;
        static readonly VertexAttributeDescriptor[] Layout=
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,3,0)
        };
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
        static readonly MeshUpdateFlags BufferUploadFlags=UploadFlags|MeshUpdateFlags.DontNotifyMeshUsers;

        public VoxelWorld World;
        public Camera Cam;
        public Transform Player;
        MainPlayerController playerController;

        readonly List<Particle> particles=new List<Particle>(150);
        readonly List<Drop> drops=new List<Drop>(128);
        readonly List<ItemDrop> itemDrops=new List<ItemDrop>(128);
        readonly List<GenericItemDrop> genericItemDrops=new List<GenericItemDrop>(64);
        readonly List<VoxelVertex> preV=new List<VoxelVertex>(4096);
        readonly List<int> preI=new List<int>(6144);
        readonly List<VoxelVertex> dropV=new List<VoxelVertex>(4096);
        readonly List<int> dropI=new List<int>(6144);
        readonly List<VoxelVertex> itemV=new List<VoxelVertex>(4096);
        readonly List<int> itemI=new List<int>(6144);
        readonly List<VoxelVertex> crackV=new List<VoxelVertex>(24);
        readonly List<int> crackI=new List<int>(36);
        Mesh preMesh,dropMesh,itemMesh,crackMesh;
        Material atlasMaterial,itemMaterial,crackMaterial;
        Texture2D crackTexture;
        int preVC=4096,preIC=8192,dropVC=4096,dropIC=8192,itemVC=4096,itemIC=8192,crackVC=32,crackIC=64;
        int preLastIndexCount=-1,dropLastIndexCount=-1,itemLastIndexCount=-1,crackLastIndexCount=-1;
        IndexFormat preIndexFormat=IndexFormat.UInt16,dropIndexFormat=IndexFormat.UInt16,itemIndexFormat=IndexFormat.UInt16,crackIndexFormat=IndexFormat.UInt16;
        ushort[] prePacked16,dropPacked16,itemPacked16,crackPacked16;
        bool crackActive;
        Vector3Int crackBlock;
        float crackProgress;
        bool preMeshHadGeometry,dropMeshHadGeometry,itemMeshHadGeometry;
        bool preHasGeometry,dropHasGeometry,itemHasGeometry,crackHasGeometry;
        bool crackStateInitialized,lastCrackActive;
        Vector3Int lastCrackBlock;
        int lastCrackStage=-1;

        public static MainTransientRenderer EnsureInstance(VoxelWorld world,Camera cam,Transform player)
        {
            if(instance==null)
            {
                var go=new GameObject("MainTransientRenderer");
                instance=go.AddComponent<MainTransientRenderer>();
            }
            instance.World=world;instance.Cam=cam;instance.Player=player;instance.playerController=player!=null?player.GetComponent<MainPlayerController>():null;
            if(world!=null)world.InvalidateDirectCommands();
            return instance;
        }

        // Exact source pass split: W7() particles are followed by kB(), then h7() drops/items.
        // Keeping them in independent persistent meshes lets VoxelWorld place the missing source passes
        // between them without creating per-effect Renderers or rebuilding command buffers each frame.
        public static void AppendParticles(CommandBuffer cb)
        {
            if(cb==null||instance==null||!instance.preHasGeometry||instance.preMesh==null||instance.atlasMaterial==null)return;
            cb.DrawMesh(instance.preMesh,Identity,instance.atlasMaterial,0,0);
        }
        public static void AppendDrops(CommandBuffer cb)
        {
            if(cb==null||instance==null)return;
            if(instance.dropHasGeometry&&instance.dropMesh!=null&&instance.atlasMaterial!=null)cb.DrawMesh(instance.dropMesh,Identity,instance.atlasMaterial,0,0);
            if(instance.itemHasGeometry&&instance.itemMesh!=null&&instance.itemMaterial!=null)cb.DrawMesh(instance.itemMesh,Identity,instance.itemMaterial,0,0);
        }
        public static void AppendCrack(CommandBuffer cb)
        {
            if(cb==null||instance==null||!instance.crackHasGeometry||instance.crackMesh==null||instance.crackMaterial==null)return;
            cb.DrawMesh(instance.crackMesh,Identity,instance.crackMaterial,0,0);
        }
        // Compatibility helpers retained for validators/older callers.
        public static void AppendBeforeEntities(CommandBuffer cb){AppendParticles(cb);AppendDrops(cb);}
        public static void AppendAfterEntities(CommandBuffer cb){AppendCrack(cb);}

        public static void SetMiningCrack(Vector3Int block,float progress)
        {
            if(instance==null)return;
            instance.crackBlock=block;instance.crackProgress=Mathf.Clamp01(progress);instance.crackActive=progress>0f;
        }
        public static void ClearMiningCrack()
        {
            if(instance==null)return;instance.crackActive=false;instance.crackProgress=0f;
        }

        // Exact main.js Yr(): <=150 particles, source tile quarter, same spawn ranges/lifetime/gravity.
        // The overload taking an AtlasRect is intentional: source death/love effects use BRICK/WOOL_RED
        // even though those blocks are not otherwise part of this port's serialized BlockId namespace.
        public static void SpawnSourceParticles(int x,int y,int z,AtlasRect a,int count=10)
        {
            if(instance==null||instance.World==null)return;
            for(int i=0;i<count&&instance.particles.Count<150;i++)
            {
                float u=Random.value*.7f,v=Random.value*.7f;
                instance.particles.Add(new Particle
                {
                    Pos=new Vector3(x+.2f+Random.value*.6f,y+.2f+Random.value*.6f,z+.2f+Random.value*.6f),
                    Vel=new Vector3((Random.value-.5f)*3.5f,Random.value*4f+1.5f,(Random.value-.5f)*3.5f),
                    U0=Mathf.Lerp(a.U0,a.U1,u),V0=Mathf.Lerp(a.V0,a.V1,v),
                    U1=Mathf.Lerp(a.U0,a.U1,u+.25f),V1=Mathf.Lerp(a.V0,a.V1,v+.25f),
                    Size=.07f+Random.value*.08f,Life=.45f+Random.value*.35f
                });
            }
        }

        public static void SpawnBreakParticles(int x,int y,int z,BlockId id,int count=10)
        {
            if(instance==null||instance.World==null)return;
            SpawnSourceParticles(x,y,z,BlockRegistry.Get(id).Side,count);
        }

        // Exact main.js Xe()/P7() launch + pickup constants for block drops.
        // Survival pickup now inserts into the source-style 36-slot inventory and remains in-world if full.
        public static void SpawnDroppedBlock(float x,float y,float z,BlockId id,int count=1)
        {
            if(instance==null||id==BlockId.Air||count<=0)return;
            float a=Random.value*Mathf.PI*2f;
            instance.drops.Add(new Drop{Pos=new Vector3(x,y,z),Id=id,Count=count,Vel=new Vector3(Mathf.Cos(a)*1.2f,2.4f,Mathf.Sin(a)*1.2f),PickAfter=.5f});
        }


        public static void SpawnDroppedItem(float x,float y,float z,MobItemId id,int count=1)
        {
            if(instance==null||id==MobItemId.None||count<=0)return;
            float a=Random.value*Mathf.PI*2f;
            instance.itemDrops.Add(new ItemDrop{Pos=new Vector3(x,y,z),Id=id,Count=count,Vel=new Vector3(Mathf.Cos(a)*1.2f,2.4f,Mathf.Sin(a)*1.2f),PickAfter=.5f});
        }

        public static void SpawnDroppedItemKey(float x,float y,float z,string key,int count=1,int dur=-1)
        {
            if(instance==null||string.IsNullOrEmpty(key)||count<=0)return;
            float a=Random.value*Mathf.PI*2f;
            instance.genericItemDrops.Add(new GenericItemDrop{Pos=new Vector3(x,y,z),Key=key,Count=count,Dur=dur,Vel=new Vector3(Mathf.Cos(a)*1.2f,2.4f,Mathf.Sin(a)*1.2f),PickAfter=.5f});
        }

        // main.js Xe(..., velocity): used by fishing to fling the catch toward the player.
        public static void SpawnDroppedItem(float x,float y,float z,MobItemId id,int count,Vector3 velocity)
        {
            if(instance==null||id==MobItemId.None||count<=0)return;
            instance.itemDrops.Add(new ItemDrop{Pos=new Vector3(x,y,z),Id=id,Count=count,Vel=velocity,PickAfter=.5f});
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;} instance=this;
            preMesh=CreateMesh("main_break_particles",preVC,preIC);
            dropMesh=CreateMesh("main_block_drops",dropVC,dropIC);
            itemMesh=CreateMesh("main_transient_items",itemVC,itemIC);
            crackMesh=CreateMesh("main_break_crack",crackVC,crackIC);
            var atlas=Resources.Load<Texture2D>("Voxel/atlas");
            var itemAtlas=Resources.Load<Texture2D>("Voxel/main_items");
            var sh=Shader.Find("Blockcraft/VoxelOpaque");
            if(sh==null||atlas==null||itemAtlas==null){Debug.LogError("MainTransientRenderer: VoxelOpaque/atlas/main_items missing");enabled=false;return;}
            atlas.filterMode=FilterMode.Point;atlas.wrapMode=TextureWrapMode.Clamp;atlas.anisoLevel=0;
            itemAtlas.filterMode=FilterMode.Point;itemAtlas.wrapMode=TextureWrapMode.Clamp;itemAtlas.anisoLevel=0;
            atlasMaterial=new Material(sh){name="main_transient_atlas",mainTexture=atlas};
            itemMaterial=new Material(sh){name="main_transient_items",mainTexture=itemAtlas};
            crackTexture=BuildCrackTexture();
            crackMaterial=new Material(sh){name="main_break_crack_mat",mainTexture=crackTexture};
        }

        Mesh CreateMesh(string name,int vc,int ic)
        {
            var m=new Mesh{name=name,indexFormat=IndexFormat.UInt16};m.MarkDynamic();
            m.SetVertexBufferParams(vc,Layout);m.SetIndexBufferParams(ic,IndexFormat.UInt16);m.subMeshCount=1;
            m.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);
            m.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);return m;
        }

        void LateUpdate()
        {
            if(World==null)return;
            float dt=Mathf.Min(Time.deltaTime,.1f);
            UpdateParticles(dt);UpdateDrops(dt);UpdateItemDrops(dt);UpdateGenericItemDrops(dt);

            bool hasParticles=particles.Count!=0;
            if(hasParticles||preMeshHadGeometry)
            {
                BuildPreMesh();
                preMeshHadGeometry=hasParticles;
            }
            bool hasDrops=drops.Count!=0;
            if(hasDrops||dropMeshHadGeometry)
            {
                BuildDropMesh();
                dropMeshHadGeometry=hasDrops;
            }
            bool hasItems=itemDrops.Count!=0||genericItemDrops.Count!=0||MobProjectileSystem.ActiveArrowCount!=0;
            if(hasItems||itemMeshHadGeometry)
            {
                BuildItemMesh();
                itemMeshHadGeometry=hasItems;
            }

            int crackStage=crackActive&&crackProgress>0f?Mathf.Min(9,Mathf.FloorToInt(crackProgress*10f)):-1;
            if(!crackStateInitialized||lastCrackActive!=crackActive||lastCrackBlock!=crackBlock||lastCrackStage!=crackStage)
            {
                BuildCrackMesh();
                crackStateInitialized=true;lastCrackActive=crackActive;lastCrackBlock=crackBlock;lastCrackStage=crackStage;
            }
        }

        void UpdateParticles(float dt)
        {
            for(int i=particles.Count-1;i>=0;i--)
            {
                Particle p=particles[i];p.Life-=dt;if(p.Life<=0f){particles.RemoveAt(i);continue;}
                p.Vel.y-=16f*dt;p.Pos+=p.Vel*dt;
                if(p.Vel.y<0f&&SolidAt(p.Pos.x,p.Pos.y-p.Size,p.Pos.z))
                { p.Vel.y=0f;p.Vel.x*=.6f;p.Vel.z*=.6f; }
                particles[i]=p;
            }
        }

        void UpdateDrops(float dt)
        {
            Vector3 pickup=Player!=null?Player.position+Vector3.up*.8f:Vector3.one*1e8f;
            for(int i=drops.Count-1;i>=0;i--)
            {
                Drop d=drops[i];d.Age+=dt;if(d.Age>300f||d.Pos.y<VoxelConstants.MinY-8){drops.RemoveAt(i);continue;}
                d.Vel.y-=18f*dt;float ny=d.Pos.y+d.Vel.y*dt;
                if(d.Vel.y<0f&&SolidAt(d.Pos.x,ny-.15f,d.Pos.z))
                { d.Pos.y=Mathf.Floor(ny-.15f)+1.15f;d.Vel.y=0f;float k=1f-Mathf.Min(1f,dt*8f);d.Vel.x*=k;d.Vel.z*=k; }
                else d.Pos.y=ny;
                if(BlockRegistry.IsWater(World.GetBlock(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y),Mathf.FloorToInt(d.Pos.z))))d.Vel.y=Mathf.Max(d.Vel.y,-.8f);
                float nx=d.Pos.x+d.Vel.x*dt,nz=d.Pos.z+d.Vel.z*dt;
                if(SolidAt(nx,d.Pos.y+.2f,d.Pos.z))d.Vel.x=0f;else d.Pos.x=nx;
                if(SolidAt(d.Pos.x,d.Pos.y+.2f,nz))d.Vel.z=0f;else d.Pos.z=nz;
                if(d.Age>d.PickAfter)
                {
                    Vector3 delta=pickup-d.Pos;float dist=delta.magnitude;
                    if(dist<1.45f)
                    {
                        if(playerController==null||playerController.IsCreative){drops.RemoveAt(i);continue;}
                        int left=playerController.AddInventoryBlock(d.Id,Mathf.Max(1,d.Count));
                        if(left<=0){drops.RemoveAt(i);continue;}d.Count=left;
                    }
                    if(dist<1.45f*2.2f&&dist>.0001f)d.Pos+=delta/dist*dt*3f;
                }
                drops[i]=d;
            }
        }

        void UpdateItemDrops(float dt)
        {
            Vector3 pickup=Player!=null?Player.position+Vector3.up*.8f:Vector3.one*1e8f;
            for(int i=itemDrops.Count-1;i>=0;i--)
            {
                ItemDrop d=itemDrops[i];d.Age+=dt;if(d.Age>300f||d.Pos.y<VoxelConstants.MinY-8){itemDrops.RemoveAt(i);continue;}
                d.Vel.y-=18f*dt;float ny=d.Pos.y+d.Vel.y*dt;
                if(d.Vel.y<0f&&SolidAt(d.Pos.x,ny-.15f,d.Pos.z))
                {d.Pos.y=Mathf.Floor(ny-.15f)+1.15f;d.Vel.y=0f;float k=1f-Mathf.Min(1f,dt*8f);d.Vel.x*=k;d.Vel.z*=k;}
                else d.Pos.y=ny;
                if(BlockRegistry.IsWater(World.GetBlock(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y),Mathf.FloorToInt(d.Pos.z))))d.Vel.y=Mathf.Max(d.Vel.y,-.8f);
                float nx=d.Pos.x+d.Vel.x*dt,nz=d.Pos.z+d.Vel.z*dt;
                if(SolidAt(nx,d.Pos.y+.2f,d.Pos.z))d.Vel.x=0f;else d.Pos.x=nx;
                if(SolidAt(d.Pos.x,d.Pos.y+.2f,nz))d.Vel.z=0f;else d.Pos.z=nz;
                if(d.Age>d.PickAfter)
                {
                    Vector3 delta=pickup-d.Pos;float dist=delta.magnitude;
                    if(dist<1.45f)
                    {
                        int left=playerController==null||playerController.IsCreative?0:playerController.AddInventoryMobItem(d.Id,d.Count);
                        if(left<d.Count)MobItemCatalog.RecordPickup(d.Id,d.Count-left);
                        if(left<=0){itemDrops.RemoveAt(i);continue;}
                        d.Count=left;
                    }
                    if(dist<1.45f*2.2f&&dist>.0001f)d.Pos+=delta/dist*dt*3f;
                }
                itemDrops[i]=d;
            }
        }

        void UpdateGenericItemDrops(float dt)
        {
            Vector3 pickup=Player!=null?Player.position+Vector3.up*.8f:Vector3.one*1e8f;
            for(int i=genericItemDrops.Count-1;i>=0;i--)
            {
                GenericItemDrop d=genericItemDrops[i];d.Age+=dt;if(d.Age>300f||d.Pos.y<VoxelConstants.MinY-8){genericItemDrops.RemoveAt(i);continue;}
                d.Vel.y-=18f*dt;float ny=d.Pos.y+d.Vel.y*dt;
                if(d.Vel.y<0f&&SolidAt(d.Pos.x,ny-.15f,d.Pos.z))
                {d.Pos.y=Mathf.Floor(ny-.15f)+1.15f;d.Vel.y=0f;float k=1f-Mathf.Min(1f,dt*8f);d.Vel.x*=k;d.Vel.z*=k;}
                else d.Pos.y=ny;
                if(BlockRegistry.IsWater(World.GetBlock(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y),Mathf.FloorToInt(d.Pos.z))))d.Vel.y=Mathf.Max(d.Vel.y,-.8f);
                float nx=d.Pos.x+d.Vel.x*dt,nz=d.Pos.z+d.Vel.z*dt;
                if(SolidAt(nx,d.Pos.y+.2f,d.Pos.z))d.Vel.x=0f;else d.Pos.x=nx;
                if(SolidAt(d.Pos.x,d.Pos.y+.2f,nz))d.Vel.z=0f;else d.Pos.z=nz;
                if(d.Age>d.PickAfter)
                {
                    Vector3 delta=pickup-d.Pos;float dist=delta.magnitude;
                    if(dist<1.45f)
                    {
                        int left=playerController==null||playerController.IsCreative?0:playerController.AddInventoryItem(d.Key,d.Count,d.Dur);
                        if(left<=0){genericItemDrops.RemoveAt(i);continue;}d.Count=left;
                    }
                    if(dist<1.45f*2.2f&&dist>.0001f)d.Pos+=delta/dist*dt*3f;
                }
                genericItemDrops[i]=d;
            }
        }

        bool SolidAt(float x,float y,float z)
        { return BlockRegistry.IsSolid(World.GetBlock(Mathf.FloorToInt(x),Mathf.FloorToInt(y),Mathf.FloorToInt(z))); }

        void BuildPreMesh()
        {
            preV.Clear();preI.Clear();
            Vector3 right=Cam!=null?Cam.transform.right:Vector3.right,up=Cam!=null?Cam.transform.up:Vector3.up;
            for(int i=0;i<particles.Count;i++)AddParticle(particles[i],right,up);
            Upload(preMesh,preV,preI,ref preVC,ref preIC,ref preLastIndexCount,ref preIndexFormat,ref prePacked16);
            SetPreGeometry(preI.Count!=0);
        }

        void BuildDropMesh()
        {
            dropV.Clear();dropI.Clear();
            for(int i=0;i<drops.Count;i++)AddDrop(drops[i]);
            Upload(dropMesh,dropV,dropI,ref dropVC,ref dropIC,ref dropLastIndexCount,ref dropIndexFormat,ref dropPacked16);
            SetDropGeometry(dropI.Count!=0);
        }

        void BuildItemMesh()
        {
            itemV.Clear();itemI.Clear();
            for(int i=0;i<itemDrops.Count;i++)AddItemDrop(itemDrops[i]);
            for(int i=0;i<genericItemDrops.Count;i++)AddGenericItemDrop(genericItemDrops[i]);
            MobProjectileSystem.AppendRenderGeometry(itemV,itemI);
            Upload(itemMesh,itemV,itemI,ref itemVC,ref itemIC,ref itemLastIndexCount,ref itemIndexFormat,ref itemPacked16);
            SetItemGeometry(itemI.Count!=0);
        }

        void AddGenericItemDrop(GenericItemDrop d)
        {
            if(!MainItemVisualCatalog.TryGet(d.Key,out MainItemVisualDef def))return;
            byte lgt=World.GetPackedLight(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y+.5f),Mathf.FloorToInt(d.Pos.z));
            float sky=(lgt>>4)/15f,blk=(lgt&15)/15f,bob=Mathf.Sin(d.Age*2.2f)*.05f,ang=d.Age*.9f;
            const float c=.22f;float dx=Mathf.Cos(ang)*c,dz=Mathf.Sin(ang)*c;Vector3 center=d.Pos+Vector3.up*bob;int b=itemV.Count;Vector3 ssb=new Vector3(1f,sky,blk);AtlasRect a=def.UV;
            AddV(itemV,new Vector3(center.x-dx,center.y+c*2f,center.z-dz),UV(a,0,0),ssb);
            AddV(itemV,new Vector3(center.x+dx,center.y+c*2f,center.z+dz),UV(a,1,0),ssb);
            AddV(itemV,new Vector3(center.x-dx,center.y,center.z-dz),UV(a,0,1),ssb);
            AddV(itemV,new Vector3(center.x+dx,center.y,center.z+dz),UV(a,1,1),ssb);
            Quad(itemI,b);itemI.Add(b);itemI.Add(b+2);itemI.Add(b+1);itemI.Add(b+1);itemI.Add(b+2);itemI.Add(b+3);
        }

        void AddItemDrop(ItemDrop d)
        {
            string key=MobItemCatalog.TextureKey(d.Id);if(string.IsNullOrEmpty(key)||!MainItemVisualCatalog.TryGet(key,out MainItemVisualDef def))return;
            byte lgt=World.GetPackedLight(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y+.5f),Mathf.FloorToInt(d.Pos.z));
            float sky=(lgt>>4)/15f,blk=(lgt&15)/15f,bob=Mathf.Sin(d.Age*2.2f)*.05f,ang=d.Age*.9f;
            const float c=.22f;float dx=Mathf.Cos(ang)*c,dz=Mathf.Sin(ang)*c;Vector3 center=d.Pos+Vector3.up*bob;int b=itemV.Count;Vector3 ssb=new Vector3(1f,sky,blk);AtlasRect a=def.UV;
            AddV(itemV,new Vector3(center.x-dx,center.y+c*2f,center.z-dz),UV(a,0,0),ssb);
            AddV(itemV,new Vector3(center.x+dx,center.y+c*2f,center.z+dz),UV(a,1,0),ssb);
            AddV(itemV,new Vector3(center.x-dx,center.y,center.z-dz),UV(a,0,1),ssb);
            AddV(itemV,new Vector3(center.x+dx,center.y,center.z+dz),UV(a,1,1),ssb);
            Quad(itemI,b);itemI.Add(b);itemI.Add(b+2);itemI.Add(b+1);itemI.Add(b+1);itemI.Add(b+2);itemI.Add(b+3);
        }

        void AddParticle(Particle p,Vector3 right,Vector3 up)
        {
            byte l=World.GetPackedLight(Mathf.FloorToInt(p.Pos.x),Mathf.FloorToInt(p.Pos.y),Mathf.FloorToInt(p.Pos.z));
            Vector3 ssb=new Vector3(.95f,(l>>4)/15f,(l&15)/15f);int b=preV.Count;
            AddV(preV,p.Pos-right*p.Size-up*p.Size,new Vector2(p.U0,p.V1),ssb);
            AddV(preV,p.Pos+right*p.Size-up*p.Size,new Vector2(p.U1,p.V1),ssb);
            AddV(preV,p.Pos-right*p.Size+up*p.Size,new Vector2(p.U0,p.V0),ssb);
            AddV(preV,p.Pos+right*p.Size+up*p.Size,new Vector2(p.U1,p.V0),ssb);
            Quad(preI,b);
        }

        static readonly float[] DropShade={.72f,.72f,1f,.55f,.82f,.82f};
        static readonly Vector3[,] DropCorners=
        {
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,0,1),new Vector3(1,1,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,0,0),new Vector3(0,1,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(0,1,0),new Vector3(1,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(0,1,1),new Vector3(1,1,1)},
            {new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(1,1,0),new Vector3(0,1,0)}
        };
        static readonly Vector2[,] DropUvs=
        {
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(0,0),new Vector2(1,1),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},
            {new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)}
        };

        void AddDrop(Drop d)
        {
            BlockDef def=BlockRegistry.Get(d.Id);byte lgt=World.GetPackedLight(Mathf.FloorToInt(d.Pos.x),Mathf.FloorToInt(d.Pos.y+.5f),Mathf.FloorToInt(d.Pos.z));
            float sky=(lgt>>4)/15f,blk=(lgt&15)/15f,bob=Mathf.Sin(d.Age*2.2f)*.05f,ang=d.Age*.9f;
            Vector3 center=d.Pos+Vector3.up*bob;BlockShape shape=def.Shape;
            bool flat=shape==BlockShape.Cross||shape==BlockShape.TallPlant||shape==BlockShape.Torch||shape==BlockShape.Rail||shape==BlockShape.FlatFaces||shape==BlockShape.Ladder;
            if(flat)
            {
                const float c=.22f;float dx=Mathf.Cos(ang)*c,dz=Mathf.Sin(ang)*c;AtlasRect a=def.Side;int b=dropV.Count;Vector3 ssb=new Vector3(1f,sky,blk);
                AddV(dropV,new Vector3(center.x-dx,center.y+c*2f,center.z-dz),UV(a,0,0),ssb);
                AddV(dropV,new Vector3(center.x+dx,center.y+c*2f,center.z+dz),UV(a,1,0),ssb);
                AddV(dropV,new Vector3(center.x-dx,center.y,center.z-dz),UV(a,0,1),ssb);
                AddV(dropV,new Vector3(center.x+dx,center.y,center.z+dz),UV(a,1,1),ssb);
                Quad(dropI,b);dropI.Add(b);dropI.Add(b+2);dropI.Add(b+1);dropI.Add(b+1);dropI.Add(b+2);dropI.Add(b+3);return;
            }
            const float s=.15f;float ca=Mathf.Cos(ang),sa=Mathf.Sin(ang);
            for(int f=0;f<6;f++)
            {
                AtlasRect a=f==2?def.Top:f==3?def.Bottom:def.Side;int b=dropV.Count;Vector3 ssb=new Vector3(DropShade[f],sky,blk);
                for(int k=0;k<4;k++)
                {
                    Vector3 n=DropCorners[f,k];float x=(n.x-.5f)*2f*s,y=n.y*2f*s,z=(n.z-.5f)*2f*s;
                    float rx=x*ca-z*sa,rz=x*sa+z*ca;
                    AddV(dropV,center+new Vector3(rx,y,rz),UV(a,DropUvs[f,k].x,DropUvs[f,k].y),ssb);
                }
                Quad(dropI,b);
            }
        }

        void BuildCrackMesh()
        {
            crackV.Clear();crackI.Clear();
            if(crackActive&&crackProgress>0f)
            {
                int stage=Mathf.Min(9,Mathf.FloorToInt(crackProgress*10f));float u0=stage/10f,u1=(stage+1)/10f;
                const float e=.004f;Vector3 mn=new Vector3(crackBlock.x-e,crackBlock.y-e,crackBlock.z-e),mx=new Vector3(crackBlock.x+1+e,crackBlock.y+1+e,crackBlock.z+1+e);
                AddCrackFace(new Vector3(mx.x,mn.y,mn.z),new Vector3(mx.x,mx.y,mn.z),new Vector3(mx.x,mn.y,mx.z),new Vector3(mx.x,mx.y,mx.z),u0,u1);
                AddCrackFace(new Vector3(mn.x,mn.y,mx.z),new Vector3(mn.x,mx.y,mx.z),new Vector3(mn.x,mn.y,mn.z),new Vector3(mn.x,mx.y,mn.z),u0,u1);
                AddCrackFace(new Vector3(mn.x,mx.y,mx.z),new Vector3(mx.x,mx.y,mx.z),new Vector3(mn.x,mx.y,mn.z),new Vector3(mx.x,mx.y,mn.z),u0,u1);
                AddCrackFace(new Vector3(mn.x,mn.y,mn.z),new Vector3(mx.x,mn.y,mn.z),new Vector3(mn.x,mn.y,mx.z),new Vector3(mx.x,mn.y,mx.z),u0,u1);
                AddCrackFace(new Vector3(mn.x,mn.y,mx.z),new Vector3(mx.x,mn.y,mx.z),new Vector3(mn.x,mx.y,mx.z),new Vector3(mx.x,mx.y,mx.z),u0,u1);
                AddCrackFace(new Vector3(mx.x,mn.y,mn.z),new Vector3(mn.x,mn.y,mn.z),new Vector3(mx.x,mx.y,mn.z),new Vector3(mn.x,mx.y,mn.z),u0,u1);
            }
            Upload(crackMesh,crackV,crackI,ref crackVC,ref crackIC,ref crackLastIndexCount,ref crackIndexFormat,ref crackPacked16);
            SetCrackGeometry(crackI.Count!=0);
        }


        void SetPreGeometry(bool value)
        {
            if(preHasGeometry==value)return;
            preHasGeometry=value;
            if(World!=null)World.InvalidateDirectCommands();
        }

        void SetDropGeometry(bool value)
        {
            if(dropHasGeometry==value)return;
            dropHasGeometry=value;
            if(World!=null)World.InvalidateDirectCommands();
        }

        void SetItemGeometry(bool value)
        {
            if(itemHasGeometry==value)return;
            itemHasGeometry=value;
            if(World!=null)World.InvalidateDirectCommands();
        }

        void SetCrackGeometry(bool value)
        {
            if(crackHasGeometry==value)return;
            crackHasGeometry=value;
            if(World!=null)World.InvalidateDirectCommands();
        }

        void AddCrackFace(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float u0,float u1)
        {
            int q=crackV.Count;Vector3 ssb=Vector3.one;
            AddV(crackV,a,new Vector2(u0,0),ssb);AddV(crackV,b,new Vector2(u0,1),ssb);
            AddV(crackV,c,new Vector2(u1,0),ssb);AddV(crackV,d,new Vector2(u1,1),ssb);Quad(crackI,q);
        }

        static int GrowVertexCapacity(int current,int required)
        {
            if(required<=current)return current;
            int next=Mathf.NextPowerOfTwo(required);
            return required<=65535&&next>65535?65535:next;
        }

        static ushort[] EnsurePacked16(ref ushort[] buffer,int count)
        {
            if(buffer==null||buffer.Length<count)
            {
                if(buffer!=null)ArrayPool<ushort>.Shared.Return(buffer,false);
                buffer=ArrayPool<ushort>.Shared.Rent(Mathf.Max(1,count));
            }
            return buffer;
        }

        void Upload(Mesh mesh,List<VoxelVertex> v,List<int> i,ref int vc,ref int ic,ref int lastIndexCount,ref IndexFormat currentFormat,ref ushort[] packed16)
        {
            int nvc=GrowVertexCapacity(vc,v.Count),nic=ic;if(i.Count>nic)nic=Mathf.NextPowerOfTwo(i.Count);
            IndexFormat wanted=nvc<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            bool reconfigure=nvc!=vc||nic!=ic||wanted!=currentFormat;
            if(reconfigure)
            {
                vc=nvc;ic=nic;currentFormat=wanted;mesh.indexFormat=wanted;
                mesh.SetVertexBufferParams(vc,Layout);mesh.SetIndexBufferParams(ic,wanted);mesh.subMeshCount=1;lastIndexCount=-1;
            }
            if(v.Count>0)
            {
                mesh.SetVertexBufferData(v,0,0,v.Count,0,BufferUploadFlags);
                if(wanted==IndexFormat.UInt16)
                {
                    ushort[] dst=EnsurePacked16(ref packed16,i.Count);for(int n=0;n<i.Count;n++)dst[n]=(ushort)i[n];
                    mesh.SetIndexBufferData(dst,0,0,i.Count,BufferUploadFlags);
                }
                else mesh.SetIndexBufferData(i,0,0,i.Count,BufferUploadFlags);
            }
            if(lastIndexCount!=i.Count)
            {
                mesh.SetSubMesh(0,new SubMeshDescriptor(0,i.Count,MeshTopology.Triangles),UploadFlags);
                lastIndexCount=i.Count;
            }
        }

        static void AddV(List<VoxelVertex> v,Vector3 p,Vector2 uv,Vector3 ssb){v.Add(new VoxelVertex(p,uv,ssb));}
        static Vector2 UV(AtlasRect a,float u,float v)=>new Vector2(Mathf.Lerp(a.U0,a.U1,u),Mathf.Lerp(a.V0,a.V1,v));
        static void Quad(List<int> i,int b){i.Add(b);i.Add(b+1);i.Add(b+2);i.Add(b+2);i.Add(b+1);i.Add(b+3);}

        static Texture2D BuildCrackTexture()
        {
            var t=new Texture2D(160,16,TextureFormat.RGBA32,false,true){name="main_break_cracks_runtime",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,anisoLevel=0};
            var px=new Color32[160*16];var paths=new Vector2Int[8,22];
            for(int g=0;g<8;g++)
            {
                int x=6+Mathf.FloorToInt(Hash(g,0,91)*4f),y=6+Mathf.FloorToInt(Hash(g,0,92)*4f);
                for(int s=0;s<22;s++)
                {
                    paths[g,s]=new Vector2Int(Mod16(x),Mod16(y));float r=Hash(g*131+s,7,93);
                    if(r<.25f)x++;else if(r<.5f)x--;else if(r<.75f)y++;else y--;
                }
            }
            var ordered=new Vector2Int[176];int o=0;for(int s=0;s<22;s++)for(int g=0;g<8;g++)ordered[o++]=paths[g,s];
            Color32 ink=new Color32(20,16,14,200);
            for(int stage=0;stage<10;stage++)
            {
                int count=(int)Mathf.Floor(ordered.Length*(stage+1)/10f+.5f);
                for(int k=0;k<count;k++)
                { int x=stage*16+ordered[k].x,y=15-ordered[k].y;px[y*160+x]=ink; }
            }
            t.SetPixels32(px);t.Apply(false,true);return t;
        }

        static int Mod16(int v){v%=16;return v<0?v+16:v;}
        static float Hash(int a,int b,int c)
        {
            unchecked
            {
                int x=a*374761393+b*668265263+c*2147483423;
                x=x^(int)((uint)x>>13);x*=1274126177;uint u=(uint)(x^(int)((uint)x>>16));
                return (float)(u/4294967296.0);
            }
        }

        void OnDestroy()
        {
            if(instance==this)instance=null;
            if(preMesh!=null)Destroy(preMesh);if(itemMesh!=null)Destroy(itemMesh);if(crackMesh!=null)Destroy(crackMesh);
            if(atlasMaterial!=null)Destroy(atlasMaterial);if(itemMaterial!=null)Destroy(itemMaterial);if(crackMaterial!=null)Destroy(crackMaterial);if(crackTexture!=null)Destroy(crackTexture);
            if(prePacked16!=null)ArrayPool<ushort>.Shared.Return(prePacked16,false);
            if(dropPacked16!=null)ArrayPool<ushort>.Shared.Return(dropPacked16,false);
            if(itemPacked16!=null)ArrayPool<ushort>.Shared.Return(itemPacked16,false);
            if(crackPacked16!=null)ArrayPool<ushort>.Shared.Return(crackPacked16,false);
        }
    }
}
