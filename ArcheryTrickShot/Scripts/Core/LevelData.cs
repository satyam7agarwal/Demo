using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Level_", menuName = "Archery Trick Shot/Level Data")]
public sealed class LevelData : ScriptableObject
{
    [Min(1)] public int LevelNumber = 1;
    [Min(1)] public int MaxShots = 3;

    [Tooltip(
        "Maximum total arrows allowed for a perfect 3-star clear. " +
        "1 = normal level. Level 10 uses 2 because its guardian is a mandatory phase.")]
    [Min(1)]
    public int ThreeStarShotLimit = 1;

    [FormerlySerializedAs("BowPosition")]
    public Vector2 ArcherPosition = new Vector2(-5f, -2f);

    [Header("Ricochet Mastery")]
    [Tooltip(
        "Maximum UNIQUE mirrors intentionally rewardable in this level. " +
        "0 = automatically use the number of entries in Mirrors.")]
    [Min(0)]
    public int MaxRewardedRicochetMirrors = 0;

    [Header("Typed Level Layout")]
    public TargetData[] Targets =
        new TargetData[0];

    public WallData[] Walls =
        new WallData[0];

    public MirrorData[] Mirrors =
        new MirrorData[0];

    public BonusPropData[] BonusProps =
        new BonusPropData[0];

    public CollectibleData[] Collectibles =
        new CollectibleData[0];

    public GateData[] Gates =
        new GateData[0];

    public FlyingBirdData[] FlyingBirds =
        new FlyingBirdData[0];

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
        public TargetFacing Facing =
            TargetFacing.Auto;

        public TargetStyle Style =
            TargetStyle.Wood;
    }

    [System.Serializable]
    public sealed class WallData : PlacementData
    {
    }

    [System.Serializable]
    public sealed class MirrorData : PlacementData
    {
    }

    [System.Serializable]
    public sealed class BonusPropData : PlacementData
    {
        public BonusPropStyle Style =
            BonusPropStyle.ClayPot;

        [Min(0)]
        public int ScoreOverride = 0;
    }

    [System.Serializable]
    public sealed class CollectibleData : PlacementData
    {
        public CollectibleStyle Style =
            CollectibleStyle.GoldenMedallion;

        public string CollectibleId =
            string.Empty;

        [Min(0f)]
        public float MoveAmplitude =
            0.65f;

        [Min(0f)]
        public float MoveSpeed =
            1.35f;
    }

    [System.Serializable]
    public sealed class GateData : PlacementData
    {
        [Tooltip(
            "Birds and gates sharing this ID participate in the same unlock group.")]
        public string UnlockGroupId =
            "default";

        public Vector2 OpenOffset =
            new Vector2(
                0f,
                2.55f);

        [Min(0.1f)]
        public float OpenDuration =
            0.72f;
    }

    [System.Serializable]
    public sealed class FlyingBirdData : PlacementData
    {
        [Tooltip(
            "When all birds in this group are defeated, gates with the same ID open.")]
        public string UnlockGroupId =
            "default";

        [Min(0f)]
        public float PatrolWidth =
            4.6f;

        [Min(0.1f)]
        public float PatrolSpeed =
            1.15f;

        [Min(0f)]
        public float BobAmplitude =
            0.28f;

        [Min(0.1f)]
        public float BobSpeed =
            2.1f;

        public bool ConsumeArrowOnHit =
            true;
    }

    // ---------------------------------------------------------------------
    // READ-ONLY PRESENTATION ADAPTER
    //
    // This property is NOT serialized by Unity and is NOT a level-layout
    // source. Existing frontend presentation code uses it only to answer:
    // "how many mirrors/walls?" and "which medallion IDs exist?".
    //
    // Gameplay uses the typed arrays above exclusively.
    // ---------------------------------------------------------------------
    public enum ObjectType
    {
        Target = 0,
        Wall = 1,
        Mirror = 2,
        BonusProp = 3,
        Collectible = 4,
        Gate = 5,
        FlyingBird = 6
    }

    public sealed class LevelObjectData
    {
        public ObjectType Type;
        public CollectibleStyle CollectibleStyle;
        public string CollectibleId;
    }

    public LevelObjectData[] Objects =>
        BuildPresentationObjectView();

    private LevelObjectData[] BuildPresentationObjectView()
    {
        int targetCount =
            Targets != null ? Targets.Length : 0;
        int wallCount =
            Walls != null ? Walls.Length : 0;
        int mirrorCount =
            Mirrors != null ? Mirrors.Length : 0;
        int bonusCount =
            BonusProps != null ? BonusProps.Length : 0;
        int collectibleCount =
            Collectibles != null ? Collectibles.Length : 0;
        int gateCount =
            Gates != null ? Gates.Length : 0;
        int birdCount =
            FlyingBirds != null ? FlyingBirds.Length : 0;

        LevelObjectData[] result =
            new LevelObjectData[
                targetCount +
                wallCount +
                mirrorCount +
                bonusCount +
                collectibleCount +
                gateCount +
                birdCount];

        int index = 0;

        for (int i = 0; i < targetCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.Target
                };
        }

        for (int i = 0; i < wallCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.Wall
                };
        }

        for (int i = 0; i < mirrorCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.Mirror
                };
        }

        for (int i = 0; i < bonusCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.BonusProp
                };
        }

        for (int i = 0; i < collectibleCount; i++)
        {
            CollectibleData collectible =
                Collectibles[i];

            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.Collectible,
                    CollectibleStyle =
                        collectible != null
                            ? collectible.Style
                            : CollectibleStyle.GoldenMedallion,
                    CollectibleId =
                        collectible != null
                            ? collectible.CollectibleId
                            : string.Empty
                };
        }

        for (int i = 0; i < gateCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.Gate
                };
        }

        for (int i = 0; i < birdCount; i++)
        {
            result[index++] =
                new LevelObjectData
                {
                    Type =
                        ObjectType.FlyingBird
                };
        }

        return result;
    }
}
