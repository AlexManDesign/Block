#!/usr/bin/env python3
"""Converts Minecraft's chest loot tables (vanilla data pack, cached in tools/.nbt_cache) into src/loot.js.

LOOT[name] = table number (stored in a generated chest's meta), LOOT_TABLES[number] = pools
[minRolls, maxRolls, [[item key, weight, minCount, maxCount], ...]]. Items this game does not have
stay in as empty entries ('' key), so the odds of the others are Minecraft's.

    python3 tools/loot.py
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import structures as S

TABLES = [('SIMPLE_DUNGEON', 'chests/simple_dungeon'), ('ABANDONED_MINESHAFT', 'chests/abandoned_mineshaft'),
          ('DESERT_PYRAMID', 'chests/desert_pyramid'), ('JUNGLE_TEMPLE', 'chests/jungle_temple'),
          ('SHIPWRECK_SUPPLY', 'chests/shipwreck_supply'), ('SHIPWRECK_TREASURE', 'chests/shipwreck_treasure'),
          ('SHIPWRECK_MAP', 'chests/shipwreck_map'), ('UNDERWATER_RUIN_SMALL', 'chests/underwater_ruin_small'),
          ('UNDERWATER_RUIN_BIG', 'chests/underwater_ruin_big'), ('IGLOO', 'chests/igloo_chest')]
for v in ('armorer', 'butcher', 'cartographer', 'desert_house', 'fisher', 'fletcher', 'mason', 'plains_house', 'savanna_house',
          'shepherd', 'snowy_house', 'taiga_house', 'tannery', 'temple', 'toolsmith', 'weaponsmith'):
    TABLES.append(('VILLAGE_' + v.upper(), 'chests/village/village_' + v))


def num(v):
    if isinstance(v, (int, float)): return v, v
    if v.get('type', 'minecraft:uniform') in ('minecraft:uniform', 'uniform'): return v['min'], v['max']
    if v.get('type') == 'minecraft:constant': return v['value'], v['value']
    raise SystemExit('number ' + json.dumps(v))


def main():
    src = open(os.path.join(S.ROOT, 'src', 'blocks.js')).read()
    items = set(re.findall(r"\[\s*'([A-Z_0-9]+)'", src))
    for mat in ('WOODEN', 'STONE', 'IRON', 'GOLDEN', 'DIAMOND'):
        for t in ('PICKAXE', 'AXE', 'SHOVEL', 'SWORD', 'HOE'): items.add(mat + '_' + t)
    blocks = set(re.findall(r'^\s*\["([A-Z_0-9]+)"', open(os.path.join(S.ROOT, 'src', 'blockdata.js')).read(), re.M))
    alias = {'BRICK': 'BRICK_ITEM', 'COD': 'RAW_COD', 'SALMON': 'RAW_SALMON', 'TORCH': 'TORCH', 'RAIL': 'RAIL', 'BRICKS': 'BRICK',
             'COBBLESTONE': 'COBBLE', 'SNOW_BLOCK': 'SNOW', 'GRASS_BLOCK': 'GRASS'}
    missing = set()

    def key(name):
        k = name.split(':')[1].upper()
        k = alias.get(k, k)
        if k.endswith('_WOOL'): k = 'WOOL_' + k[:-5]
        if k == 'SHORT_GRASS': k = 'TALL_GRASS'
        if k in items or k in blocks: return k
        missing.add(k); return ''
    out, ids = [None], {}
    for const, path in TABLES:
        d = json.loads(S.fetch(path, 'loot_table', '.json'))
        pools = []
        for p in d.get('pools', []):
            r0, r1 = num(p['rolls'])
            ents = []
            for e in p['entries']:
                t = e['type']
                w = e.get('weight', 1)
                if t == 'minecraft:empty': ents.append(['', w, 1, 1]); continue
                if t != 'minecraft:item': ents.append(['', w, 1, 1]); continue   # nested tables / tags: none used by the chests here
                c0 = c1 = 1
                for f in e.get('functions', []):
                    if f['function'] == 'minecraft:set_count': c0, c1 = num(f['count'])
                ents.append([key(e['name']), w, int(c0), int(c1)])
            pools.append([int(r0), int(r1), ents])
        ids[const] = len(out); out.append(pools)
    js = ('\'use strict\';\n// Minecraft chest loot tables (vanilla data pack), converted by tools/loot.py.\n'
          'const LOOT = ' + json.dumps(ids, separators=(',', ':')) + ';\n'
          'const LOOT_DATA = ' + json.dumps(out, separators=(',', ':')) + ';\n')
    open(os.path.join(S.ROOT, 'src', 'loot.js'), 'w').write(js)
    print('tables', len(ids), len(js) // 1024, 'KB; items not in the game:', ' '.join(sorted(missing)))


if __name__ == '__main__':
    main()
