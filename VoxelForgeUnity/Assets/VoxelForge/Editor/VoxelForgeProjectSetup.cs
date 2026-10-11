// Voxel Forge — Unity port. Editor-side project defaults required for parity with the WebGL reference:
// the reference blends and fogs in non-linear (gamma) space, and MSAA is disabled (antialias: false).
// Also makes the project buildable out of the box: creates the (empty) game scene and registers it in
// Build Settings. The game itself needs no scene objects — VoxelForgeGame bootstraps on scene load.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelForge.EditorTools
{
    [InitializeOnLoad]
    static class VoxelForgeProjectSetup
    {
        const string ScenePath = "Assets/Scenes/VoxelForge.unity";

        static VoxelForgeProjectSetup()
        {
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Apply(); };
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
            EnsureScene();
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // Additive + close, so whatever scene is open (and possibly dirty) is left untouched.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.Refresh();
                Debug.Log("[Voxel Forge] Created " + ScenePath + " (empty: the game bootstraps itself).");
            }
            var scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(scenes).ToArray();
            // Fresh project: replace the untitled, empty startup scene with the game scene.
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty && active.rootCount <= 2)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Voxel Forge/Play")]
        static void Play()
        {
            Apply();
            if (SceneManager.GetActiveScene().path != ScenePath && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
    }
}
