using UnityEngine;

public enum ArcherProjectileMotionMode
{
    Straight = 0,
    GravityArc = 1
}

/// <summary>
/// Character-owned GAMEPLAY traits.
///
/// This is deliberately separate from LevelData:
/// a level chooses layout/featured character, while the character owns how
/// their arrows behave. Add future character mechanics here rather than
/// branching on level numbers.
/// </summary>
[CreateAssetMenu(
    fileName = "ArcherGameplayTrait",
    menuName = "Archery Trick Shot/Archer Gameplay Trait")]
public sealed class ArcherGameplayTraitProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip(
        "Must match Archer3DRuntimeProfile.CharacterId.")]
    public string CharacterId = "archer";

    [Header("Projectile Motion")]
    public ArcherProjectileMotionMode ProjectileMotion =
        ArcherProjectileMotionMode.Straight;

    [Tooltip(
        "Multiplier applied to Physics2D.gravity after this character fires. " +
        "0 keeps straight-flight behaviour.")]
    [Range(0f, 2f)]
    public float GravityScale = 0f;

    [Tooltip(
        "Rotate the flying arrow to follow its instantaneous velocity.")]
    public bool AlignArrowToVelocity = true;

    [Header("Draw Strength / Launch Speed")]
    [Tooltip(
        "When enabled, launch speed is derived from the player's actual bow draw amount.")]
    public bool UsesDrawStrength = false;

    [Tooltip(
        "Arrow speed at the weakest valid draw.")]
    [Min(0.1f)]
    public float MinLaunchSpeed = 7f;

    [Tooltip(
        "Arrow speed at full draw.")]
    [Min(0.1f)]
    public float MaxLaunchSpeed = 15f;

    [Tooltip(
        "Shapes the power response. Values above 1 make partial draws softer " +
        "while preserving full-draw power.")]
    [Range(0.25f, 3f)]
    public float DrawPowerExponent = 1.35f;

    [Tooltip(
        "Separates controls so horizontal pull changes power and vertical pull changes aim. " +
        "This prevents changing power from unintentionally rotating the bow.")]
    public bool IndependentAimAndDraw = false;

    [Tooltip(
        "Vertical world-space drag required to reach the configured maximum/minimum aim angle " +
        "when IndependentAimAndDraw is enabled.")]
    [Min(0.25f)]
    public float VerticalAimRange = 2.5f;

    [Header("Radial Draw Stabilization")]
    [Tooltip(
        "When easing the string forward, angular movement inside this corridor is treated as " +
        "power adjustment rather than a request to re-aim.")]
    [Range(0.5f, 15f)]
    public float RadialAimLockToleranceDegrees = 5f;

    [Tooltip(
        "Once the radial aim axis is locked, the player must deviate this far before the aim " +
        "unlocks. Keep this larger than RadialAimLockToleranceDegrees to provide hysteresis.")]
    [Range(1f, 25f)]
    public float RadialAimUnlockThresholdDegrees = 8f;

    [Header("Character Aim Guide")]
    [Tooltip(
        "Show the character-specific ballistic curve while aiming. " +
        "This is independent from the normal mirror FULL PATH assist.")]
    public bool ShowAimCurve = false;

    [Range(0.4f, 3f)]
    public float AimCurveDuration = 1.45f;

    [Range(8, 64)]
    public int AimCurveSamples = 34;

    [Range(0.005f, 0.12f)]
    public float AimCurveWidth = 0.032f;

    public Color AimCurveColor =
        new Color(
            0.55f,
            1f,
            0.18f,
            0.92f);

    public bool UsesGravityArc =>
        ProjectileMotion ==
            ArcherProjectileMotionMode.GravityArc &&
        GravityScale > 0.0001f;

    public float EvaluateLaunchSpeed(
        float drawAmount,
        float fallbackSpeed)
    {
        if (!UsesDrawStrength)
        {
            return Mathf.Max(
                0.1f,
                fallbackSpeed);
        }

        float minSpeed =
            Mathf.Max(
                0.1f,
                MinLaunchSpeed);

        float maxSpeed =
            Mathf.Max(
                minSpeed,
                MaxLaunchSpeed);

        float normalizedDraw =
            Mathf.Pow(
                Mathf.Clamp01(
                    drawAmount),
                Mathf.Max(
                    0.01f,
                    DrawPowerExponent));

        return Mathf.Lerp(
            minSpeed,
            maxSpeed,
            normalizedDraw);
    }

    private void OnValidate()
    {
        CharacterId =
            string.IsNullOrWhiteSpace(CharacterId)
                ? name.Trim().ToLowerInvariant()
                : CharacterId.Trim().ToLowerInvariant();

        GravityScale =
            Mathf.Clamp(
                GravityScale,
                0f,
                2f);

        MinLaunchSpeed =
            Mathf.Max(
                0.1f,
                MinLaunchSpeed);

        MaxLaunchSpeed =
            Mathf.Max(
                MinLaunchSpeed,
                MaxLaunchSpeed);

        DrawPowerExponent =
            Mathf.Clamp(
                DrawPowerExponent,
                0.25f,
                3f);

        VerticalAimRange =
            Mathf.Max(
                0.25f,
                VerticalAimRange);

        RadialAimLockToleranceDegrees =
            Mathf.Clamp(
                RadialAimLockToleranceDegrees,
                0.5f,
                15f);

        RadialAimUnlockThresholdDegrees =
            Mathf.Clamp(
                RadialAimUnlockThresholdDegrees,
                RadialAimLockToleranceDegrees + 0.5f,
                25f);

        AimCurveDuration =
            Mathf.Clamp(
                AimCurveDuration,
                0.4f,
                3f);

        AimCurveSamples =
            Mathf.Clamp(
                AimCurveSamples,
                8,
                64);

        AimCurveWidth =
            Mathf.Clamp(
                AimCurveWidth,
                0.005f,
                0.12f);
    }
}

/// <summary>
/// Resolves gameplay traits by character ID.
/// No level-number knowledge is allowed here.
/// </summary>
public static class ArcherGameplayTraitResolver
{
    private const string ResourcePath =
        "Archer3D/Traits";

    private static ArcherGameplayTraitProfile[] cachedProfiles;

    public static ArcherGameplayTraitProfile ResolveSelected()
    {
        return Resolve(
            Archer3DRuntimeProfile.LoadDefault());
    }

    public static ArcherGameplayTraitProfile Resolve(
        Archer3DRuntimeProfile archerProfile)
    {
        if (archerProfile == null ||
            string.IsNullOrWhiteSpace(
                archerProfile.CharacterId))
        {
            return null;
        }

        EnsureLoaded();

        for (int i = 0;
             i < cachedProfiles.Length;
             i++)
        {
            ArcherGameplayTraitProfile candidate =
                cachedProfiles[i];

            if (candidate == null)
                continue;

            if (string.Equals(
                    candidate.CharacterId,
                    archerProfile.CharacterId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    public static void InvalidateCache()
    {
        cachedProfiles = null;
    }

    private static void EnsureLoaded()
    {
        if (cachedProfiles != null)
            return;

        cachedProfiles =
            Resources.LoadAll<ArcherGameplayTraitProfile>(
                ResourcePath);

        if (cachedProfiles == null)
        {
            cachedProfiles =
                new ArcherGameplayTraitProfile[0];
        }
    }
}
