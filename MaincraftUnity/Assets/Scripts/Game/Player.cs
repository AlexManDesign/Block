// Port of player.js (8613-8914): the local player's physics, a tick-based (20 Hz) port of Minecraft's
// LocalPlayer / LivingEntity movement: sprinting, swimming, crouch and crawl poses, jumping, ladders and
// vines, cobwebs, water and lava drag, ice friction, creative flight, 0.6 step-up, sneaking at ledges,
// soul sand, fall damage, eye-height easing, view bobbing and limb swing; collision against block shapes
// and the extra boxes (boats) Game sets before each tick; interpolation between the last two ticks.
//
// Teleports replace pos with a new array and set prev = copy, prevOf = pos: interp lerps only while
// prevOf is the same array object as pos (ReferenceEquals), so ships / boats / Game keep that idiom.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    // Player physics (main thread): Minecraft's movement, one step per game tick (1/20 s).
    public sealed class Player
    {
        public const double PLAYER_W = 0.6, PLAYER_H = 1.8, PLAYER_SNEAK_H = 1.5, SWIM_H = 0.6, EYE = 1.62;

        /// <summary>Feet position (array identity matters, see prevOf).</summary>
        public double[] pos = { 0.5, 100, 0.5 };
        /// <summary>Velocity in blocks per second (blocks per tick inside Tick).</summary>
        public double[] vel = { 0, 0, 0 };
        public double yaw = 0, pitch = 0;
        /// <summary>Body height: PLAYER_H, PLAYER_SNEAK_H or SWIM_H (compared with ==).</summary>
        public double h = PLAYER_H;
        public bool onGround = false, flying = false, sneaking = false, sprinting = false;
        public bool inWater = false, headInWater = false, inLava = false, onLadder = false, inWeb = false;
        public bool swimming = false;
        public double bob = 0, bobAmt = 0;          // limb swing of the player model (position, amount)
        public double walkDist = 0, bobA = 0;       // Minecraft view bobbing: distance walked x0.6, amplitude
        public bool hitWall = false;
        public int fallDamage = 0;                  // damage of this frame's landings (Game reads and resets it)
        public double fallDist = 0;
        public int noJumpDelay = 0;
        public bool crouching = false;
        public double eyeOffset = EYE;              // eased eye height over the feet

        /// <summary>Position at the start of the last tick.</summary>
        public double[] prev;
        /// <summary>The pos array prev belongs to: interp lerps only while ReferenceEquals(prevOf, pos).</summary>
        public double[] prevOf;
        public double? eyePrev, walkPrev, bobAPrev, limbPrev;   // undefined before the first tick
        public double[] rpos;                                   // drawn position (null until the first interp)
        public double? reye, rwalk, rbobA, rlimb;               // undefined until the first interp
        /// <summary>World boxes [x0,y0,z0,x1,y1,z1] of solid entities (boats), set by Game before each tick (Boats.colliders), or null.</summary>
        public List<double[]> extraBoxes;
        /// <summary>Double-tap-W sprint latch (set and cleared by Game).</summary>
        public bool sprintLatch;
        /// <summary>A jump happened this tick: 1, 2 for a sprint jump (Game reads it for exhaustion and clears it).</summary>
        public int jumped;

        // the tick's velocity in blocks per tick (the JS made a new array every tick)
        readonly double[] tv = new double[3];
        // moveAxis' probe position (the JS made a new array every sub-step)
        readonly double[] np = new double[3];

        // Math.hypot of two numbers as the browser computes it (V8: the magnitudes divided by the larger
        // one, their squares summed with Kahan compensation, sqrt times the larger one), so the
        // normalised input and the walked distance round exactly like the original
        static double Hypot(double a, double b)
        {
            if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            double x = Math.Abs(a), y = Math.Abs(b), max = x > y ? x : y;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            double n = x / max, summand = n * n - comp, pre = sum + summand;
            comp = (pre - sum) - summand; sum = pre;
            n = y / max; summand = n * n - comp; pre = sum + summand;
            sum = pre;
            return Math.Sqrt(sum) * max;
        }

        // the eye height of the pose (swimming / crawling, sneaking, standing); the camera eases toward it
        public double PoseEye() => h == SWIM_H ? 0.4 : h == PLAYER_SNEAK_H ? 1.27 : EYE;
        // eye at the drawn (interpolated) position, as Minecraft's camera and block picking use
        public double[] Eye() => Eye(new double[3]);
        // eye(), written into o (no allocation); returns o
        public double[] Eye(double[] o)
        {
            var r = rpos ?? pos;
            o[0] = r[0]; o[1] = r[1] + (reye ?? eyeOffset); o[2] = r[2];
            return o;
        }
        public double[] Look() => Look(new double[3]);
        // look(), written into o; returns o
        public double[] Look(double[] o)
        {
            double cp = Math.Cos(pitch);
            o[0] = Math.Sin(yaw) * cp; o[1] = Math.Sin(pitch); o[2] = -Math.Cos(yaw) * cp;
            return o;
        }
        // horizontal facing index: 0 S(+z), 1 W(-x), 2 N(-z), 3 E(+x)
        public int FacingDir()
        {
            // look() without the array: the same products
            double cp = Math.Cos(pitch), l0 = Math.Sin(yaw) * cp, l2 = -Math.Cos(yaw) * cp;
            if (Math.Abs(l0) > Math.Abs(l2)) return l0 > 0 ? 3 : 1;
            return l2 > 0 ? 0 : 2;
        }

        // AABB collision test against world at feet position p with height h
        public bool Collides(World world, double px, double py, double pz, double h)
        {
            const double hw = PLAYER_W / 2;
            double x0 = px - hw, x1 = px + hw, y0 = py, y1 = py + h, z0 = pz - hw, z1 = pz + hw;
            int bx0 = (int)Math.Floor(x0), bx1 = (int)Math.Floor(x1 - 1e-7), by0 = (int)Math.Floor(y0 - 0.5), by1 = (int)Math.Floor(y1 - 1e-7),
                bz0 = (int)Math.Floor(z0), bz1 = (int)Math.Floor(z1 - 1e-7);
            for (int y = by0; y <= by1; y++) for (int z = bz0; z <= bz1; z++) for (int x = bx0; x <= bx1; x++)
            {
                int id = world.GetBlock(x, y, z);
                if (id == 0 || COLLIDE[id] == 0) continue;
                var boxes = Shapes.CollisionBoxes(world, id, world.GetMeta(x, y, z), x, y, z);
                if (boxes == null) continue;
                foreach (var b in boxes)
                {
                    if (x + b[0] < x1 && x + b[3] > x0 && y + b[1] < y1 && y + b[4] > y0 && z + b[2] < z1 && z + b[5] > z0) return true;
                }
            }
            // solid entities (boats: Boat.canBeCollidedWith), as world boxes
            var E = extraBoxes;
            if (E != null) for (int i = 0; i < E.Count; i++)
            {
                var b = E[i];
                if (b[0] < x1 && b[3] > x0 && b[1] < y1 && b[4] > y0 && b[2] < z1 && b[5] > z0) return true;
            }
            return false;
        }

        public double MoveAxis(World world, int axis, double d)
        {
            if (d == 0) return 0;
            var p = pos;
            // a double like the JS number: NaN steps (a NaN move) run no step and return 0
            double steps = Math.Max(1, Math.Ceiling(Math.Abs(d) / 0.45));
            double sd = d / steps;
            double moved = 0;
            var q = np;
            for (int i = 0; i < steps; i++)
            {
                q[0] = p[0]; q[1] = p[1]; q[2] = p[2];
                q[axis] += sd;
                if (!Collides(world, q[0], q[1], q[2], h)) { p[axis] = q[axis]; moved += sd; continue; }
                // binary refine towards the obstacle
                double lo = 0, hi = 1;
                for (int k = 0; k < 8; k++)
                {
                    double mid = (lo + hi) / 2;
                    q[axis] = p[axis] + sd * mid;
                    if (Collides(world, q[0], q[1], q[2], h)) hi = mid; else lo = mid;
                }
                p[axis] += sd * lo; moved += sd * lo;
                return moved - d; // non-zero -> blocked
            }
            return 0;
        }

        int BAt(World world, double x, double y, double z) => world.GetBlock((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));

        // Entity.moveRelative: the input impulse (ix, iz), normalised when longer than 1, times speed,
        // turned by the yaw (sy, cy), added to v
        static void MoveRelative(double[] v, double ix, double iz, double sy, double cy, double speed)
        {
            double ax = ix, az = iz;
            double l2 = ax * ax + az * az;
            if (l2 < 1e-7) return;
            if (l2 > 1) { double l = Math.Sqrt(l2); ax /= l; az /= l; }
            ax *= speed; az *= speed;
            v[0] += -az * sy + ax * cy; v[2] += az * cy + ax * sy;
        }

        void FluidFall(double[] v, bool falling)
        {
            if (sprinting) return;
            v[1] = falling && Math.Abs(v[1] - 0.005) >= 0.003 && Math.Abs(v[1] - 0.005) < 0.003 ? -0.003 : v[1] - 0.005;
        }

        // One game tick (1/20 s) of Minecraft's player movement, following LocalPlayer.aiStep,
        // LivingEntity.aiStep / travel / move and Player.travel. Inside, velocity is in blocks per tick
        // as in Minecraft; this.vel keeps blocks per second for the rest of the game. The camera and the
        // model are drawn between the last two ticks (interp).
        public void Tick(World world, PlayerInput input, GameMode mode)
        {
            var p = pos;
            // prev is never shared (every writer gives it a fresh array), so it is refilled in place
            var pr = prev;
            if (pr == null || ReferenceEquals(pr, p)) prev = pr = new double[3];
            pr[0] = p[0]; pr[1] = p[1]; pr[2] = p[2]; prevOf = p;
            eyePrev = eyeOffset; walkPrev = walkDist; bobAPrev = bobA; limbPrev = bob;
            var v = tv;
            v[0] = vel[0] / 20; v[1] = vel[1] / 20; v[2] = vel[2] / 20;
            var k = input.keys;
            // ---- input (LocalPlayer): forward / strafe impulses
            double fx = 0, fz = 0;
            if (k.forward) fz -= 1; if (k.back) fz += 1; if (k.left) fx -= 1; if (k.right) fx += 1;
            if (input.joy != null) { fx += input.joy[0]; fz += input.joy[1]; }
            double len = Hypot(fx, fz);
            if (len > 1) { fx /= len; fz /= len; }
            // LocalPlayer.aiStep: using an item (eating, drawing back a trident) slows the walk to a fifth
            if (input.@using) { fx *= 0.2; fz *= 0.2; }
            bool jump = k.jump, shift = k.sneak;
            // ---- fluids and blocks around the body
            var fluid = FluidDepth(world);
            inWater = fluid.water > 0; inLava = fluid.lava > 0;
            // Entity.updateFluidOnEyes: the point 1/9 below the eye is under the water surface of its cell
            double eyY = p[1] + eyeOffset - 1.0 / 9; int eyB = (int)Math.Floor(eyY);
            headInWater = IsWaterId(BAt(world, p[0], eyY, p[2])) && eyB + world.FluidHeight((int)Math.Floor(p[0]), eyB, (int)Math.Floor(p[2])) > eyY;
            int feet = BAt(world, p[0], p[1], p[2]);
            // LivingEntity.onClimbable: ladders, vines, and an open trapdoor right over a ladder facing the
            // same way (trapdoorUsableAsLadder), so a hatch over a ladder can be climbed through
            bool trapLadder = false;
            if (SHAPE[feet] == SH.TRAPDOOR)
            {
                int tx = (int)Math.Floor(p[0]), ty = (int)Math.Floor(p[1]), tz = (int)Math.Floor(p[2]), tm = world.GetMeta(tx, ty, tz);
                trapLadder = (tm & 8) != 0 && SHAPE[world.GetBlock(tx, ty - 1, tz)] == SH.LADDER && (world.GetMeta(tx, ty - 1, tz) & 3) == (tm & 3);
            }
            onLadder = !flying && ((FLAGS[feet] & BF_CLIMB) != 0 || SHAPE[feet] == SH.LADDER || SHAPE[feet] == SH.VINE || trapLadder);
            inWeb = !flying && (feet == B.COBWEB || BAt(world, p[0], p[1] + 1.1, p[2]) == B.COBWEB);
            // ---- sprinting and swimming (LocalPlayer.aiStep / Player.updateSwimming)
            bool forward = -fz >= 0.8;
            // (the JS also allowed sprinting when input.hunger was undefined; Game.Update sets it from
            // surv.food before every tick, so it never is)
            bool canSprint = mode != GameMode.Survival || flying || input.hunger > 6;
            if (!sprinting && forward && canSprint && (k.sprint || sprintLatch) && !crouching && (!inWater || headInWater)) sprinting = true;
            if (sprinting && (!forward || !canSprint || (hitWall && !swimming) || (inWater && !headInWater && !swimming))) sprinting = false;
            swimming = !flying && sprinting && (swimming ? inWater : headInWater);
            if (swimming && !inWater) swimming = false;
            // pose: swimming 0.6 high, crouching 1.5, standing 1.8 (when there is room)
            crouching = shift && !flying && !swimming;
            double wantH = swimming ? SWIM_H : crouching ? PLAYER_SNEAK_H : PLAYER_H;
            if (wantH < h || !Collides(world, p[0], p[1], p[2], wantH)) h = wantH;
            else if (h < PLAYER_SNEAK_H && wantH > PLAYER_SNEAK_H && !Collides(world, p[0], p[1], p[2], PLAYER_SNEAK_H)) h = PLAYER_SNEAK_H;
            sneaking = h == PLAYER_SNEAK_H && !flying && onGround;
            // impulses: x0.3 while crouching (or squeezed into the crawl pose), x0.98 always
            double slow = crouching || (h < PLAYER_SNEAK_H && !swimming) ? 0.3 : 1;
            double ix = fx * slow * 0.98, iz = fz * slow * 0.98;
            // creative flight: jump / shift move up and down by 3 x flying speed per tick
            if (flying) v[1] += ((jump ? 1 : 0) - (shift ? 1 : 0)) * 0.15;
            // ---- LivingEntity.aiStep
            if (noJumpDelay > 0) noJumpDelay--;
            for (int i = 0; i < 3; i++) if (Math.Abs(v[i]) < 0.003) v[i] = 0;
            if (jump && !flying)
            {
                double depth = Math.Max(fluid.water, fluid.lava), thr = eyeOffset < 0.4 ? 0 : 0.4;
                if (depth > 0 && !(onGround && depth <= thr)) v[1] += 0.04;            // swim up
                else if (onGround && noJumpDelay == 0)
                {
                    v[1] = 0.42;
                    if (sprinting) { v[0] += Math.Sin(yaw) * 0.2; v[2] -= Math.Cos(yaw) * 0.2; }
                    noJumpDelay = 10;
                    jumped = sprinting ? 2 : 1;      // for the food exhaustion (Player.jumpFromGround)
                }
            }
            else noJumpDelay = 0;
            // ---- travel
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw);
            if (swimming)
            {
                // Player.travel: swimming steers vertically toward the look direction
                double ly = Math.Sin(pitch), d4 = ly < -0.2 ? 0.085 : 0.06;
                if (ly <= 0 || jump || IsWaterId(BAt(world, p[0], p[1] + 0.9, p[2]))) v[1] += (ly - v[1]) * d4;
            }
            double vy0 = v[1]; bool falling = v[1] <= 0, wasGround = onGround;
            if (inWater && !flying)
            {
                double f = sprinting ? 0.9 : 0.8;
                MoveRelative(v, ix, iz, sy, cy, 0.02);
                double y0 = p[1];
                Move(world, v, shift, wasGround);
                if (hitWall && onLadder) v[1] = 0.2;
                v[0] *= f; v[1] *= 0.8; v[2] *= f;
                FluidFall(v, falling);
                if (hitWall && IsFree(world, p[0] + v[0], p[1] + v[1] + 0.6 - p[1] + y0, p[2] + v[2])) v[1] = 0.3;
            }
            else if (inLava && !flying)
            {
                MoveRelative(v, ix, iz, sy, cy, 0.02);
                double y0 = p[1];
                Move(world, v, shift, wasGround);
                if (fluid.lava <= 0.4) { v[0] *= 0.5; v[1] *= 0.8; v[2] *= 0.5; FluidFall(v, falling); }
                else { v[0] *= 0.5; v[1] *= 0.5; v[2] *= 0.5; }
                v[1] -= 0.02;
                if (hitWall && IsFree(world, p[0] + v[0], p[1] + v[1] + 0.6 - p[1] + y0, p[2] + v[2])) v[1] = 0.3;
            }
            else
            {
                int below = BAt(world, p[0], p[1] - 0.5, p[2]);
                // (the JS also tested B.SLIME_BLOCK for 0.8; no such block exists, so that branch never ran)
                double bf = below == B.ICE || below == B.PACKED_ICE ? 0.98 : below == B.BLUE_ICE ? 0.989 : 0.6;
                double f3 = onGround ? bf * 0.91 : 0.91;
                double bas = sprinting ? 0.13 : 0.1;
                double air = flying ? (sprinting ? 0.1 : 0.05) : sprinting ? 0.026 : 0.02;
                MoveRelative(v, ix, iz, sy, cy, onGround ? bas * (0.216 / (bf * bf * bf)) : air);
                if (onLadder)
                {
                    // LivingEntity.handleOnClimbable
                    v[0] = Math.Max(-0.15, Math.Min(0.15, v[0])); v[2] = Math.Max(-0.15, Math.Min(0.15, v[2]));
                    v[1] = Math.Max(v[1], -0.15);
                    if (v[1] < 0 && shift) v[1] = 0;
                    fallDist = 0;
                }
                Move(world, v, shift, wasGround);
                if ((hitWall || jump) && onLadder) v[1] = 0.2;
                if (!flying) v[1] -= 0.08;
                v[0] *= f3; v[1] *= 0.98; v[2] *= f3;
            }
            if (flying) v[1] = vy0 * 0.6;
            // creative flight ends on touching the ground
            if (flying && onGround) flying = false;
            // ---- fall damage (Minecraft: ceil(fall distance - 3) on landing)
            if (flying || inWater || onLadder || inWeb) fallDist = 0;
            else if (onGround) { if (fallDist > 3) fallDamage += (int)Math.Ceiling(fallDist - 3); fallDist = 0; }
            else if (p[1] < prev[1]) fallDist += prev[1] - p[1];
            vel[0] = v[0] * 20; vel[1] = v[1] * 20; vel[2] = v[2] * 20;
            // ---- camera eye height eases toward the pose (Camera: 0.5 per tick); view bobbing
            eyeOffset += (PoseEye() - eyeOffset) * 0.5;
            double moved = Hypot(p[0] - prev[0], p[2] - prev[2]);
            walkDist += moved * 0.6;
            bobA += ((onGround && !swimming ? Math.Min(0.1, moved) : 0) - bobA) * 0.4;
            // limb swing of the player model (LivingEntity.calculateEntityAnimation)
            bobAmt += (Math.Min(1, moved * 4) - bobAmt) * 0.4;
            bob += bobAmt;
            if (p[1] < WORLD_MIN_Y - 64) { p[1] = WORLD_MIN_Y - 64; vel[1] = 0; }
        }

        // Fluid heights at the body (Minecraft's updateFluidHeightAndDoFluidPushing, along the centre
        // column): how deep the feet are in water / lava, 8/9 of a block for a full source block.
        public (double water, double lava) FluidDepth(World world)
        {
            var p = pos; int x = (int)Math.Floor(p[0]), z = (int)Math.Floor(p[2]);
            double water = 0, lava = 0;
            for (int y = (int)Math.Floor(p[1]), yEnd = (int)Math.Floor(p[1] + h - 0.001); y <= yEnd; y++)
            {
                int id = world.GetBlock(x, y, z); bool w = IsWaterId(id), l = id == B.LAVA;
                if (!w && !l) continue;
                double top = 8.0 / 9;
                if ((FLAGS[id] & (BF_AQUATIC | BF_WET)) == 0) { int m = world.GetMeta(x, y, z); if ((m & 8) == 0) top = (8 - (m & 7)) / 9.0; }
                int up = world.GetBlock(x, y + 1, z);
                if ((w && IsWaterId(up)) || (l && up == B.LAVA)) top = 1;
                double d = y + top - p[1];
                if (d <= 0) continue;
                if (w) water = Math.Max(water, d); else lava = Math.Max(lava, d);
            }
            return (water, lava);
        }
        public bool CollidesAt(World world, double x, double y, double z) => Collides(world, x, y, z, h);
        // Entity.isFree: no collision and no fluid of any height in the cells the box touches
        // (Level.containsAnyLiquid)
        public bool IsFree(World world, double px, double py, double pz)
        {
            if (Collides(world, px, py, pz, h)) return false;
            const double hw = PLAYER_W / 2;
            int y0 = (int)Math.Floor(py), y1 = (int)Math.Ceiling(py + h), z0 = (int)Math.Floor(pz - hw), z1 = (int)Math.Ceiling(pz + hw),
                x0 = (int)Math.Floor(px - hw), x1 = (int)Math.Ceiling(px + hw);
            for (int y = y0; y < y1; y++) for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++) if (IsLiquidId(world.GetBlock(x, y, z))) return false;
            return true;
        }

        // sneaking: the body at p + (ox, -0.6, oz) still stands on something
        bool Sup(World world, double ox, double oz) { var p = pos; return Collides(world, p[0] + ox, p[1] - 0.6, p[2] + oz, h); }
        // one sneak step (0.05) toward 0
        static double Dec(double d) => Math.Abs(d) <= 0.05 ? 0 : d - (d > 0 ? 1 : -1) * 0.05;

        // Entity.move: cobweb slow-down, sneaking stops at edges, collision Y first then X and Z, step
        // up to 0.6 blocks, collided velocity components cleared, soul sand speed factor.
        public void Move(World world, double[] v, bool shift, bool wasGround)
        {
            var p = pos;
            double dx = v[0], dy = v[1], dz = v[2];
            if (inWeb) { dx *= 0.25; dy *= 0.05; dz *= 0.25; v[0] = v[1] = v[2] = 0; fallDist = 0; }
            if (shift && !flying && dy <= 0 && wasGround)
            {
                while (dx != 0 && !Sup(world, dx, 0)) dx = Dec(dx);
                while (dz != 0 && !Sup(world, 0, dz)) dz = Dec(dz);
                while (dx != 0 && dz != 0 && !Sup(world, dx, dz)) { dx = Dec(dx); dz = Dec(dz); }
            }
            hitWall = false;
            double restY = MoveAxis(world, 1, dy);
            onGround = restY != 0 && dy < 0;
            if (restY != 0) v[1] = 0;
            // the larger horizontal move first (x on a tie)
            bool zFirst = Math.Abs(dz) > Math.Abs(dx);
            for (int o = 0; o < 2; o++)
            {
                int ax = (o == 0) == zFirst ? 2 : 0;
                double d = ax == 2 ? dz : dx;
                if (d == 0 || double.IsNaN(d)) continue;
                double rest = MoveAxis(world, ax, d);
                if (rest == 0) continue;
                // step up (maxUpStep 0.6) when standing or landing
                if (!flying && (onGround || wasGround))
                {
                    double tx = p[0], ty = p[1] + 0.6, tz = p[2];
                    if (ax == 0) tx += rest * -1; else tz += rest * -1;
                    if (!Collides(world, p[0], p[1] + 0.6, p[2], h) && !Collides(world, tx, ty, tz, h))
                    {
                        double y = ty;
                        while (y - 0.01 > p[1] && !Collides(world, tx, y - 0.01, tz, h)) y -= 0.01;
                        p[ax] = ax == 0 ? tx : tz; p[1] = y; onGround = true;
                        continue;
                    }
                }
                v[ax] = 0; hitWall = true;
            }
            if (onGround && !flying)
            {
                int b = world.GetBlock((int)Math.Floor(p[0]), (int)Math.Floor(p[1] - 0.5), (int)Math.Floor(p[2]));
                if (b == B.SOUL_SAND) { v[0] *= 0.4; v[2] *= 0.4; }
            }
        }

        // drawn position between the last two ticks (alpha 0..1); a teleport (pos replaced) snaps
        public void Interp(double alpha)
        {
            var p = pos; var q = ReferenceEquals(prevOf, p) ? prev : null; var r = rpos ?? (rpos = new double[3]);
            for (int i = 0; i < 3; i++) r[i] = q != null ? q[i] + (p[i] - q[i]) * alpha : p[i];
            reye = L(eyePrev, eyeOffset, q != null, alpha);
            rwalk = L(walkPrev, walkDist, q != null, alpha);
            rbobA = L(bobAPrev, bobA, q != null, alpha);
            rlimb = L(limbPrev, bob, q != null, alpha);
        }
        static double L(double? a, double b, bool hasPrev, double alpha) => a == null || !hasPrev ? b : a.Value + (b - a.Value) * alpha;
    }
}
