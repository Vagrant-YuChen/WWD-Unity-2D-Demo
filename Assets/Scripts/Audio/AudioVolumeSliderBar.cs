using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 音量滑条轨道（条本身）的鼠标交互入口。
/// 鼠标点击轨道空白处，会把 tickmark 直接跳到最近的刻度；
/// 按住拖动则继续自由拖动。两条轨道共用 <see cref="AudioOptionsPanel"/> 的统一逻辑。
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioVolumeSliderBar : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private AudioOptionsPanel panel;
    private AudioVolumeTickmark.SliderKind kind;

    public void Bind(AudioOptionsPanel owner, AudioVolumeTickmark.SliderKind sliderKind)
    {
        panel = owner;
        kind = sliderKind;
    }

    public void OnPointerDown(PointerEventData eventData) => panel?.NotifyBarPointerDown(kind, eventData);
    public void OnDrag(PointerEventData eventData) => panel?.NotifyDrag(kind, eventData);
    public void OnPointerUp(PointerEventData eventData) => panel?.NotifyPointerUp(kind, eventData);
}
