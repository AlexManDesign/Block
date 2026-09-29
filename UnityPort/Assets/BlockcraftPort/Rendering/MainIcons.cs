using System;
using System.Collections.Generic;
#if !PARITY_HARNESS
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace BlockcraftPort
{
    /// <summary>
    /// main.js icon painters over the reference block atlas, as pure pixel code (RGBA, top-down rows)
    /// so the parity harness can compare them with the reference canvas output:
    ///   WC(tile)  : the 16x16 tile (items, cross plants, beds);
    ///   XR(id)    : 64x64 isometric cube from top + (front ?? side) with 35% / 18% side darkening;
    ///   WR(id)/R3 : 48x48 flat silhouettes for shaped blocks (slab, stairs, fence, door, ...).
    /// </summary>
    public static class MainIconPainter
    {
        public const int Tile = SourceBlockData.AtlasTile, Cols = SourceBlockData.AtlasColumns;

        /// <summary>Reference atlas pixels, RGBA top-down (row 0 = top of the PNG).</summary>
        public static byte[] Atlas; public static int AtlasW, AtlasH;

        static void Texel(int tile, int u, int v, out byte r, out byte g, out byte b, out byte a)
        {
            int px = (tile % Cols) * Tile + u, py = (tile / Cols) * Tile + v;
            if ((uint)px >= (uint)AtlasW || (uint)py >= (uint)AtlasH) { r = g = b = a = 0; return; }
            int i = (py * AtlasW + px) * 4;
            r = Atlas[i]; g = Atlas[i + 1]; b = Atlas[i + 2]; a = Atlas[i + 3];
        }

        /// <summary>A 2D canvas with imageSmoothingEnabled = false.</summary>
        public sealed class Canvas
        {
            public readonly int S; public readonly byte[] Px;
            public Canvas(int s) { S = s; Px = new byte[s * s * 4]; }
            void Over(int x, int y, byte r, byte g, byte b, byte a)
            {
                if (a == 0 || (uint)x >= (uint)S || (uint)y >= (uint)S) return;
                int i = (y * S + x) * 4;
                if (a == 255) { Px[i] = r; Px[i + 1] = g; Px[i + 2] = b; Px[i + 3] = 255; return; }
                float sa = a / 255f, da = Px[i + 3] / 255f, oa = sa + da * (1f - sa);
                if (oa <= 0f) return;
                Px[i] = (byte)Math.Round((r * sa + Px[i] * da * (1f - sa)) / oa);
                Px[i + 1] = (byte)Math.Round((g * sa + Px[i + 1] * da * (1f - sa)) / oa);
                Px[i + 2] = (byte)Math.Round((b * sa + Px[i + 2] * da * (1f - sa)) / oa);
                Px[i + 3] = (byte)Math.Round(oa * 255f);
            }
            /// <summary>drawImage(atlas, tile, dx, dy, dw, dh).</summary>
            public void Draw(int tile, float dx, float dy, float dw, float dh)
            {
                if (tile < 0 || dw <= 0f || dh <= 0f) return;
                DrawAffine(tile, dw / Tile, 0f, 0f, dh / Tile, dx, dy, 0f);
            }

            /// <summary>
            /// setTransform(a,b,c,d,e,f); drawImage(tile,0,0,16,16); then a source-atop black fill of
            /// alpha <paramref name="darken"/> over the same quad. Like the browser canvas: texels are
            /// picked nearest-neighbour with 16.16 fixed-point inverse mapping, and the quad's edges
            /// are anti-aliased by pixel coverage (imageSmoothingEnabled only turns off filtering).
            /// </summary>
            public void DrawAffine(int tile, float a, float b, float c, float d, float e, float f, float darken)
            {
                double det = (double)a * d - (double)b * c; if (Math.Abs(det) < 1e-9 || tile < 0) return;
                double ia = d / det, ib = -b / det, ic = -c / det, id = a / det;
                long fia = (long)(ia * 65536.0), fib = (long)(ib * 65536.0), fic = (long)(ic * 65536.0), fid = (long)(id * 65536.0);
                // Device-space bounds of the quad.
                double[] qx = { e, a * Tile + e, c * Tile + e, a * Tile + c * Tile + e };
                double[] qy = { f, b * Tile + f, d * Tile + f, b * Tile + d * Tile + f };
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(Math.Min(qx[0], qx[1]), Math.Min(qx[2], qx[3]))));
                int x1 = Math.Min(S, (int)Math.Ceiling(Math.Max(Math.Max(qx[0], qx[1]), Math.Max(qx[2], qx[3]))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(Math.Min(qy[0], qy[1]), Math.Min(qy[2], qy[3]))));
                int y1 = Math.Min(S, (int)Math.Ceiling(Math.Max(Math.Max(qy[0], qy[1]), Math.Max(qy[2], qy[3]))));
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float cov = Coverage(x, y, ia, ib, ic, id, e, f);
                        if (cov <= 0f) continue;
                        // Texel lookup at the pixel centre in 16.16 fixed point (clamped for edge pixels).
                        long X = (long)((x + .5 - e) * 65536.0), Y = (long)((y + .5 - f) * 65536.0);
                        long fu = (X * fia >> 16) + (Y * fic >> 16), fv = (X * fib >> 16) + (Y * fid >> 16);
                        int u = (int)Math.Max(0, Math.Min(Tile - 1, fu >> 16)), v = (int)Math.Max(0, Math.Min(Tile - 1, fv >> 16));
                        Texel(tile, u, v, out byte tr, out byte tg, out byte tb, out byte ta);
                        if (cov < 1f) ta = (byte)Math.Round(ta * cov);
                        Over(x, y, tr, tg, tb, ta);
                        if (darken > 0f)
                        {
                            int i = (y * S + x) * 4;
                            if (Px[i + 3] > 0)
                            {
                                float k = 1f - darken * cov;
                                Px[i] = (byte)Math.Round(Px[i] * k);
                                Px[i + 1] = (byte)Math.Round(Px[i + 1] * k);
                                Px[i + 2] = (byte)Math.Round(Px[i + 2] * k);
                            }
                        }
                    }
            }

            static bool InQuad(double px, double py, double ia, double ib, double ic, double id, double e, double f)
            {
                double X = px - e, Y = py - f, u = ia * X + ic * Y, v = ib * X + id * Y;
                return u >= 0 && v >= 0 && u < Tile && v < Tile;
            }
            /// <summary>Fraction of the pixel covered by the quad: exact for interior pixels, 8x8 samples on edges.</summary>
            static float Coverage(int x, int y, double ia, double ib, double ic, double id, double e, double f)
            {
                bool c00 = InQuad(x, y, ia, ib, ic, id, e, f), c10 = InQuad(x + 1, y, ia, ib, ic, id, e, f);
                bool c01 = InQuad(x, y + 1, ia, ib, ic, id, e, f), c11 = InQuad(x + 1, y + 1, ia, ib, ic, id, e, f);
                if (c00 && c10 && c01 && c11) return 1f;
                const int N = 8; int hits = 0;
                for (int sy = 0; sy < N; sy++)
                    for (int sx = 0; sx < N; sx++)
                        if (InQuad(x + (sx + .5) / N, y + (sy + .5) / N, ia, ib, ic, id, e, f)) hits++;
                return hits / (float)(N * N);
            }
        }

        public enum Kind : byte { Tile, Cube, Shaped }

        /// <summary>main Hn(): which painter a block uses.</summary>
        public static Kind BlockKind(int i)
        {
            uint fl = SourceBlockData.Flags[i];
            if ((fl & SourceBlockData.FBed) != 0 || (fl & SourceBlockData.FCross) != 0) return Kind.Tile;
            return (fl & SourceBlockData.FShaped) != 0 ? Kind.Shaped : Kind.Cube;
        }

        /// <summary>main WC().</summary>
        public static Canvas PaintTile(int tile)
        {
            var c = new Canvas(Tile);
            c.Draw(tile, 0, 0, Tile, Tile);
            return c;
        }

        /// <summary>main XR(): xR(top, front ?? side, 64).</summary>
        public static Canvas PaintCube(int i)
        {
            int top = SourceBlockData.TileTop[i], front = SourceBlockData.TileFront[i] >= 0 ? SourceBlockData.TileFront[i] : SourceBlockData.TileSide[i];
            var c = new Canvas(64);
            c.DrawAffine(front, 2, 1, 0, 2, 0, 16, .35f);
            c.DrawAffine(front, 2, -1, 0, 2, 32, 32, .18f);
            c.DrawAffine(top, 2, 1, -2, 1, 32, 0, 0f);
            return c;
        }

        /// <summary>main WR()/R3() at 48 px.</summary>
        public static Canvas PaintShaped(int i)
        {
            const float r = 48f;
            var c = new Canvas(48);
            int side = SourceBlockData.TileSide[i], top = SourceBlockData.TileTop[i], bottom = SourceBlockData.TileBottom[i], icon = SourceBlockData.TileIcon[i];
            switch ((BlockShape)SourceBlockData.Shape[i])
            {
                case BlockShape.Slab: c.Draw(side, 0, r / 2, r, r / 2); break;
                case BlockShape.Stairs: c.Draw(side, 0, r / 2, r, r / 2); c.Draw(side, r / 2, 0, r / 2, r / 2); break;
                case BlockShape.Fence:
                    c.Draw(side, r * .42f, r * .06f, r * .16f, r * .88f); c.Draw(side, r * .08f, r * .24f, r * .84f, r * .13f); c.Draw(side, r * .08f, r * .6f, r * .84f, r * .13f); break;
                case BlockShape.Gate:
                    c.Draw(side, r * .1f, r * .08f, r * .14f, r * .84f); c.Draw(side, r * .76f, r * .08f, r * .14f, r * .84f); c.Draw(side, r * .43f, r * .26f, r * .14f, r * .5f);
                    c.Draw(side, r * .24f, r * .3f, r * .52f, r * .12f); c.Draw(side, r * .24f, r * .56f, r * .52f, r * .12f); break;
                case BlockShape.Door: c.Draw(top, r * .22f, 0, r * .56f, r / 2); c.Draw(bottom, r * .22f, r / 2, r * .56f, r / 2); break;
                case BlockShape.Trapdoor: c.Draw(side, 0, 0, r, r); break;
                case BlockShape.Wall:
                    c.Draw(side, r * .34f, r * .04f, r * .32f, r * .92f); c.Draw(side, r * .04f, r * .4f, r * .3f, r * .26f); c.Draw(side, r * .66f, r * .4f, r * .3f, r * .26f); break;
                case BlockShape.Carpet: c.Draw(top, 0, r * .76f, r, r * .24f); break;
                case BlockShape.Pane: c.Draw(side, r * .42f, 0, r * .16f, r); c.Draw(side, 0, r * .42f, r, r * .16f); break;
                case BlockShape.Plate: c.Draw(top, r * .08f, r * .66f, r * .84f, r * .2f); break;
                case BlockShape.Button: c.Draw(side, r * .34f, r * .42f, r * .32f, r * .2f); break;
                case BlockShape.TallPlant: c.Draw(top, 0, 0, r, r); break;
                case BlockShape.Bamboo: c.Draw(side, r * .4f, 0, r * .2f, r); break;
                default: c.Draw(icon, 0, 0, r, r); break;
            }
            return c;
        }

        /// <summary>main Hn() for a reference numeric id (blocks below ItemIdBase, items above).</summary>
        public static Canvas PaintSourceId(int sourceId)
        {
            if (sourceId >= SourceItemData.ItemIdBase)
            {
                int k = sourceId - SourceItemData.ItemIdBase;
                return (uint)k < (uint)SourceItemData.Count ? PaintTile(SourceItemData.IconTile[k]) : null;
            }
            int i = (int)SourceBlockData.FromSourceId(sourceId);
            if (i == 0) return null;
            switch (BlockKind(i))
            {
                case Kind.Tile: return PaintTile(SourceBlockData.TileIcon[i]);
                case Kind.Shaped: return PaintShaped(i);
                default: return PaintCube(i);
            }
        }
    }

#if !PARITY_HARNESS
    /// <summary>main.js Hn(): cached Unity textures of MainIconPainter output, plus GUI helpers.</summary>
    public static class MainIcons
    {
        static readonly Dictionary<int, Texture2D> blockIcons = new Dictionary<int, Texture2D>();
        static readonly Dictionary<int, Texture2D> tileIcons = new Dictionary<int, Texture2D>();
        static bool atlasFailed;

        static bool EnsureAtlas()
        {
            if (MainIconPainter.Atlas != null) return true;
            if (atlasFailed) return false;
            var tex = Resources.Load<Texture2D>("Voxel/atlas");
            if (tex == null) { atlasFailed = true; return false; }
            int w = tex.width, h = tex.height;
            Color32[] px;
            if (tex.isReadable) px = tex.GetPixels32();
            else
            {
                // The atlas is imported without CPU access; read it back once through the GPU.
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                copy.Apply(false, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                px = copy.GetPixels32();
                Object.Destroy(copy);
            }
            // Unity rows are bottom-up; the painters work top-down like the canvas.
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[(h - 1 - y) * w + x]; int i = (y * w + x) * 4;
                    rgba[i] = c.r; rgba[i + 1] = c.g; rgba[i + 2] = c.b; rgba[i + 3] = c.a;
                }
            MainIconPainter.AtlasW = w; MainIconPainter.AtlasH = h; MainIconPainter.Atlas = rgba;
            return true;
        }

        static Texture2D ToTexture(MainIconPainter.Canvas c, string name)
        {
            int S = c.S;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = (y * S + x) * 4;
                    px[(S - 1 - y) * S + x] = new Color32(c.Px[i], c.Px[i + 1], c.Px[i + 2], c.Px[i + 3]);
                }
            t.SetPixels32(px); t.Apply(false, true);
            return t;
        }

        /// <summary>Icon for a reference atlas tile (main WC()).</summary>
        public static Texture2D TileIcon(int tile)
        {
            if (tile < 0 || !EnsureAtlas()) return null;
            if (tileIcons.TryGetValue(tile, out var t)) return t;
            t = ToTexture(MainIconPainter.PaintTile(tile), "icon_tile_" + tile);
            tileIcons[tile] = t; return t;
        }

        /// <summary>main Hn(id) for a block.</summary>
        public static Texture2D Block(BlockId id)
        {
            int i = (int)id;
            if (id == BlockId.Air || (uint)i >= (uint)SourceBlockData.Count || !EnsureAtlas()) return null;
            if (blockIcons.TryGetValue(i, out var t)) return t;
            switch (MainIconPainter.BlockKind(i))
            {
                case MainIconPainter.Kind.Tile: t = TileIcon(SourceBlockData.TileIcon[i]); break;
                case MainIconPainter.Kind.Shaped: t = ToTexture(MainIconPainter.PaintShaped(i), "icon_shaped_" + i); break;
                default: t = ToTexture(MainIconPainter.PaintCube(i), "icon_block_" + i); break;
            }
            blockIcons[i] = t; return t;
        }

        /// <summary>Icon for an inventory item key (port key = main tex).</summary>
        public static Texture2D Item(string key)
        {
            int k = SourceItemData.IndexOfTex(key);
            if (k < 0 && key == "flint_and_steel") return Block(BlockId.FlintAndSteel);
            return k >= 0 ? TileIcon(SourceItemData.IconTile[k]) : null;
        }

        public static Texture2D Stack(MainInventoryStackData s)
        {
            if (s == null) return null;
            return s.block ? Block((BlockId)s.blockId) : Item(s.itemKey);
        }

        /// <summary>Icon for a reference numeric id (blocks below ItemIdBase, items above).</summary>
        public static Texture2D SourceId(int sourceId)
        {
            if (sourceId >= SourceItemData.ItemIdBase)
            {
                int k = sourceId - SourceItemData.ItemIdBase;
                return (uint)k < (uint)SourceItemData.Count ? TileIcon(SourceItemData.IconTile[k]) : null;
            }
            return Block(SourceBlockData.FromSourceId(sourceId));
        }

        // ---- drawing helpers shared by HUD and inventory screens ------------------------------
        static GUIStyle countStyle, numStyle;
        static Texture2D white;

        /// <summary>main L3() slot contents: icon, count pill, durability bar.</summary>
        public static void DrawStack(Rect r, MainInventoryStackData s)
        {
            if (s == null) return;
            Texture2D icon = Stack(s);
            float pad = r.width * .12f;
            Rect ir = new Rect(r.x + pad, r.y + pad, r.width - 2 * pad, r.height - 2 * pad);
            if (icon != null) GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit, true);
            else GUI.Label(ir, s.block ? ((BlockId)s.blockId).ToString() : s.itemKey);
            if (countStyle == null)
            {
                countStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold, fontSize = 13, padding = new RectOffset(0, 3, 0, 1) };
                countStyle.normal.textColor = Color.white;
            }
            if (s.count > 1)
            {
                countStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(r.height * .3f));
                var shadow = new Rect(r.x + 1, r.y + 1, r.width, r.height);
                Color old = GUI.color; GUI.color = new Color(0, 0, 0, .8f); GUI.Label(shadow, s.count.ToString(), countStyle); GUI.color = old;
                GUI.Label(r, s.count.ToString(), countStyle);
            }
            if (s.dur >= 0)
            {
                int max = s.block ? 0 : MainInventoryCatalog.DefaultDurability(s.itemKey);
                if (max > 0)
                {
                    float w = Mathf.Clamp01(s.dur / (float)max);
                    Rect outer = new Rect(r.x + r.width * .12f, r.yMax - r.height * .16f, r.width * .76f, Mathf.Max(3f, r.height * .07f));
                    Fill(outer, new Color(0, 0, 0, .85f));
                    Fill(new Rect(outer.x, outer.y, outer.width * w, outer.height), w > .5f ? new Color32(0x5a, 0xc2, 0x5a, 255) : w > .25f ? new Color32(0xc2, 0xb2, 0x5a, 255) : new Color32(0xc2, 0x5a, 0x5a, 255));
                }
            }
        }

        public static void Fill(Rect r, Color c)
        {
            if (white == null) white = Texture2D.whiteTexture;
            Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        public static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        public static void SlotNumber(Rect r, int n)
        {
            if (numStyle == null)
            {
                numStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 10, padding = new RectOffset(3, 0, 1, 0) };
                numStyle.normal.textColor = new Color(1, 1, 1, .55f);
            }
            GUI.Label(r, n.ToString(), numStyle);
        }

        /// <summary>main Y2(): display name of a stack in the current UI language.</summary>
        public static string Name(MainInventoryStackData s, bool ru)
        {
            if (s == null) return "";
            if (s.block) return BlockName((BlockId)s.blockId, ru);
            int k = SourceItemData.IndexOfTex(s.itemKey);
            if (k >= 0) return ru ? SourceItemData.NameRu[k] : SourceItemData.NameEn[k];
            if (s.itemKey == "flint_and_steel") return BlockName(BlockId.FlintAndSteel, ru);
            return s.itemKey;
        }
        public static string BlockName(BlockId id, bool ru)
        {
            int i = (int)id;
            if ((uint)i >= (uint)SourceBlockData.Count) return id.ToString();
            return ru ? SourceBlockData.NameRu[i] : SourceBlockData.NameEn[i];
        }
    }
#endif
}
