using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TargetHitZone
{
    Invalid = 0,
    OuterRed = 1,
    Blue = 2,
    Yellow = 3,
    InnerRed = 4,
    Bullseye = 5
}

public readonly struct TargetHitResult
{
    public TargetHitResult(
        TargetHitZone zone,
        int score,
        Vector2 worldPoint)
    {
        Zone = zone;
        Score = score;
        WorldPoint = worldPoint;
    }

    public TargetHitZone Zone { get; }
    public int Score { get; }
    public Vector2 WorldPoint { get; }
    public bool IsBullseye => Zone == TargetHitZone.Bullseye;

    public string Label =>
        Zone switch
        {
            TargetHitZone.Bullseye => "BULLSEYE!",
            TargetHitZone.InnerRed => "EXCELLENT HIT!",
            TargetHitZone.Yellow => "GREAT HIT!",
            TargetHitZone.Blue => "GOOD HIT!",
            TargetHitZone.OuterRed => "TARGET HIT!",
            _ => "INVALID HIT"
        };
}

/// <summary>
/// Target collision is intentionally split into two independent systems:
///
/// 1) ScoringFace
///    A thin authored PolygonCollider2D strip across the painted target face.
///    Only this collider can complete the level and award a ring score.
///
/// 2) PhysicalParts
///    Separate trigger colliders for the rear body/rim and legs. These consume
///    the shot, but never complete the level.
///
/// There is deliberately NO full-silhouette collider on the Target root.
/// That prevents a large auto-generated outline from stopping a valid arrow
/// before it reaches ScoringFace.
/// </summary>
[DisallowMultipleComponent]
public sealed class Target : MonoBehaviour
{
    private const float DirectionEpsilon = 0.0001f;
    private const float IntersectionMergeEpsilon = 0.0001f;

    [Header("Collision Geometry")]
    [Tooltip(
        "Parent of ScoringFace and all non-scoring PhysicalParts. " +
        "TargetVisualFacing mirrors this root together with the artwork.")]
    [SerializeField]
    private Transform collisionRoot;

    [Tooltip(
        "Thin curved trigger representing the real painted scoring plane. " +
        "The arrow stops at the centre plane of this strip, not its entry edge.")]
    [SerializeField]
    private PolygonCollider2D scoringFace;

    [Header("Score Markers")]
    [Tooltip(
        "Parent of the eight authored ring-boundary markers. " +
        "Only marker Y values are used for ring classification.")]
    [SerializeField]
    private Transform scoreMarkers;

    [SerializeField] private Transform blue200Top;
    [SerializeField] private Transform yellow300Top;
    [SerializeField] private Transform red400Top;
    [SerializeField] private Transform bullseyeTop;
    [SerializeField] private Transform bullseyeBottom;
    [SerializeField] private Transform red400Bottom;
    [SerializeField] private Transform yellow300Bottom;
    [SerializeField] private Transform blue200Bottom;

    [Header("Front Face Validation")]
    [Tooltip(
        "Minimum horizontal component toward the visible target face. " +
        "Rejects hits arriving almost straight from above/below or from the back.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float minimumFrontApproach = 0.12f;

    [Tooltip(
        "Horizontal movement must be at least this fraction of vertical movement. " +
        "Steep ricochet shots still work, while near-vertical underside hits do not.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float horizontalToVerticalRatio = 0.25f;

    [Header("Score")]
    [SerializeField] private int outerRedScore = 100;
    [SerializeField] private int blueScore = 200;
    [SerializeField] private int yellowScore = 300;
    [SerializeField] private int innerRedScore = 400;
    [SerializeField] private int bullseyeScore = 500;

    private bool completed;
    private bool facingRight;
    private bool markerErrorLogged;

    private GameConfig config;
    private Coroutine hitPulseRoutine;
    private Vector3 hitPulseBaseScale;
    private SpriteRenderer[] hitRenderers;
    private Color[] hitBaseColors;

    private bool authoredCollisionRootCaptured;
    private Vector3 authoredCollisionRootLocalPosition;
    private Vector3 authoredCollisionRootLocalScale = Vector3.one;

    public event Action Hit;
    public event Action<TargetHitResult> ScoredHit;
    public event Action InvalidHit;

    public TargetHitResult LastHitResult { get; private set; }

    private void Awake()
    {
        config = GameConfig.Load();
        CacheHitVisuals();
        ResolveReferences();
        CaptureAuthoredCollisionRoot();

        if (scoringFace != null)
            scoringFace.isTrigger = true;
    }

    /// <summary>
    /// Editor/setup helper. Runtime code also resolves these by hierarchy name,
    /// so prefab references are not fragile.
    /// </summary>
    public void ConfigureCollisionGeometry(
        Transform newCollisionRoot,
        PolygonCollider2D newScoringFace)
    {
        collisionRoot = newCollisionRoot;
        scoringFace = newScoringFace;

        scoreMarkers = scoringFace != null
            ? scoringFace.transform.Find("ScoreMarkers")
            : null;

        blue200Top = scoreMarkers?.Find("Blue200Top");
        yellow300Top = scoreMarkers?.Find("Yellow300Top");
        red400Top = scoreMarkers?.Find("Red400Top");
        bullseyeTop = scoreMarkers?.Find("BullseyeTop");
        bullseyeBottom = scoreMarkers?.Find("BullseyeBottom");
        red400Bottom = scoreMarkers?.Find("Red400Bottom");
        yellow300Bottom = scoreMarkers?.Find("Yellow300Bottom");
        blue200Bottom = scoreMarkers?.Find("Blue200Bottom");

        authoredCollisionRootCaptured = false;
        CaptureAuthoredCollisionRoot();
    }

    /// <summary>
    /// Called by TargetVisualFacing. Artwork and all collision geometry mirror
    /// around the Target root as one unit.
    /// </summary>
    public void ApplyFacing(bool shouldFaceRight)
    {
        ResolveReferences();
        CaptureAuthoredCollisionRoot();

        facingRight = shouldFaceRight;

        if (collisionRoot == null)
            return;

        Vector3 localPosition = authoredCollisionRootLocalPosition;
        Vector3 localScale = authoredCollisionRootLocalScale;

        localPosition.x = shouldFaceRight
            ? -Mathf.Abs(authoredCollisionRootLocalPosition.x)
            : Mathf.Abs(authoredCollisionRootLocalPosition.x);

        localScale.x = shouldFaceRight
            ? -Mathf.Abs(authoredCollisionRootLocalScale.x)
            : Mathf.Abs(authoredCollisionRootLocalScale.x);

        collisionRoot.localPosition = localPosition;
        collisionRoot.localScale = localScale;
    }

    /// <summary>
    /// Called only by TargetContactSensor children.
    /// </summary>
    public void HandleSensorEnter(
        TargetContactKind kind,
        Collider2D sensorCollider,
        Collider2D other)
    {
        if (completed ||
            sensorCollider == null ||
            !sensorCollider.enabled)
        {
            return;
        }

        ArrowController arrow =
            other != null
                ? other.GetComponent<ArrowController>()
                : null;

        if (arrow == null && other != null)
        {
            arrow =
                other.GetComponentInParent<ArrowController>();
        }

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        Vector2 velocity = arrow.GetVelocity();
        if (velocity.sqrMagnitude < DirectionEpsilon)
            return;

        Vector2 incomingDirection = velocity.normalized;
        Vector2 currentTip =
            arrow.GetVisualTipWorldPosition(incomingDirection);

        BuildContactSearchSegment(
            arrow,
            sensorCollider,
            currentTip,
            incomingDirection,
            velocity.magnitude,
            out Vector2 segmentStart,
            out Vector2 segmentEnd);

        if (kind == TargetContactKind.ScoringFace)
        {
            HandleScoringSensor(
                arrow,
                incomingDirection,
                currentTip,
                segmentStart,
                segmentEnd);

            return;
        }

        HandlePhysicalSensor(
            arrow,
            sensorCollider,
            incomingDirection,
            currentTip,
            segmentStart,
            segmentEnd);
    }

    private void HandleScoringSensor(
        ArrowController arrow,
        Vector2 incomingDirection,
        Vector2 currentTip,
        Vector2 segmentStart,
        Vector2 segmentEnd)
    {
        ResolveReferences();

        if (scoringFace == null)
            return;

        // A scoring trigger reached from the back/underside is still a MISS.
        // It stops at the scoring strip entry boundary rather than passing
        // through the target.
        if (!IsValidFrontApproach(incomingDirection))
        {
            Vector2 invalidContact = currentTip;

            TryFindFirstIntersection(
                scoringFace,
                segmentStart,
                segmentEnd,
                out invalidContact,
                out _);

            HandleInvalidHit(
                arrow,
                invalidContact,
                incomingDirection);

            return;
        }

        if (!TryFindScoringPlaneContact(
                scoringFace,
                segmentStart,
                segmentEnd,
                out Vector2 scoringContact))
        {
            // Trigger callbacks can happen when the physics body already
            // overlaps the strip. ClosestPoint is a safe visual fallback.
            scoringContact =
                scoringFace.ClosestPoint(currentTip);
        }

        HandleScoringHit(
            arrow,
            scoringContact,
            incomingDirection);
    }

    private void HandlePhysicalSensor(
        ArrowController arrow,
        Collider2D sensorCollider,
        Vector2 incomingDirection,
        Vector2 currentTip,
        Vector2 segmentStart,
        Vector2 segmentEnd)
    {
        Vector2 physicalContact = currentTip;

        if (sensorCollider is PolygonCollider2D polygon)
        {
            TryFindFirstIntersection(
                polygon,
                segmentStart,
                segmentEnd,
                out physicalContact,
                out _);
        }
        else
        {
            physicalContact =
                sensorCollider.ClosestPoint(currentTip);
        }

        HandleInvalidHit(
            arrow,
            physicalContact,
            incomingDirection);
    }

    private void HandleScoringHit(
        ArrowController arrow,
        Vector2 contactPoint,
        Vector2 incomingDirection)
    {
        TargetHitZone zone = EvaluateHitZone(contactPoint);

        if (zone == TargetHitZone.Invalid)
        {
            HandleInvalidHit(
                arrow,
                contactPoint,
                incomingDirection);

            return;
        }

        completed = true;

        int score = GetScore(zone);
        LastHitResult = new TargetHitResult(
            zone,
            score,
            contactPoint);

        // IMPORTANT:
        // scoringFace has deliberate thickness for reliable high-speed trigger
        // detection. The stop point is the midpoint between its entry and exit
        // intersections, so that thickness never makes the arrow stop early.
        arrow.StopAtVisualTipContact(
            contactPoint,
            incomingDirection);

        PlaySuccessPulse(zone);
        ScoredHit?.Invoke(LastHitResult);
        Hit?.Invoke();
    }

    private void PlaySuccessPulse(TargetHitZone zone)
    {
        if (!isActiveAndEnabled)
            return;

        if (hitPulseRoutine != null)
        {
            StopCoroutine(hitPulseRoutine);
            transform.localScale = hitPulseBaseScale;
        }

        // Capture after LevelManager has applied this level's target scale.
        hitPulseBaseScale = transform.localScale;
        hitPulseRoutine = StartCoroutine(
            SuccessPulseRoutine(zone));
    }

    private IEnumerator SuccessPulseRoutine(TargetHitZone zone)
    {
        float duration = Mathf.Max(
            0.05f,
            config != null
                ? config.TargetHitPulseDuration
                : 0.18f);

        float basePeak = config != null
            ? config.TargetHitPulseScale
            : 1.035f;

        float peak = zone == TargetHitZone.Bullseye
            ? basePeak + 0.012f
            : basePeak;

        float firstHalf = duration * 0.42f;
        float elapsed = 0f;

        while (elapsed < firstHalf)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(
                elapsed / Mathf.Max(0.001f, firstHalf));
            float eased = 1f - (1f - t) * (1f - t);
            transform.localScale =
                hitPulseBaseScale * Mathf.Lerp(1f, peak, eased);

            ApplyHitColor(
                zone == TargetHitZone.Bullseye
                    ? config.YellowColor
                    : Color.white,
                Mathf.Lerp(0f, 0.34f, eased));

            yield return null;
        }

        float returnDuration = Mathf.Max(0.001f, duration - firstHalf);
        elapsed = 0f;

        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / returnDuration);
            transform.localScale =
                hitPulseBaseScale * Mathf.Lerp(peak, 1f, t);

            ApplyHitColor(
                zone == TargetHitZone.Bullseye
                    ? config.YellowColor
                    : Color.white,
                Mathf.Lerp(0.34f, 0f, t));

            yield return null;
        }

        transform.localScale = hitPulseBaseScale;
        RestoreHitColors();
        hitPulseRoutine = null;
    }

    private void CacheHitVisuals()
    {
        hitRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        hitBaseColors = new Color[hitRenderers.Length];

        for (int i = 0; i < hitRenderers.Length; i++)
            hitBaseColors[i] = hitRenderers[i].color;
    }

    private void ApplyHitColor(Color targetColor, float amount)
    {
        if (hitRenderers == null || hitBaseColors == null)
            return;

        int count = Mathf.Min(hitRenderers.Length, hitBaseColors.Length);
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = hitRenderers[i];
            if (renderer == null)
                continue;

            Color baseColor = hitBaseColors[i];
            Color color = Color.Lerp(baseColor, targetColor, amount);
            color.a = baseColor.a;
            renderer.color = color;
        }
    }

    private void RestoreHitColors()
    {
        if (hitRenderers == null || hitBaseColors == null)
            return;

        int count = Mathf.Min(hitRenderers.Length, hitBaseColors.Length);
        for (int i = 0; i < count; i++)
        {
            if (hitRenderers[i] != null)
                hitRenderers[i].color = hitBaseColors[i];
        }
    }

    private void HandleInvalidHit(
        ArrowController arrow,
        Vector2 contactPoint,
        Vector2 incomingDirection)
    {
        arrow.StopAtVisualTipContact(
            contactPoint,
            incomingDirection);

        InvalidHit?.Invoke();
    }

    private bool IsValidFrontApproach(Vector2 incomingDirection)
    {
        Vector3 localDirection3 =
            transform.InverseTransformDirection(
                new Vector3(
                    incomingDirection.x,
                    incomingDirection.y,
                    0f));

        Vector2 localDirection =
            new Vector2(
                localDirection3.x,
                localDirection3.y);

        if (localDirection.sqrMagnitude < DirectionEpsilon)
            return false;

        localDirection.Normalize();

        float frontComponent = facingRight
            ? -localDirection.x
            : localDirection.x;

        if (frontComponent < minimumFrontApproach)
            return false;

        float verticalComponent = Mathf.Abs(localDirection.y);

        return frontComponent >=
            verticalComponent * horizontalToVerticalRatio;
    }

    private TargetHitZone EvaluateHitZone(Vector2 worldContactPoint)
    {
        if (!TryGetMarkerValues(
                out float blue200TopY,
                out float yellow300TopY,
                out float red400TopY,
                out float bullseyeTopY,
                out float bullseyeBottomY,
                out float red400BottomY,
                out float yellow300BottomY,
                out float blue200BottomY))
        {
            return TargetHitZone.Invalid;
        }

        float hitY =
            scoreMarkers
                .InverseTransformPoint(worldContactPoint)
                .y;

        if (hitY <= bullseyeTopY &&
            hitY >= bullseyeBottomY)
        {
            return TargetHitZone.Bullseye;
        }

        if (hitY <= red400TopY &&
            hitY >= red400BottomY)
        {
            return TargetHitZone.InnerRed;
        }

        if (hitY <= yellow300TopY &&
            hitY >= yellow300BottomY)
        {
            return TargetHitZone.Yellow;
        }

        if (hitY <= blue200TopY &&
            hitY >= blue200BottomY)
        {
            return TargetHitZone.Blue;
        }

        // The remaining part of ScoringFace is the outer painted red ring.
        return TargetHitZone.OuterRed;
    }

    private bool TryGetMarkerValues(
        out float blue200TopY,
        out float yellow300TopY,
        out float red400TopY,
        out float bullseyeTopY,
        out float bullseyeBottomY,
        out float red400BottomY,
        out float yellow300BottomY,
        out float blue200BottomY)
    {
        ResolveReferences();

        blue200TopY = 0f;
        yellow300TopY = 0f;
        red400TopY = 0f;
        bullseyeTopY = 0f;
        bullseyeBottomY = 0f;
        red400BottomY = 0f;
        yellow300BottomY = 0f;
        blue200BottomY = 0f;

        if (scoreMarkers == null ||
            blue200Top == null ||
            yellow300Top == null ||
            red400Top == null ||
            bullseyeTop == null ||
            bullseyeBottom == null ||
            red400Bottom == null ||
            yellow300Bottom == null ||
            blue200Bottom == null)
        {
            LogMarkerConfigurationError(
                "one or more ScoreMarkers are missing");

            return false;
        }

        blue200TopY = GetMarkerY(blue200Top);
        yellow300TopY = GetMarkerY(yellow300Top);
        red400TopY = GetMarkerY(red400Top);
        bullseyeTopY = GetMarkerY(bullseyeTop);
        bullseyeBottomY = GetMarkerY(bullseyeBottom);
        red400BottomY = GetMarkerY(red400Bottom);
        yellow300BottomY = GetMarkerY(yellow300Bottom);
        blue200BottomY = GetMarkerY(blue200Bottom);

        bool ordered =
            blue200TopY > yellow300TopY &&
            yellow300TopY > red400TopY &&
            red400TopY > bullseyeTopY &&
            bullseyeTopY > bullseyeBottomY &&
            bullseyeBottomY > red400BottomY &&
            red400BottomY > yellow300BottomY &&
            yellow300BottomY > blue200BottomY;

        if (!ordered)
        {
            LogMarkerConfigurationError(
                "marker Y values are not in the required top-to-bottom order");

            return false;
        }

        return true;
    }

    private float GetMarkerY(Transform marker)
    {
        return scoreMarkers
            .InverseTransformPoint(marker.position)
            .y;
    }

    private void ResolveReferences()
    {
        if (collisionRoot == null)
            collisionRoot = transform.Find("CollisionRoot");

        if (scoringFace == null)
        {
            Transform scoringFaceTransform =
                collisionRoot != null
                    ? collisionRoot.Find("ScoringFace")
                    : transform.Find("ScoringFace");

            if (scoringFaceTransform != null)
            {
                scoringFace =
                    scoringFaceTransform
                        .GetComponent<PolygonCollider2D>();
            }
        }

        if (scoreMarkers == null && scoringFace != null)
        {
            scoreMarkers =
                scoringFace.transform.Find("ScoreMarkers");
        }

        if (scoreMarkers == null)
            return;

        blue200Top ??= scoreMarkers.Find("Blue200Top");
        yellow300Top ??= scoreMarkers.Find("Yellow300Top");
        red400Top ??= scoreMarkers.Find("Red400Top");
        bullseyeTop ??= scoreMarkers.Find("BullseyeTop");
        bullseyeBottom ??= scoreMarkers.Find("BullseyeBottom");
        red400Bottom ??= scoreMarkers.Find("Red400Bottom");
        yellow300Bottom ??= scoreMarkers.Find("Yellow300Bottom");
        blue200Bottom ??= scoreMarkers.Find("Blue200Bottom");
    }

    private void CaptureAuthoredCollisionRoot()
    {
        if (authoredCollisionRootCaptured)
            return;

        ResolveReferences();

        if (collisionRoot != null)
        {
            authoredCollisionRootLocalPosition =
                collisionRoot.localPosition;

            authoredCollisionRootLocalScale =
                collisionRoot.localScale;
        }

        authoredCollisionRootCaptured = true;
    }

    private void LogMarkerConfigurationError(string reason)
    {
        if (markerErrorLogged)
            return;

        markerErrorLogged = true;

        Debug.LogError(
            $"Target scoring is disabled on '{name}': {reason}. " +
            "Expected CollisionRoot/ScoringFace/ScoreMarkers with " +
            "Blue200Top, Yellow300Top, Red400Top, BullseyeTop, " +
            "BullseyeBottom, Red400Bottom, Yellow300Bottom and " +
            "Blue200Bottom.",
            this);
    }

    private static void BuildContactSearchSegment(
        ArrowController arrow,
        Collider2D sensorCollider,
        Vector2 currentTip,
        Vector2 incomingDirection,
        float currentSpeed,
        out Vector2 segmentStart,
        out Vector2 segmentEnd)
    {
        float sensorSpan = sensorCollider != null
            ? sensorCollider.bounds.size.magnitude
            : 4f;

        float arrowTipDistance = Vector2.Distance(
            currentTip,
            arrow.transform.position);

        float physicsTravel =
            currentSpeed * Time.fixedDeltaTime;

        float searchDistance = Mathf.Max(
            1f,
            sensorSpan +
            arrowTipDistance * 2f +
            physicsTravel * 3f);

        segmentStart =
            currentTip -
            incomingDirection * searchDistance;

        segmentEnd =
            currentTip +
            incomingDirection * searchDistance;
    }

    /// <summary>
    /// Returns the centre plane of the authored scoring strip.
    /// For the normal case this is the midpoint between the FIRST and SECOND
    /// boundary intersections along the arrow path. The collider can therefore
    /// keep useful detection thickness without changing the visible stop point.
    /// </summary>
    private static bool TryFindScoringPlaneContact(
        PolygonCollider2D collider,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out Vector2 contactPoint)
    {
        contactPoint = default;

        if (!TryCollectIntersections(
                collider,
                segmentStart,
                segmentEnd,
                out List<float> intersections))
        {
            return false;
        }

        if (intersections.Count == 1)
        {
            float onlyT = intersections[0];
            contactPoint =
                Vector2.Lerp(segmentStart, segmentEnd, onlyT);

            return true;
        }

        // First two unique crossings = entry and exit of the thin strip.
        float planeT =
            (intersections[0] + intersections[1]) * 0.5f;

        contactPoint =
            Vector2.Lerp(segmentStart, segmentEnd, planeT);

        return true;
    }

    private static bool TryFindFirstIntersection(
        PolygonCollider2D collider,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out Vector2 intersectionPoint,
        out float normalizedDistance)
    {
        intersectionPoint = default;
        normalizedDistance = float.PositiveInfinity;

        if (!TryCollectIntersections(
                collider,
                segmentStart,
                segmentEnd,
                out List<float> intersections))
        {
            return false;
        }

        normalizedDistance = intersections[0];
        intersectionPoint =
            Vector2.Lerp(
                segmentStart,
                segmentEnd,
                normalizedDistance);

        return true;
    }

    private static bool TryCollectIntersections(
        PolygonCollider2D collider,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out List<float> sortedIntersections)
    {
        sortedIntersections = new List<float>(4);

        if (collider == null ||
            !collider.enabled ||
            collider.pathCount == 0)
        {
            return false;
        }

        for (int pathIndex = 0;
             pathIndex < collider.pathCount;
             pathIndex++)
        {
            Vector2[] path =
                collider.GetPath(pathIndex);

            if (path == null || path.Length < 2)
                continue;

            for (int i = 0; i < path.Length; i++)
            {
                Vector2 localA =
                    path[i] + collider.offset;

                Vector2 localB =
                    path[(i + 1) % path.Length] +
                    collider.offset;

                Vector2 worldA =
                    collider.transform.TransformPoint(localA);

                Vector2 worldB =
                    collider.transform.TransformPoint(localB);

                if (!TrySegmentIntersection(
                        segmentStart,
                        segmentEnd,
                        worldA,
                        worldB,
                        out float t))
                {
                    continue;
                }

                bool duplicate = false;

                for (int existingIndex = 0;
                     existingIndex < sortedIntersections.Count;
                     existingIndex++)
                {
                    if (Mathf.Abs(
                            sortedIntersections[existingIndex] - t) <=
                        IntersectionMergeEpsilon)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    sortedIntersections.Add(t);
            }
        }

        if (sortedIntersections.Count == 0)
            return false;

        sortedIntersections.Sort();
        return true;
    }

    private static bool TrySegmentIntersection(
        Vector2 p1,
        Vector2 p2,
        Vector2 q1,
        Vector2 q2,
        out float t)
    {
        const float epsilon = 0.000001f;

        Vector2 r = p2 - p1;
        Vector2 s = q2 - q1;

        float denominator = Cross(r, s);
        t = 0f;

        if (Mathf.Abs(denominator) < epsilon)
            return false;

        Vector2 qMinusP = q1 - p1;

        float candidateT =
            Cross(qMinusP, s) /
            denominator;

        float candidateU =
            Cross(qMinusP, r) /
            denominator;

        if (candidateT < -epsilon ||
            candidateT > 1f + epsilon ||
            candidateU < -epsilon ||
            candidateU > 1f + epsilon)
        {
            return false;
        }

        t = Mathf.Clamp01(candidateT);
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private int GetScore(TargetHitZone zone)
    {
        return zone switch
        {
            TargetHitZone.Bullseye => bullseyeScore,
            TargetHitZone.InnerRed => innerRedScore,
            TargetHitZone.Yellow => yellowScore,
            TargetHitZone.Blue => blueScore,
            TargetHitZone.OuterRed => outerRedScore,
            _ => 0
        };
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        outerRedScore = Mathf.Max(0, outerRedScore);
        blueScore = Mathf.Max(outerRedScore, blueScore);
        yellowScore = Mathf.Max(blueScore, yellowScore);
        innerRedScore = Mathf.Max(yellowScore, innerRedScore);
        bullseyeScore = Mathf.Max(innerRedScore, bullseyeScore);
    }
#endif
    private void OnDisable()
    {
        if (hitPulseRoutine != null)
            StopCoroutine(hitPulseRoutine);

        if (hitPulseBaseScale != Vector3.zero)
            transform.localScale = hitPulseBaseScale;

        RestoreHitColors();
        hitPulseRoutine = null;
    }

}
