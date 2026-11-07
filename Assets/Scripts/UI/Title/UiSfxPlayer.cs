using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// 简单的 UI 音效播放器（悬停 / 点击）。音效片段可留空，留空时不发声。
    /// </summary>
    public class UiSfxPlayer : MonoBehaviour
    {
        public AudioSource source;
        public AudioClip hoverClip;
        public AudioClip clickClip;
        [Range(0f, 1f)] public float volume = 0.7f;

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        public void PlayHover()
        {
            if (source != null && hoverClip != null) source.PlayOneShot(hoverClip, volume);
        }

        public void PlayClick()
        {
            if (source != null && clickClip != null) source.PlayOneShot(clickClip, volume);
        }
    }
}
