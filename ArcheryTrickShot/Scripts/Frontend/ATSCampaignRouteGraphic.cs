using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight responsive campaign route renderer. Points are normalized to the
/// map rect, so the path stays aligned on phones and tablets without hard-coded
/// pixel geometry. It renders dashed quad segments directly into one UI mesh.
/// </summary>
public sealed class ATSCampaignRouteGraphic : MaskableGraphic
{
    [SerializeField] private List<Vector2> points = new List<Vector2>();
    [SerializeField, Min(1f)] private float thickness = 8f;
    [SerializeField, Min(1f)] private float dashLength = 22f;
    [SerializeField, Min(0f)] private float gapLength = 12f;

    public void Configure(IReadOnlyList<Vector2> normalizedPoints, float lineThickness, float dash, float gap, Color lineColor)
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
        color = lineColor;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points == null || points.Count < 2)
            return;

        Rect rect = rectTransform.rect;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = NormalizedToLocal(points[i], rect);
            Vector2 b = NormalizedToLocal(points[i + 1], rect);
            AddDashedSegment(vh, a, b);
        }
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetVerticesDirty();
    }

    private static Vector2 NormalizedToLocal(Vector2 p, Rect rect)
    {
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01(p.x)),
            Mathf.Lerp(rect.yMin, rect.yMax, Mathf.Clamp01(p.y)));
    }

    private void AddDashedSegment(VertexHelper vh, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length < 0.01f)
            return;

        Vector2 dir = delta / length;
        float stride = Mathf.Max(1f, dashLength + gapLength);
        for (float start = 0f; start < length; start += stride)
        {
            float end = Mathf.Min(start + dashLength, length);
            AddQuad(vh, a + dir * start, a + dir * end);
        }
    }

    private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
        int start = vh.currentVertCount;
        Color32 c = color;

        vh.AddVert(a - n, c, Vector2.zero);
        vh.AddVert(a + n, c, Vector2.zero);
        vh.AddVert(b + n, c, Vector2.zero);
        vh.AddVert(b - n, c, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
