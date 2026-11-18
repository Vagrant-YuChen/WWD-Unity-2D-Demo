using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RunningGame.UI
{
    /// <summary>
    /// 挂在按钮上的纯音效组件：
    /// - 鼠标滑入，或键盘/手柄导航把焦点移到本按钮时，播放一次悬停音效；
    /// - 按钮被真正触发（鼠标点击、回车、空格最终都会走 Button.onClick）时，播放一次点击音效。
    /// 只负责声音，不改变按钮的任何视觉表现。
    /// 由 UiSfxAutoBinder 在运行时自动添加到场景中的所有按钮上，无需手动挂载。
    /// </summary>
    public class UiButtonSfx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler
    {
        private Button _button;
        private bool _pointerHovering;

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (_button != null) _button.onClick.AddListener(PlayClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(PlayClick);
        }

        private void OnDisable()
        {
            // 面板关闭时不会收到 PointerExit，重置状态避免下次误判。
            _pointerHovering = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerHovering = true;
            UiSfxPlayer.PlayHoverSound();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerHovering = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            // 键盘 / 手柄导航把焦点移到本按钮时也要响一次。
            // 若鼠标本来就停在按钮上（鼠标点击会先触发 Select），进入时已经响过，这里不重复。
            if (!_pointerHovering) UiSfxPlayer.PlayHoverSound();
        }

        private void PlayClick()
        {
            UiSfxPlayer.PlayClickSound();
        }
    }
}
