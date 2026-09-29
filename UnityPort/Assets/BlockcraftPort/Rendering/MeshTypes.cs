using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
namespace BlockcraftPort
{
    // Exactly main.js vertex layout: pos3 + uv2 + shade + sky + block = 8 float32 = 32 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelVertex
    {
        public Vector3 Position;
        public Vector2 UV;
        public Vector3 ShadeSkyBlock;
        public VoxelVertex(Vector3 position, Vector2 uv, Vector3 shadeSkyBlock)
        {
            Position = position; UV = uv; ShadeSkyBlock = shadeSkyBlock;
        }
    }

    public struct SectionSnapshot
    {
        public sealed class SharedBuffer
        {
            public readonly ushort[] Voxels; public readonly byte[] Meta; public readonly byte[] Lights;
            public readonly int StrideY;
            int references;
            public SharedBuffer(ushort[] voxels,byte[] meta,byte[] lights,int strideY,int references)
            {
                Voxels=voxels;Meta=meta;Lights=lights;StrideY=strideY;this.references=references;
            }
            public void Release()
            {
                if(Interlocked.Decrement(ref references)!=0)return;
                ArrayPool<ushort>.Shared.Return(Voxels,false);
                ArrayPool<byte>.Shared.Return(Meta,false);
                ArrayPool<byte>.Shared.Return(Lights,false);
            }
        }

        public readonly SectionKey Key; public readonly int Stamp;
        public readonly ushort[] Voxels; public readonly byte[] Meta; public readonly byte[] Lights;
        readonly int strideY,yOffset;
        readonly SharedBuffer shared;
        public bool Valid=>Voxels!=null;

        public SectionSnapshot(SectionKey key,int stamp,ushort[] voxels,byte[] meta,byte[] lights)
        {
            Key=key;Stamp=stamp;Voxels=voxels;Meta=meta;Lights=lights;strideY=18;yOffset=0;shared=null;
        }
        public SectionSnapshot(SectionKey key,int stamp,SharedBuffer shared,int yOffset)
        {
            Key=key;Stamp=stamp;this.shared=shared;Voxels=shared.Voxels;Meta=shared.Meta;Lights=shared.Lights;strideY=shared.StrideY;this.yOffset=yOffset;
        }
        public void Release()
        {
            if(!Valid)return;
            if(shared!=null){shared.Release();return;}
            ArrayPool<ushort>.Shared.Return(Voxels,false);
            ArrayPool<byte>.Shared.Return(Meta,false);
            ArrayPool<byte>.Shared.Return(Lights,false);
        }
        // The mesher is a 1:1 port of meshWorker and therefore works in the reference (source)
        // coordinate frame. Chunks are stored in Unity space (Z reflected, directional metadata
        // mirrored), so this view reflects Z back and un-mirrors metadata on read. ChunkMesher
        // reflects only the final vertex positions; texture coordinates, face order and every
        // metadata-driven shape then match meshWorker exactly (tools/parity synthetic test).
        int Index(int x,int y,int z)=>(x*18+(17-z))*strideY+yOffset+y;
        public BlockId Get(int x,int y,int z)=>(BlockId)Voxels[Index(x,y,z)];
        public byte GetMeta(int x,int y,int z){int i=Index(x,y,z);return SourceCoords.UnityMetaToSource((BlockId)Voxels[i],Meta[i]);}
        public byte GetLight(int x,int y,int z)=>Lights[Index(x,y,z)];
    }

    public struct MeshBuildResult
    {
        public SectionKey Key; public int Stamp;
        public VoxelVertex[] Vertices; public int VertexCount;
        public int[] Opaque,Water,Transparent;
        public int OpaqueCount,WaterCount,TransparentCount;
        public bool Empty=>VertexCount==0;

        // Meshing is the hottest producer in the streaming path. Keep its large arrays out of the
        // managed allocator: worker results rent buffers and ownership is either transferred into
        // ChunkRender or returned here when a stale/off-screen result is discarded.
        public void ReleaseBuffers()
        {
            if(Vertices!=null&&Vertices.Length!=0)ArrayPool<VoxelVertex>.Shared.Return(Vertices,false);
            if(Opaque!=null&&Opaque.Length!=0)ArrayPool<int>.Shared.Return(Opaque,false);
            if(Water!=null&&Water.Length!=0)ArrayPool<int>.Shared.Return(Water,false);
            if(Transparent!=null&&Transparent.Length!=0)ArrayPool<int>.Shared.Return(Transparent,false);
            Vertices=null;Opaque=null;Water=null;Transparent=null;
            VertexCount=OpaqueCount=WaterCount=TransparentCount=0;
        }

        public void DetachBuffers()
        {
            Vertices=null;Opaque=null;Water=null;Transparent=null;
            VertexCount=OpaqueCount=WaterCount=TransparentCount=0;
        }
    }

    /// <summary>
    /// Whole-chunk interleaved payload assembled on the mesh worker. main.js meshWorker returns the
    /// already-concatenated ov/oi/wv/wi/tv/ti arrays; keeping the equivalent merge off Unity's main
    /// thread removes the largest copy/index-fixup step from a streaming frame.
    /// </summary>
    public sealed class ChunkMeshPayload
    {
        static readonly ConcurrentBag<ChunkMeshPayload> Pool=new ConcurrentBag<ChunkMeshPayload>();
        public VoxelVertex[] Vertices; public int VertexCount;
        // Whole-chunk payloads can carry packed UInt16 indices when the merged vertex count fits.
        // This mirrors the exact same topology with half the index bytes; dense chunks keep Int32.
        public ushort[] Indices16; public int[] Indices; public int IndexCount;
        public bool Uses16BitIndices => Indices16!=null;
        public int OpaqueCount,WaterCount,TransparentCount;
        public int MinSection=-1,MaxSection=-1;

        public static ChunkMeshPayload Rent()
        {
            if(!Pool.TryTake(out var p))p=new ChunkMeshPayload();
            p.MinSection=p.MaxSection=-1;
            return p;
        }

        public void Release()
        {
            if(Vertices!=null&&Vertices.Length!=0)ArrayPool<VoxelVertex>.Shared.Return(Vertices,false);
            if(Indices16!=null&&Indices16.Length!=0)ArrayPool<ushort>.Shared.Return(Indices16,false);
            if(Indices!=null&&Indices.Length!=0)ArrayPool<int>.Shared.Return(Indices,false);
            Vertices=null;Indices16=null;Indices=null;VertexCount=IndexCount=OpaqueCount=WaterCount=TransparentCount=0;
            MinSection=MaxSection=-1;
            Pool.Add(this);
        }
    }

    /// <summary>
    /// One completed mesh-worker dispatch. main.js returns whole-chunk mesh results and integrates
    /// up to six of those per frame. R8 accidentally counted six section results instead; R9 keeps
    /// all section results from one chunk dispatch together so the frame budget has source semantics.
    /// </summary>
    public struct MeshBuildBatch
    {
        public readonly ChunkCoord Chunk;
        public readonly MeshBuildResult[] Results;
        public readonly int Count;
        public ChunkMeshPayload Combined;
        public MeshBuildBatch(ChunkCoord chunk, MeshBuildResult[] results, int count, ChunkMeshPayload combined=null)
        {
            Chunk=chunk; Results=results; Count=count; Combined=combined;
        }
        public ChunkMeshPayload DetachCombined(){var p=Combined;Combined=null;return p;}
        public void Release()
        {
            if(Results!=null)
            {
                for(int i=0;i<Count;i++)
                {
                    ref MeshBuildResult r=ref Results[i];
                    r.ReleaseBuffers();
                    r=default;
                }
                ArrayPool<MeshBuildResult>.Shared.Return(Results,true);
            }
            if(Combined!=null){Combined.Release();Combined=null;}
        }
    }
}
