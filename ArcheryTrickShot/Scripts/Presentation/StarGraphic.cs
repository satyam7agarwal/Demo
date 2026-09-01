using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight procedural five-point star for the result UI.
///
/// This intentionally avoids Unicode ★/☆ characters so TextMeshPro font atlases
/// are never involved. The same star renders on Windows, Android and iOS without
/// adding a sprite asset or extending LiberationSans SDF.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class StarGraphic : MaskableGraphic
{
    private const int PointCount = 10;

    [SerializeField]
    [Range(0.2f, 0.8f)]
    private float innerRadiusRatio = 0.46f;

    [SerializeField]
    [Range(-180f, 180f)]
    private float rotationDegrees = -90f;

    protected override void OnPopulateMesh(
        VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect rect =
            GetPixelAdjustedRect();

        float outerRadius =
            Mathf.Max(
                0f,
                Mathf.Min(
                    rect.width,
                    rect.height) * 0.5f);

        if (outerRadius <= 0f)
            return;

        float innerRadius =
            outerRadius *
            Mathf.Clamp(
                innerRadiusRatio,
                0.2f,
                0.8f);

        Vector2 center =
            rect.center;

        UIVertex vertex =
            UIVertex.simpleVert;

        vertex.color =
            color;

        vertex.position =
            center;

        vertexHelper.AddVert(vertex);

        float rotation =
            rotationDegrees *
            Mathf.Deg2Rad;

        for (int index = 0;
             index < PointCount;
             index++)
        {
            float angle =
                rotation +
                index *
                Mathf.PI /
                5f;

            float radius =
                index % 2 == 0
                    ? outerRadius
                    : innerRadius;

            vertex.position =
                center +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) *
                radius;

            vertexHelper.AddVert(vertex);
        }

        for (int index = 0;
             index < PointCount;
             index++)
        {
            int current =
                index + 1;

            int next =
                ((index + 1) %
                 PointCount) + 1;

            vertexHelper.AddTriangle(
                0,
                current,
                next);
        }
    }
}
