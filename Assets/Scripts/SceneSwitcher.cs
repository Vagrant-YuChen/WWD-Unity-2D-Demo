using UnityEngine;
using UnityEngine.SceneManagement; // 场景切换必须的命名空间

// 注意：是MonoBehaviour（中间有i）
public class SceneSwitcher : MonoBehaviour
{
    // 点击按钮时要执行的“切换场景”方法
    public void LoadGameScene()
    {
        // 加载名为“GameScene1”的场景（必须和你实际的场景名完全一致）
        SceneManager.LoadScene("GameScene1");
    }
}