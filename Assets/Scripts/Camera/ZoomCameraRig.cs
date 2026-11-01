using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全自动动态镜头（2D 正交版）。
///
/// 算法骨架直接参考《超级鸡马》的 ZoomCamera：
/// 1. 取景靠"目标集合"——目标可以是 Transform，也可以是 Collider2D 包围盒，
///    多个目标散开时取景框自动变大（= 拉远），聚拢后自动收紧；
/// 2. 每帧先把目标包围盒求出来，再叠加"按比例的四向缓冲"，然后按屏幕宽高比
///    把取景框修正到与屏幕一致（宽高都由它推导，天生适配任意分辨率）；
/// 3. 取景框有最小高度 MinFrameHeight —— 这才是"最近能拉多近"的真正限制，
///    并且它在"移动 / 静止"两档之间平滑过渡（静止时拉近、移动时拉远）；
/// 4. 死区不是直接作用于目标，而是先算出"没有死区时的取景框"，
///    再按 DeadZonePercentage 得到一块以镜头为中心的矩形，把它并入取景框——
///    这样目标在死区内移动时取景框不变，镜头自然不动；
/// 5. 镜头运动用 approachPercent（分轴的最小/最大逼近比例 + 起效距离）：
///    距离越远逼近越快，并有一条 MaxFollowSpeed 速度上限，永不越界抖动。
///
/// 在此基础上保留本项目需要的扩展：
/// - 关卡边界（避免拉远后露出关卡外的空白）；
/// - 事件镜头 CameraCue（蓄力拉远 / 发射贴身跟拍），以"临时取景点 + 额外缩放
///   + 最小取景框倍率"的方式叠加，不破坏上面的基础取景逻辑；
/// - 传送检测（复活瞬移时直接吸附，不横穿关卡）。
///
/// 挂在 Main Camera 上即可，玩家用 Tag=Player 自动发现。
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public class ZoomCameraRig : MonoBehaviour
{
    /// <summary>死区模式：与《超级鸡马》一致，便于在不改状态的情况下临时打开/关闭死区。</summary>
    public enum DeadZoneMode
    {
        Actual,
        ForceOn,
        ForceOff
    }

    public static ZoomCameraRig Instance { get; private set; }

    #region 可配置参数
    [Header("=== 取景目标 ===")]
    [Tooltip("玩家标签，用于自动发现跟随目标")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("自动给场景里的大炮挂上镜头事件组件（已手动挂过的不会重复添加）")]
    [SerializeField] private bool autoAttachCannonCues = true;

    [Header("=== 跟随 ===")]
    [Tooltip("平滑跟随（关闭则用恒定速度硬跟）")]
    [SerializeField] private bool smoothFollowCamOn = true;
    [Tooltip("是否允许镜头跟随（关闭后镜头停在原地）")]
    [SerializeField] private bool followTarget = true;
    [Tooltip("镜头最大移动速度（世界单位/秒）")]
    [SerializeField] private float maxFollowSpeed = 60f;
    [Tooltip("镜头缩放最大变化速度（仅在关闭平滑跟随时使用）")]
    [SerializeField] private float maxZoomSpeed = 40f;
    [Tooltip("分轴最小逼近比例（每秒）。越小，靠近目标时越「黏」、越不容易抖")]
    [SerializeField] private Vector3 minApproachPercent = new Vector3(2.5f, 2.5f, 3f);
    [Tooltip("分轴最大逼近比例（每秒）。距离超过 DistForMaxApproach 后取这个值")]
    [SerializeField] private Vector3 maxApproachPercent = new Vector3(9f, 9f, 6f);
    [Tooltip("偏离多远时逼近比例达到最大值（x/y 为世界距离，z 为取景框半高差）")]
    [SerializeField] private Vector3 distForMaxApproach = new Vector3(2f, 2f, 1.5f);

    [Header("=== 缓冲（相对原始取景框尺寸的比例）===")]
    [Tooltip("启用四向缓冲：目标周围多留一圈空间")]
    [SerializeField] private bool useBuffer = true;
    [SerializeField, Range(0f, 0.5f)] private float leftBuffer = 0.06f;
    [SerializeField, Range(0f, 0.5f)] private float rightBuffer = 0.06f;
    [SerializeField, Range(0f, 0.5f)] private float topBuffer = 0.12f;
    [SerializeField, Range(0f, 0.5f)] private float bottomBuffer = 0.08f;

    [Header("=== 死区 ===")]
    [Tooltip("启用死区：目标在画面中心这块范围内移动时镜头不动")]
    [SerializeField] private bool useDeadZone = true;
    [Tooltip("死区占取景框的比例（半宽/半高的百分比）")]
    [SerializeField] private Vector2 deadZonePercentage = new Vector2(0.32f, 0.16f);

    [Header("=== 取景框最小高度（决定最近能拉多近）===")]
    [Tooltip("静止时的最小取景框高度（世界单位，= 正交尺寸 × 2）")]
    [SerializeField] private float minFrameHeightStill = 7f;
    [Tooltip("移动时的最小取景框高度，比静止大 => 移动时自动拉远给反应空间")]
    [SerializeField] private float minFrameHeightMoving = 11f;
    [Tooltip("从静止档过渡到移动档的速度（世界单位/秒）")]
    [SerializeField] private float movingToMoveSpeed = 14f;
    [Tooltip("从移动档过渡回静止档的速度（世界单位/秒）")]
    [SerializeField] private float movingToStillSpeed = 8f;
    [Tooltip("判定「移动」的平均速度阈值（世界单位/秒）")]
    [SerializeField] private float movingSpeedThreshold = 0.25f;

    [Header("=== 关卡边界 ===")]
    [Tooltip("手动指定边界；留空则自动查找场景内的 CameraBounds2D")]
    [SerializeField] private CameraBounds2D levelBounds;
    [Tooltip("没有 CameraBounds2D 时，用 Ground 层碰撞体自动生成边界")]
    [SerializeField] private bool autoBuildBoundsFromGround = true;
    [Tooltip("自动生成边界时使用的层名")]
    [SerializeField] private string groundLayerName = "Ground";
    [Tooltip("自动边界的内缩距离")]
    [SerializeField] private float autoBoundsInset = 0.2f;

    [Header("=== 传送 / 复位 ===")]
    [Tooltip("单帧位移超过该距离（世界单位）就视为传送，镜头直接吸附而不是横穿关卡")]
    [SerializeField] private float teleportSnapDistance = 2.5f;
    [Tooltip("脚本被禁用时恢复进入前的镜头位置与尺寸")]
    [SerializeField] private bool restoreOnDisable = true;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;
    #endregion

    #region 运行时状态
    private Camera cam;

    private Bounds boundary;
    private bool hasBoundary;

    private Bounds frame;
    private Bounds deadZoneBounds;

    private float minFrameHeight;
    private float extraZoom = 1f;
    private float minFrameHeightMultiplier = 1f;

    private Vector3 lastAverageOfTargets;
    private Vector3 approachPercent;
    private Vector3 distance;

    private Transform player;
    private Vector2 lastPlayerPosition;
    private bool hasLastPlayerPosition;

    private float originalSize;
    private Vector3 originalPosition;
    private bool capturedOriginal;

    private readonly List<Transform> targets = new List<Transform>(32);
    private readonly List<Collider2D> boxes = new List<Collider2D>(32);
    private readonly List<CameraCue> cues = new List<CameraCue>();
    private readonly List<CuePoint> cuePoints = new List<CuePoint>();

    private struct CuePoint
    {
        public Vector2 position;
        public float radius;
    }
    #endregion

    #region 对外接口
    /// <summary>当前使用的摄像机。</summary>
    public Camera UseCamera => cam;

    /// <summary>当前跟随的玩家（可能为空）。</summary>
    public Transform Player => player;

    /// <summary>当前取景框（世界空间包围盒）。</summary>
    public Bounds Frame => frame;

    /// <summary>当前实际使用的正交尺寸。</summary>
    public float CurrentOrthoSize => cam != null ? cam.orthographicSize : 0f;

    public bool FollowTarget
    {
        get => followTarget;
        set => followTarget = value;
    }

    public void AllowFollow(bool follow) => followTarget = follow;

    /// <summary>当前取景框所需的正交尺寸（= 取景框半高）。</summary>
    public float FrameOrthoSize => frame.extents.y;

    public bool HasLevelBounds => hasBoundary;
    public Bounds LevelBounds => boundary;

    public bool TryGetLevelBounds(out Rect rect)
    {
        rect = hasBoundary
            ? Rect.MinMaxRect(boundary.min.x, boundary.min.y, boundary.max.x, boundary.max.y)
            : default;
        return hasBoundary;
    }

    /// <summary>设置关卡边界（对应《超级鸡马》的 SetBounds）。</summary>
    public void SetBounds(Bounds bounds)
    {
        boundary = bounds;
        hasBoundary = true;
        frame = GetFrame(useBuffer, true, DeadZoneMode.Actual);
    }

    public Bounds GetBounds() => boundary;

    /// <summary>取当前镜头的可视范围（世界空间包围盒）。</summary>
    public Bounds GetCameraView()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return default;

        float height = cam.orthographicSize * 2f;
        float width = height * cam.aspect;
        return new Bounds(transform.position, new Vector3(Mathf.Abs(width), Mathf.Abs(height), 0f));
    }

    /// <summary>立即吸附到取景框（切场景 / 复活 / 强制对齐时用）。</summary>
    public void SnapToTarget()
    {
        frame = GetFrame(useBuffer, true, DeadZoneMode.Actual);
        transform.position = new Vector3(frame.center.x, frame.center.y, transform.position.z);
        if (!float.IsNaN(frame.extents.y)) cam.orthographicSize = Mathf.Max(0.01f, frame.extents.y);
    }

    /// <summary>强制本帧重新计算取景框。</summary>
    public void ForceFrameUpdate()
    {
        frame = GetFrame(useBuffer, true, DeadZoneMode.Actual);
    }

    /// <summary>调整静止 / 移动两档最小取景框高度（换角色尺寸时可整体缩放）。</summary>
    public void SetFrameSizes(float still, float moving)
    {
        minFrameHeightStill = Mathf.Max(0.1f, still);
        minFrameHeightMoving = Mathf.Max(minFrameHeightStill, moving);
        minFrameHeight = minFrameHeightStill;
    }

    /// <summary>直接设置当前最小取景框高度。</summary>
    public void SetMinFrameHeight(float height)
    {
        minFrameHeight = Mathf.Max(0.1f, height);
    }

    public void AddTarget(Transform target)
    {
        if (target != null && !targets.Contains(target)) targets.Add(target);
    }

    public void AddTarget(Collider2D box)
    {
        if (box != null && !boxes.Contains(box)) boxes.Add(box);
    }

    public void RemoveTarget(Transform target)
    {
        if (target != null) targets.Remove(target);
    }

    public void RemoveTarget(Collider2D box)
    {
        if (box != null) boxes.Remove(box);
    }

    public bool HasTarget(Transform target) => target != null && targets.Contains(target);
    public bool HasTarget(Collider2D box) => box != null && boxes.Contains(box);

    public void ClearTargets()
    {
        targets.Clear();
        boxes.Clear();
    }

    /// <summary>推入一个临时取景请求，返回同一个 cue 方便驱动方后续刷新 / 释放。</summary>
    public CameraCue PushCue(CameraCue cue)
    {
        if (cue == null) return null;
        if (!cues.Contains(cue)) cues.Add(cue);
        return cue;
    }

    public CameraCue PushCue(Transform target, float sizeMultiplier, float blendIn = 0.2f, float blendOut = 0.3f)
    {
        CameraCue cue = new CameraCue(sizeMultiplier, blendIn, blendOut) { target = target };
        return PushCue(cue);
    }

    public void ReleaseCue(CameraCue cue)
    {
        if (cue != null) cue.Release();
    }
    #endregion

    #region 生命周期
    private void Awake()
    {
        cam = GetComponent<Camera>();
        Instance = this;

        if (cam != null)
        {
            originalSize = cam.orthographicSize;
            if (originalSize > 0.01f) minFrameHeightStill = Mathf.Max(minFrameHeightStill, originalSize * 1.2f);
        }
        originalPosition = transform.position;
        capturedOriginal = true;

        minFrameHeight = minFrameHeightStill;
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void Start()
    {
        ResolveBoundary();
        RefreshPlayer();
        RegisterSceneCameraTargets();

        if (player != null)
        {
            SnapToTarget();
            lastPlayerPosition = player.position;
            hasLastPlayerPosition = true;
        }
        else
        {
            ForceFrameUpdate();
        }

        if (autoAttachCannonCues) AttachCannonCues();
    }

    private void OnDisable()
    {
        for (int i = 0; i < cues.Count; i++) cues[i].Kill();
        cues.Clear();
        cuePoints.Clear();

        if (restoreOnDisable && capturedOriginal)
        {
            transform.position = originalPosition;
            if (cam != null) cam.orthographicSize = originalSize;
        }

        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        minFrameHeightMoving = Mathf.Max(minFrameHeightStill, minFrameHeightMoving);
        minFrameHeightStill = Mathf.Max(0.1f, minFrameHeightStill);
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) dt = Time.deltaTime;
        if (dt <= 0f) return;

        RefreshPlayer();
        TickCues(dt);
        CollectCuePoints();

        // 死区：用"没有死区、没有缓冲"的取景框算出以镜头为中心的矩形（与《超级鸡马》一致）
        if (useDeadZone)
        {
            Bounds raw = GetFrame(false, true, DeadZoneMode.ForceOff);
            deadZoneBounds.extents = new Vector3(
                raw.extents.x * deadZonePercentage.x,
                raw.extents.y * deadZonePercentage.y,
                0f);
            deadZoneBounds.center = transform.position;
        }

        UpdateMinFrameHeight(dt);
        frame = GetFrame(useBuffer, true, DeadZoneMode.Actual);

        if (player != null)
        {
            Vector2 playerPos = player.position;
            if (hasLastPlayerPosition &&
                Vector2.Distance(playerPos, lastPlayerPosition) > teleportSnapDistance)
            {
                // 传送（复活瞬移 / 出膛）：直接吸附，不插值
                SnapToTarget();
                lastPlayerPosition = playerPos;
                hasLastPlayerPosition = true;
                return;
            }
            lastPlayerPosition = playerPos;
            hasLastPlayerPosition = true;
        }

        if (!followTarget) return;

        MoveCamera(dt);
    }
    #endregion

    #region 目标 / 最小取景框
    private void RefreshPlayer()
    {
        if (player != null && !player.gameObject.activeInHierarchy) player = null;
        if (player != null) return;

        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go == null) return;

        player = go.transform;
        hasLastPlayerPosition = false;

        if (debugLogs) Debug.Log($"[{name}] 找到玩家 {player.name}");
    }

    /// <summary>把场景里挂了 CameraTarget 的对象自动登记为取景目标。</summary>
    private void RegisterSceneCameraTargets()
    {
        IReadOnlyList<CameraTarget> all = CameraTarget.All;
        for (int i = 0; i < all.Count; i++)
        {
            CameraTarget t = all[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            if (t.TryGetFrameBox(out Transform tr, out Collider2D box))
            {
                if (box != null) AddTarget(box);
                else AddTarget(tr);
            }
        }
    }

    /// <summary>
    /// 静止 / 移动两档之间平滑切换最小取景框高度：
    /// 静止时取景框更小（更近），一动起来就平滑放大（更远），停稳后再收回。
    /// </summary>
    private void UpdateMinFrameHeight(float dt)
    {
        Vector3 average = Vector3.zero;
        int count = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            Transform t = targets[i];
            if (t == null) continue;
            average += t.position;
            count++;
        }
        for (int i = 0; i < boxes.Count; i++)
        {
            Collider2D b = boxes[i];
            if (b == null) continue;
            average += b.transform.position;
            count++;
        }
        if (player != null)
        {
            average += player.position;
            count++;
        }

        if (count == 0) return;
        average /= count;

        float speed = dt > 0f ? (average - lastAverageOfTargets).magnitude / dt : 0f;
        lastAverageOfTargets = average;

        bool moving = speed > movingSpeedThreshold;
        float goal = moving ? minFrameHeightMoving : minFrameHeightStill;
        float rate = moving ? movingToMoveSpeed : movingToStillSpeed;
        minFrameHeight = Mathf.MoveTowards(minFrameHeight, goal, rate * dt);
    }
    #endregion

    #region 取景框求解（核心）
    /// <summary>
    /// 求当前取景框：
    /// 目标包围盒 → 叠加四向缓冲 → 并入死区 → 加上事件镜头的额外缩放
    /// → 按屏幕宽高比修正并套用最小取景框高度 → 钳制在关卡边界内。
    /// </summary>
    public Bounds GetFrame(bool withBuffer = true, bool adjusted = true,
        DeadZoneMode deadZoneMode = DeadZoneMode.Actual)
    {
        CleanupTargets();

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        bool any = false;

        void Include(float x, float y)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            any = true;
        }

        if (player != null) Include(player.position.x, player.position.y);

        for (int i = 0; i < targets.Count; i++)
        {
            Transform t = targets[i];
            if (t == null) continue;
            Vector3 p = t.position;
            // 与《超级鸡马》一致：位置为 (-1000,-1000) 的目标视为"离场"，不参与取景
            if (p.x == -1000f && p.y == -1000f) continue;
            Include(p.x, p.y);
        }

        for (int i = 0; i < boxes.Count; i++)
        {
            Collider2D b = boxes[i];
            if (b == null) continue;
            Bounds boxBounds = b.bounds;
            Include(boxBounds.min.x, boxBounds.min.y);
            Include(boxBounds.max.x, boxBounds.max.y);
        }

        for (int i = 0; i < cuePoints.Count; i++)
        {
            CuePoint cp = cuePoints[i];
            Include(cp.position.x - cp.radius, cp.position.y - cp.radius);
            Include(cp.position.x + cp.radius, cp.position.y + cp.radius);
        }

        // 没有目标：整关取景（与《超级鸡马》一致）
        if (!any)
        {
            if (hasBoundary)
            {
                minX = boundary.min.x;
                maxX = boundary.max.x;
                minY = boundary.min.y;
                maxY = boundary.max.y;
            }
            else
            {
                minX = transform.position.x;
                maxX = transform.position.x;
                minY = transform.position.y;
                maxY = transform.position.y;
            }
        }

        // 四向缓冲：按原始取景框尺寸的比例外扩（目标越散，留白越多）
        float rawWidth = maxX - minX;
        float rawHeight = maxY - minY;
        float bl = rawWidth * leftBuffer;
        float br = rawWidth * rightBuffer;
        float bt = rawHeight * topBuffer;
        float bb = rawHeight * bottomBuffer;

        // 并入死区（死区矩形本身不参与缓冲）
        if ((useDeadZone && deadZoneMode == DeadZoneMode.Actual) || deadZoneMode == DeadZoneMode.ForceOn)
        {
            minX = Mathf.Min(minX, deadZoneBounds.min.x);
            maxX = Mathf.Max(maxX, deadZoneBounds.max.x);
            minY = Mathf.Min(minY, deadZoneBounds.min.y);
            maxY = Mathf.Max(maxY, deadZoneBounds.max.y);
        }

        if (withBuffer)
        {
            minX -= bl;
            maxX += br;
            minY -= bb;
            maxY += bt;
        }

        float width = maxX - minX;
        float height = maxY - minY;
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;

        // 事件镜头要求的额外拉远：以取景框中心为基准整体放大
        if (extraZoom > 1.0001f)
        {
            width *= extraZoom;
            height *= extraZoom;
        }

        float aspect = cam != null ? Mathf.Max(0.01f, cam.aspect) : 16f / 9f;

        if (adjusted)
        {
            // 最小取景框：这才是"最近能拉多近"的真正限制
            float minHeight = minFrameHeight * minFrameHeightMultiplier;
            float minWidth = aspect * minHeight;

            if (width / Mathf.Max(0.0001f, height) >= aspect)
            {
                if (width < minWidth) width = minWidth;
                height = width / aspect;
            }
            else
            {
                if (height < minHeight) height = minHeight;
                width = height * aspect;
            }
        }

        // 钳制在关卡边界内：优先移动镜头，装不下就把取景框居中
        if (hasBoundary)
        {
            float bxMin = boundary.min.x;
            float bxMax = boundary.max.x;
            float byMin = boundary.min.y;
            float byMax = boundary.max.y;

            if (centerX - width * 0.5f < bxMin) centerX = bxMin + width * 0.5f;
            if (centerX + width * 0.5f > bxMax) centerX = bxMax - width * 0.5f;
            if (width > bxMax - bxMin) centerX = boundary.center.x;

            if (centerY - height * 0.5f < byMin) centerY = byMin + height * 0.5f;
            if (centerY + height * 0.5f > byMax) centerY = byMax - height * 0.5f;
            if (height > byMax - byMin) centerY = boundary.center.y;
        }

        return new Bounds(new Vector3(centerX, centerY, transform.position.z),
            new Vector3(width, height, 0f));
    }

    private void CleanupTargets()
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i] == null) targets.RemoveAt(i);
        }
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            if (boxes[i] == null) boxes.RemoveAt(i);
        }
    }
    #endregion

    #region 镜头运动
    /// <summary>
    /// 分轴逼近：距离越远逼近比例越大，并受 MaxFollowSpeed 限制。
    /// z 通道复用同一套比例来表示"缩放"的逼近速度——与《超级鸡马》完全一致。
    /// </summary>
    private void MoveCamera(float dt)
    {
        Vector3 center = frame.center;
        float targetHeight = frame.extents.y;
        float currentHeight = cam.orthographicSize;

        distance = center - transform.position;
        distance.z = targetHeight - currentHeight;

        if (!float.IsNaN(distance.y))
        {
            if (distForMaxApproach.sqrMagnitude < 1e-6f)
            {
                approachPercent = maxApproachPercent;
            }
            else
            {
                approachPercent.x = distForMaxApproach.x == 0f
                    ? maxApproachPercent.x
                    : Mathf.Lerp(minApproachPercent.x, maxApproachPercent.x,
                        Mathf.Abs(distance.x / distForMaxApproach.x));
                approachPercent.y = distForMaxApproach.y == 0f
                    ? maxApproachPercent.y
                    : Mathf.Lerp(minApproachPercent.y, maxApproachPercent.y,
                        Mathf.Abs(distance.y / distForMaxApproach.y));
                approachPercent.z = distForMaxApproach.z == 0f
                    ? maxApproachPercent.z
                    : Mathf.Lerp(minApproachPercent.z, maxApproachPercent.z,
                        Mathf.Abs(distance.z / distForMaxApproach.z));
            }
        }

        if (!smoothFollowCamOn)
        {
            Vector3 hard = Vector3.MoveTowards(transform.position, center, maxFollowSpeed * dt);
            hard.z = transform.position.z;
            transform.position = hard;
            cam.orthographicSize = Mathf.MoveTowards(currentHeight, targetHeight, maxZoomSpeed * dt);
            return;
        }

        // 缩放：按比例逼近，Clamp01 防止大步长越过目标
        float zoomStep = Mathf.Clamp01(approachPercent.z * dt);
        float newSize = currentHeight + (targetHeight - currentHeight) * zoomStep;
        if (!float.IsNaN(newSize)) cam.orthographicSize = Mathf.Max(0.01f, newSize);

        // 平移：按比例逼近 + 速度上限 + 不越过目标
        Vector3 step = Vector3.Scale(distance, approachPercent) * dt;
        float maxStep = maxFollowSpeed * dt;
        if (step.magnitude > maxStep) step = step.normalized * maxStep;
        if (step.magnitude > distance.magnitude) step = distance;
        step.z = 0f;

        Vector3 next = transform.position + step;
        if (!float.IsNaN(next.x) && !float.IsNaN(next.y))
        {
            next.z = transform.position.z;
            transform.position = next;
        }
    }
    #endregion

    #region 边界 / 事件镜头
    private void ResolveBoundary()
    {
        if (levelBounds == null) levelBounds = FindObjectOfType<CameraBounds2D>();

        if (levelBounds != null)
        {
            levelBounds.Rebuild();
            Rect r = levelBounds.WorldRect;
            if (r.width > 0f && r.height > 0f)
            {
                boundary = new Bounds(new Vector3(r.center.x, r.center.y, transform.position.z),
                    new Vector3(r.width, r.height, 0f));
                hasBoundary = true;
                if (debugLogs) Debug.Log($"[{name}] 使用 CameraBounds2D 边界：{boundary}");
                return;
            }
        }

        if (!autoBuildBoundsFromGround) return;

        int layer = LayerMask.NameToLayer(groundLayerName);
        if (layer < 0)
        {
            if (debugLogs) Debug.LogWarning($"[{name}] 找不到层「{groundLayerName}」，本次不限制镜头边界。");
            return;
        }

        if (CameraBounds2D.TryBuildFromLayer(layer, autoBoundsInset, out Rect rect))
        {
            boundary = new Bounds(new Vector3(rect.center.x, rect.center.y, transform.position.z),
                new Vector3(rect.width, rect.height, 0f));
            hasBoundary = true;
            if (debugLogs) Debug.Log($"[{name}] 由 Ground 层自动生成边界：{boundary}");
        }
        else if (debugLogs)
        {
            Debug.LogWarning($"[{name}] Ground 层没有可用的碰撞体，本次不限制镜头边界。");
        }
    }

    private void TickCues(float dt)
    {
        for (int i = cues.Count - 1; i >= 0; i--)
        {
            CameraCue cue = cues[i];
            if (cue == null)
            {
                cues.RemoveAt(i);
                continue;
            }

            cue.Tick(dt);
            if (cue.IsFinished) cues.RemoveAt(i);
        }
    }

    /// <summary>
    /// 把 cue 翻译成两种影响：临时取景点（参与取景框求包围盒）
    /// 和缩放倍率（extraZoom 拉远 / minFrameHeightMultiplier 拉近）。
    /// 取景点会按权重从玩家位置滑向目标，因此淡入淡出是连续的、不会跳。
    /// </summary>
    private void CollectCuePoints()
    {
        cuePoints.Clear();
        extraZoom = 1f;
        minFrameHeightMultiplier = 1f;

        Vector2 playerPos = player != null ? (Vector2)player.position : (Vector2)transform.position;

        for (int i = 0; i < cues.Count; i++)
        {
            CameraCue cue = cues[i];
            float w = cue.CurrentWeight;
            if (w <= 0.0001f) continue;

            float multiplier = Mathf.Lerp(1f, Mathf.Max(0.05f, cue.sizeMultiplier), w);
            if (multiplier > 1f) extraZoom = Mathf.Max(extraZoom, multiplier);
            else if (multiplier < 1f) minFrameHeightMultiplier = Mathf.Min(minFrameHeightMultiplier, multiplier);

            if (cue.target != null) AddCuePoint(playerPos, cue.target.position, w, cue);
            if (cue.hasWorldPoint) AddCuePoint(playerPos, cue.worldPoint, w, cue);
        }
    }

    private void AddCuePoint(Vector2 playerPos, Vector2 rawPoint, float weight, CameraCue cue)
    {
        Vector2 point = Vector2.Lerp(playerPos, rawPoint, weight);
        if (cue.worldOffset != Vector2.zero) point += cue.worldOffset * weight;

        cuePoints.Add(new CuePoint
        {
            position = point,
            radius = Mathf.Max(0.01f, cue.frameRadius.magnitude) * weight
        });
    }

    private void AttachCannonCues()
    {
        CannonLauncher[] cannons = FindObjectsOfType<CannonLauncher>();
        for (int i = 0; i < cannons.Length; i++)
        {
            CannonLauncher cannon = cannons[i];
            if (cannon == null) continue;
            if (cannon.GetComponent<CameraCueOnCannon>() == null)
            {
                cannon.gameObject.AddComponent<CameraCueOnCannon>();
                if (debugLogs) Debug.Log($"[{name}] 已为 {cannon.name} 自动挂上镜头事件组件。");
            }
        }
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Bounds view = GetCameraView();
        Bounds frameToDraw = Application.isPlaying ? frame : view;

        // 可视范围
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(view.center, view.size);

        // 取景框（含缓冲）
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(frameToDraw.center, frameToDraw.size);

        // 死区
        if (Application.isPlaying && useDeadZone)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(deadZoneBounds.center, deadZoneBounds.size);
        }

        // 关卡边界
        if (Application.isPlaying && hasBoundary)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireCube(boundary.center, boundary.size);
        }

        if (!Application.isPlaying) return;

        // 取景目标
        Gizmos.color = new Color(1f, 0.5f, 0.9f, 0.9f);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            Gizmos.DrawWireSphere(targets[i].position, 0.15f);
        }
        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i] == null) continue;
            Bounds b = boxes[i].bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }

        // cue 取景点
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.9f);
        for (int i = 0; i < cuePoints.Count; i++)
        {
            CuePoint cp = cuePoints[i];
            Gizmos.DrawWireSphere(new Vector3(cp.position.x, cp.position.y, 0f), Mathf.Max(0.05f, cp.radius));
        }
    }
    #endregion
}
