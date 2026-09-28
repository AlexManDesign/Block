using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>Static entity atlas assembled from the current main.js resource sheets.</summary>
    public static class EntityAtlasLayout
    {
        public const int AtlasWidth = 256;
        public const int AtlasHeight = 448;
        static readonly Dictionary<string,Vector2Int> Origins = new Dictionary<string,Vector2Int>
        {
            {"entity_pig",new Vector2Int(0,0)},
            {"entity_cow",new Vector2Int(64,0)},
            {"entity_sheep_combined",new Vector2Int(128,0)},
            {"entity_chicken",new Vector2Int(192,0)},
            {"entity_zombie",new Vector2Int(0,64)},
            {"entity_skeleton",new Vector2Int(64,64)},
            {"entity_creeper",new Vector2Int(128,64)},
            {"entity_sheep_sheared",new Vector2Int(192,64)},
            {"entity_sheep_black",new Vector2Int(0,128)},
            {"entity_sheep_gray",new Vector2Int(64,128)},
            {"entity_sheep_light_gray",new Vector2Int(128,128)},
            {"entity_sheep_brown",new Vector2Int(192,128)},
            {"entity_sheep_pink",new Vector2Int(0,192)},
            {"entity_sheep_sheared_black",new Vector2Int(64,192)},
            {"entity_sheep_sheared_gray",new Vector2Int(128,192)},
            {"entity_sheep_sheared_light_gray",new Vector2Int(192,192)},
            {"entity_sheep_sheared_brown",new Vector2Int(0,256)},
            {"entity_sheep_sheared_pink",new Vector2Int(64,256)},
            {"entity_spider_combined",new Vector2Int(0,320)},
            {"entity_enderman_combined",new Vector2Int(64,320)},
            {"entity_slime",new Vector2Int(128,320)},
            {"entity_salmon",new Vector2Int(192,320)},
            {"entity_shark",new Vector2Int(0,384)},
            {"entity_player",new Vector2Int(64,384)}
        };

        public static Vector2 UV(string textureName, Vector2 sourcePixel)
        {
            if(!Origins.TryGetValue(textureName,out Vector2Int o))o=Vector2Int.zero;
            return new Vector2((o.x+sourcePixel.x)/AtlasWidth,1f-(o.y+sourcePixel.y)/AtlasHeight);
        }
    }
}
