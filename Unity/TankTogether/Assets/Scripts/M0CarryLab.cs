using System;
using Prefs=TankTogether.Networking.PlayerPreferences;
using ControlAction=TankTogether.Networking.PlayerAction;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankTogether.M0
{
    // Disposable local experiment. Simulated peers are test inputs, not AI or networking.
    public sealed class M0CarryLab : MonoBehaviour
    {
        public Shader surfaceShader;
        [Tooltip("仅人物外观；留空使用胶囊。不得在预制体上添加碰撞或物理驱动。")]
        public GameObject playerVisualPrefab;
        public CarryGMPanel GM { get; private set; }
        public HeightExperiment Stretch { get; set; }
        public HeightCarryBalance HeightBalance { get; } = new HeightCarryBalance();
        public bool GMOpen => GM && GM.IsOpen;
        internal Material SafeSupportMaterial => safe;
        [Tooltip("调试显示：半透明平头支撑面；仅显示，不改变碰撞/受力。F1可切换。青色空闲，黄色承托，绿色抓握。穿透显示便于观察，不代表可隔墙接触。 ")]
        public bool ShowHeadSupport = true;
        public const float MinPitch = -70, MaxPitch = 80;
        M0CarryBaseline shippedBaseline;
        public CarryTuningExtras Tuning { get; private set; } = new CarryTuningExtras();
        CarryTuningExtras shippedTuning;
        public CarryTuningProfile ShippedTuning => CarryTuning.Capture(shippedBaseline, shippedTuning, route ? route.levelId : "flat");
        public void ReadTuningComponents() { Tuning.twoPlayerGripSpring=twoPlayerGripSpring; Tuning.twoPlayerGripDamping=twoPlayerGripDamping; Tuning.twoPlayerGripMaxTorque=twoPlayerGripMaxTorque; var lift=GetComponent<WhiteboxLift>(); if(lift){Tuning.liftSpeed=lift.speed;Tuning.liftDelay=lift.delay;} }
        public void ApplyTuningComponents() { twoPlayerGripSpring=Tuning.twoPlayerGripSpring;twoPlayerGripDamping=Tuning.twoPlayerGripDamping;twoPlayerGripMaxTorque=Tuning.twoPlayerGripMaxTorque;var lift=GetComponent<WhiteboxLift>();if(lift){lift.speed=Tuning.liftSpeed;lift.delay=Tuning.liftDelay;} if(Stretch&&Stretch.Active)Stretch.ApplyConfiguration(baseline); }
        float cameraDistance = 7.5f;
        public float CameraDistance => cameraDistance;
        public void RestoreShippedBaseline()
        {
            if (shippedBaseline) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(shippedBaseline), baseline);
            if(shippedTuning!=null){Tuning=CarryTuning.Clone(shippedTuning);ApplyTuningComponents();}
        }
        public bool IsEnvironment(Collider collider)
        {
            if (!collider || collider.isTrigger || collider.attachedRigidbody == Tank) return false;
            foreach (var a in Players) if (a != null && a.body && collider.attachedRigidbody == a.body) return false;
            return true;
        }
        public M0CarryBaseline baseline;
        public Vector3 tankSize => baseline.tankSize;
        public const float MaximumPlayerHeight = 5f;
        public float standingHeight => Mathf.Min(baseline.standingHeight, MaximumPlayerHeight);
        public BoxCollider GlassCollider => Tank.GetComponent<BoxCollider>();
        public M0TankContact TankContacts => tankContact;
        public Rigidbody Tank { get; private set; }
        public readonly Actor[] Players = new Actor[4];
        public int LocalPlayerCount => TankTogether.Networking.SessionRoster.RequiredPlayers;
        public bool Broken { get; private set; }
        public event Action TankFailed;
        public event Action<int,string> PlayerAction;
        public event Action ReturnToLobbyRequested;
        public float FailedAt { get; private set; }
        public bool FailureReady => Broken && Time.unscaledTime - FailedAt >= .65f;
        TankFailureFeedback failureFeedback;
        M0TankContact tankContact;
        readonly CarryGripStability gripStability = new CarryGripStability();
        readonly CarryJump carryJump = new CarryJump();
        public bool Testing { get; set; }
        public bool ExternalControl { get; set; }
        public bool Simulate { get; set; } = true;
        public bool DynamicRoster { get; set; }
        public int SessionTeamSize { get; set; } = TankTogether.Networking.SessionRoster.RequiredPlayers;
        int CarryTeamSize => DynamicRoster ? Mathf.Clamp(SessionTeamSize, 2, LocalPlayerCount) : LocalPlayerCount;
        // Kept for serialized scenes, old tuning profiles and room compatibility signatures.
        [HideInInspector] public float twoPlayerGripSpring = 160;
        [HideInInspector] public float twoPlayerGripDamping = 18;
        [HideInInspector] public float twoPlayerGripMaxTorque = 40;
        readonly bool[] sessionRoster = new bool[4];
        bool sessionPaused;
        public bool SessionPaused => sessionPaused;
        public int Mode { get; private set; } = 1;
        public int Selected { get; private set; }
        public string Message { get; private set; } = "左键抓取，再用 WASD 搬动。两位队友使用模拟输入。";
        public float MaxGripError { get; private set; }
        public int FailureCount { get; private set; }
        const float CarryInputDeadZoneSquared = .0001f;
        const float PassiveSpeedFraction = .65f;
        Actor turnDriver;
        Vector3 turnPivot;

        public sealed class Actor
        {
            public Rigidbody body;
            public CapsuleCollider capsule;
            public Transform visual, indicator;
            public CarryActorVisual presentation;
            public ConfigurableJoint joint;
            public Vector3 move, anchor;
            public float height;
            public int stretchInput; public bool stretchArmed, gripArmed;
            public bool held, support, crouch, jump, sprint;
            public bool active = true, remoteGrip;
            public string feedback = "自由移动";
            public float failedUntil;
            public float lastGrounded = -100, jumpQueuedUntil = -100, jumpLockUntil = -100;
            public bool grounded, carryJumpAirborne;
            public Vector3 groundVelocity;
        }

        Transform world;
        WhiteboxCarryLevel route;
        Camera view;
        Material floor, grid, glass, rim, safe, hazard, neutral, grip, supporting, rejected;
        readonly List<Material> materials = new List<Material>();
        PhysicsMaterial frictionless;
        Font font;
        [NonSerialized] GUIStyle titleStyle, textStyle, smallStyle, buttonStyle;
        bool captured;
        float yaw, pitch = 24;
        Vector3 cameraFocus;
        const int SharedGripMode = 4;
        const int AutoJumpMode = 5;
        static bool Digit5Bound => Array.IndexOf(Prefs.Data.keys, (int)Key.Digit5) >= 0;
        static bool Digit6Bound => Array.IndexOf(Prefs.Data.keys, (int)Key.Digit6) >= 0;
        readonly string[] modes = { "单独控制", "三人同向模拟", "两顺一逆模拟", "两侧反向模拟", "队友原地抓取", "队友同向自动跳" };

        void Awake()
        {
            route = GetComponent<WhiteboxCarryLevel>();
            if (!baseline) baseline = Resources.Load<M0CarryBaseline>(M0CarryBaseline.ResourceName);
            if (!baseline) throw new InvalidOperationException("Apply the document baseline before running the lab");
            shippedBaseline = baseline;
            Tuning=CarryTuning.Clone(shippedBaseline.defaults ?? new CarryTuningExtras());
            shippedTuning=CarryTuning.Clone(Tuning);
            baseline = Instantiate(shippedBaseline); // GM must never edit the project asset.
            baseline.hideFlags = HideFlags.DontSave;
            ApplyTuningComponents();
            if (!GetComponent<HeadSupportDebugView>()) gameObject.AddComponent<HeadSupportDebugView>();
            Debug.Log("[M0] baseline=" + baseline.Signature + " parameters=" + JsonUtility.ToJson(baseline));
            floor = Material("Floor", new Color(0.13f, 0.18f, 0.22f));
            grid = Material("Grid", new Color(0.22f, 0.3f, 0.34f));
            rim = Material("Tank frame", new Color(0.25f, 0.88f, 0.87f));
            safe = Material("Safe supports", new Color(0.79f, 0.88f, 0.79f));
            hazard = Material("Danger", new Color(0.97f, 0.31f, 0.23f));
            neutral = Material("Free", new Color(0.62f, 0.68f, 0.76f));
            grip = Material("Gripping", new Color(0.15f, 0.95f, 0.69f));
            supporting = Material("Supporting", new Color(1f, 0.76f, 0.22f));
            rejected = Material("Rejected", new Color(1f, 0.3f, 0.28f));
            var glassAsset = Resources.Load<Material>("TankGlass");
            glass = glassAsset ? Instantiate(glassAsset) : Material("Glass", new Color(.72f, .90f, .94f, .07f));
            if (glassAsset) materials.Add(glass);
            if (!glassAsset)
            {
                glass.SetFloat("_Surface", 1);
                glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_ZWrite", 0);
                glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glass.renderQueue = 3000;
            }
            frictionless = new PhysicsMaterial("M0 no tangential carry") { dynamicFriction = 0, staticFriction = 0, bounciness = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            view = Camera.main;
            if (!view) throw new InvalidOperationException("M0 scene requires a camera");
            view.nearClipPlane = .08f;
            failureFeedback = gameObject.AddComponent<TankFailureFeedback>();
            ResetLab();
            GM = gameObject.AddComponent<CarryGMPanel>();
            GM.Initialize(this);
        }

        IEnumerator Start()
        {
            yield return null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-m0-autotest") >= 0)
                StartChecks(true);
            else if (Array.IndexOf(Environment.GetCommandLineArgs(), "-baseline-measure") >= 0)
                StartCoroutine(M0BaselineMeasurements.Run(this, true));
        }

        Material Material(string name, Color color)
        {
            var m = new Material(surfaceShader) { name = "M0 " + name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.2f);
            materials.Add(m);
            return m;
        }

        GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool physical)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (!physical) { collider.enabled = false; Destroy(collider); }
            else collider.sharedMaterial = frictionless;
            return go;
        }

        public void ResetLab()
        {
            turnDriver = null;
            if (failureFeedback) failureFeedback.Clear();
            if (!DynamicRoster) sessionPaused = false;
            FailedAt = 0;
            if (world) { world.gameObject.SetActive(false); Destroy(world.gameObject); }
            world = new GameObject("M0 local test geometry").transform;
            Broken = false;
            MaxGripError = 0;
            for (int i = 0; i < Players.Length; i++)
                Players[i] = new Actor { height = standingHeight, active = false };
            if (!route)
            {
            var ground = Shape("Danger floor", PrimitiveType.Cube, world, new Vector3(0, -0.15f, 0), new Vector3(24, 0.3f, 24), floor, true);
            ground.AddComponent<M0Danger>();
            for (int x = -10; x <= 10; x++)
            {
                Shape("Grid", PrimitiveType.Cube, world, new Vector3(x, 0.003f, 0), new Vector3(0.012f, 0.006f, 22), grid, false);
                Shape("Grid", PrimitiveType.Cube, world, new Vector3(0, 0.003f, x), new Vector3(22, 0.006f, 0.012f), grid, false);
            }
            }
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (tankSize.x / 2 - baseline.standInset.x);
                float z = (i < 2 ? -1 : 1) * (tankSize.z / 2 - baseline.standInset.y);
                Shape("Safe cotton stand", PrimitiveType.Cube, world, new Vector3(x, baseline.carryBottomHeight / 2, z), new Vector3(baseline.standWidth, baseline.carryBottomHeight, baseline.standWidth), safe, true).AddComponent<TankSupportSurface>();
                Shape("Stand base", PrimitiveType.Cube, world, new Vector3(x, 0.025f, z), new Vector3(0.5f, 0.05f, 0.5f), safe, false);
            }
            if (!route)
            {
            DangerBlock("Danger wall", new Vector3(0, 1.4f, 8), new Vector3(10, 2.8f, 0.3f));
            DangerBlock("Corner obstacle", new Vector3(4.5f, 1.2f, 3), new Vector3(0.7f, 2.4f, 3));
            }
            var tankObject = new GameObject("Shared tank");
            tankObject.transform.SetParent(world);
            tankObject.transform.position = new Vector3(0, baseline.carryBottomHeight + tankSize.y / 2 + 0.01f, 0);
            var tankCollider = tankObject.AddComponent<BoxCollider>();
            tankCollider.size = tankSize;
            tankCollider.sharedMaterial = frictionless;
            Tank = tankObject.AddComponent<Rigidbody>();
            Tank.mass = baseline.tankMass * CarryTeamSize / 4f; // Baseline mass is calibrated for four carriers.
            Tank.linearDamping = baseline.tankDamping;
            Tank.angularDamping = baseline.angularDamping;
            Tank.solverIterations = 16;
            Tank.solverVelocityIterations = 8;
            Tank.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Tank.interpolation = RigidbodyInterpolation.Interpolate;
            tankContact = tankObject.AddComponent<M0TankContact>();
            tankContact.lab = this;
            // Visible frame corresponds to the outside of the collision box.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Shape("Glass corner", PrimitiveType.Cube, tankObject.transform, new Vector3(x * (tankSize.x / 2 - 0.025f), 0, z * (tankSize.z / 2 - 0.025f)), new Vector3(0.05f, tankSize.y, 0.05f), rim, false);
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                    Shape("Long edge", PrimitiveType.Cube, tankObject.transform, new Vector3(0, y * (tankSize.y / 2 - 0.025f), z * (tankSize.z / 2 - 0.025f)), new Vector3(tankSize.x, 0.05f, 0.05f), rim, false);
                for (int x = -1; x <= 1; x += 2)
                    Shape("Short edge", PrimitiveType.Cube, tankObject.transform, new Vector3(x * (tankSize.x / 2 - 0.025f), y * (tankSize.y / 2 - 0.025f), 0), new Vector3(0.05f, 0.05f, tankSize.z), rim, false);
            }
            // Five open-top panels; the existing physical outside box remains authoritative.
            const float pane = .012f;
            for (int z = -1; z <= 1; z += 2)
                Shape("Glass long panel", PrimitiveType.Cube, tankObject.transform, new Vector3(0, 0, z * (tankSize.z-pane)/2), new Vector3(tankSize.x, tankSize.y, pane), glass, false);
            for (int x = -1; x <= 1; x += 2)
                Shape("Glass short panel", PrimitiveType.Cube, tankObject.transform, new Vector3(x * (tankSize.x-pane)/2, 0, 0), new Vector3(pane, tankSize.y, tankSize.z), glass, false);
            Shape("Glass bottom", PrimitiveType.Cube, tankObject.transform, new Vector3(0, -(tankSize.y-pane)/2, 0), new Vector3(tankSize.x, pane, tankSize.z), glass, false);
            for (int i = 0; i < LocalPlayerCount; i++)
                if (!DynamicRoster || sessionRoster[i]) CreateActor(i);
            cameraFocus = CameraTarget(1);
            Message = "安全支架上的空缸。左键抓取后移动；黄色圆点表示仅支撑。";
            GetComponent<M1InteractionLab>()?.BuildWorld(world);
            if (route)
            {
                route.PlaceAtStart(this, world);
                cameraFocus = CameraTarget(1);
                Message = route.useCheckpointZones && route.CurrentCheckpoint > 0 ? (route.checkpoints[route.CurrentCheckpoint].tankRestHeight > 0 ? "已恢复检查点，鱼缸已放回中央台座；按住左键抓取后继续。" : "已恢复检查点，鱼缸由队员承托；按住左键抓取后继续。") : route.checkpoints.Length > 0 ? "从黄球平台出发，前往白色标记对应的鱼缸；R 重试当前检查点。" : "黄点起始平台，面向分段桥。左键抓取后前进；R 回起点。";
            }
            if (DynamicRoster)
            {
                FreezeBody(Tank, sessionPaused || !Simulate);
                for (int i = 0; i < Players.Length; i++) FreezeBody(Players[i].body, sessionPaused || !Simulate);
            }
        }

        Vector3 SpawnLocal(int index)
        {
            int count = 0, order = 0;
            for (int i = 0; i < LocalPlayerCount; i++)
                if (!DynamicRoster || sessionRoster[i]) { if (i < index) order++; count++; }
            if (count <= 1) return new Vector3(0, 0.02f, 0);
            if (count == 2) return new Vector3(0, 0.02f, (order == 0 ? -1 : 1) * baseline.spawnHalfSpacing.y);
            return CarryFormationOffset(order) + Vector3.up * .02f;
        }

        // Local feet positions for assembling three carriers under the left, right and rear edges.
        public Vector3 CarryFormationOffset(int order)
        {
            float diameter = baseline.playerDiameter;
            float gripMargin = baseline.gripInset + .01f;
            float inset = Mathf.Max(Mathf.Min(diameter * .5f, .30f), gripMargin);
            float x = Mathf.Max(0, tankSize.x * .5f - inset);
            float z = Mathf.Max(0, tankSize.z * .5f - inset);
            if (2 * x < diameter || x * x + 2.25f * z * z < diameter * diameter)
            {
                // On a narrow tank, leave the capsule overhanging before overlapping another carrier.
                x = Mathf.Max(0, tankSize.x * .5f - gripMargin);
                z = Mathf.Max(0, tankSize.z * .5f - gripMargin);
            }
            return order == 2 ? Vector3.back * z : new Vector3(order == 0 ? -x : x, 0, z * .5f);
        }

        void CreateActor(int index)
        {
            if (!world || !Tank) return;
            Color[] colors = { new Color(0.22f, 0.6f, 1), new Color(0.75f, 0.4f, 1), new Color(1, 0.58f, 0.2f), new Color(0.85f, 0.3f, 0.57f) };
            var go = new GameObject("Player " + (index + 1));
            go.transform.SetParent(world, false);
            // Lazy creation may happen after the level adapter rotated or moved the world.
            go.transform.position = world.TransformPoint(SpawnLocal(index));
            var actor = new Actor { height = standingHeight, active = true };
            actor.capsule = go.AddComponent<CapsuleCollider>();
            actor.capsule.height = standingHeight;
            actor.capsule.center = Vector3.up * standingHeight / 2;
            actor.capsule.radius = baseline.playerDiameter / 2;
            actor.capsule.sharedMaterial = frictionless;
            actor.body = go.AddComponent<Rigidbody>();
            actor.body.mass = baseline.playerMass;
            actor.body.constraints = RigidbodyConstraints.FreezeRotation;
            actor.body.solverIterations = 16;
            actor.body.solverVelocityIterations = 8;
            actor.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            actor.body.interpolation = RigidbodyInterpolation.Interpolate;
            if (playerVisualPrefab)
            {
                actor.visual = Instantiate(playerVisualPrefab, go.transform, false).transform;
                actor.visual.name = "Character Visual";
                // Defensive isolation if a later art prefab includes physics.
                foreach (var c in actor.visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                foreach (var b in actor.visual.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
                actor.presentation = actor.visual.gameObject.AddComponent<CarryActorVisual>();
                actor.presentation.Initialize(this, actor);
                var artProfile=actor.visual.GetComponent<CarryVisualProfile>(); if(artProfile)artProfile.ApplyColor(index);
                // Hats identify each player; source materials are never modified.
                foreach (var renderer in actor.visual.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.Contains("Cap"))
                    {
                        var properties = new MaterialPropertyBlock();
                        properties.SetColor("_BaseColor", colors[index]);
                        renderer.SetPropertyBlock(properties, 0);
                    }
            }
            else
                actor.visual = Shape("Player body", PrimitiveType.Capsule, go.transform, Vector3.up * standingHeight / 2,
                    new Vector3(baseline.playerDiameter, standingHeight / 2, baseline.playerDiameter),
                    materials.Find(m => m.name == "M0 Player " + index) ?? Material("Player " + index, colors[index]), false).transform;
            actor.indicator = Shape("Interaction state", PrimitiveType.Sphere, go.transform,
                Vector3.up * (standingHeight + 0.06f), Vector3.one * 0.13f, neutral, false).transform;
            // Reset while the button is held must not manufacture a new press edge.
            if (captured && !Testing && Mouse.current != null) actor.held = Prefs.Held(ControlAction.Grab);
            Players[index] = actor;
            if (DynamicRoster) FreezeBody(actor.body, sessionPaused || !Simulate);
        }

        public void UpdateActorVisual(Actor actor)
        {
            if (actor.presentation) actor.presentation.RefreshSize();
            else if (actor.visual)
            {
                actor.visual.localPosition = Vector3.up * actor.height / 2;
                actor.visual.localScale = new Vector3(baseline.playerDiameter, actor.height / 2, baseline.playerDiameter);
            }
        }

        static void FreezeBody(Rigidbody body, bool frozen)
        {
            if (!body) return;
            // Unity rejects velocity writes on a kinematic body. Always discard old motion before resuming.
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = frozen;
            if (!frozen) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }

        public void ConfigureSessionRoster(bool[] connected, bool simulate)
        {
            if (connected == null || connected.Length != Players.Length)
                throw new ArgumentException("A session roster must contain four slots", nameof(connected));
            if (!DynamicRoster) throw new InvalidOperationException("Enable DynamicRoster before configuring a session");
            Array.Copy(connected, sessionRoster, LocalPlayerCount);
            Simulate = simulate;
            // Configure only when starting/restarting at a checkpoint, never during a live carry.
            if (world && Tank) ResetLab();
        }

        public void SetSessionPaused(bool paused)
        {
            sessionPaused = paused;
            for (int i = 0; i < Players.Length; i++)
            {
                var a = Players[i];
                if (a == null) continue;
                Release(i);
                ClearInput(a);
                FreezeBody(a.body, paused || !Simulate);
            }
            FreezeBody(Tank, paused || !Simulate);
        }

        static void ClearInput(Actor actor)
        {
            actor.stretchInput=0; actor.stretchArmed=false; actor.gripArmed=false;
            actor.move = Vector3.zero;
            actor.lastGrounded = actor.jumpQueuedUntil = actor.jumpLockUntil = -100;
            actor.grounded = false;
            actor.carryJumpAirborne = false;
            actor.groundVelocity = Vector3.zero;
            actor.held = actor.jump = actor.crouch = actor.sprint = actor.support = actor.remoteGrip = false;
        }

        Actor ActorAt(int index) => index >= 0 && index < Players.Length ? Players[index] : null;
        Vector3 CameraTarget(float height)
        {
            var actor = ActorAt(Selected);
            // Keep the original framing through 2.3 m; follow the extra tall body around its midpoint.
            float tallOffset = actor != null && Stretch && Stretch.Active ? Mathf.Max(0, actor.height - 2.3f) * .5f : 0;
            return actor != null && actor.active && actor.body ? actor.body.transform.position + Vector3.up * (height + tallOffset) : Tank ? Tank.transform.position : Vector3.zero;
        }

        void DangerBlock(string name, Vector3 position, Vector3 size)
        {
            Shape(name, PrimitiveType.Cube, world, position, size, hazard, true).AddComponent<M0Danger>();
        }

        public Vector3 Head(int index)
        {
            var actor = ActorAt(index);
            return actor != null && actor.body ? actor.body.position + Vector3.up * actor.height : Tank ? Tank.position : Vector3.zero;
        }
        public bool IsGripping(int index)
        {
            var actor = ActorAt(index);
            return actor != null && actor.active && actor.body && actor.joint;
        }

        public void SetExternalView(int index, float cameraYaw, float cameraPitch)
        {
            Selected = Mathf.Clamp(index, 0, LocalPlayerCount - 1);
            yaw = cameraYaw;
            pitch = Mathf.Clamp(cameraPitch, MinPitch, MaxPitch);
        }

        public void SetActorActive(int index, bool active)
        {
            if (index < 0 || index >= Players.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= LocalPlayerCount) return; // Reserved snapshot slot has no game object.
            var a = Players[index];
            if (DynamicRoster)
            {
                sessionRoster[index] = active;
                if (!active)
                {
                    if (a != null)
                    {
                        Release(index); ClearInput(a);
                        if (a.body) { a.body.gameObject.SetActive(false); Destroy(a.body.gameObject); }
                    }
                    Players[index] = new Actor { height = standingHeight, active = false };
                    return;
                }
                if (a == null || !a.body) { CreateActor(index); return; }
            }
            if (a == null || !a.body) return;
            if (!active) { Release(index); ClearInput(a); }
            a.active = active;
            a.body.gameObject.SetActive(active);
        }

        public void SetProxyState(bool broken, string message)
        {
            if (broken && !Broken) {
                FailedAt = Time.unscaledTime;
                failureFeedback.Show(Tank, tankSize, surfaceShader);
            } else if (!broken && Broken) failureFeedback.Clear();
            Broken = broken;
            Message = message;
        }

        public void SetInput(int index, Vector3 movement, bool held, bool jump = false, bool crouch = false, bool sprint = false, int stretch = 0)
        {
            var a = ActorAt(index);
            if (a == null || !a.body || !a.active) return;
            if (Broken || sessionPaused) { Release(index); ClearInput(a); return; }
            a.move = Vector3.ClampMagnitude(new Vector3(movement.x, 0, movement.z), 1);
            a.jump |= jump;
            a.crouch = Stretch && Stretch.Active ? false : crouch;
            a.sprint = sprint;
            if(stretch==0)a.stretchArmed=true;
            a.stretchInput=a.stretchArmed?Math.Sign(stretch):0;
            if(!held)a.gripArmed=true;
            if (held && !a.held && (!(Stretch && Stretch.Active) || a.gripArmed)) TryGrip(index);
            if (!held && a.held) Release(index);
            a.held = held;
        }

        void TryGrip(int index)
        {
            var a = ActorAt(index);
            if (a == null || !a.body || !a.active || !Tank || sessionPaused) return;
            Vector3 local = Tank.transform.InverseTransformPoint(Head(index));
            if (Mathf.Abs(local.x) > tankSize.x / 2 - baseline.gripInset || Mathf.Abs(local.z) > tankSize.z / 2 - baseline.gripInset || Mathf.Abs(local.y + tankSize.y / 2) > baseline.gripVerticalReach || Vector3.Dot(Tank.transform.up, Vector3.up) < 0.4f)
            {
                a.feedback = "抓取失败：请到鱼缸底下，松开后重按";
                a.failedUntil = Time.time + 2;
                if (index == Selected) Message = a.feedback;
                return;
            }
            a.anchor = new Vector3(local.x, -tankSize.y / 2, local.z);
            a.jumpQueuedUntil = -100;
            a.joint = a.body.gameObject.AddComponent<ConfigurableJoint>();
            PlayerAction?.Invoke(index,"grab");
            a.joint.connectedBody = Tank;
            a.joint.autoConfigureConnectedAnchor = false;
            a.joint.anchor = Vector3.up * a.height;
            a.joint.connectedAnchor = a.anchor;
            a.joint.xMotion = a.joint.yMotion = a.joint.zMotion = ConfigurableJointMotion.Limited;
            a.joint.linearLimit = new SoftJointLimit { limit = baseline.gripLimit };
            a.joint.angularXMotion = a.joint.angularYMotion = a.joint.angularZMotion = ConfigurableJointMotion.Free;
            a.joint.projectionMode = JointProjectionMode.None;
            a.joint.breakForce = a.joint.breakTorque = float.PositiveInfinity;
            a.feedback = "已抓取";
            a.failedUntil = 0;
            if (index == Selected) Message = "已抓取：移动会影响鱼缸；松开左键可换位。";
        }

        public void Release(int index)
        {
            var a = ActorAt(index);
            if (a == null) return;
            if (a.joint)
            {
                turnDriver = null;
                // Destroy is deferred; free the constraint immediately before removing the component.
                a.joint.xMotion = a.joint.yMotion = a.joint.zMotion = ConfigurableJointMotion.Free;
                Destroy(a.joint);
                a.joint = null;
            }
            a.carryJumpAirborne = false;
            a.feedback = "自由移动";
            if (index == Selected && !Broken) Message = "已松手：头顶接触仍可支撑，换位后重新按左键。";
        }

        public void PlacePlayer(int index, Vector3 feet)
        {
            Release(index);
            var a = ActorAt(index);
            if (a == null || !a.body) return;
            a.held = false;
            ClearInput(a);
            if (Stretch && Stretch.Active) { a.height = standingHeight; a.stretchInput=0; a.stretchArmed=false; if(index==Selected)Stretch.ResetInput(); }
            a.body.position = feet;
            if (!a.body.isKinematic) { a.body.linearVelocity = Vector3.zero; a.body.angularVelocity = Vector3.zero; }
        }

        public int DebugTeleportToTank(bool allPlayers=false)
        {
            var session=GetComponent<TankTogether.Networking.SteamCarrySession>();
            if(!Tank||Broken||ExternalControl||DynamicRoster||GMOpen||sessionPaused ||
                (Stretch&&Stretch.Active&&!Stretch.InLevel) ||
                (session&&(session.Mode!=TankTogether.Networking.SteamCarrySession.EntryMode.LocalTest||session.InRoom||session.PersonalDialogVisible))) return 0;
            int moved=0,wanted=0;
            Physics.SyncTransforms();
            for(int i=0;i<Players.Length;i++)
            {
                var a=Players[i];if((!allPlayers&&i!=Selected)||a==null||!a.active||!a.body)continue;
                wanted++;
                float height=Stretch&&Stretch.Active?a.height:standingHeight;
                if(!TankDebugTeleport.FindFeet(this,i,height,out var feet))continue;
                PlacePlayer(i,feet);a.height=height;a.capsule.height=height;a.capsule.center=Vector3.up*height/2;
                a.body.transform.position=feet;UpdateActorVisual(a);Physics.SyncTransforms();moved++;
            }
            if(moved>0)cameraFocus=CameraTarget(1.25f);
            Message=moved==0?"传送失败：鱼缸附近没有可站立的安全空间。":$"测试传送：{moved}/{wanted} 人到达鱼缸附近；已松手并清除速度，身高保留。";
            return moved;
        }

        internal bool TryTeleportTankOverPlayer(int index)
        {
            var a = ActorAt(index);
            if (!Simulate || sessionPaused || Broken || !Tank || Tank.isKinematic ||
                a == null || !a.active || !a.body) return false;

            var rotation = Quaternion.Euler(0, Tank.rotation.eulerAngles.y, 0);
            var center = a.body.position + Vector3.up * (a.height + tankSize.y * .5f + .015f);
            Physics.SyncTransforms();
            foreach (var obstacle in Physics.OverlapBox(center, tankSize * .5f, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (obstacle != GlassCollider) return false;

            for (int i = 0; i < Players.Length; i++)
            {
                Release(i);
                if (Players[i] != null) Players[i].support = false;
            }
            Tank.position = center;
            Tank.rotation = rotation;
            Tank.transform.SetPositionAndRotation(center, rotation);
            Tank.linearVelocity = Tank.angularVelocity = Vector3.zero;
            TankContacts.ClearSupports();
            if (route) route.DisarmCheckpointZone();
            Physics.SyncTransforms();
            return true;
        }

        public void ChangeMode(int mode)
        {
            Mode = mode;
            if (mode == AutoJumpMode)
            {
                for (int i = 0; i < LocalPlayerCount; i++)
                {
                    if (i == Selected) continue;
                    Players[i].jump = false;
                    Players[i].jumpQueuedUntil = -100;
                }
                Message = "队友同向移动，过断口和上台阶时各自起跳。当前角色手动跳跃。";
                return;
            }
            if (mode != SharedGripMode)
                for (int i = 0; i < LocalPlayerCount; i++) SetInput(i, Vector3.zero, false);
            Message = mode == SharedGripMode ? "队友保持抓取，不主动移动。当前角色独立操作，Tab 换人。" : modes[Mode] + "；模拟角色只复用输入，不自动寻路或救援。";
        }

        void Update()
        {
            if (Testing || ExternalControl || GMOpen) return;
            var menu=GetComponent<TankTogether.Networking.SteamCarrySession>();if(menu&&menu.MenuOverlayVisible){ReleaseLocalControl();return;}
            var k = Keyboard.current;
            var mouse = Mouse.current;
            if (k == null || mouse == null) return;
            if (k.escapeKey.wasPressedThisFrame) SetCaptured(false);
            if (!captured && k.enterKey.wasPressedThisFrame) SetCaptured(true);
            if (k.rKey.wasPressedThisFrame) { if (Broken) RetryFailure(); else ResetLab(); }
            if (Broken) return;
            if (captured && k.f4Key.wasPressedThisFrame) { DebugTeleportToTank(k.leftShiftKey.isPressed || k.rightShiftKey.isPressed); return; }
            bool teamControls = !(Stretch && Stretch.Active && !Stretch.InLevel);
            bool shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
            if (teamControls && k.digit1Key.wasPressedThisFrame) ChangeMode(0);
            if (teamControls && k.digit2Key.wasPressedThisFrame) ChangeMode(1);
            if (teamControls && k.digit3Key.wasPressedThisFrame)
                ChangeMode(Digit6Bound && shift ? AutoJumpMode : 2);
            bool mode4Pressed = teamControls && k.digit4Key.wasPressedThisFrame;
            bool sharedGripPressed = Digit5Bound
                ? mode4Pressed && shift
                : teamControls && k.digit5Key.wasPressedThisFrame;
            if (sharedGripPressed) ChangeMode(SharedGripMode);
            else if (mode4Pressed) ChangeMode(3);
            if (teamControls && !Digit6Bound && k.digit6Key.wasPressedThisFrame) ChangeMode(AutoJumpMode);
            if (teamControls && k.tabKey.wasPressedThisFrame)
            {
                if (Mode == AutoJumpMode)
                {
                    Players[Selected].jump = false;
                    Players[Selected].jumpQueuedUntil = -100;
                }
                else if (Mode != SharedGripMode) SetInput(Selected, Vector3.zero, false);
                Selected = (Selected + 1) % LocalPlayerCount;
                Message = "切换至 P" + (Selected + 1) + "；切换操作是本地测试工具。";
            }
            if (!captured) return;
            var delta = mouse.delta.ReadValue();
            yaw += delta.x * Tuning.mouseYaw * Prefs.Data.sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * Tuning.mousePitch * Prefs.Data.sensitivity * (Prefs.Data.invertY?-1:1), MinPitch, MaxPitch);
            var raw = Prefs.Move;
            var move = Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(raw, 1);
            bool hold = Prefs.Held(ControlAction.Grab);
            bool jump = Prefs.Pressed(ControlAction.Jump);
            bool crouch = Prefs.Held(ControlAction.Crouch);
            bool sprint = Prefs.Held(ControlAction.Run);
            if (Stretch && Stretch.Active) Stretch.ReadKeys(k);
            for (int i = 0; i < LocalPlayerCount; i++)
            {
                if (i == Selected) SetInput(i, move, hold, jump, crouch, sprint, Stretch && Stretch.Active ? Stretch.Direction : 0);
                else if (Mode == SharedGripMode)
                {
                    var teammate = Players[i];
                    teammate.jump = false;
                    teammate.jumpQueuedUntil = -100;
                    if (sharedGripPressed && !teammate.joint) teammate.held = false;
                    if (!teammate.held) teammate.gripArmed = true;
                    SetInput(i, Vector3.zero, true);
                }
                else if (Mode == AutoJumpMode) SetInput(i, move, hold);
                else
                {
                    float sign = Mode == 2 && i == (Selected + 1) % LocalPlayerCount ? -1 : 1;
                    if (Mode == 3) sign = i % 2 == Selected % 2 ? 1 : -1;
                    SetInput(i, Mode == 0 ? Vector3.zero : move * sign, Mode != 0 && hold, Mode != 0 && jump, Mode != 0 && crouch, Mode != 0 && sprint, Mode != 0 && Stretch && Stretch.Active ? Stretch.Direction : 0);
                }
            }
            if(Prefs.Pressed(ControlAction.Interact)) {
                var lift=GetComponent<WhiteboxLift>();
                if(lift) { lift.TryInteract(Selected,out var feedback,Prefs.Held(ControlAction.Grab));Message=feedback; }
            }
        }

        Vector3 SoloCarryMotor(Actor driver, Vector3 localCenter, float driveSpeed)
        {
            // Use the physics pose: the rendered transform may still be on the previous frame.
            Vector3 center = Tank.position + Tank.rotation * localCenter;
            Vector3 groundVelocity = driver.grounded ? Vector3.ProjectOnPlane(driver.groundVelocity, Vector3.up) : Vector3.zero;
            if (turnDriver != driver) turnPivot = center;
            else turnPivot += groundVelocity * Time.fixedDeltaTime;
            turnDriver = driver;

            Vector3 radius = Vector3.ProjectOnPlane(Tank.rotation * (driver.anchor - localCenter), Vector3.up);
            float driverRadius = radius.magnitude;
            // A grip at the pivot can haul in any direction. Fade in turn leverage over half a foot radius.
            Vector3 tangent = Vector3.Cross(Vector3.up, radius) / Mathf.Max(driverRadius, baseline.playerDiameter * .25f);
            float outerRadius = 0, gripSpan = 0;
            foreach (var a in Players)
            {
                if (a == null || a == driver || !a.joint) continue;
                Vector3 arm = Vector3.ProjectOnPlane(Tank.rotation * (a.anchor - localCenter), Vector3.up);
                outerRadius = Mathf.Max(outerRadius, arm.magnitude);
                gripSpan = Mathf.Max(gripSpan, (arm - radius).magnitude);
            }
            // All three points share one yaw rate. Limit it by the fastest passive arc.
            float maxTangentSpeed = driveSpeed * driver.move.magnitude * PassiveSpeedFraction * driverRadius / Mathf.Max(.001f, outerRadius);
            float tangentInput = Vector3.Dot(driver.move, tangent);
            float speed = Mathf.Clamp(tangentInput * driveSpeed, -maxTangentSpeed, maxTangentSpeed);
            Vector3 pivotVelocity = Vector3.ProjectOnPlane(Tank.GetPointVelocity(center), Vector3.up);
            float relativeSpeed = Vector3.Dot(driver.body.linearVelocity - pivotVelocity, tangent);
            Vector3 radialInput = driver.move - tangent * tangentInput;
            Vector3 haulDirection = radialInput.normalized;
            float haulSpeed = radialInput.magnitude * driveSpeed;
            float footSpeed = Vector3.Dot(driver.body.linearVelocity - groundVelocity, haulDirection);
            Vector3 drive = Vector3.ClampMagnitude((tangent * (speed - relativeSpeed) + haulDirection * (haulSpeed - footSpeed))
                * baseline.carryMotorGain + haulDirection * haulSpeed * Tank.mass * baseline.tankDamping / 3f, baseline.carryMotorForce);
            Vector3 turnForce = tangent * Vector3.Dot(drive, tangent);

            // A player close to the pivot has little leverage against the other two grips.
            // Bound the team's yaw torque by one motor acting across the actual grip separation.
            float targetYaw = speed / Mathf.Max(driverRadius, baseline.playerDiameter * .5f);
            float yawTorque = Mathf.Clamp((targetYaw - Tank.angularVelocity.y) * baseline.carryMotorGain * gripSpan * gripSpan,
                -baseline.carryMotorForce * gripSpan, baseline.carryMotorForce * gripSpan);
            Tank.AddTorque(Vector3.up * (yawTorque - Vector3.Dot(Vector3.Cross(radius, drive), Vector3.up)));

            // Let the pivot follow actual radial travel, so a blocked pull cannot wind up a spring.
            float travel = Mathf.Clamp(Vector3.Dot(center - turnPivot, haulDirection), 0, haulSpeed * Time.fixedDeltaTime);
            turnPivot += haulDirection * travel;
            Vector3 slip = pivotVelocity - groundVelocity;
            slip -= haulDirection * Mathf.Clamp(Vector3.Dot(slip, haulDirection), 0, haulSpeed);
            Vector3 offset = Vector3.ProjectOnPlane(turnPivot - center, Vector3.up);
            Vector3 correction = Vector3.ClampMagnitude((offset * 6f - slip) * baseline.carryMotorGain * 2f,
                baseline.carryMotorForce);
            Vector3 reactionPoint = new Vector3(center.x, Tank.worldCenterOfMass.y, center.z);
            Tank.AddForceAtPosition(correction - turnForce, reactionPoint);
            return drive;
        }

        void FixedUpdate()
        {
            if (Tank == null || !Simulate || sessionPaused) return;
            int grips = 0, movers = 0;
            int jumpingGrips = 0;
            bool canAutoJump = Mode == AutoJumpMode && captured && !ExternalControl && !Players[Selected].jump;
            bool carryingInFlight = false;
            bool plantedSupport = false;
            Actor driver = null;
            Vector3 anchorSum = Vector3.zero;
            Vector3 carryIntent = Vector3.zero;
            float carryGrade = 0;
            foreach (var player in Players)
            {
                if (player == null || !player.active || !player.body || !player.joint) continue;
                grips++;
                anchorSum += player.anchor;
                if (player.move.sqrMagnitude < CarryInputDeadZoneSquared) continue;
                movers++;
                carryIntent += player.move;
                driver = player;
                float speed = player.sprint ? baseline.carrySprintSpeed : baseline.carrySpeed;
                carryGrade = Mathf.Max(carryGrade, speed);
            }
            Vector3 tipTorque = Vector3.zero;
            Vector3 downhill = Vector3.zero;
            bool unstable = grips > 0 && !Broken && !Tank.isKinematic &&
                gripStability.Evaluate(this, out tipTorque, out downhill);
            float carryingSpeedScale = Mathf.Pow((float)grips / CarryTeamSize, 2);
            carryGrade *= carryingSpeedScale;
            // At half the active speed grade one planted carrier absorbs one motor's maximum push.
            float halfGrade = Mathf.Max(.05f, carryGrade * .5f);
            float footDrag = baseline.carryMotorForce / (halfGrade * halfGrade);
            Vector3 sharedMove = Vector3.ClampMagnitude(carryIntent, 1);
            Vector3 turnMotor = Vector3.zero;
            if (grips == 3 && movers == 1 && !Broken)
                turnMotor = SoloCarryMotor(driver, anchorSum / grips, carryGrade);
            else
                turnDriver = null;
            for (int i = 0; i < 4; i++)
            {
                var a = Players[i];
                if (a == null) continue;
                bool supporting = a.joint || (a.grounded && a.support);
                a.support = false;
                if (!a.active || !a.body || Broken) continue;
                float maxHeight = supporting && downhill != Vector3.zero ?
                    CarryGripStability.MaxSupportHeight(this, a, downhill) : float.PositiveInfinity;
                // A high grab or a teammate letting go can lower the safe height between physics steps.
                float stepCeiling = Mathf.Max(maxHeight, a.height - 2f * Time.fixedDeltaTime);
                float newHeight = Stretch && Stretch.Active ? Stretch.Step(a, Time.fixedDeltaTime, stepCeiling) :
                    Mathf.Min(stepCeiling, Mathf.MoveTowards(a.height, a.crouch ? baseline.crouchingHeight : standingHeight, Time.fixedDeltaTime * 2));
                newHeight = Mathf.Min(newHeight, MaximumPlayerHeight);
                a.height = newHeight;
                a.capsule.height = newHeight;
                a.capsule.center = Vector3.up * newHeight / 2;
                UpdateActorVisual(a);
                a.indicator.localPosition = Vector3.up * (newHeight + 0.06f);
                if (a.joint) a.joint.anchor = Vector3.up * newHeight;
                Vector3 horizontal = Vector3.ProjectOnPlane(a.body.linearVelocity, Vector3.up);
                float targetSpeed = a.joint ? (a.sprint ? baseline.carrySprintSpeed : baseline.carrySpeed) * carryingSpeedScale : (a.sprint ? baseline.sprintSpeed : baseline.walkSpeed);
                Vector3 groundNormal, groundVelocity;
                bool hasGround = ProbeGround(a, out groundNormal, out groundVelocity);
                bool candidate = baseline.controlFeelMode >= .5f;
                if (!candidate && !a.joint) groundVelocity = Vector3.zero;
                // Contact is relative to the supporting surface, not world vertical speed.
                bool grounded = hasGround && Time.time >= a.jumpLockUntil && Vector3.Dot(a.body.linearVelocity-groundVelocity, groundNormal) < 1.5f;
                if (grounded) a.carryJumpAirborne = false;
                Vector3 footVelocity = grounded ? Vector3.ProjectOnPlane(a.body.linearVelocity - groundVelocity, groundNormal) : horizontal;
                a.grounded = grounded;
                if (grounded) { a.lastGrounded = Time.time; a.groundVelocity = groundVelocity; }
                if (canAutoJump && i != Selected && grounded && ShouldAutoJump(a, groundNormal, footVelocity))
                {
                    a.jump = true;
                    canAutoJump = false;
                }
                if (a.jump && !a.joint) a.jumpQueuedUntil = Time.time + baseline.jumpBufferTime;
                Vector3 motor;
                if (!a.joint)
                {
                    var velocity = grounded ? Vector3.ProjectOnPlane(a.body.linearVelocity-groundVelocity, groundNormal) : horizontal;
                    var desired = a.move * targetSpeed;
                    if (grounded) desired = Vector3.ProjectOnPlane(desired, groundNormal).normalized * desired.magnitude;
                    float acceleration = grounded ? (a.move.sqrMagnitude < .0001f ? baseline.freeDeceleration : baseline.freeAcceleration) : baseline.airAcceleration;
                    if (candidate && !grounded && a.move.sqrMagnitude < .0001f)
                        acceleration = baseline.airReleaseDeceleration;
                    Vector3 velocityChange = desired - velocity;
                    bool instant = baseline.instantMovementMode >= .5f && (grounded || a.move.sqrMagnitude >= .0001f);
                    if (!instant) velocityChange = Vector3.ClampMagnitude(velocityChange, acceleration * Time.fixedDeltaTime);
                    motor = velocityChange * (a.body.mass / Time.fixedDeltaTime);
                    // Cancel downhill gravity only while supported. Air and held-tank physics stay physical.
                    if (grounded) motor -= Vector3.ProjectOnPlane(Physics.gravity, groundNormal) * a.body.mass;
                }
                else if (a == turnDriver)
                    motor = grounded ? Vector3.ProjectOnPlane(turnMotor, groundNormal).normalized * turnMotor.magnitude : turnMotor;
                else if (a.move.sqrMagnitude < CarryInputDeadZoneSquared)
                {
                    motor = Vector3.zero;
                    if (grounded && movers == 0)
                        motor = Vector3.ClampMagnitude(-footVelocity * baseline.carryMotorGain,
                            (a.body.mass + Tank.mass / grips) * baseline.carryBrakeAcceleration);
                }
                else
                {
                    // Independent world-space drives reach the tank through their individual grip levers.
                    Vector3 desired = a.move * targetSpeed;
                    if (grounded) desired = Vector3.ProjectOnPlane(desired, groundNormal).normalized * desired.magnitude;
                    motor = (desired - footVelocity) * baseline.carryMotorGain;
                    motor += desired * Tank.mass * baseline.tankDamping / grips;
                    motor = Vector3.ClampMagnitude(motor, baseline.carryMotorForce);
                }
                if (a.joint && grounded && movers > 0)
                {
                    // A foot resists the part of its motion that this player isn't walking with.
                    // Pulling backwards therefore keeps the passive load and adds an opposing drive.
                    Vector3 heading = Vector3.ProjectOnPlane(a.move, groundNormal).normalized;
                    Vector3 slip = footVelocity - heading * Mathf.Clamp(Vector3.Dot(footVelocity, heading), 0, a.move.magnitude * targetSpeed);
                    motor -= Vector3.ClampMagnitude(slip * (footDrag * slip.magnitude), 2f * baseline.carryMotorForce);
                    // Planted legs carry their share of the weight uphill, including when dragged.
                    Vector3 uphillInput = a.move.sqrMagnitude < CarryInputDeadZoneSquared ? sharedMove : a.move;
                    Vector3 uphill = Vector3.ProjectOnPlane(uphillInput, groundNormal).normalized;
                    float uphillGravity = Mathf.Max(0, -Vector3.Dot(Physics.gravity, uphill));
                    motor += uphill * (uphillGravity * (a.body.mass + Tank.mass / grips) * uphillInput.magnitude);
                }
                a.body.AddForce(motor);
                bool jumpNow = a.joint ? a.jump && grounded : Time.time <= a.jumpQueuedUntil && Time.time - a.lastGrounded <= baseline.coyoteTime;
                if (jumpNow && Time.time >= a.jumpLockUntil)
                {
                    if (a.joint && !Tank.isKinematic)
                    {
                        jumpingGrips |= 1 << i;
                        a.carryJumpAirborne = true;
                    }
                    else
                    {
                        float riseGravity = Mathf.Abs(Physics.gravity.y) * baseline.freeRiseGravityMultiplier;
                        float velocity = Mathf.Sqrt(2 * riseGravity * baseline.freeJumpHeight) + riseGravity * Time.fixedDeltaTime / 2
                            + (candidate ? a.groundVelocity.y : 0) - a.body.linearVelocity.y;
                        a.body.AddForce(Vector3.up * velocity, ForceMode.VelocityChange);
                    }
                    a.jumpQueuedUntil = a.lastGrounded = -100;
                    a.jumpLockUntil = Time.time + .12f;
                    a.grounded = false;
                    PlayerAction?.Invoke(i,"jump");
                }
                a.jump = false;
                carryingInFlight |= a.carryJumpAirborne;
                Vector3 head = Head(i);
                // Unstable grips support only at their anchors. B grips already have their own vertical spring.
                bool anchorSupport = a.joint && (unstable || Stretch && Stretch.Active && Stretch.PhysicsEnabled);
                if (baseline.headSupportMode >= .5f)
                {
                    if (!anchorSupport) a.support = FlatHeadSupport.Apply(this, a) > 1;
                }
                else
                {
                    Vector3 local = Tank.transform.InverseTransformPoint(head);
                    Vector3 bottom = Tank.transform.TransformPoint(new Vector3(local.x, -tankSize.y / 2, local.z));
                    float compression = head.y - bottom.y;
                    if (!anchorSupport && Mathf.Abs(local.x) < tankSize.x / 2 - 0.1f && Mathf.Abs(local.z) < tankSize.z / 2 - 0.1f && compression >= -0.01f && compression < 0.25f && Tank.transform.up.y > 0.3f)
                    {
                        float relativeSpeed = a.body.linearVelocity.y - Tank.GetPointVelocity(bottom).y;
                        float force = Mathf.Clamp(Mathf.Max(0, compression) * baseline.supportStiffness + relativeSpeed * baseline.supportDamping, 0, baseline.supportMaxForce);
                        a.support = force > 1;
                        Tank.AddForceAtPosition(Vector3.up * force, bottom);
                        a.body.AddForce(Vector3.down * force);
                        if(Stretch && Stretch.Active && Stretch.PhysicsEnabled && !a.joint){
                            var slip=Vector3.ProjectOnPlane(a.body.GetPointVelocity(head)-Tank.GetPointVelocity(bottom),Vector3.up);
                            var friction=Vector3.ClampMagnitude(slip*20,force*.15f);
                            Tank.AddForceAtPosition(friction,bottom);a.body.AddForce(-friction);
                        }
                    }
                }
                if(Stretch && Stretch.Active && Stretch.PhysicsEnabled) Stretch.ApplyGripForce(a);
                if (!a.joint && (jumpNow || !grounded && !a.support))
                {
                    float gravityMultiplier = jumpNow || a.body.linearVelocity.y > 0 ? baseline.freeRiseGravityMultiplier : baseline.freeFallGravityMultiplier;
                    a.body.AddForce(Physics.gravity * (gravityMultiplier - 1), ForceMode.Acceleration);
                }
                if (a.joint)
                    MaxGripError = Mathf.Max(MaxGripError, Vector3.Distance(head, Tank.transform.TransformPoint(a.anchor)));
                if (a.body.position.y < (route ? route.killHeight : -4))
                {
                    PlayerAction?.Invoke(i,"fall");
                    PlacePlayer(i, route ? route.RespawnPoint(i) : new Vector3(-3 + i * 0.7f, 0.05f, -3));
                    Message = "P" + (i + 1) + " 坠落后回当前检查点，鱼缸继续。";
                }
                plantedSupport |= a.grounded && (a.joint || a.support);
            }
            // All holders now have this tick's ground contact and takeoff state, independent of slot order.
            if (grips > 0 && !Broken && !Tank.isKinematic)
            {
                CarryJump.ApplyAirGravity(this, jumpingGrips, grips);
                if (jumpingGrips != 0 || carryingInFlight) carryJump.Apply(this, jumpingGrips);
            }
            if (carryingInFlight && movers == 0 && !Broken) CarryJump.BrakePlantedGrips(this);
            HeightBalance.Step(this,Time.fixedDeltaTime);
            if (unstable && plantedSupport && !Broken && !tankContact.HasSupport) Tank.AddTorque(tipTorque);
            if (Tank.position.y < (route ? route.killHeight - 4 : -8)) Fail("鱼缸离开测试区域");
        }

        bool ShouldAutoJump(Actor actor, Vector3 groundNormal, Vector3 footVelocity)
        {
            if (actor.move.sqrMagnitude < CarryInputDeadZoneSquared) return false;
            Vector3 heading = actor.move.normalized;
            float forwardSpeed = Vector3.Dot(footVelocity, heading);
            // Probe along the current ground plane so a walkable downslope doesn't look like a gap.
            heading.y = -Vector3.Dot(heading, groundNormal) / groundNormal.y;
            if (HasStepUpAhead(actor, heading, forwardSpeed)) return true;
            if (forwardSpeed <= .15f) return false;
            float lead = Mathf.Clamp(forwardSpeed * .065f, .10f, .20f);
            Vector3 feet = actor.body.position;
            return !HasGroundAhead(feet + heading * lead) &&
                   !HasGroundAhead(feet + heading * (lead + actor.capsule.radius));
        }

        bool HasStepUpAhead(Actor actor, Vector3 heading, float forwardSpeed)
        {
            const float minRise = .08f;
            float maxRise = baseline.freeJumpHeight - .05f;
            if (maxRise <= minRise) return false;

            Vector3 feet = actor.body.position;
            float reach = actor.capsule.radius + Mathf.Clamp(forwardSpeed * .15f, .12f, .35f);
            foreach (var riser in Physics.RaycastAll(feet + Vector3.up * minRise, heading.normalized,
                         reach * heading.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (riser.normal.y >= .55f || Vector3.Dot(riser.normal, heading) > -.5f ||
                    !IsEnvironment(riser.collider)) continue;
                // The obstacle must have a walkable top just beyond the riser, within jump height.
                Vector3 beyond = riser.point + heading * .10f - Vector3.up * minRise;
                var down = new Ray(beyond + Vector3.up * maxRise, Vector3.down);
                if (riser.collider.Raycast(down, out var tread, maxRise - minRise) && tread.normal.y >= .55f)
                    return true;
            }
            return false;
        }

        bool HasGroundAhead(Vector3 point)
        {
            const float probeHeight = .18f;
            const float maxStepDown = 1.05f;
            // A nearby lower tread is walkable ground even when it lies below the foot contact probe.
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * probeHeight, Vector3.down,
                         probeHeight + maxStepDown, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y >= .55f && IsEnvironment(hit.collider)) return true;
            return false;
        }

        bool ProbeGround(Actor actor, out Vector3 normal, out Vector3 velocity)
        {
            normal = Vector3.up;
            velocity = Vector3.zero;
            float radius = actor.capsule.radius * .85f;
            float nearest = float.PositiveInfinity;
            bool found = false;
            foreach (var hit in Physics.SphereCastAll(actor.body.position + Vector3.up * (radius+.10f), radius, Vector3.down, .20f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!IsEnvironment(hit.collider) || hit.normal.y < .55f || hit.distance >= nearest) continue;
                // A planted grip needs actual foot contact. Account for the smaller probe sphere on slopes.
                float gap = (hit.distance - .10f) * hit.normal.y - (actor.capsule.radius - radius) * (1 - hit.normal.y);
                if (actor.joint && gap > actor.capsule.contactOffset + hit.collider.contactOffset) continue;
                nearest = hit.distance; normal = hit.normal; found = true;
                velocity = hit.rigidbody ? hit.rigidbody.GetPointVelocity(hit.point) : Vector3.zero;
            }
            return found;
        }

        void LateUpdate()
        {
            if (!Tank) return;
            for (int i = 0; i < 4; i++)
            {
                var a = Players[i];
                if (a == null || !a.body || !a.active || !a.indicator) continue;
                a.indicator.GetComponent<Renderer>().sharedMaterial = a.joint || a.remoteGrip ? grip : Time.time < a.failedUntil ? rejected : a.support ? supporting : neutral;
            }
            if (!view) return;
            var focus = CameraTarget(1.25f);
            float previousY = cameraFocus.y;
            cameraFocus = Vector3.Lerp(cameraFocus, focus, 1 - Mathf.Exp(-Time.unscaledDeltaTime * Tuning.cameraFollow));
            if (baseline.controlFeelMode >= .5f)
                cameraFocus.y = Mathf.Lerp(previousY, focus.y, 1 - Mathf.Exp(-Time.unscaledDeltaTime * baseline.cameraVerticalFollow));
            var direction = Quaternion.Euler(pitch, yaw, 0) * Vector3.back;
            // Include the near-plane corners, so a clear centre ray cannot leave a wall over the image.
            float radius = Mathf.Max(.25f, view.nearClipPlane * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) * Mathf.Sqrt(1+view.aspect*view.aspect) + .04f);
            float allowed = Tuning.cameraDistance;
            foreach (var hit in Physics.SphereCastAll(cameraFocus, radius, direction, allowed, ~0, QueryTriggerInteraction.Ignore))
                if (IsEnvironment(hit.collider)) allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance-.06f));
            // Casts starting in geometry cannot report that surface. Fall back to the actual player pivot.
            foreach (var overlap in Physics.OverlapSphere(cameraFocus, radius, ~0, QueryTriggerInteraction.Ignore))
                if (IsEnvironment(overlap)) { cameraFocus = focus; allowed = 0; break; }
            cameraDistance = allowed < cameraDistance ? allowed : Mathf.MoveTowards(cameraDistance, allowed, Time.unscaledDeltaTime*Tuning.cameraReturn);
            view.transform.position = cameraFocus + direction * cameraDistance;
            view.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        public bool LastFailureWasDanger { get; private set; }
        public void Fail(string reason, bool dangerContact = false)
        {
            if (Broken || !Simulate || sessionPaused) return;
            Broken = true;
            LastFailureWasDanger = dangerContact;
            FailureCount++;
            FailedAt = Time.unscaledTime;
            SetSessionPaused(true);
            if (!ExternalControl) SetCaptured(false);
            failureFeedback.Show(Tank, tankSize, surfaceShader);
            Message = "鱼缸碎了，本次搬运失败。";
            TankFailed?.Invoke();
            Debug.Log("[M0] Tank failure: " + reason);
        }

        public bool RetryFailure()
        {
            if (ExternalControl || !FailureReady) return false;
            SetCaptured(false);
            ResetLab();
            return true;
        }

        public void SetDebugCapture(bool value)
        {
            captured = value && !Broken;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
        public bool LocalInputCaptured => captured;
        public void ReleaseLocalControl() => SetCaptured(false);

        public void ReportCompletion() => Message = "已抵达测试终点：完整鱼缸已送达。";
        public void ReportCheckpoint(string label) => Message = "检查点已激活：" + label;

        public void BeginLocalSimulation()
        {
            SetCaptured(false);
            DynamicRoster = false;
            ExternalControl = false;
            Testing = false;
            Simulate = true;
            SessionTeamSize = LocalPlayerCount;
            if (route) route.SelectCheckpoint("start");
            sessionPaused = false;
            Selected = 0;
            yaw = route ? route.startYaw : 0;
            pitch = 24;
            ResetLab();
            ChangeMode(1);
        }

        void SetCaptured(bool value)
        {
            value = value && !Broken;
            captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
            if (!value) for (int i = 0; i < LocalPlayerCount; i++) SetInput(i, Vector3.zero, false);
            else
            {
                // A click on the start button is not a gameplay grab edge.
                bool alreadyHeld = Mouse.current != null && Prefs.Held(ControlAction.Grab);
                for (int i = 0; i < LocalPlayerCount; i++) if (Players[i] != null && Players[i].active && Players[i].body) Players[i].held = alreadyHeld && (Mode != SharedGripMode || i == Selected);
            }
        }
        void OnApplicationFocus(bool focus) { if (!focus && Players[0] != null && !Testing) SetCaptured(false); }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        void OnDestroy() { foreach (var m in materials) Destroy(m); if (frictionless) Destroy(frictionless); if (font) Destroy(font); if (baseline && baseline != shippedBaseline) Destroy(baseline); }

        void Panel(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        void InitStyles()
        {
            if (titleStyle != null && titleStyle.font && titleStyle.fontSize == 25) return;
            titleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            textStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true, normal = { textColor = new Color(0.88f, 0.94f, 0.96f) } };
            smallStyle = new GUIStyle(textStyle) { fontSize = 14 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18 };
        }
        void OnGUI()
        {
            if (ExternalControl || !Tank || GMOpen) return;
            InitStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            Panel(new Rect(24, 22, 400, 87), new Color(0.035f, 0.065f, 0.09f, 0.94f));
            GUI.Label(new Rect(42, 32, 370, 35), route ? "搬鱼缸  /  白模路线试玩" : GetComponent<M1InteractionLab>() ? "搬鱼缸  /  M1 交互实验" : "搬鱼缸  /  M0 交互实验", titleStyle);
            GUI.Label(new Rect(42, 73, 370, 24), "本地模拟 · 基线 " + baseline.version + " · 米", smallStyle);
            float speedRowY = 86 + LocalPlayerCount * 30;
            Panel(new Rect(width - 278, 22, 254, speedRowY + 20), new Color(0.035f, 0.065f, 0.09f, 0.94f));
            string modeLabel = modes[Mode];
            GUI.Label(new Rect(width - 260, 36, 220, 26), "当前：P" + (Selected + 1) + "  " + modeLabel, smallStyle);
            for (int i = 0; i < LocalPlayerCount; i++)
            {
                var a = Players[i];
                if (a == null || !a.body || !a.active)
                {
                    GUI.Label(new Rect(width - 238, 74 + i * 30, 200, 27), "P" + (i + 1) + "  未加入", textStyle);
                    continue;
                }
                Color c = a.joint ? new Color(0.15f, 0.95f, 0.69f) : a.support ? new Color(1, 0.76f, 0.22f) : new Color(0.62f, 0.68f, 0.76f);
                if (Time.time < a.failedUntil && !a.joint) c = new Color(1, 0.3f, 0.28f);
                Panel(new Rect(width - 258, 78 + i * 30, 8, 18), c);
                string state = a.joint ? "已抓取" : Time.time < a.failedUntil ? "抓取失败" : a.support ? "仅头顶支撑" : "自由移动";
                GUI.Label(new Rect(width - 238, 74 + i * 30, 200, 27), "P" + (i + 1) + "  " + state + (i == Selected ? " · 你" : ""), textStyle);
            }
            if (!(Stretch && Stretch.Active && !Stretch.InLevel)) GUI.Label(new Rect(width - 330, 340, 310, 48), "测试传送：F4 当前角色\nShift + F4 全体 → 鱼缸附近", smallStyle);
            GUI.Label(new Rect(width - 260, speedRowY, 224, 26), "速度 " + Tank.linearVelocity.magnitude.ToString("F2") + " m/s  |  按 R 重置", smallStyle);
            Panel(new Rect(24, height - 116, width - 48, 94), new Color(0.035f, 0.065f, 0.09f, 0.94f));
            GUI.Label(new Rect(42, height - 108, width - 90, 29), Message, textStyle);
            string jumpHelp = Prefs.Binding(ControlAction.Jump) + " 跳跃";
            GUI.Label(new Rect(42, height - 74, width - 90, 24), (Stretch && Stretch.Active ? "WASD 移动   鼠标看向   Shift 奔跑   "+jumpHelp+"   "+Prefs.Binding(ControlAction.Shrink)+" 变矮 / "+Prefs.Binding(ControlAction.Grow)+" 长高   F2 伸缩参数" : "WASD 移动   鼠标看向   左键按住抓取 / 松开脱手   Shift 奔跑   "+jumpHelp+"   Ctrl 蹲下   Tab 换角色"), smallStyle);
            GUI.Label(new Rect(42, height - 49, width - 90, 24), (Stretch && Stretch.Active && !Stretch.InLevel ? "B0 单人沙盒 · 暂无鱼缸交互" : "1 单控 / 2 同向 / 3 两顺一逆 / 4 两侧反向 / " + (Digit5Bound ? "Shift+4" : "5") + " 队友原地抓取 / " + (Digit6Bound ? "Shift+3" : "6") + " 同向自动跳   Tab 换人") + "   R 重置   Esc 释放鼠标   Enter 开始操作", smallStyle);
            if (Broken && !Testing)
            {
                float x = width / 2 - 260, y = height / 2 - 90;
                Panel(new Rect(x, y, 520, 242), new Color(.055f,.045f,.06f,.97f));
                GUI.Label(new Rect(x+24,y+18,472,38), "鱼缸碎了", titleStyle);
                GUI.Label(new Rect(x+24,y+65,472,50), "本次搬运失败。\n当前检查点：" + (route ? route.CheckpointLabel : "起点"), textStyle);
                GUI.enabled = FailureReady;
                if (GUI.Button(new Rect(x+24,y+128,472,43), FailureReady ? "从检查点重试 / R" : "本次搬运结束…", buttonStyle)) RetryFailure();
                GUI.enabled = true;
                if (ReturnToLobbyRequested != null && GUI.Button(new Rect(x+24,y+185,472,36), "返回大厅", buttonStyle)) ReturnToLobbyRequested.Invoke();
            }
            else if (!captured && !Testing)
            {
                Panel(new Rect(width / 2 - 216, height / 2 - 112, 432, 204), new Color(0.035f, 0.065f, 0.09f, 0.97f));
                GUI.Label(new Rect(width / 2 - 190, height / 2 - 96, 380, 32), route && route.checkpoints.Length > 0 ? "先前往鱼缸位置" : "先试三个人一起向前", titleStyle);
                GUI.Label(new Rect(width / 2 - 190, height / 2 - 50, 380, 60), route && route.checkpoints.Length > 0 ? (route.useCheckpointZones ? "沿路线找到鱼缸，到缸下方按左键抓取。\n将完整鱼缸运到黄球附近即可存档，不用放下。" : "沿路线找到鱼缸，到缸下方按左键抓取。\n黄球位置是安全停车检查点。") : "长按左键抓住鱼缸，再按 W。\n切到 1 可单独测试抓取与支撑。", textStyle);
                if (GUI.Button(new Rect(width / 2 - 190, height / 2 + 23, 380, 46), "开始操作  /  Enter", buttonStyle)) SetCaptured(true);
            }
            if (Testing) GUI.Label(new Rect(450, 32, 400, 38), "自动检查运行中，请勿操作", titleStyle);
            GUI.matrix = old;
        }

        public void StartChecks(bool quit = false)
        {
            if (!Testing) StartCoroutine(M0CarryChecks.Run(this, quit));
        }
    }

    public sealed class M0Danger : MonoBehaviour { }
    public sealed class M0TankContact : MonoBehaviour
    {
        public M0CarryLab lab;
        readonly HashSet<Collider> supports = new HashSet<Collider>();
        public bool Touches(Collider surface) => supports.Contains(surface);
        internal void ClearSupports() => supports.Clear();
        public bool HasSupport
        {
            get
            {
                foreach (var c in supports) if (c && c.enabled && c.gameObject.activeInHierarchy) return true;
                return false;
            }
        }
        void OnCollisionEnter(Collision collision)
        {
            TrackSupport(collision);
        }
        void OnCollisionStay(Collision collision) => TrackSupport(collision);
        void OnCollisionExit(Collision collision) => supports.Remove(collision.collider);
        void TrackSupport(Collision collision)
        {
            var other = collision.collider;
            if (lab.Broken || !lab.IsEnvironment(other)) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (!TankSupportSurface.BreaksGlass(other, collision.GetContact(i).normal)) continue;
                lab.Fail(other.name, true);
                return;
            }
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y > .5f) { supports.Add(collision.collider); return; }
            supports.Remove(collision.collider);
        }
    }
}
