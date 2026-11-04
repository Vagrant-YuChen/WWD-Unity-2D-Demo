using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 旗帜/旗杆终点触发：玩家碰到旗杆的 Trigger 即判定本关胜利。
/// 逻辑移植自原金币脚本（CoinPickup）中的胜利部分：
/// 冻结时间、禁用玩家控制、显示胜利结果面板。
/// - 若配置了胜利结果面板（resultPanel）：触发正式的胜利结算（冻结 + 显示面板 + Continue/Quit）。
/// - 若未配置任何胜利 UI（例如暂未接入 UI 的关卡）：不会冻结游戏，避免把玩家永久卡住。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class FlagVictory : MonoBehaviour
{
    [Header("=== UI元素配置（可选） ===")]
    [Tooltip("胜利提示文本（Victory，放在结果面板内）")]
    public TextMeshProUGUI victoryText;
    [Tooltip("胜利结果面板（包含 Victory 文本 + Continue/Quit 按钮）")]
    public GameObject resultPanel;
    [Tooltip("结果面板的 Continue 按钮")]
    public Button continueButton;
    [Tooltip("结果面板的 Quit 按钮")]
    public Button quitButton;

    [Header("=== 触发配置 ===")]
    [Tooltip("玩家标签（默认 Player）")]
    public string playerTag = "Player";
    [Tooltip("重玩/继续时加载的场景名")]
    public string reloadSceneName = "GameScene2";

    private bool won;

    private void Awake()
    {
        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueButtonClick);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitButtonClick);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (won) return;
        if (!other.CompareTag(playerTag)) return;

        TriggerVictory(other.gameObject);
    }

    private void TriggerVictory(GameObject player)
    {
        won = true;

        // 胜利后停止蜂群；即使没有配置胜利面板，也不让蜜蜂继续追。
        BeeSwarmController beeSwarm = FindObjectOfType<BeeSwarmController>();
        if (beeSwarm != null) beeSwarm.StopSwarm();

        // 未接入胜利面板时不冻结游戏，避免把玩家卡住；接入面板后自动变为正式结算
        if (resultPanel == null) return;

        Time.timeScale = 0f;
        var bunny = player.GetComponent<BunnyController>();
        if (bunny != null) bunny.enabled = false;

        resultPanel.SetActive(true);
        if (victoryText != null) victoryText.gameObject.SetActive(true);
    }

    private void OnContinueButtonClick()
    {
        Time.timeScale = 1f;
        if (SceneUtility.GetBuildIndexByScenePath(reloadSceneName) != -1)
        {
            SceneManager.LoadScene(reloadSceneName);
        }
        else
        {
            Debug.LogWarning("❌ 场景「" + reloadSceneName + "」未添加到 Build Settings！");
            Time.timeScale = 1f;
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
}
