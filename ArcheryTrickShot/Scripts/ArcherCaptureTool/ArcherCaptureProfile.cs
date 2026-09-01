#if UNITY_EDITOR
using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ArcherCaptureProfile",
    menuName = "Archery Trick Shot/Editor/Archer Capture Profile")]
public sealed class ArcherCaptureProfile : ScriptableObject
{
    [Serializable]
    public sealed class ClipDefinition
    {
        [Tooltip("Logical name used for the output folder/animation, e.g. Idle, Load, Hold, Release.")]
        public string Id = "Idle";

        [Tooltip("3D source animation clip to render.")]
        public AnimationClip SourceClip;

        [Tooltip("Whether the generated 2D animation should loop.")]
        public bool Loop2D;

        [Tooltip("0 = use Default FPS from this profile.")]
        [Min(0)]
        public int FpsOverride;
    }

    [Header("Source")]
    [Tooltip("Prefab containing the rig, Animator/Avatar, character mesh and bow visuals.")]
    public GameObject ArcherPrefab;

    [Tooltip("Rotate the instantiated prefab into the desired capture orientation. For our current female archer this is typically Y = 90.")]
    public Vector3 ArcherEulerRotation = new(0f, 90f, 0f);

    [Tooltip("Normally keep this at 1,1,1. Framing is handled by the capture camera.")]
    public Vector3 ArcherScale = Vector3.one;

    [Header("Clips")]
    [Tooltip("Configure the clips once here. The capture tool contains no hardcoded clip names.")]
    public ClipDefinition[] Clips = Array.Empty<ClipDefinition>();

    [Header("Capture")]
    [Min(1)] public int DefaultFps = 30;
    [Min(64)] public int FrameWidth = 1024;
    [Min(64)] public int FrameHeight = 1024;

    [Tooltip("Automatically computes one fixed camera frame that contains every configured animation. Recommended.")]
    public bool AutoFrame = true;

    [Range(1f, 2f)]
    [Tooltip("Extra space around the union of all animation poses.")]
    public float AutoFramePadding = 1.12f;

    [Tooltip("Direction of the orthographic capture camera. Keep 0,0,0 when the archer itself is rotated to a side view.")]
    public Vector3 CameraEulerRotation = Vector3.zero;

    [Min(0.1f)]
    [Tooltip("Only used when Auto Frame is disabled.")]
    public float ManualOrthographicSize = 2.5f;

    [Min(1f)]
    [Tooltip("Distance from the character. Orthographic scale is unaffected by this; it simply keeps the camera clear of the model.")]
    public float CameraDistance = 10f;

    [Header("Output")]
    [Tooltip("Must be inside Assets so Unity can import the PNGs as sprites.")]
    public string OutputRoot = "Assets/ArcheryTrickShot/Art/Sprites/Archer";

    [Min(1f)] public float PixelsPerUnit = 100f;
    public FilterMode FilterMode = FilterMode.Bilinear;

    [Header("Lighting")]
    [Range(0f, 2f)] public float KeyLightIntensity = 1.1f;
    [Range(0f, 2f)] public float FillLightIntensity = 0.45f;
}
#endif
