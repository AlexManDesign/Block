using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Player movement ported from main.js z8()/uI(). Creative permits double-Space flight;
    /// Survival uses the same ground/swim movement but forbids creative flight. Player transform
    /// stores feet position; the camera is the eye.
    /// </summary>
    public sealed class MainPlayerController : MonoBehaviour
    {
        public Camera Cam;
        public float Sensitivity = .12f;
        public MainGameMode Mode { get; private set; } = MainGameMode.Creative;
        public bool IsCreative => Mode == MainGameMode.Creative;
        public float Health { get; private set; } = 20f;
        public int Hunger { get; private set; } = 20;
        public float Air { get; private set; } = 10f;
        public float Exhaustion { get; private set; }
        public bool Dead { get; private set; }

        public bool OnGround { get; private set; }
        public bool Flying { get; private set; }
        public bool InWater { get; private set; }
        public bool HeadInWater { get; private set; }
        public bool InLava { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Sneaking { get; private set; }
        public bool Swimming { get; private set; }
        public bool EndermanPumpkinVision { get; private set; }
        public int SourceArmorPoints { get; private set; }
        public int CameraMode { get; private set; } // main p.camMode: 0=first, 1=third-behind, 2=third-front
        public float MainYawRadians => mainYaw;
        public float MainPitchRadians => mainPitch;
        public float CurrentHeight => height;
        public Vector3 AimOrigin => transform.position + Vector3.up * (height - EyeOffset);
        public Vector3 AimDirection { get { float cp=Mathf.Cos(mainPitch); return new Vector3(Mathf.Sin(mainYaw)*cp,Mathf.Sin(mainPitch),Mathf.Cos(mainYaw)*cp); } }
        public Vector3 Velocity { get; private set; }
        public bool VehicleRiding { get; private set; }
        public bool GameplayUiOpen { get; private set; }

        const float Radius = .3f;          // main Re
        const float StandingHeight = 1.8f; // main rt
        const float SneakHeight = 1.5f;    // main V4
        const float SwimHeight = .85f;     // main Or
        const float StepHeight = .6f;      // main F4
        const float EyeOffset = .18f;

        const float WalkSpeed = 4.4f;
        const float SprintSpeed = 7.2f;
        const float SneakSpeed = 1.3f;
        const float FlySpeed = 10f;
        const float FlySprintSpeed = 16f;
        const float FlyVerticalSpeed = 8f;
        const float JumpSpeed = 7.6f;
        const float Gravity = 23f;
        const float MaxFall = 42f;
        const float DoubleTapSeconds = .300f;

        float height = StandingHeight;
        // Keep camera state in MAIN.JS coordinates, in radians.
        // main first-world defaults: yaw = PI/2, pitch = -0.15.
        float mainYaw = Mathf.PI * .5f;
        float mainPitch = -.15f;
        float fov = 70f;
        float bobPhase;
        float swingPhase;
        public BlockId SelectedBlock
        {
            get
            {
                // main hn(): both modes hold whatever is in p.survInv[p.hotbarSel].
                MainInventoryStackData s=survivalInventory.Selected;
                return s!=null&&s.block?(BlockId)s.blockId:BlockId.Air;
            }
        }
        public MainSurvivalInventory SurvivalInventory => survivalInventory;
        public int SurvivalHotbarSelected => survivalInventory.HotbarSelected;
        // main rg(): held TORCH contributes block light 14. GLOW/JACK_O_LANTERN/SEA_LANTERN
        // are not yet represented by BlockId in this port, so their source level 15 remains pending.
        public float SourceHeldLightLevel
        {
            get
            {
                if(!MainModSettings.TorchEnabled)return 0f;
                BlockId primary=SelectedBlock;
                return primary==BlockId.Torch||FirstPersonOffhandBlock==BlockId.Torch?14f:0f;
            }
        }
        public float FirstPersonBobX { get; private set; }
        public float FirstPersonBobY { get; private set; }
        public float FirstPersonSwing { get; private set; }

        // Source main.js uses one numeric namespace for blocks/items and a separate offhand stack.
        // The current gameplay port still has a block-only hotbar, so these visual item hooks are
        // intentionally data-only: the inventory/item port can drive them without changing the renderer.
        public string FirstPersonPrimaryItemTexture { get; private set; }
        public string FirstPersonOffhandItemTexture { get; private set; }
        public BlockId FirstPersonOffhandBlock { get; private set; } = BlockId.Air;
        public Color FirstPersonPrimaryItemTint { get; private set; } = Color.white;
        public Color FirstPersonOffhandItemTint { get; private set; } = Color.white;
        public float FirstPersonBowCharge { get; private set; }

        public void SetFirstPersonPrimaryItem(string textureKey)
        { SetFirstPersonPrimaryItem(textureKey,Color.white); }
        public void SetFirstPersonPrimaryItem(string textureKey,Color tint)
        { FirstPersonPrimaryItemTexture=textureKey;FirstPersonPrimaryItemTint=tint; }
        public void ClearFirstPersonPrimaryItem()
        { FirstPersonPrimaryItemTexture=null;FirstPersonPrimaryItemTint=Color.white;FirstPersonBowCharge=0f; }
        public void SetFirstPersonOffhandItem(string textureKey)
        { SetFirstPersonOffhandItem(textureKey,Color.white); }
        public void SetFirstPersonOffhandItem(string textureKey,Color tint)
        { FirstPersonOffhandItemTexture=textureKey;FirstPersonOffhandBlock=BlockId.Air;FirstPersonOffhandItemTint=tint; }
        public void SetFirstPersonOffhandBlock(BlockId id)
        { FirstPersonOffhandBlock=id;FirstPersonOffhandItemTexture=null;FirstPersonOffhandItemTint=Color.white; }
        public void ClearFirstPersonOffhandItem()
        { FirstPersonOffhandItemTexture=null;FirstPersonOffhandBlock=BlockId.Air;FirstPersonOffhandItemTint=Color.white; }
        public void SetFirstPersonBowCharge(float charge)
        { FirstPersonBowCharge=Mathf.Clamp01(charge); }
        float lastSpaceAt = -10f;
        float lastForwardAt = -10f;
        bool sprintLatch;
        readonly MainSurvivalInventory survivalInventory=new MainSurvivalInventory();
        bool miningActive;
        bool miningOnShip;
        int miningShipHandle;
        Vector3Int miningBlock;
        BlockId miningId;
        float miningProgress;
        float mobAttackCooldown;
        float survivalDrownTimer, survivalLavaTimer, survivalVoidTimer, survivalBurnTimer, survivalContactTimer, survivalMagmaTimer;
        float survivalRegenTimer, survivalStarveTimer, survivalFallDistance;
        float lastSurvivalY;
        bool haveLastSurvivalY;
        float nextFoodUseAt;
        Vector3 respawnPoint;
        bool hasRespawnPoint;
        Vector3 deathPosition;
        bool hasDeathPosition;

        public int AddInventoryBlock(BlockId id,int count)
        {
            if(IsCreative)return 0;
            int left=survivalInventory.AddBlock(id,count);if(left<count)VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();return left;
        }
        public int AddInventoryMobItem(MobItemId id,int count,int dur=-1)
        {
            if(IsCreative)return 0;
            int left=survivalInventory.AddItem(MainInventoryCatalog.MobKey(id),count,dur);if(left<count)VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();return left;
        }
        public int AddInventoryItem(string itemKey,int count,int dur=-1)
        {
            if(IsCreative)return 0;
            int left=survivalInventory.AddItem(itemKey,count,dur);if(left<count)VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();return left;
        }
        public bool ConsumeSelectedInventory(int count=1)
        {
            if(IsCreative)return true;bool ok=survivalInventory.ConsumeSelected(count);if(ok){VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();}return ok;
        }
        public bool DamageSelectedInventoryItem(int amount=1)
        {
            if(IsCreative)return true;bool broke;bool ok=survivalInventory.DamageSelected(amount,out broke);if(ok){VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();}return ok;
        }
        int ReplaceSelectedInventoryItem(string key)
        {
            if(IsCreative)return 0;int left=survivalInventory.ReplaceSelectedWithItem(key);VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();return left;
        }
        public MainInventoryStackData[] CapturePersistentInventory()=>survivalInventory.Capture();
        public MainSurvivalSaveData CapturePersistentSurvival()=>new MainSurvivalSaveData{hp=Health,hunger=Hunger,air=Air,dead=Dead};
        public void RestorePersistentSurvival(MainSurvivalSaveData data)
        {
            if(data==null||IsCreative)return;Health=Mathf.Clamp(data.hp,0f,20f);Hunger=Mathf.Clamp(data.hunger,0,20);Air=Mathf.Clamp(data.air,0f,10f);Dead=data.dead||Health<=0f;Exhaustion=0f;ResetSurvivalRuntimeTimers();
        }
        public void RestorePersistentInventory(MainInventoryStackData[] data,int hotbarSel)
        {
            survivalInventory.Restore(data,hotbarSel);SyncSurvivalHeldVisual();
        }
        public void SetGameplayUiOpen(bool open)
        {
            GameplayUiOpen=open;if(open){Velocity=Vector3.zero;ResetMining();}
        }
        public void NotifyInventoryChanged(){SyncSurvivalHeldVisual();VoxelWorld.Instance?.MarkPersistentSaveDirty();}
        public bool TryGetSelectedTool(out MainToolDef tool)
        {
            tool=default(MainToolDef);if(IsCreative)return false;MainInventoryStackData s=survivalInventory.Selected;
            return s!=null&&!s.block&&MainInventoryCatalog.TryGetTool(s.itemKey,out tool);
        }
        public string SelectedInventoryItemKey
        {
            get{MainInventoryStackData s=survivalInventory.Selected;return s!=null&&!s.block?s.itemKey:null;}
        }
        void SyncSurvivalHeldVisual()
        {
            MainInventoryStackData s=survivalInventory.Selected;
            if(s==null||s.block){FirstPersonPrimaryItemTexture=null;FirstPersonPrimaryItemTint=Color.white;FirstPersonBowCharge=0f;return;}
            if(MainInventoryCatalog.TryVisibleItemKey(s,out string key)){FirstPersonPrimaryItemTexture=key;FirstPersonPrimaryItemTint=Color.white;}
            else FirstPersonPrimaryItemTexture=null;
        }

        public void ConfigureMode(MainGameMode mode)
        {
            Mode=mode;
            if(mode==MainGameMode.Survival)Flying=false;
            Health=20f;Hunger=20;Air=10f;Exhaustion=0f;Dead=false;ResetSurvivalRuntimeTimers();SyncSurvivalHeldVisual();
        }

        public void RestorePersistentPose(float yaw,float pitch,bool flying)
        {
            mainYaw=yaw;mainPitch=Mathf.Clamp(pitch,-1.55f,1.55f);
            Flying=Mode==MainGameMode.Creative&&flying;
            Velocity=Vector3.zero;OnGround=false;VehicleRiding=false;
            ApplyMainView();
        }

        public void SetRespawnPoint(Vector3 feetPosition,bool markDirty=true)
        {
            respawnPoint=feetPosition;hasRespawnPoint=true;
            if(markDirty)VoxelWorld.Instance?.MarkPersistentSaveDirty();
        }

        public void RestorePersistentSpawnSource(float[] sourceSpawn)
        {
            if(sourceSpawn!=null&&sourceSpawn.Length==3)
                SetRespawnPoint(new Vector3(sourceSpawn[0],sourceSpawn[1],SourceCoords.SourceWorldZToUnity(sourceSpawn[2])),false);
            else SetRespawnPoint(transform.position,false); // main: p.spawnPos || (p.spawnPos = Q.pos.slice())
        }

        public float[] CapturePersistentSpawnSource()
        {
            Vector3 p=hasRespawnPoint?respawnPoint:transform.position;
            return new[]{p.x,p.y,SourceCoords.UnityWorldZToSource(p.z)};
        }

        public void RespawnAtSavedPoint()
        {
            RespawnAt(hasRespawnPoint?respawnPoint:transform.position);
            VoxelWorld.Instance?.MarkPersistentSaveDirty();
        }

        // main.js kp()/jr() hooks. The inventory/armor UI can drive these without changing mob AI.
        public void SetEndermanPumpkinVision(bool enabled){EndermanPumpkinVision=enabled;}
        public void SetSourceArmorPoints(int points){SourceArmorPoints=Mathf.Max(0,points);}

        void ApplySurvivalDamage(float amount,bool armor)
        {
            if(IsCreative||Dead||amount<=0f)return;
            if(armor&&SourceArmorPoints>0)
            {
                float reduced=amount*(1f-Mathf.Min(.8f,SourceArmorPoints*.04f));
                amount=Mathf.Max(1f,SourceMath.JsRound(reduced));
            }
            Health=Mathf.Max(0f,Health-amount);VoxelWorld.Instance?.MarkPersistentSaveDirty();
            if(Health<=0f){Dead=true;deathPosition=transform.position;hasDeathPosition=true;Velocity=Vector3.zero;Flying=false;ResetMining();}
        }

        // main.js jr(amount,true): mob/projectile/explosion/contact damage with armour.
        public void TakeMobDamage(float amount){ApplySurvivalDamage(amount,true);}

        // main.js jr(amount) without the armor flag (environment / Ender Pearl).
        public void TakeDirectDamage(float amount){ApplySurvivalDamage(amount,false);}

        void Heal(float amount)
        {
            if(IsCreative||Dead||amount<=0f||Health>=20f)return;Health=Mathf.Min(20f,Health+amount);VoxelWorld.Instance?.MarkPersistentSaveDirty();
        }

        bool AddHunger(int amount)
        {
            if(amount<=0||Hunger>=20)return false;Hunger=Mathf.Min(20,Hunger+amount);VoxelWorld.Instance?.MarkPersistentSaveDirty();return true;
        }

        void AddExhaustion(float amount)
        {
            if(IsCreative||Dead||amount<=0f)return;Exhaustion+=amount;
            while(Exhaustion>=4f)
            {
                Exhaustion-=4f;if(Hunger>0){Hunger--;VoxelWorld.Instance?.MarkPersistentSaveDirty();}
            }
        }

        // Source Ke() occupancy bridge used by Ender Pearl destination search.
        public bool CanOccupyFeet(Vector3 feet,float candidateHeight)
        {
            var world=VoxelWorld.Instance;return world!=null&&!SourcePointCollision.OverlapsBody(world,feet,Radius,candidateHeight);
        }
        public void ApplyPearlTeleport(Vector3 feet,float candidateHeight)
        {
            transform.position=feet;height=candidateHeight;OnGround=false;
        }
        public void SetVehicleRiding(bool riding)
        {
            VehicleRiding=riding;if(riding){Flying=false;Velocity=Vector3.zero;InWater=false;HeadInWater=false;InLava=false;OnGround=true;}
        }
        public void ApplyVehicleRidePose(Vector3 feet)
        {
            transform.position=feet;Velocity=Vector3.zero;Flying=false;InWater=false;HeadInWater=false;InLava=false;OnGround=true;Sprinting=false;Sneaking=false;Swimming=false;height=StandingHeight;
        }
        public void ApplyVehicleDismount(Vector3 feet,float candidateHeight)
        {
            transform.position=feet;height=candidateHeight;Velocity=Vector3.zero;OnGround=true;InWater=false;HeadInWater=false;InLava=false;VehicleRiding=false;
        }
        public void AddVehicleYaw(float deltaRadians){mainYaw+=deltaRadians;ApplyMainView();}
        public void SetVehicleYaw(float radians){mainYaw=radians;ApplyMainView();}
        public void ClampVehicleYaw(float center,float halfRange)
        {
            float d=Mathf.Repeat(mainYaw-center+Mathf.PI,Mathf.PI*2f)-Mathf.PI;if(d>halfRange)mainYaw=center+halfRange;else if(d<-halfRange)mainYaw=center-halfRange;ApplyMainView();
        }
        public static float SourceStandingHeight=>StandingHeight;
        public static float SourceSwimHeight=>SwimHeight;

        public void RespawnAt(Vector3 feetPosition)
        {
            // main b7(): W6() is invoked at respawn, not at the instant of death. KeepInventory
            // therefore preserves stacks exactly; disabling it scatters the current 36-slot subset.
            if(!IsCreative&&Dead&&!MainModSettings.KeepInventoryEnabled)DropSurvivalInventoryAtDeath();
            transform.position=feetPosition;Velocity=Vector3.zero;Health=20f;Hunger=20;Air=10f;Exhaustion=0f;Dead=false;Flying=false;VehicleRiding=false;hasDeathPosition=false;ResetSurvivalRuntimeTimers();ResetMining();
        }

        void DropSurvivalInventoryAtDeath()
        {
            Vector3 p=hasDeathPosition?deathPosition:transform.position;MainInventoryStackData[] stacks=survivalInventory.ClearAndCapture();
            for(int i=0;i<stacks.Length;i++)
            {
                MainInventoryStackData s=stacks[i];if(s==null||s.count<=0)continue;
                if(s.block)MainTransientRenderer.SpawnDroppedBlock(p.x,p.y+.6f,p.z,(BlockId)s.blockId,s.count);
                else MainTransientRenderer.SpawnDroppedItemKey(p.x,p.y+.6f,p.z,s.itemKey,s.count,s.dur);
            }
            SyncSurvivalHeldVisual();VoxelWorld.Instance?.MarkPersistentSaveDirty();
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            ApplyMainView();
            if (Cam != null) UpdateCameraPose(0f);
        }

        void Update()
        {
            if(GameplayUiOpen){Velocity=Vector3.zero;ResetMining();if(Cam!=null)UpdateCameraPose(Time.deltaTime);return;}
            HandleCursorAndLook();
            if(mobAttackCooldown>0f)mobAttackCooldown=Mathf.Max(0f,mobAttackCooldown-Time.deltaTime);
            HandleModeToggles();

            var world = VoxelWorld.Instance;
            if (world == null || Cam == null) return;

            if(Dead){Velocity=Vector3.zero;ResetMining();UpdateCameraPose(Time.deltaTime);return;}

            // main waits for world data around the spawn. Freeze rather than falling through an unloaded chunk.
            if (!world.IsChunkLoadedAt(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.z)))
            {
                Velocity = Vector3.zero;
                UpdateCameraPose(Time.deltaTime);
                return;
            }

            if(!VehicleRiding)
            {
                float remaining = Mathf.Min(Time.deltaTime, .1f);
                while (remaining > 0f)
                {
                    float dt = Mathf.Min(.025f, remaining);
                    TickMovement(world, dt);
                    remaining -= dt;
                }
            }
            else
            {
                Velocity=Vector3.zero;Flying=false;InWater=false;HeadInWater=false;InLava=false;OnGround=true;ResetMining();
            }

            if(!IsCreative)TickSurvival(world,Mathf.Min(Time.deltaTime,.1f));
            HandleHotbarAndActions(world);
            UpdateCameraPose(Time.deltaTime);
        }

        void HandleCursorAndLook()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
            if (Cursor.lockState != CursorLockMode.Locked || Cam == null) return;

            // main.js: Q.yaw += movementX * k; Q.pitch -= movementY * k.
            // Unity legacy Mouse X is positive to the right, Mouse Y positive upward, while browser
            // movementY is positive downward. Therefore both Unity axes ADD to main-space yaw/pitch.
            float k = Sensitivity * 10f * Mathf.Deg2Rad;
            mainYaw += Input.GetAxisRaw("Mouse X") * k;
            mainPitch += Input.GetAxisRaw("Mouse Y") * k;
            mainPitch = Mathf.Clamp(mainPitch, -1.55f, 1.55f);
            ApplyMainView();
        }

        void ApplyMainView()
        {
            // Source world Z is reflected once by SourceCoords, so the source yaw now maps directly to Unity yaw.
            // This preserves both source forward and source screen-right instead of fixing one by inverting the other.
            float unityYaw = mainYaw * Mathf.Rad2Deg;
            float unityPitch = -mainPitch * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, unityYaw, 0f);
            if (Cam != null) Cam.transform.localRotation = Quaternion.Euler(unityPitch, 0f, 0f);
        }

        void HandleModeToggles()
        {
            float now = Time.unscaledTime;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                // main.js never restores/saves flight while p.mode == "survival". Only creative
                // accepts the double-Space flight toggle.
                if (Mode==MainGameMode.Creative && now - lastSpaceAt < DoubleTapSeconds)
                    ToggleCreativeFlight();
                lastSpaceAt = now;
            }
            // User-requested desktop shortcut. main.js keeps double-Space; F is an additive Unity
            // convenience and deliberately obeys the same creative-only rule/state reset.
            if(Mode==MainGameMode.Creative&&Input.GetKeyDown(KeyCode.F))ToggleCreativeFlight();
            if(Mode==MainGameMode.Survival&&Flying)Flying=false;
            if(Input.GetKeyDown(KeyCode.F5))CameraMode=(CameraMode+1)%3;

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (now - lastForwardAt < DoubleTapSeconds) sprintLatch = true;
                lastForwardAt = now;
            }
            if (Input.GetKeyUp(KeyCode.W) || Input.GetKeyUp(KeyCode.UpArrow)) sprintLatch = false;
        }

        void ToggleCreativeFlight()
        {
            Flying=!Flying;
            Velocity=new Vector3(Velocity.x,0f,Velocity.z);
            OnGround=false;
        }

        void TickMovement(VoxelWorld world, float dt)
        {
            // z8() restores normal standing height whenever the current space allows it.
            if (height < StandingHeight && !Collides(world, transform.position, StandingHeight)) height = StandingHeight;

            bool wasGrounded = OnGround;
            float ix = Input.GetAxisRaw("Horizontal");
            float iz = Input.GetAxisRaw("Vertical");
            Vector2 input = new Vector2(ix, iz);
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool moving = input.sqrMagnitude > .0001f;
            bool sprintHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool sprint = moving && (sprintHeld || sprintLatch);
            bool downHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool sneak = downHeld && wasGrounded && !Flying;
            if (sneak) sprint = false;

            Sprinting = sprint;
            Sneaking = sneak;

            float speed = Flying ? (sprint ? FlySprintSpeed : FlySpeed) : (sprint ? SprintSpeed : WalkSpeed);
            if (sneak) speed = SneakSpeed;

            BlockId ground = wasGrounded && !Flying
                ? world.GetBlock(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y - .01f), Mathf.FloorToInt(transform.position.z))
                : BlockId.Air;

            float groundAccel = 12f;
            if (ground == BlockId.Ice || ground == BlockId.PackedIce) groundAccel = 2.2f;
            else if (ground == BlockId.BlueIce) groundAccel = 1.3f;

            // main.js exact horizontal movement:
            // O = -u*sin(yaw) + i*cos(yaw); G = u*cos(yaw) + i*sin(yaw)
            // Unity input.y is +1 for W, while main's u is -1 for W.
            float sinYaw = Mathf.Sin(mainYaw), cosYaw = Mathf.Cos(mainYaw);
            // Compute in main.js coordinates, then apply the single source->Unity Z reflection.
            float sourceMoveX = input.y * sinYaw + input.x * cosYaw;
            float sourceMoveZ = -input.y * cosYaw + input.x * sinYaw;
            Vector3 wish = new Vector3(sourceMoveX, 0f, -sourceMoveZ);
            float accel = Flying ? 12f : (wasGrounded ? groundAccel : 5f);
            float blend = Mathf.Min(1f, dt * accel);
            Velocity = new Vector3(
                Velocity.x + (wish.x * speed - Velocity.x) * blend,
                Velocity.y,
                Velocity.z + (wish.z * speed - Velocity.z) * blend);

            int px = Mathf.FloorToInt(transform.position.x);
            int pz = Mathf.FloorToInt(transform.position.z);
            BlockId head = world.GetBlock(px, Mathf.FloorToInt(transform.position.y + height - .4f), pz);
            BlockId mid = world.GetBlock(px, Mathf.FloorToInt(transform.position.y + .5f), pz);
            BlockId feet = world.GetBlock(px, Mathf.FloorToInt(transform.position.y), pz);

            HeadInWater = Swimmable(head);
            InWater = HeadInWater || Swimmable(mid);
            InLava = BlockRegistry.IsLava(head) || BlockRegistry.IsLava(mid) || BlockRegistry.IsLava(feet);

            bool jump = Input.GetKey(KeyCode.Space);
            Swimming=false;

            if (Flying)
            {
                float verticalTarget = 0f;
                if (jump) verticalTarget += FlyVerticalSpeed;
                if (downHeld) verticalTarget -= FlyVerticalSpeed;
                Velocity = new Vector3(
                    Velocity.x,
                    Velocity.y + (verticalTarget - Velocity.y) * Mathf.Min(1f, dt * 10f),
                    Velocity.z);
            }
            else if (InWater)
            {
                bool swimming = sprint && moving && HeadInWater;
                Swimming=swimming;
                if (swimming)
                {
                    if (height > SwimHeight && !Collides(world, transform.position, SwimHeight)) height = SwimHeight;
                    float cp = Mathf.Cos(mainPitch);
                    float sp = Mathf.Sin(mainPitch);
                    // main.js keeps strafe horizontal while pitch only tilts the forward component.
                    float sourceSwimX = input.y * sinYaw * cp + input.x * cosYaw;
                    float sourceSwimZ = -input.y * cosYaw * cp + input.x * sinYaw;
                    Vector3 swimWish = new Vector3(sourceSwimX, input.y * sp, -sourceSwimZ) * speed;
                    float a = Mathf.Min(1f, dt * 5f);
                    Velocity = Vector3.Lerp(Velocity, swimWish, a);
                    if (jump) Velocity += Vector3.up * (10f * dt);
                    if (downHeld) Velocity += Vector3.down * (14f * dt);
                    Velocity = new Vector3(Velocity.x, Mathf.Clamp(Velocity.y, -speed, speed), Velocity.z);
                }
                else
                {
                    Velocity += Vector3.down * (5.5f * dt);
                    if (jump) Velocity += Vector3.up * (16f * dt);
                    if (downHeld) Velocity += Vector3.down * (22f * dt);
                    float minY = downHeld ? -5f : -3.2f;
                    Velocity = new Vector3(Velocity.x, Mathf.Clamp(Velocity.y, minY, 3.4f), Velocity.z);
                    float drag = Mathf.Min(1f, dt * (sprint ? .8f : 2f));
                    Velocity = new Vector3(Velocity.x * (1f - drag), Velocity.y, Velocity.z * (1f - drag));
                }
            }
            else
            {
                Velocity += Vector3.down * (Gravity * dt);
                if (Velocity.y < -MaxFall) Velocity = new Vector3(Velocity.x, -MaxFall, Velocity.z);
                if (jump && wasGrounded)
                {
                    Velocity = new Vector3(Velocity.x, JumpSpeed, Velocity.z);
                    OnGround = false;
                }
            }

            OnGround = false;
            Vector3 delta = Velocity * dt;
            if (sneak)
            {
                Vector2 safe = ClampSneakDelta(world, delta.x, delta.z);
                if (Mathf.Abs(safe.x) < 1e-6f) Velocity = new Vector3(0f, Velocity.y, Velocity.z);
                if (Mathf.Abs(safe.y) < 1e-6f) Velocity = new Vector3(Velocity.x, Velocity.y, 0f);
                delta.x = safe.x;
                delta.z = safe.y;
            }

            MoveAxis(world, 0, delta.x);
            MoveAxis(world, 2, delta.z);
            MoveAxis(world, 1, delta.y);

            // main z8(): landing while flying returns creative player to normal ground movement.
            if (Flying && OnGround) Flying = false;

            if (sneak && height > SneakHeight && !Collides(world, transform.position, SneakHeight)) height = SneakHeight;
        }

        Vector2 ClampSneakDelta(VoxelWorld world, float dx, float dz)
        {
            Vector3 p = transform.position;
            const float step = .05f;
            while (Mathf.Abs(dx) > 1e-6f && !Collides(world, new Vector3(p.x + dx, p.y - StepHeight, p.z), height))
                dx = Mathf.Abs(dx) <= step ? 0f : dx - Mathf.Sign(dx) * step;
            while (Mathf.Abs(dz) > 1e-6f && !Collides(world, new Vector3(p.x, p.y - StepHeight, p.z + dz), height))
                dz = Mathf.Abs(dz) <= step ? 0f : dz - Mathf.Sign(dz) * step;
            while (Mathf.Abs(dx) > 1e-6f && Mathf.Abs(dz) > 1e-6f && !Collides(world, new Vector3(p.x + dx, p.y - StepHeight, p.z + dz), height))
            {
                dx = Mathf.Abs(dx) <= step ? 0f : dx - Mathf.Sign(dx) * step;
                dz = Mathf.Abs(dz) <= step ? 0f : dz - Mathf.Sign(dz) * step;
            }
            return new Vector2(dx, dz);
        }

        void MoveAxis(VoxelWorld world, int axis, float amount)
        {
            if (Mathf.Abs(amount) < 1e-7f) return;
            Vector3 old = transform.position;
            Vector3 p = old;
            p[axis] += amount;
            if (!Collides(world, p, height))
            {
                transform.position = p;
                return;
            }

            // main S4(): 0.6 step-up exists only while not flying.
            if (!Flying && axis != 1 && Velocity.y <= .08f)
            {
                Vector3 stepped = p;
                stepped.y += StepHeight;
                if (!Collides(world, stepped, height))
                {
                    float y = stepped.y;
                    while (y - .05f > old.y && !Collides(world, new Vector3(stepped.x, y - .05f, stepped.z), height)) y -= .05f;
                    stepped.y = y;
                    transform.position = stepped;
                    OnGround = true;
                    return;
                }
            }

            // Resolve to the closest non-colliding point along the attempted axis.
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float m = (lo + hi) * .5f;
                Vector3 q = old;
                q[axis] += amount * m;
                if (Collides(world, q, height)) hi = m;
                else lo = m;
            }
            Vector3 resolved = old;
            resolved[axis] += amount * lo;
            transform.position = resolved;

            if (axis == 0) Velocity = new Vector3(0f, Velocity.y, Velocity.z);
            else if (axis == 2) Velocity = new Vector3(Velocity.x, Velocity.y, 0f);
            else
            {
                if (amount < 0f) OnGround = true;
                Velocity = new Vector3(Velocity.x, 0f, Velocity.z);
            }
        }

        static bool Swimmable(BlockId id) => BlockRegistry.IsWater(id) || BlockRegistry.IsLava(id) || BlockRegistry.IsAquatic(id);

        bool Collides(VoxelWorld world, Vector3 feet, float h)
        {
            return SourcePointCollision.OverlapsBody(world,feet,Radius,h);
        }

        bool IntersectsPlayer(Vector3Int block)
        {
            Vector3 p = transform.position;
            return block.x < p.x + Radius && block.x + 1 > p.x - Radius &&
                   block.y < p.y + height && block.y + 1 > p.y &&
                   block.z < p.z + Radius && block.z + 1 > p.z - Radius;
        }

        void UpdateCameraPose(float dt)
        {
            if (Cam == null) return;
            float horizontal = new Vector2(Velocity.x, Velocity.z).magnitude;
            if (horizontal > .5f && (OnGround || InWater)) bobPhase += dt * horizontal * 1.7f;
            float bobX = Mathf.Cos(bobPhase) * .04f;
            float bobY = -Mathf.Abs(Mathf.Sin(bobPhase)) * .04f;
            if (swingPhase > 0f) swingPhase = Mathf.Max(0f, swingPhase - dt * 4.5f); // main U3(): xo -= dt*4.5
            FirstPersonBobX = bobX;
            FirstPersonBobY = bobY;
            FirstPersonSwing = Mathf.Sin(swingPhase * Mathf.PI);
            // main oe(): camera eye itself does not bob; R2() bob is only applied to the first-person held pass.
            Vector3 eye=AimOrigin,dir=AimDirection;
            if(CameraMode==0)
            {
                Cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(dir,Vector3.up));
            }
            else
            {
                var world=VoxelWorld.Instance;float sign=CameraMode==2?1f:-1f;float dist=ThirdPersonDistance(world,eye,dir*sign);
                Vector3 pos=eye+dir*(sign*dist);Vector3 look=sign>0f?-dir:dir;
                Cam.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(look,Vector3.up));
            }

            float targetFov = Sprinting ? 78f : 70f;
            fov += (targetFov - fov) * Mathf.Min(1f, dt * 9f);
            Cam.fieldOfView = fov;
        }

        float ThirdPersonDistance(VoxelWorld world,Vector3 eye,Vector3 direction)
        {
            // main og(): nI=4, El=.12, ag=.22. Stop before the first solid voxel.
            if(world==null)return 4f;
            const float step=.12f,max=4f,margin=.22f;
            for(float d=step;d<=max+.0001f;d+=step)
            {
                Vector3 p=eye+direction*d;
                if(BlockRegistry.IsSolid(world.GetBlock(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z))))
                    return Mathf.Max(0f,d-step-margin);
            }
            return max;
        }

        void ResetSurvivalRuntimeTimers()
        {
            survivalDrownTimer=survivalLavaTimer=survivalVoidTimer=survivalBurnTimer=survivalContactTimer=survivalMagmaTimer=0f;
            survivalRegenTimer=survivalStarveTimer=survivalFallDistance=0f;haveLastSurvivalY=false;nextFoodUseAt=0f;
        }

        void TickSurvival(VoxelWorld world,float dt)
        {
            if(Dead||dt<=0f)return;float y=transform.position.y;
            if(Flying||InWater||VehicleRiding)survivalFallDistance=0f;
            else if(OnGround)
            {
                if(survivalFallDistance>3.2f)ApplySurvivalDamage(Mathf.Floor(survivalFallDistance-3f),false);survivalFallDistance=0f;
            }
            else if(haveLastSurvivalY&&y<lastSurvivalY)survivalFallDistance+=lastSurvivalY-y;
            lastSurvivalY=y;haveLastSurvivalY=true;

            if(HeadInWater)
            {
                Air-=dt;if(Air<=0f){Air=0f;survivalDrownTimer+=dt;while(survivalDrownTimer>=1f){survivalDrownTimer-=1f;ApplySurvivalDamage(2f,false);}}
            }
            else{if(Air<10f)Air=Mathf.Min(10f,Air+dt*4f);survivalDrownTimer=0f;}

            Vector3 eye=AimOrigin;BlockId eyeBlock=world.GetBlock(Mathf.FloorToInt(eye.x),Mathf.FloorToInt(eye.y),Mathf.FloorToInt(eye.z));
            if(BlockRegistry.IsLava(eyeBlock)){survivalLavaTimer+=dt;while(survivalLavaTimer>=.5f){survivalLavaTimer-=.5f;ApplySurvivalDamage(1f,false);}}else survivalLavaTimer=0f;
            if(InLava){survivalBurnTimer+=dt;if(survivalBurnTimer>=.5f){survivalBurnTimer=0f;ApplySurvivalDamage(4f,false);}}
            else
            {
                int fx=Mathf.FloorToInt(transform.position.x),fy=Mathf.FloorToInt(transform.position.y),fz=Mathf.FloorToInt(transform.position.z);
                bool fire=world.GetBlock(fx,fy,fz)==BlockId.Fire||world.GetBlock(fx,fy+1,fz)==BlockId.Fire;
                if(fire){survivalBurnTimer+=dt;if(survivalBurnTimer>=.5f){survivalBurnTimer=0f;ApplySurvivalDamage(1f,false);}}else survivalBurnTimer=0f;
            }

            if(OnGround&&!Sneaking)
            {
                int gx=Mathf.FloorToInt(transform.position.x),gy=Mathf.FloorToInt(transform.position.y-.05f),gz=Mathf.FloorToInt(transform.position.z);
                if(world.GetBlock(gx,gy,gz)==BlockId.MagmaBlock){survivalMagmaTimer+=dt;if(survivalMagmaTimer>=1f){survivalMagmaTimer=0f;ApplySurvivalDamage(1f,false);}}else survivalMagmaTimer=0f;
            }else survivalMagmaTimer=0f;

            if(y<VoxelConstants.MinY-2){survivalVoidTimer+=dt;while(survivalVoidTimer>=.4f){survivalVoidTimer-=.4f;ApplySurvivalDamage(4f,false);}}else survivalVoidTimer=0f;

            bool moving=Mathf.Sqrt(Velocity.x*Velocity.x+Velocity.z*Velocity.z)>.5f;
            if(BodyTouches(world,BlockId.Cactus)||(moving&&BodyTouches(world,BlockId.SweetBerryBush)))
            {
                survivalContactTimer+=dt;if(survivalContactTimer>=.5f){survivalContactTimer=0f;ApplySurvivalDamage(1f,true);}
            }else survivalContactTimer=0f;
            AddExhaustion(dt*(Sprinting&&moving ? .15f : moving ? .015f : .004f));

            if(Hunger>=18&&Health<20f)
            {
                survivalRegenTimer+=dt;if(survivalRegenTimer>=4f){survivalRegenTimer-=4f;Heal(1f);AddExhaustion(3f);}
            }else survivalRegenTimer=0f;
            if(Hunger<=0)
            {
                survivalStarveTimer+=dt;if(survivalStarveTimer>=4f){survivalStarveTimer-=4f;if(Health>1f)ApplySurvivalDamage(1f,false);}
            }else survivalStarveTimer=0f;
        }

        bool BodyTouches(VoxelWorld world,BlockId target)
        {
            Vector3 p=transform.position;int x0=Mathf.FloorToInt(p.x-Radius),x1=Mathf.FloorToInt(p.x+Radius),y0=Mathf.FloorToInt(p.y),y1=Mathf.FloorToInt(p.y+height-.001f),z0=Mathf.FloorToInt(p.z-Radius),z1=Mathf.FloorToInt(p.z+Radius);
            for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)if(world.GetBlock(x,y,z)==target)return true;return false;
        }

        void HandleHotbarAndActions(VoxelWorld world)
        {
            {
                // main: one 36-slot inventory for both modes; creative fills it from the E screen.
                if(Input.GetKeyDown(KeyCode.Alpha1))survivalInventory.SelectHotbar(0);
                if(Input.GetKeyDown(KeyCode.Alpha2))survivalInventory.SelectHotbar(1);
                if(Input.GetKeyDown(KeyCode.Alpha3))survivalInventory.SelectHotbar(2);
                if(Input.GetKeyDown(KeyCode.Alpha4))survivalInventory.SelectHotbar(3);
                if(Input.GetKeyDown(KeyCode.Alpha5))survivalInventory.SelectHotbar(4);
                if(Input.GetKeyDown(KeyCode.Alpha6))survivalInventory.SelectHotbar(5);
                if(Input.GetKeyDown(KeyCode.Alpha7))survivalInventory.SelectHotbar(6);
                if(Input.GetKeyDown(KeyCode.Alpha8))survivalInventory.SelectHotbar(7);
                if(Input.GetKeyDown(KeyCode.Alpha9))survivalInventory.SelectHotbar(8);
                float wheel=Input.mouseScrollDelta.y;if(wheel>0f)survivalInventory.StepHotbar(-1);else if(wheel<0f)survivalInventory.StepHotbar(1);
                SyncSurvivalHeldVisual();
            }
            MainSourceObjectRenderer.SetFishingRodHeld(MainItemVisualCatalog.IsFishingRod(FirstPersonPrimaryItemTexture));

            VoxelHit hit;
            // main.js JP(): vehicle hit test precedes mob/block breaking on a fresh left click.
            if(Input.GetMouseButtonDown(0)&&MainSourceObjectRenderer.TryBreakVehicle(AimOrigin,AimDirection))
            {
                if(swingPhase<=.05f)swingPhase=1f;ResetMining();return;
            }
            // main.js Yf()/FE(): mobs have priority over block breaking inside four blocks; bare hand = 1, creative = 5.
            if(Input.GetMouseButton(0)&&mobAttackCooldown<=0f)
            {
                MobAI mob;float mobDist;
                if(MobAI.Raycast(AimOrigin,AimDirection,4f,out mob,out mobDist))
                {
                    float attack=IsCreative?5f:1f;
                    if(!IsCreative&&TryGetSelectedTool(out MainToolDef atkTool))attack=atkTool.Damage;
                    mobAttackCooldown=.26f;if(swingPhase<=.05f)swingPhase=1f;ResetMining();
                    mob.Damage(attack,transform.position,true);
                    if(!IsCreative&&TryGetSelectedTool(out MainToolDef usedTool))
                    {
                        int wear=usedTool.Kind==MainToolKind.Sword?1:(usedTool.Kind==MainToolKind.Shears||usedTool.Kind==MainToolKind.Bow?0:2);
                        if(wear>0)DamageSelectedInventoryItem(wear);
                    }
                    return;
                }
            }

            if(IsCreative)
            {
                ResetMining();
                if(Input.GetMouseButtonDown(0))
                {
                    bool wh=VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out hit);float wd=wh?Vector3.Distance(AimOrigin,hit.Point):6.0001f;
                    MainShipRuntime.ShipVoxelHit sh;bool hs=MainShipRuntime.TryRaycastDocked(AimOrigin,AimDirection,Mathf.Min(6f,wd),out sh);
                    if(hs&&sh.Id!=BlockId.Bedrock)
                    {
                        if(swingPhase<=.05f)swingPhase=1f;
                        if(MainShipRuntime.TryBreakDockedBlock(sh,out BlockId broken,out Vector3 center))MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(center.x),Mathf.FloorToInt(center.y),Mathf.FloorToInt(center.z),broken,10);
                    }
                    else if(wh&&hit.Id!=BlockId.Bedrock)
                    {
                        if(swingPhase<=.05f)swingPhase=1f;MainTransientRenderer.SpawnBreakParticles(hit.Block.x,hit.Block.y,hit.Block.z,hit.Id,10);
                        // main gT(): other door/bed half goes too; aquatic plants leave water (Qo).
                        BlockId after=MainBlockPlacement.BreakCompanions(world,hit.Block,hit.Id,false);
                        world.SetBlock(hit.Block.x,hit.Block.y,hit.Block.z,after,0,true);
                    }
                }
            }
            else
            {
                HandleSurvivalMining(world);
            }

            if (Input.GetMouseButtonDown(1))
            {
                // main kT()/EE(): a docked moving ship is ray-tested before the ordinary world target.
                bool hasUseHit=VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out hit);
                float worldHitDistance=hasUseHit?Vector3.Distance(AimOrigin,hit.Point):6f;
                if(MainShipRuntime.TryUseMovingHelm(AimOrigin,AimDirection,worldHitDistance)){if(swingPhase<=.05f)swingPhase=1f;return;}
                string shipHeld=SelectedInventoryItemKey;if(string.IsNullOrEmpty(shipHeld))shipHeld=FirstPersonPrimaryItemTexture;
                if(shipHeld=="item_bucket"&&MainShipRuntime.TryFillBucketFromDockedShip(AimOrigin,AimDirection,Mathf.Min(6f,worldHitDistance),out string shipFilled))
                {if(!IsCreative){int left=ReplaceSelectedInventoryItem(shipFilled);if(left>0)MainTransientRenderer.SpawnDroppedItemKey(transform.position.x,transform.position.y+.6f,transform.position.z,shipFilled,left);}if(swingPhase<=.05f)swingPhase=1f;return;}
                MainShipRuntime.ShipVoxelHit shipUseHit;
                if(MainShipRuntime.TryRaycastDocked(AimOrigin,AimDirection,Mathf.Min(6f,worldHitDistance),out shipUseHit))
                {
                    if(MainShipRuntime.TryInteractDockedBlock(shipUseHit)){if(swingPhase<=.05f)swingPhase=1f;return;}
                    if((shipHeld=="item_water_bucket"||shipHeld=="item_lava_bucket")&&MainShipRuntime.TryPlaceBucketFluidOnDockedShip(shipUseHit,shipHeld,out Vector3 fluidCenter))
                    {if(!IsCreative){int left=ReplaceSelectedInventoryItem("item_bucket");if(left>0)MainTransientRenderer.SpawnDroppedItemKey(transform.position.x,transform.position.y+.6f,transform.position.z,"item_bucket",left);}if(swingPhase<=.05f)swingPhase=1f;return;}
                    if((shipHeld=="flint_and_steel"||shipHeld=="item_flint_and_steel")&&MainShipRuntime.TryUseFlintAndSteelOnDockedShip(shipUseHit))
                    {if(!IsCreative)DamageSelectedInventoryItem(1);if(swingPhase<=.05f)swingPhase=1f;return;}
                    BlockId shipPlace=SelectedBlock;
                    if(shipPlace!=BlockId.Air&&MainShipRuntime.TryPlaceDockedBlock(shipUseHit,shipPlace,0,out Vector3 shipCenter))
                    {if(swingPhase<=.05f)swingPhase=1f;if(!IsCreative)ConsumeSelectedInventory(1);return;}
                }
                // main DP(): rideable vehicle takes the use action before ordinary item placement.
                if(MainSourceObjectRenderer.TryRideVehicle(AimOrigin,AimDirection)){if(swingPhase<=.05f)swingPhase=1f;return;}
                MobAI interactMob;float interactDist;
                if(MobAI.Raycast(AimOrigin,AimDirection,4f,out interactMob,out interactDist)&&interactMob.TryInteract(this))
                {
                    if(swingPhase<=.05f)swingPhase=1f;return;
                }
                if(hasUseHit&&hit.Id==BlockId.ShipWheel&&MainShipRuntime.TryTakeStaticHelm(hit.Block))
                {
                    if(swingPhase<=.05f)swingPhase=1f;return;
                }
                // main DT(): using either half of a bed sets p.spawnPos immediately, even when it
                // is not sleeping time. Full sleep UI/time-skip is a separate gameplay layer.
                if(hasUseHit&&SourceBlockRules.IsBed(hit.Id))
                {
                    SetRespawnPoint(new Vector3(hit.Block.x+.5f,hit.Block.y+1f,hit.Block.z+.5f));
                    if(swingPhase<=.05f)swingPhase=1f;return;
                }
                // main kT(): static crafting table / furnace / chest interaction precedes held-item use.
                if(hasUseHit&&MainWorldFunctionalBlocks.TryInteractWorld(hit))
                {
                    if(swingPhase<=.05f)swingPhase=1f;return;
                }
                // main kT(): FLOWER_POT → HL(), door/gate/trapdoor → yL() before the held item.
                if(hasUseHit)
                {
                    var use=MainBlockPlacement.UseTarget(PlacementContext(world),hit,SelectedBlock,out BlockId potPlant);
                    if(use!=MainBlockPlacement.Result.None)
                    {
                        if(use==MainBlockPlacement.Result.Consumed)ConsumeSelectedInventory(1);
                        if(potPlant!=BlockId.Air){int left=survivalInventory.AddBlock(potPlant,1);if(left>0)MainTransientRenderer.SpawnDroppedBlock(transform.position.x,transform.position.y+.6f,transform.position.z,potPlant,left);SyncSurvivalHeldVisual();}
                        if(swingPhase<=.05f)swingPhase=1f;return;
                    }
                }
                if(HandleSourceItemUse(world)){if(swingPhase<=.05f)swingPhase=1f;return;}
                if(hasUseHit)
                {
                    // main kT()/zL()/VL()/FL(): orientation metadata, support rules, slab merging,
                    // two-cell doors/beds/tall plants, leaves meta 1 (never decays), chest pairing.
                    var placed=MainBlockPlacement.Place(PlacementContext(world),hit,SelectedBlock);
                    if(placed!=MainBlockPlacement.Result.None&&swingPhase<=.05f)swingPhase=1f;
                    if(placed==MainBlockPlacement.Result.Consumed&&!IsCreative)ConsumeSelectedInventory(1);
                }
            }
        }

        MainBlockPlacement.Context PlacementContext(VoxelWorld world)
            =>new MainBlockPlacement.Context{World=world,Yaw=mainYaw,Survival=!IsCreative,PlayerFeet=transform.position};

        bool HandleSourceItemUse(VoxelWorld world)
        {
            string item=SelectedInventoryItemKey;if(string.IsNullOrEmpty(item))item=FirstPersonPrimaryItemTexture;if(string.IsNullOrEmpty(item))return false;

            // main kT(): spawn egg -> persistent mob in the cell in front of the aimed face.
            int itemIndex=SourceItemData.IndexOfTex(item);
            if(itemIndex>=0&&SourceItemData.SpawnEgg[itemIndex].Length>0)
            {
                VoxelHit h;if(!VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))return true;
                Vector3Int c=h.Block+h.Normal;
                bool ok=MobSpawner.SpawnFromEgg(SourceItemData.SpawnEgg[itemIndex],new Vector3(c.x+.5f,c.y,c.z+.5f));
                if(ok&&!IsCreative)ConsumeSelectedInventory(1);
                return true;
            }
            // main ML(): bone meal on crops, saplings and grass.
            if(item=="item_bone_meal")
            {
                VoxelHit h;if(!VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))return false;
                MainBlockLifecycle life=MainBlockLifecycle.Instance;
                bool used=life!=null&&life.BoneMeal(h.Block);
                if(used){if(!IsCreative)ConsumeSelectedInventory(1);MainTransientRenderer.SpawnBreakParticles(h.Block.x,h.Block.y+1,h.Block.z,BlockId.OakLeaves,8);}
                return used;
            }
            // main kT(): seeds / carrot / potato on farmland with air above plant a crop (before eating).
            BlockId cropStage0=item=="item_wheat_seeds"?BlockId.Wheat0:item=="item_pumpkin_seeds"?BlockId.PumpkinStem0:item=="item_melon_seeds"?BlockId.MelonStem0:
                item=="item_carrot"?BlockId.Carrots0:item=="item_potato"?BlockId.Potatoes0:BlockId.Air;
            if(cropStage0!=BlockId.Air)
            {
                VoxelHit h;
                if(VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h)&&(h.Id==BlockId.Farmland||h.Id==BlockId.FarmlandMoist)&&world.GetBlock(h.Block.x,h.Block.y+1,h.Block.z)==BlockId.Air)
                {
                    MainBlockLifecycle life=MainBlockLifecycle.Instance;
                    bool ok=life!=null&&life.PlantCrop(h.Block.x,h.Block.y+1,h.Block.z,cropStage0);
                    if(ok&&!IsCreative)ConsumeSelectedInventory(1);
                    return ok;
                }
                if(item!="item_carrot"&&item!="item_potato")return false;
            }

            int food=MainInventoryCatalog.FoodPoints(item);
            if(!IsCreative&&food>0&&Time.unscaledTime>=nextFoodUseAt)
            {
                bool golden=item=="item_golden_apple";
                if(golden||Hunger<20)
                {
                    nextFoodUseAt=Time.unscaledTime+.8f;
                    if(!golden)AddHunger(food);else{AddHunger(food);Heal(8f);}
                    ConsumeSelectedInventory(1);
                    if(item=="item_mushroom_stew")
                    {
                        int left=survivalInventory.AddItem("item_bowl",1);if(left>0)MainTransientRenderer.SpawnDroppedItemKey(transform.position.x,transform.position.y+.6f,transform.position.z,"item_bowl",left);
                        VoxelWorld.Instance?.MarkPersistentSaveDirty();SyncSurvivalHeldVisual();
                    }
                    return true;
                }
            }

            MainInventoryCatalog.TryGetTool(item,out MainToolDef useTool);
            // main kT(): axe strips logs/stems (L0), keeping the axis metadata.
            if(useTool.Kind==MainToolKind.Axe)
            {
                VoxelHit h;if(VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))
                {
                    BlockId stripped=SourceBlockRules.StrippedOf(h.Id);
                    if(stripped!=BlockId.Air)
                    {
                        bool ok=world.SetBlock(h.Block.x,h.Block.y,h.Block.z,stripped,world.GetMeta(h.Block.x,h.Block.y,h.Block.z),true);
                        if(ok&&!IsCreative)DamageSelectedInventoryItem(1);
                        return ok;
                    }
                }
            }
            // main kT(): shears carve a pumpkin and drop four pumpkin seeds.
            if(useTool.Kind==MainToolKind.Shears)
            {
                VoxelHit h;if(VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h)&&h.Id==BlockId.Pumpkin)
                {
                    bool ok=world.SetBlock(h.Block.x,h.Block.y,h.Block.z,BlockId.CarvedPumpkin,0,true);
                    if(ok){MainTransientRenderer.SpawnDroppedItem(h.Block.x+.5f,h.Block.y+.5f,h.Block.z+.5f,MobItemId.PumpkinSeeds,4);if(!IsCreative)DamageSelectedInventoryItem(1);}
                    return ok;
                }
            }
            if(useTool.Kind==MainToolKind.Hoe)
            {
                VoxelHit h;if(VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))
                {
                    BlockId id=h.Id;
                    if((id==BlockId.Grass||id==BlockId.Dirt)&&world.GetBlock(h.Block.x,h.Block.y+1,h.Block.z)==BlockId.Air)
                    {
                        bool ok=world.SetBlock(h.Block.x,h.Block.y,h.Block.z,BlockId.Farmland,0,true);
                        if(ok&&!IsCreative)DamageSelectedInventoryItem(1);
                        return ok;
                    }
                }
                return false;
            }
            if(item=="item_bucket")return TryFillBucketFromWorld(world);
            if(item=="item_water_bucket"||item=="item_lava_bucket")return TryEmptyFilledBucketIntoWorld(world,item);
            if(MainItemVisualCatalog.IsFishingRod(item))return MainSourceObjectRenderer.UseFishingRod();
            if(item=="item_ender_pearl")
            {
                bool ok=MainSourceObjectRenderer.ThrowEnderPearl(Velocity,OnGround);if(ok&&!IsCreative)ConsumeSelectedInventory(1);return ok;
            }
            if(item=="item_minecart")
            {
                bool ok=MainSourceObjectRenderer.PlaceMinecartFromAim(AimOrigin,AimDirection,mainYaw);if(ok&&!IsCreative)ConsumeSelectedInventory(1);return ok;
            }
            if(item=="item_boat")
            {
                bool ok=MainSourceObjectRenderer.PlaceBoatFromAim(AimOrigin,AimDirection,mainYaw);if(ok&&!IsCreative)ConsumeSelectedInventory(1);return ok;
            }
            if(item=="flint_and_steel"||item=="item_flint_and_steel")
            {
                VoxelHit h;if(VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))
                {
                    if(h.Id==BlockId.Tnt)
                    {
                        bool primed=MainSourceObjectRenderer.PrimeTnt(h.Block.x,h.Block.y,h.Block.z,2f);if(primed&&!IsCreative)DamageSelectedInventoryItem(1);return primed;
                    }
                    if(h.Id==BlockId.Fire)return false;
                    Vector3Int b=h.Block+h.Normal;
                    if(b.y>=VoxelConstants.MinY&&b.y<=VoxelConstants.MaxY&&world.GetBlock(b.x,b.y,b.z)==BlockId.Air&&!IntersectsPlayer(b))
                    {
                        bool lit=world.SetBlock(b.x,b.y,b.z,BlockId.Fire,0,true);if(lit&&!IsCreative)DamageSelectedInventoryItem(1);return lit;
                    }
                }
            }
            return false;
        }

        bool TryFillBucketFromWorld(VoxelWorld world)
        {
            Vector3 o=AimOrigin,d=AimDirection.normalized;
            Vector3Int last=new Vector3Int(int.MinValue,int.MinValue,int.MinValue);
            for(float r=.3f;r<=6.0001f;r+=.1f)
            {
                Vector3 p=o+d*r;Vector3Int c=new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z));if(c==last)continue;last=c;
                if(c.y<VoxelConstants.MinY||c.y>VoxelConstants.MaxY)continue;BlockId id=world.GetBlock(c.x,c.y,c.z);
                if(id==BlockId.Water||id==BlockId.Lava)
                {
                    if(!world.SetBlock(c.x,c.y,c.z,BlockId.Air,0,true))return false;
                    if(!IsCreative){string full=id==BlockId.Water?"item_water_bucket":"item_lava_bucket";int left=ReplaceSelectedInventoryItem(full);if(left>0)MainTransientRenderer.SpawnDroppedItemKey(transform.position.x,transform.position.y+.6f,transform.position.z,full,left);}
                    return true;
                }
                if(id!=BlockId.Air&&!BlockRegistry.IsFluid(id)&&!BlockRegistry.IsAquatic(id)&&!IsBucketPassThrough(id))return false;
            }
            return false;
        }

        bool TryEmptyFilledBucketIntoWorld(VoxelWorld world,string item)
        {
            VoxelHit h;if(!VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out h))return false;Vector3Int b=h.Block+h.Normal;
            if(b.y<VoxelConstants.MinY||b.y>VoxelConstants.MaxY||IntersectsPlayer(b))return false;BlockId old=world.GetBlock(b.x,b.y,b.z);
            if(old!=BlockId.Air&&!BlockRegistry.IsFluid(old)&&!BlockRegistry.IsAquatic(old)&&!IsBucketPassThrough(old))return false;
            BlockId fluid=item=="item_water_bucket"?BlockId.Water:BlockId.Lava;if(!world.SetBlock(b.x,b.y,b.z,fluid,0,true))return false;
            if(!IsCreative){int left=ReplaceSelectedInventoryItem("item_bucket");if(left>0)MainTransientRenderer.SpawnDroppedItemKey(transform.position.x,transform.position.y+.6f,transform.position.z,"item_bucket",left);}
            return true;
        }

        static bool IsBucketPassThrough(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsAquatic(id))return true;
            BlockShape sh=BlockRegistry.Get(id).Shape;return sh==BlockShape.Cross||sh==BlockShape.TallPlant||id==BlockId.Vine||id==BlockId.GlowLichen||id==BlockId.Fire;
        }

        void HandleSurvivalMining(VoxelWorld world)
        {
            if(!Input.GetMouseButton(0)){ResetMining();return;}
            VoxelHit wh;bool hasWorld=VoxelRaycast.Cast(world,AimOrigin,AimDirection,6f,out wh);float worldDist=hasWorld?Vector3.Distance(AimOrigin,wh.Point):6.0001f;
            MainShipRuntime.ShipVoxelHit sh;bool hasShip=MainShipRuntime.TryRaycastDocked(AimOrigin,AimDirection,Mathf.Min(6f,worldDist),out sh);
            BlockId targetId;Vector3Int targetBlock;int shipHandle=0;
            if(hasShip){targetId=sh.Id;targetBlock=sh.Block;shipHandle=sh.Handle;}else if(hasWorld){targetId=wh.Id;targetBlock=wh.Block;}else{ResetMining();return;}

            var rule=BlockMiningRules.Get(targetId);if(rule.Hardness<0f){ResetMining();return;}
            bool onShip=hasShip;
            if(!miningActive||miningOnShip!=onShip||targetBlock!=miningBlock||targetId!=miningId||(onShip&&miningShipHandle!=shipHandle))
            {
                miningActive=true;miningOnShip=onShip;miningShipHandle=shipHandle;miningBlock=targetBlock;miningId=targetId;miningProgress=0f;
                if(swingPhase<=.05f)swingPhase=1f;
            }

            MainToolDef tool;bool hasTool=TryGetSelectedTool(out tool);bool matching=hasTool&&BlockMiningRules.Matches(tool.Kind,rule.Class);float speed=matching?tool.Speed:1f;
            string blockName=targetId.ToString();
            if(hasTool&&tool.Kind==MainToolKind.Shears)
            {if(BlockRegistry.IsClassicLeaf(targetId)||blockName.IndexOf("Leaves",System.StringComparison.OrdinalIgnoreCase)>=0)speed=15f;else if(blockName.IndexOf("Wool",System.StringComparison.OrdinalIgnoreCase)>=0)speed=5f;}
            if(targetId==BlockId.Cobweb&&hasTool&&(tool.Kind==MainToolKind.Shears||tool.Kind==MainToolKind.Sword))speed=15f;
            bool canDrop=!rule.NoDrop&&(rule.Class==BlockMiningRules.ToolClass.None||rule.Tier==0||(matching&&tool.Tier>=rule.Tier));
            if(rule.Hardness==0f)miningProgress=1f;
            else{float work=rule.Hardness*(canDrop||rule.NoDrop?1.5f:5f);float penalty=HeadInWater?5f:1f;if(HeadInWater&&!OnGround&&!Flying)penalty*=5f;miningProgress+=speed/Mathf.Max(.0001f,work*penalty)*Time.deltaTime;}
            if(!onShip)MainTransientRenderer.SetMiningCrack(miningBlock,miningProgress);else MainTransientRenderer.ClearMiningCrack();
            if(miningProgress<1f)return;

            BlockId broken=miningId;Vector3 center;
            if(onShip)
            {
                MainShipRuntime.ShipVoxelHit finalHit=sh;ResetMining();
                if(!MainShipRuntime.TryBreakDockedBlock(finalHit,out broken,out center))return;
                MainTransientRenderer.SpawnBreakParticles(Mathf.FloorToInt(center.x),Mathf.FloorToInt(center.y),Mathf.FloorToInt(center.z),broken,10);
            }
            else
            {
                Vector3Int b=miningBlock;ResetMining();center=(Vector3)b+Vector3.one*.5f;MainTransientRenderer.SpawnBreakParticles(b.x,b.y,b.z,broken,10);
                // main eJ(): other door/bed half, flower-pot plant drop, Qo() water refill.
                BlockId after=MainBlockPlacement.BreakCompanions(world,b,broken,true);
                world.SetBlock(b.x,b.y,b.z,after,0,true);
            }
            if(canDrop)SpawnSourceMiningDrops(center,broken,hasTool?tool:default(MainToolDef));
            if(hasTool&&rule.Hardness>0f&&tool.Kind!=MainToolKind.Bow)DamageSelectedInventoryItem(1);
        }

        void SpawnSourceMiningDrops(Vector3 center,BlockId broken,MainToolDef tool)
        {
            if(tool.Kind==MainToolKind.Shears&&BlockRegistry.IsLeaf(broken))
            {MainTransientRenderer.SpawnDroppedBlock(center.x,center.y,center.z,broken);return;}
            MainBlockDrops.Spawn(center.x,center.y,center.z,broken);
        }

        void ResetMining()
        {
            miningActive=false;miningOnShip=false;miningShipHandle=0;miningProgress=0f;MainTransientRenderer.ClearMiningCrack();
        }
    }
}
