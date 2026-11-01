using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 弩箭玩法的“一键装配”工具。
///
/// 脚本首次编译后会自动做三件事：
///   1) 把三个空白 AnimatorController 填好状态（Arrow 循环、ArrowBreaking 单次、ArrowShooter 循环）；
///   2) 在 Assets/Prefabs 下生成带 SpriteRenderer / Animator / Rigidbody2D / BoxCollider2D /
///      ArrowProjectile 的 Arrow 预制体；
///   3) 在打开的场景里找到使用 AS2-ArrowShooter 精灵的弩，自动挂上 CrossbowShooter 并连好引用。
///
/// 也可以随时用菜单手动重跑：
///   Tools/Arrow Trap/Setup Crossbows In Open Scene
///   Tools/Arrow Trap/Rebuild Arrow Assets
///
/// 已有内容的控制器 / 预制体不会被覆盖，只补缺失的部分，所以重复运行是安全的。
/// </summary>
public static class ArrowTrapSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string PrefabFolder = "Assets/Prefabs";

    private const string ArrowClipPath = AnimationFolder + "/Arrow.anim";
    private const string ShooterClipPath = AnimationFolder + "/ArrowShooter.anim";
    private const string BreakingClipPath = AnimationFolder + "/ArrowBreaking.anim";
    private const string IdleClipPath = AnimationFolder + "/ArrowShooterIdle.anim";

    private const string ArrowControllerPath = AnimationFolder + "/Arrow.controller";
    private const string ShooterControllerPath = AnimationFolder + "/ArrowShooter.controller";
    private const string BreakingControllerPath = AnimationFolder + "/ArrowBreaking.controller";

    private const string ArrowSpritePath = "Assets/Sprite/AS2-Arrow.000.png";
    private const string ShooterSpritePath = "Assets/Sprite/AS2-ArrowShooter.000.png";
    private const string ArrowPrefabPath = PrefabFolder + "/Arrow.prefab";

    private const float ArrowPrefabScale = 0.5f;

    /// <summary>
    /// 地形 / 沙地 / 法老雕像的 SortingOrder 是 1，道具是 2，玩家是 3。
    /// 弩如果留在 0 会被整块挡住，所以抬到 2。
    /// </summary>
    private const int CrossbowSortingOrder = 2;

    private struct StateRequest
    {
        public string stateName;
        public string clipPath;
        public bool loop;
        public bool makeDefault;

        public StateRequest(string stateName, string clipPath, bool loop, bool makeDefault)
        {
            this.stateName = stateName;
            this.clipPath = clipPath;
            this.loop = loop;
            this.makeDefault = makeDefault;
        }
    }

    #region 自动执行
    [InitializeOnLoadMethod]
    private static void ScheduleAutoSetup()
    {
        EditorApplication.delayCall += AutoSetup;
    }

    private static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        bool createdAssets = EnsureAssets();
        if (createdAssets)
        {
            Debug.Log("[ArrowTrapSetup] 已自动生成 Arrow 预制体，并把 Arrow / ArrowShooter / ArrowBreaking 状态机填好。");
        }

        AutoWireSceneCrossbows();
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Arrow Trap/Setup Crossbows In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireSceneCrossbows(repairExisting: true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Arrow Trap/Rebuild Arrow Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed ? "[ArrowTrapSetup] 箭矢资源已补全。" : "[ArrowTrapSetup] 箭矢资源已经完整，无需修改。");
    }
    #endregion

    #region 资源生成
    private static bool EnsureAssets()
    {
        bool changed = false;

        // 箭矢自己的控制器：飞行 + 破碎两个状态，箭矢只有一个 Animator，两个状态必须放在同一个控制器里
        changed |= EnsureController(ArrowControllerPath, new[]
        {
            new StateRequest("Arrow", ArrowClipPath, true, true),
            new StateRequest("ArrowBreaking", BreakingClipPath, false, false)
        });

        // 弩身控制器：射箭动画只播一次，播完回到 Idle（静态第 1 帧），等待下一发
        changed |= EnsureIdleClip();
        changed |= EnsureController(ShooterControllerPath, new[]
        {
            new StateRequest("ArrowShooter", ShooterClipPath, false, false),
            new StateRequest("Idle", IdleClipPath, true, false)
        }, forceDefaultStateName: "Idle");
        changed |= EnsureShooterTransition();

        // 单独保留的破碎控制器也补齐（当前玩法不直接使用，但保持三个状态机都可用）
        changed |= EnsureController(BreakingControllerPath, new[]
        {
            new StateRequest("ArrowBreaking", BreakingClipPath, false, true)
        });

        if (!File.Exists(ArrowPrefabPath))
        {
            changed |= CreateArrowPrefab();
        }

        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    private static bool EnsureController(string controllerPath, StateRequest[] requests, string forceDefaultStateName = null)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"[ArrowTrapSetup] 找不到状态机 {controllerPath}");
            return false;
        }

        bool changed = false;
        AnimatorStateMachine stateMachine;

        if (controller.layers.Length == 0)
        {
            stateMachine = new AnimatorStateMachine { name = "Base Layer" };
            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = stateMachine
            };
            controller.AddLayer(layer);

            stateMachine.hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(stateMachine))
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
            changed = true;
        }
        else
        {
            stateMachine = controller.layers[0].stateMachine;
        }

        if (stateMachine == null) return changed;

        foreach (StateRequest request in requests)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(request.clipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[ArrowTrapSetup] 找不到动画剪辑 {request.clipPath}");
                continue;
            }

            AnimatorState state = stateMachine.states
                .Where(s => s.state != null && s.state.name == request.stateName)
                .Select(s => s.state)
                .FirstOrDefault();

            // 剪辑的循环设置每次都强制校准：射箭剪辑必须不循环（靠结束转回 Idle），Idle 必须循环
            changed |= SetLoop(clip, request.loop);

            if (state == null)
            {
                state = stateMachine.AddState(request.stateName);
                state.motion = clip;
                state.hideFlags = HideFlags.HideInHierarchy;
                if (!AssetDatabase.Contains(state))
                    AssetDatabase.AddObjectToAsset(state, controller);
                changed = true;
            }
            else if (state.motion == null)
            {
                state.motion = clip;
                changed = true;
            }

            if (request.makeDefault && stateMachine.defaultState == null)
            {
                stateMachine.defaultState = state;
                changed = true;
            }
        }

        if (!string.IsNullOrEmpty(forceDefaultStateName))
        {
            AnimatorState wanted = stateMachine.states
                .Where(s => s.state != null && s.state.name == forceDefaultStateName)
                .Select(s => s.state)
                .FirstOrDefault();

            if (wanted != null && stateMachine.defaultState != wanted)
            {
                stateMachine.defaultState = wanted;
                changed = true;
            }
        }

        if (changed) EditorUtility.SetDirty(controller);
        return changed;
    }

    /// <summary>
    /// 生成 Idle 用的单帧动画剪辑：始终显示 ArrowShooter 第 1 帧，循环播放。
    /// </summary>
    private static bool EnsureIdleClip()
    {
        if (File.Exists(IdleClipPath)) return false;

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShooterSpritePath);
        if (sprite == null)
        {
            Debug.LogWarning($"[ArrowTrapSetup] 找不到弩的待机精灵 {ShooterSpritePath}");
            return false;
        }

        AnimationClip clip = new AnimationClip
        {
            name = "ArrowShooterIdle",
            frameRate = 12f
        };

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        ObjectReferenceKeyframe[] keys =
        {
            new ObjectReferenceKeyframe { time = 0f, value = sprite },
            new ObjectReferenceKeyframe { time = 1f / 12f, value = sprite }
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.stopTime = 1f / 12f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, IdleClipPath);
        return true;
    }

    /// <summary>
    /// 给 ArrowShooter → Idle 加一条"播完自动进入待机"的过渡：
    /// 没有条件、exitTime = 1，动画一播完就切到 Idle，期间完全不动。
    /// </summary>
    private static bool EnsureShooterTransition()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShooterControllerPath);
        if (controller == null || controller.layers.Length == 0) return false;

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        if (stateMachine == null) return false;

        AnimatorState shootState = stateMachine.states
            .Where(s => s.state != null && s.state.name == "ArrowShooter")
            .Select(s => s.state)
            .FirstOrDefault();
        AnimatorState idleState = stateMachine.states
            .Where(s => s.state != null && s.state.name == "Idle")
            .Select(s => s.state)
            .FirstOrDefault();

        if (shootState == null || idleState == null) return false;

        bool changed = false;
        AnimatorStateTransition transition = shootState.transitions
            .FirstOrDefault(t => t != null && t.destinationState == idleState);

        if (transition == null)
        {
            transition = shootState.AddTransition(idleState);
            if (!AssetDatabase.Contains(transition))
                AssetDatabase.AddObjectToAsset(transition, controller);
            changed = true;
        }

        if (!transition.hasExitTime || !Mathf.Approximately(transition.exitTime, 1f) ||
            transition.duration != 0f || !transition.hasFixedDuration)
        {
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(controller);
        return changed;
    }

    private static bool SetLoop(AnimationClip clip, bool loop)
    {
        if (clip == null) return false;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop) return false;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return true;
    }

    private static bool CreateArrowPrefab()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ArrowControllerPath);

        if (sprite == null)
        {
            Debug.LogWarning($"[ArrowTrapSetup] 找不到箭矢精灵 {ArrowSpritePath}");
            return false;
        }
        if (controller == null)
        {
            Debug.LogWarning($"[ArrowTrapSetup] 找不到 Arrow.controller");
            return false;
        }

        GameObject go = new GameObject("Arrow");
        go.transform.localScale = Vector3.one * ArrowPrefabScale;

        SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingOrder = 10;

        Animator animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.useFullKinematicContacts = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        Vector2 spriteSize = sprite.bounds.size;
        box.size = new Vector2(
            Mathf.Max(0.12f, spriteSize.x * 1.3f),
            Mathf.Max(0.90f, spriteSize.y * 0.9f));

        ArrowProjectile projectile = go.AddComponent<ArrowProjectile>();
        SerializedObject so = new SerializedObject(projectile);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("flyDirection").vector2Value = Vector2.left;
        so.FindProperty("playerLayer").intValue = 1 << 0; // Default（本项目小羊在 Default 层）
        so.FindProperty("obstacleLayer").intValue = 0;
        so.FindProperty("breakOnAnySolidCollider").boolValue = true;
        so.FindProperty("spriteFacingAngle").floatValue = 90f;
        so.FindProperty("maxLifetime").floatValue = 8f;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        PrefabUtility.SaveAsPrefabAsset(go, ArrowPrefabPath);
        Object.DestroyImmediate(go);

        return File.Exists(ArrowPrefabPath);
    }
    #endregion

    #region 场景自动装配
    private static void AutoWireSceneCrossbows(bool repairExisting = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameObject arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefabPath);
        RuntimeAnimatorController shooterController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ShooterControllerPath);

        if (arrowPrefab == null || shooterController == null) return;

        SpriteRenderer[] renderers = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer.sprite == null) continue;
            if (!renderer.sprite.name.Contains("ArrowShooter")) continue;
            if (renderer.sprite.name.Contains("Explode")) continue;

            GameObject go = renderer.gameObject;

            // 关键修复：地形 / 沙地 / 法老雕像是 SortingOrder 1，弩如果还是 0 会被整块盖住，
            // 动画帧宽高变化时只有边角偶尔露出来，看起来就像"几帧空白透明"。
            if (renderer.sortingOrder < CrossbowSortingOrder)
            {
                int oldOrder = renderer.sortingOrder;
                Undo.RecordObject(renderer, "Raise Crossbow Sorting Order");
                renderer.sortingOrder = CrossbowSortingOrder;
                EditorUtility.SetDirty(renderer);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log($"[ArrowTrapSetup] 弩「{go.name}」排序层级从 {oldOrder} 提升到 {CrossbowSortingOrder}，" +
                          "不再被地形 / 雕像挡住。", go);
            }

            CrossbowShooter shooter = go.GetComponentInParent<CrossbowShooter>();
            if (shooter != null && !repairExisting) continue;

            if (shooter == null)
            {
                Animator animator = go.GetComponent<Animator>();
                if (animator == null) animator = Undo.AddComponent<Animator>(go);
                if (animator.runtimeAnimatorController == null)
                    animator.runtimeAnimatorController = shooterController;

                shooter = Undo.AddComponent<CrossbowShooter>(go);

                SerializedObject so = new SerializedObject(shooter);
                so.FindProperty("arrowPrefab").objectReferenceValue = arrowPrefab;
                so.FindProperty("shooterAnimator").objectReferenceValue = animator;
                so.FindProperty("fireDirection").vector2Value = Vector2.left;
                so.FindProperty("muzzleOffset").vector2Value = new Vector2(0f, 0.55f);
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);

                Debug.Log($"[ArrowTrapSetup] 已给场景物体「{go.name}」挂上 CrossbowShooter（向左不停发射）。", go);
            }
            else if (repairExisting)
            {
                Animator animator = go.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController == null)
                    animator.runtimeAnimatorController = shooterController;

                SerializedObject so = new SerializedObject(shooter);
                SerializedProperty prefabProp = so.FindProperty("arrowPrefab");
                if (prefabProp.objectReferenceValue == null) prefabProp.objectReferenceValue = arrowPrefab;

                SerializedProperty animatorProp = so.FindProperty("shooterAnimator");
                if (animatorProp.objectReferenceValue == null) animatorProp.objectReferenceValue = animator;

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }
    }
    #endregion
}
