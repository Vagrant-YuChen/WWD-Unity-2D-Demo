using System.Collections;
using UnityEngine;

/// <summary>
/// 安迪·琼斯式滚石：金字塔顶的带刺巨石。
///
/// 行为总览：
///   1) 初始完全锁定（Kinematic、零速度），不会被自身质量/重力推来推去；
///   2) 只有小羊踩到左 / 右阶梯（由 <see cref="BoulderStairTrigger"/> 调用）才解除锁定并开始滚；
///   3) 向右滚会滚到流沙上，被流沙吞没（复用 QuicksandTrap 的滚石专用下沉参数）；
///   4) 向左滚会撞上加农炮，触发 CannonDestruction 的炸毁演出，然后被反作用力弹向右侧落地停下；
///   5) 无论小羊因何死亡，等它真正复活的那一刻，巨石严格复位到金字塔顶端并重新锁定，
///      同时被它炸毁过的加农炮也会一起恢复。
///
/// 滚动的方向 / 初速度 / 加速度 / 最高速 / 停下的判定 全部可在 Inspector 里慢慢调。
/// </summary>
[DisallowMultipleComponent]
public class RollingBoulder : MonoBehaviour
{
    public enum BoulderState
    {
        Locked,     // 锁定在原点，一动不动
        Rolling,    // 已触发，正在滚动
        Rebounding, // 撞炮后被弹飞，正在空中
        Stopped,    // 撞到障碍物 / 落地后停在原地
        Sunk        // 被流沙吞没
    }

    #region 可配置参数
    [Header("=== 引用 ===")]
    [Tooltip("巨石渲染器（留空自动取同物体上的 SpriteRenderer）")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("=== 滚动手感（重点调试区） ===")]
    [Tooltip("触发瞬间给的初始水平速度（世界单位/秒），保证能从金字塔顶脱离")]
    [SerializeField] private float initialPushSpeed = 2f;
    [Tooltip("滚动中持续施加的水平加速度（世界单位/秒²）。越大坡上冲得越快")]
    [SerializeField] private float rollAcceleration = 12f;
    [Tooltip("水平滚动速度上限（世界单位/秒）")]
    [SerializeField] private float maxRollSpeed = 7f;
    [Tooltip("刚体空气阻尼，越小越难在平地上自然减速。想让它滚得更远就调小")]
    [SerializeField] private float linearDrag = 0.05f;
    [Tooltip("滚动时是否强制按速度设置角速度，保证视觉上一定在转（无摩擦也能转）")]
    [SerializeField] private bool forceVisualRoll = true;

    [Header("=== 停下判定 ===")]
    [Tooltip("水平速度低于此值持续 stallTimeToStop 秒，就认为撞上障碍物并停下")]
    [SerializeField] private float stallSpeedThreshold = 0.25f;
    [Tooltip("速度不足持续多久后判定停下（秒）")]
    [SerializeField] private float stallTimeToStop = 0.4f;

    [Header("=== 连锁反应 ===")]
    [Tooltip("撞到小羊时直接把小羊扎死")]
    [SerializeField] private bool killPlayerOnTouch = true;
    [Tooltip("流沙吞没的备用深度（当流沙自身参数取不到时使用）")]
    [SerializeField] private float fallbackSinkDepth = 1.5f;
    [Tooltip("流沙吞没的备用时长（当流沙自身参数取不到时使用）")]
    [SerializeField] private float fallbackSinkDuration = 1f;

    [Header("=== 撞上加农炮的反弹（重点调试区） ===")]
    [Tooltip("撞炮后是否被反作用力弹向右侧（勾选才有反弹效果；取消则原地停住）")]
    [SerializeField] private bool bounceOffCannon = true;
    [Tooltip("撞炮反弹的初速度（世界单位/秒）。x 会被强制取正值（向右），y 为向上分量。默认是能越过金字塔落到右侧的高抛弧线")]
    [SerializeField] private Vector2 cannonBounceVelocity = new Vector2(2.8f, 9f);
    [Tooltip("反弹后到落地前的这段时间内忽略碰撞判定（秒），避免刚起跳就被地面/残骸判定停下")]
    [SerializeField] private float reboundIgnoreCollisionTime = 0.15f;
    [Tooltip("仅用于 Scene 里预览反弹弧线的起点（一般填撞炮位置），不影响实际游戏")]
    [SerializeField] private Vector2 reboundPreviewOrigin = new Vector2(-4.6f, -2.6f);

    [Header("=== 复活重置 ===")]
    [Tooltip("小羊复活时是否把巨石复位到金字塔顶端（强烈建议开启）")]
    [SerializeField] private bool resetOnPlayerRespawn = true;

    [Header("=== 阶梯触发区（运行时自动生成，可用 Gizmos 在 Scene 里调） ===")]
    [Tooltip("开启后自动在左 / 右阶梯处生成两个触发器，踩到就触发滚动。关闭则需手动摆放 BoulderStairTrigger")]
    [SerializeField] private bool autoCreateStairZones = true;
    [Tooltip("左侧阶梯触发区中心（世界坐标）")]
    [SerializeField] private Vector2 leftZoneCenter = new Vector2(-2.45f, -1.5f);
    [Tooltip("左侧阶梯触发区尺寸")]
    [SerializeField] private Vector2 leftZoneSize = new Vector2(2.1f, 2.3f);
    [Tooltip("右侧阶梯触发区中心（世界坐标）")]
    [SerializeField] private Vector2 rightZoneCenter = new Vector2(-0.3f, -1.7f);
    [Tooltip("右侧阶梯触发区尺寸")]
    [SerializeField] private Vector2 rightZoneSize = new Vector2(2.2f, 2.5f);

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    #endregion

    #region 运行时状态
    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private BoulderState state = BoulderState.Locked;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float bodyRadius = 0.5f;

    private int rollDirection;      // -1 向左，+1 向右
    private float stallTimer;
    private float reboundTimer;
    private Coroutine sinkRoutine;
    private CannonDestruction destroyedCannon;

    private BunnyController player;
    private bool playerWasDead;

    public BoulderState State => state;
    public bool IsLocked => state == BoulderState.Locked;
    #endregion

    #region 生命周期
    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        if (bodyCollider == null)
        {
            Debug.LogError($"[{name}] RollingBoulder 需要一个 Collider2D（建议 CircleCollider2D）。");
        }
        else
        {
            bodyCollider.isTrigger = false;
            bodyRadius = Mathf.Max(0.05f, bodyCollider.bounds.extents.x);
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            // 允许只挂脚本、由脚本自动补 Rigidbody2D，省去手动加组件
            rb = gameObject.AddComponent<Rigidbody2D>();
        }

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        ConfigureRigidbody();
        LockAt(initialPosition, initialRotation);

        if (autoCreateStairZones) CreateStairZones();
    }

    private void Start()
    {
        FindPlayer();
    }

    private void ConfigureRigidbody()
    {
        rb.gravityScale = 1f;
        rb.mass = 1f;
        rb.drag = linearDrag;
        rb.angularDrag = 0.05f;
        rb.freezeRotation = false;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void Update()
    {
        HandlePlayerRespawn();
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        // 反弹飞行中：只累计计时，速度交给物理（重力）自行演算
        if (state == BoulderState.Rebounding)
        {
            reboundTimer += Time.fixedDeltaTime;

            // 飞行中保持视觉滚动
            if (forceVisualRoll)
            {
                rb.angularVelocity = -(rb.velocity.x / Mathf.Max(0.05f, bodyRadius)) * Mathf.Rad2Deg;
            }
            return;
        }

        if (state != BoulderState.Rolling) return;

        // 1) 朝滚动方向持续加速（直接改速度，便于按"加速度/最高速"调试）
        float target = rollDirection * maxRollSpeed;
        float nextVx = Mathf.MoveTowards(rb.velocity.x, target, rollAcceleration * Time.fixedDeltaTime);
        rb.velocity = new Vector2(nextVx, rb.velocity.y);

        // 2) 强制视觉滚动
        if (forceVisualRoll)
        {
            rb.angularVelocity = -(nextVx / Mathf.Max(0.05f, bodyRadius)) * Mathf.Rad2Deg;
        }

        // 3) 卡住判定：水平速度长时间不足认为撞上障碍物
        if (Mathf.Abs(rb.velocity.x) < stallSpeedThreshold)
        {
            stallTimer += Time.fixedDeltaTime;
            if (stallTimer >= stallTimeToStop)
            {
                if (debugLogs) Debug.Log($"[{name}] 速度长时间过低，判定停下。");
                StopAndHold();
            }
        }
        else
        {
            stallTimer = 0f;
        }
    }
    #endregion

    #region 对外触发接口（只允许 BoulderStairTrigger 调用）
    /// <summary>让巨石向左滚（左阶梯触发）。已触发过则忽略。</summary>
    public void RollLeft() => Roll(-1);

    /// <summary>让巨石向右滚（右阶梯触发）。已触发过则忽略。</summary>
    public void RollRight() => Roll(1);

    private void Roll(int direction)
    {
        // 关键：只有"锁定"状态才能被触发，滚过一次 / 正在滚 / 已停下都不会再触发
        if (state != BoulderState.Locked) return;

        rollDirection = direction < 0 ? -1 : 1;
        stallTimer = 0f;
        state = BoulderState.Rolling;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.isKinematic = false;
        rb.simulated = true;
        rb.drag = linearDrag;
        rb.velocity = new Vector2(rollDirection * initialPushSpeed, 0f);
        rb.angularVelocity = 0f;
        rb.WakeUp();

        if (debugLogs) Debug.Log($"[{name}] 触发滚动，方向={(rollDirection < 0 ? "左" : "右")}");
    }

    private void StopAndHold()
    {
        if (state == BoulderState.Sunk) return;

        state = BoulderState.Stopped;
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }
    #endregion

    #region 碰撞：扎死小羊 / 撞炮 / 流沙
    private void OnCollisionEnter2D(Collision2D collision) => HandleContact(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => HandleContact(collision.collider);
    private void OnTriggerEnter2D(Collider2D other) => HandleContact(other);
    private void OnTriggerStay2D(Collider2D other) => HandleContact(other);

    private void HandleContact(Collider2D other)
    {
        if (other == null || state == BoulderState.Sunk) return;

        // 1) 流沙：滚到流沙上 → 被吞没（向右滚的连锁反应）
        QuicksandTrap quicksand = other.GetComponentInParent<QuicksandTrap>();
        if (quicksand != null)
        {
            BeginSink(quicksand);
            return;
        }

        // 2) 加农炮：撞上 → 触发炸毁，随后被反作用力弹向右侧（向左滚的连锁反应）
        CannonDestruction cannon = other.GetComponentInParent<CannonDestruction>();
        if (cannon != null && !cannon.IsDestroyed)
        {
            if (debugLogs) Debug.Log($"[{name}] 撞上加农炮，触发摧毁。");
            destroyedCannon = cannon;
            cannon.DestroyCannon();

            if (bounceOffCannon) BeginRebound();
            else StopAndHold();
            return;
        }

        // 3) 扎死小羊
        if (killPlayerOnTouch)
        {
            BunnyController bunny = BunnyContactUtility.FindBunny(other);
            if (bunny != null && !bunny.isDead)
            {
                bunny.TriggerDeath();
            }
        }

        // 4) 反弹飞行落地：过了忽略碰撞时间后，第一次真正接触就停下
        if (state == BoulderState.Rebounding && reboundTimer >= reboundIgnoreCollisionTime)
        {
            if (debugLogs) Debug.Log($"[{name}] 反弹落地，停下。");
            StopAndHold();
        }
    }

    /// <summary>撞炮后被反作用力弹向右侧：给定向上 + 向右的速度，让物理演算抛物线。</summary>
    private void BeginRebound()
    {
        state = BoulderState.Rebounding;
        reboundTimer = 0f;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.isKinematic = false;
            rb.simulated = true;

            // x 强制取正值（向右），避免配置成向左把石头又推回炮的位置
            float vx = Mathf.Abs(cannonBounceVelocity.x);
            float vy = cannonBounceVelocity.y;
            rb.velocity = new Vector2(vx, vy);
            rb.angularVelocity = -(vx / Mathf.Max(0.05f, bodyRadius)) * Mathf.Rad2Deg;
            rb.WakeUp();
        }
    }
    #endregion

    #region 流沙吞没
    private void BeginSink(QuicksandTrap quicksand)
    {
        if (state == BoulderState.Sunk || sinkRoutine != null) return;

        state = BoulderState.Sunk;
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
        if (bodyCollider != null) bodyCollider.enabled = false;

        float depth = quicksand != null ? quicksand.BoulderSinkDepth : fallbackSinkDepth;
        float duration = quicksand != null && quicksand.BoulderSinkDuration > 0f
            ? quicksand.BoulderSinkDuration
            : fallbackSinkDuration;

        if (debugLogs) Debug.Log($"[{name}] 滚入流沙，开始下沉。");

        sinkRoutine = StartCoroutine(SinkRoutine(depth, duration));
    }

    private IEnumerator SinkRoutine(float depth, float duration)
    {
        Vector3 start = transform.position;
        Vector3 target = start + Vector3.down * depth;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration)));
            SetPosition(Vector3.Lerp(start, target, t));
            yield return null;
        }

        SetPosition(target);

        // 下沉结束后隐藏（模拟完全没入流沙）
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        sinkRoutine = null;
    }
    #endregion

    #region 复活重置
    private void HandlePlayerRespawn()
    {
        if (!resetOnPlayerRespawn) return;

        if (player == null) FindPlayer();
        if (player == null) return;

        if (player.isDead)
        {
            if (!playerWasDead)
            {
                playerWasDead = true;
                // 死亡瞬间先让巨石停住，等复活再整体复位
                if (state == BoulderState.Rolling || state == BoulderState.Rebounding) StopAndHold();
            }
            return;
        }

        // 玩家从"死亡"变成"存活"的那一帧 = 复活瞬间 → 严格复位
        if (playerWasDead)
        {
            playerWasDead = false;
            ResetBoulder();
            if (debugLogs) Debug.Log($"[{name}] 玩家复活，巨石复位到金字塔顶端。");
        }
    }

    /// <summary>把巨石复位到金字塔顶端并重新锁定。</summary>
    public void ResetBoulder()
    {
        if (sinkRoutine != null)
        {
            StopCoroutine(sinkRoutine);
            sinkRoutine = null;
        }

        if (bodyCollider != null) bodyCollider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        // 复活时把被这颗滚石炸毁的加农炮一并恢复
        if (destroyedCannon != null)
        {
            destroyedCannon.ResetCannon();
            destroyedCannon = null;
        }

        rollDirection = 0;
        stallTimer = 0f;
        reboundTimer = 0f;
        LockAt(initialPosition, initialRotation);
        state = BoulderState.Locked;
    }

    private void LockAt(Vector3 position, Quaternion rotation)
    {
        transform.position = position;
        transform.rotation = rotation;

        if (rb == null) return;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.isKinematic = true;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.position = position;
        rb.rotation = rotation.eulerAngles.z;
    }

    private void SetPosition(Vector3 position)
    {
        transform.position = position;
        if (rb != null) rb.position = position;
    }

    private void FindPlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.GetComponent<BunnyController>();
    }
    #endregion

    #region 自动生成左右阶梯触发区
    private void CreateStairZones()
    {
        CreateZone("BoulderLeftStairZone", leftZoneCenter, leftZoneSize, BoulderStairTrigger.StairSide.Left);
        CreateZone("BoulderRightStairZone", rightZoneCenter, rightZoneSize, BoulderStairTrigger.StairSide.Right);
    }

    private void CreateZone(string zoneName, Vector2 center, Vector2 size, BoulderStairTrigger.StairSide side)
    {
        if (size.x <= 0f || size.y <= 0f) return;

        var go = new GameObject(zoneName);
        go.transform.position = new Vector3(center.x, center.y, 0f);

        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;

        var trigger = go.AddComponent<BoulderStairTrigger>();
        trigger.Configure(side, this);
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        // 初始锁定点
        Vector3 origin = Application.isPlaying ? initialPosition : transform.position;
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(origin, 0.4f);

        if (autoCreateStairZones)
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(leftZoneCenter.x, leftZoneCenter.y, 0f),
                new Vector3(leftZoneSize.x, leftZoneSize.y, 0.1f));

            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(rightZoneCenter.x, rightZoneCenter.y, 0f),
                new Vector3(rightZoneSize.x, rightZoneSize.y, 0.1f));
        }

        if (bounceOffCannon) DrawReboundPreview();
    }

    /// <summary>在 Scene 里按 cannonBounceVelocity 画出反弹抛物线，方便不开游戏就调手感。</summary>
    private void DrawReboundPreview()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);

        float g = -Physics2D.gravity.y; // 向下为正值
        Vector2 p = reboundPreviewOrigin;
        Vector2 v = new Vector2(Mathf.Abs(cannonBounceVelocity.x), cannonBounceVelocity.y);
        float dt = 0.04f;

        Gizmos.DrawWireSphere(new Vector3(p.x, p.y, 0f), 0.18f);

        for (int i = 0; i < 120; i++)
        {
            Vector2 next = p + v * dt;
            next.y -= 0.5f * g * dt * dt;
            v.y -= g * dt;

            Gizmos.DrawLine(new Vector3(p.x, p.y, 0f), new Vector3(next.x, next.y, 0f));
            p = next;

            if (p.y < reboundPreviewOrigin.y - 6f) break;
        }
    }
    #endregion
}
