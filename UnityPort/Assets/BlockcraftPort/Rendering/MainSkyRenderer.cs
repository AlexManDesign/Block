using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// Direct source-style sky pass. Sun/moon and clouds are GPU meshes referenced from the central
    /// camera CommandBuffer; there are no MeshFilter/MeshRenderer/child GameObjects. Dynamic sun/moon
    /// vertices and cloud offset uniforms update in-place without re-recording draw commands.
    /// </summary>
    public sealed class MainSkyRenderer : MonoBehaviour
    {
        static MainSkyRenderer instance;
        static readonly Matrix4x4 Identity=Matrix4x4.identity;

        public Camera Cam;
        public Transform Player;
        public VoxelWorld World;

        Material celestialMat,cloudMat;
        Texture2D celestialTex;
        Mesh celestialMesh,cloudMesh;
        readonly Vector3[] celestialVertices=new Vector3[8];
        float driftX;
        bool skyVisibilityInitialized,skyVisible=true;
        const float CloudY=192f;
        const float CloudCell=12f;
        const int CloudPeriod=24;
        const float CloudWorldPeriod=CloudCell*CloudPeriod;

        public static void AppendCelestial(CommandBuffer cb)
        {
            if(cb==null||instance==null||!instance.skyVisible||instance.celestialMesh==null||instance.celestialMat==null)return;
            cb.DrawMesh(instance.celestialMesh,Identity,instance.celestialMat,0,0);
        }

        public static void AppendClouds(CommandBuffer cb)
        {
            if(cb==null||instance==null||!instance.skyVisible||instance.cloudMesh==null||instance.cloudMat==null)return;
            cb.DrawMesh(instance.cloudMesh,Identity,instance.cloudMat,0,0);
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;}
            instance=this;
        }

        void Start()
        {
            BuildCelestial();
            BuildClouds();
            if(World!=null)World.InvalidateAllDirectCommands();
        }

        void OnDestroy()
        {
            if(instance==this)instance=null;
            if(celestialMesh!=null)Destroy(celestialMesh);
            if(cloudMesh!=null)Destroy(cloudMesh);
            if(celestialMat!=null)Destroy(celestialMat);
            if(cloudMat!=null)Destroy(cloudMat);
        }

        void LateUpdate()
        {
            if(Cam==null)Cam=Camera.main;
            if(Player==null&&Cam!=null)Player=Cam.transform.parent;
            if(World==null)World=VoxelWorld.Instance;
            if(Cam==null||Player==null)return;

            bool submerged=false;
            if(World!=null)
            {
                Vector3 cp=Cam.transform.position;
                BlockId b=World.GetBlock(Mathf.FloorToInt(cp.x),Mathf.FloorToInt(cp.y),Mathf.FloorToInt(cp.z));
                submerged=BlockRegistry.IsWater(b)||BlockRegistry.IsAquatic(b);
            }
            bool visible=!submerged;
            if(!skyVisibilityInitialized||visible!=skyVisible)
            {
                skyVisibilityInitialized=true;
                skyVisible=visible;
                if(World!=null)World.InvalidateAllDirectCommands();
            }
            if(submerged)return;

            float a=DayNight.DayTime01*Mathf.PI*2f;
            Vector3 sunDir=new Vector3(Mathf.Cos(a),Mathf.Sin(a),.25f).normalized;
            Vector3 moonDir=new Vector3(-sunDir.x,-sunDir.y,sunDir.z);
            if(celestialMesh!=null)
            {
                FillBillboard(0,sunDir,16f);
                FillBillboard(4,moonDir,11f);
                // Eight world-space vertices only; update the existing native buffer without
                // recalculating bounds or notifying renderer users. The direct command keeps the
                // same Mesh reference, so this remains visible without command-buffer rebuilds.
                celestialMesh.SetVertices(celestialVertices,0,8,
                    MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices|MeshUpdateFlags.DontNotifyMeshUsers);
            }

            driftX+=Time.deltaTime*1.4f;
            if(driftX>CloudWorldPeriod*100f)driftX%=CloudWorldPeriod;
            float x=Mathf.Floor(Player.position.x/CloudWorldPeriod)*CloudWorldPeriod+Mathf.Repeat(driftX,CloudWorldPeriod);
            float z=Mathf.Floor(Player.position.z/CloudWorldPeriod)*CloudWorldPeriod;
            if(cloudMat!=null)cloudMat.SetVector("_CloudOffset",new Vector4(x,CloudY,z,0f));
        }

        void FillBillboard(int offset,Vector3 dir,float halfSize)
        {
            Vector3 center=Cam.transform.position+dir*220f;
            Quaternion rot=Quaternion.LookRotation(-dir,Vector3.up);
            celestialVertices[offset+0]=center+rot*new Vector3(-halfSize,-halfSize,0f);
            celestialVertices[offset+1]=center+rot*new Vector3( halfSize,-halfSize,0f);
            celestialVertices[offset+2]=center+rot*new Vector3(-halfSize, halfSize,0f);
            celestialVertices[offset+3]=center+rot*new Vector3( halfSize, halfSize,0f);
        }

        void BuildCelestial()
        {
            Shader sh=Shader.Find("Blockcraft/SkySprite");
            if(sh==null)return;
            celestialTex=Resources.Load<Texture2D>("Voxel/main_celestial");
            if(celestialTex==null){Debug.LogError("Combined main sun/moon texture missing: Resources/Voxel/main_celestial.png");return;}
            celestialTex.filterMode=FilterMode.Point;celestialTex.wrapMode=TextureWrapMode.Clamp;celestialTex.anisoLevel=0;

            celestialMesh=new Mesh{name="main_celestial_batch",indexFormat=IndexFormat.UInt16};
            celestialMesh.MarkDynamic();
            celestialMesh.vertices=celestialVertices;
            celestialMesh.uv=new[]
            {
                new Vector2(0f,0f),new Vector2(.5f,0f),new Vector2(0f,1f),new Vector2(.5f,1f),
                new Vector2(.5f,0f),new Vector2(1f,0f),new Vector2(.5f,1f),new Vector2(1f,1f)
            };
            celestialMesh.triangles=new[]{0,2,1,1,2,3,4,6,5,5,6,7};
            celestialMesh.bounds=new Bounds(Vector3.zero,Vector3.one*1000000f);
            celestialMat=new Material(sh){name="main_celestial_mat",mainTexture=celestialTex};
        }

        void BuildClouds()
        {
            Shader sh=Shader.Find("Blockcraft/VoxelClouds");
            if(sh==null)return;
            cloudMesh=BuildCloudMesh();
            cloudMat=new Material(sh){name="main_cloud_mat"};
            cloudMat.SetFloat("_Alpha",.45f);
        }

        static Mesh BuildCloudMesh()
        {
            bool[,] cells=new bool[CloudPeriod,CloudPeriod];
            for(int x=0;x<CloudPeriod;x++)for(int z=0;z<CloudPeriod;z++)cells[x,z]=Hash2(x*7+3,z*11+5)<.3f;
            for(int x=0;x<CloudPeriod;x++)for(int z=0;z<CloudPeriod;z++)if(!cells[x,z])
            {
                int around=(cells[(x+1)%CloudPeriod,z]?1:0)+(cells[(x+CloudPeriod-1)%CloudPeriod,z]?1:0)+
                    (cells[x,(z+1)%CloudPeriod]?1:0)+(cells[x,(z+CloudPeriod-1)%CloudPeriod]?1:0);
                if(around>=3)cells[x,z]=true;
            }

            var v=new List<Vector3>(9000);var ti=new List<int>(14000);
            for(int gx=-38;gx<39;gx++)for(int gz=-38;gz<39;gz++)
            {
                int px=(gx%CloudPeriod+CloudPeriod)%CloudPeriod,pz=(gz%CloudPeriod+CloudPeriod)%CloudPeriod;
                if(!cells[px,pz])continue;
                float x=gx*CloudCell,z=gz*CloudCell;int n=v.Count;
                v.Add(new Vector3(x,0,z));v.Add(new Vector3(x+CloudCell,0,z));v.Add(new Vector3(x,0,z+CloudCell));v.Add(new Vector3(x+CloudCell,0,z+CloudCell));
                ti.Add(n);ti.Add(n+1);ti.Add(n+2);ti.Add(n+2);ti.Add(n+1);ti.Add(n+3);
            }
            var m=new Mesh{name="main_cloud_grid",indexFormat=IndexFormat.UInt16};
            m.SetVertices(v);m.SetTriangles(ti,0,false);
            m.bounds=new Bounds(Vector3.zero,new Vector3(1000,2,1000));m.UploadMeshData(true);return m;
        }

        static float Hash2(int x,int z)
        {
            unchecked
            {
                int r=x*374761393+z*668265263+527595518;
                r^=(int)((uint)r>>13);r*=1274126177;
                uint u=(uint)(r^(int)((uint)r>>16));
                return(float)(u/4294967296.0);
            }
        }
    }
}
