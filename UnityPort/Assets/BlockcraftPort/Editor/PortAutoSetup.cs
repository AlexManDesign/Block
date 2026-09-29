#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace BlockcraftPort.Editor
{
    [InitializeOnLoad]
    public static class PortAutoSetup
    {
        static PortAutoSetup(){EditorApplication.delayCall+=Setup;}
        static void ConfigurePixelTexture(string path)
        {
            var ti=AssetImporter.GetAtPath(path) as TextureImporter;
            if(ti==null)return;
            bool dirty=ti.filterMode!=FilterMode.Point||ti.mipmapEnabled||ti.textureCompression!=TextureImporterCompression.Uncompressed||ti.wrapMode!=TextureWrapMode.Clamp||ti.anisoLevel!=0||!ti.sRGBTexture;
            if(!dirty)return;
            ti.filterMode=FilterMode.Point;ti.mipmapEnabled=false;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.wrapMode=TextureWrapMode.Clamp;ti.anisoLevel=0;ti.sRGBTexture=true;ti.SaveAndReimport();
        }
        const string ReleaseOptimizationKey="Blockcraft.ReleaseCodeOptimizationApplied";

        // Unity's Editor defaults to "Debug" code optimization, which runs every script with the
        // debugger agent attached. Measured on the port's own pipeline (Mono, tools/parity) that makes
        // world generation ~2.3x, light stitching ~4x and meshing ~3x slower than a Release build, so
        // streaming in Play Mode falls far behind main.js. Switch to Release once per machine; a user
        // who deliberately switches back to Debug (status-bar bug icon) is not overridden again.
        static void ApplyReleaseCodeOptimizationOnce()
        {
            if(EditorPrefs.GetBool(ReleaseOptimizationKey,false))return;
            EditorPrefs.SetBool(ReleaseOptimizationKey,true);
            if(CompilationPipeline.codeOptimization==CodeOptimization.Release)return;
            Debug.Log("Blockcraft: switching Editor code optimization to Release for Play Mode performance (status-bar bug icon to change back).");
            CompilationPipeline.codeOptimization=CodeOptimization.Release;
        }

        [MenuItem("Blockcraft/Code Optimization: Release (fast Play Mode)")]
        static void MenuRelease(){CompilationPipeline.codeOptimization=CodeOptimization.Release;}

        [MenuItem("Blockcraft/Code Optimization: Debug (breakpoints)")]
        static void MenuDebug(){CompilationPipeline.codeOptimization=CodeOptimization.Debug;}

        static void Setup()
        {
            // main/WebGL performs its texture/light arithmetic in display (gamma) values, not a linear-light pipeline.
            if (PlayerSettings.colorSpace != ColorSpace.Gamma) PlayerSettings.colorSpace = ColorSpace.Gamma;
            // Streaming creates unavoidable long-lived world objects; incremental GC spreads marking work
            // across frames instead of concentrating it into an occasional long stop-the-world collection.
            if (!PlayerSettings.gcIncremental) PlayerSettings.gcIncremental = true;
            ApplyReleaseCodeOptimizationOnce();
            ConfigurePixelTexture("Assets/Resources/Voxel/atlas.png");
            ConfigurePixelTexture("Assets/Resources/Voxel/main_items.png");
            ConfigurePixelTexture("Assets/Resources/Voxel/main_sun.png");
            ConfigurePixelTexture("Assets/Resources/Voxel/main_moon.png");
            ConfigurePixelTexture("Assets/Resources/Voxel/main_celestial.png");
            var entityTextures=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/Voxel/Entities"});
            for(int i=0;i<entityTextures.Length;i++)ConfigurePixelTexture(AssetDatabase.GUIDToAssetPath(entityTextures[i]));
            const string sceneFolder="Assets/Scenes";
            if(!AssetDatabase.IsValidFolder(sceneFolder))
            {
                if(!System.IO.Directory.Exists(sceneFolder)) AssetDatabase.CreateFolder("Assets","Scenes");
                else AssetDatabase.Refresh();
            }
            const string path=sceneFolder+"/PortDemo.unity";if(System.IO.File.Exists(path))return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("BlockcraftPort").AddComponent<PortBootstrap>();EditorSceneManager.SaveScene(scene,path);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true)};AssetDatabase.SaveAssets();
        }
    }
}
#endif
