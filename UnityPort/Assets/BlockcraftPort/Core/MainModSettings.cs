using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Runtime gates for the seven main.js mods. The browser build stores enable state in
    /// blockcraft.mods.v1 and separately handles ownership/IAP. The standalone Unity port has no
    /// payments layer, so every gameplay mod is available and enabled by default while still using
    /// persistent per-mod toggles. This keeps the mechanics testable without coupling hot paths to UI.
    /// </summary>
    public enum MainMod : byte { Torch, KeepInventory, Minimap, FastLeaves, XRay, ShipControl, Sharks }

    public static class MainModSettings
    {
        const string Prefix="blockcraft.mod.enabled.";
        static readonly sbyte[] cache={-1,-1,-1,-1,-1,-1,-1};
        static readonly string[] ids={"torch","keepinv","minimap","fastleaves","xray","shipctl","sharks"};

        public static bool Enabled(MainMod mod)
        {
            int i=(int)mod;
            if(cache[i]<0)cache[i]=(sbyte)(PlayerPrefs.GetInt(Prefix+ids[i],1)!=0?1:0);
            return cache[i]!=0;
        }

        public static void SetEnabled(MainMod mod,bool enabled)
        {
            int i=(int)mod;cache[i]=(sbyte)(enabled?1:0);
            PlayerPrefs.SetInt(Prefix+ids[i],enabled?1:0);
        }

        public static bool TorchEnabled=>Enabled(MainMod.Torch);
        public static bool KeepInventoryEnabled=>Enabled(MainMod.KeepInventory);
        public static bool MinimapEnabled=>Enabled(MainMod.Minimap);
        public static bool FastLeavesEnabled=>Enabled(MainMod.FastLeaves);
        public static bool XRayEnabled=>Enabled(MainMod.XRay);
        public static bool ShipControlEnabled=>Enabled(MainMod.ShipControl);
        public static bool SharksEnabled=>Enabled(MainMod.Sharks);
    }
}
