#if UNITY_EDITOR
using UnityEditor;
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
        static void Setup()
        {
            // main/WebGL performs its texture/light arithmetic in display (gamma) values, not a linear-light pipeline.
            if (PlayerSettings.colorSpace != ColorSpace.Gamma) PlayerSettings.colorSpace = ColorSpace.Gamma;
            // Streaming creates unavoidable long-lived world objects; incremental GC spreads marking work
            // across frames instead of concentrating it into an occasional long stop-the-world collection.
            if (!PlayerSettings.gcIncremental) PlayerSettings.gcIncremental = true;
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
