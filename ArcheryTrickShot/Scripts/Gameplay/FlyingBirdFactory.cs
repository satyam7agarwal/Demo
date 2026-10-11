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

        bool hasCustomVisual =
            !string.IsNullOrWhiteSpace(
                data.VisualResourcePath);

        string primaryPath =
            hasCustomVisual
                ? data.VisualResourcePath.Trim()
                : WingUpPath;

        Sprite wingUp =
            Resources.Load<Sprite>(
                primaryPath);

        Sprite wingDown = null;

        if (hasCustomVisual)
        {
            string alternatePath =
                string.IsNullOrWhiteSpace(
                    data.AlternateVisualResourcePath)
                    ? primaryPath
                    : data.AlternateVisualResourcePath.Trim();

            wingDown =
                Resources.Load<Sprite>(
                    alternatePath);
        }
        else
        {
            wingDown =
                Resources.Load<Sprite>(
                    WingDownPath);
        }

        if (wingUp == null)
        {
            Debug.LogError(
                $"FlyingBirdFactory: Missing sprite at Resources/{primaryPath}.");
            return null;
        }

        if (wingDown == null)
            wingDown = wingUp;

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

        float fitScale = 1f;

        if (data.VisualWorldWidth > 0.001f &&
            wingUp.bounds.size.x > 0.001f)
        {
            fitScale =
                data.VisualWorldWidth /
                wingUp.bounds.size.x;
        }

        birdObject.transform.localScale =
            new Vector3(
                data.Scale.x *
                    fitScale /
                    safeParentX,
                data.Scale.y *
                    fitScale /
                    safeParentY,
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
            data.ConsumeArrowOnHit,
            data.HitboxWorldSize);

        return target;
    }
}
