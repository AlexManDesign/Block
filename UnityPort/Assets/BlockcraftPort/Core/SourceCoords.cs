namespace BlockcraftPort
{
    /// <summary>
    /// Exact coordinate-system bridge between the WebGL source and Unity.
    /// Source X/Y are preserved and source Z is reflected once at the world boundary.
    /// Integer cells use zU = -zS - 1 so [z,z+1] maps to [-z-1,-z] without drift.
    /// Directional metadata is mirrored here as part of the same boundary conversion.
    /// Runtime Unity-side edits use Unity metadata directly and must not call MirrorMetadata twice.
    /// </summary>
    public static class SourceCoords
    {
        public static int SourceBlockZToUnity(int sourceZ) => -sourceZ - 1;
        public static int UnityBlockZToSource(int unityZ) => -unityZ - 1;
        public static float SourceWorldZToUnity(float sourceZ) => -sourceZ;
        public static float UnityWorldZToSource(float unityZ) => -unityZ;
        public static ChunkCoord UnityChunkToSource(ChunkCoord unity) => new ChunkCoord(unity.X, -unity.Z - 1);
        public static ChunkCoord SourceChunkToUnity(ChunkCoord source) => new ChunkCoord(source.X, -source.Z - 1);

        // main/meshWorker horizontal direction order:
        //   0 = +Z, 1 = -X, 2 = -Z, 3 = +X.
        // Reflecting Z swaps 0 <-> 2 and leaves the X directions unchanged.
        public static int MirrorFacing(int dir) => (2 - dir) & 3;

        /// <summary>
        /// Mirrors meshWorker/main.js metadata through the source-Z -> Unity-Z reflection.
        /// This operation is an involution: MirrorMetadata(MirrorMetadata(m)) == m.
        /// </summary>
        public static byte MirrorMetadata(BlockId id, byte sourceMeta)
        {
            BlockShape shape = BlockRegistry.Get(id).Shape;
            int m = sourceMeta;

            switch (shape)
            {
                case BlockShape.Stairs:
                case BlockShape.Trapdoor:
                case BlockShape.Ladder:
                    return (byte)((m & 0xFC) | MirrorFacing(m & 3));

                case BlockShape.Door:
                    // Facing mirrors and handedness (hinge bit 4) reverses under reflection.
                    return (byte)(((m & 0xFC) | MirrorFacing(m & 3)) ^ 0x10);

                case BlockShape.Bed:
                {
                    // Bed facing is stored in bits 1..2; bit 0 is retained.
                    int dir = (m >> 1) & 3;
                    return (byte)((m & ~0x06) | (MirrorFacing(dir) << 1));
                }

                case BlockShape.Button:
                case BlockShape.Torch:
                    // 0 is the non-wall form; wall forms 1..4 are GA[meta-1].
                    if (m >= 1 && m <= 4)
                        return (byte)(MirrorFacing(m - 1) + 1);
                    return sourceMeta;

                case BlockShape.FlatFaces:
                {
                    // source face mask: 1=+Z, 2=-X, 4=-Z, 8=+X, 16=+Y, 32=-Y.
                    int rest = m & ~(1 | 4);
                    if ((m & 1) != 0) rest |= 4;
                    if ((m & 4) != 0) rest |= 1;
                    return (byte)rest;
                }

                case BlockShape.ShipWheel:
                    // Mirroring a Y rotation negates its quarter-turn index.
                    return (byte)((m & 0xFC) | ((4 - (m & 3)) & 3));

                case BlockShape.Chest:
                {
                    // Low two bits are facing. For a double chest, bit 3 is left/right
                    // handedness and therefore flips under a spatial reflection.
                    int mirrored = (m & 0xFC) | MirrorFacing(m & 3);
                    if ((m & 0x04) != 0) mirrored ^= 0x08;
                    return (byte)mirrored;
                }

                // Gate and rail store only an X-vs-Z axis bit in the metadata used by
                // the renderer; reflecting Z does not change that axis. Log/stem axis
                // metadata similarly remains Y/X/Z after reflection.
                default:
                    return sourceMeta;
            }
        }

        public static byte SourceMetaToUnity(BlockId id, byte sourceMeta) => MirrorMetadata(id, sourceMeta);
        public static byte UnityMetaToSource(BlockId id, byte unityMeta) => MirrorMetadata(id, unityMeta);
    }
}
