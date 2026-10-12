// Terrain pass of the original renderer (see Terrain.cginc). Drawn by Renderer with DrawProcedural.
Shader "Maincraft/TerrainShip"
{
    Properties { [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            // the game's faces wind counter-clockwise like GL's, Unity's front faces clockwise: Front culled by default
            Cull [_Cull]
            ZTest LEqual
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define SHIP
            #define CUTOUT
            #include "Terrain.cginc"
            ENDCG
        }
    }
}
