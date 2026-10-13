// Port of player.js (8613-8914): the local player's physics, a tick-based (20 Hz) port of Minecraft's
// LocalPlayer / LivingEntity movement: sprinting, swimming, crouch and crawl poses, jumping, ladders and
// vines, cobwebs, water and lava drag, ice friction, creative flight, 0.6 step-up, sneaking at ledges,
// soul sand, fall damage, eye-height easing, view bobbing and limb swing; collision against block shapes
// and the extra boxes (boats) Game sets before each tick; interpolation between the last two ticks.
//
// Teleports replace pos with a new array and set prev = copy, prevOf = pos: interp lerps only while
// prevOf is the same array object as pos (ReferenceEquals), so ships / boats / Game keep that idiom.
//
// API skeleton: bodies marked TODO(player) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
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

        // player.js poseEye (8635): eye height of the pose (exact compares on h)
        public double PoseEye() { throw new NotImplementedException(); } // TODO(player)
        // player.js eye (8637): the eye at the drawn position, rpos ?? pos plus reye ?? eyeOffset (a new array)
        public double[] Eye() { throw new NotImplementedException(); } // TODO(player)
        // player.js eye (8637), written into o (no allocation); returns o
        public double[] Eye(double[] o) { throw new NotImplementedException(); } // TODO(player)
        // player.js look (8638-8641): unit look vector (a new array)
        public double[] Look() { throw new NotImplementedException(); } // TODO(player)
        // player.js look (8638-8641), written into o; returns o
        public double[] Look(double[] o) { throw new NotImplementedException(); } // TODO(player)
        // player.js facingDir (8643-8647): 0 S(+z), 1 W(-x), 2 N(-z), 3 E(+x)
        public int FacingDir() { throw new NotImplementedException(); } // TODO(player)
        // player.js collides (8650-8670): the body box (feet at p, height h) against block shapes and extraBoxes
        public bool Collides(World world, double px, double py, double pz, double h) { throw new NotImplementedException(); } // TODO(player)
        // player.js moveAxis (8672-8693): moves pos along an axis; non-zero (minus the rest) when blocked
        public double MoveAxis(World world, int axis, double d) { throw new NotImplementedException(); } // TODO(player)
        // player.js tick (8699-8835): one Minecraft movement tick
        public void Tick(World world, PlayerInput input, GameMode mode) { throw new NotImplementedException(); } // TODO(player)
        // player.js fluidDepth (8838-8853): depth of water and lava above the feet along the centre column
        public (double water, double lava) FluidDepth(World world) { throw new NotImplementedException(); } // TODO(player)
        // player.js collidesAt (8854)
        public bool CollidesAt(World world, double x, double y, double z) { throw new NotImplementedException(); } // TODO(player)
        // player.js isFree (8857-8863): Entity.isFree
        public bool IsFree(World world, double px, double py, double pz) { throw new NotImplementedException(); } // TODO(player)
        // player.js move (8866-8902): Entity.move; v in blocks per tick, mutated
        public void Move(World world, double[] v, bool shift, bool wasGround) { throw new NotImplementedException(); } // TODO(player)
        // player.js interp (8904-8912): drawn values between the last two ticks (alpha 0..1)
        public void Interp(double alpha) { throw new NotImplementedException(); } // TODO(player)
    }
}
