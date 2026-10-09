// Voxel Forge — Unity port. Static content tables (atlas tile names, item tiles, entity tiles, mob info).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelForge
{
    public sealed class VirtualDef
    {
        public string key, name, en, tab, shape, @base, tool;
        public int side, top, bottom, light, stack = 64, tier, partTop = -1, partBottom = -1;
        public bool solid, transparent, cutout, plant, waterPlant, needsWater, lightBlock, falling, noDrop, axislog;
        public double hardness;
        public double explosionResistance = 3;
        public bool hasPartTop, hasPartBottom;
    }

    public sealed class MobInfo
    {
        public double hp, w, h, speed, detect, melee, neutralLight;
        public double cw = double.NaN;
        public string loot, feed, splits;
        public bool passive, aquatic, despawns, hostile, hunter, burns, ranged, creeper, climber, leaper, teleports, hatesWater, neutral, jumper;
        public int slime;
    }

    public static partial class VF
    {
        public static List<string> TILE_NAMES;
        public static int WATER_FLOW_TILE;
        public static Dictionary<string, int> TILE;
        public static int T(string n) { int v; return TILE != null && TILE.TryGetValue(n, out v) ? v : -1; }
        public static class RENDER_TILE
        {
            public static int sun, moon, white, iron, bobber, hand;
            public static int[] cracks = new int[10];
        }
        public const int TERRAIN_ATLAS_GRID = 32;
        public static Dictionary<string, VirtualDef> VIRTUAL_BLOCKS;
        public static List<string> VIRTUAL_CREATIVE_KEYS;
        public static readonly byte[] TERRAIN_ALPHA_TEST_TILE = new byte[768];

        static readonly string[] ALPHA_TEST_TILE_NAMES = {
            "oak_leaves","glass","dandelion","poppy","blue_orchid","allium","azure_bluet","cornflower","fern","dead_bush","seagrass","kelp",
            "red_mushroom","brown_mushroom","torch","ladder","wheat0","wheat1","wheat2","wheat3","oak_sapling","cactus_side","cactus_top","fire",
            "cobweb","rail","oak_trapdoor","vine","glow_lichen","lily_pad","sea_pickle","spruce_leaves","birch_leaves","jungle_leaves","acacia_leaves",
            "dark_oak_leaves","flowering_azalea_leaves","bamboo_stalk","tube_coral_fan","brain_coral_fan","bubble_coral_fan","fire_coral_fan",
            "horn_coral_fan","cave_vines","azalea_leaves","short_grass","oxeye_daisy","lily_of_the_valley","orange_tulip","pink_tulip","red_tulip",
            "white_tulip","sweet_berry_bush","sugar_cane","large_fern_bottom","large_fern_top","sunflower_bottom","sunflower_front","lilac_bottom",
            "lilac_top","rose_bush_bottom","rose_bush_top","peony_bottom","peony_top","carrots_stage0","carrots_stage1","carrots_stage2",
            "carrots_stage3","potatoes_stage0","potatoes_stage1","potatoes_stage2","potatoes_stage3","pumpkin_stem_stage0","pumpkin_stem_stage1",
            "pumpkin_stem_stage2","pumpkin_stem_stage3","melon_stem_stage0","melon_stem_stage1","melon_stem_stage2","melon_stem_stage3" };

        public static readonly string[] ITEM_TILE_NAMES = {
            "stick","coal","raw_iron","raw_gold","iron_ingot","gold_ingot","diamond","clay_ball","flint","book","melon_slice","beef","cooked_beef",
            "leather","pork","cooked_pork","wool","chicken","cooked_chicken","feather","salmon","cooked_salmon","fish","bone","gunpowder",
            "rotten_flesh","arrow","wheat","wheat_seeds","bread","wooden_pickaxe","stone_pickaxe","iron_pickaxe","diamond_pickaxe","wooden_axe",
            "stone_axe","iron_axe","diamond_axe","wooden_shovel","stone_shovel","iron_shovel","diamond_shovel","wooden_sword","stone_sword",
            "iron_sword","diamond_sword","wooden_hoe","stone_hoe","iron_hoe","diamond_hoe","mutton","cooked_mutton","carrot","potato",
            "baked_potato","cooked_fish","shears","bow","string","spider_eye","apple","brick","bucket","water_bucket","lava_bucket","raw_copper",
            "copper_ingot","green_dye","flint_and_steel","leather_helmet","leather_chestplate","leather_leggings","leather_boots","iron_helmet",
            "iron_chestplate","iron_leggings","iron_boots","golden_helmet","golden_chestplate","golden_leggings","golden_boots","diamond_helmet",
            "diamond_chestplate","diamond_leggings","diamond_boots","egg","ender_pearl","slime_ball","minecart","boat","fishing_rod","bone_meal",
            "pumpkin_seeds","melon_seeds","sweet_berries","emerald","golden_apple","golden_carrot","sugar","pig_spawn_egg","cow_spawn_egg",
            "sheep_spawn_egg","chicken_spawn_egg","zombie_spawn_egg","skeleton_spawn_egg","creeper_spawn_egg","golden_pickaxe","golden_axe",
            "golden_shovel","golden_sword","golden_hoe","bowl","mushroom_stew","white_dye","orange_dye","magenta_dye","light_blue_dye","yellow_dye",
            "lime_dye","pink_dye","gray_dye","light_gray_dye","cyan_dye","purple_dye","blue_dye","brown_dye","red_dye","black_dye",
            "spider_spawn_egg","enderman_spawn_egg","salmon_spawn_egg","slime_big_spawn_egg","shark_spawn_egg","pumpkin_pie" };
        public static readonly Dictionary<string, int> ITEM_TILE = new Dictionary<string, int>();
        public static int ItemTile(string k) { int v; return k != null && ITEM_TILE.TryGetValue(k, out v) ? v : -1; }

        public static readonly Dictionary<string, int> ENTITY_TILE = new Dictionary<string, int> {
            {"pig",0},{"cow",1},{"sheep",2},{"chicken",3},{"zombie",4},{"skeleton",5},{"creeper",6},{"sheep_sheared",7},{"sheep_black",8},
            {"sheep_gray",9},{"sheep_light_gray",10},{"sheep_brown",11},{"sheep_pink",12},{"sheep_sheared_black",13},{"sheep_sheared_gray",14},
            {"sheep_sheared_light_gray",15},{"sheep_sheared_brown",16},{"sheep_sheared_pink",17},{"spider",20},{"enderman",21},{"slime",22},
            {"salmon",23},{"shark",24},{"player",25} };

        public static readonly Dictionary<string, MobInfo> MOB_INFO = new Dictionary<string, MobInfo>();

        static TextAsset LoadRes(string name) { return Resources.Load<TextAsset>("VoxelForge/" + name); }

        static void InitData()
        {
            // TILE_NAMES (+ runtime water flow tile)
            TILE_NAMES = new List<string>();
            foreach (var o in (List<object>)Json.Parse(LoadRes("tile_names").text)) TILE_NAMES.Add((string)o);
            WATER_FLOW_TILE = TILE_NAMES.Count; TILE_NAMES.Add("water_flow_runtime");
            TILE = new Dictionary<string, int>();
            for (int i = 0; i < TILE_NAMES.Count; i++) TILE[TILE_NAMES[i]] = i;
            RENDER_TILE.sun = T("render_sun"); RENDER_TILE.moon = T("render_moon"); RENDER_TILE.white = T("render_white");
            RENDER_TILE.iron = T("render_iron_block"); RENDER_TILE.bobber = T("render_bobber"); RENDER_TILE.hand = T("render_hand");
            for (int i = 0; i < 10; i++) RENDER_TILE.cracks[i] = T("render_crack" + i);

            // Virtual (main catalog) blocks
            VIRTUAL_BLOCKS = new Dictionary<string, VirtualDef>();
            var vb = (JObj)Json.Parse(LoadRes("virtual_blocks").text);
            foreach (var kv in vb)
            {
                var o = (JObj)kv.Value;
                var d = new VirtualDef
                {
                    key = kv.Key, name = o.Str("name"), en = o.Str("en"), tab = o.Str("tab"), shape = o.Str("shape"), @base = o.Str("base"),
                    side = o.Int("side"), top = o.Int("top"), bottom = o.Int("bottom"), solid = o.Bool("solid"), transparent = o.Bool("transparent"),
                    cutout = o.Bool("cutout"), plant = o.Bool("plant"), waterPlant = o.Bool("waterPlant"), needsWater = o.Bool("needsWater"),
                    light = o.Int("light"), lightBlock = o.Bool("lightBlock"), falling = o.Bool("falling"), stack = o.Has("stack") ? o.Int("stack") : 64,
                    hardness = o.Num("hardness"), tool = o.Str("tool"), tier = o.Int("tier"), noDrop = o.Bool("noDrop"), axislog = o.Bool("axislog"),
                };
                if (o.Has("partTop") && o.Get("partTop") != null) { d.partTop = o.Int("partTop"); d.hasPartTop = true; }
                if (o.Has("partBottom") && o.Get("partBottom") != null) { d.partBottom = o.Int("partBottom"); d.hasPartBottom = true; }
                VIRTUAL_BLOCKS[kv.Key] = d;
            }
            VIRTUAL_CREATIVE_KEYS = new List<string>();
            foreach (var o in (List<object>)Json.Parse(LoadRes("virtual_creative_keys").text)) VIRTUAL_CREATIVE_KEYS.Add((string)o);

            Array.Clear(TERRAIN_ALPHA_TEST_TILE, 0, TERRAIN_ALPHA_TEST_TILE.Length);
            foreach (var n in ALPHA_TEST_TILE_NAMES) { int ti = T(n); if (ti >= 0) TERRAIN_ALPHA_TEST_TILE[ti] = 1; }

            ITEM_TILE.Clear();
            for (int i = 0; i < ITEM_TILE_NAMES.Length; i++) ITEM_TILE[ITEM_TILE_NAMES[i]] = i;

            InitMobInfo();
        }

        static void InitMobInfo()
        {
            MOB_INFO.Clear();
            MOB_INFO["cow"] = new MobInfo { hp = 10, w = 0.45, h = 1.4, loot = "говядина", passive = true, feed = "wheat", speed = 2.1 };
            MOB_INFO["pig"] = new MobInfo { hp = 10, w = 0.45, h = 0.95, loot = "свинина", passive = true, feed = "carrot", speed = 2.6 };
            MOB_INFO["sheep"] = new MobInfo { hp = 8, w = 0.45, h = 1.3, loot = "шерсть", passive = true, feed = "wheat", speed = 2.4 };
            MOB_INFO["chicken"] = new MobInfo { hp = 4, w = 0.25, h = 0.7, loot = "курятина", passive = true, feed = "wheat_seeds", speed = 2.5 };
            MOB_INFO["salmon"] = new MobInfo { hp = 3, w = 0.35, h = 0.4, loot = "лосось", passive = true, aquatic = true, despawns = true, speed = 1.6 };
            MOB_INFO["shark"] = new MobInfo { hp = 24, w = 0.45, h = 0.8, loot = "рыба", hostile = true, aquatic = true, hunter = true, despawns = true, detect = 20, melee = 6, speed = 4.2 };
            MOB_INFO["zombie"] = new MobInfo { hp = 20, w = 0.3, h = 1.95, loot = "гнилая плоть", hostile = true, detect = 16, melee = 3, burns = true, speed = 2.4 };
            MOB_INFO["skeleton"] = new MobInfo { hp = 20, w = 0.3, h = 1.95, loot = "кости", hostile = true, detect = 16, ranged = true, burns = true, speed = 2.6 };
            MOB_INFO["creeper"] = new MobInfo { hp = 20, w = 0.3, h = 1.6, loot = "порох", hostile = true, detect = 16, creeper = true, speed = 2.7 };
            MOB_INFO["spider"] = new MobInfo { hp = 16, w = 0.7, cw = 0.45, h = 0.9, loot = "нить", hostile = true, detect = 16, melee = 2, speed = 3.2, climber = true, leaper = true, neutralLight = 12 };
            MOB_INFO["enderman"] = new MobInfo { hp = 40, w = 0.3, h = 2.9, loot = "эндер-жемчуг", hostile = true, detect = 64, melee = 7, speed = 3.2, teleports = true, hatesWater = true, neutral = true };
            MOB_INFO["slime_big"] = new MobInfo { hp = 16, w = 1.02, h = 2.04, loot = "слизь", hostile = true, detect = 16, melee = 4, speed = 2.1, jumper = true, slime = 4, splits = "slime_med" };
            MOB_INFO["slime_med"] = new MobInfo { hp = 4, w = 0.51, h = 1.02, loot = "слизь", hostile = true, detect = 16, melee = 2, speed = 2.7, jumper = true, slime = 2, splits = "slime_small" };
            MOB_INFO["slime_small"] = new MobInfo { hp = 1, w = 0.255, h = 0.51, loot = "слизь", hostile = true, detect = 16, melee = 0, speed = 3.2, jumper = true, slime = 1, splits = null };
        }

        // ---- virtual (catalog) block keys ----
        public static string VK(string k) { return "v:" + k; }
        public static bool isVK(string k) { return k != null && k.StartsWith("v:", StringComparison.Ordinal); }
        public static string vkey(string k) { return isVK(k) ? k.Substring(2) : ""; }
        public static bool isVirtualId(int id) { return id == B.VIRTUAL_OPAQUE || id == B.VIRTUAL_TRANSPARENT || id == B.VIRTUAL_CUTOUT || id == B.VIRTUAL_PLANT; }
        public static VirtualDef VirtualByKey(string k) { VirtualDef d; return k != null && VIRTUAL_BLOCKS.TryGetValue(k, out d) ? d : null; }
        public static VirtualDef virtualDefFromMeta(JObj m) { return m != null ? VirtualByKey(m.Str("v")) : null; }
        public static VirtualDef virtualDefAt(int x, int y, int z) { return isVirtualId(getBlock(x, y, z)) ? virtualDefFromMeta(getBlockMeta(x, y, z)) : null; }
        public static int virtualCarrierFor(VirtualDef d)
        {
            if (d == null) return B.VIRTUAL_OPAQUE;
            var s = d.shape;
            if (d.plant || s == "tallplant" || s == "vine" || s == "lichen" || s == "lilypad" || s == "seapickle" || s == "bamboo" || s == "rail") return B.VIRTUAL_PLANT;
            if (d.lightBlock) return B.VIRTUAL_OPAQUE;
            if (d.transparent) return B.VIRTUAL_TRANSPARENT;
            if (d.cutout || s == "door" || s == "trapdoor") return B.VIRTUAL_CUTOUT;
            return B.VIRTUAL_OPAQUE;
        }
        public static ItemDef virtualItemDef(string k)
        {
            var d = VirtualByKey(vkey(k));
            return d != null ? new ItemDef { name = d.name ?? d.en ?? vkey(k), stack = d.stack != 0 ? d.stack : 64, @virtual = vkey(k) } : null;
        }
        static readonly System.Text.RegularExpressions.Regex RX_LOGSTEM = new System.Text.RegularExpressions.Regex("LOG|STEM|HYPHAE");
        public static bool isVirtualLogKey(string k)
        {
            if (!isVK(k)) return false;
            var n = vkey(k); var d = VirtualByKey(n);
            return d != null && d.axislog && RX_LOGSTEM.IsMatch(n);
        }
        public static string mainBlockItemKey(string k)
        {
            string A;
            switch (k) { case "BRICK": A = "BRICKS"; break; case "STONE_BRICKS": A = "STONEBRICK"; break; case "COPPER_ORE": A = "COPPER"; break; case "OAK_DOOR": A = "DOOR"; break; case "BAMBOO_PLANT": A = "BAMBOO"; break; default: A = k; break; }
            int id;
            if (B.NAMES.TryGetValue(A, out id)) return BK(id);
            return VIRTUAL_BLOCKS.ContainsKey(k) ? VK(k) : null;
        }
    }
}
