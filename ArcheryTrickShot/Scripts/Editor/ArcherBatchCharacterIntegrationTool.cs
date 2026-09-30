#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;


/// <summary>
/// One-click batch onboarding for production Hyper3D + Mixamo character assets.
///
/// This intentionally uses the same profile/material contract as
/// ArcherHumanoidAutoProfileTool:
/// - common AnimatorController and bow
/// - Humanoid auto finger sockets
/// - camera-facing 2D bow binding
/// - automatic character height scaling
/// - non-destructive PBR material override
///
/// Existing production characters are never overwritten and the saved active
/// character is never changed.
/// </summary>
public static class ArcherBatchCharacterIntegrationTool
{
    private const string CharacterAssetsRoot =
        "Assets/ArcheryTrickShot/Characters";


    private const string RuntimeRoot =
        "Assets/ArcheryTrickShot/Resources/Archer3D";


    private const string CharacterProfilesFolder =
        RuntimeRoot + "/Characters";


    private const string DefaultProfilePath =
        RuntimeRoot + "/DefaultArcher3D.asset";


    private const string RosterPath =
        RuntimeRoot + "/ArcherCharacterRoster.asset";


    private static readonly string[] ProtectedProductionCharacterIds =
    {
        "khaem",
        "nerissa",
        "ember"
    };


    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Batch Integrate Missing Character Assets")]
    private static void BatchIntegrateMissingCharacters()
    {
        AssetDatabase.Refresh();


        Archer3DRuntimeProfile template =
            AssetDatabase.LoadAssetAtPath<Archer3DRuntimeProfile>(
                DefaultProfilePath);


        ArcherCharacterRoster roster =
            AssetDatabase.LoadAssetAtPath<ArcherCharacterRoster>(
                RosterPath);


        if (template == null || roster == null)
        {
            EditorUtility.DisplayDialog(
                "Batch Character Integration",
                "The scalable archer template or roster is missing.\n\n" +
                "Expected:\n" +
                DefaultProfilePath + "\n" +
                RosterPath,
                "OK");
            return;
        }


        if (!AssetDatabase.IsValidFolder(CharacterAssetsRoot))
        {
            EditorUtility.DisplayDialog(
                "Batch Character Integration",
                "Character folder was not found:\n" +
                CharacterAssetsRoot,
                "OK");
            return;
        }


        EnsureFolder(CharacterProfilesFolder);


        string[] characterFolders =
            AssetDatabase.GetSubFolders(CharacterAssetsRoot);


        int createdCount = 0;
        int skippedCount = 0;
        int failedCount = 0;


        List<string> created = new List<string>();
        List<string> skipped = new List<string>();
        List<string> failed = new List<string>();


        for (int i = 0; i < characterFolders.Length; i++)
        {
            string folder = characterFolders[i];
            string folderName = GetLastPathSegment(folder);
            string displayName = HumanizeName(folderName);
            string characterId = ToStableId(displayName);


            if (string.IsNullOrWhiteSpace(characterId))
            {
                failedCount++;
                failed.Add(folderName + " - could not derive a character ID.");
                continue;
            }


            if (IsProtectedProductionCharacter(characterId))
            {
                skippedCount++;
                skipped.Add(
                    displayName +
                    " - preserved existing production integration.");
                continue;
            }


            Archer3DRuntimeProfile registered =
                FindRegisteredProfile(roster, characterId);


            if (registered != null)
            {
                skippedCount++;
                skipped.Add(
                    displayName +
                    " - already registered as '" +
                    registered.CharacterId +
                    "'.");
                continue;
            }


            string modelPath =
                FindBestRiggedModelPath(folder);


            if (string.IsNullOrWhiteSpace(modelPath))
            {
                failedCount++;
                failed.Add(
                    displayName +
                    " - no FBX model found under " +
                    folder + ".");
                continue;
            }


            string rigError;
            if (!EnsureHumanoidRig(modelPath, out rigError))
            {
                failedCount++;
                failed.Add(
                    displayName +
                    " - Humanoid import failed: " +
                    rigError);
                continue;
            }


            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    modelPath);


            if (model == null)
            {
                failedCount++;
                failed.Add(
                    displayName +
                    " - Unity could not load " +
                    modelPath + ".");
                continue;
            }


            Archer3DRuntimeProfile profile;
            string integrationError;


            if (!TryCreateOrUpdateProfile(
                    model,
                    displayName,
                    template,
                    roster,
                    out profile,
                    out integrationError))
            {
                failedCount++;
                failed.Add(
                    displayName +
                    " - " +
                    integrationError);
                continue;
            }


            createdCount++;
            created.Add(
                profile.DisplayName +
                " (" + profile.CharacterId + ") -> " +
                modelPath);
        }


        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();


        ArcherCharacterRoster.InvalidateRuntimeSelectionCache();


        StringBuilder log = new StringBuilder();
        log.AppendLine("[Archer Batch Integration] Complete");
        log.AppendLine("Created: " + createdCount);
        log.AppendLine("Skipped: " + skippedCount);
        log.AppendLine("Failed: " + failedCount);


        AppendSection(log, "Created", created);
        AppendSection(log, "Skipped", skipped);
        AppendSection(log, "Failed", failed);


        log.AppendLine();
        log.AppendLine(
            "Saved active character and roster default were not changed.");


        Debug.Log(log.ToString());


        string dialog =
            "Batch integration finished.\n\n" +
            "Created: " + createdCount + "\n" +
            "Skipped: " + skippedCount + "\n" +
            "Failed: " + failedCount + "\n\n" +
            "Khaem, Nerissa and Ember are protected and were not modified.\n" +
            "The saved active character was not changed.\n\n" +
            "See the Console for the per-character report.";


        EditorUtility.DisplayDialog(
            failedCount == 0
                ? "Batch Character Integration Complete"
                : "Batch Character Integration Completed With Issues",
            dialog,
            "OK");
    }


    private static bool TryCreateOrUpdateProfile(
        GameObject selected,
        string displayName,
        Archer3DRuntimeProfile template,
        ArcherCharacterRoster roster,
        out Archer3DRuntimeProfile profile,
        out string error)
    {
        profile = null;
        error = string.Empty;


        if (selected == null)
        {
            error = "Selected character model is null.";
            return false;
        }


        Animator sourceAnimator =
            selected.GetComponentInChildren<Animator>(true);


        if (sourceAnimator == null)
        {
            error =
                "The imported model has no Animator after Humanoid import.";
            return false;
        }


        if (sourceAnimator.avatar == null ||
            !sourceAnimator.avatar.isValid ||
            !sourceAnimator.avatar.isHuman)
        {
            error =
                "The imported model does not have a valid Humanoid Avatar.";
            return false;
        }


        string characterId =
            ToStableId(displayName);


        string profilePath =
            CharacterProfilesFolder + "/" +
            characterId + "Archer3D.asset";


        profile =
            AssetDatabase.LoadAssetAtPath<Archer3DRuntimeProfile>(
                profilePath);


        bool created = profile == null;


        if (created)
        {
            profile =
                ScriptableObject.CreateInstance<Archer3DRuntimeProfile>();


            EditorUtility.CopySerialized(
                template,
                profile);


            AssetDatabase.CreateAsset(
                profile,
                profilePath);
        }


        profile.CharacterId = characterId;
        profile.DisplayName = displayName;
        profile.PlayerSelectable = true;
        profile.ArcherPrefab = selected;


        // Same common animation/bow contract used by the existing production
        // Humanoid onboarding tool.
        profile.AnimatorController =
            template.AnimatorController;


        profile.BowPrefab =
            template.BowPrefab;


        profile.AutoScaleToDesiredHeight = true;
        profile.LocalScale = Vector3.one;
        profile.LocalOffset = Vector3.zero;


        profile.LocalEulerAngles =
            HasApproximatelySideFacingAnimatorChild(selected)
                ? Vector3.zero
                : new Vector3(0f, 90f, 0f);


        profile.BowHandBone =
            HumanBodyBones.LeftHand;


        profile.DrawHandBone =
            HumanBodyBones.RightHand;


        profile.SocketBindingMode =
            ArcherSocketBindingMode.HumanoidAutoFingerSockets;


        profile.AutoBowGripPalmReach = 0f;
        profile.AutoDrawNockFingerAdvance = 0.68f;
        profile.BowGripSocketLocalCorrection = Vector3.zero;
        profile.DrawNockSocketLocalCorrection = Vector3.zero;
        profile.NockOffsetInDrawHandLocal = Vector3.zero;


        profile.BowBindingMode =
            ArcherBowBindingMode.CameraFacing2D;


        profile.BowVisualRelativePath =
            string.Empty;


        profile.SyncExternalBowToBowHand = true;


        profile.BowLocalPosition =
            template.BowLocalPosition;


        profile.BowLocalEulerAngles =
            template.BowLocalEulerAngles;


        profile.BowLocalScale =
            template.BowLocalScale;


        profile.BowScreenPlaneOffset =
            template.BowScreenPlaneOffset;


        profile.BowCameraDepthOffset =
            template.BowCameraDepthOffset;


        profile.BowCameraFacingEulerAngles =
            template.BowCameraFacingEulerAngles;


        profile.BowAimAngleMultiplier =
            template.BowAimAngleMultiplier;


        profile.HeldArrowRelativePath =
            string.Empty;


        profile.PreferAssetHeldArrow = false;


        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();


        // Reuse the existing non-destructive production material builder.
        if (!ArcherCharacterEditorTools.TryCreateOrUpdateMaterial(
                profile,
                false))
        {
            if (created)
            {
                AssetDatabase.DeleteAsset(profilePath);
                profile = null;
            }


            error =
                "PBR material setup failed. Verify that " +
                "texture_diffuse.png is beside the rigged FBX.";
            return false;
        }


        if (!roster.Profiles.Contains(profile))
        {
            roster.Profiles.Add(profile);
        }


        EditorUtility.SetDirty(profile);
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();


        return true;
    }


    private static bool EnsureHumanoidRig(
        string modelPath,
        out string error)
    {
        error = string.Empty;


        ModelImporter importer =
            AssetImporter.GetAtPath(modelPath)
                as ModelImporter;


        if (importer == null)
        {
            error =
                "No ModelImporter exists for " +
                modelPath + ".";
            return false;
        }


        bool changed = false;


        if (importer.animationType !=
            ModelImporterAnimationType.Human)
        {
            importer.animationType =
                ModelImporterAnimationType.Human;


            changed = true;
        }


        if (importer.avatarSetup !=
            ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup =
                ModelImporterAvatarSetup.CreateFromThisModel;


            changed = true;
        }


        // The runtime derives Humanoid hand/finger sockets. Do not hide the
        // hierarchy through model optimization during onboarding.
        if (importer.optimizeGameObjects)
        {
            importer.optimizeGameObjects = false;
            changed = true;
        }


        if (changed)
        {
            importer.SaveAndReimport();
        }


        GameObject imported =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                modelPath);


        Animator animator =
            imported != null
                ? imported.GetComponentInChildren<Animator>(true)
                : null;


        if (animator == null)
        {
            error =
                "Unity imported the FBX without an Animator.";
            return false;
        }


        if (animator.avatar == null ||
            !animator.avatar.isValid ||
            !animator.avatar.isHuman)
        {
            error =
                "Unity could not build a valid Humanoid Avatar " +
                "from the Mixamo FBX.";
            return false;
        }


        return true;
    }


    private static string FindBestRiggedModelPath(
        string characterFolder)
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Model",
                new[] { characterFolder });


        string bestPath = null;
        int bestScore = int.MinValue;


        for (int i = 0; i < guids.Length; i++)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guids[i]);


            if (!path.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }


            string fileName =
                Path.GetFileNameWithoutExtension(path)
                    .ToLowerInvariant();


            int score = 0;


            if (fileName.Contains("_rigged") ||
                fileName.Contains("-rigged") ||
                fileName.EndsWith("rigged"))
            {
                score += 1000;
            }


            if (fileName.Contains("t-pose") ||
                fileName.Contains("t_pose"))
            {
                score += 800;
            }
            else if (fileName.Contains("tpose"))
            {
                score += 750;
            }


            // Hyper3D source meshes are useful references but the production
            // ArcherPrefab must be the Mixamo-rigged FBX.
            if (fileName.StartsWith("base_basic"))
                score -= 500;


            if (fileName == "base")
                score -= 400;


            if (score > bestScore)
            {
                bestScore = score;
                bestPath = path;
            }
        }


        return bestPath;
    }


    private static Archer3DRuntimeProfile FindRegisteredProfile(
        ArcherCharacterRoster roster,
        string characterId)
    {
        if (roster == null ||
            roster.Profiles == null)
        {
            return null;
        }


        for (int i = 0; i < roster.Profiles.Count; i++)
        {
            Archer3DRuntimeProfile profile =
                roster.Profiles[i];


            if (profile == null)
                continue;


            if (string.Equals(
                    profile.CharacterId,
                    characterId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }


        return null;
    }


    private static bool IsProtectedProductionCharacter(
        string characterId)
    {
        for (int i = 0;
             i < ProtectedProductionCharacterIds.Length;
             i++)
        {
            if (string.Equals(
                    ProtectedProductionCharacterIds[i],
                    characterId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }


        return false;
    }


    private static bool HasApproximatelySideFacingAnimatorChild(
        GameObject asset)
    {
        Animator animator =
            asset.GetComponentInChildren<Animator>(true);


        if (animator == null ||
            animator.transform == asset.transform)
        {
            return false;
        }


        float y =
            animator.transform.localEulerAngles.y;


        float deltaToPositive90 =
            Mathf.Abs(
                Mathf.DeltaAngle(y, 90f));


        float deltaToNegative90 =
            Mathf.Abs(
                Mathf.DeltaAngle(y, -90f));


        return Mathf.Min(
                   deltaToPositive90,
                   deltaToNegative90) <= 20f;
    }


    private static string HumanizeName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Archer";


        string input =
            value.Trim()
                .Replace('_', ' ')
                .Replace('-', ' ');


        StringBuilder result =
            new StringBuilder();


        for (int i = 0; i < input.Length; i++)
        {
            char current = input[i];


            if (i > 0 &&
                char.IsUpper(current) &&
                char.IsLetterOrDigit(input[i - 1]) &&
                !char.IsUpper(input[i - 1]) &&
                result.Length > 0 &&
                result[result.Length - 1] != ' ')
            {
                result.Append(' ');
            }


            result.Append(current);
        }


        string[] words =
            result.ToString()
                .Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries);


        return words.Length == 0
            ? "Archer"
            : string.Join(" ", words);
    }


    private static string ToStableId(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "archer";


        StringBuilder result =
            new StringBuilder();


        bool lastWasDash = false;


        foreach (char c in
                 value.Trim()
                     .ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash &&
                     result.Length > 0)
            {
                result.Append('-');
                lastWasDash = true;
            }
        }


        return result.ToString()
            .Trim('-');
    }


    private static string GetLastPathSegment(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;


        string normalized =
            path.Replace('\\', '/')
                .TrimEnd('/');


        int slash =
            normalized.LastIndexOf('/');


        return slash >= 0
            ? normalized.Substring(slash + 1)
            : normalized;
    }


    private static void AppendSection(
        StringBuilder builder,
        string heading,
        List<string> items)
    {
        if (items == null ||
            items.Count == 0)
        {
            return;
        }


        builder.AppendLine();
        builder.AppendLine(heading + ":");


        for (int i = 0;
             i < items.Count;
             i++)
        {
            builder.AppendLine(
                " - " + items[i]);
        }
    }


    private static void EnsureFolder(
        string fullPath)
    {
        string normalized =
            fullPath.Replace('\\', '/');


        string[] parts =
            normalized.Split('/');


        string current =
            parts[0];


        for (int i = 1;
             i < parts.Length;
             i++)
        {
            string next =
                current + "/" + parts[i];


            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(
                    current,
                    parts[i]);
            }


            current = next;
        }
    }
}
#endif