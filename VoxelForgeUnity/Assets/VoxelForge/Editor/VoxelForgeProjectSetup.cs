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
            EnsureScene(false);
        }

        /// <summary>The untitled startup scene of a fresh project: never saved, nothing changed in it.</summary>
        static bool PristineUntitled(Scene s) { return string.IsNullOrEmpty(s.path) && !s.isDirty; }

        static bool EnsureScene(bool interactive)
        {
            if (!File.Exists(ScenePath))
            {
                var active = SceneManager.GetActiveScene();
                bool untitled = string.IsNullOrEmpty(active.path);
                // Unity refuses an additive new scene while an untitled scene is open, and a Single new scene would
                // discard it — so an untitled scene with user changes must be saved (or explicitly discarded) first.
                if (untitled && active.isDirty)
                {
                    if (!interactive || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        Debug.Log("[Voxel Forge] The open Untitled scene has unsaved changes, so " + ScenePath + " was not created yet. Save the scene or use Voxel Forge/Play.");
                        return false;
                    }
                    active = SceneManager.GetActiveScene();
                    untitled = string.IsNullOrEmpty(active.path);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                if (untitled)
                {
                    // Fresh project: replace the untitled startup scene with the (empty) game scene.
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                else
                {
                    // A saved scene is open: create the game scene next to it and leave the user's scene untouched.
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    EditorSceneManager.CloseScene(scene, true);
                }
                Debug.Log("[Voxel Forge] Created " + ScenePath + " (empty: the game bootstraps itself).");
            }
            var scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(scenes).ToArray();
            if (PristineUntitled(SceneManager.GetActiveScene()))
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        [MenuItem("Voxel Forge/Play")]
        static void Play()
        {
            Apply();
            if (!EnsureScene(true)) return;
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            EditorApplication.isPlaying = true;
        }
    }
}
