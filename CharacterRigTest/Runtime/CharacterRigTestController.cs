using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ArcheryTrickShot.CharacterTesting
{
    /// <summary>
    /// Standalone humanoid-rig test harness. It intentionally does not depend on
    /// the production archery gameplay scripts so it can be dropped into the
    /// project without changing existing levels or prefabs.
    /// </summary>
    public sealed class CharacterRigTestController : MonoBehaviour
    {
        private enum PoseMode
        {
            Neutral,
            ArmsOut,
            ElbowStress,
            Archery,
            AutoStress
        }

        [Header("Scene References")]
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Camera testCamera;

        [Header("Characters (auto-filled by the editor scene builder)")]
        [SerializeField] private List<GameObject> characterPrefabs = new List<GameObject>();

        [Header("Camera")]
        [SerializeField] private float cameraPadding = 1.35f;
        [SerializeField] private float minimumCameraDistance = 2.5f;

        private GameObject currentInstance;
        private Animator currentAnimator;
        private int currentIndex;
        private PoseMode poseMode = PoseMode.Neutral;
        private string diagnosticText = "No character loaded.";
        private float yawDegrees;

        private readonly Dictionary<HumanBodyBones, Quaternion> baseLocalRotations =
            new Dictionary<HumanBodyBones, Quaternion>();

        private static readonly HumanBodyBones[] RequiredBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot
        };

        public void SetSceneReferences(Transform root, Camera camera)
        {
            characterRoot = root;
            testCamera = camera;
        }

        public void SetCharacterPrefabs(List<GameObject> prefabs)
        {
            characterPrefabs = prefabs ?? new List<GameObject>();
        }

        private void Start()
        {
            if (characterRoot == null)
            {
                var root = new GameObject("CharacterRoot");
                characterRoot = root.transform;
            }

            if (testCamera == null)
                testCamera = Camera.main;

            LoadCharacter(0);
        }

        private void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            HandleKeyboard();
#endif

            if (currentAnimator == null || !IsValidHumanoid(currentAnimator))
                return;

            if (poseMode == PoseMode.AutoStress)
            {
                float phase = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
                RestoreBasePose();
                ApplyArmsOutPose(Mathf.Lerp(0.15f, 1f, phase));
                ApplyElbowBend(Mathf.Lerp(0f, 0.9f, 1f - phase));
                return;
            }

            RestoreBasePose();

            switch (poseMode)
            {
                case PoseMode.ArmsOut:
                    ApplyArmsOutPose(1f);
                    break;
                case PoseMode.ElbowStress:
                    ApplyArmsOutPose(0.65f);
                    ApplyElbowBend(1f);
                    break;
                case PoseMode.Archery:
                    ApplyArcheryPose();
                    break;
            }
        }

        private void HandleKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.leftArrowKey.wasPressedThisFrame) PreviousCharacter();
            if (keyboard.rightArrowKey.wasPressedThisFrame) NextCharacter();
            if (keyboard.digit1Key.wasPressedThisFrame) SetPose(PoseMode.Neutral);
            if (keyboard.digit2Key.wasPressedThisFrame) SetPose(PoseMode.ArmsOut);
            if (keyboard.digit3Key.wasPressedThisFrame) SetPose(PoseMode.ElbowStress);
            if (keyboard.digit4Key.wasPressedThisFrame) SetPose(PoseMode.Archery);
            if (keyboard.spaceKey.wasPressedThisFrame) SetPose(PoseMode.AutoStress);
            if (keyboard.qKey.wasPressedThisFrame) RotateCharacter(-15f);
            if (keyboard.eKey.wasPressedThisFrame) RotateCharacter(15f);
            if (keyboard.fKey.wasPressedThisFrame) FitCamera();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.LeftArrow)) PreviousCharacter();
            if (Input.GetKeyDown(KeyCode.RightArrow)) NextCharacter();
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetPose(PoseMode.Neutral);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetPose(PoseMode.ArmsOut);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetPose(PoseMode.ElbowStress);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetPose(PoseMode.Archery);
            if (Input.GetKeyDown(KeyCode.Space)) SetPose(PoseMode.AutoStress);
            if (Input.GetKeyDown(KeyCode.Q)) RotateCharacter(-15f);
            if (Input.GetKeyDown(KeyCode.E)) RotateCharacter(15f);
            if (Input.GetKeyDown(KeyCode.F)) FitCamera();
#endif
        }

        private void LoadCharacter(int index)
        {
            if (characterPrefabs == null || characterPrefabs.Count == 0)
            {
                diagnosticText =
                    "No characters found. Put rigged FBX/prefabs in:\n" +
                    "Assets/CharacterRigTest/Characters\n\n" +
                    "Then use Tools > Archery Trick Shot > Character Rig Test > Create / Refresh Test Scene.";
                return;
            }

            currentIndex = Mathf.Clamp(index, 0, characterPrefabs.Count - 1);

            if (currentInstance != null)
                Destroy(currentInstance);

            GameObject prefab = characterPrefabs[currentIndex];
            if (prefab == null)
            {
                diagnosticText = "Character reference is missing.";
                return;
            }

            currentInstance = Instantiate(prefab, characterRoot);
            currentInstance.name = prefab.name + "_TEST";
            currentInstance.transform.localPosition = Vector3.zero;
            currentInstance.transform.localRotation = Quaternion.identity;
            yawDegrees = 0f;
            characterRoot.localRotation = Quaternion.identity;

            currentAnimator = currentInstance.GetComponentInChildren<Animator>();
            poseMode = PoseMode.Neutral;

            AlignFeetToGround();
            CaptureBasePose();
            UpdateDiagnostics();
            FitCamera();
        }

        private void NextCharacter()
        {
            if (characterPrefabs == null || characterPrefabs.Count == 0) return;
            int next = (currentIndex + 1) % characterPrefabs.Count;
            LoadCharacter(next);
        }

        private void PreviousCharacter()
        {
            if (characterPrefabs == null || characterPrefabs.Count == 0) return;
            int previous = (currentIndex - 1 + characterPrefabs.Count) % characterPrefabs.Count;
            LoadCharacter(previous);
        }

        private void SetPose(PoseMode mode)
        {
            poseMode = mode;
            if (mode == PoseMode.Neutral)
                RestoreBasePose();
        }

        private void RotateCharacter(float degrees)
        {
            yawDegrees += degrees;
            if (characterRoot != null)
                characterRoot.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        private void CaptureBasePose()
        {
            baseLocalRotations.Clear();

            if (!IsValidHumanoid(currentAnimator))
                return;

            foreach (HumanBodyBones bone in RequiredBones)
            {
                Transform t = currentAnimator.GetBoneTransform(bone);
                if (t != null)
                    baseLocalRotations[bone] = t.localRotation;
            }
        }

        private void RestoreBasePose()
        {
            if (!IsValidHumanoid(currentAnimator))
                return;

            foreach (var pair in baseLocalRotations)
            {
                Transform t = currentAnimator.GetBoneTransform(pair.Key);
                if (t != null)
                    t.localRotation = pair.Value;
            }
        }

        private void ApplyArmsOutPose(float amount)
        {
            if (!IsValidHumanoid(currentAnimator)) return;

            Vector3 up = characterRoot.up;
            Vector3 right = characterRoot.right;

            Vector3 leftDirection = Vector3.Lerp(GetBoneDirection(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
                (-right + up * 0.08f).normalized, amount);
            Vector3 rightDirection = Vector3.Lerp(GetBoneDirection(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
                (right + up * 0.08f).normalized, amount);

            AlignBoneToDirection(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, leftDirection);
            AlignBoneToDirection(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, rightDirection);

            AlignBoneToDirection(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, leftDirection);
            AlignBoneToDirection(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightDirection);
        }

        private void ApplyElbowBend(float amount)
        {
            if (!IsValidHumanoid(currentAnimator)) return;

            Vector3 forward = characterRoot.forward;
            Vector3 right = characterRoot.right;
            Vector3 up = characterRoot.up;

            Vector3 leftDesired = (-right * 0.15f + forward * 0.9f + up * 0.15f).normalized;
            Vector3 rightDesired = (right * 0.15f + forward * 0.9f + up * 0.15f).normalized;

            Vector3 leftCurrent = GetBoneDirection(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            Vector3 rightCurrent = GetBoneDirection(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);

            AlignBoneToDirection(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                Vector3.Slerp(leftCurrent, leftDesired, amount));
            AlignBoneToDirection(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                Vector3.Slerp(rightCurrent, rightDesired, amount));
        }

        private void ApplyArcheryPose()
        {
            if (!IsValidHumanoid(currentAnimator)) return;

            Vector3 forward = characterRoot.forward;
            Vector3 right = characterRoot.right;
            Vector3 up = characterRoot.up;

            // Bow arm: straight and forward.
            Vector3 bowDirection = (forward * 0.96f - right * 0.12f + up * 0.06f).normalized;
            AlignBoneToDirection(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, bowDirection);
            AlignBoneToDirection(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, bowDirection);

            // Draw arm: elbow out, hand pulled toward the face.
            Vector3 drawUpper = (right * 0.82f - forward * 0.42f + up * 0.22f).normalized;
            AlignBoneToDirection(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, drawUpper);

            Transform lower = currentAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform head = currentAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (lower != null && head != null)
            {
                Vector3 towardFace = (head.position - lower.position + right * 0.08f).normalized;
                AlignBoneToDirection(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, towardFace);
            }
        }

        private Vector3 GetBoneDirection(HumanBodyBones bone, HumanBodyBones child)
        {
            if (!IsValidHumanoid(currentAnimator)) return Vector3.right;

            Transform a = currentAnimator.GetBoneTransform(bone);
            Transform b = currentAnimator.GetBoneTransform(child);
            if (a == null || b == null) return Vector3.right;

            Vector3 direction = b.position - a.position;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.right;
        }

        private void AlignBoneToDirection(HumanBodyBones bone, HumanBodyBones child, Vector3 desiredWorldDirection)
        {
            Transform a = currentAnimator.GetBoneTransform(bone);
            Transform b = currentAnimator.GetBoneTransform(child);
            if (a == null || b == null) return;

            Vector3 currentDirection = b.position - a.position;
            if (currentDirection.sqrMagnitude < 0.000001f || desiredWorldDirection.sqrMagnitude < 0.000001f)
                return;

            Quaternion delta = Quaternion.FromToRotation(currentDirection.normalized, desiredWorldDirection.normalized);
            a.rotation = delta * a.rotation;
        }

        private void AlignFeetToGround()
        {
            if (currentInstance == null) return;

            Bounds bounds;
            if (!TryGetRendererBounds(currentInstance, out bounds))
                return;

            float delta = -bounds.min.y;
            currentInstance.transform.position += Vector3.up * delta;
        }

        private void FitCamera()
        {
            if (testCamera == null || currentInstance == null) return;

            Bounds bounds;
            if (!TryGetRendererBounds(currentInstance, out bounds)) return;

            float height = Mathf.Max(0.5f, bounds.size.y);
            float width = Mathf.Max(0.5f, bounds.size.x);
            float largest = Mathf.Max(height, width);
            float verticalFov = testCamera.fieldOfView * Mathf.Deg2Rad;
            float distance = (largest * 0.5f * cameraPadding) / Mathf.Tan(verticalFov * 0.5f);
            distance = Mathf.Max(minimumCameraDistance, distance);

            Vector3 center = bounds.center;
            Vector3 cameraPosition = center + characterRoot.forward * distance + Vector3.up * height * 0.03f;
            testCamera.transform.position = cameraPosition;
            testCamera.transform.LookAt(center + Vector3.up * height * 0.02f, Vector3.up);
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.transform.position, Vector3.one);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return true;
        }

        private void UpdateDiagnostics()
        {
            if (currentInstance == null)
            {
                diagnosticText = "No character instantiated.";
                return;
            }

            var lines = new List<string>();
            lines.Add("Character: " + currentInstance.name.Replace("_TEST", string.Empty));

            if (currentAnimator == null)
            {
                lines.Add("FAIL: No Animator component found.");
                lines.Add("Set the FBX Rig > Animation Type to Humanoid.");
                diagnosticText = string.Join("\n", lines);
                return;
            }

            lines.Add("Animator: found");
            lines.Add("Avatar: " + (currentAnimator.avatar != null ? "found" : "MISSING"));
            lines.Add("Humanoid: " + (IsValidHumanoid(currentAnimator) ? "YES" : "NO"));

            if (IsValidHumanoid(currentAnimator))
            {
                var missing = new List<string>();
                foreach (HumanBodyBones bone in RequiredBones)
                {
                    if (currentAnimator.GetBoneTransform(bone) == null)
                        missing.Add(bone.ToString());
                }

                lines.Add(missing.Count == 0
                    ? "Critical bones: PASS"
                    : "Missing bones: " + string.Join(", ", missing));
            }

            Bounds bounds;
            if (TryGetRendererBounds(currentInstance, out bounds))
                lines.Add("Rendered height: " + bounds.size.y.ToString("0.00") + " Unity units");

            lines.Add("");
            lines.Add("Inspect: shoulders, elbows, wrists, knees, feet, cloth, tail/wings.");
            diagnosticText = string.Join("\n", lines);
        }

        private static bool IsValidHumanoid(Animator animator)
        {
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        private void OnGUI()
        {
            const float panelWidth = 390f;
            GUILayout.BeginArea(new Rect(18f, 18f, panelWidth, Screen.height - 36f), GUI.skin.box);

            GUILayout.Label("ARCHERY TRICK SHOT - CHARACTER RIG TEST");
            GUILayout.Space(6f);
            GUILayout.Label(diagnosticText);
            GUILayout.Space(12f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Previous", GUILayout.Height(32f))) PreviousCharacter();
            if (GUILayout.Button("Next >", GUILayout.Height(32f))) NextCharacter();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Rig poses");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1 Neutral")) SetPose(PoseMode.Neutral);
            if (GUILayout.Button("2 Arms Out")) SetPose(PoseMode.ArmsOut);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("3 Elbows")) SetPose(PoseMode.ElbowStress);
            if (GUILayout.Button("4 Archery")) SetPose(PoseMode.Archery);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("SPACE Auto Stress", GUILayout.Height(30f))) SetPose(PoseMode.AutoStress);

            GUILayout.Space(8f);
            GUILayout.Label("View");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Q Rotate -15")) RotateCharacter(-15f);
            if (GUILayout.Button("E Rotate +15")) RotateCharacter(15f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("F Fit Camera")) FitCamera();

            GUILayout.Space(12f);
            GUILayout.Label("PASS CHECK");
            GUILayout.Label("- shoulders do not collapse");
            GUILayout.Label("- elbows bend cleanly");
            GUILayout.Label("- wrists/hands do not twist");
            GUILayout.Label("- legs/feet remain stable");
            GUILayout.Label("- clothing does not stretch badly");
            GUILayout.Label("- tail/wings do not corrupt body mesh");

            GUILayout.FlexibleSpace();
            GUILayout.Label("This scene does not modify production gameplay.");
            GUILayout.EndArea();
        }
    }
}
