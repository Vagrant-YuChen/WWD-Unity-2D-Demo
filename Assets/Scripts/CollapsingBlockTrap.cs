using System.Collections;
using UnityEngine;

/// <summary>
/// 会碎裂的临时站立平台（CollapsingBlock）。
///
/// 玩法：
///   1) 小兔踩上碰撞箱后开始计时，站立 <see cref="standTime"/> 秒（默认 0.5s，可在 Inspector 调）；
///   2) 计时结束的瞬间同时做两件事：播放序列帧动画 + 取消激活 BoxCollider2D，
///      小兔因为脚下没有支撑而随重力掉落；
///   3) 序列帧播放到第 <see cref="holdFrameIndex"/> 帧（默认 010）时插入一段“空白帧”停顿
///      （<see cref="holdDuration"/> 秒，可在 Inspector 调），期间保持 010 这一帧不动，
///      给掉落的小兔留出时间；
///   4) 停顿结束后继续播放 010→020 的复原动画，整段 000→020 播完，
///      BoxCollider2D 重新激活，等待下一次踩踏。
///
/// 动画由 Animator 播放 <see cref="collapseStateName"/> 状态（CollapsingBlock.controller），
/// 脚本通过 Animator.speed 在 010 帧处做“暂停/继续”来实现可调的停顿，
/// 因此停顿时长在 Inspector 里改即可，无需重新烘焙动画剪辑。
///
/// 组件与状态机由 Assets/Editor/CollapsingBlockSetup.cs 自动装配；
/// 也可运行菜单：Tools/Collapsing Block/Setup Collapsing Blocks In Open Scene
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Animator))]
public class CollapsingBlockTrap : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 站立计时（重点调试区） ===")]
    [Tooltip("小兔踩上碰撞箱后，平台能维持站立的时间（秒）。到点后动画开始、碰撞箱关闭")]
    [SerializeField, Min(0f)] private float standTime = 0.5f;

    [Header("=== 序列帧动画（自动装配会填好） ===")]
    [Tooltip("平台身上的 Animator。留空会在 Awake 自动获取")]
    [SerializeField] private Animator animator;
    [Tooltip("序列帧状态名（CollapsingBlock.controller 里的状态名）")]
    [SerializeField] private string collapseStateName = "Collapse";
    [Tooltip("完整 000→020 序列帧剪辑，用来读取帧率 / 时长以精确计算 010 帧位置。留空会从 Animator 上兜底获取")]
    [SerializeField] private AnimationClip collapseClip;

    [Header("=== 010 帧停顿（重点调试区） ===")]
    [Tooltip("播放到第几帧时插入停顿（0 起。默认 10 = CollapsingBlock2x.010）")]
    [SerializeField, Min(0)] private int holdFrameIndex = 10;
    [Tooltip("在 holdFrameIndex 这一帧停住、保持其模样的时长（秒）。这就是“几帧空白帧”的停顿时间，运行时可调")]
    [SerializeField, Min(0f)] private float holdDuration = 0.3f;

    [Header("=== 落地检测 ===")]
    [Tooltip("只在“踩在顶面”时触发（防止从侧面/下方撞到也触发）。关闭则任何接触都会触发")]
    [SerializeField] private bool requireFromTop = true;
    [Tooltip("判定“踩在顶面”时允许的竖直容差（世界单位）")]
    [SerializeField, Min(0f)] private float topTolerance = 0.15f;

    [Header("=== 复位 ===")]
    [Tooltip("整段动画播完后，把外观复位到第 000 帧（保持完好模样等待下一次踩踏）")]
    [SerializeField] private bool resetToFirstFrameOnIdle = true;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    #endregion

    #region 运行时状态
    private BoxCollider2D box;
    private bool activated;          // 已经从踩踏触发、正在走流程（含动画与停顿）
    private Coroutine routine;
    #endregion

    #region 生命周期
    private void Awake()
    {
        box = GetComponent<BoxCollider2D>();
        if (animator == null) animator = GetComponent<Animator>();

        if (animator == null)
            Debug.LogError($"[{name}] CollapsingBlockTrap 需要 Animator 才能播放序列帧动画。");
        else
            animator.speed = 0f; // 开局先冻结，避免在 Start 复位前抢先播放整段碎裂动画
    }

    private void Start()
    {
        // 开局停在完好（第 000 帧）外观，碰撞箱保持开启，等待玩家踩踏
        ResetToIdle();
    }
    #endregion

    #region 踩踏检测
    private void OnCollisionEnter2D(Collision2D collision) => TryStart(collision);
    private void OnCollisionStay2D(Collision2D collision) => TryStart(collision);

    private void TryStart(Collision2D collision)
    {
        if (activated || collision == null) return;

        BunnyController bunny = BunnyContactUtility.FindBunny(collision.collider);
        if (bunny == null || bunny.isDead) return;

        if (requireFromTop && !IsBunnyOnTop(collision.collider)) return;

        activated = true;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(CollapseSequence());

        if (debugLogs) Debug.Log($"[{name}] 小兔踩上，{standTime:0.00}s 后开始碎裂。");
    }

    /// <summary>判断小兔是不是踩在平台顶面（脚底接近碰撞箱顶面）。</summary>
    private bool IsBunnyOnTop(Collider2D bunnyCollider)
    {
        if (box == null || bunnyCollider == null) return true;
        Bounds blockBounds = box.bounds;
        Bounds bunnyBounds = bunnyCollider.bounds;
        return bunnyBounds.min.y >= blockBounds.max.y - topTolerance;
    }
    #endregion

    #region 碎裂流程
    private IEnumerator CollapseSequence()
    {
        // 1) 站立时间结束
        if (standTime > 0f) yield return new WaitForSeconds(standTime);

        // 2) 同时：触发序列帧动画 + 取消激活碰撞箱（小兔随即掉落）
        if (box != null) box.enabled = false;
        PlayFromStart();

        if (debugLogs) Debug.Log($"[{name}] 碰撞箱已关闭，开始播放碎裂动画。");

        float holdN = HoldNormalized();

        // 3) 播放到 holdFrameIndex（010）
        while (holdN > 0f && CurrentNormalized() < holdN && !IsStateFinished()) yield return null;

        // 精确停在 010 这一帧并暂停，保持其模样
        SnapToNormalized(holdN);
        SetPlaybackSpeed(0f);

        if (debugLogs) Debug.Log($"[{name}] 停在 010 帧，停顿 {holdDuration:0.00}s。");

        // 4) 空白帧停顿
        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

        // 5) 继续播放 010→020 的复原动画
        SetPlaybackSpeed(1f);
        while (!IsStateFinished()) yield return null;

        // 6) 整段 000→020 播完：复位外观并重新激活碰撞箱
        ResetToIdle();
        activated = false;
        routine = null;

        if (debugLogs) Debug.Log($"[{name}] 动画播完，碰撞箱重新激活。");
    }
    #endregion

    #region 动画控制
    private void PlayFromStart()
    {
        if (animator == null) return;
        animator.Play(collapseStateName, 0, 0f);
        animator.Update(0f);
        SetPlaybackSpeed(1f);
    }

    private void SnapToNormalized(float normalized)
    {
        if (animator == null) return;
        animator.Play(collapseStateName, 0, Mathf.Clamp01(normalized));
        animator.Update(0f);
    }

    private void SetPlaybackSpeed(float speed)
    {
        if (animator != null) animator.speed = speed;
    }

    /// <summary>复位到完好外观（第 000 帧）并重新激活碰撞箱，等待下一次踩踏。</summary>
    private void ResetToIdle()
    {
        if (animator != null)
        {
            if (resetToFirstFrameOnIdle)
            {
                animator.Play(collapseStateName, 0, 0f);
                animator.Update(0f);
                SetPlaybackSpeed(0f);
            }
            else
            {
                SetPlaybackSpeed(1f);
            }
        }

        if (box != null) box.enabled = true;
    }

    private AnimatorStateInfo CurrentStateInfo()
    {
        return animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
    }

    private float CurrentNormalized()
    {
        AnimatorStateInfo info = CurrentStateInfo();
        return info.normalizedTime;
    }

    private bool IsStateFinished()
    {
        if (animator == null) return true;
        AnimatorStateInfo info = CurrentStateInfo();
        return info.IsName(collapseStateName) && info.normalizedTime >= 0.999f;
    }

    /// <summary>holdFrameIndex 对应的归一化时间（0~1），用于精确定位动画停帧点。</summary>
    private float HoldNormalized()
    {
        AnimationClip clip = collapseClip != null ? collapseClip : ResolveClipFromAnimator();
        if (clip == null) return 0f;

        float frameRate = clip.frameRate > 0f ? clip.frameRate : 12f;
        float length = clip.length;
        if (length <= 0f) return 0f;

        float holdTime = holdFrameIndex / frameRate;
        return Mathf.Clamp01(holdTime / length);
    }

    private AnimationClip ResolveClipFromAnimator()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        return clips != null && clips.Length > 0 ? clips[0] : null;
    }
    #endregion
}
