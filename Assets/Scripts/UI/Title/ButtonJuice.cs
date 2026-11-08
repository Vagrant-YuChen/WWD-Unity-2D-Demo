using UnityEngine;
using UnityEngine.EventSystems;

namespace RunningGame.UI
{
    /// <summary>
    /// 按钮手感：悬停放大并播放 tick，按下时缩小并播放 click。
    /// 指针聚焦（选中）的按钮优先保持放大，光标离开不会缩小；指针移走后才恢复为场景里手工配置的原始缩放。
    /// 使用 unscaled 时间平滑缩放。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ButtonJuice : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public UiSfxPlayer sfx;
        public float hoverScale = 1.06f;
        public float pressScale = 0.95f;
        public float lerpSpeed = 14f;

        private RectTransform rt;
        // 以场景里手工摆好的缩放为基准，运行时只在它之上做悬停/按下缩放，
        // 避免把美术配置的非等比缩放（例如 0.7 / 0.6x0.9）强行改回 1。
        private Vector3 baseScale = Vector3.one;
        private Vector3 target = Vector3.one;

        // 记录光标是否停在按钮上、是否正被按住、是否是当前焦点（指针所指），
        // 用来决定离开/松开后该回到哪个状态。焦点优先级最高：只要指针还指着它，就保持放大。
        private bool hovering;
        private bool pressing;
        private bool focused;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            baseScale = rt.localScale == Vector3.zero ? Vector3.one : rt.localScale;
            target = baseScale;
        }

        private void OnDisable()
        {
            hovering = false;
            pressing = false;
            focused = false;
            if (rt != null) rt.localScale = baseScale;
            target = baseScale;
        }

        private void Update()
        {
            if (rt == null) return;
            rt.localScale = Vector3.Lerp(rt.localScale, target, Time.unscaledDeltaTime * lerpSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovering = true;
            RefreshTarget();
            if (sfx != null) sfx.PlayHover();
        }

        // 光标离开：若按钮仍是焦点（指针所指）则继续放大，否则回到原始大小；
        // 按住时保持按下状态，松开后再恢复。
        public void OnPointerExit(PointerEventData eventData)
        {
            hovering = false;
            RefreshTarget();
        }

        public void OnSelect(BaseEventData eventData)
        {
            // 成为焦点（指针指向）时始终放大，并且优先级高于光标离开。
            focused = true;
            if (pressing) return; // 按住时保持按下视觉，松开后由 RefreshTarget 落到焦点放大
            RefreshTarget();
            if (sfx != null) sfx.PlayHover();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            // 指针换到别的按钮后，若光标也不再停留则恢复原始大小。
            focused = false;
            RefreshTarget();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressing = true;
            RefreshTarget();
            if (sfx != null) sfx.PlayClick();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressing = false;
            RefreshTarget();
        }

        private void RefreshTarget()
        {
            if (pressing) target = baseScale * pressScale;
            else if (focused || hovering) target = baseScale * hoverScale;
            else target = baseScale;
        }
    }
}
