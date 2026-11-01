using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 取景目标标记：挂上即参与 ZoomCameraRig 的取景框计算。
///
/// 与《超级鸡马》的 AddTarget(Collider2D) / AddTarget(Transform) 同一思路——
/// 镜头不关心目标是什么，只关心它的位置或包围盒。
/// 玩家不需要挂（rig 用 Tag=Player 自动发现）；
/// 大炮、金币、Boss、需要"顺带框进画面"的道具挂上即可，
/// 目标之间拉得越开，取景框自动越大（= 镜头拉远）。
/// </summary>
[DisallowMultipleComponent]
public class CameraTarget : MonoBehaviour
{
    #region 可配置参数
    [Tooltip("用碰撞体包围盒参与取景（关闭则只取自身位置点）")]
    [SerializeField] private bool includeColliderBounds = true;

    [Tooltip("关闭后在 Scene 视图隐藏取景标记")]
    [SerializeField] private bool drawGizmos = true;
    #endregion

    #region 静态注册表
    private static readonly List<CameraTarget> all = new List<CameraTarget>();

    /// <summary>场景内所有已启用的取景目标。</summary>
    public static IReadOnlyList<CameraTarget> All => all;

    // 关闭 Domain Reload 时静态列表会残留上一局的引用，这里主动清一次
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();
    #endregion

    private Collider2D cachedCollider;

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        cachedCollider = includeColliderBounds ? GetComponentInChildren<Collider2D>() : null;
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    /// <summary>
    /// 输出参与取景的引用：优先碰撞体包围盒，否则用自身 Transform。
    /// </summary>
    public bool TryGetFrameBox(out Transform frameTransform, out Collider2D frameBox)
    {
        frameTransform = transform;
        frameBox = includeColliderBounds ? cachedCollider : null;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.9f);

        Collider2D col = includeColliderBounds ? GetComponentInChildren<Collider2D>() : null;
        if (col != null)
        {
            Bounds b = col.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 0.15f);
        }
    }
}
