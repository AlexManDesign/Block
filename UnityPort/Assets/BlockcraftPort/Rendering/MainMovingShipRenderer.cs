using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js ng()/tI() moving-ship render backend.
    /// A ship is meshed once in LOCAL voxel coordinates with the same ChunkMesher used by world chunks.
    /// Movement only changes the draw matrix; no per-block GameObjects and no re-mesh while translating/turning.
    /// Source keeps opaque+cutout in geoLocal and water in geoWater; this class mirrors that as submeshes 0/1.
    /// </summary>
    public sealed class MainMovingShipRenderer : MonoBehaviour
    {
        [Serializable]
        public struct ShipBlock
        {
            public int X,Y,Z;
            public BlockId Id;
            public byte Meta;
            public ShipBlock(int x,int y,int z,BlockId id,byte meta=0){X=x;Y=y;Z=z;Id=id;Meta=meta;}
        }

        sealed class ShipVisual
        {
            public int Handle;
            public int GeoVersion;
            public Vector3 Position;
            public float Yaw;
            public Mesh Mesh;
            public bool HasLocal,HasWater;
            public readonly List<ShipBlock> Blocks=new List<ShipBlock>(1024);
        }

        static MainMovingShipRenderer instance;
        static readonly VertexAttributeDescriptor[] Layout=
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,3,0)
        };
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices|MeshUpdateFlags.DontNotifyMeshUsers;

        readonly List<ShipVisual> ships=new List<ShipVisual>(8);
        readonly Dictionary<int,ShipVisual> byHandle=new Dictionary<int,ShipVisual>();
        int nextHandle=1;
        VoxelWorld world;
        Material opaqueMaterial,waterMaterial;

        public static MainMovingShipRenderer EnsureInstance(VoxelWorld world,Material opaque,Material water)
        {
            if(instance==null)
            {
                var go=new GameObject("MainMovingShipRenderer");
                instance=go.AddComponent<MainMovingShipRenderer>();
            }
            instance.world=world;
            instance.opaqueMaterial=opaque;
            instance.waterMaterial=water;
            return instance;
        }

        /// <summary>Creates source-style local geometry. Coordinates are local voxel coordinates around the ship origin.</summary>
        public static int Create(IList<ShipBlock> blocks,Vector3 position,float yawRadians=0f,int requestedHandle=0)
        {
            if(instance==null||blocks==null||blocks.Count==0)return -1;
            int handle=requestedHandle>0?requestedHandle:instance.nextHandle++;
            if(instance.byHandle.TryGetValue(handle,out var old))instance.RemoveInternal(old);
            var s=new ShipVisual{Handle=handle,Position=position,Yaw=yawRadians,GeoVersion=1};
            for(int i=0;i<blocks.Count;i++)if(blocks[i].Id!=BlockId.Air)s.Blocks.Add(blocks[i]);
            if(s.Blocks.Count==0)return -1;
            s.Mesh=instance.BuildLocalMesh(s);
            instance.ships.Add(s);instance.byHandle[handle]=s;
            instance.world?.InvalidateDirectCommands();
            return handle;
        }

        public static bool ReplaceGeometry(int handle,IList<ShipBlock> blocks,int geoVersion)
        {
            if(instance==null||!instance.byHandle.TryGetValue(handle,out var s)||blocks==null)return false;
            s.Blocks.Clear();for(int i=0;i<blocks.Count;i++)if(blocks[i].Id!=BlockId.Air)s.Blocks.Add(blocks[i]);
            s.GeoVersion=geoVersion;
            if(s.Mesh!=null)UnityEngine.Object.Destroy(s.Mesh);
            s.Mesh=s.Blocks.Count==0?null:instance.BuildLocalMesh(s);
            instance.world?.InvalidateDirectCommands();return true;
        }

        public static bool SetPose(int handle,Vector3 position,float yawRadians)
        {
            if(instance==null||!instance.byHandle.TryGetValue(handle,out var s))return false;
            if(s.Position==position&&Mathf.Abs(s.Yaw-yawRadians)<1e-8f)return true;
            s.Position=position;s.Yaw=yawRadians;
            // Command buffers hold the matrix value, therefore a pose change only re-records the tiny overlay.
            instance.world?.InvalidateDirectCommands();return true;
        }

        public static bool Remove(int handle)
        {
            if(instance==null||!instance.byHandle.TryGetValue(handle,out var s))return false;
            instance.RemoveInternal(s);instance.world?.InvalidateDirectCommands();return true;
        }
        public static void Clear()
        {
            if(instance==null)return;
            for(int i=0;i<instance.ships.Count;i++)if(instance.ships[i].Mesh!=null)UnityEngine.Object.Destroy(instance.ships[i].Mesh);
            instance.ships.Clear();instance.byHandle.Clear();instance.world?.InvalidateDirectCommands();
        }

        public static void AppendOpaque(CommandBuffer cb)
        {
            if(cb==null||instance==null||instance.opaqueMaterial==null)return;
            for(int i=0;i<instance.ships.Count;i++)
            {
                var s=instance.ships[i];if(s.Mesh==null||!s.HasLocal||!VisibleInLoadedWorld(s))continue;
                cb.DrawMesh(s.Mesh,DrawMatrix(s),instance.opaqueMaterial,0,0);
            }
        }
        public static void AppendWater(CommandBuffer cb)
        {
            if(cb==null||instance==null||instance.waterMaterial==null)return;
            for(int i=0;i<instance.ships.Count;i++)
            {
                // main tI(A) is guarded by geoLocal even for the water pass.
                var s=instance.ships[i];if(s.Mesh==null||!s.HasLocal||!s.HasWater||!VisibleInLoadedWorld(s))continue;
                cb.DrawMesh(s.Mesh,DrawMatrix(s),instance.waterMaterial,1,0);
            }
        }

        static bool VisibleInLoadedWorld(ShipVisual s)
        {
            // main tI(): chunks.has(DA(floor((ship.x+0.5)/16),floor((ship.z+0.5)/16))).
            // IsChunkLoadedAt performs the same floor-to-chunk conversion after the +0.5 pivot shift.
            if(instance==null||instance.world==null)return false;
            int wx=Mathf.FloorToInt(s.Position.x+.5f),wz=Mathf.FloorToInt(s.Position.z+.5f);
            return instance.world.IsChunkLoadedAt(wx,wz);
        }

        static Matrix4x4 DrawMatrix(ShipVisual s)
        {
            // main c6()/tI(): rotate local X/Z around (0.5,0.5), then add x/y/z.
            return Matrix4x4.Translate(s.Position+new Vector3(.5f,0,.5f))*
                   Matrix4x4.Rotate(Quaternion.Euler(0,s.Yaw*Mathf.Rad2Deg,0))*
                   Matrix4x4.Translate(new Vector3(-.5f,0,-.5f));
        }

        void RemoveInternal(ShipVisual s)
        {
            ships.Remove(s);byHandle.Remove(s.Handle);if(s.Mesh!=null)UnityEngine.Object.Destroy(s.Mesh);
        }

        Mesh BuildLocalMesh(ShipVisual ship)
        {
            // Source ms(): build a private voxel world for the vessel, relight it, then feed the
            // ordinary terrain mesher. ChunkColumn + VoxelLighting deliberately reuse the same
            // Es()/AD()/Yu() port as the world instead of maintaining a second ship-only mesher.
            var columns=new Dictionary<ChunkCoord,ChunkColumn>();
            var sections=new HashSet<SectionKey>();
            for(int i=0;i<ship.Blocks.Count;i++)
            {
                ShipBlock b=ship.Blocks[i];
                if(b.Y<VoxelConstants.MinY||b.Y>VoxelConstants.MaxY)continue;
                ChunkCoord cc=ChunkCoord.FromWorld(b.X,b.Z);
                if(!columns.TryGetValue(cc,out var col)){col=new ChunkColumn(cc);columns.Add(cc,col);}
                col.SetLocal(VoxelConstants.FloorMod(b.X,16),b.Y,VoxelConstants.FloorMod(b.Z,16),b.Id,b.Meta);
                sections.Add(new SectionKey(cc,(b.Y-VoxelConstants.MinY)>>4));
            }
            foreach(var kv in columns)VoxelLighting.BakeColumn(kv.Value);
            // main ms() performs Yu() while chunks are baked and then once more over all chunks.
            // Two stitch passes after the deterministic local bake reach the same stable border light
            // without depending on Dictionary iteration order.
            for(int pass=0;pass<2;pass++)foreach(var kv in columns)VoxelLighting.StitchNeighbors(columns,kv.Key);

            var vv=new List<VoxelVertex>(ship.Blocks.Count*12);
            var localIndices=new List<int>(ship.Blocks.Count*18);
            var waterIndices=new List<int>(ship.Blocks.Count*6);
            foreach(var key in sections)
            {
                SectionSnapshot snap=CaptureLocal(key,columns);
                MeshBuildResult r=ChunkMesher.Build(snap);snap.Release();
                int baseVertex=vv.Count;
                for(int i=0;i<r.VertexCount;i++)vv.Add(r.Vertices[i]);
                for(int i=0;i<r.OpaqueCount;i++)localIndices.Add(baseVertex+r.Opaque[i]);
                // main ms(): opaque and transparent/cutout are concatenated into geoLocal.
                for(int i=0;i<r.TransparentCount;i++)localIndices.Add(baseVertex+r.Transparent[i]);
                for(int i=0;i<r.WaterCount;i++)waterIndices.Add(baseVertex+r.Water[i]);
                r.ReleaseBuffers();
            }

            ship.HasLocal=localIndices.Count!=0;ship.HasWater=waterIndices.Count!=0;
            var mesh=new Mesh{name="main_ship_"+ship.Handle+"_geo"};
            mesh.indexFormat=vv.Count<=65535?IndexFormat.UInt16:IndexFormat.UInt32;
            mesh.SetVertexBufferParams(Mathf.Max(1,vv.Count),Layout);
            if(vv.Count>0)mesh.SetVertexBufferData(vv,0,0,vv.Count,0,UploadFlags);
            int total=localIndices.Count+waterIndices.Count;
            mesh.SetIndexBufferParams(Mathf.Max(1,total),mesh.indexFormat);
            if(total>0)
            {
                if(mesh.indexFormat==IndexFormat.UInt16)
                {
                    ushort[] idx=ArrayPool<ushort>.Shared.Rent(total);int p=0;
                    for(int i=0;i<localIndices.Count;i++)idx[p++]=(ushort)localIndices[i];
                    for(int i=0;i<waterIndices.Count;i++)idx[p++]=(ushort)waterIndices[i];
                    mesh.SetIndexBufferData(idx,0,0,total,UploadFlags);ArrayPool<ushort>.Shared.Return(idx,false);
                }
                else
                {
                    int[] idx=ArrayPool<int>.Shared.Rent(total);int p=0;
                    for(int i=0;i<localIndices.Count;i++)idx[p++]=localIndices[i];
                    for(int i=0;i<waterIndices.Count;i++)idx[p++]=waterIndices[i];
                    mesh.SetIndexBufferData(idx,0,0,total,UploadFlags);ArrayPool<int>.Shared.Return(idx,false);
                }
            }
            mesh.subMeshCount=2;
            mesh.SetSubMesh(0,new SubMeshDescriptor(0,localIndices.Count,MeshTopology.Triangles),UploadFlags);
            mesh.SetSubMesh(1,new SubMeshDescriptor(localIndices.Count,waterIndices.Count,MeshTopology.Triangles),UploadFlags);
            mesh.bounds=ComputeBounds(ship.Blocks);
            mesh.UploadMeshData(false);
            return mesh;
        }

        static Bounds ComputeBounds(List<ShipBlock> blocks)
        {
            if(blocks.Count==0)return new Bounds(Vector3.zero,Vector3.one);
            Vector3 mn=new Vector3(blocks[0].X,blocks[0].Y,blocks[0].Z),mx=mn+Vector3.one;
            for(int i=1;i<blocks.Count;i++){var b=blocks[i];mn=Vector3.Min(mn,new Vector3(b.X,b.Y,b.Z));mx=Vector3.Max(mx,new Vector3(b.X+1,b.Y+1,b.Z+1));}
            var q=new Bounds((mn+mx)*.5f,mx-mn);q.Expand(.05f);return q;
        }

        static SectionSnapshot CaptureLocal(SectionKey key,Dictionary<ChunkCoord,ChunkColumn> columns)
        {
            const int S=18,N=S*S*S;
            ushort[] vox=ArrayPool<ushort>.Shared.Rent(N);byte[] meta=ArrayPool<byte>.Shared.Rent(N);byte[] light=ArrayPool<byte>.Shared.Rent(N);
            int baseX=key.Chunk.X*16,baseY=VoxelConstants.MinY+key.Section*16,baseZ=key.Chunk.Z*16;
            for(int x=0;x<S;x++)for(int z=0;z<S;z++)for(int y=0;y<S;y++)
            {
                int idx=(x*S+z)*S+y,wx=baseX+x-1,wy=baseY+y-1,wz=baseZ+z-1;
                if(wy<VoxelConstants.MinY){vox[idx]=(ushort)BlockId.Bedrock;meta[idx]=0;light[idx]=0;continue;}
                if(wy>VoxelConstants.MaxY){vox[idx]=(ushort)BlockId.Air;meta[idx]=0;light[idx]=0xF0;continue;}
                ChunkCoord cc=ChunkCoord.FromWorld(wx,wz);
                if(columns.TryGetValue(cc,out var col))
                {
                    int lx=VoxelConstants.FloorMod(wx,16),lz=VoxelConstants.FloorMod(wz,16);
                    vox[idx]=(ushort)col.GetLocal(lx,wy,lz);meta[idx]=col.GetMetaLocal(lx,wy,lz);light[idx]=col.GetLightLocal(lx,wy,lz);
                }
                else {vox[idx]=(ushort)BlockId.Air;meta[idx]=0;light[idx]=0xF0;}
            }
            return new SectionSnapshot(key,1,vox,meta,light);
        }

        void OnDestroy()
        {
            for(int i=0;i<ships.Count;i++)if(ships[i].Mesh!=null)UnityEngine.Object.Destroy(ships[i].Mesh);
            ships.Clear();byHandle.Clear();if(instance==this)instance=null;
        }
    }
}
