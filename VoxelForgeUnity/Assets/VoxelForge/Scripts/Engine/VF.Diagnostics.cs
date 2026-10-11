// Voxel Forge — Unity port. Console diagnostics for checking a build without a debugger: shader/texture support
// at start-up and a world/render state snapshot a few seconds after a world is started.
using System;
using System.Text;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        /// <summary>Logs graphics capabilities and every VoxelForge shader that failed to load or is unsupported.</summary>
        public static void logGraphicsDiagnostics()
        {
            var sb = new StringBuilder("[Voxel Forge] graphics: ");
            sb.Append(SystemInfo.graphicsDeviceType).Append(" · ").Append(SystemInfo.graphicsDeviceName)
              .Append(" · 2D array textures ").Append(SystemInfo.supports2DArrayTextures ? "yes" : "NO")
              .Append(" · color space ").Append(QualitySettings.activeColorSpace)
              .Append(" · atlas ").Append(atlasArray != null ? atlasArray.width + "x" + atlasArray.height + "x" + atlasArray.depth + " mips " + atlasArray.mipmapCount : "MISSING");
            Debug.Log(sb.ToString());
            string bad = "";
            foreach (var m in new[] { matOpaque, matCutout, matTrans, matXray1, matWater, matDepth, matSprite, matSky, matEntity, matParticle, matLineSel, matCloud })
            {
                if (m == null) { bad += " <null material>"; continue; }
                if (m.shader == null || !m.shader.isSupported || !m.shader.name.StartsWith("VoxelForge/")) bad += " " + (m.shader != null ? m.shader.name + (m.shader.isSupported ? "" : " (unsupported)") : "<no shader>");
            }
            if (bad.Length > 0) Debug.LogError("[Voxel Forge] shaders not usable on this GPU/API:" + bad);
            if (!SystemInfo.supports2DArrayTextures) Debug.LogError("[Voxel Forge] this GPU/API has no 2D array textures — terrain cannot render.");
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma) Debug.LogWarning("[Voxel Forge] color space is " + QualitySettings.activeColorSpace + " (reference parity needs Gamma: Voxel Forge/Apply Project Settings).");
        }

        /// <summary>One-line world/render state used by the start-up check in VoxelForgeGame.</summary>
        public static string worldDiagnostics(int cameraRenders)
        {
            int meshed = 0, gpu = 0;
            foreach (var c in chunks.Values) { if (c.meshBuilt) meshed++; if (c.opaque != null || c.cutout != null || c.water != null || c.trans != null) gpu++; }
            return "[Voxel Forge] world: started " + started + " · ready " + worldReady + " · start screen " + startScreenVisible
                + " · spawn pending " + initialSpawnPending + "/" + resumeLoadPending
                + " · chunks " + chunks.Count + " meshed " + meshed + " on GPU " + gpu
                + " · gen " + genPending.Count + "/" + genJobs.Count + " mesh " + meshPending.Count + "/" + meshJobs.Count + (workerEngineFailed ? " · WORKERS FAILED" : "")
                + " · visible " + renderVisibleCount + " draws " + drawCalls + " tris " + triangles
                + " · camera renders " + cameraRenders
                + " · player " + JS.Fixed(player.x, 1) + " " + JS.Fixed(player.y, 1) + " " + JS.Fixed(player.z, 1);
        }
    }
}
