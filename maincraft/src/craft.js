'use strict';
// Crafting recipes (shaped + shapeless) and furnace smelting.

const RECIPES = [];
function R(pattern, keys, out, count) { RECIPES.push({ pattern, keys, out, count: count || 1 }); }
function RS(ings, out, count) { RECIPES.push({ shapeless: ings, out, count: count || 1 }); }
const ANY_PLANKS = 'planks', ANY_LOG = 'log';
function matches(tag, id) {
  if (tag === ANY_PLANKS) return /PLANKS$/.test(B_KEY[id] || '');
  if (tag === ANY_LOG) return /(_LOG|^LOG|_WOOD|_STEM|_HYPHAE)$/.test(B_KEY[id] || '') && !/MUSHROOM|PUMPKIN|MELON/.test(B_KEY[id]);
  if (tag === 'wool') return /^WOOL_/.test(B_KEY[id] || '');
  if (tag === 'stonetool') return id === B.COBBLE || id === B.COBBLED_DEEPSLATE || id === B.BLACKSTONE;
  if (tag === 'sand') return id === B.SAND || id === B.RED_SAND;
  if (tag === 'woodslab') return SHAPE[id] === SH.SLAB && /PLANKS/.test(B_KEY[BASE[id]] || '');
  return tag === id;
}
(function () {
  const P = ANY_PLANKS, S = IT.STICK;
  // every log, wood and stem (stripped or not) gives 4 planks of its kind; 4 logs give 3 wood
  const kinds = [['OAK', 'LOG', 'OAK_WOOD', 'PLANKS'], ['BIRCH', 'BIRCH_LOG', 'BIRCH_WOOD', 'BIRCH_PLANKS'],
    ['SPRUCE', 'SPRUCE_LOG', 'SPRUCE_WOOD', 'SPRUCE_PLANKS'], ['DARK_OAK', 'DARK_LOG', 'DARK_OAK_WOOD', 'DARK_PLANKS'],
    ['JUNGLE', 'JUNGLE_LOG', 'JUNGLE_WOOD', 'JUNGLE_PLANKS'], ['ACACIA', 'ACACIA_LOG', 'ACACIA_WOOD', 'ACACIA_PLANKS'],
    ['MANGROVE', 'MANGROVE_LOG', 'MANGROVE_WOOD', 'MANGROVE_PLANKS'], ['CHERRY', 'CHERRY_LOG', 'CHERRY_WOOD', 'CHERRY_PLANKS'],
    ['PALE_OAK', 'PALE_OAK_LOG', 'PALE_OAK_WOOD', 'PALE_OAK_PLANKS'], ['CRIMSON', 'CRIMSON_STEM', 'CRIMSON_HYPHAE', 'CRIMSON_PLANKS'],
    ['WARPED', 'WARPED_STEM', 'WARPED_HYPHAE', 'WARPED_PLANKS']];
  for (const [k, log, wood, planks] of kinds) {
    const sLog = 'STRIPPED_' + (k === 'OAK' || k === 'DARK_OAK' ? k + '_' + (log.endsWith('STEM') ? 'STEM' : 'LOG') : log);
    const sWood = 'STRIPPED_' + wood;
    for (const l of [log, wood, sLog, sWood]) if (B[l] && B[planks]) RS([B[l]], B[planks], 4);
    if (B[log] && B[wood]) R(['##', '##'], { '#': B[log] }, B[wood], 3);
    if (B[sLog] && B[sWood]) R(['##', '##'], { '#': B[sLog] }, B[sWood], 3);
  }
  R(['#', '#'], { '#': P }, S, 4);
  R(['##', '##'], { '#': P }, B.CRAFTING_TABLE);
  R(['###', '# #', '###'], { '#': 'stonetool' }, B.FURNACE);
  R(['###', '# #', '###'], { '#': P }, B.CHEST);
  R(['C', 'S'], { C: IT.COAL, S }, B.TORCH, 4);
  R([' #S', '# S', ' #S'], { '#': S, S: IT.STRING }, IT.BOW);
  R(['F', 'S', 'E'], { F: IT.FLINT, S, E: IT.FEATHER }, IT.ARROW, 4);
  if (B.BARREL) R(['PSP', 'P P', 'PSP'], { P, S: 'woodslab' }, B.BARREL);
  if (B.CAMPFIRE) R([' S ', 'SCS', 'LLL'], { S, C: IT.COAL, L: ANY_LOG }, B.CAMPFIRE);
  R(['# #', '###', '# #'], { '#': S }, B.LADDER, 3);
  R([' S ', 'SPS', ' S '], { S, P }, B.SHIP_WHEEL);
  for (const [mat, tier, key] of [[P, 'WOODEN'], ['stonetool', 'STONE'], [IT.IRON_INGOT, 'IRON'], [IT.GOLD_INGOT, 'GOLDEN'], [IT.DIAMOND, 'DIAMOND']].map(a => [a[0], 0, a[1]])) {
    void tier;
    R(['###', ' S ', ' S '], { '#': mat, S }, IT[key + '_PICKAXE']);
    R(['##', '#S', ' S'], { '#': mat, S }, IT[key + '_AXE']);
    R(['#', 'S', 'S'], { '#': mat, S }, IT[key + '_SHOVEL']);
    R(['#', '#', 'S'], { '#': mat, S }, IT[key + '_SWORD']);
    R(['##', ' S', ' S'], { '#': mat, S }, IT[key + '_HOE']);
  }
  R(['# #', ' # '], { '#': IT.IRON_INGOT }, IT.BUCKET);
  R([' #', '# '], { '#': IT.IRON_INGOT }, IT.SHEARS);
  RS([IT.IRON_INGOT, IT.FLINT], IT.FLINT_AND_STEEL);
  R(['###'], { '#': IT.WHEAT }, IT.BREAD);
  R(['# #', ' # '], { '#': P }, IT.BOWL, 4);
  RS([B.BROWN_MUSHROOM, B.RED_MUSHROOM, IT.BOWL], IT.MUSHROOM_STEW);
  RS([IT.BONE], IT.BONE_MEAL, 3);
  RS([IT.SUGAR_CANE_ITEM || B.SUGAR_CANE], IT.SUGAR);
  R(['###', '#A#', '###'], { '#': IT.GOLD_INGOT, A: IT.APPLE }, IT.GOLDEN_APPLE);
  // (Minecraft uses 8 gold nuggets; there are no nuggets here)
  R(['###', '#C#', '###'], { '#': IT.GOLD_INGOT, C: IT.CARROT }, IT.GOLDEN_CARROT);
  RS([IT.MELON_SLICE], IT.MELON_SEEDS); RS([B.PUMPKIN], IT.PUMPKIN_SEEDS, 4);
  R(['###', '###', '###'], { '#': IT.MELON_SLICE }, B.MELON);
  const storage = [[IT.COAL, 'COAL_BLOCK'], [IT.IRON_INGOT, 'IRON_BLOCK'], [IT.GOLD_INGOT, 'GOLD_BLOCK'], [IT.DIAMOND, 'DIAMOND_BLOCK'], [IT.EMERALD, 'EMERALD_BLOCK'], [IT.COPPER_INGOT, 'COPPER_BLOCK'], [IT.WHEAT, 'HAY_BLOCK']];
  for (const [it, b] of storage) if (B[b]) { R(['###', '###', '###'], { '#': it }, B[b]); RS([B[b]], it, 9); }
  R(['##', '##'], { '#': IT.CLAY_BALL }, B.CLAY);
  R(['##', '##'], { '#': IT.BRICK_ITEM }, B.BRICK);
  R(['##', '##'], { '#': B.SAND }, B.SANDSTONE);
  R(['##', '##'], { '#': B.STONE }, B.STONE_BRICKS, 4);
  R(['##', '##'], { '#': B.GRANITE }, B.POLISHED_GRANITE, 4);
  R(['##', '##'], { '#': B.DIORITE }, B.POLISHED_DIORITE, 4);
  R(['##', '##'], { '#': B.ANDESITE }, B.POLISHED_ANDESITE, 4);
  R(['##', '##'], { '#': B.COBBLED_DEEPSLATE }, B.POLISHED_DEEPSLATE, 4);
  R(['##', '##'], { '#': IT.STRING }, B.WOOL_WHITE);
  R(['G#G', '#G#', 'G#G'], { G: IT.GUNPOWDER, '#': 'sand' }, B.TNT);
  R(['###', '###'], { '#': B.GLASS }, B.GLASS_PANE, 16);
  R(['###', '###'], { '#': IT.IRON_INGOT }, B.IRON_BARS, 16);
  R(['P', 'T'], { P: B.CARVED_PUMPKIN, T: B.TORCH }, B.JACK_O_LANTERN);
  R(['WWW', '###'], { W: 'wool', '#': P }, B.BED);
  R(['#S#', '#S#'], { '#': P, S }, B.OAK_FENCE, 3);
  R(['S#S', 'S#S'], { '#': P, S }, B.OAK_GATE);
  R(['##', '##', '##'], { '#': P }, B.OAK_DOOR, 3);
  R(['###', '###'], { '#': P }, B.OAK_TRAPDOOR, 2);
  R(['##', '##', '##'], { '#': IT.IRON_INGOT }, B.IRON_DOOR, 3);
  R(['#'], { '#': P }, B.PLANKS_BUTTON);
  R(['#'], { '#': B.STONE }, B.STONE_BUTTON);
  R(['##'], { '#': P }, B.PLANKS_PRESSURE_PLATE);
  R(['##'], { '#': B.STONE }, B.STONE_PRESSURE_PLATE);
  R(['#', '#'], { '#': B.QUARTZ }, B.QUARTZ_PILLAR, 2);
  R(['# #', ' # '], { '#': IT.BRICK_ITEM }, B.FLOWER_POT);
  R(['#S#', '#S#', '#S#'], { '#': IT.IRON_INGOT, S }, B.RAIL, 16);
  R(['###', '###', '###'], { '#': B.ICE }, B.PACKED_ICE);
  if (B.BLUE_ICE) R(['###', '###', '###'], { '#': B.PACKED_ICE }, B.BLUE_ICE);
  // mossy and chiseled stone
  RS([B.STONE_BRICKS, B.VINE], B.MOSSY_STONE_BRICKS); RS([B.COBBLE, B.VINE], B.MOSSY);
  if (B.MOSS_BLOCK) { RS([B.STONE_BRICKS, B.MOSS_BLOCK], B.MOSSY_STONE_BRICKS); RS([B.COBBLE, B.MOSS_BLOCK], B.MOSSY); }
  if (B.STONEBRICK_SLAB && B.CHISELED_STONE_BRICKS) R(['#', '#'], { '#': B.STONEBRICK_SLAB }, B.CHISELED_STONE_BRICKS);
  if (B.SANDSTONE_SLAB && B.CHISELED_SANDSTONE) R(['#', '#'], { '#': B.SANDSTONE_SLAB }, B.CHISELED_SANDSTONE);
  if (B.MOSS_BLOCK && B.MOSS_CARPET) R(['##'], { '#': B.MOSS_BLOCK }, B.MOSS_CARPET, 3);
  // slabs, stairs, walls from their base blocks (and fences for planks)
  for (let id = 1; id < NB; id++) {
    const base = BASE[id];
    if (!base) continue;
    const sh = SHAPE[id];
    if (sh === SH.SLAB) R(['###'], { '#': base }, id, 6);
    else if (sh === SH.STAIRS) R(['#  ', '## ', '###'], { '#': base }, id, 4);
    else if (sh === SH.WALL) R(['###', '###'], { '#': base }, id, 6);
    else if (sh === SH.CARPET) R(['##'], { '#': base }, id, 3);
    else if (sh === SH.FENCE && /PLANKS/.test(B_KEY[base])) R(['#S#', '#S#'], { '#': base, S }, id, 3);
    else if (sh === SH.GATE && /PLANKS/.test(B_KEY[base])) R(['S#S', 'S#S'], { '#': base, S }, id);
    else if (sh === SH.PANE && base !== B.GLASS) R(['###', '###'], { '#': base }, id, 16);
  }
})();

// grid: array of ids (0 empty), w x h. returns {id,count} or null
function craftResult(grid, w, h) {
  let minx = w, miny = h, maxx = -1, maxy = -1;
  const items = [];
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const id = grid[y * w + x];
    if (!id) continue;
    items.push(id);
    if (x < minx) minx = x; if (y < miny) miny = y; if (x > maxx) maxx = x; if (y > maxy) maxy = y;
  }
  if (!items.length) return null;
  const gw = maxx - minx + 1, gh = maxy - miny + 1;
  for (const r of RECIPES) {
    if (r.shapeless) {
      if (r.shapeless.length !== items.length) continue;
      const left = items.slice();
      let ok = true;
      for (const need of r.shapeless) { const i = left.findIndex(id => matches(need, id)); if (i < 0) { ok = false; break; } left.splice(i, 1); }
      if (ok && r.out) return { id: r.out, count: r.count };
      continue;
    }
    const ph = r.pattern.length, pw = Math.max(...r.pattern.map(s => s.length));
    if (pw !== gw || ph !== gh) continue;
    for (const mirror of [false, true]) {
      let ok = true;
      for (let y = 0; y < ph && ok; y++) for (let x = 0; x < pw && ok; x++) {
        const row = r.pattern[y];
        const ch = row[mirror ? pw - 1 - x : x] || ' ';
        const id = grid[(miny + y) * w + minx + x];
        if (ch === ' ') { if (id) ok = false; }
        else if (!id || !matches(r.keys[ch], id)) ok = false;
      }
      if (ok && r.out) return { id: r.out, count: r.count };
    }
  }
  return null;
}

const SMELT = {};
(function () {
  const s = (a, b) => { if (a && b) SMELT[a] = b; };
  s(B.COBBLE, B.STONE); s(B.SAND, B.GLASS); s(B.RED_SAND, B.GLASS); s(B.IRON, IT.IRON_INGOT); s(B.DEEPSLATE_IRON_ORE, IT.IRON_INGOT);
  s(B.GOLD, IT.GOLD_INGOT); s(B.DEEPSLATE_GOLD_ORE, IT.GOLD_INGOT); s(IT.RAW_COPPER, IT.COPPER_INGOT); s(B.CLAY, B.TERRACOTTA);
  s(IT.CLAY_BALL, IT.BRICK_ITEM); s(IT.PORKCHOP, IT.COOKED_PORKCHOP); s(IT.BEEF, IT.COOKED_BEEF); s(IT.MUTTON, IT.COOKED_MUTTON);
  s(IT.CHICKEN, IT.COOKED_CHICKEN); s(IT.RAW_COD, IT.COOKED_COD); s(IT.RAW_SALMON, IT.COOKED_SALMON); s(IT.POTATO, IT.BAKED_POTATO);
  s(B.COBBLED_DEEPSLATE, B.DEEPSLATE); s(B.STONE, B.SMOOTH_STONE); s(B.LOG, IT.COAL);
  s(B.STONE_BRICKS, B.CRACKED_STONE_BRICKS); s(B.NETHERRACK, B.NETHER_BRICKS); s(B.WET_SPONGE, B.SPONGE);
})();
function fuelValue(id) {
  if (id === IT.COAL) return 80; if (id === B.COAL_BLOCK) return 800; if (id === IT.LAVA_BUCKET) return 1000;
  if (id === IT.STICK) return 5; const k = B_KEY[id] || '';
  if (/PLANKS|LOG|WOOD|STEM|FENCE|GATE|BOOKSHELF|CRAFTING|CHEST|LADDER|SLAB|STAIRS/.test(k) && !/STONE|BRICK|DEEPSLATE|QUARTZ|SANDSTONE|PRISMARINE|PURPUR|BLACKSTONE|TUFF|COPPER|MUD|NETHER|GRANITE|DIORITE|ANDESITE|MOSSY|END/.test(k)) return 15;
  if (/SAPLING/.test(k)) return 5;
  if (isItem(id)) { const d = itemDef(id); if (d && d[5] && d[5].tool && /WOODEN/.test(d[0])) return 10; }
  return 0;
}
