// Voxel Forge — Unity port. Armor, hunger/exhaustion, damage, death, respawn and the survival tick.
using System;

namespace VoxelForge
{
    public static partial class VF
    {
        public static double armorPoints()
        {
            double n = 0;
            for (int i = 0; i < 4; i++) { var q = armor[i]; var a = q != null ? idef(q.key).armor : null; if (a != null) n += a.pts; }
            return n;
        }
        static void damageArmor()
        {
            for (int i = 0; i < 4; i++)
            {
                var q = armor[i];
                if (q == null) continue;
                var d = idef(q.key);
                if (d.maxDur == 0) continue;
                q.dur = (q.dur != 0 ? q.dur : d.maxDur) - 1;
                if (q.dur <= 0) armor[i] = null;
            }
            inventoryDirty = true;
            saveGameSoon();
        }
        public static void addExhaustion(double n)
        {
            player.exh += Math.Max(0, n);
            while (player.exh >= 4)
            {
                player.exh -= 4;
                if (player.hunger > 0) player.hunger--; else break;
            }
        }
        static double jitter() { return (JS.random() - 0.5) * 0.8; }
        static void dropEquipmentOnDeath()
        {
            for (int i = 0; i < 4; i++)
            {
                var q = armor[i];
                if (q != null) { spawnWorldDrop(q.key, 1, player.x + jitter(), player.y + 0.8, player.z + jitter(), q.dur); armor[i] = null; }
            }
            var o = offhand[0];
            if (o != null) { spawnWorldDrop(o.key, o.count, player.x + jitter(), player.y + 0.8, player.z + jitter(), o.dur); offhand[0] = null; }
        }
        /// <summary>The HUD reads player vitals directly every frame; kept for call-site parity.</summary>
        public static void updateVitals() { }
        // death screen state
        public static bool deathUIVisible = false; public static string deathReasonText = "";
        public static void damagePlayer(double n, string reason = "урон", bool armored = true)
        {
            if (player.creative || player.dead || n <= 0) return;
            double dmg = Math.Max(0, n);
            if (armored)
            {
                double pts = armorPoints();
                if (pts > 0) { dmg = Math.Max(1, JS.round(dmg * (1 - Math.Min(0.8, pts * 0.04)))); damageArmor(); }
            }
            player.hp = Math.Max(0, player.hp - dmg);
            sfxHurt();
            toast("-" + Math.Ceiling(dmg) + " HP · " + reason);
            if (player.hp <= 0) die(reason);
            updateVitals();
            saveGameSoon();
        }
        public static void die(string reason)
        {
            if (player.creative) return;
            if (player.riding != null) forceDismountVehicle();
            player.dead = true;
            player.deathReason = reason;
            clearInputState();
            if (!modEnabled("keepinv"))
            {
                for (int i = 0; i < inventory.Length; i++)
                {
                    var st = inventory[i];
                    if (st != null) { spawnWorldDrop(st.key, st.count, player.x + jitter(), player.y + 0.8, player.z + jitter(), st.dur); inventory[i] = null; }
                }
                dropEquipmentOnDeath();
                if (uiCursor != null) { spawnWorldDrop(uiCursor.key, uiCursor.count, player.x, player.y + 1, player.z, uiCursor.dur); uiCursor = null; }
            }
            uiOpen = false;
            releasePointerLock();
            deathReasonText = "Причина: " + reason + (modEnabled("keepinv") ? " · инвентарь сохранён" : "");
            deathUIVisible = true;
            inventoryDirty = true;
            drawHotbar();
            syncTouchControls();
            saveGameNow();
        }
        public static void respawn()
        {
            if (player.riding != null) forceDismountVehicle();
            player.dead = false; player.deathReason = ""; player.h = 1.8; player.hp = 20; player.hunger = 20; player.air = 10; player.exh = 0; player.fallDist = 0; player.lastSurvY = null;
            double sx = player.spawn != null ? player.spawn[0] : SPAWN[0], sy = player.spawn != null ? player.spawn[1] : SPAWN[1], sz = player.spawn != null ? player.spawn[2] : SPAWN[2];
            var safe = findSafeWorldSpawnNear(sx, sy, sz, 16) ?? findSafeWorldSpawnNear(SPAWN[0], SPAWN[1], SPAWN[2], 24);
            if (safe == null)
            {
                initialSpawnPending = true; resumeLoadPending = false; worldReady = false;
                var a = findLandSpawnAnchor(JS.floor(sx), JS.floor(sz)) ?? findLandSpawnAnchor(0, 0) ?? new[] { 0, 0 };
                SPAWN_ANCHOR = new double[] { a[0], a[1] };
                primeInitialSpawnChunks((int)SPAWN_ANCHOR[0], (int)SPAWN_ANCHOR[1]);
                deathUIVisible = false;
                syncTouchControls();
                return;
            }
            player.x = safe[0]; player.y = safe[1]; player.z = safe[2];
            player.vx = player.vy = player.vz = 0;
            deathUIVisible = false;
            syncTouchControls();
            if (!TOUCH_DEVICE) requestGamePointerLock();
            saveGameSoon();
        }
        static double vitalsTick = 0;
        public static void survivalTick(double dt)
        {
            if (player.creative || player.dead) return;
            double ph = player.h != 0 ? player.h : 1.8;
            double headY = player.y + (ph < 1.2 ? Math.Max(0.4, (player.h != 0 ? player.h : 0.85) - 0.18) : 1.62);
            int eyeId = getBlock(player.x, headY, player.z), midId = getBlock(player.x, player.y + 0.5, player.z), feetId = getBlock(player.x, player.y + 0.05, player.z);
            bool waterHead = pointInWater(player.x, headY, player.z), fluidHead = waterHead || isLava(eyeId), fluidMid = pointInWater(player.x, player.y + 0.5, player.z) || isLava(midId);
            player.headInWater = waterHead;
            player.inLava = isLava(eyeId) || isLava(midId) || isLava(feetId);
            if (player.flying || fluidHead || fluidMid) player.fallDist = 0;
            else if (player.onGround)
            {
                if (player.fallDist > 3.2) damagePlayer(Math.Floor(player.fallDist - 3), "падение", false);
                player.fallDist = 0;
            }
            else if (player.lastSurvY != null && player.y < player.lastSurvY.Value) player.fallDist += player.lastSurvY.Value - player.y;
            player.lastSurvY = player.y;
            if (waterHead)
            {
                player.air = Math.Max(0, player.air - dt);
                if (player.air <= 0) { player.drownT += dt; if (player.drownT >= 1) { player.drownT = 0; damagePlayer(2, "утопление", false); } }
            }
            else { player.air = Math.Min(10, player.air + dt * 4); player.drownT = 0; }
            if (player.inLava) { player.fireDamageT += dt; if (player.fireDamageT >= 0.5) { player.fireDamageT = 0; damagePlayer(4, "лава", false); } }
            else if (eyeId == B.FIRE || midId == B.FIRE || feetId == B.FIRE) { player.fireDamageT += dt; if (player.fireDamageT >= 0.5) { player.fireDamageT = 0; damagePlayer(1, "огонь", false); } }
            else player.fireDamageT = 0;
            bool cactus = false;
            int bx = JS.floor(player.x), by = JS.floor(player.y), bz = JS.floor(player.z);
            double fx = player.x - Math.Floor(player.x), fz = player.z - Math.Floor(player.z);
            for (int oy = 0; oy < 2 && !cactus; oy++)
            {
                if (getBlock(bx, by + oy, bz) == B.CACTUS) cactus = true;
                if (getBlock(bx + 1, by + oy, bz) == B.CACTUS && fx > 0.62) cactus = true;
                if (getBlock(bx - 1, by + oy, bz) == B.CACTUS && fx < 0.38) cactus = true;
                if (getBlock(bx, by + oy, bz + 1) == B.CACTUS && fz > 0.62) cactus = true;
                if (getBlock(bx, by + oy, bz - 1) == B.CACTUS && fz < 0.38) cactus = true;
            }
            bool berryBush = getBlock(bx, by, bz) == B.SWEET_BERRY_BUSH || getBlock(bx, by + 1, bz) == B.SWEET_BERRY_BUSH,
                berryMoving = JS.hypot(player.vx, player.vz) > 0.3 || Math.Abs(player.vy) > 0.3;
            player.cactusT = Math.Max(0, player.cactusT - dt);
            if ((cactus || (berryBush && berryMoving)) && player.cactusT <= 0) { player.cactusT = 0.5; damagePlayer(1, cactus ? "кактус" : "куст сладких ягод", true); }
            if (player.y < WORLD_MIN_Y - 2) { player.voidT += dt; if (player.voidT >= 0.4) { player.voidT = 0; damagePlayer(4, "падение в пустоту", false); } }
            else player.voidT = 0;
            bool moving = JS.hypot(player.vx, player.vz) > 0.5;
            addExhaustion(dt * (player.sprinting && moving ? 0.15 : moving ? 0.015 : 0.004));
            if (player.hunger >= 18 && player.hp < 20)
            {
                player.regenT += dt;
                if (player.regenT >= 4) { player.regenT = 0; player.hp = Math.Min(20, player.hp + 1); addExhaustion(3); }
            }
            else player.regenT = 0;
            if (player.hunger <= 0)
            {
                player.starveT += dt;
                if (player.starveT >= 4) { player.starveT = 0; if (player.hp > 1) damagePlayer(1, "голод", false); }
            }
            else player.starveT = 0;
            vitalsTick += dt;
            if (vitalsTick >= 0.1) { vitalsTick = 0; updateVitals(); }
        }
    }
}
