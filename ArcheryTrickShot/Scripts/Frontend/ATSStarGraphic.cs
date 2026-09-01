using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight vector star used by the frontend so level ratings do not depend on
/// a TMP font containing the Unicode star glyph. The mesh is rebuilt only when the
/// graphic changes, not every frame.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ATSStarGraphic : MaskableGraphic
{
    [SerializeField, Range(0.2f, 0.8f)] private float innerRadius = 0.46f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (radius <= 0.001f)
            return;

        UIVertex centerVertex = UIVertex.simpleVert;
        centerVertex.position = center;
        centerVertex.color = color;
        vh.AddVert(centerVertex);

        const int vertexCount = 10;
        for (int i = 0; i < vertexCount; i++)
        {
            float angle = Mathf.Deg2Rad * (90f - i * 36f);
            float r = (i & 1) == 0 ? radius : radius * innerRadius;
            Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = p;
            vertex.color = color;
            vh.AddVert(vertex);
        }

        for (int i = 0; i < vertexCount; i++)
        {
            int current = i + 1;
            int next = ((i + 1) % vertexCount) + 1;
            vh.AddTriangle(0, current, next);
        }
    }
}
