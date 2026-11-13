#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 编辑器辅助：在编辑模式（Scene 视图）下，把 <see cref="IntroCameraSequence"/> 自动挂到
/// 关卡里的 Main Camera（带 <see cref="ZoomCameraRig"/> 的那台）上。
///
/// 这样你不运行游戏也能在 Inspector 里查看 / 调整开场镜头参数，改完保存场景即可持久生效，
/// 不会像运行时临时挂载那样"重开就丢"。运行时的自动挂载仍保留作为兜底（例如构建后场景里没存组件时）。
///
/// 只影响编辑模式；进入播放模式后会跳过。可通过 Undo 撤销本次自动添加。
/// </summary>
[InitializeOnLoad]
internal static class IntroCameraSequenceInstaller
{
    static IntroCameraSequenceInstaller()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        // 域重载 / 编译完成后，对当前已打开的场景也补一次。
        EditorApplication.delayCall += EnsureAllOpenScenes;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += EnsureAllOpenScenes;
    }

    private static void EnsureAllOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            EnsureScene(scene);
        }
    }

    private static void EnsureScene(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
        {
            ZoomCameraRig[] rigs = roots[r].GetComponentsInChildren<ZoomCameraRig>(true);
            for (int k = 0; k < rigs.Length; k++)
            {
                ZoomCameraRig rig = rigs[k];
                if (rig == null) continue;
                if (rig.GetComponent<IntroCameraSequence>() != null) continue;

                Undo.AddComponent<IntroCameraSequence>(rig.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
#endif
