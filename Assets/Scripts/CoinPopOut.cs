using UnityEngine;

/// <summary>
/// 藏在蜂巢里的金币的“弹出来”表现：
/// 被 <see cref="BeeHiveExplosion"/> 在 explode.005 帧调用 PopOut 后：
///   1) 激活金币并进入动态物理；
///   2) 给一个向上的初速度（力度由蜂巢脚本传入，可在 Inspector 调试）；
///   3) 受重力下落，落到地面后冻结在原地；
///   4) 落地前禁用拾取，落地后恢复 CoinPickup，玩家靠近即可正常交互拾取。
///
/// 需要金币上有一个 Rigidbody2D（BeeHiveSetup 会自动补上）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CoinPopOut : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 表现 ===")]
    [Tooltip("弹出时金币的 Sorting Order（越大越靠前，保证盖在蜂巢爆炸帧上方）")]
    [SerializeField] private int popSortingOrder = 6;
    [Tooltip("勾选后，金币在地上落稳之前不能被拾取；落地后自动恢复 CoinPickup")]
    [SerializeField] private bool allowPickupOnlyAfterLanding = true;

    [Header("=== 落地 ===")]
    [Tooltip("落地后冻结金币，避免被兔子推动或滚走；拾取仍按 CoinPickup 的距离判定")]
    [SerializeField] private bool freezeOnLand = true;
    [Tooltip("判定为“落到一个朝上的面”的接触法线阈值（0~1）。用于避免撞到天花板/侧墙就冻结")]
    [Range(0f, 1f)]
    [SerializeField] private float landingNormalThreshold = 0.5f;
    #endregion

    #region 运行时状态
    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private SpriteRenderer spriteRenderer;
    private CoinPickup coinPickup;
    private Collider2D ignoredCollider;

    private bool launched;
    private bool landed;
    private bool initialized;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 initialScale;
    private int initialSortingOrder;
    #endregion

    /// <summary>金币是否已经落地并停稳。</summary>
    public bool IsLanded => landed;

    /// <summary>金币是否已经被蜂巢顶出过。</summary>
    public bool IsLaunched => launched;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        coinPickup = GetComponent<CoinPickup>();

        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialScale = transform.localScale;
        if (spriteRenderer != null)
            initialSortingOrder = spriteRenderer.sortingOrder;

        // 未弹出前保持静止，不要因为场景加载就掉下去
        if (rb != null) rb.simulated = false;

        // 静态时是触发器，弹出时会临时变成实体碰撞体用于落地
        if (bodyCollider != null) bodyCollider.isTrigger = false;
    }

    /// <summary>
    /// 忽略与触发蜂巢的那只兔子的碰撞，避免金币刚弹出就被兔子的碰撞箱撞歪。
    /// 注意：金币此时可能还没被激活（Awake 未执行），所以要主动 GetComponent。
    /// </summary>
    public void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null) return;

        ignoredCollider = other;

        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
        if (bodyCollider != null)
            Physics2D.IgnoreCollision(bodyCollider, other, true);
    }

    /// <summary>
    /// 从蜂巢里蹦出来：激活金币、给向上初速度，随后交给重力。
    /// </summary>
    /// <param name="upwardSpeed">向上初速度（m/s）</param>
    /// <param name="horizontalSpeed">水平初速度（m/s，正值向右）</param>
    /// <param name="gravityScale">飞行时的重力缩放</param>
    public void PopOut(float upwardSpeed, float horizontalSpeed, float gravityScale)
    {
        EnsureInitialized();

        gameObject.SetActive(true);

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (coinPickup == null) coinPickup = GetComponent<CoinPickup>();

        if (rb == null)
        {
            Debug.LogError($"[{name}] CoinPopOut 需要 Rigidbody2D！", this);
            return;
        }

        launched = true;
        landed = false;

        if (spriteRenderer != null) spriteRenderer.sortingOrder = popSortingOrder;

        if (bodyCollider != null)
        {
            bodyCollider.enabled = true;
            bodyCollider.isTrigger = false;
        }

        // 落地前不允许拾取
        if (allowPickupOnlyAfterLanding && coinPickup != null)
            coinPickup.enabled = false;

        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = Mathf.Max(0.01f, gravityScale);
        rb.velocity = new Vector2(horizontalSpeed, upwardSpeed);
        rb.angularVelocity = 0f;
        rb.WakeUp();

        // 再次确认忽略兔子碰撞（激活瞬间可能重置过）
        if (ignoredCollider != null && bodyCollider != null)
            Physics2D.IgnoreCollision(bodyCollider, ignoredCollider, true);
    }

    /// <summary>
    /// 蜂巢重置时调用：把金币恢复到藏在蜂巢里的初始状态，并清空拾取状态。
    /// 已拾取的金币会把它加过的分数退回，避免反复死亡刷金币。
    /// </summary>
    public void ResetCoin()
    {
        EnsureInitialized();

        launched = false;
        landed = false;

        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
            rb.bodyType = RigidbodyType2D.Dynamic;
        }

        if (bodyCollider != null)
        {
            bodyCollider.isTrigger = false;
            bodyCollider.enabled = true;
        }

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = initialSortingOrder;

        transform.SetPositionAndRotation(initialPosition, initialRotation);
        transform.localScale = initialScale;

        if (coinPickup == null)
            coinPickup = GetComponent<CoinPickup>();

        if (coinPickup != null)
        {
            coinPickup.RefundCollectedValue();
            coinPickup.ResetPickupState();
            coinPickup.enabled = true;
        }

        gameObject.SetActive(false);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!launched || landed || collision.collider == null) return;
        if (ignoredCollider != null && collision.collider == ignoredCollider) return;
        if (collision.contactCount == 0) return;

        // 上升阶段不判定落地，避免出生时与地面轻微重叠就被判成“落地”
        if (rb != null && rb.velocity.y > 0.1f) return;

        // 只把“水平面”当作落地，撞到侧墙时继续下落
        if (Mathf.Abs(collision.GetContact(0).normal.y) < landingNormalThreshold) return;

        Land();
    }

    private void Land()
    {
        landed = true;

        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;

            if (freezeOnLand)
                rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // 落地后金币不再参与物理，避免挡住 / 推动兔子；拾取交给 CoinPickup 的距离判定
        if (bodyCollider != null) bodyCollider.enabled = false;

        // 落地后才允许交互拾取
        if (coinPickup != null) coinPickup.enabled = true;
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D col = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = landed ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
