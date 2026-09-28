namespace BlockcraftPort
{
    /// <summary>
    /// Source main.js Z6()/zc() mining constants for the block subset currently represented by BlockId.
    /// R48 connects these source class/tier rules to the survival inventory and tool durability path.
    /// </summary>
    public static class BlockMiningRules
    {
        public enum ToolClass : byte { None, Pickaxe, Axe, Shovel }
        public readonly struct Rule
        {
            public readonly float Hardness; public readonly ToolClass Class; public readonly int Tier; public readonly bool NoDrop;
            public Rule(float hardness,ToolClass cls=ToolClass.None,int tier=0,bool noDrop=false)
            { Hardness=hardness;Class=cls;Tier=tier;NoDrop=noDrop; }
            public bool CanDropByHand => !NoDrop && (Class==ToolClass.None || Tier==0);
            // main zc(): C = hard * (canDrop || noDrop ? 1.5 : 5)
            public float HandWork => Hardness * (CanDropByHand || NoDrop ? 1.5f : 5f);
        }

        public static bool Matches(MainToolKind tool,ToolClass required)
        {
            if(required==ToolClass.None)return false;
            switch(required)
            {
                case ToolClass.Pickaxe:return tool==MainToolKind.Pickaxe;
                case ToolClass.Axe:return tool==MainToolKind.Axe;
                case ToolClass.Shovel:return tool==MainToolKind.Shovel;
                default:return false;
            }
        }

        public static Rule Get(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsFluid(id)||id==BlockId.Bedrock)return new Rule(-1f);
            if(id==BlockId.Cobweb)return new Rule(4f);
            if(id==BlockId.Chest)return new Rule(2.5f,ToolClass.Axe,0);
            if(id==BlockId.BedRedFoot||id==BlockId.BedRedHead)return new Rule(.2f);
            if(id==BlockId.Fire||id==BlockId.Tnt)return new Rule(0f);

            BlockShape shape=BlockRegistry.Get(id).Shape;
            if(shape==BlockShape.Cross||shape==BlockShape.TallPlant||shape==BlockShape.Torch||shape==BlockShape.Rail||
               shape==BlockShape.FlatFaces||shape==BlockShape.LilyPad||shape==BlockShape.SeaPickle||shape==BlockShape.Carpet)
                return new Rule(0f);

            if(BlockRegistry.IsClassicLeaf(id)||BlockRegistry.IsGlassy(id))return new Rule(.25f);
            if(id==BlockId.Glass||id==BlockId.GlassPane||id==BlockId.Ice||id==BlockId.PackedIce||id==BlockId.BlueIce)
                return new Rule(.45f,ToolClass.None,0,true);
            if(id==BlockId.Snow)return new Rule(.2f,ToolClass.Shovel,0,true);

            switch(id)
            {
                case BlockId.DeepslateCoalOre: case BlockId.DeepslateCopperOre: case BlockId.DeepslateIronOre:
                case BlockId.DeepslateGoldOre: case BlockId.DeepslateDiamondOre: case BlockId.DeepslateLapisOre:
                case BlockId.DeepslateRedstoneOre: case BlockId.DeepslateEmeraldOre:
                    return new Rule(4.5f,ToolClass.Pickaxe,OreTier(id));
                case BlockId.CoalOre: case BlockId.CopperOre: case BlockId.IronOre: case BlockId.GoldOre:
                case BlockId.DiamondOre: case BlockId.LapisOre: case BlockId.RedstoneOre: case BlockId.EmeraldOre:
                    return new Rule(3f,ToolClass.Pickaxe,OreTier(id));
                case BlockId.Amethyst: case BlockId.BuddingAmethyst:
                    return new Rule(5f,ToolClass.Pickaxe,2);
                case BlockId.GoldBlock:
                    return new Rule(5f,ToolClass.Pickaxe,3);
                case BlockId.Obsidian: case BlockId.CryingObsidian:
                    return new Rule(15f,ToolClass.Pickaxe,4);
            }

            switch(id)
            {
                case BlockId.OakLog: case BlockId.BirchLog: case BlockId.SpruceLog: case BlockId.JungleLog:
                case BlockId.AcaciaLog: case BlockId.DarkOakLog: case BlockId.CherryWood:
                case BlockId.OakPlanks: case BlockId.SprucePlanks: case BlockId.BirchPlanks: case BlockId.DarkOakPlanks:
                case BlockId.OakFence: case BlockId.SpruceFence: case BlockId.DarkOakFence: case BlockId.OakFenceGate:
                case BlockId.DarkOakSlab: case BlockId.DarkOakStairs:
                case BlockId.Ladder: case BlockId.MushroomStem: case BlockId.BrownMushroomBlock: case BlockId.RedMushroomBlock:
                case BlockId.CraftingTable:
                    return new Rule(2f,ToolClass.Axe,0);
                case BlockId.BirchDoor: case BlockId.OakDoor: case BlockId.OakTrapdoor:
                    return new Rule(2f,ToolClass.Axe,0);
                case BlockId.Dirt: case BlockId.Grass: case BlockId.Podzol: case BlockId.Mycelium: case BlockId.Mud:
                case BlockId.Sand: case BlockId.RedSand: case BlockId.Gravel: case BlockId.Clay:
                case BlockId.Farmland: case BlockId.FarmlandMoist:
                    return new Rule(.6f,ToolClass.Shovel,0);
                case BlockId.Moss: case BlockId.MossCarpet: case BlockId.Pumpkin: case BlockId.Melon:
                case BlockId.TubeCoralFan: case BlockId.BrainCoralFan: case BlockId.BubbleCoralFan: case BlockId.FireCoralFan: case BlockId.HornCoralFan:
                case BlockId.TubeCoralBlock: case BlockId.BrainCoralBlock: case BlockId.BubbleCoralBlock: case BlockId.FireCoralBlock: case BlockId.HornCoralBlock:
                    return new Rule(.7f);
                case BlockId.Terracotta: case BlockId.OrangeTerracotta: case BlockId.WhiteTerracotta: case BlockId.BlueTerracotta:
                    return new Rule(1.3f,ToolClass.Pickaxe,1);
                case BlockId.Netherrack:
                    return new Rule(.4f,ToolClass.Pickaxe,1);
                case BlockId.Cactus: case BlockId.SugarCane: case BlockId.BambooPlant:
                    return new Rule(.3f);
            }

            switch(id)
            {
                case BlockId.Stone: case BlockId.Cobblestone: case BlockId.Deepslate: case BlockId.Tuff: case BlockId.Calcite:
                case BlockId.Granite: case BlockId.Diorite: case BlockId.Andesite: case BlockId.Dripstone:
                case BlockId.Sandstone: case BlockId.RedSandstone: case BlockId.CutSandstone: case BlockId.ChiseledSandstone:
                case BlockId.SandstoneSlab: case BlockId.SandstoneStairs: case BlockId.StonePressurePlate:
                case BlockId.MagmaBlock: case BlockId.Furnace: case BlockId.FurnaceLit:
                case BlockId.CobblestoneWall: case BlockId.CobblestoneSlab: case BlockId.CobblestoneStairs:
                    return new Rule(1.8f,ToolClass.Pickaxe,1);
            }
            return new Rule(1f);
        }

        static int OreTier(BlockId id)
        {
            switch(id)
            {
                case BlockId.GoldOre: case BlockId.DiamondOre: case BlockId.RedstoneOre: case BlockId.EmeraldOre:
                case BlockId.DeepslateGoldOre: case BlockId.DeepslateDiamondOre: case BlockId.DeepslateRedstoneOre: case BlockId.DeepslateEmeraldOre:
                    return 3;
                case BlockId.IronOre: case BlockId.CopperOre: case BlockId.LapisOre:
                case BlockId.DeepslateIronOre: case BlockId.DeepslateCopperOre: case BlockId.DeepslateLapisOre:
                    return 2;
                default:return 1;
            }
        }
    }
}
