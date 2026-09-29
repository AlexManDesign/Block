// Minimal stand-ins for the Unity-side classes the placement rules call into.
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    public struct VoxelHit { public Vector3Int Block; public Vector3Int Normal; public BlockId Id; public Vector3 Point; }

    /// <summary>Dictionary world with VoxelWorld.SetBlock semantics relevant to the rules (Unity coordinates).</summary>
    public class VoxelWorld
    {
        public readonly Dictionary<(int, int, int), (BlockId id, byte meta)> Cells = new Dictionary<(int, int, int), (BlockId, byte)>();
        public BlockId GetBlock(int x, int y, int z) => y < VoxelConstants.MinY || y > VoxelConstants.MaxY ? BlockId.Air : Cells.TryGetValue((x, y, z), out var v) ? v.id : BlockId.Air;
        public byte GetMeta(int x, int y, int z) => Cells.TryGetValue((x, y, z), out var v) ? v.meta : (byte)0;
        public void Raw(int x, int y, int z, BlockId id, byte meta) { if (id == BlockId.Air && meta == 0) Cells.Remove((x, y, z)); else Cells[(x, y, z)] = (id, meta); }
        public bool SetBlock(int x, int y, int z, BlockId id, byte meta = 0, bool playerEdit = false)
        {
            if (y < VoxelConstants.MinY || y > VoxelConstants.MaxY) return false;
            if (GetBlock(x, y, z) == id && GetMeta(x, y, z) == meta) return true;
            Raw(x, y, z, id, meta);
            MainBlockUpdates.Enqueue(x, y, z, id);
            MainBlockUpdates.Drain(this);
            return true;
        }
    }

    public static class MainWorldFunctionalBlocks
    {
        public static bool PlaceChestWorld(Vector3Int pos, float yaw, bool playerEdit = true) => false;
    }
    public static class MainBlockDrops
    {
        public static readonly List<string> Log = new List<string>();
        public static void Spawn(float x, float y, float z, BlockId id) => Log.Add(BlockRegistry.Key(id));
    }
    public static class MainTransientRenderer
    {
        public static void SpawnDroppedBlock(float x, float y, float z, BlockId id, int count = 1) => MainBlockDrops.Log.Add(BlockRegistry.Key(id));
    }
}
