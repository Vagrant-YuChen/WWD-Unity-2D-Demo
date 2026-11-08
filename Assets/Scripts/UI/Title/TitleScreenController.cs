using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

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

        [Header("关卡选择焦点指示（可留空：留空则运行时按主指针自动生成）")]
        public RectTransform levelPointer;
        public RectTransform[] levelButtonRects;

        [Header("关卡数字贴图 3/4/5（可留空：编辑器下会自动从 Assets 加载）")]
        public Sprite level3NumberSprite;
        public Sprite level4NumberSprite;
        public Sprite level5NumberSprite;

        // 关卡滚动窗口（运行时构建）的引用
        private ScrollRect _levelScroll;
        private RectTransform _levelViewport;
        private RectTransform _levelContent;
        private Button[] _levelButtons;
        private Vector2[] _levelButtonBaseSize;
        private Vector3[] _levelButtonBaseScale;
        private GameObject _lastLevelSelection;
        private float _referenceButtonHeight = 56f;
        private ScrollDragGuard _scrollDragGuard;

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
            {
                _referenceButtonY = reference.anchoredPosition.y;
                _referenceButtonHeight = reference.rect.height * Mathf.Abs(reference.localScale.y);
            }

            // 手动搭建的面板可能忘了把返回按钮拖到 Inspector 上，
            // 运行时在对应面板里补找一次，避免 Back 点了没反应。
            if (levelBackButton == null && levelSelectPanel != null)
                levelBackButton = FindBackButton(levelSelectPanel.transform);
            if (settingsBackButton == null && settingsPanel != null)
                settingsBackButton = FindBackButton(settingsPanel.transform);

            if (startButton != null) startButton.onClick.AddListener(OnStart);
            if (levelSelectButton != null) levelSelectButton.onClick.AddListener(OnLevelSelect);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);

            if (level1Button != null) level1Button.onClick.AddListener(() => OnLevelClicked(level1Scene));
            if (level2Button != null) level2Button.onClick.AddListener(() => OnLevelClicked(level2Scene));
            if (levelBackButton != null) levelBackButton.onClick.AddListener(OnBack);
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnBack);

            SetupLevelPointer();
        }

        // 构建关卡滚动窗口（Viewport + Content + VerticalScrollbar）：
        // 把已有 Level1/Level2 原样移入窗口，并按 Level1 的样式复制出 Level3/4/5；
        // 不修改场景里其它已经配置好的内容。
        private void SetupLevelPointer()
        {
            if (levelSelectPanel == null || level1Button == null) return;

            var panelRt = (RectTransform)levelSelectPanel.transform;
            var l1 = (RectTransform)level1Button.transform;
            var l2 = level2Button != null ? (RectTransform)level2Button.transform : null;

            Vector2 buttonSize = l1.rect.size;
            Vector3 buttonScale = l1.localScale;
            float firstY = l1.anchoredPosition.y;
            float spacing = l2 != null
                ? Mathf.Abs(l1.anchoredPosition.y - l2.anchoredPosition.y)
                : buttonSize.y * Mathf.Abs(buttonScale.y) + 14f;

            float panelW = panelRt.rect.width;
            float panelH = panelRt.rect.height;
            float panelLeft = -panelW * 0.5f;
            float panelRight = panelW * 0.5f;

            // 视口上下边界：上不越过标题，下不越过 Back。
            float viewportTop = panelH * 0.5f - 40f;
            float viewportBottom = -panelH * 0.5f + 40f;
            foreach (Transform child in panelRt)
            {
                if (child.name.StartsWith("Title"))
                {
                    var crt = (RectTransform)child;
                    viewportTop = crt.anchoredPosition.y - crt.rect.height * Mathf.Abs(crt.localScale.y) * 0.5f - 4f;
                    break;
                }
            }
            if (levelBackButton != null)
            {
                var brt = (RectTransform)levelBackButton.transform;
                viewportBottom = brt.anchoredPosition.y + brt.rect.height * Mathf.Abs(brt.localScale.y) * 0.5f + 4f;
            }

            // 右侧竖直滚动条（位置保证在 panel 内部）
            const float scrollbarW = 16f;
            float scrollbarX = panelRight - 6f - scrollbarW * 0.5f;
            float viewportLeft = panelLeft + 5f;
            float viewportRight = scrollbarX - scrollbarW * 0.5f - 6f;

            float viewportCenterX = (viewportLeft + viewportRight) * 0.5f;
            float viewportCenterY = (viewportTop + viewportBottom) * 0.5f;
            float viewportW = viewportRight - viewportLeft;
            float viewportH = viewportTop - viewportBottom;

            const int totalLevels = 5;
            float halfButtonH = buttonSize.y * Mathf.Abs(buttonScale.y) * 0.5f;
            float firstCenterFromTop = viewportTop - firstY;
            float contentHeight = firstCenterFromTop + spacing * (totalLevels - 1) + halfButtonH + 6f;
            if (contentHeight < viewportH) contentHeight = viewportH;
            float childX = -viewportCenterX; // 让关卡仍在 panel 的水平中间

            // 滚动的根：透明 Image 负责接收滚轮/拖拽事件
            var scrollRt = NewRect("LevelScroll", panelRt);
            SetCenterRect(scrollRt, new Vector2(viewportCenterX, viewportCenterY), new Vector2(viewportW, viewportH));
            var scrollBg = scrollRt.gameObject.AddComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0f);
            scrollBg.raycastTarget = true;

            _levelScroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _levelScroll.horizontal = false;
            _levelScroll.vertical = true;
            _levelScroll.movementType = ScrollRect.MovementType.Clamped;
            _levelScroll.scrollSensitivity = 25f;
            _scrollDragGuard = scrollRt.gameObject.AddComponent<ScrollDragGuard>();

            var viewportRt = NewRect("Viewport", scrollRt);
            StretchRect(viewportRt);
            viewportRt.gameObject.AddComponent<RectMask2D>();
            _levelViewport = viewportRt;

            var contentRt = NewRect("Content", viewportRt);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, contentHeight);
            _levelContent = contentRt;

            _levelScroll.viewport = viewportRt;
            _levelScroll.content = contentRt;

            // 竖直滚动条：轨道透明、手柄是运行时生成的圆头胶囊
            var sbRt = NewRect("VerticalScrollbar", panelRt);
            SetCenterRect(sbRt, new Vector2(scrollbarX, viewportCenterY), new Vector2(scrollbarW, viewportH));
            var sbBg = sbRt.gameObject.AddComponent<Image>();
            sbBg.color = new Color(1f, 1f, 1f, 0f);
            sbBg.raycastTarget = true;

            var scrollbar = sbRt.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var noNav = scrollbar.navigation;
            noNav.mode = Navigation.Mode.None;
            scrollbar.navigation = noNav;

            var handleRt = NewRect("Handle", sbRt);
            StretchRect(handleRt);
            var handleImg = handleRt.gameObject.AddComponent<Image>();
            handleImg.sprite = CreateRoundedPillSprite((int)scrollbarW, 60, new Color(0.62f, 0.62f, 0.62f, 1f));
            handleImg.type = Image.Type.Sliced;
            handleImg.raycastTarget = true;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;
            scrollbar.size = Mathf.Clamp01(viewportH / contentHeight);
            scrollbar.value = 1f;

            _levelScroll.verticalScrollbar = scrollbar;
            _levelScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // 把已有两个关卡移入 Content，并补出 Level3/4/5（样式完全复制 Level1）
            var levels = new List<Button>(totalLevels);
            levels.Add(level1Button);
            if (level2Button != null) levels.Add(level2Button);
            for (int i = levels.Count; i < totalLevels; i++)
            {
                var clone = Instantiate(level1Button, contentRt);
                clone.name = "Button_Level" + (i + 1);
                ApplyLevelNumberSprite(clone, i + 1);
                levels.Add(clone);
            }

            for (int i = 0; i < levels.Count; i++)
            {
                var rt = (RectTransform)levels[i].transform;
                rt.SetParent(contentRt, false);
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = buttonSize;
                rt.localScale = buttonScale;
                float panelY = firstY - spacing * i;
                rt.anchoredPosition = new Vector2(childX, panelY - viewportTop);
            }

            var ordered = new List<Button>(levels.Count + 1);
            ordered.AddRange(levels);
            if (levelBackButton != null) ordered.Add(levelBackButton);
            _levelButtons = ordered.ToArray();
            SetupLevelNavigation();

            _levelButtonBaseSize = new Vector2[_levelButtons.Length];
            _levelButtonBaseScale = new Vector3[_levelButtons.Length];
            levelButtonRects = new RectTransform[_levelButtons.Length];
            for (int i = 0; i < _levelButtons.Length; i++)
            {
                var brt = (RectTransform)_levelButtons[i].transform;
                levelButtonRects[i] = brt;
                _levelButtonBaseSize[i] = brt.rect.size;
                _levelButtonBaseScale[i] = brt.localScale;
            }

            // 关卡指针：用主指针克隆，挂在 panel 最上层，不会被视口裁剪
            if (levelPointer == null && pointer != null)
            {
                levelPointer = Instantiate(pointer);
                levelPointer.name = "LevelPointer";
                levelPointer.SetParent(panelRt, false);
            }
            if (levelPointer != null)
            {
                var img = levelPointer.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
                levelPointer.SetAsLastSibling();
                levelPointer.gameObject.SetActive(false);
            }

            if (_levelScroll != null) _levelScroll.verticalNormalizedPosition = 1f;
        }

        // 上/下键在 [Level1..Level5, Back] 之间循环：
        // Level5 ↓ → Back，Back ↓ → Level1；Back ↑ → Level5。
        private void SetupLevelNavigation()
        {
            if (_levelButtons == null) return;
            int n = _levelButtons.Length;
            for (int i = 0; i < n; i++)
            {
                var b = _levelButtons[i];
                if (b == null) continue;
                var nav = b.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = _levelButtons[(i - 1 + n) % n];
                nav.selectOnDown = _levelButtons[(i + 1) % n];
                nav.selectOnLeft = null;
                nav.selectOnRight = null;
                b.navigation = nav;
            }
        }

        private void ApplyLevelNumberSprite(Button button, int number)
        {
            var sprite = ResolveLevelNumberSprite(number);
            if (sprite == null) return;
            var num = FindNumberImage(button.transform);
            if (num == null) return;
            num.sprite = sprite;
            num.name = "GameUI_" + number;
        }

        // 关卡按钮里：底框(Button_Level*)、关卡图标(GameUI_Level)之外的图片就是数字。
        private static Image FindNumberImage(Transform button)
        {
            for (int i = 0; i < button.childCount; i++)
            {
                var child = button.GetChild(i);
                if (child.name.StartsWith("Button_Level")) continue;
                if (child.name == "GameUI_Level") continue;
                var img = child.GetComponent<Image>();
                if (img != null) return img;
            }
            return null;
        }

        private Sprite ResolveLevelNumberSprite(int number)
        {
            switch (number)
            {
                case 3: if (level3NumberSprite != null) return level3NumberSprite; break;
                case 4: if (level4NumberSprite != null) return level4NumberSprite; break;
                case 5: if (level5NumberSprite != null) return level5NumberSprite; break;
            }
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Texture 2D/GameUI_" + number + ".png");
#else
            return null;
#endif
        }

        private void EnsureVisible(RectTransform target)
        {
            if (_levelScroll == null || _levelContent == null || _levelViewport == null) return;
            float viewportH = _levelViewport.rect.height;
            float range = _levelContent.rect.height - viewportH;
            if (range <= 0.01f) return;

            float s = (1f - _levelScroll.verticalNormalizedPosition) * range;
            float centerFromTop = -target.anchoredPosition.y;
            float halfH = target.rect.height * Mathf.Abs(target.localScale.y) * 0.5f;
            float top = centerFromTop - halfH;
            float bottom = centerFromTop + halfH;

            if (top < s) s = top;
            else if (bottom > s + viewportH) s = bottom - viewportH;
            s = Mathf.Clamp(s, 0f, range);
            _levelScroll.verticalNormalizedPosition = 1f - s / range;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void SetCenterRect(RectTransform rt, Vector2 anchoredPosition, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
        }

        private static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 运行时生成一个两头圆润的胶囊贴图（9 宫格拉伸，中间可随滚动条长度变化）。
        private static Sprite CreateRoundedPillSprite(int width, int height, Color color)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float r = width * 0.5f;
            float cx = width * 0.5f;
            float y0 = r;
            float y1 = height - r;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cy = Mathf.Clamp(py, y0, y1);
                    float dx = px - cx;
                    float dy = py - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - dist);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();

            var border = new Vector4(0f, r, 0f, r);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = "RoundedScrollHandle";
            return sprite;
        }

        private static Button FindBackButton(Transform panelRoot)
        {
            if (panelRoot == null) return null;
            var buttons = panelRoot.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
                if (b.name == "Button_Back") return b;
            foreach (var b in buttons)
                if (b.name.IndexOf("Back", System.StringComparison.OrdinalIgnoreCase) >= 0) return b;
            return null;
        }

        private void OnEnable()
        {
            HidePanels();
            SelectDefault();
        }

        private void LateUpdate()
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
            var es = EventSystem.current;
            if (es == null) return;
            var selected = es.currentSelectedGameObject;

            UpdateMenuPointer(selected);
            UpdateLevelPointer(selected);
        }

        private void UpdateMenuPointer(GameObject selected)
        {
            if (pointer == null || menuButtonRects == null || menuButtonRects.Length == 0) return;
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

        // 关卡面板内：指针跟随当前选中的关卡/返回按钮。
        // 用 panel 局部坐标定位，因此内容滚动时指针会跟着按钮一起移动；
        // 并按按钮实际显示尺寸（含缩放）适配，指到更小的 Back 时指针会自动缩小。
        private void UpdateLevelPointer(GameObject selected)
        {
            if (levelPointer == null) return;
            if (levelSelectPanel == null || !levelSelectPanel.activeSelf) return;
            if (selected == null || _levelButtons == null) return;

            for (int i = 0; i < _levelButtons.Length; i++)
            {
                var btn = _levelButtons[i];
                if (btn == null) continue;
                var rt = (RectTransform)btn.transform;
                if (rt.gameObject != selected) continue;

                bool isLevel = i < _levelButtons.Length - 1; // 最后一个是 Back

                // 选中关卡变化时先把滚动位置调整好，再计算指针位置。
                if (isLevel && selected != _lastLevelSelection)
                    EnsureVisible(rt);

                Vector2 panelPos = levelSelectPanel.transform.InverseTransformPoint(rt.position);

                // 若该关卡被滚出视口，隐藏指针，避免指针飘到窗口外。
                if (isLevel && _levelViewport != null)
                {
                    Vector2 vpPanel = levelSelectPanel.transform.InverseTransformPoint(_levelViewport.position);
                    float halfVp = _levelViewport.rect.height * 0.5f;
                    if (panelPos.y < vpPanel.y - halfVp || panelPos.y > vpPanel.y + halfVp)
                    {
                        if (levelPointer.gameObject.activeSelf) levelPointer.gameObject.SetActive(false);
                        _lastLevelSelection = selected;
                        return;
                    }
                }

                Vector2 baseSize = (_levelButtonBaseSize != null && i < _levelButtonBaseSize.Length)
                    ? _levelButtonBaseSize[i]
                    : rt.rect.size;
                Vector3 baseScale = (_levelButtonBaseScale != null && i < _levelButtonBaseScale.Length)
                    ? _levelButtonBaseScale[i]
                    : rt.localScale;

                float buttonWidth = baseSize.x * Mathf.Abs(baseScale.x);
                float buttonHeight = baseSize.y * Mathf.Abs(baseScale.y);
                float scale = _referenceButtonHeight > 0.01f ? buttonHeight / _referenceButtonHeight : 1f;
                float pointerWidth = levelPointer.rect.width * scale;
                const float gap = 7f;

                float x = panelPos.x - buttonWidth * 0.5f - gap - pointerWidth * 0.5f;
                float y = panelPos.y;

                levelPointer.localScale = new Vector3(scale, scale, 1f);
                levelPointer.anchoredPosition = new Vector2(x, y);
                if (!levelPointer.gameObject.activeSelf) levelPointer.gameObject.SetActive(true);

                _lastLevelSelection = selected;
                return;
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

            // 打开关卡面板时回到顶部并默认选中第一关，让指针立刻有指向目标。
            if (panel == levelSelectPanel)
            {
                if (_levelScroll != null) _levelScroll.verticalNormalizedPosition = 1f;
                _lastLevelSelection = null;
                if (level1Button != null) Select(level1Button);
            }
            else if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            UpdatePointerPosition();
        }

        public void HidePanels()
        {
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        // 拖动滚动列表松手时，避免误触关卡按钮的点击。
        private void OnLevelClicked(string sceneName)
        {
            if (_scrollDragGuard != null && _scrollDragGuard.DraggedRecently) return;
            LoadLevel(sceneName);
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

    // 记录滚动窗口是否刚被拖动过：拖动松手时不要误触关卡按钮的点击。
    internal class ScrollDragGuard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private float _lastDragTime = -999f;

        public bool DraggedRecently => Time.unscaledTime - _lastDragTime < 0.25f;

        public void OnBeginDrag(PointerEventData eventData) => _lastDragTime = Time.unscaledTime;
        public void OnDrag(PointerEventData eventData) => _lastDragTime = Time.unscaledTime;
        public void OnEndDrag(PointerEventData eventData) => _lastDragTime = Time.unscaledTime;
    }
}
