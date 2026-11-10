using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 打包脚本：Windows .exe（给朋友测/本机检验）、WebGL（网页分享）。
/// 菜单：Tools > Build
///   - Windows (.exe)                仅打包
///   - Build &amp; Run Windows (部署本机) 打包后自动启动全屏游戏（改一张图就能立刻看效果）
///   - WebGL (网页版)                 仅打包
///   - Build All                     两个都打
/// 命令行（batchmode）：
///   Unity.exe -batchmode -quit -projectPath "..." -executeMethod BuildScript.BuildWindows
///   Unity.exe -batchmode -quit -projectPath "..." -executeMethod BuildScript.BuildWebGL
/// </summary>
public static class BuildScript
{
    private const string WindowsDir = "Builds/Windows";
    private const string WebGLDir = "Builds/WebGL";
    private const string ExeName = "WackyWorldDash.exe";

    [MenuItem("Tools/Build/Windows (.exe)")]
    public static void BuildWindows()
    {
        BuildWindowsInternal();
    }

    /// <summary>打包 Windows 后直接启动全屏游戏，用于"改一张图立刻验一张"。</summary>
    [MenuItem("Tools/Build/Build & Run Windows (部署本机)")]
    public static void BuildAndRunWindows()
    {
        if (IsWindowsExeRunning())
        {
            EditorUtility.DisplayDialog("游戏正在运行",
                "请先关闭正在运行的 WackyWorldDash.exe，再点打包（否则 exe 被占用、无法覆盖）。", "好");
            return;
        }

        if (BuildWindowsInternal())
        {
            string full = Path.GetFullPath(Path.Combine(WindowsDir, ExeName));
            Debug.Log($"[BuildScript] 启动游戏：{full}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full),
                UseShellExecute = true
            });
        }
    }

    [MenuItem("Tools/Build/WebGL (网页版)")]
    public static void BuildWebGL()
    {
        ApplyResolutionSettings();

        // 开启解压回退：让 brotli 压缩包在没有特殊响应头的普通静态服务器上也能加载。
        PlayerSettings.WebGL.decompressionFallback = true;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = WebGLDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };

        RunBuild(options, "WebGL");
    }

    [MenuItem("Tools/Build/Build All")]
    public static void BuildAll()
    {
        BuildWindows();
        BuildWebGL();
    }

    private static bool BuildWindowsInternal()
    {
        ApplyResolutionSettings();

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = Path.Combine(WindowsDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        return RunBuild(options, "Windows");
    }

    private static void ApplyResolutionSettings()
    {
        // 目标画质 2560x1440（16:9）。无边框全屏，便于按真实像素检验清晰度。
        PlayerSettings.defaultScreenWidth = 2560;
        PlayerSettings.defaultScreenHeight = 1440;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
    }

    private static string[] EnabledScenes()
    {
        return EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
    }

    private static bool IsWindowsExeRunning()
    {
        string full = Path.GetFullPath(Path.Combine(WindowsDir, ExeName));
        if (!File.Exists(full)) return false;
        try
        {
            using (new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static bool RunBuild(BuildPlayerOptions options, string label)
    {
        string dir = Path.GetDirectoryName(options.locationPathName);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        Debug.Log($"[BuildScript] 开始构建 {label} -> {options.locationPathName}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] {label} 构建成功：{summary.totalSize / (1024 * 1024)} MB，耗时 {summary.totalTime}");
            return true;
        }

        string errors = string.Join("\n",
            report.steps.SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                .Select(m => m.content));
        Debug.LogError($"[BuildScript] {label} 构建失败：{summary.result}\n{errors}");
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
        else
            throw new BuildFailedException($"Build {label} failed: {summary.result}");
        return false;
    }
}
