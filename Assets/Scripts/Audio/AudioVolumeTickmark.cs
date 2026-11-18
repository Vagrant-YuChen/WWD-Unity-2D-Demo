using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 音量滑条 tickmark 的鼠标拖动入口。
/// 真正的位置 / 吸附 / 音量变化都交给 <see cref="AudioOptionsPanel"/> 统一处理，
/// 这样音乐和音效两条滑条共用同一套逻辑，也方便键盘操作复用。
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioVolumeTickmark : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public enum SliderKind { Music, Sfx }

    private AudioOptionsPanel panel;
    private SliderKind kind;

    public void Bind(AudioOptionsPanel owner, SliderKind sliderKind)
    {
        panel = owner;
        kind = sliderKind;
    }

    public void OnPointerDown(PointerEventData eventData) => panel?.NotifyPointerDown(kind, eventData);
    public void OnDrag(PointerEventData eventData) => panel?.NotifyDrag(kind, eventData);
    public void OnPointerUp(PointerEventData eventData) => panel?.NotifyPointerUp(kind, eventData);
}
