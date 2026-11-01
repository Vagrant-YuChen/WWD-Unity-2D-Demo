using UnityEngine;

/// <summary>
/// 阶梯触发区：小羊的脚（碰撞体）进入该触发器区域时，通知滚石朝对应方向滚动。
///
/// 通常由 <see cref="RollingBoulder"/> 在运行时自动生成左右两个（无需手动摆）。
/// 若想手动放置，给它挂上任意 Collider2D（Is Trigger）并在 Inspector 里指定 side 与 boulder 即可。
///
/// 它只负责"踩到哪侧阶梯"这一件事，滚石是否真的会滚由 RollingBoulder 自己保证
/// （锁定状态才响应，滚过一次后不再响应），因此不会出现别的情况误触发。
/// </summary>
public class BoulderStairTrigger : MonoBehaviour
{
    public enum StairSide
    {
        Left,
        Right
    }

    [Tooltip("该触发区代表哪一侧的阶梯")]
    [SerializeField] private StairSide side = StairSide.Left;

    [Tooltip("要触发的滚石（留空时自动在场景里找一个）")]
    [SerializeField] private RollingBoulder boulder;

    [Tooltip("踩上去之后是否要等小羊离地/真正踩实才触发。默认进入即触发，最贴合「脚一碰到就触发」")]
    [SerializeField] private bool requireGrounded = false;

    /// <summary>运行时自动生成时用它注入引用。</summary>
    public void Configure(StairSide newSide, RollingBoulder target)
    {
        side = newSide;
        boulder = target;
    }

    private void Awake()
    {
        if (boulder == null)
        {
            boulder = FindObjectOfType<RollingBoulder>();
        }
    }

    private void OnTriggerEnter2D(Collider2D other) => TryTrigger(other);
    private void OnTriggerStay2D(Collider2D other) => TryTrigger(other);

    private void TryTrigger(Collider2D other)
    {
        if (boulder == null || !boulder.IsLocked) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        if (requireGrounded && !bunny.IsGroundedState) return;

        if (side == StairSide.Left)
        {
            boulder.RollLeft();
        }
        else
        {
            boulder.RollRight();
        }
    }
}
