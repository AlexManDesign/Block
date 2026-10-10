// Voxel Forge — Unity port. Frame renderer: lightmap, camera matrices, materials and the per-frame
// CommandBuffer reproducing the reference draw order and GL state for every pass.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelForge
{
    public static partial class VF
    {
        // ---------------- lightmap ----------------
        public static readonly byte[] mainLightmapPixels = new byte[16 * 16 * 4];
        public static Texture2D mainLightmapTex;
        static double mainLightFlicker = 0, mainLightFlickerTarget = 0, mainLightFlickerAt = 0;
        public static double mainTerrainLightScale = 1;
        static double mainLightCurve(double x) { return x / (4 - 3 * x); }
        public static void updateMainLightmap(SkyState A, double now)
        {
            if (now - mainLightFlickerAt > 80) { mainLightFlickerAt = now; mainLightFlickerTarget = (JS.random() - 0.5) * 0.22; }
            mainLightFlicker += (mainLightFlickerTarget - mainLightFlicker) * 0.3;
            double skyDrop = (1 - A.day) * 11, torchGain = 1 + mainLightFlicker * 0.35, night = 1 - A.day;
            double sr = 1 - night * 0.45, sg = 1 - night * 0.35, sb = 1 - night * 0.1;
            if (A.sunset > 0) { sr += (1.12 - sr) * A.sunset * 0.6; sg += (0.82 - sg) * A.sunset * 0.6; sb += (0.58 - sb) * A.sunset * 0.6; }
            for (int sky = 0; sky < 16; sky++)
            {
                double q = Math.Max(0, sky - skyDrop), bse = Math.Max(mainLightCurve(q / 15), sky / 15.0 * 0.364), r = bse * sr, g = bse * sg, b = bse * sb;
                for (int blk = 0; blk < 16; blk++)
                {
                    double c = blk / 15.0 * torchGain;
                    if (c > 1) c = 1;
                    double v = Math.Max(Math.Min(1, mainLightCurve(c) * 2.4), blk / 15.0 * 0.85), vr = v, vg = v * (v * 0.6 + 0.4), vb = v * (v * v * 0.6 + 0.4);
                    double R = Math.Max(r + vr, 0.28), G = Math.Max(g + vg, 0.29), Bv = Math.Max(b + vb, 0.32);
                    int o = (sky * 16 + blk) * 4;
                    // Uint8Array assignment truncates toward zero
                    mainLightmapPixels[o] = (byte)Math.Min(255, R * 255); mainLightmapPixels[o + 1] = (byte)Math.Min(255, G * 255);
                    mainLightmapPixels[o + 2] = (byte)Math.Min(255, Bv * 255); mainLightmapPixels[o + 3] = 255;
                }
            }
            if (mainLightmapTex != null) { mainLightmapTex.SetPixelData(mainLightmapPixels, 0); mainLightmapTex.Apply(false); }
        }

        // ---------------- materials / meshes ----------------
        static Material matOpaque, matCutout, matTrans, matXray1, matXray2, matWater, matWaterUnder, matDepth, matSprite, matSpriteItem, matSky, matCrack, matHeld, matHeldItem,
            matEntity, matEntityXray, matMobHeld, matParticle, matLineSel, matLineProj, matLineFish, matLinePrimed, matCloud;
        static DynMesh dmSky, dmEntity, dmMobHeld, dmActiveTerrain, dmActiveItem, dmDropBlock, dmDropItem, dmCrack, dmParticle, dmLineSel, dmLineProj, dmLineFish, dmLinePrimed, dmHeldMain, dmHeldOff;
        static Mesh cloudMesh;
        public static CommandBuffer frameCB;
        const int CULL_OFF = 0, CULL_FRONT = 1;
        static Material mk(string shader, Action<Material> init)
        {
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogError("Voxel Forge: shader not found: " + shader); s = Shader.Find("Hidden/InternalErrorShader"); }
            var m = new Material(s) { hideFlags = HideFlags.DontSave };
            init(m);
            return m;
        }
        static void state(Material m, int cull, BlendMode src, BlendMode dst, CompareFunction zt, bool zw)
        {
            m.SetFloat("_Cull", cull); m.SetFloat("_SrcBlend", (float)src); m.SetFloat("_DstBlend", (float)dst); m.SetFloat("_ZTest", (float)zt); m.SetFloat("_ZWrite", zw ? 1 : 0);
        }
        public static void initRenderer()
        {
            mainLightmapTex = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "vf-lightmap" };
            updateMainLightmap(mainSkyState(), JS.now());
            // GL CULL_FACE(BACK) on counter-clockwise-front geometry == Unity "Cull Front" (Unity treats clockwise as front).
            matOpaque = mk("VoxelForge/Terrain", m => { m.DisableKeyword("VF_ALPHA"); state(m, CULL_FRONT, BlendMode.One, BlendMode.Zero, CompareFunction.Less, true); });
            matCutout = mk("VoxelForge/Terrain", m => { m.EnableKeyword("VF_ALPHA"); state(m, CULL_FRONT, BlendMode.One, BlendMode.Zero, CompareFunction.Less, true); });
            matTrans = mk("VoxelForge/Terrain", m => { m.EnableKeyword("VF_ALPHA"); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Less, true); });
            matXray1 = mk("VoxelForge/TerrainXray", m => { m.SetFloat("_XrayMode", 1); state(m, CULL_FRONT, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Always, false); });
            matXray2 = mk("VoxelForge/TerrainXray", m => { m.SetFloat("_XrayMode", 2); state(m, CULL_FRONT, BlendMode.One, BlendMode.Zero, CompareFunction.Less, true); });
            matWater = mk("VoxelForge/Water", m => { m.SetFloat("_ZTest", (float)CompareFunction.LessEqual); m.SetFloat("_WaterPass", 0); });
            matWaterUnder = mk("VoxelForge/Water", m => { m.SetFloat("_ZTest", (float)CompareFunction.Less); m.SetFloat("_WaterPass", 0); });
            matDepth = mk("VoxelForge/DepthOnly", m => { });
            matSprite = mk("VoxelForge/Sprite", m => { m.mainTexture = atlasImg; m.SetFloat("_Grid", 32); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Less, true); });
            matSpriteItem = mk("VoxelForge/Sprite", m => { m.mainTexture = itemTex; m.SetFloat("_Grid", 16); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Less, true); });
            matMobHeld = mk("VoxelForge/Sprite", m => { m.mainTexture = itemTex; m.SetFloat("_Grid", 16); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Less, true); });
            matSky = mk("VoxelForge/Sprite", m => { m.mainTexture = atlasImg; m.SetFloat("_Grid", 32); m.SetFloat("_NoFog", 1); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Always, false); });
            matCrack = mk("VoxelForge/Sprite", m => { m.mainTexture = atlasImg; m.SetFloat("_Grid", 32); state(m, CULL_OFF, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.Less, false); });
            matHeld = mk("VoxelForge/Sprite", m => { m.mainTexture = atlasImg; m.SetFloat("_Grid", 32); m.SetFloat("_NoFog", 1); state(m, CULL_OFF, BlendMode.One, BlendMode.Zero, CompareFunction.Less, true); });
            matHeldItem = mk("VoxelForge/Sprite", m => { m.mainTexture = itemTex; m.SetFloat("_Grid", 16); m.SetFloat("_NoFog", 1); state(m, CULL_OFF, BlendMode.One, BlendMode.Zero, CompareFunction.Less, true); });
            matEntity = mk("VoxelForge/Entity", m => { m.mainTexture = entityTex; m.SetFloat("_ZTest", (float)CompareFunction.Less); });
            matEntityXray = mk("VoxelForge/Entity", m => { m.mainTexture = entityTex; m.SetFloat("_ZTest", (float)CompareFunction.Always); m.SetFloat("_ZWrite", 0); }); // gl.disable(DEPTH_TEST) also disables depth writes
            matParticle = mk("VoxelForge/Particle", m => { });
            matLineSel = mk("VoxelForge/Line", m => m.SetColor("_Color", new Color(0, 0, 0, 0.85f)));
            matLineProj = mk("VoxelForge/Line", m => m.SetColor("_Color", new Color(0.85f, 0.78f, 0.55f, 1)));
            matLineFish = mk("VoxelForge/Line", m => m.SetColor("_Color", new Color(0.13f, 0.13f, 0.13f, 0.9f)));
            matLinePrimed = mk("VoxelForge/Line", m => m.SetColor("_Color", new Color(1, 1, 0.5f, 1)));
            matCloud = mk("VoxelForge/Cloud", m => { });
            dmSky = new DynMesh("vf-sky", SPRITE_LAYOUT, 7, true);
            dmEntity = new DynMesh("vf-entities", SPRITE_LAYOUT, 7, true);
            dmMobHeld = new DynMesh("vf-mob-held", SPRITE_LAYOUT, 7, true);
            dmActiveTerrain = new DynMesh("vf-active-terrain", SPRITE_LAYOUT, 7, true);
            dmActiveItem = new DynMesh("vf-active-item", SPRITE_LAYOUT, 7, true);
            dmDropBlock = new DynMesh("vf-drop-block", SPRITE_LAYOUT, 7, true);
            dmDropItem = new DynMesh("vf-drop-item", SPRITE_LAYOUT, 7, true);
            dmCrack = new DynMesh("vf-crack", SPRITE_LAYOUT, 7, true);
            dmHeldMain = new DynMesh("vf-held-main", SPRITE_LAYOUT, 7, true);
            dmHeldOff = new DynMesh("vf-held-off", SPRITE_LAYOUT, 7, true);
            dmParticle = new DynMesh("vf-particles", PARTICLE_LAYOUT, BREAK_PARTICLE_FLOATS_PER_VERTEX, false);
            var lineLayout = new[] { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3) };
            dmLineSel = new DynMesh("vf-line-sel", lineLayout, 3, false, MeshTopology.Lines);
            dmLineProj = new DynMesh("vf-line-proj", lineLayout, 3, false, MeshTopology.Lines);
            dmLineFish = new DynMesh("vf-line-fish", lineLayout, 3, false, MeshTopology.Lines);
            dmLinePrimed = new DynMesh("vf-line-primed", lineLayout, 3, false, MeshTopology.Lines);
            initMainClouds();
            cloudMesh = new Mesh { name = "vf-clouds", indexFormat = IndexFormat.UInt32 };
            var cv = new Vector3[cloudVerts.Length / 3];
            for (int i = 0; i < cv.Length; i++) cv[i] = new Vector3(cloudVerts[i * 3], cloudVerts[i * 3 + 1], cloudVerts[i * 3 + 2]);
            cloudMesh.vertices = cv; cloudMesh.SetTriangles(cloudIdx, 0, false);
            cloudMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            frameCB = new CommandBuffer { name = "Voxel Forge frame" };
        }

        static Matrix4x4 toMatrix(float[] a)
        {
            var m = new Matrix4x4();
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m[r, c] = a[c * 4 + r];
            return m;
        }
        public static double renderScale = 1;
        public static int opaqueDraws, cutoutDraws, waterDraws, transDraws, renderVisibleCount;
        public static double avgRenderCpuMs = 0, avgRenderVisMs = 0;
        static readonly double[] renderEye = new double[3], renderDir = new double[3], renderTarget = new double[3], renderUp = { 0, 1, 0 };
        public static readonly double[] renderFog = new double[3];
        static readonly float[] heldProj = new float[16];
        static Chunk[] rdSortA = new Chunk[256], rdSortB = new Chunk[256];
        /// <summary>visible.sort(renderDistCompare): stable (like Array.prototype.sort) and allocation-free — insertion-sorted runs + bottom-up merge.</summary>
        static void sortByRenderDist(List<Chunk> v)
        {
            int n = v.Count;
            if (rdSortA.Length < n) { int c = rdSortA.Length; while (c < n) c *= 2; rdSortA = new Chunk[c]; rdSortB = new Chunk[c]; }
            Chunk[] a = rdSortA, b = rdSortB;
            for (int i = 0; i < n; i++) a[i] = v[i];
            const int RUN = 16;
            for (int s = 0; s < n; s += RUN)
            {
                int e = Math.Min(n, s + RUN);
                for (int i = s + 1; i < e; i++) { var x = a[i]; double k = x._renderDist2; int j = i - 1; while (j >= s && a[j]._renderDist2 > k) { a[j + 1] = a[j]; j--; } a[j + 1] = x; }
            }
            for (int w = RUN; w < n; w *= 2)
            {
                for (int lo = 0; lo < n; lo += 2 * w)
                {
                    int mid = Math.Min(n, lo + w), hi = Math.Min(n, lo + 2 * w), i = lo, j = mid, k = lo;
                    while (i < mid && j < hi) b[k++] = a[j]._renderDist2 < a[i]._renderDist2 ? a[j++] : a[i++];
                    while (i < mid) b[k++] = a[i++];
                    while (j < hi) b[k++] = a[j++];
                }
                var t = a; a = b; b = t;
            }
            for (int i = 0; i < n; i++) v[i] = a[i];
            Array.Clear(rdSortA, 0, n); Array.Clear(rdSortB, 0, n);
        }

        static void drawG(CommandBuffer cb, GpuMesh g, Material m)
        {
            if (g == null || g.mesh == null || g.count == 0) return;
            cb.DrawMesh(g.mesh, Matrix4x4.identity, m, g.submesh, 0);
            drawCalls++; triangles += g.tris;
        }
        static void drawDyn(CommandBuffer cb, DynMesh d, Material m, FloatList v)
        {
            if (v.n == 0 || !d.Upload(v.a, v.n)) return;
            cb.DrawMesh(d.mesh, Matrix4x4.identity, m, 0, 0);
            drawCalls++; triangles += d.indexCount / 3;
        }
        static void drawLines(CommandBuffer cb, DynMesh d, Material m, FloatList v)
        {
            if (v.n == 0 || !d.Upload(v.a, v.n)) return;
            cb.DrawMesh(d.mesh, Matrix4x4.identity, m, 0, 0);
            drawCalls++;
        }

        /// <summary>The render part of the reference render(now): sets camera matrices and records all passes.</summary>
        public static void renderWorld(Camera cam, double now, double dt, int pixelW, int pixelH)
        {
            var cb = frameCB;
            cb.Clear();
            updateMainRenderFov(dt);
            if (worldRunningNow) mainCloudDriftX += dt * 1.4;
            var skyState = mainSkyState(); double sun = skyState.light;
            bool cameraWet = started && worldReady && pointInWater(player.x, player.y + 1.62, player.z), underwater = started && worldReady && (cameraWet || player.headInWater);
            renderUnderwater = underwater;
            var fog = renderFog;
            mainTerrainLightScale = underwater ? 0.74 : 1;
            updateMainLightmap(skyState, now);
            {
                double fs = Math.Max(sun, 0.3); var fc = underwater ? new[] { 0.04, 0.15, 0.3 } : skyState.sky;
                fog[0] = fc[0] * fs; fog[1] = fc[1] * fs; fog[2] = fc[2] * fs;
            }
            double renderStart = JS.now();
            if (integrityGuardFrames > 0) integrityGuardFrames--;
            cam.backgroundColor = new Color((float)fog[0], (float)fog[1], (float)fog[2], 1);
            drawCalls = 0; triangles = 0; opaqueDraws = cutoutDraws = waterDraws = transDraws = 0;
            if (atlasArray == null || !started) { renderVisibleCount = 0; return; }
            var eye = renderEye; var dir = renderDir;
            eye[0] = player.x; eye[1] = player.y + 1.62; eye[2] = player.z;
            double cp = Math.Cos(player.pitch);
            dir[0] = Math.Sin(player.yaw) * cp; dir[1] = -Math.Sin(player.pitch); dir[2] = -Math.Cos(player.yaw) * cp;
            double viewX = dir[0], viewY = dir[1], viewZ = dir[2];
            if (player.camMode != 0)
            {
                int sign = player.camMode == 2 ? 1 : -1;
                double cd = mainCameraDistance(eye, new[] { dir[0] * sign, dir[1] * sign, dir[2] * sign });
                eye[0] += dir[0] * sign * cd; eye[1] += dir[1] * sign * cd; eye[2] += dir[2] * sign * cd;
                if (sign > 0) { viewX = -dir[0]; viewY = -dir[1]; viewZ = -dir[2]; }
            }
            renderTarget[0] = eye[0] + viewX; renderTarget[1] = eye[1] + viewY; renderTarget[2] = eye[2] + viewZ;
            double horizFar = renderDistance * CHUNK * 2, vertFar = Math.Max(Math.Abs(eye[1] - WORLD_MIN_Y), Math.Abs(WORLD_MAX_Y - eye[1])), projFar = Math.Max(420, JS.hypot(horizFar, vertFar) + CHUNK * 2);
            double aspect = (double)pixelW / Math.Max(1, pixelH);
            perspective(proj, mainRenderFov * Math.PI / 180, aspect, 0.06, projFar);
            lookAt(view, eye, renderTarget, renderUp);
            mul(vp, proj, view);
            updateFrustum(vp);
            var viewM = toMatrix(view); var projM = toMatrix(proj);
            cam.worldToCameraMatrix = viewM;
            cam.projectionMatrix = projM;
            double fogNear = underwater ? 2.5 : renderDistance * CHUNK - 26, fogFar = underwater ? 13.5 : renderDistance * CHUNK - 2;
            lastFogNear = fogNear; lastFogFar = fogFar;
            // globals (uniforms shared by all programs)
            cb.SetGlobalVector("_VF_Cam", new Vector4((float)eye[0], (float)eye[1], (float)eye[2], 0));
            cb.SetGlobalVector("_VF_Fog", new Vector4((float)fog[0], (float)fog[1], (float)fog[2], 1));
            cb.SetGlobalFloat("_VF_FogNear", (float)fogNear);
            cb.SetGlobalFloat("_VF_FogFar", (float)fogFar);
            cb.SetGlobalFloat("_VF_Light", (float)mainTerrainLightScale);
            cb.SetGlobalFloat("_VF_Hand", handLightLevelNow());
            cb.SetGlobalFloat("_VF_Time", (float)(now * 0.001));
            cb.SetGlobalFloat("_VF_FlowTile", WATER_FLOW_TILE);
            cb.SetGlobalTexture("_VF_Atlas", atlasArray);
            cb.SetGlobalTexture("_VF_Lightmap", mainLightmapTex);
            var visible = renderVisibleChunks;
            visible.Clear();
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
            double visStart = JS.now();
            collectVisibleChunksHierarchical(eye[0], eye[1], eye[2], fogFar, pcx, pcz, visible);
            if (visible.Count > 1) sortByRenderDist(visible);
            renderVisibleCount = visible.Count;
            buildOpaqueRenderList(visible, pcx, pcz);
            buildAlphaRenderLists(visible);
            avgRenderVisMs += (JS.now() - visStart - avgRenderVisMs) * 0.08;
            if (!underwater) { buildMainSkyBodies(eye[0], eye[1], eye[2]); drawDyn(cb, dmSky, matSky, mainSkyV); }
            if (xrayActive)
            {
                for (int i = 0; i < renderOpaqueCount; i++) { drawG(cb, renderOpaqueList[i].m, matXray1); opaqueDraws++; }
                for (int i = 0; i < cutoutRenderCount; i++) { drawG(cb, cutoutRenderList[i], matXray1); cutoutDraws++; }
                cb.ClearRenderTarget(true, false, Color.clear);
                for (int i = 0; i < renderOpaqueCount; i++) { drawG(cb, renderOpaqueList[i].m, matXray2); opaqueDraws++; }
                for (int i = 0; i < cutoutRenderCount; i++) { drawG(cb, cutoutRenderList[i], matXray2); cutoutDraws++; }
            }
            else
            {
                for (int i = 0; i < renderOpaqueCount; i++) { drawG(cb, renderOpaqueList[i].m, matOpaque); opaqueDraws++; }
                for (int i = 0; i < cutoutRenderCount; i++) { drawG(cb, cutoutRenderList[i], matCutout); cutoutDraws++; }
            }
            // break particles
            {
                var rx = view[0]; var ry = view[4]; var rz = view[8]; var ux = view[1]; var uy = view[5]; var uz = view[9];
                int n = buildBreakParticleVerts(eye[0], eye[1], eye[2], rx, ry, rz, ux, uy, uz);
                if (n > 0 && dmParticle.Upload(breakParticleVerts, n)) { cb.DrawMesh(dmParticle.mesh, Matrix4x4.identity, matParticle, 0, 0); drawCalls++; triangles += n / 33; }
            }
            // entities
            entityBuildSun = sun;
            buildEntityBatch();
            if (entityIndexCount > 0 && dmEntity.Upload(entityVertUpload, entityVertCount)) { cb.DrawMesh(dmEntity.mesh, Matrix4x4.identity, xrayActive ? matEntityXray : matEntity, 0, 0); drawCalls++; triangles += entityIndexCount / 3; }
            drawDyn(cb, dmMobHeld, matMobHeld, mobHeldV);
            buildMainActiveEntities();
            drawDyn(cb, dmActiveTerrain, matSprite, mainActiveTerrainV);
            drawDyn(cb, dmActiveItem, matSpriteItem, mainActiveItemV);
            buildFishingLine(); drawLines(cb, dmLineFish, matLineFish, fishLineV);
            buildWorldDropBatches(fogFar, sun);
            drawDyn(cb, dmDropBlock, matSprite, dropBlockV);
            drawDyn(cb, dmDropItem, matSpriteItem, dropItemV);
            if (buildMainMiningCracks()) drawDyn(cb, dmCrack, matCrack, mainCrackV);
            // translucent terrain, then water
            for (int i = 0; i < transRenderCount; i++) { drawG(cb, transRenderList[i].trans, matTrans); transDraws++; }
            if (underwater)
            {
                for (int i = 0; i < waterRenderCount; i++) { drawG(cb, waterRenderList[i].water, matWaterUnder); waterDraws++; }
            }
            else
            {
                for (int i = 0; i < waterRenderCount; i++) { var w = waterRenderList[i].water; if (w != null) cb.DrawMesh(w.mesh, Matrix4x4.identity, matDepth, w.submesh, 0); }
                for (int i = 0; i < waterRenderCount; i++) { drawG(cb, waterRenderList[i].water, matWater); waterDraws++; }
            }
            if (cloudsEnabled && !underwater)
            {
                const double period = 24 * 12;
                double ox = Math.Floor(player.x / period) * period + (mainCloudDriftX % period), oz = Math.Floor(player.z / period) * period;
                cb.SetGlobalVector("_VF_CloudOff", new Vector4((float)ox, 0, (float)oz, 0));
                cb.DrawMesh(cloudMesh, Matrix4x4.identity, matCloud, 0, 0);
                drawCalls++;
            }
            var hit = raycast();
            buildSelectionOutline(hit); drawLines(cb, dmLineSel, matLineSel, selectionLineV);
            if (primedTNT.Count > 0)
            {
                double a = 0.5 + 0.5 * Math.Sin(now * 0.02);
                matLinePrimed.SetColor("_Color", new Color(1, (float)a, (float)(a * 0.5), 1));
                buildPrimedOutlines(); drawLines(cb, dmLinePrimed, matLinePrimed, primedLineV);
            }
            // first-person held items: own projection, depth cleared
            buildFirstPersonHeld(dt);
            if (heldMainVisible || heldOffVisible)
            {
                cb.ClearRenderTarget(true, false, Color.clear);
                perspective(heldProj, 1.1, aspect, 0.05, 8);
                cb.SetViewProjectionMatrices(Matrix4x4.identity, toMatrix(heldProj));
                if (heldMainVisible && heldMain.v.n > 0 && dmHeldMain.Upload(heldMain.v.a, heldMain.v.n)) { cb.DrawMesh(dmHeldMain.mesh, toMatrix(heldMain.matrix), heldMain.terrain ? matHeld : matHeldItem, 0, 0); drawCalls++; }
                if (heldOffVisible && heldOff.v.n > 0 && dmHeldOff.Upload(heldOff.v.a, heldOff.v.n)) { cb.DrawMesh(dmHeldOff.mesh, toMatrix(heldOff.matrix), heldOff.terrain ? matHeld : matHeldItem, 0, 0); drawCalls++; }
                cb.SetViewProjectionMatrices(viewM, projM);
            }
            avgRenderCpuMs += (JS.now() - renderStart - avgRenderCpuMs) * 0.08;
        }
        public static bool worldRunningNow = false;
    }
}
