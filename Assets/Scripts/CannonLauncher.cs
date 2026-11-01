using System.Collections;
using UnityEngine;

// 加农炮：玩家靠近按住 F 进入炮口并蓄力，松开发射（蓄满自动发射）。
// 发射轨迹为受重力的抛物线，飞行中玩家可用 A/D 微调，力度越大飞得越远。
[RequireComponent(typeof(Collider2D))]
public class CannonLauncher : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 交互 ===")]
    [Tooltip("可进入加农炮的距离（越小越需贴近；Scene 视图黄色球即该范围，可自行调试）")]
    [SerializeField] private float interactDistance = 1f;
    [Tooltip("兔子所在层（Bunny 在 Default 层时保持 1）")]
    [SerializeField] private LayerMask bunnyLayer = 1;

    [Header("=== 发射弹道 ===")]
    [Tooltip("炮口位置（世界坐标），留空则用自身位置")]
    [SerializeField] private Transform muzzle;
    [Tooltip("发射仰角（度，相对水平方向）")]
    [SerializeField, Range(10f, 85f)] private float launchAngle = 47.5f;
    [Tooltip("勾选朝右发射，取消朝左发射")]
    [SerializeField] private bool fireRight = true;
    [Tooltip("轻点（几乎不蓄力）时的初速度：距离很短")]
    [SerializeField] private float minLaunchSpeed = 5f;
    [Tooltip("满蓄力（最远）时的初速度")]
    [SerializeField] private float maxLaunchSpeed = 16.3f;
    [Tooltip("飞行重力缩放：决定抛物线弧度，越小弧越平")]
    [SerializeField] private float launchGravity = 1.2f;
    [Tooltip("出生点沿发射方向前移的距离，避免出生在炮身碰撞盒内部被挤歪")]
    [SerializeField] private float spawnClearance = 0.8f;
    [Tooltip("发射后忽略与炮身碰撞的时长（秒），避免炮口残余接触干扰弹道")]
    [SerializeField] private float ignoreCannonCollisionTime = 0.4f;

    [Header("=== 蓄力 ===")]
    [Tooltip("最大蓄力时间（秒）：按满到点即使仍按着也强制发射")]
    [SerializeField] private float maxChargeTime = 3f;

    [Header("=== 表现 ===")]
    [Tooltip("炮身 Animator（留空自动 GetComponent<Animator>()）")]
    [SerializeField] private Animator cannonAnimator;
    [Tooltip("Animator 中发射用的 Trigger 名")]
    [SerializeField] private string fireTrigger = "Fire";
    [Tooltip("炮口烟特效（序列帧/粒子预制体，会自带播放并在 smokeLifetime 后销毁）")]
    [SerializeField] private GameObject muzzleSmoke;
    [Tooltip("烟特效存活时间（秒）")]
    [SerializeField] private float smokeLifetime = 1f;
    [SerializeField] private AudioClip fireSound;
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.8f;
    #endregion

    #region 私有状态
    private AudioSource audioSource;
    private Transform playerTransform;
    private BunnyController loadedBunny;
    private Rigidbody2D loadedRb;
    private SpriteRenderer loadedRenderer;
    private float currentCharge;
    private bool isCharging;
    private Coroutine restoreCollisionRoutine;
    #endregion

    #region 对外只读接口（镜头 / UI 等外部系统用）
    /// <summary>是否正在蓄力。</summary>
    public bool IsCharging => isCharging;

    /// <summary>当前蓄力比例（0..1）。</summary>
    public float ChargeRatio => GetChargeRatio();

    /// <summary>炮口 Transform（未配置时回退到自身）。</summary>
    public Transform Muzzle => muzzle != null ? muzzle : transform;

    /// <summary>实际出膛点（已沿发射方向前移 spawnClearance，与 Fire() 完全一致）。</summary>
    public Vector2 LaunchOrigin => (Vector2)Muzzle.position + LaunchDirection() * spawnClearance;

    /// <summary>当前发射方向（单位向量）。</summary>
    public Vector2 LaunchDirectionVector => LaunchDirection();

    /// <summary>指定蓄力比例下的初速度（世界空间向量）。</summary>
    public Vector2 LaunchVelocity(float chargeRatio)
    {
        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(chargeRatio));
        return LaunchDirection() * speed;
    }

    /// <summary>飞行时实际使用的重力加速度（正数，向下）。</summary>
    public float LaunchGravity => -Physics2D.gravity.y * Mathf.Max(0.01f, launchGravity);

    /// <summary>开始蓄力。</summary>
    public event System.Action ChargeStarted;
    /// <summary>蓄力比例变化（参数 0..1）。</summary>
    public event System.Action<float> ChargeChanged;
    /// <summary>发射（参数为世界空间初速度）。</summary>
    public event System.Action<Vector2> Fired;
    #endregion

    private void Awake()
    {
        if (cannonAnimator == null) cannonAnimator = GetComponent<Animator>();
        if (muzzle == null) muzzle = transform;

        if (fireSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.volume = volume;
        }
    }

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        // 还没装载玩家：尝试进入
        if (loadedBunny == null)
        {
            TryEnterCannon();
            return;
        }

        HandleChargeAndFire();
    }

    #region 进入炮口
    private void TryEnterCannon()
    {
        if (playerTransform == null) return;
        if (Vector2.Distance(transform.position, playerTransform.position) > interactDistance) return;
        if (!Input.GetKeyDown(KeyCode.F)) return;

        BunnyController bunny = playerTransform.GetComponent<BunnyController>();
        if (bunny == null || bunny.isDead) return;

        EnterCannon(bunny);
    }

    private void EnterCannon(BunnyController bunny)
    {
        loadedBunny = bunny;
        loadedRb = bunny.GetComponent<Rigidbody2D>();
        loadedRenderer = bunny.GetComponent<SpriteRenderer>();

        // 冻结控制器、停止物理、藏进炮口
        bunny.enabled = false;
        if (loadedRb != null)
        {
            loadedRb.velocity = Vector2.zero;
            loadedRb.isKinematic = true;
        }
        if (loadedRenderer != null) loadedRenderer.enabled = false;

        bunny.transform.position = muzzle.position;

        currentCharge = 0f;
        isCharging = true;

        ChargeStarted?.Invoke();
        ChargeChanged?.Invoke(0f);
    }
    #endregion

    #region 蓄力 / 发射
    private void HandleChargeAndFire()
    {
        // 进入当帧不结算，从下一帧开始按 F 计蓄力
        if (Input.GetKey(KeyCode.F))
        {
            currentCharge += Time.deltaTime;
            ChargeChanged?.Invoke(GetChargeRatio());

            if (currentCharge >= maxChargeTime)
            {
                // 到点即使仍按着也强制发射
                currentCharge = maxChargeTime;
                Fire();
            }
        }
        else
        {
            // 松开：按当前蓄力发射（轻点即短距离）
            Fire();
        }
    }

    private void Fire()
    {
        if (loadedBunny == null) { isCharging = false; return; }

        BunnyController bunny = loadedBunny;
        Vector2 dir = LaunchDirection();
        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, GetChargeRatio());
        Vector2 velocity = dir * speed;

        // 出生点沿发射方向前移，保证不在炮身碰撞盒内部
        Vector2 spawnPos = (Vector2)muzzle.position + dir * spawnClearance;

        bunny.transform.position = spawnPos;
        bunny.enabled = true;
        if (loadedRb != null) loadedRb.isKinematic = false;
        if (loadedRenderer != null) loadedRenderer.enabled = true;

        // 短暂忽略与炮身的碰撞，避免炮口残余接触把弹道挤歪
        SetIgnoreCannonCollision(bunny, true);
        if (restoreCollisionRoutine != null) StopCoroutine(restoreCollisionRoutine);
        restoreCollisionRoutine = StartCoroutine(RestoreCollisionAfter(bunny));

        bunny.ApplyLaunch(velocity, launchGravity);

        loadedBunny = null;
        loadedRb = null;
        loadedRenderer = null;
        isCharging = false;

        Fired?.Invoke(velocity);
        ChargeChanged?.Invoke(0f);

        PlayFireEffects();
    }

    private void PlayFireEffects()
    {
        TryPlayCannonFire();

        if (muzzleSmoke != null)
        {
            GameObject smoke = Instantiate(muzzleSmoke, muzzle.position, muzzle.rotation);
            if (smokeLifetime > 0f) Destroy(smoke, smokeLifetime);
        }

        if (audioSource != null && fireSound != null)
        {
            audioSource.PlayOneShot(fireSound, volume);
        }
    }

    /// <summary>
    /// 播放炮身发射动画：
    /// 若 Animator 中存在同名 Trigger 参数则用 Trigger（推荐的标准做法）；
    /// 若尚未配置 Trigger（当前 controller 就是这种情况），则把默认的发射状态从第 0 帧重播一次。
    /// </summary>
    private void TryPlayCannonFire()
    {
        if (cannonAnimator == null || string.IsNullOrEmpty(fireTrigger)) return;

        bool hasTrigger = false;
        foreach (var p in cannonAnimator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == fireTrigger)
            {
                hasTrigger = true;
                break;
            }
        }

        if (hasTrigger)
        {
            cannonAnimator.SetTrigger(fireTrigger);
        }
        else
        {
            // 退化方案：把当前默认状态（发射序列帧）从头重播
            var state = cannonAnimator.GetCurrentAnimatorStateInfo(0);
            cannonAnimator.Play(state.fullPathHash, 0, 0f);
        }
    }

    private Vector2 LaunchDirection()
    {
        float sign = fireRight ? 1f : -1f;
        float rad = launchAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad) * sign, Mathf.Sin(rad));
    }

    private float GetChargeRatio() =>
        maxChargeTime > 0f ? Mathf.Clamp01(currentCharge / maxChargeTime) : 1f;
    #endregion

    #region 碰撞忽略
    private void SetIgnoreCannonCollision(BunnyController bunny, bool ignore)
    {
        if (bunny == null) return;
        Collider2D[] bunnyCols = bunny.GetComponentsInChildren<Collider2D>();
        Collider2D[] cannonCols = GetComponentsInChildren<Collider2D>();

        foreach (var bc in bunnyCols)
        {
            if (bc == null) continue;
            foreach (var cc in cannonCols)
            {
                if (cc == null) continue;
                Physics2D.IgnoreCollision(bc, cc, ignore);
            }
        }
    }

    private IEnumerator RestoreCollisionAfter(BunnyController bunny)
    {
        yield return new WaitForSeconds(ignoreCannonCollisionTime);
        SetIgnoreCannonCollision(bunny, false);
        restoreCollisionRoutine = null;
    }
    #endregion

    #region Gizmos 弹道预览
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactDistance);

        Transform m = muzzle != null ? muzzle : transform;
        Vector2 dir = LaunchDirection();
        Vector2 start = (Vector2)m.position + dir * spawnClearance;

        // 满蓄力弹道（红色，代表最远距离；也是真正的抛物线轨迹）
        DrawTrajectory(start, dir * maxLaunchSpeed, Color.red);

        // 正在蓄力时叠加当前蓄力弹道（绿色）
        if (isCharging)
        {
            float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, GetChargeRatio());
            DrawTrajectory(start, dir * speed, Color.green);
        }
    }

    private void DrawTrajectory(Vector2 start, Vector2 velocity, Color color)
    {
        Gizmos.color = color;

        // 与飞行完全一致的重力加速度：globalGravity * launchGravity（向下）
        float g = -Physics2D.gravity.y * Mathf.Max(0.01f, launchGravity);

        float dt = 0.05f;
        Vector2 p = start;
        Vector2 v = velocity;

        for (int i = 0; i < 60; i++)
        {
            Vector2 next = p + v * dt;
            next.y -= 0.5f * g * dt * dt;
            v.y -= g * dt;

            Gizmos.DrawLine(p, next);
            p = next;

            // 模拟落地（粗略）：低于起始点一定距离就停
            if (p.y < start.y - 20f) break;
        }
    }
    #endregion
}
