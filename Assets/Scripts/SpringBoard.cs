using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SpringBoard : MonoBehaviour
{
    #region 弹跳设置
    [Header("=== 弹跳设置 ===")]
    [Tooltip("弹起后兔子的竖直速度。建议 11~14；超过 BunnyController.maxSpeed.y 也可以，脚本已放行")]
    [SerializeField] private float bounceSpeed = 12f;
    [Tooltip("是否保留兔子水平速度")]
    [SerializeField] private bool preserveHorizontalVelocity = true;
    [Tooltip("额外水平推力（0=不推）")]
    [SerializeField] private float horizontalBoost = 0f;
    [Tooltip("兔子所在层（Bunny 在 Default 层时保持 1）")]
    [SerializeField] private LayerMask bunnyLayer = 1;
    [Tooltip("两次弹跳的最小间隔（秒）")]
    [SerializeField] private float retriggerCooldown = 0.03f;
    #endregion

    #region 回落 / 复位
    [Header("=== 回落 / 复位 ===")]
    [Tooltip("弹簧回落完成、可再次弹跳的时间（秒）。有 Animation Event 时作为兜底")]
    [SerializeField] private float returnDuration = 0.35f;
    #endregion

    #region 动画 / 音效
    [Header("=== 动画 ===")]
    [Tooltip("留空会自动 GetComponent<Animator>()")]
    [SerializeField] private Animator springAnimator;
    [Tooltip("Animator 中触发的 Trigger 名称")]
    [SerializeField] private string bounceTrigger = "Bounce";

    [Header("=== 音效 ===")]
    [SerializeField] private AudioClip bounceSound;
    [Range(0f, 1f)]
    [SerializeField] private float bounceVolume = 0.8f;
    #endregion

    #region 私有状态
    private Collider2D springCollider;
    private AudioSource audioSource;
    private bool isReady = true;
    private float returnTimer;
    private float lastBounceTime = -999f;
    #endregion

    private void Awake()
    {
        springCollider = GetComponent<Collider2D>();

        // 弹簧必须是实体碰撞体，否则兔子会直接穿过去
        if (springCollider != null && springCollider.isTrigger)
        {
            Debug.LogWarning($"[{name}] SpringBoard 需要实体碰撞体（Is Trigger 关闭），已自动关闭。");
            springCollider.isTrigger = false;
        }

        if (springAnimator == null) springAnimator = GetComponent<Animator>();

        if (bounceSound != null)
        {
            if (!TryGetComponent(out audioSource))
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
            audioSource.clip = bounceSound;
            audioSource.volume = bounceVolume;
        }
    }

    private void Update()
    {
        if (isReady) return;

        returnTimer -= Time.deltaTime;
        if (returnTimer <= 0f)
        {
            isReady = true;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryBounce(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // 回落完成后，如果兔子还踩在弹簧上，可以紧接着再次弹跳
        TryBounce(collision);
    }

    #region 核心逻辑
    private void TryBounce(Collision2D collision)
    {
        if (!isReady) return;
        if (Time.time - lastBounceTime < retriggerCooldown) return;

        Collider2D other = collision.collider;
        if (!BunnyContactUtility.IsInLayerMask(other.gameObject.layer, bunnyLayer)) return;
        if (!IsFromAbove(other)) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        Rigidbody2D bunnyRb = BunnyContactUtility.FindBunnyRigidbody(other, bunny);
        if (bunnyRb == null) return;

        // 只在下落 / 静止时弹；已经在上升说明刚被弹起，不重复触发
        if (bunnyRb.velocity.y > 0.1f) return;

        bunny.ApplySpringBounce(bounceSpeed);

        float vx = preserveHorizontalVelocity ? bunnyRb.velocity.x : 0f;
        bunnyRb.velocity = new Vector2(vx + horizontalBoost, bunnyRb.velocity.y);

        lastBounceTime = Time.time;
        isReady = false;
        returnTimer = returnDuration;

        if (springAnimator != null && !string.IsNullOrEmpty(bounceTrigger))
        {
            springAnimator.SetTrigger(bounceTrigger);
        }

        if (audioSource != null && bounceSound != null)
        {
            audioSource.PlayOneShot(bounceSound, bounceVolume);
        }
    }

    private bool IsFromAbove(Collider2D other)
    {
        if (springCollider == null) return true;

        // 兔子底部必须接近或高于弹簧顶部
        float springTop = springCollider.bounds.max.y;
        if (other.bounds.min.y < springTop - 0.25f) return false;

        // 水平方向需要有重叠，避免从侧面擦到时误触发
        float dx = Mathf.Abs(other.bounds.center.x - springCollider.bounds.center.x);
        float horizontalReach = (other.bounds.extents.x + springCollider.bounds.extents.x) * 0.8f;
        return dx <= horizontalReach;
    }

    /// <summary>
    /// 供 Animation Event 调用：弹簧回落动画结束时恢复可弹跳。
    /// 在 Bounce 动画最后一帧添加 Event，Function 填 OnSpringReturnComplete。
    /// </summary>
    public void OnSpringReturnComplete()
    {
        isReady = true;
        returnTimer = 0f;
    }
    #endregion

    private void OnDrawGizmosSelected()
    {
        Collider2D col = springCollider != null ? springCollider : GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}