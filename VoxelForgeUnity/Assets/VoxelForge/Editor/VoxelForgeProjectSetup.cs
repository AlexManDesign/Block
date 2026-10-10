// Voxel Forge — Unity port. Editor-side project defaults required for parity with the WebGL reference:
// the reference blends and fogs in non-linear (gamma) space, and MSAA is disabled (antialias: false).
using UnityEditor;
using UnityEngine;

namespace VoxelForge.EditorTools
{
    [InitializeOnLoad]
    static class VoxelForgeProjectSetup
    {
        static VoxelForgeProjectSetup()
        {
            EditorApplication.delayCall += Apply;
        }

        [MenuItem("Voxel Forge/Apply Project Settings")]
        static void Apply()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Gamma)
            {
                PlayerSettings.colorSpace = ColorSpace.Gamma;
                Debug.Log("[Voxel Forge] Color space set to Gamma (WebGL reference parity).");
            }
            if (QualitySettings.antiAliasing != 0) QualitySettings.antiAliasing = 0;
            PlayerSettings.productName = "Voxel Forge";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.runInBackground = true;
        }
    }
}
