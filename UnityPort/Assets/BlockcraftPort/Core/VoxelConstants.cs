namespace BlockcraftPort
{
    public static class VoxelConstants
    {
        public const int ChunkSize = 16;
        public const int SectionSize = 16;
        public const int MinY = -64;
        public const int MaxY = 319;
        public const int WorldHeight = MaxY - MinY + 1; // 384
        public const int SectionCount = WorldHeight / SectionSize; // 24
        public const int SeaLevel = 63;

        public static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r < 0) != (divisor < 0))) q--;
            return q;
        }

        public static int FloorMod(int value, int divisor)
        {
            int r = value % divisor;
            return r < 0 ? r + divisor : r;
        }
    }
}
