#!/usr/bin/env python3
"""Generates the Unity port's block tables from the reference data (reference/data/*.json).

Outputs (all overwritten):
  UnityPort/Assets/BlockcraftPort/Core/BlockId.cs          enum: existing values kept, new source blocks appended
  UnityPort/Assets/BlockcraftPort/Core/SourceBlockData.g.cs per-block reference properties indexed by BlockId
  UnityPort/Assets/BlockcraftPort/Rendering/AtlasLayout.cs  named + indexed UV rects for the reference atlas
  UnityPort/Assets/Resources/Voxel/atlas.png                reference atlas (main.js $l()+Av(), 256x752)
  UnityPort/Assets/Resources/Voxel/atlas_layout.json

Existing BlockId values are never renumbered (saves store them); blocks missing from the port are
appended after the current last value in reference id order.
"""
import json, os, re, shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PORT = os.path.join(ROOT, 'UnityPort', 'Assets')
REF = os.path.join(ROOT, 'reference', 'data')

# Port enum names whose source key is not the mechanical PascalCase->UPPER_SNAKE conversion.
ALIAS = {
    'CoalOre': 'COAL', 'IronOre': 'IRON', 'GoldOre': 'GOLD', 'DiamondOre': 'DIAMOND', 'Moss': 'MOSS_BLOCK',
    'OakLog': 'LOG', 'OakLeaves': 'LEAVES', 'DarkOakLog': 'DARK_LOG', 'DarkOakLeaves': 'DARK_LEAVES',
    'CherryWood': 'CHERRY_PLANKS', 'Amethyst': 'AMETHYST_BLOCK', 'CherryLeaves': 'FLOWERING_AZALEA_LEAVES',
    'OrangeTerracotta': 'TERRA_ORANGE', 'WhiteTerracotta': 'TERRA_WHITE', 'OakPlanks': 'PLANKS',
    'Cobblestone': 'COBBLE', 'CobblestoneSlab': 'COBBLE_SLAB', 'CobblestoneStairs': 'COBBLE_STAIRS',
    'OakFenceGate': 'OAK_GATE', 'CobblestoneWall': 'COBBLE_WALL', 'OakPressurePlate': 'PLANKS_PRESSURE_PLATE',
    'OakButton': 'PLANKS_BUTTON', 'BedRedFoot': 'BED', 'BedRedHead': 'BED_HEAD', 'Flow3': 'FLOW3', 'Flow2': 'FLOW2',
    'Flow1': 'FLOW1', 'DarkOakPlanks': 'DARK_PLANKS', 'DarkOakSlab': 'DARK_PLANKS_SLAB', 'DarkOakStairs': 'DARK_PLANKS_STAIRS',
    'DarkOakFence': 'DARK_PLANKS_FENCE', 'LavaFlow2': 'LAVA_FLOW2', 'LavaFlow1': 'LAVA_FLOW1',
}
# Legacy port ids that alias another port id for the same source block: the canonical one wins in FromSource.
LEGACY_DUPLICATES = {'CherryLeaves'}

SHAPES = ['Cube', 'Cross', 'TallPlant', 'Bamboo', 'LilyPad', 'SeaPickle', 'Carpet', 'Slab', 'Stairs', 'Fence', 'Gate',
          'Wall', 'Pane', 'Plate', 'Button', 'Trapdoor', 'Ladder', 'Torch', 'Rail', 'FlatFaces', 'Pot', 'ShipWheel',
          'Door', 'Bed', 'Chest']
SOURCE_SHAPE = {None: 'Cube', 'slab': 'Slab', 'stairs': 'Stairs', 'fence': 'Fence', 'gate': 'Gate', 'wall': 'Wall',
                'pane': 'Pane', 'plate': 'Plate', 'button': 'Button', 'trapdoor': 'Trapdoor', 'door': 'Door',
                'carpet': 'Carpet', 'tallplant': 'TallPlant', 'vine': 'FlatFaces', 'lichen': 'FlatFaces',
                'lilypad': 'LilyPad', 'seapickle': 'SeaPickle', 'bamboo': 'Bamboo', 'pot': 'Pot', 'rail': 'Rail',
                'torch': 'Torch', 'ladder': 'Ladder', 'shipwheel': 'ShipWheel'}
FLAGS = ['FullOpaque', 'Cross', 'Aquatic', 'ClassicLeaf', 'Water', 'Lava', 'Glass', 'Glassy', 'TransparentLayer',
         'NeedsWater', 'Hang', 'Climb', 'Axis', 'EmissiveBoost', 'Falling', 'LightBlock', 'XrayOre', 'XrayClass',
         'SolidRender', 'Bed', 'Chest', 'Shaped']
FACE_KEYS = ['top', 'side', 'bottom', 'front', 'frontL', 'frontR', 'topL', 'topR', 'corner', 'icon']


def snake(name):
    s = re.sub(r'(?<!^)(?=[A-Z])', '_', name)
    s = re.sub(r'(?<=[a-z])(?=[0-9])', '_', s)
    return s.upper()


def pascal(key):
    return ''.join(p[:1] + p[1:].lower() for p in key.split('_'))


def parse_existing_enum(path):
    """Returns [(name, value, commentKey)] of the current BlockId enum plus the text after it."""
    src = open(path, encoding='utf-8').read()
    start = src.index('enum BlockId')
    body = src[src.index('{', start) + 1:src.index('}', start)]
    names, value = [], -1
    for line in body.splitlines():
        comment = None
        if '//' in line:
            line, c = line.split('//', 1)
            m = re.fullmatch(r'\s*([A-Z0-9_]+)\s*', c)
            comment = m.group(1) if m else None
        for tok in line.split(','):
            tok = tok.strip()
            if not tok:
                continue
            if '=' in tok:
                n, v = [t.strip() for t in tok.split('=')]
                value = int(v)
            else:
                n = tok
                value += 1
            names.append((n, value, comment))
    tail = src[src.index('}', start) + 1:]
    tail = tail[:tail.rindex('}')]  # drop namespace closing brace
    return names, tail


def cs_array(ctype, values, per_line=24):
    rows = []
    for i in range(0, len(values), per_line):
        rows.append('            ' + ','.join(values[i:i + per_line]))
    return f'new {ctype}[]\n        {{\n' + ',\n'.join(rows) + '\n        }'


def cs_str(s):
    if s is None:
        return 'null'
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def main():
    blocks = json.load(open(os.path.join(REF, 'blocks.json'), encoding='utf-8'))
    atlas = blocks['atlas']
    src_blocks = blocks['blocks']
    by_key = {b['key']: b for b in src_blocks}

    enum_path = os.path.join(PORT, 'BlockcraftPort', 'Core', 'BlockId.cs')
    existing, tail = parse_existing_enum(enum_path)
    if any('generated' in l for l in open(enum_path, encoding='utf-8').read().splitlines()[:3]):
        # Re-running: existing file is already generated; keep its members/values as the stable base.
        pass
    port = []  # (name, value, sourceKey)
    mapped = set()
    for name, value, comment in existing:
        key = comment or ALIAS.get(name, snake(name))
        if key not in by_key:
            raise SystemExit(f'port BlockId.{name} has no reference block {key}')
        port.append((name, value, key))
        if name not in LEGACY_DUPLICATES:
            mapped.add(key)
    used_names = {n for n, _, _ in port}
    next_value = max(v for _, v, _ in port) + 1
    appended = []
    for b in src_blocks:
        if b['key'] in mapped:
            continue
        name = pascal(b['key'])
        if name in used_names:
            name = name + 'Block'
        used_names.add(name)
        port.append((name, next_value, b['key']))
        appended.append((name, next_value, b['key']))
        mapped.add(b['key'])
        next_value += 1
    count = next_value

    # ---- BlockId.cs
    lines = ['// <auto-generated> by tools/codegen/gen_blocks.py from reference/data/blocks.json.',
             '// Values of the original port ids are fixed forever (saves store them); every reference block',
             '// the port did not have yet is appended in reference id order. Do not edit by hand.',
             'namespace BlockcraftPort', '{', '    public enum BlockId : ushort', '    {']
    for name, value, key in port:
        lines.append(f'        {name} = {value},' + ('' if snake(name) == key else f' // {key}'))
    lines.append('    }')
    out = '\n'.join(lines) + tail + '}\n'
    open(enum_path, 'w', encoding='utf-8').write(out)

    # ---- per-id tables
    by_value = {v: (n, k) for n, v, k in port}
    source_id = []
    from_source = [0] * len(src_blocks)
    keys, en, ru, shape, flags, light, base, hard, cls, tier, nodrop = [], [], [], [], [], [], [], [], [], [], []
    faces = {f: [] for f in FACE_KEYS}
    canonical = {}
    for n, v, k in port:
        if n not in LEGACY_DUPLICATES:
            canonical[k] = v
    for k, v in canonical.items():
        from_source[by_key[k]['id']] = v
    for v in range(count):
        n, k = by_value[v]
        b = by_key[k]
        source_id.append(str(b['id']))
        keys.append(cs_str(k))
        en.append(cs_str(b['en']))
        ru.append(cs_str(b['ru']))
        sh = SOURCE_SHAPE[b['shape']]
        if b['cross'] and sh == 'Cube':
            sh = 'Cross'
        is_bed = k == 'BED' or k.startswith('BED_')
        if is_bed:
            sh = 'Bed'
        if k == 'CHEST':
            sh = 'Chest'
        shape.append(str(SHAPES.index(sh)))
        f = 0
        vals = {
            'FullOpaque': b['fullOpaque'], 'Cross': b['cross'], 'Aquatic': b['aquatic'], 'ClassicLeaf': b['classicLeaf'],
            'Water': b['water'], 'Lava': b['lava'], 'Glass': b['glass'], 'Glassy': b['glassy'],
            'TransparentLayer': b['transparentLayer'], 'NeedsWater': b['needsWater'], 'Hang': b['hang'],
            'Climb': b['climb'], 'Axis': b['axis'], 'EmissiveBoost': b['emissiveBoost'], 'Falling': b['falling'],
            'LightBlock': b['lightBlock'], 'XrayOre': b['xrayClass'] == 2, 'XrayClass': b['xrayClass'] > 0,
            'SolidRender': b['solidRender'], 'Bed': is_bed, 'Chest': k == 'CHEST', 'Shaped': b['shape'] is not None,
        }
        for i, fl in enumerate(FLAGS):
            if vals[fl]:
                f |= 1 << i
        flags.append(str(f) + 'u')
        light.append(str(b['light'] or 0))
        base.append(str(from_source[b['baseBlock']] if b['baseBlock'] is not None else 0))
        fc = b['faces'] or {}
        for fk in FACE_KEYS:
            t = fc.get(fk)
            faces[fk].append(str(t if t is not None else -1))
        m = b['mine'] or {}
        hard.append(f"{float(m.get('hard', 1)):g}f")
        cls.append(str({None: 0, 'pickaxe': 1, 'axe': 2, 'shovel': 3}[m.get('cls')]))
        tier.append(str(m.get('tier', 0)))
        nodrop.append('true' if m.get('noDrop') else 'false')

    g = ['// <auto-generated> by tools/codegen/gen_blocks.py from reference/data/blocks.json. Do not edit by hand.',
         '// Every value was computed by evaluating the reference implementation (meshWorker.js tables, main.js',
         '// runtime M/_u/Z6), not transcribed by hand.',
         'namespace BlockcraftPort', '{', '    public static class SourceBlockData', '    {',
         f'        public const int Count = {count};',
         f'        public const int SourceCount = {len(src_blocks)};',
         f'        public const int AtlasWidth = {atlas["w"]}, AtlasHeight = {atlas["h"]}, AtlasTile = {atlas["tile"]}, AtlasColumns = {atlas["cols"]};',
         '']
    for i, fl in enumerate(FLAGS):
        g.append(f'        public const uint F{fl} = 1u << {i};')
    g.append('')
    g.append('        /// <summary>Reference numeric id (main.js o[KEY]) of each port BlockId.</summary>')
    g.append('        public static readonly short[] SourceId = ' + cs_array('short', source_id) + ';')
    g.append('        /// <summary>Port BlockId for each reference numeric id.</summary>')
    g.append('        public static readonly ushort[] FromSource = ' + cs_array('ushort', [str(x) for x in from_source]) + ';')
    g.append('        public static readonly string[] Key = ' + cs_array('string', keys, 8) + ';')
    g.append('        public static readonly string[] NameEn = ' + cs_array('string', en, 8) + ';')
    g.append('        public static readonly string[] NameRu = ' + cs_array('string', ru, 8) + ';')
    g.append('        /// <summary>BlockShape of each block (meshWorker h[] / cross tables).</summary>')
    g.append('        public static readonly byte[] Shape = ' + cs_array('byte', shape, 40) + ';')
    g.append('        public static readonly uint[] Flags = ' + cs_array('uint', flags, 24) + ';')
    g.append('        /// <summary>Block light emission (main.js _u).</summary>')
    g.append('        public static readonly byte[] Light = ' + cs_array('byte', light, 40) + ';')
    g.append('        /// <summary>Port id of the block whose textures/mining a shaped block inherits (main.js Te).</summary>')
    g.append('        public static readonly ushort[] BaseBlock = ' + cs_array('ushort', base, 30) + ';')
    for fk in FACE_KEYS:
        nm = 'Tile' + fk[0].upper() + fk[1:]
        g.append(f'        public static readonly short[] {nm} = ' + cs_array('short', faces[fk], 30) + ';')
    g.append('        /// <summary>main.js Z6(): hardness, tool class (0 none,1 pickaxe,2 axe,3 shovel), tier, noDrop.</summary>')
    g.append('        public static readonly float[] Hardness = ' + cs_array('float', hard, 24) + ';')
    g.append('        public static readonly byte[] ToolClass = ' + cs_array('byte', cls, 40) + ';')
    g.append('        public static readonly byte[] ToolTier = ' + cs_array('byte', tier, 40) + ';')
    g.append('        public static readonly bool[] NoDrop = ' + cs_array('bool', nodrop, 20) + ';')
    g.append('')
    g.append('        public static bool Has(BlockId id, uint flag) => (uint)id < (uint)Count && (Flags[(int)id] & flag) != 0;')
    g.append('        public static BlockId FromSourceId(int sourceId) => (uint)sourceId < (uint)FromSource.Length ? (BlockId)FromSource[sourceId] : BlockId.Air;')
    g.append('        public static int ToSourceId(BlockId id) => (uint)id < (uint)Count ? SourceId[(int)id] : 0;')
    g.append('    }')
    g.append('}')
    open(os.path.join(PORT, 'BlockcraftPort', 'Core', 'SourceBlockData.g.cs'), 'w', encoding='utf-8').write('\n'.join(g) + '\n')

    # ---- atlas
    av = json.load(open(os.path.join(REF, 'atlas_sources.json')))
    file_tile = {}
    for e in av:
        name = e['file'][:-4] if e['file'].endswith('.png') else e['file']
        file_tile.setdefault(name, e['tile'])
    old_layout = json.load(open(os.path.join(PORT, 'Resources', 'Voxel', 'atlas_layout.json')))
    names = list(old_layout['tiles'].keys())
    special = {'sunflower_top': by_key['SUNFLOWER']['faces']['top']}
    W, H, T, C = atlas['w'], atlas['h'], atlas['tile'], atlas['cols']

    def rect(t):
        x, y = (t % C) * T, (t // C) * T
        u0, u1 = (x + 0.06) / W, (x + T - 0.06) / W
        # Unity samples v from the bottom row of the PNG; flip so AtlasRect keeps its V0<V1 convention.
        v1, v0 = 1 - (y + 0.06) / H, 1 - (y + T - 0.06) / H
        return u0, v0, u1, v1

    def fl(v):
        return repr(float(v)) + 'f'

    a = ['// <auto-generated> by tools/codegen/gen_blocks.py for the reference atlas (main.js $l()+Av()).',
         '// Tile t sits at column t%16, row t/16 in 16px cells; UVs use the meshWorker N()/v() 0.06px inset.',
         'namespace BlockcraftPort', '{',
         '    public readonly struct AtlasRect', '    {', '        public readonly float U0, V0, U1, V1;',
         '        public AtlasRect(float u0, float v0, float u1, float v1) { U0=u0; V0=v0; U1=u1; V1=v1; }', '    }', '',
         '    public static class AtlasLayout', '    {',
         f'        public const int AtlasWidth = {W};', f'        public const int AtlasHeight = {H};',
         f'        public const int TileSize = {T};', f'        public const int Columns = {C};',
         f'        public const int TileCount = {(W // T) * (H // T)};',
         '        static readonly AtlasRect[] tileRects = BuildTiles();',
         '        static AtlasRect[] BuildTiles()', '        {',
         '            var r = new AtlasRect[TileCount];',
         '            for (int t = 0; t < TileCount; t++)', '            {',
         '                int x = (t % Columns) * TileSize, y = (t / Columns) * TileSize;',
         '                r[t] = new AtlasRect((x + .06f) / AtlasWidth, 1f - (y + TileSize - .06f) / AtlasHeight,',
         '                                     (x + TileSize - .06f) / AtlasWidth, 1f - (y + .06f) / AtlasHeight);',
         '            }', '            return r;', '        }',
         '        /// <summary>UV rect of reference atlas tile t (main.js M[id].top/side/... values).</summary>',
         '        public static AtlasRect Tile(int t) => (uint)t < (uint)TileCount ? tileRects[t] : tileRects[0];', '']

    # Existing public names: map each through its old rect (old atlas) or its generated comment (re-run).
    old_cs = open(os.path.join(PORT, 'BlockcraftPort', 'Rendering', 'AtlasLayout.cs'), encoding='utf-8').read()
    named = []  # (csName, tile)
    for m in re.finditer(r'public static readonly AtlasRect (\w+) = (.*?);(?:[ \t]*//[ \t]*(\S+))?', old_cs):
        cs_name, expr = m.group(1), m.group(2)
        tm = re.fullmatch(r'Tile\((\d+)\)', expr.strip())
        if tm:
            named.append((cs_name, int(tm.group(1)), m.group(3)))
            continue
        nums = [float(x.rstrip('f')) for x in re.findall(r'[-0-9.]+f', expr)]
        src_name = None
        for jn, r in old_layout['tiles'].items():
            if all(abs(a_ - b_) < 1e-6 for a_, b_ in zip(r, nums)):
                src_name = jn
                break
        if src_name is not None:
            t = special.get(src_name, file_tile.get(src_name))
        else:
            # R56 added two particle cells that are not in atlas_layout.json.
            t = {'Bricks': by_key['BRICK']['faces']['top'], 'RedWool': by_key['WOOL_RED']['faces']['top']}.get(cs_name)
            src_name = cs_name
        if t is None:
            raise SystemExit('atlas tile not found for ' + cs_name)
        named.append((cs_name, t, src_name))
    for cs_name, t, src_name in named:
        a.append(f'        public static readonly AtlasRect {cs_name} = Tile({t}); // {src_name}')
    tiles = {n: special.get(n, file_tile.get(n)) for n in names}
    a.append('    }')
    a.append('}')
    open(os.path.join(PORT, 'BlockcraftPort', 'Rendering', 'AtlasLayout.cs'), 'w', encoding='utf-8').write('\n'.join(a) + '\n')

    layout = {'width': W, 'height': H, 'tile': T, 'columns': C, 'tiles': {n: list(rect(tiles[n])) for n in names}}
    json.dump(layout, open(os.path.join(PORT, 'Resources', 'Voxel', 'atlas_layout.json'), 'w'), separators=(',', ':'))
    shutil.copyfile(os.path.join(REF, 'atlas.png'), os.path.join(PORT, 'Resources', 'Voxel', 'atlas.png'))

    print(f'BlockId: {len(existing)} existing + {len(appended)} appended = {count}')


if __name__ == '__main__':
    main()
