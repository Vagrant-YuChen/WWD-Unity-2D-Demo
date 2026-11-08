using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

/// <summary>
/// 跨关卡金币钱包（本地存档，PlayerPrefs）。
/// 设计目标：不允许反复刷金币——每一关的金币只在“通关结算”时并入存档总额，且每一关最多贡献该关的金币总数。
///
/// 入账口径（高水位）：为每一关记录“已经入账过的最高金币数” LevelBankedAmount_&lt;场景名&gt;。
/// 通关时只把“本局收集数 - 该关已入账数”的正差额并入 Total，并把水位抬到本局收集数。
/// 因此：
///  - 第一次通关收集多少就入账多少；
///  - 某关之前只入账了部分金币（例如先 0 币通关、或漏捡），之后通关可以补齐差额，直到该关金币上限；
///  - 反复重玩同一关不会重复累加（差额为 0），天然被封顶在“每关金币总数”。
///
/// 数据：
///  - Total：已入账的累计金币（存 PlayerPrefs）。
///  - LevelBankedAmount_&lt;场景名&gt;：该关已入账的最高金币数（存 PlayerPrefs）。
/// </summary>
public static class CoinWallet
{
    private const string TotalKey = "WackyWorldDash.TotalGoldenCoins";
    private const string LevelBankedAmountPrefix = "WackyWorldDash.LevelBankedAmount_";
    // 旧版本（布尔“是否已入账”）使用的前缀，仅用于迁移/清理，逻辑上不再依赖它。
    private const string LegacyLevelBankedPrefix = "WackyWorldDash.LevelBanked_";

    private static bool _loaded;
    private static int _total;

    /// <summary>已入账的累计金币总数（读取本地存档）。</summary>
    public static int Total
    {
        get { EnsureLoaded(); return _total; }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _total = Mathf.Max(0, PlayerPrefs.GetInt(TotalKey, 0));
    }

    /// <summary>该关已经入账过的金币最高数量（0 表示还没入账过）。</summary>
    public static int GetBankedAmount(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return 0;
        return PlayerPrefs.GetInt(LevelBankedAmountPrefix + sceneName, 0);
    }

    /// <summary>该关卡是否已经入账过金币。</summary>
    public static bool IsLevelBanked(string sceneName)
    {
        return GetBankedAmount(sceneName) > 0;
    }

    /// <summary>以“当前场景名 + 本局获取的金币数”入账本关（只补差额）。</summary>
    public static bool BankCurrentLevel()
    {
        return BankLevelBest(SceneManager.GetActiveScene().name, CoinPickup.TotalCollected);
    }

    /// <summary>
    /// 高水位入账：把本局收集数与“该关已入账最高数”的正差额并入总额，并抬高水位。
    /// 返回 true 表示本次确实新增了金币入账；若本局收集数没有超过历史水位（无新增），返回 false。
    /// </summary>
    public static bool BankLevelBest(string sceneName, int collectedThisLevel)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(sceneName)) return false;
        if (collectedThisLevel < 0) collectedThisLevel = 0;

        int alreadyBanked = GetBankedAmount(sceneName);
        int delta = collectedThisLevel - alreadyBanked;
        if (delta <= 0) return false;

        _total = Mathf.Max(0, _total + delta);
        PlayerPrefs.SetInt(TotalKey, _total);
        PlayerPrefs.SetInt(LevelBankedAmountPrefix + sceneName, collectedThisLevel);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>清空存档：总额清零，并移除所有关卡入账记录（开新游戏 / 调试用）。</summary>
    public static void ResetAll()
    {
        EnsureLoaded();
        _total = 0;
        PlayerPrefs.DeleteKey(TotalKey);
        CoinPickup.ResetTotal();

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name))
            {
                PlayerPrefs.DeleteKey(LevelBankedAmountPrefix + name);
                PlayerPrefs.DeleteKey(LegacyLevelBankedPrefix + name);
            }
        }

        PlayerPrefs.Save();
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Wacky World Dash/Reset Total Golden Coins")]
    private static void ResetAllMenu()
    {
        ResetAll();
        Debug.Log("[CoinWallet] 已清空存档金币总额与所有关卡入账记录。");
    }

    [UnityEditor.MenuItem("Tools/Wacky World Dash/Debug: Log Coin Wallet State")]
    private static void LogStateMenu()
    {
        EnsureLoaded();
        Debug.Log("[CoinWallet] Total = " + _total
            + " | GameScene1 BankedAmount = " + GetBankedAmount("GameScene1")
            + " | GameScene2 BankedAmount = " + GetBankedAmount("GameScene2"));
    }
#endif
}
