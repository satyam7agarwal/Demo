#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Full-body smooth archer setup.
/// Reuses the existing HumanF capture profile.
/// </summary>
public sealed class Archer3DRuntimeSetupWindow : EditorWindow
{
    private const string RuntimeRootFolder =
        "Assets/ArcheryTrickShot/Resources/Archer3D";

    private const string RuntimeProfileFolder =
        RuntimeRootFolder +
        "/Characters";

    private const string RosterPath =
        RuntimeRootFolder +
        "/ArcherCharacterRoster.asset";

    private const string AnimationFolder =
        "Assets/ArcheryTrickShot/Animations/Archer3D";

    private const string ControllerPath =
        AnimationFolder +
        "/Archer3D.controller";

    private ArcherCaptureProfile sourceProfile;
    private Archer3DRuntimeProfile editingProfile;
    private GameObject runtimeCharacterPrefab;
    private GameObject externalBowPrefab;

    [SerializeField]
    private string characterId = "new-archer";

    [SerializeField]
    private string displayName = "New Archer";

    [SerializeField]
    private bool setAsDefaultCharacter = true;

    [SerializeField]
    private Vector3 characterLocalEulerAngles =
        Vector3.zero;

    [SerializeField]
    private Vector3 characterLocalScale =
        Vector3.one;

    [SerializeField]
    private ArcherBowBindingMode bowBindingMode =
        ArcherBowBindingMode.FollowAnimatedHand;

    [SerializeField]
    private Vector3 bowLocalPosition =
        Vector3.zero;

    [SerializeField]
    private Vector3 bowLocalEulerAngles =
        Vector3.zero;

    [SerializeField]
    private Vector3 bowLocalScale =
        Vector3.one;

    [SerializeField]
    private Vector2 bowScreenPlaneOffset =
        Vector2.zero;

    [SerializeField]
    private float bowCameraDepthOffset =
        0.05f;

    [SerializeField]
    private bool preferAssetHeldArrow;

    [SerializeField]
    private float desiredWorldHeight =
        3.6f;

    [SerializeField]
    private float releaseFireNormalizedTime =
        0.24f;

    private Vector2 scroll;

    [MenuItem(
        "Tools/Archery Trick Shot/Setup Smooth 3D Archer")]
    private static void Open()
    {
        GetWindow<Archer3DRuntimeSetupWindow>(
            "Smooth 3D Archer");
    }

    private void OnEnable()
    {
        AutoResolveSourceProfile();
        LoadCurrentRosterProfile();
    }

    private void OnGUI()
    {
        scroll =
            EditorGUILayout.BeginScrollView(
                scroll);

        EditorGUILayout.LabelField(
            "Final Smooth Full-Body Archer",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField(
            "Character Profile",
            EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        Archer3DRuntimeProfile selectedProfile =
            (Archer3DRuntimeProfile)
            EditorGUILayout.ObjectField(
                "Edit Existing Profile",
                editingProfile,
                typeof(Archer3DRuntimeProfile),
                false);

        if (EditorGUI.EndChangeCheck())
        {
            editingProfile = selectedProfile;

            if (editingProfile != null)
            {
                LoadProfileIntoFields(
                    editingProfile);
            }
        }

        characterId =
            EditorGUILayout.TextField(
                "Stable Character ID",
                characterId);

        displayName =
            EditorGUILayout.TextField(
                "Display Name",
                displayName);

        setAsDefaultCharacter =
            EditorGUILayout.Toggle(
                "Set As Default",
                setAsDefaultCharacter);

        EditorGUILayout.Space(6f);

        sourceProfile =
            (ArcherCaptureProfile)
            EditorGUILayout.ObjectField(
                "Existing Capture Profile",
                sourceProfile,
                typeof(ArcherCaptureProfile),
                false);

        runtimeCharacterPrefab =
            (GameObject)
            EditorGUILayout.ObjectField(
                "Runtime Character Prefab",
                runtimeCharacterPrefab,
                typeof(GameObject),
                false);

        externalBowPrefab =
            (GameObject)
            EditorGUILayout.ObjectField(
                "Authored Bow Prefab",
                externalBowPrefab,
                typeof(GameObject),
                false);

        desiredWorldHeight =
            EditorGUILayout.FloatField(
                "Desired World Height",
                desiredWorldHeight);

        releaseFireNormalizedTime =
            EditorGUILayout.Slider(
                "Release Fire Normalized Time",
                releaseFireNormalizedTime,
                0.02f,
                0.95f);

        characterLocalEulerAngles =
            EditorGUILayout.Vector3Field(
                "Character Rotation",
                characterLocalEulerAngles);

        characterLocalScale =
            EditorGUILayout.Vector3Field(
                "Character Base Scale",
                characterLocalScale);

        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField(
            "One-Time Bow Calibration",
            EditorStyles.boldLabel);

        bowBindingMode =
            (ArcherBowBindingMode)
            EditorGUILayout.EnumPopup(
                "Binding Mode",
                bowBindingMode);

        bowLocalPosition =
            EditorGUILayout.Vector3Field(
                "Bow Local Position",
                bowLocalPosition);

        bowLocalEulerAngles =
            EditorGUILayout.Vector3Field(
                "Bow Local Rotation",
                bowLocalEulerAngles);

        bowLocalScale =
            EditorGUILayout.Vector3Field(
                "Bow Local Scale",
                bowLocalScale);

        bowScreenPlaneOffset =
            EditorGUILayout.Vector2Field(
                "Screen Plane Offset",
                bowScreenPlaneOffset);

        bowCameraDepthOffset =
            EditorGUILayout.FloatField(
                "Camera Depth Offset",
                bowCameraDepthOffset);

        preferAssetHeldArrow =
            EditorGUILayout.Toggle(
                "Use Asset Held Arrow",
                preferAssetHeldArrow);

        EditorGUILayout.Space(8f);

        EditorGUILayout.HelpBox(
            "Each character is stored as one reusable profile and registered " +
            "in the roster. Adding another Humanoid does not require scene, " +
            "level, BowController, projectile, reflection, or scoring edits. " +
            "Only character-specific visual calibration belongs here.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(
                   sourceProfile == null))
        {
            if (GUILayout.Button(
                    "Create / Update Smooth 3D Archer",
                    GUILayout.Height(38f)))
            {
                Build();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void AutoResolveSourceProfile()
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:ArcherCaptureProfile");

        foreach (string guid in guids)
        {
            ArcherCaptureProfile candidate =
                AssetDatabase
                    .LoadAssetAtPath<
                        ArcherCaptureProfile>(
                        AssetDatabase
                            .GUIDToAssetPath(
                                guid));

            if (candidate == null ||
                candidate.ArcherPrefab == null)
            {
                continue;
            }

            if (HasRequiredAnimationClips(
                    candidate))
            {
                sourceProfile = candidate;
                return;
            }
        }

        if (guids.Length > 0)
        {
            sourceProfile =
                AssetDatabase
                    .LoadAssetAtPath<
                        ArcherCaptureProfile>(
                        AssetDatabase
                            .GUIDToAssetPath(
                                guids[0]));
        }
    }

    private static bool HasRequiredAnimationClips(
        ArcherCaptureProfile candidate)
    {
        if (candidate == null ||
            candidate.Clips == null)
        {
            return false;
        }

        string[] required =
        {
            "Idle",
            "Load",
            "Hold",
            "Release"
        };

        return required.All(
            id => candidate.Clips.Any(
                definition =>
                    definition != null &&
                    definition.SourceClip != null &&
                    string.Equals(
                        definition.Id?.Trim(),
                        id,
                        StringComparison
                            .OrdinalIgnoreCase)));
    }

    private void LoadCurrentRosterProfile()
    {
        ArcherCharacterRoster roster =
            AssetDatabase.LoadAssetAtPath<
                ArcherCharacterRoster>(
                    RosterPath);

        Archer3DRuntimeProfile profile =
            roster != null
                ? roster.FindProfile(
                    roster.DefaultCharacterId)
                : null;

        if (profile == null)
            return;

        editingProfile = profile;
        LoadProfileIntoFields(profile);
    }

    private void LoadProfileIntoFields(
        Archer3DRuntimeProfile profile)
    {
        if (profile == null)
            return;

        characterId = profile.CharacterId;
        displayName = profile.DisplayName;
        runtimeCharacterPrefab = profile.ArcherPrefab;
        externalBowPrefab = profile.BowPrefab;
        desiredWorldHeight = profile.DesiredWorldHeight;
        releaseFireNormalizedTime =
            profile.ReleaseFireNormalizedTime;
        characterLocalEulerAngles =
            profile.LocalEulerAngles;
        characterLocalScale =
            profile.LocalScale;
        bowBindingMode =
            profile.BowBindingMode;
        bowLocalPosition =
            profile.BowLocalPosition;
        bowLocalEulerAngles =
            profile.BowLocalEulerAngles;
        bowLocalScale =
            profile.BowLocalScale;
        bowScreenPlaneOffset =
            profile.BowScreenPlaneOffset;
        bowCameraDepthOffset =
            profile.BowCameraDepthOffset;
        preferAssetHeldArrow =
            profile.PreferAssetHeldArrow;
    }

    private void Build()
    {
        if (!ValidateSource(
                out string error))
        {
            EditorUtility.DisplayDialog(
                "Smooth 3D Archer",
                error,
                "OK");
            return;
        }

        try
        {
            EnsureAssetFolder(
                RuntimeRootFolder);

            EnsureAssetFolder(
                RuntimeProfileFolder);

            EnsureAssetFolder(
                AnimationFolder);

            Dictionary<string, AnimationClip>
                sourceClips =
                    sourceProfile.Clips
                        .Where(
                            definition =>
                                definition != null &&
                                definition.SourceClip != null &&
                                !string
                                    .IsNullOrWhiteSpace(
                                        definition.Id))
                        .ToDictionary(
                            definition =>
                                definition.Id.Trim(),
                            definition =>
                                definition.SourceClip,
                            StringComparer
                                .OrdinalIgnoreCase);

            AnimationClip idleSource =
                RequireClip(
                    sourceClips,
                    "Idle");

            AnimationClip loadSource =
                RequireClip(
                    sourceClips,
                    "Load");

            AnimationClip holdSource =
                RequireClip(
                    sourceClips,
                    "Hold");

            AnimationClip releaseSource =
                RequireClip(
                    sourceClips,
                    "Release");

            AnimationClip idle =
                CreateOrUpdateRuntimeClip(
                    idleSource,
                    "Idle",
                    true,
                    null);

            AnimationClip load =
                CreateOrUpdateRuntimeClip(
                    loadSource,
                    "Load",
                    false,
                    null);

            AnimationClip hold =
                CreateOrUpdateRuntimeClip(
                    holdSource,
                    "Hold",
                    true,
                    null);

            AnimationEvent releaseEvent =
                new AnimationEvent
                {
                    functionName =
                        "OnReleaseFireEvent",
                    time =
                        releaseSource.length *
                        Mathf.Clamp01(
                            releaseFireNormalizedTime)
                };

            AnimationClip release =
                CreateOrUpdateRuntimeClip(
                    releaseSource,
                    "Release",
                    false,
                    new[]
                    {
                        releaseEvent
                    });

            AnimatorController controller =
                LoadOrCreateController();

            RebuildController(
                controller,
                idle,
                load,
                hold,
                release);

            Archer3DRuntimeProfile
                runtimeProfile =
                    LoadOrCreateRuntimeProfile(
                        GetRuntimeProfilePath());

            GameObject selectedRuntimePrefab =
                runtimeCharacterPrefab != null
                    ? runtimeCharacterPrefab
                    : sourceProfile.ArcherPrefab;

            runtimeProfile.CharacterId =
                NormalizeCharacterId(
                    characterId);

            runtimeProfile.DisplayName =
                string.IsNullOrWhiteSpace(
                    displayName)
                    ? runtimeProfile.CharacterId
                    : displayName.Trim();

            runtimeProfile.ArcherPrefab =
                selectedRuntimePrefab;

            runtimeProfile.AnimatorController =
                controller;

            runtimeProfile.BowPrefab =
                externalBowPrefab;

            runtimeProfile.DesiredWorldHeight =
                Mathf.Max(
                    0.1f,
                    desiredWorldHeight);

            runtimeProfile.LocalEulerAngles =
                characterLocalEulerAngles;

            runtimeProfile.ReleaseFireNormalizedTime =
                releaseFireNormalizedTime;

            runtimeProfile.LocalScale =
                ClampScale(
                    characterLocalScale);

            runtimeProfile.AutoScaleToDesiredHeight =
                true;

            runtimeProfile.HeldArrowRelativePath =
                DetectHeldArrowPath(
                    selectedRuntimePrefab);

            runtimeProfile.BowVisualRelativePath =
                DetectBowVisualPath(
                    selectedRuntimePrefab);

            // Recommended defaults from the proven working direct-bone version.
            runtimeProfile.AimAngleMultiplier = 1f;
            runtimeProfile.MaxVisualAimAngle = 60f;
            runtimeProfile.AimSmoothTime = 0.04f;
            runtimeProfile.MaxAimSpeed = 1080f;
            runtimeProfile.PoseBlendSmoothTime = 0.055f;

            // New natural posture defaults.
            runtimeProfile.BodyEngagementCurve =
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.30f, 0.03f),
                    new Keyframe(0.55f, 0.24f),
                    new Keyframe(0.80f, 0.67f),
                    new Keyframe(1f, 1f));

            runtimeProfile.UpBodyContribution = 0.24f;
            runtimeProfile.DownBodyContribution = 0.28f;

            runtimeProfile.SpineShare = 0.34f;
            runtimeProfile.ChestShare = 0.46f;
            runtimeProfile.UpperChestShare = 0.20f;

            runtimeProfile.HeadAimContribution = 0.08f;
            runtimeProfile.ArmAimWeight = 1f;
            runtimeProfile.PreferShoulderBones = true;
            runtimeProfile.SyncExternalBowToBowHand = true;

            runtimeProfile.BowBindingMode =
                bowBindingMode;

            // Camera-facing is the standard Hyper/Mixamo path: derive real
            // grip/nock sockets from Humanoid finger bones instead of wrist pivots.
            runtimeProfile.SocketBindingMode =
                bowBindingMode == ArcherBowBindingMode.CameraFacing2D
                    ? ArcherSocketBindingMode.HumanoidAutoFingerSockets
                    : ArcherSocketBindingMode.HandBonePivots;

            if (runtimeProfile.SocketBindingMode ==
                ArcherSocketBindingMode.HumanoidAutoFingerSockets)
            {
                runtimeProfile.AutoBowGripPalmReach = 0f;
                runtimeProfile.AutoDrawNockFingerAdvance = 0.68f;
                runtimeProfile.BowGripSocketLocalCorrection = Vector3.zero;
                runtimeProfile.DrawNockSocketLocalCorrection = Vector3.zero;
                runtimeProfile.NockOffsetInDrawHandLocal = Vector3.zero;
            }

            runtimeProfile.BowLocalPosition =
                bowLocalPosition;

            runtimeProfile.BowLocalEulerAngles =
                bowLocalEulerAngles;

            runtimeProfile.BowLocalScale =
                ClampScale(
                    bowLocalScale);

            runtimeProfile.BowScreenPlaneOffset =
                bowScreenPlaneOffset;

            runtimeProfile.BowCameraDepthOffset =
                Mathf.Max(
                    0f,
                    bowCameraDepthOffset);

            runtimeProfile.BowAimAngleMultiplier =
                1f;

            runtimeProfile.PreferAssetHeldArrow =
                preferAssetHeldArrow;

            ArcherCharacterRoster roster =
                LoadOrCreateRoster();

            RegisterProfile(
                roster,
                runtimeProfile,
                setAsDefaultCharacter);

            editingProfile = runtimeProfile;

            EditorUtility.SetDirty(
                runtimeProfile);

            EditorUtility.SetDirty(
                roster);

            EditorUtility.SetDirty(
                controller);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject =
                runtimeProfile;

            EditorGUIUtility.PingObject(
                runtimeProfile);

            EditorUtility.DisplayDialog(
                "Reusable Archer Profile Ready",
                "Updated profile:\n" +
                AssetDatabase.GetAssetPath(
                    runtimeProfile) +
                "\n\nRoster:\n" +
                RosterPath +
                "\n\n" +
                "Open Level01 and test horizontal -> high -> low aim. " +
                "Future Humanoid characters use this same one-profile workflow.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(
                exception);

            EditorUtility.DisplayDialog(
                "Smooth 3D Archer Setup Failed",
                exception.Message +
                "\n\nCheck Console for details.",
                "OK");
        }
    }

    private AnimationClip
        CreateOrUpdateRuntimeClip(
            AnimationClip source,
            string name,
            bool loop,
            AnimationEvent[] events)
    {
        string path =
            AnimationFolder +
            "/" +
            name +
            ".anim";

        AnimationClip workingCopy =
            Instantiate(source);

        workingCopy.name = name;

        AnimationClipSettings settings =
            AnimationUtility
                .GetAnimationClipSettings(
                    workingCopy);

        settings.loopTime = loop;
        settings.loopBlend = false;

        AnimationUtility
            .SetAnimationClipSettings(
                workingCopy,
                settings);

        AnimationUtility
            .SetAnimationEvents(
                workingCopy,
                events ??
                Array.Empty<AnimationEvent>());

        AnimationClip existing =
            AssetDatabase
                .LoadAssetAtPath<
                    AnimationClip>(
                    path);

        if (existing != null)
        {
            EditorUtility.CopySerialized(
                workingCopy,
                existing);

            DestroyImmediate(
                workingCopy);

            EditorUtility.SetDirty(
                existing);

            return existing;
        }

        AssetDatabase.CreateAsset(
            workingCopy,
            path);

        return workingCopy;
    }

    private AnimatorController
        LoadOrCreateController()
    {
        AnimatorController controller =
            AssetDatabase
                .LoadAssetAtPath<
                    AnimatorController>(
                    ControllerPath);

        if (controller != null)
            return controller;

        return AnimatorController
            .CreateAnimatorControllerAtPath(
                ControllerPath);
    }

    private static void RebuildController(
        AnimatorController controller,
        AnimationClip idleClip,
        AnimationClip loadClip,
        AnimationClip holdClip,
        AnimationClip releaseClip)
    {
        while (
            controller.parameters.Length > 0)
        {
            controller.RemoveParameter(0);
        }

        if (controller.layers.Length == 0)
        {
            controller.AddLayer(
                "Base Layer");
        }

        while (
            controller.layers.Length > 1)
        {
            controller.RemoveLayer(1);
        }

        AnimatorControllerLayer layer =
            controller.layers[0];

        // No Animator IK dependency.
        layer.iKPass = false;
        layer.defaultWeight = 1f;
        layer.name = "Base Layer";

        AnimatorStateMachine machine =
            layer.stateMachine;

        foreach (
            AnimatorStateTransition transition
            in machine
                .anyStateTransitions
                .ToArray())
        {
            machine
                .RemoveAnyStateTransition(
                    transition);
        }

        foreach (
            ChildAnimatorState child
            in machine.states.ToArray())
        {
            machine.RemoveState(
                child.state);
        }

        foreach (
            ChildAnimatorStateMachine child
            in machine
                .stateMachines
                .ToArray())
        {
            machine.RemoveStateMachine(
                child.stateMachine);
        }

        controller.AddParameter(
            "IsDrawing",
            AnimatorControllerParameterType.Bool);

        controller.AddParameter(
            "Release",
            AnimatorControllerParameterType.Trigger);

        controller.AddParameter(
            "AimAngle",
            AnimatorControllerParameterType.Float);

        controller.AddParameter(
            "DrawAmount",
            AnimatorControllerParameterType.Float);

        AnimatorState idle =
            machine.AddState(
                "Idle",
                new Vector3(
                    250f,
                    100f));

        idle.motion = idleClip;

        AnimatorState load =
            machine.AddState(
                "Load",
                new Vector3(
                    500f,
                    20f));

        load.motion = loadClip;

        AnimatorState hold =
            machine.AddState(
                "Hold",
                new Vector3(
                    750f,
                    20f));

        hold.motion = holdClip;

        AnimatorState release =
            machine.AddState(
                "Release",
                new Vector3(
                    500f,
                    210f));

        release.motion =
            releaseClip;

        machine.defaultState = idle;

        AnimatorStateTransition
            idleToLoad =
                idle.AddTransition(
                    load);

        ConfigureTransition(
            idleToLoad,
            false,
            0f,
            0.055f);

        idleToLoad.AddCondition(
            AnimatorConditionMode.If,
            0f,
            "IsDrawing");

        AnimatorStateTransition
            loadToHold =
                load.AddTransition(
                    hold);

        ConfigureTransition(
            loadToHold,
            true,
            0.96f,
            0.035f);

        loadToHold.AddCondition(
            AnimatorConditionMode.If,
            0f,
            "IsDrawing");

        AnimatorStateTransition
            loadCancel =
                load.AddTransition(
                    idle);

        ConfigureTransition(
            loadCancel,
            false,
            0f,
            0.05f);

        loadCancel.AddCondition(
            AnimatorConditionMode.IfNot,
            0f,
            "IsDrawing");

        AnimatorStateTransition
            holdCancel =
                hold.AddTransition(
                    idle);

        ConfigureTransition(
            holdCancel,
            false,
            0f,
            0.055f);

        holdCancel.AddCondition(
            AnimatorConditionMode.IfNot,
            0f,
            "IsDrawing");

        AnimatorStateTransition
            anyToRelease =
                machine
                    .AddAnyStateTransition(
                        release);

        ConfigureTransition(
            anyToRelease,
            false,
            0f,
            0.025f);

        anyToRelease
            .canTransitionToSelf =
                false;

        anyToRelease.AddCondition(
            AnimatorConditionMode.If,
            0f,
            "Release");

        AnimatorStateTransition
            releaseToIdle =
                release.AddTransition(
                    idle);

        ConfigureTransition(
            releaseToIdle,
            true,
            1f,
            0.07f);
    }

    private static void ConfigureTransition(
        AnimatorStateTransition transition,
        bool hasExitTime,
        float exitTime,
        float durationSeconds)
    {
        transition.hasExitTime =
            hasExitTime;

        transition.exitTime =
            exitTime;

        transition.duration =
            durationSeconds;

        transition.hasFixedDuration =
            true;
    }

    private Archer3DRuntimeProfile
        LoadOrCreateRuntimeProfile(
            string runtimeProfilePath)
    {
        Archer3DRuntimeProfile profile =
            AssetDatabase
                .LoadAssetAtPath<
                    Archer3DRuntimeProfile>(
                    runtimeProfilePath);

        if (profile != null)
            return profile;

        profile =
            CreateInstance<
                Archer3DRuntimeProfile>();

        AssetDatabase.CreateAsset(
            profile,
            runtimeProfilePath);

        return profile;
    }

    private ArcherCharacterRoster
        LoadOrCreateRoster()
    {
        ArcherCharacterRoster roster =
            AssetDatabase.LoadAssetAtPath<
                ArcherCharacterRoster>(
                    RosterPath);

        if (roster != null)
            return roster;

        roster =
            CreateInstance<
                ArcherCharacterRoster>();

        AssetDatabase.CreateAsset(
            roster,
            RosterPath);

        return roster;
    }

    private static void RegisterProfile(
        ArcherCharacterRoster roster,
        Archer3DRuntimeProfile profile,
        bool setAsDefault)
    {
        if (roster == null ||
            profile == null)
        {
            return;
        }

        int existingIndex = -1;

        for (int index = 0;
             index < roster.Profiles.Count;
             index++)
        {
            Archer3DRuntimeProfile existing =
                roster.Profiles[index];

            if (existing == profile ||
                (existing != null &&
                 existing.MatchesCharacterId(
                     profile.CharacterId)))
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            roster.Profiles[existingIndex] =
                profile;
        }
        else
        {
            roster.Profiles.Add(profile);
        }

        if (setAsDefault ||
            string.IsNullOrWhiteSpace(
                roster.DefaultCharacterId))
        {
            roster.DefaultCharacterId =
                profile.CharacterId;
        }
    }

    private string GetRuntimeProfilePath()
    {
        string normalizedId =
            NormalizeCharacterId(
                characterId);

        if (editingProfile != null &&
            editingProfile.MatchesCharacterId(
                normalizedId))
        {
            string existingPath =
                AssetDatabase.GetAssetPath(
                    editingProfile);

            if (!string.IsNullOrWhiteSpace(
                    existingPath))
            {
                return existingPath;
            }
        }

        return
            RuntimeProfileFolder +
            "/" +
            normalizedId +
            ".asset";
    }

    private static string NormalizeCharacterId(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Stable Character ID is required.");
        }

        char[] source =
            value.Trim().ToLowerInvariant()
                .ToCharArray();

        for (int index = 0;
             index < source.Length;
             index++)
        {
            char character = source[index];

            if ((character >= 'a' &&
                 character <= 'z') ||
                (character >= '0' &&
                 character <= '9') ||
                character == '-' ||
                character == '_')
            {
                continue;
            }

            source[index] = '-';
        }

        string normalized =
            new string(source).Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(
                "Stable Character ID must contain at least one letter or number.");
        }

        return normalized;
    }

    private static Vector3 ClampScale(
        Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0.001f, scale.x),
            Mathf.Max(0.001f, scale.y),
            Mathf.Max(0.001f, scale.z));
    }

    private static Vector3
        CalculateRuntimeScale(
            GameObject prefab,
            Vector3 euler,
            Vector3 sourceScale,
            float desiredHeight)
    {
        GameObject instance =
            Instantiate(prefab);

        instance.hideFlags =
            HideFlags.HideAndDontSave;

        try
        {
            instance.transform.position =
                Vector3.zero;

            instance.transform.rotation =
                Quaternion.Euler(
                    euler);

            instance.transform.localScale =
                sourceScale;

            Renderer[] renderers =
                instance
                    .GetComponentsInChildren<
                        Renderer>(
                        true);

            bool initialized = false;
            Bounds bounds = default;

            foreach (
                Renderer renderer
                in renderers)
            {
                if (!renderer.enabled)
                    continue;

                if (!initialized)
                {
                    bounds =
                        renderer.bounds;

                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(
                        renderer.bounds);
                }
            }

            if (!initialized ||
                bounds.size.y <=
                    0.0001f)
            {
                return sourceScale;
            }

            float multiplier =
                desiredHeight /
                bounds.size.y;

            return
                sourceScale *
                multiplier;
        }
        finally
        {
            DestroyImmediate(
                instance);
        }
    }

    private static string
        DetectHeldArrowPath(
            GameObject prefab)
    {
        Transform match =
            FindFirstTransform(
                prefab.transform,
                name =>
                    name.IndexOf(
                        "ArrowToShoot",
                        StringComparison
                            .OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(
                        "Arrow To Shoot",
                        StringComparison
                            .OrdinalIgnoreCase) >= 0);

        return match != null
            ? GetRelativePath(
                prefab.transform,
                match)
            : "";
    }

    private static string
        DetectBowVisualPath(
            GameObject prefab)
    {
        Transform best = null;
        int bestScore =
            int.MinValue;

        foreach (
            Transform candidate
            in prefab
                .GetComponentsInChildren<
                    Transform>(
                    true))
        {
            if (candidate ==
                prefab.transform)
            {
                continue;
            }

            string lower =
                candidate.name
                    .ToLowerInvariant();

            if (!lower.Contains("bow"))
                continue;

            bool hasRenderer =
                candidate
                    .GetComponentInChildren<
                        Renderer>(
                        true) != null;

            int score =
                hasRenderer
                    ? 100
                    : 0;

            if (lower.Contains(
                    "humanarcher_bow"))
            {
                score += 50;
            }

            if (lower.Contains(
                    "bowwithscript"))
            {
                score += 40;
            }

            if (lower == "bow")
                score += 20;

            if (lower.Contains("root") ||
                lower.Contains("bone"))
            {
                score -= 40;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best != null
            ? GetRelativePath(
                prefab.transform,
                best)
            : "";
    }

    private static Transform
        FindFirstTransform(
            Transform root,
            Func<string, bool> predicate)
    {
        foreach (Transform child
                 in root)
        {
            if (predicate(child.name))
                return child;

            Transform nested =
                FindFirstTransform(
                    child,
                    predicate);

            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string
        GetRelativePath(
            Transform root,
            Transform target)
    {
        List<string> parts =
            new List<string>();

        Transform current =
            target;

        while (current != null &&
               current != root)
        {
            parts.Add(
                current.name);

            current =
                current.parent;
        }

        parts.Reverse();

        return string.Join(
            "/",
            parts);
    }

    private static AnimationClip
        RequireClip(
            Dictionary<
                string,
                AnimationClip> clips,
            string id)
    {
        if (!clips.TryGetValue(
                id,
                out AnimationClip clip))
        {
            throw new
                InvalidOperationException(
                    "Existing ArcherCaptureProfile " +
                    "is missing clip Id '" +
                    id +
                    "'.");
        }

        return clip;
    }

    private bool ValidateSource(
        out string error)
    {
        if (sourceProfile == null)
        {
            error =
                "Assign an ArcherCaptureProfile with Idle, Load, Hold and Release clips.";
            return false;
        }

        if (sourceProfile.ArcherPrefab ==
            null)
        {
            error =
                "The existing capture profile has no Archer Prefab.";
            return false;
        }

        string[] required =
        {
            "Idle",
            "Load",
            "Hold",
            "Release"
        };

        foreach (string id in required)
        {
            bool found =
                sourceProfile.Clips !=
                    null &&
                sourceProfile.Clips.Any(
                    definition =>
                        definition != null &&
                        definition.SourceClip != null &&
                        string.Equals(
                            definition.Id?.Trim(),
                            id,
                            StringComparison
                                .OrdinalIgnoreCase));

            if (!found)
            {
                error =
                    "Existing capture profile must contain Id=" +
                    id +
                    ".";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static void
        EnsureAssetFolder(
            string assetFolder)
    {
        string normalized =
            assetFolder
                .Replace(
                    '\\',
                    '/')
                .TrimEnd('/');

        string[] parts =
            normalized.Split('/');

        if (parts.Length == 0 ||
            !parts[0].Equals(
                "Assets",
                StringComparison
                    .OrdinalIgnoreCase))
        {
            throw new
                InvalidOperationException(
                    "Asset folder must start with Assets.");
        }

        string current =
            "Assets";

        for (
            int index = 1;
            index < parts.Length;
            index++)
        {
            string next =
                current +
                "/" +
                parts[index];

            if (!AssetDatabase
                .IsValidFolder(
                    next))
            {
                AssetDatabase
                    .CreateFolder(
                        current,
                        parts[index]);
            }

            current = next;
        }
    }
}
#endif
