#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click onboarding for the project's normal future character source:
/// Hyper/Mixamo-style Humanoid models/prefabs.
///
/// Select an imported Humanoid GameObject asset, then run either:
/// Tools > Archery Trick Shot > Characters > Create Hyper-Mixamo Archer From Selected
/// or the convenience command:
/// Tools > Archery Trick Shot > Characters > Create + Select Hyper-Mixamo Archer From Selected
///
/// The created profile reuses the common archery Animator + bow and enables the
/// runtime automatic finger sockets. No scene/level/BowController edits are made.
/// </summary>
public static class ArcherHumanoidAutoProfileTool
{
    private const string RuntimeRoot =
        "Assets/ArcheryTrickShot/Resources/Archer3D";

    private const string CharacterProfilesFolder =
        RuntimeRoot + "/Characters";

    private const string DefaultProfilePath =
        RuntimeRoot + "/DefaultArcher3D.asset";

    private const string RosterPath =
        RuntimeRoot + "/ArcherCharacterRoster.asset";

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create Hyper-Mixamo Archer From Selected",
        true)]
    private static bool ValidateCreateFromSelected()
    {
        return Selection.activeObject is GameObject;
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create Hyper-Mixamo Archer From Selected")]
    private static void CreateFromSelected()
    {
        CreateFromSelectedInternal(false);
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create + Select Hyper-Mixamo Archer From Selected",
        true)]
    private static bool ValidateCreateAndSelectFromSelected()
    {
        return Selection.activeObject is GameObject;
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create + Select Hyper-Mixamo Archer From Selected")]
    private static void CreateAndSelectFromSelected()
    {
        CreateFromSelectedInternal(true);
    }

    private static void CreateFromSelectedInternal(
        bool selectForGameplay)
    {
        GameObject selected = Selection.activeObject as GameObject;

        if (selected == null)
        {
            EditorUtility.DisplayDialog(
                "Create Archer",
                "Select an imported Humanoid model or prefab in the Project window first.",
                "OK");
            return;
        }

        Animator sourceAnimator =
            selected.GetComponentInChildren<Animator>(true);

        if (sourceAnimator == null)
        {
            EditorUtility.DisplayDialog(
                "Create Archer",
                "The selected asset has no Animator. Import/rig it as a Humanoid first.",
                "OK");
            return;
        }

        if (sourceAnimator.avatar == null ||
            !sourceAnimator.avatar.isValid ||
            !sourceAnimator.avatar.isHuman)
        {
            EditorUtility.DisplayDialog(
                "Create Archer",
                "The selected asset does not have a valid Humanoid Avatar.\n\n" +
                "Select the FBX, set Rig > Animation Type = Humanoid, Apply, then run this command again.",
                "OK");
            return;
        }

        Archer3DRuntimeProfile template =
            AssetDatabase.LoadAssetAtPath<Archer3DRuntimeProfile>(DefaultProfilePath);

        ArcherCharacterRoster roster =
            AssetDatabase.LoadAssetAtPath<ArcherCharacterRoster>(RosterPath);

        if (template == null || roster == null)
        {
            EditorUtility.DisplayDialog(
                "Create Archer",
                "Default scalable archer assets are missing. Restore Assets/ArcheryTrickShot/Resources/Archer3D first.",
                "OK");
            return;
        }

        EnsureFolder(CharacterProfilesFolder);

        string displayName =
            GetCleanDisplayName(selected.name);

        string characterId =
            ToStableId(displayName);

        string profilePath =
            CharacterProfilesFolder + "/" + characterId + "Archer3D.asset";

        Archer3DRuntimeProfile profile =
            AssetDatabase.LoadAssetAtPath<Archer3DRuntimeProfile>(profilePath);

        bool created = profile == null;

        if (created)
        {
            profile = ScriptableObject.CreateInstance<Archer3DRuntimeProfile>();
            EditorUtility.CopySerialized(template, profile);
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        profile.CharacterId = characterId;
        profile.DisplayName = displayName;
        profile.PlayerSelectable = true;
        profile.ArcherPrefab = selected;

        // Reuse the common project-owned animation/bow contract.
        profile.AnimatorController = template.AnimatorController;
        profile.BowPrefab = template.BowPrefab;

        profile.AutoScaleToDesiredHeight = true;
        profile.LocalScale = Vector3.one;
        profile.LocalOffset = Vector3.zero;

        // Raw Mixamo/Hyper models normally face +Z and need a 90-degree turn for
        // this side-view game. Wrapper prefabs such as KhaemCharacter already
        // contain that turn on their Animator child, so avoid double rotation.
        profile.LocalEulerAngles =
            HasApproximatelySideFacingAnimatorChild(selected)
                ? Vector3.zero
                : new Vector3(0f, 90f, 0f);

        profile.BowHandBone = HumanBodyBones.LeftHand;
        profile.DrawHandBone = HumanBodyBones.RightHand;

        profile.SocketBindingMode =
            ArcherSocketBindingMode.HumanoidAutoFingerSockets;

        profile.AutoBowGripPalmReach = 0f;
        profile.AutoDrawNockFingerAdvance = 0.68f;
        profile.BowGripSocketLocalCorrection = Vector3.zero;
        profile.DrawNockSocketLocalCorrection = Vector3.zero;
        profile.NockOffsetInDrawHandLocal = Vector3.zero;

        profile.BowBindingMode = ArcherBowBindingMode.CameraFacing2D;
        profile.BowVisualRelativePath = string.Empty;
        profile.SyncExternalBowToBowHand = true;

        // These are common-bow calibration values, not character calibration.
        profile.BowLocalPosition = template.BowLocalPosition;
        profile.BowLocalEulerAngles = template.BowLocalEulerAngles;
        profile.BowLocalScale = template.BowLocalScale;
        profile.BowScreenPlaneOffset = template.BowScreenPlaneOffset;
        profile.BowCameraDepthOffset = template.BowCameraDepthOffset;
        profile.BowCameraFacingEulerAngles = template.BowCameraFacingEulerAngles;
        profile.BowAimAngleMultiplier = template.BowAimAngleMultiplier;

        profile.HeldArrowRelativePath = string.Empty;
        profile.PreferAssetHeldArrow = false;

        if (!roster.Profiles.Contains(profile))
        {
            roster.Profiles.Add(profile);
        }

        EditorUtility.SetDirty(profile);
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // If Hyper3D PBR textures were imported beside the selected model,
        // create the material and store it as a runtime profile override. The
        // original rigged FBX/prefab remains profile.ArcherPrefab.
        ArcherCharacterEditorTools.TryCreateOrUpdateMaterial(
            profile,
            false);

        if (selectForGameplay)
        {
            if (!roster.SelectCharacter(profile.CharacterId))
            {
                EditorUtility.DisplayDialog(
                    "Create Archer",
                    "The profile was created, but it could not be selected from ArcherCharacterRoster.",
                    "OK");
                return;
            }

            ArcherCharacterRoster.InvalidateRuntimeSelectionCache();
        }

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);

        string selectionMessage =
            selectForGameplay
                ? "\n\nThe character is also selected for the next Play Mode."
                : "\n\nUse 'Use Selected Archer Profile' when you want to test this character.";

        EditorUtility.DisplayDialog(
            "Scalable Humanoid Archer Ready",
            (created ? "Created" : "Updated") +
            " profile:\n" + profilePath +
            "\n\nAutomatic setup enabled:\n" +
            "• Humanoid left/right hand mapping\n" +
            "• Finger-derived bow grip socket\n" +
            "• Finger-derived string/arrow nock socket\n" +
            "• Stable camera-facing bow plane\n" +
            "• Existing common archery animations and bow\n" +
            "• Nearby Hyper3D PBR textures become safe runtime material overrides\n" +
            "• Rig/Mixamo suffixes are removed from the player-facing name/ID\n\n" +
            "No scene, level, projectile, mirror, scoring, or BowController changes were made." +
            selectionMessage,
            "OK");
    }

    private static bool HasApproximatelySideFacingAnimatorChild(GameObject asset)
    {
        Animator animator = asset.GetComponentInChildren<Animator>(true);

        if (animator == null || animator.transform == asset.transform)
            return false;

        float y = animator.transform.localEulerAngles.y;
        float deltaToPositive90 = Mathf.Abs(Mathf.DeltaAngle(y, 90f));
        float deltaToNegative90 = Mathf.Abs(Mathf.DeltaAngle(y, -90f));

        return Mathf.Min(deltaToPositive90, deltaToNegative90) <= 20f;
    }

    private static string GetCleanDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Archer";

        string result = value.Trim();

        string[] suffixes =
        {
            "_Rigged",
            "-Rigged",
            " Rigged",
            "_Mixamo",
            "-Mixamo",
            " Mixamo",
            "_Humanoid",
            "-Humanoid",
            " Humanoid"
        };

        bool removed;
        do
        {
            removed = false;

            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];

                if (!result.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result = result
                    .Substring(0, result.Length - suffix.Length)
                    .Trim(' ', '_', '-');

                removed = true;
                break;
            }
        }
        while (removed && result.Length > 0);

        return string.IsNullOrWhiteSpace(result)
            ? "Archer"
            : result;
    }

    private static string ToStableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "archer";

        System.Text.StringBuilder result = new System.Text.StringBuilder();
        bool lastWasDash = false;

        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && result.Length > 0)
            {
                result.Append('-');
                lastWasDash = true;
            }
        }

        return result.ToString().Trim('-');
    }

    private static void EnsureFolder(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        string[] parts = normalized.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
#endif
