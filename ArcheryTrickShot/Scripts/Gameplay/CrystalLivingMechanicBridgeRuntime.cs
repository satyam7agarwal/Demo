using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic bridge for living Crystal-Cavern unlock sources.
///
/// This deliberately contains no level-number special cases:
/// - FlyingBirdTarget groups can unlock CrystalGates with the same group ID.
/// - CrystalGateData can optionally replace the legacy leaf art with one
///   authored Resources sprite while retaining the proven gate collision/runtime.
///
/// LevelData remains the source of truth.
/// </summary>
[DefaultExecutionOrder(1600)]
[DisallowMultipleComponent]
public sealed class CrystalLivingMechanicBridgeRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private Transform lastLevelObjects;

    private readonly List<FlyingBirdTarget> trackedBirds =
        new List<FlyingBirdTarget>();

    private readonly Dictionary<string, int> remainingBirdsByGroup =
        new Dictionary<string, int>();

    private readonly HashSet<string> crystalGateGroups =
        new HashSet<string>();

    private static FieldInfo currentLevelField;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<CrystalLivingMechanicBridgeRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                CrystalLivingMechanicBridgeRuntime>();
        }
    }

    private void Awake()
    {
        levelManager =
            GetComponent<LevelManager>();

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
            levelManager.transform.Find(
                "LevelObjects");

        if (levelObjects == null)
        {
            if (lastLevelObjects != null)
                ResetTrackedBirds();

            lastLevelObjects = null;
            return;
        }

        if (levelObjects == lastLevelObjects)
            return;

        ResetTrackedBirds();
        lastLevelObjects = levelObjects;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        BuildCrystalGateGroupSet(
            level.CrystalGates);

        ApplyAuthoredGatePresentation(
            level.CrystalGates,
            levelObjects);

        RegisterLivingUnlockSources(
            levelObjects);
    }

    private LevelData ResolveCurrentLevel()
    {
        if (currentLevelField == null ||
            levelManager == null)
        {
            return null;
        }

        return currentLevelField.GetValue(
            levelManager) as LevelData;
    }

    private void BuildCrystalGateGroupSet(
        LevelData.CrystalGateData[] gates)
    {
        crystalGateGroups.Clear();

        if (gates == null)
            return;

        for (int i = 0; i < gates.Length; i++)
        {
            LevelData.CrystalGateData gate =
                gates[i];

            if (gate == null)
                continue;

            crystalGateGroups.Add(
                Normalize(
                    gate.UnlockGroupId));
        }
    }

    private void RegisterLivingUnlockSources(
        Transform parent)
    {
        if (parent == null ||
            crystalGateGroups.Count == 0)
        {
            return;
        }

        FlyingBirdTarget[] birds =
            parent.GetComponentsInChildren<
                FlyingBirdTarget>(true);

        for (int i = 0; i < birds.Length; i++)
        {
            FlyingBirdTarget bird =
                birds[i];

            if (bird == null ||
                bird.IsConsumed)
            {
                continue;
            }

            string group =
                Normalize(
                    bird.UnlockGroupId);

            if (!crystalGateGroups.Contains(group))
                continue;

            bird.Hit +=
                OnLivingSourceHit;

            trackedBirds.Add(
                bird);

            if (remainingBirdsByGroup.TryGetValue(
                    group,
                    out int remaining))
            {
                remainingBirdsByGroup[group] =
                    remaining + 1;
            }
            else
            {
                remainingBirdsByGroup[group] =
                    1;
            }
        }
    }

    private void OnLivingSourceHit(
        FlyingBirdHitResult result)
    {
        string group =
            Normalize(
                result.UnlockGroupId);

        if (!remainingBirdsByGroup.TryGetValue(
                group,
                out int remaining))
        {
            return;
        }

        remaining =
            Mathf.Max(
                0,
                remaining - 1);

        remainingBirdsByGroup[group] =
            remaining;

        if (remaining > 0)
            return;

        CrystalWorldMechanicRuntime crystalRuntime =
            levelManager != null
                ? levelManager.GetComponent<
                    CrystalWorldMechanicRuntime>()
                : null;

        crystalRuntime?.ActivateGroup(
            group,
            result.WorldPoint);
    }

    private static void ApplyAuthoredGatePresentation(
        LevelData.CrystalGateData[] gates,
        Transform parent)
    {
        if (gates == null ||
            parent == null)
        {
            return;
        }

        for (int i = 0; i < gates.Length; i++)
        {
            LevelData.CrystalGateData data =
                gates[i];

            if (data == null ||
                string.IsNullOrWhiteSpace(
                    data.VisualResourcePath))
            {
                continue;
            }

            string group =
                Normalize(
                    data.UnlockGroupId);

            string expectedName =
                "CrystalGate_" + group;

            for (int childIndex = 0;
                 childIndex < parent.childCount;
                 childIndex++)
            {
                Transform child =
                    parent.GetChild(
                        childIndex);

                if (child == null ||
                    !string.Equals(
                        child.name,
                        expectedName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ApplyAuthoredGatePresentation(
                    child,
                    data);
            }
        }
    }

    private static void ApplyAuthoredGatePresentation(
        Transform gateRoot,
        LevelData.CrystalGateData data)
    {
        Sprite sprite =
            Resources.Load<Sprite>(
                data.VisualResourcePath.Trim());

        if (sprite == null)
        {
            Debug.LogWarning(
                "[CrystalLivingBridge] Missing gate sprite at Resources/" +
                data.VisualResourcePath,
                gateRoot);
            return;
        }

        if (data.HideDefaultVisual)
        {
            SpriteRenderer[] oldRenderers =
                gateRoot.GetComponentsInChildren<
                    SpriteRenderer>(true);

            for (int i = 0;
                 i < oldRenderers.Length;
                 i++)
            {
                SpriteRenderer oldRenderer =
                    oldRenderers[i];

                if (oldRenderer != null)
                    oldRenderer.enabled = false;
            }
        }

        Transform existing =
            gateRoot.Find(
                "AuthoredCrystalGateVisual");

        GameObject visualObject =
            existing != null
                ? existing.gameObject
                : new GameObject(
                    "AuthoredCrystalGateVisual",
                    typeof(SpriteRenderer));

        if (existing == null)
            visualObject.transform.SetParent(
                gateRoot,
                false);

        visualObject.transform.localPosition =
            new Vector3(
                data.VisualOffset.x,
                data.VisualOffset.y,
                -0.03f);

        visualObject.transform.localRotation =
            Quaternion.identity;

        float fitScale = 1f;

        if (data.VisualWorldWidth > 0.001f &&
            sprite.bounds.size.x > 0.001f)
        {
            fitScale =
                data.VisualWorldWidth /
                sprite.bounds.size.x;
        }

        visualObject.transform.localScale =
            Vector3.one *
            fitScale;

        SpriteRenderer renderer =
            visualObject.GetComponent<
                SpriteRenderer>();

        renderer.sprite =
            sprite;

        renderer.color =
            Color.white;

        renderer.sortingOrder =
            data.VisualSortingOrder;

        CrystalAuthoredGateVisualRuntime runtime =
            gateRoot.GetComponent<
                CrystalAuthoredGateVisualRuntime>();

        if (runtime == null)
        {
            runtime =
                gateRoot.gameObject.AddComponent<
                    CrystalAuthoredGateVisualRuntime>();
        }

        Transform top =
            gateRoot.Find(
                "CrystalGateTop");

        Transform bottom =
            gateRoot.Find(
                "CrystalGateBottom");

        runtime.Configure(
            renderer,
            top,
            bottom);
    }

    private void ResetTrackedBirds()
    {
        for (int i = 0;
             i < trackedBirds.Count;
             i++)
        {
            FlyingBirdTarget bird =
                trackedBirds[i];

            if (bird != null)
            {
                bird.Hit -=
                    OnLivingSourceHit;
            }
        }

        trackedBirds.Clear();
        remainingBirdsByGroup.Clear();
        crystalGateGroups.Clear();
    }

    private void OnDestroy()
    {
        ResetTrackedBirds();
    }

    private static string Normalize(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "default"
            : value.Trim();
    }
}

/// <summary>
/// Keeps an authored one-piece magical gate visual synchronized with the
/// existing two-collider CrystalGateRuntime. The hidden legacy leaves still
/// perform the proven physical opening; this overlay fades as soon as those
/// leaves begin retracting.
/// </summary>
[DisallowMultipleComponent]
internal sealed class CrystalAuthoredGateVisualRuntime : MonoBehaviour
{
    private SpriteRenderer renderer;
    private Transform topHalf;
    private Transform bottomHalf;
    private Vector3 topClosed;
    private Vector3 bottomClosed;
    private bool opening;
    private float fadeElapsed;

    public void Configure(
        SpriteRenderer configuredRenderer,
        Transform top,
        Transform bottom)
    {
        renderer =
            configuredRenderer;

        topHalf =
            top;

        bottomHalf =
            bottom;

        topClosed =
            topHalf != null
                ? topHalf.localPosition
                : Vector3.zero;

        bottomClosed =
            bottomHalf != null
                ? bottomHalf.localPosition
                : Vector3.zero;

        opening = false;
        fadeElapsed = 0f;

        if (renderer != null)
        {
            Color color =
                renderer.color;

            color.a = 1f;
            renderer.color = color;
            renderer.enabled = true;
        }
    }

    private void LateUpdate()
    {
        if (renderer == null ||
            opening)
        {
            if (opening)
                FadeVisual();

            return;
        }

        bool topMoved =
            topHalf != null &&
            (topHalf.localPosition -
             topClosed).sqrMagnitude >
            0.0001f;

        bool bottomMoved =
            bottomHalf != null &&
            (bottomHalf.localPosition -
             bottomClosed).sqrMagnitude >
            0.0001f;

        if (topMoved ||
            bottomMoved)
        {
            opening = true;
            fadeElapsed = 0f;
        }
    }

    private void FadeVisual()
    {
        if (renderer == null)
            return;

        const float duration =
            0.20f;

        fadeElapsed +=
            Time.deltaTime;

        float t =
            Mathf.Clamp01(
                fadeElapsed /
                duration);

        Color color =
            renderer.color;

        color.a =
            1f - t;

        renderer.color =
            color;

        if (t >= 1f)
            renderer.enabled = false;
    }
}
