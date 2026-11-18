using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RunningGame.UI;

/// <summary>
/// 音量选项面板：音乐 / 音效两条滑条的 tickmark 拖拽、键盘与吸附。
/// 由 <see cref="GameAudioManager"/> 在场景加载时自动挂到 Panel_Options 上，无需手动配置引用。
///
/// 档位（都与你在场景里摆放的空物体一一对应）：
///   音乐 0..10（0%..100%）：M0..M9 对应 0..90%，GameUI_TickMark 原位为 100%。
///   音效 0..9（0%..90%）：S1..S9 对应 0..80%，GameUI_TickMark (1) 原位为 90%。
/// 50%（M5 / S6）时全局系数为 1.0，完全不改变已经调好的基础音量。
///
/// 键盘：A/← 减小，D/→ 增大；1 秒内松开走一格，按住满 1 秒后连续移动，松开即停。
/// 键盘只作用于「当前聚焦」的那条滑条，点击某条 tickmark 即可切换聚焦（默认音乐）。
///
/// 焦点切换：W / ↑ 上一个，S / ↓ 下一个，小键盘 8 / 2 同样可用；
/// 在「音乐滑条 → 音效滑条 → 返回」之间循环。
/// 点击滑条轨道的空白处，会把 tickmark 直接吸附到最近的刻度。
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioOptionsPanel : MonoBehaviour
{
    private const float LongPressDelay = 1f;      // 长按触发连续移动的停滞时间
    private const float RepeatInterval = 0.12f;   // 连续移动间隔
    private const float TapDistancePixels = 6f;   // 判定「点击聚焦」而非拖动的位移阈值

    private RectTransform panelRect;
    private RectTransform musicTickmark;
    private RectTransform sfxTickmark;
    private RectTransform musicBar;
    private RectTransform sfxBar;

    private Vector3 musicBaseScale;
    private Vector3 sfxBaseScale;
    private float musicHomeY;
    private float sfxHomeY;

    private readonly float[] musicSteps = new float[11]; // 0..10（面板本地 X）
    private readonly float[] sfxSteps = new float[10];   // 0..9

    private int musicStep;
    private int sfxStep;
    private bool initialized;

    // 焦点区域：音乐滑条 / 音效滑条 / 返回按钮，三者用 W/S 与小键盘↑↓循环切换。
    private enum FocusArea { Music, Sfx, Back }
    private FocusArea focusedArea = FocusArea.Music;

    private Button backButton;
    private Navigation backButtonNav;
    private bool backNavCaptured;

    // 键盘长按状态
    private int activeDir;
    private KeyCode activeKey = KeyCode.None;
    private float holdStart;
    private bool tapPending;
    private float repeatNextTime;

    // 鼠标拖动状态
    private AudioVolumeTickmark.SliderKind draggingKind;
    private bool dragging;
    private Vector2 dragStartScreen;
    private int dragNearest;

    // 以下相对布局来自 StartScene 的 19 个空物体（滑条条本地 X），
    // 供没有摆放空物体的游戏内暂停面板兜底使用。
    private static readonly float[] FallbackMusicLocal =
    {
        -50.04999f, -39.100002f, -30.099998f, -18.999987f, -10.400013f,
        -1.2999916f, 7.8000107f, 18.300003f, 26.100033f, 36.90001f
    };

    private static readonly float[] FallbackSfxLocal =
    {
        -49.3f, -37.299988f, -26.94999f, -14.899988f, -6.39999f,
        5.4999847f, 17.700005f, 29.149986f, 38.549988f
    };

    private void Awake()
    {
        if (initialized) return;
        initialized = true;

        panelRect = GetComponent<RectTransform>();
        if (panelRect == null) return;

        musicTickmark = transform.Find("GameUI_TickMark") as RectTransform;
        sfxTickmark = transform.Find("GameUI_TickMark (1)") as RectTransform;
        musicBar = transform.Find("GameUI_MusicSlider") as RectTransform;
        sfxBar = transform.Find("GameUI_SoundFxSlider") as RectTransform;

        // 不是音频面板（缺少 tickmark）就什么都不做，避免误伤同名的其它面板。
        if (musicTickmark == null || sfxTickmark == null) return;

        musicBaseScale = musicTickmark.localScale;
        sfxBaseScale = sfxTickmark.localScale;

        // 保证 tickmark 一定能接收鼠标事件（即使场景里 Raycast Target 被关掉）。
        EnsureRaycastTarget(musicTickmark);
        EnsureRaycastTarget(sfxTickmark);

        BindTickmark(musicTickmark, AudioVolumeTickmark.SliderKind.Music);
        BindTickmark(sfxTickmark, AudioVolumeTickmark.SliderKind.Sfx);
        ResolveBackButton();
        BindBars();

        CaptureLayout();
        SetFocusedArea(FocusArea.Music);
        ApplySavedStep(AudioVolumeTickmark.SliderKind.Music, playSound: false);
        ApplySavedStep(AudioVolumeTickmark.SliderKind.Sfx, playSound: false);
    }

    private void OnEnable()
    {
        if (!initialized) return;
        ApplySavedStep(AudioVolumeTickmark.SliderKind.Music, playSound: false);
        ApplySavedStep(AudioVolumeTickmark.SliderKind.Sfx, playSound: false);
        // 面板打开期间由本脚本接管上下切换：临时关掉返回按钮的自动导航，
        // 避免 Unity 的 Vertical 轴（含 W/S/↑↓）抢走 EventSystem 的焦点。
        SetBackNavigationLocked(true);
        SetFocusedArea(FocusArea.Music);
    }

    private void OnDisable()
    {
        dragging = false;
        activeDir = 0;
        activeKey = KeyCode.None;
        tapPending = false;
        SetBackNavigationLocked(false);

        var es = EventSystem.current;
        if (es != null && backButton != null && es.currentSelectedGameObject == backButton.gameObject)
            es.SetSelectedGameObject(null);
    }

    private void Update()
    {
        HandleFocusSwitch();
        HandleKeyboard();
    }

    #region 布局：把每个档位换算成「面板本地 X」
    private void CaptureLayout()
    {
        musicHomeY = musicTickmark.anchoredPosition.y;
        sfxHomeY = sfxTickmark.anchoredPosition.y;
        float musicHomeX = musicTickmark.anchoredPosition.x;
        float sfxHomeX = sfxTickmark.anchoredPosition.x;

        if (!TryFillFromMarkers(musicBar, musicSteps, 0, 9, "M", 0))
            FillFallback(musicBar, musicSteps, 0, 9, FallbackMusicLocal);
        musicSteps[10] = musicHomeX;

        if (!TryFillFromMarkers(sfxBar, sfxSteps, 0, 8, "S", 1))
            FillFallback(sfxBar, sfxSteps, 0, 8, FallbackSfxLocal);
        sfxSteps[9] = sfxHomeX;
    }

    private static bool TryFillFromMarkers(RectTransform bar, float[] arr, int start, int end, string prefix, int numberOffset)
    {
        if (bar == null) return false;

        for (int i = start; i <= end; i++)
        {
            Transform marker = bar.Find(prefix + (i + numberOffset));
            RectTransform rt = marker as RectTransform;
            if (rt == null) return false;

            // 标记都是滑条的直接子物体且 anchor/pivot 居中：
            // 面板本地 X = 滑条 anchoredPosition.x + 标记 anchoredPosition.x * 滑条 localScale.x
            arr[i] = bar.anchoredPosition.x + rt.anchoredPosition.x * bar.localScale.x;
        }
        return true;
    }

    private static void FillFallback(RectTransform bar, float[] arr, int start, int end, float[] localXs)
    {
        float barX = bar != null ? bar.anchoredPosition.x : 0f;
        float scaleX = bar != null ? bar.localScale.x : 1f;
        for (int i = start; i <= end; i++)
            arr[i] = barX + localXs[i - start] * scaleX;
    }

    private void BindTickmark(RectTransform tick, AudioVolumeTickmark.SliderKind kind)
    {
        var handler = tick.GetComponent<AudioVolumeTickmark>();
        if (handler == null) handler = tick.gameObject.AddComponent<AudioVolumeTickmark>();
        handler.Bind(this, kind);
    }

    private static void EnsureRaycastTarget(RectTransform tick)
    {
        var image = tick.GetComponent<Image>();
        if (image != null) image.raycastTarget = true;
    }

    private void ResolveBackButton()
    {
        Transform t = transform.Find("Button_Back");
        if (t == null)
        {
            var all = GetComponentsInChildren<Button>(true);
            foreach (var b in all)
            {
                if (b.name.IndexOf("Back", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t = b.transform;
                    break;
                }
            }
        }
        if (t != null) backButton = t.GetComponent<Button>();
    }

    private void BindBars()
    {
        BindBar(musicBar, AudioVolumeTickmark.SliderKind.Music);
        BindBar(sfxBar, AudioVolumeTickmark.SliderKind.Sfx);
    }

    private void BindBar(RectTransform bar, AudioVolumeTickmark.SliderKind kind)
    {
        if (bar == null) return;
        EnsureRaycastTarget(bar);

        var handler = bar.GetComponent<AudioVolumeSliderBar>();
        if (handler == null) handler = bar.gameObject.AddComponent<AudioVolumeSliderBar>();
        handler.Bind(this, kind);
    }

    private void SetBackNavigationLocked(bool locked)
    {
        if (backButton == null) return;

        if (locked)
        {
            if (!backNavCaptured)
            {
                backButtonNav = backButton.navigation;
                backNavCaptured = true;
            }
            var nav = backButton.navigation;
            nav.mode = Navigation.Mode.None;
            backButton.navigation = nav;
        }
        else if (backNavCaptured)
        {
            backButton.navigation = backButtonNav;
            backNavCaptured = false;
        }
    }
    #endregion

    #region 档位应用
    private void ApplySavedStep(AudioVolumeTickmark.SliderKind kind, bool playSound)
    {
        int step = kind == AudioVolumeTickmark.SliderKind.Music
            ? GameAudioManager.MusicStep
            : GameAudioManager.SfxStep;
        SetStep(kind, step, playSound);
    }

    private void SetStep(AudioVolumeTickmark.SliderKind kind, int index, bool playSound, bool moveTick = true)
    {
        bool isMusic = kind == AudioVolumeTickmark.SliderKind.Music;
        int max = isMusic ? 10 : 9;
        int clamped = Mathf.Clamp(index, 0, max);

        int previous = isMusic ? musicStep : sfxStep;
        if (isMusic) musicStep = clamped;
        else sfxStep = clamped;

        float[] steps = isMusic ? musicSteps : sfxSteps;
        RectTransform tick = isMusic ? musicTickmark : sfxTickmark;
        float homeY = isMusic ? musicHomeY : sfxHomeY;
        if (moveTick && tick != null) tick.anchoredPosition = new Vector2(steps[clamped], homeY);

        if (isMusic) GameAudioManager.SetMusicStep(clamped);
        else GameAudioManager.SetSfxStep(clamped);

        if (playSound && clamped != previous)
            UiSfxPlayer.PlaySliderTickSound();
    }
    #endregion

    #region 鼠标拖动 / 点击聚焦
    public void NotifyPointerDown(AudioVolumeTickmark.SliderKind kind, PointerEventData eventData)
    {
        draggingKind = kind;
        dragging = true;
        dragStartScreen = eventData.position;
        dragNearest = kind == AudioVolumeTickmark.SliderKind.Music ? musicStep : sfxStep;
        SetFocusedArea(AreaOf(kind));
    }

    // 点击滑条轨道空白处：tickmark 直接跳到最近的刻度，并顺带进入拖拽状态，
    // 这样按住不放继续拖动时也能平滑跟随。
    public void NotifyBarPointerDown(AudioVolumeTickmark.SliderKind kind, PointerEventData eventData)
    {
        SetFocusedArea(AreaOf(kind));

        bool isMusic = kind == AudioVolumeTickmark.SliderKind.Music;
        int nearest = isMusic ? musicStep : sfxStep;

        if (TryScreenToPanelLocal(eventData, out Vector2 local))
        {
            float[] steps = isMusic ? musicSteps : sfxSteps;
            nearest = NearestStep(steps, local.x);
            SetStep(kind, nearest, playSound: true);
        }

        draggingKind = kind;
        dragging = true;
        dragStartScreen = eventData.position;
        dragNearest = nearest;
    }

    public void NotifyDrag(AudioVolumeTickmark.SliderKind kind, PointerEventData eventData)
    {
        if (!dragging || kind != draggingKind) return;
        if (!TryScreenToPanelLocal(eventData, out Vector2 local)) return;

        float[] steps = kind == AudioVolumeTickmark.SliderKind.Music ? musicSteps : sfxSteps;
        float minX = Mathf.Min(steps[0], steps[steps.Length - 1]);
        float maxX = Mathf.Max(steps[0], steps[steps.Length - 1]);
        float x = Mathf.Clamp(local.x, minX, maxX);

        RectTransform tick = kind == AudioVolumeTickmark.SliderKind.Music ? musicTickmark : sfxTickmark;
        float homeY = kind == AudioVolumeTickmark.SliderKind.Music ? musicHomeY : sfxHomeY;
        tick.anchoredPosition = new Vector2(x, homeY);

        int nearest = NearestStep(steps, x);
        if (nearest != dragNearest)
        {
            dragNearest = nearest;
            // 自由拖动期间只更新数值（并响刻度），不把 tickmark 吸到坐标上。
            SetStep(kind, nearest, playSound: true, moveTick: false);
        }
    }

    public void NotifyPointerUp(AudioVolumeTickmark.SliderKind kind, PointerEventData eventData)
    {
        if (!dragging || kind != draggingKind) return;
        dragging = false;

        RectTransform tick = kind == AudioVolumeTickmark.SliderKind.Music ? musicTickmark : sfxTickmark;
        float[] steps = kind == AudioVolumeTickmark.SliderKind.Music ? musicSteps : sfxSteps;

        // 松手吸附到最近档位（自由拖动期间只在跨越中点时才改变音量/响刻度）。
        int nearest = NearestStep(steps, tick.anchoredPosition.x);
        SetStep(kind, nearest, playSound: false);

        bool tapped = (eventData.position - dragStartScreen).sqrMagnitude <= TapDistancePixels * TapDistancePixels;
        if (tapped) SetFocusedArea(AreaOf(kind));
    }

    private static int NearestStep(float[] steps, float x)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < steps.Length; i++)
        {
            float d = Mathf.Abs(steps[i] - x);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private bool TryScreenToPanelLocal(PointerEventData eventData, out Vector2 local)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            panelRect, eventData.position, eventData.pressEventCamera, out local);
    }
    #endregion

    #region 键盘
    // W/S 与小键盘 ↑↓（以及方向键 ↑↓）：在 音乐滑条 → 音效滑条 → 返回 之间循环切换焦点。
    // 复用与关卡选择一致的「(i + dir + n) % n」首尾循环写法。
    private void HandleFocusSwitch()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Keypad8) || Input.GetKeyDown(KeyCode.UpArrow))
            CycleFocus(-1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.DownArrow))
            CycleFocus(+1);
    }

    private void CycleFocus(int dir)
    {
        const int count = 3;
        int current = (int)focusedArea;
        int next = (current + dir + count) % count;
        SetFocusedArea((FocusArea)next);
    }

    private void HandleKeyboard()
    {
        // 焦点停在返回按钮上时不处理左右调值，避免误改音量。
        if (focusedArea == FocusArea.Back)
        {
            activeDir = 0;
            activeKey = KeyCode.None;
            tapPending = false;
            return;
        }

        if (TryGetNewDirection(out int newDir, out KeyCode newKey))
        {
            activeDir = newDir;
            activeKey = newKey;
            holdStart = Time.unscaledTime;
            tapPending = true;
            repeatNextTime = holdStart + LongPressDelay;
            // 不在这里 return：同一帧内极快地按下并松开时，下面要继续处理 GetKeyUp。
        }

        if (activeDir == 0) return;

        if (Input.GetKeyUp(activeKey))
        {
            OnActiveKeyReleased();
            return;
        }

        if (!Input.GetKey(activeKey)) return;

        float now = Time.unscaledTime;
        if (tapPending && now >= holdStart + LongPressDelay)
        {
            // 长按满 1 秒：取消点按，立即走一格，之后按 RepeatInterval 连续走。
            tapPending = false;
            MoveFocusedStep(activeDir);
            repeatNextTime = now + RepeatInterval;
        }
        else if (!tapPending && now >= repeatNextTime)
        {
            MoveFocusedStep(activeDir);
            repeatNextTime += RepeatInterval;
        }
    }

    private void OnActiveKeyReleased()
    {
        if (tapPending)
            MoveFocusedStep(activeDir); // 1 秒内松开 = 点按走一格

        activeDir = 0;
        activeKey = KeyCode.None;
        tapPending = false;

        // 另一个方向键仍按着时，切换为新的活动方向（重新计时）。
        if (TryGetHeldDirection(out int dir, out KeyCode key))
        {
            activeDir = dir;
            activeKey = key;
            holdStart = Time.unscaledTime;
            tapPending = true;
            repeatNextTime = holdStart + LongPressDelay;
        }
    }

    private void MoveFocusedStep(int dir)
    {
        if (focusedArea == FocusArea.Back) return;
        var kind = focusedArea == FocusArea.Music
            ? AudioVolumeTickmark.SliderKind.Music
            : AudioVolumeTickmark.SliderKind.Sfx;
        bool isMusic = kind == AudioVolumeTickmark.SliderKind.Music;
        int current = isMusic ? musicStep : sfxStep;
        int max = isMusic ? 10 : 9;
        int next = Mathf.Clamp(current + dir, 0, max);
        if (next == current) return; // 到端点后不再重复
        SetStep(kind, next, playSound: true);
    }

    private static bool TryGetNewDirection(out int dir, out KeyCode key)
    {
        dir = 0;
        key = KeyCode.None;
        if (Input.GetKeyDown(KeyCode.D)) { dir = +1; key = KeyCode.D; return true; }
        if (Input.GetKeyDown(KeyCode.RightArrow)) { dir = +1; key = KeyCode.RightArrow; return true; }
        if (Input.GetKeyDown(KeyCode.A)) { dir = -1; key = KeyCode.A; return true; }
        if (Input.GetKeyDown(KeyCode.LeftArrow)) { dir = -1; key = KeyCode.LeftArrow; return true; }
        return false;
    }

    private static bool TryGetHeldDirection(out int dir, out KeyCode key)
    {
        dir = 0;
        key = KeyCode.None;
        if (Input.GetKey(KeyCode.D)) { dir = +1; key = KeyCode.D; return true; }
        if (Input.GetKey(KeyCode.RightArrow)) { dir = +1; key = KeyCode.RightArrow; return true; }
        if (Input.GetKey(KeyCode.A)) { dir = -1; key = KeyCode.A; return true; }
        if (Input.GetKey(KeyCode.LeftArrow)) { dir = -1; key = KeyCode.LeftArrow; return true; }
        return false;
    }
    #endregion

    #region 聚焦视觉（键盘操作目标）
    private void SetFocusedArea(FocusArea area)
    {
        focusedArea = area;
        const float focusScale = 1.15f;

        if (musicTickmark != null)
        {
            musicTickmark.localScale = area == FocusArea.Music
                ? new Vector3(musicBaseScale.x * focusScale, musicBaseScale.y * focusScale, musicBaseScale.z)
                : musicBaseScale;
        }

        if (sfxTickmark != null)
        {
            sfxTickmark.localScale = area == FocusArea.Sfx
                ? new Vector3(sfxBaseScale.x * focusScale, sfxBaseScale.y * focusScale, sfxBaseScale.z)
                : sfxBaseScale;
        }

        var es = EventSystem.current;
        if (es != null)
        {
            if (area == FocusArea.Back && backButton != null)
            {
                if (es.currentSelectedGameObject != backButton.gameObject)
                    es.SetSelectedGameObject(backButton.gameObject);
            }
            else
            {
                // 滑条聚焦时不保留任何 EventSystem 选中，避免回车意外触发别处按钮。
                if (es.currentSelectedGameObject != null)
                    es.SetSelectedGameObject(null);
            }
        }
    }

    private static FocusArea AreaOf(AudioVolumeTickmark.SliderKind kind)
    {
        return kind == AudioVolumeTickmark.SliderKind.Music ? FocusArea.Music : FocusArea.Sfx;
    }
    #endregion
}
