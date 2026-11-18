using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局音量宏观调控：只有 BGM 和 SFX 两个倍率。
///
/// 50%（默认，M5 / S6）时倍率为 1.0，完全保留场景 / 资产里已经调好的基础音量；
/// 0% 为静音；音乐 100% = 2.0x，音效 90% = 1.8x。
///
/// 本脚本只负责「乘倍率」，不修改任何 clip、基础音量或播放逻辑。
/// BGM 通过场景里 Main Camera 上的 AudioSource（clip 名以 BGM 开头）识别，
/// 场景加载时缓存其基础音量，之后按倍率实时应用。
/// </summary>
[DisallowMultipleComponent]
public sealed class GameAudioManager : MonoBehaviour
{
    private const string MusicStepKey = "wwd.audio.musicStep";
    private const string SfxStepKey = "wwd.audio.sfxStep";
    private const int DefaultStep = 5;

    private const int MusicMaxStep = 10; // 0..100%
    private const int SfxMaxStep = 9;    // 0..90%

    private static int _musicStep = DefaultStep;
    private static int _sfxStep = DefaultStep;
    private static GameAudioManager _instance;

    /// <summary>音乐档位 0..10（10 = 100%）。</summary>
    public static int MusicStep => _musicStep;

    /// <summary>音效档位 0..9（9 = 90%）。</summary>
    public static int SfxStep => _sfxStep;

    /// <summary>音乐倍率：50% = 1.0，100% = 2.0，0% = 0。</summary>
    public static float MusicMultiplier => _musicStep / 5f;

    /// <summary>音效倍率：50% = 1.0，90% = 1.8，0% = 0。</summary>
    public static float SfxMultiplier => _sfxStep / 5f;

    /// <summary>音效倍率变化时通知，供正在播放的循环音实时刷新音量。</summary>
    public static event Action SfxMultiplierChanged;

    private readonly List<AudioSource> _bgmSources = new List<AudioSource>();
    private readonly List<float> _bgmBaseVolumes = new List<float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject("GameAudioManager");
        DontDestroyOnLoad(go);
        go.AddComponent<GameAudioManager>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        LoadSteps();

        SceneManager.sceneLoaded += HandleSceneLoaded;
        // AfterSceneLoad：首个场景的 sceneLoaded 可能已经触发，这里补一次。
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        _instance = null;
    }

    private void OnApplicationQuit() => PlayerPrefs.Save();

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) PlayerPrefs.Save();
    }

    private static void LoadSteps()
    {
        _musicStep = Mathf.Clamp(PlayerPrefs.GetInt(MusicStepKey, DefaultStep), 0, MusicMaxStep);
        _sfxStep = Mathf.Clamp(PlayerPrefs.GetInt(SfxStepKey, DefaultStep), 0, SfxMaxStep);
    }

    public static void SetMusicStep(int step)
    {
        step = Mathf.Clamp(step, 0, MusicMaxStep);
        if (step == _musicStep) return;

        _musicStep = step;
        PlayerPrefs.SetInt(MusicStepKey, step);
        if (_instance != null) _instance.ApplyMusicVolume();
    }

    public static void SetSfxStep(int step)
    {
        step = Mathf.Clamp(step, 0, SfxMaxStep);
        if (step == _sfxStep) return;

        _sfxStep = step;
        PlayerPrefs.SetInt(SfxStepKey, step);
        SfxMultiplierChanged?.Invoke();
        // SFX 的最终音量在每个播放点读取 SfxMultiplier，循环音在各自 Update 里实时刷新。
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CaptureBgmSources(scene);
        ApplyMusicVolume();
        InstallOptionsPanel(scene);
    }

    private void CaptureBgmSources(Scene scene)
    {
        _bgmSources.Clear();
        _bgmBaseVolumes.Clear();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            AudioSource[] sources = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource src = sources[i];
                if (!IsBgmSource(src)) continue;

                _bgmSources.Add(src);
                _bgmBaseVolumes.Add(src.volume);
            }
        }
    }

    private static bool IsBgmSource(AudioSource src)
    {
        if (src == null || src.clip == null) return false;

        // 本项目所有 BGM 片段都以 BGM 开头（BGM_Startscene / BGM_Scene1 / BGM_Scene2 ...）。
        if (src.clip.name.StartsWith("BGM", System.StringComparison.OrdinalIgnoreCase)) return true;

        // 兜底：Main Camera 上循环、playOnAwake 的音源。
        Camera cam = Camera.main;
        return cam != null
            && src.gameObject == cam.gameObject
            && src.loop
            && src.playOnAwake;
    }

    private void ApplyMusicVolume()
    {
        float mult = MusicMultiplier;
        for (int i = 0; i < _bgmSources.Count; i++)
        {
            AudioSource src = _bgmSources[i];
            if (src == null) continue;
            src.volume = Mathf.Clamp01(_bgmBaseVolumes[i] * mult);
        }
    }

    private static void InstallOptionsPanel(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t.name != "Panel_Options") continue;
                // 只给真正带音量 tickmark 的面板挂脚本，避免误伤同名的其它面板。
                if (t.Find("GameUI_TickMark") == null) continue;
                if (t.GetComponent<AudioOptionsPanel>() == null)
                    t.gameObject.AddComponent<AudioOptionsPanel>();
            }
        }
    }
}
