using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// 角色 idle：上下轻微浮动 + 呼吸式挤压拉伸，让标题页的角色"活"起来。
    /// 使用 unscaled 时间。
    /// </summary>
    public class IdleBreathe : MonoBehaviour
    {
        public float bobAmplitude = 6f;
        public float bobSpeed = 1.6f;
        public float squashAmount = 0.03f;
        public float squashSpeed = 1.6f;

        private RectTransform rt;
        private Vector2 basePos;
        private Vector3 baseScale;
        private bool ready;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            basePos = rt.anchoredPosition;
            baseScale = rt.localScale == Vector3.zero ? Vector3.one : rt.localScale;
            ready = true;
        }

        private void Update()
        {
            if (!ready || rt == null) return;

            float t = Time.unscaledTime;
            rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Sin(t * bobSpeed) * bobAmplitude);

            float s = 1f + Mathf.Sin(t * squashSpeed) * squashAmount;
            // 竖向拉伸时横向略微收窄，保持体积感
            rt.localScale = new Vector3(baseScale.x * (2f - s), baseScale.y * s, baseScale.z);
        }
    }
}
