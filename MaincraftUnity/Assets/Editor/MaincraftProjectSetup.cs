// Editor setup of the port: the original renders in sRGB values as they are (no linear lighting), so the
// project uses Unity's Gamma colour space. Set once when the project is opened.
using UnityEditor;
using UnityEngine;

namespace Maincraft.EditorTools
{
    [InitializeOnLoad]
    static class MaincraftProjectSetup
    {
        static MaincraftProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (PlayerSettings.colorSpace == ColorSpace.Gamma) return;
                PlayerSettings.colorSpace = ColorSpace.Gamma;
                Debug.Log("Maincraft: Color Space set to Gamma (as the original renders)");
            };
        }
    }
}
