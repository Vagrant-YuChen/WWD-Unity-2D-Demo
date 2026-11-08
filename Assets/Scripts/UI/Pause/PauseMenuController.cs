using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RunningGame.UI
{
    /// <summary>
    /// 暂停菜单控制器：在 GameScene1 / GameScene2 中按 ESC 打开/关闭暂停界面。
    /// 所有交互与动画均使用 unscaled 时间，避免受 timeScale 影响。
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [Header("暂停界面")]
        public GameObject menuRoot;
        public GameObject settingsPanel;
        public Button resumeButton;
        public Button optionsButton;
        public Button mainMenuButton;
        public Button settingsBackButton;

        [Header("焦点指示（可留空）")]
        public RectTransform pointer;
        public RectTransform[] menuButtonRects;

        [Header("设置")]
        public string mainMenuScene = "StartScene";
        public bool allowEscape = true;

        private float _savedTimeScale = 1f;
        private Vector2 _pointerHomePosition;
        private float _referenceButtonY;
        private bool _isPaused;

        private void Awake()
        {
            if (pointer != null)
                _pointerHomePosition = pointer.anchoredPosition;

            var reference = resumeButton != null
                ? (RectTransform)resumeButton.transform
                : (menuButtonRects != null && menuButtonRects.Length > 0 ? menuButtonRects[0] : null);
            if (reference != null)
            {
                _referenceButtonY = reference.anchoredPosition.y;
            }

            if (settingsBackButton == null && settingsPanel != null)
            {
                foreach (var b in settingsPanel.GetComponentsInChildren<Button>(true))
                    if (b.name == "Button_Back") settingsBackButton = b;
            }

            if (resumeButton != null) resumeButton.onClick.AddListener(OnResume);
            if (optionsButton != null) optionsButton.onClick.AddListener(OnOptions);
            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveAllListeners();
                mainMenuButton.onClick.AddListener(OnMainMenu);
            }
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnSettingsBack);

            if (menuRoot != null) menuRoot.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        private void Update()
        {
            if (allowEscape && Input.GetKeyDown(KeyCode.Escape))
            {
                if (settingsPanel != null && settingsPanel.activeSelf)
                {
                    OnSettingsBack();
                }
                else
                {
                    if (_isPaused) OnResume();
                    else OpenPause();
                }
            }

            UpdatePointerPosition();
        }

        private void OpenPause()
        {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _isPaused = true;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (menuRoot != null) menuRoot.SetActive(true);
            HideSettings();
            Select(resumeButton);
            UpdatePointerPosition();
        }

        public void OnResume()
        {
            if (menuRoot != null) menuRoot.SetActive(false);
            HideSettings();
            _isPaused = false;
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        }

        public void OnOptions()
        {
            if (settingsPanel == null) return;
            if (menuRoot != null) menuRoot.SetActive(false);
            settingsPanel.SetActive(true);
        }

        public void OnSettingsBack()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (menuRoot != null) menuRoot.SetActive(true);
            Select(resumeButton);
        }

        public void OnMainMenu()
        {
            _isPaused = false;
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
            if (string.IsNullOrEmpty(mainMenuScene)) return;
            if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
            {
                Debug.LogWarning($"[Pause] 场景 \"{mainMenuScene}\" 未加入 Build Settings，无法加载。");
                return;
            }
            SceneManager.LoadScene(mainMenuScene);
        }

        private void HideSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        private void Select(Button target)
        {
            if (target == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void UpdatePointerPosition()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var selected = es.currentSelectedGameObject;

            if (pointer == null || menuButtonRects == null || menuButtonRects.Length == 0) return;
            if (!menuRoot || !menuRoot.activeSelf) return;
            if (selected == null) return;

            for (int i = 0; i < menuButtonRects.Length; i++)
            {
                var rt = menuButtonRects[i];
                if (rt != null && rt.gameObject == selected)
                {
                    pointer.anchoredPosition = new Vector2(
                        _pointerHomePosition.x,
                        _pointerHomePosition.y + (rt.anchoredPosition.y - _referenceButtonY));
                    if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);
                    return;
                }
            }
        }

        private void OnDisable()
        {
            if (_isPaused)
            {
                _isPaused = false;
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
            }
        }
    }
}
