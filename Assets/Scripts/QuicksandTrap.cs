using System.Collections;
using UnityEngine;

/// <summary>
/// 流沙陷阱：玩家（小羊/兔子）接触到流沙时瞬间死亡并播放死亡动画，
/// 尸体在 1 秒内从流沙表面下沉到看不见的位置，随后由角色控制器自行复活回原点。
/// </summary>
public class QuicksandTrap : MonoBehaviour
{
    #region 可配置参数
    [Tooltip("尸体下沉深度（世界单位）")]
    [SerializeField] private float sinkDepth = 1.5f;

    [Tooltip("尸体从流沙表面完全下沉所需时间（秒）")]
    [SerializeField] private float sinkDuration = 1f;

    [Tooltip("尸体下沉过程中最终降到的 Order in Layer（0 表示被场景中所有对象遮挡）")]
    [SerializeField] private int targetSortingOrder = 0;

    [Tooltip("死亡触发音效（可选）")]
    [SerializeField] private AudioClip deathSound;

    [Header("=== 滚石专用下沉（只影响滚石，不影响小羊） ===")]
    [Tooltip("滚石下沉深度（世界单位）。滚石比小羊大，通常要填得比上面的大")]
    [SerializeField] private float boulderSinkDepth = 2f;

    [Tooltip("滚石完全沉入流沙所需时间（秒）")]
    [SerializeField] private float boulderSinkDuration = 1.2f;
    #endregion

    #region 对外只读参数（滚石等非玩家物体复用下沉表现）
    /// <summary>尸体下沉深度（世界单位）。</summary>
    public float SinkDepth => sinkDepth;

    /// <summary>尸体完全下沉所需时间（秒）。</summary>
    public float SinkDuration => sinkDuration;

    /// <summary>滚石专用下沉深度（世界单位）。</summary>
    public float BoulderSinkDepth => boulderSinkDepth;

    /// <summary>滚石专用下沉时长（秒）。</summary>
    public float BoulderSinkDuration => boulderSinkDuration;
    #endregion

    #region 私有状态
    private Collider2D trapCollider;
    private AudioSource audioSource;
    #endregion

    private void Awake()
    {
        trapCollider = GetComponent<Collider2D>();

        if (deathSound != null && !TryGetComponent(out audioSource))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = deathSound;
            audioSource.volume = 0.8f;
            audioSource.playOnAwake = false;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryKill(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryKill(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKill(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryKill(other);
    }

    #region 核心逻辑
    private void TryKill(Collider2D other)
    {
        if (other == null) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        // 触发死亡（播放死亡动画、进入复活倒计时）
        bunny.TriggerDeath();

        // 播放音效
        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        // 尸体下沉
        StartCoroutine(SinkCorpse(bunny));
    }

    private IEnumerator SinkCorpse(BunnyController bunny)
    {
        if (bunny == null) yield break;

        Transform corpse = bunny.transform;
        Rigidbody2D rb = bunny.GetComponent<Rigidbody2D>();
        SpriteRenderer corpseRenderer = bunny.GetComponent<SpriteRenderer>();
        SpriteRenderer quicksandRenderer = GetComponent<SpriteRenderer>();

        Vector3 start = corpse.position;
        Vector3 target = start + Vector3.down * sinkDepth;

        // 从当前渲染层级逐渐降到 targetSortingOrder，让尸体被流沙/地面/背景逐层遮挡
        int startSortingOrder = corpseRenderer != null ? corpseRenderer.sortingOrder : 0;
        int startSortingLayerID = corpseRenderer != null
            ? corpseRenderer.sortingLayerID
            : (quicksandRenderer != null ? quicksandRenderer.sortingLayerID : 0);

        if (corpseRenderer != null)
        {
            corpseRenderer.enabled = true;

            // 关键：小羊原本在 Player 排序层，流沙/地面在 Default 排序层。
            // 必须切到流沙所在排序层，降低 Order in Layer 才会真正被场景遮挡。
            if (quicksandRenderer != null)
            {
                corpseRenderer.sortingLayerID = quicksandRenderer.sortingLayerID;
            }
        }

        float elapsed = 0f;
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sinkDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            Vector3 pos = Vector3.Lerp(start, target, eased);
            corpse.position = pos;
            if (rb != null)
            {
                rb.position = pos;
            }

            if (corpseRenderer != null)
            {
                corpseRenderer.sortingOrder = Mathf.RoundToInt(
                    Mathf.Lerp(startSortingOrder, targetSortingOrder, eased));
            }

            yield return null;
        }

        corpse.position = target;
        if (rb != null)
        {
            rb.position = target;
        }

        // 下沉结束：确保渲染层级最低并直接隐藏，避免仍然露在场景前面
        if (corpseRenderer != null)
        {
            corpseRenderer.sortingOrder = targetSortingOrder;
            corpseRenderer.enabled = false;
        }

        // 等待角色复活后再恢复渲染层级与显示
        while (bunny != null && bunny.isDead)
        {
            yield return null;
        }

        if (corpseRenderer != null)
        {
            corpseRenderer.sortingLayerID = startSortingLayerID;
            corpseRenderer.sortingOrder = startSortingOrder;
            corpseRenderer.enabled = true;
        }
    }
    #endregion
}
