using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 蜂巢爆炸机关的“一键装配”工具。
///
/// 编译 / 打开场景后会尝试自动：
///   1) 把 BeeHiveExploded.controller 补成：Idle(默认空状态) + BeeHiveExploded(播放序列帧)，
///      并添加 Explode 触发器；
///   2) 在 BeeHiveExploded.anim 的 explode.005 帧（0.5 秒）写入动画事件 ReleaseCoin；
///   3) 在打开的场景里找到蜂巢，挂上 Animator + BeeHiveExplosion；
///   4) 找到藏在蜂巢里的金币，补上 Rigidbody2D + CoinPopOut，并和蜂巢连线。
///
/// 也可以随时用菜单手动重跑：
///   Tools/Bee Hive/Setup Bee Hives In Open Scene
///   Tools/Bee Hive/Rebuild Bee Hive Assets
///
/// 已有内容不会被反复覆盖：重复运行只会补缺失的部分。
/// </summary>
public static class BeeHiveSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string ClipPath = AnimationFolder + "/BeeHiveExploded.anim";
    private const string ControllerPath = AnimationFolder + "/BeeHiveExploded.controller";

    private const string IdleStateName = "Idle";
    private const string ExplodeStateName = "BeeHiveExploded";
    private const string ExplodeTriggerName = "Explode";
    private const string CoinEventFunction = "ReleaseCoin";

    /// <summary>BeeHiveExploded 剪辑里 explode.005 的时间点（12fps，帧 000~008 中的 005）。</summary>
    private const float CoinReleaseTime = 0.5f;

    #region 自动执行
    [InitializeOnLoadMethod]
    private static void ScheduleAutoSetup()
    {
        EditorApplication.delayCall += AutoSetup;

        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += AutoSetup;
    }

    private static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        bool assetsChanged = EnsureAssets();
        if (assetsChanged)
            Debug.Log("[BeeHiveSetup] 已补全蜂巢爆炸动画状态机 / explode.005 动画事件。");

        AutoWireScene();
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Bee Hive/Setup Bee Hives In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireScene(repairExisting: true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Bee Hive/Rebuild Bee Hive Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed
            ? "[BeeHiveSetup] 蜂巢资源已补全。"
            : "[BeeHiveSetup] 蜂巢资源已经完整，无需修改。");
    }
    #endregion

    #region 资源：动画事件 / 状态机
    private static bool EnsureAssets()
    {
        bool changed = false;

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip != null)
            changed |= EnsureCoinAnimationEvent(clip);
        else
            Debug.LogWarning($"[BeeHiveSetup] 找不到动画剪辑 {ClipPath}");

        changed |= EnsureController();

        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    /// <summary>在剪辑的 explode.005 帧（0.5s）插入释放金币的动画事件。</summary>
    private static bool EnsureCoinAnimationEvent(AnimationClip clip)
    {
        AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);

        foreach (AnimationEvent e in events)
        {
            if (e.functionName == CoinEventFunction && Mathf.Abs(e.time - CoinReleaseTime) < 0.001f)
                return false;
        }

        var list = new List<AnimationEvent>(events)
        {
            new AnimationEvent
            {
                functionName = CoinEventFunction,
                time = CoinReleaseTime,
                messageOptions = SendMessageOptions.DontRequireReceiver
            }
        };
        list.Sort((a, b) => a.time.CompareTo(b.time));

        AnimationUtility.SetAnimationEvents(clip, list.ToArray());
        EditorUtility.SetDirty(clip);
        return true;
    }

    private static bool EnsureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);

        if (controller == null)
        {
            Debug.LogWarning($"[BeeHiveSetup] 找不到状态机 {ControllerPath}");
            return false;
        }
        if (clip == null) return false;

        bool changed = false;

        // 空白 controller 的 Layer 权重可能是 0，那样即使有状态也不会播放，这里修正成 1
        AnimatorControllerLayer[] layers = controller.layers;

        if (layers.Length == 0)
        {
            AnimatorStateMachine newStateMachine = new AnimatorStateMachine { name = "Base Layer" };
            newStateMachine.hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(newStateMachine))
                AssetDatabase.AddObjectToAsset(newStateMachine, controller);

            layers = new[]
            {
                new AnimatorControllerLayer
                {
                    name = "Base Layer",
                    defaultWeight = 1f,
                    stateMachine = newStateMachine
                }
            };
            controller.layers = layers;
            changed = true;
        }
        else if (!Mathf.Approximately(layers[0].defaultWeight, 1f))
        {
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
            changed = true;
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        if (stateMachine == null) return changed;

        changed |= EnsureTrigger(controller, ExplodeTriggerName);

        AnimatorState idleState = EnsureState(stateMachine, controller, IdleStateName, null, false, ref changed);
        AnimatorState explodeState = EnsureState(stateMachine, controller, ExplodeStateName, clip, false, ref changed);

        if (idleState != null && stateMachine.defaultState != idleState)
        {
            stateMachine.defaultState = idleState;
            changed = true;
        }

        if (idleState != null && explodeState != null)
            changed |= EnsureTransition(idleState, explodeState, controller, ExplodeTriggerName);

        if (changed) EditorUtility.SetDirty(controller);
        return changed;
    }

    private static bool EnsureTrigger(AnimatorController controller, string triggerName)
    {
        if (controller.parameters.Any(p =>
                p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger))
        {
            return false;
        }

        controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
        return true;
    }

    private static AnimatorState EnsureState(
        AnimatorStateMachine stateMachine,
        AnimatorController controller,
        string stateName,
        AnimationClip clip,
        bool loop,
        ref bool changed)
    {
        if (clip != null)
            changed |= SetLoop(clip, loop);

        AnimatorState state = stateMachine.states
            .Where(s => s.state != null && s.state.name == stateName)
            .Select(s => s.state)
            .FirstOrDefault();

        if (state == null)
        {
            state = stateMachine.AddState(stateName);
            state.hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(state))
                AssetDatabase.AddObjectToAsset(state, controller);
            changed = true;
        }

        if (state.motion != clip)
        {
            state.motion = clip;
            state.speed = 1f;
            changed = true;
        }

        return state;
    }

    /// <summary>Idle → Explode 的即时过渡（不等待播放完，靠 Trigger 触发）。</summary>
    private static bool EnsureTransition(
        AnimatorState fromState,
        AnimatorState toState,
        AnimatorController controller,
        string triggerName)
    {
        AnimatorStateTransition transition = fromState.transitions
            .FirstOrDefault(t => t != null && t.destinationState == toState);

        bool changed = false;

        if (transition == null)
        {
            transition = fromState.AddTransition(toState);
            if (!AssetDatabase.Contains(transition))
                AssetDatabase.AddObjectToAsset(transition, controller);
            changed = true;
        }

        bool hasCondition = transition.conditions.Any(c =>
            c.parameter == triggerName && c.mode == AnimatorConditionMode.If);

        if (transition.hasExitTime || !Mathf.Approximately(transition.duration, 0f) || !hasCondition)
        {
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.hasFixedDuration = true;
            transition.duration = 0f;

            while (transition.conditions.Length > 0)
                transition.RemoveCondition(transition.conditions[0]);

            transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
            changed = true;
        }

        return changed;
    }

    private static bool SetLoop(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return false;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return true;
    }
    #endregion

    #region 场景自动装配
    private static void AutoWireScene(bool repairExisting = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameObject hiveObject = FindHive();
        if (hiveObject == null) return;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning("[BeeHiveSetup] 状态机缺失，无法自动装配蜂巢。");
            return;
        }

        bool changed = false;

        // 0) 蜂巢本体要能挡住兔子：把所有 Collider2D 的 Is Trigger 关掉
        Collider2D[] hiveColliders = hiveObject.GetComponents<Collider2D>();
        for (int i = 0; i < hiveColliders.Length; i++)
        {
            if (hiveColliders[i] == null || !hiveColliders[i].isTrigger) continue;
            hiveColliders[i].isTrigger = false;
            changed = true;
        }

        // 1) Animator
        Animator animator = hiveObject.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(hiveObject);
            changed = true;
        }

        if (animator.runtimeAnimatorController != controller)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            changed = true;
        }

        // 2) BeeHiveExplosion
        BeeHiveExplosion hive = hiveObject.GetComponent<BeeHiveExplosion>();
        bool addedHive = false;
        if (hive == null)
        {
            hive = Undo.AddComponent<BeeHiveExplosion>(hiveObject);
            addedHive = true;
            changed = true;
        }

        // 3) 金币
        GameObject coinObject = FindCoinNear(hiveObject.transform);
        if (coinObject == null)
        {
            Debug.LogWarning("[BeeHiveSetup] 没在场景里找到金币（CoinPickup），蜂巢暂时不会弹出金币。");
        }
        else
        {
            changed |= EnsureCoinComponents(coinObject);
        }

        // 4) 连线（只补空引用，保留你在 Inspector 里改过的设置）
        SerializedObject hiveSerialized = new SerializedObject(hive);

        SerializedProperty animatorProperty = hiveSerialized.FindProperty("hiveAnimator");
        if (animatorProperty != null && (repairExisting || animatorProperty.objectReferenceValue == null))
        {
            if (animatorProperty.objectReferenceValue != animator)
            {
                animatorProperty.objectReferenceValue = animator;
                changed = true;
            }
        }

        if (coinObject != null)
            changed |= SetObjectReferenceIfNull(hiveSerialized, "hiddenCoin", coinObject);

        changed |= hiveSerialized.ApplyModifiedPropertiesWithoutUndo();

        if (changed)
        {
            EditorUtility.SetDirty(hive);
            EditorUtility.SetDirty(animator);
            if (coinObject != null) EditorUtility.SetDirty(coinObject);
            EditorSceneManager.MarkSceneDirty(hiveObject.scene);

            if (addedHive)
            {
                Debug.Log("[BeeHiveSetup] 已给蜂巢挂上 Animator + BeeHiveExplosion，并给金币配置弹出组件。", hiveObject);
            }
        }
    }

    private static bool EnsureCoinComponents(GameObject coinObject)
    {
        bool changed = false;

        Rigidbody2D rb = coinObject.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody2D>(coinObject);
            changed = true;
        }

        if (rb.bodyType != RigidbodyType2D.Dynamic) { rb.bodyType = RigidbodyType2D.Dynamic; changed = true; }
        if (!rb.freezeRotation) { rb.freezeRotation = true; changed = true; }
        if (rb.interpolation != RigidbodyInterpolation2D.Interpolate) { rb.interpolation = RigidbodyInterpolation2D.Interpolate; changed = true; }
        if (rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous) { rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; changed = true; }

        if (coinObject.GetComponent<CoinPopOut>() == null)
        {
            Undo.AddComponent<CoinPopOut>(coinObject);
            changed = true;
        }

        return changed;
    }

    private static GameObject FindHive()
    {
        // 优先按当前场景里的默认名字找
        GameObject named = GameObject.Find("Beehive-Beehive.000");
        if (named != null && named.GetComponent<Collider2D>() != null)
            return named;

        // 兜底：找带“触发用 PolygonCollider2D”、且贴图名包含 Beehive 的物体（跳过爆炸帧和装饰蜜蜂）
        SpriteRenderer[] renderers = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null) continue;

            string spriteName = renderer.sprite.name.ToLowerInvariant();
            if (!spriteName.StartsWith("beehive-beehive")) continue;

            PolygonCollider2D polygon = renderer.GetComponent<PolygonCollider2D>();
            if (polygon != null)
                return renderer.gameObject;
        }

        return null;
    }

    private static GameObject FindCoinNear(Transform hive)
    {
        CoinPickup[] coins = Object.FindObjectsOfType<CoinPickup>(true);

        CoinPickup nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (CoinPickup coin in coins)
        {
            if (coin == null) continue;

            float sqrDistance = (coin.transform.position - hive.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = coin;
            }
        }

        return nearest != null ? nearest.gameObject : null;
    }

    private static bool SetObjectReferenceIfNull(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue != null || value == null) return false;

        property.objectReferenceValue = value;
        return true;
    }
    #endregion
}
