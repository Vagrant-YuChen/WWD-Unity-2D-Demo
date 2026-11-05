using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 火焰喷射器玩法的“一键装配”工具。
///
/// 打开工程 / 编译后会自动：
///   1) 把火焰 9 帧的轴心统一到「火焰根部」，让整段动画的火焰始终从左上角喷火口喷出；
///   2) 把已有动画 FlameThrower 的精灵曲线重新绑定到子物体 Flame（本体继续留在根物体上）；
///   3) 生成只隐藏火焰的待机剪辑 FlameThrowerIdle.anim；
///   4) 把空白的 FlameThrower.controller 填成 Spray → Idle 两个状态；
///   5) 在打开的场景里找到喷火器本体，自动补上 Flame 子物体、Animator 和 FlameThrowerTrap，
///      并把伤害帧默认设为 2-5（Flame.000 / .001 / .002 / FlameOff.000）。
///
/// 也可以随时用菜单手动重跑：
///   Tools/Flame Thrower/Setup Flame Throwers In Open Scene
///   Tools/Flame Thrower/Rebuild Flame Thrower Assets
///
/// 已有内容不会被覆盖：重复运行只会补缺失的部分。
/// </summary>
public static class FlameThrowerSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string ClipPath = AnimationFolder + "/FlameThrower.anim";
    private const string IdleClipPath = AnimationFolder + "/FlameThrowerIdle.anim";
    private const string ControllerPath = AnimationFolder + "/FlameThrower.controller";
    private const string BodySpritePath = "Assets/Sprite/FlameThrower-NewFlameThrowerBody.000.png";

    private const string SprayStateName = "Spray";
    private const string IdleStateName = "Idle";
    private const string FlameChildName = "Flame";
    private const int UnifiedFlameWidth = 186;
    private const int UnifiedFlameHeight = 140;

    /// <summary>本体压在道具层（2），火焰比本体低一层，喷火口的接缝会被本体盖住。</summary>
    private const int MinimumBodySortingOrder = 2;

    /// <summary>编辑模式下火焰的预览贴图：用伤害帧里最大的一张，方便直接看着火焰调长度 / 宽度 / 位置。</summary>
    private const string PreviewSpritePath = "Assets/Sprite/FlameThrower-Flame.000.png";

    /// <summary>新装配火焰的默认外观倍率：宽度 1.2 倍、长度 2 倍（长度沿火焰纵向，根部固定在喷火口）。</summary>
    private const float DefaultFlameWidthScale = 1.2f;
    private const float DefaultFlameLengthScale = 2f;

    /// <summary>喷火口在本体贴图上的像素位置（左上角突出的那个口）。</summary>
    private static readonly Vector2 NozzlePixel = new Vector2(17.5f, 2.5f);

    /// <summary>9 帧火焰贴图，顺序必须和 FlameThrower.anim 一致。</summary>
    private static readonly string[] FlameSpritePaths =
    {
        "Assets/Sprite/FlameThrower-FlameOn.000.png",
        "Assets/Sprite/FlameThrower-FlameOn.001.png",
        "Assets/Sprite/FlameThrower-Flame.000.png",
        "Assets/Sprite/FlameThrower-Flame.001.png",
        "Assets/Sprite/FlameThrower-Flame.002.png",
        "Assets/Sprite/FlameThrower-FlameOff.000.png",
        "Assets/Sprite/FlameThrower-FlameOff.001.png",
        "Assets/Sprite/FlameThrower-FlameOff.002.png",
        "Assets/Sprite/FlameThrower-FlameOff.003.png"
    };

    /// <summary>统一画布 186×140，所有帧共用同一轴心像素位置（从左下角算）。</summary>
    private const float FlamePivotX = 0.6290323f;
    private const float FlamePivotY = 0.0214286f;

    private static readonly Vector2[] FlameSpritePivots =
    {
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY),
        new Vector2(FlamePivotX, FlamePivotY)
    };

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
            Debug.Log("[FlameThrowerSetup] 已自动补全火焰喷射器的动画 / 状态机 / 喷火口对齐。");

        AutoWireSceneFlameThrowers();
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Flame Thrower/Setup Flame Throwers In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireSceneFlameThrowers(repairExisting: true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Flame Thrower/Rebuild Flame Thrower Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed ? "[FlameThrowerSetup] 火焰喷射器资源已补全。" : "[FlameThrowerSetup] 火焰喷射器资源已经完整，无需修改。");
    }
    #endregion

    #region 资源生成
    private static bool EnsureAssets()
    {
        bool changed = false;

        changed |= ReimportStaleFlameTextures();
        changed |= FixFlameSpritePivots();
        changed |= EnsureFlameClipBinding();
        changed |= EnsureIdleClip();
        changed |= EnsureController();

        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    /// <summary>
    /// 如果 9 帧火焰贴图的导入尺寸不是统一画布（比如在工程外被重新打包、而 Unity 仍在用旧的缓存贴图），
    /// 强制重新导入，确保序列帧动画里换上的就是统一后的 186×140 帧，而不是原先的旧图。
    /// </summary>
    private static bool ReimportStaleFlameTextures()
    {
        bool changed = false;
        foreach (string path in FlameSpritePaths)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            bool stale = sprite == null
                || Mathf.RoundToInt(sprite.rect.width) != UnifiedFlameWidth
                || Mathf.RoundToInt(sprite.rect.height) != UnifiedFlameHeight;

            if (!stale) continue;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            changed = true;
        }

        if (changed)
            Debug.Log("[FlameThrowerSetup] 检测到火焰序列帧尺寸与统一画布不一致，已强制重新导入 9 帧贴图。");

        return changed;
    }

    private static bool FixFlameSpritePivots()
    {
        bool changed = false;

        for (int i = 0; i < FlameSpritePaths.Length && i < FlameSpritePivots.Length; i++)
        {
            string path = FlameSpritePaths[i];
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            if (importer.spriteImportMode != SpriteImportMode.Single) continue;

            Vector2 target = FlameSpritePivots[i];

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool alreadyCorrect =
                settings.spriteAlignment == (int)SpriteAlignment.Custom &&
                Mathf.Abs(settings.spritePivot.x - target.x) < 0.0005f &&
                Mathf.Abs(settings.spritePivot.y - target.y) < 0.0005f;

            if (alreadyCorrect) continue;

            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = target;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            changed = true;
        }

        if (changed)
            Debug.Log("[FlameThrowerSetup] 已将 9 帧火焰轴心统一到共同锚点，换帧时火焰不会离开喷火口。");

        return changed;
    }

    /// <summary>
    /// 用户生成的 FlameThrower.anim 默认把 9 张火焰图绑在根物体的 SpriteRenderer 上，
    /// 这样播放时会把喷火器本体替换成火焰。这里把 m_Sprite 曲线改到子物体 Flame，
    /// 本体永远留在根物体上，火焰则从左上角喷火口喷出。
    /// </summary>
    private static bool EnsureFlameClipBinding()
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogWarning($"[FlameThrowerSetup] 找不到喷火动画 {ClipPath}");
            return false;
        }

        bool changed = false;
        EditorCurveBinding targetBinding =
            EditorCurveBinding.PPtrCurve(FlameChildName, typeof(SpriteRenderer), "m_Sprite");

        EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding binding = bindings[i];
            if (binding.propertyName != "m_Sprite") continue;
            if (binding.path == FlameChildName) continue;

            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys == null || keys.Length == 0) continue;

            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clip, targetBinding, keys);
            changed = true;
        }

        // 喷火状态里显式把火焰渲染器打开，避免从 Idle（enabled=0）切回来时闪一帧空
        EditorCurveBinding enabledBinding =
            EditorCurveBinding.FloatCurve(FlameChildName, typeof(SpriteRenderer), "m_Enabled");
        AnimationCurve enabledCurve = AnimationUtility.GetEditorCurve(clip, enabledBinding);
        if (enabledCurve == null || enabledCurve.length == 0)
        {
            AnimationUtility.SetEditorCurve(clip, enabledBinding,
                AnimationCurve.Constant(0f, Mathf.Max(0.01f, clip.length), 1f));
            changed = true;
        }

        AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        if (clipSettings.loopTime)
        {
            clipSettings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(clip);
        return changed;
    }

    private static bool EnsureIdleClip()
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        bool created = false;

        if (clip == null)
        {
            clip = new AnimationClip
            {
                name = "FlameThrowerIdle",
                frameRate = 12f
            };
            created = true;
        }

        bool changed = created;

        EditorCurveBinding enabledBinding =
            EditorCurveBinding.FloatCurve(FlameChildName, typeof(SpriteRenderer), "m_Enabled");
        AnimationCurve enabledCurve = AnimationUtility.GetEditorCurve(clip, enabledBinding);
        if (enabledCurve == null || enabledCurve.length == 0 || enabledCurve.Evaluate(0f) > 0.001f)
        {
            AnimationUtility.SetEditorCurve(clip, enabledBinding,
                AnimationCurve.Constant(0f, 1f / 12f, 0f));
            changed = true;
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (!settings.loopTime || !Mathf.Approximately(settings.stopTime, 1f / 12f))
        {
            settings.loopTime = true;
            settings.stopTime = 1f / 12f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            changed = true;
        }

        if (created)
        {
            AssetDatabase.CreateAsset(clip, IdleClipPath);
            return true;
        }

        if (changed) EditorUtility.SetDirty(clip);
        return changed;
    }

    private static bool EnsureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"[FlameThrowerSetup] 找不到状态机 {ControllerPath}");
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

        AnimationClip sprayClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
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
            Debug.LogWarning($"[FlameThrowerSetup] 找不到动画剪辑，无法创建状态 {stateName}。");
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
    /// 这条过渡保证就算只靠 Animator 也能自然停回无火焰待机。
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
    private static void AutoWireSceneFlameThrowers(bool repairExisting = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        AnimationClip sprayClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        Sprite previewSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PreviewSpritePath);

        if (controller == null)
        {
            Debug.LogWarning("[FlameThrowerSetup] 状态机缺失，无法自动装配火焰喷射器。");
            return;
        }

        SpriteRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer bodyRenderer in renderers)
        {
            if (bodyRenderer == null || bodyRenderer.sprite == null) continue;

            bool isBody = bodyRenderer.sprite.name.Contains("NewFlameThrowerBody")
                          || bodyRenderer.gameObject.name == "FlameThrower";
            if (!isBody) continue;

            GameObject go = bodyRenderer.gameObject;
            Vector3 flameLocalPosition = CalculateFlameLocalPosition();
            bool sceneChanged = false;

            if (bodyRenderer.sortingOrder < MinimumBodySortingOrder)
            {
                Undo.RecordObject(bodyRenderer, "Raise Flame Thrower Sorting Order");
                bodyRenderer.sortingOrder = MinimumBodySortingOrder;
                EditorUtility.SetDirty(bodyRenderer);
                sceneChanged = true;
            }

            // 1) 左上角喷火口处的火焰子物体
            Transform flameTransform = go.transform.Find(FlameChildName);
            bool createdFlame = false;

            if (flameTransform == null)
            {
                GameObject flameGo = new GameObject(FlameChildName);
                Undo.RegisterCreatedObjectUndo(flameGo, "Create Flame Child");
                flameGo.transform.SetParent(go.transform, false);
                flameTransform = flameGo.transform;
                createdFlame = true;
                sceneChanged = true;
            }

            SpriteRenderer flameRenderer = flameTransform.GetComponent<SpriteRenderer>();
            if (flameRenderer == null)
            {
                flameRenderer = Undo.AddComponent<SpriteRenderer>(flameTransform.gameObject);
                createdFlame = true;
                sceneChanged = true;
            }

            if (createdFlame || repairExisting)
            {
                flameTransform.localPosition = flameLocalPosition;
                flameTransform.localRotation = Quaternion.identity;

                // 新建火焰时套用默认长度 / 宽度倍率；已存在时保留用户在 Scene 里调好的缩放
                if (createdFlame)
                    flameTransform.localScale = new Vector3(DefaultFlameWidthScale, DefaultFlameLengthScale, 1f);

                Undo.RecordObject(flameRenderer, "Configure Flame Renderer");
                flameRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
                flameRenderer.sortingOrder = Mathf.Max(0, bodyRenderer.sortingOrder - 1);
                flameRenderer.color = Color.white;
                flameRenderer.enabled = true;
                EditorUtility.SetDirty(flameRenderer);
                sceneChanged = true;
            }

            // 编辑模式下给火焰渲染器贴一张预览图，Scene 里能直接看到火焰、调长度 / 宽度 / 位置，
            // 运行时会由动画重新覆盖这张图。已经有图（例如动画帧）时不要覆盖。
            if (!Application.isPlaying && previewSprite != null && flameRenderer.sprite == null)
            {
                Undo.RecordObject(flameRenderer, "Assign Flame Preview Sprite");
                flameRenderer.sprite = previewSprite;
                EditorUtility.SetDirty(flameRenderer);
                sceneChanged = true;
            }

            // 2) Animator
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

            // 3) 玩法脚本
            FlameThrowerTrap trap = go.GetComponent<FlameThrowerTrap>();
            bool added = false;

            if (trap == null)
            {
                trap = Undo.AddComponent<FlameThrowerTrap>(go);
                added = true;
                sceneChanged = true;
            }

            bool forceRefs = added || repairExisting;
            SerializedObject so = new SerializedObject(trap);

            SetObjectReference(so, "flameAnimator", animator, forceRefs);
            SetObjectReference(so, "bodyRenderer", bodyRenderer, forceRefs);
            SetObjectReference(so, "flameRenderer", flameRenderer, forceRefs);
            SetObjectReference(so, "sprayClip", sprayClip, forceRefs);
            SetObjectReference(so, "idleClip", idleClip, forceRefs);
            SetObjectReference(so, "flamePreviewSprite", previewSprite, forceRefs);
            SetStringIfEmpty(so, "sprayStateName", SprayStateName);
            SetStringIfEmpty(so, "idleStateName", IdleStateName);

            if (added)
            {
                // 默认值对应当前 9 帧素材：第 2-5 帧（Flame.000 / .001 / .002 / FlameOff.000）有伤害
                so.FindProperty("idleHoldTime").floatValue = 0.4f;
                so.FindProperty("startDelay").floatValue = 0f;
                so.FindProperty("playbackSpeed").floatValue = 1f;
                so.FindProperty("playerLayer").intValue = ~0;
                so.FindProperty("damageBoxSize").vector2Value = new Vector2(2.49f, 0.85f);
                so.FindProperty("damageBoxRotation").floatValue = -50.84f;
                so.FindProperty("damageBoxOffset").vector2Value = new Vector2(-0.61f, 0.98f);
                so.FindProperty("damageStartFrame").intValue = 2;
                so.FindProperty("damageEndFrame").intValue = 5;
                so.FindProperty("totalFramesOverride").intValue = 0;
                so.FindProperty("killPlayerOnHit").boolValue = true;
                so.FindProperty("drawDamageGizmo").boolValue = true;
                so.FindProperty("alwaysShowDamageGizmo").boolValue = true;
                so.FindProperty("showSpriteBoundsGizmo").boolValue = true;
            }

            bool serializedChanged = so.ApplyModifiedPropertiesWithoutUndo();

            if (sceneChanged || serializedChanged)
            {
                EditorUtility.SetDirty(trap);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            if (added)
                Debug.Log($"[FlameThrowerSetup] 已给「{go.name}」挂上 FlameThrowerTrap，火焰对齐到左上角喷火口，伤害帧 2-5。", go);
        }
    }

    /// <summary>
    /// 把喷火口像素位置换算成火焰子物体在本体本地坐标里的位置。
    /// 本体轴心是中心，所以直接 (像素 - 轴心像素) / PPU。
    /// </summary>
    private static Vector3 CalculateFlameLocalPosition()
    {
        Sprite bodySprite = AssetDatabase.LoadAssetAtPath<Sprite>(BodySpritePath);
        if (bodySprite == null) return new Vector3(-0.22f, 0.44f, 0f);

        float ppu = bodySprite.pixelsPerUnit > 0.01f ? bodySprite.pixelsPerUnit : 100f;
        Vector2 pivotPx = bodySprite.pivot;
        float nozzlePxYFromBottom = bodySprite.rect.height - NozzlePixel.y;

        return new Vector3(
            (NozzlePixel.x - pivotPx.x) / ppu,
            (nozzlePxYFromBottom - pivotPx.y) / ppu,
            0f);
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
