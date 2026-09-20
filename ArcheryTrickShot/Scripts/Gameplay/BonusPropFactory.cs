using UnityEngine;

/// <summary>
/// Builds authored bonus-prop art from typed LevelData.
/// </summary>
public static class BonusPropFactory
{
    public static BonusProp Create(
        LevelData.BonusPropData data,
        Transform parent)
    {
        if (data == null)
            return null;

        string resourcePath =
            GetSpriteResourcePath(
                data.Style);

        Sprite sprite =
            Resources.Load<Sprite>(
                resourcePath);

        if (sprite == null)
        {
            Debug.LogError(
                $"BonusPropFactory: Missing sprite at Resources/{resourcePath}.");
            return null;
        }

        GameObject propObject =
            new GameObject(
                $"BonusProp_{data.Style}");

        propObject.transform.SetParent(
            parent,
            true);

        propObject.transform.position =
            new Vector3(
                data.Position.x,
                data.Position.y,
                -0.02f);

        propObject.transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                data.Rotation);

        Vector3 parentLossyScale =
            parent != null
                ? parent.lossyScale
                : Vector3.one;

        float safeParentX =
            Mathf.Abs(parentLossyScale.x) >
            0.0001f
                ? parentLossyScale.x
                : 1f;

        float safeParentY =
            Mathf.Abs(parentLossyScale.y) >
            0.0001f
                ? parentLossyScale.y
                : 1f;

        propObject.transform.localScale =
            new Vector3(
                data.Scale.x / safeParentX,
                data.Scale.y / safeParentY,
                1f);

        SpriteRenderer renderer =
            propObject.AddComponent<SpriteRenderer>();

        renderer.sprite =
            sprite;
        renderer.sortingOrder =
            1;

        BoxCollider2D collider =
            propObject.AddComponent<BoxCollider2D>();

        collider.isTrigger =
            true;

        ApplyColliderShape(
            collider,
            sprite,
            data.Style);

        BonusProp prop =
            propObject.AddComponent<BonusProp>();

        prop.Configure(
            data.Style,
            data.ScoreOverride);

        return prop;
    }

    private static string GetSpriteResourcePath(
        LevelData.BonusPropStyle style)
    {
        return style switch
        {
            LevelData.BonusPropStyle.ClayPot =>
                "Art/BonusProps/ClayPot",
            LevelData.BonusPropStyle.GlassBottle =>
                "Art/BonusProps/GlassBottle",
            LevelData.BonusPropStyle.Bell =>
                "Art/BonusProps/Bell",
            _ =>
                "Art/BonusProps/ClayPot"
        };
    }

    private static void ApplyColliderShape(
        BoxCollider2D collider,
        Sprite sprite,
        LevelData.BonusPropStyle style)
    {
        if (collider == null ||
            sprite == null)
        {
            return;
        }

        Vector2 size =
            sprite.bounds.size;

        switch (style)
        {
            case LevelData.BonusPropStyle.ClayPot:
                collider.size =
                    new Vector2(
                        size.x * 0.58f,
                        size.y * 0.62f);
                collider.offset =
                    new Vector2(
                        0f,
                        -size.y * 0.04f);
                break;

            case LevelData.BonusPropStyle.GlassBottle:
                collider.size =
                    new Vector2(
                        size.x * 0.34f,
                        size.y * 0.76f);
                collider.offset =
                    new Vector2(
                        0f,
                        -size.y * 0.02f);
                break;

            case LevelData.BonusPropStyle.Bell:
                collider.size =
                    new Vector2(
                        size.x * 0.62f,
                        size.y * 0.58f);
                collider.offset =
                    new Vector2(
                        0f,
                        -size.y * 0.02f);
                break;
        }
    }
}
