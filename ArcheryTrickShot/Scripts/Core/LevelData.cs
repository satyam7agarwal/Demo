using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Level_", menuName = "Archery Trick Shot/Level Data")]
public sealed class LevelData : ScriptableObject
{
    [Min(1)] public int LevelNumber = 1;
    [Min(1)] public int MaxShots = 3;

    [Header("Campaign Completion")]
    [Tooltip(
        "Explicit opt-in for the true final level of the whole campaign. " +
        "Do not infer campaign completion from the highest LevelData currently authored.")]
    public bool IsGameFinale = false;

    [Tooltip(
        "Maximum total arrows allowed for a perfect 3-star clear. " +
        "1 = normal level. Level 10 uses 2 because its guardian is a mandatory phase.")]
    [Min(1)]
    public int ThreeStarShotLimit = 1;

    [FormerlySerializedAs("BowPosition")]
    public Vector2 ArcherPosition = new Vector2(-5f, -2f);

    [Header("Character")]
    [Tooltip(
        "Optional character required for this level. This selects WHO plays the level; " +
        "character abilities such as gravity remain character-owned gameplay traits.")]
    public string RequiredCharacterId = string.Empty;

    [Header("Ricochet Mastery")]
    [Tooltip(
        "Maximum UNIQUE mirrors intentionally rewardable in this level. " +
        "0 = automatically use the number of entries in Mirrors.")]
    [Min(0)]
    public int MaxRewardedRicochetMirrors = 0;

    [Header("Typed Level Layout")]
    public TargetData[] Targets = new TargetData[0];
    public WallData[] Walls = new WallData[0];
    public MirrorData[] Mirrors = new MirrorData[0];
    public BonusPropData[] BonusProps = new BonusPropData[0];
    public CollectibleData[] Collectibles = new CollectibleData[0];
    public GateData[] Gates = new GateData[0];
    public FlyingBirdData[] FlyingBirds = new FlyingBirdData[0];

    [Header("Level Presentation")]
    [Tooltip(
        "Pure visual sprites layered over the world background. " +
        "Keep gameplay collision in PresentationColliders so art can be adjusted independently.")]
    public PresentationSpriteData[] PresentationSprites =
        new PresentationSpriteData[0];

    [Tooltip(
        "Simple authored collision geometry for presentation/environment art. " +
        "These colliders are spawned generically for any level that declares them.")]
    public PresentationColliderData[] PresentationColliders =
        new PresentationColliderData[0];

    [Header("Crystal Caverns")]
    [Tooltip("Arrow-reactive switches. Matching CrystalGates open when a crystal is crossed.")]
    public ResonanceCrystalData[] ResonanceCrystals = new ResonanceCrystalData[0];

    [Tooltip("Crystal-powered gates controlled by ResonanceCrystals with the same group ID.")]
    public CrystalGateData[] CrystalGates = new CrystalGateData[0];

    [Tooltip("Fixed-exit prisms. Gravity continues normally after redirection.")]
    public PrismCrystalData[] PrismCrystals = new PrismCrystalData[0];

    [Tooltip(
        "Authored obstacles that warn, dissolve completely, stay absent for a fair shooting window, " +
        "then telegraph and materialize again. The mechanic is generic and reusable across levels.")]
    public VanishingObstacleData[] VanishingObstacles =
        new VanishingObstacleData[0];

    [Tooltip(
        "Living crystal serpents that telegraph beneath the water, emerge on a deterministic cycle, " +
        "and can unlock CrystalGates through a weak-point hit.")]
    public AbyssalSerpentData[] AbyssalSerpents =
        new AbyssalSerpentData[0];

    [Tooltip(
        "Paired one-way crystal rifts. The arrow enters the EntryPosition and is moved to " +
        "ExitPosition while preserving its instantaneous velocity; gravity continues normally.")]
    public CrystalRiftPairData[] CrystalRifts =
        new CrystalRiftPairData[0];

    public enum CollectibleStyle
    {
        GoldenMedallion = 0
    }

    public enum BonusPropStyle
    {
        ClayPot = 0,
        GlassBottle = 1,
        Bell = 2
    }

    public enum TargetFacing
    {
        Auto = 0,
        Left = 1,
        Right = 2
    }

    public enum TargetStyle
    {
        Wood = 0,
        Ruins = 1,
        Crystal = 2,
        Molten = 3,
        Clockwork = 4
    }

    public enum PresentationColliderShape
    {
        Box = 0,
        Polygon = 1
    }

    [System.Serializable]
    public class PlacementData
    {
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale = Vector2.one;
    }

    [System.Serializable]
    public sealed class TargetData : PlacementData
    {
        public TargetFacing Facing = TargetFacing.Auto;
        public TargetStyle Style = TargetStyle.Wood;

        [Tooltip(
            "Hide the target's generic stone pedestal when authored environment art " +
            "already provides a natural supporting ledge.")]
        public bool HidePedestalBase = false;
    }

    [System.Serializable]
    public sealed class WallData : PlacementData { }

    [System.Serializable]
    public sealed class MirrorData : PlacementData { }

    [System.Serializable]
    public sealed class BonusPropData : PlacementData
    {
        public BonusPropStyle Style = BonusPropStyle.ClayPot;
        [Min(0)] public int ScoreOverride = 0;
    }

    [System.Serializable]
    public sealed class CollectibleData : PlacementData
    {
        public CollectibleStyle Style = CollectibleStyle.GoldenMedallion;
        public string CollectibleId = string.Empty;
        [Min(0f)] public float MoveAmplitude = 0.65f;
        [Min(0f)] public float MoveSpeed = 1.35f;
    }

    [System.Serializable]
    public sealed class GateData : PlacementData
    {
        [Tooltip("Birds and gates sharing this ID participate in the same unlock group.")]
        public string UnlockGroupId = "default";
        public Vector2 OpenOffset = new Vector2(0f, 2.55f);
        [Min(0.1f)] public float OpenDuration = 0.72f;
    }

    [System.Serializable]
    public sealed class FlyingBirdData : PlacementData
    {
        [Tooltip("When all birds in this group are defeated, gates with the same ID open.")]
        public string UnlockGroupId = "default";
        [Min(0f)] public float PatrolWidth = 4.6f;
        [Min(0.1f)] public float PatrolSpeed = 1.15f;
        [Min(0f)] public float BobAmplitude = 0.28f;
        [Min(0.1f)] public float BobSpeed = 2.1f;
        public bool ConsumeArrowOnHit = true;

        [Header("Optional Creature Presentation")]
        [Tooltip(
            "Optional Resources-relative sprite path. Empty preserves the legacy EvilBird visual. " +
            "Use this to present the same generic flying-target mechanic as bats or other creatures.")]
        public string VisualResourcePath = string.Empty;

        [Tooltip(
            "Optional alternate sprite used by the existing wing-cycle animation. " +
            "When empty, VisualResourcePath is reused so authored single-frame creatures remain stable.")]
        public string AlternateVisualResourcePath = string.Empty;

        [Tooltip(
            "Optional final world width for the creature sprite. " +
            "0 preserves the legacy scale behavior.")]
        [Min(0f)]
        public float VisualWorldWidth = 0f;

        [Tooltip(
            "Optional world-space hitbox size. Zero keeps the legacy 1.48 x 0.72 local capsule.")]
        public Vector2 HitboxWorldSize = Vector2.zero;
    }

    [System.Serializable]
    public sealed class PresentationSpriteData : PlacementData
    {
        [Tooltip("Optional hierarchy name. Used only for readability/debugging.")]
        public string Name = string.Empty;

        [Tooltip("Resources-relative Sprite path, without extension.")]
        public string ResourcePath = string.Empty;

        [Tooltip(
            "When greater than zero, the runtime scales the sprite uniformly to this world width. " +
            "This keeps authored art resolution independent from gameplay layout.")]
        [Min(0f)] public float WorldWidth = 0f;

        public float Depth = 0f;
        public int SortingOrder = 0;
        public bool FlipX = false;
        public bool FlipY = false;
    }

    [System.Serializable]
    public sealed class PresentationColliderData : PlacementData
    {
        [Tooltip("Optional hierarchy name. Used only for readability/debugging.")]
        public string Name = string.Empty;

        [Tooltip(
            "Optional PresentationSprite Name to follow. When set, this collider is parented " +
            "under that visual so WorldWidth/resizing automatically scales the collision too. " +
            "Position/Rotation/Scale then act as local offsets relative to the matched visual.")]
        public string MatchVisual = string.Empty;

        public PresentationColliderShape Shape =
            PresentationColliderShape.Box;

        [Tooltip("Local size for Box colliders.")]
        public Vector2 Size = Vector2.one;

        [Tooltip("Local offset for Box colliders.")]
        public Vector2 Offset = Vector2.zero;

        [Tooltip("Local points for Polygon colliders, in winding order.")]
        public Vector2[] Points = new Vector2[0];

        public bool IsTrigger = false;
    }

    [System.Serializable]
    public sealed class ResonanceCrystalData : PlacementData
    {
        public string UnlockGroupId = "default";
        [Tooltip("How much larger the arrow-trigger volume is than the crystal visual.")]
        [Range(0.6f, 1.8f)] public float TriggerRadius = 0.82f;
    }

    [System.Serializable]
    public sealed class CrystalGateData : PlacementData
    {
        public string UnlockGroupId = "default";
        public Vector2 OpenOffset = new Vector2(0f, 3.2f);
        [Min(0.08f)] public float OpenDuration = 0.24f;

        [Header("Optional Gate Presentation")]
        [Tooltip(
            "Optional Resources-relative authored gate sprite. Empty preserves the existing two-leaf gate.")]
        public string VisualResourcePath = string.Empty;

        [Tooltip(
            "Optional final world width of the authored gate sprite. " +
            "0 keeps the sprite's imported size.")]
        [Min(0f)]
        public float VisualWorldWidth = 0f;

        [Tooltip("Local visual offset relative to the CrystalGate root.")]
        public Vector2 VisualOffset = Vector2.zero;

        [Tooltip(
            "When true, the legacy leaf renderers are hidden but their proven physical colliders remain active.")]
        public bool HideDefaultVisual = false;

        public int VisualSortingOrder = 9;
    }

    [System.Serializable]
    public sealed class PrismCrystalData : PlacementData
    {
        [Tooltip("Absolute world-space exit direction in degrees. 0 = right, 90 = up.")]
        public float ExitAngle = -20f;
        [Range(0.5f, 1.5f)] public float SpeedMultiplier = 1f;
        [Range(0.6f, 1.8f)] public float TriggerRadius = 0.82f;
    }

    [System.Serializable]
    public sealed class VanishingObstacleData : PlacementData
    {
        [Tooltip("Resources-relative Sprite path, without extension.")]
        public string ResourcePath = string.Empty;

        [Tooltip(
            "Final sprite width in world units. 0 keeps the imported sprite size. " +
            "Placement Scale is applied after this fit.")]
        [Min(0f)]
        public float WorldWidth = 0f;

        [Tooltip("World-space blocking size while the obstacle is solid.")]
        public Vector2 HitboxWorldSize =
            new Vector2(1.5f, 4f);

        [Tooltip("World-space offset of the blocking box from the visual pivot.")]
        public Vector2 HitboxOffset =
            Vector2.zero;

        public int SortingOrder = 8;

        [Tooltip("Optional initial delay before the first warning.")]
        [Min(0f)]
        public float StartDelay = 0.25f;

        [Tooltip("How long the stone remains fully solid before its warning begins.")]
        [Min(0.05f)]
        public float VisibleDuration = 1.10f;

        [Tooltip("Readable glow/crack warning before the stone dissolves.")]
        [Min(0.05f)]
        public float WarningDuration = 0.45f;

        [Tooltip("Fade/dissolve duration. Collision remains until the stone is almost gone.")]
        [Min(0.05f)]
        public float DisappearDuration = 0.25f;

        [Tooltip("True open shooting window: renderer hidden and collision disabled.")]
        [Min(0.10f)]
        public float HiddenDuration = 1.30f;

        [Tooltip(
            "Ghost shimmer before the obstacle returns. It is still non-colliding during this warning.")]
        [Min(0f)]
        public float ReappearWarningDuration = 0.35f;

        [Tooltip("Materialization duration. Collision is restored only when fully visible.")]
        [Min(0.05f)]
        public float ReappearDuration = 0.30f;
    }

    [System.Serializable]
    public sealed class AbyssalSerpentData : PlacementData
    {
        [Tooltip("Resources-relative authored serpent sprite path, without extension.")]
        public string ResourcePath = string.Empty;

        [Tooltip(
            "Final serpent sprite width in world units. 0 keeps imported sprite size. " +
            "Placement Scale is applied after this fit.")]
        [Min(0f)]
        public float WorldWidth = 0f;

        [Tooltip(
            "Where the serpent waits while submerged, relative to its authored emerged Position.")]
        public Vector2 HiddenOffset =
            new Vector2(0f, -4.2f);

        [Tooltip("World-space water-surface Y used for the warning ripple.")]
        public float WaterSurfaceY = -2.65f;

        [Tooltip(
            "World-space offset of the hittable crystal weak point from the serpent pivot.")]
        public Vector2 WeakPointOffset =
            new Vector2(0f, 0.15f);

        [Tooltip("World-space weak-point radius.")]
        [Min(0.10f)]
        public float WeakPointRadius = 0.45f;

        [Tooltip("CrystalGate unlock group activated by a successful weak-point hit.")]
        public string UnlockGroupId = "default";

        [Tooltip(
            "Normally false in first-teach encounters so the same arrow continues through the newly opened route.")]
        public bool ConsumeArrowOnWeakPoint = false;

        public int SortingOrder = 8;

        [Min(0f)]
        public float StartDelay = 0.35f;

        [Min(0.05f)]
        public float WarningDuration = 0.65f;

        [Min(0.05f)]
        public float RiseDuration = 0.52f;

        [Min(0.10f)]
        public float EmergedDuration = 1.55f;

        [Min(0.05f)]
        public float RetreatDuration = 0.48f;

        [Min(0.10f)]
        public float HiddenDuration = 1.35f;
    }

    [System.Serializable]
    public sealed class CrystalRiftPairData
    {
        [Tooltip("Readable/debug identifier for this linked pair.")]
        public string PairId = "rift";

        public Vector2 EntryPosition;
        public float EntryRotation = 0f;
        public Vector2 EntryScale = Vector2.one;

        public Vector2 ExitPosition;
        public float ExitRotation = 0f;
        public Vector2 ExitScale = Vector2.one;

        [Tooltip("Resources-relative entry portal sprite path, without extension.")]
        public string EntryResourcePath =
            "Art/Gameplay/World2/Rifts/CrystalRift_Entry";

        [Tooltip("Resources-relative exit portal sprite path, without extension.")]
        public string ExitResourcePath =
            "Art/Gameplay/World2/Rifts/CrystalRift_Exit";

        [Tooltip("Final world width of each rift visual before Entry/ExitScale.")]
        [Min(0.1f)]
        public float VisualWorldWidth = 2.25f;

        [Tooltip(
            "World-space radius of the interactive inner portal. " +
            "This is intentionally smaller than the visible crystal ring.")]
        [Min(0.15f)]
        public float TriggerRadius = 0.62f;

        [Tooltip(
            "Distance the arrow appears just beyond the exit centre, along its preserved travel direction.")]
        [Min(0f)]
        public float ExitClearance = 0.35f;

        [Tooltip(
            "Multiplier applied to the instantaneous velocity magnitude while crossing the rift. " +
            "1 preserves speed exactly.")]
        [Range(0.5f, 1.5f)]
        public float SpeedMultiplier = 1f;

        public int SortingOrder = 10;
    }


    // ---------------------------------------------------------------------
    // READ-ONLY PRESENTATION ADAPTER
    // ---------------------------------------------------------------------
    public enum ObjectType
    {
        Target = 0,
        Wall = 1,
        Mirror = 2,
        BonusProp = 3,
        Collectible = 4,
        Gate = 5,
        FlyingBird = 6,
        ResonanceCrystal = 7,
        CrystalGate = 8,
        PrismCrystal = 9,
        VanishingObstacle = 10,
        AbyssalSerpent = 11,
        CrystalRift = 12
    }

    public sealed class LevelObjectData
    {
        public ObjectType Type;
        public CollectibleStyle CollectibleStyle;
        public string CollectibleId;
    }

    public LevelObjectData[] Objects => BuildPresentationObjectView();

    private LevelObjectData[] BuildPresentationObjectView()
    {
        int targetCount = Targets != null ? Targets.Length : 0;
        int wallCount = Walls != null ? Walls.Length : 0;
        int mirrorCount = Mirrors != null ? Mirrors.Length : 0;
        int bonusCount = BonusProps != null ? BonusProps.Length : 0;
        int collectibleCount = Collectibles != null ? Collectibles.Length : 0;
        int gateCount = Gates != null ? Gates.Length : 0;
        int birdCount = FlyingBirds != null ? FlyingBirds.Length : 0;
        int resonanceCount = ResonanceCrystals != null ? ResonanceCrystals.Length : 0;
        int crystalGateCount = CrystalGates != null ? CrystalGates.Length : 0;
        int prismCount = PrismCrystals != null ? PrismCrystals.Length : 0;
        int vanishingObstacleCount =
            VanishingObstacles != null ? VanishingObstacles.Length : 0;
        int abyssalSerpentCount =
            AbyssalSerpents != null ? AbyssalSerpents.Length : 0;
        int crystalRiftCount =
            CrystalRifts != null ? CrystalRifts.Length : 0;

        LevelObjectData[] result = new LevelObjectData[
            targetCount + wallCount + mirrorCount + bonusCount + collectibleCount +
            gateCount + birdCount + resonanceCount + crystalGateCount + prismCount +
            vanishingObstacleCount + abyssalSerpentCount + crystalRiftCount];

        int index = 0;
        for (int i = 0; i < targetCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.Target };
        for (int i = 0; i < wallCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.Wall };
        for (int i = 0; i < mirrorCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.Mirror };
        for (int i = 0; i < bonusCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.BonusProp };

        for (int i = 0; i < collectibleCount; i++)
        {
            CollectibleData collectible = Collectibles[i];
            result[index++] = new LevelObjectData
            {
                Type = ObjectType.Collectible,
                CollectibleStyle = collectible != null ? collectible.Style : CollectibleStyle.GoldenMedallion,
                CollectibleId = collectible != null ? collectible.CollectibleId : string.Empty
            };
        }

        for (int i = 0; i < gateCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.Gate };
        for (int i = 0; i < birdCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.FlyingBird };
        for (int i = 0; i < resonanceCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.ResonanceCrystal };
        for (int i = 0; i < crystalGateCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.CrystalGate };
        for (int i = 0; i < prismCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.PrismCrystal };
        for (int i = 0; i < vanishingObstacleCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.VanishingObstacle };
        for (int i = 0; i < abyssalSerpentCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.AbyssalSerpent };
        for (int i = 0; i < crystalRiftCount; i++) result[index++] = new LevelObjectData { Type = ObjectType.CrystalRift };

        return result;
    }
}
