using UnityEngine;

public static class FlyingBirdFactory
{
    private const string WingUpPath =
        "Art/Hazards/EvilBird_Up";

    private const string WingDownPath =
        "Art/Hazards/EvilBird_Down";

    public static FlyingBirdTarget Create(
        LevelData.FlyingBirdData data,
        Transform parent)
    {
        if (data == null)
            return null;

        Sprite wingUp =
            Resources.Load<Sprite>(
                WingUpPath);

        Sprite wingDown =
            Resources.Load<Sprite>(
                WingDownPath);

        if (wingUp == null)
        {
            Debug.LogError(
                $"FlyingBirdFactory: Missing sprite at Resources/{WingUpPath}.");
            return null;
        }

        GameObject birdObject =
            new GameObject(
                "FlyingBirdGuardian");

        birdObject.transform.SetParent(
            parent,
            true);

        birdObject.transform.position =
            new Vector3(
                data.Position.x,
                data.Position.y,
                -0.04f);

        birdObject.transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                data.Rotation);

        Vector3 parentLossyScale =
            parent != null
                ? parent.lossyScale
                : Vector3.one;

        float safeParentX =
            Mathf.Abs(parentLossyScale.x) > 0.0001f
                ? parentLossyScale.x
                : 1f;

        float safeParentY =
            Mathf.Abs(parentLossyScale.y) > 0.0001f
                ? parentLossyScale.y
                : 1f;

        birdObject.transform.localScale =
            new Vector3(
                data.Scale.x / safeParentX,
                data.Scale.y / safeParentY,
                1f);

        SpriteRenderer renderer =
            birdObject.AddComponent<SpriteRenderer>();

        renderer.sprite =
            wingUp;

        renderer.sortingOrder =
            12;

        CapsuleCollider2D collider =
            birdObject.AddComponent<CapsuleCollider2D>();

        collider.isTrigger =
            true;

        FlyingBirdTarget target =
            birdObject.AddComponent<FlyingBirdTarget>();

        target.Configure(
            wingUp,
            wingDown,
            data.PatrolWidth,
            data.PatrolSpeed,
            data.BobAmplitude,
            data.BobSpeed,
            data.UnlockGroupId,
            data.ConsumeArrowOnHit);

        return target;
    }
}
