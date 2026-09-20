using UnityEngine;

public static class CollectibleFactory
{
    public static GoldenMedallionCollectible Create(
        LevelData.CollectibleData data,
        Transform parent)
    {
        if (data == null)
            return null;

        if (string.IsNullOrWhiteSpace(
                data.CollectibleId))
        {
            Debug.LogError(
                "CollectibleFactory: CollectibleId is required for persistence.");
            return null;
        }

        string resourcePath =
            GetSpriteResourcePath(
                data.Style);

        Sprite sprite =
            Resources.Load<Sprite>(
                resourcePath);

        if (sprite == null)
        {
            Debug.LogError(
                $"CollectibleFactory: Missing sprite at Resources/{resourcePath}.");
            return null;
        }

        GameObject collectibleObject =
            new GameObject(
                $"Collectible_{data.Style}");

        collectibleObject.transform.SetParent(
            parent,
            true);

        collectibleObject.transform.position =
            new Vector3(
                data.Position.x,
                data.Position.y,
                -0.03f);

        collectibleObject.transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                data.Rotation);

        Vector3 parentLossyScale =
            parent != null
                ? parent.lossyScale
                : Vector3.one;

        float safeParentX =
            Mathf.Abs(
                parentLossyScale.x) >
            0.0001f
                ? parentLossyScale.x
                : 1f;

        float safeParentY =
            Mathf.Abs(
                parentLossyScale.y) >
            0.0001f
                ? parentLossyScale.y
                : 1f;

        collectibleObject.transform.localScale =
            new Vector3(
                data.Scale.x /
                    safeParentX,
                data.Scale.y /
                    safeParentY,
                1f);

        SpriteRenderer renderer =
            collectibleObject
                .AddComponent<SpriteRenderer>();

        renderer.sprite =
            sprite;

        renderer.sortingOrder =
            3;

        CircleCollider2D collider =
            collectibleObject
                .AddComponent<CircleCollider2D>();

        collider.isTrigger =
            true;

        collider.radius =
            sprite.bounds.extents.x *
            0.37f;

        GoldenMedallionCollectible collectible =
            collectibleObject
                .AddComponent<GoldenMedallionCollectible>();

        collectible.Configure(
            data.Style,
            data.CollectibleId,
            ATSPlayerProgress
                .IsCollectibleCollected(
                    data.CollectibleId),
            data.MoveAmplitude,
            data.MoveSpeed);

        return collectible;
    }

    private static string GetSpriteResourcePath(
        LevelData.CollectibleStyle style)
    {
        return style switch
        {
            LevelData.CollectibleStyle.GoldenMedallion =>
                "Art/Collectibles/GoldenMedallion",
            _ =>
                "Art/Collectibles/GoldenMedallion"
        };
    }
}
