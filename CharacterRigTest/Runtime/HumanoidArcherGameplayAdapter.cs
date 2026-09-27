using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ArcheryTrickShot.CharacterTesting
{
    /// <summary>
    /// Generic Humanoid presentation adapter for the existing production archery gameplay.
    ///
    /// It deliberately does NOT replace BowController, ArrowController, Target or LevelManager.
    /// Those remain authoritative for input, aim direction, projectile physics and level state.
    ///
    /// The adapter owns only the character presentation contract:
    /// - retarget a valid Humanoid model into an archery pose,
    /// - place the production bow on the character's bow hand,
    /// - place the real gameplay arrow on the character's draw-hand/nock while aiming,
    /// - relocate the real projectile to that nock at the exact Shot event,
    /// - update the existing trajectory renderer from the character-controlled arrow position.
    ///
    /// Because it works from HumanBodyBones, the same component can be reused for Ember,
    /// Bruno, Tiko, Pogo, Mochi and other Humanoid-compatible characters. Per-character
    /// differences are calibration values, not separate shooting controllers.
    /// </summary>
    [DefaultExecutionOrder(1200)]
    [DisallowMultipleComponent]
    public class HumanoidArcherGameplayAdapter : MonoBehaviour
    {
        [Header("Production gameplay")]
        [SerializeField] private BowController bowController;

        [Header("Character")]
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private string characterDisplayName = "Character";
        [SerializeField] private Material characterMaterialOverride;

        [Header("Automatic fitting")]
        [SerializeField] private float characterHeightMultiplier = 1f;
        [SerializeField] private float modelYawDegrees = 90f;
        [SerializeField] private float characterCameraDepthOffset = 0.06f;

        [Header("Bow calibration")]
        [Range(0.65f, 1.15f)]
        [SerializeField] private float bowArmExtension = 0.96f;
        [SerializeField] private float bowCameraDepthOffset = 0.025f;
        [SerializeField] private Vector2 bowGripScreenOffset = Vector2.zero;
        [Range(0.5f, 1.5f)]
        [SerializeField] private float bowScaleMultiplier = 0.90f;

        [Header("Draw calibration")]
        [SerializeField] private float drawHandBehindHead = 0.08f;
        [SerializeField] private float drawHandVerticalOffset = -0.035f;
        [SerializeField] private float drawHandCameraOffset = 0.045f;
        [SerializeField] private float drawElbowBackFactor = 0.78f;
        [SerializeField] private float drawElbowVerticalOffset = 0.08f;
        [SerializeField] private float drawElbowCameraOffset = 0.10f;
        [SerializeField] private float releaseFollowThrough = 0.22f;
        [SerializeField] private float releasePoseSeconds = 0.16f;

        [Header("Original presentation")]
        [SerializeField] private bool hideOriginalCharacterBody = true;
        [SerializeField] private bool disableOriginalVisualDriver = true;

        private GameObject characterInstance;
        private Animator characterAnimator;
        private Camera gameplayCamera;
        private AimTrajectoryRenderer trajectoryRenderer;

        private MonoBehaviour originalVisualDriver;
        private Transform originalVisualRoot;
        private Transform productionBow;
        private Quaternion productionBowBaseRotation;
        private Vector3 productionBowBaseScale;

        private ArrowController currentArrow;
        private Renderer[] currentArrowRenderers;
        private SpriteRenderer currentArrowSprite;

        private readonly Dictionary<HumanBodyBones, Quaternion> baseLocalRotations =
            new Dictionary<HumanBodyBones, Quaternion>();

        private readonly List<RendererState> hiddenOriginalRenderers =
            new List<RendererState>();

        private Vector2 aimDirection = Vector2.right;
        private bool aiming;
        private float releasePoseUntil;
        private bool initialized;
        private bool characterVisible = true;
        private bool originalBodyVisible;
        private string status = "Initializing generic Humanoid archer...";

        private struct RendererState
        {
            public Renderer Renderer;
            public bool WasEnabled;
        }

        public GameObject CharacterInstance => characterInstance;
        public Animator CharacterAnimator => characterAnimator;
        public Transform BowVisual => productionBow;
        public Vector3 NockWorldPosition => GetDrawHandNockPosition();
        public Vector2 AimDirection => aimDirection;

        public void Configure(
            BowController controller,
            GameObject prefab,
            string displayName,
            Material materialOverride = null)
        {
            bowController = controller;
            characterPrefab = prefab;
            characterDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "Character"
                : displayName;
            characterMaterialOverride = materialOverride;
        }

        private void Awake()
        {
            if (bowController == null)
                bowController = GetComponentInParent<BowController>();

            gameplayCamera = Camera.main;

            if (bowController != null)
            {
                trajectoryRenderer =
                    bowController.GetComponentInChildren<AimTrajectoryRenderer>(true);
            }
        }

        private void OnEnable()
        {
            SubscribeToBow();
        }

        private void Start()
        {
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            if (bowController == null)
            {
                status = "FAIL: BowController was not found.";
                yield break;
            }

            if (characterPrefab == null)
            {
                status = "FAIL: character FBX/prefab is not assigned.";
                yield break;
            }

            SpawnCharacter();

            if (!IsValidHumanoid(characterAnimator))
            {
                status = "FAIL: " + characterDisplayName + " does not have a valid Humanoid avatar.";
                yield break;
            }

            // The production controller creates its current Archer3D presentation at runtime.
            // Wait until that setup exists so we can reuse its proven bow visual and framing.
            for (int i = 0; i < 12 && originalVisualDriver == null; i++)
            {
                yield return null;
                FindOriginalPresentation();
            }

            if (originalVisualRoot != null)
                FitCharacterToProductionVisual();
            else
                FitCharacterFallback();

            CacheBasePose();
            FindProductionBow();

            if (productionBow != null)
            {
                productionBowBaseRotation = productionBow.rotation;
                productionBowBaseScale = productionBow.localScale;
                productionBow.localScale = productionBowBaseScale * bowScaleMultiplier;
                SetProductionBowVisible(true);
            }

            if (hideOriginalCharacterBody && originalVisualRoot != null)
                SetOriginalCharacterBodyVisible(false);

            // From this point forward this adapter owns character/bow/held-arrow presentation.
            // Production gameplay logic remains enabled; only its old visual driver is paused.
            if (disableOriginalVisualDriver && originalVisualDriver != null)
                originalVisualDriver.enabled = false;

            RefreshCurrentArrow();
            initialized = true;

            status = productionBow != null
                ? characterDisplayName + " ready — generic Humanoid hands + production bow + real ArrowController."
                : characterDisplayName + " ready — bow visual not found; arrow/gameplay test still active.";
        }

        private void SpawnCharacter()
        {
            characterInstance = Instantiate(characterPrefab, bowController.transform);
            characterInstance.name = characterDisplayName + "_HUMANOID_ARCHER";
            characterInstance.transform.localPosition = Vector3.zero;
            characterInstance.transform.localRotation = Quaternion.Euler(0f, modelYawDegrees, 0f);
            characterInstance.transform.localScale = Vector3.one;

            characterAnimator = characterInstance.GetComponentInChildren<Animator>(true);
            if (characterAnimator != null)
            {
                characterAnimator.applyRootMotion = false;
                characterAnimator.runtimeAnimatorController = null;
                characterAnimator.enabled = true;
            }

            ApplyCharacterMaterialOverride();
            SetCharacterVisible(true);
        }

        private void ApplyCharacterMaterialOverride()
        {
            if (characterInstance == null || characterMaterialOverride == null)
                return;

            Renderer[] renderers = characterInstance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] current = renderer.sharedMaterials;
                if (current == null || current.Length == 0)
                {
                    renderer.sharedMaterial = characterMaterialOverride;
                    continue;
                }

                Material[] replacement = new Material[current.Length];
                for (int i = 0; i < replacement.Length; i++)
                    replacement[i] = characterMaterialOverride;

                renderer.sharedMaterials = replacement;
            }
        }

        private void FindOriginalPresentation()
        {
            if (bowController == null)
                return;

            MonoBehaviour[] behaviours = bowController.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour == this)
                    continue;

                if (characterInstance != null &&
                    behaviour.transform.IsChildOf(characterInstance.transform))
                {
                    continue;
                }

                if (behaviour.GetType().Name != "Archer3DVisualController")
                    continue;

                originalVisualDriver = behaviour;
                originalVisualRoot = behaviour.transform;
                return;
            }
        }

        private void FindProductionBow()
        {
            productionBow = null;

            Transform searchRoot = originalVisualRoot != null
                ? originalVisualRoot
                : (bowController != null ? bowController.transform : null);

            if (searchRoot == null)
                return;

            Transform[] transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            int bestScore = int.MinValue;

            foreach (Transform candidate in transforms)
            {
                if (candidate == null || candidate == searchRoot)
                    continue;

                if (bowController != null && candidate == bowController.transform)
                    continue;

                if (characterInstance != null &&
                    candidate.IsChildOf(characterInstance.transform))
                {
                    continue;
                }

                string lower = candidate.name.ToLowerInvariant();
                if (!lower.Contains("bow"))
                    continue;
                if (lower.Contains("arrow") || lower.Contains("controller"))
                    continue;

                Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                    continue;

                int score = 100;
                if (lower == "humanarcher_bow") score += 200;
                if (lower.Contains("visual")) score += 30;
                if (candidate.GetComponent<BowController>() != null) score -= 500;
                score -= GetDepthFrom(candidate, searchRoot);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                productionBow = candidate;
            }
        }

        private static int GetDepthFrom(Transform child, Transform root)
        {
            int depth = 0;
            Transform current = child;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private void FitCharacterToProductionVisual()
        {
            if (characterInstance == null || originalVisualRoot == null)
                return;

            if (!TryGetRendererBounds(originalVisualRoot.gameObject, out Bounds targetBounds) ||
                !TryGetRendererBounds(characterInstance, out Bounds characterBounds))
            {
                FitCharacterFallback();
                return;
            }

            float safeHeight = Mathf.Max(0.01f, characterBounds.size.y);
            float scale = targetBounds.size.y / safeHeight;
            scale *= Mathf.Max(0.1f, characterHeightMultiplier);
            characterInstance.transform.localScale *= scale;

            if (!TryGetRendererBounds(characterInstance, out characterBounds))
                return;

            Vector3 delta = new Vector3(
                targetBounds.center.x - characterBounds.center.x,
                targetBounds.min.y - characterBounds.min.y,
                targetBounds.center.z - characterBounds.center.z);

            characterInstance.transform.position += delta;
            MoveCharacterTowardCamera(characterCameraDepthOffset);
        }

        private void FitCharacterFallback()
        {
            if (characterInstance == null || bowController == null)
                return;

            characterInstance.transform.position = bowController.transform.position;
            if (TryGetRendererBounds(characterInstance, out Bounds bounds))
            {
                characterInstance.transform.position +=
                    Vector3.up * (bowController.transform.position.y - bounds.min.y);
            }

            MoveCharacterTowardCamera(characterCameraDepthOffset);
        }

        private void MoveCharacterTowardCamera(float amount)
        {
            if (characterInstance == null || Mathf.Approximately(amount, 0f))
                return;

            EnsureCamera();
            Vector3 towardCamera = gameplayCamera != null
                ? -gameplayCamera.transform.forward
                : Vector3.back;

            characterInstance.transform.position += towardCamera * amount;
        }

        private void CacheBasePose()
        {
            baseLocalRotations.Clear();
            if (!IsValidHumanoid(characterAnimator))
                return;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                    continue;

                Transform boneTransform = characterAnimator.GetBoneTransform(bone);
                if (boneTransform != null)
                    baseLocalRotations[bone] = boneTransform.localRotation;
            }

            // The shared adapter now supplies the procedural archery pose.
            characterAnimator.enabled = false;
        }

        private void LateUpdate()
        {
            if (!initialized || characterInstance == null || !characterVisible)
                return;

            if (!IsValidHumanoid(characterAnimator))
                return;

            RefreshCurrentArrow();
            RestoreBasePose();
            ApplyHumanoidArcheryPose();
            PositionProductionBow();
            UpdateArrowSpawnAnchor();
            UpdateHeldGameplayArrow();
            UpdateTrajectoryFromCharacter();
        }

        private void ApplyHumanoidArcheryPose()
        {
            EnsureCamera();

            Vector2 direction2D = aimDirection.sqrMagnitude > 0.0001f
                ? aimDirection.normalized
                : Vector2.right;

            Vector3 aimWorld = ScreenDirectionToWorld(direction2D);
            Vector3 screenUp = gameplayCamera != null
                ? gameplayCamera.transform.up
                : Vector3.up;
            Vector3 towardCamera = gameplayCamera != null
                ? -gameplayCamera.transform.forward
                : Vector3.back;

            Transform head = Bone(HumanBodyBones.Head);
            Transform leftUpper = Bone(HumanBodyBones.LeftUpperArm);
            Transform leftLower = Bone(HumanBodyBones.LeftLowerArm);
            Transform leftHand = Bone(HumanBodyBones.LeftHand);
            Transform rightUpper = Bone(HumanBodyBones.RightUpperArm);
            Transform rightLower = Bone(HumanBodyBones.RightLowerArm);
            Transform rightHand = Bone(HumanBodyBones.RightHand);

            if (head == null || leftUpper == null || leftLower == null || leftHand == null ||
                rightUpper == null || rightLower == null || rightHand == null)
            {
                return;
            }

            bool releasing = Time.unscaledTime < releasePoseUntil;
            bool activeArcheryPose = aiming || releasing;

            float leftReach = GetTwoBoneReach(leftUpper, leftLower, leftHand);
            float rightReach = GetTwoBoneReach(rightUpper, rightLower, rightHand);

            if (activeArcheryPose)
            {
                Vector3 bowHandTarget =
                    leftUpper.position +
                    aimWorld * (leftReach * Mathf.Clamp(bowArmExtension, 0.65f, 1.0f));

                Vector3 bowElbowPole =
                    leftUpper.position +
                    aimWorld * (leftReach * 0.55f) -
                    screenUp * (leftReach * 0.10f) +
                    towardCamera * (leftReach * 0.025f);

                SolveTwoBoneIK(
                    HumanBodyBones.LeftUpperArm,
                    HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand,
                    bowHandTarget,
                    bowElbowPole);

                Vector3 drawTarget =
                    head.position -
                    aimWorld * drawHandBehindHead +
                    screenUp * drawHandVerticalOffset +
                    towardCamera * drawHandCameraOffset;

                if (releasing && !aiming)
                    drawTarget += aimWorld * releaseFollowThrough;

                Vector3 drawElbowPole =
                    rightUpper.position -
                    aimWorld * (rightReach * Mathf.Max(0.25f, drawElbowBackFactor)) +
                    screenUp * drawElbowVerticalOffset +
                    towardCamera * drawElbowCameraOffset;

                SolveTwoBoneIK(
                    HumanBodyBones.RightUpperArm,
                    HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand,
                    drawTarget,
                    drawElbowPole);

                ApplyHeadAim(direction2D);
                return;
            }

            // Ready stance: bow arm low/forward, draw arm relaxed near the torso.
            Vector3 idleAim = ScreenDirectionToWorld(new Vector2(0.95f, -0.18f).normalized);
            Vector3 idleBowTarget =
                leftUpper.position + idleAim * (leftReach * 0.72f);

            Vector3 idleBowPole =
                leftUpper.position + idleAim * (leftReach * 0.42f) - screenUp * 0.08f;

            SolveTwoBoneIK(
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                idleBowTarget,
                idleBowPole);

            Vector3 relaxedRightTarget =
                rightUpper.position +
                ScreenDirectionToWorld(new Vector2(0.15f, -0.65f).normalized) *
                (rightReach * 0.72f) +
                towardCamera * 0.025f;

            Vector3 relaxedPole =
                rightUpper.position - screenUp * 0.10f + towardCamera * 0.08f;

            SolveTwoBoneIK(
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                relaxedRightTarget,
                relaxedPole);
        }

        private void SolveTwoBoneIK(
            HumanBodyBones upperBone,
            HumanBodyBones lowerBone,
            HumanBodyBones handBone,
            Vector3 target,
            Vector3 pole)
        {
            Transform upper = Bone(upperBone);
            Transform lower = Bone(lowerBone);
            Transform hand = Bone(handBone);

            if (upper == null || lower == null || hand == null)
                return;

            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 0.0001f || lowerLength < 0.0001f)
                return;

            Vector3 toTarget = target - upper.position;
            float requestedDistance = toTarget.magnitude;
            if (requestedDistance < 0.0001f)
                return;

            float minDistance = Mathf.Abs(upperLength - lowerLength) + 0.0005f;
            float maxDistance = (upperLength + lowerLength) * 0.995f;
            float distance = Mathf.Clamp(requestedDistance, minDistance, maxDistance);
            Vector3 direction = toTarget / requestedDistance;
            Vector3 clampedTarget = upper.position + direction * distance;

            Vector3 poleVector = pole - upper.position;
            Vector3 poleProjected = Vector3.ProjectOnPlane(poleVector, direction);

            if (poleProjected.sqrMagnitude < 0.000001f)
            {
                EnsureCamera();
                Vector3 normal = gameplayCamera != null
                    ? gameplayCamera.transform.forward
                    : Vector3.forward;
                poleProjected = Vector3.Cross(normal, direction);
            }

            poleProjected.Normalize();

            float a =
                (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
                (2f * distance);

            float hSquared = Mathf.Max(0f, upperLength * upperLength - a * a);
            float h = Mathf.Sqrt(hSquared);

            Vector3 elbowTarget =
                upper.position + direction * a + poleProjected * h;

            RotateBoneTowardChildTarget(upper, lower, elbowTarget);
            RotateBoneTowardChildTarget(lower, hand, clampedTarget);

            // One correction pass removes most residual error from retargeted rigs
            // whose bone axes are not perfectly planar.
            RotateBoneTowardChildTarget(lower, hand, clampedTarget);
        }

        private static void RotateBoneTowardChildTarget(
            Transform bone,
            Transform child,
            Vector3 childTarget)
        {
            if (bone == null || child == null)
                return;

            Vector3 current = child.position - bone.position;
            Vector3 desired = childTarget - bone.position;

            if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f)
                return;

            Quaternion delta = Quaternion.FromToRotation(current.normalized, desired.normalized);
            bone.rotation = delta * bone.rotation;
        }

        private float GetTwoBoneReach(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null)
                return 0.5f;

            return Vector3.Distance(upper.position, lower.position) +
                   Vector3.Distance(lower.position, hand.position);
        }

        private void ApplyHeadAim(Vector2 direction)
        {
            Transform head = Bone(HumanBodyBones.Head);
            if (head == null)
                return;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, -28f, 28f);

            EnsureCamera();
            Vector3 axis = gameplayCamera != null
                ? gameplayCamera.transform.forward
                : Vector3.forward;

            head.rotation = Quaternion.AngleAxis(angle * 0.18f, axis) * head.rotation;
        }

        private void PositionProductionBow()
        {
            if (productionBow == null)
                return;

            Transform bowHand = Bone(HumanBodyBones.LeftHand);
            if (bowHand == null)
                return;

            EnsureCamera();

            Vector3 screenRight = gameplayCamera != null
                ? gameplayCamera.transform.right
                : Vector3.right;
            Vector3 screenUp = gameplayCamera != null
                ? gameplayCamera.transform.up
                : Vector3.up;
            Vector3 towardCamera = gameplayCamera != null
                ? -gameplayCamera.transform.forward
                : Vector3.back;

            Vector3 position =
                bowHand.position +
                screenRight * bowGripScreenOffset.x +
                screenUp * bowGripScreenOffset.y +
                towardCamera * bowCameraDepthOffset;

            float angle = aiming || Time.unscaledTime < releasePoseUntil
                ? Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg
                : -10f;

            Vector3 rotationAxis = gameplayCamera != null
                ? gameplayCamera.transform.forward
                : Vector3.forward;

            productionBow.SetPositionAndRotation(
                position,
                Quaternion.AngleAxis(angle, rotationAxis) * productionBowBaseRotation);
        }


        private void SetProductionBowVisible(bool visible)
        {
            if (productionBow == null)
                return;

            Renderer[] renderers = productionBow.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                // The real gameplay ArrowController is our held arrow. Keep any
                // old asset arrow hidden so there is never a doubled projectile.
                if (HasNameInHierarchy(renderer.transform, productionBow, "arrow"))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = visible;
            }
        }

        private void UpdateArrowSpawnAnchor()
        {
            if (bowController == null || bowController.ArrowSpawnPoint == null)
                return;

            Vector3 nock = GetDrawHandNockPosition();
            Vector3 spawn = bowController.ArrowSpawnPoint.position;
            spawn.x = nock.x;
            spawn.y = nock.y;
            bowController.ArrowSpawnPoint.position = spawn;
        }

        private Vector3 GetDrawHandNockPosition()
        {
            Transform drawHand = Bone(HumanBodyBones.RightHand);
            if (drawHand != null)
                return drawHand.position;

            Transform head = Bone(HumanBodyBones.Head);
            if (head != null)
                return head.position;

            return bowController != null
                ? bowController.transform.position
                : transform.position;
        }

        private void RefreshCurrentArrow()
        {
            if (currentArrow != null && !currentArrow.IsStopped)
                return;

            ArrowController found = FindBestCurrentArrow();
            if (found == currentArrow)
                return;

            if (currentArrow != null)
                currentArrow.Shot -= OnProjectileShot;

            currentArrow = found;
            CacheArrowVisuals();

            if (currentArrow != null)
                currentArrow.Shot += OnProjectileShot;
        }

        private ArrowController FindBestCurrentArrow()
        {
            ArrowController[] arrows =
                UnityEngine.Object.FindObjectsByType<ArrowController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            ArrowController best = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 anchor = bowController != null
                ? bowController.transform.position
                : transform.position;

            foreach (ArrowController candidate in arrows)
            {
                if (candidate == null || candidate.IsStopped || candidate.HasFired)
                    continue;

                float distance = (candidate.transform.position - anchor).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private void CacheArrowVisuals()
        {
            if (currentArrow == null)
            {
                currentArrowRenderers = null;
                currentArrowSprite = null;
                return;
            }

            currentArrowRenderers = currentArrow.GetComponentsInChildren<Renderer>(true);
            currentArrowSprite = currentArrow.GetComponentInChildren<SpriteRenderer>(true);
        }

        private void UpdateHeldGameplayArrow()
        {
            if (currentArrow == null || currentArrow.HasFired || currentArrow.IsStopped)
                return;

            if (!aiming)
            {
                SetCurrentArrowVisible(false);
                return;
            }

            Vector2 direction = aimDirection.sqrMagnitude > 0.0001f
                ? aimDirection.normalized
                : Vector2.right;

            currentArrow.SetDirection(direction);
            AlignArrowTailToNock(currentArrow, GetDrawHandNockPosition());
            SetCurrentArrowVisible(true);
        }

        private void OnProjectileShot()
        {
            if (currentArrow == null)
                return;

            // ArrowController.Fire has already applied the exact production velocity
            // and direction. Moving only the Rigidbody root here changes launch origin,
            // not projectile behaviour. This callback occurs synchronously inside Fire,
            // before the next physics step, so there is no visible jump from the old rig.
            AlignArrowTailToNock(currentArrow, GetDrawHandNockPosition());
            SetCurrentArrowVisible(true);
            status = characterDisplayName + " shot — projectile launched from the character's real draw hand.";
        }

        private void AlignArrowTailToNock(ArrowController arrow, Vector3 nockWorld)
        {
            if (arrow == null)
                return;

            Vector3 rootPosition = arrow.transform.position;
            nockWorld.z = rootPosition.z;
            arrow.transform.position = nockWorld;

            SpriteRenderer sprite = currentArrowSprite;
            if (sprite == null || sprite.sprite == null)
                return;

            Bounds spriteBounds = sprite.sprite.bounds;
            Vector3 tailLocal = new Vector3(
                spriteBounds.min.x,
                spriteBounds.center.y,
                spriteBounds.center.z);

            Vector3 tailWorld = sprite.transform.TransformPoint(tailLocal);
            Vector3 correction = nockWorld - tailWorld;
            correction.z = 0f;
            arrow.transform.position += correction;
        }

        private void SetCurrentArrowVisible(bool visible)
        {
            if (currentArrowRenderers == null)
                return;

            foreach (Renderer renderer in currentArrowRenderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        private void UpdateTrajectoryFromCharacter()
        {
            if (!aiming || trajectoryRenderer == null || currentArrow == null)
                return;

            if (!bowController.FullTrajectoryPreviewEnabled)
                return;

            trajectoryRenderer.UpdateTrajectory(
                currentArrow.transform.position,
                aimDirection.sqrMagnitude > 0.0001f
                    ? aimDirection.normalized
                    : Vector2.right);
        }

        private void RestoreBasePose()
        {
            foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in baseLocalRotations)
            {
                Transform bone = Bone(pair.Key);
                if (bone != null)
                    bone.localRotation = pair.Value;
            }
        }

        private Transform Bone(HumanBodyBones bone)
        {
            return characterAnimator != null
                ? characterAnimator.GetBoneTransform(bone)
                : null;
        }

        private Vector3 ScreenDirectionToWorld(Vector2 direction)
        {
            EnsureCamera();

            if (gameplayCamera == null)
                return new Vector3(direction.x, direction.y, 0f).normalized;

            Vector3 world =
                gameplayCamera.transform.right * direction.x +
                gameplayCamera.transform.up * direction.y;

            return world.sqrMagnitude > 0.000001f
                ? world.normalized
                : gameplayCamera.transform.right;
        }

        private void EnsureCamera()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;
        }

        private void SubscribeToBow()
        {
            if (bowController == null)
                return;

            bowController.AimStarted -= OnAimStarted;
            bowController.AimChanged -= OnAimChanged;
            bowController.AimReleased -= OnAimReleased;
            bowController.AimCancelled -= OnAimCancelled;

            bowController.AimStarted += OnAimStarted;
            bowController.AimChanged += OnAimChanged;
            bowController.AimReleased += OnAimReleased;
            bowController.AimCancelled += OnAimCancelled;
        }

        private void UnsubscribeFromBow()
        {
            if (bowController == null)
                return;

            bowController.AimStarted -= OnAimStarted;
            bowController.AimChanged -= OnAimChanged;
            bowController.AimReleased -= OnAimReleased;
            bowController.AimCancelled -= OnAimCancelled;
        }

        private void OnAimStarted()
        {
            RefreshCurrentArrow();
            aiming = true;
            status = characterDisplayName + " drawing — bow and real arrow are attached to Humanoid hands.";
        }

        private void OnAimChanged(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
                aimDirection = direction.normalized;

            aiming = true;
        }

        private void OnAimReleased(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
                aimDirection = direction.normalized;

            aiming = false;
            releasePoseUntil = Time.unscaledTime + Mathf.Max(0.01f, releasePoseSeconds);
        }

        private void OnAimCancelled()
        {
            aiming = false;
            releasePoseUntil = 0f;
            SetCurrentArrowVisible(false);
            status = characterDisplayName + " aim cancelled.";
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame)
                SetOriginalCharacterBodyVisible(!originalBodyVisible);

            if (keyboard.f2Key.wasPressedThisFrame)
                SetCharacterVisible(!characterVisible);

            if (keyboard.tKey.wasPressedThisFrame && characterInstance != null)
            {
                modelYawDegrees += 180f;
                characterInstance.transform.Rotate(0f, 180f, 0f, Space.Self);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F1))
                SetOriginalCharacterBodyVisible(!originalBodyVisible);

            if (Input.GetKeyDown(KeyCode.F2))
                SetCharacterVisible(!characterVisible);

            if (Input.GetKeyDown(KeyCode.T) && characterInstance != null)
            {
                modelYawDegrees += 180f;
                characterInstance.transform.Rotate(0f, 180f, 0f, Space.Self);
            }
#endif
        }

        private void SetCharacterVisible(bool visible)
        {
            characterVisible = visible;
            if (characterInstance == null)
                return;

            Renderer[] renderers = characterInstance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        private void SetOriginalCharacterBodyVisible(bool visible)
        {
            originalBodyVisible = visible;
            if (originalVisualRoot == null)
                return;

            if (!visible && hiddenOriginalRenderers.Count == 0)
            {
                Renderer[] renderers = originalVisualRoot.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    // Keep the production bow, but hide any old held-arrow renderer
                    // even when it lives under the bow hierarchy. The adapter uses
                    // the real gameplay ArrowController as the held arrow.
                    bool underBow =
                        productionBow != null &&
                        renderer.transform.IsChildOf(productionBow);

                    bool looksLikeArrow = HasNameInHierarchy(
                        renderer.transform,
                        productionBow != null ? productionBow : originalVisualRoot,
                        "arrow");

                    if (underBow && !looksLikeArrow)
                        continue;

                    hiddenOriginalRenderers.Add(new RendererState
                    {
                        Renderer = renderer,
                        WasEnabled = renderer.enabled
                    });

                    renderer.enabled = false;
                }

                return;
            }

            foreach (RendererState state in hiddenOriginalRenderers)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = visible ? state.WasEnabled : false;
            }
        }

        private static bool HasNameInHierarchy(
            Transform start,
            Transform stopInclusive,
            string token)
        {
            if (start == null || string.IsNullOrEmpty(token))
                return false;

            Transform current = start;
            while (current != null)
            {
                if (current.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (current == stopInclusive)
                    break;

                current = current.parent;
            }

            return false;
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool initializedBounds = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!initializedBounds)
                {
                    bounds = renderer.bounds;
                    initializedBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initializedBounds;
        }

        private static bool IsValidHumanoid(Animator animator)
        {
            return animator != null &&
                   animator.avatar != null &&
                   animator.avatar.isValid &&
                   animator.avatar.isHuman;
        }

        private void OnGUI()
        {
            const float width = 600f;
            const float height = 145f;

            GUI.Box(new Rect(14f, 14f, width, height), GUIContent.none);
            GUI.Label(
                new Rect(28f, 24f, width - 28f, 24f),
                characterDisplayName.ToUpperInvariant() + " — GENERIC HUMANOID ARCHER TEST");
            GUI.Label(new Rect(28f, 49f, width - 28f, 22f), status);
            GUI.Label(
                new Rect(28f, 72f, width - 28f, 22f),
                "Character hands → production bow + real ArrowController → Target / LevelManager");
            GUI.Label(
                new Rect(28f, 95f, width - 28f, 22f),
                "Bow: " + (productionBow != null ? "FOUND" : "NOT FOUND") +
                "   |   Projectile: " + (currentArrow != null ? "READY" : "waiting") +
                "   |   Nock: Humanoid RightHand");
            GUI.Label(
                new Rect(28f, 118f, width - 28f, 22f),
                "F1 original body  |  F2 test character  |  T flip character 180°");
        }

        private void OnDisable()
        {
            UnsubscribeFromBow();

            if (currentArrow != null)
                currentArrow.Shot -= OnProjectileShot;

            foreach (RendererState state in hiddenOriginalRenderers)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            if (originalVisualDriver != null && disableOriginalVisualDriver)
                originalVisualDriver.enabled = true;

            if (productionBow != null)
                productionBow.localScale = productionBowBaseScale;
        }
    }
}
