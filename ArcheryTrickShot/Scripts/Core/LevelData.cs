using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Level_", menuName = "Archery Trick Shot/Level Data")]
public sealed class LevelData : ScriptableObject
{
    [Min(1)] public int LevelNumber = 1;
    [Min(1)] public int MaxShots = 3;

    [FormerlySerializedAs("BowPosition")]
    public Vector2 ArcherPosition = new Vector2(-5f, -2f);

    public LevelObjectData[] Objects = new LevelObjectData[0];

    public enum ObjectType
    {
        Target,
        Wall,
        Mirror
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
    public sealed class LevelObjectData
    {
        public ObjectType Type;
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale = Vector2.one;

        [Tooltip(
            "Used only for Target objects. Auto faces the target toward " +
            "the archer based on horizontal position.")]
        public TargetFacing Facing = TargetFacing.Auto;

        [Tooltip(
            "Used only for Target objects. Existing levels default to Wood.")]
        public TargetStyle Style = TargetStyle.Wood;
    }
}
