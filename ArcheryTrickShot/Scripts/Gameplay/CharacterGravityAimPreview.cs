using UnityEngine;

/// <summary>
/// Character-specific ballistic aim curve.
///
/// Nerissa gets this guide because her projectile is physically affected by
/// gravity. In Crystal Caverns the guide also understands the first Prism
/// Crystal it intersects: the ordinary green arc stops at the prism and a
/// purple/cyan continuation shows the redirected ballistic path.
///
/// Prism prediction deliberately uses the real Arrow BoxCollider2D footprint.
/// This keeps preview contact/clearance aligned with runtime physics instead of
/// treating the arrow as a zero-size point.
/// </summary>
public sealed class CharacterGravityAimPreview : MonoBehaviour
{
    private const float SweepEpsilon = 0.0001f;
    private const int SweepHitBufferSize = 32;

    private BowController bow;
    private ArrowController arrow;
    private ArcherGameplayTraitProfile trait;
    private GameConfig config;

    private LineRenderer line;
    private Material lineMaterial;

    private LineRenderer prismLine;
    private Material prismLineMaterial;

    private LineRenderer riftLine;
    private Material riftLineMaterial;

    private bool aiming;
    private Vector2 aimDirection = Vector2.right;

    private readonly Vector3[] basePoints = new Vector3[64];
    private readonly Vector3[] prismPoints = new Vector3[64];
    private readonly Vector3[] riftPoints = new Vector3[64];
    private readonly RaycastHit2D[] sweepHits =
        new RaycastHit2D[SweepHitBufferSize];

    public void Bind(BowController bowController)
    {
        if (bow == bowController)
            return;

        Unsubscribe();
        bow = bowController;

        if (bow == null)
            return;

        bow.AimStarted += OnAimStarted;
        bow.AimChanged += OnAimChanged;
        bow.AimReleased += OnAimReleased;
        bow.AimCancelled += OnAimCancelled;
    }

    public void Configure(
        ArrowController currentArrow,
        ArcherGameplayTraitProfile gameplayTrait,
        GameConfig gameConfig)
    {
        arrow = currentArrow;
        trait = gameplayTrait;
        config = gameConfig != null
            ? gameConfig
            : GameConfig.Load();

        aiming = false;

        EnsureLine();
        EnsurePrismLine();
        EnsureRiftLine();
        ApplyStyle();
        Hide();
    }

    public void ClearArrow()
    {
        arrow = null;
        aiming = false;
        Hide();
    }

    private void OnAimStarted()
    {
        aiming = true;
    }

    private void OnAimChanged(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;

        aimDirection = direction.normalized;
        aiming = true;
    }

    private void OnAimReleased(Vector2 ignoredDirection)
    {
        aiming = false;
        Hide();
    }

    private void OnAimCancelled()
    {
        aiming = false;
        Hide();
    }

    private void LateUpdate()
    {
        if (!ShouldRender())
        {
            Hide();
            return;
        }

        RenderCurve();
    }

    private bool ShouldRender()
    {
        return
            aiming &&
            arrow != null &&
            !arrow.HasFired &&
            trait != null &&
            trait.UsesGravityArc &&
            trait.ShowAimCurve &&
            config != null;
    }

    private void RenderCurve()
    {
        EnsureLine();
        EnsurePrismLine();
        EnsureRiftLine();

        int samples =
            Mathf.Clamp(
                trait.AimCurveSamples,
                8,
                64);

        Vector2 origin =
            arrow.transform.position;

        float fallbackSpeed =
            Mathf.Max(
                0.01f,
                config.ArrowSpeed);

        float drawAmount =
            bow != null
                ? bow.CurrentDrawAmount
                : 1f;

        float speed =
            trait.EvaluateLaunchSpeed(
                drawAmount,
                fallbackSpeed);

        Vector2 initialVelocity =
            aimDirection *
            speed;

        Vector2 gravity =
            Physics2D.gravity *
            trait.GravityScale;

        float duration =
            Mathf.Clamp(
                trait.AimCurveDuration,
                0.4f,
                3f);

        int baseCount = 0;

        bool prismHit = false;
        PrismCrystalRuntime hitPrism = null;
        Vector2 prismHitPivot = default;
        float prismHitTime = 0f;
        Vector2 prismIncomingVelocity = default;

        bool riftHit = false;
        CrystalRiftEntryRuntime hitRift = null;
        Vector2 riftHitPivot = default;
        float riftHitTime = 0f;
        Vector2 riftIncomingVelocity = default;

        Vector2 previousPosition =
            origin;

        Vector2 previousVelocity =
            initialVelocity;

        float previousTime =
            0f;

        basePoints[baseCount++] =
            new Vector3(
                origin.x,
                origin.y,
                arrow.transform.position.z);

        for (int i = 1;
             i < samples;
             i++)
        {
            float normalized =
                i /
                (float)(
                    samples - 1);

            float t =
                duration *
                normalized;

            Vector2 position =
                origin +
                initialVelocity *
                    t +
                0.5f *
                gravity *
                t *
                t;

            Vector2 velocity =
                initialVelocity +
                gravity *
                t;

            TryFindFirstSweepEvent(
                previousPosition,
                position,
                previousVelocity,
                velocity,
                true,
                null,
                true,
                null,
                out PrismCrystalRuntime prism,
                out float prismT,
                out CrystalRiftEntryRuntime rift,
                out float riftT,
                out Collider2D blockingCollider,
                out float blockingT);

            float firstInteractiveT =
                Mathf.Min(
                    prismT,
                    riftT);

            bool interactiveComesFirst =
                (prism != null ||
                 rift != null) &&
                (blockingCollider == null ||
                 firstInteractiveT <=
                 blockingT);

            if (interactiveComesFirst)
            {
                bool chooseRift =
                    rift != null &&
                    riftT <= prismT;

                float chosenT =
                    chooseRift
                        ? riftT
                        : prismT;

                float hitTime =
                    Mathf.Lerp(
                        previousTime,
                        t,
                        chosenT);

                Vector2 hitPivot =
                    Vector2.Lerp(
                        previousPosition,
                        position,
                        chosenT);

                basePoints[baseCount++] =
                    new Vector3(
                        hitPivot.x,
                        hitPivot.y,
                        arrow.transform.position.z);

                Vector2 incomingVelocity =
                    initialVelocity +
                    gravity *
                    hitTime;

                if (chooseRift)
                {
                    riftHit = true;
                    hitRift = rift;
                    riftHitPivot = hitPivot;
                    riftHitTime = hitTime;
                    riftIncomingVelocity =
                        incomingVelocity;
                }
                else
                {
                    prismHit = true;
                    hitPrism = prism;
                    prismHitPivot = hitPivot;
                    prismHitTime = hitTime;
                    prismIncomingVelocity =
                        incomingVelocity;
                }

                break;
            }

            if (blockingCollider != null)
            {
                Vector2 blockedPivot =
                    Vector2.Lerp(
                        previousPosition,
                        position,
                        blockingT);

                basePoints[baseCount++] =
                    new Vector3(
                        blockedPivot.x,
                        blockedPivot.y,
                        arrow.transform.position.z);

                break;
            }

            basePoints[baseCount++] =
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z);

            previousPosition =
                position;

            previousVelocity =
                velocity;

            previousTime =
                t;
        }

        line.positionCount =
            baseCount;

        for (int i = 0;
             i < baseCount;
             i++)
        {
            line.SetPosition(
                i,
                basePoints[i]);
        }

        line.enabled =
            baseCount > 1;

        if (riftHit &&
            hitRift != null)
        {
            HidePrismContinuation();
            PrismCrystalRuntime.ClearPreviewFocus();

            CrystalRiftEntryRuntime.SetPreviewFocus(
                hitRift);

            RenderRiftContinuation(
                hitRift,
                riftIncomingVelocity,
                gravity,
                duration,
                samples);

            return;
        }

        HideRiftContinuation();
        CrystalRiftEntryRuntime.ClearPreviewFocus();

        if (!prismHit ||
            hitPrism == null)
        {
            HidePrismContinuation();
            PrismCrystalRuntime.ClearPreviewFocus();
            return;
        }

        PrismCrystalRuntime.SetPreviewFocus(
            hitPrism);

        Vector2 outgoingVelocity =
            hitPrism.GetPreviewOutgoingVelocity(
                prismIncomingVelocity);

        float continuationDuration =
            Mathf.Clamp(
                duration *
                0.92f,
                0.95f,
                1.45f);

        if (outgoingVelocity.sqrMagnitude <
            0.0001f)
        {
            HidePrismContinuation();
            return;
        }

        int continuationSamples =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    samples *
                    0.86f),
                18,
                64);

        int prismCount =
            0;

        prismPoints[prismCount++] =
            new Vector3(
                prismHitPivot.x,
                prismHitPivot.y,
                arrow.transform.position.z);

        Vector2 previousPrismPosition =
            prismHitPivot;

        Vector2 previousPrismVelocity =
            outgoingVelocity;

        for (int i = 1;
             i < continuationSamples;
             i++)
        {
            float normalized =
                i /
                (float)(
                    continuationSamples - 1);

            float t =
                continuationDuration *
                normalized;

            Vector2 position =
                prismHitPivot +
                outgoingVelocity *
                    t +
                0.5f *
                gravity *
                t *
                t;

            Vector2 velocity =
                outgoingVelocity +
                gravity *
                t;

            TryFindFirstSweepEvent(
                previousPrismPosition,
                position,
                previousPrismVelocity,
                velocity,
                false,
                hitPrism,
                false,
                null,
                out PrismCrystalRuntime ignoredPrism,
                out float ignoredPrismT,
                out CrystalRiftEntryRuntime ignoredRift,
                out float ignoredRiftT,
                out Collider2D blockingCollider,
                out float blockingT);

            if (blockingCollider != null)
            {
                Vector2 blockedPivot =
                    Vector2.Lerp(
                        previousPrismPosition,
                        position,
                        blockingT);

                prismPoints[prismCount++] =
                    new Vector3(
                        blockedPivot.x,
                        blockedPivot.y,
                        arrow.transform.position.z);

                break;
            }

            prismPoints[prismCount++] =
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z);

            previousPrismPosition =
                position;

            previousPrismVelocity =
                velocity;
        }

        prismLine.positionCount =
            prismCount;

        for (int i = 0;
             i < prismCount;
             i++)
        {
            prismLine.SetPosition(
                i,
                prismPoints[i]);
        }

        prismLine.enabled =
            prismCount > 1;
    }

    private void RenderRiftContinuation(
        CrystalRiftEntryRuntime rift,
        Vector2 incomingVelocity,
        Vector2 gravity,
        float duration,
        int samples)
    {
        Vector2 outgoingVelocity =
            rift.GetPreviewOutgoingVelocity(
                incomingVelocity);

        if (outgoingVelocity.sqrMagnitude <
            0.0001f)
        {
            HideRiftContinuation();
            return;
        }

        Vector2 exitPivot =
            rift.GetPreviewExitPivot(
                outgoingVelocity);

        float continuationDuration =
            Mathf.Clamp(
                duration *
                0.92f,
                0.95f,
                1.45f);

        int continuationSamples =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    samples *
                    0.86f),
                18,
                64);

        int riftCount =
            0;

        riftPoints[riftCount++] =
            new Vector3(
                exitPivot.x,
                exitPivot.y,
                arrow.transform.position.z);

        Vector2 previousPosition =
            exitPivot;

        Vector2 previousVelocity =
            outgoingVelocity;

        for (int i = 1;
             i < continuationSamples;
             i++)
        {
            float normalized =
                i /
                (float)(
                    continuationSamples - 1);

            float t =
                continuationDuration *
                normalized;

            Vector2 position =
                exitPivot +
                outgoingVelocity *
                    t +
                0.5f *
                gravity *
                t *
                t;

            Vector2 velocity =
                outgoingVelocity +
                gravity *
                t;

            TryFindFirstSweepEvent(
                previousPosition,
                position,
                previousVelocity,
                velocity,
                false,
                null,
                false,
                rift,
                out PrismCrystalRuntime ignoredPrism,
                out float ignoredPrismT,
                out CrystalRiftEntryRuntime ignoredRift,
                out float ignoredRiftT,
                out Collider2D blockingCollider,
                out float blockingT);

            if (blockingCollider != null)
            {
                Vector2 blockedPivot =
                    Vector2.Lerp(
                        previousPosition,
                        position,
                        blockingT);

                riftPoints[riftCount++] =
                    new Vector3(
                        blockedPivot.x,
                        blockedPivot.y,
                        arrow.transform.position.z);

                break;
            }

            riftPoints[riftCount++] =
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z);

            previousPosition =
                position;

            previousVelocity =
                velocity;
        }

        riftLine.positionCount =
            riftCount;

        for (int i = 0;
             i < riftCount;
             i++)
        {
            riftLine.SetPosition(
                i,
                riftPoints[i]);
        }

        riftLine.enabled =
            riftCount > 1;
    }

    private void TryFindFirstSweepEvent(
        Vector2 startPivot,
        Vector2 endPivot,
        Vector2 startVelocity,
        Vector2 endVelocity,
        bool detectPrism,
        PrismCrystalRuntime ignoredPrism,
        bool detectRift,
        CrystalRiftEntryRuntime ignoredRift,
        out PrismCrystalRuntime prism,
        out float prismT,
        out CrystalRiftEntryRuntime rift,
        out float riftT,
        out Collider2D blockingCollider,
        out float blockingT)
    {
        prism = null;
        prismT = float.PositiveInfinity;
        rift = null;
        riftT = float.PositiveInfinity;
        blockingCollider = null;
        blockingT = float.PositiveInfinity;

        if (!TryGetArrowPredictionBox(
                out Vector2 boxSize,
                out Vector2 scaledLocalOffset))
        {
            if (detectPrism &&
                PrismCrystalRuntime.TryFindFirstPreviewHit(
                    startPivot,
                    endPivot,
                    out PrismCrystalRuntime fallbackPrism,
                    out Vector2 ignoredPrismPoint,
                    out float fallbackPrismT))
            {
                prism =
                    fallbackPrism;

                prismT =
                    fallbackPrismT;
            }

            if (detectRift &&
                CrystalRiftEntryRuntime.TryFindFirstPreviewHit(
                    startPivot,
                    endPivot,
                    out CrystalRiftEntryRuntime fallbackRift,
                    out Vector2 ignoredRiftPoint,
                    out float fallbackRiftT))
            {
                rift =
                    fallbackRift;

                riftT =
                    fallbackRiftT;
            }

            return;
        }

        float startAngle =
            GetArrowRotationAngle(
                startVelocity);

        float endAngle =
            GetArrowRotationAngle(
                endVelocity);

        Vector2 startCenter =
            startPivot +
            RotateVector(
                scaledLocalOffset,
                startAngle);

        Vector2 endCenter =
            endPivot +
            RotateVector(
                scaledLocalOffset,
                endAngle);

        Vector2 castDelta =
            endCenter -
            startCenter;

        float castDistance =
            castDelta.magnitude;

        if (castDistance <
            SweepEpsilon)
        {
            return;
        }

        Vector2 castDirection =
            castDelta /
            castDistance;

        Vector2 middleVelocity =
            startVelocity +
            endVelocity;

        if (middleVelocity.sqrMagnitude <
            SweepEpsilon)
        {
            middleVelocity =
                endVelocity.sqrMagnitude >
                SweepEpsilon
                    ? endVelocity
                    : startVelocity;
        }

        float castAngle =
            GetArrowRotationAngle(
                middleVelocity);

        ContactFilter2D filter =
            new ContactFilter2D();

        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;

        int hitCount =
            Physics2D.BoxCast(
                startCenter,
                boxSize,
                castAngle,
                castDirection,
                filter,
                sweepHits,
                castDistance);

        for (int i = 0;
             i < hitCount;
             i++)
        {
            RaycastHit2D hit =
                sweepHits[i];

            Collider2D hitCollider =
                hit.collider;

            if (hitCollider == null ||
                IsArrowCollider(
                    hitCollider))
            {
                continue;
            }

            float normalizedT =
                Mathf.Clamp01(
                    hit.distance /
                    castDistance);

            PrismCrystalRuntime hitPrism =
                hitCollider.GetComponentInParent<
                    PrismCrystalRuntime>();

            if (hitPrism != null)
            {
                if (detectPrism &&
                    hitPrism !=
                    ignoredPrism &&
                    normalizedT <
                    prismT)
                {
                    prism =
                        hitPrism;

                    prismT =
                        normalizedT;
                }

                continue;
            }

            CrystalRiftEntryRuntime hitRift =
                hitCollider.GetComponentInParent<
                    CrystalRiftEntryRuntime>();

            if (hitRift != null)
            {
                if (detectRift &&
                    hitRift !=
                    ignoredRift &&
                    normalizedT <
                    riftT)
                {
                    rift =
                        hitRift;

                    riftT =
                        normalizedT;
                }

                continue;
            }

            if (hitCollider.isTrigger)
            {
                // Targets, resonance switches and other trigger-only gameplay
                // volumes do not physically block the real arrow.
                continue;
            }

            if (normalizedT <
                blockingT)
            {
                blockingCollider =
                    hitCollider;

                blockingT =
                    normalizedT;
            }
        }
    }

    private bool TryGetArrowPredictionBox(
        out Vector2 worldSize,
        out Vector2 scaledLocalOffset)
    {
        worldSize = Vector2.zero;
        scaledLocalOffset = Vector2.zero;

        if (arrow == null)
            return false;

        BoxCollider2D box =
            arrow.GetComponent<BoxCollider2D>();

        if (box == null || !box.enabled)
            return false;

        Vector3 lossyScale =
            arrow.transform.lossyScale;

        worldSize =
            new Vector2(
                Mathf.Abs(
                    box.size.x *
                    lossyScale.x),
                Mathf.Abs(
                    box.size.y *
                    lossyScale.y));

        scaledLocalOffset =
            new Vector2(
                box.offset.x *
                lossyScale.x,
                box.offset.y *
                lossyScale.y);

        return
            worldSize.x > SweepEpsilon &&
            worldSize.y > SweepEpsilon;
    }

    private bool IsArrowCollider(
        Collider2D candidate)
    {
        if (candidate == null ||
            arrow == null)
        {
            return false;
        }

        Transform candidateTransform =
            candidate.transform;

        Transform arrowTransform =
            arrow.transform;

        return
            candidateTransform == arrowTransform ||
            candidateTransform.IsChildOf(arrowTransform);
    }

    private float GetArrowRotationAngle(
        Vector2 velocity)
    {
        Vector2 direction =
            velocity.sqrMagnitude > SweepEpsilon
                ? velocity.normalized
                : Vector2.right;

        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x) *
            Mathf.Rad2Deg;

        if (config != null)
        {
            angle +=
                config.ArrowVisualAngleOffset;
        }

        return angle;
    }

    private static Vector2 RotateVector(
        Vector2 value,
        float angleDegrees)
    {
        float radians =
            angleDegrees *
            Mathf.Deg2Rad;

        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return
            new Vector2(
                value.x * cos -
                value.y * sin,
                value.x * sin +
                value.y * cos);
    }

    private void EnsureLine()
    {
        if (line != null)
            return;

        Transform existing =
            transform.Find(
                "CharacterGravityAimCurve");

        GameObject lineObject;

        if (existing != null)
        {
            lineObject =
                existing.gameObject;
        }
        else
        {
            lineObject =
                new GameObject(
                    "CharacterGravityAimCurve");

            lineObject.transform.SetParent(
                transform,
                false);
        }

        line =
            lineObject.GetComponent<LineRenderer>();

        if (line == null)
            line = lineObject.AddComponent<LineRenderer>();

        if (lineMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "Sprites/Default");

            if (shader != null)
            {
                lineMaterial =
                    new Material(shader)
                    {
                        name =
                            "RuntimeCharacterGravityAimMaterial",
                        hideFlags =
                            HideFlags.DontSave
                    };
            }
        }

        if (lineMaterial != null)
            line.sharedMaterial = lineMaterial;

        line.useWorldSpace = true;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.sortingOrder = 20;
        line.enabled = false;
    }

    private void EnsurePrismLine()
    {
        if (prismLine != null)
            return;

        Transform existing =
            transform.Find(
                "CharacterPrismAimContinuation");

        GameObject lineObject;

        if (existing != null)
        {
            lineObject =
                existing.gameObject;
        }
        else
        {
            lineObject =
                new GameObject(
                    "CharacterPrismAimContinuation");

            lineObject.transform.SetParent(
                transform,
                false);
        }

        prismLine =
            lineObject.GetComponent<LineRenderer>();

        if (prismLine == null)
            prismLine =
                lineObject.AddComponent<LineRenderer>();

        if (prismLineMaterial == null)
        {
            Shader shader =
                Shader.Find("Sprites/Default");

            if (shader != null)
            {
                prismLineMaterial =
                    new Material(shader)
                    {
                        name =
                            "RuntimeCharacterPrismAimMaterial",
                        hideFlags =
                            HideFlags.DontSave
                    };
            }
        }

        if (prismLineMaterial != null)
            prismLine.sharedMaterial =
                prismLineMaterial;

        prismLine.useWorldSpace = true;
        prismLine.numCornerVertices = 3;
        prismLine.numCapVertices = 3;
        prismLine.textureMode = LineTextureMode.Stretch;
        prismLine.sortingOrder = 21;
        prismLine.enabled = false;
    }

    private void EnsureRiftLine()
    {
        if (riftLine != null)
            return;

        Transform existing =
            transform.Find(
                "CharacterRiftAimContinuation");

        GameObject lineObject;

        if (existing != null)
        {
            lineObject =
                existing.gameObject;
        }
        else
        {
            lineObject =
                new GameObject(
                    "CharacterRiftAimContinuation");

            lineObject.transform.SetParent(
                transform,
                false);
        }

        riftLine =
            lineObject.GetComponent<
                LineRenderer>();

        if (riftLine == null)
        {
            riftLine =
                lineObject.AddComponent<
                    LineRenderer>();
        }

        if (riftLineMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "Sprites/Default");

            if (shader != null)
            {
                riftLineMaterial =
                    new Material(
                        shader)
                    {
                        name =
                            "RuntimeCharacterRiftAimMaterial",
                        hideFlags =
                            HideFlags.DontSave
                    };
            }
        }

        if (riftLineMaterial != null)
        {
            riftLine.sharedMaterial =
                riftLineMaterial;
        }

        riftLine.useWorldSpace =
            true;

        riftLine.numCornerVertices =
            3;

        riftLine.numCapVertices =
            3;

        riftLine.textureMode =
            LineTextureMode.Stretch;

        riftLine.sortingOrder =
            22;

        riftLine.enabled =
            false;
    }

    private void ApplyStyle()
    {
        if (line == null || trait == null)
            return;

        float width =
            Mathf.Clamp(
                trait.AimCurveWidth,
                0.005f,
                0.12f);

        line.startWidth = width;
        line.endWidth = width * 0.58f;

        Color start = trait.AimCurveColor;
        Color end = trait.AimCurveColor;
        end.a *= 0.18f;

        line.startColor = start;
        line.endColor = end;

        if (prismLine != null)
        {
            prismLine.startWidth =
                width * 2.35f;

            prismLine.endWidth =
                width * 1.10f;

            prismLine.startColor =
                new Color(
                    0.96f,
                    0.66f,
                    1f,
                    1f);

            prismLine.endColor =
                new Color(
                    0.24f,
                    0.98f,
                    1f,
                    0.62f);
        }

        if (riftLine != null)
        {
            riftLine.startWidth =
                width * 2.30f;

            riftLine.endWidth =
                width * 1.05f;

            riftLine.startColor =
                new Color(
                    0.96f,
                    0.52f,
                    1f,
                    1f);

            riftLine.endColor =
                new Color(
                    0.40f,
                    0.90f,
                    1f,
                    0.58f);
        }
    }

    private void HidePrismContinuation()
    {
        if (prismLine != null)
        {
            prismLine.enabled = false;
            prismLine.positionCount = 0;
        }
    }

    private void HideRiftContinuation()
    {
        if (riftLine != null)
        {
            riftLine.enabled = false;
            riftLine.positionCount = 0;
        }
    }

    private void Hide()
    {
        if (line != null)
            line.enabled = false;

        HidePrismContinuation();
        HideRiftContinuation();
        PrismCrystalRuntime.ClearPreviewFocus();
        CrystalRiftEntryRuntime.ClearPreviewFocus();
    }

    private void Unsubscribe()
    {
        if (bow == null)
            return;

        bow.AimStarted -= OnAimStarted;
        bow.AimChanged -= OnAimChanged;
        bow.AimReleased -= OnAimReleased;
        bow.AimCancelled -= OnAimCancelled;
    }

    private void OnDestroy()
    {
        Unsubscribe();

        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }

        if (prismLineMaterial != null)
        {
            Destroy(prismLineMaterial);
            prismLineMaterial = null;
        }

        if (riftLineMaterial != null)
        {
            Destroy(riftLineMaterial);
            riftLineMaterial = null;
        }
    }
}
