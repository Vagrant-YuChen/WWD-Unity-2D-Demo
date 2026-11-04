using System;
using UnityEngine;

/// <summary>
/// 蜂巢蜂群控制器（挂在蜂巢物体上即可）。
///
/// 玩法对应：
///   1) 未爆炸前：在蜂巢上生成若干“指示蜜蜂”，不规律地爬动，作为危险预警；
///   2) 蜂巢爆炸到 explode.005（金币弹出的同一帧）：开始逐只放出蜜蜂；
///   3) 全部蜜蜂出巢后：蜂群中心才开始追兔子；碰到兔子直接 TriggerDeath；
///   4) 整团蜂云只用一个半径判定，不做单只蜜蜂伤害；
///   5) 玩家碰到旗帜胜利、时间被冻结后蜂群停止。
///
/// 群体运动参考了“蜂群中心 + 历史位置 + 每只不同延迟 + Perlin 噪声”的思路：
///   蜂群中心朝兔子移动，每只蜜蜂去追中心“若干帧前”的位置，
///   再叠加噪声漫游，所以看起来是一团会拖尾、会绕圈的蜂云，而不是 50 只同步飞行。
/// </summary>
public class BeeSwarmController : MonoBehaviour
{
    #region 可配置参数
    // 用于一次性迁移旧场景里的默认值，避免以后每次编译都覆盖你的手动设置。
    [SerializeField, HideInInspector] private int configMigrationVersion = 0;

    [Header("=== 引用 ===")]
    [Tooltip("蜂巢的 SpriteRenderer；留空会自动取同物体 / 场景里的蜂巢")]
    [SerializeField] private SpriteRenderer hiveRenderer;
    [Tooltip("三组蜜蜂动画机（BeeSwarmSetup 会自动从 Sprite 文件夹生成并填进来）")]
    [SerializeField] private RuntimeAnimatorController[] beeAnimators = new RuntimeAnimatorController[0];
    [Tooltip("三组动画的首帧，用来在 Animator 启动前避免闪白（可留空）")]
    [SerializeField] private Sprite[] beePreviewSprites = new Sprite[0];

    [Header("=== 数量 / 出巢时长 ===")]
    [Tooltip("蜜蜂总数（你的玩法是 50 只）")]
    [SerializeField] private int beeCount = 50;
    [Tooltip("从第一只到最后一只全部出巢的时间（秒）。调大 = 玩家有更多逃跑时间")]
    [SerializeField] private float emergeDuration = 2.5f;
    [Tooltip("出巢节奏随机程度：0=整齐均匀，1=完全随机、会一阵一阵地冒出来")]
    [Range(0f, 1f)]
    [SerializeField] private float emergeBurstiness = 0.65f;

    [Header("=== 蜂群追逐 ===")]
    [Tooltip("蜂群中心追兔子的速度。要略快于兔子冲刺速度，才能保证玩家甩不掉")]
    [SerializeField] private float swarmSpeed = 11f;
    [Tooltip("全部出巢后开启的“整团蜂云”判定半径。只有这一个判定，单只蜜蜂不伤人")]
    [SerializeField] private float catchRadius = 0.6f;
    [Tooltip("单只蜜蜂最大速度，必须大于 swarmSpeed，否则尾巴跟不上")]
    [SerializeField] private float maxBeeSpeed = 18f;
    [Tooltip("蜜蜂速度跟随目标速度的响应快慢（越大越黏，越小越飘）")]
    [SerializeField] private float steerResponsiveness = 6f;
    [Tooltip("贴近目标点时开始减速的半径，避免蜜蜂在中心点疯狂抖动")]
    [SerializeField] private float arriveRadius = 0.35f;
    [Tooltip("蜜蜂自身随机游走的速度（让蜂群像一团乱飞，而不是一条直线）")]
    [SerializeField] private float wanderSpeed = 2.5f;
    [Tooltip("Perlin 噪声控制朝向变化的速度")]
    [SerializeField] private float headingNoiseSpeed = 0.5f;
    [Tooltip("Perlin 噪声控制强度变化的速度")]
    [SerializeField] private float magnitudeNoiseSpeed = 3f;
    [Tooltip("朝向噪声的扰动强度")]
    [SerializeField] private float headingNoiseStrength = 0.6f;
    [Tooltip("历史位置记录长度（物理帧）。100 足够做出拖尾")]
    [SerializeField] private int historySize = 100;
    [Tooltip("最后面的蜜蜂追多少帧前的位置；越大尾巴越长")]
    [SerializeField] private int maxDelayFrames = 12;
    [Tooltip("0 = 最前面的蜜蜂，1 = 最后面的蜜蜂；控制尾巴形状")]
    [SerializeField] private AnimationCurve followDelayCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("蜜蜂是否朝运动方向旋转（关掉就只做平移）")]
    [SerializeField] private bool rotateToVelocity = true;
    [Tooltip("旋转偏移角。如果蜜蜂贴图默认朝向不是右边，用这个修正")]
    [SerializeField] private float rotationOffsetDegrees = 0f;

    [Header("=== 外观 / 出巢表现 ===")]
    [Tooltip("蜜蜂缩放。蜂巢缩放是 0.4，蜜蜂建议 0.35~0.5")]
    [SerializeField] private float beeScale = 0.4f;
    [Tooltip("蜜蜂渲染在蜂巢前面的 Sorting Order 偏移")]
    [SerializeField] private int sortingOrderOffset = 4;
    [Tooltip("出巢时从蜂巢中心随机散开的半径")]
    [SerializeField] private float spawnRadius = 0.08f;
    [Tooltip("出巢瞬间向外飞出的初速度，让蜜蜂像“炸出来”一样")]
    [SerializeField] private float spawnOutwardSpeed = 2.5f;
    [Tooltip("蜂群中心相对蜂巢中心的偏移（默认稍微往上一点）")]
    [SerializeField] private Vector2 swarmCenterOffset = new Vector2(0f, 0.05f);

    [Header("=== 玩家死亡后的消散 ===")]
    [Tooltip("玩家死亡后，蜂群逐只开始消散的时间窗口（秒）。最后一只还会额外花 vanishPopTime 缩小。建议小于兔子复活时间（默认 3 秒）")]
    [Min(0.01f)]
    [SerializeField] private float vanishDuration = 1.5f;
    [Tooltip("消散节奏随机程度：0 = 均匀逐只，1 = 完全随机、一阵一阵地消失")]
    [Range(0f, 1f)]
    [SerializeField] private float vanishBurstiness = 0.65f;
    [Tooltip("每只蜜蜂从开始消失到完全消失的耗时（缩小 + 飞走）")]
    [Min(0.01f)]
    [SerializeField] private float vanishPopTime = 0.18f;
    [Tooltip("轮到自己消失的蜜蜂向外/向上飞走的速度")]
    [Min(0f)]
    [SerializeField] private float vanishFlySpeed = 3.5f;
    [Tooltip("还没轮到消失的蜜蜂缓慢散开的速度，让整团蜂云看起来在离开")]
    [Min(0f)]
    [SerializeField] private float vanishScatterSpeed = 1.2f;

    [Header("=== 蜂巢上的指示蜜蜂（预警）===")]
    [Tooltip("未爆炸前在蜂巢上爬的蜜蜂数量；0 = 关闭预警")]
    [SerializeField] private int indicatorCount = 5;
    [Tooltip("指示蜜蜂缩放，通常比蜂群蜜蜂稍大一点才看得清")]
    [SerializeField] private float indicatorScale = 0.45f;
    [Tooltip("指示蜜蜂爬行速度")]
    [SerializeField] private float indicatorSpeed = 0.35f;
    [Tooltip("指示蜜蜂重新选目标点的最短 / 最长时间（秒）")]
    [SerializeField] private Vector2 indicatorRetargetInterval = new Vector2(0.8f, 2f);
    [Tooltip("指示蜜蜂活动范围占蜂巢精灵的比例（0.7 = 四周各缩进 15%）")]
    [Range(0.3f, 1f)]
    [SerializeField] private float indicatorAreaScale = 0.7f;

    [Header("=== 调试 ===")]
    [Tooltip("选中时在 Scene 视图画出判定半径 / 蜂巢活动范围")]
    [SerializeField] private bool drawGizmos = true;
    #endregion

    #region 运行时状态
    private BunnyController bunny;

    private Transform beeContainer;
    private Transform[] beeTransforms;
    private SpriteRenderer[] beeRenderers;
    private Animator[] beeAnimatorsInst;
    private Vector2[] beeVelocities;
    private float[] beeHeadings;
    private float[] beeMagnitudes;
    private bool[] beeActive;
    private float[] beeVanishAt;
    private bool[] beeVanishing;
    private float[] beeVanishTimer;
    private Vector2[] beeVanishVelocity;

    private int spawnedCount;
    private float emergeElapsed;
    private float[] spawnTimes;
    private int nextSpawnIndex;

    private bool started;
    private bool allOut;
    private bool stopped;

    private bool retreating;
    private bool swarmGone;
    private float retreatElapsed;

    private Vector2 swarmCenter;
    private Vector2[] history;
    private int historyIndex;

    private Transform indicatorContainer;
    private Transform[] indicatorTransforms;
    private SpriteRenderer[] indicatorRenderers;
    private Vector2[] indicatorTargets;
    private float[] indicatorTimers;
    private Bounds indicatorBounds;

    private float noiseSeed;
    private bool missingHive;
    #endregion

    #region 生命周期
    private void Reset()
    {
        followDelayCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    private void Awake()
    {
        ResolveHive();

        if (hiveRenderer == null)
        {
            missingHive = true;
            Debug.LogError($"[{name}] 找不到蜂巢 SpriteRenderer，蜂群不会工作。请把蜂巢拖到 Hive Renderer 或把本脚本挂在蜂巢上。", this);
            return;
        }

        if (followDelayCurve == null || followDelayCurve.length == 0)
            followDelayCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        noiseSeed = UnityEngine.Random.Range(0f, 10000f);
        swarmCenter = GetHiveSpawnPoint();

        bunny = FindObjectOfType<BunnyController>();

        InitHistory();
        CreateSwarmBees();
        CreateIndicatorBees();
    }

    private void FixedUpdate()
    {
        if (missingHive || stopped) return;
        if (Time.timeScale <= 0f) return;

        float dt = Time.fixedDeltaTime;

        // 任何死法（蜜蜂、割草机、滚石、流沙、箭……）都会把 isDead 置 true。
        UpdatePlayerDeathState();

        // 蜂群已经全部消散：玩家复活后是安全的，不再追也不再判定。
        if (swarmGone) return;

        // 还没爆炸：只让蜂巢上的指示蜜蜂爬行。
        if (!started)
        {
            UpdateIndicatorBees(dt);
            return;
        }

        // 正在消散：停止出巢、追逐和判定，只推进逐只消失。
        if (retreating)
        {
            UpdateRetreat(dt);
            UpdateSwarmBees(dt);
            if (!AnyBeeActive())
            {
                retreating = false;
                swarmGone = true;
            }
            return;
        }

        // 出巢阶段：按预先生成的随机时间表逐只激活。
        if (!allOut)
        {
            emergeElapsed += dt;
            while (nextSpawnIndex < beeCount && emergeElapsed >= spawnTimes[nextSpawnIndex])
            {
                ActivateNextBee();
                nextSpawnIndex++;
            }
        }

        // 只有全部出巢后，蜂群中心才开始追人。
        if (allOut)
            MoveSwarmCenter(dt);

        UpdateHistory();
        UpdateSwarmBees(dt);

        if (allOut)
            CheckCatch();
    }

    private void OnDestroy()
    {
        if (beeContainer != null) Destroy(beeContainer.gameObject);
        if (indicatorContainer != null) Destroy(indicatorContainer.gameObject);
    }
    #endregion

    #region 引用 / 初始化
    private void ResolveHive()
    {
        if (hiveRenderer == null)
            hiveRenderer = GetComponent<SpriteRenderer>();

        if (hiveRenderer == null)
        {
            GameObject named = GameObject.Find("Beehive-Beehive.000");
            if (named != null) hiveRenderer = named.GetComponent<SpriteRenderer>();
        }
    }

    private Vector2 GetHiveSpawnPoint()
    {
        if (hiveRenderer == null) return transform.position;
        return (Vector2)hiveRenderer.bounds.center + swarmCenterOffset;
    }

    private void InitHistory()
    {
        history = new Vector2[Mathf.Max(1, historySize)];
        for (int i = 0; i < history.Length; i++)
            history[i] = swarmCenter;
        historyIndex = 0;
    }

    private void CreateSwarmBees()
    {
        beeContainer = new GameObject("BeeSwarmBees").transform;
        beeContainer.position = Vector3.zero;

        int count = Mathf.Max(0, beeCount);
        beeTransforms = new Transform[count];
        beeRenderers = new SpriteRenderer[count];
        beeAnimatorsInst = new Animator[count];
        beeVelocities = new Vector2[count];
        beeHeadings = new float[count];
        beeMagnitudes = new float[count];
        beeActive = new bool[count];
        beeVanishAt = new float[count];
        beeVanishing = new bool[count];
        beeVanishTimer = new float[count];
        beeVanishVelocity = new Vector2[count];

        int sortingOrder = hiveRenderer.sortingOrder + sortingOrderOffset;
        string sortingLayer = hiveRenderer.sortingLayerName;

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Bee_{i:00}");
            go.transform.SetParent(beeContainer, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * beeScale;
            go.transform.rotation = Quaternion.identity;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = sortingOrder + (i % 3);

            if (beePreviewSprites != null && beePreviewSprites.Length > 0)
                sr.sprite = beePreviewSprites[i % beePreviewSprites.Length];

            Animator anim = null;
            if (beeAnimators != null && beeAnimators.Length > 0)
            {
                RuntimeAnimatorController controller = beeAnimators[i % beeAnimators.Length];
                if (controller != null)
                {
                    anim = go.AddComponent<Animator>();
                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    anim.speed = UnityEngine.Random.Range(0.85f, 1.15f);
                }
            }

            go.SetActive(false);

            beeTransforms[i] = go.transform;
            beeRenderers[i] = sr;
            beeAnimatorsInst[i] = anim;
            beeMagnitudes[i] = UnityEngine.Random.Range(0.2f, 1f);
        }
    }

    private void CreateIndicatorBees()
    {
        if (indicatorCount <= 0) return;

        indicatorContainer = new GameObject("BeehiveIndicatorBees").transform;
        indicatorContainer.position = Vector3.zero;

        Bounds hiveBounds = hiveRenderer.bounds;
        indicatorBounds = new Bounds(hiveBounds.center, hiveBounds.size * indicatorAreaScale);

        indicatorTransforms = new Transform[indicatorCount];
        indicatorRenderers = new SpriteRenderer[indicatorCount];
        indicatorTargets = new Vector2[indicatorCount];
        indicatorTimers = new float[indicatorCount];

        int sortingOrder = hiveRenderer.sortingOrder + sortingOrderOffset + 6;
        string sortingLayer = hiveRenderer.sortingLayerName;

        for (int i = 0; i < indicatorCount; i++)
        {
            GameObject go = new GameObject($"IndicatorBee_{i:00}");
            go.transform.SetParent(indicatorContainer, false);
            go.transform.localScale = Vector3.one * indicatorScale;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = sortingOrder;

            if (beePreviewSprites != null && beePreviewSprites.Length > 0)
                sr.sprite = beePreviewSprites[i % beePreviewSprites.Length];

            Animator anim = null;
            if (beeAnimators != null && beeAnimators.Length > 0)
            {
                RuntimeAnimatorController controller = beeAnimators[i % beeAnimators.Length];
                if (controller != null)
                {
                    anim = go.AddComponent<Animator>();
                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    anim.speed = UnityEngine.Random.Range(0.8f, 1.2f);
                }
            }

            Vector2 pos = RandomPointInBounds(indicatorBounds);
            go.transform.position = pos;

            indicatorTransforms[i] = go.transform;
            indicatorRenderers[i] = sr;
            indicatorTargets[i] = RandomPointInBounds(indicatorBounds);
            indicatorTimers[i] = UnityEngine.Random.Range(
                indicatorRetargetInterval.x, indicatorRetargetInterval.y);
        }
    }
    #endregion

    #region 出巢
    /// <summary>
    /// 由 BeeHiveExplosion 在 explode.005（金币弹出的同一时刻）调用。
    /// 重复调用安全。
    /// </summary>
    public void BeginEmergence()
    {
        if (missingHive || started) return;

        started = true;
        allOut = false;
        stopped = false;
        swarmCenter = GetHiveSpawnPoint();
        emergeElapsed = 0f;
        spawnedCount = 0;

        for (int i = 0; i < history.Length; i++)
            history[i] = swarmCenter;
        historyIndex = 0;

        HideIndicatorBees();

        BuildSpawnSchedule();

        // 第一只在当前帧就出现，和金币弹出的时间对齐。
        if (beeCount > 0)
        {
            ActivateNextBee();
            nextSpawnIndex = 1;
        }
        else
        {
            allOut = true;
            nextSpawnIndex = 0;
        }
    }

    private void BuildSpawnSchedule()
    {
        int count = Mathf.Max(0, beeCount);
        spawnTimes = new float[count];
        if (count == 0) return;

        float evenStep = emergeDuration / count;
        for (int i = 0; i < count; i++)
        {
            float even = (i + 1) * evenStep;
            float random = UnityEngine.Random.value * emergeDuration;
            spawnTimes[i] = Mathf.Lerp(even, random, emergeBurstiness);
        }

        // 第一只固定在 0 秒，保证和 005 帧同时冒出来。
        spawnTimes[0] = 0f;
        Array.Sort(spawnTimes);
    }

    private void ActivateNextBee()
    {
        if (spawnedCount >= beeCount) return;

        int index = spawnedCount;
        spawnedCount++;

        beeActive[index] = true;
        beeVelocities[index] = Vector2.zero;
        beeHeadings[index] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        Vector2 pos = swarmCenter + UnityEngine.Random.insideUnitCircle * spawnRadius;
        beeTransforms[index].position = pos;
        beeTransforms[index].rotation = Quaternion.identity;
        beeTransforms[index].localScale = Vector3.one * beeScale;
        beeTransforms[index].gameObject.SetActive(true);

        Vector2 outward = UnityEngine.Random.insideUnitCircle.normalized;
        if (outward.sqrMagnitude < 0.01f) outward = Vector2.up;
        beeVelocities[index] = outward * spawnOutwardSpeed + Vector2.up * 1.2f;

        if (spawnedCount >= beeCount)
            allOut = true;
    }
    #endregion

    #region 蜂群追逐
    private void MoveSwarmCenter(float dt)
    {
        if (bunny == null)
            bunny = FindObjectOfType<BunnyController>();

        if (bunny == null) return;

        Vector2 target = bunny.transform.position;
        swarmCenter = Vector2.MoveTowards(swarmCenter, target, swarmSpeed * dt);
    }

    private void UpdateHistory()
    {
        if (history == null || history.Length == 0) return;

        historyIndex++;
        if (historyIndex >= history.Length)
            historyIndex = 0;

        history[historyIndex] = swarmCenter;
    }

    private Vector2 BeePastPosition(int framesInPast)
    {
        framesInPast = Mathf.Clamp(framesInPast, 0, history.Length - 1);
        int index = historyIndex - framesInPast;
        if (index < 0) index += history.Length;
        return history[index];
    }

    private void UpdateSwarmBees(float dt)
    {
        if (beeTransforms == null) return;

        float invCount = 1f / Mathf.Max(1, beeCount - 1);

        for (int i = 0; i < beeCount; i++)
        {
            if (!beeActive[i]) continue;

            Vector2 pos = beeTransforms[i].position;

            // 玩家死亡后：蜂群不再追人，而是边散开边逐只消失。
            if (retreating)
            {
                if (beeVanishing[i])
                {
                    beeVanishTimer[i] -= dt;
                    float remain = Mathf.Clamp01(
                        beeVanishTimer[i] / Mathf.Max(0.01f, vanishPopTime));
                    beeTransforms[i].localScale = Vector3.one * (beeScale * remain);

                    pos += beeVanishVelocity[i] * dt;
                    beeTransforms[i].position = pos;
                    ApplyBeeRotation(i);

                    if (beeVanishTimer[i] <= 0f)
                    {
                        beeActive[i] = false;
                        beeTransforms[i].gameObject.SetActive(false);
                    }

                    continue;
                }

                // 还没轮到自己消失的蜜蜂：缓慢向外/向上散开，让整团蜂云看起来在离开。
                Vector2 away = pos - swarmCenter;
                if (away.sqrMagnitude < 0.0001f)
                    away = UnityEngine.Random.insideUnitCircle;
                if (away.sqrMagnitude < 0.0001f)
                    away = Vector2.up;

                Vector2 fleeDir = (away.normalized + Vector2.up * 0.6f).normalized;
                Vector2 desiredScatter = fleeDir * vanishScatterSpeed;

                float scatterBlend = Mathf.Clamp01(steerResponsiveness * dt);
                beeVelocities[i] = Vector2.Lerp(beeVelocities[i], desiredScatter, scatterBlend);

                pos += beeVelocities[i] * dt;
                beeTransforms[i].position = pos;
                ApplyBeeRotation(i);
                continue;
            }

            float ratio = i * invCount;
            int delay = Mathf.RoundToInt(followDelayCurve.Evaluate(ratio) * maxDelayFrames);
            Vector2 target = BeePastPosition(delay);

            Vector2 toTarget = target - pos;
            float distance = toTarget.magnitude;

            // 到点前满速追，靠近后减速，避免在中心点抖成筛子。
            float arriveMul = distance <= 0.0001f
                ? 0f
                : Mathf.Clamp01(distance / Mathf.Max(0.01f, arriveRadius));
            Vector2 seekVelocity = distance <= 0.0001f
                ? Vector2.zero
                : (toTarget / distance) * (maxBeeSpeed * arriveMul);

            // Perlin 噪声：平滑变化的朝向 + 强度，形成有机的乱飞。
            float headingNoise = Mathf.PerlinNoise(
                noiseSeed + Time.time * headingNoiseSpeed + i * 23f, i * 3.7f);
            beeHeadings[i] += (headingNoise - 0.5f) * headingNoiseStrength;

            float magnitudeNoise = Mathf.PerlinNoise(
                noiseSeed + Time.time * magnitudeNoiseSpeed + i * 95f, i * 7.1f);
            beeMagnitudes[i] = magnitudeNoise;

            Vector2 wander = new Vector2(
                Mathf.Cos(beeHeadings[i]),
                Mathf.Sin(beeHeadings[i])) * (wanderSpeed * beeMagnitudes[i]);

            Vector2 desiredVelocity = seekVelocity + wander;
            desiredVelocity = Vector2.ClampMagnitude(desiredVelocity, maxBeeSpeed);

            float blend = Mathf.Clamp01(steerResponsiveness * dt);
            beeVelocities[i] += (desiredVelocity - beeVelocities[i]) * blend;
            beeVelocities[i] = Vector2.ClampMagnitude(beeVelocities[i], maxBeeSpeed);

            pos += beeVelocities[i] * dt;
            beeTransforms[i].position = pos;
            ApplyBeeRotation(i);
        }
    }

    private void ApplyBeeRotation(int i)
    {
        if (!rotateToVelocity || beeVelocities[i].sqrMagnitude <= 0.0001f) return;

        float angle = Mathf.Atan2(beeVelocities[i].y, beeVelocities[i].x)
            * Mathf.Rad2Deg + rotationOffsetDegrees;
        beeTransforms[i].rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void CheckCatch()
    {
        if (beeCount <= 0) return;
        if (retreating || swarmGone) return;

        if (bunny == null)
            bunny = FindObjectOfType<BunnyController>();

        if (bunny == null || bunny.isDead) return;

        Collider2D bunnyCollider = bunny.GetComponent<Collider2D>();
        Vector2 closest = bunnyCollider != null
            ? (Vector2)bunnyCollider.bounds.ClosestPoint(swarmCenter)
            : (Vector2)bunny.transform.position;

        if ((closest - swarmCenter).sqrMagnitude <= catchRadius * catchRadius)
        {
            // 整团蜂云只做这一个判定，单只蜜蜂不单独伤害。
            bunny.TriggerDeath();
        }
    }

    #region 玩家死亡后的消散
    private void UpdatePlayerDeathState()
    {
        if (bunny == null)
        {
            bunny = FindObjectOfType<BunnyController>();
            if (bunny == null) return;
        }

        // 不用上升沿：只要玩家处于死亡状态且蜂群已经开始，就保证进入消散。
        // 这样即使玩家在蜂群出巢前/出巢瞬间就死了，蜂群也不会等复活后继续追。
        if (bunny.isDead)
            BeginRetreat();
    }

    /// <summary>
    /// 玩家死亡后调用（无论死因）：蜂群停止追逐，并按随机时间表逐只飞散消失。
    /// 如果玩家死时蜂群还在出巢，也会立刻停止出巢，只让已经出来的蜜蜂消散。
    /// </summary>
    private void BeginRetreat()
    {
        if (!started || retreating || swarmGone) return;

        retreating = true;
        retreatElapsed = 0f;

        int activeCount = 0;
        for (int i = 0; i < beeCount; i++)
            if (beeActive[i]) activeCount++;

        if (activeCount == 0)
        {
            retreating = false;
            swarmGone = true;
            return;
        }

        // 和出巢一样：均匀时间 + 随机时间混合，形成概率式、陆陆续续的消失。
        int order = 0;
        for (int i = 0; i < beeCount; i++)
        {
            if (!beeActive[i]) continue;

            float even = (order + 1) * vanishDuration / activeCount;
            float random = UnityEngine.Random.value * vanishDuration;
            beeVanishAt[i] = Mathf.Lerp(even, random, vanishBurstiness);
            order++;
        }

        // 至少让一只在当前帧就开始消失，避免死亡瞬间看不出反应。
        for (int i = 0; i < beeCount; i++)
        {
            if (beeActive[i])
            {
                beeVanishAt[i] = 0f;
                break;
            }
        }
    }

    private void UpdateRetreat(float dt)
    {
        retreatElapsed += dt;

        for (int i = 0; i < beeCount; i++)
        {
            if (!beeActive[i] || beeVanishing[i]) continue;
            if (retreatElapsed < beeVanishAt[i]) continue;

            beeVanishing[i] = true;
            beeVanishTimer[i] = Mathf.Max(0.01f, vanishPopTime);

            Vector2 outward = (Vector2)beeTransforms[i].position - swarmCenter;
            if (outward.sqrMagnitude < 0.0001f)
                outward = UnityEngine.Random.insideUnitCircle;
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector2.up;

            Vector2 fleeDir = (outward.normalized + Vector2.up * 0.6f).normalized;
            beeVanishVelocity[i] = fleeDir * vanishFlySpeed;
            beeVelocities[i] = beeVanishVelocity[i];
        }
    }

    private bool AnyBeeActive()
    {
        for (int i = 0; i < beeCount; i++)
            if (beeActive[i]) return true;
        return false;
    }
    #endregion

    /// <summary>胜利 / 需要手动停止蜂群时调用。</summary>
    public void StopSwarm()
    {
        stopped = true;
        if (indicatorContainer != null)
            indicatorContainer.gameObject.SetActive(false);
    }

    /// <summary>爆炸开始时把蜂巢上的预警蜜蜂藏掉（出巢时也会再调用一次，重复调用安全）。</summary>
    public void HideIndicatorBees()
    {
        if (indicatorContainer != null)
            indicatorContainer.gameObject.SetActive(false);
    }

    /// <summary>
    /// 玩家复活、蜂巢重置时调用：把蜂群恢复到“蜂巢未爆炸”的初始状态。
    /// 已经出现的蜜蜂全部收回，蜂巢上的指示蜜蜂重新显示，等待下一次爆炸。
    /// </summary>
    public void ResetSwarm()
    {
        if (missingHive || beeTransforms == null) return;

        stopped = false;
        started = false;
        allOut = false;
        retreating = false;
        swarmGone = false;

        emergeElapsed = 0f;
        spawnedCount = 0;
        nextSpawnIndex = 0;

        swarmCenter = GetHiveSpawnPoint();

        if (history != null)
        {
            for (int i = 0; i < history.Length; i++)
                history[i] = swarmCenter;
        }
        historyIndex = 0;

        for (int i = 0; i < beeCount; i++)
        {
            beeActive[i] = false;
            beeVelocities[i] = Vector2.zero;
            beeVanishAt[i] = 0f;
            beeVanishing[i] = false;
            beeVanishTimer[i] = 0f;
            beeVanishVelocity[i] = Vector2.zero;

            if (beeTransforms[i] == null) continue;

            beeTransforms[i].localScale = Vector3.one * beeScale;
            beeTransforms[i].rotation = Quaternion.identity;
            beeTransforms[i].gameObject.SetActive(false);
        }

        if (indicatorContainer != null)
            indicatorContainer.gameObject.SetActive(true);

        if (indicatorTransforms != null)
        {
            for (int i = 0; i < indicatorTransforms.Length; i++)
            {
                if (indicatorTransforms[i] == null) continue;

                indicatorTargets[i] = RandomPointInBounds(indicatorBounds);
                indicatorTimers[i] = UnityEngine.Random.Range(
                    indicatorRetargetInterval.x, indicatorRetargetInterval.y);
            }
        }
    }

    /// <summary>外部查询：是否已经开始出巢。</summary>
    public bool HasStarted => started;

    /// <summary>外部查询：是否所有蜜蜂都已出巢。</summary>
    public bool HasAllBeesOut => allOut;
    #endregion

    #region 指示蜜蜂（蜂巢上爬行）
    private void UpdateIndicatorBees(float dt)
    {
        if (indicatorTransforms == null) return;

        for (int i = 0; i < indicatorTransforms.Length; i++)
        {
            Transform t = indicatorTransforms[i];
            if (t == null) continue;

            indicatorTimers[i] -= dt;
            if (indicatorTimers[i] <= 0f)
                RetargetIndicator(i);

            Vector2 pos = t.position;
            Vector2 target = indicatorTargets[i];
            Vector2 toTarget = target - pos;

            if (toTarget.sqrMagnitude < 0.0025f)
            {
                RetargetIndicator(i);
                continue;
            }

            // 方向 + 一点噪声，让爬行不是直线。
            float noise = Mathf.PerlinNoise(
                noiseSeed + Time.time * 1.7f + i * 13f, i * 5.3f);
            float angle = noise * Mathf.PI * 2f;
            Vector2 wander = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                * (indicatorSpeed * 0.6f);

            Vector2 move = toTarget.normalized * indicatorSpeed + wander;
            pos += move * dt;

            pos.x = Mathf.Clamp(pos.x, indicatorBounds.min.x, indicatorBounds.max.x);
            pos.y = Mathf.Clamp(pos.y, indicatorBounds.min.y, indicatorBounds.max.y);
            t.position = pos;

            if (indicatorRenderers[i] != null)
                indicatorRenderers[i].flipX = move.x < 0f;
        }
    }

    private void RetargetIndicator(int index)
    {
        indicatorTargets[index] = RandomPointInBounds(indicatorBounds);
        indicatorTimers[index] = UnityEngine.Random.Range(
            indicatorRetargetInterval.x, indicatorRetargetInterval.y);
    }

    private Vector2 RandomPointInBounds(Bounds bounds)
    {
        return new Vector2(
            UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
            UnityEngine.Random.Range(bounds.min.y, bounds.max.y));
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        SpriteRenderer sr = hiveRenderer != null ? hiveRenderer : GetComponent<SpriteRenderer>();
        if (sr == null) return;

        // 蜂巢/指示蜜蜂活动范围
        Bounds hiveBounds = sr.bounds;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(hiveBounds.center, hiveBounds.size * indicatorAreaScale);

        // 蜂群判定半径
        Vector2 center = Application.isPlaying ? swarmCenter : GetHiveSpawnPoint();
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(center, catchRadius);
    }
    #endregion
}
