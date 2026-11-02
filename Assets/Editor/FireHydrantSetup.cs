using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 消防栓玩法的“一键装配”工具。
///
/// 打开工程 / 编译后会自动：
///   1) 生成只显示 000 帧的待机剪辑 FireHydrantSprayIdle.anim；
///   2) 把空白的 FireHydrantSpray.controller 填成两个状态：
///        Spray（完整 20 帧，不循环）→ Idle（000 帧，循环）
///   3) 在打开的场景里找到火焰消防栓，自动挂 Animator + FireHydrantSpray，
///      并把伤害盒默认对准 007-014 的水柱。
///
/// 也可以随时用菜单手动重跑：
///   Tools/Fire Hydrant/Setup Fire Hydrants In Open Scene
///   Tools/Fire Hydrant/Rebuild Fire Hydrant Assets
///
/// 已有内容不会被覆盖：重复运行只会补缺失的部分。
/// </summary>
public static class FireHydrantSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string SprayClipPath = AnimationFolder + "/FireHydrantSpray.anim";
    private const string IdleClipPath = AnimationFolder + "/FireHydrantSprayIdle.anim";
    private const string ControllerPath = AnimationFolder + "/FireHydrantSpray.controller";
    private const string IdleSpritePath = "Assets/Sprite/FireHydrant.000.png";

    private const string SprayStateName = "Spray";
    private const string IdleStateName = "Idle";

    private const int SpriteFrameCount = 20;
    private const string SpritePathFormat = "Assets/Sprite/FireHydrant.{0:000}.png";

    /// <summary>
    /// 序列帧是紧贴着内容裁切的，而且都是中心轴心。水柱越长，画面中心越往上跑，
    /// 消防栓本体就会看起来越来越往下沉、最后整个埋进地面。
    /// 这里把每一帧的轴心都改成「距图片底边固定 58.5 像素」（正好是 000 帧的半高），
    /// 这样本体底边在整段动画里都钉在原地，只有水柱往上长，场景摆放和碰撞体都不用动。
    /// </summary>
    private const float PivotPixelsFromBottom = 58.5f;

    /// <summary>消防栓是场景道具，排序层要压在地形（1）之上，否则动画换帧时会被地面挡住。</summary>
    private const int MinimumSortingOrder = 2;

    #region 自动执行
    [InitializeOnLoadMethod]
    private static void ScheduleAutoSetup()
    {
        EditorApplication.delayCall += AutoSetup;

        // 切换 / 打开场景时也补一次装配，避免只有重新编译才会自动挂组件
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

        bool changed = EnsureAssets();
        if (changed)
            Debug.Log("[FireHydrantSetup] 已自动补全消防栓动画状态机 / Idle 剪辑。");

        AutoWireSceneHydrants();
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Fire Hydrant/Setup Fire Hydrants In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireSceneHydrants(repairExisting: true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Fire Hydrant/Rebuild Fire Hydrant Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed ? "[FireHydrantSetup] 消防栓资源已补全。" : "[FireHydrantSetup] 消防栓资源已经完整，无需修改。");
    }
    #endregion

    #region 资源生成
    private static bool EnsureAssets()
    {
        bool changed = false;

        changed |= FixSpritePivots();
        changed |= EnsureIdleClip();

        if (!File.Exists(IdleClipPath))
        {
            // 待机剪辑没能生成时，仍然把喷水状态填好，运行时脚本会退回为只播 000 帧
            Debug.LogWarning("[FireHydrantSetup] 待机剪辑生成失败，Idle 状态可能缺失。");
        }

        changed |= EnsureController();

        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    /// <summary>
    /// 把 20 帧的轴心统一到「距底边 58.5 像素」，让消防栓本体的底边固定不动。
    /// 只改轴心，不改图片、不改动画曲线、不改 GUID，所以重复执行是安全的。
    /// </summary>
    private static bool FixSpritePivots()
    {
        bool changed = false;

        for (int i = 0; i < SpriteFrameCount; i++)
        {
            string path = string.Format(SpritePathFormat, i);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            if (importer.spriteImportMode != SpriteImportMode.Single) continue;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || texture.height <= 0) continue;

            float targetPivotY = Mathf.Clamp01(PivotPixelsFromBottom / texture.height);

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool alreadyCorrect =
                settings.spriteAlignment == (int)SpriteAlignment.Custom &&
                Mathf.Abs(settings.spritePivot.y - targetPivotY) < 0.0001f &&
                Mathf.Abs(settings.spritePivot.x - 0.5f) < 0.0001f;

            if (alreadyCorrect) continue;

            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, targetPivotY);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            changed = true;
        }

        if (changed)
            Debug.Log("[FireHydrantSetup] 已把消防栓 20 帧的轴心统一到本体底边，喷水时消防栓不会再沉进地面。");

        return changed;
    }

    private static bool EnsureIdleClip()
    {
        if (File.Exists(IdleClipPath)) return false;

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IdleSpritePath);
        if (sprite == null)
        {
            Debug.LogWarning($"[FireHydrantSetup] 找不到待机精灵 {IdleSpritePath}");
            return false;
        }

        AnimationClip clip = new AnimationClip
        {
            name = "FireHydrantSprayIdle",
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

    private static bool EnsureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"[FireHydrantSetup] 找不到状态机 {ControllerPath}");
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

        AnimationClip sprayClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SprayClipPath);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);

        changed |= EnsureState(stateMachine, controller, SprayStateName, sprayClip, loop: false);
        changed |= EnsureState(stateMachine, controller, IdleStateName, idleClip, loop: true);

        AnimatorState idleState = FindState(stateMachine, IdleStateName);
        if (idleState != null && stateMachine.defaultState != idleState)
        {
            stateMachine.defaultState = idleState;
            changed = true;
        }

        changed |= EnsureSprayToIdleTransition(stateMachine, controller);

        if (changed) EditorUtility.SetDirty(controller);
        return changed;
    }

    private static bool EnsureState(
        AnimatorStateMachine stateMachine,
        AnimatorController controller,
        string stateName,
        AnimationClip clip,
        bool loop)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[FireHydrantSetup] 找不到动画剪辑，无法创建状态 {stateName}。");
            return false;
        }

        bool changed = SetLoop(clip, loop);

        AnimatorState state = FindState(stateMachine, stateName);
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

        return changed;
    }

    /// <summary>
    /// Spray 播完（exitTime = 1）自动切到 Idle。脚本自己也会在播完时调用 Play(Idle)，
    /// 这条过渡保证就算只靠 Animator 也能自然停回 000 帧。
    /// </summary>
    private static bool EnsureSprayToIdleTransition(AnimatorStateMachine stateMachine, AnimatorController controller)
    {
        AnimatorState sprayState = FindState(stateMachine, SprayStateName);
        AnimatorState idleState = FindState(stateMachine, IdleStateName);
        if (sprayState == null || idleState == null) return false;

        bool changed = false;
        AnimatorStateTransition transition = sprayState.transitions
            .FirstOrDefault(t => t != null && t.destinationState == idleState);

        if (transition == null)
        {
            transition = sprayState.AddTransition(idleState);
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

        return changed;
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        return stateMachine.states
            .Where(s => s.state != null && s.state.name == stateName)
            .Select(s => s.state)
            .FirstOrDefault();
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
    #endregion

    #region 场景自动装配
    private static void AutoWireSceneHydrants(bool repairExisting = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        AnimationClip sprayClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SprayClipPath);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);

        if (controller == null)
        {
            Debug.LogWarning("[FireHydrantSetup] 状态机缺失，无法自动装配消防栓。");
            return;
        }

        SpriteRenderer[] renderers = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer.sprite == null) continue;
            if (!renderer.sprite.name.Contains("FireHydrant")) continue;
            if (renderer.sprite.name.Contains("Explode")) continue;

            GameObject go = renderer.gameObject;

            if (renderer.sortingOrder < MinimumSortingOrder)
            {
                Undo.RecordObject(renderer, "Raise Fire Hydrant Sorting Order");
                renderer.sortingOrder = MinimumSortingOrder;
                EditorUtility.SetDirty(renderer);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            Animator animator = go.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(go);
            if (animator.runtimeAnimatorController == null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            FireHydrantSpray hydrant = go.GetComponent<FireHydrantSpray>();
            bool added = false;

            if (hydrant == null)
            {
                hydrant = Undo.AddComponent<FireHydrantSpray>(go);
                added = true;
            }
            else if (!repairExisting)
            {
                // 已经装配过：保留用户调好的参数，不反复覆盖
                continue;
            }

            SerializedObject so = new SerializedObject(hydrant);

            SetObjectReferenceIfNull(so, "hydrantAnimator", animator);
            SetObjectReferenceIfNull(so, "sprayClip", sprayClip);
            SetObjectReferenceIfNull(so, "idleClip", idleClip);
            SetStringIfEmpty(so, "sprayStateName", SprayStateName);
            SetStringIfEmpty(so, "idleStateName", IdleStateName);

            if (added)
            {
                // 下面这些默认值来自对 007-014 序列帧的实测（轴心已对齐到本体底边）：
                // 以消防栓 Transform 为原点，水柱大约覆盖本地 x[-0.6, 0.6]、y[0.45, 2.85]
                so.FindProperty("idleHoldTime").floatValue = 0.3f;
                so.FindProperty("startDelay").floatValue = 0f;
                so.FindProperty("playbackSpeed").floatValue = 1f;
                so.FindProperty("playerLayer").intValue = ~0;
                so.FindProperty("damageBoxOffset").vector2Value = new Vector2(0f, 1.65f);
                so.FindProperty("damageBoxSize").vector2Value = new Vector2(1.19f, 2.41f);
                so.FindProperty("damageStartFrame").intValue = 7;
                so.FindProperty("damageEndFrame").intValue = 14;
                so.FindProperty("totalFramesOverride").intValue = 0;
                so.FindProperty("killPlayerOnHit").boolValue = true;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hydrant);
            EditorSceneManager.MarkSceneDirty(go.scene);

            if (added)
                Debug.Log($"[FireHydrantSetup] 已给「{go.name}」挂上 FireHydrantSpray，并把 007-014 设为伤害帧。", go);
        }
    }

    private static void SetObjectReferenceIfNull(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null && property.objectReferenceValue == null && value != null)
            property.objectReferenceValue = value;
    }

    private static void SetStringIfEmpty(SerializedObject so, string propertyName, string value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null && string.IsNullOrEmpty(property.stringValue))
            property.stringValue = value;
    }
    #endregion
}
