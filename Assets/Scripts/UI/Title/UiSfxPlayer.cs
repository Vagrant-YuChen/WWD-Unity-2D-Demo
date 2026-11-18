using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// UI 音效播放器（悬停 / 点击）。
    /// 每个场景里已有一个挂在本组件上的 "UiSfx" 对象，用来配置音效片段；
    /// 音量统一在 Assets/Resources/UiSfxSettings 资产里调节。
    /// 真正发声的是一条跨场景常驻的全局 AudioSource，这样点击"开始/关卡/继续"等会立刻切场景的按钮时，
    /// 点击音不会被场景卸载截断。其它 UI 声音统一通过静态方法 PlayHoverSound / PlayClickSound 调用。
    /// </summary>
    public class UiSfxPlayer : MonoBehaviour
    {
        [HideInInspector] public AudioSource source; // 旧字段，保留用于兼容场景数据
        public AudioClip hoverClip;
        public AudioClip clickClip;
        [HideInInspector] public float volume = 0.7f; // 旧字段，音量已迁移到 UiSfxSettings

        private const string SettingsResourcePath = "UiSfxSettings";
        private const float DefaultHoverVolume = 0.35f;
        private const float DefaultClickVolume = 0.6f;

        private static AudioSource _globalSource;
        private static AudioClip _hoverClip;
        private static AudioClip _clickClip;
        private static UiSfxSettings _settings;
        private static bool _settingsLoaded;

        private void Awake()
        {
            // 场景里的 "UiSfx" 负责把片段登记给全局播放器。
            if (hoverClip != null) _hoverClip = hoverClip;
            if (clickClip != null) _clickClip = clickClip;
            EnsureGlobalSource();
        }

        private static void EnsureGlobalSource()
        {
            if (_globalSource != null) return;

            LoadSettings();

            var go = new GameObject("UiSfxPlayer (Global)");
            DontDestroyOnLoad(go);

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            // 胜利结算会用 AudioListener.pause 做全局静音，UI 音效需要忽略暂停才能照常播放。
            src.ignoreListenerPause = true;

            _globalSource = src;
        }

        private static void LoadSettings()
        {
            if (_settingsLoaded) return;
            _settingsLoaded = true;
            _settings = Resources.Load<UiSfxSettings>(SettingsResourcePath);
        }

        private static float HoverVolume
        {
            get
            {
                LoadSettings();
                return _settings != null ? _settings.hoverVolume : DefaultHoverVolume;
            }
        }

        private static float ClickVolume
        {
            get
            {
                LoadSettings();
                return _settings != null ? _settings.clickVolume : DefaultClickVolume;
            }
        }

        public void PlayHover() => Play(_hoverClip, HoverVolume);
        public void PlayClick() => Play(_clickClip, ClickVolume);

        public static void PlayHoverSound() => Play(_hoverClip, HoverVolume);
        public static void PlayClickSound() => Play(_clickClip, ClickVolume);
        public static void PlaySliderTickSound()
        {
            LoadSettings();
            if (_settings == null || _settings.sliderTickClip == null) return;
            Play(_settings.sliderTickClip, _settings.sliderTickVolume);
        }

        private static void Play(AudioClip clip, float volume)
        {
            EnsureGlobalSource();
            if (_globalSource != null && clip != null)
                _globalSource.PlayOneShot(clip, Mathf.Clamp01(volume * GameAudioManager.SfxMultiplier));
        }
    }
}
