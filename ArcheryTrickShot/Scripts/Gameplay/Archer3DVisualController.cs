using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Final runtime archer visual:
///
/// 1. Kevin Iglesias Animator writes Idle/Load/Hold/Release.
/// 2. LateUpdate adds continuous procedural posture.
/// 3. Small/medium angles are mostly arms.
/// 4. Extreme angles progressively recruit Spine/Chest/UpperChest.
/// 5. Feet/hips remain untouched, avoiding foot sliding.
/// 6. Bow is rigidly reconstructed from one stable bow-hand binding.
/// 7. Bow hand is corrected at runtime from the actual bow position.
/// 8. PoseApplied fires only after the final hand/bow pose so BowController
///    can attach the arrow to the final draw-hand nock.
///
/// This deliberately does NOT use Animator IK and does NOT use discrete
/// Up/Down sprite/animation bands.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class Archer3DVisualController : MonoBehaviour
{
    private Animator animator;
    private Archer3DRuntimeProfile profile;
    private Transform characterRoot;

    private Transform spine;
    private Transform chest;
    private Transform upperChest;
    private Transform neck;
    private Transform head;

    private Transform leftArmAimRoot;
    private Transform rightArmAimRoot;

    private Transform leftHand;
    private Transform rightHand;
    private Transform bowHand;
    private Transform drawHand;

    // Standard runtime sockets for Hyper/Mixamo-style Humanoids.
    // Kevin stays on the legacy authored-hand path.
    private ArcherHumanoidSocketAdapter humanoidSockets;

    private Transform externalBowVisual;
    private bool externalBowNeedsSync;
    private bool captureCameraFacingBowNextPose;

    // Adapter around Kevin Iglesias' REAL bow/string references.
    // No mesh inference, no generated tip anchors.
    private KevinBowRuntimeController kevinBow;

    // Captured once at draw start. Never accumulated frame-to-frame.
    private bool bowBindingValid;
    private bool captureBowBindingNextPose;
    private Vector3 bowPositionInHandLocal;
    private Quaternion bowRotationInHandLocal = Quaternion.identity;

    private readonly List<Renderer> heldArrowRenderers = new List<Renderer>();

    private float targetAimAngle;
    private float currentAimAngle;
    private float aimVelocity;

    private float poseBlend;
    private float poseBlendVelocity;

    private bool drawing;
    private bool releasePending;
    private bool releaseFireQueued;
    private float releaseElapsed;

    private int isDrawingHash;
    private int releaseHash;
    private int aimAngleHash;
    private int drawAmountHash;

    private bool hasIsDrawing;
    private bool hasRelease;
    private bool hasAimAngle;
    private bool hasDrawAmount;

    private bool rigReady;

    public event Action ReleaseFrame;

    /// <summary>
    /// Runs after final body/arm/bow posing.
    /// BowController attaches the held arrow after this callback.
    /// </summary>
    public event Action PoseApplied;

    public bool HasHeldArrowVisual =>
        profile != null && profile.PreferAssetHeldArrow && heldArrowRenderers.Count > 0;

    public float CurrentVisualAimAngle => currentAimAngle;

    public Archer3DRuntimeProfile Profile => profile;

    public Vector3 NockWorldPosition
    {
        get
        {
            // The bow owns the final string nock. This is the single visual
            // source of truth used by BowController for the held arrow tail.
            if (kevinBow != null && kevinBow.IsReady)
            {
                return kevinBow.NockWorldPosition;
            }

            return DrawNockSocketWorldPosition;
        }
    }

    public Vector3 BowGripSocketWorldPosition
    {
        get
        {
            if (UsesHumanoidAutoSockets &&
                humanoidSockets != null &&
                humanoidSockets.BowGripSocket != null)
            {
                return humanoidSockets.BowGripSocket.position;
            }

            return bowHand != null
                ? bowHand.position
                : transform.position;
        }
    }

    public Vector3 DrawNockSocketWorldPosition
    {
        get
        {
            if (UsesHumanoidAutoSockets &&
                humanoidSockets != null &&
                humanoidSockets.DrawNockSocket != null)
            {
                return humanoidSockets.DrawNockSocket.position;
            }

            if (drawHand == null)
                return transform.position;

            Vector3 offset =
                profile != null
                    ? profile.NockOffsetInDrawHandLocal
                    : Vector3.zero;

            return drawHand.TransformPoint(offset);
        }
    }

    public Vector2 PoseDirection
    {
        get
        {
            Vector2 direction =
                (Vector2)(BowGripSocketWorldPosition - DrawNockSocketWorldPosition);

            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.right;
        }
    }

    public void Configure(
        Archer3DRuntimeProfile runtimeProfile,
        Transform instantiatedCharacterRoot
    )
    {
        profile = runtimeProfile;
        characterRoot = instantiatedCharacterRoot;

        animator = GetComponent<Animator>();

        if (profile == null || animator == null)
        {
            Debug.LogError("Archer3DVisualController requires a profile and Animator.", this);
            return;
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        animator.updateMode = AnimatorUpdateMode.Normal;

        // CameraFacing2D intentionally does NOT capture wrist/hand rotation.
        // Retargeted wrist axes differ between characters and clips. The bow
        // plane is reconstructed directly from the gameplay camera in LateUpdate.

        ResolveRig();
        ResolveRuntimeSockets();
        ResolveExternalBow();
        ResolveHeldArrowVisual();
        SetupKevinBowRuntime();
        CacheAnimatorParameters();

        targetAimAngle = 0f;
        currentAimAngle = 0f;
        aimVelocity = 0f;

        poseBlend = 0f;
        poseBlendVelocity = 0f;

        drawing = false;
        releasePending = false;
        releaseFireQueued = false;
        releaseElapsed = 0f;

        bowBindingValid = false;
        captureBowBindingNextPose = false;
        captureCameraFacingBowNextPose = false;
        PrepareInitialBowBinding();

        SetHeldArrowVisible(false);
        SetReady();

        Debug.Log(
            "[Smooth Archer] Full-body aim ready="
                + rigReady
                + ", spine="
                + (spine != null)
                + ", chest="
                + (chest != null)
                + ", upperChest="
                + (upperChest != null)
                + ", leftAimRoot="
                + (leftArmAimRoot != null ? leftArmAimRoot.name : "NULL")
                + ", rightAimRoot="
                + (rightArmAimRoot != null ? rightArmAimRoot.name : "NULL")
                + ", bowHand="
                + (bowHand != null ? bowHand.name : "NULL")
                + ", drawHand="
                + (drawHand != null ? drawHand.name : "NULL")
                + ", externalBow="
                + (externalBowVisual != null ? externalBowVisual.name : "none"),
            this
        );
    }

    private void ResolveRig()
    {
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);

        chest = animator.GetBoneTransform(HumanBodyBones.Chest);

        upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        neck = animator.GetBoneTransform(HumanBodyBones.Neck);

        head = animator.GetBoneTransform(HumanBodyBones.Head);

        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        bowHand = animator.GetBoneTransform(profile.BowHandBone);

        drawHand = animator.GetBoneTransform(profile.DrawHandBone);

        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);

        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);

        Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

        Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

        leftArmAimRoot =
            profile.PreferShoulderBones && leftShoulder != null ? leftShoulder : leftUpperArm;

        rightArmAimRoot =
            profile.PreferShoulderBones && rightShoulder != null ? rightShoulder : rightUpperArm;

        Transform torsoRoot = upperChest ?? chest ?? spine;

        rigReady =
            torsoRoot != null
            && leftArmAimRoot != null
            && rightArmAimRoot != null
            && leftHand != null
            && rightHand != null;

        if (!rigReady)
        {
            Debug.LogError(
                "Full-body aiming cannot run because required Humanoid bones are missing.",
                this
            );
        }
    }

    private bool UsesHumanoidAutoSockets =>
        profile != null &&
        profile.SocketBindingMode ==
            ArcherSocketBindingMode.HumanoidAutoFingerSockets;

    private void ResolveRuntimeSockets()
    {
        humanoidSockets = null;

        if (!UsesHumanoidAutoSockets ||
            animator == null ||
            bowHand == null ||
            drawHand == null)
        {
            return;
        }

        humanoidSockets =
            GetComponent<ArcherHumanoidSocketAdapter>();

        if (humanoidSockets == null)
        {
            humanoidSockets =
                gameObject.AddComponent<ArcherHumanoidSocketAdapter>();
        }

        humanoidSockets.Configure(
            animator,
            profile,
            bowHand,
            drawHand);
    }

    private Transform GetDrawNockTargetTransform()
    {
        if (UsesHumanoidAutoSockets &&
            humanoidSockets != null &&
            humanoidSockets.DrawNockSocket != null)
        {
            return humanoidSockets.DrawNockSocket;
        }

        return drawHand;
    }

    private Vector3 GetDrawNockTargetLocalOffset()
    {
        return UsesHumanoidAutoSockets
            ? Vector3.zero
            : profile != null
                ? profile.NockOffsetInDrawHandLocal
                : Vector3.zero;
    }

    private void ResolveExternalBow()
    {
        externalBowVisual = null;
        externalBowNeedsSync = false;

        if (characterRoot == null || !profile.SyncExternalBowToBowHand)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.BowVisualRelativePath))
        {
            externalBowVisual = characterRoot.Find(profile.BowVisualRelativePath);
        }

        if (externalBowVisual == null)
        {
            externalBowVisual = FindLikelyBowVisual(characterRoot);
        }

        if (externalBowVisual != null &&
            externalBowVisual.name == "RuntimeVisualBow" &&
            profile.BowPrefab != null &&
            bowHand != null)
        {
            externalBowVisual.SetParent(
                bowHand,
                false);

            ApplyProfileBowLocalTransform(
                externalBowVisual);
        }

        if (externalBowVisual == null &&
            profile.BowPrefab != null &&
            bowHand != null)
        {
            GameObject bowInstance =
                Instantiate(
                    profile.BowPrefab,
                    bowHand,
                    false);

            bowInstance.name =
                "RuntimeVisualBow";

            externalBowVisual =
                bowInstance.transform;

            ApplyProfileBowLocalTransform(
                externalBowVisual);
        }

        if (externalBowVisual == null || bowHand == null)
        {
            return;
        }

        // CameraFacing2D keeps the bow temporarily parented to the configured
        // hand only so the factory can finish its initial nock calibration.
        // The first LateUpdate detaches it and reconstructs a true screen-plane
        // orientation that is independent of retargeted wrist rotation.

        externalBowNeedsSync =
            UsesCameraFacingBow ||
            !externalBowVisual.IsChildOf(bowHand);
    }

    private void ApplyProfileBowLocalTransform(
        Transform bowTransform)
    {
        if (bowTransform == null ||
            profile == null)
        {
            return;
        }

        bowTransform.localPosition =
            profile.BowLocalPosition;

        bowTransform.localRotation =
            Quaternion.Euler(
                profile.BowLocalEulerAngles);

        bowTransform.localScale =
            profile.BowLocalScale;
    }

    private bool UsesCameraFacingBow =>
        profile != null &&
        profile.BowBindingMode ==
            ArcherBowBindingMode.CameraFacing2D;

    private void PrepareInitialBowBinding()
    {
        captureCameraFacingBowNextPose =
            UsesCameraFacingBow &&
            externalBowVisual != null &&
            bowHand != null;

        if (captureCameraFacingBowNextPose)
        {
            bowBindingValid = false;
        }
    }

    private static Transform FindDescendant(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrEmpty(exactName))
        {
            return null;
        }

        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate != null &&
                string.Equals(candidate.name, exactName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Transform FindLikelyBowVisual(Transform root)
    {
        if (root == null)
            return null;

        Transform best = null;
        int bestScore = int.MinValue;

        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == null || candidate == root)
                continue;

            string name = candidate.name ?? string.Empty;
            string lower = name.ToLowerInvariant();

            // Runtime Humanoid adapters deliberately create sockets such as
            // ATS_BowGripSocket. They are attachment targets, NOT bow visuals.
            // Never let a generated socket prevent BowPrefab instantiation.
            if (name.StartsWith("ATS_", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!lower.Contains("bow"))
                continue;

            bool hasRenderer =
                candidate.GetComponentInChildren<Renderer>(true) != null;

            bool hasLineRenderer =
                candidate.GetComponentInChildren<LineRenderer>(true) != null;

            bool hasAuthoredBowRig =
                FindDescendant(candidate, "B-nockPoint") != null ||
                FindDescendant(candidate, "B-bowLimb01") != null ||
                FindDescendant(candidate, "B-bowLimb02") != null;

            // A transform merely containing the word "bow" is not enough.
            // Real visual bows must own renderable content or the authored rig.
            if (!hasRenderer && !hasLineRenderer && !hasAuthoredBowRig)
                continue;

            int score = 0;

            if (hasAuthoredBowRig) score += 200;
            if (hasLineRenderer) score += 120;
            if (hasRenderer) score += 100;

            if (lower.Contains("humanarcher_bow"))
                score += 50;

            if (lower.Contains("bowwithscript"))
                score += 40;

            if (lower == "runtimevisualbow")
                score += 35;

            if (lower == "bow")
                score += 20;

            if (lower.Contains("root") || lower.Contains("bone"))
                score -= 40;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void SetupKevinBowRuntime()
    {
        if (characterRoot == null)
            return;

        kevinBow = GetComponent<KevinBowRuntimeController>();

        if (kevinBow == null)
        {
            kevinBow = gameObject.AddComponent<KevinBowRuntimeController>();
        }

        kevinBow.Configure(
            characterRoot,
            externalBowVisual,
            GetDrawNockTargetTransform(),
            GetDrawNockTargetLocalOffset(),
            profile);
    }

    private void ResolveHeldArrowVisual()
    {
        heldArrowRenderers.Clear();

        if (characterRoot == null || string.IsNullOrWhiteSpace(profile.HeldArrowRelativePath))
        {
            return;
        }

        Transform heldArrow = characterRoot.Find(profile.HeldArrowRelativePath);

        if (heldArrow == null)
            return;

        heldArrowRenderers.AddRange(heldArrow.GetComponentsInChildren<Renderer>(true));
    }

    private void CacheAnimatorParameters()
    {
        isDrawingHash = Animator.StringToHash(profile.IsDrawingParameter);

        releaseHash = Animator.StringToHash(profile.ReleaseParameter);

        aimAngleHash = Animator.StringToHash(profile.AimAngleParameter);

        drawAmountHash = Animator.StringToHash(profile.DrawAmountParameter);

        hasIsDrawing = false;
        hasRelease = false;
        hasAimAngle = false;
        hasDrawAmount = false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (
                parameter.nameHash == isDrawingHash
                && parameter.type == AnimatorControllerParameterType.Bool
            )
            {
                hasIsDrawing = true;
            }
            else if (
                parameter.nameHash == releaseHash
                && parameter.type == AnimatorControllerParameterType.Trigger
            )
            {
                hasRelease = true;
            }
            else if (
                parameter.nameHash == aimAngleHash
                && parameter.type == AnimatorControllerParameterType.Float
            )
            {
                hasAimAngle = true;
            }
            else if (
                parameter.nameHash == drawAmountHash
                && parameter.type == AnimatorControllerParameterType.Float
            )
            {
                hasDrawAmount = true;
            }
        }
    }

    private void Update()
    {
        if (profile == null)
            return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // BowController already supplies a gameplay-clamped direction. For the
        // generic Humanoid/Mixamo path, do NOT clamp it a second time with a
        // character-specific visual limit. A second clamp was the reason the
        // hands stopped around +/-60 degrees while gameplay/arrow continued to
        // +/-70 degrees.
        float requested =
            UsesHumanoidAutoSockets
                ? targetAimAngle
                : targetAimAngle * profile.AimAngleMultiplier;

        if (!UsesHumanoidAutoSockets)
        {
            requested = Mathf.Clamp(
                requested,
                -profile.MaxVisualAimAngle,
                profile.MaxVisualAimAngle);
        }

        // Humanoid presentation must stay on the same aim line as gameplay.
        // Legacy/Kevin retains the proven smoothing behaviour.
        if (UsesHumanoidAutoSockets)
        {
            currentAimAngle = requested;
            aimVelocity = 0f;
        }
        else
        {
            currentAimAngle = Mathf.SmoothDampAngle(
                currentAimAngle,
                requested,
                ref aimVelocity,
                profile.AimSmoothTime,
                profile.MaxAimSpeed,
                dt);
        }

        bool releaseStateStillPlaying = IsAnimatorInState(profile.ReleaseStateName);

        float targetBlend = drawing || releasePending || releaseStateStillPlaying ? 1f : 0f;

        poseBlend = Mathf.SmoothDamp(
            poseBlend,
            targetBlend,
            ref poseBlendVelocity,
            profile.PoseBlendSmoothTime,
            Mathf.Infinity,
            dt
        );

        if (hasAimAngle)
        {
            animator.SetFloat(aimAngleHash, currentAimAngle);
        }

        if (releasePending)
        {
            releaseElapsed += dt;

            if (!releaseFireQueued && releaseElapsed >= profile.ReleaseEventFallbackSeconds)
            {
                releaseFireQueued = true;
            }
        }
    }

    private void LateUpdate()
    {
        // Camera-facing characters do not have Kevin's authored hand-prop
        // bone. Capture the correctly aligned bow once after Animator has
        // evaluated its first real pose, then detach it from wrist rotation.
        if (captureCameraFacingBowNextPose)
        {
            CaptureCameraFacingBowBinding();
            captureCameraFacingBowNextPose = false;
        }

        // Capture authored bow-to-hand binding BEFORE
        // procedural posture changes.
        if (captureBowBindingNextPose &&
            !UsesCameraFacingBow)
        {
            CaptureStableBowBinding();
            captureBowBindingNextPose = false;
        }

        if (rigReady && poseBlend > 0.0001f)
        {
            ApplyNaturalFullBodyAim();
        }

        // Rebuild standard grip/nock sockets from the current Humanoid pose.
        humanoidSockets?.RefreshPose();

        // The authored animation + distributed torso/arm rotations are a good
        // natural starting pose, but separate shoulder pivots do not guarantee
        // that the final hand-to-hand line equals the requested gameplay line.
        // Apply a small feedback correction so draw fingers -> bow grip follows
        // the SAME direction used by trajectory/projectile gameplay.
        ApplyHumanoidAimLineCoherence();

        // Reconstruct from FINAL bow-grip socket / authored hand binding.
        ApplyStableBowBinding();

        // Now update Kevin's real bow internals:
        // B-bowLimb01 / B-bowTip01
        // B-bowLimb02 / B-bowTip02
        // B-nockPoint / AnchorPoint / original LineRenderer.
        // This runs AFTER the final body/arm/bow pose so all references are
        // in their correct world positions for this frame.
        kevinBow?.ApplyAfterArcherPose(Time.deltaTime);

        if (releaseFireQueued)
        {
            releaseFireQueued = false;
            releasePending = false;

            SetHeldArrowVisible(false);
            ReleaseFrame?.Invoke();
        }

        PoseApplied?.Invoke();
    }

    private void ApplyNaturalFullBodyAim()
    {
        float totalAngle = currentAimAngle * poseBlend;

        if (Mathf.Abs(totalAngle) < 0.001f)
        {
            return;
        }

        float normalizedAbsAngle = Mathf.Clamp01(
            Mathf.Abs(totalAngle) / Mathf.Max(1f, profile.MaxVisualAimAngle)
        );

        float bodyEngagement =
            profile.BodyEngagementCurve != null
                ? Mathf.Clamp01(profile.BodyEngagementCurve.Evaluate(normalizedAbsAngle))
                : normalizedAbsAngle;

        float maxBodyContribution =
            totalAngle >= 0f ? profile.UpBodyContribution : profile.DownBodyContribution;

        // Small angles = almost all arms.
        // Extreme angles = body joins progressively.
        float desiredBodyAngle = totalAngle * maxBodyContribution * bodyEngagement;

        float actualBodyAngle = ApplyTorsoDistribution(desiredBodyAngle);

        // Keep the final visual hand/bow direction close to
        // the requested total aim angle.
        float remainingArmAngle = (totalAngle - actualBodyAngle) * profile.ArmAimWeight;

        Quaternion armRotation = Quaternion.AngleAxis(remainingArmAngle, Vector3.forward);

        leftArmAimRoot.rotation = armRotation * leftArmAimRoot.rotation;

        rightArmAimRoot.rotation = armRotation * rightArmAimRoot.rotation;

        ApplyHeadFollow(totalAngle, bodyEngagement);
    }

    /// <summary>
    /// Generic Humanoid/Mixamo coherence pass. The animation remains responsible
    /// for the natural pose; this only removes residual angular error between the
    /// actual finger-to-grip line and the gameplay aim direction.
    ///
    /// This solves a common retargeting problem without per-character offsets:
    /// rotating two arms around different shoulder pivots does not rotate the
    /// hand-to-hand line by exactly the same number of degrees.
    /// </summary>
    private void ApplyHumanoidAimLineCoherence()
    {
        if (!UsesHumanoidAutoSockets ||
            humanoidSockets == null ||
            leftArmAimRoot == null ||
            rightArmAimRoot == null ||
            poseBlend <= 0.0001f)
        {
            return;
        }

        float targetAngle = currentAimAngle;

        // A few tiny feedback iterations are more stable across different
        // Mixamo/Hyper arm proportions than one hardcoded angle/offset.
        const int iterations = 4;
        const float gain = 0.72f;
        const float maxStepDegrees = 8f;
        const float toleranceDegrees = 0.12f;

        for (int i = 0; i < iterations; i++)
        {
            humanoidSockets.RefreshPose();

            Vector2 poseDirection = PoseDirection;
            if (poseDirection.sqrMagnitude < 0.0001f)
                break;

            float poseAngle =
                Mathf.Atan2(poseDirection.y, poseDirection.x) *
                Mathf.Rad2Deg;

            float error =
                Mathf.DeltaAngle(poseAngle, targetAngle);

            if (Mathf.Abs(error) <= toleranceDegrees)
                break;

            float correction =
                Mathf.Clamp(
                    error * gain,
                    -maxStepDegrees,
                    maxStepDegrees);

            Quaternion correctionRotation =
                Quaternion.AngleAxis(
                    correction,
                    Vector3.forward);

            leftArmAimRoot.rotation =
                correctionRotation * leftArmAimRoot.rotation;

            rightArmAimRoot.rotation =
                correctionRotation * rightArmAimRoot.rotation;
        }

        humanoidSockets.RefreshPose();
    }

    private float ApplyTorsoDistribution(float desiredBodyAngle)
    {
        float spineWeight = spine != null ? Mathf.Max(0f, profile.SpineShare) : 0f;

        float chestWeight = chest != null ? Mathf.Max(0f, profile.ChestShare) : 0f;

        float upperChestWeight = upperChest != null ? Mathf.Max(0f, profile.UpperChestShare) : 0f;

        float totalWeight = spineWeight + chestWeight + upperChestWeight;

        if (totalWeight <= 0.0001f)
            return 0f;

        // Normalize across ACTUALLY MAPPED bones.
        // Example: Kevin rig has no UpperChest -> its share
        // automatically redistributes to Spine + Chest.
        spineWeight /= totalWeight;
        chestWeight /= totalWeight;
        upperChestWeight /= totalWeight;

        float applied = 0f;

        applied += RotateBoneWorldZ(spine, desiredBodyAngle * spineWeight);

        applied += RotateBoneWorldZ(chest, desiredBodyAngle * chestWeight);

        applied += RotateBoneWorldZ(upperChest, desiredBodyAngle * upperChestWeight);

        return applied;
    }

    private static float RotateBoneWorldZ(Transform bone, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.001f)
        {
            return 0f;
        }

        bone.rotation = Quaternion.AngleAxis(degrees, Vector3.forward) * bone.rotation;

        return degrees;
    }

    private void ApplyHeadFollow(float totalAngle, float bodyEngagement)
    {
        if (profile.HeadAimContribution <= 0f)
            return;

        Transform headBone = head ?? neck;

        if (headBone == null)
            return;

        // Head only joins noticeably at medium/extreme angles.
        float headAngle = totalAngle * profile.HeadAimContribution * bodyEngagement;

        RotateBoneWorldZ(headBone, headAngle);
    }

    private void CaptureStableBowBinding()
    {
        if (!externalBowNeedsSync || externalBowVisual == null)
        {
            bowBindingValid = false;
            return;
        }

        AutoCorrectHandRolesFromActualBow();

        // Runtime hand-role correction can swap the profile's initial hand
        // assignment. Keep Kevin's real B-nockPoint constraint attached to the
        // corrected draw hand as well.
        kevinBow?.SetDrawHand(
            drawHand,
            profile != null ? profile.NockOffsetInDrawHandLocal : Vector3.zero
        );

        if (bowHand == null)
        {
            bowBindingValid = false;
            return;
        }

        bowPositionInHandLocal = bowHand.InverseTransformPoint(externalBowVisual.position);

        bowRotationInHandLocal = Quaternion.Inverse(bowHand.rotation) * externalBowVisual.rotation;

        bowBindingValid = true;

        Debug.Log(
            "[Smooth Archer] Stable bow binding captured. "
                + "bowHand="
                + bowHand.name
                + ", drawHand="
                + (drawHand != null ? drawHand.name : "NULL"),
            this
        );
    }

    private void CaptureCameraFacingBowBinding()
    {
        if (externalBowVisual == null ||
            bowHand == null ||
            characterRoot == null)
        {
            bowBindingValid = false;
            return;
        }

        AutoCorrectHandRolesFromActualBow();

        kevinBow?.SetDrawHand(
            GetDrawNockTargetTransform(),
            GetDrawNockTargetLocalOffset());

        if (externalBowVisual.IsChildOf(bowHand))
        {
            externalBowVisual.SetParent(
                characterRoot,
                true);
        }

        externalBowNeedsSync = true;
        bowBindingValid = true;

        Debug.Log(
            "[Smooth Archer] Camera-facing bow binding captured. " +
            "bowHand=" + bowHand.name +
            ", drawHand=" +
            (drawHand != null
                ? drawHand.name
                : "NULL"),
            this);
    }

    private void AutoCorrectHandRolesFromActualBow()
    {
        // Camera-facing / retargeted characters (for example Khaem) use the
        // explicit Humanoid hand mapping stored in their character profile.
        // Their runtime BowPrefab is initially created on that configured hand,
        // so distance-based detection would merely reinforce a bad profile value
        // and can also swap a correct mapping after retargeted wrist motion.
        //
        // FollowAnimatedHand remains auto-correctable for legacy/authored rigs
        // such as Kevin, preserving the proven rollback behaviour.
        if (UsesCameraFacingBow)
        {
            return;
        }

        if (externalBowVisual == null || leftHand == null || rightHand == null)
        {
            return;
        }

        float leftScore = GetBowHandDistanceScore(leftHand);

        float rightScore = GetBowHandDistanceScore(rightHand);

        if (leftScore <= rightScore)
        {
            bowHand = leftHand;
            drawHand = rightHand;
        }
        else
        {
            bowHand = rightHand;
            drawHand = leftHand;
        }

        Debug.Log(
            "[Smooth Archer] Runtime bow-hand detection: "
                + "leftScore="
                + leftScore.ToString("F4")
                + ", rightScore="
                + rightScore.ToString("F4")
                + ", bowHand="
                + bowHand.name
                + ", drawHand="
                + drawHand.name,
            this
        );
    }

    private float GetBowHandDistanceScore(Transform hand)
    {
        float pivotDistance = Vector3.Distance(hand.position, externalBowVisual.position);

        float rendererDistance = float.PositiveInfinity;

        Renderer[] renderers = externalBowVisual.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            Vector3 closest = renderer.bounds.ClosestPoint(hand.position);

            rendererDistance = Mathf.Min(
                rendererDistance,
                Vector3.Distance(hand.position, closest)
            );
        }

        if (float.IsPositiveInfinity(rendererDistance))
        {
            rendererDistance = pivotDistance;
        }

        return pivotDistance + rendererDistance * 0.35f;
    }

    private void ApplyStableBowBinding()
    {
        if (
            !externalBowNeedsSync
            || !bowBindingValid
            || externalBowVisual == null
            || bowHand == null
        )
        {
            return;
        }

        if (UsesCameraFacingBow)
        {
            Camera gameplayCamera =
                Camera.main;

            Vector3 cameraRight =
                gameplayCamera != null
                    ? gameplayCamera.transform.right
                    : Vector3.right;

            Vector3 cameraUp =
                gameplayCamera != null
                    ? gameplayCamera.transform.up
                    : Vector3.up;

            Vector3 cameraForward =
                gameplayCamera != null
                    ? gameplayCamera.transform.forward
                    : Vector3.forward;

            Vector3 position =
                BowGripSocketWorldPosition +
                cameraRight *
                profile.BowScreenPlaneOffset.x +
                cameraUp *
                profile.BowScreenPlaneOffset.y -
                cameraForward *
                profile.BowCameraDepthOffset;

            float visualBowAngle;

            if (UsesHumanoidAutoSockets &&
                poseBlend > 0.0001f)
            {
                Vector2 finalPoseDirection = PoseDirection;

                visualBowAngle =
                    Mathf.Atan2(
                        finalPoseDirection.y,
                        finalPoseDirection.x) *
                    Mathf.Rad2Deg *
                    profile.BowAimAngleMultiplier;
            }
            else
            {
                visualBowAngle =
                    currentAimAngle *
                    poseBlend *
                    profile.BowAimAngleMultiplier;
            }

            Quaternion aimRotation =
                Quaternion.AngleAxis(
                    visualBowAngle,
                    cameraForward);

            // HumanArcher_Bow is authored in its local XY plane (local +Z is
            // its facing direction). Rebuild that plane from the camera every
            // frame instead of inheriting/capturing any Humanoid wrist axes.
            // This is the critical rule that makes retargeted characters such
            // as Khaem deterministic across Idle/Load/Hold/Release.
            Quaternion screenPlaneRotation =
                Quaternion.LookRotation(
                    cameraForward,
                    cameraUp);

            Quaternion prefabCorrection =
                Quaternion.Euler(
                    profile.BowCameraFacingEulerAngles);

            externalBowVisual.SetPositionAndRotation(
                position,
                aimRotation *
                screenPlaneRotation *
                prefabCorrection);

            return;
        }

        externalBowVisual.position =
            bowHand.TransformPoint(
                bowPositionInHandLocal);

        externalBowVisual.rotation =
            bowHand.rotation *
            bowRotationInHandLocal;
    }

    private bool IsAnimatorInState(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);

        if (current.IsName(stateName))
            return true;

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);

        return next.IsName(stateName);
    }

    public void SetReady()
    {
        drawing = false;
        releasePending = false;
        releaseFireQueued = false;
        releaseElapsed = 0f;

        if (!UsesCameraFacingBow)
        {
            bowBindingValid = false;
            captureBowBindingNextPose = false;
        }
        else if (!bowBindingValid &&
                 externalBowVisual != null)
        {
            captureCameraFacingBowNextPose = true;
        }

        if (animator == null)
            return;

        if (hasRelease)
            animator.ResetTrigger(releaseHash);

        if (hasIsDrawing)
            animator.SetBool(isDrawingHash, false);

        if (hasDrawAmount)
            animator.SetFloat(drawAmountHash, 0f);

        SetHeldArrowVisible(false);
        kevinBow?.SetReady();
    }

    public void BeginDraw()
    {
        drawing = true;
        releasePending = false;
        releaseFireQueued = false;
        releaseElapsed = 0f;

        if (!UsesCameraFacingBow)
        {
            bowBindingValid = false;
            captureBowBindingNextPose = true;
        }
        else if (!bowBindingValid)
        {
            captureCameraFacingBowNextPose = true;
        }

        if (hasIsDrawing)
        {
            animator.SetBool(isDrawingHash, true);
        }

        SetHeldArrowVisible(true);
        kevinBow?.BeginDraw();
    }

    public void UpdateAim(Vector2 direction, float drawAmount)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        targetAimAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        float clampedDrawAmount = Mathf.Clamp01(drawAmount);

        if (hasDrawAmount)
        {
            animator.SetFloat(drawAmountHash, clampedDrawAmount);
        }

        kevinBow?.SetDrawAmount(clampedDrawAmount);
    }

    public void Release()
    {
        drawing = false;
        releasePending = true;
        releaseFireQueued = false;
        releaseElapsed = 0f;

        // Hide Kevin's temporary held arrow immediately.
        // The real gameplay arrow is fired by BowController on finger release.
        SetHeldArrowVisible(false);

        if (hasIsDrawing)
        {
            animator.SetBool(isDrawingHash, false);
        }

        kevinBow?.Release();

        if (hasRelease)
        {
            animator.SetTrigger(releaseHash);
        }
        else
        {
            releaseFireQueued = true;
        }
    }

    public void CancelDraw()
    {
        kevinBow?.CancelDraw();
        SetReady();
    }

    // Called by generated Release.anim Animation Event.
    public void OnReleaseFireEvent()
    {
        if (!releasePending)
            return;

        releaseFireQueued = true;
    }

    public void SetHeldArrowVisible(bool visible)
    {
        foreach (Renderer renderer in heldArrowRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }
}
