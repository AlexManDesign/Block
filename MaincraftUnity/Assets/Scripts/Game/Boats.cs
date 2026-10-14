// Port of boats.js (8271-8612): Minecraft-style boats. Placed from the item on water (or ground), boarded
// with the use key, steered with W / A / S / D, left with Shift, broken by hitting (drops the item in
// survival). Per game tick (20/s) a boat floats by its depth in water against gravity, takes water / ice /
// ground friction, the rider's turn and push, rows its two paddles, and moves axis by axis with
// bisection. Boats are solid boxes for the player (colliders) and are drawn as CPU-transformed boxes
// plus a depth-only water mask inside each hull.
//
// Boats, after Minecraft's Boat: placed from the item on water (or ground), boarded with the use
// key, steered with W / A / S / D, left with Shift, broken by hitting it (it drops the item).
// Per game tick (20/s): the boat floats up by how deep it sits in water (0.0615 x depth / height)
// against gravity 0.04, is slowed by 0.9 in water, by the block under it on land (0.6, ice 0.98)
// and 0.45 under water; the rider turns it by 1 degree a tick and pushes it 0.04 forward
// (0.005 back), so it tops out near 8 blocks a second on open water. Two paddles row as the rider
// steers: both going forward, the outer one alone in a turn (Boat.setPaddleState).
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>Boat.getStatus (the JS strings 'air', 'water', 'under', 'land').</summary>
    public enum BoatStatus { Air, Water, Under, Land }

    /// <summary>One boat (boats.js place 8291).</summary>
    public sealed class Boat
    {
        public double[] pos;                // bottom centre
        public double[] prev;               // position at the previous tick
        public double[] vel = { 0, 0, 0 };  // blocks per tick
        public double yaw, pyaw;            // heading (forward = (sin yaw, -cos yaw)) now / at the previous tick
        public double drot = 0;             // turn rate, degrees per tick
        public double damage = 0;           // breaks over 40, -1 a tick
        public bool onGround = false;
        public BoatStatus status = BoatStatus.Air;
        public double turned = 0, lastTurn = 0;            // turn since boarding / of the last tick (rad)
        public double[] pad = { 0, 0 };                    // paddle rowing angles (rad)
        public bool[] padOn = { false, false };            // paddle rowing this tick
        public int hurtT;                   // shake ticks left (undefined -> 0)
        public int hurtDir;                 // shake direction +1 / -1 (0: undefined, never hit)
    }

    /// <summary>Boats.raycast result {b, t}.</summary>
    public struct BoatHit
    {
        public Boat b;
        public double t;
        public BoatHit(Boat b, double t) { this.b = b; this.t = t; }
    }

    public sealed class Boats
    {
        public const double BOAT_W = 1.375, BOAT_H = 0.5625;

        public Game game;
        /// <summary>All boats (read by entities.js fallMove and game.js obstructed).</summary>
        public List<Boat> list = new List<Boat>();
        /// <summary>The boat the player rides, or null.</summary>
        public Boat riding = null;
        /// <summary>Tick accumulator (s); acc / 0.05 is the render alpha (game.js 13456, entities.js 11463).</summary>
        public double acc = 0;
        public double turnShown;            // part of the boat's turn already given to the rider's yaw
        public List<double[]> cboxes;       // colliders' reused output (player.extraBoxes points at it)
        public VBuf vb, tmp, pv;            // draw buffer (render and renderMask), one boat's geometry, one paddle's
        public double[] col;                // vertex colour (light, light, light, 1)

        // colliders' boxes, reused from call to call (the JS made new arrays; nothing keeps the old ones)
        readonly List<double[]> cpool = new List<double[]>();
        // raycast's box corners (the JS made two arrays per boat)
        readonly double[] rlo = new double[3], rhi = new double[3];
        // Entity.move axis orders: Y, then the larger of X and Z (X on a tie)
        static readonly int[] ORDER_ZX = { 1, 2, 0 }, ORDER_XZ = { 1, 0, 2 };
        // own design: a flat bottom, low sides and ends, a bench across the middle (local x across, z along)
        static readonly double[][] PARTS =
        {
            new[] { -0.62, 0, -0.85, 0.62, 0.12, 0.85 }, new[] { -0.69, 0.06, -0.85, -0.56, 0.5, 0.85 }, new[] { 0.56, 0.06, -0.85, 0.69, 0.5, 0.85 },
            new[] { -0.56, 0.06, -0.92, 0.56, 0.44, -0.8 }, new[] { -0.56, 0.06, 0.8, 0.56, 0.44, 0.92 }, new[] { -0.56, 0.28, -0.12, 0.56, 0.36, 0.12 },
        };
        // the water mask quad's corners (local x, z) and its two triangles
        static readonly double[] MASK_X = { -0.56, 0.56, 0.56, -0.56 }, MASK_Z = { -0.8, -0.8, 0.8, 0.8 };
        static readonly int[] MASK_TRI = { 0, 1, 2, 0, 2, 3 };
        static int itBoat = -1;

        public Boats(Game game)
        {
            this.game = game;
            list = new List<Boat>();
            riding = null;
            acc = 0;
        }
        public Boat Place(double x, double y, double z, double yaw)
        {
            var b = new Boat
            {
                pos = new[] { x, y, z }, prev = new[] { x, y, z }, vel = new double[] { 0, 0, 0 }, yaw = yaw, pyaw = yaw, drot = 0, damage = 0, onGround = false,
                status = BoatStatus.Air, turned = 0, lastTurn = 0, pad = new double[] { 0, 0 }, padOn = new[] { false, false },
            };
            list.Add(b);
            return b;
        }
        // nearest boat on the ray (eye, unit dir) within range: {b, t}; skip: a boat to leave out
        public BoatHit? Raycast(double[] eye, double[] dir, double range, Boat skip = null)
        {
            BoatHit? best = null;
            double[] lo = rlo, hi = rhi;
            foreach (var b in list)
            {
                if (b == skip) continue;
                const double hw = BOAT_W / 2;
                lo[0] = b.pos[0] - hw; lo[1] = b.pos[1]; lo[2] = b.pos[2] - hw;
                hi[0] = b.pos[0] + hw; hi[1] = b.pos[1] + BOAT_H; hi[2] = b.pos[2] + hw;
                double t0 = 0, t1 = range; bool ok = true;
                for (int a = 0; a < 3 && ok; a++)
                {
                    if (Math.Abs(dir[a]) < 1e-9) { if (eye[a] < lo[a] || eye[a] > hi[a]) ok = false; continue; }
                    double ta = (lo[a] - eye[a]) / dir[a], tb = (hi[a] - eye[a]) / dir[a];
                    if (ta > tb) { double t = ta; ta = tb; tb = t; }
                    t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
                    if (t0 > t1) ok = false;
                }
                if (ok && (best == null || t0 < best.Value.t)) best = new BoatHit(b, t0);
            }
            return best;
        }
        // Boat.hurt: the damage builds up (10 a point of the hit) and wears off 1 a tick; over 40 the
        // boat breaks and drops itself (in creative the first hit takes it, without a drop). A hit rocks
        // the boat for 10 ticks, the other way each time.
        public void Hit(Boat b, double dmg)
        {
            var g = game;
            if (!list.Contains(b)) return;
            b.damage += dmg * 10;
            b.hurtT = 10; b.hurtDir = -(b.hurtDir != 0 ? b.hurtDir : -1);
            if (g.mode != GameMode.Survival || b.damage > 40)
            {
                if (riding == b) Leave();
                list.RemoveAt(list.IndexOf(b));
                if (g.mode == GameMode.Survival)
                {
                    if (itBoat < 0) itBoat = Items.Id("BOAT");
                    g.ents.DropItem(itBoat, 1, b.pos[0], b.pos[1] + 0.5, b.pos[2]);
                }
                Sfx.Block(B.PLANKS, SfxKind.Break);
            }
            else Sfx.Block(B.PLANKS, SfxKind.Hit);
        }
        public void Board(Boat b)
        {
            riding = b;
            var p = game.player;
            p.flying = false; p.vel = new double[] { 0, 0, 0 }; p.fallDist = 0;
            b.turned = 0; turnShown = 0;
            Seat();
        }
        // Boat.getDismountLocationForPassenger: a spot just past the boat's side in the direction the
        // rider looks; when the ground there is not water, onto that floor (the cell at the boat's top or
        // the one below it) if the player fits; otherwise (open water) on top of the boat
        public void Leave()
        {
            Boat b = riding; var p = game.player; var w = game.world;
            riding = null;
            if (b == null) return;
            double top = b.pos[1] + BOAT_H;
            double fx = Math.Sin(p.yaw), fz = -Math.Cos(p.yaw), f2 = Math.Max(Math.Abs(fx), Math.Abs(fz));
            if (f2 == 0 || double.IsNaN(f2)) f2 = 1;
            const double d0 = (BOAT_W * 1.4142135623730951 + Player.PLAYER_W + 1e-5) / 2;   // Math.SQRT2
            double x = b.pos[0] + fx * d0 / f2, z = b.pos[2] + fz * d0 / f2;
            int bx = (int)Math.Floor(x), by = (int)Math.Floor(top), bz = (int)Math.Floor(z);
            double[] at = null;
            if (!IsWaterId(w.GetBlock(bx, by - 1, bz)))
            {
                for (int cy = by, i = 0; i < 2; i++, cy = by - 1)
                {
                    double? y = FloorAt(bx, cy, bz);
                    if (y != null && !p.Collides(w, x, y.Value, z, Player.PLAYER_H)) { at = new[] { x, y.Value, z }; break; }
                }
            }
            p.pos = at ?? new[] { b.pos[0], top, b.pos[2] };
            p.prev = (double[])p.pos.Clone(); p.prevOf = p.pos; p.vel = new double[] { 0, 0, 0 }; p.fallDist = 0;
            p.eyeOffset = Player.EYE; p.eyePrev = Player.EYE;
            p.Interp(1);
        }
        // Level.getBlockFloorHeight + DismountHelper.isBlockFloorValid: standing height in cell y (world
        // y), null when there is no floor there: a shape lower than a full block in the cell gives its
        // top, an empty cell over a full-height block gives the cell's bottom
        public double? FloorAt(int x, int y, int z)
        {
            double t = FloorTop(x, y, z);
            if (t >= 0) return t < 1 ? y + t : (double?)null;
            return FloorTop(x, y - 1, z) >= 1 ? y : (double?)null;
        }
        // the highest collision box top in cell (x, cy, z), -1 without any
        double FloorTop(int x, int cy, int z)
        {
            var w = game.world;
            int id = w.GetBlock(x, cy, z);
            var bx = COLLIDE[id] != 0 ? Shapes.CollisionBoxes(w, id, w.GetMeta(x, cy, z), x, cy, z) : null;
            if (bx == null || bx.Length == 0) return -1;
            double t = 0;
            foreach (var q in bx) t = Math.Max(t, q[4]);
            return t;
        }
        // the boats the player stands on or walks into (all but the one ridden), as boxes for the
        // player's collisions; one bobbing up a little into a player standing on it lifts the player
        // onto its top, one the player is deep inside (just placed over them) does not hold them
        public List<double[]> Colliders(Player p)
        {
            var outL = cboxes ?? (cboxes = new List<double[]>());
            outL.Clear();
            const double hw = Player.PLAYER_W / 2;
            int used = 0;
            foreach (var b in list)
            {
                if (b == riding) continue;
                const double h = BOAT_W / 2; double top = b.pos[1] + BOAT_H;
                if (used == cpool.Count) cpool.Add(new double[6]);
                var box = cpool[used];
                box[0] = b.pos[0] - h; box[1] = b.pos[1]; box[2] = b.pos[2] - h; box[3] = b.pos[0] + h; box[4] = top; box[5] = b.pos[2] + h;
                bool over = p.pos[0] + hw > box[0] && p.pos[0] - hw < box[3] && p.pos[2] + hw > box[2] && p.pos[2] - hw < box[5] && p.pos[1] < top && p.pos[1] + p.h > b.pos[1];
                if (over)
                {
                    if (p.pos[1] < top - 0.35 || p.flying) continue;
                    p.pos[1] = top; if (p.vel[1] < 0) p.vel[1] = 0; p.onGround = true; p.fallDist = 0;
                }
                used++;
                outL.Add(box);
            }
            return outL;
        }
        // the rider sits in the middle, a little down into the hull. Like the boat, the rider is drawn
        // between the last two ticks (Minecraft moves the passenger with its vehicle every tick and the
        // camera interpolates both), so p.prev follows the boat's previous tick.
        // (seated: the boat's passenger point a third of its 0.5625 height up, the player's own riding
        // point 0.6 over its feet, so the feet sit 0.4125 under the boat's bottom: legs out in the hull)
        public void Seat()
        {
            Boat b = riding; var p = game.player;
            p.pos[0] = b.pos[0]; p.pos[1] = b.pos[1] - 0.4125; p.pos[2] = b.pos[2];
            // (a fresh array in the JS; nothing else holds the player's prev, so it is refilled in place)
            var pr = p.prev;
            if (pr == null || ReferenceEquals(pr, p.pos)) p.prev = pr = new double[3];
            pr[0] = b.prev[0]; pr[1] = b.prev[1] - 0.4125; pr[2] = b.prev[2]; p.prevOf = p.pos;
            p.eyePrev = p.eyeOffset;
            p.vel[0] = p.vel[1] = p.vel[2] = 0; p.onGround = true; p.inWater = false; p.fallDist = 0;
        }
        // the boat's turn carried over to the rider's view, spread over the frames between ticks
        // (LocalPlayer.getViewYRot interpolates a passenger's yaw): the rider's yaw takes the part of the
        // turn that is due at this point between ticks
        public void TurnView(double alpha)
        {
            Boat b = riding;
            if (b == null) return;
            double due = b.turned - b.lastTurn * (1 - alpha);
            game.player.yaw += due - turnShown;
            turnShown = due;
        }

        // ---------------------------------------------------------------- physics
        public void Update(double dt, InputKeys keys)
        {
            if (riding != null && keys != null && keys.sneak) { Leave(); return; }
            acc += dt;
            int n = 0;
            while (acc >= 0.05 && n < 10)
            {
                acc -= 0.05; n++;
                foreach (var b in list)
                {
                    // (b.prev = b.pos.slice() in the JS; the boat's own prev array is refilled)
                    b.prev[0] = b.pos[0]; b.prev[1] = b.pos[1]; b.prev[2] = b.pos[2];
                    b.pyaw = b.yaw; b.lastTurn = 0; Tick(b, b == riding ? keys : null);
                }
            }
            if (n >= 10) acc = 0;
            if (riding != null) { Seat(); TurnView(acc / 0.05); ClampView(acc / 0.05); }
        }
        // Boat.clampRotation: the rider looks at most 105 degrees to either side of the bow
        public void ClampView(double alpha)
        {
            Boat b = riding; var p = game.player; double by = b.pyaw + (b.yaw - b.pyaw) * alpha, lim = 105 * Math.PI / 180;
            double d = p.yaw - by;
            d -= Math.Floor(d / (2 * Math.PI) + 0.5) * 2 * Math.PI;
            if (d > lim) p.yaw -= d - lim; else if (d < -lim) p.yaw -= d + lim;
        }
        public void Tick(Boat b, InputKeys keys)
        {
            var w = game.world; double[] P = b.pos, V = b.vel; const double hw = BOAT_W / 2;
            if (!w.IsLoaded((int)Math.Floor(P[0]), (int)Math.Floor(P[2]))) return;
            // water around the hull: the highest surface over the cells under it, and whether it is covered
            double level = double.NegativeInfinity; bool under = false;
            int y0 = (int)Math.Floor(P[1]); double ytop = P[1] + BOAT_H; int yTopCell = (int)Math.Floor(ytop);
            for (int bx = (int)Math.Floor(P[0] - hw), bx1 = (int)Math.Floor(P[0] + hw); bx <= bx1; bx++)
                for (int bz = (int)Math.Floor(P[2] - hw), bz1 = (int)Math.Floor(P[2] + hw); bz <= bz1; bz++)
                {
                    for (int by = y0; by <= yTopCell; by++)
                    {
                        double h = w.FluidHeight(bx, by, bz);
                        if (h < 0 || !IsWaterId(w.GetBlock(bx, by, bz))) continue;
                        double s = by + h;
                        if (by >= yTopCell && s > ytop + 0.001) under = true;
                        if (s > level) level = s;
                    }
                }
            bool inWater = level > P[1];
            // Boat.getStatus, then floatBoat
            BoatStatus status = under ? BoatStatus.Under : inWater ? BoatStatus.Water : b.onGround ? BoatStatus.Land : BoatStatus.Air;
            double inv = 0.05, lift = 0, grav = -0.04;
            if (b.status == BoatStatus.Air && (status == BoatStatus.Water || status == BoatStatus.Under))
            {
                // dropping in from the air: it settles on the surface at once
                P[1] = level - BOAT_H + 0.101; V[1] = 0; b.status = BoatStatus.Water;
            }
            else
            {
                b.status = status;
                if (status == BoatStatus.Water) { inv = 0.9; lift = (level - P[1]) / BOAT_H; }
                else if (status == BoatStatus.Under) { inv = 0.45; lift = 0.01; }
                // on land the block friction under the hull (Boat.getGroundFriction; its halving for a
                // rider changes only the value read again next tick, so it has no effect)
                else if (status == BoatStatus.Land) inv = GroundFriction(b);
                else inv = 0.9;
                V[0] *= inv; V[2] *= inv; V[1] += grav;
                b.drot *= inv;
                if (lift > 0) V[1] = (V[1] + lift * 0.06153846) * 0.75;
            }
            // Boat.controlBoat
            if (keys != null)
            {
                double f = 0;
                if (keys.left) b.drot -= 1;
                if (keys.right) b.drot += 1;
                if (keys.right != keys.left && !keys.forward && !keys.back) f += 0.005;
                if (keys.forward) f += 0.04;
                if (keys.back) f -= 0.005;
                double r = b.drot * Math.PI / 180;
                b.yaw += r;
                b.turned += r; b.lastTurn = r;
                V[0] += Math.Sin(b.yaw) * f; V[2] -= Math.Cos(b.yaw) * f;
            }
            // the paddles (Boat.setPaddleState / tick): the left one rows going forward or turning right,
            // the right one going forward or turning left; a rowing paddle turns pi/8 a tick (a stroke in
            // 16 ticks) and splashes (on land scrapes) once a stroke; an idle one rests at 0
            bool on0 = keys != null && (keys.forward || (keys.right && !keys.left)), on1 = keys != null && (keys.forward || (keys.left && !keys.right));
            for (int i = 0; i < 2; i++)
            {
                bool on = i == 0 ? on0 : on1;
                b.padOn[i] = on;
                if (!on) { b.pad[i] = 0; continue; }
                double q = b.pad[i] % (2 * Math.PI);
                if (q < Math.PI / 4 && q + Math.PI / 8 >= Math.PI / 4 && b.status != BoatStatus.Air) Sfx.Paddle(b.status != BoatStatus.Land);
                b.pad[i] += Math.PI / 8;
            }
            // Entity.move: Y first, then the larger of X and Z; a blocked axis moves up to the contact and
            // its velocity is cleared (no stepping up: a boat's step height is 0)
            b.onGround = false;
            foreach (int a in Math.Abs(V[2]) > Math.Abs(V[0]) ? ORDER_ZX : ORDER_XZ)
            {
                double d = V[a];
                if (d == 0 || double.IsNaN(d)) continue;
                double o = P[a];
                P[a] = o + d;
                if (!EntityUtil.EntCollides(w, P[0], P[1], P[2], BOAT_W, BOAT_H)) continue;
                double lo = 0, hi = 1;
                for (int k = 0; k < 8; k++) { double m = (lo + hi) / 2; P[a] = o + d * m; if (EntityUtil.EntCollides(w, P[0], P[1], P[2], BOAT_W, BOAT_H)) hi = m; else lo = m; }
                P[a] = o + d * lo;
                if (a == 1 && d < 0) b.onGround = true;
                V[a] = 0;
            }
            if (b.damage > 0) b.damage = Math.Max(0, b.damage - 1);
            if (b.hurtT > 0) b.hurtT--;
        }

        // Boat.getGroundFriction: the mean friction of the blocks whose collision shape touches the thin
        // slab just under the hull (ice and packed ice 0.98, blue ice 0.989, slime 0.8, others 0.6)
        public double GroundFriction(Boat b)
        {
            var w = game.world; var P = b.pos; const double hw = BOAT_W / 2; int y = (int)Math.Floor(P[1] - 0.001);
            double f = 0; int n = 0;
            for (int bx = (int)Math.Floor(P[0] - hw), bx1 = (int)Math.Floor(P[0] + hw - 1e-7); bx <= bx1; bx++)
                for (int bz = (int)Math.Floor(P[2] - hw), bz1 = (int)Math.Floor(P[2] + hw - 1e-7); bz <= bz1; bz++)
                {
                    int id = w.GetBlock(bx, y, bz);
                    if (id == 0 || COLLIDE[id] == 0 || id == B.LILY_PAD) continue;
                    var boxes = Shapes.CollisionBoxes(w, id, w.GetMeta(bx, y, bz), bx, y, bz);
                    if (boxes == null) continue;
                    bool some = false;
                    foreach (var q in boxes)
                        if (bx + q[0] < P[0] + hw && bx + q[3] > P[0] - hw && bz + q[2] < P[2] + hw && bz + q[5] > P[2] - hw && y + q[4] >= P[1] - 0.001 && y + q[1] < P[1]) { some = true; break; }
                    if (!some) continue;
                    // (the JS also tested B.SLIME_BLOCK for 0.8; no such block exists, so that branch never ran)
                    f += id == B.ICE || id == B.PACKED_ICE ? 0.98 : id == B.BLUE_ICE ? 0.989 : 0.6; n++;
                }
            return n != 0 ? f / n : 0.6;
        }

        // ---------------------------------------------------------------- drawing
        public void Render(double[] cam, Env env)
        {
            if (list.Count == 0) return;
            var g = game; var r = g.r; double al = acc / 0.05;
            var v = vb ?? (vb = new VBuf()); var tmpB = tmp ?? (tmp = new VBuf()); var C = col ?? (col = new double[4]);
            v.n = 0; C[3] = 1;
            g.assets.layers.TryGetValue("oak_planks", out int L);
            foreach (var b in list)
            {
                double x = b.prev[0] + (b.pos[0] - b.prev[0]) * al, y = b.prev[1] + (b.pos[1] - b.prev[1]) * al, z = b.prev[2] + (b.pos[2] - b.prev[2]) * al;
                double yaw = b.pyaw + (b.yaw - b.pyaw) * al, c = Math.Cos(yaw), s = Math.Sin(yaw);
                // BoatRenderer: after a hit the boat rocks about its cross axis, 0.375 up:
                // sin(t) t damage / 10 degrees, t = ticks of the shake left
                double ht = b.hurtT - al, hd = Math.Max(0, b.damage - al);
                double rock = ht > 0 ? Math.Sin(ht) * ht * hd / 10 * (b.hurtDir != 0 ? b.hurtDir : 1) * Math.PI / 180 : 0, rc = Math.Cos(rock), rs = Math.Sin(rock);
                C[0] = C[1] = C[2] = g.LightAt(x, y + 0.5, z, env);
                tmpB.n = 0;
                foreach (var q in PARTS) g.PushBox(tmpB, q[0], q[1], q[2], q[3], q[4], q[5], L, C, 0, null, EntityUtil.ITEM_SHADE);
                for (int i = 0; i < 2; i++) Paddle(tmpB, b, i, al, L, C);
                float[] T = tmpB.a; int n = tmpB.n, o = v.n; float[] a = v.Reserve(n);
                for (int i = 0; i < n; i += 10)
                {
                    double lx = T[i], ly = T[i + 1] - 0.375, lz0 = T[i + 2];
                    double ry = ly * rc - lz0 * rs + 0.375, lz = ly * rs + lz0 * rc;
                    a[o + i] = (float)(lx * c - lz * s + x - cam[0]); a[o + i + 1] = (float)(ry + y - cam[1]); a[o + i + 2] = (float)(lx * s + lz * c + z - cam[2]);
                    for (int j = 3; j < 10; j++) a[o + i + j] = T[i + j];
                }
                v.n = o + n;
            }
            r.DrawArr(v.a, v.n / 10, env, 0.5);
        }
        // Minecraft's water mask: a depth-only patch over the inside of the hull, so the water surface
        // drawn later does not show inside the boat. It goes after the entities (RenderType.waterMask is
        // flushed after them), else it would hide what sits in the hull, such as the rider's legs
        public void RenderMask(double[] cam, Env env)
        {
            if (list.Count == 0) return;
            var g = game; var r = g.r; double al = acc / 0.05;
            // (render() made vb earlier in the frame; made here too should it ever run first)
            var v = vb ?? (vb = new VBuf());
            v.n = 0;
            g.assets.layers.TryGetValue("white", out int W);
            foreach (var b in list)
            {
                double x = b.prev[0] + (b.pos[0] - b.prev[0]) * al, y = b.prev[1] + (b.pos[1] - b.prev[1]) * al + 0.42, z = b.prev[2] + (b.pos[2] - b.prev[2]) * al;
                double yaw = b.pyaw + (b.yaw - b.pyaw) * al, c = Math.Cos(yaw), s = Math.Sin(yaw);
                // the corners P(lx, lz) = [lx c - lz s + x - cam0, y - cam1, lx s + lz c + z - cam2] of the
                // quad (-0.56, -0.8), (0.56, -0.8), (0.56, 0.8), (-0.56, 0.8), as triangles 0 1 2, 0 2 3
                foreach (int i in MASK_TRI)
                {
                    double lx = MASK_X[i], lz = MASK_Z[i];
                    v.Push(lx * c - lz * s + x - cam[0], y - cam[1], lx * s + lz * c + z - cam[2], 0, 0, W, 1, 1, 1, 1);
                }
            }
            r.ColorWrite = false; r.Cull = false;
            r.DrawArr(v.a, v.n / 10, env, 0.5);
            r.ColorWrite = true; r.Cull = true;
        }

        // a paddle in the hull's frame (own design: a square shaft through an oarlock on the side wall, a
        // flat blade at the outer end). The stroke follows Minecraft's BoatModel.animatePaddle: with the
        // rowing time t (the paddle's turn, between the last two ticks; 0 at rest) the blade dips
        // 60..15 degrees as (sin(-t) + 1) / 2 and sweeps -45..45 degrees as (sin(1 - t) + 1) / 2, so it
        // is deep while it sweeps toward the stern and comes back up toward the bow
        public void Paddle(VBuf outBuf, Boat b, int side, double al, int L, double[] C)
        {
            var g = game; var pvB = pv ?? (pv = new VBuf());
            double t = b.padOn[side] ? b.pad[side] - Math.PI / 8 * (1 - al) : 0;
            double dip = (60 - 45 * (Math.Sin(-t) + 1) / 2) * Math.PI / 180;
            double sw = (-45 + 90 * (Math.Sin(1 - t) + 1) / 2) * Math.PI / 180;
            if (side == 0) sw = -sw;
            pvB.n = 0;
            // u outward along the shaft, v up, w toward the stern
            g.PushBox(pvB, -0.3, -0.04, -0.04, 0.84, 0.04, 0.04, L, C, 0, null, EntityUtil.ITEM_SHADE);
            g.PushBox(pvB, 0.54, -0.16, -0.025, 0.98, 0.16, 0.025, L, C, 0, null, EntityUtil.ITEM_SHADE);
            double cd = Math.Cos(dip), sd = Math.Sin(dip), cs = Math.Cos(sw), ss = Math.Sin(sw); int m = side == 0 ? -1 : 1;
            float[] A = pvB.a; int n = pvB.n, o = outBuf.n; float[] a = outBuf.Reserve(n);
            for (int i = 0; i < n; i += 10)
            {
                double u = A[i], vv = A[i + 1], w = A[i + 2];
                double x = u * cd + vv * sd, y = -u * sd + vv * cd;
                double x2 = x * cs + w * ss, z2 = -x * ss + w * cs;
                // the left paddle is the right one turned half round (the boxes are even in w, so no mirroring)
                a[o + i] = (float)(m * (x2 + 0.6)); a[o + i + 1] = (float)(y + 0.47); a[o + i + 2] = (float)(m * z2 - 0.05);
                for (int j = 3; j < 10; j++) a[o + i + j] = A[i + j];
            }
            outBuf.n = o + n;
        }

        // ---------------------------------------------------------------- saving
        public double[][] Serialize()
        {
            var r = new double[list.Count][];
            for (int i = 0; i < list.Count; i++) { var b = list[i]; r[i] = new[] { b.pos[0], b.pos[1], b.pos[2], b.yaw }; }
            return r;
        }
        public void Restore(double[][] d)
        {
            list = new List<Boat>(); riding = null;
            if (d != null)
                foreach (var e in d)
                {
                    // [x, y, z, yaw] with missing entries undefined (NaN in arithmetic) and yaw || 0; a null
                    // entry (which the JS destructuring would throw on) is skipped
                    if (e == null) continue;
                    double yaw = e.Length > 3 ? e[3] : 0;
                    Place(e.Length > 0 ? e[0] : double.NaN, e.Length > 1 ? e[1] : double.NaN, e.Length > 2 ? e[2] : double.NaN, yaw == 0 || double.IsNaN(yaw) ? 0 : yaw);
                }
        }
    }
}
