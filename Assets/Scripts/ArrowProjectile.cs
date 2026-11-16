using UnityEngine;

/// <summary>
/// 弩射出的箭矢。
///
/// 设计要点：
///   1) 匀速飞行，没有重力也没有加速度（用 Kinematic 刚体 + MovePosition 直接位移，
///      不交给物理演算，所以绝对不会越飞越快或下坠）；
///   2) 飞行中循环播放 Arrow 序列帧；
///   3) 碰到小羊 → 杀死小羊后破碎；碰到障碍物（任意实体碰撞体）→ 破碎；
///   4) 破碎时停住并播放 ArrowBreaking 序列帧，播完自动销毁。
///
/// 预制体一般由 Tools/Arrow Trap 菜单（或首次编译自动执行）生成，
/// 速度 / 方向由 CrossbowShooter 在发射时通过 Launch() 覆盖。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class ArrowProjectile : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 飞行 ===")]
    [Tooltip("飞行方向（世界空间），一般由 CrossbowShooter 在发射时覆盖。默认向左")]
    [SerializeField] private Vector2 flyDirection = Vector2.left;
    [Tooltip("飞行速度（世界单位/秒）。匀速，不加速度、不受重力影响")]
    [SerializeField, Min(0.01f)] private float flySpeed = 8f;
    [Tooltip("精灵本身在 0° 时朝向哪个方向：向右=0，向上=90，向左=180。用来把箭头的朝向旋转到飞行方向")]
    [SerializeField] private float spriteFacingAngle = 90f;

    [Header("=== 命中判定 ===")]
    [Tooltip("小羊所在层（本项目小羊在 Default 层，保持 1 即可）")]
    [SerializeField] private LayerMask playerLayer = 1;
    [Tooltip("额外的障碍物层。留空时靠下面「实体碰撞体」选项自动判定，一般不用填")]
    [SerializeField] private LayerMask obstacleLayer;
    [Tooltip("碰到任意「非 Trigger 的实体碰撞体」就破碎（地面 / 墙 / 桥 / 道具都算）。" +
             "只忽略 Trigger 区域、玩家、其他箭矢和弩本身")]
    [SerializeField] private bool breakOnAnySolidCollider = true;
    [Tooltip("箭矢命中时是否杀死小羊")]
    [SerializeField] private bool killPlayerOnHit = true;

    [Header("=== 序列帧动画 ===")]
    [Tooltip("箭矢 Animator（留空自动取同物体上的）。控制器里需要有飞行 / 破碎两个状态")]
    [SerializeField] private Animator animator;
    [Tooltip("飞行状态名（Arrow.controller 中的状态名）")]
    [SerializeField] private string flyStateName = "Arrow";
    [Tooltip("破碎状态名（Arrow.controller 中的状态名）")]
    [SerializeField] private string breakStateName = "ArrowBreaking";
    [Tooltip("非循环剪辑时脚本会自动从头重播，让飞行序列帧一直转")]
    [SerializeField] private bool loopFlyAnimation = true;
    [Tooltip("读不到破碎动画时长时，按这个秒数后销毁")]
    [SerializeField, Min(0.05f)] private float breakDurationFallback = 0.7f;

    [Header("=== 清理 ===")]
    [Tooltip("最长存活时间（秒），防止漏网的箭矢永远留在场景里")]
    [SerializeField, Min(0.1f)] private float maxLifetime = 8f;

    [Header("=== 箭矢音效 ===")]
    [Tooltip("飞行中循环播放的飞掠音（loop = true）。在弩箭发射音 SFX_Pieces_Crossbow_Shot 播放结束后立即开始，与飞行同步")]
    [SerializeField] private AudioClip flyByClip;
    [Tooltip("命中实体障碍物（如金字塔楼梯 PyramidStair）的一次性音效")]
    [SerializeField] private AudioClip hitSolidsClip;
    [Tooltip("命中玩家（羊 Sheep）的一次性音效")]
    [SerializeField] private AudioClip hitPlayerClip;
    [Tooltip("飞掠音启动延迟（秒）。CrossbowShooter 发射时会按弩箭发射音长度自动覆盖，确保飞掠音与发射音无缝衔接；手动放置时按此值")]
    [SerializeField, Min(0f)] private float flyByStartDelay = 0f;

    [Header("=== 音量距离衰减（与 ProximityLoopAudio2D 一致）===")]
    [Tooltip("音量衰减圆心。留空 = 箭矢自身位置，即声音随箭矢在世界中的位置衰减")]
    [SerializeField] private Transform rangeOrigin;
    [SerializeField, Min(0.1f)] private float hearingRadius = 10f;
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;
    [SerializeField] private ProximityLoopAudio2D.VolumeCurve volumeCurve = ProximityLoopAudio2D.VolumeCurve.Squared;
    [Tooltip("用于就近查找活着的玩家（羊/兔）以计算距离衰减，与命中判定的 playerLayer 复用同一套层")]
    [SerializeField] private LayerMask audioPlayerLayer = ~0;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    #endregion

    #region 运行时状态
    private Rigidbody2D rb;
    private Collider2D hitCollider;
    private Component owner;
    private bool launched;
    private bool broken;
    private float lifeTimer;
    private int flyHash;
    private int breakHash;

    // 音频
    private AudioSource flyBySource;    // 循环播放飞掠音
    private AudioSource oneShotSource;  // 播放命中一次性音效
    private float flyByTimer;
    private bool flyByActive;
    #endregion

    #region 对外只读接口
    public bool IsBroken => broken;
    public Vector2 FlyDirection => flyDirection;
    public float FlySpeed => flySpeed;

    /// <summary>飞掠音启动延迟（秒），由 CrossbowShooter 在发射时按弩箭发射音长度设置。</summary>
    public float FlyByStartDelay { get => flyByStartDelay; set => flyByStartDelay = value; }
    #endregion

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();

        flyHash = Animator.StringToHash(flyStateName);
        breakHash = Animator.StringToHash(breakStateName);

        hitCollider = GetComponent<Collider2D>();
        if (hitCollider == null) hitCollider = gameObject.AddComponent<BoxCollider2D>();
        hitCollider.isTrigger = true;

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();

        // Kinematic：不会受重力、阻尼、碰撞反弹影响，位移完全由 FixedUpdate 里的 MovePosition 决定
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.useFullKinematicContacts = true; // 兼容实体碰撞体，Trigger 回调也更稳
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // 音频声道：飞掠音走循环声道，命中音走一次性声道，互不打断
        flyBySource = gameObject.AddComponent<AudioSource>();
        flyBySource.playOnAwake = false;
        flyBySource.loop = true;
        flyBySource.spatialBlend = 0f; // 2D 音，音量靠脚本按距离衰减（与 ProximityLoopAudio2D 一致）
        flyBySource.dopplerLevel = 0f;

        oneShotSource = gameObject.AddComponent<AudioSource>();
        oneShotSource.playOnAwake = false;
        oneShotSource.loop = false;
        oneShotSource.spatialBlend = 0f;
        oneShotSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        // 手动摆在场景里直接播放时，也按默认方向飞起来
        if (!launched)
        {
            launched = true;
            ApplyRotation();
            PlayFlyAnimation();
        }
    }

    private void Update()
    {
        if (broken) return;

        lifeTimer += Time.deltaTime;
        if (maxLifetime > 0f && lifeTimer >= maxLifetime)
        {
            Break();
            return;
        }

        UpdateFlyByAudio();
        LoopFlyAnimation();
    }

    private void FixedUpdate()
    {
        if (!launched || broken || rb == null) return;

        // 匀速位移：每帧移动 flySpeed * dt，不存在加速度
        rb.MovePosition(rb.position + flyDirection * (flySpeed * Time.fixedDeltaTime));
    }

    #region 发射
    /// <summary>
    /// 由 CrossbowShooter 调用：设定方向、速度并开始飞行。
    /// </summary>
    /// <param name="direction">世界空间方向（内部会归一化）</param>
    /// <param name="speed">世界单位/秒；&lt;=0 时保留预制体上的默认速度</param>
    /// <param name="ownerComponent">发射者（弩），其碰撞体会被忽略，避免刚出生就扎到自己</param>
    public void Launch(Vector2 direction, float speed, Component ownerComponent = null)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.left;
        flyDirection = direction.normalized;

        if (speed > 0f) flySpeed = speed;

        owner = ownerComponent;
        launched = true;
        broken = false;
        lifeTimer = 0f;

        // 飞掠音：延迟到弩箭发射音结束后再开始（延迟时长由 CrossbowShooter 按发射音长度设置）
        flyByTimer = flyByStartDelay;
        flyByActive = false;
        if (flyBySource != null)
        {
            flyBySource.Stop();
            flyBySource.clip = null;
        }

        ApplyRotation();
        PlayFlyAnimation();
        IgnoreOwnerColliders();

        if (debugLogs) Debug.Log($"[{name}] 发射：dir={flyDirection}, speed={flySpeed}, flyByDelay={flyByStartDelay}");
    }

    private void ApplyRotation()
    {
        // 精灵 0° 时的朝向为 spriteFacingAngle，换算成飞行方向所需的角度
        float angle = Mathf.Atan2(flyDirection.y, flyDirection.x) * Mathf.Rad2Deg - spriteFacingAngle;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        if (rb != null) rb.rotation = angle;
    }

    private void IgnoreOwnerColliders()
    {
        if (owner == null || hitCollider == null) return;

        Collider2D[] ownerColliders = owner.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] == null) continue;
            Physics2D.IgnoreCollision(hitCollider, ownerColliders[i], true);
        }
    }
    #endregion

    #region 序列帧
    private void PlayFlyAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.Play(flyHash, 0, 0f);
    }

    private void LoopFlyAnimation()
    {
        if (!loopFlyAnimation || animator == null || animator.runtimeAnimatorController == null) return;
        if (animator.IsInTransition(0)) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != flyHash) return;

        // 循环剪辑由 Animator 自己循环；非循环剪辑播到头就手动重播
        if (!state.loop && state.normalizedTime >= 0.999f)
        {
            animator.Play(flyHash, 0, 0f);
        }
    }
    #endregion

    #region 命中 / 破碎
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (broken || other == null) return;

        // 忽略发射者、其他箭矢
        if (IsOwnerOrOtherArrow(other)) return;

        bool playerLike = IsInLayerMask(other.gameObject.layer, playerLayer);

        if (playerLike)
        {
            BunnyController bunny = BunnyContactUtility.FindBunny(other);
            if (bunny != null && !bunny.isDead)
            {
                if (killPlayerOnHit) bunny.TriggerDeath();
                PlayHitSound(hitPlayerClip);
                if (debugLogs) Debug.Log($"[{name}] 命中玩家，破碎。");
                Break();
                return;
            }
            // 只是恰好和小羊同层的障碍，继续往下走障碍判定
        }

        // 明确配置了障碍物层：命中即破碎
        if (obstacleLayer.value != 0 && IsInLayerMask(other.gameObject.layer, obstacleLayer))
        {
            PlayHitSound(hitSolidsClip);
            if (debugLogs) Debug.Log($"[{name}] 命中障碍物层，破碎。");
            Break();
            return;
        }

        // 默认策略：任意实体碰撞体都算障碍物；Trigger 区域（金币、镜头区域等）直接穿过
        if (breakOnAnySolidCollider && !other.isTrigger)
        {
            PlayHitSound(hitSolidsClip);
            if (debugLogs) Debug.Log($"[{name}] 命中实体碰撞体 {other.name}，破碎。");
            Break();
        }
    }

    private bool IsOwnerOrOtherArrow(Collider2D other)
    {
        if (other.GetComponentInParent<ArrowProjectile>() != null) return true;
        if (owner != null && (other.transform == owner.transform || other.transform.IsChildOf(owner.transform))) return true;
        return false;
    }

    /// <summary>停止飞行并播放破碎动画，动画结束后销毁自身。</summary>
    public void Break()
    {
        if (broken) return;
        broken = true;

        StopFlyBy();

        if (hitCollider != null) hitCollider.enabled = false;
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        float duration = PlayBreakAnimation();
        Destroy(gameObject, Mathf.Max(0.02f, duration));
    }

    private float PlayBreakAnimation()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.Play(breakHash, 0, 0f);

            float clipLength = FindClipLength(breakStateName);
            if (clipLength > 0f) return clipLength;
        }

        return breakDurationFallback;
    }

    private float FindClipLength(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return 0f;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == stateName) return clips[i].length;
        }

        return 0f;
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
    #endregion

    #region 音效
    /// <summary>
    /// 每帧更新飞掠音：延迟计时结束后开始循环播放，并持续按到玩家的距离刷新音量。
    /// </summary>
    private void UpdateFlyByAudio()
    {
        // 延迟启动：等弩箭发射音 SFX_Pieces_Crossbow_Shot 播完再无缝衔接飞掠音
        if (!flyByActive)
        {
            flyByTimer -= Time.deltaTime;
            if (flyByTimer <= 0f) StartFlyBy();
        }

        if (flyByActive && flyBySource != null)
        {
            flyBySource.volume = DistanceVolume();
        }
    }

    private void StartFlyBy()
    {
        flyByActive = true;
        if (flyBySource == null || flyByClip == null) return;

        flyBySource.clip = flyByClip;
        flyBySource.loop = true;
        flyBySource.volume = DistanceVolume();
        flyBySource.Play();

        if (debugLogs) Debug.Log($"[{name}] 飞掠音开始（音量={flyBySource.volume:0.00}）。");
    }

    private void StopFlyBy()
    {
        flyByActive = false;
        if (flyBySource != null)
        {
            flyBySource.Stop();
            flyBySource.clip = null;
        }
    }

    /// <summary>在箭矢当前位置播放一次性命中音效，音量按到玩家的距离衰减。</summary>
    private void PlayHitSound(AudioClip clip)
    {
        if (clip == null || oneShotSource == null) return;

        float volume = DistanceVolume();
        oneShotSource.PlayOneShot(clip, volume);

        if (debugLogs) Debug.Log($"[{name}] 命中音效 {clip.name}（音量={volume:0.00}）。");
    }

    /// <summary>按到最近活着的玩家的距离计算当前音量，算法与 ProximityLoopAudio2D 一致。</summary>
    private float DistanceVolume()
    {
        float distance = TryGetNearestPlayerDistance(out float d) ? d : float.MaxValue;
        return Mathf.Clamp01(maxVolume * VolumeScaleAtDistance(distance));
    }

    private float VolumeScaleAtDistance(float distance)
    {
        float remaining = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, hearingRadius));
        switch (volumeCurve)
        {
            case ProximityLoopAudio2D.VolumeCurve.SquareRoot:
                return Mathf.Sqrt(remaining);
            case ProximityLoopAudio2D.VolumeCurve.Squared:
                return remaining * remaining;
            default:
                return remaining;
        }
    }

    private bool TryGetNearestPlayerDistance(out float distance)
    {
        distance = float.MaxValue;

        // 缩放过的箭矢用 world position 更稳，直接取 transform.position
        Vector2 center = rangeOrigin != null ? (Vector2)rangeOrigin.position : (Vector2)transform.position;
        float searchRadius = Mathf.Max(0.1f, hearingRadius) + 2f;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, searchRadius, audioPlayerLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            BunnyController player = BunnyContactUtility.FindBunny(hit);
            if (player == null || player.isDead) continue;

            float d = Vector2.Distance(center, player.transform.position);
            if (d < distance) distance = d;
        }

        return distance < float.MaxValue;
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        Vector2 dir = flyDirection.sqrMagnitude > 0.0001f ? flyDirection.normalized : Vector2.left;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3)dir * 2f);
    }
    #endregion
}
