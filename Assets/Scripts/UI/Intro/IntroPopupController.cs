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

        /// <summary>弹窗关闭（玩家点击 Resume 正式进入游戏）时触发，供镜头开场序列等外部系统监听。</summary>
        public event System.Action Closed;

        /// <summary>弹窗当前是否处于打开（冻结）状态。</summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// 因按下"空格"而关闭弹窗时记录的帧号。角色会忽略这一帧的空格，
        /// 避免"用空格开始游戏"导致角色开局自动跳一次。
        /// </summary>
        public static int SpaceResumeFrame { get; private set; } = int.MinValue;

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

            // 通知外部系统（如开场镜头序列）游戏已正式进入
            Closed?.Invoke();
        }

        private void Update()
        {
            // 弹窗打开时，按回车 / 小键盘回车 / 空格 都等同于点击 Resume。
            if (!_isOpen) return;

            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool space = Input.GetKeyDown(KeyCode.Space);
            if (!enter && !space) return;

            // 记录这一帧是"空格"关闭的，让角色忽略这次空格，避免开局自动跳。
            if (space) SpaceResumeFrame = Time.frameCount;

            Close();
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
