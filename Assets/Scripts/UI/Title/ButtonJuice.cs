using UnityEngine;
using UnityEngine.EventSystems;

namespace RunningGame.UI
{
    /// <summary>
    /// 按钮手感：悬停/键盘选中时放大并播放 tick，按下时缩小并播放 click。
    /// 使用 unscaled 时间平滑缩放。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ButtonJuice : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IPointerDownHandler, IPointerUpHandler
    {
        public UiSfxPlayer sfx;
        public float hoverScale = 1.06f;
        public float pressScale = 0.95f;
        public float lerpSpeed = 14f;

        private RectTransform rt;
        private Vector3 target = Vector3.one;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            target = Vector3.one;
        }

        private void OnDisable()
        {
            if (rt != null) rt.localScale = Vector3.one;
            target = Vector3.one;
        }

        private void Update()
        {
            if (rt == null) return;
            rt.localScale = Vector3.Lerp(rt.localScale, target, Time.unscaledDeltaTime * lerpSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData) => Hover();
        public void OnSelect(BaseEventData eventData) => Hover();
        public void OnPointerDown(PointerEventData eventData)
        {
            target = Vector3.one * pressScale;
            if (sfx != null) sfx.PlayClick();
        }
        public void OnPointerUp(PointerEventData eventData) => Hover();

        private void Hover()
        {
            target = Vector3.one * hoverScale;
            if (sfx != null) sfx.PlayHover();
        }
    }
}
