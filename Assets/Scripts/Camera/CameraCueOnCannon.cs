using UnityEngine;

/// <summary>
/// 把加农炮的状态翻译成镜头语言（自动挂在场景里的每一门炮上，无需手工配置）：
///
/// 蓄力：画面随蓄力比例逐渐拉远，并把炮口与预测落点一起框进画面，
///       于是"一边蓄力一边看见整条弹道"；
/// 发射：立刻松掉远景，切成贴身跟拍（画面更近），
///       同时让炮口在短暂时间里保持一个弱焦点，保证弧线起点还在画面内；
/// 落地：自动松掉跟拍，回到常态取景。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CannonLauncher))]
public class CameraCueOnCannon : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 绑定 ===")]
    [Tooltip("目标大炮（留空自动取同物体上的 CannonLauncher）")]
    [SerializeField] private CannonLauncher cannon;

    [Header("=== 蓄力拉远 ===")]
    [Tooltip("刚开始蓄力时的取景权重")]
    [SerializeField, Range(0f, 1f)] private float chargeMinWeight = 0.35f;
    [Tooltip("蓄满时的取景权重（越大越强势地框住弹道）")]
    [SerializeField, Range(0f, 1f)] private float chargeMaxWeight = 0.85f;
    [Tooltip("蓄满时相对近景的拉远倍率")]
    [SerializeField] private float chargeSizeMultiplier = 1.15f;
    [Tooltip("蓄力镜头的淡入时长")]
    [SerializeField] private float chargeBlendIn = 0.15f;
    [Tooltip("松手后远景的淡出时长")]
    [SerializeField] private float chargeBlendOut = 0.25f;
    [Tooltip("炮口 / 落点取景点的留白半径")]
    [SerializeField] private float chargeFrameRadius = 0.15f;

    [Header("=== 发射后贴身跟拍 ===")]
    [Tooltip("飞行期间相对近景的缩放倍率（<1 表示拉近）")]
    [SerializeField, Range(0.4f, 1.5f)] private float flightSizeMultiplier = 0.85f;
    [Tooltip("跟拍的淡入时长")]
    [SerializeField] private float flightBlendIn = 0.1f;
    [Tooltip("落地后跟拍的淡出时长")]
    [SerializeField] private float flightBlendOut = 0.4f;

    [Header("=== 调试 ===")]
    [SerializeField] private bool debugLogs = false;
    #endregion

    #region 运行时状态
    private CameraCue chargeCue;
    private CameraCue flightCue;
    private BunnyController playerBunny;
    #endregion

    private void Reset()
    {
        cannon = GetComponent<CannonLauncher>();
    }

    private void OnEnable()
    {
        if (cannon == null) cannon = GetComponent<CannonLauncher>();
        if (cannon != null) cannon.Fired += OnCannonFired;
    }

    private void OnDisable()
    {
        if (cannon != null) cannon.Fired -= OnCannonFired;

        ReleaseAll();
        chargeCue = null;
        flightCue = null;
    }

    private void Update()
    {
        ZoomCameraRig rig = ZoomCameraRig.Instance;
        if (rig == null || cannon == null) return;

        UpdateChargeCue(rig);
        UpdateFlightCue(rig);
    }

    #region 蓄力
    private void UpdateChargeCue(ZoomCameraRig rig)
    {
        if (cannon.IsCharging)
        {
            if (chargeCue == null)
            {
                chargeCue = rig.PushCue(new CameraCue(1f, chargeBlendIn, chargeBlendOut)
                {
                    target = cannon.Muzzle,
                    frameRadius = Vector2.one * Mathf.Max(0.01f, chargeFrameRadius)
                });

                if (debugLogs) Debug.Log($"[{name}] 进入蓄力取景。");
            }

            float ratio = cannon.ChargeRatio;
            chargeCue.targetWeight = Mathf.Lerp(chargeMinWeight, chargeMaxWeight, ratio);
            chargeCue.sizeMultiplier = Mathf.Lerp(1f, chargeSizeMultiplier, ratio);
            chargeCue.target = cannon.Muzzle;

            // 预测落点一起参与取景：蓄力越满，弹道越长，画面自然越远
            if (TryPredictLanding(out Vector2 landing))
            {
                chargeCue.worldPoint = landing;
                chargeCue.hasWorldPoint = true;
            }
            else
            {
                chargeCue.hasWorldPoint = false;
            }

            return;
        }

        if (chargeCue == null) return;

        chargeCue.Release();
        if (chargeCue.IsFinished)
        {
            chargeCue = null;
            if (debugLogs) Debug.Log($"[{name}] 蓄力取景已结束。");
        }
    }

    /// <summary>用与飞行完全一致的参数解算落点（平地 + 关卡边界钳制）。</summary>
    private bool TryPredictLanding(out Vector2 landing)
    {
        landing = Vector2.zero;

        ZoomCameraRig rig = ZoomCameraRig.Instance;
        if (rig == null) return false;

        float floorY;
        if (rig.TryGetLevelBounds(out Rect rect))
        {
            floorY = rect.yMin;
        }
        else if (rig.Player != null)
        {
            floorY = rig.Player.position.y;
        }
        else
        {
            return false;
        }

        Vector2 start = cannon.LaunchOrigin;
        Vector2 velocity = cannon.LaunchVelocity(cannon.ChargeRatio);
        float g = Mathf.Max(0.01f, cannon.LaunchGravity);

        float dy = start.y - floorY;
        float discriminant = velocity.y * velocity.y + 2f * g * dy;
        if (discriminant < 0f) return false;

        float t = (velocity.y + Mathf.Sqrt(discriminant)) / g;
        if (t <= 0f) return false;

        landing = new Vector2(start.x + velocity.x * t, floorY);

        if (rig.TryGetLevelBounds(out rect))
        {
            landing.x = Mathf.Clamp(landing.x, rect.xMin, rect.xMax);
        }

        return true;
    }
    #endregion

    #region 发射 / 飞行
    private void OnCannonFired(Vector2 velocity)
    {
        ZoomCameraRig rig = ZoomCameraRig.Instance;
        if (rig == null) return;

        if (chargeCue != null)
        {
            chargeCue.Release();
            chargeCue = null;
        }

        flightCue = rig.PushCue(new CameraCue(flightSizeMultiplier, flightBlendIn, flightBlendOut));
        flightCue.targetWeight = 1f;

        if (debugLogs) Debug.Log($"[{name}] 发射，切换到贴身跟拍。velocity={velocity}");
    }

    private void UpdateFlightCue(ZoomCameraRig rig)
    {
        if (flightCue != null)
        {
            if (playerBunny == null && rig.Player != null)
            {
                playerBunny = rig.Player.GetComponent<BunnyController>();
            }

            bool stillFlying = playerBunny != null && playerBunny.IsLaunched && !playerBunny.IsDeadState;
            if (!stillFlying) flightCue.Release();

            if (flightCue.IsFinished)
            {
                flightCue = null;
                if (debugLogs) Debug.Log($"[{name}] 飞行结束，跟拍收回。");
            }
        }
    }
    #endregion

    private void ReleaseAll()
    {
        if (chargeCue != null) chargeCue.Release();
        if (flightCue != null) flightCue.Release();
    }
}
