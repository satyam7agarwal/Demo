using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic data-driven Crystal Rift system.
///
/// The Entry Rift is a trigger. When a fired arrow reaches it, the arrow is
/// moved to its paired Exit Rift while preserving the arrow's instantaneous
/// velocity direction and (by default) speed. Rigidbody gravityScale is not
/// touched, so Nerissa's gravity arc continues immediately after exit.
///
/// No level-number conditions live in this runtime.
/// </summary>
[DefaultExecutionOrder(1540)]
[DisallowMultipleComponent]
public sealed class CrystalRiftSystemRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private static FieldInfo currentLevelField;
    private static Material sharedLineMaterial;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<CrystalRiftSystemRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                CrystalRiftSystemRuntime>();
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

        EnsureSharedLineMaterial();
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
            lastLevelObjects = null;
            return;
        }

        if (levelObjects == lastLevelObjects)
            return;

        lastLevelObjects =
            levelObjects;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        SpawnPairs(
            level.CrystalRifts,
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

    private static void SpawnPairs(
        LevelData.CrystalRiftPairData[] data,
        Transform parent)
    {
        if (data == null ||
            parent == null)
        {
            return;
        }

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.CrystalRiftPairData item =
                data[i];

            if (item == null)
                continue;

            Sprite entrySprite =
                Resources.Load<Sprite>(
                    item.EntryResourcePath);

            Sprite exitSprite =
                Resources.Load<Sprite>(
                    item.ExitResourcePath);

            if (entrySprite == null ||
                exitSprite == null)
            {
                Debug.LogWarning(
                    "[CrystalRift] Missing entry/exit artwork for pair '" +
                    item.PairId +
                    "'.",
                    parent);
                continue;
            }

            GameObject pairRoot =
                new GameObject(
                    "CrystalRiftPair_" +
                    Normalize(
                        item.PairId));

            pairRoot.transform.SetParent(
                parent,
                false);

            GameObject entry =
                CreateRiftVisual(
                    pairRoot.transform,
                    "EntryRift",
                    item.EntryPosition,
                    item.EntryRotation,
                    item.EntryScale,
                    item.VisualWorldWidth,
                    item.SortingOrder,
                    entrySprite,
                    true);

            GameObject exit =
                CreateRiftVisual(
                    pairRoot.transform,
                    "ExitRift",
                    item.ExitPosition,
                    item.ExitRotation,
                    item.ExitScale,
                    item.VisualWorldWidth,
                    item.SortingOrder,
                    exitSprite,
                    false);

            CircleCollider2D trigger =
                entry.AddComponent<
                    CircleCollider2D>();

            trigger.isTrigger =
                true;

            float worldScale =
                Mathf.Max(
                    0.0001f,
                    Mathf.Max(
                        Mathf.Abs(
                            entry.transform.lossyScale.x),
                        Mathf.Abs(
                            entry.transform.lossyScale.y)));

            trigger.radius =
                item.TriggerRadius /
                worldScale;

            LineRenderer link =
                CreateEnergyLink(
                    pairRoot.transform,
                    item.EntryPosition,
                    item.ExitPosition,
                    item.SortingOrder - 2);

            CrystalRiftEntryRuntime runtime =
                entry.AddComponent<
                    CrystalRiftEntryRuntime>();

            runtime.Configure(
                exit.transform,
                item.ExitPosition,
                item.ExitClearance,
                item.SpeedMultiplier,
                trigger,
                entry.GetComponent<
                    SpriteRenderer>(),
                exit.GetComponent<
                    SpriteRenderer>(),
                link);
        }
    }

    private static GameObject CreateRiftVisual(
        Transform parent,
        string name,
        Vector2 position,
        float rotation,
        Vector2 authoredScale,
        float worldWidth,
        int sortingOrder,
        Sprite sprite,
        bool isEntry)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(SpriteRenderer));

        go.transform.SetParent(
            parent,
            false);

        go.transform.position =
            new Vector3(
                position.x,
                position.y,
                -0.10f);

        go.transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                rotation);

        float fitScale =
            worldWidth > 0.001f &&
            sprite.bounds.size.x > 0.001f
                ? worldWidth /
                  sprite.bounds.size.x
                : 1f;

        go.transform.localScale =
            new Vector3(
                authoredScale.x *
                    fitScale,
                authoredScale.y *
                    fitScale,
                1f);

        SpriteRenderer renderer =
            go.GetComponent<
                SpriteRenderer>();

        renderer.sprite =
            sprite;

        renderer.color =
            Color.white;

        renderer.sortingOrder =
            sortingOrder;

        GameObject glowObject =
            new GameObject(
                isEntry
                    ? "EntryRiftGlow"
                    : "ExitRiftGlow",
                typeof(SpriteRenderer));

        glowObject.transform.SetParent(
            go.transform,
            false);

        SpriteRenderer glow =
            glowObject.GetComponent<
                SpriteRenderer>();

        glow.sprite =
            sprite;

        glow.sortingOrder =
            sortingOrder - 1;

        glow.color =
            isEntry
                ? new Color(
                    0.12f,
                    0.80f,
                    1f,
                    0.16f)
                : new Color(
                    0.88f,
                    0.24f,
                    1f,
                    0.16f);

        glowObject.transform.localScale =
            Vector3.one * 1.08f;

        return go;
    }

    private static LineRenderer CreateEnergyLink(
        Transform parent,
        Vector2 entry,
        Vector2 exit,
        int sortingOrder)
    {
        GameObject go =
            new GameObject(
                "RiftEnergyLink",
                typeof(LineRenderer));

        go.transform.SetParent(
            parent,
            false);

        LineRenderer line =
            go.GetComponent<
                LineRenderer>();

        line.sharedMaterial =
            sharedLineMaterial;

        line.useWorldSpace =
            true;

        line.positionCount =
            28;

        line.startWidth =
            0.035f;

        line.endWidth =
            0.035f;

        line.numCapVertices =
            3;

        line.sortingOrder =
            sortingOrder;

        line.startColor =
            new Color(
                0.20f,
                0.82f,
                1f,
                0.46f);

        line.endColor =
            new Color(
                0.91f,
                0.35f,
                1f,
                0.46f);

        Vector2 delta =
            exit - entry;

        Vector2 normal =
            delta.sqrMagnitude >
            0.0001f
                ? new Vector2(
                    -delta.y,
                    delta.x).normalized
                : Vector2.up;

        for (int i = 0;
             i < line.positionCount;
             i++)
        {
            float t =
                i /
                (float)(
                    line.positionCount - 1);

            Vector2 point =
                Vector2.Lerp(
                    entry,
                    exit,
                    t);

            float wave =
                Mathf.Sin(
                    t *
                    Mathf.PI *
                    2f) *
                0.13f;

            point +=
                normal *
                wave;

            line.SetPosition(
                i,
                new Vector3(
                    point.x,
                    point.y,
                    -0.08f));
        }

        return line;
    }

    private static void EnsureSharedLineMaterial()
    {
        if (sharedLineMaterial != null)
            return;

        Shader shader =
            Shader.Find(
                "Sprites/Default");

        if (shader == null)
            return;

        sharedLineMaterial =
            new Material(
                shader)
            {
                name =
                    "RuntimeCrystalRiftLineMaterial",
                hideFlags =
                    HideFlags.DontSave
            };
    }

    private static string Normalize(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "rift"
            : value.Trim()
                .Replace(
                    " ",
                    "_");
    }
}

/// <summary>
/// Entry trigger and preview source for one paired Crystal Rift.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalRiftEntryRuntime : MonoBehaviour
{
    private static readonly List<
        CrystalRiftEntryRuntime>
        ActiveEntries =
            new List<
                CrystalRiftEntryRuntime>();

    private Transform exitTransform;
    private Vector2 exitWorldPosition;
    private float exitClearance;
    private float speedMultiplier;
    private CircleCollider2D trigger;
    private SpriteRenderer entryRenderer;
    private SpriteRenderer exitRenderer;
    private LineRenderer energyLink;

    private Vector3 entryBaseScale;
    private Vector3 exitBaseScale;

    private int lastArrowId =
        int.MinValue;

    private float lastTeleportTime =
        -999f;

    private bool previewFocus;
    private Coroutine pulseRoutine;

    public void Configure(
        Transform configuredExit,
        Vector2 configuredExitWorldPosition,
        float configuredExitClearance,
        float configuredSpeedMultiplier,
        CircleCollider2D configuredTrigger,
        SpriteRenderer configuredEntryRenderer,
        SpriteRenderer configuredExitRenderer,
        LineRenderer configuredEnergyLink)
    {
        exitTransform =
            configuredExit;

        exitWorldPosition =
            configuredExitWorldPosition;

        exitClearance =
            Mathf.Max(
                0f,
                configuredExitClearance);

        speedMultiplier =
            Mathf.Clamp(
                configuredSpeedMultiplier,
                0.5f,
                1.5f);

        trigger =
            configuredTrigger;

        entryRenderer =
            configuredEntryRenderer;

        exitRenderer =
            configuredExitRenderer;

        energyLink =
            configuredEnergyLink;

        entryBaseScale =
            transform.localScale;

        exitBaseScale =
            exitTransform != null
                ? exitTransform.localScale
                : Vector3.one;
    }

    private void OnEnable()
    {
        if (!ActiveEntries.Contains(
                this))
        {
            ActiveEntries.Add(
                this);
        }
    }

    private void OnDisable()
    {
        ActiveEntries.Remove(
            this);
    }

    private void Update()
    {
        float wave =
            0.5f +
            0.5f *
            Mathf.Sin(
                Time.time *
                2.7f);

        float scalePulse =
            1f +
            wave *
            (previewFocus
                ? 0.055f
                : 0.025f);

        transform.localScale =
            entryBaseScale *
            scalePulse;

        if (exitTransform != null)
        {
            exitTransform.localScale =
                exitBaseScale *
                scalePulse;
        }

        if (entryRenderer != null)
        {
            entryRenderer.color =
                Color.Lerp(
                    Color.white,
                    new Color(
                        0.72f,
                        0.96f,
                        1f,
                        1f),
                    previewFocus
                        ? 0.32f
                        : wave * 0.10f);
        }

        if (exitRenderer != null)
        {
            exitRenderer.color =
                Color.Lerp(
                    Color.white,
                    new Color(
                        1f,
                        0.74f,
                        1f,
                        1f),
                    previewFocus
                        ? 0.32f
                        : wave * 0.10f);
        }

        if (energyLink != null)
        {
            float alpha =
                previewFocus
                    ? 0.76f
                    : 0.34f +
                      wave *
                      0.12f;

            energyLink.startColor =
                new Color(
                    0.20f,
                    0.82f,
                    1f,
                    alpha);

            energyLink.endColor =
                new Color(
                    0.91f,
                    0.35f,
                    1f,
                    alpha);
        }
    }

    private void OnTriggerEnter2D(
        Collider2D other)
    {
        ArrowController arrow =
            other != null
                ? other.GetComponentInParent<
                    ArrowController>()
                : null;

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        int arrowId =
            arrow.GetInstanceID();

        if (arrowId ==
                lastArrowId &&
            Time.time -
                lastTeleportTime <
                0.20f)
        {
            return;
        }

        Rigidbody2D body =
            arrow.GetComponent<
                Rigidbody2D>();

        if (body == null)
            return;

        Vector2 incoming =
            body.linearVelocity;

        if (incoming.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        Vector2 outgoing =
            GetPreviewOutgoingVelocity(
                incoming);

        Vector2 destination =
            GetPreviewExitPivot(
                outgoing);

        lastArrowId =
            arrowId;

        lastTeleportTime =
            Time.time;

        // Atomic physics + render relocation.
        body.position =
            destination;

        body.linearVelocity =
            outgoing;

        Vector3 current =
            arrow.transform.position;

        arrow.transform.position =
            new Vector3(
                destination.x,
                destination.y,
                current.z);

        // Prevent TrailRenderer from drawing one long line across the rift gap.
        TrailRenderer[] trails =
            arrow.GetComponentsInChildren<
                TrailRenderer>(true);

        for (int i = 0;
             i < trails.Length;
             i++)
        {
            if (trails[i] != null)
                trails[i].Clear();
        }

        GameAudioController.Instance?.
            PlayTargetHit();

        ATSHaptics.Pulse();

        if (pulseRoutine != null)
            StopCoroutine(
                pulseRoutine);

        pulseRoutine =
            StartCoroutine(
                TeleportPulse());
    }

    public Vector2 GetPreviewOutgoingVelocity(
        Vector2 incomingVelocity)
    {
        return incomingVelocity *
            speedMultiplier;
    }

    public Vector2 GetPreviewExitPivot(
        Vector2 outgoingVelocity)
    {
        Vector2 direction =
            outgoingVelocity.sqrMagnitude >
            0.0001f
                ? outgoingVelocity.normalized
                : Vector2.right;

        return
            exitWorldPosition +
            direction *
            exitClearance;
    }

    public static bool TryFindFirstPreviewHit(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out CrystalRiftEntryRuntime rift,
        out Vector2 hitPoint,
        out float segmentT)
    {
        rift = null;
        hitPoint = default;
        segmentT =
            float.PositiveInfinity;

        for (int i =
                 ActiveEntries.Count - 1;
             i >= 0;
             i--)
        {
            CrystalRiftEntryRuntime candidate =
                ActiveEntries[i];

            if (candidate == null ||
                !candidate.isActiveAndEnabled)
            {
                ActiveEntries.RemoveAt(
                    i);
                continue;
            }

            if (!candidate.TryGetPreviewHit(
                    segmentStart,
                    segmentEnd,
                    out Vector2 candidatePoint,
                    out float candidateT))
            {
                continue;
            }

            if (candidateT >=
                segmentT)
            {
                continue;
            }

            rift =
                candidate;

            hitPoint =
                candidatePoint;

            segmentT =
                candidateT;
        }

        return rift != null;
    }

    public static void SetPreviewFocus(
        CrystalRiftEntryRuntime focused)
    {
        for (int i =
                 ActiveEntries.Count - 1;
             i >= 0;
             i--)
        {
            CrystalRiftEntryRuntime candidate =
                ActiveEntries[i];

            if (candidate == null)
            {
                ActiveEntries.RemoveAt(
                    i);
                continue;
            }

            candidate.previewFocus =
                candidate ==
                focused;
        }
    }

    public static void ClearPreviewFocus()
    {
        SetPreviewFocus(
            null);
    }

    private bool TryGetPreviewHit(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out Vector2 hitPoint,
        out float segmentT)
    {
        hitPoint =
            default;

        segmentT =
            0f;

        Vector2 d =
            segmentEnd -
            segmentStart;

        float a =
            Vector2.Dot(
                d,
                d);

        if (a <
            0.000001f)
        {
            return false;
        }

        Vector2 center =
            transform.position;

        float radius =
            ResolveWorldTriggerRadius();

        Vector2 f =
            segmentStart -
            center;

        float c =
            Vector2.Dot(
                f,
                f) -
            radius *
            radius;

        if (c <= 0f)
        {
            hitPoint =
                segmentStart;

            segmentT =
                0f;

            return true;
        }

        float b =
            2f *
            Vector2.Dot(
                f,
                d);

        float discriminant =
            b * b -
            4f *
            a *
            c;

        if (discriminant < 0f)
            return false;

        float root =
            Mathf.Sqrt(
                discriminant);

        float t0 =
            (-b - root) /
            (2f * a);

        float t1 =
            (-b + root) /
            (2f * a);

        float resolved =
            t0 >= 0f &&
            t0 <= 1f
                ? t0
                : t1 >= 0f &&
                  t1 <= 1f
                    ? t1
                    : -1f;

        if (resolved < 0f)
            return false;

        segmentT =
            resolved;

        hitPoint =
            Vector2.Lerp(
                segmentStart,
                segmentEnd,
                resolved);

        return true;
    }

    private float ResolveWorldTriggerRadius()
    {
        float scale =
            Mathf.Max(
                Mathf.Abs(
                    transform.lossyScale.x),
                Mathf.Abs(
                    transform.lossyScale.y));

        float radius =
            trigger != null
                ? trigger.radius *
                  scale
                : 0.62f;

        return Mathf.Max(
            0.15f,
            radius);
    }

    private IEnumerator TeleportPulse()
    {
        Vector3 entryStart =
            entryBaseScale;

        Vector3 exitStart =
            exitBaseScale;

        const float duration =
            0.26f;

        float elapsed =
            0f;

        while (elapsed <
               duration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float punch =
                Mathf.Sin(
                    t *
                    Mathf.PI);

            transform.localScale =
                entryStart *
                (1f +
                 punch *
                 0.15f);

            if (exitTransform != null)
            {
                exitTransform.localScale =
                    exitStart *
                    (1f +
                     punch *
                     0.15f);
            }

            yield return null;
        }

        transform.localScale =
            entryStart;

        if (exitTransform != null)
        {
            exitTransform.localScale =
                exitStart;
        }

        pulseRoutine =
            null;
    }

    private void OnDestroy()
    {
        ActiveEntries.Remove(
            this);
    }
}
