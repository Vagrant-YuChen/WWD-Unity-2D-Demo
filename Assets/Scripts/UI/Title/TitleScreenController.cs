using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RunningGame.UI
{
    /// <summary>
    /// 标题页主控制器：负责按钮回调、面板开关、默认选中与键盘/手柄焦点指示。
    /// 所有交互与动画均使用 unscaled 时间，避免受 timeScale 影响。
    /// </summary>
    public class TitleScreenController : MonoBehaviour
    {
        [Header("场景")]
        public string startSceneName = "GameScene1";
        public string level1Scene = "GameScene1";
        public string level2Scene = "GameScene2";

        [Header("主菜单按钮")]
        public Button startButton;
        public Button levelSelectButton;
        public Button settingsButton;
        public Button quitButton;

        [Header("面板（可留空）")]
        public GameObject levelSelectPanel;
        public GameObject settingsPanel;
        public Button level1Button;
        public Button level2Button;
        public Button levelBackButton;
        public Button settingsBackButton;

        [Header("焦点指示（可留空）")]
        public RectTransform pointer;
        public RectTransform[] menuButtonRects;

        // 记录场景里手动摆好的指针位置与起始按钮的 Y，作为运行时定位基准。
        // X 始终锁定在作者设置的位置，Y 只按选中按钮的相对高度移动，
        // 这样每次运行指针都落在设置的位置，而不会被偏移量推到更左边。
        private Vector2 _pointerHomePosition;
        private float _referenceButtonY;

        private void Awake()
        {
            if (pointer != null)
                _pointerHomePosition = pointer.anchoredPosition;

            var reference = startButton != null
                ? (RectTransform)startButton.transform
                : (menuButtonRects != null && menuButtonRects.Length > 0 ? menuButtonRects[0] : null);
            if (reference != null)
                _referenceButtonY = reference.anchoredPosition.y;

            if (startButton != null) startButton.onClick.AddListener(OnStart);
            if (levelSelectButton != null) levelSelectButton.onClick.AddListener(OnLevelSelect);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);

            if (level1Button != null) level1Button.onClick.AddListener(() => LoadLevel(level1Scene));
            if (level2Button != null) level2Button.onClick.AddListener(() => LoadLevel(level2Scene));
            if (levelBackButton != null) levelBackButton.onClick.AddListener(OnBack);
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnBack);
        }

        private void OnEnable()
        {
            HidePanels();
            SelectDefault();
        }

        private void Update()
        {
            UpdatePointerPosition();
        }

        #region 回调
        public void OnStart() => LoadLevel(startSceneName);
        public void OnLevelSelect() => ShowPanel(levelSelectPanel);
        public void OnSettings() => ShowPanel(settingsPanel);
        public void OnQuit() => QuitToDesktop();
        public void OnBack()
        {
            HidePanels();
            SelectDefault();
        }
        #endregion

        private void SelectDefault()
        {
            Select(startButton);
        }

        private void Select(Button target)
        {
            if (target == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void UpdatePointerPosition()
        {
            if (pointer == null || menuButtonRects == null || menuButtonRects.Length == 0) return;
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return;

            for (int i = 0; i < menuButtonRects.Length; i++)
            {
                var rt = menuButtonRects[i];
                if (rt != null && rt.gameObject == es.currentSelectedGameObject)
                {
                    pointer.anchoredPosition = new Vector2(
                        _pointerHomePosition.x,
                        _pointerHomePosition.y + (rt.anchoredPosition.y - _referenceButtonY));
                    if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);
                    return;
                }
            }
        }

        private void ShowPanel(GameObject panel)
        {
            HidePanels();
            if (panel == null)
            {
                Debug.Log("[Title] 该面板尚未配置（将在暂停菜单任务中复用统一面板）。");
                return;
            }
            panel.SetActive(true);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public void HidePanels()
        {
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        private void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[Title] 场景 \"{sceneName}\" 不在 Build Settings，无法加载。");
                return;
            }
            SceneManager.LoadScene(sceneName);
        }

        private void QuitToDesktop()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
