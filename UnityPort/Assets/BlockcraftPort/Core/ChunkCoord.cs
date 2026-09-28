using System;
namespace BlockcraftPort
{
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int X, Z;
        public ChunkCoord(int x,int z){X=x;Z=z;}
        public bool Equals(ChunkCoord o)=>X==o.X&&Z==o.Z;
        public override bool Equals(object o)=>o is ChunkCoord c&&Equals(c);
        public override int GetHashCode(){unchecked{return (X*73856093)^(Z*19349663);}}
        public override string ToString()=>$"{X},{Z}";
        public static ChunkCoord FromWorld(int x,int z)=>new ChunkCoord(VoxelConstants.FloorDiv(x,16),VoxelConstants.FloorDiv(z,16));
    }

    public readonly struct SectionKey : IEquatable<SectionKey>
    {
        public readonly ChunkCoord Chunk; public readonly int Section;
        public SectionKey(ChunkCoord c,int s){Chunk=c;Section=s;}
        public bool Equals(SectionKey o)=>Chunk.Equals(o.Chunk)&&Section==o.Section;
        public override bool Equals(object o)=>o is SectionKey k&&Equals(k);
        public override int GetHashCode()=>Chunk.GetHashCode()*31+Section;
        public override string ToString()=>$"{Chunk.X},{Section},{Chunk.Z}";
    }
}
