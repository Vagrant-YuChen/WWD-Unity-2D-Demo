using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 金币拾取：
/// - 玩家靠近后按 F 拾取，金币计入分数并从地图消失（Destroy）。
/// - 为兼容旧关卡（GameScene1），如果配置了胜利 UI（resultPanel 等），拾取时仍会触发原来的胜利流程；
///   若未配置任何胜利 UI（如 GameScene2），则只会安静地加分并消失，不再弹 UI、不再冻结游戏。
/// 提示：Scene2 的胜利判定已改由旗杆触发（见 FlagVictory）。
/// </summary>
public class CoinPickup : MonoBehaviour
{
    #region 可配置参数（保留原字段名，避免破坏 GameScene1 的序列化引用）
    [Header("=== UI元素配置（可选，仅旧关卡胜利流程使用） ===")]
    [Tooltip("胜利提示文本（Victory，需放在结果面板内）")]
    public TextMeshProUGUI victoryText;
    [Tooltip("F键交互提示框（包含F文本的Image）")]
    public GameObject interactTipPanel;
    [Tooltip("胜利结果面板（包含Victory文本+Continue/Quit按钮）")]
    public GameObject resultPanel;
    [Tooltip("结果面板的Continue按钮")]
    public Button continueButton;
    [Tooltip("结果面板的Quit按钮")]
    public Button quitButton;
    [Tooltip("测试用红色UI面板（用于排查显示问题）")]
    public GameObject testUI;

    [Header("=== 交互配置 ===")]
    [Tooltip("触发交互提示的距离阈值（单位：米）")]
    public float interactDistance = 1.5f;

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

    /// <summary>本局（当前场景）累计拾取的金币数量，供 UI / 兑换系统读取。</summary>
    public static int TotalCollected { get; private set; }

    /// <summary>需要时（例如重新开始关卡）可手动清零。</summary>
    public static void ResetTotal() => TotalCollected = 0;

    static CoinPickup()
    {
        // 每次场景加载时把金币计数清零，保证是“本关”的统计
        SceneManager.sceneLoaded += (scene, mode) => TotalCollected = 0;
    }

    private void Awake()
    {
        coinPopOut = GetComponent<CoinPopOut>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        // 初始化所有已配置的 UI 为隐藏（未配置为 null 的直接跳过）
        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (interactTipPanel != null) interactTipPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (testUI != null) testUI.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueButtonClick);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitButtonClick);
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
        if (playerTransform == null) return;

        float distance = Vector2.Distance(transform.position, playerTransform.position);
        isPlayerInRange = distance <= interactDistance;

        // 仅在配置了提示框的旧关卡里显示/隐藏 F 提示；Scene2 未配置则不会弹出任何 UI
        if (interactTipPanel != null)
            interactTipPanel.SetActive(isPlayerInRange);
        if (testUI != null)
            testUI.SetActive(isPlayerInRange);
    }

    private void CollectCoin()
    {
        isCoinCollected = true;

        wasPlayerDead = bunnyController != null && bunnyController.isDead;

        // 计入分数
        TotalCollected += coinValue;

        // 蜂巢金币不能 Destroy：复活时要复位并重新弹出；普通金币维持原行为。
        if (coinPopOut != null)
            gameObject.SetActive(false);
        else
        {
            // Scene2 的普通金币不销毁，而是隐藏，等待玩家死亡后重新出现（胜利除外）。
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            if (bodyCollider != null) bodyCollider.enabled = false;
        }

        if (interactTipPanel != null) interactTipPanel.SetActive(false);
        if (testUI != null) testUI.SetActive(false);

        // 兼容旧关卡：只有当配置了胜利结果面板时，才执行胜利流程（冻结/禁用玩家/弹面板）
        if (resultPanel != null)
        {
            Time.timeScale = 0f;
            if (bunnyController != null) bunnyController.enabled = false;
            if (testUI != null) testUI.SetActive(true);
            resultPanel.SetActive(true);
            if (victoryText != null) victoryText.gameObject.SetActive(true);
        }
    }

    /// <summary>蜂巢重置时调用：如果这枚金币已经被拾取，把它的分数退回。</summary>
    public void RefundCollectedValue()
    {
        if (!isCoinCollected) return;

        TotalCollected = Mathf.Max(0, TotalCollected - coinValue);
        isCoinCollected = false;
    }

    /// <summary>蜂巢重置时调用：清空拾取状态和交互提示，让金币可以再次被拾取。</summary>
    public void ResetPickupState()
    {
        isCoinCollected = false;
        isPlayerInRange = false;

        if (interactTipPanel != null) interactTipPanel.SetActive(false);
        if (testUI != null) testUI.SetActive(false);
    }

    /// <summary>
    /// 普通金币（Scene2）：玩家死亡复活时，若本枚金币已被拾取且尚未胜利，则恢复到原位置并退回分数。
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

    private void OnContinueButtonClick()
    {
        Time.timeScale = 1f;
        string nextSceneName = "GameScene2";
        if (SceneUtility.GetBuildIndexByScenePath(nextSceneName) != -1)
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogWarning("❌ 场景「" + nextSceneName + "」未添加到 Build Settings！");
            Time.timeScale = 1f;
            if (bunnyController != null) bunnyController.enabled = true;
            if (resultPanel != null) resultPanel.SetActive(false);
            if (victoryText != null) victoryText.gameObject.SetActive(false);
            if (testUI != null) testUI.SetActive(false);
        }
    }

    private void OnQuitButtonClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}
