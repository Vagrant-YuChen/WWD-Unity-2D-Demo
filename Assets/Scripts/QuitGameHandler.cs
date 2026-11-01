using UnityEngine;

public class QuitGameHandler : MonoBehaviour
{
    // 点击按钮时执行的“退出游戏”方法
    public void QuitGame()
    {
        // 退出当前应用（打包后生效，编辑器中无反应）
        Application.Quit();
    }
}