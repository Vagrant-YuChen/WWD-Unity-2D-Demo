using UnityEngine;

/// <summary>
/// 小羊控制器：完整沿用 BunnyController 的移动 / 跳跃 / 台阶容错手感，
/// 只针对"没有履带、脚底更窄、整体体型更小"的羊模型做必要适配。
///
/// 之所以继承 BunnyController 而不是复制一份：
/// SpringBoard / BladeKill / CannonLauncher / CoinPickup 都是按 BunnyController 查找玩家的，
/// 继承后这些脚本不用改一行就能作用到小羊身上，也不会出现两套手感逐渐跑偏的问题。
/// </summary>
public class SheepController : BunnyController
{
    #region 小羊模型适配参数
    [Header("=== 小羊模型适配 ===")]
    [Tooltip("按碰撞体包围盒自动生成三个脚底探针（羊预制体没有脚底子物体时用）")]
    [SerializeField] private bool autoFitGroundProbes = true;
    [Tooltip("探针点相对碰撞体最低点的内缩距离，避免探针埋进地面")]
    [SerializeField] private float probeInset = 0.01f;
    [Tooltip("左右探针相对碰撞体半宽的偏移比例（0.35≈脚掌宽度）")]
    [SerializeField, Range(0f, 1f)] private float probeSideRatio = 0.35f;
    [Tooltip("用碰撞体高度自动计算地面检测半径，避免固定 0.2 对小羊过大（会悬空判定）")]
    [SerializeField] private bool fitProbeRadiusToCollider = true;
    [Tooltip("检测半径占碰撞体高度的比例")]
    [SerializeField, Range(0.02f, 0.5f)] private float probeRadiusRatio = 0.16f;
    [Tooltip("检测半径的上下限（世界单位）")]
    [SerializeField] private Vector2 probeRadiusClamp = new Vector2(0.06f, 0.16f);
    [Tooltip("落地姿势保持时间：小羊落地动画只有 0.167s，比兔子的 0.15s 略长")]
    [SerializeField] private float sheepLandPoseHold = 0.17f;
    [Tooltip("触发落地动画的最低下落速度，-0.5 与兔子一致；想让小羊更软可调到 -0.35")]
    [SerializeField] private float sheepLandedSpeedThreshold = -0.5f;
    [Tooltip("即将落地预判半径（世界单位）。羊的着地半径更小，单独用较大的预判半径保证下落动画不丢")]
    [SerializeField] private float sheepLandingProbeRadius = 0.24f;
    [Tooltip("小羊体型小、大炮发射速度快，开启连续碰撞防止穿模")]
    [SerializeField] private bool useContinuousCollision = true;
    [Tooltip("未配置 Ground 层时自动使用 Ground 层，否则台阶检测会把墙当成台阶去爬")]
    [SerializeField] private bool autoResolveGroundLayer = true;
    #endregion

    #region 大炮发射音效（被射出时的羊叫）
    [Header("=== 大炮发射音效 ===")]
    [Tooltip("被加农炮射出瞬间播放的音效（羊叫），与开炮音效同步")]
    [SerializeField] private AudioClip launchVoiceClip;
    [Tooltip("羊叫音量")]
    [SerializeField, Range(0f, 1f)] private float launchVoiceVolume = 1f;

    private AudioSource launchVoiceSource;
    #endregion

    #region 平滑行走：平底碰撞体
    [Header("=== 平滑行走（平底碰撞体）===")]
    [Tooltip("用平底 BoxCollider2D 替换精灵多边形碰撞体。精灵多边形底部有多个高低不一的顶点，" +
             "在被拆成很多小碰撞箱的桥面上行走时会反复卡在接缝处导致一顿一顿；换成平底盒子后底边是平直线段，滑动顺滑。")]
    [SerializeField] private bool useFlatBottomCollider = true;
    [Tooltip("平底盒子宽度占多边形宽度的比例")]
    [SerializeField, Range(0.3f, 1f)] private float flatBoxWidthRatio = 0.9f;
    [Tooltip("平底盒子高度占多边形高度的比例（底边始终对齐脚底最低点）")]
    [SerializeField, Range(0.3f, 1f)] private float flatBoxHeightRatio = 0.92f;
    #endregion

    #region 运行时状态
    private Vector3 probeCenterOffset;
    private Vector3 probeLeftOffset;
    private Vector3 probeRightOffset;
    private bool probesFitted;
    #endregion

    /// <summary>
    /// 用平底 BoxCollider2D 替换精灵多边形碰撞体。
    /// 精灵多边形底部有多个高低不一的顶点，在被拆成很多小碰撞箱的桥面上行走时，
    /// 这些顶点会反复卡在接缝处，表现为"一顿一顿"。换成平底盒子后底边是一条平直线段，
    /// 压在等高的面上滑动就顺滑了（这也是平台角色碰撞体的标准做法）。
    /// </summary>
    private void ReplacePolygonWithFlatBox()
    {
        if (!useFlatBottomCollider) return;
        if (!(bodyCollider is PolygonCollider2D poly)) return;

        int pathCount = poly.pathCount;
        if (pathCount <= 0) return;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int p = 0; p < pathCount; p++)
        {
            Vector2[] pts = poly.GetPath(p);
            for (int i = 0; i < pts.Length; i++)
            {
                min = Vector2.Min(min, pts[i]);
                max = Vector2.Max(max, pts[i]);
            }
        }
        if (min.x > max.x) return;

        float bottom = min.y;
        float width = Mathf.Max(0.01f, (max.x - min.x) * flatBoxWidthRatio);
        float height = Mathf.Max(0.01f, (max.y - bottom) * flatBoxHeightRatio);
        Vector2 size = new Vector2(width, height);
        Vector2 offset = new Vector2((min.x + max.x) * 0.5f, bottom + height * 0.5f);

        PhysicsMaterial2D mat = bodyCollider != null ? bodyCollider.sharedMaterial : null;

        poly.enabled = false;

        var box = gameObject.AddComponent<BoxCollider2D>();
        box.size = size;
        box.offset = offset;
        box.isTrigger = false;
        if (mat != null) box.sharedMaterial = mat;

        bodyCollider = box;

        if (debugLogs)
            Debug.Log($"[{name}] 已用平底 BoxCollider2D 替换多边形碰撞体：size={size}, offset={offset}");
    }

    protected override void OnCharacterConfigured()
    {
        EnsureLaunchVoiceSource();

        ReplacePolygonWithFlatBox();   // 先换碰撞体，后面的半径/探针计算都基于新碰撞体

        base.OnCharacterConfigured();

        if (autoResolveGroundLayer && groundLayer.value == 0)
        {
            int groundIndex = LayerMask.NameToLayer("Ground");
            if (groundIndex >= 0)
            {
                groundLayer = 1 << groundIndex;
                if (debugLogs) Debug.Log($"[{name}] groundLayer 未配置，已自动使用 Ground 层。");
            }
            else
            {
                Debug.LogWarning($"[{name}] groundLayer 为空且找不到 Ground 层，地面检测/台阶检测会失效。");
            }
        }

        if (!CompareTag("Player"))
        {
            Debug.LogWarning($"[{name}] Tag 不是 Player，CoinPickup / CannonLauncher 会找不到玩家。");
        }

        if (useContinuousCollision && rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (bodyCollider != null && bodyCollider.sharedMaterial != null)
        {
            bodyCollider.sharedMaterial.name = "SheepNoFriction";
        }

        if (bodyCollider == null) return;

        // 半径按模型身高重算（小羊 0.25 缩放后身高约 0.72，0.2 的世界半径会大得离谱）
        if (fitProbeRadiusToCollider)
        {
            groundCheckRadius = Mathf.Clamp(
                bodyCollider.bounds.size.y * probeRadiusRatio,
                probeRadiusClamp.x,
                probeRadiusClamp.y);
        }

        // 只有在三个探针都没手动绑定时才自动生成，避免覆盖美术/关卡手摆的位置
        bool noManualProbes = HasNoManualProbes();

        if (autoFitGroundProbes && noManualProbes)
        {
            FitProbesToCollider();
        }
    }

    private void FitProbesToCollider()
    {
        Bounds b = bodyCollider.bounds;
        Vector3 origin = transform.position;

        float feetY = b.min.y - origin.y + probeInset;
        float centerX = b.center.x - origin.x;
        float halfWidth = b.extents.x * probeSideRatio;

        probeCenterOffset = new Vector3(centerX, feetY, 0f);
        probeLeftOffset = probeCenterOffset + Vector3.left * halfWidth;
        probeRightOffset = probeCenterOffset + Vector3.right * halfWidth;
        probesFitted = true;

        if (debugLogs)
        {
            Debug.Log($"[{name}] 脚底探针已按碰撞体自动生成：feetY={feetY:F3}, halfWidth={halfWidth:F3}, r={groundCheckRadius:F3}");
        }
    }

    #region 覆盖脚底检测位置
    protected override Vector3 GroundCheckCenterPosition =>
        probesFitted ? transform.position + probeCenterOffset : base.GroundCheckCenterPosition;

    protected override Vector3 GroundCheckLeftPosition =>
        probesFitted ? transform.position + probeLeftOffset : base.GroundCheckLeftPosition;

    protected override Vector3 GroundCheckRightPosition =>
        probesFitted ? transform.position + probeRightOffset : base.GroundCheckRightPosition;

    // 着地和"即将落地"用两个不同的半径：
    // 着地收紧，避免小羊悬空；预判放宽，保证高速下落时一定能播到落地动画。
    protected override float LandingProbeRadius =>
        Mathf.Max(GroundCheckRadius, sheepLandingProbeRadius);
    #endregion

    #region 覆盖落地表现
    protected override float LandStateResetDelay =>
        sheepLandPoseHold > 0f ? sheepLandPoseHold : base.LandStateResetDelay;

    protected override float LandedSpeedThreshold => sheepLandedSpeedThreshold;
    #endregion

    #region 大炮发射音效
    /// <summary>
    /// 被加农炮发射时的羊叫。CannonLauncher 会在开炮音效播放结束后才调用该方法，
    /// 因此叫声与炮声错开、不会重叠。
    /// </summary>
    public override void PlayLaunchVoice()
    {
        if (isDead || launchVoiceClip == null) return;
        EnsureLaunchVoiceSource();
        launchVoiceSource.PlayOneShot(launchVoiceClip, launchVoiceVolume * GameAudioManager.SfxMultiplier);
    }

    private void EnsureLaunchVoiceSource()
    {
        if (launchVoiceSource != null) return;

        launchVoiceSource = GetComponent<AudioSource>();
        if (launchVoiceSource == null) launchVoiceSource = gameObject.AddComponent<AudioSource>();

        launchVoiceSource.playOnAwake = false;
        launchVoiceSource.spatialBlend = 0f;
    }
    #endregion

    #region 移动音效（小羊专用片段与音量）
    /// <summary>小羊使用自己的移动循环音效片段。</summary>
    protected override AudioClip MoveLoopClip =>
        PlayerSfxConfig != null ? PlayerSfxConfig.sheepMoveLoopClip : null;

    /// <summary>小羊使用独立的移动循环音量。</summary>
    protected override float MoveLoopVolume =>
        PlayerSfxConfig != null ? PlayerSfxConfig.sheepMoveVolume : 0.3f;
    #endregion

    #region 编辑器预览
    private bool HasNoManualProbes() =>
        groundCheckPoint == null
        && groundCheckPointLeft == null
        && groundCheckPointRight == null;

    /// <summary>
    /// 未运行游戏时 Awake 不会执行，自动探针还没算出来。
    /// 这里用碰撞体实时算一份"预览"，方便在 Scene 视图里直接检查探针位置。
    /// </summary>
    protected override void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying
            && autoFitGroundProbes
            && HasNoManualProbes()
            && TryGetProbePreview(out Vector3 center, out Vector3 left, out Vector3 right, out float radius))
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(center, radius);
            Gizmos.DrawWireSphere(left, radius);
            Gizmos.DrawWireSphere(right, radius);

            // 青色 = 即将落地预判圈（比着地圈更大）
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(center, Mathf.Max(radius, sheepLandingProbeRadius));

            // 从身体中心连一条线到脚底，方便一眼找到
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, center);
            return;
        }

        base.OnDrawGizmosSelected();
    }

    private bool TryGetProbePreview(out Vector3 center, out Vector3 left, out Vector3 right, out float radius)
    {
        center = left = right = transform.position;
        radius = groundCheckRadius;

        Collider2D col = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (col == null) return false;

        Bounds b = col.bounds;
        Vector3 origin = transform.position;

        float feetY = b.min.y - origin.y + probeInset;
        float centerX = b.center.x - origin.x;
        float halfWidth = b.extents.x * probeSideRatio;

        center = origin + new Vector3(centerX, feetY, 0f);
        left = center + Vector3.left * halfWidth;
        right = center + Vector3.right * halfWidth;
        radius = fitProbeRadiusToCollider
            ? Mathf.Clamp(b.size.y * probeRadiusRatio, probeRadiusClamp.x, probeRadiusClamp.y)
            : groundCheckRadius;

        return true;
    }
    #endregion
}
