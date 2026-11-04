using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 蜂群的“一键装配”工具：
///   1) 从 Assets/Sprite 读取 Beehive-Bee1 / Bee2 / Bee3 三组序列帧，
///      生成 BeeFly1/2/3.anim 和对应的 AnimatorController；
///   2) 在打开的场景里找到蜂巢，挂上 BeeSwarmController；
///   3) 把三组动画机、首帧、蜂巢 SpriteRenderer 自动填好；
///   4) 把 BeeHiveExplosion 的 beeSwarm 引用连上。
///
/// 也可以手动执行：
///   Tools/Bee Hive/Setup Bee Swarm In Open Scene
///   Tools/Bee Hive/Rebuild Bee Swarm Assets
/// </summary>
public static class BeeSwarmSetup
{
    private const string AnimationFolder = "Assets/Animation";
    private const string StateName = "Fly";
    private const float FrameRate = 15f;

    private const string HiveObjectName = "Beehive-Beehive.000";

    private static readonly string[] SpritePatterns =
    {
        "Beehive-Bee1.*.png",
        "Beehive-Bee2.*.png",
        "Beehive-Bee3.*.png"
    };

    private static readonly string[] ClipPaths =
    {
        AnimationFolder + "/BeeFly1.anim",
        AnimationFolder + "/BeeFly2.anim",
        AnimationFolder + "/BeeFly3.anim"
    };

    private static readonly string[] ControllerPaths =
    {
        AnimationFolder + "/BeeFly1.controller",
        AnimationFolder + "/BeeFly2.controller",
        AnimationFolder + "/BeeFly3.controller"
    };

    private static readonly List<AnimationClip> Clips = new List<AnimationClip>();
    private static readonly List<AnimatorController> Controllers = new List<AnimatorController>();
    private static readonly List<Sprite> PreviewSprites = new List<Sprite>();

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
        bool sceneChanged = AutoWireScene();

        if (assetsChanged || sceneChanged)
            Debug.Log("[BeeSwarmSetup] 已补全蜜蜂动画机 / 场景引用。");
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Bee Hive/Setup Bee Swarm In Open Scene", priority = 3)]
    public static void MenuSetupScene()
    {
        EnsureAssets();
        AutoWireScene();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Bee Hive/Rebuild Bee Swarm Assets", priority = 4)]
    public static void MenuRebuildAssets()
    {
        bool changed = EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log(changed
            ? "[BeeSwarmSetup] 蜜蜂动画资源已重建。"
            : "[BeeSwarmSetup] 蜜蜂动画资源已经完整，无需修改。");
    }
    #endregion

    #region 资源：三组动画 / 状态机
    private static bool EnsureAssets()
    {
        bool changed = false;

        Clips.Clear();
        Controllers.Clear();
        PreviewSprites.Clear();

        for (int i = 0; i < SpritePatterns.Length; i++)
        {
            Sprite[] sprites = LoadSprites(SpritePatterns[i]);
            if (sprites.Length == 0)
            {
                Debug.LogWarning($"[BeeSwarmSetup] 没找到匹配 {SpritePatterns[i]} 的蜜蜂序列帧。");
                Clips.Add(null);
                Controllers.Add(null);
                PreviewSprites.Add(null);
                continue;
            }

            AnimationClip clip = EnsureClip(ClipPaths[i], sprites, ref changed);
            AnimatorController controller = EnsureController(ControllerPaths[i], clip, ref changed);

            Clips.Add(clip);
            Controllers.Add(controller);
            PreviewSprites.Add(sprites[0]);
        }

        if (changed) AssetDatabase.SaveAssets();
        return changed;
    }

    private static Sprite[] LoadSprites(string pattern)
    {
        string spriteFolder = Path.Combine(Application.dataPath, "Sprite");
        if (!Directory.Exists(spriteFolder)) return new Sprite[0];

        string projectRoot = Directory.GetParent(Application.dataPath).FullName
            .Replace('\\', '/');

        string[] files = Directory.GetFiles(spriteFolder, pattern);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var sprites = new List<Sprite>();
        foreach (string file in files)
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Length <= projectRoot.Length + 1) continue;

            string assetPath = normalized.Substring(projectRoot.Length + 1);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null) sprites.Add(sprite);
        }

        return sprites.ToArray();
    }

    private static AnimationClip EnsureClip(string path, Sprite[] sprites, ref bool changed)
    {
        if (sprites == null || sprites.Length == 0) return null;

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(path),
                frameRate = FrameRate
            };
            AssetDatabase.CreateAsset(clip, path);
            changed = true;
        }

        if (!Mathf.Approximately(clip.frameRate, FrameRate))
        {
            clip.frameRate = FrameRate;
            changed = true;
        }

        if (clip.legacy)
        {
            clip.legacy = false;
            changed = true;
        }

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
            string.Empty, typeof(SpriteRenderer), "m_Sprite");

        // 多补一个“保持最后一帧”的关键帧，保证每一帧都完整显示 1/FrameRate 秒，
        // 否则最后一帧在循环边界上几乎不显示。
        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[sprites.Length + 1];
        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / FrameRate,
                value = sprites[i]
            };
        }

        keys[sprites.Length] = new ObjectReferenceKeyframe
        {
            time = sprites.Length / FrameRate,
            value = sprites[sprites.Length - 1]
        };

        ObjectReferenceKeyframe[] existing = AnimationUtility.GetObjectReferenceCurve(clip, binding);
        if (!SameKeys(existing, keys))
        {
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            changed = true;
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (!settings.loopTime)
        {
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(clip);
        return clip;
    }

    private static bool SameKeys(ObjectReferenceKeyframe[] a, ObjectReferenceKeyframe[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].value != b[i].value) return false;
            if (Mathf.Abs(a[i].time - b[i].time) > 0.0001f) return false;
        }

        return true;
    }

    private static AnimatorController EnsureController(string path, AnimationClip clip, ref bool changed)
    {
        if (clip == null) return null;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            changed = true;
        }

        // 空白 controller 的 Layer 权重可能是 0，那样状态不会播放。
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
        if (stateMachine == null) return controller;

        AnimatorState state = stateMachine.states
            .Select(s => s.state)
            .FirstOrDefault(s => s != null && s.name == StateName);

        if (state == null)
        {
            state = stateMachine.AddState(StateName);
            state.hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(state))
                AssetDatabase.AddObjectToAsset(state, controller);
            changed = true;
        }

        if (state.motion != clip)
        {
            state.motion = clip;
            changed = true;
        }

        if (!Mathf.Approximately(state.speed, 1f))
        {
            state.speed = 1f;
            changed = true;
        }

        if (stateMachine.defaultState != state)
        {
            stateMachine.defaultState = state;
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(controller);
        return controller;
    }
    #endregion

    #region 场景自动装配
    private static bool AutoWireScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

        GameObject hive = FindHive();
        if (hive == null) return false;

        SpriteRenderer hiveRenderer = hive.GetComponent<SpriteRenderer>();
        if (hiveRenderer == null) return false;

        bool changed = false;

        BeeSwarmController swarm = hive.GetComponent<BeeSwarmController>();
        if (swarm == null)
        {
            swarm = Undo.AddComponent<BeeSwarmController>(hive);
            changed = true;
        }

        SerializedObject swarmSerialized = new SerializedObject(swarm);

        changed |= SetObjectArrayIfDifferent(
            swarmSerialized, "beeAnimators",
            Controllers.Cast<UnityEngine.Object>().ToArray());
        changed |= SetObjectArrayIfDifferent(
            swarmSerialized, "beePreviewSprites",
            PreviewSprites.Cast<UnityEngine.Object>().ToArray());
        changed |= SetObjectReferenceIfNull(swarmSerialized, "hiveRenderer", hiveRenderer);

        // 旧场景里指示蜜蜂默认是 3 只：一次性迁移到 5 只，之后不再覆盖手动设置。
        SerializedProperty versionProp = swarmSerialized.FindProperty("configMigrationVersion");
        if (versionProp != null && versionProp.intValue < 1)
        {
            SerializedProperty indicatorCountProp = swarmSerialized.FindProperty("indicatorCount");
            if (indicatorCountProp != null && indicatorCountProp.intValue == 3)
                indicatorCountProp.intValue = 5;

            versionProp.intValue = 1;
            changed = true;
        }

        if (swarmSerialized.ApplyModifiedPropertiesWithoutUndo() && !changed)
            changed = true;

        // 把蜂巢爆炸脚本的 beeSwarm 连上（运行时也有 GetComponent 兜底）。
        BeeHiveExplosion explosion = hive.GetComponent<BeeHiveExplosion>();
        if (explosion != null)
        {
            SerializedObject explosionSerialized = new SerializedObject(explosion);
            if (SetObjectReferenceIfNull(explosionSerialized, "beeSwarm", swarm))
            {
                explosionSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(explosion);
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(swarm);
            EditorSceneManager.MarkSceneDirty(hive.scene);
        }

        return changed;
    }

    private static bool SetObjectArrayIfDifferent(
        SerializedObject so, string propertyName, UnityEngine.Object[] values)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null) return false;

        bool different = property.arraySize != values.Length;
        if (!different)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue != values[i])
                {
                    different = true;
                    break;
                }
            }
        }

        if (!different) return false;

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

        return true;
    }

    private static bool SetObjectReferenceIfNull(
        SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue != null || value == null)
            return false;

        property.objectReferenceValue = value;
        return true;
    }

    private static GameObject FindHive()
    {
        GameObject named = GameObject.Find(HiveObjectName);
        if (named != null && named.GetComponent<SpriteRenderer>() != null)
            return named;

        SpriteRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null) continue;

            string spriteName = renderer.sprite.name.ToLowerInvariant();
            if (spriteName.StartsWith("beehive-beehive"))
                return renderer.gameObject;
        }

        return null;
    }
    #endregion
}
