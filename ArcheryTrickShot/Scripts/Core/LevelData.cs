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
    }

    [System.Serializable]
    public sealed class PrismCrystalData : PlacementData
    {
        [Tooltip("Absolute world-space exit direction in degrees. 0 = right, 90 = up.")]
        public float ExitAngle = -20f;
        [Range(0.5f, 1.5f)] public float SpeedMultiplier = 1f;
        [Range(0.6f, 1.8f)] public float TriggerRadius = 0.82f;
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
        PrismCrystal = 9
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

        LevelObjectData[] result = new LevelObjectData[
            targetCount + wallCount + mirrorCount + bonusCount + collectibleCount +
            gateCount + birdCount + resonanceCount + crystalGateCount + prismCount];

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

        return result;
    }
}
