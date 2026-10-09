// Voxel Forge — Unity port. Light transport predicates (stops / attenuation / signature).
namespace VoxelForge
{
    public static partial class VF
    {
        public static bool axisLogIdSync(int id)
        {
            return id == B.LOG || id == B.SPRUCE_LOG || id == B.BIRCH_LOG || id == B.JUNGLE_LOG || id == B.ACACIA_LOG || id == B.DARK_LOG;
        }
        public static bool isOpaque(int id)
        {
            var b = bdef(id);
            return id != B.AIR && b != null && b.solid && !b.transparent && !b.cutout && !b.plant && !b.waterPlant && b.special == null && !axisLogIdSync(id);
        }
        public static bool lightStops(int id)
        {
            var b = bdef(id);
            return id != B.AIR && b != null && b.solid && !b.transparent && !b.cutout && !b.plant;
        }
        static bool attenuatingId(int id)
        {
            return id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1
                || id == B.LEAVES || id == B.BIRCH_LEAVES || id == B.SPRUCE_LEAVES || id == B.DARK_LEAVES || id == B.JUNGLE_LEAVES || id == B.ACACIA_LEAVES;
        }
        public static bool lightAttenuation(int id)
        {
            var b = bdef(id);
            if (b == null || id == B.AIR) return false;
            return attenuatingId(id) || b.waterPlant;
        }
        public static int lightSignature(int id)
        {
            var b = bdef(id);
            return (lightStops(id) ? 1 : 0) | ((lightAttenuation(id) ? 1 : 0) << 1) | ((b != null ? b.light : 0) << 4);
        }
        public static bool lightStopsState(int id, JObj meta)
        {
            if (id == B.AIR) return false;
            if (isVirtualId(id))
            {
                var v = virtualDefFromMeta(meta);
                return v != null && v.solid && !v.transparent && !v.cutout && !v.plant;
            }
            var b = bdef(id);
            return b != null && b.solid && !b.transparent && !b.cutout && !b.plant;
        }
        public static bool lightAttenuationState(int id, JObj meta)
        {
            if (id == B.AIR) return false;
            bool waterPlant;
            if (isVirtualId(id)) { var v = virtualDefFromMeta(meta); if (v == null) return false; waterPlant = v.waterPlant; }
            else { var b = bdef(id); if (b == null) return false; waterPlant = b.waterPlant; }
            return (meta != null && Json.Truthy(meta.Get("waterlogged"))) || attenuatingId(id) || waterPlant;
        }
        public static int lightSignatureState(int id, JObj meta)
        {
            int light;
            if (isVirtualId(id)) { var v = virtualDefFromMeta(meta); light = v != null ? v.light : 0; }
            else { var b = bdef(id); light = b != null ? b.light : 0; }
            return (lightStopsState(id, meta) ? 1 : 0) | ((lightAttenuationState(id, meta) ? 1 : 0) << 1) | (light << 4);
        }
        public static bool lightStopsAt(int x, int y, int z) { int id = getBlock(x, y, z); return lightStopsState(id, getBlockMeta(x, y, z)); }
        public static int lightCostAt(int x, int y, int z) { int id = getBlock(x, y, z); return lightAttenuationState(id, getBlockMeta(x, y, z)) ? 2 : 1; }
        public static int faceTile(int id, int d, int sign) { var b = blocks[id]; return d == 1 ? (sign > 0 ? b.top : b.bottom) : b.side; }
    }
}
