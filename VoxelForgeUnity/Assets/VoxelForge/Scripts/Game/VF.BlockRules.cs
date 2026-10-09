// Voxel Forge — Unity port. Harvesting, block drops, attachment support and neighbour transforms.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VoxelForge
{
    public struct ItemCount { public string k; public int n; public ItemCount(string k, int n) { this.k = k; this.n = n; } }

    public static partial class VF
    {
        public static bool finKey(JObj m, string k) { return m != null && Json.IsFinite(m.Get(k)); }

        public static ItemDef currentTool()
        {
            var s = selectedStack();
            if (s == null) return null;
            var d = idef(s.key);
            return d.tool != null ? d : null;
        }
        public static bool canHarvest(int id)
        {
            if (player.creative) return true;
            var b = bdef(id); var t = currentTool();
            if (b == null || b.tier == 0) return true;
            return t != null && t.tool == b.tool && t.tier >= b.tier;
        }
        public static double miningRate(int id, JObj meta = null)
        {
            double hardness; string btool; int btier; bool bplant;
            if (isVirtualId(id)) { var v = virtualDefFromMeta(meta); if (v == null) return 0; hardness = v.hardness; btool = v.tool; btier = v.tier; bplant = v.plant; }
            else { var b = bdef(id); if (b == null) return 0; hardness = b.hardness; btool = b.tool; btier = b.tier; bplant = b.plant; }
            var tool = currentTool();
            if (hardness >= 1e8 || id == B.BEDROCK) return 0;
            double hard = Math.Max(0, hardness);
            if (hard == 0) return double.PositiveInfinity;
            bool needsTool = !string.IsNullOrEmpty(btool), correct = tool != null && needsTool && tool.tool == btool;
            double speed = correct ? (tool.speed != 0 ? tool.speed : 1) : 1;
            if (tool != null && tool.tool == "sword" && bplant) speed = Math.Max(speed, 2);
            // Original main.js behaviour: correct harvest path uses hardness*1.5, wrong/under-tier tool uses hardness*5.
            bool canDrop = !needsTool || !(btier > 0) || (correct && tool.tier >= btier);
            double cost = hard * (canDrop ? 1.5 : 5);
            bool headInWater = pointInWater(player.x, player.y + 1.62, player.z);
            if (headInWater) cost *= 5;
            if (headInWater && !player.onGround && !player.flying) cost *= 5;
            return speed / Math.Max(0.001, cost);
        }
        public static void cleanupContainerAt(int x, int y, int z, int id)
        {
            var k = key3(x, y, z);
            if (id == B.CHEST)
            {
                unpairChestNeighbor(x, y, z);
                var ch = chests.Get(k);
                if (ch != null)
                {
                    foreach (var st in ch) if (st != null) spawnWorldDrop(st.key, st.count, x + 0.5, y + 0.65, z + 0.5, st.dur);
                    chests.Delete(k);
                }
            }
            if (id == B.FURNACE || id == B.FURNACE_LIT)
            {
                var f = furnaces.Get(k);
                if (f != null)
                {
                    foreach (var q in new[] { f.input, f.fuel, f.output }) if (q != null) spawnWorldDrop(q.key, q.count, x + 0.5, y + 0.65, z + 0.5, q.dur);
                    furnaces.Delete(k);
                }
            }
        }
        static List<ItemCount> D(params object[] a)
        {
            var l = new List<ItemCount>();
            for (int i = 0; i + 1 < a.Length; i += 2) l.Add(new ItemCount((string)a[i], (int)a[i + 1]));
            return l;
        }
        public static List<ItemCount> blockDrops(int id)
        {
            if (id == B.BEDROCK || isLava(id) || id == B.FIRE || id == B.GLASS || id == B.GLASS_PANE || id == B.ICE || id == B.PACKED_ICE || id == B.SNOW || id == B.BUDDING_AMETHYST) return D();
            if (id == B.GRASS || id == B.MYCELIUM || id == B.PODZOL || id == B.FARMLAND || id == B.FARMLAND_MOIST) return D(BK(B.DIRT), 1);
            if (id == B.STONE) return canHarvest(id) ? D(BK(B.COBBLE), 1) : D();
            if (id == B.DEEPSLATE) return canHarvest(id) ? D(VK("COBBLED_DEEPSLATE"), 1) : D();
            if (id == B.COAL || id == B.DEEPSLATE_COAL_ORE) return canHarvest(id) ? D("coal", 1) : D();
            if (id == B.IRON || id == B.DEEPSLATE_IRON_ORE) return canHarvest(id) ? D("raw_iron", 1) : D();
            if (id == B.GOLD || id == B.DEEPSLATE_GOLD_ORE) return canHarvest(id) ? D("raw_gold", 1) : D();
            if (id == B.COPPER || id == B.DEEPSLATE_COPPER_ORE) return canHarvest(id) ? D("raw_copper", 2 + (int)Math.Floor(JS.random() * 3)) : D();
            if (id == B.DIAMOND || id == B.DEEPSLATE_DIAMOND_ORE) return canHarvest(id) ? D("diamond", 1) : D();
            if (id == B.EMERALD_ORE || id == B.DEEPSLATE_EMERALD_ORE) return canHarvest(id) ? D("emerald", 1) : D();
            if (id == B.REDSTONE_ORE || id == B.DEEPSLATE_REDSTONE_ORE || id == B.LAPIS_ORE || id == B.DEEPSLATE_LAPIS_ORE) return canHarvest(id) ? D(BK(id), 1) : D();
            if (id == B.OBSIDIAN || id == B.CRYING_OBSIDIAN) return canHarvest(id) ? D(BK(id), 1) : D();
            if (id == B.CLAY) return D("clay_ball", 4);
            if (id == B.GRAVEL) return JS.random() < 0.15 ? D("flint", 1) : D(BK(B.GRAVEL), 1);
            if (id == B.BOOKSHELF) return D("book", 3);
            if (id == B.MELON) return D("melon_slice", randInt(3, 5));
            var crop = cropInfo(id);
            if (crop != null)
            {
                if (crop.type == "wheat") return crop.idx == 3 ? D("wheat", 1, "wheat_seeds", randInt(1, 3)) : D("wheat_seeds", 1);
                if (crop.type == "carrot") return D("carrot", crop.idx == 3 ? randInt(2, 4) : 1);
                if (crop.type == "potato") return D("potato", crop.idx == 3 ? randInt(2, 4) : 1);
                if (crop.type == "pumpkin") return D("pumpkin_seeds", crop.idx == 3 ? randInt(1, 3) : 1);
                if (crop.type == "melon") return D("melon_seeds", crop.idx == 3 ? randInt(1, 3) : 1);
            }
            if (id == B.SWEET_BERRY_BUSH) return D("sweet_berries", 2);
            if ((id == B.SHORT_GRASS || id == B.FERN) && JS.random() < 0.2) return D("wheat_seeds", 1);
            if (id == B.DEAD_BUSH) return D();
            if (isTreeLeaves(id))
            {
                var t = currentTool();
                if (t != null && t.tool == "shears") return D(BK(id), 1);
                double r = JS.random(); bool jungle = id == B.JUNGLE_LEAVES; double sapChance = jungle ? 0.025 : 0.05;
                string sap = BK(B.SAPLING);
                if (id == B.BIRCH_LEAVES) sap = VK("BIRCH_SAPLING");
                else if (id == B.SPRUCE_LEAVES) sap = VK("SPRUCE_SAPLING");
                else if (id == B.JUNGLE_LEAVES) sap = VK("JUNGLE_SAPLING");
                else if (id == B.ACACIA_LEAVES) sap = VK("ACACIA_SAPLING");
                else if (id == B.DARK_LEAVES) sap = VK("DARK_OAK_SAPLING");
                if (r < sapChance) return D(sap, 1);
                if (r < sapChance + 0.02) return D("stick", 1);
                if ((id == B.LEAVES || id == B.DARK_LEAVES || id == B.AZALEA_LEAVES || id == B.FLOWERING_AZALEA_LEAVES) && r < sapChance + 0.06) return D("apple", 1);
                return D();
            }
            if (id == B.COBWEB) return D("string", 1);
            if (id == B.FURNACE_LIT) return D(BK(B.FURNACE), 1);
            if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) return D();
            if (id == B.DOOR_OPEN) return D(BK(B.DOOR), 1);
            return D(BK(id), 1);
        }

        // ---------------- support ----------------
        static bool topSupportId(int id)
        {
            var b = bdef(id);
            return b != null && b.solid && !b.plant && !b.waterPlant && !isFluidWater(id) && !isLava(id);
        }
        static readonly HashSet<string> VIRTUAL_NON_SUPPORT_SHAPES = new HashSet<string> { "button", "plate", "carpet", "rail", "pot", "door", "trapdoor", "tallplant", "cross", "vine", "lichen", "lilypad", "seapickle", "bamboo" };
        static bool virtualSolidSupport(VirtualDef d) { return d != null && d.solid && !d.plant && !d.waterPlant && !VIRTUAL_NON_SUPPORT_SHAPES.Contains(d.shape ?? ""); }
        public static bool topSupportAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (!isVirtualId(id)) return topSupportId(id);
            return virtualSolidSupport(virtualDefAt(x, y, z));
        }
        public static bool sideSupportAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (isVirtualId(id)) return virtualSolidSupport(virtualDefAt(x, y, z));
            var b = bdef(id);
            return b != null && b.solid && !b.plant && !b.waterPlant && !isFluidWater(id) && !isLava(id);
        }
        public static int[] attachmentDir(string f) { return f == "west" ? new[] { -1, 0 } : f == "east" ? new[] { 1, 0 } : f == "north" ? new[] { 0, -1 } : new[] { 0, 1 }; }
        public static string attachmentMountFromPlacement(Hit h, Hit p)
        {
            int dx = p.x - h.x, dy = p.y - h.y, dz = p.z - h.z;
            if (dy > 0) return "floor"; if (dy < 0) return "ceiling";
            if (dx > 0) return "west"; if (dx < 0) return "east";
            if (dz > 0) return "north"; if (dz < 0) return "south";
            return "south";
        }
        public static string attachmentMountFromMeta(JObj m, string fallback = "south")
        {
            var q = m != null ? m.Str("mount") : null;
            if (q == "floor" || q == "ceiling" || q == "west" || q == "east" || q == "north" || q == "south") return q;
            var f = m != null ? m.Str("facing") : null;
            return f == "west" || f == "east" || f == "north" || f == "south" ? f : fallback;
        }
        public static int attachmentBitForMount(string m) { return m == "south" ? 1 : m == "west" ? 2 : m == "north" ? 4 : m == "east" ? 8 : m == "ceiling" ? 16 : m == "floor" ? 32 : 0; }
        public static int attachmentFaceMask(JObj m, int allowed = 63)
        {
            int raw = finKey(m, "faces") ? JS.toInt32(m.Num("faces")) : attachmentBitForMount(attachmentMountFromMeta(m));
            return raw & allowed;
        }
        public static bool attachmentSupportAt(int x, int y, int z, string mount)
        {
            if (mount == "floor") return topSupportAt(x, y - 1, z);
            if (mount == "ceiling") return sideSupportAt(x, y + 1, z);
            var d = attachmentDir(mount);
            return sideSupportAt(x + d[0], y, z + d[1]);
        }
        public static int supportedAttachmentMask(int x, int y, int z, int mask = 63)
        {
            int n = 0;
            if ((mask & 1) != 0 && sideSupportAt(x, y, z + 1)) n |= 1;
            if ((mask & 2) != 0 && sideSupportAt(x - 1, y, z)) n |= 2;
            if ((mask & 4) != 0 && sideSupportAt(x, y, z - 1)) n |= 4;
            if ((mask & 8) != 0 && sideSupportAt(x + 1, y, z)) n |= 8;
            if ((mask & 16) != 0 && sideSupportAt(x, y + 1, z)) n |= 16;
            if ((mask & 32) != 0 && topSupportAt(x, y - 1, z)) n |= 32;
            return n;
        }
        public static List<double[]> multiFaceBoxes(int mask)
        {
            var a = new List<double[]>();
            if ((mask & 1) != 0) a.Add(new double[] { 0, 0, 0.875, 1, 1, 1 });
            if ((mask & 2) != 0) a.Add(new double[] { 0, 0, 0, 0.125, 1, 1 });
            if ((mask & 4) != 0) a.Add(new double[] { 0, 0, 0, 1, 1, 0.125 });
            if ((mask & 8) != 0) a.Add(new double[] { 0.875, 0, 0, 1, 1, 1 });
            if ((mask & 16) != 0) a.Add(new double[] { 0, 0.875, 0, 1, 1, 1 });
            if ((mask & 32) != 0) a.Add(new double[] { 0, 0, 0, 1, 0.125, 1 });
            if (a.Count == 0) a.Add(new double[] { 0, 0, 0.875, 1, 1, 1 });
            return a;
        }
        public static double[] buttonBoxFromMeta(JObj m)
        {
            switch (attachmentMountFromMeta(m))
            {
                case "floor": return new[] { 0.3125, 0, 0.375, 0.6875, 0.125, 0.625 };
                case "ceiling": return new[] { 0.3125, 0.875, 0.375, 0.6875, 1, 0.625 };
                case "west": return new[] { 0, 0.375, 0.3125, 0.125, 0.625, 0.6875 };
                case "east": return new[] { 0.875, 0.375, 0.3125, 1, 0.625, 0.6875 };
                case "north": return new[] { 0.3125, 0.375, 0, 0.6875, 0.625, 0.125 };
                default: return new[] { 0.3125, 0.375, 0.875, 0.6875, 0.625, 1 };
            }
        }
        public static double[] torchBoxFromMeta(JObj m)
        {
            switch (attachmentMountFromMeta(m, "floor"))
            {
                case "west": return new[] { 0, 0.15, 0.35, 0.34, 0.85, 0.65 };
                case "east": return new[] { 0.66, 0.15, 0.35, 1, 0.85, 0.65 };
                case "north": return new[] { 0.35, 0.15, 0, 0.65, 0.85, 0.34 };
                case "south": return new[] { 0.35, 0.15, 0.66, 0.65, 0.85, 1 };
                default: return new[] { 0.375, 0, 0.375, 0.625, 0.7, 0.625 };
            }
        }

        // ---------------- neighbour transforms ----------------
        public static readonly int[][] MAIN_NEIGHBOR_DIRS = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        static Dictionary<int, string> DRY_CORAL_VIRTUAL;
        static Dictionary<int, string> dryCoral()
        {
            return DRY_CORAL_VIRTUAL ?? (DRY_CORAL_VIRTUAL = new Dictionary<int, string> {
                { B.TUBE_CORAL_BLOCK, "DEAD_TUBE_CORAL_BLOCK" }, { B.TUBE_CORAL_FAN, "DEAD_TUBE_CORAL_FAN" },
                { B.BRAIN_CORAL_BLOCK, "DEAD_BRAIN_CORAL_BLOCK" }, { B.BRAIN_CORAL_FAN, "DEAD_BRAIN_CORAL_FAN" },
                { B.BUBBLE_CORAL_BLOCK, "DEAD_BUBBLE_CORAL_BLOCK" }, { B.BUBBLE_CORAL_FAN, "DEAD_BUBBLE_CORAL_FAN" },
                { B.FIRE_CORAL_BLOCK, "DEAD_FIRE_CORAL_BLOCK" }, { B.FIRE_CORAL_FAN, "DEAD_FIRE_CORAL_FAN" },
                { B.HORN_CORAL_BLOCK, "DEAD_HORN_CORAL_BLOCK" }, { B.HORN_CORAL_FAN, "DEAD_HORN_CORAL_FAN" } });
        }
        static bool waterVolumeAtCell(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (hasWaterVolume(id)) return true;
            if (!isVirtualId(id)) return false;
            var m = getBlockMeta(x, y, z);
            return m != null && m.Bool("waterlogged");
        }
        public static bool anyNeighborWater(int x, int y, int z)
        {
            foreach (var d in MAIN_NEIGHBOR_DIRS) if (waterVolumeAtCell(x + d[0], y + d[1], z + d[2])) return true;
            return false;
        }
        static string concreteSolidKey(string powderKey)
        {
            if (powderKey == null || !powderKey.EndsWith("_CONCRETE_POWDER", StringComparison.Ordinal)) return null;
            var k = powderKey.Substring(0, powderKey.Length - 7);
            return VIRTUAL_BLOCKS.ContainsKey(k) ? k : null;
        }
        public static bool hardenConcretePowderAt(int x, int y, int z, bool forceWater = false)
        {
            int id = getBlock(x, y, z);
            if (!isVirtualId(id)) return false;
            var m = getBlockMeta(x, y, z) ?? new JObj();
            var solidKey = concreteSolidKey(m.Str("v"));
            if (solidKey == null || (!forceWater && !anyNeighborWater(x, y, z))) return false;
            var d = VirtualByKey(solidKey);
            if (d == null) return false;
            var nm = m.Clone(); nm["v"] = solidKey; nm.Remove("waterlogged");
            setBlock(x, y, z, virtualCarrierFor(d), nm);
            return true;
        }
        static bool dryCoralAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z); string deadKey;
            if (!dryCoral().TryGetValue(id, out deadKey) || anyNeighborWater(x, y, z)) return false;
            var d = VirtualByKey(deadKey);
            if (d == null) return false;
            var nm = (getBlockMeta(x, y, z) ?? new JObj()).Clone(); nm["v"] = deadKey;
            setBlock(x, y, z, virtualCarrierFor(d), nm);
            return true;
        }
        static bool spongeAbsorbActive = false;
        static bool absorbSpongeAt(int x, int y, int z)
        {
            if (spongeAbsorbActive) return false;
            int id = getBlock(x, y, z);
            var m = isVirtualId(id) ? getBlockMeta(x, y, z) : null;
            if (m == null || m.Str("v") != "SPONGE") return false;
            var q = new List<int[]>(); var seen = new HashSet<string>(); var water = new List<int[]>();
            Action<int, int, int, int> push = (xx, yy, zz, depth) =>
            {
                var k = xx + "," + yy + "," + zz;
                if (!seen.Add(k)) return;
                if (isFluidWater(getBlock(xx, yy, zz))) { water.Add(new[] { xx, yy, zz }); q.Add(new[] { xx, yy, zz, depth }); }
            };
            foreach (var d in MAIN_NEIGHBOR_DIRS) push(x + d[0], y + d[1], z + d[2], 1);
            for (int h = 0; h < q.Count && water.Count < 65; h++)
            {
                var e = q[h];
                if (e[3] >= 7) continue;
                foreach (var d in MAIN_NEIGHBOR_DIRS) { if (water.Count >= 65) break; push(e[0] + d[0], e[1] + d[1], e[2] + d[2], e[3] + 1); }
            }
            if (water.Count == 0) return false;
            var wet = VirtualByKey("WET_SPONGE");
            if (wet == null) return false;
            spongeAbsorbActive = true;
            try
            {
                var nm = m.Clone(); nm["v"] = "WET_SPONGE";
                setBlock(x, y, z, virtualCarrierFor(wet), nm);
                for (int i = 0; i < water.Count && i < 65; i++) setBlock(water[i][0], water[i][1], water[i][2], B.AIR);
            }
            finally { spongeAbsorbActive = false; }
            return true;
        }
        public static bool applyMainNeighborTransformAt(int x, int y, int z)
        {
            return hardenConcretePowderAt(x, y, z, false) || dryCoralAt(x, y, z) || absorbSpongeAt(x, y, z);
        }

        // ---------------- saplings / attachments ----------------
        public static readonly OrderedMap<string, string> VIRTUAL_SAPLING_SPECIES = new OrderedMap<string, string>();
        static void InitSaplingSpecies()
        {
            VIRTUAL_SAPLING_SPECIES.Clear();
            VIRTUAL_SAPLING_SPECIES.Set("OAK_SAPLING", "oak"); VIRTUAL_SAPLING_SPECIES.Set("BIRCH_SAPLING", "birch"); VIRTUAL_SAPLING_SPECIES.Set("SPRUCE_SAPLING", "spruce");
            VIRTUAL_SAPLING_SPECIES.Set("JUNGLE_SAPLING", "jungle"); VIRTUAL_SAPLING_SPECIES.Set("ACACIA_SAPLING", "acacia"); VIRTUAL_SAPLING_SPECIES.Set("DARK_OAK_SAPLING", "dark_oak");
        }
        public static string saplingSpeciesAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (id == B.SAPLING) return "oak";
            if (!isVirtualId(id)) return null;
            var m = getBlockMeta(x, y, z); var v = m != null ? m.Str("v") : null;
            return v != null ? VIRTUAL_SAPLING_SPECIES.Get(v) : null;
        }
        public static bool saplingGroundId(int id) { return id == B.GRASS || id == B.DIRT || id == B.PODZOL || id == B.FARMLAND || id == B.FARMLAND_MOIST; }
        static string virtualSaplingSpecies(VirtualDef d)
        {
            if (d == null) return null;
            foreach (var kv in VIRTUAL_SAPLING_SPECIES) if (VirtualByKey(kv.Key) == d) return kv.Value;
            return null;
        }
        static bool virtualPairMatches(int x, int y, int z, string key, string part = null)
        {
            int id = getBlock(x, y, z); var m = getBlockMeta(x, y, z);
            return isVirtualId(id) && m != null && m.Str("v") == key && (part == null || m.Str("part") == part);
        }
        static readonly HashSet<string> SUPPORT_BELOW_SHAPES = new HashSet<string> { "seapickle", "bamboo", "rail", "carpet", "plate", "pot" };
        static bool virtualAttachmentSupported(int x, int y, int z, JObj m, VirtualDef d)
        {
            string k = (m != null ? m.Str("v") : null) ?? ""; int below = getBlock(x, y - 1, z);
            if (virtualSaplingSpecies(d) != null) return saplingGroundId(below);
            if (m != null && m.Bool("bedPart"))
            {
                if (!topSupportAt(x, y - 1, z) || !finKey(m, "mateX") || !finKey(m, "mateZ")) return false;
                var mm = getBlockMeta(m.Int("mateX"), y, m.Int("mateZ"));
                return mm != null && mm.Bool("bedPart") && finKey(mm, "mateX") && mm.Num("mateX") == x && finKey(mm, "mateZ") && mm.Num("mateZ") == z;
            }
            if (d.shape == "door" || d.shape == "tallplant")
            {
                if (m != null && m.Str("part") == "top") return virtualPairMatches(x, y - 1, z, k, "bottom");
                return topSupportAt(x, y - 1, z) && virtualPairMatches(x, y + 1, z, k, "top");
            }
            if (d.shape == "button") return attachmentSupportAt(x, y, z, attachmentMountFromMeta(m));
            if (d.shape == "vine" || d.shape == "lichen")
            {
                int allow = d.shape == "vine" ? 15 : 63;
                return supportedAttachmentMask(x, y, z, attachmentFaceMask(m, allow)) != 0;
            }
            if (k == "WEEPING_VINES") return virtualPairMatches(x, y + 1, z, k) || topSupportAt(x, y + 1, z);
            if (k == "TWISTING_VINES") return virtualPairMatches(x, y - 1, z, k) || topSupportAt(x, y - 1, z);
            if (d.shape == "lilypad") return hasWaterVolume(below) || below == B.ICE || below == B.PACKED_ICE;
            if (d.waterPlant) return m != null && m.Bool("waterlogged") && topSupportAt(x, y - 1, z);
            if (SUPPORT_BELOW_SHAPES.Contains(d.shape ?? "") || d.plant) return topSupportAt(x, y - 1, z);
            return true;
        }
        public static bool attachmentSupported(int x, int y, int z, int id)
        {
            int below = getBlock(x, y - 1, z); var b = bdef(id); var sp = b != null ? b.special : null;
            var m = getBlockMeta(x, y, z) ?? new JObj();
            if (sp == "web") return true;
            if (sp == "fire") return topSupportAt(x, y - 1, z);
            if (sp == "lilypad") return hasWaterVolume(below) || below == B.ICE;
            if (id == B.TORCH) return attachmentMountFromMeta(m, "floor") != "ceiling" && attachmentSupportAt(x, y, z, attachmentMountFromMeta(m, "floor"));
            if (id == B.BED) return topSupportAt(x, y - 1, z);
            if (isCropBlock(id)) return below == B.FARMLAND || below == B.FARMLAND_MOIST;
            if (id == B.SAPLING) return saplingGroundId(below);
            if (id == B.CACTUS)
                return (below == B.SAND || below == B.RED_SAND || below == B.CACTUS) && !sideSupportAt(x + 1, y, z) && !sideSupportAt(x - 1, y, z) && !sideSupportAt(x, y, z + 1) && !sideSupportAt(x, y, z - 1);
            if (sp == "bamboo") return topSupportAt(x, y - 1, z) || below == B.BAMBOO;
            if (sp == "seapickle") return topSupportAt(x, y - 1, z);
            if (id == B.CAVE_VINES) { int above = getBlock(x, y + 1, z); return topSupportAt(x, y + 1, z) || above == B.CAVE_VINES; }
            if (sp == "rail" || sp == "carpet" || sp == "plate" || sp == "pot") return topSupportAt(x, y - 1, z);
            if (sp == "button") return attachmentSupportAt(x, y, z, attachmentMountFromMeta(m));
            if (sp == "vine" || sp == "lichen") { int allow = sp == "vine" ? 15 : 63; return supportedAttachmentMask(x, y, z, attachmentFaceMask(m, allow)) != 0; }
            if (id == B.LADDER) { var d = attachmentDir(m.Str("facing") ?? "south"); return sideSupportAt(x + d[0], y, z + d[1]); }
            if (id == B.KELP) return topSupportAt(x, y - 1, z) || below == B.KELP;
            if (id == B.SEAGRASS) return topSupportAt(x, y - 1, z);
            if (b != null && b.waterPlant) return topSupportAt(x, y - 1, z);
            if (b != null && b.plant)
            {
                if (id == B.DEAD_BUSH) return below == B.SAND || below == B.RED_SAND;
                if (id == B.RED_MUSHROOM || id == B.BROWN_MUSHROOM) return topSupportAt(x, y - 1, z);
                return below == B.GRASS || below == B.DIRT || below == B.PODZOL || below == B.MYCELIUM || below == B.MUD || below == B.MOSS_BLOCK || below == B.ROOTED_DIRT || below == B.FARMLAND || below == B.FARMLAND_MOIST;
            }
            if (id == B.DOOR || id == B.DOOR_OPEN) { int top = getBlock(x, y + 1, z); return topSupportAt(x, y - 1, z) && (top == B.DOOR_TOP || top == B.DOOR_OPEN_TOP); }
            if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) { int bot = getBlock(x, y - 1, z); return bot == B.DOOR || bot == B.DOOR_OPEN; }
            return true;
        }
        static bool attachmentMayNeedSupport(int id)
        {
            var b = bdef(id);
            if (b == null) return false;
            if (b.plant || b.waterPlant) return true;
            if (id == B.TORCH || id == B.BED || id == B.CACTUS || id == B.CAVE_VINES || id == B.LADDER || id == B.DOOR || id == B.DOOR_OPEN || id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) return true;
            switch (b.special)
            {
                case "fire": case "lilypad": case "bamboo": case "seapickle": case "rail": case "carpet": case "plate": case "pot": case "button": case "vine": case "lichen": return true;
                default: return false;
            }
        }
        static readonly Regex NO_DROP_VIRTUAL = new Regex("GLASS|PANE|(^|_)ICE$|PACKED_ICE|BLUE_ICE");
        public static string virtualDropKey(string v)
        {
            if (v == "DIRT_PATH") return BK(B.DIRT);
            var d = VirtualByKey(v);
            if ((d != null && d.noDrop) || NO_DROP_VIRTUAL.IsMatch(v ?? "")) return null;
            return VK(v);
        }
        static bool detachUnsupportedAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (id == 0) return false;
            if (isVirtualId(id))
            {
                var m = getBlockMeta(x, y, z) ?? new JObj(); var d = virtualDefFromMeta(m);
                if (d == null || virtualAttachmentSupported(x, y, z, m, d)) return false;
                var dropKey = virtualDropKey(m.Str("v")); int restore = d.waterPlant || m.Bool("waterlogged") ? B.WATER : B.AIR;
                if (m.Bool("potKey")) dropPotContent(m, x, y, z);
                if (m.Bool("bedPart") && finKey(m, "mateX") && finKey(m, "mateZ"))
                {
                    int mx = m.Int("mateX"), mz = m.Int("mateZ"); var mm = getBlockMeta(mx, y, mz) ?? new JObj();
                    if (isVirtualId(getBlock(mx, y, mz)) && mm.Bool("bedPart")) setBlock(mx, y, mz, mm.Bool("waterlogged") ? B.WATER : B.AIR);
                    var v = m.Str("v") ?? "";
                    if (v.StartsWith("BED_HEAD", StringComparison.Ordinal)) dropKey = VK("BED" + v.Substring(8));
                }
                else if (d.shape == "door" || d.shape == "tallplant")
                {
                    int oy = m.Str("part") == "top" ? -1 : 1; var om = getBlockMeta(x, y + oy, z) ?? new JObj();
                    if (isVirtualId(getBlock(x, y + oy, z)) && om.Str("v") == m.Str("v")) setBlock(x, y + oy, z, om.Bool("waterlogged") ? B.WATER : B.AIR);
                }
                setBlock(x, y, z, restore);
                if (!player.creative && dropKey != null) spawnWorldDrop(dropKey, 1, x + 0.5, y + 0.55, z + 0.5);
                return true;
            }
            if (!attachmentMayNeedSupport(id) || attachmentSupported(x, y, z, id)) return false;
            int dropId = id; var oldMeta = getBlockMeta(x, y, z) ?? new JObj();
            if (id == B.FLOWER_POT && oldMeta.Bool("potKey")) dropPotContent(oldMeta, x, y, z);
            if (id == B.DOOR || id == B.DOOR_OPEN)
            {
                int top = getBlock(x, y + 1, z);
                if (top == B.DOOR_TOP || top == B.DOOR_OPEN_TOP) setBlock(x, y + 1, z, B.AIR);
                dropId = B.DOOR;
            }
            else if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) { setBlock(x, y, z, B.AIR); return true; }
            var bd = bdef(id);
            setBlock(x, y, z, bd != null && bd.waterPlant ? B.WATER : B.AIR);
            if (!player.creative) foreach (var dr in blockDrops(dropId)) spawnWorldDrop(dr.k, dr.n, x + 0.5, y + 0.55, z + 0.5);
            return true;
        }
        static bool pruneMultiFaceAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z); var m = getBlockMeta(x, y, z) ?? new JObj();
            string shape;
            if (isVirtualId(id)) { var vd = virtualDefFromMeta(m); shape = vd != null ? vd.shape : null; }
            else { var bd = bdef(id); shape = bd != null ? bd.special : null; }
            if (shape != "vine" && shape != "lichen") return false;
            int allow = shape == "vine" ? 15 : 63, old = attachmentFaceMask(m, allow), valid = supportedAttachmentMask(x, y, z, old);
            if (valid == 0) return false;
            if (valid != old) { var nm = m.Clone(); nm["faces"] = (double)valid; setBlock(x, y, z, id, nm); }
            return true;
        }
        static readonly List<int> attachmentUpdateQueue = new List<int>();
        static bool attachmentUpdateActive = false;
        public static void validateAttachmentsAround(int x, int y, int z)
        {
            var q = attachmentUpdateQueue;
            q.Add(x); q.Add(y); q.Add(z); q.Add(x); q.Add(y + 1); q.Add(z); q.Add(x); q.Add(y - 1); q.Add(z);
            q.Add(x + 1); q.Add(y); q.Add(z); q.Add(x - 1); q.Add(y); q.Add(z); q.Add(x); q.Add(y); q.Add(z + 1); q.Add(x); q.Add(y); q.Add(z - 1);
            if (attachmentUpdateActive) return;
            attachmentUpdateActive = true;
            try
            {
                for (int h = 0; h < q.Count; h += 3)
                {
                    int qx = q[h], qy = q[h + 1], qz = q[h + 2];
                    applyMainNeighborTransformAt(qx, qy, qz);
                    if (pruneMultiFaceAt(qx, qy, qz)) continue;
                    detachUnsupportedAt(qx, qy, qz);
                }
            }
            finally { q.Clear(); attachmentUpdateActive = false; }
        }
    }
}
