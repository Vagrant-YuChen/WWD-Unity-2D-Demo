using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RunningGame.UI;

namespace RunningGame.EditorTools
{
    /// <summary>
    /// 一键重建标题界面。运行后会清空 StartScene 的 Canvas 子物体，
    /// 按当前实际使用的「左侧角色 + 标题图 + 右侧图标按钮」布局重新生成，
    /// 并挂载运行时脚本、接线。素材均来自 Assets/Texture 2D 与 Assets/Sprite。
    /// 菜单：Tools -> Wacky World Dash -> Rebuild Title Screen
    /// </summary>
    public static class TitleScreenSetup
    {
        private const string ScenePath = "Assets/Scenes/StartScene.unity";

        private const string BgSprite = "Assets/Texture 2D/pic0.png";
        private const string HeroSprite = "Assets/Sprite/PlayLocal6.png";
        private const string TitleArtSprite = "Assets/Texture 2D/GameTitle_Wacky World Dash.png";
        private const string ScreensArtSprite = "Assets/Texture 2D/GameUI_Screens.png";
        private const string RoundedBgSprite = "Assets/Texture 2D/roundedbg.png";
        private const string ButtonSprite = "Assets/Texture 2D/button.png";
        private const string ButtonHoverSprite = "Assets/Texture 2D/button hover.png";
        private const string StartHoverSprite = "Assets/Sprite/Scorecard-Scorepaper.000.png";
        private const string ArrowSprite = "Assets/Texture 2D/ArrowPointer.png";
        private const string PanelWindowSprite = "Assets/Texture 2D/window.png";

        private const string IconStart = "Assets/Texture 2D/GameUI_Start.png";
        private const string IconHappyFace = "Assets/Texture 2D/GameUI_HappyFace.png";
        private const string IconLevelSelect = "Assets/Texture 2D/GameUI_LevelSelect.png";
        private const string IconSaveMark = "Assets/Texture 2D/GameUI_SaveMark.png";
        private const string IconOptions = "Assets/Texture 2D/GameUI_Options.png";
        private const string IconOptionsMark = "Assets/Sprite/Options.png";
        private const string IconQuit = "Assets/Texture 2D/GameUI_Quit.png";
        private const string IconQuitMark = "Assets/Texture 2D/GameUI_CrossMark.png";

        private const string FontTitle = "Assets/Font/KGBlankSpaceSolid SDF.asset";
        private const string FontButton = "Assets/Font/Always Together SDF.asset";

        private static readonly Color BgTint = new Color(0.6905185f, 0.8679245f, 0.8399466f, 1f);
        private static readonly Color Gold = new Color(1f, 0.847f, 0.349f, 1f);
        private static readonly Color TextDark = new Color(0.231f, 0.180f, 0.141f, 1f);
        private static readonly Color FooterColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ButtonFrame = new Color(0.2784314f, 0.49019608f, 0.3529412f, 0.9019608f);
        private static readonly Color ButtonFill = new Color(0.5803922f, 0.84705883f, 0.654902f, 1f);
        private static readonly Color HappyTint = new Color(0.9137255f, 0.47058824f, 0.47058824f, 1f);
        private static readonly Color SaveTint = new Color(0.6156863f, 0.53333336f, 0.9019608f, 1f);
        private static readonly Color OptionsTint = new Color(1f, 0.9605225f, 0.83962256f, 1f);
        private static readonly Color OptionsMarkTint = new Color(0.6792453f, 0.6044856f, 0.6044856f, 1f);
        private static readonly Color QuitTint = new Color(0.9411765f, 0.6117647f, 0.40784314f, 1f);

        [MenuItem("Tools/Wacky World Dash/Rebuild Title Screen")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("标题页重建", "请先退出 Play 模式再执行。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var canvas = FindCanvas();
            if (canvas == null)
            {
                Debug.LogError("[TitleScreenSetup] 找不到 Canvas，请确认 StartScene 已打开且存在 Canvas。");
                return;
            }
            EnsureEventSystem();
            ConfigureCanvas(canvas);

            var canvasT = canvas.transform;
            for (int i = canvasT.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(canvasT.GetChild(i).gameObject);

            var fontTitle = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontTitle);
            var fontButton = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontButton);
            var spBg = LoadSprite(BgSprite);
            var spHero = LoadSprite(HeroSprite);
            var spTitleArt = LoadSprite(TitleArtSprite);
            var spScreensArt = LoadSprite(ScreensArtSprite);
            var spRounded = LoadSprite(RoundedBgSprite);
            var spButton = LoadSprite(ButtonSprite);
            var spButtonHover = LoadSprite(ButtonHoverSprite) ?? spButton;
            var spStartHover = LoadSprite(StartHoverSprite) ?? spButtonHover;
            var spArrow = LoadSprite(ArrowSprite);

            var spPanel = AssetDatabase.LoadAssetAtPath<Sprite>(PanelWindowSprite);
            if (spPanel == null)
            {
                spPanel = spRounded;
                Debug.LogWarning("[TitleScreenSetup] window.png 已不存在，面板背景回退为 roundedbg.png。");
            }

            var spIconStart = LoadSprite(IconStart);
            var spIconHappy = LoadSprite(IconHappyFace);
            var spIconLevel = LoadSprite(IconLevelSelect);
            var spIconSave = LoadSprite(IconSaveMark);
            var spIconOptions = LoadSprite(IconOptions);
            var spIconOptionsMark = LoadSprite(IconOptionsMark);
            var spIconQuit = LoadSprite(IconQuit);
            var spIconQuitMark = LoadSprite(IconQuitMark);

            // 根节点
            var root = NewUI("TitleRoot", canvasT);
            StretchFull(root);

            // 背景
            var bg = NewImage("Background", root, spBg, false);
            bg.color = BgTint;
            bg.type = Image.Type.Simple;
            bg.preserveAspect = false;
            StretchFull(bg.rectTransform);

            // 左区角色：PlayLocal6 站立图
            var hero = NewImage("Hero", root, spHero, false);
            hero.type = Image.Type.Simple;
            hero.preserveAspect = true;
            Place(hero.rectTransform, -250f, -58f, 220f, 240f);
            var idle = hero.gameObject.AddComponent<IdleBreathe>();
            idle.bobAmplitude = 7f;
            idle.bobSpeed = 1.5f;
            idle.squashAmount = 0.025f;
            idle.squashSpeed = 1.6f;

            // 右区菜单容器
            var menu = NewUI("MenuLayer", root);
            StretchFull(menu);
            menu.anchoredPosition = new Vector2(0f, -5.5f);
            menu.sizeDelta = new Vector2(0f, 11f);

            // 菜单底图与标题图
            var screensArt = NewImage("GameUI_Screens", menu, spScreensArt, true);
            screensArt.type = Image.Type.Simple;
            Place(screensArt.rectTransform, 192f, -71f, 100f, 100f);
            screensArt.rectTransform.localScale = new Vector3(3f, 3f, 0.3125f);

            var titleArt = NewImage("GameTitle_Wacky World Dash", menu, spTitleArt, true);
            titleArt.type = Image.Type.Simple;
            Place(titleArt.rectTransform, 194f, 152.1f, 85f, 75f);
            titleArt.rectTransform.localScale = new Vector3(3f, 2f, 0.3125f);

            // 4 个主按钮：深色外框 + 浅色内底 + 图形图标
            var startBtn = NewIconButton("Button_Start", menu, 200f, 31f, spRounded, spStartHover, spStartHover);
            AddIcon(startBtn.transform, "GameUI_Start", spIconStart, Color.white, 24.9f, 2.6f, 5f, 2.5f, 20f, 20f);
            AddIcon(startBtn.transform, "GameUI_HappyFace", spIconHappy, HappyTint, -81.1f, 2.6f, 90f, 90f, 0.45f, 0.45f);

            var levelBtn = NewIconButton("Button_LevelSelect", menu, 200f, -38f, spRounded, spButtonHover, spButton);
            AddIcon(levelBtn.transform, "GameUI_LevelSelect", spIconLevel, Color.white, 23.6f, 1.6f, 22f, 7.5f, 6.25f, 6.25f);
            AddIcon(levelBtn.transform, "GameUI_SaveMark", spIconSave, SaveTint, -83.4f, 2.6f, 90f, 90f, 0.5f, 0.5f);

            var optionsBtn = NewIconButton("Button_Options", menu, 200f, -106f, spRounded, spButtonHover, spButton);
            AddIcon(optionsBtn.transform, "GameUI_Options", spIconOptions, OptionsTint, 24.5f, 3.6f, 97f, 92f, 0.9f, 0.55f);
            AddIcon(optionsBtn.transform, "GameUI_OptionsMark", spIconOptionsMark, OptionsMarkTint, -82.2f, 2.6f, 90f, 90f, 0.45f, 0.45f);

            var quitBtn = NewIconButton("Button_Quit", menu, 200f, -173f, spRounded, spButtonHover, spButton);
            AddIcon(quitBtn.transform, "GameUI_Quit", spIconQuit, Color.white, 27.4f, 3.3f, 100f, 90f, 1f, 0.55f);
            AddIcon(quitBtn.transform, "GameUI_QuitMark", spIconQuitMark, QuitTint, -83.9f, 2.4f, 90f, 90f, 0.45f, 0.45f);

            // 焦点箭头
            var pointer = NewImage("Pointer", menu, spArrow, false);
            pointer.type = Image.Type.Simple;
            pointer.preserveAspect = true;
            Place(pointer.rectTransform, 66.8f, 33.9f, 44f, 44f);

            // 音效与按钮手感
            var sfxGo = NewUI("UiSfx", root).gameObject;
            var sfx = sfxGo.AddComponent<UiSfxPlayer>();

            AddJuice(startBtn, sfx);
            AddJuice(levelBtn, sfx);
            AddJuice(optionsBtn, sfx);
            AddJuice(quitBtn, sfx);

            // 入场演出（标题现在是图片，因此不绑定 logo 缩放）
            var reveal = menu.gameObject.AddComponent<PlaySequenceReveal>();
            reveal.logo = null;
            reveal.logoStartScale = 0.8f;

            var btnRects = new[]
            {
                startBtn.GetComponent<RectTransform>(),
                levelBtn.GetComponent<RectTransform>(),
                optionsBtn.GetComponent<RectTransform>(),
                quitBtn.GetComponent<RectTransform>()
            };

            // 面板
            var levelPanel = BuildLevelPanel(root, spPanel, spButton, spButtonHover, fontTitle, fontButton,
                out var level1Btn, out var level2Btn, out var levelBackBtn);
            var settingsPanel = BuildSettingsPanel(root, spPanel, spButton, spButtonHover, fontTitle, fontButton,
                out var settingsBackBtn);

            // 页脚
            var version = NewText("Footer_Version", root, fontButton, "v" + Application.version, 20f, FooterColor,
                TextAlignmentOptions.MidlineLeft);
            var versionRt = version.rectTransform;
            versionRt.anchorMin = new Vector2(0f, 0f);
            versionRt.anchorMax = new Vector2(0f, 0f);
            versionRt.pivot = new Vector2(0f, 0f);
            versionRt.sizeDelta = new Vector2(220f, 30f);
            versionRt.anchoredPosition = new Vector2(24f, 18f);

            // 控制器
            var controller = root.gameObject.AddComponent<TitleScreenController>();
            controller.startSceneName = "GameScene1";
            controller.level1Scene = "GameScene1";
            controller.level2Scene = "GameScene2";
            controller.startButton = startBtn;
            controller.levelSelectButton = levelBtn;
            controller.settingsButton = optionsBtn;
            controller.quitButton = quitBtn;
            controller.levelSelectPanel = levelPanel;
            controller.settingsPanel = settingsPanel;
            controller.level1Button = level1Btn;
            controller.level2Button = level2Btn;
            controller.levelBackButton = levelBackBtn;
            controller.settingsBackButton = settingsBackBtn;
            controller.pointer = pointer.rectTransform;
            controller.menuButtonRects = btnRects;

            AddJuice(level1Btn, sfx);
            AddJuice(level2Btn, sfx);
            AddJuice(levelBackBtn, sfx);
            AddJuice(settingsBackBtn, sfx);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.Refresh();

            Debug.Log("[TitleScreenSetup] 标题界面已按当前排版重建并保存 StartScene。");
        }

        #region 面板构建
        private static GameObject BuildLevelPanel(RectTransform parent, Sprite panelSprite, Sprite btn, Sprite hover,
            TMP_FontAsset fontTitle, TMP_FontAsset fontBtn, out Button level1, out Button level2, out Button back)
        {
            var panel = NewImage("Panel_LevelSelect", parent, panelSprite, true);
            panel.type = Image.Type.Sliced;
            Place(panel.rectTransform, 0f, 0f, 440f, 320f);
            panel.gameObject.SetActive(false);

            var title = NewText("Title", panel.rectTransform, fontTitle, "Select Level", 40f, Gold, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0f, 120f, 380f, 60f);

            level1 = NewTextButton("Button_Level1", panel.rectTransform, btn, hover, "Level 1", fontBtn, TextDark,
                0f, 30f, 240f, 56f);
            level2 = NewTextButton("Button_Level2", panel.rectTransform, btn, hover, "Level 2", fontBtn, TextDark,
                0f, -40f, 240f, 56f);
            back = NewTextButton("Button_Back", panel.rectTransform, btn, hover, "Back", fontBtn, TextDark,
                0f, -110f, 160f, 50f);

            return panel.gameObject;
        }

        private static GameObject BuildSettingsPanel(RectTransform parent, Sprite panelSprite, Sprite btn, Sprite hover,
            TMP_FontAsset fontTitle, TMP_FontAsset fontBtn, out Button back)
        {
            var panel = NewImage("Panel_Settings", parent, panelSprite, true);
            panel.type = Image.Type.Sliced;
            Place(panel.rectTransform, 0f, 0f, 440f, 320f);
            panel.gameObject.SetActive(false);

            var title = NewText("Title", panel.rectTransform, fontTitle, "Settings", 40f, Gold, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0f, 120f, 380f, 60f);

            var body = NewText("Body", panel.rectTransform, fontBtn,
                "Coming soon...\n音量/画质设置将随暂停菜单一起加入", 22f, FooterColor, TextAlignmentOptions.Center);
            Place(body.rectTransform, 0f, 0f, 360f, 120f);

            back = NewTextButton("Button_Back", panel.rectTransform, btn, hover, "Back", fontBtn, TextDark,
                0f, -110f, 160f, 50f);

            return panel.gameObject;
        }
        #endregion

        #region 构建设施
        private static Canvas FindCanvas()
        {
            foreach (var c in Object.FindObjectsOfType<Canvas>())
                if (c.name == "Canvas") return c;
            return Object.FindObjectOfType<Canvas>();
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetAsFirstSibling();
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 450f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
        }

        private static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, bool raycast)
        {
            var rt = NewUI(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = raycast;
            img.preserveAspect = false;
            return img;
        }

        private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, string text,
            float size, Color color, TextAlignmentOptions align)
        {
            var rt = NewUI(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Max(8f, size * 0.5f);
            tmp.fontSizeMax = size;
            tmp.fontSize = size;
            return tmp;
        }

        private static Button NewIconButton(string name, Transform parent, float x, float y,
            Sprite rounded, Sprite highlight, Sprite disabled)
        {
            var rt = NewUI(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = rounded;
            img.color = ButtonFrame;
            img.type = Image.Type.Simple;
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.SpriteSwap;

            var ss = btn.spriteState;
            ss.highlightedSprite = highlight;
            ss.pressedSprite = highlight;
            ss.selectedSprite = highlight;
            ss.disabledSprite = disabled;
            btn.spriteState = ss;

            var nav = btn.navigation;
            nav.mode = Navigation.Mode.Automatic;
            btn.navigation = nav;

            Place(rt, x, y, 225f, 56f);

            var fill = NewImage("roundedbg", rt, rounded, true);
            fill.color = ButtonFill;
            fill.type = Image.Type.Simple;
            Place(fill.rectTransform, -5f, 4f, 225f, 53f);

            return btn;
        }

        private static void AddIcon(Transform parent, string name, Sprite sprite, Color color,
            float x, float y, float w, float h, float scaleX, float scaleY)
        {
            var icon = NewImage(name, parent, sprite, true);
            icon.color = color;
            icon.type = Image.Type.Simple;
            Place(icon.rectTransform, x, y, w, h);
            icon.rectTransform.localScale = new Vector3(scaleX, scaleY, 0.3125f);
        }

        private static Button NewTextButton(string name, Transform parent, Sprite normal, Sprite hover,
            string label, TMP_FontAsset font, Color labelColor, float x, float y, float w, float h)
        {
            var rt = NewUI(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = normal;
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.SpriteSwap;

            var ss = btn.spriteState;
            ss.highlightedSprite = hover;
            ss.pressedSprite = hover;
            ss.selectedSprite = hover;
            ss.disabledSprite = normal;
            btn.spriteState = ss;

            var nav = btn.navigation;
            nav.mode = Navigation.Mode.Automatic;
            btn.navigation = nav;

            Place(rt, x, y, w, h);

            var text = NewText(name + "_Label", rt, font, label, 26f, labelColor, TextAlignmentOptions.Center);
            StretchFull(text.rectTransform);

            return btn;
        }

        private static void AddJuice(Button btn, UiSfxPlayer sfx)
        {
            if (btn == null) return;
            var juice = btn.gameObject.GetComponent<ButtonJuice>();
            if (juice == null) juice = btn.gameObject.AddComponent<ButtonJuice>();
            juice.sfx = sfx;
        }

        private static void StretchFull(RectTransform rt, float l = 0f, float b = 0f, float r = 0f, float t = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static Sprite LoadSprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) return s;
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var a in all)
                if (a is Sprite sp) return sp;
            Debug.LogWarning($"[TitleScreenSetup] 找不到精灵：{path}");
            return null;
        }
        #endregion
    }
}
