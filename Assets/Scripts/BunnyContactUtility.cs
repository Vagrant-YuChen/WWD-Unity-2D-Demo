using UnityEngine;

/// <summary>
/// 兔子接触类脚本（BladeKill / SpringBoard 等）的公共查找与过滤逻辑。
/// </summary>
public static class BunnyContactUtility
{
    /// <summary>检测某个 Layer 是否在 LayerMask 中。</summary>
    public static bool IsInLayerMask(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }

    /// <summary>
    /// 从碰撞体上查找 BunnyController。
    /// 兼容碰撞体在兔子子物体上的情况。
    /// </summary>
    public static BunnyController FindBunny(Collider2D other)
    {
        if (other == null) return null;

        if (other.TryGetComponent(out BunnyController directBunny))
        {
            return directBunny;
        }

        return other.GetComponentInParent<BunnyController>();
    }

    /// <summary>
    /// 获取兔子真正受力的 Rigidbody2D。
    /// 优先使用碰撞体挂载的刚体，否则回退到 BunnyController 所在物体。
    /// </summary>
    public static Rigidbody2D FindBunnyRigidbody(Collider2D other, BunnyController bunny)
    {
        if (other != null && other.attachedRigidbody != null)
        {
            return other.attachedRigidbody;
        }

        return bunny != null ? bunny.GetComponent<Rigidbody2D>() : null;
    }
}