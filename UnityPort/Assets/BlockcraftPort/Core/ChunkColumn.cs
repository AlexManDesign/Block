namespace BlockcraftPort
{
    public sealed class ChunkSection
    {
        public readonly ushort[] Blocks=new ushort[4096];
        public readonly byte[] Meta=new byte[4096];
        public readonly byte[] Light=new byte[4096];
        public int NonAir;
        static int Index(int x,int y,int z)=>((x*16)+z)*16+y;
        public BlockId Get(int x,int y,int z)=>(BlockId)Blocks[Index(x,y,z)];
        public byte GetMeta(int x,int y,int z)=>Meta[Index(x,y,z)];
        public byte GetLight(int x,int y,int z)=>Light[Index(x,y,z)];
        public void SetLight(int x,int y,int z,byte v)=>Light[Index(x,y,z)]=v;
        public void Set(int x,int y,int z,BlockId id,byte meta=0){int i=Index(x,y,z);ushort old=Blocks[i];if(old==0&&id!=BlockId.Air)NonAir++;else if(old!=0&&id==BlockId.Air)NonAir--;Blocks[i]=(ushort)id;Meta[i]=meta;}
    }
    public sealed class ChunkColumn
    {
        public readonly ChunkCoord Coord;
        public readonly ChunkSection[] Sections=new ChunkSection[VoxelConstants.SectionCount];
        public readonly byte[] Biomes=new byte[256];
        public readonly short[] Heights=new short[256];
        public readonly short[] LightCeilings=new short[256];
        public int Revision;
        public ChunkColumn(ChunkCoord c){Coord=c;for(int i=0;i<256;i++)LightCeilings[i]=VoxelConstants.MinY-1;}
        public ChunkSection EnsureSection(int s)=>Sections[s]??(Sections[s]=new ChunkSection());
        public BlockId GetLocal(int x,int y,int z){if(y<VoxelConstants.MinY)return BlockId.Bedrock;if(y>VoxelConstants.MaxY)return BlockId.Air;var sec=Sections[(y-VoxelConstants.MinY)>>4];return sec==null?BlockId.Air:sec.Get(x,(y-VoxelConstants.MinY)&15,z);}
        public byte GetMetaLocal(int x,int y,int z){if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return 0;var sec=Sections[(y-VoxelConstants.MinY)>>4];return sec==null?(byte)0:sec.GetMeta(x,(y-VoxelConstants.MinY)&15,z);}
        public byte GetLightLocal(int x,int y,int z){if(y>VoxelConstants.MaxY)return 0xF0;if(y<VoxelConstants.MinY)return 0;var sec=Sections[(y-VoxelConstants.MinY)>>4];if(sec!=null)return sec.GetLight(x,(y-VoxelConstants.MinY)&15,z);return y>LightCeilings[x*16+z]?(byte)0xF0:(byte)0;}
        public void SetLightLocal(int x,int y,int z,byte light){if((uint)x>=16||(uint)z>=16||y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;int sy=(y-VoxelConstants.MinY)>>4;if(light==0xF0&&Sections[sy]==null&&y>LightCeilings[x*16+z])return;EnsureSection(sy).SetLight(x,(y-VoxelConstants.MinY)&15,z,light);}
        public void SetLocal(int x,int y,int z,BlockId id,byte meta=0){if((uint)x>=16||(uint)z>=16||y<VoxelConstants.MinY||y>VoxelConstants.MaxY)return;int sy=(y-VoxelConstants.MinY)>>4;if(id==BlockId.Air&&meta==0&&Sections[sy]==null)return;EnsureSection(sy).Set(x,(y-VoxelConstants.MinY)&15,z,id,meta);Revision++;}
        public bool SetWorldIfInside(int wx,int y,int wz,BlockId id,byte meta=0){int minX=Coord.X*16,minZ=Coord.Z*16;if(wx<minX||wx>=minX+16||wz<minZ||wz>=minZ+16)return false;SetLocal(wx-minX,y,wz-minZ,id,meta);return true;}
    }
}
