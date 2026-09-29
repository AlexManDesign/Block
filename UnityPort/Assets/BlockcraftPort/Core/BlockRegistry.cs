namespace BlockcraftPort
{
    public readonly struct BlockDef
    {
        public readonly RenderLayer Layer;
        public readonly BlockShape Shape;
        public readonly bool Solid;
        public readonly bool OccludesAO;
        public readonly byte Emission;
        public readonly AtlasRect Side, Top, Bottom;

        /// <summary>meshWorker LE[]: faces of this block light at least 0.85 regardless of neighbours.</summary>
        public readonly bool Glow;

        public BlockDef(RenderLayer layer, bool solid, bool ao, AtlasRect side, AtlasRect top, AtlasRect bottom,
            byte emission = 0, BlockShape shape = BlockShape.Cube, bool glow = false)
        {
            Layer=layer; Shape=shape; Solid=solid; OccludesAO=ao;
            Side=side; Top=top; Bottom=bottom; Emission=emission; Glow=glow;
        }
        internal BlockDef WithSourceFaces(AtlasRect side, AtlasRect top, AtlasRect bottom, bool glow)
            => new BlockDef(Layer, Solid, OccludesAO, side, top, bottom, Emission, Shape, glow);
        public AtlasRect Face(FaceDir f)=>f==FaceDir.PosY?Top:f==FaceDir.NegY?Bottom:Side;
    }

    public static class BlockRegistry
    {
        // Sized for the full source id space (main.js has 674 block ids) so lookups never go out of range.
        public const int Capacity=1024;
        static readonly BlockDef[] D=new BlockDef[Capacity];
        // Hot-path classification tables. Lighting, meshing and generation query these per voxel, so the
        // compound predicates below are evaluated once per id at startup instead of on every call.
        static readonly bool[] TFullOpaque=new bool[Capacity],TWater=new bool[Capacity],TLava=new bool[Capacity],
            TAquatic=new bool[Capacity],TClassicLeaf=new bool[Capacity],TGlassy=new bool[Capacity],TXray=new bool[Capacity],
            TNeedsWater=new bool[Capacity],TAxis=new bool[Capacity],TLeafSupport=new bool[Capacity];
        static readonly byte[] TLightCost=new byte[Capacity];
        static BlockRegistry()
        {
            var air=new BlockDef(RenderLayer.None,false,false,AtlasLayout.Stone,AtlasLayout.Stone,AtlasLayout.Stone);
            for(int i=0;i<D.Length;i++)D[i]=air;
            Set(BlockId.Bedrock,O(AtlasLayout.Bedrock)); Set(BlockId.Stone,O(AtlasLayout.Stone)); Set(BlockId.Dirt,O(AtlasLayout.Dirt));
            Set(BlockId.Grass,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.GrassBlockSide,AtlasLayout.GrassBlockTop,AtlasLayout.Dirt));
            Set(BlockId.Sand,O(AtlasLayout.Sand)); Set(BlockId.Sandstone,O(AtlasLayout.Sandstone)); Set(BlockId.RedSand,O(AtlasLayout.RedSand));
            Set(BlockId.RedSandstone,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.RedSandstone,AtlasLayout.RedSandstoneTop,AtlasLayout.RedSandstoneTop));
            Set(BlockId.Gravel,O(AtlasLayout.Gravel)); Set(BlockId.Water,Fluid(AtlasLayout.WaterStill)); Set(BlockId.Flow3,Fluid(AtlasLayout.WaterStill)); Set(BlockId.Flow2,Fluid(AtlasLayout.WaterStill)); Set(BlockId.Flow1,Fluid(AtlasLayout.WaterStill)); Set(BlockId.Lava,Lava(AtlasLayout.LavaStill,14)); Set(BlockId.LavaFlow2,Lava(AtlasLayout.LavaStill,13)); Set(BlockId.LavaFlow1,Lava(AtlasLayout.LavaStill,12));
            Set(BlockId.Ice,O(AtlasLayout.Ice)); Set(BlockId.PackedIce,O(AtlasLayout.PackedIce)); Set(BlockId.BlueIce,O(AtlasLayout.BlueIce));
            Set(BlockId.Snow,O(AtlasLayout.Snow)); Set(BlockId.Deepslate,O(AtlasLayout.Deepslate)); Set(BlockId.Tuff,O(AtlasLayout.Tuff)); Set(BlockId.Calcite,O(AtlasLayout.Calcite));
            Set(BlockId.Granite,O(AtlasLayout.Granite)); Set(BlockId.Diorite,O(AtlasLayout.Diorite)); Set(BlockId.Andesite,O(AtlasLayout.Andesite));
            SetOre(BlockId.CoalOre,AtlasLayout.CoalOre); SetOre(BlockId.CopperOre,AtlasLayout.CopperOre); SetOre(BlockId.IronOre,AtlasLayout.IronOre); SetOre(BlockId.GoldOre,AtlasLayout.GoldOre); SetOre(BlockId.DiamondOre,AtlasLayout.DiamondOre); SetOre(BlockId.LapisOre,AtlasLayout.LapisOre); SetOre(BlockId.RedstoneOre,AtlasLayout.RedstoneOre); SetOre(BlockId.EmeraldOre,AtlasLayout.EmeraldOre);
            SetOre(BlockId.DeepslateCoalOre,AtlasLayout.DeepslateCoalOre); SetOre(BlockId.DeepslateCopperOre,AtlasLayout.DeepslateCopperOre); SetOre(BlockId.DeepslateIronOre,AtlasLayout.DeepslateIronOre); SetOre(BlockId.DeepslateGoldOre,AtlasLayout.DeepslateGoldOre); SetOre(BlockId.DeepslateDiamondOre,AtlasLayout.DeepslateDiamondOre); SetOre(BlockId.DeepslateLapisOre,AtlasLayout.DeepslateLapisOre); SetOre(BlockId.DeepslateRedstoneOre,AtlasLayout.DeepslateRedstoneOre); SetOre(BlockId.DeepslateEmeraldOre,AtlasLayout.DeepslateEmeraldOre);
            Set(BlockId.Clay,O(AtlasLayout.Clay)); Set(BlockId.Mud,O(AtlasLayout.Mud)); Set(BlockId.Moss,O(AtlasLayout.MossBlock));
            Set(BlockId.Mycelium,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.MyceliumSide,AtlasLayout.MyceliumTop,AtlasLayout.Dirt));
            Set(BlockId.Podzol,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.PodzolSide,AtlasLayout.PodzolTop,AtlasLayout.Dirt));
            Set(BlockId.Terracotta,O(AtlasLayout.Terracotta)); Set(BlockId.OrangeTerracotta,O(AtlasLayout.OrangeTerracotta)); Set(BlockId.WhiteTerracotta,O(AtlasLayout.WhiteTerracotta));
            // main's FA() explicitly excludes cactus from full-face occlusion/AO.
            Set(BlockId.Cactus,new BlockDef(RenderLayer.Opaque,true,false,AtlasLayout.CactusSide,AtlasLayout.CactusTop,AtlasLayout.CactusTop));
            SetLog(BlockId.OakLog,AtlasLayout.OakLog,AtlasLayout.OakLogTop); SetClassicLeaves(BlockId.OakLeaves,AtlasLayout.OakLeaves);
            SetLog(BlockId.BirchLog,AtlasLayout.BirchLog,AtlasLayout.BirchLogTop); SetClassicLeaves(BlockId.BirchLeaves,AtlasLayout.BirchLeaves);
            SetLog(BlockId.SpruceLog,AtlasLayout.SpruceLog,AtlasLayout.SpruceLogTop); SetClassicLeaves(BlockId.SpruceLeaves,AtlasLayout.SpruceLeaves);
            SetLog(BlockId.JungleLog,AtlasLayout.JungleLog,AtlasLayout.JungleLogTop); SetClassicLeaves(BlockId.JungleLeaves,AtlasLayout.JungleLeaves);
            SetLog(BlockId.AcaciaLog,AtlasLayout.AcaciaLog,AtlasLayout.AcaciaLogTop); SetClassicLeaves(BlockId.AcaciaLeaves,AtlasLayout.AcaciaLeaves);
            SetLog(BlockId.DarkOakLog,AtlasLayout.DarkOakLog,AtlasLayout.DarkOakLogTop); SetClassicLeaves(BlockId.DarkOakLeaves,AtlasLayout.DarkOakLeaves);
            Set(BlockId.CherryWood,O(AtlasLayout.CherryPlanks));
            // Natural cherry in C2() uses LOG plus these glassy leaf blocks.
            Set(BlockId.CherryLeaves,GlassyLeaves(AtlasLayout.FloweringAzaleaLeaves)); // legacy serialized alias retained
            Set(BlockId.AzaleaLeaves,GlassyLeaves(AtlasLayout.AzaleaLeaves));
            Set(BlockId.FloweringAzaleaLeaves,GlassyLeaves(AtlasLayout.FloweringAzaleaLeaves));
            Set(BlockId.Amethyst,O(AtlasLayout.AmethystBlock)); Set(BlockId.BuddingAmethyst,O(AtlasLayout.BuddingAmethyst)); Set(BlockId.Dripstone,O(AtlasLayout.DripstoneBlock));
            Set(BlockId.MushroomStem,O(AtlasLayout.MushroomStem)); Set(BlockId.BrownMushroomBlock,O(AtlasLayout.BrownMushroomBlock)); Set(BlockId.RedMushroomBlock,O(AtlasLayout.RedMushroomBlock));
            Set(BlockId.Pumpkin,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.PumpkinSide,AtlasLayout.PumpkinTop,AtlasLayout.PumpkinTop));
            Set(BlockId.Melon,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.MelonSide,AtlasLayout.MelonTop,AtlasLayout.MelonTop));

            SetCross(BlockId.TallGrass,AtlasLayout.ShortGrass); SetCross(BlockId.Fern,AtlasLayout.Fern); SetCross(BlockId.Dandelion,AtlasLayout.Dandelion); SetCross(BlockId.Poppy,AtlasLayout.Poppy); SetCross(BlockId.DeadBush,AtlasLayout.DeadBush);
            Set(BlockId.BambooPlant,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.BambooStalk,AtlasLayout.BambooStalk,AtlasLayout.BambooStalk,0,BlockShape.Bamboo));
            SetCross(BlockId.SugarCane,AtlasLayout.SugarCane); SetCross(BlockId.SweetBerryBush,AtlasLayout.SweetBerryBush);
            SetCross(BlockId.Allium,AtlasLayout.Allium); SetCross(BlockId.Cornflower,AtlasLayout.Cornflower); SetCross(BlockId.OxeyeDaisy,AtlasLayout.OxeyeDaisy); SetCross(BlockId.AzureBluet,AtlasLayout.AzureBluet); SetCross(BlockId.LilyOfTheValley,AtlasLayout.LilyOfTheValley);
            SetCross(BlockId.PinkTulip,AtlasLayout.PinkTulip); SetCross(BlockId.OrangeTulip,AtlasLayout.OrangeTulip); SetCross(BlockId.RedTulip,AtlasLayout.RedTulip); SetCross(BlockId.WhiteTulip,AtlasLayout.WhiteTulip); SetCross(BlockId.BlueOrchid,AtlasLayout.BlueOrchid); SetCross(BlockId.BrownMushroom,AtlasLayout.BrownMushroom); SetCross(BlockId.RedMushroom,AtlasLayout.RedMushroom);
            SetTall(BlockId.Sunflower,AtlasLayout.SunflowerBottom,AtlasLayout.SunflowerTop); SetTall(BlockId.Lilac,AtlasLayout.LilacBottom,AtlasLayout.LilacTop); SetTall(BlockId.RoseBush,AtlasLayout.RoseBushBottom,AtlasLayout.RoseBushTop); SetTall(BlockId.Peony,AtlasLayout.PeonyBottom,AtlasLayout.PeonyTop); SetTall(BlockId.LargeFern,AtlasLayout.LargeFernBottom,AtlasLayout.LargeFernTop);
            Set(BlockId.MossCarpet,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.MossBlock,AtlasLayout.MossBlock,AtlasLayout.MossBlock,0,BlockShape.Carpet));
            SetCross(BlockId.CaveVines,AtlasLayout.CaveVines);

            SetAquaticCross(BlockId.Kelp,AtlasLayout.Kelp); SetAquaticCross(BlockId.Seagrass,AtlasLayout.Seagrass);
            SetAquaticCross(BlockId.TubeCoralFan,AtlasLayout.TubeCoralFan); SetAquaticCross(BlockId.BrainCoralFan,AtlasLayout.BrainCoralFan); SetAquaticCross(BlockId.BubbleCoralFan,AtlasLayout.BubbleCoralFan); SetAquaticCross(BlockId.FireCoralFan,AtlasLayout.FireCoralFan); SetAquaticCross(BlockId.HornCoralFan,AtlasLayout.HornCoralFan);
            Set(BlockId.TubeCoralBlock,O(AtlasLayout.TubeCoralBlock)); Set(BlockId.BrainCoralBlock,O(AtlasLayout.BrainCoralBlock)); Set(BlockId.BubbleCoralBlock,O(AtlasLayout.BubbleCoralBlock)); Set(BlockId.FireCoralBlock,O(AtlasLayout.FireCoralBlock)); Set(BlockId.HornCoralBlock,O(AtlasLayout.HornCoralBlock));
            Set(BlockId.SeaPickle,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.SeaPickle,AtlasLayout.SeaPickle,AtlasLayout.SeaPickle,0,BlockShape.SeaPickle));
            Set(BlockId.LilyPad,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.LilyPad,AtlasLayout.LilyPad,AtlasLayout.LilyPad,0,BlockShape.LilyPad));

            // First source-faithful special-shape rendering wave from meshWorker.SE().
            Set(BlockId.OakPlanks,O(AtlasLayout.OakPlanks));
            Set(BlockId.SprucePlanks,O(AtlasLayout.SprucePlanks));
            Set(BlockId.Cobblestone,O(AtlasLayout.Cobblestone));
            Set(BlockId.Glass,new BlockDef(RenderLayer.Transparent,true,false,AtlasLayout.Glass,AtlasLayout.Glass,AtlasLayout.Glass));

            SetSpecial(BlockId.OakSlab,AtlasLayout.OakPlanks,BlockShape.Slab,true);
            SetSpecial(BlockId.SpruceSlab,AtlasLayout.SprucePlanks,BlockShape.Slab,true);
            SetSpecial(BlockId.CobblestoneSlab,AtlasLayout.Cobblestone,BlockShape.Slab,true);
            SetSpecial(BlockId.OakStairs,AtlasLayout.OakPlanks,BlockShape.Stairs,true);
            SetSpecial(BlockId.SpruceStairs,AtlasLayout.SprucePlanks,BlockShape.Stairs,true);
            SetSpecial(BlockId.CobblestoneStairs,AtlasLayout.Cobblestone,BlockShape.Stairs,true);
            SetSpecial(BlockId.OakFence,AtlasLayout.OakPlanks,BlockShape.Fence,true);
            SetSpecial(BlockId.SpruceFence,AtlasLayout.SprucePlanks,BlockShape.Fence,true);
            SetSpecial(BlockId.OakFenceGate,AtlasLayout.OakPlanks,BlockShape.Gate,true);
            SetSpecial(BlockId.CobblestoneWall,AtlasLayout.Cobblestone,BlockShape.Wall,true);
            Set(BlockId.GlassPane,new BlockDef(RenderLayer.Transparent,true,false,AtlasLayout.Glass,AtlasLayout.Glass,AtlasLayout.Glass,0,BlockShape.Pane));
            SetSpecial(BlockId.OakPressurePlate,AtlasLayout.OakPlanks,BlockShape.Plate,true);
            SetSpecial(BlockId.OakButton,AtlasLayout.OakPlanks,BlockShape.Button,true);
            Set(BlockId.OakTrapdoor,new BlockDef(RenderLayer.Cutout,true,false,AtlasLayout.OakTrapdoor,AtlasLayout.OakTrapdoor,AtlasLayout.OakTrapdoor,0,BlockShape.Trapdoor));
            Set(BlockId.Ladder,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.Ladder,AtlasLayout.Ladder,AtlasLayout.Ladder,0,BlockShape.Ladder));
            Set(BlockId.Torch,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.Torch,AtlasLayout.Torch,AtlasLayout.Torch,14,BlockShape.Torch));
            Set(BlockId.Rail,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.Rail,AtlasLayout.Rail,AtlasLayout.Rail,0,BlockShape.Rail));
            Set(BlockId.Vine,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.Vine,AtlasLayout.Vine,AtlasLayout.Vine,0,BlockShape.FlatFaces));
            Set(BlockId.GlowLichen,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.GlowLichen,AtlasLayout.GlowLichen,AtlasLayout.GlowLichen,0,BlockShape.FlatFaces));
            Set(BlockId.FlowerPot,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.FlowerPot,AtlasLayout.FlowerPot,AtlasLayout.FlowerPot,0,BlockShape.Pot));
            Set(BlockId.IronBars,new BlockDef(RenderLayer.Cutout,true,false,AtlasLayout.IronBars,AtlasLayout.IronBars,AtlasLayout.IronBars,0,BlockShape.Pane));
            Set(BlockId.ShipWheel,new BlockDef(RenderLayer.Opaque,true,false,AtlasLayout.SprucePlanks,AtlasLayout.SprucePlanks,AtlasLayout.SprucePlanks,0,BlockShape.ShipWheel));

            // R4: source functional blocks used by meshWorker late/special render paths.
            Set(BlockId.BirchDoor,new BlockDef(RenderLayer.Cutout,true,false,AtlasLayout.BirchDoorBottom,AtlasLayout.BirchDoorTop,AtlasLayout.BirchDoorBottom,0,BlockShape.Door));
            Set(BlockId.OakDoor,new BlockDef(RenderLayer.Cutout,true,false,AtlasLayout.OakDoorBottom,AtlasLayout.OakDoorTop,AtlasLayout.OakDoorBottom,0,BlockShape.Door));
            Set(BlockId.Chest,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.ChestSide,AtlasLayout.ChestTop,AtlasLayout.ChestTop,0,BlockShape.Chest));
            Set(BlockId.BedRedFoot,new BlockDef(RenderLayer.Opaque,true,false,AtlasLayout.BedSide,AtlasLayout.BedFootTop,AtlasLayout.BedSide,0,BlockShape.Bed));
            Set(BlockId.BedRedHead,new BlockDef(RenderLayer.Opaque,true,false,AtlasLayout.BedSide,AtlasLayout.BedHeadTop,AtlasLayout.BedSide,0,BlockShape.Bed));
            Set(BlockId.CraftingTable,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.CraftingTableFront,AtlasLayout.CraftingTableTop,AtlasLayout.OakPlanks));
            Set(BlockId.Furnace,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.FurnaceFront,AtlasLayout.FurnaceTop,AtlasLayout.FurnaceTop));
            Set(BlockId.FurnaceLit,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.FurnaceFrontOn,AtlasLayout.FurnaceTop,AtlasLayout.FurnaceTop,13));
            Set(BlockId.Farmland,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.Dirt,AtlasLayout.Farmland,AtlasLayout.Dirt));
            Set(BlockId.FarmlandMoist,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.Dirt,AtlasLayout.FarmlandMoist,AtlasLayout.Dirt));
            SetCross(BlockId.Wheat0,AtlasLayout.Wheat0); SetCross(BlockId.Wheat1,AtlasLayout.Wheat1); SetCross(BlockId.Wheat2,AtlasLayout.Wheat2); SetCross(BlockId.Wheat3,AtlasLayout.Wheat3);
            Set(BlockId.Fire,new BlockDef(RenderLayer.Cutout,false,false,AtlasLayout.Fire,AtlasLayout.Fire,AtlasLayout.Fire,15,BlockShape.Cross));
            SetCross(BlockId.Cobweb,AtlasLayout.Cobweb);
            Set(BlockId.Tnt,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.TntSide,AtlasLayout.TntTop,AtlasLayout.TntBottom));

            // R36: blocks used by source desert structures, pillager outposts and ruined portals.
            Set(BlockId.CutSandstone,O(AtlasLayout.CutSandstone));
            Set(BlockId.ChiseledSandstone,O(AtlasLayout.ChiseledSandstone));
            Set(BlockId.BlueTerracotta,O(AtlasLayout.BlueTerracotta));
            SetSpecial(BlockId.SandstoneSlab,AtlasLayout.Sandstone,BlockShape.Slab,true);
            SetSpecial(BlockId.SandstoneStairs,AtlasLayout.Sandstone,BlockShape.Stairs,true);
            SetSpecial(BlockId.StonePressurePlate,AtlasLayout.Stone,BlockShape.Plate,true);
            Set(BlockId.BirchPlanks,O(AtlasLayout.BirchPlanks));
            Set(BlockId.DarkOakPlanks,O(AtlasLayout.DarkOakPlanks));
            SetSpecial(BlockId.DarkOakSlab,AtlasLayout.DarkOakPlanks,BlockShape.Slab,true);
            SetSpecial(BlockId.DarkOakStairs,AtlasLayout.DarkOakPlanks,BlockShape.Stairs,true);
            SetSpecial(BlockId.DarkOakFence,AtlasLayout.DarkOakPlanks,BlockShape.Fence,true);
            Set(BlockId.Netherrack,O(AtlasLayout.Netherrack));
            Set(BlockId.MagmaBlock,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.Magma,AtlasLayout.Magma,AtlasLayout.Magma,3));
            Set(BlockId.Obsidian,O(AtlasLayout.Obsidian));
            Set(BlockId.CryingObsidian,new BlockDef(RenderLayer.Opaque,true,true,AtlasLayout.CryingObsidian,AtlasLayout.CryingObsidian,AtlasLayout.CryingObsidian,10));
            Set(BlockId.GoldBlock,O(AtlasLayout.GoldBlock));

            // R60 leaf-decay item blocks. main q6() drops these block IDs directly.
            SetCross(BlockId.OakSapling,AtlasLayout.OakSapling);
            SetCross(BlockId.BirchSapling,AtlasLayout.BirchSapling);
            SetCross(BlockId.SpruceSapling,AtlasLayout.SpruceSapling);
            SetCross(BlockId.JungleSapling,AtlasLayout.JungleSapling);
            SetCross(BlockId.AcaciaSapling,AtlasLayout.AcaciaSapling);
            SetCross(BlockId.DarkOakSapling,AtlasLayout.DarkOakSapling);
            FillFromSource();
            ApplySourceFaces();
            BuildTables();
        }

        // Face textures and the LE[] glow flag of every block come from the reference tables (main.js M,
        // meshWorker q4()/LE), including the hand-tuned definitions: tile-exact parity is verified by the
        // tools/parity synthetic all-blocks mesher test.
        static void ApplySourceFaces()
        {
            for(int i=1;i<SourceBlockData.Count;i++)
            {
                if(D[i].Layer==RenderLayer.None)continue;
                D[i]=D[i].WithSourceFaces(AtlasLayout.Tile(SourceBlockData.TileSide[i]),AtlasLayout.Tile(SourceBlockData.TileTop[i]),
                    AtlasLayout.Tile(SourceBlockData.TileBottom[i]),(SourceBlockData.Flags[i]&SourceBlockData.FEmissiveBoost)!=0);
            }
        }

        // Every block the hand-tuned definitions above do not cover (the 493 reference blocks appended to
        // BlockId by tools/codegen) is built from SourceBlockData, i.e. from the reference tables.
        static void FillFromSource()
        {
            for(int i=1;i<SourceBlockData.Count;i++)
            {
                if(D[i].Layer!=RenderLayer.None)continue;
                D[i]=SourceDef(i);
            }
        }

        static BlockDef SourceDef(int i)
        {
            uint f=SourceBlockData.Flags[i];
            var shape=(BlockShape)SourceBlockData.Shape[i];
            bool water=(f&SourceBlockData.FWater)!=0,lava=(f&SourceBlockData.FLava)!=0;
            RenderLayer layer=water?RenderLayer.Water:
                (f&SourceBlockData.FTransparentLayer)!=0?RenderLayer.Transparent:
                lava||(shape==BlockShape.Cube&&(f&(SourceBlockData.FClassicLeaf|SourceBlockData.FGlass))==0)?RenderLayer.Opaque:
                RenderLayer.Cutout;
            bool solid=!water&&!lava&&shape!=BlockShape.Cross&&shape!=BlockShape.TallPlant&&shape!=BlockShape.Bamboo&&
                       shape!=BlockShape.SeaPickle&&shape!=BlockShape.LilyPad&&shape!=BlockShape.Torch&&shape!=BlockShape.Rail&&
                       shape!=BlockShape.FlatFaces&&shape!=BlockShape.Ladder&&shape!=BlockShape.Pot&&shape!=BlockShape.Carpet;
            AtlasRect side=AtlasLayout.Tile(SourceBlockData.TileSide[i]);
            AtlasRect top=AtlasLayout.Tile(SourceBlockData.TileTop[i]);
            AtlasRect bottom=AtlasLayout.Tile(SourceBlockData.TileBottom[i]);
            return new BlockDef(layer,solid,(f&SourceBlockData.FFullOpaque)!=0,side,top,bottom,SourceBlockData.Light[i],shape);
        }

        static readonly System.Text.RegularExpressions.Regex FireFuelKey=new System.Text.RegularExpressions.Regex("(^|_)LOG$|PLANKS$|LEAVES$|^WOOL_|^BOOKSHELF$|^CRAFTING_TABLE$|FENCE|SAPLING$");
        static readonly bool[] TFireFuel=new bool[Capacity],TSolidRender=new bool[Capacity];
        /// <summary>meshWorker WA(): blocks next to which fire draws a leaning plane (wood, leaves, wool, cross plants).</summary>
        public static bool FeedsFire(BlockId id)=>TFireFuel[(int)id];
        /// <summary>meshWorker TC(): a non-fluid, non-cross, plain (unshaped) block.</summary>
        public static bool IsSolidRender(BlockId id)=>TSolidRender[(int)id];

        static readonly bool[] TConnectPlain=new bool[Capacity];
        /// <summary>
        /// meshWorker B2()/a2()/i2() common part: fences, walls and panes attach to any block that is not
        /// air, water, a cross plant or a special-shaped block (h[] entry). Lava, glass, leaves, chests count.
        /// </summary>
        public static bool ConnectsPlain(BlockId id)=>TConnectPlain[(int)id];
        static readonly bool[] TXrayOre=new bool[Capacity];
        /// <summary>meshWorker V1[]==2 (ores): every face is emitted for the X-Ray pass.</summary>
        public static bool IsXrayOre(BlockId id)=>TXrayOre[(int)id];

        static System.Collections.Generic.Dictionary<string,BlockId> byKey;
        /// <summary>BlockId of a reference key (main.js o[KEY]), or Air.</summary>
        public static BlockId FromKey(string key)
        {
            if(byKey==null)
            {
                var d=new System.Collections.Generic.Dictionary<string,BlockId>(SourceBlockData.Count);
                for(int i=0;i<SourceBlockData.Count;i++)
                {
                    var bid=(BlockId)SourceBlockData.FromSource[SourceBlockData.SourceId[i]];
                    d[SourceBlockData.Key[i]]=bid;
                }
                byKey=d;
            }
            return byKey.TryGetValue(key,out var id)?id:BlockId.Air;
        }
        public static string Key(BlockId id)=>(uint)id<(uint)SourceBlockData.Count?SourceBlockData.Key[(int)id]:"AIR";

        static bool IsSourceBlock(BlockId id)=>(int)id>(int)BlockId.LavaFlow1&&(int)id<SourceBlockData.Count;
        static readonly System.Text.RegularExpressions.Regex LeafSupportKey=new System.Text.RegularExpressions.Regex("(^|_)LOG$|_WOOD$|^MUSHROOM_STEM$"); // main.js PT

        static void BuildTables()
        {
            for(int i=0;i<Capacity;i++)
            {
                var id=(BlockId)i;
                if(IsSourceBlock(id))
                {
                    uint f=SourceBlockData.Flags[i];string key=SourceBlockData.Key[i];
                    TWater[i]=(f&SourceBlockData.FWater)!=0;TLava[i]=(f&SourceBlockData.FLava)!=0;
                    TAquatic[i]=(f&SourceBlockData.FAquatic)!=0;TClassicLeaf[i]=(f&SourceBlockData.FClassicLeaf)!=0;
                    // Port "glassy" means the non-occluding leaf family (azalea/cherry), not stained glass.
                    TGlassy[i]=(f&SourceBlockData.FGlass)!=0&&key.Contains("LEAVES");
                    TXray[i]=(f&SourceBlockData.FXrayClass)!=0;
                    TNeedsWater[i]=(f&SourceBlockData.FNeedsWater)!=0;
                    TAxis[i]=(f&SourceBlockData.FAxis)!=0;
                    TLeafSupport[i]=LeafSupportKey.IsMatch(key);
                    continue;
                }
                TWater[i]=ComputeIsWater(id);TLava[i]=ComputeIsLava(id);TAquatic[i]=ComputeIsAquatic(id);
                TClassicLeaf[i]=ComputeIsClassicLeaf(id);TGlassy[i]=ComputeIsGlassy(id);
                // meshWorker V1[]: only LAVA (not its flow levels), RAIL, COBWEB, CHEST, PLANKS, OAK_FENCE and ores.
                TXray[i]=i<SourceBlockData.Count&&(SourceBlockData.Flags[i]&SourceBlockData.FXrayClass)!=0;
                TNeedsWater[i]=ComputeNeedsWater(id);TAxis[i]=ComputeIsAxisBlock(id);TLeafSupport[i]=ComputeIsLeafDecaySupport(id);
            }
            for(int i=1;i<SourceBlockData.Count;i++)
            {
                uint f=SourceBlockData.Flags[i];
                TSolidRender[i]=(f&SourceBlockData.FSolidRender)!=0;
                TConnectPlain[i]=(f&(SourceBlockData.FWater|SourceBlockData.FCross|SourceBlockData.FShaped))==0;
                TXrayOre[i]=(f&SourceBlockData.FXrayOre)!=0;
                TFireFuel[i]=i!=(int)BlockId.Fire&&(FireFuelKey.IsMatch(SourceBlockData.Key[i])||(f&SourceBlockData.FCross)!=0);
            }
            for(int i=0;i<Capacity;i++)
            {
                var id=(BlockId)i;
                // meshWorker FA() for every id (the old hand predicate treated CHEST as non-occluding).
                TFullOpaque[i]=i<SourceBlockData.Count?(SourceBlockData.Flags[i]&SourceBlockData.FFullOpaque)!=0:ComputeIsFullOpaque(id);
                TLightCost[i]=(byte)((TWater[i]||TAquatic[i]||TClassicLeaf[i])?2:1);
            }
        }

        static BlockDef O(AtlasRect r)=>new BlockDef(RenderLayer.Opaque,true,true,r,r,r);
        static BlockDef Fluid(AtlasRect r)=>new BlockDef(RenderLayer.Water,false,false,r,r,r);
        static BlockDef Lava(AtlasRect r,byte emission)=>new BlockDef(RenderLayer.Opaque,false,false,r,r,r,emission);
        static BlockDef GlassyLeaves(AtlasRect r)=>new BlockDef(RenderLayer.Cutout,true,false,r,r,r);
        static void Set(BlockId id,BlockDef d)=>D[(int)id]=d;
        static void SetOre(BlockId id,AtlasRect tex)=>Set(id,O(tex));
        static void SetLog(BlockId id,AtlasRect side,AtlasRect top)=>Set(id,new BlockDef(RenderLayer.Opaque,true,true,side,top,top));
        static void SetClassicLeaves(BlockId id,AtlasRect tex)=>Set(id,new BlockDef(RenderLayer.Cutout,true,false,tex,tex,tex));
        static void SetCross(BlockId id,AtlasRect tex)=>Set(id,new BlockDef(RenderLayer.Cutout,false,false,tex,tex,tex,0,BlockShape.Cross));
        static void SetAquaticCross(BlockId id,AtlasRect tex)=>SetCross(id,tex);
        static void SetTall(BlockId id,AtlasRect bottom,AtlasRect top)=>Set(id,new BlockDef(RenderLayer.Cutout,false,false,bottom,top,bottom,0,BlockShape.TallPlant));
        static void SetSpecial(BlockId id,AtlasRect tex,BlockShape shape,bool solid)=>Set(id,new BlockDef(RenderLayer.Opaque,solid,false,tex,tex,tex,0,shape));

        public static BlockDef Get(BlockId id)=>D[(int)id];
        public static bool IsSolid(BlockId id)=>D[(int)id].Solid;
        public static bool IsWater(BlockId id)=>TWater[(int)id];
        static bool ComputeIsWater(BlockId id)=>id==BlockId.Water||id==BlockId.Flow3||id==BlockId.Flow2||id==BlockId.Flow1;
        public static int WaterLevel(BlockId id)=>id==BlockId.Water?4:id==BlockId.Flow3?3:id==BlockId.Flow2?2:id==BlockId.Flow1?1:0;
        public static BlockId WaterForLevel(int level)=>level>=4?BlockId.Water:level>=3?BlockId.Flow3:level==2?BlockId.Flow2:level==1?BlockId.Flow1:BlockId.Air;
        public static float WaterSurfaceHeight(BlockId id)=>id==BlockId.Water?.875f:id==BlockId.Flow3?.65f:id==BlockId.Flow2?.45f:id==BlockId.Flow1?.25f:0f;
        public static bool IsLava(BlockId id)=>TLava[(int)id];
        static bool ComputeIsLava(BlockId id)=>id==BlockId.Lava||id==BlockId.LavaFlow2||id==BlockId.LavaFlow1;
        public static int LavaLevel(BlockId id)=>id==BlockId.Lava?3:id==BlockId.LavaFlow2?2:id==BlockId.LavaFlow1?1:0;
        public static BlockId LavaForLevel(int level)=>level>=3?BlockId.Lava:level==2?BlockId.LavaFlow2:level==1?BlockId.LavaFlow1:BlockId.Air;
        public static float LavaSurfaceHeight(BlockId id)=>id==BlockId.Lava?.875f:id==BlockId.LavaFlow2?.55f:id==BlockId.LavaFlow1?.30f:0f;
        public static bool IsFluid(BlockId id)=>TWater[(int)id]||TLava[(int)id];
        public static bool IsClassicLeaf(BlockId id)=>TClassicLeaf[(int)id];
        static bool ComputeIsClassicLeaf(BlockId id)=>id==BlockId.OakLeaves||id==BlockId.BirchLeaves||id==BlockId.SpruceLeaves||id==BlockId.DarkOakLeaves||id==BlockId.JungleLeaves||id==BlockId.AcaciaLeaves;
        public static bool IsGlassy(BlockId id)=>TGlassy[(int)id];
        static bool ComputeIsGlassy(BlockId id)=>id==BlockId.CherryLeaves||id==BlockId.AzaleaLeaves||id==BlockId.FloweringAzaleaLeaves;
        // main BT/PT tables are derived from source enum names /LEAVES/ and /(^|_)LOG$|_WOOD$|^MUSHROOM_STEM$/.
        public static bool IsLeaf(BlockId id)=>IsClassicLeaf(id)||IsGlassy(id);
        public static bool IsLeafDecaySupport(BlockId id)=>TLeafSupport[(int)id];
        static bool ComputeIsLeafDecaySupport(BlockId id)=>id==BlockId.OakLog||id==BlockId.BirchLog||id==BlockId.SpruceLog||id==BlockId.JungleLog||id==BlockId.AcaciaLog||id==BlockId.DarkOakLog||id==BlockId.CherryWood||id==BlockId.MushroomStem;
        public static bool IsAquatic(BlockId id)=>TAquatic[(int)id];
        static bool ComputeIsAquatic(BlockId id)=>id==BlockId.Kelp||id==BlockId.Seagrass||id==BlockId.TubeCoralFan||id==BlockId.BrainCoralFan||id==BlockId.BubbleCoralFan||id==BlockId.FireCoralFan||id==BlockId.HornCoralFan||id==BlockId.SeaPickle;
        public static bool NeedsWater(BlockId id)=>TNeedsWater[(int)id];
        static bool ComputeNeedsWater(BlockId id)=>id==BlockId.Kelp||id==BlockId.Seagrass;
        // meshWorker b1: pillar/log UV axis comes from meta&3 (0=Y, 1=X, 2=Z).
        public static bool IsAxisBlock(BlockId id)=>TAxis[(int)id];
        static bool ComputeIsAxisBlock(BlockId id)=>id==BlockId.OakLog||id==BlockId.BirchLog||id==BlockId.SpruceLog||id==BlockId.JungleLog||id==BlockId.AcaciaLog||id==BlockId.DarkOakLog||id==BlockId.MushroomStem;
        // Shape families are defined by the block's shape (meshWorker h[]), so every material variant counts.
        public static bool IsStairs(BlockId id)=>D[(int)id].Shape==BlockShape.Stairs;
        public static bool IsFence(BlockId id)=>D[(int)id].Shape==BlockShape.Fence;
        public static bool IsGate(BlockId id)=>D[(int)id].Shape==BlockShape.Gate;
        public static bool IsWall(BlockId id)=>D[(int)id].Shape==BlockShape.Wall;
        public static bool IsPane(BlockId id)=>D[(int)id].Shape==BlockShape.Pane;
        // main.js meshWorker V1[] class marker used by the X-Ray two-pass renderer.
        // This is the exact subset currently represented by BlockId in the Unity port.
        public static bool XrayClassified(BlockId id)=>TXray[(int)id];
        static bool ComputeXrayClassified(BlockId id)
        {
            switch(id)
            {
                case BlockId.CoalOre: case BlockId.CopperOre: case BlockId.IronOre: case BlockId.GoldOre:
                case BlockId.DiamondOre: case BlockId.LapisOre: case BlockId.RedstoneOre: case BlockId.EmeraldOre:
                case BlockId.DeepslateCoalOre: case BlockId.DeepslateCopperOre: case BlockId.DeepslateIronOre:
                case BlockId.DeepslateGoldOre: case BlockId.DeepslateDiamondOre: case BlockId.DeepslateLapisOre:
                case BlockId.DeepslateRedstoneOre: case BlockId.DeepslateEmeraldOre:
                case BlockId.Lava: case BlockId.LavaFlow2: case BlockId.LavaFlow1: case BlockId.Rail: case BlockId.Cobweb: case BlockId.Chest:
                case BlockId.OakPlanks: case BlockId.OakFence:
                    return true;
                default:return false;
            }
        }
        public static bool IsSpecial(BlockId id){var s=D[(int)id].Shape;return s!=BlockShape.Cube&&s!=BlockShape.Cross;}
        // main.js bA()/Ae(): cross vegetation is displaced by fluids, while only ordinary cube blocks
        // count as solid fluid support. This is intentionally different from render occlusion: glass,
        // leaves and cactus may support fluid even though they are not full-face light/mesh occluders.
        public static bool IsSourceFluidReplaceableCross(BlockId id)
        {
            BlockShape s=D[(int)id].Shape;
            return s==BlockShape.Cross||s==BlockShape.TallPlant;
        }
        public static bool IsSourceFluidSupport(BlockId id)
        {
            return id!=BlockId.Air&&!IsWater(id)&&!IsLava(id)&&!IsSourceFluidReplaceableCross(id)&&D[(int)id].Shape==BlockShape.Cube;
        }
        // meshWorker FA(): full-face occluder. Glass, classic leaves, cactus, fluids, cross/special and glassy blocks are excluded.
        public static bool IsFullOpaque(BlockId id)=>TFullOpaque[(int)id];
        static bool ComputeIsFullOpaque(BlockId id)=>id!=BlockId.Air&&!TGlassy[(int)id]&&!TClassicLeaf[(int)id]&&id!=BlockId.Cactus&&!TWater[(int)id]&&!TLava[(int)id]&&D[(int)id].Shape==BlockShape.Cube&&D[(int)id].Layer!=RenderLayer.Transparent;
        public static bool Occludes(BlockId id)=>TFullOpaque[(int)id];
        public static bool OccludesAO(BlockId id)=>TFullOpaque[(int)id];
        public static bool LightPasses(BlockId id)=>!TFullOpaque[(int)id];
        public static int LightCost(BlockId id)=>TLightCost[(int)id];
        public static byte Emission(BlockId id,byte meta=0)
        {
            if(id==BlockId.Lava||id==BlockId.Torch)return 14;
            if(id==BlockId.LavaFlow2)return 13;
            if(id==BlockId.LavaFlow1)return 12;
            if(id==BlockId.CaveVines&&(meta&1)!=0)return 14;
            if(id==BlockId.SeaPickle)return 0; // main di()/Hw(): contextual; VoxelLighting checks meta&4 or surrounding water.
            return D[(int)id].Emission;
        }
        public static bool FaceVisible(BlockId self,BlockId neighbor)
        {
            // meshWorker bC().
            int n=(int)neighbor;
            if(TWater[(int)self])return neighbor==BlockId.Air||(!TFullOpaque[n]&&!TWater[n]&&!TAquatic[n]);
            if(TFullOpaque[(int)self])return !TFullOpaque[n];
            return !TFullOpaque[n]&&neighbor!=self;
        }
    }
}
