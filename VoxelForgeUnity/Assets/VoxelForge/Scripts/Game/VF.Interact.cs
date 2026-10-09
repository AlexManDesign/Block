// Voxel Forge — Unity port. Breaking, mining, melee, block interaction, placement, buckets, TNT.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoxelForge
{
    public sealed class PrimedTnt { public double x, y, z, t; }
    public sealed class FallingBlock { public double x, y, z, vy; public int id; public JObj meta; }
    public struct DamageResult { public bool dead; public string loot; }

    public static partial class VF
    {
        // ---------------- breaking ----------------
        static bool virtualHarvestable(VirtualDef d)
        {
            if (player.creative || d == null || d.tier == 0) return true;
            var t = currentTool();
            return t != null && t.tool == d.tool && t.tier >= d.tier;
        }
        static bool destroyVirtualBlock(Hit h, bool byExplosion = false)
        {
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj(); var d = virtualDefFromMeta(m);
            if (d == null) return false;
            string key = virtualDropKey(m.Str("v")); int particleTile = virtualBreakParticleTile(h, d, m);
            if (!byExplosion) { sfxBlockBreak(B.VIRTUAL_OPAQUE); spawnBreakParticlesTile(h.x, h.y, h.z, particleTile, 10, breakParticleFaceShade(h)); }
            else if (JS.random() < 0.12) spawnBreakParticlesTile(h.x, h.y, h.z, particleTile, 2, breakParticleFaceShade(h));
            int restore = d.waterPlant || m.Bool("waterlogged") ? B.WATER : B.AIR;
            if (m.Bool("bedPart") && finKey(m, "mateX") && finKey(m, "mateZ"))
            {
                int mx = m.Int("mateX"), mz = m.Int("mateZ"), mid = getBlock(mx, h.y, mz); var mm = getBlockMeta(mx, h.y, mz) ?? new JObj();
                if (m.Bool("nativeBed") && mid == B.BED) { setBlock(mx, h.y, mz, B.AIR); key = BK(B.BED); }
                else if (isVirtualId(mid) && mm.Bool("bedPart"))
                {
                    setBlock(mx, h.y, mz, B.AIR);
                    var v = m.Str("v") ?? "";
                    if (v.StartsWith("BED_HEAD", StringComparison.Ordinal)) key = VK("BED" + v.Substring(8));
                }
            }
            else if (d.shape == "door" || d.shape == "tallplant")
            {
                int oy = m.Str("part") == "top" ? -1 : 1; var om = getBlockMeta(h.x, h.y + oy, h.z); int oid = getBlock(h.x, h.y + oy, h.z);
                if (isVirtualId(oid) && om != null && om.Str("v") == m.Str("v")) setBlock(h.x, h.y + oy, h.z, restore);
            }
            if (m.Bool("potKey")) dropPotContent(m, h.x, h.y, h.z);
            setBlock(h.x, h.y, h.z, restore);
            validateAttachmentsAround(h.x, h.y, h.z);
            if (key != null && !player.creative && !byExplosion && virtualHarvestable(d)) spawnWorldDrop(key, 1, h.x + 0.5, h.y + 0.55, h.z + 0.5);
            if (!player.creative && !byExplosion && currentTool() != null) damageHeldTool(1);
            inventoryDirty = true;
            saveGameSoon();
            drawHotbar();
            return true;
        }
        public static bool destroyBlock(Hit h, bool byExplosion = false)
        {
            if (h == null || h.id == B.BEDROCK) return false;
            if (isVirtualId(h.id)) return destroyVirtualBlock(h, byExplosion);
            int id = h.id, dropId = id;
            var oldMeta = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            if (!byExplosion) sfxBlockBreak(id);
            if (byExplosion) { if (JS.random() < 0.12) spawnBreakParticles(h, id, 2, oldMeta); }
            else spawnBreakParticles(h, id, 10, oldMeta);
            if (id == B.DOOR || id == B.DOOR_OPEN)
            {
                int top = getBlock(h.x, h.y + 1, h.z);
                if (top == B.DOOR_TOP || top == B.DOOR_OPEN_TOP) setBlock(h.x, h.y + 1, h.z, B.AIR);
                dropId = B.DOOR;
            }
            else if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP)
            {
                int bot = getBlock(h.x, h.y - 1, h.z);
                if (bot == B.DOOR || bot == B.DOOR_OPEN) { setBlock(h.x, h.y - 1, h.z, B.AIR); dropId = B.DOOR; }
            }
            else if (id == B.BED)
            {
                var bm = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
                if (finKey(bm, "mateX") && finKey(bm, "mateZ"))
                {
                    int mx = bm.Int("mateX"), mz = bm.Int("mateZ"), mid = getBlock(mx, h.y, mz); var mm = getBlockMeta(mx, h.y, mz) ?? new JObj();
                    if (isVirtualId(mid) && mm.Bool("nativeBed")) setBlock(mx, h.y, mz, B.AIR);
                }
            }
            cleanupContainerAt(h.x, h.y, h.z, id);
            if (id == B.FLOWER_POT && oldMeta.Bool("potKey")) dropPotContent(oldMeta, h.x, h.y, h.z);
            setBlock(h.x, h.y, h.z, blocks[id].waterPlant ? B.WATER : B.AIR);
            validateAttachmentsAround(h.x, h.y, h.z);
            if (!player.creative && !byExplosion)
            {
                var drops = blockDrops(dropId);
                foreach (var dr in drops) spawnWorldDrop(dr.k, dr.n, h.x + 0.5, h.y + 0.55, h.z + 0.5);
                if (currentTool() != null) damageHeldTool(1);
                toast(drops.Count > 0 ? "Выпало: " + string.Join(", ", drops.Select(dr => idef(dr.k).name + " ×" + dr.n)) : "Блок разрушен — дропа нет");
            }
            inventoryDirty = true;
            saveGameSoon();
            drawHotbar();
            return true;
        }

        // ---------------- mining state ----------------
        public static bool mouseL = false, bowCharging = false;
        public static string miningTarget = "";
        public static double miningProgress = 0, lastCreativeBreakAt = -1e9, bowCharge = 0, bowChargeStartedAt = 0;
        public const double CREATIVE_REPEAT_MS = 260;
        // HUD bar state (minebar element)
        public static bool mineBarVisible; public static double mineBarFrac;
        public static void resetMining() { miningTarget = ""; miningProgress = 0; updateMineBar(); }
        public static void updateMineBar()
        {
            bool bow = bowCharging && mouseL && !uiOpen && !player.dead, active = bow || (!player.creative && miningProgress > 0 && mouseL && !uiOpen && !player.dead);
            mineBarVisible = active;
            mineBarFrac = Math.Min(100, (bow ? bowCharge : miningProgress) * 100) / 100;
        }
        public static readonly string[] SHEEP_COLORS = { "white", "black", "gray", "light_gray", "brown", "pink" };
        public static string randomSheepColorSource()
        {
            double r = JS.random();
            return r < 0.05 ? "black" : r < 0.1 ? "gray" : r < 0.15 ? "light_gray" : r < 0.18 ? "brown" : r < 0.1816 ? "pink" : "white";
        }
        public static string sheepWoolKey(Mob m)
        {
            var c = (m != null ? m.color : null) ?? "white";
            int id = c == "black" ? B.WOOL_BLACK : c == "gray" ? B.WOOL_GRAY : c == "light_gray" ? B.WOOL_LIGHT_GRAY : c == "brown" ? B.WOOL_BROWN : c == "pink" ? B.WOOL_PINK : B.WOOL_WHITE;
            return BK(id);
        }
        static List<ItemCount> mobDropList(Mob m)
        {
            var a = new List<ItemCount>();
            switch (m.type)
            {
                case "cow": a.Add(new ItemCount("beef", 1 + (JS.random() < 0.5 ? 1 : 0))); if (JS.random() < 0.7) a.Add(new ItemCount("leather", 1)); return a;
                case "pig": a.Add(new ItemCount("pork", 1 + (JS.random() < 0.5 ? 1 : 0))); return a;
                case "sheep": a.Add(new ItemCount("mutton", 1)); if (!m.sheared) a.Add(new ItemCount(sheepWoolKey(m), 1)); return a;
                case "chicken": a.Add(new ItemCount("chicken", 1)); if (JS.random() < 0.6) a.Add(new ItemCount("feather", 1 + (JS.random() < 0.4 ? 1 : 0))); return a;
                case "salmon": a.Add(new ItemCount(m.burnT > 0 || m.lavaT > 0 ? "cooked_salmon" : "salmon", 1)); return a;
                case "shark": a.Add(new ItemCount(m.burnT > 0 || m.lavaT > 0 ? "cooked_fish" : "fish", randInt(1, 2))); if (JS.random() < 0.35) a.Add(new ItemCount("bone", 1)); return a;
                case "zombie":
                    {
                        a.Add(new ItemCount("rotten_flesh", 1 + (JS.random() < 0.5 ? 1 : 0))); double r = JS.random();
                        if (r < 0.05) a.Add(new ItemCount("carrot", 1)); else if (r < 0.1) a.Add(new ItemCount("potato", 1));
                        return a;
                    }
                case "skeleton": a.Add(new ItemCount("bone", 1 + (JS.random() < 0.5 ? 1 : 0))); if (JS.random() < 0.5) a.Add(new ItemCount("arrow", 1 + (int)Math.Floor(JS.random() * 2))); return a;
                case "creeper": a.Add(new ItemCount("gunpowder", 1 + (int)Math.Floor(JS.random() * 2))); return a;
                case "spider": { int n = (int)Math.Floor(JS.random() * 3); if (n != 0) a.Add(new ItemCount("string", n)); if (JS.random() < 0.33) a.Add(new ItemCount("spider_eye", 1)); return a; }
                case "enderman": { int n = (int)Math.Floor(JS.random() * 2); if (n != 0) a.Add(new ItemCount("ender_pearl", n)); return a; }
                case "slime_small": { int n = (int)Math.Floor(JS.random() * 3); if (n != 0) a.Add(new ItemCount("slime_ball", n)); return a; }
                default: return a;
            }
        }
        public static string mobDrops(Mob m)
        {
            var a = mobDropList(m);
            foreach (var q in a) spawnWorldDrop(q.k, q.n, m.x, m.y + 0.55, m.z);
            return a.Count > 0 ? string.Join(", ", a.Select(q => idef(q.k).name + " ×" + q.n)) : "ничего";
        }
        public static string killMob(Mob m, bool withDrops = true)
        {
            if (m == null || m.dead) return "";
            m.lastHitByPlayer = withDrops && !player.creative;
            return finishMobDeath(m, false);
        }

        // ---------------- melee attack charge ----------------
        public static double attackLastAt = 0; public static int attackSlot = -1; public static string attackKey = ""; public static bool attackPrimed = false; public static int attackMeterToken = 0;
        // attack meter HUD (animated bar 0→100% over 1/speed seconds)
        public static bool attackMeterVisible; public static double attackMeterStart, attackMeterDur;
        static ItemDef combatHeldDef() { var st = selectedStack(); return st != null ? idef(st.key) : null; }
        static void syncAttackSelection(double now)
        {
            var st = selectedStack(); var key = st != null ? st.key : "";
            if (selected != attackSlot || key != attackKey) { attackSlot = selected; attackKey = key; attackLastAt = now; attackPrimed = false; hideAttackMeter(); }
        }
        static double currentAttackSpeed() { syncAttackSelection(JS.now()); var d = combatHeldDef(); return d != null && d.attackSpeed != 0 ? d.attackSpeed : 4; }
        static double currentAttackCharge(double now) { syncAttackSelection(now); return Math.Max(0, Math.Min(1, (now - attackLastAt) / 1000 * currentAttackSpeed())); }
        static double currentAttackScale(double now) { double q = currentAttackCharge(now); return 0.2 + 0.8 * q * q; }
        static void hideAttackMeter() { attackMeterVisible = false; }
        static void animateAttackMeter(double speed)
        {
            if (player.creative) return;
            int token = ++attackMeterToken; double dur = 1 / Math.Max(0.01, speed);
            attackMeterVisible = true; attackMeterStart = JS.now(); attackMeterDur = dur;
            Timers.setTimeout(() => { if (token == attackMeterToken) attackMeterVisible = false; }, dur * 1000 + 90);
        }
        static void consumeAttackCharge(double now)
        {
            syncAttackSelection(now);
            attackLastAt = now; attackPrimed = true;
            var d = combatHeldDef();
            if (d != null && d.attackSpeed != 0) animateAttackMeter(d.attackSpeed);
        }
        static bool attackAutoReady() { double now = JS.now(); syncAttackSelection(now); return !attackPrimed || currentAttackCharge(now) >= 1; }
        public static bool attackMob(MobHit hit)
        {
            if (hit == null) return false;
            var m = hit.m; MobInfo info;
            if (!MOB_INFO.TryGetValue(m.type, out info) || m.dead) return false;
            double now = JS.now(); var t = currentTool();
            double bse = player.creative ? 5 : t != null && t.hasDamage ? t.damage : 1, damage = player.creative ? bse : bse * currentAttackScale(now);
            if (!player.creative) consumeAttackCharge(now);
            var r = damageMobSource(m, damage, new[] { player.x, player.y, player.z }, true, false);
            if (t != null && !player.creative)
            {
                var kind = t.tool; int cost = kind == "sword" || kind == "hoe" ? 1 : kind == "shears" || kind == "bow" ? 0 : 2;
                if (cost != 0) damageHeldTool(cost);
            }
            if (r.dead) toast(player.creative ? "Существо убито" : "Добыто: " + (string.IsNullOrEmpty(r.loot) ? "ничего" : r.loot));
            else toast(m.type + ": " + Math.Max(0, Math.Ceiling(m.hp)) + "/" + info.hp);
            return true;
        }
        public static void primaryAction()
        {
            pulseMainHand();
            var ss = selectedStack();
            if (ss != null && ss.key == "bow") { beginBowCharge(); resetMining(); return; }
            var b = raycast(); var m = raycastMob(); var vh = raycastVehicleHit();
            if (vh != null && (b == null || vh.t < b.t) && (m == null || vh.t < m.t)) { destroyVehicle(vh.v); resetMining(); return; }
            if (m != null && (b == null || m.t < b.t)) { attackMob(m); resetMining(); return; }
            if (b == null || b.id == B.BEDROCK) { resetMining(); return; }
            if (player.creative)
            {
                withPlayerEdit(() => destroyBlock(b));
                lastCreativeBreakAt = JS.now();
                resetMining();
                return;
            }
            miningTarget = key3(b.x, b.y, b.z);
            miningProgress = 0;
            updateMineBar();
        }
        public static void processMining(double dt)
        {
            if (bowCharging)
            {
                var st = selectedStack();
                if (mouseL && !uiOpen && !player.dead && st != null && st.key == "bow")
                {
                    bowCharge = Math.Min(1, Math.Max(0, (JS.now() - bowChargeStartedAt) / 1000));
                    updateMineBar();
                    return;
                }
                cancelBowCharge();
                return;
            }
            if (!mouseL || uiOpen || player.dead) { if (miningProgress != 0 || miningTarget != "") resetMining(); return; }
            var b = raycast(); var m = raycastMob();
            if (m != null && (b == null || m.t < b.t))
            {
                if (player.creative)
                {
                    double now = JS.now();
                    if (now - lastCreativeBreakAt >= CREATIVE_REPEAT_MS) { attackMob(m); lastCreativeBreakAt = now; }
                }
                else if (attackAutoReady()) attackMob(m);
                resetMining();
                return;
            }
            if (player.creative)
            {
                double now = JS.now();
                if (now - lastCreativeBreakAt < CREATIVE_REPEAT_MS) return;
                if (b != null && b.id != B.BEDROCK) { withPlayerEdit(() => destroyBlock(b)); lastCreativeBreakAt = now; }
                return;
            }
            if (b == null || b.id == B.BEDROCK) { resetMining(); return; }
            var k = key3(b.x, b.y, b.z);
            if (k != miningTarget) { miningTarget = k; miningProgress = 0; }
            double rate = miningRate(b.id, getBlockMeta(b.x, b.y, b.z));
            if (rate <= 0) { resetMining(); return; }
            if (double.IsInfinity(rate)) { withPlayerEdit(() => destroyBlock(b)); resetMining(); return; }
            miningProgress += dt * rate;
            updateMineBar();
            if (miningProgress >= 1) { withPlayerEdit(() => destroyBlock(b)); resetMining(); }
        }

        // ---------------- item use helpers ----------------
        static bool consumeSelectedOne()
        {
            var st = selectedStack();
            if (st == null) return false;
            if (!player.creative)
            {
                st.count--;
                if (st.count <= 0) inventory[selected] = null;
                inventoryDirty = true; saveGameSoon(); drawHotbar();
            }
            return true;
        }
        static bool interactMobFeed()
        {
            var st = selectedStack();
            if (st == null) return false;
            var hit = raycastMob(4);
            if (hit == null) return false;
            var m = hit.m; var inf = mobInfo(m);
            if (inf.feed == null || inf.feed != st.key || m.baby || m.loveT > 0 || m.breedCd > 0) return false;
            m.loveT = 30; m.persist = true;
            consumeSelectedOne();
            saveGameSoon();
            toast("Животное накормлено");
            return true;
        }
        static bool interactMobShear()
        {
            var st = selectedStack();
            if (st == null || st.key != "shears") return false;
            var hit = raycastMob(4);
            if (hit == null) return false;
            var m = hit.m;
            if (m.type != "sheep" || m.baby || m.sheared) return false;
            m.sheared = true; m.woolT = 90 + JS.random() * 60;
            spawnWorldDrop(sheepWoolKey(m), 1 + (int)Math.Floor(JS.random() * 3), m.x, m.y + 0.6, m.z);
            if (!player.creative) damageHeldTool(1);
            saveGameSoon();
            toast("Овца пострижена");
            return true;
        }

        // ---------------- interactions, farming, oriented placement, TNT ----------------
        public static readonly List<PrimedTnt> primedTNT = new List<PrimedTnt>();
        public static readonly List<FallingBlock> fallingBlocks = new List<FallingBlock>();
        static HashSet<int> FALLING_IDS;
        public static readonly OrderedSet<string> fallingWake = new OrderedSet<string>();
        public static double cropT = 0, fallingTickT = 0;
        public static string placementFacing()
        {
            var d = viewDir();
            if (Math.Abs(d[0]) > Math.Abs(d[2])) return d[0] > 0 ? "west" : "east";
            return d[2] > 0 ? "north" : "south";
        }
        static bool placementUpperHalf(Hit h, Hit p)
        {
            if (p.y < h.y) return true;
            if (p.y > h.y) return false;
            var d = viewDir(); double hy = player.y + 1.62 + d[1] * (JS.isFinite(h.t) ? h.t : 0);
            return hy - Math.Floor(hy) > 0.5;
        }
        static void consumeFarmItemOne()
        {
            if (player.creative) return;
            var s = selectedStack();
            if (s == null) return;
            s.count--;
            if (s.count <= 0) inventory[selected] = null;
            inventoryDirty = true; drawHotbar(); saveGameSoon();
        }
        static bool plantCrop(Hit h)
        {
            var s = selectedStack();
            if (s == null || h == null) return false;
            int farm = getBlock(h.x, h.y, h.z);
            if ((farm != B.FARMLAND && farm != B.FARMLAND_MOIST) || getBlock(h.x, h.y + 1, h.z) != B.AIR) return false;
            string type = s.key == "wheat_seeds" ? "wheat" : s.key == "pumpkin_seeds" ? "pumpkin" : s.key == "melon_seeds" ? "melon" : s.key == "carrot" ? "carrot" : s.key == "potato" ? "potato" : null;
            if (type == null) return false;
            int id = CROP_TYPES[type][0], y = h.y + 1;
            setBlock(h.x, y, h.z, id);
            worldSim.crops.Set(simKey(h.x, y, h.z), new TimerState { t = 0 });
            consumeFarmItemOne();
            toast("Посажено: " + idef(s.key).name);
            return true;
        }
        static bool boneMealTarget(Hit h)
        {
            if (h == null) return false;
            var ci = cropInfo(h.id);
            if (ci != null && ci.idx < ci.stages.Length - 1)
            {
                int n = Math.Min(ci.stages.Length - 1, ci.idx + 1 + (int)Math.Floor(JS.random() * 2));
                setBlock(h.x, h.y, h.z, ci.stages[n]);
                if (n < ci.stages.Length - 1 || ci.fruit != 0) worldSim.crops.Set(simKey(h.x, h.y, h.z), new TimerState { t = 0 });
                return true;
            }
            if (saplingSpeciesAt(h.x, h.y, h.z) != null) { if (JS.random() < 0.45) growSaplingAt(h.x, h.y, h.z); return true; }
            if (h.id == B.GRASS)
            {
                bool changed = false;
                for (int n = 0; n < 24; n++)
                {
                    int x = h.x + (int)Math.Floor(JS.random() * 7) - 3, z = h.z + (int)Math.Floor(JS.random() * 7) - 3;
                    for (int y = h.y + 1; y >= h.y - 1; y--)
                    {
                        if (getBlock(x, y, z) != B.GRASS || getBlock(x, y + 1, z) != B.AIR) continue;
                        double r = JS.random(); int id = r < 0.875 ? B.SHORT_GRASS : r < 0.9375 ? B.DANDELION : B.POPPY;
                        setBlock(x, y + 1, z, id);
                        changed = true;
                        break;
                    }
                }
                return changed;
            }
            return false;
        }
        static bool useFarmItem(Hit h)
        {
            var s = selectedStack();
            if (s == null || h == null) return false;
            if (s.key == "shears" && h.id == B.PUMPKIN)
            {
                setBlock(h.x, h.y, h.z, B.CARVED_PUMPKIN, new JObj { { "facing", placementFacing() } });
                spawnWorldDrop("pumpkin_seeds", 4, h.x + 0.5, h.y + 0.5, h.z + 0.5);
                if (!player.creative) damageHeldTool(1);
                toast("Тыква вырезана");
                return true;
            }
            if (s.key == "bone_meal")
            {
                if (!boneMealTarget(h)) return false;
                consumeFarmItemOne();
                toast("Костная мука использована");
                return true;
            }
            return plantCrop(h);
        }
        static Dictionary<int, string> NATIVE_STRIP_KEYS;
        static readonly Dictionary<string, string> VIRTUAL_STRIP_KEYS = new Dictionary<string, string> {
            { "CRIMSON_STEM", "STRIPPED_CRIMSON_STEM" }, { "WARPED_STEM", "STRIPPED_WARPED_STEM" }, { "CRIMSON_HYPHAE", "STRIPPED_CRIMSON_HYPHAE" }, { "WARPED_HYPHAE", "STRIPPED_WARPED_HYPHAE" } };
        static bool useAxeStrip(Hit h)
        {
            if (h == null) return false;
            var tool = selectedDef();
            if (tool == null || tool.tool != "axe") return false;
            if (NATIVE_STRIP_KEYS == null) NATIVE_STRIP_KEYS = new Dictionary<int, string> { { B.LOG, "STRIPPED_OAK_LOG" }, { B.BIRCH_LOG, "STRIPPED_BIRCH_LOG" }, { B.SPRUCE_LOG, "STRIPPED_SPRUCE_LOG" }, { B.DARK_LOG, "STRIPPED_DARK_OAK_LOG" }, { B.JUNGLE_LOG, "STRIPPED_JUNGLE_LOG" }, { B.ACACIA_LOG, "STRIPPED_ACACIA_LOG" } };
            var oldMeta = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            string outKey; NATIVE_STRIP_KEYS.TryGetValue(h.id, out outKey);
            if (outKey == null && isVirtualId(h.id)) { var src = oldMeta.Str("v"); if (src != null) VIRTUAL_STRIP_KEYS.TryGetValue(src, out outKey); }
            var d = outKey != null ? VirtualByKey(outKey) : null;
            if (d == null) return false;
            var nm = oldMeta.Clone(); nm["v"] = outKey;
            setBlock(h.x, h.y, h.z, virtualCarrierFor(d), nm);
            if (!player.creative) damageHeldTool(1);
            sfxPlace(virtualCarrierFor(d));
            toast("Снята кора");
            return true;
        }
        static bool useHoe(Hit h)
        {
            var d = selectedDef();
            if (d == null || d.tool != "hoe" || !(h.id == B.GRASS || h.id == B.DIRT) || getBlock(h.x, h.y + 1, h.z) != B.AIR) return false;
            setBlock(h.x, h.y, h.z, waterNearbyFarmland(h.x, h.y, h.z) ? B.FARMLAND_MOIST : B.FARMLAND);
            damageHeldTool(1);
            toast("Земля вспахана");
            return true;
        }

        // ---------------- sleeping ----------------
        public const double SLEEP_START = 12070 / 24000.0, SLEEP_END = 23459 / 24000.0;
        static int sleepTimer = 0;
        sealed class SleepReturn { public double[] pos; public double yaw, pitch; public double[] bed; }
        static SleepReturn sleepReturn = null;
        // overlay state for the UI: 0 hidden, 1 visible, 2 visible+asleep (fade)
        public static int sleepOverlayState = 0; public static string sleepLabel = "";
        static void finishSleep(bool completed)
        {
            Timers.clearTimeout(sleepTimer); sleepTimer = 0;
            if (!player.sleeping) return;
            player.sleeping = false;
            if (sleepReturn != null)
            {
                player.x = sleepReturn.pos[0]; player.y = sleepReturn.pos[1]; player.z = sleepReturn.pos[2];
                player.vx = player.vy = player.vz = 0;
                double dx = sleepReturn.bed[0] - player.x, dz = sleepReturn.bed[1] - player.z;
                player.yaw = Math.Atan2(dx, -dz); player.pitch = -0.5;
                sleepReturn = null;
            }
            sleepOverlayState = 1;
            Timers.setTimeout(() => { if (!player.sleeping) sleepOverlayState = 0; }, 900);
            if (completed) { day = 0; toast("Вы спали до утра"); sfxSleep(); saveGameSoon(); }
            syncAudioState();
        }
        public static void wakeFromSleep() { if (player.sleeping) finishSleep(false); }
        static Hit sleepBedFoot(Hit h)
        {
            if (h == null) return null;
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            if ((m.Str("bedPart") == "head" || m.Str("part") == "head") && finKey(m, "mateX") && finKey(m, "mateZ")) return new Hit(m.Int("mateX"), h.y, m.Int("mateZ"));
            return new Hit(h.x, h.y, h.z);
        }
        static void sleepInBed(Hit h)
        {
            if (h == null) return;
            player.spawn = new double[] { h.x + 0.5, h.y + 1, h.z + 0.5 };
            saveGameSoon();
            bool canSleep = day >= SLEEP_START && day <= SLEEP_END;
            if (!canSleep) { sfxClick(); toast("Точка возрождения установлена"); return; }
            if (player.sleeping) return;
            var foot = sleepBedFoot(h) ?? h;
            sleepReturn = new SleepReturn { pos = new[] { player.x, player.y, player.z }, yaw = player.yaw, pitch = player.pitch, bed = new[] { h.x + 0.5, h.z + 0.5 } };
            player.x = foot.x + 0.5; player.y = foot.y + 0.6; player.z = foot.z + 0.5;
            player.vx = player.vy = player.vz = 0; player.pitch = 1.5;
            player.sleeping = true;
            clearInputState();
            releasePointerLock();
            sleepOverlayState = 1; sleepLabel = "Засыпаем...";
            Timers.setTimeout(() => { if (player.sleeping) sleepOverlayState = 2; }, 16);
            sleepTimer = Timers.setTimeout(() => { if (player.sleeping) finishSleep(true); }, 3500);
            syncAudioState();
        }
        static void toggleDoor(Hit h)
        {
            int y = h.y, id = h.id;
            if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) y--;
            var meta = getBlockMeta(h.x, y, h.z) ?? new JObj { { "facing", "south" } };
            int bottom = getBlock(h.x, y, h.z); bool open = bottom == B.DOOR_OPEN;
            setBlock(h.x, y, h.z, open ? B.DOOR : B.DOOR_OPEN, meta);
            setBlock(h.x, y + 1, h.z, open ? B.DOOR_TOP : B.DOOR_OPEN_TOP, meta);
            toast(open ? "Дверь закрыта" : "Дверь открыта");
            sfxClick();
        }
        static bool igniteTNT(Hit h)
        {
            if (h.id != B.TNT) return false;
            setBlock(h.x, h.y, h.z, B.AIR);
            primedTNT.Add(new PrimedTnt { x = h.x + 0.5, y = h.y + 0.5, z = h.z + 0.5, t = 3 });
            toast("TNT зажжён");
            sfxClick();
            return true;
        }
        static bool interactCaveVine(Hit h)
        {
            if (h == null || h.id != B.CAVE_VINES) return false;
            var st = selectedStack();
            if (st != null && !isBK(st.key)) return false;
            var old = getBlockMeta(h.x, h.y, h.z) ?? new JObj(); bool berries = !old.Bool("berries");
            var nm = old.Clone(); nm["berries"] = berries;
            setBlock(h.x, h.y, h.z, B.CAVE_VINES, nm);
            queueLightUpdate(h.x, h.y, h.z);
            saveGameSoon();
            sfxClick();
            toast(berries ? "Светящиеся ягоды появились" : "Светящиеся ягоды убраны");
            return true;
        }

        // ---------------- virtual placement ----------------
        static string virtualFacingFromAttach(Hit h, Hit p, string bse)
        {
            int dx = p.x - h.x, dz = p.z - h.z;
            return dx > 0 ? "west" : dx < 0 ? "east" : dz > 0 ? "north" : dz < 0 ? "south" : bse;
        }
        static bool virtualPlacementSupport(int x, int y, int z, VirtualDef d, string key = "")
        {
            int below = getBlock(x, y - 1, z);
            if (virtualSaplingSpecies(d) != null) return saplingGroundId(below);
            if (d.shape == "lilypad") return hasWaterVolume(below) || below == B.ICE || below == B.PACKED_ICE;
            if (d.shape == "vine" || d.shape == "lichen" || d.shape == "button") return true;
            if (key == "WEEPING_VINES") return topSupportAt(x, y + 1, z) || virtualPairMatches(x, y + 1, z, key);
            if (key == "TWISTING_VINES") return topSupportAt(x, y - 1, z) || virtualPairMatches(x, y - 1, z, key);
            if (d.shape == "tallplant" || d.plant || d.shape == "bamboo" || d.shape == "seapickle" || d.shape == "rail" || d.shape == "carpet" || d.shape == "plate" || d.shape == "pot" || d.shape == "door") return topSupportAt(x, y - 1, z);
            return true;
        }
        static int[] bedFacingStep(string f) { return f == "north" ? new[] { 0, -1 } : f == "east" ? new[] { 1, 0 } : f == "west" ? new[] { -1, 0 } : new[] { 0, 1 }; }
        static Dictionary<int, int> NATIVE_SLAB_BASE;
        static void consumePlacedOne(Stack s)
        {
            if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
            inventoryDirty = true; saveGameSoon(); drawHotbar();
        }
        static bool replaceCellWithItemKey(int x, int y, int z, string key)
        {
            if (isBK(key)) { setBlock(x, y, z, bid(key)); return true; }
            if (isVK(key))
            {
                var d = VirtualByKey(vkey(key));
                if (d == null) return false;
                setBlock(x, y, z, virtualCarrierFor(d), new JObj { { "v", vkey(key) } });
                return true;
            }
            return false;
        }
        static bool tryMergeSelectedSlab(Hit h, Stack s, string key)
        {
            if (h == null || s == null) return false;
            if (NATIVE_SLAB_BASE == null) NATIVE_SLAB_BASE = new Dictionary<int, int> { { B.OAK_SLAB, B.PLANKS }, { B.SPRUCE_SLAB, B.SPRUCE_PLANKS }, { B.BIRCH_SLAB, B.BIRCH_PLANKS }, { B.STONE_SLAB, B.STONE }, { B.COBBLE_SLAB, B.COBBLE }, { B.STONEBRICK_SLAB, B.STONEBRICK }, { B.SANDSTONE_SLAB, B.SANDSTONE }, { B.DARK_SLAB, B.DARK_PLANKS } };
            if (isBK(key))
            {
                int sid = bid(key), bse;
                if (!NATIVE_SLAB_BASE.TryGetValue(sid, out bse) || h.id != sid) return false;
                setBlock(h.x, h.y, h.z, bse);
                consumePlacedOne(s);
                sfxPlace(bse);
                return true;
            }
            if (isVK(key))
            {
                var vk = vkey(key); var d = VirtualByKey(vk); var hm = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
                if (d == null || d.shape != "slab" || string.IsNullOrEmpty(d.@base) || !isVirtualId(h.id) || hm.Str("v") != vk) return false;
                var baseKey = mainBlockItemKey(d.@base);
                if (baseKey == null || !replaceCellWithItemKey(h.x, h.y, h.z, baseKey)) return false;
                consumePlacedOne(s);
                sfxPlace(isBK(baseKey) ? bid(baseKey) : virtualCarrierFor(VirtualByKey(vkey(baseKey))));
                return true;
            }
            return false;
        }
        static bool tryStackSeaPickle(Hit h, Stack s)
        {
            if (h == null || h.id != B.SEA_PICKLE || s == null || s.key != BK(B.SEA_PICKLE)) return false;
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            int n = (int)Math.Max(1, Math.Min(4, m.Num("count", 0) != 0 ? m.Num("count") : 1));
            if (n >= 4) return true;
            var nm = m.Clone(); nm["count"] = (double)(n + 1); nm["waterlogged"] = true;
            setBlock(h.x, h.y, h.z, B.SEA_PICKLE, nm);
            consumePlacedOne(s);
            sfxPlace(B.SEA_PICKLE);
            return true;
        }
        static readonly Regex BED_FOOT_RE = new Regex("^BED_(?!HEAD)");
        static bool placeVirtualBlock(Hit h, Stack s, string key)
        {
            var d = VirtualByKey(key);
            if (h == null || h.prev == null || d == null) return false;
            var p = h.prev;
            if (BED_FOOT_RE.IsMatch(key))
            {
                string facing = placementFacing(); var step = bedFacingStep(facing); int hx = p.x + step[0], hz = p.z + step[1];
                string headKey = "BED_HEAD_" + key.Substring(4); var hd = VirtualByKey(headKey);
                if (hd == null) { toast("Нет head-варианта кровати"); return true; }
                if (getBlock(p.x, p.y, p.z) != B.AIR || getBlock(hx, p.y, hz) != B.AIR || !topSupportAt(p.x, p.y - 1, p.z) || !topSupportAt(hx, p.y - 1, hz)) { toast("Кровати нужны 2 свободных блока с опорой"); return true; }
                if (intersectsPlayer(p.x, p.y, p.z) || intersectsPlayer(hx, p.y, hz)) { toast("Здесь стоит игрок"); return true; }
                setBlock(p.x, p.y, p.z, virtualCarrierFor(d), new JObj { { "v", key }, { "facing", facing }, { "bedPart", "foot" }, { "mateX", (double)hx }, { "mateZ", (double)hz } });
                setBlock(hx, p.y, hz, virtualCarrierFor(hd), new JObj { { "v", headKey }, { "facing", facing }, { "bedPart", "head" }, { "mateX", (double)p.x }, { "mateZ", (double)p.z } });
                if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
                inventoryDirty = true; saveGameSoon(); drawHotbar();
                toast("Поставлено: " + d.name);
                sfxPlace(virtualCarrierFor(d));
                return true;
            }
            if (d.shape == "lilypad")
            {
                var lp = lilyPadPlacementCell(h);
                if (lp == null) { toast("Ставится на поверхность воды или льда"); return true; }
                p = lp;
            }
            if (p.y <= WORLD_MIN_Y || p.y >= WORLD_MAX_Y) return true;
            int target = getBlock(p.x, p.y, p.z);
            string bse = placementFacing(), attach = virtualFacingFromAttach(h, p, bse), mount = attachmentMountFromPlacement(h, p);
            bool sideAttach = p.x != h.x || p.z != h.z;
            var meta = new JObj { { "v", key } };
            if (d.shape == "stairs" || d.shape == "gate" || d.shape == "trapdoor" || d.shape == "door") meta["facing"] = d.shape == "trapdoor" && sideAttach ? attach : bse;
            if (d.shape == "button") { meta["mount"] = mount; meta["facing"] = mount == "floor" || mount == "ceiling" ? bse : mount; }
            if (d.shape == "vine" || d.shape == "lichen")
            {
                int allow = d.shape == "vine" ? 15 : 63, faces = supportedAttachmentMask(p.x, p.y, p.z, allow);
                if (target != B.AIR || faces == 0) { toast("Нужна подходящая опора"); return true; }
                meta["faces"] = (double)faces; meta["facing"] = attach;
            }
            if (d.shape == "slab" || d.shape == "stairs" || d.shape == "trapdoor") meta["upper"] = placementUpperHalf(h, p);
            if (d.axislog) meta["axis"] = p.y != h.y ? "y" : p.x != h.x ? "x" : "z";
            if (d.shape == "button") { if (target != B.AIR || !attachmentSupportAt(p.x, p.y, p.z, mount)) { toast("Кнопке нужна опора"); return true; } }
            if (d.waterPlant || d.needsWater)
            {
                if (target != B.WATER) { toast("Этот блок ставится в воду"); return true; }
                meta["waterlogged"] = true;
            }
            else if (target != B.AIR && target != B.WATER) { toast("Место занято"); return true; }
            if (hasWaterVolume(target))
            {
                var solidKey = concreteSolidKey(key);
                if (solidKey != null) { key = solidKey; d = VirtualByKey(key); meta["v"] = key; }
            }
            if (!virtualPlacementSupport(p.x, p.y, p.z, d, key)) { toast("Нужна опора"); return true; }
            if (d.shape == "door" || d.shape == "tallplant")
            {
                if (p.y + 1 >= WORLD_MAX_Y || getBlock(p.x, p.y + 1, p.z) != B.AIR) { toast("Нужно 2 блока высоты"); return true; }
                if (intersectsPlayer(p.x, p.y, p.z) && d.solid) { toast("Здесь стоит игрок"); return true; }
                var mb = meta.Clone(); mb["part"] = "bottom"; var mt = meta.Clone(); mt["part"] = "top";
                setBlock(p.x, p.y, p.z, virtualCarrierFor(d), mb);
                setBlock(p.x, p.y + 1, p.z, virtualCarrierFor(d), mt);
            }
            else
            {
                if (intersectsPlayer(p.x, p.y, p.z) && d.solid) { toast("Здесь стоит игрок"); return true; }
                setBlock(p.x, p.y, p.z, virtualCarrierFor(d), meta);
            }
            if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
            inventoryDirty = true; saveGameSoon(); drawHotbar();
            toast("Поставлено: " + d.name);
            sfxPlace(virtualCarrierFor(d));
            return true;
        }

        // ---------------- flower pots / openables ----------------
        static HashSet<int> POTTABLE_NATIVE_IDS;
        static readonly HashSet<string> POTTABLE_VIRTUAL_KEYS = new HashSet<string> { "WITHER_ROSE", "CRIMSON_ROOTS", "WARPED_ROOTS", "CRIMSON_FUNGUS", "WARPED_FUNGUS", "OAK_SAPLING", "SPRUCE_SAPLING", "BIRCH_SAPLING", "JUNGLE_SAPLING", "ACACIA_SAPLING", "DARK_OAK_SAPLING" };
        static bool isPottableItemKey(string key)
        {
            if (POTTABLE_NATIVE_IDS == null) POTTABLE_NATIVE_IDS = new HashSet<int> { B.DANDELION, B.POPPY, B.FERN, B.DEAD_BUSH, B.ALLIUM, B.AZURE_BLUET, B.BLUE_ORCHID, B.CORNFLOWER, B.LILY_OF_THE_VALLEY, B.OXEYE_DAISY, B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP, B.WHITE_TULIP, B.RED_MUSHROOM, B.BROWN_MUSHROOM, B.SAPLING };
            if (isBK(key)) return POTTABLE_NATIVE_IDS.Contains(bid(key));
            return isVK(key) && POTTABLE_VIRTUAL_KEYS.Contains(vkey(key)) && VirtualByKey(vkey(key)) != null;
        }
        static bool isFlowerPotCell(Hit h)
        {
            if (h == null) return false;
            if (h.id == B.FLOWER_POT) return true;
            if (!isVirtualId(h.id)) return false;
            var d = virtualDefAt(h.x, h.y, h.z);
            return d != null && d.shape == "pot";
        }
        public static void dropPotContent(JObj meta, int x, int y, int z)
        {
            var key = meta != null ? meta.Str("potKey") : null;
            if (string.IsNullOrEmpty(key) || player.creative) return;
            spawnWorldDrop(key, 1, x + 0.5, y + 0.55, z + 0.5);
        }
        static bool interactFlowerPot(Hit h)
        {
            if (!isFlowerPotCell(h)) return false;
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            if (m.Bool("potKey"))
            {
                var key = m.Str("potKey"); var nm = m.Clone(); nm.Remove("potKey");
                setBlock(h.x, h.y, h.z, h.id, nm.Count > 0 ? nm : null);
                if (!player.creative) { int left = addItem(key, 1); if (left != 0) spawnWorldDrop(key, left, h.x + 0.5, h.y + 0.55, h.z + 0.5); }
                inventoryDirty = true; saveGameSoon(); drawHotbar(); sfxClick();
                return true;
            }
            var st = selectedStack();
            if (st == null || !isPottableItemKey(st.key)) return false;
            var pm = m.Clone(); pm["potKey"] = st.key;
            setBlock(h.x, h.y, h.z, h.id, pm);
            consumeSelectedOne();
            sfxClick();
            return true;
        }
        static JObj withOpen(JObj m, bool open) { var n = m.Clone(); n["open"] = open; return n; }
        public static bool setInteractableOpenAt(int x, int y, int z, bool open)
        {
            int id = getBlock(x, y, z); var m = getBlockMeta(x, y, z) ?? new JObj();
            if (isVirtualId(id))
            {
                var d = virtualDefFromMeta(m);
                if (d == null || !(d.shape == "door" || d.shape == "gate" || d.shape == "trapdoor")) return false;
                if (d.shape == "door")
                {
                    int by = m.Str("part") == "top" ? y - 1 : y;
                    var bot = getBlockMeta(x, by, z) ?? m; var top = getBlockMeta(x, by + 1, z) ?? m;
                    if (bot.Bool("open") == open && top.Bool("open") == open) return true;
                    setBlock(x, by, z, getBlock(x, by, z), withOpen(bot, open));
                    setBlock(x, by + 1, z, getBlock(x, by + 1, z), withOpen(top, open));
                }
                else if (m.Bool("open") != open) setBlock(x, y, z, id, withOpen(m, open));
                return true;
            }
            if (id == B.DOOR || id == B.DOOR_TOP || id == B.DOOR_OPEN || id == B.DOOR_OPEN_TOP)
            {
                int by = y;
                if (id == B.DOOR_TOP || id == B.DOOR_OPEN_TOP) by--;
                var bm = getBlockMeta(x, by, z) ?? m; int bid0 = getBlock(x, by, z); bool cur = bid0 == B.DOOR_OPEN;
                if (cur == open) return true;
                setBlock(x, by, z, open ? B.DOOR_OPEN : B.DOOR, bm.Clone());
                setBlock(x, by + 1, z, open ? B.DOOR_OPEN_TOP : B.DOOR_TOP, bm.Clone());
                return true;
            }
            var bd = bdef(id); var sp = bd != null ? bd.special : null;
            if (sp == "gate" || sp == "trapdoor") { if (m.Bool("open") != open) setBlock(x, y, z, id, withOpen(m, open)); return true; }
            return false;
        }
        static bool toggleNativeSpecial(Hit h)
        {
            var bd = h != null ? bdef(h.id) : null; var sp = bd != null ? bd.special : null;
            if (sp != "gate" && sp != "trapdoor") return false;
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj();
            setBlock(h.x, h.y, h.z, h.id, withOpen(m, !m.Bool("open")));
            sfxClick();
            return true;
        }
        static bool interactVirtualBlock(Hit h)
        {
            if (h == null || !isVirtualId(h.id)) return false;
            var m = getBlockMeta(h.x, h.y, h.z) ?? new JObj(); var d = virtualDefFromMeta(m);
            if (d == null) return false;
            if (d.shape == "door" || d.shape == "gate" || d.shape == "trapdoor")
            {
                bool open = !m.Bool("open");
                if (d.shape == "door")
                {
                    int by = m.Str("part") == "top" ? h.y - 1 : h.y;
                    var mm = getBlockMeta(h.x, by, h.z) ?? m; var tm = getBlockMeta(h.x, by + 1, h.z) ?? m;
                    setBlock(h.x, by, h.z, getBlock(h.x, by, h.z), withOpen(mm, open));
                    setBlock(h.x, by + 1, h.z, getBlock(h.x, by + 1, h.z), withOpen(tm, open));
                }
                else setBlock(h.x, h.y, h.z, h.id, withOpen(m, open));
                sfxClick();
                return true;
            }
            if ((m.Str("v") ?? "").StartsWith("BED_", StringComparison.Ordinal)) { sleepInBed(h); return true; }
            return false;
        }
        static bool useSpawnEgg(Hit h)
        {
            var s = selectedStack(); var d = s != null ? idef(s.key) : null;
            if (s == null || d == null || d.spawnEgg == null) return false;
            if (h == null) return true;
            int x = h.prev != null ? h.prev.x : h.x, y = h.prev != null ? h.prev.y : h.y, z = h.prev != null ? h.prev.z : h.z;
            MobInfo inf;
            if (!MOB_INFO.TryGetValue(d.spawnEgg, out inf)) return true;
            if (inf.aquatic)
            {
                if (isWater(h.id)) { x = h.x; y = h.y; z = h.z; }
                else if (!isWater(getBlock(x, y, z))) { toast("Водный моб призывается в воде"); return true; }
            }
            else if (getBlock(x, y, z) != B.AIR) { toast("Для моба нет места"); return true; }
            var c = chunkFastGet(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            if (c == null) return true;
            var m = makeMob(d.spawnEgg, x + 0.5, y, z + 0.5, new MobOpts { persist = true });
            if (m == null) return true;
            attachLiveMob(m, c);
            if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
            inventoryDirty = true; saveGameSoon(); drawHotbar();
            toast("Призван: " + d.name);
            return true;
        }
        static bool interactBlock(Hit h)
        {
            if (h == null) return false;
            if (interactFlowerPot(h)) return true;
            if (interactVirtualBlock(h)) return true;
            if (h.id == B.CRAFT) { openGameUI(player.creative ? "inventory" : "craft"); return true; }
            if (h.id == B.CHEST) { openGameUI("chest", key3(h.x, h.y, h.z)); return true; }
            if (h.id == B.FURNACE || h.id == B.FURNACE_LIT) { openGameUI("furnace", key3(h.x, h.y, h.z)); return true; }
            if (h.id == B.BED) { sleepInBed(h); return true; }
            if (h.id == B.DOOR || h.id == B.DOOR_TOP || h.id == B.DOOR_OPEN || h.id == B.DOOR_OPEN_TOP) { toggleDoor(h); return true; }
            if (toggleNativeSpecial(h)) return true;
            if (h.id == B.TNT) return igniteTNT(h);
            return false;
        }
        static double lastEatUseAt = -1e9;
        static bool eatSelected()
        {
            if (player.creative) return false;
            var s = selectedStack();
            if (s == null) return false;
            var d = idef(s.key); bool golden = s.key == "golden_apple";
            if (d.food == 0 || (player.hunger >= 20 && !golden)) return false;
            double now = JS.now();
            if (now - lastEatUseAt < 800) return true;
            lastEatUseAt = now;
            player.hunger = Math.Min(20, player.hunger + d.food);
            if (golden) player.hp = Math.Min(20, player.hp + 8);
            var ateKey = s.key;
            s.count--;
            if (s.count <= 0) inventory[selected] = null;
            if (ateKey == "mushroom_stew") { int left = addItem("bowl", 1); if (left != 0) spawnWorldDrop("bowl", left, player.x, player.y + 0.6, player.z); }
            inventoryDirty = true; saveGameSoon(); drawHotbar(); updateVitals();
            toast("Съедено: " + d.name);
            return true;
        }

        // ---------------- fluids in hand ----------------
        static Hit lilyPadPlacementCell(Hit h)
        {
            if (h == null) return null;
            var q = new List<Hit>();
            if (h.prev != null) q.Add(h.prev);
            q.Add(h);
            foreach (var p in q)
            {
                int x = p.x, y = p.y, z = p.z, id = getBlock(x, y, z);
                if (id == B.ICE)
                {
                    int py = y + 1;
                    if (py < WORLD_MAX_Y && getBlock(x, py, z) == B.AIR && attachmentSupported(x, py, z, B.LILY_PAD)) return new Hit(x, py, z);
                    continue;
                }
                if (!hasWaterVolume(id)) continue;
                while (y < WORLD_MAX_Y - 1 && hasWaterVolume(getBlock(x, y, z))) y++;
                if (getBlock(x, y, z) == B.AIR && attachmentSupported(x, y, z, B.LILY_PAD)) return new Hit(x, y, z);
            }
            return null;
        }
        static Hit raycastSourceFluid(double max = 7)
        {
            var d = viewDir(); double ex = player.x, ey = player.y + 1.62, ez = player.z, dx = nz9(d[0]), dy = nz9(d[1]), dz = nz9(d[2]), ix = 1 / dx, iy = 1 / dy, iz = 1 / dz;
            int x = JS.floor(ex), y = JS.floor(ey), z = JS.floor(ez), sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            double tx = Math.Abs(ix), ty = Math.Abs(iy), tz = Math.Abs(iz), nx = (sx > 0 ? x + 1 - ex : ex - x) * tx, ny = (sy > 0 ? y + 1 - ey : ey - y) * ty, nzv = (sz > 0 ? z + 1 - ez : ez - z) * tz, t = 0;
            for (int step = 0; step < 96 && t <= max; step++)
            {
                int id = getBlock(x, y, z);
                if (id == B.WATER || id == B.LAVA) return new Hit { x = x, y = y, z = z, id = id, t = t };
                if (id != B.AIR)
                {
                    var b = bdef(id);
                    if (!(b != null && (b.plant || b.waterPlant || (b.cutout && !b.solid)))) return null;
                }
                if (nx < ny && nx < nzv) { x += sx; t = nx; nx += tx; }
                else if (ny < nzv) { y += sy; t = ny; ny += ty; }
                else { z += sz; t = nzv; nzv += tz; }
            }
            return null;
        }
        static bool bucketReplaceableAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (id == B.AIR || isFluidWater(id) || isLava(id)) return true;
            if (isVirtualId(id)) { var d = virtualDefAt(x, y, z); return d != null && (d.plant || d.waterPlant); }
            var b = bdef(id);
            return b != null && (b.plant || b.waterPlant);
        }
        static bool useBucketOrIgniter(Hit h)
        {
            var st = selectedStack();
            if (st == null) return false;
            if (st.key == "bucket")
            {
                var f = raycastSourceFluid();
                if (f != null)
                {
                    var filled = f.id == B.WATER ? "water_bucket" : "lava_bucket";
                    if (!player.creative)
                    {
                        if (st.count > 1)
                        {
                            st.count--;
                            int left = addItem(filled, 1);
                            if (left != 0) { st.count++; toast("Нет места для наполненного ведра"); return true; }
                        }
                        else inventory[selected] = new Stack { key = filled, count = 1 };
                    }
                    setBlock(f.x, f.y, f.z, B.AIR);
                    inventoryDirty = true; drawHotbar(); saveGameSoon();
                    return true;
                }
            }
            if ((st.key == "water_bucket" || st.key == "lava_bucket") && h != null && h.prev != null)
            {
                var p = h.prev;
                if (p.y <= WORLD_MIN_Y || p.y >= WORLD_MAX_Y || !bucketReplaceableAt(p.x, p.y, p.z)) return false;
                setBlock(p.x, p.y, p.z, st.key == "water_bucket" ? B.WATER : B.LAVA);
                if (!player.creative) inventory[selected] = new Stack { key = "bucket", count = 1 };
                drawHotbar(); saveGameSoon();
                return true;
            }
            if (st.key == "flint_and_steel" && h != null)
            {
                if (h.id == B.FIRE) return true;
                if (h.id == B.TNT) return igniteTNT(h);
                var p = h.prev;
                if (p != null && p.y > WORLD_MIN_Y && p.y < WORLD_MAX_Y && getBlock(p.x, p.y, p.z) == B.AIR) { setBlock(p.x, p.y, p.z, B.FIRE); damageHeldTool(1); return true; }
            }
            return false;
        }

        // ---------------- placement ----------------
        public static void placeBlock()
        {
            var h = raycast();
            if (interactVehicle(h)) return;
            if (useActiveItem(h)) return;
            if (useSpawnEgg(h)) return;
            var pre = selectedStack();
            if (h != null && pre != null && (tryStackSeaPickle(h, pre) || tryMergeSelectedSlab(h, pre, pre.key))) return;
            if (h != null && interactBlock(h)) return;
            if (interactCaveVine(h)) return;
            if (useBucketOrIgniter(h)) return;
            if (interactMobFeed()) return;
            if (interactMobShear()) return;
            if (h != null && (useAxeStrip(h) || useFarmItem(h) || useHoe(h))) return;
            if (eatSelected()) return;
            var s = selectedStack();
            if (s == null) { toast("В руке нет блока"); return; }
            if (isVK(s.key)) { if (h == null) return; placeVirtualBlock(h, s, vkey(s.key)); return; }
            if (!isBK(s.key)) { toast("В руке нет блока"); return; }
            int id = bid(s.key); var bb = bdef(id);
            if (h == null) return;
            if (id == B.LILY_PAD)
            {
                var lp = lilyPadPlacementCell(h);
                if (lp == null) { toast("Кувшинка ставится на поверхность воды или льда"); return; }
                if (intersectsPlayer(lp.x, lp.y, lp.z)) { toast("Здесь стоит игрок"); return; }
                setBlock(lp.x, lp.y, lp.z, id);
                if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
                inventoryDirty = true; saveGameSoon(); drawHotbar();
                toast("Поставлено: " + blocks[id].name);
                sfxPlace(id);
                return;
            }
            if (h.prev == null) return;
            var p = h.prev;
            if (p.y <= WORLD_MIN_Y || p.y >= WORLD_MAX_Y) { toast("За границей мира"); return; }
            int target = getBlock(p.x, p.y, p.z);
            string baseFacing = placementFacing(), mount = attachmentMountFromPlacement(h, p);
            int dx = p.x - h.x, dz = p.z - h.z; bool sideAttach = dx != 0 || dz != 0;
            string attachFacing = dx > 0 ? "west" : dx < 0 ? "east" : dz > 0 ? "north" : dz < 0 ? "south" : baseFacing, sp = bb != null ? bb.special : null;
            int[] chestPair = null;
            if (id == B.CHEST)
            {
                var q = findChestPairTarget(p.x, p.y, p.z, baseFacing);
                if (q.error != null) { toast(q.error); return; }
                chestPair = q.target;
            }
            string facing = id == B.LADDER ? attachFacing : baseFacing;
            JObj meta = id == B.FURNACE || id == B.CHEST || id == B.LADDER || id == B.BED ? new JObj { { "facing", facing } } : null;
            if (id == B.TORCH || id == B.STONE_BUTTON) meta = new JObj { { "mount", mount }, { "facing", mount == "floor" || mount == "ceiling" ? baseFacing : mount } };
            if (id == B.VINE || id == B.GLOW_LICHEN)
            {
                int allow = id == B.VINE ? 15 : 63, faces = supportedAttachmentMask(p.x, p.y, p.z, allow);
                if (target != B.AIR || faces == 0) { toast("Нужна подходящая опора"); return; }
                meta = new JObj { { "faces", (double)faces }, { "facing", attachFacing } };
            }
            if (sp == "stairs" || sp == "gate" || sp == "trapdoor") { meta = meta != null ? meta.Clone() : new JObj(); meta["facing"] = sp == "trapdoor" && sideAttach ? attachFacing : baseFacing; }
            if (sp == "slab" || sp == "stairs" || sp == "trapdoor") { meta = meta != null ? meta.Clone() : new JObj(); meta["upper"] = placementUpperHalf(h, p); }
            if (id == B.RAIL) meta = new JObj { { "axis", baseFacing == "east" || baseFacing == "west" ? "x" : "z" } };
            if (axisLogIdSync(id)) { meta = meta != null ? meta.Clone() : new JObj(); meta["axis"] = p.y != h.y ? "y" : p.x != h.x ? "x" : "z"; }
            if (isTreeLeaves(id)) { meta = meta != null ? meta.Clone() : new JObj(); meta["persistent"] = true; }
            if (bb != null && bb.waterPlant)
            {
                if (target != B.WATER) { toast("Подводное растение ставится только в воду"); return; }
                if (!attachmentSupported(p.x, p.y, p.z, id)) { toast("Растению нужна опора снизу"); return; }
                meta = meta != null ? meta.Clone() : new JObj(); meta["waterlogged"] = true;
            }
            else if (bb != null && bb.plant)
            {
                if (target != B.AIR) { toast("Растение ставится только в воздух"); return; }
                if (!attachmentSupported(p.x, p.y, p.z, id)) { toast("Растению нужна подходящая опора"); return; }
            }
            if (id == B.CACTUS && (target != B.AIR || !attachmentSupported(p.x, p.y, p.z, id))) { toast("Кактус ставится на песок или кактус"); return; }
            if (id == B.TORCH && (target != B.AIR || mount == "ceiling" || !attachmentSupportAt(p.x, p.y, p.z, mount))) { toast("Факелу нужна опора снизу или сбоку"); return; }
            if (id == B.LADDER && (target != B.AIR || !sideAttach || !sideSupportAt(h.x, h.y, h.z))) { toast("Лестнице нужна боковая твёрдая опора"); return; }
            if (id == B.BAMBOO && (target != B.AIR || !attachmentSupported(p.x, p.y, p.z, id))) { toast("Бамбуку нужна опора снизу"); return; }
            if ((id == B.RAIL || id == B.WHITE_CARPET || id == B.STONE_PLATE || id == B.FLOWER_POT) && (target != B.AIR || !attachmentSupported(p.x, p.y, p.z, id))) { toast("Этому блоку нужна опора снизу"); return; }
            if (id == B.STONE_BUTTON && (target != B.AIR || !attachmentSupportAt(p.x, p.y, p.z, mount))) { toast("Кнопке нужна опора"); return; }
            if (id == B.DOOR)
            {
                if (p.y + 1 >= WORLD_MAX_Y) { toast("Для двери не хватает высоты мира"); return; }
                if (!topSupportAt(p.x, p.y - 1, p.z)) { toast("Двери нужна опора снизу"); return; }
                if (target != B.AIR || getBlock(p.x, p.y + 1, p.z) != B.AIR) { toast("Для двери нужно 2 блока высоты"); return; }
                if (intersectsPlayer(p.x, p.y, p.z)) { toast("Здесь стоит игрок"); return; }
                setBlock(p.x, p.y, p.z, B.DOOR, new JObj { { "facing", facing } });
                setBlock(p.x, p.y + 1, p.z, B.DOOR_TOP, new JObj { { "facing", facing } });
            }
            else if (id == B.BED)
            {
                var step = bedFacingStep(facing); int hx = p.x + step[0], hz = p.z + step[1]; var head = VirtualByKey("BED_HEAD");
                if (target != B.AIR || getBlock(hx, p.y, hz) != B.AIR || !topSupportAt(p.x, p.y - 1, p.z) || !topSupportAt(hx, p.y - 1, hz)) { toast("Кровати нужны 2 свободных блока с опорой"); return; }
                if (intersectsPlayer(p.x, p.y, p.z) || intersectsPlayer(hx, p.y, hz)) { toast("Здесь стоит игрок"); return; }
                setBlock(p.x, p.y, p.z, id, new JObj { { "facing", facing }, { "bedPart", "foot" }, { "mateX", (double)hx }, { "mateZ", (double)hz } });
                if (head != null) setBlock(hx, p.y, hz, virtualCarrierFor(head), new JObj { { "v", "BED_HEAD" }, { "facing", facing }, { "bedPart", "head" }, { "mateX", (double)p.x }, { "mateZ", (double)p.z }, { "nativeBed", true } });
            }
            else
            {
                if (!(bb != null && bb.plant) && !(bb != null && bb.waterPlant) && id != B.CACTUS && id != B.TORCH && id != B.LADDER && id != B.VINE && id != B.GLOW_LICHEN && target != B.AIR && target != B.WATER) { toast("Место занято"); return; }
                if (intersectsPlayer(p.x, p.y, p.z) && bb != null && bb.solid) { toast("Здесь стоит игрок"); return; }
                setBlock(p.x, p.y, p.z, id, meta);
                if (id == B.CHEST && chestPair != null) linkChestPair(p.x, p.y, p.z, chestPair[0], chestPair[1], baseFacing);
            }
            if (!player.creative) { s.count--; if (s.count <= 0) inventory[selected] = null; }
            inventoryDirty = true; saveGameSoon(); drawHotbar();
            toast("Поставлено: " + blocks[id].name);
            sfxPlace(id);
        }

        // ---------------- explosions ----------------
        public static void explodeAt(double x, double y, double z, double r = 4)
        {
            sfxExplosion();
            var hit = new OrderedSet<string>();
            for (int ix = 0; ix < 16; ix++)
                for (int iy = 0; iy < 16; iy++)
                    for (int iz = 0; iz < 16; iz++)
                    {
                        if (ix != 0 && ix != 15 && iy != 0 && iy != 15 && iz != 0 && iz != 15) continue;
                        double dx = ix / 15.0 * 2 - 1, dy = iy / 15.0 * 2 - 1, dz = iz / 15.0 * 2 - 1, len = JS.hypot(dx, dy, dz);
                        dx /= len; dy /= len; dz /= len;
                        double power = r * (0.7 + JS.random() * 0.6), px = x + 0.5, py = y + 0.5, pz = z + 0.5;
                        while (power > 0)
                        {
                            int bx = JS.floor(px), by = JS.floor(py), bz = JS.floor(pz), id = getBlock(bx, by, bz);
                            if (id != B.AIR)
                            {
                                power -= (explosionResistanceAt(bx, by, bz, id) + 0.3) * 0.3;
                                if (power > 0 && id != B.BEDROCK && !isFluidWater(id) && !isLava(id)) hit.Add(bx + "," + by + "," + bz);
                            }
                            px += dx * 0.3; py += dy * 0.3; pz += dz * 0.3;
                            power -= 0.225;
                        }
                    }
            foreach (var k in hit)
            {
                var a = k.Split(','); int xx = int.Parse(a[0]), yy = int.Parse(a[1]), zz = int.Parse(a[2]), id = getBlock(xx, yy, zz);
                if (id == B.AIR || id == B.BEDROCK || isFluidWater(id) || isLava(id)) continue;
                if (id == B.TNT) { setBlock(xx, yy, zz, B.AIR); primedTNT.Add(new PrimedTnt { x = xx + 0.5, y = yy + 0.5, z = zz + 0.5, t = 0.1 + JS.random() * 0.25 }); }
                else destroyBlock(new Hit(xx, yy, zz, id), true);
            }
            double dp = JS.hypot(player.x - x, player.y + 1 - y, player.z - z);
            if (dp < r * 2) damagePlayer(Math.Max(0, (r * 2 - dp) * 1.7), "взрыв");
            foreach (var m in liveMobs.ToArray())
            {
                if (m.dead) continue;
                double d = JS.hypot(m.x - x, m.y - y, m.z - z);
                if (d < r * 1.7) damageMobSource(m, Math.Max(1, (r * 1.7 - d) * 3), null, false, true);
            }
            toast("БУМ!");
        }
        public static void tickTNT(double dt)
        {
            for (int i = primedTNT.Count - 1; i >= 0; i--)
            {
                var q = primedTNT[i];
                q.t -= dt;
                if (q.t <= 0) { primedTNT.RemoveAt(i); explodeAt(q.x, q.y, q.z, 4); if (i > primedTNT.Count) i = primedTNT.Count; }
            }
        }
    }
}
