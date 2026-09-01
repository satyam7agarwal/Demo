using UnityEngine;

/// <summary>
/// Keeps Khaem's visual bow attached to the left hand without inheriting the
/// wrist rotation from the retargeted animation.
///
/// Add this component to the KhaemCharacter root in the isolated KhaemRigTest
/// scene. Keep HumanArcher_Bow parented under mixamorig:LeftHand and preserve
/// the edit-time Transform that makes the bow vertical.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class KhaemBowAttachment : MonoBehaviour
{
    private const string DefaultBowObjectName = "HumanArcher_Bow";

    [Header("Optional stable references")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform bowVisual;
    [SerializeField] private Camera gameplayCamera;

    [Header("Attachment")]
    [SerializeField] private HumanBodyBones handBone = HumanBodyBones.LeftHand;
    [SerializeField] private string bowObjectName = DefaultBowObjectName;

    [Tooltip(
        "Applies the verified Khaem-to-Kevin bow alignment before the stable " +
        "world rotation is captured.")]
    [SerializeField] private bool applyVerifiedLocalAlignment = true;

    [SerializeField] private Vector3 bowLocalPosition = Vector3.zero;

    [SerializeField] private Vector3 bowLocalEulerAngles =
        new Vector3(90f, 180f, 0f);

    [SerializeField] private Vector3 bowLocalScale =
        new Vector3(0.75f, 0.75f, 0.75f);

    [Tooltip("Screen-plane adjustment in world units: X moves horizontally and Y vertically.")]
    [SerializeField] private Vector2 screenPlaneOffset = Vector2.zero;

    [Tooltip("Moves the bow toward the camera so the character does not hide part of it.")]
    [Min(0f)]
    [SerializeField] private float cameraDepthOffset = 0.05f;

    private Transform hand;
    private Transform drawHand;
    private KevinBowRuntimeController bowRig;
    private Quaternion stableWorldRotation;
    private float aimAngleDegrees;
    private bool initialized;

    private void Awake()
    {
        initialized = TryInitialize();
        enabled = initialized;
    }

    private bool TryInitialize()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            Debug.LogError("[KhaemBowAttachment] No Animator was found under KhaemCharacter.", this);
            return false;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("[KhaemBowAttachment] The Animator must use a valid Humanoid avatar.", animator);
            return false;
        }

        hand = animator.GetBoneTransform(handBone);
        if (hand == null)
        {
            Debug.LogError($"[KhaemBowAttachment] Humanoid bone '{handBone}' was not found.", animator);
            return false;
        }

        if (bowVisual == null)
            bowVisual = FindDescendantByName(transform, bowObjectName);

        if (bowVisual == null)
        {
            Debug.LogError(
                $"[KhaemBowAttachment] Could not find '{bowObjectName}'. " +
                "Keep the visible bow under the hand or assign Bow Visual.",
                this);
            return false;
        }

        if (applyVerifiedLocalAlignment)
        {
            bowVisual.localPosition =
                bowLocalPosition;

            bowVisual.localRotation =
                Quaternion.Euler(
                    bowLocalEulerAngles);

            bowVisual.localScale =
                bowLocalScale;
        }

        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        drawHand =
            animator.GetBoneTransform(
                HumanBodyBones.RightHand);

        // Capture the correctly aligned edit-time orientation before the
        // retargeted wrist animation starts rotating the hand.
        stableWorldRotation = bowVisual.rotation;

        bowRig =
            GetComponent<KevinBowRuntimeController>();

        if (bowRig == null)
        {
            bowRig =
                gameObject.AddComponent<
                    KevinBowRuntimeController>();
        }

        bowRig.Configure(
            transform,
            bowVisual,
            drawHand,
            Vector3.zero,
            null);

        return true;
    }

    private void LateUpdate()
    {
        if (!initialized || hand == null || bowVisual == null)
            return;

        Vector3 position = hand.position;
        Vector3 rotationAxis = Vector3.forward;

        if (gameplayCamera != null)
        {
            Transform cameraTransform = gameplayCamera.transform;

            position += cameraTransform.right * screenPlaneOffset.x;
            position += cameraTransform.up * screenPlaneOffset.y;
            position -= cameraTransform.forward * cameraDepthOffset;

            rotationAxis = cameraTransform.forward;
        }
        else
        {
            position += new Vector3(screenPlaneOffset.x, screenPlaneOffset.y, -cameraDepthOffset);
        }

        Quaternion aimRotation = Quaternion.AngleAxis(aimAngleDegrees, rotationAxis);
        bowVisual.SetPositionAndRotation(position, aimRotation * stableWorldRotation);

        bowRig?.ApplyAfterArcherPose(
            Time.deltaTime);
    }

    /// <summary>
    /// Allows the gameplay aiming system to rotate the visual bow later.
    /// Zero keeps the edit-time vertical orientation used by KhaemRigTest.
    /// </summary>
    public void SetAimAngle(float angleDegrees)
    {
        aimAngleDegrees = angleDegrees;
    }

    /// <summary>
    /// Re-captures the bow's current world rotation as its stable orientation.
    /// Useful after intentionally realigning the bow while not in Play Mode.
    /// </summary>
    public void CaptureCurrentRotation()
    {
        if (bowVisual != null)
            stableWorldRotation = bowVisual.rotation;
    }

    public void SetTestAnimationState(
        int stateIndex)
    {
        if (bowRig == null ||
            !bowRig.IsReady)
        {
            return;
        }

        switch (stateIndex)
        {
            case 1:
                bowRig.BeginDraw();
                bowRig.SetDrawAmount(0.45f);
                break;

            case 2:
                bowRig.BeginDraw();
                bowRig.SetDrawAmount(1f);
                break;

            case 3:
                bowRig.Release();
                break;

            default:
                bowRig.SetReady();
                break;
        }
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (descendants[i].name == targetName)
                return descendants[i];
        }

        return null;
    }
}
