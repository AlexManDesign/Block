using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Source-exact static-world leaf scheduling from main.js Eu()/hT()/gL()/dT().
    /// Timers retain insertion order like JS Map; support search is six-neighbour BFS, depth 6;
    /// simulation runs every .25 s and performs at most 24 actual decays per simulation tick.
    /// </summary>
    public sealed class MainLeafDecay : MonoBehaviour
    {
        sealed class TimerEntry
        {
            public float Remaining;
            public LinkedListNode<Vector3Int> Node;
        }
        struct SearchNode
        {
            public Vector3Int Pos;
            public byte Depth;
            public SearchNode(Vector3Int pos,byte depth){Pos=pos;Depth=depth;}
        }

        static MainLeafDecay instance;
        public VoxelWorld World;
        readonly Dictionary<Vector3Int,TimerEntry> timers=new Dictionary<Vector3Int,TimerEntry>(512);
        readonly LinkedList<Vector3Int> order=new LinkedList<Vector3Int>();
        readonly List<Vector3Int> due=new List<Vector3Int>(128);
        readonly HashSet<Vector3Int> visited=new HashSet<Vector3Int>();
        readonly List<SearchNode> queue=new List<SearchNode>(384);
        float accumulator;
        const float TickSeconds=.25f;
        const int MaxDecaysPerTick=24;
        const int SupportDepth=6;
        static readonly Vector3Int[] Dirs={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};

        public static MainLeafDecay EnsureInstance(VoxelWorld world)
        {
            if(instance!=null){instance.World=world;return instance;}
            var go=new GameObject("MainLeafDecay");instance=go.AddComponent<MainLeafDecay>();instance.World=world;return instance;
        }

        public static void NotifyBlockChanged(int x,int y,int z)
        {
            if(instance==null||instance.World==null)return;
            instance.ScheduleNeighbours(new Vector3Int(x,y,z));
        }

        void Awake()
        {
            if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;
        }

        void OnDestroy()
        {
            if(instance==this)instance=null;
        }

        void ScheduleNeighbours(Vector3Int p)
        {
            for(int i=0;i<6;i++)Schedule(p+Dirs[i]);
        }

        void Schedule(Vector3Int p)
        {
            if(p.y<VoxelConstants.MinY||p.y>VoxelConstants.MaxY||timers.ContainsKey(p))return;
            BlockId id=World.GetBlock(p.x,p.y,p.z);if(!BlockRegistry.IsLeaf(id))return;
            // main: (.6 + Math.random()*2.4) * (Cr("fastleaves") ? 1 : 2.5)
            float scale=MainModSettings.FastLeavesEnabled?1f:2.5f;
            var e=new TimerEntry{Remaining=(.6f+Random.value*2.4f)*scale};
            e.Node=order.AddLast(p);timers.Add(p,e);
        }

        void Update()
        {
            if(World==null||timers.Count==0)return;
            accumulator+=Time.deltaTime;if(accumulator<TickSeconds)return;
            float elapsed=accumulator;accumulator=0f;due.Clear();

            // LinkedList mirrors JS Map insertion order and permits stable O(1) removal.
            for(var n=order.First;n!=null;n=n.Next)
            {
                Vector3Int p=n.Value;if(!timers.TryGetValue(p,out TimerEntry e))continue;
                e.Remaining-=elapsed;if(e.Remaining<=0f)due.Add(p);
            }

            int decayed=0;
            for(int i=0;i<due.Count;i++)
            {
                Vector3Int p=due[i];if(!timers.TryGetValue(p,out TimerEntry e))continue;
                if(decayed>=MaxDecaysPerTick){e.Remaining=.3f;continue;}
                timers.Remove(p);order.Remove(e.Node);

                if(!World.IsChunkLoadedAt(p.x,p.z))continue;
                BlockId id=World.GetBlock(p.x,p.y,p.z);
                if(!BlockRegistry.IsLeaf(id)||(World.GetMeta(p.x,p.y,p.z)&1)!=0||HasWoodSupport(p))continue;

                // main nA() records simulation edits but does not enter rA.playerEdit's synchronous path.
                if(!World.SetBlockDeferredPersistent(p.x,p.y,p.z,BlockId.Air,0))continue;
                MainTransientRenderer.SpawnBreakParticles(p.x,p.y,p.z,id,6);
                SpawnLeafDrops(p.x+.5f,p.y+.5f,p.z+.5f,id);
                decayed++;
            }
        }

        bool HasWoodSupport(Vector3Int start)
        {
            visited.Clear();queue.Clear();visited.Add(start);queue.Add(new SearchNode(start,0));
            int head=0;
            while(head<queue.Count)
            {
                SearchNode cur=queue[head++];int nextDepth=cur.Depth+1;
                for(int i=0;i<6;i++)
                {
                    Vector3Int q=cur.Pos+Dirs[i];BlockId id=World.GetBlock(q.x,q.y,q.z);
                    if(BlockRegistry.IsLeafDecaySupport(id))return true;
                    if(nextDepth<SupportDepth&&BlockRegistry.IsLeaf(id)&&visited.Add(q))queue.Add(new SearchNode(q,(byte)nextDepth));
                }
            }
            return false;
        }

        public static void SpawnLeafDrops(float x,float y,float z,BlockId id)
        {
            // main r2(): one shared random decides sapling -> stick -> apple. Jungle sapling is 2.5%,
            // other saplings 5%; apple applies only to source LEAVES/DARK_LEAVES/AZALEA classes.
            float r=Random.value,n=id==BlockId.JungleLeaves?.025f:.05f;
            if(r<n){MainTransientRenderer.SpawnDroppedBlock(x,y,z,SaplingFor(id));return;}
            if(r<n+.02f){MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Stick,1);return;}
            if(r<n+.06f&&CanDropApple(id))MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Apple,1);
        }

        static BlockId SaplingFor(BlockId id)
        {
            switch(id)
            {
                case BlockId.BirchLeaves:return BlockId.BirchSapling;
                case BlockId.SpruceLeaves:return BlockId.SpruceSapling;
                case BlockId.JungleLeaves:return BlockId.JungleSapling;
                case BlockId.AcaciaLeaves:return BlockId.AcaciaSapling;
                case BlockId.DarkOakLeaves:return BlockId.DarkOakSapling;
                default:return BlockId.OakSapling; // q6() source fallback includes cherry/azalea leaves.
            }
        }

        static bool CanDropApple(BlockId id)
        {
            return id==BlockId.OakLeaves||id==BlockId.DarkOakLeaves||id==BlockId.AzaleaLeaves||id==BlockId.FloweringAzaleaLeaves;
        }
    }
}
