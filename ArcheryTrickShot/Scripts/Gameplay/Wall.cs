using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class Wall : MonoBehaviour
{
    private const string HorizontalSpritePath = "Environment/Wall_Horizontal_Premium";
    private const string VerticalSpritePath = "Environment/Wall_Vertical_Premium";
    private const string VisualChildName = "WallVisual";

    private static Sprite cachedHorizontal;
    private static Sprite cachedVertical;

    private SpriteRenderer visualRenderer;

    public void ApplyPresentation()
    {
        BoxCollider2D collider2D = GetComponent<BoxCollider2D>();
        if (collider2D == null)
            return;

        EnsureVisual();
        if (visualRenderer == null)
            return;

        float z = NormalizeAngle(transform.eulerAngles.z);
        bool vertical = Mathf.Abs(Mathf.Abs(z) - 90f) <= 20f;

        if (cachedHorizontal == null)
            cachedHorizontal = Resources.Load<Sprite>(HorizontalSpritePath);

        if (cachedVertical == null)
            cachedVertical = Resources.Load<Sprite>(VerticalSpritePath);

        Sprite selected = vertical ? cachedVertical : cachedHorizontal;
        if (selected == null)
            return;

        visualRenderer.sprite = selected;
        visualRenderer.color = Color.white;
        visualRenderer.sortingOrder = 8;

        // A vertical wall has dedicated upright artwork. Counter-rotate only
        // the visual so the collider keeps the authored level rotation.
        visualRenderer.transform.localRotation = vertical
            ? Quaternion.Euler(0f, 0f, -z)
            : Quaternion.identity;

        visualRenderer.transform.localPosition = Vector3.zero;
        visualRenderer.transform.localScale = Vector3.one;

        Vector2 spriteSize = selected.bounds.size;
        collider2D.offset = Vector2.zero;

        if (vertical)
        {
            // Because the child visual cancels the parent's 90-degree turn,
            // swap dimensions in root-local space so world collision matches it.
            collider2D.size = new Vector2(spriteSize.y, spriteSize.x);
        }
        else
        {
            collider2D.size = spriteSize;
        }

        // Slightly strengthen smaller blockers without changing the intended
        // level layout dramatically.
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Max(scale.x, vertical ? 0.88f : 0.72f);
        scale.y = Mathf.Max(scale.y, vertical ? 0.88f : 0.72f);
        transform.localScale = scale;
    }

    private void Awake()
    {
        ApplyPresentation();
    }

    private void EnsureVisual()
    {
        if (visualRenderer != null)
            return;

        Transform existing = transform.Find(VisualChildName);
        if (existing != null)
            visualRenderer = existing.GetComponent<SpriteRenderer>();

        if (visualRenderer == null)
        {
            GameObject visual = new GameObject(VisualChildName);
            visual.transform.SetParent(transform, false);
            visualRenderer = visual.AddComponent<SpriteRenderer>();
        }

        // Disable the legacy root renderer so only the calibrated child draws.
        SpriteRenderer rootRenderer = GetComponent<SpriteRenderer>();
        if (rootRenderer != null && rootRenderer != visualRenderer)
            rootRenderer.enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            ApplyPresentation();
    }
#endif

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        return degrees;
    }
}
