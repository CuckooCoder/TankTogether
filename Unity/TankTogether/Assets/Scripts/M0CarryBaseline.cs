using UnityEngine;

namespace TankTogether.M0
{
    [CreateAssetMenu(menuName = "Tank Together/Carry Baseline")]
    public sealed class M0CarryBaseline : ScriptableObject
    {
        public const string ResourceName = "CarryBaseline";
        public string version = "v0.2.2-r1";
        [Header("策划 V0.2.2：米，X长/Y高/Z宽")]
        public Vector3 tankSize = new Vector3(3.2f, 0.95f, 2.2f);
        public float standingHeight = 1.7f, playerDiameter = 0.6f, crouchingHeight = 1.1f;
        public Vector2 spawnHalfSpacing = new Vector2(0.9f, 0.55f);
        public float carryBottomHeight = 1.7f;
        public float walkSpeed = 4, sprintSpeed = 6, carrySpeed = 2.5f, carrySprintSpeed = 3.5f;
        public float freeJumpHeight = 0.65f, carryJumpHeightTarget = 0.55f;
        [Header("实验参数：非冻结玩法规则")]
        public Vector2 standInset = new Vector2(0.20f, 0.15f);
        public float standWidth = 0.25f, gripInset = 0.12f, gripVerticalReach = 0.23f;
        public float tankMass = 24, playerMass = 7, tankDamping = 0.8f, angularDamping = 2;
        public float motorGain = 95, motorForce = 160, carryMotorGain = 65, carryMotorForce = 70;
        public float carryBrakeAcceleration = 2.5f, carryJumpVelocity = 6;
        public float gripLimit = 0.12f, supportStiffness = 3500, supportDamping = 100, supportMaxForce = 240;
        [Header("人物伸缩：B / B0 实验")]
        public float stretchMinHeight = 1.1f;
        // Serialized default for older assets and their package signature. The gameplay ceiling is shared by M0CarryLab.
        public float stretchMaxHeight = 2.3f;
        public float stretchGrowSpeed = .3f, stretchShrinkSpeed = .3f;
        public float stretchGripStiffness = 3000, stretchGripDamping = 100, stretchGripMaxForce = 240;
        public float stretchCompliance = .08f;
        public float stretchBalanceMode=1, stretchBalanceStart=2.3f, stretchBalanceLever=.3f, stretchBalanceTorque=24;
        public float stretchBalanceLowDamping=2, stretchBalanceHighDamping=.9f;
        [Header("附加玩法参数：与 F1 GM 共用")]
        public CarryTuningExtras defaults = new CarryTuningExtras();
        [Header("平头支撑对照（实验）")]
        [Tooltip("0旧单点；1有限平头四点。只改变承托，皮肤和身体胶囊不变；抓握伸缩仍用原有限连接。")]
        public float headSupportMode = 1;
        [Tooltip("平头边长/角色直径。0.7对应0.42米方形，四角仍在0.6米身体圆形投影内。")]
        public float headSupportWidthRatio = .7f;
        [Tooltip("仅平头未抓握接触的被动摩擦上限/支撑力。默认0.15沿用原伸缩摩擦，不会锁死或主动吸附。")]
        public float headSupportFriction = .15f;
        [Header("自由走跳：仅未抓缸时，米/秒²与秒")]
        public float freeAcceleration = 40, freeDeceleration = 60, airAcceleration = 18;
        public float coyoteTime = .10f, jumpBufferTime = .12f;
        [Tooltip("空手且不在头顶支撑鱼缸时，下落阶段的重力倍率。1为原效果；增大落地更快、跳远略短，不改变上升和跳高。")]
        [Range(1,4)] public float freeFallGravityMultiplier = 2;
        [Header("3C 对照：0旧版 / 1候选版")]
        public float controlFeelMode = 0;
        public float airReleaseDeceleration = 8;
        public float cameraVerticalFollow = 18;
        public string Signature => version + "-" + Hash128.Compute(JsonUtility.ToJson(this) + "|" +
            JsonUtility.ToJson(Physics.gravity) + "|" + Mathf.RoundToInt(Time.fixedDeltaTime * 1000000)).ToString();
    }
}
