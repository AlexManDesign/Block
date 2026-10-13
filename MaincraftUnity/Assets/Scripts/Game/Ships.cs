// Port of ships.js (7980-8270): ships and rafts. Using a ship wheel gathers every connected building block
// (not ground, rock, plants, leaves or fluids), lifts them out of the world (water below the waterline)
// and makes them one vessel, steered from the wheel: W / S drive, A / D turn, it runs aground on banks
// and shallows; Shift drops anchor (the turn rounded to a quarter, the position to a block) and puts the
// blocks back, chests and furnaces with their contents. The vessel is meshed in its own 16^3 sections by a
// private main-thread Mesher and drawn with Renderer.DrawShip.
//
// API skeleton: bodies marked TODO(player) are filled in by that implementer.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>A vessel block: cell relative to the wheel, dry id, meta.</summary>
    public sealed class ShipBlock
    {
        public int x, y, z, id, m;
    }

    /// <summary>Container contents travelling with a vessel, keyed by the wheel-relative cell "dx,dy,dz" (the same objects as in meta).</summary>
    public sealed class ShipStore
    {
        public OrderedMap<string, ItemStack[]> chests = new OrderedMap<string, ItemStack[]>();
        public OrderedMap<string, FurnaceRec> furnaces = new OrderedMap<string, FurnaceRec>();
    }

    /// <summary>The vessel under way (ships.js makeShip 8058).</summary>
    public sealed class Ship
    {
        public List<ShipBlock> blocks;      // BFS order (drives the setBlock order of clearing and anchoring, and the save)
        public double[] pos;                // world position of the wheel cell's corner (y stays integer)
        public double yaw;                  // turn (rad) about the wheel cell's centre
        public int face;                    // wheel facing (meta & 3), index into DIR_X / DIR_Z
        public double speed = 0, turn = 0;  // blocks/s along forward (-2..5); rad/s (yaw -= turn * dt)
        /// <summary>Cell lookup by Ships.MapKey(x, y, z) (the JS keyed "x,y,z" strings).</summary>
        public Dictionary<long, ShipBlock> map = new Dictionary<long, ShipBlock>();
        public List<ShipMesh> meshes = null;                 // GPU meshes per 16^3 section (null: not built)
        public double t = 0;                // time for the bob
        public int[] box;                   // local bounds x0, y0, z0, x1, y1, z1
        public List<ShipBlock> hull;        // blocks with an open side (collision test)
        public List<ShipBlock> keel;        // lowest block of each local column (support test)
        public ShipStore store;
    }

    /// <summary>One block of a saved vessel: [x, y, z, blockKey, meta].</summary>
    public struct ShipRecBlock
    {
        public int x, y, z;
        public string key;
        public int m;
    }

    /// <summary>The saved vessel (meta.ship): {b, pos, yaw, face, store}.</summary>
    public sealed class ShipRec
    {
        public List<ShipRecBlock> b;
        public double[] pos;
        public double yaw;
        public int face;
        public ShipStore store;             // null: absent (restore makes an empty one)
    }

    public sealed class Ships
    {
        public const int SHIP_MAX = 6000;
        /// <summary>Block keys a vessel never takes along (ground, rock, loose material, plants, fluids...): ships.js 7994.</summary>
        public static readonly Regex SHIP_NATURAL = null;   // TODO(player): the pattern of 7994 (precompute a per-id table from it)
        public static readonly int[] DIR_X = { 0, -1, 0, 1 }, DIR_Z = { 1, 0, -1, 0 };

        // ships.js shipMaterial (7997-8003): the block can be part of a vessel
        public static bool ShipMaterial(int id) { throw new NotImplementedException(); } // TODO(player)
        // ships.js shipFree (8005): a cell a vessel may move through or anchor in
        public static bool ShipFree(int id) { throw new NotImplementedException(); } // TODO(player)
        /// <summary>Packed key of a wheel-relative cell for Ship.map (replaces the JS "x,y,z" strings).</summary>
        public static long MapKey(int x, int y, int z) { throw new NotImplementedException(); } // TODO(player)

        public Game game;
        /// <summary>The vessel under way (one at a time), or null.</summary>
        public Ship ship = null;
        public Mesher mesher = null;        // private main-thread mesher for vessel meshes (lazy)

        // ships.js constructor (8008-8013)
        public Ships(Game game) { throw new NotImplementedException(); } // TODO(player)
        // ships.js assemble (8017-8056): gather the vessel from the wheel at (x, y, z); false (with a message) when it can not sail
        public bool Assemble(int x, int y, int z) { throw new NotImplementedException(); } // TODO(player)
        // ships.js makeShip (8057-8077)
        public Ship MakeShip(List<ShipBlock> blocks, double[] pos, double yaw, int face) { throw new NotImplementedException(); } // TODO(player)
        // ships.js takeStores (8079-8089): list holds the vessel's world cells [x, y, z]
        public ShipStore TakeStores(List<int[]> list, int x, int y, int z) { throw new NotImplementedException(); } // TODO(player)
        // ships.js toWorld (8094-8097): vessel-local point -> world point (the JS returned a new array)
        public void ToWorld(Ship S, double[] pos, double yaw, double lx, double ly, double lz, out double wx, out double wy, out double wz) { throw new NotImplementedException(); } // TODO(player)
        // ships.js support (8099-8107): fraction of the keel in water at a pose
        public double Support(Ship S, double[] pos, double yaw) { throw new NotImplementedException(); } // TODO(player)
        // ships.js blocked (8109-8117): a hull block would sit in something solid (or unloaded) at a pose
        public bool Blocked(Ship S, double[] pos, double yaw) { throw new NotImplementedException(); } // TODO(player)
        // ships.js board (8120-8124)
        public void Board() { throw new NotImplementedException(); } // TODO(player)
        // ships.js placePlayer (8126-8135)
        public void PlacePlayer(double dyaw) { throw new NotImplementedException(); } // TODO(player)
        // ships.js bob (8136)
        public double Bob() { throw new NotImplementedException(); } // TODO(player)
        // ships.js update (8137-8166): per-frame steering with real dt
        public void Update(double dt, InputKeys keys) { throw new NotImplementedException(); } // TODO(player)
        // ships.js anchor (8169-8203)
        public bool Anchor() { throw new NotImplementedException(); } // TODO(player)
        // ships.js serialize (8206-8210): null without a vessel
        public ShipRec Serialize() { throw new NotImplementedException(); } // TODO(player)
        // ships.js restore (8211-8220): unknown block keys skipped; boards the player
        public void Restore(ShipRec d) { throw new NotImplementedException(); } // TODO(player)
        // ships.js buildMeshes (8224-8254): Renderer.UploadShipMesh per non-empty section
        public void BuildMeshes(Ship S) { throw new NotImplementedException(); } // TODO(player)
        // ships.js freeMeshes (8255-8259)
        public void FreeMeshes(Ship S) { throw new NotImplementedException(); } // TODO(player)
        // ships.js render (8260-8265)
        public void Render(double[] cam, Env env, double time, uint tick) { throw new NotImplementedException(); } // TODO(player)
        // ships.js say (8268)
        public void Say(string text) { throw new NotImplementedException(); } // TODO(player)
    }
}
