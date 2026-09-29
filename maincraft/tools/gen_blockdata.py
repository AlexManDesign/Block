#!/usr/bin/env python3
"""One-off generator: turns the block list extracted from the original game
(names + texture file names + shapes) into our own compact data table
src/blockdata.js.  The resulting file is committed and hand-edited afterwards;
this script documents where the data came from."""
import json, sys, os

SP = sys.argv[1] if len(sys.argv) > 1 else '.'
blocks = json.load(open(os.path.join(SP, 'blocks.json')))['blocks']
names = {n['k']: n for n in json.load(open(os.path.join(SP, 'names.json')))}

# textures of the classic base blocks (ids < 136 in the original)
BASE_TEX = {
    'GRASS': 'grass_block_top,grass_block_side,dirt', 'DIRT': 'dirt', 'STONE': 'stone',
    'COBBLE': 'cobblestone', 'SAND': 'sand', 'GRAVEL': 'gravel', 'LOG': 'oak_log_top,oak_log',
    'LEAVES': 'oak_leaves', 'PLANKS': 'oak_planks', 'GLASS': 'glass', 'WATER': 'water_still',
    'BRICK': 'bricks', 'SNOW': 'snow', 'ICE': 'ice', 'SANDSTONE': 'sandstone_top,sandstone',
    'BEDROCK': 'bedrock', 'COAL': 'coal_ore', 'IRON': 'iron_ore', 'GOLD': 'gold_ore',
    'DIAMOND': 'diamond_ore', 'MOSSY': 'mossy_cobblestone', 'OBSIDIAN': 'obsidian',
    'GLOW': 'glowstone', 'BOOKSHELF': 'oak_planks,bookshelf', 'TNT': 'tnt_top,tnt_side,tnt_bottom',
    'WOOL_WHITE': 'white_wool', 'WOOL_RED': 'red_wool', 'WOOL_ORANGE': 'orange_wool',
    'WOOL_YELLOW': 'yellow_wool', 'WOOL_GREEN': 'green_wool', 'WOOL_BLUE': 'blue_wool',
    'WOOL_PURPLE': 'purple_wool', 'WOOL_BLACK': 'black_wool',
    'BIRCH_LOG': 'birch_log_top,birch_log', 'BIRCH_LEAVES': 'birch_leaves',
    'SPRUCE_LOG': 'spruce_log_top,spruce_log', 'SPRUCE_LEAVES': 'spruce_leaves',
    'BIRCH_PLANKS': 'birch_planks', 'SPRUCE_PLANKS': 'spruce_planks',
    'CACTUS': 'cactus_top,cactus_side', 'TALL_GRASS': 'short_grass', 'DANDELION': 'dandelion',
    'POPPY': 'poppy', 'DEAD_BUSH': 'dead_bush', 'DARK_LOG': 'dark_oak_log_top,dark_oak_log',
    'DARK_LEAVES': 'dark_oak_leaves', 'JUNGLE_LOG': 'jungle_log_top,jungle_log',
    'JUNGLE_LEAVES': 'jungle_leaves', 'ACACIA_LOG': 'acacia_log_top,acacia_log',
    'ACACIA_LEAVES': 'acacia_leaves', 'PODZOL': 'podzol_top,podzol_side,dirt', 'RED_SAND': 'red_sand',
    'TERRACOTTA': 'terracotta', 'TERRA_ORANGE': 'orange_terracotta', 'TERRA_WHITE': 'white_terracotta',
    'FERN': 'fern', 'ANDESITE': 'andesite', 'POLISHED_ANDESITE': 'polished_andesite',
    'DIORITE': 'diorite', 'POLISHED_DIORITE': 'polished_diorite', 'GRANITE': 'granite',
    'POLISHED_GRANITE': 'polished_granite', 'STONE_BRICKS': 'stone_bricks',
    'MOSSY_STONE_BRICKS': 'mossy_stone_bricks', 'CRACKED_STONE_BRICKS': 'cracked_stone_bricks',
    'CHISELED_STONE_BRICKS': 'chiseled_stone_bricks', 'SMOOTH_STONE': 'smooth_stone',
    'CALCITE': 'calcite', 'DEEPSLATE': 'deepslate', 'COBBLED_DEEPSLATE': 'cobbled_deepslate',
    'POLISHED_DEEPSLATE': 'polished_deepslate', 'DEEPSLATE_BRICKS': 'deepslate_bricks',
    'NETHERRACK': 'netherrack', 'NETHER_BRICKS': 'nether_bricks', 'RED_NETHER_BRICKS': 'red_nether_bricks',
    'BLACKSTONE': 'blackstone', 'QUARTZ': 'quartz_block_side', 'QUARTZ_BRICKS': 'quartz_bricks',
    'CHISELED_QUARTZ': 'chiseled_quartz_block', 'PRISMARINE': 'prismarine',
    'PRISMARINE_BRICKS': 'prismarine_bricks', 'DARK_PRISMARINE': 'dark_prismarine',
    'END_STONE': 'end_stone', 'END_STONE_BRICKS': 'end_stone_bricks', 'PURPUR': 'purpur_block',
    'PURPUR_PILLAR': 'purpur_pillar', 'CLAY': 'clay', 'PACKED_ICE': 'packed_ice', 'BLUE_ICE': 'blue_ice',
    'TUFF': 'tuff', 'TORCH': 'torch', 'LADDER': 'ladder',
    'OAK_DOOR': 'oak_door_top,oak_door_bottom', 'SPRUCE_DOOR': 'spruce_door_top,spruce_door_bottom',
    'BIRCH_DOOR': 'birch_door_top,birch_door_bottom', 'OAK_TRAPDOOR': 'oak_trapdoor',
    'SPRUCE_TRAPDOOR': 'spruce_trapdoor', 'BIRCH_TRAPDOOR': 'birch_trapdoor',
}
for c in ['white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray', 'light_gray',
          'cyan', 'purple', 'blue', 'brown', 'green', 'red', 'black']:
    BASE_TEX[c.upper() + '_CONCRETE'] = c + '_concrete'

SKIP = {'AIR', 'FLOW3', 'FLOW2', 'FLOW1', 'LAVA_FLOW2', 'LAVA_FLOW1', 'FLINT_AND_STEEL', 'SHIP_WHEEL'}
RENAME_TAB = {'colored': 'c', 'building': 'b', 'natural': 'n', 'functional': 'f'}
LEAVES = {'LEAVES', 'BIRCH_LEAVES', 'SPRUCE_LEAVES', 'DARK_LEAVES', 'JUNGLE_LEAVES', 'ACACIA_LEAVES',
          'AZALEA_LEAVES', 'FLOWERING_AZALEA_LEAVES'}
LOGS = {'LOG', 'BIRCH_LOG', 'SPRUCE_LOG', 'DARK_LOG', 'JUNGLE_LOG', 'ACACIA_LOG', 'QUARTZ_PILLAR',
        'BASALT', 'POLISHED_BASALT', 'PURPUR_PILLAR', 'BONE_BLOCK', 'HAY_BLOCK', 'CRIMSON_STEM',
        'WARPED_STEM', 'STRIPPED_CRIMSON_STEM', 'STRIPPED_WARPED_STEM'}
BASE_TAB = {}
for k in ['GRASS', 'DIRT', 'STONE', 'SAND', 'GRAVEL', 'LOG', 'LEAVES', 'SNOW', 'ICE', 'BEDROCK', 'COAL',
          'IRON', 'GOLD', 'DIAMOND', 'OBSIDIAN', 'BIRCH_LOG', 'BIRCH_LEAVES', 'SPRUCE_LOG', 'SPRUCE_LEAVES',
          'CACTUS', 'TALL_GRASS', 'DANDELION', 'POPPY', 'DEAD_BUSH', 'DARK_LOG', 'DARK_LEAVES', 'JUNGLE_LOG',
          'JUNGLE_LEAVES', 'ACACIA_LOG', 'ACACIA_LEAVES', 'PODZOL', 'RED_SAND', 'FERN', 'ANDESITE', 'DIORITE',
          'GRANITE', 'CALCITE', 'DEEPSLATE', 'NETHERRACK', 'BLACKSTONE', 'END_STONE', 'CLAY', 'PACKED_ICE',
          'BLUE_ICE', 'TUFF', 'WATER', 'TERRACOTTA']:
    BASE_TAB[k] = 'n'
for k in ['GLASS', 'TNT', 'TORCH', 'LADDER', 'GLOW', 'BOOKSHELF']:
    BASE_TAB[k] = 'f'

out = []
for b in blocks:
    k = b['k']
    if k in SKIP or k.startswith('BED_HEAD'):
        continue
    d = b.get('def') or {}
    nm = names.get(k, {})
    en = nm.get('en') or k
    ru = nm.get('ru') or en
    shape = b.get('shape') or d.get('shape') or ''
    tex = BASE_TEX.get(k)
    if tex is None:
        t = d.get('tex')
        if isinstance(t, str):
            tex = t
        elif isinstance(t, dict):
            parts = [t.get('top'), t.get('side') or t.get('bottom')]
            if t.get('side') and t.get('bottom'):
                parts.append(t['bottom'])
            if t.get('front'):
                if len(parts) == 2:
                    parts.append(parts[0])
                parts.append(t['front'])
            tex = ','.join(parts)
        else:
            tex = ''
    flags = ''
    if k in LEAVES: flags += 'L'
    if k in LOGS or (k.startswith('STRIPPED_') and k.endswith('_LOG')) or k.endswith('_STEM') and 'MUSHROOM' not in k and 'PUMPKIN' not in k and 'MELON' not in k:
        flags += 'Y'
    if k in ('GLASS', 'GLASS_PANE', 'IRON_BARS'): flags += 'G'
    if d.get('glassy') and k not in ('GLASS_PANE',): flags += 'T'
    if k in ('ICE',): flags += 'T'
    if d.get('falling') or k in ('SAND', 'GRAVEL', 'RED_SAND'): flags += 'F'
    if d.get('emissive') or k in ('GLOW',): flags += 'e'
    if d.get('cross') or b.get('cross'): shape = shape or 'x'
    if d.get('aquatic'): flags += 'A'
    if d.get('needsWater'): flags += 'W'
    if d.get('hang'): flags += 'h'
    if d.get('climb'): flags += 'c'
    if d.get('stack'): flags += 'S'
    if k in ('FURNACE', 'FURNACE_LIT', 'CARVED_PUMPKIN', 'JACK_O_LANTERN', 'CHEST', 'CRAFTING_TABLE'):
        flags += 'O'
    light = b.get('light') or d.get('light') or 0
    if k == 'GLOW': light = 15
    if k == 'TORCH': light = 14
    base = b.get('base') or d.get('base') or ''
    tab = RENAME_TAB.get(d.get('tab'), BASE_TAB.get(k, 'b'))
    if shape in ('x', 'tallplant', 'vine', 'lilypad', 'seapickle', 'bamboo', 'lichen'): tab = 'n'
    if shape in ('door', 'trapdoor', 'gate', 'fence', 'plate', 'button', 'torch', 'ladder', 'rail', 'pot'):
        tab = 'f'
    if k == 'BED' or k.startswith('BED_'):
        shape = 'bed'
        tab = 'f'
    if k == 'CACTUS': shape = 'cactus'
    if k == 'WATER': shape = 'water'
    if k == 'LAVA': shape = 'lava'
    if k == 'CHEST': shape = 'chest'
    if k in ('FARMLAND', 'FARMLAND_MOIST', 'DIRT_PATH'): shape = 'farmland'
    if k == 'FIRE': shape = 'fire'
    if k == 'SNOW': pass
    rgb = d.get('rgb')
    out.append([k, en, ru, tex, shape, flags, light, base, tab])

lines = ['// Block table (generated by tools/gen_blockdata.py, then hand-edited).',
         '// [key, english, russian, textures(top,side[,bottom[,front]]), shape, flags, light, base, tab]',
         '// flags: L leaves, Y axis-rotatable, G clear glass, T translucent, F falling, e emissive,',
         '//        A aquatic, W needs water, h hanging, c climbable, S stackable plant, O facing',
         'const BLOCK_TABLE = [']
for r in out:
    lines.append('  ' + json.dumps(r, ensure_ascii=False) + ',')
lines.append('];')
open(os.path.join(os.path.dirname(__file__), '..', 'src', 'blockdata.js'), 'w').write('\n'.join(lines) + '\n')
print(len(out), 'blocks')
