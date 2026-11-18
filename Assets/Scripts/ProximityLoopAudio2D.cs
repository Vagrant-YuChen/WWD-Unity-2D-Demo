using UnityEngine;

/// <summary>
/// 持续/阶段性循环播放的环境音效（喷火器音圈的通用版）。
///
/// 三种播放模式：
///   1) Continuous —— 从游戏开始到结束不停循环播放 loopClip（quicksand 等）；
///   2) FollowSpray —— 只在陷阱“正在喷”期间循环播放，Idle 空档期静音（消防栓）；
///   3) ShotAtFrame —— 每轮射击时，动画播到指定帧触发一次性音效，Idle 空档期静音（弩箭）。
///
/// 三种模式都遵循同一套距离衰减：
///   贴脸 = 满音量；圆边缘 = 0；默认用 Squared 曲线，远处衰减更快；
///   玩家（兔 / 羊）死亡后不再计入距离；圈外听不到。
///
/// 音圈圆心由 rangeOrigin 决定（留空 = 本物体），半径 hearingRadius / 最大音量 maxVolume
/// 可在 Inspector 里调，并在 Scene 视图实时显示。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class ProximityLoopAudio2D : MonoBehaviour
{
    /// <summary>音量随距离的衰减曲线。</summary>
    public enum VolumeCurve
    {
        [Tooltip("音量 = 1 - 距离/半径")]
        Linear = 0,
        [Tooltip("音量 = √(1 - 距离/半径)，远处衰减更慢")]
        SquareRoot = 1,
        [Tooltip("音量 = (1 - 距离/半径)²，远处衰减更快（本项目默认）")]
        Squared = 2,
    }

    /// <summary>音频的播放方式。</summary>
    public enum PlaybackMode
    {
        [Tooltip("从游戏开始到结束一直循环播放（不停顿）")]
        Continuous = 0,
        [Tooltip("只在关联的消防栓正在喷水期间循环播放，Idle 空档期静音")]
        FollowSpray = 1,
        [Tooltip("每轮射击时，动画播到 triggerFrame 触发一次性音效，Idle 空档期静音")]
        ShotAtFrame = 2,
        [Tooltip("蜂巢未爆炸（idle）时循环播放；蜂巢爆炸后停止，玩家复活重置蜂巢后再次恢复")]
        FollowHiveIdle = 3,
        [Tooltip("蜂群追击玩家期间循环播放；音圈跟随蜂群中心一起移动，胜利/小兔死亡重置后停止")]
        FollowSwarm = 4,
    }

    #region 可配置参数
    [Tooltip("持续/循环播放的音频")]
    [SerializeField] private AudioClip loopClip;
    [Tooltip("音圈原点：音量按「玩家到这个 Transform」的直线距离衰减。留空 = 用本物体自身位置")]
    [SerializeField] private Transform rangeOrigin;
    [Tooltip("音圈半径（世界单位）。圈外听不到，圈内按距离衰减")]
    [SerializeField, Min(0.1f)] private float hearingRadius = 10f;
    [Tooltip("最大音量（玩家贴着音圈原点时的音量）")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;
    [Tooltip("距离衰减曲线，默认 Squared（与喷火器一致）")]
    [SerializeField] private VolumeCurve volumeCurve = VolumeCurve.Squared;
    [Tooltip("要检测的层；脚本只认 BunnyController / SheepController，所以多选也不会误判")]
    [SerializeField] private LayerMask playerLayer = ~0;
    [Tooltip("播放模式：Continuous 一直播；FollowSpray 随陷阱喷水状态开关；ShotAtFrame 随弩箭动画帧触发一次性")]
    [SerializeField] private PlaybackMode playbackMode = PlaybackMode.Continuous;

    // FollowSpray：关联的消防栓脚本，用来读取 IsSpraying（开启/空档）。
    [Tooltip("FollowSpray 模式：同物体上的 FireHydrantSpray，读取 IsSpraying 决定是否播放")]
    [SerializeField] private FireHydrantSpray fireHydrantSpray;
    // ShotAtFrame：关联的弩箭脚本，用来读取射击动画进度；在 triggerFrame 处触发一次性音效。
    [Tooltip("ShotAtFrame 模式：同物体上的 CrossbowShooter，读取射击动画进度触发一次性音效")]
    [SerializeField] private CrossbowShooter crossbowShooter;
    // FollowHiveIdle：关联的蜂巢，读取 HasExploded 决定是否播放 idle 声。
    [Tooltip("FollowHiveIdle 模式：关联的 BeeHiveExplosion，未爆炸（idle）时循环播放；爆炸后停止，重置后恢复。")]
    [SerializeField] private BeeHiveExplosion beeHive;

    // FollowSwarm：关联的蜂群控制器，读取 FollowAudioActive 决定是否播放；音圈中心跟随蜂群。
    [Tooltip("FollowSwarm 模式：关联的 BeeSwarmController，蜂群追击期间循环播放；音圈中心随蜂群中心移动。")]
    [SerializeField] private BeeSwarmController beeSwarm;
    [Tooltip("FollowSwarm：蜂群停止追击（击杀小兔）后，音量在多少秒内淡出到 0（避免瞬间掐断）。参考螺旋桨 0.5s。")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;

    [Header("ShotAtFrame 参数")]
    [Tooltip("弩箭动画中触发音效的帧序号（按 1 起算，便于对照序列帧命名）。例：第 12 帧 → 填 12；总帧数 25 → 填 25")]
    [SerializeField, Min(0)] private int triggerFrame = 12;
    [SerializeField, Min(1)] private int totalFrames = 25;

    [Tooltip("在 Scene 视图画出音圈（不选中也显示）")]
    [SerializeField] private bool drawGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);
    #endregion

    #region 运行时状态
    private AudioSource source;
    private bool shotTriggeredThisCycle;
    private bool fadingOut;
    private float fadeTimer;
    private float fadeFromVolume;
    #endregion

    /// <summary>音圈的世界圆心。FollowSwarm 模式下跟随蜂群中心移动。</summary>
    public Vector2 AudioCenter
    {
        get
        {
            if (playbackMode == PlaybackMode.FollowSwarm && beeSwarm != null)
                return beeSwarm.SwarmCenter;
            return rangeOrigin != null ? (Vector2)rangeOrigin.position : (Vector2)transform.position;
        }
    }

    /// <summary>
    /// 当前循环音频。供弩箭等外部脚本读取长度，用来让「箭矢飞掠音」在弩箭发射音结束后无缝衔接。
    /// </summary>
    public AudioClip LoopClip => loopClip;

    private void Awake()
    {
        if (!TryGetComponent(out source))
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 是否听得见由音圈决定，圆内音量稳定
        source.dopplerLevel = 0f;
        source.clip = loopClip;
    }

    private void Start()
    {
        if (loopClip != null && playbackMode == PlaybackMode.Continuous) source.Play();
    }

    private void OnDisable()
    {
        if (source != null) source.Stop();
    }

    private void Update()
    {
        if (loopClip == null) return;

        if (source.clip != loopClip) source.clip = loopClip;

        switch (playbackMode)
        {
            case PlaybackMode.Continuous:
                UpdateContinuous();
                break;
            case PlaybackMode.FollowSpray:
                UpdateFollowSpray();
                break;
            case PlaybackMode.ShotAtFrame:
                UpdateShotAtFrame();
                break;
            case PlaybackMode.FollowHiveIdle:
                UpdateFollowHiveIdle();
                break;
            case PlaybackMode.FollowSwarm:
                UpdateFollowSwarm();
                break;
        }
    }

    /// <summary>距离 → 实际音量（0 = 圈外/很远，maxVolume = 贴脸）。</summary>
    private float DistanceVolume()
    {
        float distance = TryGetNearestPlayerDistance(out float d) ? d : float.MaxValue;
        return Mathf.Clamp01(maxVolume * VolumeScaleAtDistance(distance) * GameAudioManager.SfxMultiplier);
    }

    /// <summary>距离 → 音量倍率（0 = 圈外，1 = 贴脸）。</summary>
    private float VolumeScaleAtDistance(float distance)
    {
        float remaining = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, hearingRadius));
        switch (volumeCurve)
        {
            case VolumeCurve.SquareRoot:
                return Mathf.Sqrt(remaining);
            case VolumeCurve.Squared:
                return remaining * remaining;
            default:
                return remaining;
        }
    }

    /// <summary>找到离音圈圆心最近的活着的玩家（兔 / 羊）；输出到圆心的直线距离。</summary>
    private bool TryGetNearestPlayerDistance(out float distance)
    {
        distance = float.MaxValue;

        Vector2 center = AudioCenter;
        float searchRadius = Mathf.Max(0.1f, hearingRadius) + 2f;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, searchRadius, playerLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            BunnyController player = BunnyContactUtility.FindBunny(hit);   // 羊继承自 BunnyController
            if (player == null || player.isDead) continue;

            float d = Vector2.Distance(center, player.transform.position);
            if (d < distance) distance = d;
        }

        return distance < float.MaxValue;
    }

    #region 各播放模式

    private void UpdateContinuous()
    {
        source.loop = true;
        if (!source.isPlaying) source.Play();
        source.volume = DistanceVolume();
    }

    private void UpdateFollowSpray()
    {
        source.loop = true;
        bool spraying = fireHydrantSpray != null && fireHydrantSpray.IsSpraying;

        if (spraying)
        {
            if (!source.isPlaying) source.Play();
            source.volume = DistanceVolume();
        }
        else if (source.isPlaying)
        {
            source.Stop();
        }
    }

    private void UpdateShotAtFrame()
    {
        bool shotTriggered = false;

        if (crossbowShooter != null
            && crossbowShooter.TryGetShotProgress(out float normalizedTime))
        {
            float frame = normalizedTime * totalFrames;

            if (!shotTriggeredThisCycle && frame >= triggerFrame)
            {
                shotTriggeredThisCycle = true;
                float dist = TryGetNearestPlayerDistance(out float d) ? d : float.MaxValue;
                float vol = Mathf.Clamp01(maxVolume * VolumeScaleAtDistance(dist) * GameAudioManager.SfxMultiplier);
                if (vol > 0.0001f)
                {
                    source.PlayOneShot(loopClip, vol);
                }
            }

            // 动画重新回到第 1 帧附近时，准备下一轮触发
            if (normalizedTime < 0.05f) shotTriggeredThisCycle = false;
        }
        else
        {
            shotTriggeredThisCycle = false;
        }
    }

    /// <summary>蜂巢 idle 声：未爆炸时循环，爆炸后静音（复活重置后再次恢复）。</summary>
    private void UpdateFollowHiveIdle()
    {
        source.loop = true;
        bool idle = beeHive == null || !beeHive.HasExploded;

        if (idle)
        {
            if (!source.isPlaying) source.Play();
            source.volume = DistanceVolume();
        }
        else if (source.isPlaying)
        {
            source.Stop();
        }
    }

    /// <summary>蜂群追击声：追击期间循环，音圈中心跟随蜂群移动；击杀/胜利/重置后淡出停止。</summary>
    private void UpdateFollowSwarm()
    {
        source.loop = true;
        bool active = beeSwarm != null && beeSwarm.FollowAudioActive;

        if (active)
        {
            // 重新进入追击：取消淡出，恢复按距离的音量
            fadingOut = false;
            if (!source.isPlaying) source.Play();
            source.volume = DistanceVolume();   // AudioCenter 已改为跟随蜂群中心
            return;
        }

        if (!source.isPlaying) return;

        // 不再追击（如小兔被击杀）：进入 0.5s 淡出，避免声音瞬间掐断。
        if (!fadingOut)
        {
            fadingOut = true;
            fadeFromVolume = source.volume;
            fadeTimer = Mathf.Max(0f, fadeOutDuration);
            if (fadeTimer <= 0f)
            {
                source.Stop();
                fadingOut = false;
                return;
            }
        }

        fadeTimer -= Time.deltaTime;
        float k = Mathf.Clamp01(fadeTimer / Mathf.Max(0.0001f, fadeOutDuration));
        source.volume = fadeFromVolume * k;

        if (fadeTimer <= 0f)
        {
            source.Stop();
            source.volume = 0f;
            fadingOut = false;
        }
    }
    #endregion

    #region 编辑器
    private void OnValidate()
    {
        hearingRadius = Mathf.Max(0.1f, hearingRadius);
        maxVolume = Mathf.Clamp01(maxVolume);
        totalFrames = Mathf.Max(1, totalFrames);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        Vector3 center = new Vector3(AudioCenter.x, AudioCenter.y, 0f);
        float r = Mathf.Max(0.1f, hearingRadius);

        Color fill = gizmoColor;
        fill.a = 0.07f;
        Gizmos.color = fill;
        Gizmos.DrawSphere(center, r);

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(center, r);

        // 半音量参考圈（按当前曲线：Squared 在约 0.293r 处为半音量，Linear 在 0.5r）
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.4f);
        Gizmos.DrawWireSphere(center, r * DistanceScaleAtHalfVolume());
    }

    private float DistanceScaleAtHalfVolume()
    {
        switch (volumeCurve)
        {
            case VolumeCurve.SquareRoot:
                return 1f - 0.25f;      // √(0.25) = 0.5
            case VolumeCurve.Squared:
                return 1f - Mathf.Sqrt(0.5f);
            default:
                return 0.5f;
        }
    }
    #endregion
}
