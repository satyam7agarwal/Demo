using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic Wildheart Grove counterweight bridge.
///
/// A heavy seed pod is connected to one side of a 1:1 pulley and a lift platform
/// to the other. A breakable lock restrains the heavier seed pod. Shooting the
/// lock OR striking the heavy pod directly releases the same mechanism. The pod
/// still has to descend; rope tension raises the linked platform/target by the
/// exact opposite displacement during that descent.
///
/// No level-number checks are used. Any LevelData that declares
/// WildheartCounterweights opts into the mechanic.
/// </summary>
[DefaultExecutionOrder(1650)]
[DisallowMultipleComponent]
public sealed class WildheartCounterweightRuntime : MonoBehaviour
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

        if (manager.GetComponent<WildheartCounterweightRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                WildheartCounterweightRuntime>();
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

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        lastLevelObjects =
            levelObjects;

        SpawnForLevel(
            level,
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

    private void SpawnForLevel(
        LevelData level,
        Transform parent)
    {
        LevelData.WildheartCounterweightData[] data =
            level.WildheartCounterweights;

        if (data == null ||
            data.Length == 0)
        {
            return;
        }

        Wall[] walls =
            parent.GetComponentsInChildren<Wall>(
                true);

        Target[] targets =
            parent.GetComponentsInChildren<Target>(
                true);

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.WildheartCounterweightData item =
                data[i];

            if (item == null)
                continue;

            if (item.LiftWallIndex < 0 ||
                item.LiftWallIndex >= walls.Length)
            {
                Debug.LogWarning(
                    "[Wildheart] Counterweight '" +
                    item.MechanismId +
                    "' cannot find Walls[" +
                    item.LiftWallIndex +
                    "].");
                continue;
            }

            if (item.TargetIndex < 0 ||
                item.TargetIndex >= targets.Length)
            {
                Debug.LogWarning(
                    "[Wildheart] Counterweight '" +
                    item.MechanismId +
                    "' cannot find Targets[" +
                    item.TargetIndex +
                    "].");
                continue;
            }

            CreateMechanism(
                item,
                walls[item.LiftWallIndex].transform,
                targets[item.TargetIndex].transform,
                parent,
                i);
        }
    }

    private static void CreateMechanism(
        LevelData.WildheartCounterweightData data,
        Transform liftPlatform,
        Transform target,
        Transform parent,
        int index)
    {
        GameObject root =
            new GameObject(
                "WildheartCounterweight_" +
                Normalize(
                    data.MechanismId) +
                "_" +
                index);

        root.transform.SetParent(
            parent,
            false);

        WildheartCounterweightMechanism mechanism =
            root.AddComponent<
                WildheartCounterweightMechanism>();

        mechanism.Configure(
            data,
            liftPlatform,
            target,
            sharedLineMaterial);
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
            new Material(shader)
            {
                name =
                    "RuntimeWildheartRopeMaterial",
                hideFlags =
                    HideFlags.DontSave
            };
    }

    private static string Normalize(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "counterweight"
            : value.Trim()
                .Replace(" ", "_");
    }
}

/// <summary>
/// One seed / latch / pulley / platform mechanism.
/// </summary>
[DisallowMultipleComponent]
internal sealed class WildheartCounterweightMechanism :
    MonoBehaviour
{
    private LevelData.WildheartCounterweightData data;
    private Transform liftPlatform;
    private Transform target;

    private Transform seed;
    private SpriteRenderer seedRenderer;

    private BoxCollider2D latchTrigger;
    private SpriteRenderer latchRenderer;
    private LineRenderer holdingVine;
    private LineRenderer latchMarker;
    private LineRenderer rope;
    private LineRenderer leftPulley;
    private LineRenderer rightPulley;
    private LineRenderer[] seedRibs;

    private Vector3 seedStart;
    private Vector3 platformStart;
    private Vector3 targetStart;

    private Collider2D[] targetColliders;
    private bool[] targetColliderInitialState;

    private Material lineMaterial;
    private bool released;
    private Coroutine animationRoutine;

    public void Configure(
        LevelData.WildheartCounterweightData configuredData,
        Transform configuredLiftPlatform,
        Transform configuredTarget,
        Material configuredLineMaterial)
    {
        data =
            configuredData;

        liftPlatform =
            configuredLiftPlatform;

        target =
            configuredTarget;

        lineMaterial =
            configuredLineMaterial;

        if (data == null ||
            liftPlatform == null ||
            target == null)
        {
            enabled = false;
            return;
        }

        platformStart =
            liftPlatform.position;

        targetStart =
            target.position;

        CreateSeedPod();
        CreateReleaseLatch();
        CreatePulleyPresentation();
        ConfigureInitialTargetLock();
        UpdateRopeGeometry();
        UpdateHoldingVineGeometry();
    }

    private void Update()
    {
        if (rope != null)
            UpdateRopeGeometry();

        if (!released &&
            holdingVine != null)
        {
            UpdateHoldingVineGeometry();
        }
    }

    private void CreateSeedPod()
    {
        GameObject seedObject =
            new GameObject(
                "HeavySeedPod",
                typeof(SpriteRenderer));

        seedObject.transform.SetParent(
            transform,
            false);

        seedObject.transform.position =
            new Vector3(
                data.SeedPosition.x,
                data.SeedPosition.y,
                -0.16f);

        seed =
            seedObject.transform;

        seedStart =
            seed.position;

        seedRenderer =
            seedObject.GetComponent<
                SpriteRenderer>();

        Sprite sprite =
            string.IsNullOrWhiteSpace(
                data.SeedVisualResourcePath)
                ? null
                : Resources.Load<Sprite>(
                    data.SeedVisualResourcePath.Trim());

        seedRenderer.sprite =
            sprite;

        seedRenderer.sortingOrder =
            data.SortingOrder;

        // Temporary shell until a dedicated authored jungle seed-pod sprite is
        // approved. It is presentation only; the player no longer shoots it.
        if (string.Equals(
                data.SeedVisualResourcePath?.Trim(),
                "Art/BonusProps/ClayPot",
                StringComparison.OrdinalIgnoreCase))
        {
            seedRenderer.color =
                new Color(
                    1f,
                    0.67f,
                    0.28f,
                    1f);
        }
        else
        {
            seedRenderer.color =
                Color.white;
        }

        float fitScale =
            1f;

        if (sprite != null &&
            data.SeedWorldWidth > 0.001f &&
            sprite.bounds.size.x > 0.001f)
        {
            fitScale =
                data.SeedWorldWidth /
                sprite.bounds.size.x;
        }

        float scaleX =
            fitScale *
            Mathf.Max(
                0.01f,
                Mathf.Abs(
                    data.SeedScale.x));

        float scaleY =
            fitScale *
            Mathf.Max(
                0.01f,
                Mathf.Abs(
                    data.SeedScale.y));

        seed.localScale =
            new Vector3(
                scaleX,
                scaleY,
                1f);

        CreateSeedCollider(
            seedObject,
            scaleX,
            scaleY);

        CreateSeedRibs();
    }

    private void CreateSeedCollider(
        GameObject seedObject,
        float scaleX,
        float scaleY)
    {
        if (seedObject == null)
            return;

        CapsuleCollider2D collider =
            seedObject.AddComponent<CapsuleCollider2D>();

        collider.isTrigger =
            false;

        collider.direction =
            CapsuleDirection2D.Vertical;

        float safeScaleX =
            Mathf.Max(
                0.0001f,
                Mathf.Abs(scaleX));

        float safeScaleY =
            Mathf.Max(
                0.0001f,
                Mathf.Abs(scaleY));

        Vector2 worldSize =
            data.SeedColliderWorldSize;

        collider.size =
            new Vector2(
                Mathf.Max(
                    0.20f,
                    worldSize.x) /
                safeScaleX,
                Mathf.Max(
                    0.25f,
                    worldSize.y) /
                safeScaleY);

        collider.offset =
            new Vector2(
                data.SeedColliderWorldOffset.x /
                safeScaleX,
                data.SeedColliderWorldOffset.y /
                safeScaleY);

        WildheartSeedDirectHit directHit =
            seedObject.AddComponent<
                WildheartSeedDirectHit>();

        directHit.Configure(
            this,
            data.DirectSeedHitReleases);
    }

    private void CreateReleaseLatch()
    {
        GameObject latchObject =
            new GameObject(
                "BreakablePulleyLock",
                typeof(BoxCollider2D),
                typeof(WildheartSeedReleaseTrigger));

        latchObject.transform.SetParent(
            transform,
            false);

        latchObject.transform.position =
            new Vector3(
                data.ReleaseLatchPosition.x,
                data.ReleaseLatchPosition.y,
                -0.15f);

        latchTrigger =
            latchObject.GetComponent<BoxCollider2D>();

        latchTrigger.isTrigger =
            true;

        latchTrigger.size =
            new Vector2(
                Mathf.Max(
                    0.25f,
                    data.ReleaseLatchHitboxWorldSize.x),
                Mathf.Max(
                    0.20f,
                    data.ReleaseLatchHitboxWorldSize.y));

        WildheartSeedReleaseTrigger trigger =
            latchObject.GetComponent<
                WildheartSeedReleaseTrigger>();

        trigger.Configure(
            this);

        Sprite latchSprite =
            LoadSprite(
                data.ReleaseLatchVisualResourcePath);

        if (latchSprite != null)
        {
            GameObject visual =
                new GameObject(
                    "BreakableLatchVisual",
                    typeof(SpriteRenderer));

            visual.transform.SetParent(
                latchObject.transform,
                false);

            latchRenderer =
                visual.GetComponent<SpriteRenderer>();

            latchRenderer.sprite =
                latchSprite;

            latchRenderer.sortingOrder =
                data.SortingOrder + 3;

            float fitScale =
                data.ReleaseLatchWorldWidth > 0.001f &&
                latchSprite.bounds.size.x > 0.001f
                    ? data.ReleaseLatchWorldWidth /
                      latchSprite.bounds.size.x
                    : 1f;

            visual.transform.localScale =
                new Vector3(
                    fitScale,
                    fitScale,
                    1f);
        }
        else
        {
            // Backward-compatible fallback for older level data.
            latchMarker =
                CreateLine(
                    "ShootableVineKnot",
                    true,
                    data.SortingOrder + 3,
                    0.075f,
                    new Color(
                        0.96f,
                        0.80f,
                        0.20f,
                        0.96f));

            const int segments =
                20;

            latchMarker.positionCount =
                segments;

            float radius =
                Mathf.Max(
                    0.20f,
                    data.ReleaseLatchVisualLength *
                    0.28f);

            Vector2 centre =
                data.ReleaseLatchPosition;

            for (int i = 0;
                 i < segments;
                 i++)
            {
                float radians =
                    i /
                    (float)segments *
                    Mathf.PI *
                    2f;

                latchMarker.SetPosition(
                    i,
                    new Vector3(
                        centre.x +
                        Mathf.Cos(radians) *
                        radius,
                        centre.y +
                        Mathf.Sin(radians) *
                        radius,
                        -0.15f));
            }
        }

        holdingVine =
            CreateLine(
                "HoldingVine",
                false,
                data.SortingOrder + 2,
                0.105f,
                new Color(
                    0.20f,
                    0.34f,
                    0.08f,
                    1f));
    }

    private static Sprite LoadSprite(
        string resourcePath)
    {
        return string.IsNullOrWhiteSpace(
                resourcePath)
            ? null
            : Resources.Load<Sprite>(
                resourcePath.Trim());
    }

    private void CreateSeedRibs()
    {
        seedRibs =
            new LineRenderer[3];

        float[] offsets =
        {
            -0.22f,
            0f,
            0.22f
        };

        for (int i = 0;
             i < seedRibs.Length;
             i++)
        {
            GameObject ribObject =
                new GameObject(
                    "SeedPodRib_" +
                    i);

            ribObject.transform.SetParent(
                seed,
                false);

            LineRenderer rib =
                ribObject.AddComponent<
                    LineRenderer>();

            rib.sharedMaterial =
                lineMaterial;

            rib.useWorldSpace =
                false;

            rib.positionCount =
                2;

            rib.startWidth =
                0.032f;

            rib.endWidth =
                0.018f;

            rib.sortingOrder =
                data.SortingOrder + 1;

            rib.startColor =
                new Color(
                    0.34f,
                    0.12f,
                    0.04f,
                    0.72f);

            rib.endColor =
                rib.startColor;

            float x =
                offsets[i];

            rib.SetPosition(
                0,
                new Vector3(
                    x,
                    0.34f,
                    -0.01f));

            rib.SetPosition(
                1,
                new Vector3(
                    x * 0.35f,
                    -0.34f,
                    -0.01f));

            seedRibs[i] =
                rib;
        }
    }

    private void CreatePulleyPresentation()
    {
        rope =
            CreateLine(
                "CounterweightRope",
                false,
                data.SortingOrder - 1,
                0.070f,
                new Color(
                    0.34f,
                    0.20f,
                    0.08f,
                    1f));

        leftPulley =
            CreatePulleyRing(
                "LeftPulley",
                data.LeftPulleyPosition);

        rightPulley =
            CreatePulleyRing(
                "RightPulley",
                data.RightPulleyPosition);
    }

    private LineRenderer CreatePulleyRing(
        string objectName,
        Vector2 worldPosition)
    {
        LineRenderer ring =
            CreateLine(
                objectName,
                true,
                data.SortingOrder,
                0.075f,
                new Color(
                    0.22f,
                    0.14f,
                    0.06f,
                    1f));

        const int segments =
            28;

        ring.positionCount =
            segments;

        float radius =
            Mathf.Max(
                0.08f,
                data.PulleyRadius);

        for (int i = 0;
             i < segments;
             i++)
        {
            float radians =
                i /
                (float)segments *
                Mathf.PI *
                2f;

            ring.SetPosition(
                i,
                new Vector3(
                    worldPosition.x +
                    Mathf.Cos(radians) *
                    radius,
                    worldPosition.y +
                    Mathf.Sin(radians) *
                    radius,
                    -0.15f));
        }

        return ring;
    }

    private LineRenderer CreateLine(
        string objectName,
        bool loop,
        int sortingOrder,
        float width,
        Color color)
    {
        GameObject lineObject =
            new GameObject(
                objectName);

        lineObject.transform.SetParent(
            transform,
            false);

        LineRenderer line =
            lineObject.AddComponent<
                LineRenderer>();

        line.sharedMaterial =
            lineMaterial;

        line.useWorldSpace =
            true;

        line.loop =
            loop;

        line.startWidth =
            width;

        line.endWidth =
            width;

        line.numCapVertices =
            3;

        line.sortingOrder =
            sortingOrder;

        line.startColor =
            color;

        line.endColor =
            color;

        return line;
    }

    private void ConfigureInitialTargetLock()
    {
        if (!data.LockTargetUntilRaised ||
            target == null)
        {
            return;
        }

        targetColliders =
            target.GetComponentsInChildren<
                Collider2D>(true);

        targetColliderInitialState =
            new bool[
                targetColliders.Length];

        for (int i = 0;
             i < targetColliders.Length;
             i++)
        {
            Collider2D collider =
                targetColliders[i];

            if (collider == null)
                continue;

            targetColliderInitialState[i] =
                collider.enabled;

            collider.enabled =
                false;
        }
    }

    public void Release(
        ArrowController arrow,
        Vector2 impactVelocity = default)
    {
        if (released)
            return;

        released =
            true;

        if (latchTrigger != null)
            latchTrigger.enabled =
                false;

        if (data.ConsumeArrowOnRelease &&
            arrow != null &&
            !arrow.IsStopped)
        {
            arrow.Stop();
        }

        Vector2 incomingVelocity =
            impactVelocity.sqrMagnitude > 0.001f
                ? impactVelocity
                : arrow != null
                    ? arrow.GetVelocity()
                    : Vector2.right;

        GameAudioController.Instance?.PlayWallHit();
        ATSHaptics.Pulse();

        BreakLatch(
            incomingVelocity);

        SnapHoldingVine();

        if (animationRoutine != null)
            StopCoroutine(
                animationRoutine);

        animationRoutine =
            StartCoroutine(
                AnimateRelease());
    }

    private void BreakLatch(
        Vector2 incomingVelocity)
    {
        if (latchRenderer != null)
            latchRenderer.enabled = false;

        if (latchMarker != null)
            latchMarker.enabled = false;

        Vector2 direction =
            incomingVelocity.sqrMagnitude > 0.001f
                ? incomingVelocity.normalized
                : Vector2.right;

        CreateBrokenLatchPiece(
            data.ReleaseLatchBrokenLeftResourcePath,
            -1f,
            direction);

        CreateBrokenLatchPiece(
            data.ReleaseLatchBrokenRightResourcePath,
            1f,
            direction);
    }

    private void CreateBrokenLatchPiece(
        string resourcePath,
        float side,
        Vector2 incomingDirection)
    {
        Sprite sprite =
            LoadSprite(
                resourcePath);

        if (sprite == null)
            return;

        GameObject pieceObject =
            new GameObject(
                side < 0f
                    ? "BrokenLatch_Left"
                    : "BrokenLatch_Right",
                typeof(SpriteRenderer),
                typeof(Rigidbody2D));

        pieceObject.transform.SetParent(
            transform,
            false);

        pieceObject.transform.position =
            new Vector3(
                data.ReleaseLatchPosition.x,
                data.ReleaseLatchPosition.y,
                -0.14f);

        SpriteRenderer renderer =
            pieceObject.GetComponent<SpriteRenderer>();

        renderer.sprite =
            sprite;

        renderer.sortingOrder =
            data.SortingOrder + 4;

        float fitScale =
            data.ReleaseLatchWorldWidth > 0.001f &&
            sprite.bounds.size.x > 0.001f
                ? data.ReleaseLatchWorldWidth /
                  sprite.bounds.size.x
                : 1f;

        pieceObject.transform.localScale =
            new Vector3(
                fitScale,
                fitScale,
                1f);

        Rigidbody2D body =
            pieceObject.GetComponent<Rigidbody2D>();

        body.gravityScale =
            1.35f;

        body.collisionDetectionMode =
            CollisionDetectionMode2D.Continuous;

        body.linearVelocity =
            incomingDirection * 1.15f +
            new Vector2(
                side * 1.65f,
                1.45f);

        body.angularVelocity =
            side * 310f;

        Destroy(
            pieceObject,
            1.25f);
    }

    private void SnapHoldingVine()
    {
        if (holdingVine != null)
        {
            holdingVine.enabled =
                false;
        }

        if (latchMarker != null)
        {
            latchMarker.enabled =
                false;
        }
    }

    private IEnumerator AnimateRelease()
    {
        float duration =
            Mathf.Max(
                0.10f,
                data.MoveDuration);

        Vector3 seedEnd =
            seedStart +
            new Vector3(
                data.SeedDropOffset.x,
                data.SeedDropOffset.y,
                0f);

        Vector3 platformEnd =
            platformStart +
            new Vector3(
                data.LiftOffset.x,
                data.LiftOffset.y,
                0f);

        Vector3 targetEnd =
            targetStart +
            new Vector3(
                data.LiftOffset.x,
                data.LiftOffset.y,
                0f);

        float elapsed =
            0f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            // A counterweight starts slowly, accelerates as the restraint is
            // removed, then the pulley brake catches it near the endpoint.
            // SmoothStep gives zero velocity at both ends while keeping the
            // seed/platform perfectly rope-synchronised.
            float motion =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    t);

            if (seed != null)
            {
                seed.position =
                    Vector3.LerpUnclamped(
                        seedStart,
                        seedEnd,
                        motion);

                float sway =
                    Mathf.Sin(
                        t *
                        Mathf.PI) *
                    6f;

                seed.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        sway);
            }

            if (liftPlatform != null)
            {
                liftPlatform.position =
                    Vector3.LerpUnclamped(
                        platformStart,
                        platformEnd,
                        motion);
            }

            if (target != null)
            {
                target.position =
                    Vector3.LerpUnclamped(
                        targetStart,
                        targetEnd,
                        motion);
            }

            yield return null;
        }

        if (seed != null)
        {
            seed.position =
                seedEnd;

            seed.localRotation =
                Quaternion.identity;
        }

        if (liftPlatform != null)
            liftPlatform.position =
                platformEnd;

        if (target != null)
            target.position =
                targetEnd;

        RestoreTargetCollision();

        animationRoutine =
            null;
    }

    private void RestoreTargetCollision()
    {
        if (targetColliders == null ||
            targetColliderInitialState == null)
        {
            return;
        }

        int count =
            Mathf.Min(
                targetColliders.Length,
                targetColliderInitialState.Length);

        for (int i = 0;
             i < count;
             i++)
        {
            if (targetColliders[i] != null)
            {
                targetColliders[i].enabled =
                    targetColliderInitialState[i];
            }
        }
    }

    private void UpdateHoldingVineGeometry()
    {
        if (holdingVine == null ||
            seed == null)
        {
            return;
        }

        float halfLength =
            Mathf.Max(
                0.08f,
                data.ReleaseLatchVisualLength *
                0.5f);

        Vector3 top =
            new Vector3(
                data.ReleaseLatchPosition.x,
                data.ReleaseLatchPosition.y +
                halfLength,
                -0.15f);

        Vector3 seedAttach =
            seed.position +
            new Vector3(
                0f,
                0.54f,
                0f);

        seedAttach.z =
            -0.15f;

        holdingVine.positionCount =
            2;

        holdingVine.SetPosition(
            0,
            top);

        holdingVine.SetPosition(
            1,
            seedAttach);
    }

    private void UpdateRopeGeometry()
    {
        if (rope == null ||
            seed == null ||
            liftPlatform == null)
        {
            return;
        }

        rope.positionCount =
            4;

        Vector3 seedAttach =
            seed.position +
            new Vector3(
                0f,
                0.34f,
                0f);

        Vector3 left =
            new Vector3(
                data.LeftPulleyPosition.x,
                data.LeftPulleyPosition.y,
                -0.15f);

        Vector3 right =
            new Vector3(
                data.RightPulleyPosition.x,
                data.RightPulleyPosition.y,
                -0.15f);

        Vector3 platformAttach =
            liftPlatform.position +
            new Vector3(
                0f,
                0.48f,
                0f);

        seedAttach.z =
            -0.15f;

        platformAttach.z =
            -0.15f;

        rope.SetPosition(
            0,
            seedAttach);

        rope.SetPosition(
            1,
            left);

        rope.SetPosition(
            2,
            right);

        rope.SetPosition(
            3,
            platformAttach);
    }
}

/// <summary>
/// Arrow trigger attached only to the holding vine/latch.
/// The heavy seed pod itself is not a shoot-to-drop object.
/// </summary>
[DisallowMultipleComponent]
internal sealed class WildheartSeedReleaseTrigger :
    MonoBehaviour
{
    private WildheartCounterweightMechanism mechanism;

    public void Configure(
        WildheartCounterweightMechanism configuredMechanism)
    {
        mechanism =
            configuredMechanism;
    }

    private void OnTriggerEnter2D(
        Collider2D other)
    {
        if (mechanism == null ||
            other == null)
        {
            return;
        }

        ArrowController arrow =
            other.GetComponentInParent<
                ArrowController>();

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        mechanism.Release(
            arrow,
            arrow.GetVelocity());
    }
}

/// <summary>
/// Solid collision handler for the heavy seed pod.
///
/// The pod is intentionally NOT a trigger: arrows should visibly hit a real,
/// weighty object instead of travelling behind/through the artwork. The normal
/// ArrowController solid-collision path therefore consumes this first shot and
/// prepares the next arrow. This component only starts the counterweight.
///
/// Callback order is irrelevant: ArrowController may already have stopped the
/// arrow when this runs, so HasFired is checked but IsStopped is deliberately
/// not used.
/// </summary>
[DisallowMultipleComponent]
internal sealed class WildheartSeedDirectHit :
    MonoBehaviour
{
    private WildheartCounterweightMechanism mechanism;
    private bool releaseOnHit;

    public void Configure(
        WildheartCounterweightMechanism configuredMechanism,
        bool configuredReleaseOnHit)
    {
        mechanism =
            configuredMechanism;

        releaseOnHit =
            configuredReleaseOnHit;
    }

    private void OnCollisionEnter2D(
        Collision2D collision)
    {
        if (!releaseOnHit ||
            mechanism == null ||
            collision == null)
        {
            return;
        }

        ArrowController arrow =
            collision.collider != null
                ? collision.collider.GetComponentInParent<
                    ArrowController>()
                : null;

        if (arrow == null &&
            collision.gameObject != null)
        {
            arrow =
                collision.gameObject.GetComponentInParent<
                    ArrowController>();
        }

        if (arrow == null ||
            !arrow.HasFired)
        {
            return;
        }

        Vector2 impactVelocity =
            collision.relativeVelocity;

        mechanism.Release(
            arrow,
            impactVelocity);
    }
}
