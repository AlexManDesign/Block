using System;
using System.Buffers;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// GPU chunk mesh + optional nearby CPU section geometry. Rendering is submitted centrally by
    /// MainChunkDirectRenderer, matching main.js' direct VAO/VBO/IBO draw architecture: no chunk
    /// GameObject, Transform, MeshFilter or MeshRenderer exists in the steady-state world.
    /// </summary>
    public sealed class ChunkRender
    {
        static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3, 0)
        };

        struct SectionData
        {
            public bool Present;
            public VoxelVertex[] Vertices; public int VertexCount;
            public int[] Opaque, Water, Transparent;
            public int OpaqueCount,WaterCount,TransparentCount;

            public void Release()
            {
                if(Vertices!=null&&Vertices.Length!=0)ArrayPool<VoxelVertex>.Shared.Return(Vertices,false);
                if(Opaque!=null&&Opaque.Length!=0)ArrayPool<int>.Shared.Return(Opaque,false);
                if(Water!=null&&Water.Length!=0)ArrayPool<int>.Shared.Return(Water,false);
                if(Transparent!=null&&Transparent.Length!=0)ArrayPool<int>.Shared.Return(Transparent,false);
                Vertices=null;Opaque=null;Water=null;Transparent=null;
                VertexCount=OpaqueCount=WaterCount=TransparentCount=0;Present=false;
            }
        }

        Mesh mesh;
        readonly ChunkCoord coord;
        readonly SectionData[] sections=new SectionData[VoxelConstants.SectionCount];
        VoxelVertex[] cpuVertices;
        int[] cpuIndices;
        ushort[] cpuIndices16;
        readonly int[] sectionVertexOffsets=new int[VoxelConstants.SectionCount];
        readonly SubMeshDescriptor[] subMeshes=new SubMeshDescriptor[3];
        static readonly MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
        static readonly MeshUpdateFlags BufferUploadFlags=UploadFlags|MeshUpdateFlags.DontNotifyMeshUsers;
        int vertexCapacity,indexCapacity;
        IndexFormat currentIndexFormat=IndexFormat.UInt32;
        int boundsMinSection=-1,boundsMaxSection=-1;
        Bounds cachedBounds;
        Vector3 cachedBoundsMin,cachedBoundsMax;
        bool objectActive=true;
        bool meshWritable=true;
        int opaqueIndexCount,waterIndexCount,transparentIndexCount;
        int descriptorVertexCount=-1,descriptorOpaqueCount=-1,descriptorWaterCount=-1,descriptorTransparentCount=-1;
        Bounds descriptorBounds;
        bool descriptorStateValid;
        ChunkMeshPayload premerged;
        const int VoxelVertexStrideBytes=32;

        public float UploadReadyAt{get;private set;}
        public bool NeedsUpload{get;private set;}
        public bool HasCpuGeometry{get;private set;}=true;
        public bool FullCpuRebuildPending{get;private set;}
        public int LastUploadBytes{get;private set;}
        public bool LastUploadReplacedMesh{get;private set;}
        public bool LastUploadUsed16BitIndices{get;private set;}
        public int LastUploadIndexCount{get;private set;}
        public int MeshReplacementCount{get;private set;}

        // Cheap managed estimate used only by the frame upload governor. Cave chunks can contain far
        // more exposed faces than surface chunks; letting several multi-megabyte native Mesh uploads
        // land in one frame is a common source of p99 spikes even when each upload is individually valid.
        public int PendingUploadBytesEstimate
        {
            get
            {
                if(!NeedsUpload)return 0;
                if(premerged!=null)
                    return premerged.VertexCount*VoxelVertexStrideBytes + premerged.IndexCount*(premerged.Uses16BitIndices?sizeof(ushort):sizeof(int));
                int vertices=0,indices=0;
                for(int i=0;i<sections.Length;i++)
                {
                    var sd=sections[i];if(!sd.Present)continue;
                    vertices+=sd.VertexCount;indices+=sd.OpaqueCount+sd.WaterCount+sd.TransparentCount;
                }
                return vertices*VoxelVertexStrideBytes + indices*(vertices<=65535?sizeof(ushort):sizeof(int));
            }
        }

        public ChunkRender(ChunkCoord coord)
        {
            this.coord=coord;
            mesh=CreateMesh();
        }

        Mesh CreateMesh()
        {
            // Deliberately do NOT MarkDynamic(): main.js creates chunk VBO/IBO with STATIC_DRAW.
            var m=new Mesh{name=$"voxel_chunk_{coord.X}_{coord.Z}",indexFormat=IndexFormat.UInt32};
            float centerY=(VoxelConstants.MinY+(VoxelConstants.MaxY+1))*.5f;
            // World-space chunk bounds never change when blocks are edited, so set them once instead
            // of notifying Unity about identical bounds on every fast rebuild.
            cachedBounds=new Bounds(new Vector3(coord.X*16+8f,centerY,coord.Z*16+8f),new Vector3(16f,VoxelConstants.WorldHeight,16f));
            cachedBoundsMin=cachedBounds.min;cachedBoundsMax=cachedBounds.max;
            m.bounds=cachedBounds;
            return m;
        }

        bool EnsureWritableMesh()
        {
            // Track writability ourselves instead of crossing into Unity native code through
            // Mesh.isReadable on every upload. Only cold/far meshes are sealed; hot meshes stay
            // writable and update in place like main.js VBOs.
            if(mesh!=null&&meshWritable)return false;
            var old=mesh;
            mesh=CreateMesh();
            meshWritable=true;
            vertexCapacity=0;indexCapacity=0;currentIndexFormat=IndexFormat.UInt32;
            boundsMinSection=-1;boundsMaxSection=-1;
            descriptorStateValid=false;
            MeshReplacementCount++;
            if(old!=null)UnityEngine.Object.Destroy(old);
            return true;
        }

        public void ApplySection(ref MeshBuildResult r)
        {
            if(premerged!=null){premerged.Release();premerged=null;}
            int s=r.Key.Section;if((uint)s>=VoxelConstants.SectionCount)return;
            if(sections[s].Present)sections[s].Release();
            if(!r.Empty)
            {
                sections[s]=new SectionData
                {
                    Present=true,Vertices=r.Vertices,VertexCount=r.VertexCount,
                    Opaque=r.Opaque,OpaqueCount=r.OpaqueCount,
                    Water=r.Water,WaterCount=r.WaterCount,
                    Transparent=r.Transparent,TransparentCount=r.TransparentCount
                };
                r.DetachBuffers();
            }
            if(!FullCpuRebuildPending)HasCpuGeometry=true;
            NeedsUpload=true;
            UploadReadyAt=Time.unscaledTime;
        }

        public void SetPremerged(ChunkMeshPayload payload)
        {
            if(premerged!=null)premerged.Release();
            premerged=payload;NeedsUpload=true;UploadReadyAt=Time.unscaledTime;
        }

        /// <summary>
        /// Upload changed section geometry. Returns true only when the recorded direct-draw command
        /// structure must be rebuilt (mesh object replacement, layer presence change, or culling
        /// bounds change). Pure vertex/index content updates stay visible through the existing
        /// CommandBuffer DrawMesh reference and do not force command re-recording.
        /// </summary>
        public bool Rebuild()
        {
            if(!NeedsUpload)return false;NeedsUpload=false;
            LastUploadBytes=0;
            LastUploadReplacedMesh=false;
            LastUploadUsed16BitIndices=false;
            LastUploadIndexCount=0;
            int oldLayerMask=LayerMask;
            int oldMin=boundsMinSection,oldMax=boundsMaxSection;
            bool oldActive=objectActive;
            bool meshReplaced=EnsureWritableMesh();
            LastUploadReplacedMesh=meshReplaced;
            if(premerged!=null)
            {
                var payload=premerged;premerged=null;
                try{return UploadPremerged(payload,meshReplaced,oldLayerMask,oldMin,oldMax,oldActive);}
                finally{payload.Release();}
            }
            int totalVertices=0,totalIndices=0,minSection=-1,maxSection=-1;
            // First pass gives exact staging sizes and a tight vertical culling range. A fixed 384-high
            // bound makes many off-screen chunks survive Unity frustum culling unnecessarily.
            for(int i=0;i<sections.Length;i++)
            {
                sectionVertexOffsets[i]=totalVertices;
                var sd=sections[i];if(!sd.Present)continue;
                if(minSection<0)minSection=i;maxSection=i;
                totalVertices+=sd.VertexCount;
                totalIndices+=sd.OpaqueCount+sd.WaterCount+sd.TransparentCount;
            }
            if(totalVertices==0)
            {
                SetObjectActive(false);
                opaqueIndexCount=waterIndexCount=transparentIndexCount=0;
                boundsMinSection=boundsMaxSection=-1;
                if(mesh.subMeshCount!=0)mesh.subMeshCount=0;
                descriptorStateValid=false;
                ReleaseCpuStaging();
                return meshReplaced||oldActive||oldLayerMask!=0||oldMin!=boundsMinSection||oldMax!=boundsMaxSection;
            }
            SetObjectActive(true);
            if(minSection!=boundsMinSection||maxSection!=boundsMaxSection)
            {
                boundsMinSection=minSection;boundsMaxSection=maxSection;
                float y0=VoxelConstants.MinY+minSection*16f;
                float y1=VoxelConstants.MinY+(maxSection+1)*16f;
                cachedBounds=new Bounds(new Vector3(coord.X*16+8f,(y0+y1)*.5f,coord.Z*16+8f),new Vector3(16f,y1-y0,16f));
                cachedBoundsMin=cachedBounds.min;cachedBoundsMax=cachedBounds.max;
                mesh.bounds=cachedBounds;
            }
            bool use16=totalVertices<=65535;
            EnsureCpuStaging(totalVertices,totalIndices,use16);

            int vWrite=0;
            for(int i=0;i<sections.Length;i++)
            {
                var sd=sections[i];if(!sd.Present)continue;
                Array.Copy(sd.Vertices,0,cpuVertices,vWrite,sd.VertexCount);
                vWrite+=sd.VertexCount;
            }

            // Player edits normally shrink or only slightly grow a chunk. Retain native GPU capacity;
            // only redefine buffers when required, exactly like reusing WebGL VBO/IBO capacity.
            if(totalVertices>vertexCapacity)
            {
                vertexCapacity=GrowCapacity(totalVertices);
                mesh.SetVertexBufferParams(vertexCapacity,VertexLayout);
            }
            mesh.SetVertexBufferData(cpuVertices,0,0,totalVertices,0,BufferUploadFlags);

            int iWrite=0;
            if(use16)
            {
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices16(sd.Opaque,sd.OpaqueCount,cpuIndices16,iWrite,sectionVertexOffsets[i]);}
                int waterStart16=iWrite;
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices16(sd.Water,sd.WaterCount,cpuIndices16,iWrite,sectionVertexOffsets[i]);}
                int transparentStart16=iWrite;
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices16(sd.Transparent,sd.TransparentCount,cpuIndices16,iWrite,sectionVertexOffsets[i]);}
                int terrainCount16=waterStart16,waterCount16=transparentStart16-waterStart16,transparentCount16=iWrite-transparentStart16;
                opaqueIndexCount=terrainCount16;waterIndexCount=waterCount16;transparentIndexCount=transparentCount16;
                EnsureIndexBuffer(iWrite,IndexFormat.UInt16);
                mesh.SetIndexBufferData(cpuIndices16,0,0,iWrite,BufferUploadFlags);
                ApplySubMeshesIfChanged(totalVertices,terrainCount16,waterCount16,transparentCount16,waterStart16,transparentStart16,cachedBounds,meshReplaced);
                LastUploadUsed16BitIndices=true;LastUploadIndexCount=iWrite;
                LastUploadBytes=totalVertices*VoxelVertexStrideBytes+iWrite*sizeof(ushort);
            }
            else
            {
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices(sd.Opaque,sd.OpaqueCount,cpuIndices,iWrite,sectionVertexOffsets[i]);}
                int waterStart=iWrite;
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices(sd.Water,sd.WaterCount,cpuIndices,iWrite,sectionVertexOffsets[i]);}
                int transparentStart=iWrite;
                for(int i=0;i<sections.Length;i++){var sd=sections[i];if(sd.Present)iWrite=CopyIndices(sd.Transparent,sd.TransparentCount,cpuIndices,iWrite,sectionVertexOffsets[i]);}
                int terrainCount=waterStart,waterCount=transparentStart-waterStart,transparentCount=iWrite-transparentStart;
                opaqueIndexCount=terrainCount;waterIndexCount=waterCount;transparentIndexCount=transparentCount;
                EnsureIndexBuffer(iWrite,IndexFormat.UInt32);
                mesh.SetIndexBufferData(cpuIndices,0,0,iWrite,BufferUploadFlags);
                ApplySubMeshesIfChanged(totalVertices,terrainCount,waterCount,transparentCount,waterStart,transparentStart,cachedBounds,meshReplaced);
                LastUploadUsed16BitIndices=false;LastUploadIndexCount=iWrite;
                LastUploadBytes=totalVertices*VoxelVertexStrideBytes+iWrite*sizeof(int);
            }
            return meshReplaced||oldActive!=objectActive||oldLayerMask!=LayerMask||oldMin!=boundsMinSection||oldMax!=boundsMaxSection;
        }

        bool UploadPremerged(ChunkMeshPayload p,bool meshReplaced,int oldLayerMask,int oldMin,int oldMax,bool oldActive)
        {
            int totalVertices=p.VertexCount,totalIndices=p.IndexCount;
            if(totalVertices<=0||totalIndices<=0)
            {
                SetObjectActive(false);opaqueIndexCount=waterIndexCount=transparentIndexCount=0;boundsMinSection=boundsMaxSection=-1;if(mesh.subMeshCount!=0)mesh.subMeshCount=0;descriptorStateValid=false;ReleaseCpuStaging();
                return meshReplaced||oldActive||oldLayerMask!=0||oldMin!=boundsMinSection||oldMax!=boundsMaxSection;
            }
            SetObjectActive(true);
            if(p.MinSection!=boundsMinSection||p.MaxSection!=boundsMaxSection)
            {
                boundsMinSection=p.MinSection;boundsMaxSection=p.MaxSection;
                float y0=VoxelConstants.MinY+boundsMinSection*16f;float y1=VoxelConstants.MinY+(boundsMaxSection+1)*16f;
                cachedBounds=new Bounds(new Vector3(coord.X*16+8f,(y0+y1)*.5f,coord.Z*16+8f),new Vector3(16f,y1-y0,16f));
                cachedBoundsMin=cachedBounds.min;cachedBoundsMax=cachedBounds.max;
                mesh.bounds=cachedBounds;
            }
            if(totalVertices>vertexCapacity){vertexCapacity=GrowCapacity(totalVertices);mesh.SetVertexBufferParams(vertexCapacity,VertexLayout);}
            mesh.SetVertexBufferData(p.Vertices,0,0,totalVertices,0,BufferUploadFlags);
            if(p.Uses16BitIndices)
            {
                EnsureIndexBuffer(totalIndices,IndexFormat.UInt16);
                mesh.SetIndexBufferData(p.Indices16,0,0,totalIndices,BufferUploadFlags);
                LastUploadUsed16BitIndices=true;LastUploadIndexCount=totalIndices;
                LastUploadBytes=totalVertices*VoxelVertexStrideBytes+totalIndices*sizeof(ushort);
            }
            else
            {
                EnsureIndexBuffer(totalIndices,IndexFormat.UInt32);
                mesh.SetIndexBufferData(p.Indices,0,0,totalIndices,BufferUploadFlags);
                LastUploadUsed16BitIndices=false;LastUploadIndexCount=totalIndices;
                LastUploadBytes=totalVertices*VoxelVertexStrideBytes+totalIndices*sizeof(int);
            }
            opaqueIndexCount=p.OpaqueCount;waterIndexCount=p.WaterCount;transparentIndexCount=p.TransparentCount;
            int waterStart=opaqueIndexCount,transparentStart=waterStart+waterIndexCount;
            ApplySubMeshesIfChanged(totalVertices,opaqueIndexCount,waterIndexCount,transparentIndexCount,waterStart,transparentStart,cachedBounds,meshReplaced);
            // The worker already provided the exact interleaved upload arrays, so no main-thread staging
            // copy is required for this full-chunk rebuild. Section buffers remain retained for edits.
            ReleaseCpuStaging();
            return meshReplaced||oldActive!=objectActive||oldLayerMask!=LayerMask||oldMin!=boundsMinSection||oldMax!=boundsMaxSection;
        }

        void ApplySubMeshesIfChanged(int vertexCount,int opaqueCount,int waterCount,int transparentCount,int waterStart,int transparentStart,Bounds bounds,bool force)
        {
            bool same=descriptorStateValid&&!force&&
                      descriptorVertexCount==vertexCount&&descriptorOpaqueCount==opaqueCount&&
                      descriptorWaterCount==waterCount&&descriptorTransparentCount==transparentCount&&
                      descriptorBounds.center==bounds.center&&descriptorBounds.size==bounds.size;
            if(same)return;
            subMeshes[0]=Descriptor(0,opaqueCount,vertexCount,bounds);
            subMeshes[1]=Descriptor(waterStart,waterCount,vertexCount,bounds);
            subMeshes[2]=Descriptor(transparentStart,transparentCount,vertexCount,bounds);
            mesh.SetSubMeshes(subMeshes,0,3,UploadFlags);
            descriptorVertexCount=vertexCount;descriptorOpaqueCount=opaqueCount;
            descriptorWaterCount=waterCount;descriptorTransparentCount=transparentCount;
            descriptorBounds=bounds;descriptorStateValid=true;
        }

        void EnsureIndexBuffer(int required,IndexFormat format)
        {
            bool formatChanged=currentIndexFormat!=format;
            if(!formatChanged&&required<=indexCapacity)return;
            indexCapacity=GrowCapacity(required);
            currentIndexFormat=format;
            mesh.SetIndexBufferParams(indexCapacity,format);
            // SetIndexBufferParams redefines the native index storage. The topology/counts are the
            // same, but conservatively rewrite descriptors after a format/capacity change.
            descriptorStateValid=false;
        }

        static int GrowCapacity(int required)
        {
            int c=256;
            while(c<required&&c<(1<<30))c<<=1;
            return c<required?required:c;
        }

        void EnsureCpuStaging(int vertices,int indices,bool use16)
        {
            if(cpuVertices==null||cpuVertices.Length<vertices)
            {
                if(cpuVertices!=null)ArrayPool<VoxelVertex>.Shared.Return(cpuVertices,false);
                cpuVertices=ArrayPool<VoxelVertex>.Shared.Rent(GrowCapacity(vertices));
            }
            if(use16)
            {
                if(cpuIndices!=null){ArrayPool<int>.Shared.Return(cpuIndices,false);cpuIndices=null;}
                if(cpuIndices16==null||cpuIndices16.Length<indices)
                {
                    if(cpuIndices16!=null)ArrayPool<ushort>.Shared.Return(cpuIndices16,false);
                    cpuIndices16=ArrayPool<ushort>.Shared.Rent(GrowCapacity(indices));
                }
            }
            else
            {
                if(cpuIndices16!=null){ArrayPool<ushort>.Shared.Return(cpuIndices16,false);cpuIndices16=null;}
                if(cpuIndices==null||cpuIndices.Length<indices)
                {
                    if(cpuIndices!=null)ArrayPool<int>.Shared.Return(cpuIndices,false);
                    cpuIndices=ArrayPool<int>.Shared.Rent(GrowCapacity(indices));
                }
            }
        }

        void ReleaseCpuStaging()
        {
            if(cpuVertices!=null){ArrayPool<VoxelVertex>.Shared.Return(cpuVertices,false);cpuVertices=null;}
            if(cpuIndices16!=null){ArrayPool<ushort>.Shared.Return(cpuIndices16,false);cpuIndices16=null;}
            if(cpuIndices!=null){ArrayPool<int>.Shared.Return(cpuIndices,false);cpuIndices=null;}
        }

        static int CopyIndices16(int[] src,int count,ushort[] dst,int write,int offset)
        {
            if(src==null||count<=0)return write;
            for(int i=0;i<count;i++)dst[write++]=(ushort)(src[i]+offset);
            return write;
        }

        static int CopyIndices(int[] src,int count,int[] dst,int write,int offset)
        {
            if(src==null||count<=0)return write;
            for(int i=0;i<count;i++)dst[write++]=src[i]+offset;
            return write;
        }

        void ReleaseSections()
        {
            for(int i=0;i<sections.Length;i++)
            {
                if(!sections[i].Present)continue;
                sections[i].Release();
            }
        }

        public void BeginFullCpuRebuild()
        {
            if(FullCpuRebuildPending)return;
            if(premerged!=null){premerged.Release();premerged=null;}
            ReleaseSections();
            FullCpuRebuildPending=true;
            HasCpuGeometry=false;
        }

        public void CompleteFullCpuRebuild()
        {
            FullCpuRebuildPending=false;
            HasCpuGeometry=true;
        }

        /// <summary>main.js releases chunk.geo outside radius 2 while retaining the GPU mesh.</summary>
        public void DiscardCpuGeometry(){DiscardCpuGeometry(false);}

        public void DiscardCpuGeometry(bool keepMeshWritable)
        {
            if(NeedsUpload)return;
            if(premerged!=null){premerged.Release();premerged=null;}
            ReleaseSections();
            ReleaseCpuStaging();
            // Unity has no exact WebGL equivalent of an updateable VBO with no CPU-side Mesh copy.
            // Keep the small hot ring writable to avoid edit/lighting recreation hitches; seal only
            // colder chunks where the native CPU copy is worth reclaiming. If a sealed chunk later
            // becomes dirty, EnsureWritableMesh replaces it and telemetry exposes that event.
            if(!keepMeshWritable&&mesh!=null&&meshWritable)
            {
                mesh.UploadMeshData(true);
                meshWritable=false;
            }
            HasCpuGeometry=false;
        }

        static SubMeshDescriptor Descriptor(int start,int count,int vertexCount,Bounds bounds)
        {
            var d=new SubMeshDescriptor(start,count,MeshTopology.Triangles);
            d.firstVertex=0;d.vertexCount=vertexCount;d.bounds=bounds;
            return d;
        }
        void SetObjectActive(bool active)
        {
            objectActive=active;
        }
        int LayerMask => (opaqueIndexCount>0?1:0)|(waterIndexCount>0?2:0)|(transparentIndexCount>0?4:0);
        public Mesh Mesh => mesh;
        // Managed cache: frustum culling runs for hundreds of chunks while the camera moves.
        // Reading Mesh.bounds would cross into Unity native code once per candidate; bounds only change
        // when this class changes them, so keep the exact same value on the managed side.
        public Bounds Bounds => cachedBounds;
        public Vector3 BoundsMin => cachedBoundsMin;
        public Vector3 BoundsMax => cachedBoundsMax;
        public bool Drawable => objectActive&&mesh!=null&&mesh.subMeshCount>=3;
        public bool HasOpaque => opaqueIndexCount>0;
        public bool HasWater => waterIndexCount>0;
        public bool HasTransparent => transparentIndexCount>0;
        public int TriangleCount => (opaqueIndexCount+waterIndexCount+transparentIndexCount)/3;
        public bool Uses16BitIndices => currentIndexFormat==IndexFormat.UInt16;

        public void Dispose(){if(premerged!=null){premerged.Release();premerged=null;}ReleaseSections();ReleaseCpuStaging();if(mesh!=null)UnityEngine.Object.Destroy(mesh);mesh=null;}
    }
}
