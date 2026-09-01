#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click onboarding for the project's normal future character source:
/// Hyper/Mixamo-style Humanoid models/prefabs.
///
/// Select the imported Humanoid GameObject asset, then run:
/// Tools > Archery Trick Shot > Characters > Create Hyper-Mixamo Archer From Selected
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
        GameObject selected = Selection.activeObject as GameObject;

        if (selected == null)
        {
            EditorUtility.DisplayDialog(
                "Create Archer",
                "Select an imported Humanoid model or prefab in the Project window first.",
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

        string characterId = ToStableId(selected.name);
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
        profile.DisplayName = selected.name;
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

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);

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
            "• Nearby Hyper3D PBR textures become safe runtime material overrides\n\n" +
            "No scene, level, projectile, mirror, scoring, or BowController changes were made.",
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
