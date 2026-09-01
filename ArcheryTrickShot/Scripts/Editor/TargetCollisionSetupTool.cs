#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds Target.prefab collision without touching the authored scoring-face
/// polygon or ScoreMarker positions.
///
/// The old full-silhouette root collider is removed. Three non-scoring
/// physical trigger polygons are created instead: rear/rim body, left leg and
/// right leg. ScoringFace remains the only collider that can award score.
/// </summary>
public static class TargetCollisionSetupTool
{
    private const string TargetPrefabPath =
        "Assets/ArcheryTrickShot/Resources/Prefabs/Gameplay/Target.prefab";

    [MenuItem(
        "Tools/Archery Trick Shot/Setup Target Collision v6")]
    public static void SetupTargetCollision()
    {
        GameObject prefabRoot =
            PrefabUtility.LoadPrefabContents(TargetPrefabPath);

        if (prefabRoot == null)
        {
            Debug.LogError(
                "[Target Collision] Could not load Target.prefab.");
            return;
        }

        try
        {
            Target target = prefabRoot.GetComponent<Target>();
            if (target == null)
            {
                Debug.LogError(
                    "[Target Collision] Target.cs is missing.");
                return;
            }

            // Remove the obsolete all-in-one physical outline from the root.
            // That collider was the reason valid scoring shots could stop
            // before reaching ScoringFace.
            Collider2D[] rootColliders =
                prefabRoot.GetComponents<Collider2D>();

            foreach (Collider2D collider in rootColliders)
                Object.DestroyImmediate(collider, true);

            Transform collisionRoot =
                GetOrCreateChild(
                    prefabRoot.transform,
                    "CollisionRoot");

            collisionRoot.localPosition = Vector3.zero;
            collisionRoot.localRotation = Quaternion.identity;
            collisionRoot.localScale = Vector3.one;

            Transform scoringFaceTransform =
                prefabRoot.transform.Find("ScoringFace");

            if (scoringFaceTransform == null)
            {
                scoringFaceTransform =
                    collisionRoot.Find("ScoringFace");
            }

            if (scoringFaceTransform == null)
            {
                Debug.LogError(
                    "[Target Collision] ScoringFace was not found. " +
                    "Nothing was changed beyond removing root colliders.");
                return;
            }

            // Preserve the user's authored scoring geometry exactly.
            if (scoringFaceTransform.parent != collisionRoot)
            {
                scoringFaceTransform.SetParent(
                    collisionRoot,
                    true);
            }

            // BoxCollider2D was an earlier experiment. The curved polygon is
            // the single final scoring detector.
            BoxCollider2D[] obsoleteBoxes =
                scoringFaceTransform.GetComponents<BoxCollider2D>();

            foreach (BoxCollider2D box in obsoleteBoxes)
                Object.DestroyImmediate(box, true);

            PolygonCollider2D scoringFace =
                scoringFaceTransform
                    .GetComponent<PolygonCollider2D>();

            if (scoringFace == null)
            {
                Debug.LogError(
                    "[Target Collision] ScoringFace needs its authored " +
                    "PolygonCollider2D. Physical parts were not created.");
                return;
            }

            scoringFace.isTrigger = true;

            TargetContactSensor scoringSensor =
                GetOrAddSensor(scoringFaceTransform.gameObject);

            scoringSensor.Configure(
                TargetContactKind.ScoringFace);

            Transform physicalParts =
                GetOrCreateChild(
                    collisionRoot,
                    "PhysicalParts");

            physicalParts.localPosition = Vector3.zero;
            physicalParts.localRotation = Quaternion.identity;
            physicalParts.localScale = Vector3.one;

            RemoveAllChildren(physicalParts);

            CreatePhysicalPart(
                physicalParts,
                "PhysicalBody",
                new[]
                {
                    // Concave C-shaped body/rim. The open notch on the left is
                    // deliberate: ScoringFace owns that entire valid scoring
                    // route, so this collider can never stop the arrow first.
                    new Vector2(-1.60f,  3.35f),
                    new Vector2(-0.70f,  3.70f),
                    new Vector2( 0.80f,  3.60f),
                    new Vector2( 1.55f,  3.00f),
                    new Vector2( 1.90f,  1.80f),
                    new Vector2( 1.95f, -1.60f),
                    new Vector2( 1.55f, -2.70f),
                    new Vector2( 0.65f, -3.10f),
                    new Vector2(-1.45f, -2.85f),
                    new Vector2(-1.10f, -2.05f),
                    new Vector2(-1.10f,  2.85f),
                });

            CreatePhysicalPart(
                physicalParts,
                "PhysicalLeftLeg",
                new[]
                {
                    new Vector2(-1.55f, -2.55f),
                    new Vector2(-0.45f, -2.45f),
                    new Vector2(-0.78f, -3.55f),
                    new Vector2(-1.00f, -4.60f),
                    new Vector2(-0.98f, -5.12f),
                    new Vector2(-2.95f, -5.12f),
                    new Vector2(-2.58f, -4.20f),
                    new Vector2(-2.08f, -3.18f),
                });

            CreatePhysicalPart(
                physicalParts,
                "PhysicalRightLeg",
                new[]
                {
                    new Vector2(-0.20f, -2.55f),
                    new Vector2( 0.78f, -2.42f),
                    new Vector2( 1.10f, -3.45f),
                    new Vector2( 1.55f, -4.45f),
                    new Vector2( 2.55f, -5.12f),
                    new Vector2( 0.12f, -5.12f),
                    new Vector2( 0.16f, -4.10f),
                });

            target.ConfigureCollisionGeometry(
                collisionRoot,
                scoringFace);

            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(prefabRoot);

            PrefabUtility.SaveAsPrefabAsset(
                prefabRoot,
                TargetPrefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Object prefabAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    TargetPrefabPath);

            Selection.activeObject = prefabAsset;
            EditorGUIUtility.PingObject(prefabAsset);

            Debug.Log(
                "[Target Collision] v6 applied. Root full-silhouette collider " +
                "removed. ScoringFace preserved. PhysicalBody + two leg " +
                "colliders now handle only non-scoring impacts.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void CreatePhysicalPart(
        Transform parent,
        string name,
        Vector2[] points)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = Vector3.zero;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = Vector3.one;

        PolygonCollider2D collider =
            part.AddComponent<PolygonCollider2D>();

        collider.isTrigger = true;
        collider.pathCount = 1;
        collider.SetPath(0, points);

        TargetContactSensor sensor =
            part.AddComponent<TargetContactSensor>();

        sensor.Configure(
            TargetContactKind.PhysicalPart);
    }

    private static TargetContactSensor GetOrAddSensor(
        GameObject gameObject)
    {
        TargetContactSensor sensor =
            gameObject.GetComponent<TargetContactSensor>();

        if (sensor == null)
        {
            sensor =
                gameObject.AddComponent<TargetContactSensor>();
        }

        return sensor;
    }

    private static Transform GetOrCreateChild(
        Transform parent,
        string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        return child.transform;
    }

    private static void RemoveAllChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(
                parent.GetChild(i).gameObject,
                true);
        }
    }
}
#endif
