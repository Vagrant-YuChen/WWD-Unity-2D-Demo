using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
public class BunnyController : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 移动核心配置 ===")]
    [Tooltip("基础移动速度")]
    [SerializeField] protected float moveSpeed = 5f;
    [Tooltip("冲刺速度倍率")]
    [SerializeField] protected float sprintMultiplier = 1.8f;
    [Tooltip("地面摩擦力（越大停得越快）")]
    [SerializeField] protected float groundFriction = 20f;
    [Tooltip("空中惯性（0=无惯性，1=自然）")]
    [SerializeField] protected float airInertia = 0.8f;
    [Tooltip("最大水平/垂直速度限制")]
    [SerializeField] protected Vector2 maxSpeed = new Vector2(8f, 15f);

    [Header("=== 大炮发射飞行配置 ===")]
    [Tooltip("发射飞行时使用的重力缩放（由大炮传入；决定抛物线弧度）")]
    [SerializeField] protected float launchGravityFallback = 1.2f;

    [Header("=== 跳跃核心配置 ===")]
    [Tooltip("基础跳跃力度")]
    [SerializeField] protected float jumpForce = 7f;
    [Tooltip("Coyote Time：离地后仍可跳的时间")]
    [SerializeField] protected float jumpGraceTime = 0.1f;
    [Tooltip("Jump Buffer：按下后多久内落地仍可跳")]
    [SerializeField] protected float jumpBufferTime = 0.1f;
    [Tooltip("地面重力缩放")]
    [SerializeField] protected float groundGravity = 1f;
    [Tooltip("空中重力缩放（下落更快）")]
    [SerializeField] protected float airGravity = 1.2f;

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

    [Header("=== 动画/落地配置 ===")]
    [Tooltip("判断跳跃顶点的速度阈值")]
    [SerializeField] protected float jumpTopThreshold = 0.01f;
    [Tooltip("判断下落的速度阈值")]
    [SerializeField] protected float jumpDownThreshold = -0.01f;
    [Tooltip("判定为「上升/起跳」所需的最小竖直速度。达不到此速度的正向 vy（斜坡接触回弹、贴地小抖动）不再被当成跳跃")]
    [SerializeField] protected float jumpRiseSpeedThreshold = 1f;
    [Tooltip("落地动画保持时间（秒）")]
    [SerializeField] protected float landStateResetDelay = 0.15f;

    [Header("=== 调试 ===")]
    [SerializeField] protected bool debugLogs = false;
    #endregion

    #region 私有状态
    protected Animator anim;
    protected Rigidbody2D rb;
    protected SpriteRenderer spriteRenderer;

    // 输入
    private float horizontalInput;
    private bool isSprinting;

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
    private bool launchAirborne;     // 发射后是否已经离地（用于准确判定落地瞬间）
    private bool risingFromJump;     // 真正的起跳/弹簧/发射造成上升中，此期间不能靠接触法线判着地

    // 跳跃阶段
    private int jumpState;   // 0=地面 1=上升 2=顶点 3=下落 4=即将落地
    private int lastJumpState;

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

        OnCharacterConfigured();
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

    /// <summary>触发"落地"所需的最低下落速度（负值，越小越难触发）。</summary>
    protected virtual float LandedSpeedThreshold => LandedSpeedThresholdDefault;

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
            HandleRespawn();
            return;
        }

        UpdateInput();
        UpdateJumpBuffer();

        // 跳跃触发（Coyote + Buffer）
        if (jumpBufferTimer > 0f && CanJump())
        {
            Jump();
        }

        UpdateJumpState();
        UpdateLandedState();
        UpdateAnimatorParams();
    }

    private void FixedUpdate()
    {
        if (isDead)
        {
            rb.velocity = Vector2.zero;
            return;
        }

        UpdateGroundDetection();
        UpdateCoyoteTimer();

        // 发射飞行中强制使用发射重力，保证一定是抛物线，且不受地面/空中状态影响
        if (launchActive)
            rb.gravityScale = activeLaunchGravity;
        else
            rb.gravityScale = isGrounded ? groundGravity : airGravity;

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
        bool horizontallyBlocked = pushing && isGrounded && Mathf.Abs(rb.velocity.x) < 0.2f;

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
        rb.velocity = new Vector2(horizontalInput * Mathf.Max(0.5f, Mathf.Abs(rb.velocity.x)), rb.velocity.y);
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
        horizontalInput = Input.GetAxisRaw("Horizontal");
        isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private void UpdateJumpBuffer()
    {
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

        bool newGrounded = c || l || r || supportedByContact;

        // 风道上升期间强制视为空中，避免站在风扇碰撞体上播放地面/落地动画
        if (windControlActive) newGrounded = false;

        if (newGrounded || rb.velocity.y <= jumpRiseSpeedThreshold)
        {
            risingFromJump = false;
        }

        // 仅在"上一帧空中 -> 本帧落地"瞬间置位，避免重复触发
        if (newGrounded && !wasGroundedLastFrame && rb.velocity.y < LandedSpeedThreshold)
        {
            isJustLanded = true;
            landTimer = LandStateResetDelay;
        }

        wasGroundedLastFrame = isGrounded;
        isGrounded = newGrounded;
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

        float targetSpeed = horizontalInput * (isSprinting ? moveSpeed * sprintMultiplier : moveSpeed);
        float vx = rb.velocity.x;

        if (isGrounded)
        {
            float speedDiff = targetSpeed - vx;
            float moveForce = Mathf.Pow(Mathf.Abs(speedDiff) * groundFriction, 0.9f) * Mathf.Sign(speedDiff);
            rb.AddForce(Vector2.right * moveForce, ForceMode2D.Force);
        }
        else
        {
            float airControl = airInertia * (isSprinting ? 1.2f : 1f);
            float airSpeedDiff = (targetSpeed - vx) * airControl;
            rb.velocity = new Vector2(vx + airSpeedDiff * Time.fixedDeltaTime, rb.velocity.y);
        }

        ApplyFacing();
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
    private void Jump()
    {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        jumpState = 1;
        lastJumpState = 1;
        isJustLanded = false;
        landTimer = 0f;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        springBounceActive = false;
        risingFromJump = true;

        if (debugLogs) Debug.Log($"[{name}] Jump | vy={rb.velocity.y}");
    }

    private bool CanJump()
    {
        // coyote time 真正生效：离地短时间仍可跳；风力上升期间禁用跳跃
        return coyoteTimer > 0f && !isDead && !windControlActive;
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
        else if (vy > jumpRiseSpeedThreshold)
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
            isSprinting = false;
            springBounceActive = false;
            launchActive = false;
            launchAirborne = false;
            windControlActive = false;
            activeLaunchGravity = launchGravityFallback;
            risingFromJump = false;

            if (anim != null)
            {
                anim.SetBool("IsDead", false);
                anim.Rebind();
            }
        }
    }

    public void TriggerDeath()
    {
        if (isDead) return;

        isDead = true;
        isRespawning = true;
        respawnTimer = respawnDelay;

        rb.velocity = Vector2.zero;
        rb.isKinematic = true;
        springBounceActive = false;
        windControlActive = false;

        if (anim != null)
        {
            anim.SetBool("IsDead", true);
            anim.SetTrigger("OnDeath");
        }
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

        rb.velocity = new Vector2(rb.velocity.x, upwardSpeed);
    }

    /// <summary>
    /// 大炮发射：进入发射态，直接设置斜向发射速度。
    /// 发射态下 MoveLogic/ClampVelocity 放行速度并强制发射重力，落地后自动解除。
    /// </summary>
    /// <param name="velocity">世界空间初速度</param>
    /// <param name="gravityScale">飞行中使用的重力缩放（<=0 时用 fallback）</param>
    public void ApplyLaunch(Vector2 velocity, float gravityScale)
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
        risingFromJump = true;

        activeLaunchGravity = gravityScale > 0f ? gravityScale : launchGravityFallback;

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

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.simulated = true;
            rb.WakeUp();
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
