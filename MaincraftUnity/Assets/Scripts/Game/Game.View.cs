// Port of game.js (14450-15205): respawn placement, random ticks, furnaces, block particles, the frame's
// rendering (world, vessels, boats, clouds, particles, entities, water, selection and cracks, the hand,
// the in-wall and fire overlays), the shared box / model-box / sprite-model builders, the HUD and debug
// texts, the performance report, input and dropping the held stack.
//
// bindInput: the DOM handlers become BindInput (one-time setup) and PollInput (the Unity input polling
// entry point, called first thing in Loop every frame, which does what the handlers did, in their order);
// OnBlur and OnPageHide are the window blur and pagehide / visibilitychange handlers, called by the
// MonoBehaviour GameMain (OnApplicationFocus(false) / OnApplicationPause(true), OnApplicationQuit).
//
// API skeleton: bodies marked TODO(game-c) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;
using Cursor = UnityEngine.Cursor;

namespace Maincraft
{
    public sealed partial class Game
    {
        // game.js placeRespawn (14456-14484): settles this.respawning; false while its ground is not loaded
        public bool PlaceRespawn() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js randomTicks (14486-14509): 9x9 columns, 2 xorshift picks per section
        public void RandomTicks() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js randomTick (14510-14552): by RT.kind
        public void RandomTick(int x, int y, int z, int id) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js tickFurnaces (14554-14577): every record of meta.furnaces (burnMax = burn = FuelValue(id) / 8.0)
        public void TickFurnaces(double dt) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js particleLayer (14584-14588)
        public int ParticleLayer(int id) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js addParticle (14589-14598): appends to this.particles and returns it
        public Particle AddParticle(double x, double y, double z, double vx, double vy, double vz, int layer) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js destroyParticles (14600-14608)
        public void DestroyParticles(int x, int y, int z, int id) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js crackParticle (14610-14621)
        public void CrackParticle(int x, int y, int z, int id, int face) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js spawnParticles (14623-14628): n loose particles in the unit cube from (x, y, z)
        public void SpawnParticles(double x, double y, double z, int id, int n) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js trimParticles (14629): at most 4000, the oldest go
        public void TrimParticles() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js updateParticles (14630-14634)
        public void UpdateParticles(double dt) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js tickParticles (14635-14653)
        public void TickParticles() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js lightAt (14656-14661): brightness 0.05..1 at a point (boats and entities use it too)
        public double LightAt(double x, double y, double z, Env env) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js renderFrame (14662-14706): the frame's passes in the original's order (BeginFrame / EndFrame around them)
        public void RenderFrame(double dt) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawInWall (14709-14726)
        public void DrawInWall() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawFire (14730-14750)
        public void DrawFire() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawSelection (14751-14780): outline and crack overlay of the target
        public void DrawSelection(Env env) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawMobCracks (14782-14797): ents.cracks
        public void DrawMobCracks() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js pushBox (14799-14812): a textured box (camera-relative), 6 faces x 6 vertices of
        // [x, y, z, u, v, layer, r, g, b, a] into v. col: rgba; faceLayers / shades / uvFromBox (6 x 8 uv) optional
        public void PushBox(VBuf v, double x0, double y0, double z0, double x1, double y1, double z1, int layer, double[] col,
            double grow = 0, ushort[] faceLayers = null, double[] shades = null, double[][] uvFromBox = null) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawParticles (14813-14836)
        public void DrawParticles(Env env) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js viewPose (14841-14848): hurt tilt and view bob as a pose, written into M and returned
        public double[] ViewPose(double[] M) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js drawHand (14853-14968): the first-person hand / held item
        public void DrawHand(Env env, double dt) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js spriteModel (14971-14997): extruded item sprite of an atlas layer, 6 floats a vertex
        // (x, y, z, u, v, shade), cached per layer in spriteCache
        public float[] SpriteModel(int layer) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js pushModelBox (15003-15030): MC model box with the skin unwrap; M(MP, x, y, z, o) maps
        // local points to camera-relative ones (Game.PmApplyFn or EntityUtil.ModelXformFn)
        public void PushModelBox(VBuf outBuf, PoseFn M, double[] MP, double x0, double y0, double z0, double x1, double y1, double z1,
            double u, double v, double w, double h, double d, double tw, double th, double light, double[] tint = null, bool mirror = false) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js updateHotbar (15034-15038): resets atkT when the held id changes, then UI.UpdateHotbar
        public void UpdateHotbar() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js updateAttackBar (15040-15048): the #atk indicator
        public void UpdateAttackBar() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js updateSurvivalHud (15049)
        public void UpdateSurvivalHud() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js updateDebug (15050-15082): #biomeInfo and the F3 text
        public void UpdateDebug() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js perfReport (15085-15117)
        public string PerfReport() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js copyPerfReport (15118-15126)
        public void CopyPerfReport() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js copyFallback (15127-15133)
        public void CopyFallback(string text) { throw new NotImplementedException(); } // TODO(game-c)
        // game.js bindInput (15136-15193): one-time input setup (called by the constructor)
        public void BindInput() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js bindInput handlers (15138-15188): per-frame polling of mouse buttons, pointer lock, look,
        // wheel and keys (KeyCode -> KeyboardEvent.code names in this.keys); first thing in Loop. A press while
        // the cursor is unlocked counts as the canvas mousedown only when !UI.PointerOverUI(Input.mousePosition);
        // the keydown handling is skipped while UI.TextInputFocused (keyup still releases)
        public void PollInput() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js bindInput blur handler (15190): keys and buttons released
        public void OnBlur() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js bindInput pagehide / visibilitychange handlers (15191-15192): saveWorld(false) while playing
        public void OnPageHide() { throw new NotImplementedException(); } // TODO(game-c)
        // game.js dropHeld (15194-15203): Q
        public void DropHeld() { throw new NotImplementedException(); } // TODO(game-c)
    }
}
