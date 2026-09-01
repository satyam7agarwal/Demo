using UnityEngine;

public enum ArcherBowBindingMode
{
    FollowAnimatedHand = 0,
    CameraFacing2D = 1
}

public enum ArcherSocketBindingMode
{
    HandBonePivots = 0,
    HumanoidAutoFingerSockets = 1
}

[CreateAssetMenu(
    fileName = "DefaultArcher3D",
    menuName = "Archery Trick Shot/Archer 3D Runtime Profile")]
public sealed class Archer3DRuntimeProfile : ScriptableObject
{
    private const string DefaultResourcesPath =
        "Archer3D/DefaultArcher3D";

    [Header("Character Identity")]
    [Tooltip(
        "Stable, case-insensitive ID used by the character roster and save data. " +
        "Changing this after release can invalidate a saved character selection.")]
    public string CharacterId = "archer";

    [Tooltip("Player-facing name. It is safe to localize or change this value.")]
    public string DisplayName = "Archer";

    [Header("Source")]
    public GameObject ArcherPrefab;
    public RuntimeAnimatorController AnimatorController;

    [Tooltip(
        "Optional authored bow prefab. When the character prefab does not " +
        "already contain a bow, runtime setup attaches this prefab to the " +
        "configured bow hand.")]
    public GameObject BowPrefab;

    [Header("Runtime Material Override")]
    [Tooltip(
        "Optional character-body material applied at runtime after the original " +
        "FBX/prefab is instantiated. This keeps the proven rigged source asset " +
        "unchanged while allowing Hyper3D PBR textures to be added safely.")]
    public Material CharacterMaterialOverride;

    [Tooltip(
        "When enabled, Character Material Override replaces every material slot " +
        "on MeshRenderer/SkinnedMeshRenderer components inside the character " +
        "before the runtime bow is created. Leave enabled for normal single-atlas " +
        "Hyper3D/Mixamo characters.")]
    public bool ApplyCharacterMaterialToAllSlots = true;

    [Header("Runtime Placement")]
    [Min(0.1f)]
    public float DesiredWorldHeight = 3.6f;

    public Vector3 LocalScale = Vector3.one;
    public Vector3 LocalEulerAngles =
        new Vector3(0f, 90f, 0f);

    public Vector3 LocalOffset = Vector3.zero;

    [Tooltip(
        "Keeps differently sized humanoid characters at Desired World Height " +
        "without requiring per-prefab Inspector scale tuning.")]
    public bool AutoScaleToDesiredHeight = true;

    [Header("Humanoid Hands")]
    public HumanBodyBones BowHandBone =
        HumanBodyBones.LeftHand;

    public HumanBodyBones DrawHandBone =
        HumanBodyBones.RightHand;

    [Header("Automatic Humanoid Archery Sockets")]
    [Tooltip(
        "Recommended for Hyper/Mixamo-style Humanoids. Runtime exposes a standard bow-grip " +
        "socket and derives the arrow/string nock from the draw-hand finger roots, " +
        "draw-hand finger roots, avoiding character-specific wrist-pivot fixes. " +
        "Use Hand Bone Pivots only for authored legacy rigs such as Kevin.")]
    public ArcherSocketBindingMode SocketBindingMode =
        ArcherSocketBindingMode.HumanoidAutoFingerSockets;

    [Tooltip(
        "Optional proportional bow-grip reach from the Humanoid hand pivot toward " +
        "the proximal finger roots. The shared project bow is calibrated to the " +
        "standard Humanoid hand pivot, so keep the default at zero.")]
    [Range(0f, 1.25f)]
    public float AutoBowGripPalmReach = 0f;

    [Tooltip(
        "Automatic nock placement across the full visible index/middle finger span. " +
        "0.68 is the recommended Mixamo/Hyper default near the finger-pad/string-hook area. " +
        "The value is proportional to each rig's own finger length, not a world-space offset.")]
    [Range(0f, 1f)]
    public float AutoDrawNockFingerAdvance = 0.68f;

    [Tooltip(
        "Rare fallback only. Local-space correction after automatic bow-grip socket resolution. " +
        "Leave zero for normal Hyper/Mixamo characters.")]
    public Vector3 BowGripSocketLocalCorrection = Vector3.zero;

    [Tooltip(
        "Rare fallback only. Local-space correction after automatic draw-nock socket resolution. " +
        "Leave zero for normal Hyper/Mixamo characters.")]
    public Vector3 DrawNockSocketLocalCorrection = Vector3.zero;

    [Header("Continuous Aim")]
    [Tooltip(
        "Physics always uses the exact BowController angle. " +
        "This affects only visual character posing.")]
    [Range(-1.5f, 1.5f)]
    public float AimAngleMultiplier = 1f;

    [Range(10f, 85f)]
    public float MaxVisualAimAngle = 60f;

    [Tooltip(
        "Lower is more responsive; higher filters input jitter.")]
    [Range(0.005f, 0.2f)]
    public float AimSmoothTime = 0.04f;

    [Min(30f)]
    public float MaxAimSpeed = 1080f;

    [Tooltip(
        "Smoothly fades the procedural posture in during Load/Hold " +
        "and out after Release.")]
    [Range(0.005f, 0.2f)]
    public float PoseBlendSmoothTime = 0.055f;

    [Header("Natural Full-Body Posture")]
    [Tooltip(
        "How strongly the body joins the aim as the shot becomes extreme. " +
        "X = normalized absolute aim angle, Y = body engagement.")]
    public AnimationCurve BodyEngagementCurve =
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.30f, 0.03f),
            new Keyframe(0.55f, 0.24f),
            new Keyframe(0.80f, 0.67f),
            new Keyframe(1f, 1f));

    [Tooltip(
        "Maximum fraction of an UPWARD aim angle handled by torso posture. " +
        "The arms automatically receive the remaining angle.")]
    [Range(0f, 0.45f)]
    public float UpBodyContribution = 0.24f;

    [Tooltip(
        "Maximum fraction of a DOWNWARD aim angle handled by torso posture.")]
    [Range(0f, 0.45f)]
    public float DownBodyContribution = 0.28f;

    [Tooltip(
        "Share of the torso contribution applied to Spine.")]
    [Range(0f, 1f)]
    public float SpineShare = 0.34f;

    [Tooltip(
        "Share of the torso contribution applied to Chest.")]
    [Range(0f, 1f)]
    public float ChestShare = 0.46f;

    [Tooltip(
        "Share of the torso contribution applied to UpperChest when mapped. " +
        "If UpperChest is absent, its share is automatically redistributed.")]
    [Range(0f, 1f)]
    public float UpperChestShare = 0.20f;

    [Tooltip(
        "Extra head follow at stronger angles. " +
        "Kept deliberately small to avoid a robotic neck.")]
    [Range(0f, 0.25f)]
    public float HeadAimContribution = 0.08f;

    [Tooltip(
        "1 = arms use all remaining angle after the torso contribution.")]
    [Range(0.5f, 1.25f)]
    public float ArmAimWeight = 1f;

    [Tooltip(
        "Prefer Shoulder bones as arm-chain roots, falling back to UpperArm.")]
    public bool PreferShoulderBones = true;

    [Header("Stable Bow Binding")]
    [Tooltip(
        "Optional path to the rendered bow if it is outside the bow-hand hierarchy. " +
        "Setup auto-detects it when possible.")]
    public string BowVisualRelativePath = "";

    [Tooltip(
        "Keep an external bow rigidly bound to the correctly detected bow hand.")]
    public bool SyncExternalBowToBowHand = true;

    [Tooltip(
        "Follow Animated Hand is correct for Kevin's authored prop bone. " +
        "Camera Facing 2D is for retargeted characters such as Khaem whose " +
        "wrist rotates differently between clips.")]
    public ArcherBowBindingMode BowBindingMode =
        ArcherBowBindingMode.FollowAnimatedHand;

    [Tooltip(
        "Local placement used when Bow Prefab is instantiated on the humanoid " +
        "bow hand.")]
    public Vector3 BowLocalPosition = Vector3.zero;

    public Vector3 BowLocalEulerAngles = Vector3.zero;

    public Vector3 BowLocalScale = Vector3.one;

    [Tooltip(
        "Camera-plane correction after binding. X moves horizontally and Y " +
        "moves vertically in world units.")]
    public Vector2 BowScreenPlaneOffset = Vector2.zero;

    [Tooltip(
        "Moves a camera-facing bow toward the camera so the character mesh " +
        "does not hide the string or lower limb.")]
    [Min(0f)]
    public float BowCameraDepthOffset = 0.05f;

    [Tooltip(
        "Optional prefab-space correction applied AFTER Camera Facing 2D aligns " +
        "the bow prefab to the camera XY plane. Keep zero for bow prefabs such " +
        "as HumanArcher_Bow that are authored in local XY / facing local +Z.")]
    public Vector3 BowCameraFacingEulerAngles = Vector3.zero;

    [Tooltip(
        "How strongly the camera-facing bow follows the visual aim angle.")]
    [Range(-1.5f, 1.5f)]
    public float BowAimAngleMultiplier = 1f;

    [Tooltip(
        "Fallback release response when the selected character does not carry " +
        "Kevin's demo HumanArcherController. The real projectile is still " +
        "released by BowController.")]
    public AnimationCurve BowReleaseCurve =
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.34f, 1.16f),
            new Keyframe(0.71f, 0.87f),
            new Keyframe(1f, 1f));

    [Header("Arrow / Nock")]
    [Tooltip(
        "Small character-specific correction from the draw hand to the real string nock.")]
    public Vector3 NockOffsetInDrawHandLocal =
        Vector3.zero;

    public float GameplayArrowZOffset = -0.08f;

    [Tooltip(
        "Optional path of the asset's visual held arrow.")]
    public string HeldArrowRelativePath = "";

    public bool PreferAssetHeldArrow = true;

    [Header("Animation Contract")]
    public string IdleStateName = "Idle";
    public string LoadStateName = "Load";
    public string HoldStateName = "Hold";
    public string ReleaseStateName = "Release";

    public string IsDrawingParameter = "IsDrawing";
    public string ReleaseParameter = "Release";
    public string AimAngleParameter = "AimAngle";
    public string DrawAmountParameter = "DrawAmount";

    [Range(0.02f, 0.95f)]
    public float ReleaseFireNormalizedTime = 0.24f;

    [Min(0.05f)]
    public float ReleaseEventFallbackSeconds = 0.35f;

    [Header("Third-party Demo Isolation")]
    public string[] DisableBehaviourTypeNames =
    {
        "HumanArcherController"
    };

    private static Archer3DRuntimeProfile cachedDefault;

    public static Archer3DRuntimeProfile LoadDefault()
    {
        if (cachedDefault != null)
            return cachedDefault;

        ArcherCharacterRoster roster =
            ArcherCharacterRoster.LoadDefault();

        if (roster != null)
        {
            cachedDefault =
                roster.ResolveSelectedProfile();

            if (cachedDefault != null)
                return cachedDefault;
        }

        cachedDefault =
            Resources.Load<Archer3DRuntimeProfile>(
                DefaultResourcesPath);

        if (cachedDefault == null)
        {
            Debug.LogError(
                "DefaultArcher3D.asset was not found at Resources/" +
                DefaultResourcesPath +
                ". Run Tools > Archery Trick Shot > Setup Smooth 3D Archer.");
        }

        return cachedDefault;
    }

    public bool MatchesCharacterId(
        string characterId)
    {
        return
            !string.IsNullOrWhiteSpace(
                characterId) &&
            string.Equals(
                CharacterId?.Trim(),
                characterId.Trim(),
                System.StringComparison
                    .OrdinalIgnoreCase);
    }

    public static void InvalidateCachedDefault()
    {
        cachedDefault = null;
    }

    private void OnValidate()
    {
        CharacterId =
            string.IsNullOrWhiteSpace(
                CharacterId)
                ? name.Trim().ToLowerInvariant()
                : CharacterId.Trim().ToLowerInvariant();

        DisplayName =
            string.IsNullOrWhiteSpace(
                DisplayName)
                ? name.Trim()
                : DisplayName.Trim();

        DesiredWorldHeight =
            Mathf.Max(0.1f, DesiredWorldHeight);

        AimSmoothTime =
            Mathf.Max(0.005f, AimSmoothTime);

        PoseBlendSmoothTime =
            Mathf.Max(0.005f, PoseBlendSmoothTime);

        MaxAimSpeed =
            Mathf.Max(30f, MaxAimSpeed);

        ReleaseEventFallbackSeconds =
            Mathf.Max(
                0.05f,
                ReleaseEventFallbackSeconds);

        BowCameraDepthOffset =
            Mathf.Max(
                0f,
                BowCameraDepthOffset);

        BowLocalScale =
            new Vector3(
                Mathf.Max(0.001f, BowLocalScale.x),
                Mathf.Max(0.001f, BowLocalScale.y),
                Mathf.Max(0.001f, BowLocalScale.z));

        AutoBowGripPalmReach =
            Mathf.Clamp(AutoBowGripPalmReach, 0f, 1.25f);

        AutoDrawNockFingerAdvance =
            Mathf.Clamp(AutoDrawNockFingerAdvance, 0f, 1f);

    }
}
