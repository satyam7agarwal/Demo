using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Archery Trick Shot/Game Config")]
public sealed class GameConfig : ScriptableObject
{
    private const string ResourcesPath = "GameConfig";

    [Header("Camera")]
    [Min(1f)] public float BaseOrthographicSize = 6.2f;

    [Header("Arrow")]
    [Min(0.1f)] public float ArrowSpeed = 12f;
    [Min(0f)] public float ArrowOutOfBoundsMargin = 0.12f;
    public float ArrowVisualAngleOffset = 0f;

    [Header("Aiming")]
    [Min(0.01f)] public float MinimumAimDistance = 0.35f;
    [Range(-89f, 0f)] public float MinimumAimAngle = -70f;
    [Range(0f, 89f)] public float MaximumAimAngle = 70f;
    [Min(0.1f)] public float FullDrawDistance = 3f;
    [Tooltip("Minimum pointer movement required before a press becomes a drag-to-aim gesture, expressed as a fraction of the shorter screen dimension.")]
    [Range(0.005f, 0.1f)] public float MinimumDragScreenFraction = 0.02f;

    [Header("Trajectory")]
    [Range(6, 40)] public int TrajectoryDotCount = 18;
    [Min(0f)] public float TrajectoryStartOffset = 0.5f;
    [Min(0.05f)] public float TrajectorySpacing = 0.42f;
    [Min(0.01f)] public float TrajectoryDotScale = 0.15f;
    [Range(0f, 1f)] public float TrajectoryStartAlpha = 1f;
    [Range(0f, 1f)] public float TrajectoryEndAlpha = 0.60f;
    public Color TrajectoryColor = new Color(0.20f, 1f, 0.05f, 1f);

    [Header("Full Trajectory Assist")]
    [Range(24, 160)] public int FullTrajectoryDotCount = 96;
    [Range(1, 12)] public int FullTrajectoryMaxBounces = 8;
    [Min(5f)] public float FullTrajectoryMaxDistance = 50f;
    [Min(0.001f)] public float FullTrajectorySurfaceOffset = 0.025f;

    [Header("Audio")]
    [Range(0f, 1f)] public float MusicVolume = 0.28f;

    [Header("Shot Timing")]
    [Min(0f)] public float HitResultDelay = 0.32f;
    [Min(0f)] public float MissFeedbackDelay = 0.30f;
    [Min(0f)] public float NextArrowDelay = 0.08f;

    [Header("Game Feel")]
    [Min(0f)] public float HitShakeDuration = 0.11f;
    [Min(0f)] public float HitShakeMagnitude = 0.055f;
    [Min(0f)] public float MissShakeDuration = 0.07f;
    [Min(0f)] public float MissShakeMagnitude = 0.025f;
    [Min(0f)] public float RicochetShakeDuration = 0.055f;
    [Min(0f)] public float RicochetShakeMagnitude = 0.012f;
    [Range(0.95f, 1f)] public float HitCameraZoomFactor = 0.985f;
    [Min(0.05f)] public float HitCameraZoomDuration = 0.16f;
    [Range(1f, 1.15f)] public float TargetHitPulseScale = 1.035f;
    [Min(0.05f)] public float TargetHitPulseDuration = 0.18f;
    [Range(1f, 1.15f)] public float MirrorPulseScale = 1.055f;
    [Min(0.05f)] public float MirrorPulseDuration = 0.12f;

    [Header("Arrow Trail")]
    [Range(0.03f, 0.30f)] public float ArrowTrailTime = 0.11f;
    [Range(0.005f, 0.08f)] public float ArrowTrailWidth = 0.028f;
    [Range(0f, 1f)] public float ArrowTrailStartAlpha = 0.55f;

    [Header("Performance")]
    [Min(0f)] public float TrajectoryMinPositionDelta = 0.006f;
    [Range(0f, 2f)] public float TrajectoryMinAngleDelta = 0.12f;

    [Header("UI Layout")]
    public Vector2 UIReferenceResolution = new Vector2(1920f, 1080f);
    [Range(0f, 1f)] public float UIMatchWidthOrHeight = 0.5f;
    [Min(0.05f)] public float UIFadeDuration = 0.14f;
    [Min(0.05f)] public float UICardPopDuration = 0.22f;
    [Min(0.1f)] public float UIFeedbackDuration = 0.52f;

    [Header("Palette")]
    public Color BackgroundTop = new Color(0.06f, 0.035f, 0.15f, 1f);
    public Color BackgroundBottom = new Color(0.13f, 0.08f, 0.30f, 1f);
    public Color PanelColor = new Color(0.12f, 0.07f, 0.29f, 0.96f);
    public Color PanelBorderColor = new Color(0.42f, 0.20f, 0.86f, 0.85f);
    public Color LimeColor = new Color(0.43f, 1f, 0.0f, 1f);
    public Color YellowColor = new Color(1f, 0.86f, 0.03f, 1f);
    public Color PinkColor = new Color(1f, 0.24f, 0.50f, 1f);
    public Color PrimaryTextColor = Color.white;
    public Color SecondaryTextColor = new Color(0.72f, 0.69f, 0.84f, 1f);

    [Header("Mobile")]
    [Range(30, 120)] public int MobileTargetFrameRate = 60;

    private static GameConfig cached;

    public static GameConfig Load()
    {
        if (cached != null)
            return cached;

        cached = Resources.Load<GameConfig>(ResourcesPath);

        if (cached != null)
            return cached;

        cached = CreateInstance<GameConfig>();
        cached.name = "RuntimeGameConfig";
        cached.hideFlags = HideFlags.DontSave;
        Debug.LogWarning(
            "GameConfig.asset was not found in Resources. Runtime defaults are being used.");
        return cached;
    }

    private void OnValidate()
    {
        MaximumAimAngle = Mathf.Max(0f, MaximumAimAngle);
        MinimumAimAngle = Mathf.Min(0f, MinimumAimAngle);
        MinimumDragScreenFraction = Mathf.Clamp(MinimumDragScreenFraction, 0.005f, 0.1f);
        TrajectoryEndAlpha = Mathf.Min(TrajectoryStartAlpha, TrajectoryEndAlpha);
        FullTrajectoryDotCount = Mathf.Max(TrajectoryDotCount, FullTrajectoryDotCount);
        FullTrajectoryMaxBounces = Mathf.Max(1, FullTrajectoryMaxBounces);
        FullTrajectoryMaxDistance = Mathf.Max(5f, FullTrajectoryMaxDistance);
        FullTrajectorySurfaceOffset = Mathf.Max(0.001f, FullTrajectorySurfaceOffset);
        MusicVolume = Mathf.Clamp01(MusicVolume);
        HitCameraZoomFactor = Mathf.Clamp(HitCameraZoomFactor, 0.95f, 1f);
        TargetHitPulseScale = Mathf.Max(1f, TargetHitPulseScale);
        MirrorPulseScale = Mathf.Max(1f, MirrorPulseScale);
        ArrowTrailTime = Mathf.Max(0.03f, ArrowTrailTime);
        ArrowTrailWidth = Mathf.Max(0.005f, ArrowTrailWidth);
        ArrowTrailStartAlpha = Mathf.Clamp01(ArrowTrailStartAlpha);
        TrajectoryMinPositionDelta = Mathf.Max(0f, TrajectoryMinPositionDelta);
        TrajectoryMinAngleDelta = Mathf.Clamp(TrajectoryMinAngleDelta, 0f, 2f);
    }
}
