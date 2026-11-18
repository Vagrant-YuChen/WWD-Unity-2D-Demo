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

    [Header("=== 胜利音效（想调音量就在这里，不在 ResultPanel 上）===")]
    [Tooltip("胜利音效：胜利时全局静音后播出一次，播完即止（不循环）")]
    [SerializeField] private AudioClip victorySound;
    [Tooltip("胜利音效音量（0~1）。最终音量 = 此值 × 【音效】滑条的全局倍率。觉得太大声就把这里调低。")]
    [SerializeField, Range(0f, 1f)] private float victorySoundVolume = 0.5f;

    private bool won;
    private AudioSource victorySource;

    public static bool HasVictory { get; private set; }

    static FlagVictory()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
        {
            HasVictory = false;
            // 场景切换后必须解除全局静音，否则新场景将没有声音。
            AudioListener.pause = false;
        };
    }

    private void Awake()
    {
        // 场景里若未手动连线（例如 Scene2 早期版本），自动按名称查找 ResultPanel
        // 以及其中的 Continue / Quit 按钮，避免因为序列化引用丢失导致胜利不触发。
        ResolveUiReferences();

        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueButtonClick);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitButtonClick);
    }

    private void ResolveUiReferences()
    {
        // 1. 自动查找结果面板（含未激活对象）
        if (resultPanel == null)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                var go = t.gameObject;
                if (go.scene != gameObject.scene) continue;
                if (go.name != "ResultPanel") continue;
                resultPanel = go;
                break;
            }
        }

        if (resultPanel == null) return;

        // 2. 自动查找面板内的 Continue / Quit 按钮
        var buttons = resultPanel.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            string n = b.gameObject.name.ToLowerInvariant();
            if (continueButton == null && (n.Contains("continue") || n.Contains("resume") || n.Contains("next")))
            {
                continueButton = b;
                continue;
            }
            if (quitButton == null && (n.Contains("main") || n.Contains("quit") || n.Contains("exit") || n.Contains("menu")))
            {
                quitButton = b;
            }
        }
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
        HasVictory = true;

        // 胜利：先全局静音（暂停所有音源），再用一条忽略暂停的声道播放一次性胜利音效。
        PlayVictorySound();

        // 本关通关结算：把本局金币并入存档总额（按“每关最高已入账数”只补差额，杜绝重复刷）。
        CoinWallet.BankCurrentLevel();

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

    private void PlayVictorySound()
    {
        AudioListener.pause = true;

        if (victorySound == null) return;

        if (victorySource == null)
        {
            victorySource = gameObject.AddComponent<AudioSource>();
            victorySource.playOnAwake = false;
            victorySource.loop = false;
            victorySource.spatialBlend = 0f;
            victorySource.dopplerLevel = 0f;
            victorySource.ignoreListenerPause = true;
        }

        victorySource.Stop();
        victorySource.PlayOneShot(victorySound, victorySoundVolume * GameAudioManager.SfxMultiplier);
    }

    private void StopVictorySound()
    {
        if (victorySource != null) victorySource.Stop();
        AudioListener.pause = false;
    }

    private void OnContinueButtonClick()
    {
        StopVictorySound();
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
        StopVictorySound();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
