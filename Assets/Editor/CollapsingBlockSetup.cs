using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CollapsingBlock（临时站立平台）的“一键装配”工具。
///
/// 打开工程 / 编译后会自动：
///   1) 把空白的 CollapsingBlock.controller 填成只含一个 Collapse 状态的播放器，
///      状态挂上 000→020 的 CollapsingBlock.anim 序列帧（不循环）；
///   2) 在打开的场景里找到所有 CollapsingBlock2x.000 平台，自动补上 Animator 和
///      CollapsingBlockTrap，并把动画 / 碰撞箱 / 剪辑等引用接好。
///
/// 也可以随时用菜单手动重跑：
///   Tools/Collapsing Block/Setup Collapsing Blocks In Open Scene
///   Tools/Collapsing Block/Rebuild Collapsing Block Assets
///
/// 已有内容不会被覆盖：重复运行只会补缺失的部分。
/// </summary>
public static class CollapsingBlockSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string ClipPath = AnimationFolder + "/CollapsingBlock.anim";
    private const string ControllerPath = AnimationFolder + "/CollapsingBlock.controller";

    private const string CollapseStateName = "Collapse";
    private const string SpriteNameToken = "CollapsingBlock2x";

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

        bool changed = EnsureAssets();
        if (changed)
            Debug.Log("[CollapsingBlockSetup] 已自动补全碎裂平台的动画状态机。");

        AutoWireSceneBlocks();
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Collapsing Block/Setup Collapsing Blocks In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireSceneBlocks(repairExisting: true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Collapsing Block/Rebuild Collapsing Block Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed
            ? "[CollapsingBlockSetup] 碎裂平台资源已补全。"
            : "[CollapsingBlockSetup] 碎裂平台资源已经完整，无需修改。");
    }
    #endregion

    #region 资源生成
    private static bool EnsureAssets()
    {
        bool changed = EnsureController();
        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    private static bool EnsureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"[CollapsingBlockSetup] 找不到状态机 {ControllerPath}");
            return false;
        }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogWarning($"[CollapsingBlockSetup] 找不到序列帧剪辑 {ClipPath}");
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

        changed |= SetLoop(clip, loop: false);
        changed |= EnsureState(stateMachine, controller, CollapseStateName, clip);

        AnimatorState collapseState = FindState(stateMachine, CollapseStateName);
        if (collapseState != null && stateMachine.defaultState != collapseState)
        {
            stateMachine.defaultState = collapseState;
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(controller);
        return changed;
    }

    private static bool EnsureState(
        AnimatorStateMachine stateMachine,
        AnimatorController controller,
        string stateName,
        AnimationClip clip)
    {
        bool changed = false;

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
    private static void AutoWireSceneBlocks(bool repairExisting = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);

        if (controller == null)
        {
            Debug.LogWarning("[CollapsingBlockSetup] 状态机缺失，无法自动装配碎裂平台。");
            return;
        }

        SpriteRenderer[] renderers = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer blockRenderer in renderers)
        {
            if (blockRenderer == null || blockRenderer.sprite == null) continue;

            bool isBlock = blockRenderer.sprite.name.Contains(SpriteNameToken)
                           || blockRenderer.gameObject.name.StartsWith(SpriteNameToken);
            if (!isBlock) continue;

            GameObject go = blockRenderer.gameObject;
            bool sceneChanged = false;

            // 1) Animator
            Animator animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(go);
                sceneChanged = true;
            }

            if (animator.runtimeAnimatorController == null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                sceneChanged = true;
            }

            // 2) 玩法脚本
            CollapsingBlockTrap trap = go.GetComponent<CollapsingBlockTrap>();
            bool added = false;

            if (trap == null)
            {
                trap = Undo.AddComponent<CollapsingBlockTrap>(go);
                added = true;
                sceneChanged = true;
            }

            bool forceRefs = added || repairExisting;
            SerializedObject so = new SerializedObject(trap);

            SetObjectReference(so, "animator", animator, forceRefs);
            SetObjectReference(so, "collapseClip", clip, forceRefs);
            SetStringIfEmpty(so, "collapseStateName", CollapseStateName);

            if (added)
            {
                so.FindProperty("standTime").floatValue = 0.5f;
                so.FindProperty("holdFrameIndex").intValue = 10;
                so.FindProperty("holdDuration").floatValue = 0.3f;
                so.FindProperty("requireFromTop").boolValue = true;
                so.FindProperty("topTolerance").floatValue = 0.15f;
                so.FindProperty("resetToFirstFrameOnIdle").boolValue = true;
                so.FindProperty("debugLogs").boolValue = false;
            }

            bool serializedChanged = so.ApplyModifiedPropertiesWithoutUndo();

            if (sceneChanged || serializedChanged)
            {
                EditorUtility.SetDirty(trap);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            if (added)
                Debug.Log($"[CollapsingBlockSetup] 已给「{go.name}」挂上 CollapsingBlockTrap，站立 0.5s 后碎裂、010 帧停顿 0.3s 后复原。", go);
        }
    }

    private static void SetObjectReference(SerializedObject so, string propertyName, Object value, bool force)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null) return;
        if (!force && property.objectReferenceValue != null) return;
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
