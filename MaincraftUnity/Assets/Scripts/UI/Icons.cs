// Port of ui.js Icons (12562-12649): a lazy per-id cache of 64x64 inventory icons rendered on the CPU
// from the atlas pixels: a flat sprite (items, plants, torches, ladders, vines, rails, fire, doors as two
// halves, panes, fluids) scaled to 56x56 at (4, 4), or a fake-isometric cube of three affine-mapped atlas
// tiles shaded 1.0 / 0.8 / 0.62 whose height and inset depend on the shape. The canvas 2D calls become
// a software rasterizer into a Color32[64 * 64] work buffer (inverse affine, nearest sampling,
// source-over blending; the canvas clips to its 64x64 area), uploaded into a point-filtered Texture2D
// (rows flipped: Texture2D rows go bottom-up).
//
// API skeleton: bodies marked TODO(ui) are filled in by that implementer.
using System;
using System.Collections.Generic;
using UnityEngine;
using static Maincraft.Blocks;

namespace Maincraft
{
    public static class Icons
    {
        /// <summary>Rendered icons by id (the JS cached PNG data URLs).</summary>
        public static readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
        /// <summary>The 64x64 work canvas (cv / ctx), RGBA, top row first.</summary>
        public static Color32[] cv;
        /// <summary>The 16x16 tile canvas (tile / tctx; layerCanvas's result, reused), RGBA, top row first.</summary>
        public static Color32[] tile;
        /// <summary>assets.atlasPixels: the CPU atlas (Renderer.atlasPixels), RGBA, top row first.</summary>
        public static byte[] atlasPixels;
        /// <summary>assets.atlasImage.width (Renderer.atlasW).</summary>
        public static int atlasW;
        /// <summary>assets.cols: tiles per atlas row.</summary>
        public static int cols;
        /// <summary>assets.layers: texture name -> atlas layer.</summary>
        public static Dictionary<string, int> layers;

        // ui.js Icons.init (12566-12572)
        public static void Init(GameAssets assets) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js Icons.layerCanvas (12573-12584): atlas tile 'layer' with RGB times shade (clamped and rounded
        // like a Uint8ClampedArray), alpha copied; into tile (returned)
        public static Color32[] LayerCanvas(int layer, double shade) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js Icons.get (12585-12591): cached Render(id)
        public static Texture2D Get(int id) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js Icons.render (12592-12648): draws the icon into cv and returns it as a new texture
        public static Texture2D Render(int id) { throw new NotImplementedException(); } // TODO(ui)
    }
}
