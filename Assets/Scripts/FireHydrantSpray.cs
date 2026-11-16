using UnityEngine;

/// <summary>
/// 消防栓喷水陷阱。
///
/// 玩法：
///   1) 从游戏开始到结束不停循环播放 FireHydrantSpray 序列帧；
///   2) 一整套动画（000 蓄力 → 007~014 正式喷水 → 015~019 消散）播完后，
///      自动回到 000 的 Idle 姿势停一小会儿，再重新喷，节奏和 CrossbowShooter 类似；
///   3) 只有 007~014 这几帧的水柱会造成伤害，蓄力和消散阶段完全无害。
///      帧区间、伤害盒大小/偏移、待机时长都可以在 Inspector 里调。
///
/// 动画状态机由 Assets/Editor/FireHydrantSetup.cs 自动填好：
///   Spray = 完整 20 帧剪辑（不循环）
///   Idle  = 只显示第 000 帧的待机剪辑（循环）
/// 如果你的工程里状态机还是空的，运行菜单
///   Tools/Fire Hydrant/Setup Fire Hydrants In Open Scene
/// 即可一键补全。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FireHydrantSpray : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 动画（自动装配会填好） ===")]
    [Tooltip("消防栓身上的 Animator。留空会在 Awake 自动获取")]
    [SerializeField] private Animator hydrantAnimator;
    [Tooltip("完整喷水序列帧 FireHydrantSpray.anim。用来读取帧率 / 时长，也是状态机缺失时的兜底播放")]
    [SerializeField] private AnimationClip sprayClip;
    [Tooltip("只显示第 000 帧的待机剪辑 FireHydrantSprayIdle.anim")]
    [SerializeField] private AnimationClip idleClip;
    [Tooltip("喷水状态名（FireHydrantSpray.controller 里的状态名）")]
    [SerializeField] private string sprayStateName = "Spray";
    [Tooltip("待机状态名（FireHydrantSpray.controller 里的状态名）")]
    [SerializeField] private string idleStateName = "Idle";

    [Header("=== 播放节奏（重点调试区） ===")]
    [Tooltip("整套喷水动画播完后，停留在 000 待机帧的时长（秒）")]
    [SerializeField, Min(0f)] private float idleHoldTime = 0.3f;
    [Tooltip("开局后延迟多久开始第一次喷水（秒），0 = 立刻开始")]
    [SerializeField, Min(0f)] private float startDelay = 0f;
    [Tooltip("整体播放速度倍率。1 = 原始 12 帧/秒；2 = 快一倍")]
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

    [Header("=== 水柱伤害判定（重点调试区） ===")]
    [Tooltip("要检测的层。默认 Everything；脚本只认 BunnyController，所以多选出别的层也不会误伤")]
    [SerializeField] private LayerMask playerLayer = ~0;
    [Tooltip("伤害盒中心相对消防栓的本地偏移（会跟随消防栓的缩放）。默认对准 007-014 水柱的中段")]
    [SerializeField] private Vector2 damageBoxOffset = new Vector2(0f, 1.65f);
    [Tooltip("伤害盒大小（本地单位，会跟随消防栓的缩放）。默认 1.19 x 2.41 正好覆盖 007-014 的水柱")]
    [SerializeField] private Vector2 damageBoxSize = new Vector2(1.19f, 2.41f);
    [Tooltip("从第几帧开始造成伤害（含）。当前素材 007 开始正式喷水")]
    [SerializeField, Min(0)] private int damageStartFrame = 7;
    [Tooltip("到第几帧结束造成伤害（含）。当前素材 014 结束后进入消散，015 起无害")]
    [SerializeField, Min(0)] private int damageEndFrame = 14;
    [Tooltip("序列帧总帧数。0 = 自动按「剪辑时长 × 帧率」计算（当前素材会自动算出 20）")]
    [SerializeField, Min(0)] private int totalFramesOverride = 0;
    [Tooltip("命中时是否杀死小羊")]
    [SerializeField] private bool killPlayerOnHit = true;

    // 死亡语音已统一由角色控制器（SheepController.TriggerDeath）播放，
    // 此陷阱不再单独配置死亡音效，避免音量不一致 / 3D 距离衰减。

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    [Tooltip("在 Scene 视图画出水柱伤害范围，方便直接对着水柱调大小和位置")]
    [SerializeField] private bool drawDamageGizmo = true;
    [SerializeField] private Color damageGizmoColor = new Color(1f, 0.15f, 0.1f, 0.35f);
    #endregion

    #region 运行时状态
    private int sprayHash;
    private int idleHash;
    private bool hasSprayState;
    private bool hasIdleState;
    private bool manualSampling;
    private float sprayLength = 1.6667f;
    private float clipFrameRate = 12f;
    private int totalFrames = 20;
    private float sprayClock;
    private float idleClock;
    private bool spraying;
    private bool initialized;
    private bool warnedMissingAnimation;
    #endregion

    #region 对外只读接口（方便其他脚本 / 调试）
    /// <summary>当前这一帧是否处于会造成伤害的水柱阶段。</summary>
    public bool IsDamaging { get; private set; }

    /// <summary>是否正在播放喷水动画（false = 停留在 000 待机）。</summary>
    public bool IsSpraying => spraying;

    /// <summary>当前喷水动画的归一化进度（0 = 第一帧，1 = 播完）。</summary>
    public float NormalizedTime => sprayLength > 0.0001f ? Mathf.Clamp01(sprayClock / sprayLength) : 0f;

    /// <summary>当前序列帧序号（0 起）。</summary>
    public int CurrentFrame =>
        Mathf.Clamp(Mathf.FloorToInt(NormalizedTime * totalFrames), 0, Mathf.Max(0, totalFrames - 1));
    #endregion

    private void Reset()
    {
        hydrantAnimator = GetComponent<Animator>();
    }

    private void Awake()
    {
        if (hydrantAnimator == null) hydrantAnimator = GetComponent<Animator>();

        sprayHash = Animator.StringToHash(sprayStateName);
        idleHash = Animator.StringToHash(idleStateName);
    }

    private void Start()
    {
        ResolveClipInfo();

        if (hydrantAnimator != null)
        {
            hydrantAnimator.speed = Mathf.Max(0.01f, playbackSpeed);

            bool hasController = hydrantAnimator.runtimeAnimatorController != null;
            hasSprayState = hasController && hydrantAnimator.HasState(0, sprayHash);
            hasIdleState = hasController && hydrantAnimator.HasState(0, idleHash);
        }

        // 状态机还没填好时，直接用 AnimationClip 采样，保证玩法依然能跑
        manualSampling = !hasSprayState && sprayClip != null;
        if (manualSampling && hydrantAnimator != null) hydrantAnimator.enabled = false;

        if (!hasSprayState && sprayClip == null && !warnedMissingAnimation)
        {
            warnedMissingAnimation = true;
            Debug.LogWarning($"[{name}] 找不到喷水动画。请运行菜单 Tools/Fire Hydrant/Setup Fire Hydrants In Open Scene 补全状态机。", this);
        }

        initialized = true;

        if (startDelay > 0f) BeginIdle(startDelay);
        else BeginSpray();

        if (debugLogs)
        {
            Debug.Log($"[{name}] 消防栓启动：共 {totalFrames} 帧 / {clipFrameRate:0.#} fps / 时长 {sprayLength:0.###}s，" +
                      $"伤害帧 {damageStartFrame}-{damageEndFrame}，待机 {idleHoldTime:0.##}s，状态机={hasSprayState}，手动采样={manualSampling}");
        }
    }

    private void Update()
    {
        if (!initialized) return;

        float speed = manualSampling
            ? Mathf.Max(0.01f, playbackSpeed)
            : (hydrantAnimator != null ? Mathf.Max(0f, hydrantAnimator.speed) : Mathf.Max(0.01f, playbackSpeed));
        float dt = Time.deltaTime * speed;

        if (spraying)
        {
            sprayClock += dt;

            if (manualSampling)
                sprayClip.SampleAnimation(gameObject, Mathf.Clamp(sprayClock, 0f, sprayLength));

            UpdateDamageWindow();

            if (sprayClock >= sprayLength)
                BeginIdle(idleHoldTime);
        }
        else
        {
            IsDamaging = false;
            idleClock -= dt;
            if (idleClock <= 0f) BeginSpray();
        }
    }

    private void OnDisable()
    {
        IsDamaging = false;
    }

    #region 动画剪辑信息
    private void ResolveClipInfo()
    {
        if (sprayClip == null && hydrantAnimator != null && hydrantAnimator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = hydrantAnimator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == "FireHydrantSpray")
                {
                    sprayClip = clips[i];
                    break;
                }
            }
        }

        sprayLength = sprayClip != null ? Mathf.Max(0.01f, sprayClip.length) : 1.6667f;
        clipFrameRate = sprayClip != null && sprayClip.frameRate > 0.01f ? sprayClip.frameRate : 12f;
        totalFrames = totalFramesOverride > 0
            ? totalFramesOverride
            : Mathf.Max(1, Mathf.RoundToInt(sprayLength * clipFrameRate));
    }
    #endregion

    #region 动画循环
    private void BeginSpray()
    {
        spraying = true;
        sprayClock = 0f;
        IsDamaging = false;

        if (hasSprayState && hydrantAnimator != null)
        {
            hydrantAnimator.Play(sprayHash, 0, 0f);
        }
        else if (manualSampling)
        {
            sprayClip.SampleAnimation(gameObject, 0f);
        }

        if (debugLogs) Debug.Log($"[{name}] 开始喷水。");
    }

    private void BeginIdle(float hold)
    {
        spraying = false;
        idleClock = Mathf.Max(0f, hold);
        IsDamaging = false;

        if (hasIdleState && hydrantAnimator != null)
        {
            hydrantAnimator.Play(idleHash, 0, 0f);
        }
        else if (manualSampling)
        {
            // 没有独立 Idle 状态时，直接采样第 000 帧
            SampleIdlePose();
        }
    }

    private void SampleIdlePose()
    {
        if (idleClip != null) idleClip.SampleAnimation(gameObject, 0f);
        else if (sprayClip != null) sprayClip.SampleAnimation(gameObject, 0f);
    }
    #endregion

    #region 伤害判定
    private void UpdateDamageWindow()
    {
        float normalized = sprayLength > 0.0001f ? sprayClock / sprayLength : 0f;
        float frame = normalized * totalFrames;

        // 007-014：frame ∈ [7, 15)。正好对应 8 帧的强水柱阶段，蓄力和消散都不在区间内。
        IsDamaging = frame >= damageStartFrame && frame < damageEndFrame + 1;

        if (!IsDamaging || !killPlayerOnHit) return;

        ApplyDamage();
    }

    private void ApplyDamage()
    {
        Vector3 center = transform.TransformPoint(damageBoxOffset);
        Vector3 size = Vector3.Scale(damageBoxSize, transform.lossyScale);
        size.z = 1f;
        float angle = transform.eulerAngles.z;

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, angle, playerLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            BunnyController bunny = BunnyContactUtility.FindBunny(hit);
            if (bunny == null || bunny.isDead) continue;

            // 死亡语音统一由角色控制器（SheepController.TriggerDeath）播放，这里不再重复播放，
            // 避免与其他死法音量不一致 / 3D PlayClipAtPoint 随镜头远近衰减。
            bunny.TriggerDeath();

            if (debugLogs) Debug.Log($"[{name}] 水柱命中 {bunny.name}（第 {CurrentFrame} 帧），触发死亡。");
        }
    }
    #endregion

    #region 编辑器
    private void OnValidate()
    {
        idleHoldTime = Mathf.Max(0f, idleHoldTime);
        startDelay = Mathf.Max(0f, startDelay);
        playbackSpeed = Mathf.Max(0.01f, playbackSpeed);

        damageStartFrame = Mathf.Max(0, damageStartFrame);
        damageEndFrame = Mathf.Max(damageStartFrame, damageEndFrame);

        damageBoxSize.x = Mathf.Max(0.01f, damageBoxSize.x);
        damageBoxSize.y = Mathf.Max(0.01f, damageBoxSize.y);

        if (hydrantAnimator == null) hydrantAnimator = GetComponent<Animator>();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDamageGizmo) return;

        Vector3 center = transform.TransformPoint(damageBoxOffset);
        Vector3 size = Vector3.Scale(damageBoxSize, transform.lossyScale);
        size.z = 0.001f;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);

        Gizmos.color = damageGizmoColor;
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(Vector3.zero, size);

        Gizmos.matrix = oldMatrix;

#if UNITY_EDITOR
        UnityEditor.Handles.Label(center + Vector3.up * (size.y * 0.5f + 0.08f),
            $"水柱伤害帧 {damageStartFrame}-{damageEndFrame}\n(共 {totalFrames} 帧)");
#endif
    }
    #endregion
}
