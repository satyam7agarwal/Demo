#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Level 23 v3: synchronize completion stars with LevelManager's actual
/// per-level mastery award. The result view must never recalculate stars
/// using an unrelated legacy 1/2/3-shot-only formula.
/// 
/// Automatically upgrades existing local sources once, preserving their
/// .meta GUIDs, level 22 patches, and other per-level game content.
/// Does not run in player builds.
/// </summary>
[InitializeOnLoad]
internal static class Level23AwardedStarsSourceUpgrader
{
    private const string ManagerPath =
        "Assets/ArcheryTrickShot/Scripts/Core/LevelManager.cs";
    private const string UiPath =
        "Assets/ArcheryTrickShot/Scripts/Presentation/GameUIController.cs";

    private const string ManagerOldTail =
        "            currentLevelNewMedallionIds.Count,\n" +
        "            isGameFinale,\n" +
        "            isMushroomLevel);";

    private const string ManagerNewTail =
        "            currentLevelNewMedallionIds.Count,\n" +
        "            isGameFinale,\n" +
        "            earnedStars,\n" +
        "            isMushroomLevel);";

    private const string UiOldSignature =
        "        bool isMushroomLevel = false)";

    private const string UiNewSignature =
        "        int awardedStars = 0,\n" +
        "        bool isMushroomLevel = false)";

    private const string UiOldCalculation =
        "        int stars = CalculateStars(shotsUsed, maxShots);";

    private const string UiNewCalculation =
        "        int stars = awardedStars > 0\n" +
        "            ? Mathf.Clamp(awardedStars, 1, 3)\n" +
        "            : CalculateStars(shotsUsed, maxShots);";

    static Level23AwardedStarsSourceUpgrader()
    {
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Install;
            return;
        }

        if (!File.Exists(ManagerPath) || !File.Exists(UiPath))
        {
            Debug.LogError("[Level 23 v3] Shared source not found. " +
                "Extract this patch into the Unity project root (Assets folder).");
            return;
        }

        string manager = File.ReadAllText(ManagerPath).Replace("\r\n", "\n");
        string ui = File.ReadAllText(UiPath).Replace("\r\n", "\n");

        bool managerDone = manager.Contains(ManagerNewTail);
        bool uiDone =
            ui.Contains(UiNewSignature) &&
            ui.Contains(UiNewCalculation);

        if (managerDone && uiDone)
            return;

        // Level 22 v3 must already be installed. This leaves mushroom
        // labels, explicit IsGameFinale and its authoring intact.
        if (Count(manager, ManagerOldTail) != (managerDone ? 0 : 1) ||
            Count(ui, UiOldSignature) != (uiDone ? 0 : 1) ||
            Count(ui, UiOldCalculation) != (uiDone ? 0 : 1) ||
            (managerDone && !manager.Contains(ManagerNewTail)) ||
            (uiDone && !ui.Contains(UiNewSignature)))
        {
            Debug.LogError(
                "[Level 23 v3] Source doesn't match the expected Level 22 v3 " +
                "baseline. No changes made. Ensure Level 22 v3 was installed.");
            return;
        }

        string modifiedManager = managerDone
            ? manager
            : manager.Replace(ManagerOldTail, ManagerNewTail);

        string modifiedUi = uiDone
            ? ui
            : ui.Replace(UiOldSignature, UiNewSignature)
                .Replace(UiOldCalculation, UiNewCalculation);

        try
        {
            string backupDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library", "Level23V3StarFixBackups");

            Directory.CreateDirectory(backupDir);

            if (!managerDone)
                BackupAndWrite(ManagerPath, modifiedManager,
                    Path.Combine(backupDir, "LevelManager.cs.bak"));

            if (!uiDone)
                BackupAndWrite(UiPath, modifiedUi,
                    Path.Combine(backupDir, "GameUIController.cs.bak"));

            Debug.Log("[Level 23 v3] Installed canonical earned-star " +
                "display fix. Saved original scripts in Library/Level23V3StarFixBackups.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
        catch (Exception e)
        {
            Debug.LogError("[Level 23 v3] Source upgrade failed: " + e);
        }
    }

    private static int Count(string source, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return 0;

        int count = 0, pos = 0;
        while ((pos = source.IndexOf(pattern, pos,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += pattern.Length;
        }
        return count;
    }

    private static void BackupAndWrite(
        string path, string content, string backupPath)
    {
        if (!File.Exists(backupPath))
            File.Copy(path, backupPath);

        bool windowsLineEndings =
            File.ReadAllText(path).Contains("\r\n");

        string text = windowsLineEndings
            ? content.Replace("\n", "\r\n")
            : content;

        string temp = path + ".level23v3.tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        try
        {
            File.Copy(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
#endif
