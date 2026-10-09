// Voxel Forge — Unity port. Main-thread world generator instance (WORLDGEN) and its wrappers.
// Worker threads own separate kernels; this instance must only be used from the main thread.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public static partial class VF
    {
        static WorldGenKernel WORLDGEN;
        static int WORLDGEN_SEED;
        static WorldGenKernel WG
        {
            get
            {
                if (WORLDGEN == null) { GenData.Ensure(); WORLDGEN = new WorldGenKernel(WORLD_SEED); WORLDGEN_SEED = WORLD_SEED; }
                else if (WORLD_SEED != WORLDGEN_SEED) { WORLDGEN_SEED = WORLD_SEED; WORLDGEN.setSeed(WORLDGEN_SEED); }
                return WORLDGEN;
            }
        }
        public static void syncWorldGenSeed() { var _ = WG; }
        public static ColumnInfo columnInfo(int x, int z) { return WG.columnInfo(x, z); }
        public static double heightAtRaw(int x, int z) { return WG.heightAtRaw(x, z); }
        public static int heightFromRaw(double raw) { return Math.Max(WORLD_MIN_Y, Math.Min(WORLD_MAX_Y - 1, (int)JS.round(raw))); }
        public static string biomeAt(int x, int z) { return WG.biomeAt(x, z); }
        public static string biomeAt(double x, double z) { return WG.biomeAt(JS.floor(x), JS.floor(z)); }
        public static double dryAt(int x, int z) { return WG.dryAt(x, z); }
        public static double terrainDensity(int x, int y, int z) { return WG.terrainDensity(x, y, z); }
        public static int heightAt(int x, int z) { return WG.heightAt(x, z); }
        public static int heightAt(double x, double z) { return WG.heightAt(JS.floor(x), JS.floor(z)); }
        public static int caveBiomeAt(int x, int y, int z, int? h = null) { return WG.caveBiomeAt(x, y, z, h); }
        public static string effectiveBiomeAt(int x, int y, int z)
        {
            int cb = caveBiomeAt(x, y, z);
            return cb == 1 ? "LUSH_CAVES" : cb == 2 ? "DRIPSTONE_CAVES" : cb == 3 ? "DEEP_DARK" : biomeAt(x, z);
        }
        public static int oreAt(int x, int y, int z) { return WG.oreAt(x, y, z); }
        public static bool bedrockAt(int x, int y, int z) { return WG.bedrockAt(x, y, z); }
        public static int terrainBlock(int x, int y, int z) { return WG.terrainBlock(x, y, z); }
        public static int baseBlock(int x, int y, int z) { return WG.baseBlock(x, y, z); }
        public static bool treeAt(int x, int z) { return WG.treeAt(x, z); }
        public static bool cactusAt(int x, int z) { return WG.cactusAt(x, z); }
        public static int landPlantAt(int x, int z) { return WG.landPlantAt(x, z); }
        public static int aquaPlantAt(int x, int y, int z) { return WG.aquaPlantAt(x, y, z); }
        public static string structureChestKindAt(int x, int y, int z) { return WG.structureChestKindAt(x, y, z); }
    }
}
