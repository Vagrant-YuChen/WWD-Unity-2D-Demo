using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// 轻微的视差漂浮：用正弦让 UI 元素缓慢来回移动，不会产生接缝，也不需要双图循环。
    /// 使用 unscaled 时间，暂停时也能保持呼吸感。
    /// </summary>
    public class ParallaxDrift : MonoBehaviour
    {
        public float amplitudeX = 12f;
        public float amplitudeY = 0f;
        public float speed = 0.6f;
        public float phase = 0f;

        private RectTransform rt;
        private Vector2 basePos;
        private bool ready;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            basePos = rt.anchoredPosition;
            ready = true;
        }

        private void Update()
        {
            if (!ready || rt == null) return;
            float t = Time.unscaledTime * speed + phase;
            rt.anchoredPosition = basePos + new Vector2(
                Mathf.Sin(t) * amplitudeX,
                Mathf.Sin(t * 1.7f) * amplitudeY);
        }
    }
}
