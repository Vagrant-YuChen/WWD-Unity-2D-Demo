using UnityEngine;

/// <summary>
/// 触发式音频管理器（SFX 系统）。
///
/// 这是一个可持续追加的通用触发音系统，把游戏里所有「被动/主动触发」的音效
/// 统一到这里管理。当前支持两种声音：
///   1) 一次性音效（PlayOneShot / PlayDefaultOneshot）：开关门、炮击发射、掉落落地等，
///      触发即响一次，不打断循环声道；
///   2) 循环音（StartLoop / StopLoop）：螺旋桨、机器运转等「激活后持续播放、结束立即掐断」
///      的声音，走独立声道，不被一次性音效打断。
///
/// 循环音支持自动跟踪一个 IActivityAudioSource：
///   - 激活时自动开始循环（如踩上螺旋桨、风扇转动）；
///   - 停转/离开时进入可配置的短淡出（默认 0.5 秒），从当前音量平滑消散后停止；
///     如果淡出期间重新启动，音频会取消淡出并继续播放，不会重新从开头跳播。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class TriggeredAudio2D : MonoBehaviour
{
    /// <summary>实现该接口的组件可以被 TriggeredAudio2D 自动跟踪是否应该发声。</summary>
    public interface IActivityAudioSource
    {
        /// <summary>当前是否处于「应该发出声音」的激活状态。</summary>
        bool IsAudioActive { get; }
    }

    #region 循环音（激活时开始，掐断时停止）
    [Header("=== 循环音（激活后播放，停转/离开时掐断） ===")]
    [Tooltip("循环播放的音频（如螺旋桨持续运转声）")]
    [SerializeField] private AudioClip loopClip;
    [SerializeField, Range(0f, 1f)] private float loopVolume = 1f;
    [Tooltip("循环音的激活源：挂载了 IActivityAudioSource 的组件（如 PropellerWind）。" +
             " 激活时自动播放循环，不激活时进入下方的淡出。留空则不自动开关，只供手动调用。")]
    [SerializeField] private MonoBehaviour loopActivitySource;
    [Tooltip("激活源停止后，循环音淡出所需时间（秒）。0 = 立即停止；建议 0.5 秒。")]
    [SerializeField, Min(0f)] private float loopFadeOutDuration = 0.5f;
    #endregion

    #region 一次性音效（外部脚本按需调用）
    [Header("=== 一次性音效（按需触发） ===")]
    [Tooltip("默认一次性音效（外部调用 PlayDefaultOneshot() 时使用）")]
    [SerializeField] private AudioClip defaultOneshotClip;
    [SerializeField, Range(0f, 1f)] private float defaultOneshotVolume = 1f;
    #endregion

    #region 运行时状态
    private AudioSource loopSource;      // 专门负责循环，不会被一次性音效打断
    private AudioSource oneshotSource;   // 专门负责一次性，不打断循环
    private bool loopPlaying;
    private bool prevActive;
    private bool fadingOut;
    private float fadeTimer;
    private float fadeFromVolume;
    #endregion

    /// <summary>循环音当前是否在播放。</summary>
    public bool IsLoopPlaying => loopPlaying;

    private void Awake()
    {
        var all = GetComponents<AudioSource>();
        if (all.Length >= 2)
        {
            loopSource = all[0];
            oneshotSource = all[1];
        }
        else if (all.Length == 1)
        {
            loopSource = all[0];
            oneshotSource = gameObject.AddComponent<AudioSource>();
        }
        else
        {
            loopSource = gameObject.AddComponent<AudioSource>();
            oneshotSource = gameObject.AddComponent<AudioSource>();
        }

        SetupSource(loopSource, loop: true, playOnAwake: false);
        SetupSource(oneshotSource, loop: false, playOnAwake: false);
    }

    private void Start()
    {
        // 启动时若已处于激活态，直接开始循环
        if (loopActivitySource is IActivityAudioSource src && src.IsAudioActive && loopClip != null)
        {
            StartLoop();
        }
        prevActive = ReadActivity();
    }

    private void Update()
    {
        bool active = ReadActivity();

        if (active && !prevActive && loopClip != null)
            StartLoop();                              // 刚激活：开始循环（风扇开始转）
        else if (!active && prevActive && loopPlaying)
            StopLoop();                               // 刚停转：开始淡出循环（风扇停转）

        UpdateLoopFade();
        prevActive = active;
    }

    private bool ReadActivity()
    {
        return loopActivitySource is IActivityAudioSource s && s.IsAudioActive;
    }

    #region 对外公开 API（供其他脚本调用）

    /// <summary>手动开始循环播放 loopClip。</summary>
    public void StartLoop()
    {
        if (loopClip == null || loopSource == null) return;

        fadingOut = false;
        fadeTimer = 0f;
        loopSource.clip = loopClip;
        loopSource.loop = true;
        loopSource.volume = loopVolume * GameAudioManager.SfxMultiplier;
        if (!loopSource.isPlaying) loopSource.Play();
        loopPlaying = true;
    }

    /// <summary>停止循环并开始淡出；淡出结束后停止 AudioSource。</summary>
    public void StopLoop()
    {
        if (loopSource == null) return;
        if (!loopSource.isPlaying)
        {
            loopPlaying = false;
            fadingOut = false;
            return;
        }

        fadeFromVolume = loopSource.volume;
        fadeTimer = Mathf.Max(0f, loopFadeOutDuration);
        fadingOut = fadeTimer > 0f;
        if (!fadingOut)
        {
            loopSource.Stop();
            loopPlaying = false;
        }
    }

    private void UpdateLoopFade()
    {
        if (!fadingOut || loopSource == null) return;

        fadeTimer -= Time.deltaTime;
        float duration = Mathf.Max(0.0001f, loopFadeOutDuration);
        loopSource.volume = fadeFromVolume * Mathf.Clamp01(fadeTimer / duration);

        if (fadeTimer <= 0f)
        {
            loopSource.Stop();
            loopSource.volume = 0f;
            fadingOut = false;
            loopPlaying = false;
        }
    }

    /// <summary>播放一次性音效（不打断循环声道）。</summary>
    public void PlayOneShot(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || oneshotSource == null) return;
        oneshotSource.PlayOneShot(clip, volumeScale * GameAudioManager.SfxMultiplier);
    }

    /// <summary>播放 Inspector 里配置的默认一次性音效。</summary>
    public void PlayDefaultOneshot() => PlayOneShot(defaultOneshotClip, defaultOneshotVolume);

    private void OnEnable() => GameAudioManager.SfxMultiplierChanged += OnSfxMultiplierChanged;
    private void OnDisable() => GameAudioManager.SfxMultiplierChanged -= OnSfxMultiplierChanged;
    private void OnSfxMultiplierChanged()
    {
        if (!loopPlaying || fadingOut || loopSource == null) return;
        loopSource.volume = loopVolume * GameAudioManager.SfxMultiplier;
    }

    #endregion

    #region 内部
    private static void SetupSource(AudioSource s, bool loop, bool playOnAwake)
    {
        s.playOnAwake = playOnAwake;
        s.loop = loop;
        s.spatialBlend = 0f;   // 2D，是否能听到由调用方/音圈决定
        s.dopplerLevel = 0f;
    }
    #endregion
}
