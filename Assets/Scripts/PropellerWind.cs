using UnityEngine;

/// <summary>
/// 螺旋桨风扇：
/// 小羊的脚碰到螺旋桨顶部碰撞体后风扇启动，在螺旋桨正上方形成一条矩形风道。
/// 风道内持续给玩家向上的风力，玩家可以用 A/D 左右微调飞行方向；
/// 身体完全离开风道后风力立即消失、恢复正常重力，风扇也会自动停下，
/// 必须等小羊再次用脚碰到顶部碰撞体才会重新启动。
/// </summary>
public class PropellerWind : MonoBehaviour, TriggeredAudio2D.IActivityAudioSource
{
    #region 启动触发
    [Header("=== 启动触发 ===")]
    [Tooltip("螺旋桨顶部的碰撞体（实体或 Trigger 都行）；留空自动取自身/子物体上的 Collider2D")]
    [SerializeField] private Collider2D activationCollider;
    [Tooltip("玩家所在层（小羊在 Default 层时保持 1）")]
    [SerializeField] private LayerMask playerLayer = 1;
    [Tooltip("脚底判定带高度（世界单位）：脚底进入碰撞体顶部上下这个范围就算触碰")]
    [SerializeField] private float feetTouchTolerance = 0.12f;
    [Tooltip("风扇停止后，至少间隔多久才允许再次启动（秒），避免同一帧反复开关")]
    [SerializeField] private float reactivationCooldown = 0.15f;
    [Tooltip("小羊落到螺旋桨上后，先停顿多久再启动风扇（秒），用于播放落地/待机动画；0 表示落地后立即启动")]
    [SerializeField, Min(0f)] private float landingPause = 0.1f;
    [Tooltip("启动瞬间把下落速度清零，像被强风托住；关闭则只靠风力慢慢减速")]
    [SerializeField] private bool catchFallingOnActivate = true;
    #endregion

    #region 风道范围
    [Header("=== 风道范围 ===")]
    [Tooltip("风道高度（世界单位），从碰撞体顶部往上算")]
    [SerializeField] private float windHeight = 4.5f;
    [Tooltip("风道宽度（世界单位）；<=0 时自动使用碰撞体宽度 + 两侧扩展")]
    [SerializeField] private float windWidth = 0f;
    [Tooltip("自动宽度时，碰撞体左右各扩展多少")]
    [SerializeField] private float windWidthPadding = 0.6f;
    [Tooltip("风道底部相对碰撞体顶部的偏移（通常保持 0；正值会把整条风道往上移，注意别让小羊站的位置落到风道外）")]
    [SerializeField] private float windBottomOffset = 0f;
    [Tooltip("身体离开风道边缘多少距离后才判定为离开，避免边界抖动导致风扇反复开关")]
    [SerializeField] private float windExitPadding = 0.05f;
    #endregion

    #region 风力
    [Header("=== 风力 ===")]
    [Tooltip("风道内希望达到的稳定上升速度")]
    [SerializeField] private float windRiseSpeed = 6.5f;
    [Tooltip("接近目标上升速度的响应速度，越大越猛")]
    [SerializeField] private float liftResponsiveness = 5f;
    [Tooltip("向上加速度上限（含抵消重力），防止瞬间弹射")]
    [SerializeField] private float maxUpAcceleration = 35f;
    [Tooltip("向下加速度上限（从高处落入风道时用于快速减速）")]
    [SerializeField] private float maxDownAcceleration = 45f;
    [Tooltip("风力从 0 到满的启动时间（秒）")]
    [SerializeField] private float windRampUpTime = 0.2f;
    [Tooltip("A/D 在风道内的水平目标速度")]
    [SerializeField] private float windSteerSpeed = 2.5f;
    [Tooltip("水平速度逼近目标值的加速度")]
    [SerializeField] private float windSteerAcceleration = 15f;
    [Tooltip("松开 A/D 时是否把水平速度拉回 0；关闭则保留惯性漂移")]
    [SerializeField] private bool stopHorizontalWhenNoInput = true;
    #endregion

    #region 动画 / 表现
    [Header("=== 动画 / 表现 ===")]
    [Tooltip("螺旋桨 Animator；留空自动 GetComponent")]
    [SerializeField] private Animator propellerAnimator;
    [Tooltip("Animator 控制器；如果物体上还没有 Animator，运行时会自动添加并挂上它")]
    [SerializeField] private RuntimeAnimatorController animatorController;
    [Tooltip("Animator 中控制旋转的 Bool 参数名（默认已配置在 PropellerRotate.controller 中）")]
    [SerializeField] private string spinningParameter = "Spinning";
    [Tooltip("启动/停止时开关的风特效（可选）")]
    [SerializeField] private GameObject windEffect;
    [SerializeField] private bool debugLogs = false;
    #endregion

    #region 运行时状态
    private BunnyController windSheep;
    private Collider2D windSheepCollider;
    private Rigidbody2D windRb;
    private bool fanActive;
    private float windPower;
    private float windGraceTimer;
    private float nextActivationTime;
    private bool landingHoldStarted;
    private float landingHoldUntil;
    private bool warnedMissingAnimatorParameter;
    private static readonly Collider2D[] overlapBuffer = new Collider2D[16];
    #endregion

    #region 对外只读
    /// <summary>风扇当前是否在吹风。</summary>
    public bool IsFanActive => fanActive;

    /// <summary>风扇当前是否在转动（用于 TriggeredAudio2D 自动开/掐循环音）。</summary>
    public bool IsAudioActive => fanActive;

    /// <summary>当前风道矩形（世界坐标）。</summary>
    public Rect WindArea => ComputeWindRect(activationCollider);
    #endregion

    private void Awake()
    {
        if (activationCollider == null) activationCollider = GetComponent<Collider2D>();
        if (activationCollider == null) activationCollider = GetComponentInChildren<Collider2D>();

        if (activationCollider == null)
        {
            Debug.LogError($"[{name}] PropellerWind 找不到碰撞体，请把螺旋桨顶部的 Collider2D 拖到 Activation Collider。");
            enabled = false;
            return;
        }

        SetupAnimator();

        if (windEffect != null && windEffect != gameObject) windEffect.SetActive(false);
        SetAnimatorSpinning(false);
    }

    private void OnDisable()
    {
        DeactivateFan();
    }

    private void OnDestroy()
    {
        DeactivateFan();
    }

    private void FixedUpdate()
    {
        if (activationCollider == null ||
            !activationCollider.enabled ||
            !activationCollider.gameObject.activeInHierarchy)
        {
            DeactivateFan();
            return;
        }

        if (fanActive)
        {
            // 小羊死亡 / 被装进大炮（禁用或变成 Kinematic）时立即停风
            if (windSheep == null || windSheep.isDead || !windSheep.enabled ||
                !windSheep.gameObject.activeInHierarchy ||
                windRb == null || windRb.isKinematic || !windRb.simulated)
            {
                DeactivateFan();
                return;
            }

            if (windGraceTimer > 0f)
            {
                windGraceTimer -= Time.fixedDeltaTime;
            }
            else if (!IsSheepInsideWind(windSheep))
            {
                // 身体完全离开风道：停止吹风，恢复正常重力
                DeactivateFan();
                return;
            }

            windPower = Mathf.MoveTowards(
                windPower, 1f, Time.fixedDeltaTime / Mathf.Max(0.01f, windRampUpTime));
            ApplyWind(windRb, Time.fixedDeltaTime);
        }
        else
        {
            if (!TryFindSheepOnTop(out BunnyController sheep, out Collider2D sheepCollider))
            {
                return;
            }

            if (!CanActivateNow(sheep)) return;

            ActivateFan(sheep, sheepCollider);
        }
    }

    #region 启动 / 停止
    private void ActivateFan(BunnyController sheep, Collider2D sheepCollider)
    {
        if (sheep == null || sheep.isDead || !sheep.enabled) return;

        Rigidbody2D rb = sheepCollider != null && sheepCollider.attachedRigidbody != null
            ? sheepCollider.attachedRigidbody
            : sheep.GetComponent<Rigidbody2D>();
        if (rb == null || !rb.simulated || rb.isKinematic) return;

        fanActive = true;
        windSheep = sheep;
        windSheepCollider = GetSheepCollider(sheep);
        if (windSheepCollider == null) windSheepCollider = sheepCollider;
        windRb = rb;
        windPower = 0f;
        windGraceTimer = 0.15f;
        landingHoldStarted = false;
        landingHoldUntil = 0f;

        // 强风兜底：先托住正在下落的小羊，避免它一帧穿过 Trigger 碰撞体
        if (catchFallingOnActivate && rb.velocity.y < 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, 0f);
        }

        // 交给风道脚本控制水平速度；BunnyController 会跳过普通空中操控
        sheep.SetWindControl(true);

        SetAnimatorSpinning(true);
        if (windEffect != null && windEffect != gameObject) windEffect.SetActive(true);

        if (debugLogs) Debug.Log($"[{name}] 风扇启动：{sheep.name}");
    }

    private void DeactivateFan()
    {
        if (!fanActive) return;

        fanActive = false;
        windPower = 0f;
        windGraceTimer = 0f;
        landingHoldStarted = false;
        landingHoldUntil = 0f;
        nextActivationTime = Time.time + Mathf.Max(0f, reactivationCooldown);

        if (windSheep != null) windSheep.SetWindControl(false);
        windSheep = null;
        windSheepCollider = null;
        windRb = null;

        SetAnimatorSpinning(false);
        if (windEffect != null && windEffect != gameObject) windEffect.SetActive(false);

        if (debugLogs) Debug.Log($"[{name}] 风扇停止");
    }
    #endregion

    #region 风道判定
    private Rect ComputeWindRect(Collider2D source)
    {
        if (source == null) return new Rect();

        Bounds b = source.bounds;
        float width = windWidth > 0f ? windWidth : b.size.x + windWidthPadding * 2f;
        float bottom = b.max.y + windBottomOffset;

        return new Rect(
            b.center.x - width * 0.5f,
            bottom,
            Mathf.Max(0.05f, width),
            Mathf.Max(0.05f, windHeight));
    }

    private bool IsSheepInsideWind(BunnyController sheep)
    {
        Collider2D col = windSheepCollider != null ? windSheepCollider : GetSheepCollider(sheep);
        if (col == null) return false;

        Rect r = ComputeWindRect(activationCollider);
        Bounds b = col.bounds;
        float pad = windExitPadding;

        return b.max.x > r.xMin - pad
            && b.min.x < r.xMax + pad
            && b.max.y > r.yMin - pad
            && b.min.y < r.yMax + pad;
    }

    /// <summary>
    /// 在碰撞体顶部附近找小羊。只接受"脚底在顶部附近、身体没有完全钻到顶部以下"的接触，
    /// 避免从侧面或下方顶到碰撞体时误启动。
    /// </summary>
    private bool TryFindSheepOnTop(out BunnyController sheep, out Collider2D sheepCollider)
    {
        sheep = null;
        sheepCollider = null;
        if (activationCollider == null) return false;

        Bounds b = activationCollider.bounds;
        Vector2 center = new Vector2(b.center.x, b.max.y);
        Vector2 size = new Vector2(b.size.x + 0.1f, Mathf.Max(0.02f, feetTouchTolerance) * 2f);

        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, overlapBuffer, playerLayer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == activationCollider) continue;

            BunnyController candidate = col.GetComponentInParent<BunnyController>();
            if (candidate == null || candidate.isDead || !candidate.enabled) continue;

            Bounds sb = col.bounds;
            if (sb.min.y < b.max.y - feetTouchTolerance) continue;
            if (sb.max.y <= b.max.y) continue;

            sheep = candidate;
            sheepCollider = col;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 风扇重新启动前的统一门槛：小羊必须先落到碰撞体上，并保持 landingPause 秒，
    /// 让 Landing / Idle 动画有时间播放，然后再启动。
    /// </summary>
    private bool CanActivateNow(BunnyController sheep)
    {
        if (!IsSheepLanded(sheep))
        {
            landingHoldStarted = false;
            return false;
        }

        if (Time.time < nextActivationTime) return false;

        if (!landingHoldStarted)
        {
            landingHoldStarted = true;
            landingHoldUntil = Time.time + Mathf.Max(0f, landingPause);
        }

        return Time.time >= landingHoldUntil;
    }

    private bool IsSheepLanded(BunnyController sheep)
    {
        if (sheep == null) return false;
        if (sheep.IsGroundedState) return true;

        // 薄碰撞体上偶尔会晚一帧才被判定为 grounded；垂直速度接近 0 时也视为已经停稳
        Rigidbody2D rb = sheep.GetComponent<Rigidbody2D>();
        return rb != null && Mathf.Abs(rb.velocity.y) <= 0.1f;
    }

    private static Collider2D GetSheepCollider(BunnyController sheep)
    {
        if (sheep == null) return null;
        Collider2D col = sheep.GetComponent<Collider2D>();
        return col != null ? col : sheep.GetComponentInChildren<Collider2D>();
    }
    #endregion

    #region 物理接触兜底
    // 实体碰撞体高速落下时，OverlapBox 可能刚好错过判定带；
    // 碰撞回调可以保证"脚踩上去"这一刻一定被捕捉到。
    private void OnCollisionEnter2D(Collision2D collision) => TryActivateFromContact(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryActivateFromContact(collision.collider);
    private void OnTriggerEnter2D(Collider2D other) => TryActivateFromContact(other);
    private void OnTriggerStay2D(Collider2D other) => TryActivateFromContact(other);

    private void TryActivateFromContact(Collider2D other)
    {
        if (activationCollider == null) return;
        if (fanActive || other == null) return;
        if (!BunnyContactUtility.IsInLayerMask(other.gameObject.layer, playerLayer)) return;

        BunnyController sheep = other.GetComponentInParent<BunnyController>();
        if (sheep == null || sheep.isDead || !sheep.enabled) return;

        Bounds b = activationCollider.bounds;
        Bounds sb = other.bounds;
        if (sb.min.y < b.max.y - feetTouchTolerance) return;
        if (sb.max.y <= b.max.y) return;

        if (!CanActivateNow(sheep)) return;

        ActivateFan(sheep, other);
    }
    #endregion

    #region 风力施加
    private void ApplyWind(Rigidbody2D rb, float dt)
    {
        if (rb == null) return;

        // 垂直：用弹簧-阻尼逼近目标上升速度，并抵消当前重力与线性阻力
        float gravityAccel = Mathf.Max(0f, -Physics2D.gravity.y * rb.gravityScale);
        float vy = rb.velocity.y;
        float dragAccel = rb.drag * vy;
        float accel = (windRiseSpeed - vy) * liftResponsiveness + gravityAccel + dragAccel;
        accel = Mathf.Clamp(accel, -maxDownAcceleration, maxUpAcceleration) * windPower;
        rb.AddForce(Vector2.up * (accel * rb.mass), ForceMode2D.Force);

        // 水平：A/D 微操，直接逼近目标水平速度
        float input = Input.GetAxisRaw("Horizontal");
        float vx = rb.velocity.x;

        if (Mathf.Abs(input) > 0.01f)
        {
            vx = Mathf.MoveTowards(vx, input * windSteerSpeed, windSteerAcceleration * dt);
        }
        else if (stopHorizontalWhenNoInput)
        {
            vx = Mathf.MoveTowards(vx, 0f, windSteerAcceleration * dt);
        }

        rb.velocity = new Vector2(vx, rb.velocity.y);
    }
    #endregion

    #region 动画
    private void SetupAnimator()
    {
        if (propellerAnimator == null) propellerAnimator = GetComponent<Animator>();

        if (propellerAnimator == null && animatorController != null)
        {
            propellerAnimator = gameObject.AddComponent<Animator>();
        }

        if (propellerAnimator != null &&
            propellerAnimator.runtimeAnimatorController == null &&
            animatorController != null)
        {
            propellerAnimator.runtimeAnimatorController = animatorController;
        }

        if (propellerAnimator == null)
        {
            Debug.LogWarning($"[{name}] 没有 Animator / 控制器，风扇功能正常，但螺旋桨不会播放旋转动画。");
        }
    }

    private void SetAnimatorSpinning(bool spinning)
    {
        if (propellerAnimator == null) return;

        if (HasParameter(propellerAnimator, spinningParameter))
        {
            propellerAnimator.SetBool(spinningParameter, spinning);
            return;
        }

        if (!warnedMissingAnimatorParameter)
        {
            warnedMissingAnimatorParameter = true;
            Debug.LogWarning($"[{name}] Animator 没有 Bool 参数 '{spinningParameter}'，暂用 speed 控制。请在控制器里添加该参数。");
        }

        propellerAnimator.speed = spinning ? 1f : 0f;
    }

    private static bool HasParameter(Animator animator, string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return false;

        foreach (var p in animator.parameters)
        {
            if (p.name == parameterName) return true;
        }

        return false;
    }
    #endregion

    #region 编辑器预览
#if UNITY_EDITOR
    /// <summary>
    /// 在编辑器里第一次挂上脚本时自动找碰撞体、Animator 和 PropellerRotate 控制器，
    /// 省掉手动拖拽；运行时不会执行。
    /// </summary>
    private void Reset()
    {
        if (activationCollider == null) activationCollider = GetComponent<Collider2D>();
        if (activationCollider == null) activationCollider = GetComponentInChildren<Collider2D>();

        if (propellerAnimator == null) propellerAnimator = GetComponent<Animator>();

        if (animatorController == null)
        {
            animatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animation/PropellerRotate.controller");
        }

        if (propellerAnimator == null && animatorController != null)
        {
            propellerAnimator = gameObject.AddComponent<Animator>();
            propellerAnimator.runtimeAnimatorController = animatorController;
        }
        else if (propellerAnimator != null &&
                 propellerAnimator.runtimeAnimatorController == null &&
                 animatorController != null)
        {
            propellerAnimator.runtimeAnimatorController = animatorController;
        }
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Collider2D col = activationCollider != null ? activationCollider : GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();
        if (col == null) return;

        Bounds b = col.bounds;

        // 脚底触发带
        Gizmos.color = fanActive ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(
            new Vector3(b.center.x, b.max.y, 0f),
            new Vector3(b.size.x + 0.1f, Mathf.Max(0.02f, feetTouchTolerance) * 2f, 0.01f));

        // 风道
        Rect r = ComputeWindRect(col);
        Vector3 windCenter = new Vector3(r.center.x, r.center.y, 0f);
        Vector3 windSize = new Vector3(r.width, r.height, 0.01f);

        Gizmos.color = fanActive ? new Color(0.2f, 1f, 1f, 0.9f) : new Color(0.2f, 0.6f, 1f, 0.55f);
        Gizmos.DrawWireCube(windCenter, windSize);

        // 风向箭头
        Gizmos.color = new Color(0.2f, 1f, 1f, 0.8f);
        const int arrows = 3;
        for (int i = 0; i < arrows; i++)
        {
            float t = arrows == 1 ? 0.5f : i / (float)(arrows - 1);
            float x = Mathf.Lerp(r.xMin + r.width * 0.2f, r.xMax - r.width * 0.2f, t);
            float y = Mathf.Lerp(r.yMin, r.yMax, 0.35f);

            Vector3 basePos = new Vector3(x, y, 0f);
            Vector3 tip = basePos + Vector3.up * 0.25f;
            Gizmos.DrawLine(basePos, tip);
            Gizmos.DrawLine(tip, tip + new Vector3(-0.08f, -0.1f, 0f));
            Gizmos.DrawLine(tip, tip + new Vector3(0.08f, -0.1f, 0f));
        }
    }
    #endregion
}
