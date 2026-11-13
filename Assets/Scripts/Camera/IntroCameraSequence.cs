using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 关卡开场镜头序列（叠加在 <see cref="ZoomCameraRig"/> 之上，不修改它的取景逻辑）。
///
/// 触发时机：玩家点击 IntroPopup 的 Resume、游戏正式进入场景之后。
/// 流程：
///   1. 先在角色身上停留一小会儿（保持默认取景，不做任何移动）；
///   2. 平滑地向右平移到终点旗帜 / 旗杆，并在移动过程中逐渐拉近成一个特写；
///   3. 拉远展开到整张地图（以 BackGround 为准），停留一会儿；
///   4. 平滑地把镜头拉回到角色身上，然后把控制权交还给 ZoomCameraRig。
///
/// 所有镜头都会被限制在 BackGround 精灵的范围内，避免拍到地图外未布置的区域。
///
/// 实现方式：序列期间把 <see cref="ZoomCameraRig.FollowTarget"/> 临时关掉并直接驱动
/// 摄像机的位置与正交尺寸；序列结束后再交还给 rig。因为本类执行顺序晚于 rig
/// （DefaultExecutionOrder 更大），每帧最终由本类决定镜头，绝不会和 rig 打架。
///
/// 组件会在每个关卡场景加载后自动挂到 Main Camera 上（见文件末尾的 Bootstrapper），
/// 因此无需在任何场景里手动拖拽。想调参数时，在 Main Camera 上会看到这些字段。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class IntroCameraSequence : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 总开关 ===")]
    [Tooltip("关闭后不做任何开场镜头，直接保持原有行为")]
    [SerializeField] private bool enableSequence = true;

    [Header("=== 1. 起始停在角色身上 ===")]
    [Tooltip("进入场景后先在角色身上停留的时长（秒）。这段时间镜头保持默认跟随，不做任何移动。")]
    [SerializeField] private float startHoldDuration = 0.4f;

    [Header("=== 2. 平移前往旗帜（特写） ===")]
    [Tooltip("平移速度（世界单位/秒）。越大移动越快，越小越慢越柔和。")]
    [SerializeField] private float panSpeed = 6f;
    [Tooltip("平移的最短时长（秒）。距离很近时也保证有一段平滑过渡。")]
    [SerializeField] private float minPanDuration = 0.35f;
    [Tooltip("平移过程使用的取景框大小（正交尺寸，越小越近）。设为 0 表示沿用角色当前的默认取景大小。")]
    [SerializeField] private float panFrameOrthoSize = 0f;
    [Tooltip("到达旗帜后特写的取景框大小（正交尺寸，越小越近）。")]
    [SerializeField] private float flagCloseupOrthoSize = 2.2f;
    [Tooltip("旗帜特写保持时长（秒）。")]
    [SerializeField] private float flagCloseupHold = 1.0f;

    [Header("=== 3. 展开全图 ===")]
    [Tooltip("从旗帜特写拉远到全图的时长（秒）。")]
    [SerializeField] private float pullOutDuration = 1.2f;
    [Tooltip("全图停留时长（秒）。")]
    [SerializeField] private float mapHoldDuration = 1.0f;
    [Tooltip("全图取景在 BackGround 范围基础上的留白倍率（1 = 刚好铺满背景，>1 会略微拍到背景外，不推荐）。")]
    [SerializeField] private float mapPadding = 1f;

    [Header("=== 4. 回到角色 ===")]
    [Tooltip("从全图拉回角色的时长（秒）。")]
    [SerializeField] private float returnDuration = 1.1f;
    [Tooltip("回到角色后的取景框大小（正交尺寸）。设为 0 表示沿用角色当前的默认取景大小。")]
    [SerializeField] private float returnFrameOrthoSize = 0f;

    [Header("=== 背景限制（防止拍到地图外） ===")]
    [Tooltip("把镜头取景限制在 BackGround 精灵范围内。强烈建议开启。")]
    [SerializeField] private bool clampToBackground = true;
    [Tooltip("用于取景限制的背景对象名（场景里的 Big 精灵）。")]
    [SerializeField] private string backgroundObjectName = "BackGround";
    [Tooltip("背景边界向内收缩的距离：镜头不贴到背景边缘。")]
    [SerializeField] private float backgroundInset = 0.05f;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    #endregion

    private enum Phase
    {
        Idle,
        StartHold,   // 停在角色身上
        PanToFlag,   // 向右平移到旗帜
        FlagHold,    // 旗帜特写保持
        PullOut,     // 拉远到全图
        MapHold,     // 全图保持
        Return,      // 拉回角色
        Done
    }

    #region 运行时状态
    private ZoomCameraRig rig;
    private Camera cam;
    private Transform camT;
    private RunningGame.UI.IntroPopupController intro;
    private Transform player;
    private Transform flagRoot;
    private SpriteRenderer backgroundRenderer;

    private Phase phase = Phase.Idle;
    private bool begun;

    private float camZ;
    private float phaseTimer;
    private float homeOrtho;
    private float returnOrtho;

    // 背景限制
    private bool hasBgRect;
    private Rect bgRect;

    // 全图取景
    private Vector2 mapCenter;
    private float mapOrtho;

    // 平移段
    private Vector2 panStartCenter;
    private float panStartOrtho;
    private Vector2 panEndCenter;
    private float panEndOrtho;
    private float panDuration;

    // 拉远 / 拉回段的起点
    private Vector2 legStartCenter;
    private float legStartOrtho;
    #endregion

    #region 生命周期
    private void Awake()
    {
        CacheRefs();
    }

    private void Start()
    {
        if (!enableSequence || rig == null || cam == null)
        {
            phase = Phase.Done;
            enabled = false;
            return;
        }

        DetermineTrigger();
    }

    private void OnDisable()
    {
        if (intro != null)
        {
            intro.Closed -= OnIntroClosed;
            intro = null;
        }

        // 若在序列进行中被销毁 / 禁用，确保把控制权还回去，避免镜头卡死。
        if (begun && phase != Phase.Done && rig != null)
        {
            rig.FollowTarget = true;
        }
    }

    private void LateUpdate()
    {
        if (cam == null || camT == null) return;

        switch (phase)
        {
            case Phase.Idle:
            case Phase.Done:
                return;

            case Phase.StartHold:
            {
                // 这段时间刻意不接管镜头：rig 继续把镜头保持在角色上（默认状态）。
                phaseTimer += DeltaTime();
                if (phaseTimer >= startHoldDuration)
                    BeginPan();
                break;
            }

            case Phase.PanToFlag:
            {
                phaseTimer += DeltaTime();
                float t = panDuration > 0.0001f ? Mathf.Clamp01(phaseTimer / panDuration) : 1f;
                float s = SmoothStep(t);
                ApplyCamera(Vector2.Lerp(panStartCenter, panEndCenter, s),
                    Mathf.Lerp(panStartOrtho, panEndOrtho, s));
                if (t >= 1f)
                {
                    EnterPhase(Phase.FlagHold);
                }
                break;
            }

            case Phase.FlagHold:
            {
                ApplyCamera(panEndCenter, panEndOrtho);
                phaseTimer += DeltaTime();
                if (phaseTimer >= flagCloseupHold)
                {
                    legStartCenter = panEndCenter;
                    legStartOrtho = panEndOrtho;
                    EnterPhase(Phase.PullOut);
                }
                break;
            }

            case Phase.PullOut:
            {
                phaseTimer += DeltaTime();
                float t = pullOutDuration > 0.0001f ? Mathf.Clamp01(phaseTimer / pullOutDuration) : 1f;
                float s = SmoothStep(t);
                ApplyCamera(Vector2.Lerp(legStartCenter, mapCenter, s),
                    Mathf.Lerp(legStartOrtho, mapOrtho, s));
                if (t >= 1f)
                {
                    ApplyCamera(mapCenter, mapOrtho);
                    legStartCenter = mapCenter;
                    legStartOrtho = mapOrtho;
                    EnterPhase(Phase.MapHold);
                }
                break;
            }

            case Phase.MapHold:
            {
                ApplyCamera(mapCenter, mapOrtho);
                phaseTimer += DeltaTime();
                if (phaseTimer >= mapHoldDuration)
                {
                    legStartCenter = mapCenter;
                    legStartOrtho = mapOrtho;
                    EnterPhase(Phase.Return);
                }
                break;
            }

            case Phase.Return:
            {
                phaseTimer += DeltaTime();
                float t = returnDuration > 0.0001f ? Mathf.Clamp01(phaseTimer / returnDuration) : 1f;
                float s = SmoothStep(t);

                RefreshPlayer();
                Vector2 playerCenter = player != null ? (Vector2)player.position : legStartCenter;
                playerCenter = ClampCenter(playerCenter, returnOrtho);

                ApplyCamera(Vector2.Lerp(legStartCenter, playerCenter, s),
                    Mathf.Lerp(legStartOrtho, returnOrtho, s));

                if (t >= 1f) Finish();
                break;
            }
        }
    }
    #endregion

    #region 触发
    private void DetermineTrigger()
    {
        intro = FindObjectOfType<RunningGame.UI.IntroPopupController>();

        if (intro != null)
        {
            intro.Closed += OnIntroClosed;
            StartCoroutine(CheckAfterFirstFrameRoutine());
        }
        else
        {
            StartCoroutine(BeginNextFrameRoutine());
        }
    }

    private IEnumerator CheckAfterFirstFrameRoutine()
    {
        yield return null;
        if (begun) yield break;
        if (intro == null || !intro.IsOpen) BeginSequence();
    }

    private IEnumerator BeginNextFrameRoutine()
    {
        yield return null;
        if (!begun) BeginSequence();
    }

    private void OnIntroClosed()
    {
        BeginSequence();
    }

    private void BeginSequence()
    {
        if (begun) return;
        begun = true;

        if (intro != null)
        {
            intro.Closed -= OnIntroClosed;
            intro = null;
        }

        if (rig == null) CacheRefs();
        if (rig == null || cam == null)
        {
            phase = Phase.Done;
            enabled = false;
            return;
        }

        RefreshPlayer();
        ResolveFlag();
        ResolveBackground();

        camZ = camT.position.z;

        if (debugLogs)
            Debug.Log($"[{name}] 开场镜头开始。flag={(flagRoot != null)} bg={(hasBgRect ? bgRect.ToString() : "无")}");

        // 先停在角色身上：不接管镜头，直接进入 StartHold。
        EnterPhase(Phase.StartHold);
    }
    #endregion

    #region 各段计算
    private void BeginPan()
    {
        // 从此刻开始接管镜头。
        rig.FollowTarget = false;

        homeOrtho = cam.orthographicSize > 0.01f ? cam.orthographicSize : 5f;
        returnOrtho = returnFrameOrthoSize > 0.01f ? returnFrameOrthoSize : homeOrtho;

        panStartCenter = camT.position;
        panStartOrtho = panFrameOrthoSize > 0.01f ? panFrameOrthoSize : homeOrtho;

        ComputeMap();

        Vector2 flagCenter = panStartCenter;
        bool hasFlag = TryComputeFlagCenter(out flagCenter);
        panEndOrtho = flagCloseupOrthoSize > 0.01f ? flagCloseupOrthoSize : panStartOrtho;
        panEndCenter = ClampCenter(flagCenter, panEndOrtho);

        if (hasFlag)
        {
            float dist = Vector2.Distance(panStartCenter, panEndCenter);
            panDuration = panSpeed > 0.01f ? Mathf.Max(minPanDuration, dist / panSpeed) : minPanDuration;

            // 平移起点用"当前"取景框大小，终点用特写大小：移动过程中同时完成拉近。
            if (panFrameOrthoSize <= 0.01f) panStartOrtho = homeOrtho;

            if (debugLogs)
                Debug.Log($"[{name}] 平移前往旗帜：dist={dist:F2} duration={panDuration:F2}");

            EnterPhase(Phase.PanToFlag);
        }
        else
        {
            // 找不到旗帜：直接从当前位置拉到全图。
            legStartCenter = camT.position;
            legStartOrtho = cam.orthographicSize;
            EnterPhase(Phase.PullOut);
        }
    }

    private bool TryComputeFlagCenter(out Vector2 center)
    {
        center = Vector2.zero;
        if (flagRoot == null) return false;

        Bounds b;
        if (TryGetFlagBounds(flagRoot, out b))
        {
            center = b.center;
            return true;
        }
        center = flagRoot.position;
        return true;
    }

    private void ComputeMap()
    {
        float aspect = Mathf.Max(0.01f, cam.aspect);

        if (hasBgRect)
        {
            mapCenter = bgRect.center;
            float halfH = bgRect.height * 0.5f;
            float halfW = bgRect.width * 0.5f / aspect;
            mapOrtho = Mathf.Max(halfH, halfW) * mapPadding;
            return;
        }

        if (rig != null && rig.TryGetLevelBounds(out Rect rect) && rect.width > 0f && rect.height > 0f)
        {
            mapCenter = rect.center;
            float levelHalfH = rect.height * 0.5f;
            float levelHalfW = rect.width * 0.5f / aspect;
            mapOrtho = Mathf.Max(levelHalfH, levelHalfW) * mapPadding;
            return;
        }

        // 兜底：用玩家 + 旗帜的联合包围盒粗略展示全图跨度。
        RefreshPlayer();
        Vector2 pPlayer = player != null ? (Vector2)player.position : (Vector2)camT.position;
        Vector2 pFlag;
        if (!TryComputeFlagCenter(out pFlag)) pFlag = pPlayer;

        Vector2 min = Vector2.Min(pPlayer, pFlag);
        Vector2 max = Vector2.Max(pPlayer, pFlag);
        mapCenter = (min + max) * 0.5f;
        float spanH = (max.y - min.y) * 0.5f + 2f;
        float spanW = (max.x - min.x) * 0.5f / aspect + 2f;
        mapOrtho = Mathf.Max(spanH, spanW) * mapPadding;
    }
    #endregion

    #region 引用解析
    private void CacheRefs()
    {
        rig = ZoomCameraRig.Instance;
        if (rig == null) rig = FindObjectOfType<ZoomCameraRig>();
        if (rig == null) return;

        cam = rig.UseCamera != null ? rig.UseCamera : rig.GetComponent<Camera>();
        if (cam != null) camT = cam.transform;
    }

    private void RefreshPlayer()
    {
        if (rig != null) player = rig.Player;
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }
    }

    private void ResolveFlag()
    {
        Transform pole = null;

        FlagVictory victory = FindObjectOfType<FlagVictory>();
        if (victory != null) pole = victory.transform;

        if (pole == null)
        {
            GameObject g = GameObject.Find("Flag-FlagPole");
            if (g != null) pole = g.transform;
        }
        if (pole == null)
        {
            GameObject g = GameObject.Find("Flag");
            if (g != null) pole = g.transform;
        }

        flagRoot = pole != null && pole.parent != null ? pole.parent : pole;
    }

    private void ResolveBackground()
    {
        hasBgRect = false;

        if (backgroundRenderer == null && !string.IsNullOrEmpty(backgroundObjectName))
        {
            GameObject go = GameObject.Find(backgroundObjectName);
            if (go != null)
            {
                backgroundRenderer = go.GetComponent<SpriteRenderer>();
                if (backgroundRenderer == null) backgroundRenderer = go.GetComponentInChildren<SpriteRenderer>();
            }
        }

        if (backgroundRenderer == null)
        {
            // 兜底：找不到 BackGround 时，用 rig 的关卡边界作为限制范围。
            if (rig != null && rig.TryGetLevelBounds(out Rect levelRect) &&
                levelRect.width > 0.01f && levelRect.height > 0.01f)
            {
                bgRect = levelRect;
                hasBgRect = true;
            }
            return;
        }

        Bounds b = backgroundRenderer.bounds;
        Rect r = Rect.MinMaxRect(b.min.x + backgroundInset, b.min.y + backgroundInset,
            b.max.x - backgroundInset, b.max.y - backgroundInset);

        if (r.width > 0.01f && r.height > 0.01f)
        {
            bgRect = r;
            hasBgRect = true;
        }
    }

    private static bool TryGetFlagBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null) return false;

        bool any = false;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer r = renderers[i];
            if (r == null || !r.enabled) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!any)
        {
            Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D c = colliders[i];
                if (c == null) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
        }

        if (!any) bounds = new Bounds(root.position, Vector3.one);
        return true;
    }
    #endregion

    #region 应用镜头（含背景钳制）
    private void EnterPhase(Phase next)
    {
        phase = next;
        phaseTimer = 0f;
    }

    /// <summary>把摄像机的可视矩形钳制在 BackGround 范围内，保证不会拍到背景外。</summary>
    private Vector2 ClampCenter(Vector2 center, float ortho)
    {
        if (!clampToBackground || !hasBgRect) return center;

        float aspect = Mathf.Max(0.01f, cam != null ? cam.aspect : 16f / 9f);
        float halfW = ortho * aspect;
        float halfH = ortho;

        float x;
        if (halfW * 2f >= bgRect.width) x = bgRect.center.x;
        else x = Mathf.Clamp(center.x, bgRect.xMin + halfW, bgRect.xMax - halfW);

        float y;
        if (halfH * 2f >= bgRect.height) y = bgRect.center.y;
        else y = Mathf.Clamp(center.y, bgRect.yMin + halfH, bgRect.yMax - halfH);

        return new Vector2(x, y);
    }

    private void ApplyCamera(Vector2 center, float ortho)
    {
        float aspect = Mathf.Max(0.01f, cam != null ? cam.aspect : 16f / 9f);

        // 钳制取景框大小：保证整块可视区域都能装进 BackGround 内。
        if (clampToBackground && hasBgRect)
        {
            float maxOrtho = Mathf.Min(bgRect.height * 0.5f, bgRect.width * 0.5f / aspect);
            if (ortho > maxOrtho) ortho = maxOrtho;
        }

        center = ClampCenter(center, ortho);

        if (camT != null)
        {
            Vector3 p = camT.position;
            p.x = center.x;
            p.y = center.y;
            p.z = camZ;
            camT.position = p;
        }
        if (cam != null) cam.orthographicSize = Mathf.Max(0.01f, ortho);
    }

    private void Finish()
    {
        RefreshPlayer();
        if (player != null)
            ApplyCamera(player.position, returnOrtho);

        if (rig != null) rig.FollowTarget = true;

        phase = Phase.Done;
        if (debugLogs) Debug.Log($"[{name}] 开场镜头结束，控制权交还 ZoomCameraRig。");
        enabled = false;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private float DeltaTime()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) dt = Time.deltaTime;
        return dt > 0f ? dt : 0.0001f;
    }
    #endregion
}

/// <summary>
/// 运行时自动把 <see cref="IntroCameraSequence"/> 挂到关卡里的 Main Camera 上，
/// 并在每次加载关卡场景后重新挂一次（Continue 重载、切关都覆盖），
/// 这样无需在任何场景里手动拖拽组件。
/// </summary>
internal static class IntroCameraSequenceBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TrySpawn();
    }

    private static void TrySpawn()
    {
        if (Object.FindObjectOfType<IntroCameraSequence>() != null) return;

        ZoomCameraRig rig = Object.FindObjectOfType<ZoomCameraRig>();
        if (rig == null) return;

        rig.gameObject.AddComponent<IntroCameraSequence>();
    }
}
