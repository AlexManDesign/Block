using System;
using System.Collections.Generic;

namespace BlockcraftPort
{
    public enum MobItemId
    {
        None=0,
        Carrot,Wheat,WheatSeeds,Shears,
        Porkchop,Beef,Leather,Mutton,Chicken,Feather,RottenFlesh,Potato,
        Bone,Arrow,Gunpowder,String,SpiderEye,EnderPearl,SlimeBall,
        RawSalmon,CookedSalmon,RawCod,CookedCod,Egg,
        Stick,ClayBall,DiamondGem,GoldIngot,EmeraldGem,Boat,Minecart,
        WoolWhite,WoolBlack,WoolGray,WoolLightGray,WoolBrown,WoolPink,
        // R48 append-only item namespace used by exact main r2() block drops.
        Flint,Coal,RawCopper,MelonSlice,SweetBerries,PumpkinSeeds,MelonSeeds,Apple
    }

    public readonly struct MobItemDrop
    {
        public readonly MobItemId Id;
        public readonly int Count;
        public MobItemDrop(MobItemId id,int count){Id=id;Count=count;}
    }

    /// <summary>
    /// Mob-facing source items. Logical identity remains separate from BlockId; R48 routes pickups
    /// into MainSurvivalInventory while retaining counters for validation/telemetry.
    /// </summary>
    public static class MobItemCatalog
    {
        static readonly Dictionary<MobItemId,int> pendingPickups=new Dictionary<MobItemId,int>();

        public static string TextureKey(MobItemId id)
        {
            switch(id)
            {
                case MobItemId.Carrot:return "item_carrot";
                case MobItemId.Wheat:return "item_wheat";
                case MobItemId.WheatSeeds:return "item_wheat_seeds";
                case MobItemId.Shears:return "item_shears";
                case MobItemId.Porkchop:return "item_porkchop";
                case MobItemId.Beef:return "item_beef";
                case MobItemId.Leather:return "item_leather";
                case MobItemId.Mutton:return "item_mutton";
                case MobItemId.Chicken:return "item_chicken";
                case MobItemId.Feather:return "item_feather";
                case MobItemId.RottenFlesh:return "item_rotten_flesh";
                case MobItemId.Potato:return "item_potato";
                case MobItemId.Bone:return "item_bone";
                case MobItemId.Arrow:return "item_arrow";
                case MobItemId.Gunpowder:return "item_gunpowder";
                case MobItemId.String:return "item_string";
                case MobItemId.SpiderEye:return "item_spider_eye";
                case MobItemId.RawSalmon:return "item_raw_salmon";
                case MobItemId.CookedSalmon:return "item_cooked_salmon";
                case MobItemId.RawCod:return "item_raw_cod";
                case MobItemId.CookedCod:return "item_cooked_cod";
                case MobItemId.Stick:return "item_stick";
                case MobItemId.ClayBall:return "item_clay_ball";
                case MobItemId.DiamondGem:return "item_diamond";
                case MobItemId.GoldIngot:return "item_gold_ingot";
                case MobItemId.EmeraldGem:return "item_emerald";
                case MobItemId.Flint:return "item_flint";
                case MobItemId.Coal:return "item_coal";
                case MobItemId.RawCopper:return "item_raw_copper";
                case MobItemId.MelonSlice:return "item_melon_slice";
                case MobItemId.SweetBerries:return "item_sweet_berries";
                case MobItemId.PumpkinSeeds:return "item_pumpkin_seeds";
                case MobItemId.MelonSeeds:return "item_melon_seeds";
                case MobItemId.Apple:return "item_apple";
                case MobItemId.Boat:return "item_boat";
                case MobItemId.Minecart:return "item_minecart";
                case MobItemId.EnderPearl:return "item_ender_pearl";
                // The supplied source texture bundle does not contain these remaining item sprites. Preserve the
                // logical item instead of substituting an unrelated texture.
                case MobItemId.SlimeBall:
                case MobItemId.Egg:
                case MobItemId.WoolWhite:
                case MobItemId.WoolBlack:
                case MobItemId.WoolGray:
                case MobItemId.WoolLightGray:
                case MobItemId.WoolBrown:
                case MobItemId.WoolPink:
                default:return null;
            }
        }

        public static MobItemId FromTextureKey(string key)
        {
            if(string.IsNullOrEmpty(key))return MobItemId.None;
            switch(key)
            {
                case "item_carrot":return MobItemId.Carrot;
                case "item_wheat":return MobItemId.Wheat;
                case "item_wheat_seeds":return MobItemId.WheatSeeds;
                case "item_shears":return MobItemId.Shears;
                case "item_boat":return MobItemId.Boat;
                case "item_minecart":return MobItemId.Minecart;
                case "item_ender_pearl":return MobItemId.EnderPearl;
                case "item_arrow":return MobItemId.Arrow;
                case "item_bone":return MobItemId.Bone;
                case "item_string":return MobItemId.String;
                case "item_clay_ball":return MobItemId.ClayBall;
                case "item_diamond":return MobItemId.DiamondGem;
                case "item_gold_ingot":return MobItemId.GoldIngot;
                case "item_emerald":return MobItemId.EmeraldGem;
                case "item_flint":return MobItemId.Flint;
                case "item_coal":return MobItemId.Coal;
                case "item_raw_copper":return MobItemId.RawCopper;
                case "item_melon_slice":return MobItemId.MelonSlice;
                case "item_sweet_berries":return MobItemId.SweetBerries;
                case "item_pumpkin_seeds":return MobItemId.PumpkinSeeds;
                case "item_melon_seeds":return MobItemId.MelonSeeds;
                case "item_apple":return MobItemId.Apple;
                default:return MobItemId.None;
            }
        }

        public static MobItemId FeedFor(MobKind kind)
        {
            switch(kind)
            {
                case MobKind.Pig:return MobItemId.Carrot;
                case MobKind.Cow:
                case MobKind.Sheep:return MobItemId.Wheat;
                case MobKind.Chicken:return MobItemId.WheatSeeds;
                default:return MobItemId.None;
            }
        }

        public static MobItemId WoolFor(MobSheepColor color)
        {
            switch(color)
            {
                case MobSheepColor.Black:return MobItemId.WoolBlack;
                case MobSheepColor.Gray:return MobItemId.WoolGray;
                case MobSheepColor.LightGray:return MobItemId.WoolLightGray;
                case MobSheepColor.Brown:return MobItemId.WoolBrown;
                case MobSheepColor.Pink:return MobItemId.WoolPink;
                default:return MobItemId.WoolWhite;
            }
        }

        public static void RecordPickup(MobItemId id,int count)
        {
            if(id==MobItemId.None||count<=0)return;
            pendingPickups.TryGetValue(id,out int n);pendingPickups[id]=n+count;
        }

        public static int PendingPickupCount(MobItemId id)
        {
            return pendingPickups.TryGetValue(id,out int n)?n:0;
        }
    }
}
