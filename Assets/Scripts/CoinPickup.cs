using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 金币拾取：
/// - 玩家进入金币左右两侧的水平检测框后按 F 拾取，金币计入分数并从地图消失。
///   检测框只在 x 轴方向展开（horizontalRange），并带一个很小的垂直容差（verticalTolerance），
///   因此不会像圆形范围那样穿透地板/墙体从上下方拾取。
/// - 胜利判定由旗杆触发（见 FlagVictory）。
/// </summary>
public class CoinPickup : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 交互配置（仅水平方向检测）===")]
    [Tooltip("水平(x轴)检测半宽：玩家与金币的水平距离需 ≤ 此值（单位：米）。只在金币左右两侧生效，不再使用圆形范围")]
    [Min(0f)] public float horizontalRange = 1.5f;
    [Tooltip("垂直(y轴)容差：玩家与金币的垂直距离需 ≤ 此值（单位：米）。调小可避免隔着地板/墙体上下拾取")]
    [Min(0f)] public float verticalTolerance = 0.6f;
    [Tooltip("检测框相对金币的偏移（x 向右，y 向上）。用于把检测框对准金币实际可见的位置")]
    public Vector2 detectionOffset = Vector2.zero;

    [Header("=== 玩家配置 ===")]
    [Tooltip("玩家标签（默认Player）")]
    public string playerTag = "Player";

    [Header("=== 计分配置 ===")]
    [Tooltip("本枚金币的价值（拾取后累加到 TotalCollected）")]
    public int coinValue = 1;
    #endregion

    #region 私有变量
    private Transform playerTransform;
    private BunnyController bunnyController;
    private CoinPopOut coinPopOut;
    private bool isPlayerInRange;
    private bool isCoinCollected;
    private bool wasPlayerDead;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    #endregion

    /// <summary>本局（当前场景）累计拾取的金币数量；通关结算时并入存档总额（见 CoinWallet）。</summary>
    public static int TotalCollected { get; private set; }

    /// <summary>需要时（例如重新开始关卡）可手动清零本局计数。</summary>
    public static void ResetTotal() => TotalCollected = 0;

    static CoinPickup()
    {
        // 每次场景加载时清零，只统计“本关这一局”的拾取数量；真正入账在通关时进行（见 CoinWallet）。
        SceneManager.sceneLoaded += (scene, mode) => TotalCollected = 0;
    }

    private void Awake()
    {
        coinPopOut = GetComponent<CoinPopOut>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();
    }

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player != null)
        {
            playerTransform = player.transform;
            bunnyController = player.GetComponent<BunnyController>();
            wasPlayerDead = bunnyController != null && bunnyController.isDead;
        }
    }

    private void Update()
    {
        if (isCoinCollected)
        {
            // 已拾取的普通金币（无 CoinPopOut）在死亡复活时要重新出现，除非已经胜利。
            if (coinPopOut == null)
                CheckRespawnReset();
            return;
        }

        CheckPlayerDistance();

        if (isPlayerInRange && Input.GetKeyDown(KeyCode.F))
            CollectCoin();
    }

    #region 核心逻辑
    private void CheckPlayerDistance()
    {
        if (playerTransform == null)
        {
            isPlayerInRange = false;
            return;
        }

        Vector2 center = GetDetectionCenter();
        Vector2 playerPos = playerTransform.position;

        // 只做水平(x轴)判定，再叠加一个很小的垂直容差：
        // 这样玩家必须站在金币左右两侧的窄带内，无法隔墙/隔地板从上下方拾取。
        float dx = Mathf.Abs(playerPos.x - center.x);
        float dy = Mathf.Abs(playerPos.y - center.y);
        isPlayerInRange = dx <= horizontalRange && dy <= verticalTolerance;
    }

    /// <summary>检测框中心（金币位置 + Inspector 偏移）。</summary>
    private Vector2 GetDetectionCenter()
    {
        return (Vector2)transform.position + detectionOffset;
    }

    private void CollectCoin()
    {
        isCoinCollected = true;

        wasPlayerDead = bunnyController != null && bunnyController.isDead;

        // 计入本局金币数（通关结算时才并入存档，见 CoinWallet）。
        TotalCollected = Mathf.Max(0, TotalCollected + coinValue);

        // 蜂巢金币不能 Destroy：复活时要复位并重新弹出；普通金币隐藏，等待玩家死亡后重新出现。
        if (coinPopOut != null)
            gameObject.SetActive(false);
        else
        {
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            if (bodyCollider != null) bodyCollider.enabled = false;
        }
    }

    /// <summary>蜂巢重置时调用：如果这枚金币已经被拾取，把它的分数退回。</summary>
    public void RefundCollectedValue()
    {
        if (!isCoinCollected) return;

        TotalCollected = Mathf.Max(0, TotalCollected - coinValue);
        isCoinCollected = false;
    }

    /// <summary>蜂巢重置时调用：清空拾取状态，让金币可以再次被拾取。</summary>
    public void ResetPickupState()
    {
        isCoinCollected = false;
        isPlayerInRange = false;
    }

    /// <summary>
    /// 普通金币：玩家死亡复活时，若本枚金币已被拾取且尚未胜利，则恢复到原位置并退回分数。
    /// </summary>
    private void CheckRespawnReset()
    {
        if (bunnyController == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null) bunnyController = player.GetComponent<BunnyController>();
            if (bunnyController == null) return;
        }

        bool dead = bunnyController.isDead;

        // 检测 isDead 的下降沿：玩家复活时重置
        if (wasPlayerDead && !dead && !IsVictory())
            ResetPlainCoin();

        wasPlayerDead = dead;
    }

    private void ResetPlainCoin()
    {
        if (!isCoinCollected) return;

        RefundCollectedValue();
        ResetPickupState();

        // 恢复显示与交互
        if (spriteRenderer != null) spriteRenderer.enabled = true;
        if (bodyCollider != null) bodyCollider.enabled = true;

        isPlayerInRange = false;
    }

    private bool IsVictory()
    {
        return FlagVictory.HasVictory;
    }
    #endregion

    private void OnDrawGizmos()
    {
        DrawDetectionGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawDetectionGizmo(true);
    }

    /// <summary>
    /// 在 Scene 视图里画出水平检测框，方便直观调试范围。
    /// 未选中时显示黄色线框；选中时高亮为绿色并显示半透明填充与尺寸标签。
    /// </summary>
    private void DrawDetectionGizmo(bool selected)
    {
        Vector2 center2 = GetDetectionCenter();
        Vector3 center = new Vector3(center2.x, center2.y, transform.position.z);
        Vector3 size = new Vector3(horizontalRange * 2f, verticalTolerance * 2f, 0.02f);

        // 水平检测框（主体范围）
        Gizmos.color = selected ? new Color(0f, 1f, 0.6f, 1f) : new Color(1f, 0.85f, 0.1f, 1f);
        Gizmos.DrawWireCube(center, size);

        // 强调“只沿 x 轴检测”：画出水平中轴线与左右两条边界
        Vector3 left = new Vector3(center.x - horizontalRange, center.y, center.z);
        Vector3 right = new Vector3(center.x + horizontalRange, center.y, center.z);
        Vector3 up = Vector3.up * verticalTolerance;
        Gizmos.DrawLine(left, right);
        Gizmos.DrawLine(left - up, left + up);
        Gizmos.DrawLine(right - up, right + up);

        // 金币中心 -> 检测框中心（存在偏移时才明显）
        if (detectionOffset != Vector2.zero)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 1f);
            Gizmos.DrawLine(transform.position, center);
        }

        if (!selected) return;

        // 选中时半透明填充，便于观察覆盖范围
        Gizmos.color = new Color(0f, 1f, 0.6f, 0.12f);
        Gizmos.DrawCube(center, size);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(
            center + Vector3.up * (verticalTolerance + 0.15f),
            $"水平半宽 {horizontalRange:0.##}m\n垂直容差 {verticalTolerance:0.##}m");
#endif
    }
}
