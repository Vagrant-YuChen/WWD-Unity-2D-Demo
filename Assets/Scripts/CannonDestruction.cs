using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 加农炮被摧毁：播放"整炮炸毁"序列帧，并接管炮身 + 轮子的显示。
///
/// 背景：炮身（CanonLauncher，带逻辑）和轮子（CanonWheel，纯装饰）原本是两个
/// 平级的独立物体；而炸毁序列帧本身已经画好了"炮身 + 轮子"的完整残骸。
/// 所以摧毁时不需要真的拼网格，只需要：
///   1) 隐藏正常视觉（炮身 SpriteRenderer + 轮子）；
///   2) 在一个专用渲染器上逐帧播放炸毁序列；
///   3) 关掉碰撞和发射逻辑，残骸停在原地。
///
/// 建议把轮子设为炮身的子物体（层级里"合成一个整体"），
/// 再把本脚本挂到炮身（CanonLauncher）上，在 Inspector 里拖入引用即可。
/// </summary>
[DisallowMultipleComponent]
public class CannonDestruction : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 引用 ===")]
    [Tooltip("炮身正常状态的渲染器（留空自动取同物体上的 SpriteRenderer）")]
    [SerializeField] private SpriteRenderer bodyRenderer;
    [Tooltip("装饰轮子物体（摧毁时整体隐藏；建议它是炮身的子物体）")]
    [SerializeField] private GameObject wheel;
    [Tooltip("发射逻辑脚本（摧毁时禁用；留空自动取同物体上的 CannonLauncher）")]
    [SerializeField] private CannonLauncher launcher;
    [Tooltip("炮身碰撞体（摧毁时禁用；留空自动取同物体上的 Collider2D）")]
    [SerializeField] private Collider2D bodyCollider;

    [Header("=== 炸毁序列帧 ===")]
    [Tooltip("专门用来播放炸毁序列的渲染器。留空时自动在本物体上新建一个子物体承载。")]
    [SerializeField] private SpriteRenderer destroyRenderer;
    [Tooltip("炸毁序列帧（按 000→005 顺序拖入，会按文件名自动排序）")]
    [SerializeField] private Sprite[] destroyFrames;
    [Tooltip("序列帧播放帧率")]
    [SerializeField] private float frameRate = 12f;
    [Tooltip("炸毁渲染器的 Sorting Order（要盖过地面和正常炮身，一般 >= 2）")]
    [SerializeField] private int destroySortingOrder = 3;

    [Header("=== 收尾 ===")]
    [Tooltip("播完后是否让残骸停留在最后一帧（保持显示）。取消勾选则碎屑逗留后彻底清除，画面变空白。")]
    [SerializeField] private bool holdLastFrame = false;
    [Tooltip("最后一帧（残骸/碎屑）停留多久后再清空或销毁（秒）")]
    [SerializeField] private float lingerTime = 0f;
    [Tooltip("清空时是否连整个物体一起销毁（否则只把残骸清成透明，物体保留）")]
    [SerializeField] private bool destroyObjectAfter = false;
    #endregion

    #region 运行时状态
    private bool destroyed;
    public bool IsDestroyed => destroyed;
    #endregion

    private void Awake()
    {
        if (bodyRenderer == null) bodyRenderer = GetComponent<SpriteRenderer>();
        if (launcher == null) launcher = GetComponent<CannonLauncher>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
    }

    private void Reset()
    {
        bodyRenderer = GetComponent<SpriteRenderer>();
        launcher = GetComponent<CannonLauncher>();
        bodyCollider = GetComponent<Collider2D>();
#if UNITY_EDITOR
        AutoFillFrames();
#endif
    }

#if UNITY_EDITOR
    /// <summary>在 Inspector 里右键组件 → 自动从 Assets/Sprite 抓取 Canon_Exploded 序列帧。</summary>
    [ContextMenu("自动填充炸毁序列帧")]
    private void AutoFillFrames()
    {
        string[] guids = AssetDatabase.FindAssets("Canon_Exploded t:Sprite", new[] { "Assets/Sprite" });
        if (guids.Length == 0) return;

        var sprites = new List<Sprite>();
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) sprites.Add(s);
        }
        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        destroyFrames = sprites.ToArray();
        EditorUtility.SetDirty(this);
    }
#endif

    /// <summary>触发摧毁（可被刀片、碰撞、事件等外部调用）。重复调用安全。</summary>
    public void DestroyCannon()
    {
        if (destroyed) return;
        destroyed = true;
        StartCoroutine(DestroyRoutine());
    }

    /// <summary>
    /// 复活/关卡重置时把加农炮恢复成完好状态（与滚石复位同步调用）。
    /// 会中止正在播放的炸毁序列，恢复炮身、轮子、碰撞与发射逻辑。
    /// </summary>
    public void ResetCannon()
    {
        StopAllCoroutines();
        destroyed = false;

        if (bodyRenderer != null) bodyRenderer.enabled = true;
        if (wheel != null) wheel.SetActive(true);
        if (launcher != null) launcher.enabled = true;
        if (bodyCollider != null) bodyCollider.enabled = true;

        // 清掉炸毁残骸视觉
        if (destroyRenderer != null)
        {
            destroyRenderer.sprite = null;
            destroyRenderer.enabled = false;
        }
    }


    private IEnumerator DestroyRoutine()
    {
        // 1) 关掉功能与碰撞
        if (launcher != null) launcher.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;

        // 2) 隐藏正常视觉（炮身 + 装饰轮子），因为炸毁帧已包含它们
        if (bodyRenderer != null) bodyRenderer.enabled = false;
        if (wheel != null) wheel.SetActive(false);

        // 3) 准备炸毁渲染器
        SpriteRenderer fr = EnsureDestroyRenderer();

        // 4) 逐帧播放炸毁序列，停在最后一帧
        if (fr != null && destroyFrames != null && destroyFrames.Length > 0)
        {
            float frameTime = frameRate > 0f ? 1f / frameRate : 0f;

            var frames = SortFrames(destroyFrames);
            foreach (var frame in frames)
            {
                fr.sprite = frame;
                if (frameTime > 0f) yield return new WaitForSeconds(frameTime);
            }

            // 停在最后一帧（残骸留在原地）
            fr.sprite = frames[frames.Count - 1];
        }

        // 5) 收尾：残骸逗留后清空画面 / 销毁物体
        if (!holdLastFrame)
        {
            // 让最后一帧的碎屑先停留 lingerTime 秒
            if (lingerTime > 0f) yield return new WaitForSeconds(lingerTime);

            if (destroyObjectAfter)
            {
                Destroy(gameObject);
                yield break;
            }

            // 只清掉残骸视觉：把炸毁渲染器清空并关掉，画面彻底变空白，
            // 但物体本身保留（碰撞、脚本早在前面的步骤里已经禁用）。
            if (fr != null)
            {
                fr.sprite = null;
                fr.enabled = false;
            }
        }
    }

    /// <summary>获取（必要时创建）用于播放炸毁序列的渲染器。</summary>
    private SpriteRenderer EnsureDestroyRenderer()
    {
        if (destroyRenderer != null)
        {
            destroyRenderer.enabled = true;
            destroyRenderer.sortingOrder = destroySortingOrder;
            return destroyRenderer;
        }

        // 在本物体下新建一个子物体承载炸毁视觉，与炮身对齐
        var go = new GameObject("DestroyedVisual");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        destroyRenderer = go.AddComponent<SpriteRenderer>();
        destroyRenderer.sortingOrder = destroySortingOrder;
        if (bodyRenderer != null)
        {
            destroyRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
        }
        return destroyRenderer;
    }

    /// <summary>按名称排序，保证 000→005 的播放顺序正确。</summary>
    private static List<Sprite> SortFrames(Sprite[] frames)
    {
        var list = new List<Sprite>(frames);
        list.Sort((a, b) =>
        {
            if (a == null || b == null) return 0;
            return string.CompareOrdinal(a.name, b.name);
        });
        return list;
    }
}
