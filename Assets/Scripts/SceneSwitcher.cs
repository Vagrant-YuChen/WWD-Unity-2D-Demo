using UnityEngine;
using UnityEngine.SceneManagement; // 场景切换必须的命名空间

// 注意：是MonoBehaviour（中间有i）
public class SceneSwitcher : MonoBehaviour
{
    // 点击按钮时要执行的“切换场景”方法（保持旧调用不变）
    public void LoadGameScene()
    {
        LoadScene("GameScene1");
    }

    // 通用：按场景名加载（供标题页/选关/暂停菜单复用）
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"[SceneSwitcher] 场景 \"{sceneName}\" 未加入 Build Settings，无法加载。");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}