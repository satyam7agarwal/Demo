using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic, data-driven presentation layer for all levels.
///
/// LevelManager remains responsible for gameplay objects. This component reads
/// the PresentationSprites / PresentationColliders authored on the active
/// LevelData and builds only environment visuals and matching simple collision.
/// No level number, resource path, position or scale is hardcoded here.
/// </summary>
[DefaultExecutionOrder(1350)]
[DisallowMultipleComponent]
public sealed class LevelPresentationRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private Transform lastLevelObjects;

    private static FieldInfo currentLevelField;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<LevelPresentationRuntime>() == null)
        {
            manager.gameObject.AddComponent<LevelPresentationRuntime>();
        }
    }

    private void Awake()
    {
        levelManager = GetComponent<LevelManager>();

        currentLevelField ??=
            typeof(LevelManager).GetField(
                "currentLevel",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
    }

    private void LateUpdate()
    {
        if (levelManager == null)
            return;

        Transform levelObjects =
            levelManager.transform.Find("LevelObjects");

        if (levelObjects == null)
        {
            lastLevelObjects = null;
            return;
        }

        if (levelObjects == lastLevelObjects)
            return;

        lastLevelObjects = levelObjects;

        LevelData level = ResolveCurrentLevel();
        if (level == null)
            return;

        Dictionary<string, Transform> presentationVisuals =
            SpawnPresentationSprites(
                level.PresentationSprites,
                levelObjects);

        SpawnPresentationColliders(
            level.PresentationColliders,
            levelObjects,
            presentationVisuals);

        ApplyTargetPresentation(
            level.Targets,
            levelObjects);
    }

    private LevelData ResolveCurrentLevel()
    {
        if (currentLevelField == null ||
            levelManager == null)
        {
            return null;
        }

        return currentLevelField.GetValue(levelManager)
            as LevelData;
    }

    private static Dictionary<string, Transform> SpawnPresentationSprites(
        LevelData.PresentationSpriteData[] data,
        Transform parent)
    {
        Dictionary<string, Transform> visuals =
            new Dictionary<string, Transform>(StringComparer.Ordinal);

        if (data == null || parent == null)
            return visuals;

        for (int i = 0; i < data.Length; i++)
        {
            LevelData.PresentationSpriteData item = data[i];

            if (item == null ||
                string.IsNullOrWhiteSpace(item.ResourcePath))
            {
                continue;
            }

            Sprite sprite =
                Resources.Load<Sprite>(item.ResourcePath.Trim());

            if (sprite == null)
            {
                Debug.LogWarning(
                    "[LevelPresentation] Missing sprite at Resources/" +
                    item.ResourcePath,
                    parent);
                continue;
            }

            string objectName =
                string.IsNullOrWhiteSpace(item.Name)
                    ? "PresentationSprite_" + i
                    : item.Name.Trim();

            GameObject go =
                new GameObject(
                    objectName,
                    typeof(SpriteRenderer));

            go.transform.SetParent(parent, false);
            go.transform.position =
                new Vector3(
                    item.Position.x,
                    item.Position.y,
                    item.Depth);
            go.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    item.Rotation);

            Vector3 scale =
                new Vector3(
                    item.Scale.x,
                    item.Scale.y,
                    1f);

            if (item.WorldWidth > 0.001f &&
                sprite.bounds.size.x > 0.001f)
            {
                float fitScale =
                    item.WorldWidth /
                    sprite.bounds.size.x;

                scale.x *= fitScale;
                scale.y *= fitScale;
            }

            go.transform.localScale = scale;

            SpriteRenderer renderer =
                go.GetComponent<SpriteRenderer>();

            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = item.SortingOrder;
            renderer.flipX = item.FlipX;
            renderer.flipY = item.FlipY;

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                string key = item.Name.Trim();

                if (!visuals.TryAdd(key, go.transform))
                {
                    Debug.LogWarning(
                        "[LevelPresentation] Duplicate PresentationSprite Name '" +
                        key +
                        "'. MatchVisual references will use the first instance.",
                        parent);
                }
            }
        }

        return visuals;
    }

    private static void SpawnPresentationColliders(
        LevelData.PresentationColliderData[] data,
        Transform parent,
        IReadOnlyDictionary<string, Transform> visuals)
    {
        if (data == null || parent == null)
            return;

        for (int i = 0; i < data.Length; i++)
        {
            LevelData.PresentationColliderData item = data[i];
            if (item == null)
                continue;

            string objectName =
                string.IsNullOrWhiteSpace(item.Name)
                    ? "PresentationCollider_" + i
                    : item.Name.Trim();

            Transform matchedVisual = null;
            string matchVisual =
                string.IsNullOrWhiteSpace(item.MatchVisual)
                    ? string.Empty
                    : item.MatchVisual.Trim();

            if (matchVisual.Length > 0 &&
                visuals != null &&
                !visuals.TryGetValue(matchVisual, out matchedVisual))
            {
                Debug.LogWarning(
                    "[LevelPresentation] Collider '" +
                    objectName +
                    "' references missing PresentationSprite '" +
                    matchVisual +
                    "'. Falling back to world-space placement.",
                    parent);
            }

            GameObject go = new GameObject(objectName);

            if (matchedVisual != null)
            {
                // Parenting is intentional: WorldWidth or any live Transform
                // tuning on the visual automatically scales/moves the collider
                // with it, so visual size remains the single source of truth.
                go.transform.SetParent(matchedVisual, false);
                go.transform.localPosition =
                    new Vector3(
                        item.Position.x,
                        item.Position.y,
                        0f);
                go.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        item.Rotation);
                go.transform.localScale =
                    new Vector3(
                        item.Scale.x,
                        item.Scale.y,
                        1f);
            }
            else
            {
                go.transform.SetParent(parent, false);
                go.transform.position =
                    new Vector3(
                        item.Position.x,
                        item.Position.y,
                        0f);
                go.transform.rotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        item.Rotation);
                go.transform.localScale =
                    new Vector3(
                        item.Scale.x,
                        item.Scale.y,
                        1f);
            }

            if (item.Shape ==
                    LevelData.PresentationColliderShape.Polygon &&
                item.Points != null &&
                item.Points.Length >= 3)
            {
                PolygonCollider2D polygon =
                    go.AddComponent<PolygonCollider2D>();

                polygon.points = item.Points;
                polygon.isTrigger = item.IsTrigger;
            }
            else
            {
                BoxCollider2D box =
                    go.AddComponent<BoxCollider2D>();

                box.size = item.Size;
                box.offset = item.Offset;
                box.isTrigger = item.IsTrigger;
            }
        }
    }

    private static void ApplyTargetPresentation(
        LevelData.TargetData[] targetData,
        Transform parent)
    {
        if (targetData == null ||
            targetData.Length == 0 ||
            parent == null)
        {
            return;
        }

        Target[] targets =
            parent.GetComponentsInChildren<Target>(true);

        if (targets == null || targets.Length == 0)
            return;

        bool[] used = new bool[targets.Length];

        for (int dataIndex = 0;
             dataIndex < targetData.Length;
             dataIndex++)
        {
            LevelData.TargetData item = targetData[dataIndex];
            if (item == null)
                continue;

            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;

            for (int targetIndex = 0;
                 targetIndex < targets.Length;
                 targetIndex++)
            {
                if (used[targetIndex] || targets[targetIndex] == null)
                    continue;

                float distance =
                    Vector2.SqrMagnitude(
                        (Vector2)targets[targetIndex].transform.position -
                        item.Position);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestIndex = targetIndex;
            }

            if (bestIndex < 0)
                continue;

            used[bestIndex] = true;

            TargetVisualFacing visual =
                targets[bestIndex]
                    .GetComponent<TargetVisualFacing>();

            if (visual != null)
            {
                visual.SetPedestalVisible(
                    !item.HidePedestalBase);
            }
        }
    }
}
