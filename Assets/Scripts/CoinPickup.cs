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
    private bool isPlayerInRange;
    private bool isCoinCollected;
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
        }
    }

    private void Update()
    {
        if (isCoinCollected) return;

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

        // 计入分数并从地图消失
        TotalCollected += coinValue;
        Destroy(gameObject);

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
