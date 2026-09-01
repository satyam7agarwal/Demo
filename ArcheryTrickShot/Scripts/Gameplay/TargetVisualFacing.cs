using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class TargetVisualFacing : MonoBehaviour
{
    private const string PedestalSpritePath = "Environment/Target_BaseStone";
    private const string PedestalObjectName = "PedestalBase";

    // Tuned for the cleaned/cropped pedestal sprite.
    private const float PedestalHorizontalOffset = 0.00f;
    private const float PedestalVerticalOverlap = 0.6f;
    private const float PedestalDepth = 0.10f;

    private static readonly Vector3 PedestalVisualScale =
        new Vector3(0.72f, 0.32f, 1f);

    private static Sprite cachedPedestalSprite;

    [SerializeField]
    private SpriteRenderer bodyRenderer;

    [Header("Premium Target Variants")]
    [SerializeField] private Sprite wood;

    [FormerlySerializedAs("steel")]
    [SerializeField] private Sprite ruins;

    [SerializeField] private Sprite crystal;
    [SerializeField] private Sprite molten;

    [FormerlySerializedAs("mechanical")]
    [SerializeField] private Sprite clockwork;

    [SerializeField, HideInInspector]
    private Vector3 baseBodyScale = Vector3.one;

    [SerializeField, HideInInspector]
    private Vector3 baseBodyLocalPosition = Vector3.zero;

    [SerializeField, HideInInspector]
    private bool calibrationConfigured;

    private SpriteRenderer pedestalRenderer;
    private PolygonCollider2D pedestalCollider;
    private TargetContactSensor pedestalContactSensor;

    // Reused buffer so configuring the polygon does not allocate one list per path.
    private readonly List<Vector2> physicsShapePoints = new List<Vector2>(32);

    public void Configure(
        SpriteRenderer renderer,
        Sprite woodSprite,
        Sprite ruinsSprite,
        Sprite crystalSprite,
        Sprite moltenSprite,
        Sprite clockworkSprite)
    {
        bodyRenderer = renderer;
        wood = woodSprite;
        ruins = ruinsSprite;
        crystal = crystalSprite;
        molten = moltenSprite;
        clockwork = clockworkSprite;

        if (bodyRenderer != null)
        {
            baseBodyScale = bodyRenderer.transform.localScale;
            baseBodyLocalPosition = bodyRenderer.transform.localPosition;
            calibrationConfigured = true;
        }
    }

    public void ApplyVisual(
        LevelData.TargetStyle style,
        LevelData.TargetFacing facing,
        float archerWorldX)
    {
        ResolveRenderer();
        EnsurePedestal();

        if (bodyRenderer == null)
            return;

        Sprite selected = GetSprite(style);
        if (selected != null)
            bodyRenderer.sprite = selected;

        bodyRenderer.transform.localScale = baseBodyScale;
        bodyRenderer.transform.localPosition = baseBodyLocalPosition;
        bodyRenderer.transform.localRotation = Quaternion.identity;

        PositionPedestalBelowTarget();

        bool faceRight;

        switch (facing)
        {
            case LevelData.TargetFacing.Right:
                faceRight = true;
                break;

            case LevelData.TargetFacing.Left:
                faceRight = false;
                break;

            default:
                faceRight = transform.position.x < archerWorldX;
                break;
        }

        bodyRenderer.flipX = faceRight;

        if (pedestalRenderer != null)
        {
            pedestalRenderer.flipX = false;

            // Keep the pedestal in front of the background. Do not use
            // bodyRenderer.sortingOrder - 1 because the background is also 0.
            pedestalRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
            pedestalRenderer.sortingOrder = bodyRenderer.sortingOrder;
        }

        Target target = GetComponent<Target>();
        target?.ApplyFacing(faceRight);
    }

    public void ApplyFacing(
        LevelData.TargetFacing facing,
        float archerWorldX)
    {
        ApplyVisual(
            LevelData.TargetStyle.Wood,
            facing,
            archerWorldX);
    }

    private void ResolveRenderer()
    {
        if (bodyRenderer == null)
        {
            Transform body = transform.Find("VisualRoot/Body");

            if (body != null)
                bodyRenderer = body.GetComponent<SpriteRenderer>();
        }

        if (bodyRenderer != null && !calibrationConfigured)
        {
            baseBodyScale = bodyRenderer.transform.localScale;
            baseBodyLocalPosition = bodyRenderer.transform.localPosition;
            calibrationConfigured = true;
        }
    }

    private void EnsurePedestal()
    {
        if (bodyRenderer == null)
            return;

        if (pedestalRenderer == null)
        {
            Transform visualRoot =
                bodyRenderer.transform.parent != null
                    ? bodyRenderer.transform.parent
                    : bodyRenderer.transform;

            Transform existing = visualRoot.Find(PedestalObjectName);

            if (existing != null)
                pedestalRenderer = existing.GetComponent<SpriteRenderer>();

            if (pedestalRenderer == null)
            {
                GameObject pedestal = new GameObject(PedestalObjectName);
                pedestal.transform.SetParent(visualRoot, false);
                pedestalRenderer = pedestal.AddComponent<SpriteRenderer>();
            }
        }

        if (cachedPedestalSprite == null)
            cachedPedestalSprite = Resources.Load<Sprite>(PedestalSpritePath);

        if (pedestalRenderer == null)
            return;

        pedestalRenderer.sprite = cachedPedestalSprite;
        pedestalRenderer.color = Color.white;
        pedestalRenderer.transform.localRotation = Quaternion.identity;
        pedestalRenderer.transform.localScale = PedestalVisualScale;

        EnsurePedestalCollision();
    }

    private void EnsurePedestalCollision()
    {
        if (pedestalRenderer == null || pedestalRenderer.sprite == null)
            return;

        // v17 used a BoxCollider2D based on the full sprite rectangle. That can
        // stop an arrow slightly before the visible pedestal on slanted edges.
        // Disable/remove it and use one PolygonCollider2D instead.
        BoxCollider2D oldBox = pedestalRenderer.GetComponent<BoxCollider2D>();
        if (oldBox != null)
        {
            oldBox.enabled = false;

            if (Application.isPlaying)
                Destroy(oldBox);
            else
                DestroyImmediate(oldBox);
        }

        if (pedestalCollider == null)
        {
            pedestalCollider = pedestalRenderer.GetComponent<PolygonCollider2D>();

            if (pedestalCollider == null)
                pedestalCollider = pedestalRenderer.gameObject.AddComponent<PolygonCollider2D>();
        }

        pedestalCollider.isTrigger = true;

        // Explicitly copy the Sprite Importer's physics shape to the collider.
        // In Target_BaseStone import settings keep "Generate Physics Shape" ON.
        // This makes collision follow the visible pedestal rather than its
        // rectangular texture bounds.
        ApplySpritePhysicsShape(pedestalRenderer.sprite, pedestalCollider);

        if (pedestalContactSensor == null)
        {
            pedestalContactSensor =
                pedestalRenderer.GetComponent<TargetContactSensor>();

            if (pedestalContactSensor == null)
            {
                pedestalContactSensor =
                    pedestalRenderer.gameObject.AddComponent<TargetContactSensor>();
            }
        }

        pedestalContactSensor.Configure(TargetContactKind.PhysicalPart);
    }

    private void ApplySpritePhysicsShape(
        Sprite sprite,
        PolygonCollider2D polygonCollider)
    {
        int shapeCount = sprite.GetPhysicsShapeCount();

        if (shapeCount <= 0)
        {
            Debug.LogWarning(
                $"[{nameof(TargetVisualFacing)}] No physics shape was generated for " +
                $"'{sprite.name}'. Enable 'Generate Physics Shape' in the sprite " +
                "import settings and click Apply.",
                sprite);
            return;
        }

        polygonCollider.pathCount = shapeCount;

        for (int pathIndex = 0; pathIndex < shapeCount; pathIndex++)
        {
            physicsShapePoints.Clear();
            sprite.GetPhysicsShape(pathIndex, physicsShapePoints);
            polygonCollider.SetPath(pathIndex, physicsShapePoints);
        }
    }

    private void PositionPedestalBelowTarget()
    {
        if (bodyRenderer == null ||
            bodyRenderer.sprite == null ||
            pedestalRenderer == null ||
            pedestalRenderer.sprite == null)
        {
            return;
        }

        // Calculate placement from the selected target sprite so the pedestal
        // stays under the feet for all target variants.
        Bounds bodyBounds = bodyRenderer.sprite.bounds;
        float bodyScaleY = bodyRenderer.transform.localScale.y;
        float bodyBottom =
            bodyRenderer.transform.localPosition.y +
            Mathf.Min(
                bodyBounds.min.y * bodyScaleY,
                bodyBounds.max.y * bodyScaleY);

        Bounds pedestalBounds = pedestalRenderer.sprite.bounds;
        float pedestalScaleY = pedestalRenderer.transform.localScale.y;
        float pedestalTopFromPivot =
            Mathf.Max(
                pedestalBounds.min.y * pedestalScaleY,
                pedestalBounds.max.y * pedestalScaleY);

        float pedestalY =
            bodyBottom +
            PedestalVerticalOverlap -
            pedestalTopFromPivot;

        pedestalRenderer.transform.localPosition =
            new Vector3(
                baseBodyLocalPosition.x + PedestalHorizontalOffset,
                pedestalY,
                PedestalDepth);
    }

    private Sprite GetSprite(LevelData.TargetStyle style)
    {
        switch (style)
        {
            case LevelData.TargetStyle.Ruins:
                return ruins != null ? ruins : wood;

            case LevelData.TargetStyle.Crystal:
                return crystal != null ? crystal : wood;

            case LevelData.TargetStyle.Molten:
                return molten != null ? molten : wood;

            case LevelData.TargetStyle.Clockwork:
                return clockwork != null ? clockwork : wood;

            default:
                return wood;
        }
    }
}
