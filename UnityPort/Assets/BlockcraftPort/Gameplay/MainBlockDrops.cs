using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>main.js r2(): what a broken block drops (mining, support loss via L1(), etc.).</summary>
    public static class MainBlockDrops
    {
        /// <summary>Spawns r2(id) at a Unity-space position (normally the block centre).</summary>
        public static void Spawn(float x,float y,float z,BlockId broken)
        {
            if(BlockRegistry.IsLeaf(broken)){MainLeafDecay.SpawnLeafDrops(x,y,z,broken);return;}
            // main.js r2(), complete: every reference block, same rules and counts.
            float r=Random.value;
            string key=BlockRegistry.Key(broken);
            switch(broken)
            {
                case BlockId.Stone: MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.Cobblestone);return;
                case BlockId.Deepslate: MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.CobbledDeepslate);return;
                case BlockId.Grass: case BlockId.Mycelium: case BlockId.Podzol: case BlockId.DirtPath:
                    MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.Dirt);return;
                case BlockId.Gravel:
                    if(r<.15f)MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Flint,1);else MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.Gravel);return;
                case BlockId.Clay: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.ClayBall,4);return;
                case BlockId.Melon: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.MelonSlice,3+Random.Range(0,3));return;
                case BlockId.SweetBerryBush: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.SweetBerries,2);return;
                case BlockId.Farmland: case BlockId.FarmlandMoist: MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.Dirt);return;
                case BlockId.Fire: case BlockId.DeadBush: return;
                case BlockId.Cobweb: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.String,1);return;
                case BlockId.FurnaceLit:MainTransientRenderer.SpawnDroppedBlock(x,y,z,BlockId.Furnace);return;
                case BlockId.Wheat3:
                    MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Wheat,1);MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.WheatSeeds,1+Random.Range(0,3));return;
                case BlockId.Wheat0: case BlockId.Wheat1: case BlockId.Wheat2:
                    MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.WheatSeeds,1);return;
                case BlockId.Carrots3: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Carrot,2+Random.Range(0,3));return;
                case BlockId.Carrots0: case BlockId.Carrots1: case BlockId.Carrots2: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Carrot,1);return;
                case BlockId.Potatoes3: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Potato,2+Random.Range(0,3));return;
                case BlockId.Potatoes0: case BlockId.Potatoes1: case BlockId.Potatoes2: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Potato,1);return;
                case BlockId.PumpkinStem3: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.PumpkinSeeds,1+Random.Range(0,3));return;
                case BlockId.PumpkinStem0: case BlockId.PumpkinStem1: case BlockId.PumpkinStem2: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.PumpkinSeeds,1);return;
                case BlockId.MelonStem3: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.MelonSeeds,1+Random.Range(0,3));return;
                case BlockId.MelonStem0: case BlockId.MelonStem1: case BlockId.MelonStem2: MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.MelonSeeds,1);return;
                case BlockId.TallGrass: case BlockId.Fern: if(r<.2f)MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.WheatSeeds,1);return;
            }
            if(key=="COAL"||key.Contains("COAL_ORE")){MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.Coal,1);return;}
            if(key=="DIAMOND"||key.Contains("DIAMOND_ORE")){MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.DiamondGem,1);return;}
            if(key.Contains("EMERALD_ORE")){MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.EmeraldGem,1);return;}
            if(key.Contains("COPPER_ORE")){MainTransientRenderer.SpawnDroppedItem(x,y,z,MobItemId.RawCopper,2+Random.Range(0,3));return;}
            // main H1[]: breaking a bed head drops the (foot) bed item of the same colour.
            if(key.StartsWith("BED_HEAD",System.StringComparison.Ordinal))
            {
                BlockId foot=BlockRegistry.FromKey(key=="BED_HEAD"?"BED":"BED_"+key.Substring(9));
                MainTransientRenderer.SpawnDroppedBlock(x,y,z,foot!=BlockId.Air?foot:broken);return;
            }
            MainTransientRenderer.SpawnDroppedBlock(x,y,z,broken);
        }
    }
}
