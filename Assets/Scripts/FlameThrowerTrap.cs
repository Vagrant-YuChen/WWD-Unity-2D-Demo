using UnityEngine;

/// <summary>
/// 火焰喷射器陷阱。
///
/// 玩法（和消防栓 / 弩箭同一套节奏）：
///   1) 从游戏开始到结束不停循环播放 FlameThrower 序列帧；
///   2) 一整套动画（点火 FlameOn → 持续喷火 Flame → 熄火 FlameOff）播完后，
///      自动回到无火焰的 Idle 停一小会儿，再重新喷，节奏和 CrossbowShooter / FireHydrantSpray 一致；
///   3) 只有 Flame.000 ~ Flame.002 与 FlameOff.000 这四帧会烧死小兔，
///      点火与熄火的后半段完全无害。帧区间、伤害范围都可以在 Inspector 里调。
///
/// 动画状态机由 Assets/Editor/FlameThrowerSetup.cs 自动填好：
///   Spray = 完整 9 帧剪辑（不循环，绑定到子物体 Flame 的 SpriteRenderer）
///   Idle  = 隐藏火焰的待机剪辑（循环）
/// 火焰本体是 FlameThrower 的子物体 Flame，轴心已经对齐到喷火口，
/// 所以整段动画里火焰根部都固定在左上角那个突出的喷火口上。
///
/// 如果没有自动装配，运行菜单：
///   Tools/Flame Thrower/Setup Flame Throwers In Open Scene
///
/// 喷火音效（shoot_flame）：
///   · 每喷一次（每个喷火周期）最多播放一次；
///   · 只有兔子进入「听觉圆」时才会响，避免玩家在全图都能听到喷火声；
///   · 音量按到喷火器的直线距离线性衰减：贴着喷火器 = 满音量，圆内一半距离 = 半音量，
///     圆边缘 / 圈外 = 0（volume = 1 - 距离 / hearingRadius），播放过程中随兔子移动实时变化；
///   · 听觉圆的半径 hearingRadius / 圆心偏移 hearingOffset 可以在 Inspector 里调，
///     并在 Scene 视图里以青色圆实时显示（选中时右边缘有拖拽手柄，细线是半音量参考圈）。
///
/// 在 Scene 里调火焰 / 伤害盒：
///   · 选中 FlameThrower 下的子物体 Flame，用 Inspector / Scene 的 Transform 工具调
///     Scale X = 火焰宽度、Scale Y = 火焰长度、Position = 喷出位置、Rotation = 喷火方向；
///     编辑模式下已贴好预览图，改完立刻能看到火焰，动画只覆盖 Sprite，不会覆盖 Transform。
///   · FlameThrowerTrap 上的 drawDamageGizmo / alwaysShowDamageGizmo 打开后，
///     Scene 里会实时画出伤害盒（橙红，带旋转）与火焰完整包围盒（白），大小与运行时判定完全一致；
///     damageBoxSize / damageBoxRotation / damageBoxOffset 可把盒子调成贴合斜向火焰的长条矩形。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FlameThrowerTrap : MonoBehaviour
{
    /// <summary>喷火音效的距离衰减曲线。</summary>
    public enum HearingVolumeCurve
    {
        [Tooltip("音量 = 1 - 距离/半径。贴脸满音量，半半径处 0.5，边缘 0（默认）")]
        Linear = 0,
        [Tooltip("音量 = √(1 - 距离/半径)。远处衰减更慢，整个圈内都更响，适合想让范围存在感更强的情况")]
        SquareRoot = 1,
        [Tooltip("音量 = (1 - 距离/半径)²。远处衰减更快，只有贴近才明显，适合强调\"靠近才听得到\"")]
        Squared = 2,
    }

    #region 可配置参数
    [Header("=== 动画（自动装配会填好） ===")]
    [Tooltip("火焰喷射器身上的 Animator。留空会在 Awake 自动获取")]
    [SerializeField] private Animator flameAnimator;
    [Tooltip("喷火器本体（根物体上的 SpriteRenderer），Idle 时保持显示")]
    [SerializeField] private SpriteRenderer bodyRenderer;
    [Tooltip("喷火器左上角喷火口处的火焰 SpriteRenderer（子物体 Flame）")]
    [SerializeField] private SpriteRenderer flameRenderer;
    [Tooltip("完整喷火序列帧 FlameThrower.anim。用来读取帧率 / 时长，也是状态机缺失时的兜底播放")]
    [SerializeField] private AnimationClip sprayClip;
    [Tooltip("隐藏火焰的待机剪辑 FlameThrowerIdle.anim")]
    [SerializeField] private AnimationClip idleClip;
    [Tooltip("喷火状态名（FlameThrower.controller 里的状态名）")]
    [SerializeField] private string sprayStateName = "Spray";
    [Tooltip("待机状态名（FlameThrower.controller 里的状态名）")]
    [SerializeField] private string idleStateName = "Idle";

    [Header("=== 播放节奏（重点调试区） ===")]
    [Tooltip("整套喷火动画播完后，停在无火焰 Idle 的时长（秒）")]
    [SerializeField, Min(0f)] private float idleHoldTime = 0.4f;
    [Tooltip("开局后延迟多久开始第一次喷火（秒），0 = 立刻开始")]
    [SerializeField, Min(0f)] private float startDelay = 0f;
    [Tooltip("整体播放速度倍率。1 = 原始 12 帧/秒；2 = 快一倍")]
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

    [Header("=== 火焰伤害判定（重点调试区） ===")]
    [Tooltip("要检测的层。默认 Everything；脚本只认 BunnyController，所以多选出别的层也不会误伤")]
    [SerializeField] private LayerMask playerLayer = ~0;
    [Tooltip("伤害盒完整尺寸（世界单位）。X 是长边、Y 是短边；配合下面的旋转可以把长边转到和斜向火焰一致")]
    [SerializeField] private Vector2 damageBoxSize = new Vector2(2.49f, 0.85f);
    [Tooltip("伤害盒绕 Z 轴旋转的角度（世界空间，单位：度）。火焰斜着喷，用它把盒子转向贴合火焰，玩家不会在火焰外被烧死")]
    [SerializeField] private float damageBoxRotation = -50.84f;
    [Tooltip("伤害盒中心相对喷火口（火焰根部 / Flame 的轴心）的偏移（世界单位），用来把盒子平移到火焰正中")]
    [SerializeField] private Vector2 damageBoxOffset = new Vector2(-0.61f, 0.98f);
    [Tooltip("从第几帧开始造成伤害（含，0 起）。当前素材第 2 帧 Flame.000 开始喷出大火")]
    [SerializeField, Min(0)] private int damageStartFrame = 2;
    [Tooltip("到第几帧结束造成伤害（含，0 起）。当前素材第 5 帧 FlameOff.000 仍有火焰，第 6 帧起无害")]
    [SerializeField, Min(0)] private int damageEndFrame = 5;
    [Tooltip("序列帧总帧数。0 = 自动按「剪辑时长 × 帧率」计算（当前素材会自动算出 9）")]
    [SerializeField, Min(0)] private int totalFramesOverride = 0;
    [Tooltip("命中时是否杀死小兔")]
    [SerializeField] private bool killPlayerOnHit = true;

    [Header("=== 喷火音效（按圆形范围播放） ===")]
    [Tooltip("喷火音效：每次喷火只播放一次")]
    [SerializeField] private AudioClip spraySound;
    [SerializeField, Range(0f, 1f)] private float spraySoundVolume = 0.8f;
    [Tooltip("听觉检测圆的半径（世界单位）。只有兔子进入这个圆内才会听到喷火音效，避免全图都听到。音量按距离线性衰减：圆心处满音量，圆边缘衰减到 0")]
    [SerializeField, Min(0.1f)] private float hearingRadius = 10f;
    [Tooltip("听觉检测圆的圆心相对本物体中心的偏移（世界单位）")]
    [SerializeField] private Vector2 hearingOffset = Vector2.zero;
    [Tooltip("距离衰减曲线：Linear = 1 - 距离/半径（半半径处半音量）；SquareRoot = 远处更响；Squared = 远处更轻")]
    [SerializeField] private HearingVolumeCurve hearingVolumeCurve = HearingVolumeCurve.Linear;
    [Tooltip("在 Scene 视图画出听觉检测圆（不选中也显示）")]
    [SerializeField] private bool drawHearingGizmo = true;
    [SerializeField] private Color hearingGizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    [Header("=== 表现 ===")]
    [Tooltip("火焰命中音效（可选）")]
    [SerializeField] private AudioClip killSound;
    [SerializeField, Range(0f, 1f)] private float killSoundVolume = 0.8f;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    [Tooltip("在 Scene 视图画出当前火焰的伤害范围，方便直接对着火焰调大小")]
    [SerializeField] private bool drawDamageGizmo = true;
    [Tooltip("即使没有选中这个物体也一直显示伤害盒（选中子物体 Flame 调整长度 / 位置时也能看到盒子）")]
    [SerializeField] private bool alwaysShowDamageGizmo = true;
    [Tooltip("同时画出火焰 Sprite 的完整世界包围盒（白线），方便对照伤害盒")]
    [SerializeField] private bool showSpriteBoundsGizmo = true;
    [Tooltip("编辑模式下用于预览火焰、并计算伤害盒的贴图（动画没播放时火焰渲染器可能没有 Sprite，用它兜底）")]
    [SerializeField] private Sprite flamePreviewSprite;
    [SerializeField] private Color damageGizmoColor = new Color(1f, 0.35f, 0.05f, 0.35f);
    #endregion

    #region 运行时状态
    private int sprayHash;
    private int idleHash;
    private bool hasSprayState;
    private bool hasIdleState;
    private bool manualSampling;
    private float sprayLength = 0.75f;
    private float clipFrameRate = 12f;
    private int totalFrames = 9;
    private float sprayClock;
    private float idleClock;
    private bool spraying;
    private bool initialized;
    private bool warnedMissingAnimation;

    // 喷火音效：每个喷火周期最多播放一次，且只在兔子处于听觉圆内时播放。
    private AudioSource spraySfxSource;
    private bool spraySoundPlayedThisCycle;
    #endregion

    #region 对外只读接口（方便其他脚本 / 调试）
    /// <summary>当前这一帧是否处于会造成伤害的火焰阶段。</summary>
    public bool IsDamaging { get; private set; }

    /// <summary>是否正在播放喷火动画（false = 无火焰 Idle）。</summary>
    public bool IsSpraying => spraying;

    /// <summary>当前喷火动画的归一化进度（0 = 第一帧，1 = 播完）。</summary>
    public float NormalizedTime => sprayLength > 0.0001f ? Mathf.Clamp01(sprayClock / sprayLength) : 0f;

    /// <summary>当前序列帧序号（0 起）。</summary>
    public int CurrentFrame =>
        Mathf.Clamp(Mathf.FloorToInt(NormalizedTime * totalFrames), 0, Mathf.Max(0, totalFrames - 1));
    #endregion

    private void Reset()
    {
        flameAnimator = GetComponent<Animator>();
        bodyRenderer = GetComponent<SpriteRenderer>();
    }

    private void Awake()
    {
        if (flameAnimator == null) flameAnimator = GetComponent<Animator>();
        if (bodyRenderer == null) bodyRenderer = GetComponent<SpriteRenderer>();
        if (flameRenderer == null) flameRenderer = FindFlameRenderer();

        sprayHash = Animator.StringToHash(sprayStateName);
        idleHash = Animator.StringToHash(idleStateName);

        EnsureSpraySfxSource();
    }

    private void EnsureSpraySfxSource()
    {
        if (spraySfxSource != null) return;

        spraySfxSource = GetComponent<AudioSource>();
        if (spraySfxSource == null) spraySfxSource = gameObject.AddComponent<AudioSource>();

        spraySfxSource.playOnAwake = false;
        spraySfxSource.loop = false;
        spraySfxSource.spatialBlend = 0f;   // 是否听得见由听觉圆决定，这里用 2D 保证圆内音量稳定
        spraySfxSource.dopplerLevel = 0f;
    }

    private SpriteRenderer FindFlameRenderer()
    {
        Transform flame = transform.Find("Flame");
        if (flame != null)
        {
            SpriteRenderer found = flame.GetComponent<SpriteRenderer>();
            if (found != null) return found;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i] != bodyRenderer) return renderers[i];
        }

        return null;
    }

    private void Start()
    {
        ResolveClipInfo();

        if (flameAnimator != null)
        {
            flameAnimator.speed = Mathf.Max(0.01f, playbackSpeed);

            bool hasController = flameAnimator.runtimeAnimatorController != null;
            hasSprayState = hasController && flameAnimator.HasState(0, sprayHash);
            hasIdleState = hasController && flameAnimator.HasState(0, idleHash);
        }

        // 状态机还没填好时，直接用 AnimationClip 采样，保证玩法依然能跑
        manualSampling = !hasSprayState && sprayClip != null;
        if (manualSampling && flameAnimator != null) flameAnimator.enabled = false;

        if (!hasSprayState && sprayClip == null && !warnedMissingAnimation)
        {
            warnedMissingAnimation = true;
            Debug.LogWarning($"[{name}] 找不到喷火动画。请运行菜单 Tools/Flame Thrower/Setup Flame Throwers In Open Scene 补全状态机。", this);
        }

        if (flameRenderer == null)
        {
            Debug.LogWarning($"[{name}] 找不到火焰子物体 Flame，火焰不会显示、也无法造成伤害。" +
                             "请运行菜单 Tools/Flame Thrower/Setup Flame Throwers In Open Scene 自动补全。", this);
        }

        initialized = true;

        if (startDelay > 0f) BeginIdle(startDelay);
        else BeginSpray();

        if (debugLogs)
        {
            Debug.Log($"[{name}] 火焰喷射器启动：共 {totalFrames} 帧 / {clipFrameRate:0.#} fps / 时长 {sprayLength:0.###}s，" +
                      $"伤害帧 {damageStartFrame}-{damageEndFrame}，待机 {idleHoldTime:0.##}s，状态机={hasSprayState}，手动采样={manualSampling}");
        }
    }

    private void Update()
    {
        if (!initialized) return;

        float speed = manualSampling
            ? Mathf.Max(0.01f, playbackSpeed)
            : (flameAnimator != null ? Mathf.Max(0f, flameAnimator.speed) : Mathf.Max(0.01f, playbackSpeed));
        float dt = Time.deltaTime * speed;

        if (spraying)
        {
            // Animator 的 Idle 状态可能把渲染器关掉，喷火期间强制打开，避免只看到本体
            if (flameRenderer != null && !flameRenderer.enabled) flameRenderer.enabled = true;

            // 喷火开始后才走进听觉圆的玩家，补播一次本轮音效（每个周期仍只播一次）
            if (!spraySoundPlayedThisCycle) TryPlaySpraySound();

            sprayClock += dt;

            if (manualSampling)
                sprayClip.SampleAnimation(gameObject, Mathf.Clamp(sprayClock, 0f, sprayLength));

            if (sprayClock >= sprayLength)
                BeginIdle(idleHoldTime);
        }
        else
        {
            if (flameRenderer != null && flameRenderer.enabled) flameRenderer.enabled = false;

            idleClock -= dt;
            if (idleClock <= 0f) BeginSpray();
        }

        // 音量跟随距离实时变化（喷火结束后的音效尾巴也继续随距离衰减）
        UpdateSpraySoundVolume();
    }

    private void LateUpdate()
    {
        if (!initialized) return;

        // 放到 LateUpdate，保证用的是 Animator 本帧最终写进 SpriteRenderer 的火焰帧
        if (spraying) UpdateDamageWindow();
        else IsDamaging = false;
    }

    private void OnDisable()
    {
        IsDamaging = false;

        if (spraySfxSource != null && spraySfxSource.isPlaying) spraySfxSource.Stop();
    }

    #region 动画剪辑信息
    private void ResolveClipInfo()
    {
        if (sprayClip == null && flameAnimator != null && flameAnimator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = flameAnimator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == "FlameThrower")
                {
                    sprayClip = clips[i];
                    break;
                }
            }
        }

        sprayLength = sprayClip != null ? Mathf.Max(0.01f, sprayClip.length) : 0.75f;
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
        spraySoundPlayedThisCycle = false;   // 新一轮喷火，音效计数重置

        if (flameRenderer != null) flameRenderer.enabled = true;

        TryPlaySpraySound();

        if (hasSprayState && flameAnimator != null)
        {
            flameAnimator.Play(sprayHash, 0, 0f);
        }
        else if (manualSampling)
        {
            sprayClip.SampleAnimation(gameObject, 0f);
        }

        if (debugLogs) Debug.Log($"[{name}] 开始喷火。");
    }

    private void BeginIdle(float hold)
    {
        spraying = false;
        idleClock = Mathf.Max(0f, hold);
        IsDamaging = false;

        if (flameRenderer != null) flameRenderer.enabled = false;

        if (hasIdleState && flameAnimator != null)
        {
            flameAnimator.Play(idleHash, 0, 0f);
        }

        if (debugLogs) Debug.Log($"[{name}] 熄火，待机 {idleClock:0.##}s。");
    }
    #endregion

    #region 伤害判定
    private void UpdateDamageWindow()
    {
        float normalized = sprayLength > 0.0001f ? sprayClock / sprayLength : 0f;
        float frame = normalized * totalFrames;

        // 第 2-5 帧：frame ∈ [2, 6)。正好对应 Flame.000 / .001 / .002 / FlameOff.000。
        IsDamaging = frame >= damageStartFrame && frame < damageEndFrame + 1;

        if (!IsDamaging || !killPlayerOnHit) return;

        ApplyDamage();
    }

    private void ApplyDamage()
    {
        // 用同一套几何计算伤害盒，保证 Scene 视图里看到的 gizmo 与实际判定完全一致
        if (!TryGetDamageBox(out Vector3 center, out Vector2 size, out float angle)) return;

        Collider2D[] hits = Physics2D.OverlapBoxAll(
            new Vector2(center.x, center.y),
            size,
            angle,
            playerLayer);

        bool killedAny = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            BunnyController bunny = BunnyContactUtility.FindBunny(hit);
            if (bunny == null || bunny.isDead) continue;

            bunny.TriggerDeath();
            killedAny = true;

            if (debugLogs) Debug.Log($"[{name}] 火焰烧到 {bunny.name}（第 {CurrentFrame} 帧），触发死亡。");
        }

        if (killedAny && killSound != null)
            AudioSource.PlayClipAtPoint(killSound, center, killSoundVolume);
    }
    #endregion

    #region 听觉检测与喷火音效
    /// <summary>听觉检测圆的世界圆心（物体中心 + 偏移）。</summary>
    private Vector2 HearingCenter => (Vector2)transform.position + hearingOffset;

    private float HearingRadius => Mathf.Max(0.1f, hearingRadius);

    /// 距离 → 音量倍率的衰减曲线，由 Inspector 的 Hearing Volume Curve 选择。
    /// Linear: 1 - 距离/半径；SquareRoot: √(1 - 距离/半径)；Squared: (1 - 距离/半径)²。
    /// </summary>
    private float VolumeScaleAtDistance(float distance)
    {
        float remaining = Mathf.Clamp01(1f - distance / HearingRadius);
        switch (hearingVolumeCurve)
        {
            case HearingVolumeCurve.SquareRoot:
                return Mathf.Sqrt(remaining);
            case HearingVolumeCurve.Squared:
                return remaining * remaining;
            default:
                return remaining;
        }
    }

    /// <summary>
    /// 每个喷火周期最多触发一次音效；只有兔子处于听觉圆内（音量 > 0）才会真正开始播放。
    /// 起始音量按当前距离算出，之后每帧由 UpdateSpraySoundVolume 跟随距离实时变化。
    /// </summary>
    private void TryPlaySpraySound()
    {
        if (spraySoundPlayedThisCycle) return;
        if (spraySound == null) return;

        if (!TryGetNearestBunnyDistance(out float distance)) return;

        float volumeScale = VolumeScaleAtDistance(distance);
        if (volumeScale <= 0.0001f) return;   // 圆外 / 贴着圆边缘：一点声音都不播

        spraySoundPlayedThisCycle = true;

        EnsureSpraySfxSource();
        if (spraySfxSource == null) return;

        // 用 Play() + 逐帧改 volume 的方式播放，而不是 PlayOneShot 固定音量，
        // 这样兔子在喷火过程中走动时，听到的音量会随距离连续变化。
        spraySfxSource.clip = spraySound;
        spraySfxSource.volume = Mathf.Clamp01(spraySoundVolume * volumeScale);
        spraySfxSource.Play();

        if (debugLogs)
            Debug.Log($"[{name}] 播放喷火音效（距离 {distance:0.##} / 半径 {HearingRadius:0.##}，音量 {spraySfxSource.volume:0.##}）。");
    }

    /// <summary>
    /// 音效播放期间每帧刷新音量：按兔子到圆心的距离线性衰减；兔子死亡 / 不在场时静音，
    /// 但不停掉音源，这样重新走进范围还能继续听到剩余部分。
    /// </summary>
    private void UpdateSpraySoundVolume()
    {
        if (spraySfxSource == null || !spraySfxSource.isPlaying) return;

        float volumeScale = TryGetNearestBunnyDistance(out float distance)
            ? VolumeScaleAtDistance(distance)
            : 0f;

        spraySfxSource.volume = Mathf.Clamp01(spraySoundVolume * volumeScale);
    }

    /// <summary>找到离听觉圆心最近的活着的兔子；输出它到圆心的直线距离（不限半径，圈外也算）。</summary>
    private bool TryGetNearestBunnyDistance(out float distance)
    {
        distance = float.MaxValue;

        Vector2 center = HearingCenter;
        // 用半径 + 少量余量做粗筛，避免全场景搜索
        float searchRadius = HearingRadius + 2f;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, searchRadius, playerLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            BunnyController bunny = BunnyContactUtility.FindBunny(hit);
            if (bunny == null || bunny.isDead) continue;

            float d = Vector2.Distance(center, bunny.transform.position);
            if (d < distance) distance = d;
        }

        return distance < float.MaxValue;
    }
    #endregion

    #region 编辑器
    private void OnValidate()
    {
        idleHoldTime = Mathf.Max(0f, idleHoldTime);
        startDelay = Mathf.Max(0f, startDelay);
        playbackSpeed = Mathf.Max(0.01f, playbackSpeed);

        damageBoxSize = new Vector2(
            Mathf.Max(0.01f, damageBoxSize.x),
            Mathf.Max(0.01f, damageBoxSize.y));
        damageStartFrame = Mathf.Max(0, damageStartFrame);
        damageEndFrame = Mathf.Max(damageStartFrame, damageEndFrame);

        hearingRadius = Mathf.Max(0.1f, hearingRadius);

        if (flameAnimator == null) flameAnimator = GetComponent<Animator>();
        if (bodyRenderer == null) bodyRenderer = GetComponent<SpriteRenderer>();

#if UNITY_EDITOR
        // 编辑模式下把预览火焰贴到火焰渲染器，这样 Scene 里能直接看到火焰本体，
        // 用移动 / 旋转 / 缩放工具就能调长度、宽度和位置；运行时会由动画重新覆盖。
        if (!Application.isPlaying && flameRenderer != null && flameRenderer.sprite == null && flamePreviewSprite != null)
            flameRenderer.sprite = flamePreviewSprite;
#endif
    }

    /// <summary>
    /// 计算火焰当前的世界包围盒。运行时用渲染器实际 Sprite 的 bounds，
    /// 编辑模式（动画没播放、渲染器可能没有 Sprite）用预览贴图兜底推算。
    /// </summary>
    private bool TryGetFlameWorldBounds(out Bounds worldBounds)
    {
        worldBounds = new Bounds();

        if (flameRenderer == null) return false;

        Sprite sprite = flameRenderer.sprite != null ? flameRenderer.sprite : flamePreviewSprite;
        if (sprite == null) return false;

        worldBounds = flameRenderer.sprite == sprite
            ? flameRenderer.bounds
            : ComputeSpriteWorldBounds(sprite, flameRenderer.transform);

        return true;
    }

    /// <summary>
    /// 计算伤害盒的世界中心、尺寸与旋转角。Scene gizmo 与运行时判定共用这一份计算，保证所见即所得。
    /// 盒子是「有朝向」的长条矩形：以火焰根部（喷火口）为锚点，按 damageBoxOffset 平移，
    /// 再按 damageBoxRotation 绕 Z 轴旋转，从而贴合斜向喷出的火焰，不会烧到火焰外的玩家。
    /// </summary>
    private bool TryGetDamageBox(out Vector3 center, out Vector2 size, out float angle)
    {
        center = Vector3.zero;
        size = Vector2.zero;
        angle = 0f;

        if (flameRenderer == null) return false;

        // 火焰根部（Flame 的轴心）世界位置作为锚点，不会随序列帧轴心变化而抖动
        center = flameRenderer.transform.position + new Vector3(damageBoxOffset.x, damageBoxOffset.y, 0f);
        size = new Vector2(Mathf.Max(0.01f, damageBoxSize.x), Mathf.Max(0.01f, damageBoxSize.y));
        angle = damageBoxRotation;

        return true;
    }

    /// <summary>在没有实际渲染 Sprite 时，用 Sprite 自身几何 + 变换推算世界包围盒。</summary>
    private static Bounds ComputeSpriteWorldBounds(Sprite sprite, Transform flameTransform)
    {
        Bounds local = sprite.bounds;
        Matrix4x4 matrix = flameTransform.localToWorldMatrix;

        Vector3 min = local.min;
        Vector3 max = local.max;

        Vector3 a = matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, 0f));
        Vector3 b = matrix.MultiplyPoint3x4(new Vector3(min.x, max.y, 0f));
        Vector3 c = matrix.MultiplyPoint3x4(new Vector3(max.x, min.y, 0f));
        Vector3 d = matrix.MultiplyPoint3x4(new Vector3(max.x, max.y, 0f));

        Vector3 boundsMin = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
        Vector3 boundsMax = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));

        Bounds result = new Bounds();
        result.SetMinMax(boundsMin, boundsMax);
        return result;
    }

    private void OnDrawGizmos()
    {
        // 不选中物体也一直显示伤害盒：这样选中子物体 Flame 调长度 / 位置时，盒子依然可见
        if (drawDamageGizmo && alwaysShowDamageGizmo) DrawDamageBox();

        // 听觉检测圆始终可见，方便确认兔子进出半径范围
        if (drawHearingGizmo) DrawHearingCircle(alwaysShowDamageGizmo ? 1f : 0.55f);
    }

    private void OnDrawGizmosSelected()
    {
        DrawNozzleMarker();

        if (drawDamageGizmo)
        {
            // 没开“始终显示”时，选中才画盒子
            if (!alwaysShowDamageGizmo) DrawDamageBox();

#if UNITY_EDITOR
            if (TryGetDamageBox(out Vector3 center, out Vector2 size, out float angle))
            {
                int frames = totalFramesOverride > 0 ? totalFramesOverride : totalFrames;
                UnityEditor.Handles.Label(
                    center + Vector3.up * (size.y * 0.5f + 0.08f),
                    $"火焰伤害帧 {damageStartFrame}-{damageEndFrame}（共 {frames} 帧）\n伤害盒 {size.x:0.##} × {size.y:0.##}  旋转 {angle:0.#}°");
            }
#endif
        }

        if (drawHearingGizmo)
        {
            DrawHearingCircle(1f);

#if UNITY_EDITOR
            DrawHearingRadiusHandle();
            UnityEditor.Handles.Label(
                HearingCenter + Vector2.up * (HearingRadius + 0.25f),
                $"喷火音效听觉半径 {HearingRadius:0.##}（音量 = 1 - 距离/半径）\n圆心满音量 · {HearingRadius * 0.5f:0.##} 处半音量 · 边缘为 0");
#endif
        }
    }

    private void DrawDamageBox()
    {
        if (!TryGetDamageBox(out Vector3 center, out Vector2 size, out float angle)) return;

        // 以盒子中心为原点、绕 Z 轴旋转后进行绘制，这样 gizmo 和物理判定是同一个朝向的矩形
        Vector3 boxSize = new Vector3(size.x, size.y, 0.01f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);

        // 半透明填充 + 高亮描边：既能看清范围，又不会完全挡住火焰
        Gizmos.color = damageGizmoColor;
        Gizmos.DrawCube(Vector3.zero, boxSize);

        Gizmos.color = new Color(1f, 0.15f, 0f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);

        Gizmos.matrix = previousMatrix;

        // 白色框是火焰 Sprite 的完整世界包围盒（轴对齐），方便对照
        if (showSpriteBoundsGizmo && TryGetFlameWorldBounds(out Bounds flameBounds))
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube(flameBounds.center, flameBounds.size);
        }
    }

    private void DrawNozzleMarker()
    {
        // 火焰根部（喷火口）位置，方便确认火焰有没有对齐
        Vector3 nozzle = flameRenderer != null
            ? flameRenderer.transform.position
            : transform.TransformPoint(new Vector3(-0.22f, 0.44f, 0f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(nozzle, 0.06f);
    }

    /// <summary>画出喷火音效的圆形听觉范围（半径 / 圆心与运行时检测完全一致）。</summary>
    private void DrawHearingCircle(float alpha)
    {
        Vector3 center = new Vector3(HearingCenter.x, HearingCenter.y, 0f);
        float r = HearingRadius;

        Color fillColor = hearingGizmoColor;
        fillColor.a = 0.07f * alpha;
        Gizmos.color = fillColor;
        Gizmos.DrawSphere(center, r);

        Color wireColor = hearingGizmoColor;
        wireColor.a = Mathf.Clamp01(hearingGizmoColor.a * alpha);
        Gizmos.color = wireColor;
        Gizmos.DrawWireSphere(center, r);

        // 半音量参考圈（半径的一半处，音量正好 0.5），方便直观看到距离与音量的关系
        Color innerColor = hearingGizmoColor;
        innerColor.a = 0.45f * alpha;
        Gizmos.color = innerColor;
        Gizmos.DrawWireSphere(center, r * 0.5f);

        // 圆心十字，便于看清听觉圆的中心
        Gizmos.color = wireColor;
        float crossSize = Mathf.Max(0.12f, r * 0.05f);
        Gizmos.DrawLine(center - Vector3.right * crossSize, center + Vector3.right * crossSize);
        Gizmos.DrawLine(center - Vector3.up * crossSize, center + Vector3.up * crossSize);
    }

#if UNITY_EDITOR
    /// <summary>选中时在圆右侧边缘放一个拖拽手柄，直接拖就能改听觉半径。</summary>
    private void DrawHearingRadiusHandle()
    {
        Vector3 center = new Vector3(HearingCenter.x, HearingCenter.y, 0f);
        Vector3 handlePos = center + Vector3.right * HearingRadius;

        UnityEditor.Handles.color = hearingGizmoColor;
        float handleSize = UnityEditor.HandleUtility.GetHandleSize(handlePos) * 0.06f;

        UnityEditor.EditorGUI.BeginChangeCheck();
        var fmh_674_24_639302928269654815 = Quaternion.identity; Vector3 newPos = UnityEditor.Handles.FreeMoveHandle(
            handlePos, handleSize, Vector3.zero, UnityEditor.Handles.DotHandleCap);
        if (UnityEditor.EditorGUI.EndChangeCheck())
        {
            UnityEditor.Undo.RecordObject(this, "调整喷火音效听觉半径");
            float newRadius = new Vector2(newPos.x - center.x, newPos.y - center.y).magnitude;
            hearingRadius = Mathf.Max(0.1f, newRadius);
        }
    }
#endif
    #endregion
}
