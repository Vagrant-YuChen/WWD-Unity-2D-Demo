using UnityEngine;

/// <summary>
/// 一次"临时取景请求"。
///
/// 事件性镜头（蓄力拉远、发射贴身跟拍、死亡特写……）不直接改摄像机，
/// 而是推一个 cue 给 <see cref="ZoomCameraRig"/>，由 rig 做权重混合。
/// 这样多个事件叠在一起也不会打架：拉远永远取较大值，拉近按权重压。
///
/// 生命周期：目标权重由驱动方每帧刷新（targetWeight），
/// 调用 <see cref="Release"/> 后按 blendOut 淡出，权重归零即自动回收。
/// </summary>
public class CameraCue
{
    #region 可配置
    /// <summary>cue 自己的取景点（如炮口）。为空时用玩家位置。</summary>
    public Transform target;

    /// <summary>额外的世界取景点（如预测落点）。hasWorldPoint 为 true 时参与取景。</summary>
    public Vector2 worldPoint;
    public bool hasWorldPoint;

    /// <summary>取景点半径（世界单位），让点状目标也有一点留白。</summary>
    public Vector2 frameRadius = Vector2.zero;

    /// <summary>
    /// 相对基础尺寸的倍率：&gt;1 拉远、&lt;1 拉近、1 = 不改变尺寸。
    /// 拉远时基准是"舒适近景尺寸"，拉近时基准是当前取景尺寸。
    /// </summary>
    public float sizeMultiplier = 1f;

    /// <summary>画面中心相对基础中心的额外偏移（世界单位）。</summary>
    public Vector2 worldOffset = Vector2.zero;

    /// <summary>淡入 / 淡出时长（秒，非缩放时间）。</summary>
    public float blendIn = 0.2f;
    public float blendOut = 0.3f;
    #endregion

    #region 运行时
    /// <summary>期望权重（0..1），由驱动方每帧刷新。</summary>
    public float targetWeight = 1f;

    private float weight;

    /// <summary>当前实际权重（已含淡入淡出）。</summary>
    public float CurrentWeight => weight;

    /// <summary>是否已经淡出完毕，可以被 rig 回收。</summary>
    public bool IsFinished => released && weight <= 0.0001f;

    private bool released;
    #endregion

    public CameraCue(float sizeMultiplier = 1f, float blendIn = 0.2f, float blendOut = 0.3f)
    {
        this.sizeMultiplier = sizeMultiplier;
        this.blendIn = blendIn;
        this.blendOut = blendOut;
    }

    /// <summary>开始淡出。</summary>
    public void Release()
    {
        released = true;
    }

    /// <summary>立即结束（切场景 / 复位时用）。</summary>
    public void Kill()
    {
        released = true;
        weight = 0f;
    }

    internal void Tick(float dt)
    {
        float goal = released ? 0f : Mathf.Clamp01(targetWeight);
        float duration = goal > weight ? blendIn : blendOut;
        float step = duration > 0.0001f ? dt / duration : 1f;
        weight = Mathf.MoveTowards(weight, goal, step);
    }

    /// <summary>解析当前取景中心点。</summary>
    public Vector2 ResolvePoint(Vector2 fallback)
    {
        if (target != null) return target.position;
        if (hasWorldPoint) return worldPoint;
        return fallback;
    }
}
