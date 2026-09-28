namespace BlockcraftPort
{
    public enum BlockId : ushort
    {
        Air = 0,
        Bedrock, Stone, Dirt, Grass, Sand, Sandstone, RedSand, RedSandstone, Gravel,
        Water, Ice, Snow, Deepslate, CoalOre, IronOre, GoldOre, DiamondOre, Granite,
        Diorite, Andesite, Tuff, Clay, Mud, Moss, Mycelium, Podzol, Terracotta, Cactus,
        OakLog, OakLeaves, BirchLog, BirchLeaves, SpruceLog, SpruceLeaves, JungleLog,
        JungleLeaves, AcaciaLog, AcaciaLeaves, DarkOakLog, DarkOakLeaves, CherryWood,
        Amethyst, BuddingAmethyst, Dripstone, Lava,
        TallGrass, Fern, Dandelion, Poppy, DeadBush, CherryLeaves, AzaleaLeaves,

        PackedIce, BlueIce, OrangeTerracotta, WhiteTerracotta,
        BambooPlant, SugarCane, SweetBerryBush,
        Allium, Cornflower, OxeyeDaisy, AzureBluet, LilyOfTheValley,
        PinkTulip, OrangeTulip, RedTulip, WhiteTulip, BlueOrchid,
        BrownMushroom, RedMushroom, MushroomStem, BrownMushroomBlock, RedMushroomBlock,

        // Deepslate V2()/Sw() natural blocks.
        CopperOre, LapisOre, RedstoneOre, EmeraldOre,
        DeepslateCoalOre, DeepslateCopperOre, DeepslateIronOre, DeepslateGoldOre,
        DeepslateDiamondOre, DeepslateLapisOre, DeepslateRedstoneOre, DeepslateEmeraldOre,
        Calcite,

        // gw() ocean / river decoration.
        Kelp, Seagrass,
        TubeCoralFan, BrainCoralFan, BubbleCoralFan, FireCoralFan, HornCoralFan,
        TubeCoralBlock, BrainCoralBlock, BubbleCoralBlock, FireCoralBlock, HornCoralBlock,
        SeaPickle, LilyPad,

        // $4()/b4() two-block and cave vegetation.
        Sunflower, Lilac, RoseBush, Peony, LargeFern,
        MossCarpet, CaveVines,

        Pumpkin, Melon,

        // meshWorker special-render first wave. Metadata bits follow main.js.
        OakPlanks, SprucePlanks, Cobblestone, Glass,
        OakSlab, SpruceSlab, CobblestoneSlab,
        OakStairs, SpruceStairs, CobblestoneStairs,
        OakFence, SpruceFence, OakFenceGate, CobblestoneWall, GlassPane,
        OakPressurePlate, OakButton, OakTrapdoor, Ladder, Torch, Rail,
        Vine, GlowLichen, FlowerPot, IronBars, ShipWheel,

        // R4 renderer parity: source functional/late blocks.
        BirchDoor, Chest, BedRedFoot, BedRedHead, CraftingTable, Furnace, FurnaceLit,
        Farmland, FarmlandMoist, Wheat0, Wheat1, Wheat2, Wheat3, Fire, Cobweb,

        // R32: main.js flowing-water levels. Appended to preserve all existing serialized numeric IDs.
        Flow3, Flow2, Flow1,

        // R34: source TNT appended to preserve all existing serialized numeric IDs.
        Tnt,

        // R36: main/genWorker structure palette. Appended only; existing save IDs stay stable.
        CutSandstone, ChiseledSandstone, BlueTerracotta,
        SandstoneSlab, SandstoneStairs, StonePressurePlate,
        BirchPlanks, DarkOakPlanks, DarkOakSlab, DarkOakStairs, DarkOakFence,
        Netherrack, MagmaBlock, Obsidian, CryingObsidian, GoldBlock,

        // R37: semantic source ID. Appended only: old CherryLeaves stays as a legacy save-compatible alias.
        FloweringAzaleaLeaves,

        // R49: source shipwreck palette; appended to preserve all prior serialized IDs.
        OakDoor,

        // R60: source leaf-decay drops. Appended only so every previous save ID remains stable.
        OakSapling, BirchSapling, SpruceSapling, JungleSapling, AcaciaSapling, DarkOakSapling,

        // R75: main.js lava flow levels. Appended only; every previous serialized ID remains stable.
        LavaFlow2, LavaFlow1
    }

    public enum RenderLayer : byte { Opaque = 0, Cutout = 1, Water = 2, Transparent = 3, None = 4 }
    public enum BlockShape : byte
    {
        Cube = 0, Cross = 1, TallPlant = 2, Bamboo = 3, LilyPad = 4, SeaPickle = 5, Carpet = 6,
        Slab = 7, Stairs = 8, Fence = 9, Gate = 10, Wall = 11, Pane = 12, Plate = 13, Button = 14,
        Trapdoor = 15, Ladder = 16, Torch = 17, Rail = 18, FlatFaces = 19, Pot = 20, ShipWheel = 21, Door = 22, Bed = 23, Chest = 24
    }
    public enum FaceDir : byte { PosX, NegX, PosY, NegY, PosZ, NegZ }
}
