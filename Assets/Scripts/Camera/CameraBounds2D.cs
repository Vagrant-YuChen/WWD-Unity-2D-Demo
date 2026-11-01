using UnityEngine;

/// <summary>
/// 关卡取景边界：拉远时把画面钳在关卡内，避免露出关卡外的空白。
///
/// 用法（三种，任选其一）：
/// 1. 什么都不做 —— ZoomCameraRig 会自动用 Ground 层碰撞体的联合包围盒生成边界；
/// 2. 手动放一个空物体挂本脚本，用同物体上的 BoxCollider2D 框出边界；
/// 3. 关掉 useCollider，直接手填 manualRect。
/// </summary>
[DisallowMultipleComponent]
public class CameraBounds2D : MonoBehaviour
{
    #region 可配置参数
    [Tooltip("用同物体上的 Collider2D 包围盒作为边界（推荐，Scene 视图里可以直接拖）")]
    [SerializeField] private bool useCollider = true;

    [Tooltip("useCollider 关闭时使用的手填边界（世界坐标）")]
    [SerializeField] private Rect manualRect = new Rect(-12f, -8f, 24f, 16f);

    [Tooltip("边界内缩距离：镜头不贴到关卡边缘")]
    [SerializeField] private float inset = 0.2f;

    [Tooltip("关闭后在 Scene 视图隐藏边界框")]
    [SerializeField] private bool drawGizmos = true;
    #endregion

    /// <summary>最终可用的世界边界（已内缩）。</summary>
    public Rect WorldRect { get; private set; }

    private void Awake()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        if (Application.isPlaying) Rebuild();
    }

    public void Rebuild()
    {
        Rect rect;

        Collider2D col = useCollider ? GetComponent<Collider2D>() : null;
        if (col != null)
        {
            Bounds b = col.bounds;
            rect = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
        }
        else
        {
            rect = manualRect;
        }

        // 内缩后如果尺寸变成负数就退回不内缩
        float w = rect.width - inset * 2f;
        float h = rect.height - inset * 2f;
        WorldRect = (w > 0f && h > 0f)
            ? new Rect(rect.x + inset, rect.y + inset, w, h)
            : rect;
    }

    /// <summary>
    /// 用指定层的所有碰撞体联合包围盒生成一个临时边界。
    /// 场景里没手放边界时由 ZoomCameraRig 调用，做到"零配置"。
    /// </summary>
    public static bool TryBuildFromLayer(int layer, float inset, out Rect rect)
    {
        rect = default;
        if (layer < 0) return false;

        Collider2D[] colliders = Object.FindObjectsOfType<Collider2D>();
        bool any = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
            if (c == null || c.isTrigger) continue;
            if (c.gameObject.layer != layer) continue;
            if (c.GetComponentInParent<BunnyController>() != null) continue;

            Bounds b = c.bounds;
            if (!any)
            {
                min = b.min;
                max = b.max;
                any = true;
            }
            else
            {
                min = Vector2.Min(min, b.min);
                max = Vector2.Max(max, b.max);
            }
        }

        if (!any) return false;

        float w = max.x - min.x - inset * 2f;
        float h = max.y - min.y - inset * 2f;
        if (w <= 0f || h <= 0f) return false;

        rect = new Rect(min.x + inset, min.y + inset, w, h);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Rect r;
        if (Application.isPlaying)
        {
            r = WorldRect;
        }
        else
        {
            Collider2D col = useCollider ? GetComponent<Collider2D>() : null;
            r = col != null
                ? Rect.MinMaxRect(col.bounds.min.x, col.bounds.min.y, col.bounds.max.x, col.bounds.max.y)
                : manualRect;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, 0f), new Vector3(r.width, r.height, 0f));
    }
}
