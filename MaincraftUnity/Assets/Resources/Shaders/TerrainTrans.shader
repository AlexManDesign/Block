// Terrain pass of the original renderer (see Terrain.cginc). Drawn by Renderer with DrawProcedural.
Shader "Maincraft/TerrainTrans"
{
    Properties { [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            // the game's faces wind counter-clockwise like GL's, Unity's front faces clockwise: Front culled by default
            Cull [_Cull]
            ZTest LEqual
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            // a water face lying in the plane of a neighbour face stays behind it (polygonOffset(1, 2))
            Offset 1, 2
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define TRANS
            #include "Terrain.cginc"
            ENDCG
        }
    }
}
