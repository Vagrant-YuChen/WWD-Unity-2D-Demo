using UnityEngine;

/// <summary>
/// 摆锤（WreckingBall）击杀判定：挂在链条球下方的 Ball 子物体上，由 WreckingBallSetup 自动装配。
///
/// 球体是 Kinematic 刚体 + 触发器，位置完全由 Animator 的序列帧曲线驱动（也就是每帧直接改 Transform）。
/// 这种“动画搬运”的碰撞体在摆锤摆动较快时，物理引擎很可能整帧跳过与小羊的接触，导致触发器事件漏掉，
/// 所以这里不只依赖 OnTrigger 回调：
///   1) 每帧动画结算后（LateUpdate）用 CircleCollider2D 的真实世界圆心/半径做一次重叠检测；
///   2) 再沿「上一帧圆心 → 当前帧圆心」做扫掠采样，防止球一帧跨过小羊；
///   3) 保留 OnTrigger / OnCollision 回调作为兜底。
/// 三者任意一个命中就调用 BunnyController.TriggerDeath()。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class WreckingBallKill : MonoBehaviour
{
    #region 可配置参数
    [Tooltip("击杀半径相对 CircleCollider2D 的缩放。1 = 完全按碰撞体大小；略大于 1 更容易命中，但不能太夸张")]
    [SerializeField] private float killRadiusScale = 1f;

    [Tooltip("是否在上一帧与当前帧之间做扫掠检测（开启后可避免高速摆动穿过小羊却不判定）")]
    [SerializeField] private bool sweepBetweenFrames = true;

    [Tooltip("在 Console 打印击杀信息，方便确认判定是否生效")]
    [SerializeField] private bool debugLogs;
    #endregion

    #region 运行时状态
    private Collider2D ballCollider;
    private CircleCollider2D ballCircle;

    private ContactFilter2D overlapFilter;
    private readonly Collider2D[] overlapBuffer = new Collider2D[8];

    private Vector2 lastCenter;
    private bool hasLastCenter;
    #endregion

    #region 生命周期
    private void Awake()
    {
        ballCollider = GetComponent<Collider2D>();
        ballCircle = ballCollider as CircleCollider2D;

        overlapFilter = new ContactFilter2D();
        overlapFilter.NoFilter();
        overlapFilter.useTriggers = true;

        if (ballCollider == null)
        {
            Debug.LogError($"[{name}] WreckingBallKill 需要一个 Collider2D（建议 CircleCollider2D）。");
        }
        else if (ballCircle == null)
        {
            Debug.LogWarning($"[{name}] 摆锤球上的碰撞体不是 CircleCollider2D，将退回整体包围盒判定。", this);
        }
    }
    #endregion

    #region 每帧重叠检测（核心判定）
    private void LateUpdate()
    {
        if (ballCollider == null) return;

        // Animator 在 Update 阶段已经写完 Transform，这里取到的是本帧最终的球心。
        Vector2 center = GetColliderCenter();
        if (!hasLastCenter)
        {
            lastCenter = center;
            hasLastCenter = true;
        }

        CheckOverlapAt(center);

        if (sweepBetweenFrames)
        {
            SweepBetween(lastCenter, center);
        }

        lastCenter = center;
    }

    /// <summary>在当前圆心做一次精确形状重叠检测。</summary>
    private void CheckOverlapAt(Vector2 center)
    {
        int count = ballCircle != null
            ? Physics2D.OverlapCircle(center, GetKillRadius(), overlapFilter, overlapBuffer)
            : ballCollider.OverlapCollider(overlapFilter, overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapBuffer[i];
            if (hit == null || hit == ballCollider) continue;
            TryKill(hit);
        }
    }

    /// <summary>沿线段采样，补齐两帧之间被“跳过”的那一段。</summary>
    private void SweepBetween(Vector2 from, Vector2 to)
    {
        if (ballCircle == null) return;

        float radius = GetKillRadius();
        if (radius <= 0f) return;

        float distance = Vector2.Distance(from, to);
        if (distance <= radius) return;

        int steps = Mathf.Clamp(Mathf.CeilToInt(distance / (radius * 0.75f)), 1, 8);
        for (int i = 1; i < steps; i++)
        {
            CheckOverlapAt(Vector2.Lerp(from, to, (float)i / steps));
        }
    }
    #endregion

    #region 物理回调兜底
    private void OnTriggerEnter2D(Collider2D other) => TryKill(other);
    private void OnTriggerStay2D(Collider2D other) => TryKill(other);
    private void OnCollisionEnter2D(Collision2D collision) => TryKill(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryKill(collision.collider);

    private void TryKill(Collider2D other)
    {
        if (other == null) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        // 死亡语音统一由角色控制器（SheepController.TriggerDeath）播放，不再在此重复播放。
        bunny.TriggerDeath();

        if (debugLogs)
        {
            Debug.Log($"[{name}] 摆锤击中 {bunny.name}（球心 {GetColliderCenter()}，半径 {GetKillRadius():F3}）", bunny);
        }
    }
    #endregion

    #region 辅助方法
    private Vector2 GetColliderCenter()
    {
        if (ballCircle != null)
        {
            return transform.TransformPoint(ballCircle.offset);
        }

        return ballCollider != null ? (Vector2)ballCollider.bounds.center : (Vector2)transform.position;
    }

    private float GetKillRadius()
    {
        if (ballCircle == null) return 0f;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        return ballCircle.radius * scale * Mathf.Max(0.01f, killRadiusScale);
    }
    #endregion

    #region Gizmos调试
    private void OnDrawGizmosSelected()
    {
        CircleCollider2D circle = ballCircle != null ? ballCircle : GetComponent<CircleCollider2D>();
        if (circle == null) return;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float radius = circle.radius * scale * Mathf.Max(0.01f, killRadiusScale);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.TransformPoint(circle.offset), radius);
    }
    #endregion
}
