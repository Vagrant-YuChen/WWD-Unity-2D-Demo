using UnityEngine;

/// <summary>
/// 地图边缘的弩：从游戏开始到结束，按固定间隔朝指定方向不停发射匀速箭矢。
///
/// 重点调试参数都在 Inspector 里：
///   - Fire Interval：两发箭之间的间隔（秒），越小射得越密；
///   - Arrow Speed：箭矢飞行速度（世界单位/秒），发射时覆盖箭矢预制体的默认速度；
///   - Fire Direction：发射方向，默认向左 (-1, 0)；
///   - Muzzle / Muzzle Offset：箭矢出生的枪口位置。
///
/// 箭矢本身是匀速直线飞行（见 ArrowProjectile），不加速度、不受重力。
/// 脚本只负责“什么时候射、从哪射、往哪射、射多快”，没有任何停止条件。
/// </summary>
[DisallowMultipleComponent]
public class CrossbowShooter : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 箭矢预制体 ===")]
    [Tooltip("要发射的箭矢预制体（必须带 ArrowProjectile）。" +
             "一般由 Tools/Arrow Trap 菜单自动生成并赋值，不需要手动拖")]
    [SerializeField] private GameObject arrowPrefab;
    [Tooltip("箭矢的父物体。留空则放到场景根节点，避免跟着弩一起移动 / 旋转 / 缩放")]
    [SerializeField] private Transform arrowParent;

    [Header("=== 射击节奏（重点调试区） ===")]
    [Tooltip("两发箭之间的间隔（秒）。0.6 ≈ 每秒 1.6 发；0.2 ≈ 每秒 5 发；越小越密")]
    [SerializeField, Min(0.02f)] private float fireInterval = 0.6f;
    [Tooltip("开局后延迟多久射出第一发（秒），0 = 立刻开火")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    [Header("=== 箭矢速度（重点调试区） ===")]
    [Tooltip("箭矢飞行速度（世界单位/秒）。匀速直线飞行，不会加速也不会下坠")]
    [SerializeField, Min(0.01f)] private float arrowSpeed = 8f;

    [Header("=== 发射方向 / 枪口 ===")]
    [Tooltip("发射方向（世界空间）。默认向左 (-1, 0)；也可以写任意角度")]
    [SerializeField] private Vector2 fireDirection = Vector2.left;
    [Tooltip("枪口位置。留空时使用「自身位置 + 枪口偏移」")]
    [SerializeField] private Transform muzzle;
    [Tooltip("枪口偏移（自身局部坐标）。十字弓精灵通常朝上绘制，局部 +Y 方向就是弓臂箭头射出的位置")]
    [SerializeField] private Vector2 muzzleOffset = new Vector2(0f, 0.55f);
    [Tooltip("出生点沿发射方向再前移的距离，避免箭矢一出生就和弓身重叠")]
    [SerializeField, Min(0f)] private float spawnClearance = 0.35f;

    [Header("=== 射击动画 ===")]
    [Tooltip("弩身的 Animator。留空会在 Awake 自动取同物体上的 Animator")]
    [SerializeField] private Animator shooterAnimator;
    [Tooltip("弩身持续射击动画的状态名（对应 ArrowShooter.controller 里的状态）")]
    [SerializeField] private string shooterStateName = "ArrowShooter";
    [Tooltip("射击剪辑没有勾 Loop 时，脚本会在播完后自动从头重播，保证一刻不停地循环")]
    [SerializeField] private bool loopShooterAnimation = true;

    [Header("=== 射箭动画 / Idle 同步 ===")]
    [Tooltip("开启后：每隔 Fire Interval 先完整播放一遍 ArrowShooter（拉弓上箭→射出→收势），" +
             "播完自动回到 Idle（静态第 1 帧），停到下一发再重复。关闭则退回旧版：动画一直循环")]
    [SerializeField] private bool syncAnimationWithShots = true;
    [Tooltip("待机状态名（ArrowShooter.controller 里的 Idle 状态）")]
    [SerializeField] private string idleStateName = "Idle";
    [Tooltip("箭矢在动画的哪个进度真正飞出：0=动画第一帧，1=动画最后一帧。" +
             "当前 25 帧素材第 13 帧出箭，约 0.52")]
    [SerializeField, Range(0f, 1f)] private float arrowReleaseNormalizedTime = 0.52f;
    [Tooltip("完整播完射箭动画后至少保留的 Idle 时间（秒），让玩家看清弩停住的样子")]
    [SerializeField, Min(0f)] private float minimumIdleTime = 0.2f;
    [Tooltip("Fire Interval 比整段射箭动画还短时，自动延长到完整播放，保证每次都能播完再进 Idle。" +
             "关闭后允许下一发打断当前动画")]
    [SerializeField] private bool ensureFullAnimation = true;

    [Header("=== 表现 / 调试 ===")]
    [Tooltip("发射音效（可选）")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [SerializeField] private bool debugLogs = false;

    [Header("=== 渲染层级 ===")]
    [Tooltip("地形 / 沙地 / 法老雕像的排序是 1，玩家是 3。弩如果留在 0 会被它们整个挡住，" +
             "动画帧一变化就像凭空消失。低于该值时自动抬到该值，保证弩始终可见。")]
    [SerializeField] private int minimumSortingOrder = 2;
    [Tooltip("关闭后不再自动修正 SpriteRenderer.sortingOrder，完全由你手动控制")]
    [SerializeField] private bool enforceSortingOrder = true;
    #endregion

    #region 运行时状态
    private float fireTimer;
    private int shooterHash;
    private int idleHash;
    private bool warnedMissingPrefab;

    // Idle / 射击一次 的同步状态
    private bool syncActive;
    private float shootClipDuration;
    private float arrowReleaseDelay;
    private bool arrowPending;
    private float arrowReleaseTimer;
    private bool warnedIntervalClamped;
    #endregion

    #region 对外可调接口（运行时也可以改）
    /// <summary>射击间隔（秒）。外部脚本可实时调整。</summary>
    public float FireInterval
    {
        get => fireInterval;
        set => fireInterval = Mathf.Max(0.02f, value);
    }

    /// <summary>箭矢速度（世界单位/秒）。外部脚本可实时调整，只影响之后发射的箭。</summary>
    public float ArrowSpeed
    {
        get => arrowSpeed;
        set => arrowSpeed = Mathf.Max(0.01f, value);
    }

    /// <summary>发射方向（世界空间，内部会归一化）。</summary>
    public Vector2 FireDirection
    {
        get => fireDirection.sqrMagnitude > 0.0001f ? fireDirection.normalized : Vector2.left;
        set => fireDirection = value;
    }

    /// <summary>枪口世界坐标（和 FireArrow 实际出生点一致，已含 spawnClearance）。</summary>
    public Vector2 SpawnPosition => GetMuzzleWorldPosition() + FireDirection * spawnClearance;
    #endregion

    private void Awake()
    {
        if (shooterAnimator == null) shooterAnimator = GetComponent<Animator>();
        shooterHash = Animator.StringToHash(shooterStateName);
        idleHash = Animator.StringToHash(idleStateName);

        EnforceSortingOrder();
    }

    /// <summary>
    /// 弩的 SpriteRenderer 如果和背景 / 沙地处于同一排序层级或更低，
    /// 会被沙地、法老雕像等 order 1 的精灵整块盖住；动画帧宽高变化后
    /// 只有边缘偶尔露出来，看起来就像"几帧消失了"。这里统一抬到地形之上。
    /// </summary>
    private void EnforceSortingOrder()
    {
        if (!enforceSortingOrder) return;

        SpriteRenderer crossbowRenderer = GetComponent<SpriteRenderer>();
        if (crossbowRenderer == null) crossbowRenderer = GetComponentInChildren<SpriteRenderer>();
        if (crossbowRenderer == null) return;

        if (crossbowRenderer.sortingOrder < minimumSortingOrder)
        {
            if (debugLogs)
                Debug.Log($"[{name}] 弩被地形遮挡（sortingOrder {crossbowRenderer.sortingOrder} < {minimumSortingOrder}），" +
                          $"已提升到 {minimumSortingOrder}。");
            crossbowRenderer.sortingOrder = minimumSortingOrder;
        }
    }

    private void Start()
    {
        shootClipDuration = FindClipLength(shooterStateName);

        // 只有控制器里真的有 Idle 状态时才启用"播完进 Idle"模式，否则退回旧的连续循环
        syncActive = syncAnimationWithShots
            && shooterAnimator != null
            && shooterAnimator.runtimeAnimatorController != null
            && !string.IsNullOrEmpty(idleStateName)
            && shooterAnimator.HasState(0, idleHash);

        if (syncActive)
        {
            arrowReleaseDelay = Mathf.Clamp01(arrowReleaseNormalizedTime) * Mathf.Max(0f, shootClipDuration);
            PlayIdleAnimation();
        }
        else
        {
            PlayShooterAnimation();
        }

        fireTimer = Mathf.Max(0f, startDelay);

        // 提前把"间隔太短会被动画长度顶住"这件事告诉使用者，避免以为 Fire Interval 失效
        if (syncActive && ensureFullAnimation && shootClipDuration > 0f)
        {
            float minInterval = shootClipDuration + minimumIdleTime;
            if (fireInterval < minInterval)
            {
                Debug.LogWarning($"[{name}] Fire Interval = {fireInterval:0.00}s 小于完整射箭动画 + Idle 需要的 " +
                                 $"{minInterval:0.00}s，实际会按 {minInterval:0.00}s 射击。想更快请关闭 Ensure Full Animation。");
            }
        }

        if (debugLogs)
            Debug.Log($"[{name}] 弩启动：间隔 {fireInterval}s，速度 {arrowSpeed}，方向 {FireDirection}，" +
                      $"同步动画={syncActive}");
    }

    private void Update()
    {
        if (syncActive)
        {
            UpdatePendingArrow();
            UpdateShotTimer();
        }
        else
        {
            UpdateShooterAnimation();
            UpdateLegacyFireTimer();
        }
    }

    /// <summary>
    /// 同步模式下的射击计时：间隔 = max(Fire Interval, 动画时长 + 最小 Idle 时间)。
    /// </summary>
    private void UpdateShotTimer()
    {
        float interval = EffectiveShotInterval();

        fireTimer -= Time.deltaTime;
        int safety = 0;
        while (fireTimer <= 0f && safety < 16)
        {
            BeginShotCycle();
            fireTimer += interval;
            safety++;
        }

        if (fireTimer < -interval) fireTimer = 0f;
    }

    /// <summary>
    /// 开始一次完整射击：从第 1 帧播放 ArrowShooter，箭矢在配置的进度（默认第 13 帧）才真正飞出。
    /// 动画播完后由 ArrowShooter.controller 里的 ArrowShooter → Idle 过渡自动进入待机。
    /// </summary>
    private void BeginShotCycle()
    {
        // 极端情况：关闭了 Ensure Full Animation 且间隔比动画还短，上一发箭还没出膛就开下一轮，
        // 这里先把上一发补出来，避免"吞箭"
        if (arrowPending)
        {
            arrowPending = false;
            FireArrow();
        }

        if (shooterAnimator != null)
            shooterAnimator.Play(shooterHash, 0, 0f);

        arrowPending = true;
        arrowReleaseTimer = arrowReleaseDelay;

        if (arrowReleaseTimer <= 0f)
        {
            arrowPending = false;
            FireArrow();
        }
    }

    private void UpdatePendingArrow()
    {
        if (!arrowPending) return;

        arrowReleaseTimer -= Time.deltaTime;
        if (arrowReleaseTimer <= 0f)
        {
            arrowPending = false;

            // 兜底：如果动画提前被打断/切走，仍然把这一发箭补出来
            FireArrow();
        }
    }

    private float EffectiveShotInterval()
    {
        float interval = Mathf.Max(0.02f, fireInterval);

        if (syncActive && ensureFullAnimation && shootClipDuration > 0f)
        {
            float minInterval = shootClipDuration + minimumIdleTime;
            if (interval < minInterval)
            {
                interval = minInterval;
                if (!warnedIntervalClamped)
                {
                    warnedIntervalClamped = true;
                    Debug.LogWarning($"[{name}] Fire Interval 被动画长度限制为 {interval:0.00}s（完整动画 {shootClipDuration:0.00}s + Idle {minimumIdleTime:0.00}s）。");
                }
            }
        }

        return interval;
    }

    /// <summary>旧版行为：不区分 Idle，按间隔直接出箭，动画自己循环。</summary>
    private void UpdateLegacyFireTimer()
    {
        fireTimer -= Time.deltaTime;

        float interval = Mathf.Max(0.02f, fireInterval);
        int safety = 0;
        while (fireTimer <= 0f && safety < 16)
        {
            FireArrow();
            fireTimer += interval;
            safety++;
        }

        if (fireTimer < -interval) fireTimer = 0f;
    }

    private float FindClipLength(string stateName)
    {
        if (shooterAnimator == null || shooterAnimator.runtimeAnimatorController == null) return 0f;

        AnimationClip[] clips = shooterAnimator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == stateName) return clips[i].length;
        }

        return 0f;
    }

    #region 射击
    private void FireArrow()
    {
        if (arrowPrefab == null)
        {
            if (!warnedMissingPrefab)
            {
                warnedMissingPrefab = true;
                Debug.LogError($"[{name}] 没有配置箭矢预制体，无法射击。" +
                               "请运行菜单 Tools/Arrow Trap/Setup Crossbows In Open Scene。");
            }
            return;
        }

        Vector2 dir = FireDirection;
        Vector3 spawnPos = GetMuzzleWorldPosition() + dir * spawnClearance;

        GameObject arrowGo = Instantiate(arrowPrefab, spawnPos, Quaternion.identity, arrowParent);
        ArrowProjectile arrow = arrowGo.GetComponent<ArrowProjectile>();
        if (arrow == null) arrow = arrowGo.GetComponentInChildren<ArrowProjectile>();

        if (arrow == null)
        {
            Debug.LogError($"[{name}] 箭矢预制体 {arrowPrefab.name} 上没有 ArrowProjectile 组件。");
            Destroy(arrowGo);
            return;
        }

        arrow.Launch(dir, arrowSpeed, this);

        if (fireSound != null)
        {
            AudioSource.PlayClipAtPoint(fireSound, spawnPos, volume);
        }

        if (debugLogs) Debug.Log($"[{name}] 发射箭矢：pos={spawnPos}, dir={dir}, speed={arrowSpeed}");
    }

    private Vector2 GetMuzzleWorldPosition()
    {
        if (muzzle != null) return muzzle.position;
        return transform.TransformPoint(muzzleOffset);
    }
    #endregion

    #region 射击动画循环
    private void PlayIdleAnimation()
    {
        if (shooterAnimator == null || shooterAnimator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(idleStateName)) return;

        shooterAnimator.Play(idleHash, 0, 0f);
    }

    private void PlayShooterAnimation()
    {
        if (shooterAnimator == null || shooterAnimator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(shooterStateName)) return;

        shooterAnimator.Play(shooterHash, 0, 0f);
    }

    private void UpdateShooterAnimation()
    {
        if (!loopShooterAnimation || shooterAnimator == null || shooterAnimator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(shooterStateName)) return;
        if (shooterAnimator.IsInTransition(0)) return;

        AnimatorStateInfo state = shooterAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != shooterHash) return;

        // 循环剪辑由 Animator 自己循环；非循环剪辑播到头就手动重播
        if (!state.loop && state.normalizedTime >= 0.999f)
        {
            shooterAnimator.Play(shooterHash, 0, 0f);
        }
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        Vector2 dir = FireDirection;
        Vector3 muzzlePos = GetMuzzleWorldPosition();
        Vector3 spawnPos = muzzlePos + (Vector3)(dir * spawnClearance);

        // 枪口与出生点
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(muzzlePos, 0.08f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(spawnPos, 0.08f);

        // 发射方向与射速示意（每 1 秒的飞行距离）
        Gizmos.color = Color.yellow;
        Vector3 tip = spawnPos + (Vector3)dir * Mathf.Max(1f, arrowSpeed);
        Gizmos.DrawLine(spawnPos, tip);

        for (int i = 1; i <= 4; i++)
        {
            Gizmos.DrawWireSphere(spawnPos + (Vector3)dir * (Mathf.Max(1f, arrowSpeed) * i / 4f), 0.05f);
        }

        // 射击间隔的直观提示：在 Scene 视图里标出每秒能射几发
        if (fireInterval > 0f)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(spawnPos + (Vector3)dir * 0.4f, new Vector3(0.1f, 0.1f, 0.1f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(spawnPos + (Vector3)dir * 0.6f + Vector3.up * 0.25f,
                $"每 {fireInterval:0.00}s 一发（{1f / fireInterval:0.0} 发/秒）\n速度 {arrowSpeed:0.0}/s");
#endif
        }
    }
    #endregion
}
