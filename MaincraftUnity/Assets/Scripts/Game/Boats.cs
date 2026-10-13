// Port of boats.js (8271-8612): Minecraft-style boats. Placed from the item on water (or ground), boarded
// with the use key, steered with W / A / S / D, left with Shift, broken by hitting (drops the item in
// survival). Per game tick (20/s) a boat floats by its depth in water against gravity, takes water / ice /
// ground friction, the rider's turn and push, rows its two paddles, and moves axis by axis with
// bisection. Boats are solid boxes for the player (colliders) and are drawn as CPU-transformed boxes
// plus a depth-only water mask inside each hull.
//
// API skeleton: bodies marked TODO(player) are filled in by that implementer.
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

        // boats.js constructor (8284-8289)
        public Boats(Game game) { throw new NotImplementedException(); } // TODO(player)
        // boats.js place (8290-8295): a new boat appended to the list
        public Boat Place(double x, double y, double z, double yaw) { throw new NotImplementedException(); } // TODO(player)
        // boats.js raycast (8297-8313): nearest boat box on the ray (eye, unit dir) within range, skipping skip; null if none
        // (game.js useEntity 13747 passes no skip: undefined matches no boat)
        public BoatHit? Raycast(double[] eye, double[] dir, double range, Boat skip = null) { throw new NotImplementedException(); } // TODO(player)
        // boats.js hit (8317-8328): Boat.hurt
        public void Hit(Boat b, double dmg) { throw new NotImplementedException(); } // TODO(player)
        // boats.js board (8329-8335)
        public void Board(Boat b) { throw new NotImplementedException(); } // TODO(player)
        // boats.js leave (8339-8359): Boat.getDismountLocationForPassenger
        public void Leave() { throw new NotImplementedException(); } // TODO(player)
        // boats.js floorAt (8363-8369): standing height in cell y, or null
        public double? FloorAt(int x, int y, int z) { throw new NotImplementedException(); } // TODO(player)
        // boats.js colliders (8373-8389): boxes of the boats not ridden for the player's collisions (reused
        // list); lifts a player sunk a little into a boat top onto it
        public List<double[]> Colliders(Player p) { throw new NotImplementedException(); } // TODO(player)
        // boats.js seat (8395-8401)
        public void Seat() { throw new NotImplementedException(); } // TODO(player)
        // boats.js turnView (8405-8411)
        public void TurnView(double alpha) { throw new NotImplementedException(); } // TODO(player)
        // boats.js update (8414-8424): 20 Hz ticks; keys null when the player does not ride
        public void Update(double dt, InputKeys keys) { throw new NotImplementedException(); } // TODO(player)
        // boats.js clampView (8426-8431): Boat.clampRotation
        public void ClampView(double alpha) { throw new NotImplementedException(); } // TODO(player)
        // boats.js tick (8432-8507): one game tick of a boat; keys null unless ridden
        public void Tick(Boat b, InputKeys keys) { throw new NotImplementedException(); } // TODO(player)
        // boats.js groundFriction (8511-8522): Boat.getGroundFriction
        public double GroundFriction(Boat b) { throw new NotImplementedException(); } // TODO(player)
        // boats.js render (8525-8555)
        public void Render(double[] cam, Env env) { throw new NotImplementedException(); } // TODO(player)
        // boats.js renderMask (8559-8575): depth-only quads over the hulls (ColorWrite off, Cull off)
        public void RenderMask(double[] cam, Env env) { throw new NotImplementedException(); } // TODO(player)
        // boats.js paddle (8582-8603): one paddle into outBuf, in the hull's frame
        public void Paddle(VBuf outBuf, Boat b, int side, double al, int L, double[] C) { throw new NotImplementedException(); } // TODO(player)
        // boats.js serialize (8606): [[x, y, z, yaw], ...]
        public double[][] Serialize() { throw new NotImplementedException(); } // TODO(player)
        // boats.js restore (8607-8610)
        public void Restore(double[][] d) { throw new NotImplementedException(); } // TODO(player)
    }
}
