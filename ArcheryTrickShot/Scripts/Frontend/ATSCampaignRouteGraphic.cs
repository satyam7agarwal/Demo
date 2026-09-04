using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Responsive campaign route renderer using ordinary Unity UI Images.
///
/// V6 originally rendered the route as one custom MaskableGraphic mesh. On some
/// project/UI configurations that mesh did not appear even though the level
/// nodes were positioned correctly. This implementation intentionally uses
/// normal UI elements so the route follows the same rendering path as the rest
/// of the frontend.
///
/// Points remain normalized to this RectTransform, so the path stays aligned
/// across phones, tablets and different landscape aspect ratios.
/// </summary>
public sealed class ATSCampaignRouteGraphic : MonoBehaviour
{
    private readonly List<Vector2> points = new List<Vector2>();
    private readonly List<RectTransform> dashRects = new List<RectTransform>();

    private float thickness = 8f;
    private float dashLength = 22f;
    private float gapLength = 12f;
    private Color lineColor = Color.white;

    private RectTransform cachedRect;
    private Vector2 lastSize = new Vector2(-1f, -1f);
    private bool rebuildPending;

    private RectTransform Rect
    {
        get
        {
            if (cachedRect == null)
                cachedRect = transform as RectTransform;
            return cachedRect;
        }
    }

    public void Configure(IReadOnlyList<Vector2> normalizedPoints, float lineThickness, float dash, float gap, Color color)
    {
        points.Clear();
        if (normalizedPoints != null)
        {
            for (int i = 0; i < normalizedPoints.Count; i++)
                points.Add(normalizedPoints[i]);
        }

        thickness = Mathf.Max(1f, lineThickness);
        dashLength = Mathf.Max(1f, dash);
        gapLength = Mathf.Max(0f, gap);
        lineColor = color;

        rebuildPending = true;
        TryRebuild();
    }

    private void OnEnable()
    {
        rebuildPending = true;
    }

    private void LateUpdate()
    {
        RectTransform rect = Rect;
        if (rect == null)
            return;

        Vector2 size = rect.rect.size;
        if ((size - lastSize).sqrMagnitude > 0.25f)
            rebuildPending = true;

        if (rebuildPending)
            TryRebuild();
    }

    private void OnRectTransformDimensionsChange()
    {
        rebuildPending = true;
    }

    private void TryRebuild()
    {
        RectTransform rectTransform = Rect;
        if (rectTransform == null || points.Count < 2)
        {
            SetVisibleDashCount(0);
            return;
        }

        Rect rect = rectTransform.rect;
        if (rect.width < 2f || rect.height < 2f)
        {
            rebuildPending = true;
            return;
        }

        int used = 0;
        float stride = Mathf.Max(1f, dashLength + gapLength);

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = NormalizedToLocal(points[i], rect);
            Vector2 b = NormalizedToLocal(points[i + 1], rect);
            Vector2 delta = b - a;
            float segmentLength = delta.magnitude;
            if (segmentLength < 0.5f)
                continue;

            Vector2 dir = delta / segmentLength;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            for (float start = 0f; start < segmentLength; start += stride)
            {
                float end = Mathf.Min(start + dashLength, segmentLength);
                float visibleLength = Mathf.Max(1f, end - start);
                Vector2 centre = a + dir * ((start + end) * 0.5f);

                RectTransform dashRect = GetDash(used++);
                dashRect.gameObject.SetActive(true);
                dashRect.anchoredPosition = centre;
                dashRect.sizeDelta = new Vector2(visibleLength, thickness);
                dashRect.localRotation = Quaternion.Euler(0f, 0f, angle);

                RawImage image = dashRect.GetComponent<RawImage>();
                image.color = lineColor;
            }
        }

        SetVisibleDashCount(used);
        lastSize = rect.size;
        rebuildPending = false;
    }

    private RectTransform GetDash(int index)
    {
        if (index < dashRects.Count && dashRects[index] != null)
            return dashRects[index];

        GameObject dash = new GameObject(
            "Dash_" + index,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        dash.transform.SetParent(transform, false);

        RectTransform rect = dash.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;

        RawImage image = dash.GetComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.raycastTarget = false;
        image.maskable = true;
        image.color = lineColor;

        dashRects.Add(rect);
        return rect;
    }

    private void SetVisibleDashCount(int count)
    {
        for (int i = 0; i < dashRects.Count; i++)
        {
            if (dashRects[i] != null)
                dashRects[i].gameObject.SetActive(i < count);
        }
    }

    private static Vector2 NormalizedToLocal(Vector2 p, Rect rect)
    {
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01(p.x)),
            Mathf.Lerp(rect.yMin, rect.yMax, Mathf.Clamp01(p.y)));
    }
}
