using UnityEngine;

// 刀片杀死兔子的核心脚本（适配 BunnyController 优化版，复用 BunnyContactUtility）
[RequireComponent(typeof(Collider2D))] // 需要 BoxCollider2D/CircleCollider2D/PolygonCollider2D 等具体碰撞体
public class BladeKill : MonoBehaviour
{
    #region 可配置参数（Inspector面板调试）
    [Tooltip("兔子的Layer层（需和兔子预制体的Layer一致）")]
    [SerializeField] private LayerMask bunnyLayer;

    [Tooltip("是否只触发一次死亡（比如旋转刀片设为false，一次性刀片设为true）")]
    [SerializeField] private bool triggerOnce = false;

    [Tooltip("死亡触发音效（可选）")]
    [SerializeField] private AudioClip deathSound;

    [Tooltip("刀片碰撞后是否播放动画（可选）")]
    [SerializeField] private Animator bladeAnimator;

    [Tooltip("死亡触发的动画参数名（比如\"OnHit\"）")]
    [SerializeField] private string hitAnimTrigger = "OnHit";
    #endregion

    #region 私有状态
    private AudioSource audioSource; // 音效组件
    private bool hasTriggered = false; // 防重复触发标记
    private Collider2D bladeCollider; // 刀片碰撞体
    #endregion

    private void Awake()
    {
        bladeCollider = GetComponent<Collider2D>();

        if (bladeCollider == null)
        {
            Debug.LogError($"[{name}] BladeKill 需要 Collider2D 组件！");
            enabled = false;
            return;
        }

        // 强制设置碰撞体为触发器（避免物理碰撞，只检测接触）
        bladeCollider.isTrigger = true;

        // 自动添加音效组件（如果配置了音效）
        if (deathSound != null && !TryGetComponent(out audioSource))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = deathSound;
            audioSource.volume = 0.8f;
            audioSource.playOnAwake = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKill(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (triggerOnce || hasTriggered) return; // 一次性刀片跳过持续检测
        TryKill(other);
    }

    #region 核心逻辑
    // 2D触发器碰撞检测（核心：检测兔子并触发死亡）
    private void TryKill(Collider2D other)
    {
        if (hasTriggered) return;
        if (!BunnyContactUtility.IsInLayerMask(other.gameObject.layer, bunnyLayer)) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        bunny.TriggerDeath();
        TriggerBladeEffect();

        if (triggerOnce) hasTriggered = true;
    }
    #endregion

    #region 辅助方法
    // 触发刀片特效（音效+动画）
    private void TriggerBladeEffect()
    {
        // 播放死亡音效
        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        // 播放刀片碰撞动画
        if (bladeAnimator != null && !string.IsNullOrEmpty(hitAnimTrigger))
        {
            bladeAnimator.SetTrigger(hitAnimTrigger);
        }
    }

    // 重置刀片状态（可选：供其他脚本调用，比如复活后重置刀片）
    public void ResetBlade()
    {
        hasTriggered = false;
    }
    #endregion

    #region Gizmos调试（Scene窗口可视化刀片碰撞范围）
    private void OnDrawGizmosSelected()
    {
        if (bladeCollider == null)
        {
            bladeCollider = GetComponent<Collider2D>();
        }

        if (bladeCollider != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(bladeCollider.bounds.center, bladeCollider.bounds.size);
        }
    }
    #endregion
}