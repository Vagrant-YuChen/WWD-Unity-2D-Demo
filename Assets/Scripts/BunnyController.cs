using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
public class BunnyController : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 移动核心配置 ===")]
    [Tooltip("基础移动速度")]
    [SerializeField] protected float moveSpeed = 5f;
    [Tooltip("冲刺速度倍率")]
    [SerializeField] protected float sprintMultiplier = 1.8f;

    [Header("=== 冲刺(Shift 加速)场景限制 ===")]
    [Tooltip("允许 Shift 冲刺的场景名列表（区分大小写不敏感）。只有当前场景在此列表中才可冲刺；" +
             "例如默认只在第二关 GameScene2 可用，第一关 GameScene1 不在列表中，因此不能加速。留空则所有场景都可用。")]
    [SerializeField] protected string[] sprintEnabledScenes = { "GameScene2" };
    // 当前场景是否允许冲刺，Awake 时根据场景名计算一次
    private bool sprintAllowedInScene = true;
    [Tooltip("地面摩擦力（越大停得越快）")]
    [SerializeField] protected float groundFriction = 20f;
    [Tooltip("空中惯性（0=无惯性，1=自然）")]
    [SerializeField] protected float airInertia = 0.8f;
    [Tooltip("最大水平/垂直速度限制")]
    [SerializeField] protected Vector2 maxSpeed = new Vector2(8f, 15f);

    [Header("=== 输入响应曲线 ===")]
    [Tooltip("是否读取模拟量输入（手柄摇杆等）。关闭时使用 GetAxisRaw 的数字输入，键盘手感更干脆")]
    [SerializeField] protected bool useAnalogInput = false;
    [Tooltip("模拟量修正曲线（横轴 = 输入强度 0~1，纵轴 = 目标速度缩放）。把原始输入映射成更细腻的推杆力度；数字输入时通常保持线性即可")]
    [SerializeField] protected AnimationCurve analogueModifier = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("响应时间曲线（横轴 = 同向按键持续秒数，纵轴 = 加速度系数 0~1）。起步时系数较小、长按后升到 1，避免瞬间满速，让起步有重量感")]
    [SerializeField] protected AnimationCurve responseTimeModifier = new AnimationCurve(
        new Keyframe(0f, 0.25f),
        new Keyframe(0.6f, 1f));

    [Header("=== 大炮发射飞行配置 ===")]
    [Tooltip("发射飞行时使用的重力缩放（由大炮传入；决定抛物线弧度）")]
    [SerializeField] protected float launchGravityFallback = 1.2f;

    [Header("=== 跳跃核心配置 ===")]
    [Tooltip("基础跳跃力度")]
    [SerializeField] protected float jumpForce = 7f;
    [Tooltip("空跳次数：仅在「不是靠起跳进入空中」时可用（如走落/从高处坠落、掉下平台）。1 = 每次腾空可空跳一次。\n注意：从地面/Coyote 主动起跳会耗尽本次腾空的空跳预算，即「平地起跳后不能再空跳」。默认 1 启用该玩法")]
    [SerializeField, Min(0)] protected int maxAirJumps = 1;
    [Tooltip("空中跳跃冷却（秒）：一次空中跳后短时间内不再触发，避免同一次按键被重复判定为多次空中跳；0 = 不启用")]
    [SerializeField, Min(0f)] protected float airJumpGraceTime = 0.05f;
    [Tooltip("Coyote Time：离地后仍可跳的时间")]
    [SerializeField] protected float jumpGraceTime = 0.1f;
    [Tooltip("Jump Buffer：按下后多久内落地仍可跳")]
    [SerializeField] protected float jumpBufferTime = 0.1f;
    [Tooltip("可变跳跃高度：提前松开跳跃键时，上升速度乘以此系数（0~1，越小跳得越矮）")]
    [SerializeField, Range(0.1f, 1f)] protected float variableJumpMultiplier = 0.5f;
    [Tooltip("地面重力缩放")]
    [SerializeField] protected float groundGravity = 1f;
    [Tooltip("空中重力缩放（旧版一刀切；仅当重力曲线未配置时的回退值）")]
    [SerializeField] protected float airGravity = 1.2f;

    [Header("=== 跳跃弧度（重力顶点过渡）===")]
    [Tooltip("起跳初期的低重力系数：上升阶段维持这个较轻的重力，让弧线更「软」更有滞空感（1 = 与全重力相同）")]
    [SerializeField] protected float loGravityMod = 0.5f;
    [Tooltip("上升速度低于此值时判定为接近顶点，退出低重力并开始向全重力过渡")]
    [SerializeField] protected float gravityVelocityPeak = 1.5f;
    [Tooltip("重力过渡时长曲线（横轴 = 起跳后时间，纵轴 = 过渡到全重力所需秒数）。越过顶点后 gravityTransitionTime 沿此曲线 MoveTowards，实现渐进加重")]
    [SerializeField] protected AnimationCurve jumpCapTransitionTime = AnimationCurve.Constant(0f, 5f, 0.25f);
    [Tooltip("全重力曲线（横轴 = 竖直速度 vy，纵轴 = 重力缩放）。下落越快越重、顶点附近较轻，塑造软起软落的手感")]
    [SerializeField] protected AnimationCurve gravityCurve = new AnimationCurve(
        new Keyframe(-16f, 2f),
        new Keyframe(0f, 1.2f),
        new Keyframe(16f, 1f));

    [Header("=== 检测配置 ===")]
    [Tooltip("地面检测中心点（绑定脚底空物体）")]
    [SerializeField] protected Transform groundCheckPoint;
    [Tooltip("地面检测左点")]
    [SerializeField] protected Transform groundCheckPointLeft;
    [Tooltip("地面检测右点")]
    [SerializeField] protected Transform groundCheckPointRight;
    [Tooltip("地面检测半径")]
    [SerializeField] protected float groundCheckRadius = 0.2f;
    [Tooltip("地面层")]
    [SerializeField] protected LayerMask groundLayer;

    [Header("=== 台阶容错配置 ===")]
    [Tooltip("高于脚底多少以内、且可越过的小台阶/接缝会自动抬脚跨过")]
    [SerializeField] protected float stepAssistHeight = 0.08f;
    [Tooltip("每次抬脚上移的世界距离")]
    [SerializeField] protected float stepAssistLift = 0.02f;
    [Tooltip("抬脚后的冷却（物理帧）")]
    [SerializeField] protected int stepAssistCooldownFrames = 8;
    [Tooltip("触发抬脚所需的最少阻挡帧数")]
    [SerializeField] protected int stepAssistTriggerFrames = 3;

    [Header("=== 死亡复活配置 ===")]
    [Tooltip("死亡后复活倒计时")]
    [SerializeField] protected float respawnDelay = 3f;
    [Tooltip("死亡语音（可选）。所有致死方式（滚石 / 弩箭 / 流沙 / 摆锤 / 消防栓 / 火焰 / 蜂群 / 刀片等）共用这一个片段，" +
             "统一由角色控制器在死亡瞬间播放，保证同一声、同音量、2D 不随镜头变化")]
    [SerializeField] protected AudioClip deathVoiceClip;
    [Tooltip("死亡语音音量（固定，不随距离或镜头变化）")]
    [SerializeField, Range(0f, 1f)] protected float deathVoiceVolume = 1f;

    [Header("=== 动画/落地配置 ===")]
    [Tooltip("判断跳跃顶点的速度阈值")]
    [SerializeField] protected float jumpTopThreshold = 0.01f;
    [Tooltip("判断下落的速度阈值")]
    [SerializeField] protected float jumpDownThreshold = -0.01f;
    [Tooltip("判定为「上升/起跳」所需的最小竖直速度。达不到此速度的正向 vy（斜坡接触回弹、贴地小抖动）不再被当成跳跃")]
    [SerializeField] protected float jumpRiseSpeedThreshold = 1f;
    [Tooltip("落地动画保持时间（秒）")]
    [SerializeField] protected float landStateResetDelay = 0.15f;

    [Header("=== 落地烟尘特效 ===")]
    [Tooltip("落地烟尘预制体（可留空：留空时按下方控制器在运行时临时生成）")]
    [SerializeField] protected GameObject landingDustPrefab;
    [Tooltip("落地烟尘使用的动画控制器；一般指定 JumpSmoke.controller（预制体为空时使用）")]
    [SerializeField] protected RuntimeAnimatorController landingDustController;
    [Tooltip("下落速度绝对值超过此阈值才生成烟尘，避免小跳/走下缓坡也冒烟（正数，单位/秒）")]
    [SerializeField, Min(0f)] protected float minSmokeVelocity = 2f;
    [Tooltip("烟尘对象存活时间（秒），超时自动销毁")]
    [SerializeField, Min(0.01f)] protected float landingDustLifetime = 0.5f;
    [Tooltip("烟尘生成位置相对脚底的横向/纵向偏移")]
    [SerializeField] protected Vector2 landingDustOffset = Vector2.zero;
    [Tooltip("烟尘相对角色的缩放")]
    [SerializeField, Min(0.01f)] protected float landingDustScale = 1f;
    [Tooltip("烟尘在 SpriteRenderer 基础上叠加的渲染层级（越大越靠前）")]
    [SerializeField] protected int landingDustSortingOrderOffset = 1;

    [Header("=== 调试 ===")]
    [SerializeField] protected bool debugLogs = false;
    #endregion

    #region 私有状态
    protected Animator anim;
    protected Rigidbody2D rb;
    protected SpriteRenderer spriteRenderer;
    private AudioSource deathVoiceSource;

    // 角色音效（跳跃 / 落地 / 移动循环）
    private AudioSource playerSfxSource;    // 跳跃、落地（PlayOneShot）
    private AudioSource playerMoveSource;   // 移动循环（loop）
    private bool moveSoundOn;               // 逻辑上是否应播放移动音效
    private float moveVolumeCurrent;        // 当前淡入淡出音量
    private bool landSoundPrimed;           // 出生后的首次触地不发声，之后每次"离地->着地"都播
    private static PlayerSfxSettings cachedPlayerSfx;
    private static bool cachedPlayerSfxLoaded;

    // 输入
    private float horizontalInput;
    private bool isSprinting;
    private float horizontalInputHoldTime;
    private int horizontalInputDirection;

    // 地面 / 计时器
    private bool isGrounded;
    private bool wasGroundedLastFrame;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool isJustLanded;
    private float landTimer;
    private bool springBounceActive; // 弹簧弹起时允许暂时超过 maxSpeed.y
    private bool launchActive;       // 大炮发射飞行中：不夹取速度、不做空中操控
    private bool windControlActive;  // 风扇上升中：水平操控交给 PropellerWind
    private float activeLaunchGravity = 1.2f; // 本次发射使用的重力缩放
    private bool wasOnMovingPlatform;          // 上一物理帧是否站在运动平台(摩天轮)上
    private Vector2 lastPlatformWorldPos;      // 上一物理帧所站平台的World位置(用于按位移携带)
    private Rigidbody2D standingPlatformBody;   // 当前脚底所踩的 Kinematic 运动平台刚体
    private Collider2D standingPlatformCollider; // 该平台的碰撞体(用于水平范围/顶面判断)
    private float feetLocalOffsetY;             // 脚底(碰撞体下沿)相对刚体中心的Y偏移(负值，Awake缓存)
    // 骑乘状态用“小范围邻近”来保持（桥接平台下降时接触丢失的那一两帧），
    // 但首次站上去必须靠真实接触——因此不会像几何吸附那样在空中被"吸"到平台、也不会产生悬空。
    private const float platformRideGapDown = 0.25f; // 继续骑乘时允许的最大穿透
    private const float platformRideGapUp = 0.20f;   // 继续骑乘时允许的最大离缝(应对帧率抖动/瞬间接触丢失)
    private bool launchAirborne;     // 发射后是否已经离地（用于准确判定落地瞬间）
    private bool risingFromJump;     // 真正的起跳/弹簧/发射造成上升中，此期间不能靠接触法线判着地

    // 可变跳跃高度：每次起跳后只削减一次上升速度
    private bool jumpCutApplied;
    private bool jumpCutEligible;    // 仅玩家主动起跳的可变高度生效；弹簧/大炮/风力不削减

    // 跳跃阶段
    private int jumpState;   // 0=地面 1=上升 2=顶点 3=下落 4=即将落地
    private int lastJumpState;

    // 二段跳 / 空中跳
    private int airJumpsUsed;          // 本次腾空已消耗的空中跳次数
    private float airJumpGraceTimer;   // 空中跳冷却计时（<=0 才允许下一次空中跳）

    // 记录本段腾空期间达到的最大下落速度（负值），落地时据此判断是否触发烟尘
    private float lastFallSpeed;

    // 落地音效：追踪本次腾空最高点，用"最高点 - 落地点"的真实下落距离决定是否发声
    private float airbornePeakY;
    private bool airborneFallTracking;

    // 跳跃弧度（重力顶点过渡）
    private float gravityTransitionTime; // 已过渡的重力时间（越接近目标越接近全重力）
    private float timeSinceJump;         // 起跳后经过的时间
    private bool loGravity;              // 上升低重力阶段
    private bool inJumpArc;              // 本次腾空是否由跳跃/弹簧引起（走下平台不参与低重力过渡）

    // 死亡 / 复活
    public bool isDead;
    private bool isRespawning;
    private float respawnTimer;
    private Vector3 initialPosition;

    private const float LandedSpeedThresholdDefault = -0.5f;

    #region 对外只读状态（镜头 / UI 等外部系统用）
    /// <summary>是否处于死亡状态。</summary>
    public bool IsDeadState => isDead;

    /// <summary>是否处于大炮发射飞行中。</summary>
    public bool IsLaunched => launchActive;

    /// <summary>是否处于风扇风力上升中。</summary>
    public bool IsInWindControl => windControlActive;

    /// <summary>当前速度（未初始化时为零）。</summary>
    public Vector2 Velocity => rb != null ? rb.velocity : Vector2.zero;

    /// <summary>当前是否着地。</summary>
    public bool IsGroundedState => isGrounded;
    #endregion

    // 台阶容错运行时状态
    protected Collider2D bodyCollider;
    private int stepBlockedFrames;
    private int stepCooldown;
    private static readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[16];
    #endregion

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        if (anim == null) Debug.LogError($"[{name}] 缺少 Animator 组件！");

        // 玩家自身碰撞体设为低摩擦，避免脚部多边形顶点被坑洼表面的摩擦"粘住"
        if (bodyCollider != null)
        {
            var noFriction = new PhysicsMaterial2D("BunnyNoFriction") { friction = 0f, bounciness = 0f };
            bodyCollider.sharedMaterial = noFriction;

            // 缓存脚底相对刚体中心的偏移（碰撞体不旋转，恒定），用于吸附到平台顶面
            feetLocalOffsetY = bodyCollider.bounds.min.y - transform.position.y;
        }

        rb.gravityScale = groundGravity;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        initialPosition = transform.position;
        jumpState = 0;
        lastJumpState = 0;
        isDead = false;
        isRespawning = false;

        sprintAllowedInScene = ResolveSprintAllowedInScene();

        OnCharacterConfigured();

        InitPlayerSfx();
    }

    /// <summary>
    /// 根据当前场景名判断是否允许 Shift 冲刺。
    /// 列表为空时视为全部场景可用（保持旧行为）；否则仅当场景名在列表内才允许。
    /// </summary>
    private bool ResolveSprintAllowedInScene()
    {
        if (sprintEnabledScenes == null || sprintEnabledScenes.Length == 0) return true;

        string current = SceneManager.GetActiveScene().name;
        for (int i = 0; i < sprintEnabledScenes.Length; i++)
        {
            if (string.Equals(sprintEnabledScenes[i], current, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    #region 子类扩展点（供 SheepController 等做模型适配）
    /// <summary>脚底检测中心点的世界坐标；未绑定时回退到自身位置。</summary>
    protected virtual Vector3 GroundCheckCenterPosition =>
        groundCheckPoint != null ? groundCheckPoint.position : transform.position;

    /// <summary>脚底检测左点的世界坐标。</summary>
    protected virtual Vector3 GroundCheckLeftPosition =>
        groundCheckPointLeft != null ? groundCheckPointLeft.position : GroundCheckCenterPosition;

    /// <summary>脚底检测右点的世界坐标。</summary>
    protected virtual Vector3 GroundCheckRightPosition =>
        groundCheckPointRight != null ? groundCheckPointRight.position : GroundCheckCenterPosition;

    protected virtual float GroundCheckRadius => groundCheckRadius;

    /// <summary>
    /// "即将落地"预判用的检测半径。默认比着地检测更大，保证下落动画能提前切到落地。
    /// </summary>
    protected virtual float LandingProbeRadius => GroundCheckRadius * 1.5f;

    protected virtual float LandStateResetDelay => landStateResetDelay;

    /// <summary>触发落地动画所需的最低下落速度（负值）。</summary>
    protected virtual float LandedSpeedThreshold => LandedSpeedThresholdDefault;

    /// <summary>落地烟尘特效的最小下落速度阈值（正数，单位/秒）。</summary>
    protected virtual float MinSmokeVelocity => minSmokeVelocity;

    protected virtual float StepAssistHeight => stepAssistHeight;
    protected virtual float StepAssistLift => stepAssistLift;
    protected virtual int StepAssistTriggerFrames => Mathf.Max(1, stepAssistTriggerFrames);
    protected virtual int StepAssistCooldownFrames => Mathf.Max(1, stepAssistCooldownFrames);

    /// <summary>Awake 末尾调用，子类在这里按模型做一次性适配。</summary>
    protected virtual void OnCharacterConfigured() { }
    #endregion

    private void Update()
    {
        // 死亡状态下仅处理复活逻辑（含外部误设 isDead 的兜底）
        if (isDead)
        {
            if (!isRespawning)
            {
                isRespawning = true;
                respawnTimer = respawnDelay;
                rb.isKinematic = true;
                rb.velocity = Vector2.zero;
            }
            StopMoveSoundImmediate();
            HandleRespawn();
            return;
        }

        UpdateInput();
        UpdateJumpBuffer();
        if (airJumpGraceTimer > 0f)
        {
            airJumpGraceTimer = Mathf.Max(0f, airJumpGraceTimer - Time.deltaTime);
        }

        // 跳跃触发（Coyote + Buffer + 空中跳）
        if (jumpBufferTimer > 0f && CanJump())
        {
            Jump();
        }

        UpdateJumpState();
        ApplyVariableJumpHeight();
        UpdateLandedState();
        UpdateAnimatorParams();
        UpdateMoveSound();
    }

    private void FixedUpdate()
    {
        if (isDead)
        {
            rb.velocity = Vector2.zero;
            return;
        }

        // Record the strongest downward speed before ground detection changes the state.
        lastFallSpeed = Mathf.Min(lastFallSpeed, rb.velocity.y);
        UpdateGroundDetection();
        UpdateCoyoteTimer();

        // 发射飞行中强制使用发射重力，保证一定是抛物线，且不受地面/空中状态影响
        if (launchActive)
        {
            rb.gravityScale = activeLaunchGravity;
        }
        else if (windControlActive)
        {
            // 风扇上升保持原有行为
            rb.gravityScale = airGravity;
        }
        else if (isGrounded)
        {
            rb.gravityScale = groundGravity;
            timeSinceJump = 0f;
            gravityTransitionTime = 0f;
            inJumpArc = false;
            loGravity = false;
        }
        else
        {
            timeSinceJump += Time.fixedDeltaTime;
            // 上升阶段维持低重力；越过顶点（vy 回落到阈值以下）后开始渐进加重
            if (inJumpArc && loGravity && rb.velocity.y <= gravityVelocityPeak)
            {
                loGravity = false;
            }
            rb.gravityScale = ComputeAirGravityScale();
        }

        MoveLogic();
        TryStepAssist();
        ClampVelocity();
    }

    /// <summary>
    /// 弯台阶/接缝容错：在地面推着走但被一个"近竖直、高度在脚底小范围内"的
    /// 碰撞体面卡住时，轻微向上抬一下角色，帮助脚部多边形顶点越过去。
    /// 仅在有明确输入、着地、且水平被挡时触发，避免影响正常物理。
    /// </summary>
    private void TryStepAssist()
    {
        if (stepCooldown > 0)
        {
            stepCooldown--;
        }

        bool pushing = Mathf.Abs(horizontalInput) > 0.1f;
        bool horizontallyBlocked = pushing && isGrounded && Mathf.Abs(rb.velocity.x - GetPlatformVelocity().x) < 0.2f;

        if (!horizontallyBlocked || stepCooldown > 0)
        {
            stepBlockedFrames = 0;
            return;
        }

        // 检测脚下阻挡是否为可越过的低矮竖直齿
        if (!TryFindStepToClimb())
        {
            stepBlockedFrames = 0;
            return;
        }

        stepBlockedFrames++;
        if (stepBlockedFrames < StepAssistTriggerFrames)
        {
            return;
        }

        // 抬脚：上移 + 给顶点一次越过的机会（用 rb.position 在物理步内移动更稳）
        rb.position = rb.position + Vector2.up * StepAssistLift;
        Vector2 pv = GetPlatformVelocity();
        rb.velocity = new Vector2(pv.x + horizontalInput * Mathf.Max(0.5f, Mathf.Abs(rb.velocity.x - pv.x)), rb.velocity.y);
        stepCooldown = StepAssistCooldownFrames;
        stepBlockedFrames = 0;
    }

    private bool TryFindStepToClimb()
    {
        if (bodyCollider == null) return false;

        int n = rb.GetContacts(contactBuffer);
        if (n > contactBuffer.Length) n = contactBuffer.Length;

        Bounds b = bodyCollider.bounds;
        float footY = b.min.y;
        float pushSign = Mathf.Sign(horizontalInput);

        for (int i = 0; i < n; i++)
        {
            ContactPoint2D cp = contactBuffer[i];

            // 近竖直面（像墙/齿），且法线朝向与前进方向相反 -> 是正面阻挡
            bool nearVertical = Mathf.Abs(cp.normal.y) < 0.5f;
            if (!nearVertical) continue;
            if (Mathf.Sign(cp.normal.x) != -pushSign) continue;

            // 该接触点必须接近脚底高度，且不超过可跨越的台阶高度
            float heightAboveFeet = cp.point.y - footY;
            if (heightAboveFeet < -0.02f || heightAboveFeet > StepAssistHeight) continue;

            // 关键区分：真正的墙在"台阶高度以上"仍会挡住；低矮台阶/齿则不会。
            // 在脚底上方 stepAssistHeight 处朝前进方向短射线，若命中说明是墙，跳过。
            Vector2 probeOrigin = new Vector2(b.center.x, footY + StepAssistHeight + 0.03f);
            float probeDist = b.extents.x + 0.15f;
            if (Physics2D.Raycast(probeOrigin, new Vector2(pushSign, 0f), probeDist, groundLayer).collider != null)
            {
                continue;
            }

            // 头顶也要有空间，避免在低矮天花板下强行抬脚
            if (!HasHeadroom()) continue;

            return true;
        }

        return false;
    }

    private bool HasHeadroom()
    {
        if (bodyCollider == null) return true;
        Bounds b = bodyCollider.bounds;
        Vector2 origin = new Vector2(b.center.x, b.max.y);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.up, StepAssistLift * 2f + 0.02f, groundLayer);
        return hit.collider == null;
    }

    #region 输入
    private void UpdateInput()
    {
        // 数字输入用 GetAxisRaw 保持 -1/0/1 的干脆手感；模拟输入用 GetAxis 取带渐变的摇杆幅度
        horizontalInput = useAnalogInput
            ? Input.GetAxis("Horizontal")
            : Input.GetAxisRaw("Horizontal");

        // 仅当当前场景允许冲刺时才响应 Shift，第一关（GameScene1）无法加速
        isSprinting = sprintAllowedInScene
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        // 记录同向按住的持续时间（供 ResponseTimeModifier 做加速度渐进）。
        // 与 Character.cs 的 left/rightResponseTime 行为一致：同向持续按住累加，
        // 松开时保留已累积时长（快速点按不会每次都从零起步），换向时重新归零。
        int dir = 0;
        if (horizontalInput < -0.1f) dir = -1;
        else if (horizontalInput > 0.1f) dir = 1;

        if (dir != 0)
        {
            if (dir != horizontalInputDirection)
            {
                horizontalInputDirection = dir;
                horizontalInputHoldTime = 0f;
            }
            else
            {
                horizontalInputHoldTime += Time.deltaTime;
            }
        }
    }

    private void UpdateJumpBuffer()
    {
        // 暂停 / 冻结时（timeScale = 0，如开场弹窗、暂停菜单）不接收跳跃输入，
        // 并清空残留缓冲：否则 Update 仍在跑而 deltaTime 为 0，缓冲不会衰减，
        // 恢复后会导致角色自动跳一次。
        //
        // 另外：如果这一帧的空格被用于关闭开场弹窗（SpaceResumeFrame），也一并忽略，
        // 避免"用空格开始游戏"在开局瞬间触发一次跳跃。
        if (Time.timeScale <= 0f ||
            RunningGame.UI.IntroPopupController.SpaceResumeFrame == Time.frameCount)
        {
            jumpBufferTimer = 0f;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpBufferTimer = jumpBufferTime;
        }
        jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.deltaTime);
    }
    #endregion

    #region 地面检测 / Coyote
    private void UpdateGroundDetection()
    {
        // 空引用兜底：缺失的检测点回退到中心点
        Vector3 center = GroundCheckCenterPosition;
        Vector3 left   = GroundCheckLeftPosition;
        Vector3 right  = GroundCheckRightPosition;

        bool c = Physics2D.OverlapCircle(center, GroundCheckRadius, groundLayer);
        bool l = Physics2D.OverlapCircle(left,   GroundCheckRadius, groundLayer);
        bool r = Physics2D.OverlapCircle(right,  GroundCheckRadius, groundLayer);

        // 固定三点在凸角/圆弧上会漏检：补充"接触法线朝上"的判定。
        // 只要身体被一个朝上的支撑面托住，就视为着地。
        // 唯独真正起跳/弹簧/发射的上升阶段例外，否则会被自己起跳的接触误判成着地。
        // （原实现用 vy < 0.5 一刀切，斜坡上坡时 vy 本来就超过 0.5，会让着地判定在斜坡上反复闪烁）
        bool risingFromRealJump = risingFromJump && rb.velocity.y > jumpRiseSpeedThreshold;
        bool supportedByContact = !c && !l && !r
            && !risingFromRealJump
            && HasUpwardSupport();

        bool contactGrounded = c || l || r || supportedByContact;

        // 找出脚底附近/正下方的运动平台，用于判断是否仍在骑乘。
        Rigidbody2D platBody = null;
        Collider2D platCol = null;
        bool platformNearby = !windControlActive && !risingFromRealJump
            && TryGetStandingPlatform(out platBody, out platCol);

        // 骑乘判定：
        //  - 首次踏上必须靠真实接触（落到平台上）才吸附，避免在空中被几何"吸"上去；
        //  - 已在平台上时，只要平台仍在很小范围内邻近（桥接平台下降时接触丢掉的瞬间）就保持骑乘，
        //    这样按位移携带不会掉帧，也就不会出现"静止却滑动/上下抖动"。
        bool ridingPlatform = false;
        if (platformNearby)
        {
            if (wasOnMovingPlatform) ridingPlatform = true;
            else if (contactGrounded) ridingPlatform = true;
        }

        standingPlatformBody = ridingPlatform ? platBody : null;
        standingPlatformCollider = ridingPlatform ? platCol : null;

        bool newGrounded = contactGrounded || ridingPlatform;

        // 起跳上升阶段强制视为空中：避免屋顶等复杂碰撞体在起跳后仍被 OverlapCircle 误判为着地
        if (risingFromJump && rb.velocity.y > jumpRiseSpeedThreshold) newGrounded = false;

        // 风道上升期间强制视为空中，避免站在风扇碰撞体上播放地面/落地动画
        if (windControlActive) newGrounded = false;

        if (newGrounded || rb.velocity.y <= jumpRiseSpeedThreshold)
        {
            risingFromJump = false;
        }

        // 追踪本次腾空达到的最高点（用于落地音效按真实下落距离判定）
        if (!newGrounded)
        {
            if (!airborneFallTracking)
            {
                airborneFallTracking = true;
                airbornePeakY = rb.position.y;
            }
            else if (rb.position.y > airbornePeakY)
            {
                airbornePeakY = rb.position.y;
            }
        }

        // 仅在"上一帧空中 -> 本帧落地"瞬间置位，避免重复触发
        if (newGrounded && !wasGroundedLastFrame &&
            (rb.velocity.y < LandedSpeedThreshold || lastFallSpeed < -MinSmokeVelocity))
        {
            isJustLanded = true;
            landTimer = LandStateResetDelay;
            if (lastFallSpeed < -MinSmokeVelocity)
            {
                SpawnLandingDust(lastFallSpeed);
            }
            lastFallSpeed = 0f;
        }

        // 角色落地音效：只有本次腾空"最高点 -> 落地点"的下落距离超过阈值才播放，
        // 避免轻微下台阶 / 贴地抖动频繁触发；出生后的第一次触地不发声。
        if (newGrounded && !wasGroundedLastFrame)
        {
            float fallDistance = airborneFallTracking ? airbornePeakY - rb.position.y : 0f;
            airborneFallTracking = false;

            if (landSoundPrimed && fallDistance > LandMinFallDistance)
            {
                PlayLandSound();
            }
        }

        if (!newGrounded && wasGroundedLastFrame)
        {
            lastFallSpeed = 0f;
        }

        // 落地即补满空中跳次数，并清掉冷却，保证每次腾空都能重新使用
        if (newGrounded)
        {
            airJumpsUsed = 0;
            airJumpGraceTimer = 0f;
        }

        wasGroundedLastFrame = isGrounded;
        isGrounded = newGrounded;

        // 完成一帧着地检测后启用落地音效：出生瞬间的首次触地不响，之后任何"离地->着地"都响
        landSoundPrimed = true;
    }

    /// <summary>
    /// 检查静态刚体接触中是否有"朝上的支撑面"（normal.y 足够大），
    /// 用于凸角/圆弧等固定检测点会漏检的着地情况。
    /// </summary>
    private bool HasUpwardSupport()
    {
        int n = rb.GetContacts(contactBuffer);
        if (n > contactBuffer.Length) n = contactBuffer.Length;

        for (int i = 0; i < n; i++)
        {
            ContactPoint2D cp = contactBuffer[i];
            if (cp.normal.y > 0.4f)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateCoyoteTimer()
    {
        if (isGrounded)
        {
            coyoteTimer = jumpGraceTime;
        }
        else
        {
            coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
        }
    }
    #endregion

    #region 移动
    private void MoveLogic()
    {
        // 大炮发射飞行中：保持朝向，不做水平操控（落地定住后自动恢复控制）
        if (launchActive)
        {
            ApplyFacing();
            return;
        }

        // 风扇上升中：水平速度由 PropellerWind 直接控制，这里只保留朝向
        if (windControlActive)
        {
            ApplyFacing();
            return;
        }

        // 模拟量修正：把输入幅度 0~1 映射成速度缩放，支持手柄摇杆的细腻推杆
        float inputMagnitude = Mathf.Clamp01(Mathf.Abs(horizontalInput));
        float analogueScale = EvaluateCurve(analogueModifier, inputMagnitude, 1f);

        // 响应时间修正：同向按住越久，加速度系数越接近 1；起步/换向时系数较低，避免瞬间满速
        float responseFactor = EvaluateCurve(responseTimeModifier, horizontalInputHoldTime, 1f);

        float baseSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        float targetSpeed = horizontalInput * analogueScale * baseSpeed;

        float vx = rb.velocity.x;

        if (isGrounded)
        {
            // 站在旋转的摩天轮平台上：按平台的世界位移把角色带走（水平+垂直），与平台同步平移。
            // 用干净的位移差 carrying（而不是 Kinematic 刚体的 velocity，它被 transform 驱动噪声大），
            // 并且 carrying 每一物理帧都执行——由 UpdateGroundDetection 的骑乘锁存保证不会被接触闪烁打断。
            // 不做任何"吸附/压入平台"，避免宽于平台的兔子与平台边角互相挤压而被横推。
            if (standingPlatformBody != null && standingPlatformCollider != null && standingPlatformCollider.enabled)
            {
                Vector2 platformPos = standingPlatformBody.position;
                if (!wasOnMovingPlatform)
                {
                    // 首次踏上：只记录基准，不携带，避免落地瞬间瞬移
                    lastPlatformWorldPos = platformPos;
                }
                else
                {
                    rb.position = rb.position + (platformPos - lastPlatformWorldPos);
                }
                lastPlatformWorldPos = platformPos;
                wasOnMovingPlatform = true;

                // 关闭重力并清零竖直速度：垂直方向跟随完全由上面的位移携带负责，避免物理积分抖动。
                rb.gravityScale = 0f;
                rb.velocity = new Vector2(rb.velocity.x, 0f);

                // 水平：摩擦力作用于相对速度（相对平台），目标是纯输入速度
                float speedDiff = targetSpeed - rb.velocity.x;
                float moveForce = Mathf.Pow(Mathf.Abs(speedDiff) * groundFriction, 0.9f) * Mathf.Sign(speedDiff) * responseFactor;
                rb.AddForce(Vector2.right * moveForce, ForceMode2D.Force);
            }
            else
            {
                wasOnMovingPlatform = false;
                float speedDiff = targetSpeed - vx;
                float moveForce = Mathf.Pow(Mathf.Abs(speedDiff) * groundFriction, 0.9f) * Mathf.Sign(speedDiff) * responseFactor;
                rb.AddForce(Vector2.right * moveForce, ForceMode2D.Force);
            }
        }
        else
        {
            wasOnMovingPlatform = false;
            float airControl = airInertia * (isSprinting ? 1.2f : 1f);
            float airSpeedDiff = (targetSpeed - vx) * airControl * responseFactor;
            rb.velocity = new Vector2(vx + airSpeedDiff * Time.fixedDeltaTime, rb.velocity.y);
        }

        ApplyFacing();
    }

    /// <summary>
    /// 安全地求值一条曲线；曲线未配置（null 或空）时返回 fallback。
    /// </summary>
    private static float EvaluateCurve(AnimationCurve curve, float time, float fallback)
    {
        return (curve != null && curve.length > 0) ? curve.Evaluate(time) : fallback;
    }

    private static readonly Collider2D[] platformProbeHits = new Collider2D[16];

    /// <summary>
    /// 几何判定：角色脚底附近是否存在被脚本驱动的 Kinematic 平台，且角色水平范围仍在其上。
    /// 与瞬时物理接触解耦（平台下降时接触会短暂缺失），仅用于“是否仍在骑乘”的邻近判断；
    /// 首次踏上仍需真实接触（由调用方把关），避免在空中被几何吸附。
    /// </summary>
    private bool TryGetStandingPlatform(out Rigidbody2D platformBody, out Collider2D platformCol)
    {
        platformBody = null;
        platformCol = null;
        if (bodyCollider == null) return false;

        float feetY = rb.position.y + feetLocalOffsetY;
        float centerX = rb.position.x;

        int count = Physics2D.OverlapCircleNonAlloc(
            new Vector2(centerX, feetY + GroundCheckRadius * 0.5f),
            GroundCheckRadius * 3f,
            platformProbeHits,
            Physics2D.AllLayers);

        float bestScore = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider2D col = platformProbeHits[i];
            if (col == null) continue;
            Rigidbody2D body = col.attachedRigidbody;
            if (body == null || body == rb || body.bodyType != RigidbodyType2D.Kinematic) continue;

            Bounds cb = col.bounds;
            float topY = body.position.y + cb.extents.y;
            float dY = feetY - topY;

            // 必须踩在顶面之上：允许少量穿透，也允许接触瞬间丢失造成的一小段离缝。
            if (dY < -platformRideGapDown || dY > platformRideGapUp) continue;
            // 角色中心仍需落在平台水平范围内（含少量边距），走出边缘后自然掉落。
            float halfW = cb.extents.x;
            if (centerX < body.position.x - halfW - 0.15f || centerX > body.position.x + halfW + 0.15f) continue;

            float score = Mathf.Abs(dY);
            if (score < bestScore)
            {
                bestScore = score;
                platformBody = body;
                platformCol = col;
            }
        }

        return platformBody != null;
    }

    private bool TryGetPlatformBody(out Rigidbody2D platformBody)
    {
        platformBody = null;
        if (!isGrounded) return false;

        Vector3[] probes = { GroundCheckCenterPosition, GroundCheckLeftPosition, GroundCheckRightPosition };
        for (int p = 0; p < probes.Length; p++)
        {
            int count = Physics2D.OverlapCircleNonAlloc(
                probes[p], GroundCheckRadius, platformProbeHits, Physics2D.AllLayers);
            for (int i = 0; i < count; i++)
            {
                Collider2D col = platformProbeHits[i];
                Rigidbody2D body = col != null ? col.attachedRigidbody : null;
                if (body == null || body == rb || body.bodyType != RigidbodyType2D.Kinematic) continue;
                platformBody = body;
                return true;
            }
        }
        return false;
    }

    protected Vector2 GetPlatformVelocity()
    {
        return TryGetPlatformBody(out Rigidbody2D body) ? body.velocity : Vector2.zero;
    }

    private void ApplyFacing()
    {
        // 朝向：优先 SpriteRenderer.flipX，避免镜像子物体检测点
        if (horizontalInput != 0)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = horizontalInput < 0;
            }
            else
            {
                Vector3 s = transform.localScale;
                s.x = Mathf.Sign(horizontalInput) * Mathf.Abs(s.x);
                transform.localScale = s;
            }
        }
    }

    private void ClampVelocity()
    {
        Vector2 v = rb.velocity;

        // 大炮发射态：起飞/飞行阶段放行速度；一旦真正落地就瞬间定住落点并交还控制
        if (launchActive)
        {
            // 记录已经离地，避免出生帧的地面误判造成"落地定住"
            if (!isGrounded) launchAirborne = true;

            // 真正落地：已离地后又触地，或触地且不再上升（覆盖极短距离/贴地情形）
            if (isGrounded && (launchAirborne || v.y <= 0.1f))
            {
                // 落地：清空一切惯性，直接定在落点，不做二次位移/弹跳
                rb.velocity = Vector2.zero;
                springBounceActive = false;
                launchActive = false;
                launchAirborne = false;
            }
            else
            {
                rb.velocity = v;
            }
            return;
        }

        v.x = Mathf.Clamp(v.x, -maxSpeed.x, maxSpeed.x);

        // 弹簧弹起时允许上升速度超过 maxSpeed.y，直到回落到正常范围
        if (springBounceActive && v.y > maxSpeed.y)
        {
            // 不截断
        }
        else
        {
            springBounceActive = false;
            v.y = Mathf.Clamp(v.y, -maxSpeed.y, maxSpeed.y);
        }

        rb.velocity = v;
    }
    #endregion

    #region 跳跃
    /// <summary>
    /// 根据「重力顶点过渡曲线」计算空中重力缩放：
    /// 起跳初期维持 loGravityMod 的低重力，越过顶点后 gravityTransitionTime 沿
    /// jumpCapTransitionTime 曲线 MoveTowards，使重力从低重力渐进过渡到 gravityCurve 的全重力。
    /// 走下平台等非跳跃腾空直接使用全重力，避免无谓的滞空。
    /// </summary>
    private float ComputeAirGravityScale()
    {
        // 曲线未配置时回退到旧的固定空中重力
        if (gravityCurve == null || gravityCurve.length == 0 ||
            jumpCapTransitionTime == null || jumpCapTransitionTime.length == 0)
        {
            return airGravity;
        }

        float vy = rb.velocity.y;
        float fullGravity = gravityCurve.Evaluate(vy);

        // 非跳跃腾空（走下平台等）：不做低重力过渡，直接全重力
        if (!inJumpArc)
        {
            return fullGravity;
        }

        // 过渡目标时长取正，避免除零
        float capTransition = Mathf.Max(jumpCapTransitionTime.Evaluate(timeSinceJump), 0.0001f);

        if (loGravity)
        {
            // 上升低重力阶段：保持过渡进度为 0（重力停留在 loGravityMod）
            gravityTransitionTime = 0f;
        }
        else
        {
            gravityTransitionTime = Mathf.MoveTowards(gravityTransitionTime, capTransition, Time.fixedDeltaTime);
        }

        float ratio = Mathf.Clamp01(gravityTransitionTime / capTransition);
        return Mathf.Lerp(loGravityMod, fullGravity, ratio);
    }

    private void Jump()
    {
        // 判定本次是否为空跳：地面/Coyote 起跳时 coyoteTimer > 0；
        // coyote 用尽且已离地，才是空跳（此时不消耗也不重置预算，airJumpsUsed 仍是 0）
        bool isAirJump = coyoteTimer <= 0f;
        if (isAirJump)
        {
            airJumpsUsed++;
            airJumpGraceTimer = airJumpGraceTime;
        }
        else
        {
            // 从地面 / Coyote 主动起跳：耗尽本次腾空的空跳预算，
            // 从而取消「平地起跳后还能在空中再跳」的情况
            airJumpsUsed = maxAirJumps;
            airJumpGraceTimer = airJumpGraceTime;
        }

        rb.velocity = new Vector2(rb.velocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        jumpState = 1;
        lastJumpState = 1;
        isJustLanded = false;
        landTimer = 0f;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        springBounceActive = false;
        isGrounded = false;
        wasGroundedLastFrame = false;
        risingFromJump = true;
        jumpCutApplied = false;
        jumpCutEligible = true;
        lastFallSpeed = 0f;

        // 重置跳跃弧度：从低重力起步，越过顶点后沿曲线过渡到全重力
        timeSinceJump = 0f;
        gravityTransitionTime = 0f;
        loGravity = true;
        inJumpArc = true;

        PlayJumpSound();

        if (debugLogs) Debug.Log($"[{name}] Jump | air={isAirJump} used={airJumpsUsed}/{maxAirJumps} vy={rb.velocity.y}");
    }

    private bool CanJump()
    {
        if (isDead || windControlActive) return false;

        // 地面 / Coyote 起跳优先：留在地上的宽限时间内视为普通起跳
        if (coyoteTimer > 0f) return true;

        // 空跳：仅当本次腾空「不是靠平地/Coyote 起跳进入」时可用（预算未被起跳耗尽），
        // 例如走落平台、从高处坠落；不限制离地时间，可随时使用一次
        if (launchActive) return false;
        return !isGrounded
            && airJumpsUsed < maxAirJumps
            && airJumpGraceTimer <= 0f;
    }

    /// <summary>
    /// 可变跳跃高度（Jump Cut）：
    /// 起跳后若玩家提前松开跳跃键，且角色仍在上升，则把上升速度削减一次，
    /// 让玩家能通过按键时长控制跳跃高度。只在每次起跳后执行一次，避免每帧递减。
    /// </summary>
    private void ApplyVariableJumpHeight()
    {
        if (isDead || windControlActive || launchActive) return;
        if (!jumpCutEligible) return;

        // 仍在上升时才削减；落地/下落阶段不动
        if (rb.velocity.y <= 0f) return;
        if (jumpCutApplied) return;

        // 跳跃键仍按住：允许完整高度
        if (Input.GetKey(KeyCode.Space)) return;

        jumpCutApplied = true;
        float newVy = rb.velocity.y * variableJumpMultiplier;
        rb.velocity = new Vector2(rb.velocity.x, newVy);

        if (debugLogs) Debug.Log($"[{name}] JumpCut | vy -> {newVy:F2}");
    }

    private void UpdateJumpState()
    {
        // lastJumpState 先取上一帧值，用于顶点捕捉
        lastJumpState = jumpState;
        float vy = rb.velocity.y;

        int candidate;
        if (isGrounded)
        {
            candidate = 0;
        }
        else if (risingFromJump && vy > jumpRiseSpeedThreshold)
        {
            candidate = 1; // 上升：必须是真的跳起来（跳跃/弹簧/大炮），斜坡贴地回弹的速度达不到这个值
        }
        else if (vy > jumpTopThreshold)
        {
            candidate = 2; // 顶点：斜坡接触回弹/贴地抖动产生的微小正向速度，归到这里而不是"起跳"
        }
        else if (Mathf.Abs(vy) <= jumpTopThreshold)
        {
            candidate = 2; // 顶点
        }
        else
        {
            candidate = 3; // 下落
        }

        // 上升->下落直接跨越时，强制插入一帧顶点，保证 Animator 捕捉
        if (!isGrounded && candidate == 3 && lastJumpState == 1)
        {
            candidate = 2;
        }

        // 即将落地预判
        if (!isGrounded && vy < jumpDownThreshold)
        {
            Vector3 center = GroundCheckCenterPosition;
            Vector3 left   = GroundCheckLeftPosition;
            Vector3 right  = GroundCheckRightPosition;
            float probeRadius = LandingProbeRadius;

            if (Physics2D.OverlapCircle(center, probeRadius, groundLayer) ||
                Physics2D.OverlapCircle(left,   probeRadius, groundLayer) ||
                Physics2D.OverlapCircle(right,  probeRadius, groundLayer))
            {
                candidate = 4;
            }
        }

        jumpState = candidate;
    }

    private void SpawnLandingDust(float fallSpeed)
    {
        if (fallSpeed >= -minSmokeVelocity) return;

        Vector3 spawnPosition = GroundCheckCenterPosition + (Vector3)landingDustOffset;
        GameObject dust;
        if (landingDustPrefab != null)
        {
            dust = Instantiate(landingDustPrefab, spawnPosition, Quaternion.identity);
        }
        else
        {
            if (landingDustController == null) return;

            dust = new GameObject("LandingDust");
            dust.transform.position = spawnPosition;
            dust.AddComponent<SpriteRenderer>();
            Animator dustAnimator = dust.AddComponent<Animator>();
            dustAnimator.runtimeAnimatorController = landingDustController;
        }

        // 无论走预制体还是运行时生成，都把烟尘对齐到角色所在的排序层并叠加层级偏移，
        // 避免预制体自带的 Default 排序层（order 0）被场景地面/前景（order >= 1）盖住。
        SpriteRenderer dustSprite = dust.GetComponentInChildren<SpriteRenderer>(true);
        if (dustSprite != null)
        {
            dustSprite.sortingLayerID = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
            dustSprite.sortingOrder = (spriteRenderer != null ? spriteRenderer.sortingOrder : 0) + landingDustSortingOrderOffset;
        }

        dust.transform.localScale = Vector3.one * landingDustScale;
        Destroy(dust, landingDustLifetime);
    }

    private void UpdateLandedState()
    {
        // 用计时器替代协程，避免泄漏与 timeScale 耦合
        if (isJustLanded)
        {
            landTimer -= Time.deltaTime;
            if (landTimer <= 0f)
            {
                isJustLanded = false;
                landTimer = 0f;
            }
        }
    }
    #endregion

    #region 动画参数
    private void UpdateAnimatorParams()
    {
        if (anim == null) return;

        // 按当前目标最大速度归一化，步行/冲刺都能到 1
        float currentMax = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        float speedNormalized = currentMax > 0f
            ? Mathf.Clamp01(Mathf.Abs(rb.velocity.x) / currentMax)
            : 0f;

        anim.SetFloat("Speed", speedNormalized);
        anim.SetBool("IsSprinting", isSprinting);
        anim.SetBool("IsGrounded", isGrounded);
        anim.SetBool("IsJustLanded", isJustLanded);
        anim.SetInteger("JumpState", jumpState);
        anim.SetFloat("VerticalSpeed", rb.velocity.y);
        anim.SetBool("IsDead", isDead);
    }
    #endregion

    #region 死亡 / 复活
    private void HandleRespawn()
    {
        if (!isRespawning) return;

        respawnTimer -= Time.unscaledDeltaTime;
        if (respawnTimer <= 0f)
        {
            isDead = false;
            isRespawning = false;
            rb.isKinematic = false;
            rb.velocity = Vector2.zero;
            transform.position = initialPosition;

            // 全面重置状态
            isGrounded = false;
            wasGroundedLastFrame = false;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            isJustLanded = false;
            landTimer = 0f;
            jumpState = 0;
            lastJumpState = 0;
            horizontalInput = 0f;
            horizontalInputHoldTime = 0f;
            horizontalInputDirection = 0;
            isSprinting = false;
            springBounceActive = false;
            launchActive = false;
            launchAirborne = false;
            windControlActive = false;
            activeLaunchGravity = launchGravityFallback;
            risingFromJump = false;
            jumpCutApplied = false;
            jumpCutEligible = false;
            lastFallSpeed = 0f;
            airJumpsUsed = 0;
            airJumpGraceTimer = 0f;

            // 重置跳跃弧度状态
            timeSinceJump = 0f;
            gravityTransitionTime = 0f;
            loGravity = false;
            inJumpArc = false;

            if (anim != null)
            {
                anim.SetBool("IsDead", false);
                anim.Rebind();
            }
        }
    }

    public virtual void TriggerDeath()
    {
        if (isDead) return;

        isDead = true;
        isRespawning = true;
        respawnTimer = respawnDelay;

        rb.velocity = Vector2.zero;
        rb.isKinematic = true;
        springBounceActive = false;
        windControlActive = false;

        StopMoveSoundImmediate();

        if (anim != null)
        {
            anim.SetBool("IsDead", true);
            anim.SetTrigger("OnDeath");
        }

        PlayDeathVoice();
    }

    /// <summary>
    /// 角色死亡时的语音。默认用专属的 2D 声道播放固定音量的 deathVoiceClip，
    /// 让所有致死方式（无论由哪个陷阱脚本触发）都发出同一个声音、同一音量。
    /// 子类如无特殊需要无需覆写。
    /// </summary>
    protected virtual void PlayDeathVoice()
    {
        if (deathVoiceClip == null) return;

        EnsureDeathVoiceSource();
        deathVoiceSource.PlayOneShot(deathVoiceClip, deathVoiceVolume * GameAudioManager.SfxMultiplier);
    }

    private void EnsureDeathVoiceSource()
    {
        if (deathVoiceSource != null) return;

        // 独立声道：不与其他叫声/环境音互相打断，2D 播放确保任何位置、镜头远近音量恒定
        deathVoiceSource = gameObject.AddComponent<AudioSource>();
        deathVoiceSource.playOnAwake = false;
        deathVoiceSource.loop = false;
        deathVoiceSource.spatialBlend = 0f;
        deathVoiceSource.dopplerLevel = 0f;
    }

    public void SetRespawnPosition(Vector3 newPos)
    {
        initialPosition = newPos;
    }

    /// <summary>
    /// 外部弹跳（弹簧等）：重置跳跃状态并设置竖直速度。
    /// 上升阶段会临时跳过 maxSpeed.y 的截断，保证能到达设定高度。
    /// </summary>
    public void ApplySpringBounce(float upwardSpeed)
    {
        if (isDead) return;

        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        isGrounded = false;
        isJustLanded = false;
        landTimer = 0f;
        jumpState = 1;
        lastJumpState = 1;
        springBounceActive = true;
        risingFromJump = true;
        jumpCutApplied = false;
        lastFallSpeed = 0f;
        jumpCutEligible = false;
        airJumpsUsed = 0;
        airJumpGraceTimer = 0f;

        // 弹簧同样使用低重力起跳弧度
        timeSinceJump = 0f;
        gravityTransitionTime = 0f;
        loGravity = true;
        inJumpArc = true;

        rb.velocity = new Vector2(rb.velocity.x, upwardSpeed);
    }

    /// <summary>
    /// 大炮发射：进入发射态，直接设置斜向发射速度。
    /// 发射态下 MoveLogic/ClampVelocity 放行速度并强制发射重力，落地后自动解除。
    /// </summary>
    /// <param name="velocity">世界空间初速度</param>
    /// <param name="gravityScale">飞行中使用的重力缩放（<=0 时用 fallback）</param>
    public virtual void ApplyLaunch(Vector2 velocity, float gravityScale)
    {
        if (isDead) return;

        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        isGrounded = false;
        wasGroundedLastFrame = false;
        isJustLanded = false;
        landTimer = 0f;
        jumpState = 1;
        lastJumpState = 1;
        springBounceActive = true;
        launchActive = true;
        launchAirborne = false;
        lastFallSpeed = 0f;
        risingFromJump = true;
        jumpCutApplied = false;
        jumpCutEligible = false;
        airJumpsUsed = 0;
        airJumpGraceTimer = 0f;

        activeLaunchGravity = gravityScale > 0f ? gravityScale : launchGravityFallback;

        // 发射使用固定重力，退出跳跃弧度过渡
        timeSinceJump = 0f;
        gravityTransitionTime = 0f;
        loGravity = false;
        inJumpArc = false;

        // 确保刚体处于可受力的动态状态，再给速度
        rb.isKinematic = false;
        rb.simulated = true;
        rb.gravityScale = activeLaunchGravity;
        rb.velocity = velocity;
        rb.WakeUp();
    }

    /// <summary>兼容旧调用：使用默认发射重力。</summary>
    public void ApplyLaunch(Vector2 velocity) => ApplyLaunch(velocity, launchGravityFallback);

    /// <summary>
    /// 被加农炮发射时的发声钩子。基类默认不发声，具体角色（如小羊）可覆写播放自己的叫声。
    /// 由 CannonLauncher 在开炮音效播放结束后调用，避免与炮声重叠。
    /// </summary>
    public virtual void PlayLaunchVoice() { }

    /// <summary>
    /// 进入 / 退出风扇风力上升状态。
    /// 进入后水平操控交给 PropellerWind（它直接设置水平速度），
    /// 但朝向仍然跟随 A/D；离开后自动恢复普通的空中 / 地面操控。
    /// </summary>
    public void SetWindControl(bool active)
    {
        if (windControlActive == active) return;
        windControlActive = active;

        if (!active) return;

        // 风力上升视为一次真正的上升：清掉地面、跳跃、发射残留，保证播放上升动画
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        isGrounded = false;
        wasGroundedLastFrame = false;
        isJustLanded = false;
        landTimer = 0f;
        jumpState = 1;
        lastJumpState = 1;
        springBounceActive = false;
        launchActive = false;
        launchAirborne = false;
        activeLaunchGravity = launchGravityFallback;
        risingFromJump = true;
        jumpCutApplied = false;
        jumpCutEligible = false;
        airJumpsUsed = 0;
        airJumpGraceTimer = 0f;
        lastFallSpeed = 0f;
        timeSinceJump = 0f;
        gravityTransitionTime = 0f;
        loGravity = false;
        inJumpArc = false;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.simulated = true;
            rb.WakeUp();
        }
    }
    #endregion

    #region 角色音效（跳跃 / 落地 / 移动）
    /// <summary>
    /// 全局音效设置（Assets/Resources/PlayerSfxSettings）：片段、音量、移动判定都在该资产里调。
    /// </summary>
    protected static PlayerSfxSettings PlayerSfxConfig
    {
        get
        {
            if (!cachedPlayerSfxLoaded)
            {
                cachedPlayerSfxLoaded = true;
                cachedPlayerSfx = Resources.Load<PlayerSfxSettings>("PlayerSfxSettings");
            }
            return cachedPlayerSfx;
        }
    }

    /// <summary>当前角色使用的移动循环片段：兔子默认，小羊覆写为自己的片段。</summary>
    protected virtual AudioClip MoveLoopClip =>
        PlayerSfxConfig != null ? PlayerSfxConfig.bunnyMoveLoopClip : null;

    /// <summary>当前角色使用的移动循环音量：兔子默认，小羊覆写为自己的音量。</summary>
    protected virtual float MoveLoopVolume =>
        PlayerSfxConfig != null ? PlayerSfxConfig.bunnyMoveVolume : 0.3f;

    /// <summary>播放落地音效所需的最小下落距离（本次腾空最高点到落地点）。</summary>
    protected float LandMinFallDistance =>
        PlayerSfxConfig != null ? PlayerSfxConfig.landMinFallDistance : 0.2f;

    private void InitPlayerSfx()
    {
        EnsurePlayerSfxSources();
        landSoundPrimed = false;
        moveSoundOn = false;
        moveVolumeCurrent = 0f;
        airborneFallTracking = false;
        airbornePeakY = 0f;
        if (playerMoveSource != null) playerMoveSource.volume = 0f;
    }

    private void EnsurePlayerSfxSources()
    {
        if (playerSfxSource == null)
        {
            playerSfxSource = gameObject.AddComponent<AudioSource>();
            playerSfxSource.playOnAwake = false;
            playerSfxSource.loop = false;
            playerSfxSource.spatialBlend = 0f;
            playerSfxSource.dopplerLevel = 0f;
        }

        if (playerMoveSource == null)
        {
            playerMoveSource = gameObject.AddComponent<AudioSource>();
            playerMoveSource.playOnAwake = false;
            playerMoveSource.loop = true;
            playerMoveSource.spatialBlend = 0f;
            playerMoveSource.dopplerLevel = 0f;
            playerMoveSource.volume = 0f;
        }
    }

    private void PlayJumpSound()
    {
        PlayerSfxSettings settings = PlayerSfxConfig;
        if (settings == null || settings.jumpClip == null) return;

        EnsurePlayerSfxSources();
        playerSfxSource.PlayOneShot(settings.jumpClip, Mathf.Clamp01(settings.jumpVolume * GameAudioManager.SfxMultiplier));
    }

    private void PlayLandSound()
    {
        PlayerSfxSettings settings = PlayerSfxConfig;
        if (settings == null || settings.landClip == null) return;

        EnsurePlayerSfxSources();
        playerSfxSource.PlayOneShot(settings.landClip, Mathf.Clamp01(settings.landVolume * GameAudioManager.SfxMultiplier));
    }

    /// <summary>
    /// 移动循环音效：在地面且角色自身水平速度超过阈值时播放；
    /// 离地、水平速度衰减到停止阈值以下、暂停或死亡时淡出停止。
    /// 用速度而非按键判定：松开 A/D 后惯性滑行期间仍响，顶墙原地推（速度≈0）则停。
    /// 站在移动平台上被位移携带不会产生自身水平速度，因此不会误触发。
    /// </summary>
    private void UpdateMoveSound()
    {
        PlayerSfxSettings settings = PlayerSfxConfig;
        if (settings == null)
        {
            StopMoveSoundImmediate();
            return;
        }

        EnsurePlayerSfxSources();
        AudioClip loopClip = MoveLoopClip;
        if (loopClip == null)
        {
            StopMoveSoundImmediate();
            return;
        }

        bool groundedForSound = isGrounded && !isDead && Time.timeScale > 0f;
        float absVx = rb != null ? Mathf.Abs(rb.velocity.x) : 0f;

        // 迟滞：正在播放时用较低的停止阈值，避免在临界速度附近反复启停
        float threshold = moveSoundOn ? settings.moveStopSpeed : settings.moveStartSpeed;
        bool shouldBeOn = groundedForSound && absVx > threshold;

        if (shouldBeOn)
        {
            if (playerMoveSource.clip != loopClip) playerMoveSource.clip = loopClip;

            if (!moveSoundOn)
            {
                moveSoundOn = true;
                moveVolumeCurrent = 0f;
                playerMoveSource.volume = 0f;
                playerMoveSource.Play();
            }
            else if (!playerMoveSource.isPlaying)
            {
                playerMoveSource.Play();
            }
        }

        float moveTargetVolume = Mathf.Clamp01(MoveLoopVolume * GameAudioManager.SfxMultiplier);
        float targetVolume = shouldBeOn ? moveTargetVolume : 0f;
        float fadeSpeed = settings.moveFadeTime <= 0.0001f
            ? float.MaxValue
            : Mathf.Max(moveTargetVolume, 0.0001f) / settings.moveFadeTime;

        moveVolumeCurrent = Mathf.MoveTowards(moveVolumeCurrent, targetVolume, fadeSpeed * Time.unscaledDeltaTime);
        playerMoveSource.volume = moveVolumeCurrent;

        if (!shouldBeOn && moveSoundOn && moveVolumeCurrent <= 0.0001f)
        {
            playerMoveSource.Stop();
            moveSoundOn = false;
        }
    }

    private void StopMoveSoundImmediate()
    {
        moveSoundOn = false;
        moveVolumeCurrent = 0f;
        if (playerMoveSource != null)
        {
            playerMoveSource.volume = 0f;
            if (playerMoveSource.isPlaying) playerMoveSource.Stop();
        }
    }
    #endregion

    #region 调试 Gizmos
    protected virtual void OnDrawGizmosSelected()
    {
        Vector3 center3D = GroundCheckCenterPosition;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center3D, LandingProbeRadius);

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(center3D, GroundCheckRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GroundCheckLeftPosition, GroundCheckRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(GroundCheckRightPosition, GroundCheckRadius);

        if (jumpState == 2)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
    #endregion
}
