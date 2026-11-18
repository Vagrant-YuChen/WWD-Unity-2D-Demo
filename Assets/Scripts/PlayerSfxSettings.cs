using UnityEngine;

/// <summary>
/// 玩家角色（兔子 / 小羊）的移动、跳跃、落地音效全局设置。
/// 在 Assets/Resources/PlayerSfxSettings 选中该资产，即可在 Inspector 中替换片段、调节音量与移动判定参数。
/// 跳跃 / 落地为两个角色共用；移动循环音效按角色区分（兔子 / 小羊各一条）。
/// </summary>
[CreateAssetMenu(menuName = "Wacky World Dash/Player SFX Settings", fileName = "PlayerSfxSettings")]
public class PlayerSfxSettings : ScriptableObject
{
    [Header("音效片段")]
    [Tooltip("跳跃音效（两个角色共用，空空格起跳时触发）")]
    public AudioClip jumpClip;

    [Tooltip("落地音效（两个角色共用，离地后再次着地时触发）")]
    public AudioClip landClip;

    [Tooltip("兔子的移动循环音效")]
    public AudioClip bunnyMoveLoopClip;

    [Tooltip("小羊的移动循环音效")]
    public AudioClip sheepMoveLoopClip;

    [Header("音量")]
    [Tooltip("跳跃音效音量")]
    [Range(0f, 1f)] public float jumpVolume = 0.5f;

    [Tooltip("落地音效音量")]
    [Range(0f, 1f)] public float landVolume = 0.5f;

    [Tooltip("兔子移动循环音效音量")]
    [Range(0f, 1f)] public float bunnyMoveVolume = 0.3f;

    [Tooltip("小羊移动循环音效音量")]
    [Range(0f, 1f)] public float sheepMoveVolume = 0.3f;

    [Header("落地音效判定")]
    [Tooltip("本次腾空最高点到落地点的高度差超过此值才播放落地音效，避免轻微下台阶/贴地抖动频繁触发（以金币高度 0.3 为参照，默认 0.2）")]
    [Min(0f)] public float landMinFallDistance = 0.2f;

    [Header("移动音效判定（基于角色自身水平速度）")]
    [Tooltip("在地面且水平速度绝对值超过此值时，开始播放移动音效")]
    [Min(0f)] public float moveStartSpeed = 0.4f;

    [Tooltip("水平速度低于此值时开始淡出（略低于起播阈值，形成迟滞以避免临界反复启停）")]
    [Min(0f)] public float moveStopSpeed = 0.25f;

    [Tooltip("移动音效淡入 / 淡出时长（秒）")]
    [Min(0f)] public float moveFadeTime = 0.08f;
}
