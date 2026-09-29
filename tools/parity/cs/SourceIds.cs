using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BlockcraftPort;

/// Maps port BlockId values to reference (main.js/genWorker.js) numeric block ids by name.
static class SourceIds
{
    static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
    {
        ["CoalOre"]="COAL",["IronOre"]="IRON",["GoldOre"]="GOLD",["DiamondOre"]="DIAMOND",["Moss"]="MOSS_BLOCK",
        ["OakLog"]="LOG",["OakLeaves"]="LEAVES",["DarkOakLog"]="DARK_LOG",["DarkOakLeaves"]="DARK_LEAVES",
        ["CherryWood"]="CHERRY_PLANKS",["Amethyst"]="AMETHYST_BLOCK",["CherryLeaves"]="FLOWERING_AZALEA_LEAVES",
        ["OrangeTerracotta"]="TERRA_ORANGE",["WhiteTerracotta"]="TERRA_WHITE",["OakPlanks"]="PLANKS",
        ["Cobblestone"]="COBBLE",["CobblestoneSlab"]="COBBLE_SLAB",["CobblestoneStairs"]="COBBLE_STAIRS",
        ["OakFenceGate"]="OAK_GATE",["CobblestoneWall"]="COBBLE_WALL",["OakPressurePlate"]="PLANKS_PRESSURE_PLATE",
        ["OakButton"]="PLANKS_BUTTON",["BedRedFoot"]="BED",["BedRedHead"]="BED_HEAD",["Flow3"]="FLOW3",["Flow2"]="FLOW2",
        ["Flow1"]="FLOW1",["DarkOakPlanks"]="DARK_PLANKS",["DarkOakSlab"]="DARK_PLANKS_SLAB",["DarkOakStairs"]="DARK_PLANKS_STAIRS",
        ["DarkOakFence"]="DARK_PLANKS_FENCE",["LavaFlow2"]="LAVA_FLOW2",["LavaFlow1"]="LAVA_FLOW1",
    };
    public static ushort[] PortToSource;
    public static Dictionary<int, string> SourceNames = new Dictionary<int, string>();

    public static void Load(string jsonPath)
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(jsonPath), "\"([A-Z0-9_]+)\":(\\d+)"))
            SourceNames[int.Parse(m.Groups[2].Value)] = m.Groups[1].Value;
        PortToSource = new ushort[65536];
        for (int i = 0; i < SourceBlockData.Count; i++) PortToSource[i] = (ushort)SourceBlockData.SourceId[i];
    }
}
