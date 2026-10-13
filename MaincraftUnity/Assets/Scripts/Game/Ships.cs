// Port of ships.js (7980-8270): ships and rafts. Using a ship wheel gathers every connected building block
// (not ground, rock, plants, leaves or fluids), lifts them out of the world (water below the waterline)
// and makes them one vessel, steered from the wheel: W / S drive, A / D turn, it runs aground on banks
// and shallows; Shift drops anchor (the turn rounded to a quarter, the position to a block) and puts the
// blocks back, chests and furnaces with their contents. The vessel is meshed in its own 16^3 sections by a
// private main-thread Mesher and drawn with Renderer.DrawShip.
//
// Ships and rafts: blocks built on water around a ship wheel sail as one vessel.
//
// Using the wheel gathers every block connected to it (building materials only: not ground, stone,
// sand, plants, leaves or fluids), lifts them out of the world (the cells below the waterline turn
// to water) and makes them a vessel: one mesh drawn with the vessel's turn and position, the player
// standing at the wheel. W / S drive it forward and back, A / D turn it; it runs aground on banks
// and shallows. Shift drops anchor: the turn is rounded to a quarter, the position to a block, and
// the blocks go back into the world there (chests and furnaces keep what is in them). A vessel
// under way is saved with the world.
using System;
using System.Collections.Generic;
using System.Globalization;
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
        // blocks a vessel never takes along: the ground, rock, loose material, plants and fluids
        public static readonly Regex SHIP_NATURAL = new Regex(
            "^(GRASS|SNOWY_GRASS|DIRT|COARSE_DIRT|ROOTED_DIRT|PODZOL|MYCELIUM|MUD|CLAY|FARMLAND.*|DIRT_PATH|STONE|DEEPSLATE|GRANITE|DIORITE|ANDESITE|TUFF|CALCITE|DRIPSTONE|SMOOTH_BASALT|BASALT|BEDROCK|SAND|RED_SAND|GRAVEL|SANDSTONE|RED_SANDSTONE|SNOW|SNOW_LAYER|ICE|PACKED_ICE|BLUE_ICE|NETHERRACK|MAGMA_BLOCK|SOUL_SAND|SOUL_SOIL|OBSIDIAN|CRYING_OBSIDIAN|TERRACOTTA|TERRA_.*|.*_TERRACOTTA|MOSS_BLOCK|MOSS_CARPET|PALE_MOSS_BLOCK|PALE_MOSS_CARPET|SCULK.*|AMETHYST_BLOCK|BUDDING_AMETHYST|.*AMETHYST_BUD|AMETHYST_CLUSTER|.*_ORE|.*LEAVES|.*CORAL.*|SEA_PICKLE|KELP.*|SEAGRASS|TALL_SEAGRASS|LILY_PAD|BEDROCK|SPAWNER|POINTED_DRIPSTONE|MANGROVE_ROOTS.*|MUDDY_MANGROVE_ROOTS)$",
            RegexOptions.CultureInvariant);
        public static readonly int[] DIR_X = { 0, -1, 0, 1 }, DIR_Z = { 1, 0, -1, 0 };

        // shipMaterial for every id below NBX, computed once from SHIP_NATURAL after Blocks.Init (the JS
        // tested the regex on every BFS neighbour)
        static bool[] SHIP_MAT;
        static bool[] ShipMatTable()
        {
            var t = new bool[NBX];
            for (int id = 1; id < NBX; id++)
            {
                int d = DryId(id);
                if (IsLiquidId(d) || d == B.WATER || d == B.LAVA) continue;
                if ((FLAGS[d] & BF_REPLACE) != 0) continue;
                t[id] = !SHIP_NATURAL.IsMatch(B_KEY[d] ?? "");
            }
            return t;
        }
        // the block can be part of a vessel
        public static bool ShipMaterial(int id)
        {
            if (id == 0) return false;
            var t = SHIP_MAT ?? (SHIP_MAT = ShipMatTable());
            // (an id past every table has no key, no flags and is no fluid: the JS counted it as material)
            return id >= NBX || t[id];
        }
        // cells a vessel may move through or anchor in
        public static bool ShipFree(int id) => id == 0 || IsLiquidId(id) || (FLAGS[id] & BF_REPLACE) != 0;
        /// <summary>Packed key of a wheel-relative cell for Ship.map (replaces the JS "x,y,z" strings; 21 bits an axis).</summary>
        public static long MapKey(int x, int y, int z) => ((long)(x + 1048576) << 42) | ((long)(y + 1048576) << 21) | (long)(z + 1048576);
        // the `${x},${y},${z}` key of meta.chests / meta.furnaces and of a vessel's store (saved: culture-independent digits)
        static string CellKey(int x, int y, int z) =>
            x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture);

        public Game game;
        /// <summary>The vessel under way (one at a time), or null.</summary>
        public Ship ship = null;
        public Mesher mesher = null;        // private main-thread mesher for vessel meshes (lazy)
        /// <summary>Set to null by the constructor and never used (Game.Say owns the message element).</summary>
        public UnityEngine.UIElements.VisualElement msgEl = null;

        // a pose being tried by update (the JS made a new array per try; an accepted one becomes S.pos and
        // the vessel's old array becomes the next scratch)
        double[] tryPos = new double[3];
        // buildMeshes' padded section arrays (the JS made new ones per section)
        ushort[] mIds; byte[] mMeta, mLight;
        static readonly int[] FACE_DX = { 1, -1, 0, 0, 0, 0 }, FACE_DY = { 0, 0, 1, -1, 0, 0 }, FACE_DZ = { 0, 0, 0, 0, 1, -1 };
        // anchoring: the spots tried around the rounded position, in order
        static readonly int[] ANCHOR_DX = { 0, 1, -1, 0, 0, 1, -1, 1, -1 }, ANCHOR_DZ = { 0, 0, 0, 1, -1, 1, -1, -1, 1 };

        public Ships(Game game)
        {
            this.game = game;
            ship = null;            // the vessel under way (one at a time: the one being steered)
            mesher = null;
            msgEl = null;
        }

        // ------------------------------------------------------------------ assembling
        // gather the vessel from the wheel at (x, y, z); returns false (with a message) when it can not sail
        public bool Assemble(int x, int y, int z)
        {
            var g = game; var w = g.world;
            if (ship != null) return false;
            // the JS number key (a - x + 512) * 1048576 + (b - y + 512) * 1024 + (c - z + 512), in a long
            long Key(int a, int b, int c) => (long)(a - x + 512) * 1048576 + (long)(b - y + 512) * 1024 + (c - z + 512);
            var seen = new HashSet<long> { Key(x, y, z) }; var list = new List<int[]> { new[] { x, y, z } };
            for (int i = 0; i < list.Count; i++)
            {
                if (list.Count > SHIP_MAX) { Say(Lang.T("shipTooBig")); return false; }
                int cx = list[i][0], cy = list[i][1], cz = list[i][2];
                for (int f = 0; f < 6; f++)
                {
                    int nx = cx + FACE_DX[f], ny = cy + FACE_DY[f], nz = cz + FACE_DZ[f];
                    long k = Key(nx, ny, nz);
                    if (seen.Contains(k)) continue;
                    seen.Add(k);
                    if (!w.IsLoaded(nx, nz) || ny <= WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
                    if (ShipMaterial(w.GetBlock(nx, ny, nz))) list.Add(new[] { nx, ny, nz });
                }
            }
            // the blocks with their cells relative to the wheel
            var blocks = new List<ShipBlock>(list.Count);
            foreach (var c in list)
            {
                int bx = c[0], by = c[1], bz = c[2];
                int id = w.GetBlock(bx, by, bz);
                blocks.Add(new ShipBlock { x = bx - x, y = by - y, z = bz - z, id = DryId(id), m = w.GetMeta(bx, by, bz) });
            }
            var S = MakeShip(blocks, new double[] { x, y, z }, 0, w.GetMeta(x, y, z) & 3);
            if (Support(S, S.pos, 0) <= 0) { Say(Lang.T("shipNoWater")); return false; }
            // the waterline: the highest still water touching the hull from outside
            int line = -1000000000;
            foreach (var c in list)
            {
                int bx = c[0], by = c[1], bz = c[2];
                for (int f = 0; f < 6; f++)
                {
                    int nx = bx + FACE_DX[f], ny = by + FACE_DY[f], nz = bz + FACE_DZ[f];
                    if (seen.Contains(Key(nx, ny, nz)) && ShipMaterial(w.GetBlock(nx, ny, nz))) continue;
                    if (w.GetBlock(nx, ny, nz) == B.WATER && w.GetMeta(nx, ny, nz) == 0) line = Math.Max(line, ny);
                }
            }
            // containers travel with their contents
            S.store = TakeStores(list, x, y, z);
            foreach (var c in list) w.SetBlock(c[0], c[1], c[2], c[1] <= line ? B.WATER : 0, 0);
            ship = S;
            Board();
            Say(Lang.T("shipHelp"));
            return true;
        }
        public Ship MakeShip(List<ShipBlock> blocks, double[] pos, double yaw, int face)
        {
            var S = new Ship { blocks = blocks, pos = new[] { pos[0], pos[1], pos[2] }, yaw = yaw, face = face, speed = 0, turn = 0, map = new Dictionary<long, ShipBlock>(), meshes = null, t = 0 };
            int x0 = 1000000000, y0 = 1000000000, z0 = 1000000000, x1 = -1000000000, y1 = -1000000000, z1 = -1000000000;
            foreach (var b in blocks)
            {
                S.map[MapKey(b.x, b.y, b.z)] = b;
                x0 = Math.Min(x0, b.x); y0 = Math.Min(y0, b.y); z0 = Math.Min(z0, b.z); x1 = Math.Max(x1, b.x); y1 = Math.Max(y1, b.y); z1 = Math.Max(z1, b.z);
            }
            S.box = new[] { x0, y0, z0, x1, y1, z1 };
            // hull: blocks with an open side (only those can touch the outside); keel: the lowest block of
            // every column (what rests in the water)
            S.hull = new List<ShipBlock>();
            // low: column key b.x * 4096 + b.z -> its place in the keel (the JS Map kept the first insertion's order)
            var low = new Dictionary<long, int>(); var keel = new List<ShipBlock>();
            foreach (var b in blocks)
            {
                bool open = false;
                for (int f = 0; f < 6; f++) if (!S.map.ContainsKey(MapKey(b.x + FACE_DX[f], b.y + FACE_DY[f], b.z + FACE_DZ[f]))) { open = true; break; }
                if (open) S.hull.Add(b);
                long k = (long)b.x * 4096 + b.z;
                if (!low.TryGetValue(k, out int li)) { low[k] = keel.Count; keel.Add(b); }
                else if (b.y < keel[li].y) keel[li] = b;
            }
            S.keel = keel;
            return S;
        }
        // chest / furnace contents of the vessel's cells, keyed by the cell relative to the wheel
        public ShipStore TakeStores(List<int[]> list, int x, int y, int z)
        {
            var g = game; var outS = new ShipStore();
            var C = g.meta.chests ?? (g.meta.chests = new OrderedMap<string, ItemStack[]>());
            var F = g.meta.furnaces ?? (g.meta.furnaces = new OrderedMap<string, FurnaceRec>());
            g.chests = C;
            foreach (var c in list)
            {
                int bx = c[0], by = c[1], bz = c[2];
                string k = CellKey(bx, by, bz), l = CellKey(bx - x, by - y, bz - z);
                if (C.TryGetValue(k, out var ch) && ch != null) { outS.chests.Set(l, ch); C.Remove(k); }
                if (F.TryGetValue(k, out var fu) && fu != null) { outS.furnaces.Set(l, fu); F.Remove(k); }
            }
            return outS;
        }

        // ------------------------------------------------------------------ geometry
        // world point of a vessel-local point (cell units, the wheel cell's corner at 0, turned about the
        // wheel cell's centre)
        public void ToWorld(Ship S, double[] pos, double yaw, double lx, double ly, double lz, out double wx, out double wy, out double wz)
        {
            double c = Math.Cos(yaw), s = Math.Sin(yaw), ax = lx - 0.5, az = lz - 0.5;
            wx = pos[0] + 0.5 + ax * c - az * s; wy = pos[1] + ly; wz = pos[2] + 0.5 + ax * s + az * c;
        }
        // how much of the keel sits in water (fraction of its columns) at a pose
        public double Support(Ship S, double[] pos, double yaw)
        {
            var w = game.world;
            int n = 0;
            foreach (var b in S.keel)
            {
                ToWorld(S, pos, yaw, b.x + 0.5, b.y + 0.5, b.z + 0.5, out double px, out double py, out double pz);
                int X = (int)Math.Floor(px), Y = (int)Math.Floor(py), Z = (int)Math.Floor(pz);
                if (IsWaterId(w.GetBlock(X, Y, Z)) || IsWaterId(w.GetBlock(X, Y - 1, Z))) n++;
            }
            return n / (double)Math.Max(1, S.keel.Count);
        }
        // true when a hull block would sit in something solid at a pose
        public bool Blocked(Ship S, double[] pos, double yaw)
        {
            var w = game.world;
            foreach (var b in S.hull)
            {
                ToWorld(S, pos, yaw, b.x + 0.5, b.y + 0.5, b.z + 0.5, out double px, out double py, out double pz);
                if (!w.IsLoaded((int)Math.Floor(px), (int)Math.Floor(pz))) return true;
                if (!ShipFree(w.GetBlock((int)Math.Floor(px), (int)Math.Floor(py), (int)Math.Floor(pz)))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ sailing
        public void Board()
        {
            var p = game.player;
            p.vel = new double[] { 0, 0, 0 }; p.flying = false; p.fallDist = 0;
            PlacePlayer(0);
        }
        // the player stands in front of the wheel, turned with the vessel
        public void PlacePlayer(double dyaw)
        {
            var S = ship; var p = game.player;
            int fx = DIR_X[S.face], fz = DIR_Z[S.face];
            ToWorld(S, S.pos, S.yaw, fx + 0.5, 0, fz + 0.5, out double qx, out _, out double qz);
            p.pos[0] = qx; p.pos[1] = S.pos[1] + Bob(); p.pos[2] = qz;
            // (p.pos.slice() in the JS; nothing else holds the player's prev, so it is refilled in place)
            var pr = p.prev;
            if (pr == null || ReferenceEquals(pr, p.pos)) p.prev = pr = new double[3];
            pr[0] = p.pos[0]; pr[1] = p.pos[1]; pr[2] = p.pos[2]; p.prevOf = p.pos;
            p.vel[0] = p.vel[1] = p.vel[2] = 0;
            p.onGround = true; p.inWater = false; p.fallDist = 0;
            p.yaw += dyaw;
        }
        public double Bob() => ship != null ? Math.Sin(ship.t * 1.3) * 0.04 : 0;
        public void Update(double dt, InputKeys keys)
        {
            var S = ship;
            if (S == null) return;
            S.t += dt;
            if (keys.sneak) { Anchor(); return; }
            // throttle and rudder: speed builds up and fades, the turn is quicker when under way
            int thrust = (keys.forward ? 1 : 0) - (keys.back ? 1 : 0);
            double target = thrust > 0 ? 5 : thrust < 0 ? -2 : 0;
            double diff = target - S.speed, sign = diff > 0 ? 1 : diff < 0 ? -1 : diff;   // Math.sign (0, -0 and NaN as they are)
            S.speed += sign * Math.Min(Math.Abs(target - S.speed), (thrust != 0 ? 1.6 : 1.0) * dt);
            int rud = (keys.left ? 1 : 0) - (keys.right ? 1 : 0);
            S.turn += (rud * (0.35 + Math.Min(1, Math.Abs(S.speed) / 4) * 0.45) - S.turn) * Math.Min(1, dt * 3);
            double dyaw = -S.turn * dt;
            // forward is away from the wheel's face
            double c = Math.Cos(S.yaw), s = Math.Sin(S.yaw); int fx = -DIR_X[S.face], fz = -DIR_Z[S.face];
            double vx = (fx * c - fz * s) * S.speed * dt, vz = (fx * s + fz * c) * S.speed * dt;
            bool moved = false;
            bool turning = dyaw != 0 && !double.IsNaN(dyaw);
            if (turning && TryPose(S, S.pos, S.yaw + dyaw)) { moved = true; PlacePlayer(dyaw); }
            else if (turning) S.turn = 0;
            if ((vx != 0 && !double.IsNaN(vx)) || (vz != 0 && !double.IsNaN(vz)))
            {
                var tp = tryPos;
                tp[0] = S.pos[0] + vx; tp[1] = S.pos[1]; tp[2] = S.pos[2] + vz;
                if (TryPose(S, tp, S.yaw)) moved = true;
                else
                {
                    tp[0] = S.pos[0] + vx; tp[1] = S.pos[1]; tp[2] = S.pos[2];
                    bool ok = TryPose(S, tp, S.yaw);
                    if (!ok) { tp[0] = S.pos[0]; tp[1] = S.pos[1]; tp[2] = S.pos[2] + vz; ok = TryPose(S, tp, S.yaw); }
                    if (ok) { moved = true; S.speed *= 0.5; }
                    else S.speed = 0;            // aground
                }
            }
            _ = moved;
            PlacePlayer(0);
        }
        // the pose is taken when no hull block runs into anything and a quarter of the keel is in water
        bool TryPose(Ship S, double[] p, double yaw)
        {
            if (Blocked(S, p, yaw) || Support(S, p, yaw) < 0.25) return false;
            if (!ReferenceEquals(p, S.pos)) { tryPos = S.pos; S.pos = p; }
            S.yaw = yaw; return true;
        }

        // ------------------------------------------------------------------ anchoring
        public bool Anchor()
        {
            var S = ship; var g = game; var w = g.world;
            if (S == null) return true;
            // Math.round (half up) of the quarter turns, then the JS ((n % 4) + 4) % 4 on the number
            int r = (int)(((Math.Floor(S.yaw / (Math.PI / 2) + 0.5) % 4) + 4) % 4);
            int bx = (int)Math.Floor(S.pos[0] + 0.5), by = (int)S.pos[1], bz = (int)Math.Floor(S.pos[2] + 0.5);   // (pos[1] is an integer)
            int atX = 0, atZ = 0; bool found = false;
            for (int i = 0; i < ANCHOR_DX.Length; i++) if (Fits(S, w, r, by, bx + ANCHOR_DX[i], bz + ANCHOR_DZ[i])) { atX = bx + ANCHOR_DX[i]; atZ = bz + ANCHOR_DZ[i]; found = true; break; }
            if (!found) { Say(Lang.T("shipNoAnchor")); S.speed = 0; return false; }
            int X = atX, Z = atZ;
            var C = g.meta.chests ?? (g.meta.chests = new OrderedMap<string, ItemStack[]>());
            var F = g.meta.furnaces ?? (g.meta.furnaces = new OrderedMap<string, FurnaceRec>());
            g.chests = C;
            foreach (var b in S.blocks)
            {
                var o = WorldGen.TplPos(b.x, b.z, r, 0, 0); int wx = X + o.Item1, wy = by + b.y, wz = Z + o.Item2;
                int cur = w.GetBlock(wx, wy, wz);
                int wet = b.id >= 0 && b.id < WET.Length ? WET[b.id] : 0;
                int id = wet != 0 && cur == B.WATER ? wet : b.id;
                w.SetBlock(wx, wy, wz, id, WG.RotMeta(b.id, b.m, r));
                string l = CellKey(b.x, b.y, b.z), k = CellKey(wx, wy, wz);
                if (S.store != null && S.store.chests != null && S.store.chests.TryGetValue(l, out var ch) && ch != null) C.Set(k, ch);
                if (S.store != null && S.store.furnaces != null && S.store.furnaces.TryGetValue(l, out var fu) && fu != null) F.Set(k, fu);
            }
            // the player steps off in front of the wheel
            var p = g.player; var po = WorldGen.TplPos(DIR_X[S.face], DIR_Z[S.face], r, 0, 0);
            p.pos = new double[] { X + po.Item1 + 0.5, S.pos[1], Z + po.Item2 + 0.5 }; p.prev = (double[])p.pos.Clone(); p.prevOf = p.pos; p.vel = new double[] { 0, 0, 0 };
            FreeMeshes(S);
            ship = null;
            Say(Lang.T("shipAnchored"));
            return true;
        }
        // every vessel block lands in a free cell with the turn r and the wheel at (X, by, Z)
        static bool Fits(Ship S, World w, int r, int by, int X, int Z)
        {
            foreach (var b in S.blocks)
            {
                var o = WorldGen.TplPos(b.x, b.z, r, 0, 0);
                if (!ShipFree(w.GetBlock(X + o.Item1, by + b.y, Z + o.Item2))) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ saving
        public ShipRec Serialize()
        {
            var S = ship;
            if (S == null) return null;
            var bl = new List<ShipRecBlock>(S.blocks.Count);
            foreach (var b in S.blocks) bl.Add(new ShipRecBlock { x = b.x, y = b.y, z = b.z, key = B_KEY[b.id], m = b.m });
            // (pos: S.pos in the JS, an array the vessel then replaces rather than changes; update reuses
            // its arrays here, so the record gets a copy)
            return new ShipRec { b = bl, pos = new[] { S.pos[0], S.pos[1], S.pos[2] }, yaw = S.yaw, face = S.face, store = S.store };
        }
        public void Restore(ShipRec d)
        {
            // (the JS dropped a vessel still under way without freeing its GPU buffers)
            if (ship != null) FreeMeshes(ship);
            ship = null;
            if (d == null || d.b == null) return;
            var blocks = new List<ShipBlock>();
            foreach (var e in d.b) if (Has(e.key)) blocks.Add(new ShipBlock { x = e.x, y = e.y, z = e.z, id = Id(e.key), m = e.m });
            if (blocks.Count == 0) return;
            ship = MakeShip(blocks, d.pos, double.IsNaN(d.yaw) ? 0 : d.yaw, d.face);
            ship.store = d.store ?? new ShipStore();
            Board();
        }

        // ------------------------------------------------------------------ drawing
        // the vessel's blocks meshed in 16-cell sections of its own grid, lit by the open sky
        public void BuildMeshes(Ship S)
        {
            var g = game; var r = g.r;
            // (the JS ran initMesherTextures here: its main thread had no mesher tables yet; GameMain filled
            // the shared static ones at boot)
            if (mesher == null) { mesher = new Mesher(); mesher.sway = false; }
            int x0 = S.box[0], y0 = S.box[1], z0 = S.box[2], x1 = S.box[3], y1 = S.box[4], z1 = S.box[5];
            var outL = new List<ShipMesh>();
            const int N = 18 * 18 * 18;
            var ids = mIds ?? (mIds = new ushort[N]); var meta = mMeta ?? (mMeta = new byte[N]); var light = mLight ?? (mLight = new byte[N]);
            for (int sy = y0; sy <= y1; sy += 16) for (int sz = z0; sz <= z1; sz += 16) for (int sx = x0; sx <= x1; sx += 16)
            {
                Array.Clear(ids, 0, N); Array.Clear(meta, 0, N);
                for (int i = 0; i < N; i++) light[i] = 0xF0;
                bool any = false;
                for (int y = -1; y <= 16; y++) for (int z = -1; z <= 16; z++) for (int x = -1; x <= 16; x++)
                {
                    if (!S.map.TryGetValue(MapKey(sx + x, sy + y, sz + z), out var b)) continue;
                    int i = ((y + 1) * 18 + (z + 1)) * 18 + (x + 1);
                    ids[i] = (ushort)b.id; meta[i] = (byte)b.m; light[i] = 0;
                    if (x >= 0 && x < 16 && y >= 0 && y < 16 && z >= 0 && z < 16) any = true;
                }
                if (!any) continue;
                var d = mesher.Mesh(new MeshJob { ids = ids, meta = meta, light = light, leaves = 2, sway = false });
                int total = d.counts[0] + d.counts[1] + d.counts[2];
                if (total == 0) continue;
                // the section's quads on the GPU, drawn from its own origin in the vessel's grid
                outL.Add(r.UploadShipMesh(d, sx, sy, sz));
            }
            S.meshes = outL;
        }
        public void FreeMeshes(Ship S)
        {
            var r = game.r;
            if (S.meshes != null) foreach (var m in S.meshes) r.FreeShipMesh(m);
            S.meshes = null;
        }
        public void Render(double[] cam, Env env, double time, uint tick)
        {
            var S = ship;
            if (S == null) return;
            if (S.meshes == null) BuildMeshes(S);
            game.r.DrawShip(S.meshes, S.yaw, S.pos[0], S.pos[1], S.pos[2], Bob(), cam[0], cam[1], cam[2], env, time, tick);
        }

        // ------------------------------------------------------------------ messages
        public void Say(string text) => game.Say(text);
    }
}
