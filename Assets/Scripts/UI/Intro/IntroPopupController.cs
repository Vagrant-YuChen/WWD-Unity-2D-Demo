using UnityEngine;
using UnityEngine.UI;

namespace RunningGame.UI
{
    /// <summary>
    /// 关卡开场提示弹窗控制器：在 GameScene1 / GameScene2 打开时显示，
    /// 使用 ControlCardboard 作为面板底图，介绍玩法；点击 Resume 后进入游戏。
    /// </summary>
    public class IntroPopupController : MonoBehaviour
    {
        [Header("弹窗")]
        public GameObject panelRoot;
        public Button resumeButton;

        [Header("设置")]
        public bool pauseGameWhileOpen = true;
        public bool hidePauseEscapeWhileOpen = true;

        private float _savedTimeScale = 1f;
        private bool _isOpen;
        private PauseMenuController _pauseController;

        private void Awake()
        {
            _pauseController = FindObjectOfType<PauseMenuController>();
            if (resumeButton != null)
                resumeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            if (panelRoot != null)
                panelRoot.SetActive(true);
            Show();
        }

        private void Show()
        {
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            if (pauseGameWhileOpen)
                Time.timeScale = 0f;

            _isOpen = true;
            if (panelRoot != null)
                panelRoot.SetActive(true);

            if (hidePauseEscapeWhileOpen && _pauseController != null)
                _pauseController.allowEscape = false;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;

            if (panelRoot != null)
                panelRoot.SetActive(false);

            if (pauseGameWhileOpen)
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;

            if (hidePauseEscapeWhileOpen && _pauseController != null)
                _pauseController.allowEscape = true;

            // 不改动光标：与暂停菜单一致，保持可见/解锁状态。
        }

        private void OnDisable()
        {
            if (_isOpen && pauseGameWhileOpen)
            {
                _isOpen = false;
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
                if (hidePauseEscapeWhileOpen && _pauseController != null)
                    _pauseController.allowEscape = true;
            }
        }
    }
}
