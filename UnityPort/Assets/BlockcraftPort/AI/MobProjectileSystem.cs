using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-style main.js Lt arrow lifecycle: speed 16, +1.5 vertical lead, gravity 12,
    /// <=.4 block substeps, 7-step block impact binary search, 8 s flight, stuck arrows for 60 s,
    /// block-release and pickup radius 2.2. World geometry is appended to the main_items batch.
    /// </summary>
    public sealed class MobProjectileSystem : MonoBehaviour
    {
        struct Arrow
        {
            public Vector3 Pos,PrevPos,Vel,Dir;
            public float Age;
            public bool Stuck,Owned;
            public Vector3Int HitBlock;
            public MobAI Source;
            public VoxelWorld World;
            public MainPlayerController Player;
        }

        static MobProjectileSystem instance;
        readonly List<Arrow> arrows=new List<Arrow>(32);
        public static int PendingPickedArrows { get; private set; }
        public static int ActiveArrowCount => instance!=null?instance.arrows.Count:0;

        public static void SpawnArrow(VoxelWorld world,MainPlayerController player,Vector3 origin,Vector3 target,MobAI source)
        {
            if(world==null||player==null)return;
            if(instance==null){var go=new GameObject("MainMobProjectiles");instance=go.AddComponent<MobProjectileSystem>();}
            Vector3 d=target-origin;float len=d.magnitude;if(len<.001f)len=1f;d/=len;
            Vector3 v=new Vector3(d.x*16f,d.y*16f+1.5f,d.z*16f);
            instance.arrows.Add(new Arrow{Pos=origin,PrevPos=origin,Vel=v,Dir=v.normalized,World=world,Player=player,Source=source});
        }

        public static void CapturePreviousSourcePoses()
        {
            if(instance==null)return;
            for(int i=0;i<instance.arrows.Count;i++){Arrow a=instance.arrows[i];a.PrevPos=a.Pos;instance.arrows[i]=a;}
        }

        public static void SourceStep(float dt)
        {
            if(instance!=null)instance.Step(dt);
        }

        void Step(float dt)
        {
            for(int i=arrows.Count-1;i>=0;i--)
            {
                Arrow a=arrows[i];a.Age+=dt;
                if(a.Stuck)
                {
                    if(a.World==null){arrows.RemoveAt(i);continue;}
                    if(a.Player!=null&&!a.Player.Dead&&Vector3.Distance(MobAI.SourceTickPlayerPosition+Vector3.up*.9f,a.Pos)<2.2f)
                    {
                        if(a.Player.IsCreative||a.Player.AddInventoryMobItem(MobItemId.Arrow,1)<=0)
                        {PendingPickedArrows++;MobItemCatalog.RecordPickup(MobItemId.Arrow,1);arrows.RemoveAt(i);continue;}
                    }
                    if(a.Age>60f){arrows.RemoveAt(i);continue;}
                    arrows[i]=a;continue;
                }

                // main.js applies gravity before the 8-second flight lifetime check.
                a.Vel.y-=12f*dt;
                if(a.Age>8f){arrows.RemoveAt(i);continue;}
                float speed=a.Vel.magnitude;int steps=Mathf.Max(1,Mathf.CeilToInt(speed*dt/.4f));float sub=dt/steps;bool remove=false;
                for(int s=0;s<steps&&!remove&&!a.Stuck;s++)
                {
                    Vector3 before=a.Pos;a.Pos+=a.Vel*sub;
                    if(HitsSolid(a.World,a.Pos))
                    {
                        Vector3 dir=a.Vel.sqrMagnitude>.000001f?a.Vel.normalized:Vector3.down;a.Dir=dir;
                        float lo=0f,hi=1f;
                        for(int q=0;q<7;q++){float mid=(lo+hi)*.5f;Vector3 p=Vector3.Lerp(before,a.Pos,mid);if(HitsSolid(a.World,p))hi=mid;else lo=mid;}
                        Vector3 impact=Vector3.Lerp(before,a.Pos,hi);
                        a.HitBlock=new Vector3Int(Mathf.FloorToInt(impact.x+dir.x*.05f),Mathf.FloorToInt(impact.y+dir.y*.05f),Mathf.FloorToInt(impact.z+dir.z*.05f));
                        // main.js: Xh=.3*sqrt(2), fp=Xh*2*.1 and the stuck shaft is pulled back by Xh-fp.
                        const float pullback=.339408f;
                        a.Pos=impact-dir*pullback;a.Vel=Vector3.zero;a.Stuck=true;a.Age=0f;break;
                    }

                    if(MobAI.ProjectileHit(a.Pos,a.Source,out MobAI mob))
                    {mob.ReceiveArrow(3f,a.Pos-a.Vel*.05f,a.Owned);remove=true;break;}
                    if(!a.Owned&&a.Player!=null&&MobAI.SourceTickPlayerSurvivalActive&&Vector3.Distance(MobAI.SourceTickPlayerPosition+Vector3.up*.9f,a.Pos)<.9f)
                    {a.Player.TakeMobDamage(3f);remove=true;break;}
                }
                if(remove)arrows.RemoveAt(i);else arrows[i]=a;
            }
        }

        static bool HitsSolid(VoxelWorld world,Vector3 p)
        {
            if(world==null)return true;
            return SourcePointCollision.Contains(world,p);
        }

        public static void AppendRenderGeometry(List<VoxelVertex> vertices,List<int> indices)
        {
            if(instance==null||instance.arrows.Count==0)return;
            if(!MainItemVisualCatalog.TryGet("item_arrow",out MainItemVisualDef arrowVisual))return;
            AtlasRect uv=arrowVisual.UV;const float h=.3f;
            for(int ai=0;ai<instance.arrows.Count;ai++)
            {
                Arrow a=instance.arrows[ai];float alpha=MobAI.SourceInterpolationAlpha;Vector3 p=Vector3.Lerp(a.PrevPos,a.Pos,alpha);
                byte packed=a.World!=null?a.World.GetPackedLight(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z)):(byte)0xF0;
                float sky=(packed>>4)/15f,blk=(packed&15)/15f;Vector3 ssb=new Vector3(1f,sky,blk);
                Vector3 d=a.Stuck&&a.Dir.sqrMagnitude>.000001f?a.Dir:(a.Vel.sqrMagnitude>.000001f?a.Vel.normalized:Vector3.forward);
                float T=d.x,R=d.y,J=d.z,O=J,G=-T,N=Mathf.Sqrt(O*O+G*G);if(N<1e-4f){O=1f;G=0f;N=1f;}O/=N;G/=N;
                Vector3 plane0=new Vector3(-G*R,G*T-O*J,O*R),plane1=new Vector3(O,0f,G);
                for(int plane=0;plane<2;plane++)
                {
                    Vector3 q=plane==0?plane0:plane1;int b=vertices.Count;
                    for(int row=0;row<2;row++)for(int col=0;col<2;col++)
                    {
                        float fa=(col-.5f)*2f*h,wa=(.5f-row)*2f*h,ta=(fa+wa)*.7071f,aa=(wa-fa)*.7071f;
                        Vector3 wp=p+ta*d+aa*q;float u=col,v=row;
                        vertices.Add(new VoxelVertex(wp,new Vector2(Mathf.Lerp(uv.U0,uv.U1,u),Mathf.Lerp(uv.V0,uv.V1,v)),ssb));
                    }
                    indices.Add(b);indices.Add(b+1);indices.Add(b+2);indices.Add(b+2);indices.Add(b+1);indices.Add(b+3);
                    indices.Add(b);indices.Add(b+2);indices.Add(b+1);indices.Add(b+1);indices.Add(b+2);indices.Add(b+3);
                }
            }
        }

        public static void NotifyBlockChanged(int x,int y,int z)
        {
            if(instance==null)return;
            for(int i=0;i<instance.arrows.Count;i++)
            {
                Arrow a=instance.arrows[i];if(!a.Stuck||a.HitBlock.x!=x||a.HitBlock.y!=y||a.HitBlock.z!=z)continue;
                a.Stuck=false;a.Vel=new Vector3(0f,-.5f,0f);a.Dir=Vector3.zero;a.Age=0f;a.PrevPos=a.Pos;instance.arrows[i]=a;
            }
        }

        void OnDestroy(){if(instance==this)instance=null;}
    }
}
