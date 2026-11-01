using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click cleanup for blurry 2D sprites:
///   - raises the texture size cap so 4K art is not silently downscaled
///   - enables mipmaps (helps non-integer on-screen scaling, neutral at 1:1)
///   - switches Standalone compression to High Quality
/// Run via menu: Tools/Improve Sprite Clarity
/// </summary>
public static class ImproveSpriteClarity
{
    // Set to FilterMode.Point if your art is pixel-art; keep Bilinear for smooth high-res art.
    private const FilterMode TargetFilterMode = FilterMode.Bilinear;

    [MenuItem("Tools/Improve Sprite Clarity")]
    public static void Run()
    {
        if (!EditorUtility.DisplayDialog(
                "Improve Sprite Clarity",
                "This will reimport ALL Texture2D assets in the project with:\n" +
                "  - maxTextureSize = 4096\n" +
                "  - mipmaps enabled\n" +
                "  - filter mode = " + TargetFilterMode + "\n" +
                "  - Standalone compression = High Quality (quality 100)\n\n" +
                "Mobile platforms keep their default compression. Continue?",
                "Run", "Cancel"))
        {
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D");
        int total = guids.Length;
        int changed = 0;

        for (int i = 0; i < total; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = false;

            if (importer.maxTextureSize < 4096)
            {
                importer.maxTextureSize = 4096;
                dirty = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                dirty = true;
            }

            if (importer.filterMode != TargetFilterMode)
            {
                importer.filterMode = TargetFilterMode;
                dirty = true;
            }

            // High-quality compression for the Standalone (PC) target only.
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden == false ||
                standalone.textureCompression != TextureImporterCompression.CompressedHQ ||
                standalone.compressionQuality != 100)
            {
                standalone.overridden = true;
                standalone.textureCompression = TextureImporterCompression.CompressedHQ;
                standalone.compressionQuality = 100;
                importer.SetPlatformTextureSettings(standalone);
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                changed++;
            }

            if (i % 50 == 0)
            {
                EditorUtility.DisplayProgressBar("Improve Sprite Clarity",
                    path, (float)i / total);
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ImproveSpriteClarity] Reimported {changed}/{total} textures.");
        EditorUtility.DisplayDialog("Done",
            $"Reimported {changed} of {total} textures.", "OK");
    }
}
