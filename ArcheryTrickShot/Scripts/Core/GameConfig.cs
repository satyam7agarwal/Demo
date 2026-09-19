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

    [Header("Ricochet Combo Fever")]
    [Tooltip("Style bonus awarded on a successful shot after touching 1 unique mirror.")]
    [Min(0)] public int RicochetStyleBonus1 = 50;

    [Tooltip("Style bonus awarded on a successful shot after touching 2 unique mirrors.")]
    [Min(0)] public int RicochetStyleBonus2 = 125;

    [Tooltip("Style bonus awarded on a successful shot after touching 3 unique mirrors.")]
    [Min(0)] public int RicochetStyleBonus3 = 250;

    [Tooltip("Maximum style bonus awarded after touching 4 or more unique mirrors.")]
    [Min(0)] public int RicochetStyleBonus4 = 400;

    [Tooltip(
        "Only unique mirrors increase the style score. Re-bouncing off the same mirror " +
        "still advances combo feedback but cannot farm score.")]
    [Range(1, 8)] public int RicochetMaxRewardedUniqueMirrors = 4;

    [Tooltip("Extra ricochet SFX pitch added per combo tier.")]
    [Range(0f, 0.15f)] public float RicochetAudioPitchStep = 0.075f;

    [Tooltip("Extra ricochet SFX volume added per combo tier.")]
    [Range(0f, 0.20f)] public float RicochetAudioVolumeStep = 0.075f;

    [Tooltip("Camera-shake growth per ricochet combo tier. Kept deliberately restrained.")]
    [Range(0f, 0.35f)] public float RicochetShakeGrowthPerTier = 0.18f;

    [Tooltip("How long the ricochet combo meter stays visible after each bounce.")]
    [Range(0.15f, 0.80f)] public float RicochetComboUiHoldDuration = 0.30f;

    [Header("Final Shot Cinematic")]
    [Tooltip(
        "Only starts when the fired arrow's nearest upcoming collision is a valid target scoring face. " +
        "Misses, walls, invalid target contacts and pre-ricochet segments stay at normal speed.")]
    public bool FinalShotCinematicEnabled = true;

    [Tooltip("World-space distance from the scoring face at which the final approach begins.")]
    [Range(0.5f, 4f)] public float FinalApproachTriggerDistance = 2.85f;

    [Tooltip("Slow-motion scale for a confirmed direct final approach.")]
    [Range(0.30f, 1f)] public float FinalApproachTimeScale = 0.68f;

    [Tooltip("Slow motion for a direct approach predicted to land in the bullseye.")]
    [Range(0.25f, 1f)] public float BullseyeApproachTimeScale = 0.50f;

    [Tooltip("Deeper slow motion after at least one ricochet.")]
    [Range(0.25f, 1f)] public float TrickShotApproachTimeScale = 0.48f;

    [Tooltip("Strongest slow motion for a ricochet shot predicted to land in the bullseye.")]
    [Range(0.20f, 1f)] public float BullseyeTrickShotApproachTimeScale = 0.40f;

    [Tooltip("Orthographic zoom used during the final approach. Lower means closer.")]
    [Range(0.92f, 1f)] public float FinalApproachZoomFactor = 0.935f;

    [Tooltip("How much the camera drifts toward the predicted impact point on direct shots.")]
    [Range(0f, 0.30f)] public float FinalApproachCameraFocusStrength = 0.23f;

    [Tooltip("Slightly stronger target focus after a ricochet.")]
    [Range(0f, 0.35f)] public float TrickShotCameraFocusStrength = 0.29f;

    [Tooltip("Maximum camera translation during final-shot focus, in world units.")]
    [Range(0f, 1.5f)] public float FinalApproachMaxCameraShift = 1.05f;

    [Min(0.01f)] public float FinalApproachEaseInDuration = 0.22f;

    [Tooltip("Real-time impact freeze for normal scoring hits.")]
    [Range(0f, 0.12f)] public float FinalImpactFreezeDuration = 0.050f;

    [Tooltip("Slightly longer real-time impact freeze for a bullseye.")]
    [Range(0f, 0.15f)] public float BullseyeImpactFreezeDuration = 0.070f;

    [Tooltip("Camera zoom at the exact impact frame for normal hits.")]
    [Range(0.90f, 1f)] public float FinalImpactZoomFactor = 0.915f;

    [Tooltip("Camera zoom at the exact impact frame for bullseyes.")]
    [Range(0.90f, 1f)] public float BullseyeImpactZoomFactor = 0.905f;

    [Tooltip("How strongly the camera settles on the impact point during the hit-stop.")]
    [Range(0f, 0.40f)] public float FinalImpactCameraFocusStrength = 0.34f;

    [Tooltip("Brief real-time hold after hit-stop before the camera begins returning.")]
    [Range(0f, 0.35f)] public float FinalImpactHoldDuration = 0.20f;

    [Tooltip("Bullseyes get a slightly longer hold before the camera returns.")]
    [Range(0f, 0.40f)] public float BullseyeImpactHoldDuration = 0.26f;

    [Min(0.05f)] public float FinalImpactCameraReturnDuration = 0.32f;

    [Header("Final Shot Result Timing")]
    [Min(0.2f)] public float CinematicHitResultDelay = 0.82f;
    [Min(0.2f)] public float CinematicBullseyeResultDelay = 1.02f;
    [Min(0.2f)] public float CinematicTrickShotResultDelay = 0.94f;
    [Min(0.2f)] public float CinematicBullseyeTrickShotResultDelay = 1.12f;

    [Header("Final Shot Trail")]
    [Range(1f, 8f)] public float FinalApproachTrailWidthMultiplier = 5.25f;
    [Range(0.5f, 2f)] public float FinalApproachTrailAlphaMultiplier = 1.75f;
    [Range(1f, 2f)] public float FinalApproachTrailTimeMultiplier = 1.45f;

    [Tooltip(
        "Fixed world-space length of the luminous winning streak behind the actual arrow. " +
        "Keeping this independent of frame sampling makes it stable on mobile.")]
    [Range(0.60f, 1.30f)]
    public float FinalApproachWinningStreakLength = 1.10f;

    [Tooltip(
        "Small overlap into the physical arrow tail so there is no visible gap, " +
        "while keeping the streak clearly behind the arrow artwork.")]
    [Range(0f, 0.10f)]
    public float FinalApproachWinningStreakOverlap = 0.035f;

    [Tooltip("Width multiplier for the bright white-gold core inside the final winning trail.")]
    [Range(0.8f, 3f)] public float FinalApproachCoreTrailWidthMultiplier = 1.95f;

    [Tooltip(
        "Wide dark under-ribbon used only to keep the winning streak readable " +
        "against bright sand/sky backgrounds.")]
    [Range(2f, 12f)]
    public float FinalApproachContrastTrailWidthMultiplier = 8.0f;

    [Tooltip("Opacity of the dark contrast ribbon behind the gold streak.")]
    [Range(0f, 0.60f)]
    public float FinalApproachContrastTrailAlpha = 0.30f;

    [Tooltip("Core-trail lifetime relative to the configured outer comet tail.")]
    [Range(0.4f, 0.8f)] public float FinalApproachCoreTrailTimeRatio = 0.54f;

    [Tooltip("Scale of the soft silhouette glow around the arrow during the confirmed winning segment.")]
    [Range(1f, 1.25f)] public float FinalApproachArrowGlowScale = 1.09f;

    [Tooltip("Opacity of the soft gold arrow glow during the confirmed winning segment.")]
    [Range(0f, 0.60f)] public float FinalApproachArrowGlowAlpha = 0.03f;

    [Header("Final Shot Audio")]
    [Tooltip("Background-music volume multiplier while the arrow is on a confirmed final approach.")]
    [Range(0.20f, 1f)] public float FinalApproachMusicDuckMultiplier = 0.48f;

    [Tooltip("Music pitch during the final approach. Kept subtle to avoid sounding distorted.")]
    [Range(0.85f, 1f)] public float FinalApproachMusicPitch = 0.965f;

    [Min(0.2f)] public float FinalApproachMusicDuckDuration = 0.90f;

    [Header("Final Shot Impact VFX")]
    [Range(6, 40)] public int FinalImpactParticleCount = 10;
    [Range(8, 48)] public int BullseyeImpactParticleCount = 14;
    [Range(0.5f, 6f)] public float FinalImpactParticleSpeed = 2.5f;
    [Range(0.15f, 1.5f)] public float FinalImpactShockwaveRadius = 0.56f;
    [Range(0.20f, 1.8f)] public float BullseyeImpactShockwaveRadius = 0.74f;

    [Tooltip("Very short localized white flash at the exact arrow-tip contact.")]
    [Range(0.02f, 0.10f)] public float FinalImpactFlashDuration = 0.050f;

    [Tooltip("World-space length of each directional impact spark.")]
    [Range(0.04f, 0.30f)] public float FinalImpactSparkLength = 0.13f;

    [Header("Target Impact Recoil")]
    [Range(0f, 0.10f)] public float TargetImpactRecoilDistance = 0.050f;
    [Range(0f, 0.14f)] public float BullseyeTargetImpactRecoilDistance = 0.070f;
    [Range(0.08f, 0.40f)] public float TargetImpactRecoilDuration = 0.26f;

    [Tooltip("Maximum small Z-axis rocking caused by an off-centre normal hit.")]
    [Range(0f, 4f)] public float TargetImpactRecoilRotation = 1.8f;

    [Tooltip("Maximum small Z-axis rocking caused by an off-centre bullseye/inner hit.")]
    [Range(0f, 5f)] public float BullseyeTargetImpactRecoilRotation = 2.4f;

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
        RicochetStyleBonus1 = Mathf.Max(0, RicochetStyleBonus1);
        RicochetStyleBonus2 = Mathf.Max(RicochetStyleBonus1, RicochetStyleBonus2);
        RicochetStyleBonus3 = Mathf.Max(RicochetStyleBonus2, RicochetStyleBonus3);
        RicochetStyleBonus4 = Mathf.Max(RicochetStyleBonus3, RicochetStyleBonus4);
        RicochetMaxRewardedUniqueMirrors = Mathf.Clamp(RicochetMaxRewardedUniqueMirrors, 1, 8);
        RicochetAudioPitchStep = Mathf.Clamp(RicochetAudioPitchStep, 0f, 0.15f);
        RicochetAudioVolumeStep = Mathf.Clamp(RicochetAudioVolumeStep, 0f, 0.20f);
        RicochetShakeGrowthPerTier = Mathf.Clamp(RicochetShakeGrowthPerTier, 0f, 0.35f);
        RicochetComboUiHoldDuration = Mathf.Clamp(RicochetComboUiHoldDuration, 0.15f, 0.80f);
        FinalApproachTriggerDistance = Mathf.Max(0.5f, FinalApproachTriggerDistance);
        FinalApproachTimeScale = Mathf.Clamp(FinalApproachTimeScale, 0.30f, 1f);
        BullseyeApproachTimeScale = Mathf.Clamp(BullseyeApproachTimeScale, 0.25f, 1f);
        TrickShotApproachTimeScale = Mathf.Clamp(TrickShotApproachTimeScale, 0.25f, 1f);
        BullseyeTrickShotApproachTimeScale = Mathf.Clamp(BullseyeTrickShotApproachTimeScale, 0.20f, 1f);
        FinalApproachZoomFactor = Mathf.Clamp(FinalApproachZoomFactor, 0.92f, 1f);
        FinalApproachCameraFocusStrength = Mathf.Clamp(FinalApproachCameraFocusStrength, 0f, 0.30f);
        TrickShotCameraFocusStrength = Mathf.Clamp(TrickShotCameraFocusStrength, 0f, 0.35f);
        FinalApproachMaxCameraShift = Mathf.Max(0f, FinalApproachMaxCameraShift);
        FinalApproachEaseInDuration = Mathf.Max(0.01f, FinalApproachEaseInDuration);
        FinalImpactFreezeDuration = Mathf.Max(0f, FinalImpactFreezeDuration);
        BullseyeImpactFreezeDuration = Mathf.Max(FinalImpactFreezeDuration, BullseyeImpactFreezeDuration);
        FinalImpactZoomFactor = Mathf.Clamp(FinalImpactZoomFactor, 0.90f, 1f);
        BullseyeImpactZoomFactor = Mathf.Clamp(BullseyeImpactZoomFactor, 0.90f, FinalImpactZoomFactor);
        FinalImpactCameraFocusStrength = Mathf.Clamp(FinalImpactCameraFocusStrength, 0f, 0.40f);
        FinalImpactHoldDuration = Mathf.Max(0f, FinalImpactHoldDuration);
        BullseyeImpactHoldDuration = Mathf.Max(FinalImpactHoldDuration, BullseyeImpactHoldDuration);
        FinalImpactCameraReturnDuration = Mathf.Max(0.05f, FinalImpactCameraReturnDuration);
        CinematicHitResultDelay = Mathf.Max(0.2f, CinematicHitResultDelay);
        CinematicBullseyeResultDelay = Mathf.Max(CinematicHitResultDelay, CinematicBullseyeResultDelay);
        CinematicTrickShotResultDelay = Mathf.Max(CinematicHitResultDelay, CinematicTrickShotResultDelay);
        CinematicBullseyeTrickShotResultDelay = Mathf.Max(
            Mathf.Max(CinematicBullseyeResultDelay, CinematicTrickShotResultDelay),
            CinematicBullseyeTrickShotResultDelay);
        FinalApproachTrailWidthMultiplier = Mathf.Clamp(FinalApproachTrailWidthMultiplier, 1f, 8f);
        FinalApproachTrailAlphaMultiplier = Mathf.Clamp(FinalApproachTrailAlphaMultiplier, 0.5f, 2f);
        FinalApproachTrailTimeMultiplier = Mathf.Max(1f, FinalApproachTrailTimeMultiplier);
        FinalApproachWinningStreakLength = Mathf.Clamp(
            FinalApproachWinningStreakLength,
            0.60f,
            1.30f);
        FinalApproachWinningStreakOverlap = Mathf.Clamp(
            FinalApproachWinningStreakOverlap,
            0f,
            0.10f);
        FinalApproachCoreTrailWidthMultiplier = Mathf.Clamp(
            FinalApproachCoreTrailWidthMultiplier,
            0.8f,
            3f);
        FinalApproachContrastTrailWidthMultiplier = Mathf.Clamp(
            FinalApproachContrastTrailWidthMultiplier,
            2f,
            12f);
        FinalApproachContrastTrailAlpha = Mathf.Clamp(
            FinalApproachContrastTrailAlpha,
            0f,
            0.60f);
        FinalApproachCoreTrailTimeRatio = Mathf.Clamp(
            FinalApproachCoreTrailTimeRatio,
            0.4f,
            0.8f);
        FinalApproachArrowGlowScale = Mathf.Clamp(FinalApproachArrowGlowScale, 1f, 1.25f);
        FinalApproachArrowGlowAlpha = Mathf.Clamp(FinalApproachArrowGlowAlpha, 0f, 0.60f);
        FinalApproachMusicDuckMultiplier = Mathf.Clamp(FinalApproachMusicDuckMultiplier, 0.20f, 1f);
        FinalApproachMusicPitch = Mathf.Clamp(FinalApproachMusicPitch, 0.85f, 1f);
        FinalApproachMusicDuckDuration = Mathf.Max(0.2f, FinalApproachMusicDuckDuration);
        FinalImpactParticleCount = Mathf.Clamp(FinalImpactParticleCount, 6, 40);
        BullseyeImpactParticleCount = Mathf.Clamp(BullseyeImpactParticleCount, FinalImpactParticleCount, 48);
        FinalImpactParticleSpeed = Mathf.Max(0.5f, FinalImpactParticleSpeed);
        FinalImpactShockwaveRadius = Mathf.Max(0.15f, FinalImpactShockwaveRadius);
        BullseyeImpactShockwaveRadius = Mathf.Max(FinalImpactShockwaveRadius, BullseyeImpactShockwaveRadius);
        FinalImpactFlashDuration = Mathf.Clamp(FinalImpactFlashDuration, 0.02f, 0.10f);
        FinalImpactSparkLength = Mathf.Clamp(FinalImpactSparkLength, 0.04f, 0.30f);
        TargetImpactRecoilDistance = Mathf.Clamp(TargetImpactRecoilDistance, 0f, 0.10f);
        BullseyeTargetImpactRecoilDistance = Mathf.Clamp(
            BullseyeTargetImpactRecoilDistance,
            TargetImpactRecoilDistance,
            0.14f);
        TargetImpactRecoilDuration = Mathf.Clamp(TargetImpactRecoilDuration, 0.08f, 0.40f);
        TargetImpactRecoilRotation = Mathf.Clamp(TargetImpactRecoilRotation, 0f, 4f);
        BullseyeTargetImpactRecoilRotation = Mathf.Clamp(
            BullseyeTargetImpactRecoilRotation,
            TargetImpactRecoilRotation,
            5f);
        ArrowTrailTime = Mathf.Max(0.03f, ArrowTrailTime);
        ArrowTrailWidth = Mathf.Max(0.005f, ArrowTrailWidth);
        ArrowTrailStartAlpha = Mathf.Clamp01(ArrowTrailStartAlpha);
        TrajectoryMinPositionDelta = Mathf.Max(0f, TrajectoryMinPositionDelta);
        TrajectoryMinAngleDelta = Mathf.Clamp(TrajectoryMinAngleDelta, 0f, 2f);
    }
}
