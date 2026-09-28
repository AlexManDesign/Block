using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// main renderer selection pass: one static 24-line unit box, submitted by the central direct
    /// CommandBuffer. Origin/size/visibility are shader uniforms, matching main.js ps/uOrigin/uSize.
    /// </summary>
    public sealed class MainSelectionOutline : MonoBehaviour
    {
        static MainSelectionOutline instance;
        static readonly Matrix4x4 Identity=Matrix4x4.identity;

        public VoxelWorld World;
        public Camera Cam;
        public MainPlayerController Controller;
        public float Reach=6f;

        Mesh mesh;
        Material material;
        bool visible;
        Vector3 lastOrigin,lastSize;
        VoxelWorld cachedAimWorld;
        Vector3 cachedAimOrigin,cachedAimDirection;
        float cachedReach;
        int cachedWorldQueryRevision=int.MinValue;
        bool cachedAimValid,cachedHasHit;
        Vector3 cachedBoxOrigin,cachedBoxSize;

        public static void AppendDirect(CommandBuffer cb)
        {
            if(cb==null||instance==null||!instance.visible||instance.mesh==null||instance.material==null)return;
            cb.DrawMesh(instance.mesh,Identity,instance.material,0,0);
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;}
            instance=this;
            mesh=new Mesh{name="MainSelectionBox"};
            mesh.vertices=new[]
            {
                new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),
                new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1)
            };
            mesh.SetIndices(new[]
            {
                0,1,1,2,2,3,3,0,
                4,5,5,6,6,7,7,4,
                0,4,1,5,2,6,3,7
            },MeshTopology.Lines,0,false);
            mesh.bounds=new Bounds(Vector3.one*.5f,Vector3.one*1.02f);
            mesh.UploadMeshData(true);

            var shader=Shader.Find("Blockcraft/MainSelectionLine");
            if(shader==null){Debug.LogError("Required Blockcraft selection shader missing");enabled=false;return;}
            material=new Material(shader){name="MainSelectionLineMaterial"};
            material.SetFloat("_Visible",0f);
        }

        void Start()
        {
            if(World!=null)World.InvalidateDirectCommands();
        }

        void LateUpdate()
        {
            if(material==null||World==null||Cam==null||Cursor.lockState!=CursorLockMode.Locked)
            {
                SetVisible(false);return;
            }
            if(Controller==null)Controller=Cam.GetComponentInParent<MainPlayerController>();
            Vector3 origin=Controller!=null?Controller.AimOrigin:Cam.transform.position;
            Vector3 direction=Controller!=null?Controller.AimDirection:Cam.transform.forward;
            int queryRevision=World.WorldQueryRevision;
            bool sameAim=cachedAimValid&&cachedAimWorld==World&&cachedWorldQueryRevision==queryRevision&&
                         cachedReach==Reach&&SameVectorExact(cachedAimOrigin,origin)&&SameVectorExact(cachedAimDirection,direction);
            if(!sameAim)
            {
                cachedAimValid=true;cachedAimWorld=World;cachedWorldQueryRevision=queryRevision;cachedReach=Reach;
                cachedAimOrigin=origin;cachedAimDirection=direction;
                VoxelHit hit;cachedHasHit=VoxelRaycast.Cast(World,origin,direction,Reach,out hit);
                if(cachedHasHit)
                {
                    VoxelBox box=VoxelShapes.Selection(hit.Id,World.GetMeta(hit.Block.x,hit.Block.y,hit.Block.z));
                    const float expand=.002f;
                    cachedBoxOrigin=new Vector3(hit.Block.x+box.X0-expand,hit.Block.y+box.Y0-expand,hit.Block.z+box.Z0-expand);
                    cachedBoxSize=new Vector3(box.X1-box.X0+expand*2f,box.Y1-box.Y0+expand*2f,box.Z1-box.Z0+expand*2f);
                }
            }
            if(!cachedHasHit){SetVisible(false);return;}
            if(cachedBoxOrigin!=lastOrigin){lastOrigin=cachedBoxOrigin;material.SetVector("_Origin",new Vector4(cachedBoxOrigin.x,cachedBoxOrigin.y,cachedBoxOrigin.z,0f));}
            if(cachedBoxSize!=lastSize){lastSize=cachedBoxSize;material.SetVector("_Size",new Vector4(cachedBoxSize.x,cachedBoxSize.y,cachedBoxSize.z,0f));}
            SetVisible(true);
        }


        static bool SameVectorExact(Vector3 a,Vector3 b)
        {return a.x==b.x&&a.y==b.y&&a.z==b.z;}

        void SetVisible(bool value)
        {
            if(visible==value)return;
            visible=value;
            if(material!=null)material.SetFloat("_Visible",value?1f:0f);
            if(World!=null)World.InvalidateDirectCommands();
        }

        void OnDestroy()
        {
            if(instance==this)instance=null;
            if(mesh!=null)Destroy(mesh);
            if(material!=null)Destroy(material);
        }
    }
}
