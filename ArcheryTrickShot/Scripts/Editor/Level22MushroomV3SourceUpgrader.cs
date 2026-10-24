#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time source upgrade for the Level 22 mushroom patch.
/// Unity automatically applies it on import; no scene or Inspector work.
/// Patches the shared UI with data-driven labels and explicit IsGameFinale.
/// Runs only when the expected original source matches; never guesses.
/// The upgrade is idempotent and creates a backup under Library.
/// </summary>
[InitializeOnLoad]
internal static class Level22MushroomV3SourceUpgrader
{
    private const string ManagerPath =
        "Assets/ArcheryTrickShot/Scripts/Core/LevelManager.cs";
    private const string UiPath =
        "Assets/ArcheryTrickShot/Scripts/Presentation/GameUIController.cs";

    static Level22MushroomV3SourceUpgrader()
    {
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        // Avoid writing scripts during compilation or an ongoing import.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Install;
            return;
        }

        string managerBefore;
        string uiBefore;
        if (!TryRead(ManagerPath, out managerBefore) ||
            !TryRead(UiPath, out uiBefore))
        {
            return;
        }

        bool managerInstalled =
            managerBefore.Contains("bool isGameFinale = currentLevel != null") &&
            managerBefore.Contains("isMushroomLevel);");
        bool uiInstalled =
            uiBefore.Contains("bool isMushroomLevel = false") &&
            uiBefore.Contains("MUSHROOM BOUNCE");

        // An already upgraded project is left completely untouched.
        if (managerInstalled && uiInstalled)
            return;

        string managerAfter = managerBefore;
        string uiAfter = uiBefore;

        // Preflight BOTH files before changing either. Fail safe when a local
        // edit has changed the expected code, rather than corrupting sources.
        if (!managerInstalled && !PatchManager(ref managerAfter))
            return;
        if (!uiInstalled && !PatchUi(ref uiAfter))
            return;

        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string backupRoot = Path.Combine(
                projectRoot, "Library", "Level22MushroomV3Backups");
            Directory.CreateDirectory(backupRoot);

            if (!managerInstalled)
            {
                Backup(ManagerPath, Path.Combine(backupRoot, "LevelManager.cs.bak"));
                Write(ManagerPath, managerAfter);
            }
            if (!uiInstalled)
            {
                Backup(UiPath, Path.Combine(backupRoot, "GameUIController.cs.bak"));
                Write(UiPath, uiAfter);
            }

            Debug.Log("[Level 22 v3] Installed campaign-finale and mushroom-result UI fixes. " +
                "Original sources backed up under Library/Level22MushroomV3Backups.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
        catch (Exception error)
        {
            Debug.LogError("[Level 22 v3] Source upgrade failed: " + error);
        }
    }

    private static bool TryRead(string path, out string contents)
    {
        contents = null;
        if (!File.Exists(path))
        {
            Debug.LogError("[Level 22 v3] Required source missing: " + path);
            return false;
        }
        // Git may check out C# files with CRLF on Windows. Match snippets
        // against normalized LF, then restore the original style when writing.
        contents = File.ReadAllText(path).Replace("\r\n", "\n");
        return true;
    }

    private static void Backup(string source, string destination)
    {
        if (!File.Exists(destination))
            File.Copy(source, destination);
    }

    private static void Write(string path, string contents)
    {
        string temporaryPath = path + ".level22v3.tmp";
        bool useCrLf = File.ReadAllText(path).Contains("\r\n");
        string platformContents = useCrLf
            ? contents.Replace("\n", "\r\n")
            : contents;
        File.WriteAllText(temporaryPath, platformContents, new UTF8Encoding(false));
        try
        {
            File.Copy(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool ReplaceOnce(
        ref string contents, string from, string to, string description)
    {
        int index = contents.IndexOf(from, StringComparison.Ordinal);
        if (index < 0 || contents.IndexOf(from, index + from.Length,
                StringComparison.Ordinal) >= 0)
        {
            Debug.LogError("[Level 22 v3] " + description +
                " does not match the expected GitHub baseline exactly. " +
                "No source file was changed. Review your local modifications.");
            return false;
        }
        contents = contents.Substring(0, index) + to +
            contents.Substring(index + from.Length);
        return true;
    }

    private static bool PatchManager(ref string contents)
    {
        if (!ReplaceOnce(ref contents,
            "        bool isLastLevel = currentLevelIndex >= levels.Length - 1;",
            @"        bool isLastLevel = currentLevelIndex >= levels.Length - 1;
        // The last installed level is not necessarily the campaign finale.
        bool isGameFinale = currentLevel != null && currentLevel.IsGameFinale;
        // Identify reusable mushroom surfaces from level data, never level ID.
        bool isMushroomLevel = currentLevel != null &&
            currentLevel.PresentationColliders != null &&
            currentLevel.PresentationColliders.Any(item =>
                item != null && !string.IsNullOrEmpty(item.Name) &&
                item.Name.StartsWith(""MushroomBounce_"",
                    System.StringComparison.Ordinal));",
            "LevelManager completion flags"))
            return false;

        if (!ReplaceOnce(ref contents,
            "            currentLevelNewMedallionIds.Count);",
            @"            currentLevelNewMedallionIds.Count,
            isGameFinale,
            isMushroomLevel);",
            "LevelManager completion arguments"))
            return false;
        return true;
    }

    private static bool PatchUi(ref string contents)
    {
        if (!ReplaceOnce(ref contents,
            "        int newlyCollectedMedallionCount = 0)",
            @"        int newlyCollectedMedallionCount = 0,
        bool isGameFinale = false,
        bool isMushroomLevel = false)",
            "GameUIController completion signature"))
            return false;

        if (!ReplaceOnce(ref contents,
            @"        view.ResultTitle.text =
            GetCelebrationTitle(
                shotsUsed,
                isBullseye,
                ricochetCount,
                hitLabel,
                isLastLevel);",
            @"        view.ResultTitle.text =
            isMushroomLevel && !isGameFinale && ricochetCount >= 2
                ? (isBullseye
                    ? ""MUSHROOM BULLSEYE!""
                    : ""DOUBLE MUSHROOM BOUNCE!"")
                : GetCelebrationTitle(
                    shotsUsed,
                    isBullseye,
                    ricochetCount,
                    hitLabel,
                    isGameFinale);",
            "GameUIController completion title"))
            return false;

        if (!ReplaceOnce(ref contents,
            "        string ricochetLabel =\n" +
            "            ricochetCount > 0\n" +
            "                ? $\"{ricochetCount} RICOCHET{(ricochetCount == 1 ? string.Empty : \"S\")}\"\n" +
            "                : string.Empty;",
            "        string ricochetLabel =\n" +
            "            ricochetCount > 0\n" +
            "                ? isMushroomLevel\n" +
            "                    ? $\"{ricochetCount} MUSHROOM BOUNCE{(ricochetCount == 1 ? string.Empty : \"S\")}\"\n" +
            "                    : $\"{ricochetCount} RICOCHET{(ricochetCount == 1 ? string.Empty : \"S\")}\"\n" +
            "                : string.Empty;",
            "GameUIController bounce label"))
            return false;

        if (!ReplaceOnce(ref contents,
            "        string mirrorLabel =\n" +
            "            uniqueMirrorCount > 0",
            "        string mirrorLabel =\n" +
            "            !isMushroomLevel && uniqueMirrorCount > 0",
            "GameUIController mirror label"))
            return false;

        if (!ReplaceOnce(ref contents,
            "        view.ResultPrimaryButtonText.text = isLastLevel ? \"PLAY AGAIN  >>\" : \"NEXT LEVEL  >>\";\n" +
            "        view.ResultReplayButton.gameObject.SetActive(!isLastLevel);",
            "        view.ResultPrimaryButtonText.text = isLastLevel\n" +
            "            ? (isGameFinale ? \"PLAY AGAIN  >>\" : \"LEVEL SELECT  >>\")\n" +
            "            : \"NEXT LEVEL  >>\";\n" +
            "        view.ResultReplayButton.gameObject.SetActive(!isGameFinale);",
            "GameUIController result actions"))
            return false;
        return true;
    }
}
#endif
