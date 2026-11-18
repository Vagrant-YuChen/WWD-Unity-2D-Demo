using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// 按钮 UI 音效的全局音量设置。
    /// 在 Assets/Resources/UiSfxSettings 里选中该资产，即可在 Inspector 中拖动滑杆调节。
    /// </summary>
    [CreateAssetMenu(menuName = "Wacky World Dash/UI SFX Settings", fileName = "UiSfxSettings")]
    public class UiSfxSettings : ScriptableObject
    {
        [Header("按钮音效音量")]
        [Tooltip("鼠标滑过 / 键盘导航进入按钮时的悬停音音量")]
        [Range(0f, 1f)] public float hoverVolume = 0.35f;

        [Tooltip("点击 / 回车 / 空格触发按钮时的点击音音量")]
        [Range(0f, 1f)] public float clickVolume = 0.6f;

        [Header("音量滑条刻度音效")]
        [Tooltip("音量滑条每经过一个刻度时播放的音效")]
        public AudioClip sliderTickClip;
        [Tooltip("刻度音效基础音量（最终还会乘上全局 SFX 倍率）")]
        [Range(0f, 1f)] public float sliderTickVolume = 0.35f;
    }
}
