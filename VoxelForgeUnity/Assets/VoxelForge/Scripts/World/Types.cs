// Voxel Forge — Unity port. Core runtime types (chunk/section storage, item defs, stacks, sim state).
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class Section
    {
        public byte[] blocks;
        public byte[] light;
        public Section() { blocks = new byte[4096]; light = new byte[4096]; for (int i = 0; i < 4096; i++) light[i] = 0xf0; }
        public Section(byte[] b, byte[] l) { blocks = b; light = l; }
    }

    public sealed class ItemDef
    {
        public string name;
        public int stack = 64;
        public double fuel, food;
        public string tool;
        public int tier;
        public double speed, damage, attackSpeed;
        public int maxDur;
        public string spawnEgg;
        public int block = -1;
        public ArmorInfo armor;
        public string @virtual;
        public bool hasDamage;
        public ItemDef Clone() { return (ItemDef)MemberwiseClone(); }
    }

    public sealed class Stack
    {
        public string key;
        public int count;
        public int dur; // 0 = undefined
        public Stack() { }
        public Stack(string k, int c, int d = 0) { key = k; count = c; dur = d; }
        public Stack Clone() { return new Stack(key, count, dur); }
        public JObj ToJson() { var o = new JObj { { "key", key }, { "count", (double)count } }; if (dur != 0) o["dur"] = (double)dur; return o; }
        public static Stack FromJson(object v)
        {
            var o = v as JObj; if (o == null) return null;
            return new Stack(o.Str("key"), o.Int("count"), o.Int("dur"));
        }
    }

    public sealed class FarmState { public double dryT; }
    public sealed class TimerState { public double t, delay; }
    public sealed class FireState { public double t, next, age; }

    public sealed class WorldSim
    {
        public readonly OrderedSet<string> waterUrgentQueue = new OrderedSet<string>(), waterQueue = new OrderedSet<string>(), waterSeedQueue = new OrderedSet<string>(),
            lavaQueue = new OrderedSet<string>(), lavaSeedQueue = new OrderedSet<string>();
        public readonly OrderedMap<string, FarmState> farmland = new OrderedMap<string, FarmState>();
        public readonly OrderedMap<string, TimerState> crops = new OrderedMap<string, TimerState>(), saplings = new OrderedMap<string, TimerState>(), growColumns = new OrderedMap<string, TimerState>();
        public readonly OrderedMap<string, double> leafDecay = new OrderedMap<string, double>();
        public readonly OrderedMap<string, FireState> fires = new OrderedMap<string, FireState>();
    }

    public struct GenMetaEntry { public int lx, iy, lz; public JObj m; public GenMetaEntry(int a, int b, int c, JObj o) { lx = a; iy = b; lz = c; m = o; } }

    public sealed class CropInfo { public string type; public int idx; public int[] stages; public int fruit; }
}
