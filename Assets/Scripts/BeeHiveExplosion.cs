using System.Collections;
using UnityEngine;

/// <summary>
/// 蜂巢机关：
/// 玩家碰碰撞箱接触到蜂巢（PolygonCollider2D + Is Trigger）后，
/// 播放 BeeHiveExploded 序列帧动画（000→008）；
/// 动画播到 explode.005 这一帧时，把藏在蜂巢里的金币激活并从蜂巢里顶出来，
/// 随后金币由重力落回地面，落地后即可正常交互拾取。
///
/// 金币的“向上顶出”力度、水平偏移、下落重力全部在 Inspector 里可调，
/// 方便你反复试手感，不需要改代码。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BeeHiveExplosion : MonoBehaviour
{
    #region 可配置参数
    [Header("=== 引用 ===")]
    [Tooltip("蜂巢的 Animator（留空自动取同物体上的 Animator）")]
    [SerializeField] private Animator hiveAnimator;
    [Tooltip("藏在蜂巢里的金币（默认未激活）。留空时运行时会自动找离蜂巢最近的 CoinPickup")]
    [SerializeField] private GameObject hiddenCoin;
    [Tooltip("蜂群控制器（留空会自动在同物体 / 场景里查找；BeeSwarmSetup 会自动连线）")]
    [SerializeField] private BeeSwarmController beeSwarm;
    [Tooltip("蜂巢爆炸音效（TriggeredAudio2D）：被触碰引爆的第一帧播放一次性音效")]
    [SerializeField] private TriggeredAudio2D triggeredAudio;

    [Header("=== 爆炸动画 ===")]
    [Tooltip("Animator 中触发爆炸的 Trigger 参数名（BeeHiveSetup 会自动创建）")]
    [SerializeField] private string explodeTrigger = "Explode";
    [Tooltip("找不到同名 Trigger 时，直接播放的状态名（兜底）")]
    [SerializeField] private string explodeStateName = "BeeHiveExploded";
    [Tooltip("重置时回到的待机状态名（BeeHiveSetup 会创建 Idle）")]
    [SerializeField] private string idleStateName = "Idle";
    [Tooltip("爆炸播放速度：1=原始速度，0.5=半速，越小越慢。给蜜蜂 AI 留反应时间时调小")]
    [Range(0.05f, 2f)]
    [SerializeField] private float explosionSpeed = 0.5f;

    [Header("=== 金币弹出（自由调试）===")]
    [Tooltip("金币被顶出时向上的初速度（m/s）。不需要很大，3~6 就能蹦出蜂巢")]
    [SerializeField] private float coinLaunchUpwardSpeed = 4.5f;
    [Tooltip("金币水平初速度（m/s），正值向右；0 = 垂直向上")]
    [SerializeField] private float coinLaunchHorizontalSpeed = 0f;
    [Tooltip("在水平初速度上随机浮动的范围（±m/s），0 = 不随机")]
    [SerializeField] private float coinLaunchHorizontalRandom = 0f;
    [Tooltip("金币飞行时的重力缩放：越大下落越快、蹦得越低。作用在金币的 Rigidbody2D 上")]
    [SerializeField] private float coinGravityScale = 1.6f;
    [Tooltip("爆炸剪辑里 explode.005 的时间（秒）。兜底释放会按播放速度换算成真实时间")]
    [SerializeField] private float releaseClipTime = 0.5f;

    [Header("=== 碰撞 ===")]
    [Tooltip("关掉 Is Trigger，让蜂巢本体能挡住兔子；检测改用碰撞回调")]
    [SerializeField] private bool blockBunnyWithCollider = true;
    [Tooltip("触发后是否关闭蜂巢碰撞体，避免重复触发，也让玩家能穿过已炸毁的蜂巢")]
    [SerializeField] private bool disableColliderAfterTrigger = true;
    #endregion

    #region 运行时状态
    private Collider2D[] hiveColliders;
    private CoinPopOut coinPopOut;
    private bool exploded;
    private bool coinReleased;
    private SpriteRenderer hiveSpriteRenderer;
    private Sprite originalHiveSprite;
    private BunnyController bunny;
    private bool wasBunnyDead;
    private bool followStarted;
    #endregion

    /// <summary>蜂巢是否已经爆炸（供外部查询）。</summary>
    public bool HasExploded => exploded;

    private void Awake()
    {
        hiveColliders = GetComponents<Collider2D>();

        // 关键：关掉 Is Trigger，蜂巢本体会实实在在挡住兔子。
        // 检测改由 OnCollisionEnter2D / OnCollisionStay2D 负责（见下方）。
        if (blockBunnyWithCollider)
        {
            for (int i = 0; i < hiveColliders.Length; i++)
            {
                if (hiveColliders[i] != null) hiveColliders[i].isTrigger = false;
            }
        }

        if (hiveAnimator == null) hiveAnimator = GetComponent<Animator>();

        hiveSpriteRenderer = GetComponent<SpriteRenderer>();
        if (hiveSpriteRenderer != null)
            originalHiveSprite = hiveSpriteRenderer.sprite;

        bunny = FindObjectOfType<BunnyController>();
        if (bunny != null)
            wasBunnyDead = bunny.isDead;

        ResolveCoin();
        ResolveSwarm();
    }

    private void Update()
    {
        // 复活不是事件而是状态变化：检测 isDead 的下降沿。
        // 只要蜂巢已经爆炸过，玩家一复活就把它恢复成未爆炸状态。
        if (bunny == null)
        {
            bunny = FindObjectOfType<BunnyController>();
            if (bunny == null) return;
            wasBunnyDead = bunny.isDead;
            return;
        }

        bool dead = bunny.isDead;
        if (wasBunnyDead && !dead)
            ResetIfExploded();

        // 蜂巢已爆炸且追击音效尚未请求：等爆炸序列帧动画播到最后一帧后，
        // 开始蜂群追击音效（由 ProximityLoopAudio2D 的 FollowSwarm 模式持续播放）。
        if (exploded && !followStarted)
        {
            bool animEnded = hiveAnimator == null
                || hiveAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.999f;
            if (animEnded)
            {
                followStarted = true;
                ResolveSwarm();
                if (beeSwarm != null) beeSwarm.BeginFollowAudio();
            }
        }

        wasBunnyDead = dead;
    }

    // 实体碰撞：蜂巢挡住兔子时触发（现在的检测方式）
    private void OnCollisionEnter2D(Collision2D collision) => TryTrigger(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryTrigger(collision.collider);

    // 兼容：如果以后又给蜂巢加了独立的 Trigger 传感器，也能正常触发
    private void OnTriggerEnter2D(Collider2D other) => TryTrigger(other);
    private void OnTriggerStay2D(Collider2D other) => TryTrigger(other);

    #region 触发
    private void TryTrigger(Collider2D other)
    {
        if (exploded || other == null) return;

        // 用项目里统一的兔子查找逻辑，兼容碰撞体挂在兔子子物体上的情况
        BunnyController bunny = BunnyContactUtility.FindBunny(other);
        if (bunny == null || bunny.isDead) return;

        StartExplosion(other);
    }

    /// <summary>立即引爆（也可被外部机关 / 测试菜单调用）。重复调用安全。</summary>
    public void StartExplosion(Collider2D bunnyCollider = null)
    {
        if (exploded) return;
        exploded = true;

        // 爆炸第一帧就播放蜂巢爆裂音效（gore）。
        if (triggeredAudio != null) triggeredAudio.PlayDefaultOneshot();

        if (hiveColliders == null) hiveColliders = GetComponents<Collider2D>();

        if (disableColliderAfterTrigger)
        {
            for (int i = 0; i < hiveColliders.Length; i++)
            {
                if (hiveColliders[i] != null) hiveColliders[i].enabled = false;
            }
        }

        ResolveCoin();

        // 让金币忽略触发蜂巢的那只兔子，避免刚弹出就被兔子的碰撞箱撞歪
        if (coinPopOut != null)
            coinPopOut.IgnoreCollisionWith(bunnyCollider);

        PlayExplosion();

        // 爆炸一开始就把蜂巢上的预警蜜蜂藏起来，避免它们爬在已经碎裂的蜂巢上。
        ResolveSwarm();
        if (beeSwarm != null)
            beeSwarm.HideIndicatorBees();

        // 兜底：就算动画事件没有被正确写入剪辑，也按 explode.005 的时间点放出金币
        StartCoroutine(ReleaseCoinFallback());
    }

    #region 重置
    /// <summary>
    /// 玩家复活时调用：如果蜂巢已经爆炸过，就恢复成未爆炸状态。
    /// 蜂巢动画/碰撞、金币、蜂群会一起重置，让玩家可以再次挑战。
    /// </summary>
    public void ResetHive()
    {
        StopAllCoroutines();

        exploded = false;
        coinReleased = false;
        followStarted = false;

        if (hiveColliders == null)
            hiveColliders = GetComponents<Collider2D>();

        for (int i = 0; i < hiveColliders.Length; i++)
        {
            if (hiveColliders[i] == null) continue;

            hiveColliders[i].enabled = true;
            if (blockBunnyWithCollider)
                hiveColliders[i].isTrigger = false;
        }

        if (hiveAnimator != null)
        {
            hiveAnimator.speed = 1f;

            if (!string.IsNullOrEmpty(explodeTrigger))
                hiveAnimator.ResetTrigger(explodeTrigger);

            hiveAnimator.Rebind();

            if (!string.IsNullOrEmpty(idleStateName))
                hiveAnimator.Play(idleStateName, 0, 0f);

            hiveAnimator.Update(0f);
        }

        // Animator 的 Idle 状态没有动画帧，这里显式把蜂巢贴图恢复成完整状态。
        if (hiveSpriteRenderer != null && originalHiveSprite != null)
            hiveSpriteRenderer.sprite = originalHiveSprite;

        ResolveCoin();
        if (coinPopOut != null)
            coinPopOut.ResetCoin();

        ResolveSwarm();
        if (beeSwarm != null)
            beeSwarm.ResetSwarm();
    }

    private void ResetIfExploded()
    {
        if (!exploded) return;
        ResetHive();
    }
    #endregion

    private void PlayExplosion()
    {
        if (hiveAnimator == null)
        {
            Debug.LogWarning($"[{name}] 缺少 Animator，蜂巢不会播放爆炸动画，但金币仍会按兜底时间弹出。");
            return;
        }

        // 放慢/加快爆炸：Animator.speed 是全局倍率，动画事件仍会精确落在 explode.005
        hiveAnimator.speed = Mathf.Max(0.01f, explosionSpeed);

        if (HasTriggerParameter(explodeTrigger))
        {
            hiveAnimator.SetTrigger(explodeTrigger);
        }
        else if (!string.IsNullOrEmpty(explodeStateName))
        {
            // 没有 Trigger 时直接重播爆炸状态（默认状态是 Idle，不会自动播放爆炸）
            hiveAnimator.Play(explodeStateName, 0, 0f);
        }
    }

    private bool HasTriggerParameter(string triggerName)
    {
        if (hiveAnimator == null || string.IsNullOrEmpty(triggerName)) return false;

        AnimatorControllerParameter[] parameters = hiveAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger &&
                parameters[i].name == triggerName)
            {
                return true;
            }
        }
        return false;
    }
    #endregion

    #region 金币释放
    /// <summary>
    /// 由 BeeHiveExploded 剪辑上的动画事件在 explode.005 帧调用：激活并顶出金币。
    /// 重复调用只会生效一次。
    /// </summary>
    public void ReleaseCoin()
    {
        if (coinReleased) return;
        coinReleased = true;

        ResolveCoin();
        ResolveSwarm();

        // 蜜蜂和金币在 explode.005 同一时刻开始出现。
        if (beeSwarm != null)
            beeSwarm.BeginEmergence();

        if (coinPopOut == null)
        {
            Debug.LogWarning($"[{name}] 没找到金币（CoinPopOut），无法弹出金币。");
            return;
        }

        float horizontalSpeed = coinLaunchHorizontalSpeed;
        if (coinLaunchHorizontalRandom > 0f)
            horizontalSpeed += Random.Range(-coinLaunchHorizontalRandom, coinLaunchHorizontalRandom);

        coinPopOut.PopOut(coinLaunchUpwardSpeed, horizontalSpeed, coinGravityScale);
    }

    private IEnumerator ReleaseCoinFallback()
    {
        // 按动画播放倍率把剪辑时间换算成真实时间，保证放慢爆炸后仍然同步在 explode.005 放出金币
        float speed = Mathf.Max(0.01f, explosionSpeed);
        if (releaseClipTime > 0f)
            yield return new WaitForSeconds(releaseClipTime / speed);

        ReleaseCoin();
    }

    /// <summary>解析 hiddenCoin / coinPopOut：优先用 Inspector 指定的金币，否则找离蜂巢最近的 CoinPickup。</summary>
    private void ResolveCoin()
    {
        if (hiddenCoin != null)
        {
            if (coinPopOut == null)
                coinPopOut = hiddenCoin.GetComponent<CoinPopOut>();
            return;
        }

        CoinPickup[] coins = FindObjectsOfType<CoinPickup>(true);
        CoinPickup nearest = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < coins.Length; i++)
        {
            if (coins[i] == null) continue;

            float sqrDistance = (coins[i].transform.position - transform.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = coins[i];
            }
        }

        if (nearest != null)
        {
            hiddenCoin = nearest.gameObject;
            coinPopOut = hiddenCoin.GetComponent<CoinPopOut>();
        }
    }

    /// <summary>解析 beeSwarm：优先 Inspector 指定，否则在同物体 / 场景中查找。</summary>
    private void ResolveSwarm()
    {
        if (beeSwarm != null) return;

        beeSwarm = GetComponent<BeeSwarmController>();
        if (beeSwarm == null)
            beeSwarm = FindObjectOfType<BeeSwarmController>();
    }
    #endregion

    #region 调试
    [ContextMenu("立即引爆蜂巢")]
    private void DebugExplode()
    {
        StartExplosion(null);
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D[] colliders = hiveColliders != null && hiveColliders.Length > 0
            ? hiveColliders
            : GetComponents<Collider2D>();
        if (colliders == null) return;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null) continue;
            Gizmos.DrawWireCube(colliders[i].bounds.center, colliders[i].bounds.size);
        }
    }
    #endregion
}
