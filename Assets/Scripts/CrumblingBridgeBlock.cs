using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 会随踩踏逐级开裂的桥面石块（模仿《超级鸡马 / Ultimate Chicken Horse》的机制）。
///
/// 机制：
///   - 初始完好（BlockA_000）；小羊的脚每"踩上去一次"就开裂一级；
///     所有开裂外观都出现过之后再踩一次 → 石块碎裂消失，小羊掉进流沙。
///   - 停留在上面不会继续裂，走开再踩才算新的一次。
///
/// 物理表面：
///   12 块碰撞箱合并进一个 CompositeCollider2D（无缝连续表面），消除行走接缝卡顿；
///   某块碎裂时禁用其碰撞箱，Composite 重新生成几何形成洞。
///
/// 踩踏检测：
///   合并后每块自己的 OnCollision 不再触发，而 Composite 子碰撞体的 bounds 也不可靠，
///   所以这里完全用"变换+配置尺寸"本地推算：把本块顶面高度、左右列边界算出来，
///   再看小羊脚底中心是否落在本块列内、脚底是否贴着顶面、且不是跳起中。
///   不依赖任何 Collider2D.bounds / 触发器，避免被 Composite 吞掉。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class CrumblingBridgeBlock : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 外观 ===")]
    [Tooltip("完好状态（BlockA_000）")]
    [SerializeField] private Sprite intactSprite;
    [Tooltip("每踩一次后依次显示的外观：第1次踩→第0个，第2次踩→第1个，第3次踩→第2个……\n全部显示过之后再踩一次，石块就彻底碎掉消失。")]
    [SerializeField] private Sprite[] damageStages;

    [Header("=== 尺寸统一 ===")]
    [Tooltip("各种开裂外观画布大小不一致，开启后会统一到完好帧的显示大小，避免石头忽大忽小")]
    [SerializeField] private bool normalizeFrameSize = true;
    [Tooltip("true=等比缩放(不变形)；false=拉伸到完全相同的宽高")]
    [SerializeField] private bool preserveAspect = true;

    [Header("=== 碰撞体 ===")]
    [Tooltip("没有碰撞体时，自动生成一个 BoxCollider2D")]
    [SerializeField] private bool ensureCollider = true;
    [Tooltip("所有石块统一使用的碰撞箱尺寸（本地单位）")]
    [SerializeField] private Vector2 colliderSize = new Vector2(0.70f, 0.66f);
    [Tooltip("所有石块统一使用的碰撞箱偏移（本地单位）")]
    [SerializeField] private Vector2 colliderOffset = Vector2.zero;

    [Header("=== 渲染层级 ===")]
    [Tooltip("桥块在同一排序层内的顺序。地面/背景通常为 0~1，务必取比它们大的值，否则桥会被地面挡住看不见。\n小羊一般是 3，想让小羊站在桥前面就保持 < 3。")]
    [SerializeField] private int sortingOrder = 2;
    #endregion

    #region 美术量测表
    private static readonly Dictionary<string, Vector4> kMetrics = new Dictionary<string, Vector4>
    {
        {"BlockA_000",            new Vector4(0.66f, 0.65f, -0.010f,  0.000f)},
        {"BlockA_001",            new Vector4(0.34f, 0.34f, -0.005f,  0.000f)},
        {"BlockA_002",            new Vector4(0.69f, 0.68f, -0.005f,  0.005f)},
        {"BlockA_003",            new Vector4(0.71f, 0.70f,  0.030f, -0.200f)},
        {"BlockA_003_MoreDamage", new Vector4(0.72f, 0.72f,  0.020f, -0.190f)},
        {"BlockA_004",            new Vector4(0.37f, 0.37f,  0.145f, -0.160f)},
        {"BlockA_005",            new Vector4(0.35f, 0.38f,  0.010f, -0.135f)},
        {"BlockA_006",            new Vector4(0.37f, 0.39f,  0.145f, -0.145f)},
        {"BlockA_007",            new Vector4(0.37f, 0.40f,  0.050f, -0.115f)},
        {"BlockA_008",            new Vector4(0.38f, 0.42f,  0.000f, -0.080f)},
        {"BlockA_009",            new Vector4(0.38f, 0.43f,  0.005f, -0.130f)},
        {"BlockA_010",            new Vector4(0.38f, 0.44f, -0.005f, -0.125f)},
        {"BlockA_011",            new Vector4(0.35f, 0.45f,  0.000f, -0.130f)},
        {"BlockA_012",            new Vector4(0.36f, 0.46f,  0.115f, -0.155f)},
        {"BlockA_013",            new Vector4(0.36f, 0.47f,  0.120f, -0.165f)},
        {"BlockA_014",            new Vector4(0.36f, 0.47f,  0.280f, -0.080f)},
        {"BlockA_015",            new Vector4(0.36f, 0.49f,  0.160f, -0.180f)},
    };
    #endregion

    #region 共享的无缝桥面（CompositeCollider2D）
    private static Transform s_bridgeParent;
    private static CompositeCollider2D s_composite;

    private static void EnsureBridgeParent()
    {
        if (s_bridgeParent != null && s_bridgeParent.gameObject != null) return;

        var go = new GameObject("CrumblingBridgeComposite");
        s_bridgeParent = go.transform;
        s_bridgeParent.position = Vector3.zero;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        s_composite = go.AddComponent<CompositeCollider2D>();
        s_composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        s_composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
    }

    private static void RegenerateComposite()
    {
        if (s_composite != null) s_composite.GenerateGeometry();
    }
    #endregion

    #region 运行时状态
    private SpriteRenderer originalSR;
    private SpriteRenderer visualSR;
    private Transform visualTf;
    private Collider2D col;          // 本块的 BoxCollider2D（usedByComposite）

    private Vector2 targetSize;
    private Vector2 targetCenter;
    private bool targetValid;

    [SerializeField] private int damageLevel;
    private bool destroyed;

    // 位置检测状态
    private bool wasOnMe;
    private BunnyController cachedBunny;
    private Collider2D cachedBunnyCol;

    private BunnyController watchedBunny;
    private Coroutine respawnWatch;
    #endregion

    public int DamageLevel => damageLevel;
    public bool IsDestroyed => destroyed;

    private void Awake()
    {
        originalSR = GetComponent<SpriteRenderer>();
        if (intactSprite == null && originalSR != null) intactSprite = originalSR.sprite;

        // 桥必须画在地面/背景之上，否则会被 terrain 挡住看不见
        if (originalSR != null) originalSR.sortingOrder = sortingOrder;

        EnsureBridgeParent();
        EnsureCollider();
        transform.SetParent(s_bridgeParent, true);
        RegenerateComposite();

        BuildVisual();
        targetValid = normalizeFrameSize && TryMetric(intactSprite, out targetSize, out targetCenter);

        ShowSprite(intactSprite);
    }

    private void BuildVisual()
    {
        var go = new GameObject("Visual");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        visualSR = go.AddComponent<SpriteRenderer>();
        visualTf = go.transform;

        if (originalSR != null)
        {
            visualSR.sortingLayerID = originalSR.sortingLayerID;
            visualSR.sortingOrder = sortingOrder;
            visualSR.sharedMaterial = originalSR.sharedMaterial;
            visualSR.color = originalSR.color;
            originalSR.enabled = false;
        }
        else
        {
            visualSR.sortingOrder = sortingOrder;
        }
    }

    private void EnsureCollider()
    {
        col = GetComponent<BoxCollider2D>();
        if (col == null) col = GetComponent<Collider2D>();
        if (col == null && ensureCollider) col = gameObject.AddComponent<BoxCollider2D>();

        if (col is BoxCollider2D box)
        {
            box.size = colliderSize;
            box.offset = colliderOffset;
            box.isTrigger = false;
            box.usedByComposite = true;
        }
    }

    private void ShowSprite(Sprite s)
    {
        if (s == null || visualSR == null) return;

        visualSR.enabled = true;

        Vector2 size, center;
        if (!normalizeFrameSize || !targetValid || !TryMetric(s, out size, out center))
        {
            visualSR.sprite = s;
            visualTf.localScale = Vector3.one;
            visualTf.localPosition = Vector3.zero;
            return;
        }

        float sx = size.x > 1e-4f ? targetSize.x / size.x : 1f;
        float sy = size.y > 1e-4f ? targetSize.y / size.y : 1f;
        if (preserveAspect)
        {
            float u = Mathf.Min(sx, sy);
            sx = u; sy = u;
        }

        visualTf.localScale = new Vector3(sx, sy, 1f);
        visualTf.localPosition = new Vector3(
            targetCenter.x - center.x * sx,
            targetCenter.y - center.y * sy,
            0f);
        visualSR.sprite = s;
    }

    private static bool TryMetric(Sprite s, out Vector2 size, out Vector2 center)
    {
        size = Vector2.zero;
        center = Vector2.zero;
        if (s == null || string.IsNullOrEmpty(s.name)) return false;

        if (kMetrics.TryGetValue(s.name, out Vector4 v))
        {
            size = new Vector2(v.x, v.y);
            center = new Vector2(v.z, v.w);
            return true;
        }

        foreach (var kv in kMetrics)
        {
            if (s.name.Contains(kv.Key))
            {
                size = new Vector2(kv.Value.x, kv.Value.y);
                center = new Vector2(kv.Value.z, kv.Value.w);
                return true;
            }
        }
        return false;
    }

    #region 踩踏检测（纯位置推算，不依赖 Collider2D.bounds / 触发器）
    private void FixedUpdate()
    {
        if (destroyed) return;

        if (!ResolveBunny()) { wasOnMe = false; return; }
        if (cachedBunny.isDead) { wasOnMe = false; return; }

        // 用"变换 + 配置尺寸"本地推算本块顶面/列边界（Composite 子碰撞体 bounds 不可靠）
        Vector3 sc = transform.lossyScale;
        float left  = transform.position.x - Mathf.Abs(colliderSize.x) * 0.5f * Mathf.Abs(sc.x);
        float right = transform.position.x + Mathf.Abs(colliderSize.x) * 0.5f * Mathf.Abs(sc.x);
        float top   = transform.position.y + (colliderOffset.y + colliderSize.y * 0.5f) * sc.y;

        Bounds sb = cachedBunnyCol.bounds;
        float cx    = sb.center.x;
        float feetY = sb.min.y;

        bool inColumn = cx > left && cx <= right;
        bool nearTop  = Mathf.Abs(feetY - top) <= 0.15f;

        bool onNow = inColumn && nearTop;

        if (onNow && !wasOnMe)
        {
            wasOnMe = true;
            RegisterStep(cachedBunny);
        }
        else if (wasOnMe && !onNow)
        {
            // 只有在"确实离开"时才复位，避免边界抖动导致重复计数
            bool clearlyOff = cx < left - 0.05f || cx > right + 0.05f || Mathf.Abs(feetY - top) > 0.25f;
            if (clearlyOff) wasOnMe = false;
        }
    }

    private bool ResolveBunny()
    {
        if (cachedBunny != null && cachedBunnyCol != null) return true;

        cachedBunny = null;
        cachedBunnyCol = null;

        var go = GameObject.FindWithTag("Player");
        if (go == null) return false;

        cachedBunny = go.GetComponent<BunnyController>();
        if (cachedBunny == null) cachedBunny = go.GetComponentInParent<BunnyController>();

        Collider2D[] cs = go.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < cs.Length; i++) if (cs[i].enabled) { cachedBunnyCol = cs[i]; break; }
        if (cachedBunnyCol == null && cs.Length > 0) cachedBunnyCol = cs[0];

        return cachedBunny != null && cachedBunnyCol != null;
    }

    private void RegisterStep(BunnyController bunny)
    {
        WatchBunnyRespawn(bunny);

        damageLevel++;

        if (damageStages != null && damageLevel <= damageStages.Length &&
            damageStages[damageLevel - 1] != null)
        {
            ShowSprite(damageStages[damageLevel - 1]);
        }
        else
        {
            DestroyBlock();
        }
    }
    #endregion

    #region 碎裂与复位
    private void DestroyBlock()
    {
        destroyed = true;
        damageLevel = damageStages != null ? damageStages.Length + 1 : 1;
        wasOnMe = false;

        if (col != null) col.enabled = false;
        if (visualSR != null) visualSR.enabled = false;

        RegenerateComposite();
    }

    private void WatchBunnyRespawn(BunnyController bunny)
    {
        if (bunny == null) return;
        watchedBunny = bunny;
        if (respawnWatch == null) respawnWatch = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        while (watchedBunny != null && !watchedBunny.isDead) yield return null;
        while (watchedBunny != null && watchedBunny.isDead) yield return null;

        respawnWatch = null;
        if (watchedBunny != null) ResetBlock();
    }

    /// <summary>复位成完好状态（复活 / 关卡重置时使用）。</summary>
    public void ResetBlock()
    {
        damageLevel = 0;
        destroyed = false;
        wasOnMe = false;

        if (visualSR != null) visualSR.enabled = true;
        ShowSprite(intactSprite);
        if (col != null) col.enabled = true;

        RegenerateComposite();

        watchedBunny = null;
    }
    #endregion
}
