using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 所有变量必须在类内部声明！
public class CoinPickup : MonoBehaviour
{
    #region 可配置参数（拖入对应UI元素）
    [Header("=== UI元素配置 ===")]
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
    public GameObject testUI; // ✅ 修正：移到类内部

    [Header("=== 交互配置 ===")]
    [Tooltip("触发交互提示的距离阈值（单位：米）")]
    public float interactDistance = 1.5f;

    [Header("=== 玩家配置 ===")]
    [Tooltip("玩家标签（默认Player）")]
    public string playerTag = "Player";
    #endregion

    #region 私有变量
    private Transform playerTransform;
    private BunnyController bunnyController;
    private bool isPlayerInRange;
    private bool isCoinCollected;
    #endregion

    private void Awake()
    {
        // 初始化所有UI为隐藏（包括测试UI）
        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (interactTipPanel != null) interactTipPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (testUI != null) testUI.SetActive(false); // ✅ 新增：初始化测试UI

        // 绑定按钮事件（加空值判断，避免空引用报错）
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueButtonClick);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitButtonClick);
    }

    private void Start()
    {
        // ✅ 新增调试日志：确认脚本启动
        Debug.Log("=== CoinPickup脚本启动 ===");

        // 查找玩家（加日志验证）
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player != null)
        {
            playerTransform = player.transform;
            bunnyController = player.GetComponent<BunnyController>();
            Debug.Log("找到玩家对象：" + player.name);
        }
        else
        {
            Debug.LogError("❌ 未找到标签为「" + playerTag + "」的玩家！请检查兔子标签。");
        }

        // ✅ 新增日志：验证UI引用是否为空
        Debug.Log("interactTipPanel是否为空：" + (interactTipPanel == null));
        Debug.Log("resultPanel是否为空：" + (resultPanel == null));
        Debug.Log("testUI是否为空：" + (testUI == null));
        Debug.Log("victoryText是否为空：" + (victoryText == null));
    }

    private void Update()
    {
        if (isCoinCollected) return;

        CheckPlayerDistance();

        // 按F拾取（加日志）
        if (isPlayerInRange && Input.GetKeyDown(KeyCode.F))
        {
            Debug.Log("✅ 按下F键，触发拾取硬币");
            CollectCoin();
        }
    }

    #region 核心逻辑
    private void CheckPlayerDistance()
    {
        if (playerTransform == null) return;

        // 计算距离（加日志）
        float distance = Vector2.Distance(transform.position, playerTransform.position);
        isPlayerInRange = distance <= interactDistance;
        Debug.Log($"玩家与硬币距离：{distance:F2} | 阈值：{interactDistance} | 是否在范围内：{isPlayerInRange}");

        // 激活/隐藏F提示框
        if (interactTipPanel != null)
            interactTipPanel.SetActive(isPlayerInRange);

        // ✅ 新增：激活/隐藏测试UI（核心！）
        if (testUI != null)
            testUI.SetActive(isPlayerInRange);
    }

    private void CollectCoin()
    {
        isCoinCollected = true;
        Destroy(gameObject);

        Time.timeScale = 0f;
        if (bunnyController != null) bunnyController.enabled = false;

        // 隐藏F提示框，强制显示测试UI和结果面板
        if (interactTipPanel != null) interactTipPanel.SetActive(false);
        if (testUI != null) testUI.SetActive(true); // ✅ 强制显示测试UI
        if (resultPanel != null) resultPanel.SetActive(true);
        if (victoryText != null) victoryText.gameObject.SetActive(true);
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
            Debug.LogWarning("❌ 场景「" + nextSceneName + "」未添加到Build Settings！");
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
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}