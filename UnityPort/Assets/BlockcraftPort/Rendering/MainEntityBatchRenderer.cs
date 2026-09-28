using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    // R45_DYNAMIC_INDEX_BANDWIDTH_2026-09-14: safe UInt16/UInt32 adaptive indices for dynamic direct meshes.
    /// <summary>
    /// main.js-style entity batch. Visible mobs are rebuilt into one dynamic GPU mesh and submitted
    /// by MainChunkDirectRenderer's command buffer. There is no MeshFilter/MeshRenderer scene object:
    /// the batch is the Unity equivalent of main.js Tt()/Bd() dynamic VBO+IBO draw.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class MainEntityBatchRenderer : MonoBehaviour
    {
        static MainEntityBatchRenderer instance;
        static int globalModelRevision;
        readonly List<MainEntityModel> models=new List<MainEntityModel>(96);
        static readonly VertexAttributeDescriptor[] VertexLayout=
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,3,0)
        };
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
        static readonly MeshUpdateFlags BufferUploadFlags=UploadFlags|MeshUpdateFlags.DontNotifyMeshUsers;
        static readonly Matrix4x4 Identity=Matrix4x4.identity;

        readonly List<VoxelVertex> vertices=new List<VoxelVertex>(32768);
        readonly List<int> indices=new List<int>(49152);
        readonly List<VoxelVertex> heldVertices=new List<VoxelVertex>(256);
        readonly List<int> heldIndices=new List<int>(512);
        readonly List<VoxelVertex> itemVertices=new List<VoxelVertex>(256);
        readonly List<int> itemIndices=new List<int>(512);
        readonly List<MainEntityModel> visibleModels=new List<MainEntityModel>(96);
        readonly List<MainEntityModel> lastVisibleModels=new List<MainEntityModel>(96);
        readonly List<uint> lastVisibleVersions=new List<uint>(96);
        readonly Vector4[] frustumPlanes=new Vector4[6];
        Mesh mesh,heldMesh,itemMesh;
        Material normalMaterial,xrayMaterial,heldMaterial,heldXrayMaterial,itemMaterial,itemXrayMaterial;
        Camera cachedCamera;
        int vertexCapacity=32768,indexCapacity=65536;
        int heldVertexCapacity=256,heldIndexCapacity=512,itemVertexCapacity=256,itemIndexCapacity=512;
        IndexFormat currentIndexFormat=IndexFormat.UInt32,heldIndexFormat=IndexFormat.UInt32,itemIndexFormat=IndexFormat.UInt32;
        ushort[] packedIndices16,heldPackedIndices16,itemPackedIndices16;
        int lastIndexCount=-1,lastHeldIndexCount=-1,lastItemIndexCount=-1;
        bool hasGeometry,hasHeldGeometry,hasItemGeometry;
        bool lastThirdPersonVisible,lastHadPlanes;
        Matrix4x4 lastCullMatrix;
        int lastGlobalModelRevision=int.MinValue;
        int stableBatchSkips,batchRebuilds;
        float lastBatchBuildMs,maxBatchBuildMs;
        int lastBatchBuildFrame=-1;

        // BC_TEMP_DEBUG_BEGIN [F3_ENTITY_BATCH_PERF] REMOVE BEFORE RELEASE
        public static int StableBatchSkips => instance!=null?instance.stableBatchSkips:0;
        public static int BatchRebuilds => instance!=null?instance.batchRebuilds:0;
        public static float LastBatchBuildMs => instance!=null?instance.lastBatchBuildMs:0f;
        public static float MaxBatchBuildMs => instance!=null?instance.maxBatchBuildMs:0f;
        public static int LastBatchBuildFrame => instance!=null?instance.lastBatchBuildFrame:-1;
        public static void ResetPerformancePeaks(){if(instance!=null)instance.maxBatchBuildMs=0f;}
        // BC_TEMP_DEBUG_END [F3_ENTITY_BATCH_PERF]

        internal static void NotifyModelDirty(){unchecked{globalModelRevision++;}}

        public static MainEntityBatchRenderer EnsureInstance()
        {
            if(instance!=null)return instance;
            var go=new GameObject("MainEntityBatch");
            instance=go.AddComponent<MainEntityBatchRenderer>();
            return instance;
        }

        public static void Register(MainEntityModel model)
        {
            if(model==null)return;
            var r=EnsureInstance();
            if(!r.models.Contains(model)){r.models.Add(model);NotifyModelDirty();}
        }

        public static void Unregister(MainEntityModel model)
        {
            if(instance!=null&&model!=null&&instance.models.Remove(model))NotifyModelDirty();
        }

        /// <summary>Append one direct entity draw in the exact source position: after terrain, before water.</summary>
        public static void AppendDirect(CommandBuffer cb,bool xray)
        {
            if(cb==null||instance==null)return;
            if(instance.hasGeometry&&instance.mesh!=null)
            {
                Material mat=xray?instance.xrayMaterial:instance.normalMaterial;
                if(mat!=null)cb.DrawMesh(instance.mesh,Identity,mat,0,0);
            }
            // main Bd()/Qd(): player/mob body uses the entity sheet first, then held geometry.
            // Block-held geometry keeps the voxel atlas; skeleton bows use the item atlas in a separate
            // persistent mesh so neither atlas has to be repacked or rebound per mob.
            if(instance.hasHeldGeometry&&instance.heldMesh!=null)
            {
                Material mat=xray?instance.heldXrayMaterial:instance.heldMaterial;
                if(mat!=null)cb.DrawMesh(instance.heldMesh,Identity,mat,0,0);
            }
            if(instance.hasItemGeometry&&instance.itemMesh!=null)
            {
                Material mat=xray?instance.itemXrayMaterial:instance.itemMaterial;
                if(mat!=null)cb.DrawMesh(instance.itemMesh,Identity,mat,0,0);
            }
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;}
            instance=this;
            mesh=new Mesh{name="main_entity_batch",indexFormat=IndexFormat.UInt32};
            mesh.MarkDynamic();
            ConfigureBuffers(IndexFormat.UInt16);
            heldMesh=new Mesh{name="main_entity_held_batch",indexFormat=IndexFormat.UInt32};
            heldMesh.MarkDynamic();
            ConfigureHeldBuffers(IndexFormat.UInt16);
            itemMesh=new Mesh{name="main_entity_item_batch",indexFormat=IndexFormat.UInt32};
            itemMesh.MarkDynamic();
            ConfigureItemBuffers(IndexFormat.UInt16);
            // World-space vertices; native renderer culling is intentionally bypassed by direct submission.
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);
            heldMesh.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);
            itemMesh.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);

            var shader=Shader.Find("Blockcraft/EntityPixel");
            if(shader==null){Debug.LogError("Blockcraft/EntityPixel shader missing");enabled=false;return;}
            var atlas=Resources.Load<Texture2D>("Voxel/Entities/entity_atlas");
            if(atlas!=null){atlas.filterMode=FilterMode.Point;atlas.wrapMode=TextureWrapMode.Clamp;atlas.anisoLevel=0;}
            normalMaterial=new Material(shader){name="main_entity_batch_mat",mainTexture=atlas};
            normalMaterial.SetFloat("_AlphaCut",.5f);
            var xrayShader=Shader.Find("Blockcraft/EntityXray");
            xrayMaterial=xrayShader!=null?new Material(xrayShader){name="main_entity_xray_mat",mainTexture=atlas}:normalMaterial;
            if(xrayMaterial!=normalMaterial)xrayMaterial.SetFloat("_AlphaCut",.5f);

            var blockAtlas=Resources.Load<Texture2D>("Voxel/atlas");
            if(blockAtlas!=null){blockAtlas.filterMode=FilterMode.Point;blockAtlas.wrapMode=TextureWrapMode.Clamp;blockAtlas.anisoLevel=0;}
            var heldShader=Shader.Find("Blockcraft/VoxelOpaque");
            if(heldShader!=null&&blockAtlas!=null)heldMaterial=new Material(heldShader){name="main_entity_held_mat",mainTexture=blockAtlas};
            heldXrayMaterial=xrayShader!=null&&blockAtlas!=null?new Material(xrayShader){name="main_entity_held_xray_mat",mainTexture=blockAtlas}:heldMaterial;
            if(heldXrayMaterial!=null&&heldXrayMaterial!=heldMaterial)heldXrayMaterial.SetFloat("_AlphaCut",.5f);
            var itemAtlas=Resources.Load<Texture2D>("Voxel/main_items");
            if(itemAtlas!=null){itemAtlas.filterMode=FilterMode.Point;itemAtlas.wrapMode=TextureWrapMode.Clamp;itemAtlas.anisoLevel=0;}
            if(itemAtlas!=null)itemMaterial=new Material(shader){name="main_entity_item_mat",mainTexture=itemAtlas};
            if(itemMaterial!=null)itemMaterial.SetFloat("_AlphaCut",.5f);
            itemXrayMaterial=xrayShader!=null&&itemAtlas!=null?new Material(xrayShader){name="main_entity_item_xray_mat",mainTexture=itemAtlas}:itemMaterial;
            if(itemXrayMaterial!=null&&itemXrayMaterial!=itemMaterial)itemXrayMaterial.SetFloat("_AlphaCut",.5f);
            if(VoxelWorld.Instance!=null)VoxelWorld.Instance.InvalidateDirectCommands();
        }

        static int GrowVertexCapacity(int current,int required)
        {
            if(required<=current)return current;
            int next=Mathf.NextPowerOfTwo(required);
            // Keep a 16-bit mesh's declared vertex buffer within the conservative 65,535 limit.
            return required<=65535&&next>65535?65535:next;
        }

        void ConfigureBuffers(IndexFormat format)
        {
            currentIndexFormat=format;
            mesh.indexFormat=format;
            mesh.SetVertexBufferParams(vertexCapacity,VertexLayout);
            mesh.SetIndexBufferParams(indexCapacity,format);
            mesh.subMeshCount=1;
            mesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);
            lastIndexCount=0;
        }

        void ConfigureHeldBuffers(IndexFormat format)
        {
            heldIndexFormat=format;
            heldMesh.indexFormat=format;
            heldMesh.SetVertexBufferParams(heldVertexCapacity,VertexLayout);
            heldMesh.SetIndexBufferParams(heldIndexCapacity,format);
            heldMesh.subMeshCount=1;
            heldMesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);
            lastHeldIndexCount=0;
        }

        void ConfigureItemBuffers(IndexFormat format)
        {
            itemIndexFormat=format;
            itemMesh.indexFormat=format;
            itemMesh.SetVertexBufferParams(itemVertexCapacity,VertexLayout);
            itemMesh.SetIndexBufferParams(itemIndexCapacity,format);
            itemMesh.subMeshCount=1;
            itemMesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);
            lastItemIndexCount=0;
        }

        IndexFormat EnsureItemBufferCapacity(int vertexCount,int indexCount)
        {
            int newV=GrowVertexCapacity(itemVertexCapacity,vertexCount),newI=itemIndexCapacity;
            if(indexCount>newI)newI=Mathf.NextPowerOfTwo(indexCount);
            IndexFormat format=newV<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            bool resized=newV!=itemVertexCapacity||newI!=itemIndexCapacity;
            if(resized||format!=itemIndexFormat){itemVertexCapacity=newV;itemIndexCapacity=newI;ConfigureItemBuffers(format);}
            return format;
        }

        IndexFormat EnsureHeldBufferCapacity(int vertexCount,int indexCount)
        {
            int newV=GrowVertexCapacity(heldVertexCapacity,vertexCount),newI=heldIndexCapacity;
            if(indexCount>newI)newI=Mathf.NextPowerOfTwo(indexCount);
            IndexFormat format=newV<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            bool resized=newV!=heldVertexCapacity||newI!=heldIndexCapacity;
            if(resized||format!=heldIndexFormat){heldVertexCapacity=newV;heldIndexCapacity=newI;ConfigureHeldBuffers(format);}
            return format;
        }

        IndexFormat EnsureBufferCapacity(int vertexCount,int indexCount)
        {
            int newV=GrowVertexCapacity(vertexCapacity,vertexCount),newI=indexCapacity;
            if(indexCount>newI)newI=Mathf.NextPowerOfTwo(indexCount);
            IndexFormat format=newV<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            bool resized=newV!=vertexCapacity||newI!=indexCapacity;
            if(resized||format!=currentIndexFormat){vertexCapacity=newV;indexCapacity=newI;ConfigureBuffers(format);}
            return format;
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

        static void Pack16(List<int> source,ushort[] target)
        {
            for(int i=0;i<source.Count;i++)target[i]=(ushort)source[i];
        }

        void LateUpdate()
        {
            lastBatchBuildMs=0f;
            if(mesh==null)return;
            cachedCamera=cachedCamera!=null?cachedCamera:Camera.main;
            bool hasPlanes=cachedCamera!=null;
            Matrix4x4 currentCull=hasPlanes?cachedCamera.cullingMatrix:default(Matrix4x4);
            var player=MainThirdPersonPlayerRenderer.Instance;
            bool thirdPersonVisible=player!=null&&player.Visible;

            // Fastest stable-frame path: no camera change and no model render-state change means the
            // previously uploaded batch is byte-for-byte the one this frame would rebuild.
            bool matrixSame=hasPlanes==lastHadPlanes&&(!hasPlanes||currentCull.Equals(lastCullMatrix));
            if(!thirdPersonVisible&&!lastThirdPersonVisible&&matrixSame&&lastGlobalModelRevision==globalModelRevision)
            {
                stableBatchSkips++;
                return;
            }
            if(hasPlanes)ExtractSourceFrustum(currentCull,frustumPlanes);

            visibleModels.Clear();
            for(int i=models.Count-1;i>=0;i--)
            {
                MainEntityModel m=models[i];
                if(m==null){models.RemoveAt(i);continue;}
                if(!m.isActiveAndEnabled||!m.gameObject.activeInHierarchy)continue;
                if(hasPlanes)
                {
                    m.GetWorldBoundsMinMax(out Vector3 mn,out Vector3 mx);
                    if(!FastFrustumAabb(frustumPlanes,mn,mx))continue;
                }
                visibleModels.Add(m);
            }

            bool sameVisibleSet=lastVisibleModels.Count==visibleModels.Count;
            if(sameVisibleSet)for(int i=0;i<visibleModels.Count;i++)if(!object.ReferenceEquals(visibleModels[i],lastVisibleModels[i])){sameVisibleSet=false;break;}
            bool stable=!thirdPersonVisible&&!lastThirdPersonVisible&&sameVisibleSet;
            if(stable)
            {
                for(int i=0;i<visibleModels.Count;i++)
                {
                    MainEntityModel m=visibleModels[i];
                    if(m.RenderVersion!=lastVisibleVersions[i]){stable=false;break;}
                }
            }
            if(stable)
            {
                lastGlobalModelRevision=globalModelRevision;lastHadPlanes=hasPlanes;if(hasPlanes)lastCullMatrix=currentCull;
                lastThirdPersonVisible=thirdPersonVisible;
                stableBatchSkips++;
                return;
            }

            double batchStart=Time.realtimeSinceStartupAsDouble;
            batchRebuilds++;
            // Mob box topology is immutable for a given visible identity set. Pose/animation/light changes
            // alter vertices only, so do not regenerate or re-upload the same index buffers every frame.
            bool reuseMobTopology=!thirdPersonVisible&&!lastThirdPersonVisible&&sameVisibleSet&&lastIndexCount>0;
            bool reuseItemTopology=!thirdPersonVisible&&!lastThirdPersonVisible&&sameVisibleSet&&lastItemIndexCount>=0;
            vertices.Clear();if(!reuseMobTopology)indices.Clear();
            heldVertices.Clear();heldIndices.Clear();
            itemVertices.Clear();if(!reuseItemTopology)itemIndices.Clear();
            for(int i=0;i<visibleModels.Count;i++)
            {
                if(reuseMobTopology)visibleModels[i].AppendToBatch(vertices,indices,false);
                else visibleModels[i].AppendToBatch(vertices,indices);
                if(reuseItemTopology)visibleModels[i].AppendHeldItemToBatch(itemVertices,itemIndices,false);
                else visibleModels[i].AppendHeldItemToBatch(itemVertices,itemIndices);
            }
            if(player!=null)player.AppendToBatch(vertices,indices,heldVertices,heldIndices);

            if(vertices.Count==0)
            {
                if(lastIndexCount!=0){mesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);lastIndexCount=0;}
            }
            else
            {
                IndexFormat indexFormat=EnsureBufferCapacity(vertices.Count,indices.Count);
                mesh.SetVertexBufferData(vertices,0,0,vertices.Count,0,BufferUploadFlags);
                if(!reuseMobTopology)
                {
                    if(indexFormat==IndexFormat.UInt16)
                    {
                        ushort[] packed=EnsurePacked16(ref packedIndices16,indices.Count);Pack16(indices,packed);
                        mesh.SetIndexBufferData(packed,0,0,indices.Count,BufferUploadFlags);
                    }
                    else mesh.SetIndexBufferData(indices,0,0,indices.Count,BufferUploadFlags);
                }
                if(lastIndexCount!=indices.Count)
                {
                    mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Count,MeshTopology.Triangles),UploadFlags);
                    lastIndexCount=indices.Count;
                }
            }
            if(heldVertices.Count==0)
            {
                if(lastHeldIndexCount!=0){heldMesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);lastHeldIndexCount=0;}
            }
            else
            {
                IndexFormat heldFormat=EnsureHeldBufferCapacity(heldVertices.Count,heldIndices.Count);
                heldMesh.SetVertexBufferData(heldVertices,0,0,heldVertices.Count,0,BufferUploadFlags);
                if(heldFormat==IndexFormat.UInt16)
                {
                    ushort[] packed=EnsurePacked16(ref heldPackedIndices16,heldIndices.Count);Pack16(heldIndices,packed);
                    heldMesh.SetIndexBufferData(packed,0,0,heldIndices.Count,BufferUploadFlags);
                }
                else heldMesh.SetIndexBufferData(heldIndices,0,0,heldIndices.Count,BufferUploadFlags);
                if(lastHeldIndexCount!=heldIndices.Count)
                {
                    heldMesh.SetSubMesh(0,new SubMeshDescriptor(0,heldIndices.Count,MeshTopology.Triangles),UploadFlags);
                    lastHeldIndexCount=heldIndices.Count;
                }
            }
            if(itemVertices.Count==0)
            {
                if(lastItemIndexCount!=0){itemMesh.SetSubMesh(0,new SubMeshDescriptor(0,0,MeshTopology.Triangles),UploadFlags);lastItemIndexCount=0;}
            }
            else
            {
                IndexFormat itemFormat=EnsureItemBufferCapacity(itemVertices.Count,itemIndices.Count);
                itemMesh.SetVertexBufferData(itemVertices,0,0,itemVertices.Count,0,BufferUploadFlags);
                if(!reuseItemTopology)
                {
                    if(itemFormat==IndexFormat.UInt16)
                    {
                        ushort[] packed=EnsurePacked16(ref itemPackedIndices16,itemIndices.Count);Pack16(itemIndices,packed);
                        itemMesh.SetIndexBufferData(packed,0,0,itemIndices.Count,BufferUploadFlags);
                    }
                    else itemMesh.SetIndexBufferData(itemIndices,0,0,itemIndices.Count,BufferUploadFlags);
                }
                if(lastItemIndexCount!=itemIndices.Count)
                {
                    itemMesh.SetSubMesh(0,new SubMeshDescriptor(0,itemIndices.Count,MeshTopology.Triangles),UploadFlags);
                    lastItemIndexCount=itemIndices.Count;
                }
            }
            bool newHasGeometry=indices.Count!=0,newHasHeldGeometry=heldIndices.Count!=0,newHasItemGeometry=itemIndices.Count!=0;
            if(newHasGeometry!=hasGeometry||newHasHeldGeometry!=hasHeldGeometry||newHasItemGeometry!=hasItemGeometry)
            {
                hasGeometry=newHasGeometry;hasHeldGeometry=newHasHeldGeometry;hasItemGeometry=newHasItemGeometry;
                if(VoxelWorld.Instance!=null)VoxelWorld.Instance.InvalidateDirectCommands();
            }

            lastVisibleModels.Clear();lastVisibleVersions.Clear();
            for(int i=0;i<visibleModels.Count;i++)
            {
                MainEntityModel m=visibleModels[i];lastVisibleModels.Add(m);lastVisibleVersions.Add(m.RenderVersion);
            }
            lastThirdPersonVisible=thirdPersonVisible;
            lastGlobalModelRevision=globalModelRevision;lastHadPlanes=hasPlanes;if(hasPlanes)lastCullMatrix=currentCull;
            lastBatchBuildMs=(float)((Time.realtimeSinceStartupAsDouble-batchStart)*1000.0);
            lastBatchBuildFrame=Time.frameCount;if(lastBatchBuildMs>maxBatchBuildMs)maxBatchBuildMs=lastBatchBuildMs;
        }

        static void ExtractSourceFrustum(Matrix4x4 m,Vector4[] planes)
        {
            Vector4 r0=m.GetRow(0),r1=m.GetRow(1),r2=m.GetRow(2),r3=m.GetRow(3);
            planes[0]=r3+r0;planes[1]=r3-r0;planes[2]=r3+r1;
            planes[3]=r3-r1;planes[4]=r3+r2;planes[5]=r3-r2;
        }

        static bool FastFrustumAabb(Vector4[] planes,Vector3 mn,Vector3 mx)
        {
            for(int i=0;i<6;i++)
            {
                Vector4 p=planes[i];
                float x=p.x>0f?mx.x:mn.x;
                float y=p.y>0f?mx.y:mn.y;
                float z=p.z>0f?mx.z:mn.z;
                if(p.x*x+p.y*y+p.z*z+p.w<0f)return false;
            }
            return true;
        }

        void OnDestroy()
        {
            if(VoxelWorld.Instance!=null)VoxelWorld.Instance.InvalidateDirectCommands();
            if(instance==this)instance=null;
            if(mesh!=null)Destroy(mesh);
            if(heldMesh!=null)Destroy(heldMesh);
            if(itemMesh!=null)Destroy(itemMesh);
            if(normalMaterial!=null)Destroy(normalMaterial);
            if(xrayMaterial!=null&&xrayMaterial!=normalMaterial)Destroy(xrayMaterial);
            if(heldMaterial!=null)Destroy(heldMaterial);
            if(heldXrayMaterial!=null&&heldXrayMaterial!=heldMaterial)Destroy(heldXrayMaterial);
            if(itemMaterial!=null)Destroy(itemMaterial);
            if(itemXrayMaterial!=null&&itemXrayMaterial!=itemMaterial)Destroy(itemXrayMaterial);
            if(packedIndices16!=null)ArrayPool<ushort>.Shared.Return(packedIndices16,false);
            if(heldPackedIndices16!=null)ArrayPool<ushort>.Shared.Return(heldPackedIndices16,false);
            if(itemPackedIndices16!=null)ArrayPool<ushort>.Shared.Return(itemPackedIndices16,false);
        }
    }
}
