using UnityEngine;
using UnityEngine.Rendering;

namespace BlockcraftPort
{
    /// <summary>
    /// Direct chunk submission backend. main.js owns chunk VAO/VBO/IBO objects and submits them
    /// directly every frame; it does not create a scene Renderer per chunk. This component mirrors
    /// that architecture with reusable camera command buffers and no per-chunk GameObjects.
    /// The alpha stage is deliberately split as world-water -> moving-ship-water -> glass so moving
    /// vessels can update a tiny command buffer without re-recording every visible static chunk.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MainChunkDirectRenderer : MonoBehaviour
    {
        public VoxelWorld World;

        Camera cam;
        CommandBuffer terrain;
        CommandBuffer terrainDynamic;
        CommandBuffer alphaWater;
        CommandBuffer alphaShip;
        CommandBuffer alphaGlass;
        CommandBuffer alphaDynamic;
        int lastRevision = int.MinValue;
        int lastOverlayRevision = int.MinValue;
        int lastCandidateRevision = int.MinValue;
        Vector3 lastPos;
        Quaternion lastRot;
        float lastFov, lastAspect, lastNear, lastFar;
        bool initialized;

        public void Initialize(VoxelWorld world, Camera camera)
        {
            World = world;
            cam = camera != null ? camera : GetComponent<Camera>();
            EnsureBuffers();
            InvalidateAll();
        }

        void Awake(){cam = GetComponent<Camera>();}
        void OnEnable(){EnsureBuffers();InvalidateAll();}
        void OnDisable(){DetachBuffers();}

        void OnDestroy()
        {
            DetachBuffers();
            Release(ref terrain);Release(ref terrainDynamic);Release(ref alphaWater);
            Release(ref alphaShip);Release(ref alphaGlass);Release(ref alphaDynamic);
        }

        static void Release(ref CommandBuffer cb){if(cb==null)return;cb.Release();cb=null;}

        public void Invalidate()
        {
            // Static chunk-pass invalidation must not throw away the candidate cache unless
            // VoxelWorld also increments its separate candidate revision.
            lastRevision = int.MinValue;
        }

        void InvalidateAll()
        {
            lastRevision = int.MinValue;
            lastOverlayRevision = int.MinValue;
            lastCandidateRevision = int.MinValue;
            initialized = false;
        }

        public void ClearWorld(VoxelWorld world)
        {
            if(World==world)World=null;
            terrain?.Clear();terrainDynamic?.Clear();alphaWater?.Clear();alphaShip?.Clear();alphaGlass?.Clear();alphaDynamic?.Clear();
            InvalidateAll();
        }

        void EnsureBuffers()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (terrain == null) terrain = new CommandBuffer { name = "Blockcraft Direct Terrain" };
            if (terrainDynamic == null) terrainDynamic = new CommandBuffer { name = "Blockcraft Direct Terrain Dynamic" };
            if (alphaWater == null) alphaWater = new CommandBuffer { name = "Blockcraft Direct World Water" };
            if (alphaShip == null) alphaShip = new CommandBuffer { name = "Blockcraft Direct Moving Ship Water" };
            if (alphaGlass == null) alphaGlass = new CommandBuffer { name = "Blockcraft Direct Glass" };
            if (alphaDynamic == null) alphaDynamic = new CommandBuffer { name = "Blockcraft Direct Alpha Dynamic" };

            // Source pass order: celestial+terrain -> dynamic opaque -> world water -> ship water
            // -> transparent/glass -> clouds/selection. Buffers attached to one event execute in
            // insertion order, so the three alpha buffers preserve tI(true)'s exact placement.
            DetachBuffers();
            cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, terrain);
            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, terrainDynamic);
            cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaWater);
            cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaShip);
            cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaGlass);
            cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, alphaDynamic);
        }

        void DetachBuffers()
        {
            if (cam == null) return;
            if (terrain != null) cam.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, terrain);
            if (terrainDynamic != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, terrainDynamic);
            if (alphaWater != null) cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaWater);
            if (alphaShip != null) cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaShip);
            if (alphaGlass != null) cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, alphaGlass);
            if (alphaDynamic != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, alphaDynamic);
        }

        void OnPreCull()
        {
            if (World == null) World = VoxelWorld.Instance;
            if (cam == null) cam = GetComponent<Camera>();
            if (World == null || cam == null || terrain == null || terrainDynamic == null || alphaWater == null || alphaShip == null || alphaGlass == null || alphaDynamic == null) return;

            Vector3 p = cam.transform.position;
            Quaternion r = cam.transform.rotation;
            int revision = World.RenderRevision;
            int overlayRevision = World.RenderOverlayRevision;
            int candidateRevision = World.RenderCandidateRevision;
            bool structureChanged=!initialized||revision!=lastRevision;
            bool overlayChanged=!initialized||overlayRevision!=lastOverlayRevision;
            bool candidatesChanged=!initialized||candidateRevision!=lastCandidateRevision;
            bool viewChanged=structureChanged||p!=lastPos||r!=lastRot||
                             cam.fieldOfView!=lastFov||cam.aspect!=lastAspect||
                             cam.nearClipPlane!=lastNear||cam.farClipPlane!=lastFar;
            if(viewChanged)
            {
                // Re-test visibility on view change; large static command lists are rewritten only
                // if their visible sequence or static render structure actually changed.
                World.RebuildDirectDrawCommands(cam,terrain,alphaWater,alphaGlass,structureChanged,candidatesChanged);
            }
            if(overlayChanged)World.RebuildDirectOverlayCommands(terrainDynamic,alphaShip,alphaDynamic);
            if(!viewChanged&&!overlayChanged)return;

            initialized = true;
            lastRevision = revision;
            lastOverlayRevision = overlayRevision;
            lastCandidateRevision = candidateRevision;
            lastPos = p;lastRot = r;lastFov = cam.fieldOfView;lastAspect = cam.aspect;
            lastNear = cam.nearClipPlane;lastFar = cam.farClipPlane;
        }
    }
}
