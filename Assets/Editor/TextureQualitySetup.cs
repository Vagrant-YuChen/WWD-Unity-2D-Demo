using UnityEditor;
using UnityEngine;

/// <summary>
/// 2D 素材导入设置统一工具。
/// 作用：把所有贴图的最大导入尺寸提高到 4096、使用高质量压缩，
/// 保证你放进来的 2K/4K 原图不会被 Unity 截断或严重压缩变糊。
///
/// 注意：这个工具只能“避免 Unity 把有细节的图压糊”，
/// 无法把本来就只有 200~300px 的低清原图变成真 2K——那种必须换源文件。
/// 菜单：Tools > Textures > Normalize 2D Import Settings
/// </summary>
public static class TextureQualitySetup
{
    private const int MaxTextureSize = 4096;

    [MenuItem("Tools/Textures/Normalize 2D Import Settings")]
    public static void NormalizeAllTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        int changed = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool dirty = false;

                if (importer.maxTextureSize < MaxTextureSize)
                {
                    importer.maxTextureSize = MaxTextureSize;
                    dirty = true;
                }
                if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
                {
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.compressionQuality = 100;
                    dirty = true;
                }
                // 关闭纹理流式加载，避免运行时按距离降采样导致变糊。
                if (importer.streamingMipmaps)
                {
                    importer.streamingMipmaps = false;
                    dirty = true;
                }

                dirty |= ApplyPlatform(importer, "Standalone");
                dirty |= ApplyPlatform(importer, "WebGL");

                if (dirty)
                {
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    changed++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[TextureQualitySetup] 完成：{changed} 个贴图已统一为 最大 4096 / 高质量压缩 / 关闭流式加载。（未改动 Mipmap 与 Filter）");
    }

    private static bool ApplyPlatform(TextureImporter importer, string platform)
    {
        TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
        bool dirty = false;

        if (s.maxTextureSize < MaxTextureSize) { s.maxTextureSize = MaxTextureSize; dirty = true; }
        if (s.textureCompression != TextureImporterCompression.CompressedHQ) { s.textureCompression = TextureImporterCompression.CompressedHQ; s.compressionQuality = 100; dirty = true; }
        if (!s.overridden) { s.overridden = true; dirty = true; }
        if (s.crunchedCompression) { s.crunchedCompression = false; dirty = true; }

        if (dirty)
        {
            s.name = platform;
            importer.SetPlatformTextureSettings(s);
        }
        return dirty;
    }
}
