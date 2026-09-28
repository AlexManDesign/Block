using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Ports main.js' 16x16 sky/block light lookup texture and distance fog uniforms.
    /// The LUT is tiny and updated at a low cadence; all terrain materials share it through shader globals.
    /// </summary>
    public sealed class VoxelRenderEnvironment : MonoBehaviour
    {
        public static bool XRayActive { get; private set; }
        public VoxelWorld World;
        public Camera Cam;
        public MainPlayerController Player;
        Texture2D lightmap;
        Color32[] pixels;
        float flicker, flickerTarget;
        float nextFlickerAt;
        bool appliedXray;
        float appliedWorldLight=float.NaN,appliedFogNear=float.NaN,appliedFogFar=float.NaN,appliedHeldLight=float.NaN;
        public float EffectiveFogNear { get; private set; }
        public float EffectiveFogFar { get; private set; }

        void Awake()
        {
            lightmap = new Texture2D(16, 16, TextureFormat.RGBA32, false, true)
            {
                name = "BlockcraftLightmap16",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color32[16 * 16];
            Shader.SetGlobalTexture("_VoxelLightmap", lightmap);
            Shader.SetGlobalFloat("_VoxelHeldLight",0f);
            RenderSettings.fog = false;
            UpdateLut(true);
        }

        void OnDestroy()
        {
            XRayActive=false;
            if (lightmap != null) Destroy(lightmap);
        }

        void Update()
        {
            if (Cam == null) Cam = Camera.main;
            if (World == null) World = VoxelWorld.Instance;
            if (Player == null) Player = FindObjectOfType<MainPlayerController>();

            // main xs(): X only toggles the render mode when the xray mod is enabled.
            if(!MainModSettings.XRayEnabled)XRayActive=false;
            else if(Input.GetKeyDown(KeyCode.X))XRayActive=!XRayActive;
            if(World!=null&&appliedXray!=XRayActive){World.SetXray(XRayActive);appliedXray=XRayActive;}

            // main.js calls eg() every rendered frame. The random target changes every ~80 ms,
            // but the current flicker value eases toward it every frame.
            UpdateLut(false);
            UpdateGlobals();
        }

        static float Curve(float a)
        {
            return a / Mathf.Max(.0001f, 4f - 3f * a);
        }

        void UpdateLut(bool force)
        {
            if (pixels == null || lightmap == null) return;

            float now = Time.realtimeSinceStartup;
            if (force || now >= nextFlickerAt)
            {
                nextFlickerAt = now + .08f;
                flickerTarget = (Random.value - .5f) * .22f;
            }
            flicker += (flickerTarget - flicker) * .3f;

            bool changed=force;
            float day = DayNight.DayAmount;
            float sunset = DayNight.SunsetAmount;
            float skyOffset = (1f - day) * 11f;
            float blockFlicker = 1f + flicker * .35f;
            float night = 1f - day;
            float skyR = 1f - night * .45f;
            float skyG = 1f - night * .35f;
            float skyB = 1f - night * .10f;
            if (sunset > 0f)
            {
                skyR = Mathf.Lerp(skyR, 1.12f, sunset * .6f);
                skyG = Mathf.Lerp(skyG, .82f, sunset * .6f);
                skyB = Mathf.Lerp(skyB, .58f, sunset * .6f);
            }

            // Texture x = block light, y = sky light, identical to main's lookup coordinates.
            for (int sky = 0; sky < 16; sky++)
            {
                float s = Mathf.Max(0f, sky - skyOffset);
                float skyCurve = Curve(s / 15f);
                skyCurve = Mathf.Max(skyCurve, sky / 15f * .364f);
                float sr = skyCurve * skyR, sg = skyCurve * skyG, sb = skyCurve * skyB;

                for (int block = 0; block < 16; block++)
                {
                    float b = block / 15f * blockFlicker;
                    if (b > 1f) b = 1f;
                    float bc = Mathf.Min(1f, Curve(b) * 2.4f);
                    bc = Mathf.Max(bc, block / 15f * .85f);
                    float br = bc;
                    float bg = bc * (bc * .6f + .4f);
                    float bb = bc * (bc * bc * .6f + .4f);
                    float r = Mathf.Max(sr + br, .28f);
                    float g = Mathf.Max(sg + bg, .29f);
                    float bl = Mathf.Max(sb + bb, .32f);
                    int pi=sky*16+block;
                    Color32 next=new Color32(
                        (byte)Mathf.Clamp((int)(r * 255f), 0, 255),
                        (byte)Mathf.Clamp((int)(g * 255f), 0, 255),
                        (byte)Mathf.Clamp((int)(bl * 255f), 0, 255), 255);
                    if(!force&&!next.Equals(pixels[pi])) changed=true;
                    pixels[pi]=next;
                }
            }
            // main.js uploads this 16x16 LUT every frame via texSubImage2D. In Unity Texture2D.Apply
            // can be a much more expensive native/GPU synchronization point. Preserve byte-identical
            // output, but skip the upload when the quantized Color32 LUT is unchanged.
            if(force||changed)
            {
                lightmap.SetPixels32(pixels);
                lightmap.Apply(false, false);
            }
        }

        void UpdateGlobals()
        {
            if (Cam == null || World == null) return;
            Vector3 p = Cam.transform.position;
            BlockId inside = World.GetBlock(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
            // main.js: underwater rendering is enabled for WATER/flowing-water or aquatic blocks (EA || NA),
            // not for lava.
            bool submerged = BlockRegistry.IsWater(inside) || BlockRegistry.IsAquatic(inside);

            Color sky = submerged ? new Color(.05f, .18f, .42f, 1f) : DayNight.SkyColor;
            float worldLight = submerged ? .8f : 1f;
            // main clears/fogs with sky * max(day world light, .3), while uLight remains 1 (.8 underwater).
            Color clear = sky * Mathf.Max(DayNight.WorldLight, .3f); clear.a = 1f;
            Cam.backgroundColor = clear;

            // main.js aI(): fog is a camera/render setting, not a streaming-progress indicator.
            // Keep it exact and stable: air uses RENDER_R*16-26 .. RENDER_R*16-2, underwater 4..18.
            // R62 temporarily tied fog to an integer GPU-ready chunk-ring value;
            // every upload/drop could therefore jump the horizon by roughly 16 blocks. R64 removes
            // that coupling completely. Streaming latency is solved in VoxelWorld by predictive
            // generation/mesh/upload work ahead of creative flight, so fog never pulses with workers.
            float fogNear = submerged ? 4f : World.RenderDistance * 16f - 26f;
            float fogFar  = submerged ? 18f : World.RenderDistance * 16f - 2f;
            EffectiveFogNear=fogNear;EffectiveFogFar=fogFar;

            // _VoxelLightmap is bound once in Awake; updating the Texture2D contents does not require
            // rebinding the same global texture object every frame.
            if(worldLight!=appliedWorldLight){Shader.SetGlobalFloat("_VoxelWorldLight",worldLight);appliedWorldLight=worldLight;}
            Shader.SetGlobalColor("_VoxelFogColor", clear);
            if(fogNear!=appliedFogNear){Shader.SetGlobalFloat("_VoxelFogNear",fogNear);appliedFogNear=fogNear;}
            if(fogFar!=appliedFogFar){Shader.SetGlobalFloat("_VoxelFogFar",fogFar);appliedFogFar=fogFar;}

            // main aI()/rg(): this is one scalar uniform, not a light object or extra render pass.
            // Cache the write so a stationary selection costs nothing on the Unity->native boundary.
            float held=Player!=null?Player.SourceHeldLightLevel:0f;
            if(held!=appliedHeldLight){Shader.SetGlobalFloat("_VoxelHeldLight",held);appliedHeldLight=held;}
        }
    }
}
