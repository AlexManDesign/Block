// Generates a block placement / use / break / support scenario shared by the reference runner
// (ref_place.js, evaluated inside the headless reference game) and the C# port (PlaceParity).
// Coordinates are SOURCE coordinates relative to each test's 6x6 cell; the runners add the cell
// origin. Every test starts from a cleared cell with a 4x4 stone floor at y=0 (unless noFloor).
import fs from 'node:fs';
const tests = [];
const yaws = [0, 0.4, 1.2, Math.PI / 2, 2.2, 3.0, Math.PI, 3.7, 4.5, 5.2, 6.0, -0.8, 0.79, 2.36, 3.93, 5.5];
const R = (x, y, z, id, meta = 0) => ({ op: 'raw', x, y, z, id, meta });
const click = (held, yaw, hit, k, h, extra = {}) => ({ op: 'click', held, yaw, hit, k, h, ...extra });
const brk = (hit) => ({ op: 'break', hit });
const edit = (x, y, z, id, meta = 0) => ({ op: 'edit', x, y, z, id, meta });
function T(name, fixture, actions, opts = {}) { tests.push({ name, fixture, actions, ...opts }); }

// Click helpers (block (1,0,1) is the floor under the placement cell (1,1,1)).
const top = (held, yaw, hx = 1.37, hz = 1.61) => click(held, yaw, [1, 0, 1], [0, 1, 0], [hx, 1.0, hz]);
const PILLAR = [R(1, 1, 1, 'STONE'), R(1, 2, 1, 'STONE')];
const CEIL = [R(1, 3, 1, 'STONE')];
// side of the pillar at (1,1,1): outward direction d, hit height fraction fy
const SIDES = [[1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1]];
function side(held, yaw, d, fy = .3, fu = .4) {
  const hit = [1, 1, 1];
  const h = [1 + .5 + d[0] * .5 + (d[0] ? 0 : fu - .5), 1 + fy, 1 + .5 + d[2] * .5 + (d[2] ? 0 : fu - .5)];
  return click(held, yaw, hit, d, h);
}
const ceil = (held, yaw) => click(held, yaw, [1, 3, 1], [0, -1, 0], [1.4, 3.0, 1.6]);

const SHAPED_TOP = ['OAK_STAIRS', 'OAK_TRAPDOOR', 'OAK_GATE', 'RAIL', 'OAK_FENCE', 'COBBLE_WALL', 'GLASS_PANE', 'WHITE_CARPET',
  'STONE_PRESSURE_PLATE', 'SHIP_WHEEL', 'FLOWER_POT', 'TORCH', 'STONE_BUTTON', 'LADDER', 'OAK_DOOR', 'SPRUCE_DOOR'];
for (const held of SHAPED_TOP) for (const yaw of yaws) T(`${held}/top/yaw${yaw.toFixed(2)}`, [], [top(held, yaw)]);
for (const held of ['OAK_STAIRS', 'OAK_TRAPDOOR', 'TORCH', 'LADDER', 'STONE_BUTTON', 'OAK_GATE', 'VINE', 'GLOW_LICHEN', 'LOG', 'BIRCH_LOG', 'STONE_SLAB'])
  for (const d of SIDES) for (const fy of [.2, .8]) for (const yaw of [0.3, 2.0, 4.1])
    T(`${held}/side${d}/fy${fy}/yaw${yaw}`, PILLAR, [side(held, yaw, d, fy)]);
for (const held of ['OAK_STAIRS', 'OAK_TRAPDOOR', 'TORCH', 'STONE_BUTTON', 'STONE_SLAB', 'LOG', 'GLOW_LICHEN', 'CAVE_VINES', 'LADDER'])
  for (const yaw of [0.3, 3.3]) T(`${held}/ceiling/yaw${yaw}`, CEIL, [ceil(held, yaw)]);
// Torch / button / ladder on a side face of a non-plain block (slab pillar): must fail.
for (const held of ['TORCH', 'LADDER', 'STONE_BUTTON'])
  T(`${held}/side-of-slab`, [R(1, 1, 1, 'STONE_SLAB', 1)], [side(held, 1, [1, 0, 0])]);

// Slabs: bottom, top by hit height, merge by clicking the slab, merge into the other half via a side click.
T('slab/merge-top-click', [], [top('STONE_SLAB', 0), click('STONE_SLAB', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.5, 1.5])]);
T('slab/merge-ceiling', CEIL, [ceil('STONE_SLAB', 0), click('STONE_SLAB', 0, [1, 2, 1], [0, -1, 0], [1.5, 2.5, 1.5])]);
T('slab/side-into-other-half', [...PILLAR, R(2, 1, 1, 'STONE_SLAB', 0)], [side('STONE_SLAB', 0, [1, 0, 0], .8)]);
T('slab/side-into-same-half', [...PILLAR, R(2, 1, 1, 'STONE_SLAB', 0)], [side('STONE_SLAB', 0, [1, 0, 0], .2)]);
T('slab/other-slab-type', [R(1, 1, 1, 'OAK_SLAB', 0)], [click('STONE_SLAB', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.5, 1.5])]);

// Doors: hinge from neighbours / walls / hit point; blocked above; toggle both halves.
for (const yaw of [0, Math.PI / 2, Math.PI, 4.71]) for (const hx of [1.1, 1.9]) for (const hz of [1.1, 1.9])
  T(`door/hitpoint/yaw${yaw}/h${hx},${hz}`, [], [top('OAK_DOOR', yaw, hx, hz)]);
for (const yaw of [0, Math.PI / 2, Math.PI, 4.71]) for (const w of [[2, 1], [0, 1], [1, 2], [1, 0]])
  T(`door/wall/yaw${yaw}/w${w}`, [R(w[0], 1, w[1], 'STONE'), R(w[0], 2, w[1], 'STONE')], [top('OAK_DOOR', yaw)]);
for (const yaw of [0, Math.PI / 2, Math.PI, 4.71]) for (const w of [[2, 1], [0, 1], [1, 2], [1, 0]])
  T(`door/pair/yaw${yaw}/n${w}`, [], [click('OAK_DOOR', yaw, [w[0], 0, w[1]], [0, 1, 0], [w[0] + .5, 1, w[1] + .5]), top('OAK_DOOR', yaw)]);
T('door/blocked-above', [R(1, 2, 1, 'STONE')], [top('OAK_DOOR', 0)]);
T('door/no-support', [R(1, 0, 1, 'AIR'), R(1, -1, 1, 'STONE'), R(1, 0, 1, 'TORCH')], [click('OAK_DOOR', 0, [1, 0, 1], [0, 1, 0], [1.5, 1, 1.5])]);
T('door/toggle', [], [top('OAK_DOOR', 1), click(null, 0, [1, 1, 1], [1, 0, 0], [2, 1.5, 1.5]), click(null, 0, [1, 2, 1], [1, 0, 0], [2, 2.5, 1.5]), click('STONE', 0, [1, 2, 1], [1, 0, 0], [2, 2.5, 1.5])]);
T('trapdoor/toggle', [], [top('OAK_TRAPDOOR', 2), click('STONE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.2, 1.5])]);
T('gate/toggle', [], [top('OAK_GATE', 2), click(null, 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5])]);
T('door/break-lower', [], [top('OAK_DOOR', 1), brk([1, 1, 1])]);
T('door/break-upper', [], [top('OAK_DOOR', 1), brk([1, 2, 1])]);

// Beds.
for (const held of ['BED', 'BED_WHITE', 'BED_BLACK']) for (const yaw of [0, Math.PI / 2, Math.PI, 4.71, 0.7, 2.4])
  T(`${held}/yaw${yaw}`, [], [top(held, yaw)]);
for (const yaw of [0, Math.PI / 2, Math.PI, 4.71]) T(`bed/blocked/yaw${yaw}`, [R(1, 1, 2, 'STONE'), R(1, 1, 0, 'STONE'), R(2, 1, 1, 'STONE')], [top('BED', yaw)]);
T('bed/break-foot', [], [top('BED_WHITE', 0), brk([1, 1, 1])]);
T('bed/break-head', [], [top('BED_WHITE', Math.PI / 2), brk([0, 1, 1]), brk([2, 1, 1])]);

// Axis blocks / leaves / plain.
for (const held of ['LOG', 'LEAVES', 'STONE', 'GRASS', 'SPONGE']) T(`${held}/top`, [], [top(held, 0)]);

// Cross plants, supports and replacement.
for (const [held, ground] of [['POPPY', 'STONE'], ['POPPY', 'GRASS'], ['POPPY', 'DIRT'], ['OAK_SAPLING', 'SAND'], ['OAK_SAPLING', 'PODZOL'],
  ['DEAD_BUSH', 'SAND'], ['DEAD_BUSH', 'STONE_SLAB'], ['TALL_GRASS', 'GRASS'], ['CAVE_VINES', 'STONE'], ['BROWN_MUSHROOM', 'STONE'],
  ['SWEET_BERRY_BUSH', 'GRASS'], ['BAMBOO_PLANT', 'SAND'], ['SUNFLOWER', 'GRASS'], ['SUNFLOWER', 'STONE_SLAB'], ['SEA_PICKLE', 'SAND'],
  ['LILY_PAD', 'STONE'], ['SUGAR_CANE', 'SAND'], ['CACTUS', 'SAND'], ['CACTUS', 'STONE'], ['WHEAT_0', 'FARMLAND']])
  T(`${held}/on-${ground}`, [R(1, 0, 1, ground)], [top(held, 0)]);
T('cross/replace-tall-grass', [R(1, 0, 1, 'GRASS'), R(1, 1, 1, 'TALL_GRASS')], [click('POPPY', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.4, 1.5])]);
T('stone/replace-tall-grass', [R(1, 0, 1, 'GRASS'), R(1, 1, 1, 'TALL_GRASS')], [click('STONE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.4, 1.5])]);
T('sunflower/blocked-above', [R(1, 0, 1, 'GRASS'), R(1, 2, 1, 'STONE')], [top('SUNFLOWER', 0)]);
T('cactus/neighbour', [R(1, 0, 1, 'SAND'), R(2, 1, 1, 'STONE')], [top('CACTUS', 0)]);
T('cactus/stack', [R(1, 0, 1, 'SAND'), R(1, 1, 1, 'CACTUS')], [click('CACTUS', 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5])]);
T('sugarcane/water', [R(1, 0, 1, 'SAND'), R(2, 0, 1, 'WATER')], [top('SUGAR_CANE', 0), click('SUGAR_CANE', 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5])]);
T('bamboo/stack', [R(1, 0, 1, 'SAND'), R(1, 1, 1, 'BAMBOO_PLANT')], [click('BAMBOO_PLANT', 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5])]);
T('cave-vines/under-ceiling', CEIL, [ceil('CAVE_VINES', 0), click('STONE', 0, [1, 2, 1], [1, 0, 0], [2, 2.5, 1.5]), click('STONE', 0, [1, 2, 1], [1, 0, 0], [2, 2.5, 1.5])]);
// Water plants.
const POOL = [R(1, 1, 1, 'WATER'), R(1, 2, 1, 'WATER'), R(0, 1, 1, 'STONE'), R(2, 1, 1, 'STONE'), R(1, 1, 0, 'STONE'), R(1, 1, 2, 'STONE')];
for (const held of ['SEAGRASS', 'KELP', 'SEA_PICKLE', 'LILY_PAD', 'TORCH', 'STONE', 'POPPY', 'BRAIN_CORAL_FAN', 'BRAIN_CORAL_BLOCK'])
  T(`${held}/in-water`, POOL, [top(held, 0)]);
T('kelp/stack-in-water', POOL, [top('KELP', 0), click('KELP', 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5])]);
T('seagrass/in-air', [], [top('SEAGRASS', 0)]);
T('kelp/break-in-water', POOL, [top('KELP', 0), brk([1, 1, 1])]);
T('sea-pickle/stack', [R(1, 0, 1, 'SAND')], [top('SEA_PICKLE', 0), click('SEA_PICKLE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5]), click('SEA_PICKLE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5]), click('SEA_PICKLE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5]), click('SEA_PICKLE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5])]);
// Vines / lichen with several supports.
T('vine/corner', [R(2, 1, 1, 'STONE'), R(1, 1, 2, 'STONE'), R(1, 2, 1, 'STONE')], [side('VINE', 0, [-1, 0, 0])].map(a => ({ ...a, hit: [2, 1, 1], h: [2, 1.5, 1.5] })));
T('lichen/corner', [R(2, 1, 1, 'STONE'), R(1, 1, 2, 'STONE'), R(1, 2, 1, 'STONE')], [side('GLOW_LICHEN', 0, [-1, 0, 0])].map(a => ({ ...a, hit: [2, 1, 1], h: [2, 1.5, 1.5] })));
T('vine/nothing', [], [top('VINE', 0)]);

// Flower pot.
T('pot/fill-empty', [], [top('FLOWER_POT', 0), click('POPPY', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5]), click('STONE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5]), click('OAK_SAPLING', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5])]);
T('pot/non-plant', [], [top('FLOWER_POT', 0), click('STONE', 0, [1, 1, 1], [0, 1, 0], [1.5, 1.3, 1.5])]);

// Player overlap (feet at the placement cell).
for (const held of ['STONE', 'TORCH', 'OAK_STAIRS', 'LADDER', 'RAIL', 'WATER', 'POPPY'])
  T(`${held}/player-overlap`, [R(1, 0, 1, 'GRASS')], [top(held, 0)], { player: [1.5, 1, 1.5] });
T('bed/player-overlap-head', [], [top('BED', 0)], { player: [1.5, 1, 2.6] });

// Support loss after edits (main Qu / ri / Ai / ei / DI).
T('support/wall-torch', PILLAR, [side('TORCH', 0, [1, 0, 0]), edit(1, 1, 1, 'AIR')]);
T('support/standing-torch', [], [top('TORCH', 0), edit(1, 0, 1, 'AIR')]);
T('support/ladder', PILLAR, [side('LADDER', 0, [0, 0, 1]), edit(1, 1, 1, 'AIR')]);
T('support/button-ceiling', CEIL, [ceil('STONE_BUTTON', 0), edit(1, 3, 1, 'AIR')]);
T('support/vine-partial', [R(2, 1, 1, 'STONE'), R(1, 1, 2, 'STONE')], [click('VINE', 0, [2, 1, 1], [-1, 0, 0], [2, 1.5, 1.5]), edit(2, 1, 1, 'AIR'), edit(1, 1, 2, 'AIR')]);
T('support/sunflower', [R(1, 0, 1, 'GRASS')], [top('SUNFLOWER', 0), edit(1, 0, 1, 'AIR')]);
T('support/sunflower-top', [R(1, 0, 1, 'GRASS')], [top('SUNFLOWER', 0), edit(1, 2, 1, 'AIR')]);
T('support/sugarcane-column', [R(1, 0, 1, 'SAND'), R(2, 0, 1, 'WATER'), R(1, 1, 1, 'SUGAR_CANE'), R(1, 2, 1, 'SUGAR_CANE'), R(1, 3, 1, 'SUGAR_CANE')], [edit(1, 0, 1, 'AIR')]);
T('support/sugarcane-water', [R(1, 0, 1, 'SAND'), R(2, 0, 1, 'WATER'), R(1, 1, 1, 'SUGAR_CANE')], [edit(2, 0, 1, 'STONE')]);
T('support/poppy', [R(1, 0, 1, 'GRASS')], [top('POPPY', 0), edit(1, 0, 1, 'STONE')]);
T('support/cave-vines', CEIL, [ceil('CAVE_VINES', 0), edit(1, 3, 1, 'AIR')]);
T('support/door-floor', [], [top('OAK_DOOR', 0), edit(1, 0, 1, 'AIR')]);
T('support/rail', [], [top('RAIL', 0), edit(1, 0, 1, 'AIR')]);
T('support/carpet', [], [top('WHITE_CARPET', 0), edit(1, 0, 1, 'AIR')]);
T('support/kelp', POOL, [top('KELP', 0), click('KELP', 0, [1, 1, 1], [0, 1, 0], [1.5, 2, 1.5]), edit(1, 0, 1, 'AIR')]);
T('support/lily', POOL, [top('LILY_PAD', 0), edit(1, 2, 1, 'AIR')]);
T('cactus/neighbour-edit', [R(1, 0, 1, 'SAND'), R(1, 1, 1, 'CACTUS'), R(1, 2, 1, 'CACTUS')], [edit(2, 1, 1, 'STONE')]);
T('cactus/sand-removed', [R(1, 0, 1, 'SAND'), R(1, 1, 1, 'CACTUS'), R(1, 2, 1, 'CACTUS')], [edit(1, 0, 1, 'GLASS')]);
T('concrete/powder-water', [R(1, 1, 1, 'WHITE_CONCRETE_POWDER'), R(3, 1, 1, 'RED_CONCRETE_POWDER')], [edit(2, 1, 1, 'WATER'), edit(1, 2, 1, 'WATER')]);
T('concrete/place-by-water', [R(2, 1, 1, 'WATER')], [top('WHITE_CONCRETE_POWDER', 0)]);
T('coral/dies-in-air', [], [top('BRAIN_CORAL_BLOCK', 0), top('TUBE_CORAL_FAN', 0)]);
T('coral/lives-in-water', POOL, [edit(1, 1, 1, 'FIRE_CORAL_BLOCK'), edit(1, 2, 1, 'AIR')]);
const LAKE = []; for (let x = 0; x < 6; x++) for (let z = 0; z < 6; z++) for (let y = 1; y <= 3; y++) LAKE.push(R(x, y, z, 'WATER'));
T('sponge/absorb', LAKE, [edit(2, 2, 2, 'SPONGE')], { size: [6, 5, 6] });
T('sponge/water-arrives', [R(1, 1, 1, 'SPONGE')], [edit(2, 1, 1, 'WATER')]);

const out = { cell: [6, 6, 6], tests };
fs.writeFileSync(process.argv[2] || 'scenario.json', JSON.stringify(out));
console.log('tests:', tests.length);
