using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders the dotted aiming guide.
///
/// Default mode keeps the existing short/direct guide.
/// Full-path mode predicts mirror ricochets using the actual arrowhead
/// collider footprint, so near-edge wall clearance matches real arrow
/// collision much more closely.
///
/// This is prediction/presentation only. It never changes real arrow physics.
/// </summary>
public sealed class AimTrajectoryRenderer : MonoBehaviour
{
    private const float DirectionEpsilon = 0.0001f;

    private const string ArrowPrefabResourcePath =
        "Prefabs/Gameplay/Arrow";

    // Safe fallback matching the current arrowhead collider approximately
    // after the Arrow prefab's root scale is applied.
    private static readonly Vector2 FallbackArrowCastSize =
        new Vector2(0.195f, 0.084f);

    private static readonly Vector2 FallbackArrowColliderOffset =
        new Vector2(0.57f, 0f);

    private readonly List<SpriteRenderer> dots =
        new List<SpriteRenderer>();

    private readonly List<SpriteRenderer> dotShadows =
        new List<SpriteRenderer>();

    private readonly List<Vector2> pathPoints =
        new List<Vector2>(12);

    private readonly List<Vector2> bouncePoints =
        new List<Vector2>(8);

    private readonly RaycastHit2D[] raycastHits =
        new RaycastHit2D[64];

    private readonly List<SpriteRenderer> bounceMarkers =
        new List<SpriteRenderer>(8);

    private GameConfig config;
    private Sprite dotSprite;
    private bool visible;
    private bool fullPathEnabled;

    // Full-path prediction uses BoxCast and is intentionally more expensive
    // than drawing the dots. Skip recalculation when the held arrow has moved
    // by less than a tiny visual threshold and the aim angle is effectively
    // unchanged. This removes redundant physics queries on mobile.
    private bool previewCacheValid;
    private Vector2 lastPreviewOrigin;
    private Vector2 lastPreviewDirection = Vector2.right;
    private bool lastPreviewWasFullPath;

    // The actual held ArrowController is supplied by BowController. Using the
    // live arrow avoids guessing where its Rigidbody/pivot sits after the
    // sprite tail has been aligned to the bow string.
    private ArrowController predictionArrow;
    private Transform predictionArrowRoot;
    private SpriteRenderer predictionArrowSprite;

    // Sprite vertices expressed in arrow-root local space. These let the
    // preview reproduce ArrowController.GetVisualTipWorldPosition() for any
    // predicted direction without rotating the real held arrow.
    private readonly List<Vector2> arrowVisualVerticesRootLocal =
        new List<Vector2>(16);

    private Vector2 arrowRootWorldScale = Vector2.one;

    // World-space dimensions of the arrow collision footprint used only
    // for trajectory prediction.
    private Vector2 arrowCastSize = FallbackArrowCastSize;

    // Local-space collider center relative to the arrow pivot, converted
    // to world units from the Arrow prefab.
    private Vector2 arrowColliderOffset =
        FallbackArrowColliderOffset;

    public bool FullPathEnabled => fullPathEnabled;

    public void Configure(GameConfig gameConfig)
    {
        config =
            gameConfig != null
                ? gameConfig
                : GameConfig.Load();

        CacheArrowPredictionShape();
        InvalidatePreviewCache();
        EnsureDots();
        EnsureBounceMarkers();
        Hide();
    }

    private void Awake()
    {
        Configure(GameConfig.Load());
    }

    public void SetPredictionArrow(ArrowController arrowController)
    {
        predictionArrow = arrowController;
        CacheArrowPredictionShape();
        InvalidatePreviewCache();
    }

    public void SetFullPathEnabled(bool enabled)
    {
        if (fullPathEnabled == enabled)
            return;

        fullPathEnabled = enabled;
        InvalidatePreviewCache();

        if (!enabled)
            SetBounceMarkersActive(false);
    }

    public void Show()
    {
        EnsureDots();
        visible = true;
        InvalidatePreviewCache();
    }

    public void Hide()
    {
        visible = false;
        InvalidatePreviewCache();
        SetDotsActive(false, 0);
        SetBounceMarkersActive(false);
    }

    public void UpdateTrajectory(
        Vector2 origin,
        Vector2 direction)
    {
        if (!visible ||
            direction.sqrMagnitude < DirectionEpsilon)
        {
            return;
        }

        EnsureDots();
        direction.Normalize();

        if (CanReusePreview(origin, direction))
            return;

        lastPreviewOrigin = origin;
        lastPreviewDirection = direction;
        lastPreviewWasFullPath = fullPathEnabled;
        previewCacheValid = true;

        if (fullPathEnabled)
        {
            UpdateFullPath(origin, direction);
            return;
        }

        UpdateDirectPath(origin, direction);
    }

    private bool CanReusePreview(
        Vector2 origin,
        Vector2 direction)
    {
        if (!previewCacheValid ||
            config == null ||
            lastPreviewWasFullPath != fullPathEnabled)
        {
            return false;
        }

        float minPositionDelta =
            Mathf.Max(0f, config.TrajectoryMinPositionDelta);

        if ((origin - lastPreviewOrigin).sqrMagnitude >
            minPositionDelta * minPositionDelta)
        {
            return false;
        }

        float minAngleDelta =
            Mathf.Max(0f, config.TrajectoryMinAngleDelta);

        if (minAngleDelta <= 0f)
            return direction == lastPreviewDirection;

        float minimumDot =
            Mathf.Cos(minAngleDelta * Mathf.Deg2Rad);

        return Vector2.Dot(
            direction,
            lastPreviewDirection) >= minimumDot;
    }

    private void InvalidatePreviewCache()
    {
        previewCacheValid = false;
    }

    private void UpdateDirectPath(
        Vector2 origin,
        Vector2 direction)
    {
        SetBounceMarkersActive(false);

        int count = Mathf.Clamp(
            config.TrajectoryDotCount,
            1,
            dots.Count);

        SetDotsActive(true, count);

        for (int i = 0; i < count; i++)
        {
            float normalized =
                count <= 1
                    ? 0f
                    : i / (float)(count - 1);

            float distance =
                config.TrajectoryStartOffset +
                config.TrajectorySpacing * i;

            Vector2 worldPosition =
                origin +
                GetVisualTipOffset(direction) +
                direction * distance;

            ApplyDotVisual(
                i,
                worldPosition,
                normalized,
                config.TrajectoryColor);
        }
    }

    private void UpdateFullPath(
        Vector2 origin,
        Vector2 direction)
    {
        BuildPredictedPath(
            origin,
            direction,
            pathPoints,
            bouncePoints);

        int maxDots = Mathf.Clamp(
            config.FullTrajectoryDotCount,
            1,
            dots.Count);

        int usedDots = PlaceDotsAlongPath(
            pathPoints,
            maxDots);

        SetDotsActive(true, usedDots);
        UpdateBounceMarkers(bouncePoints);
    }

    private void BuildPredictedPath(
        Vector2 origin,
        Vector2 direction,
        List<Vector2> points,
        List<Vector2> bounces)
    {
        points.Clear();
        bounces.Clear();

        Vector2 arrowPivot = origin;
        Vector2 rayDirection = direction.normalized;

        // The guide represents the VISUAL ARROWHEAD path. That is the point
        // players judge against the mirror, and ArrowController also preserves
        // this exact tip at mirror incidence.
        points.Add(
            arrowPivot +
            GetVisualTipOffset(rayDirection));

        float remainingDistance =
            Mathf.Max(1f, config.FullTrajectoryMaxDistance);

        int maxBounces =
            Mathf.Max(0, config.FullTrajectoryMaxBounces);

        Collider2D previousMirrorCollider = null;
        int bounceCount = 0;

        while (remainingDistance > 0.001f)
        {
            if (!TryGetFirstRelevantHit(
                    arrowPivot,
                    rayDirection,
                    remainingDistance,
                    previousMirrorCollider,
                    out RaycastHit2D hit))
            {
                Vector2 endPivot =
                    arrowPivot +
                    rayDirection * remainingDistance;

                points.Add(
                    endPivot +
                    GetVisualTipOffset(rayDirection));
                break;
            }

            // BoxCast distance is the distance travelled by the arrow pivot
            // before its real arrowhead collider first touches something.
            Vector2 impactPivot =
                arrowPivot +
                rayDirection * hit.distance;

            Mirror mirror =
                hit.collider != null
                    ? hit.collider.GetComponentInParent<Mirror>()
                    : null;

            if (mirror == null ||
                bounceCount >= maxBounces)
            {
                // For a wall/target, stop the preview where the visible tip
                // really is when the collider first makes contact.
                points.Add(
                    impactPivot +
                    GetVisualTipOffset(rayDirection));
                break;
            }

            // Mirror.cs uses ArrowController.ReflectFromMirror(). That method
            // does NOT reflect at the BoxCast corner. It raycasts along the
            // visible arrow tip to recover the mirror's true surface point and
            // normal. Reproduce the same calculation here.
            Vector2 contactPoint;
            Vector2 ignoredPhysicsNormal;

            // Match ArrowController: mirror edges still use the mirror plane
            // normal instead of the BoxCollider2D end-cap normal.
            Vector2 surfaceNormal =
                (Vector2)hit.collider.transform.right;

            if (surfaceNormal.sqrMagnitude < DirectionEpsilon)
                surfaceNormal = Vector2.right;

            surfaceNormal.Normalize();

            if (!TryFindMirrorSurfaceHit(
                    hit.collider,
                    impactPivot,
                    rayDirection,
                    out contactPoint,
                    out ignoredPhysicsNormal))
            {
                contactPoint =
                    CalculateMirrorPlaneIntersection(
                        hit.collider,
                        impactPivot,
                        rayDirection,
                        surfaceNormal);
            }

            if (surfaceNormal.sqrMagnitude < DirectionEpsilon)
                break;

            surfaceNormal.Normalize();

            Vector2 reflectedDirection =
                Vector2.Reflect(
                    rayDirection,
                    surfaceNormal);

            if (reflectedDirection.sqrMagnitude < DirectionEpsilon)
                break;

            reflectedDirection.Normalize();

            // The yellow marker and the green path now use exactly the same
            // mirror incidence point as ArrowController.
            points.Add(contactPoint);
            bounces.Add(contactPoint);
            bounceCount++;

            remainingDistance -=
                Mathf.Max(0f, hit.distance);

            previousMirrorCollider = hit.collider;
            rayDirection = reflectedDirection;

            // ArrowController rotates the arrow, then translates its Rigidbody
            // so the rendered arrowhead stays exactly on contactPoint. Recreate
            // that tip-preserving pivot correction before predicting the next
            // segment.
            arrowPivot =
                contactPoint -
                GetVisualTipOffset(reflectedDirection);
        }
    }

    private bool TryGetFirstRelevantHit(
        Vector2 arrowPivot,
        Vector2 direction,
        float distance,
        Collider2D previousMirrorCollider,
        out RaycastHit2D closestHit)
    {
        closestHit = default;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;

        float angleDegrees =
            GetArrowRotationAngle(direction);

        Vector2 castCenter =
            arrowPivot +
            RotateVector(
                arrowColliderOffset,
                angleDegrees);

        int hitCount = Physics2D.BoxCast(
            castCenter,
            arrowCastSize,
            angleDegrees,
            direction,
            filter,
            raycastHits,
            distance);

        float closestDistance =
            float.PositiveInfinity;

        bool found = false;

        float previousMirrorIgnoreDistance =
            Mathf.Max(
                0.08f,
                arrowCastSize.x * 0.65f);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = raycastHits[i];
            Collider2D collider = hit.collider;

            if (collider == null)
                continue;

            if (collider.GetComponentInParent<ArrowController>() != null)
                continue;

            if (collider.GetComponentInParent<BowController>() != null)
                continue;

            if (collider == previousMirrorCollider &&
                hit.distance <= previousMirrorIgnoreDistance)
            {
                continue;
            }

            if (hit.distance < 0f ||
                hit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = hit.distance;
            closestHit = hit;
            found = true;
        }

        return found;
    }

    private void CacheArrowPredictionShape()
    {
        arrowCastSize = FallbackArrowCastSize;
        arrowColliderOffset = FallbackArrowColliderOffset;
        arrowVisualVerticesRootLocal.Clear();
        predictionArrowRoot = null;
        predictionArrowSprite = null;
        arrowRootWorldScale = Vector2.one;

        GameObject arrowObject =
            predictionArrow != null
                ? predictionArrow.gameObject
                : Resources.Load<GameObject>(ArrowPrefabResourcePath);

        if (arrowObject == null)
        {
            Debug.LogWarning(
                $"AimTrajectoryRenderer could not resolve the Arrow at Resources/{ArrowPrefabResourcePath}. " +
                "Using fallback trajectory dimensions.");
            return;
        }

        predictionArrowRoot = arrowObject.transform;

        BoxCollider2D arrowCollider =
            arrowObject.GetComponentInChildren<BoxCollider2D>(true);

        predictionArrowSprite =
            arrowObject.GetComponentInChildren<SpriteRenderer>(true);

        Vector3 rootScale3 =
            predictionArrowRoot.lossyScale;

        arrowRootWorldScale =
            new Vector2(
                Mathf.Abs(rootScale3.x),
                Mathf.Abs(rootScale3.y));

        if (arrowCollider != null)
        {
            Vector3 colliderScale =
                arrowCollider.transform.lossyScale;

            arrowCastSize =
                new Vector2(
                    Mathf.Abs(
                        arrowCollider.size.x *
                        colliderScale.x),
                    Mathf.Abs(
                        arrowCollider.size.y *
                        colliderScale.y));

            // Convert the collider centre into arrow-root local space first,
            // then to world-sized root axes. It can then be rotated for every
            // predicted travel direction without depending on the held pose.
            Vector3 colliderCenterWorld =
                arrowCollider.transform.TransformPoint(
                    arrowCollider.offset);

            Vector3 colliderCenterRootLocal =
                predictionArrowRoot.InverseTransformPoint(
                    colliderCenterWorld);

            arrowColliderOffset =
                new Vector2(
                    colliderCenterRootLocal.x *
                    arrowRootWorldScale.x,
                    colliderCenterRootLocal.y *
                    arrowRootWorldScale.y);
        }
        else
        {
            Debug.LogWarning(
                "AimTrajectoryRenderer could not find BoxCollider2D on the Arrow. " +
                "Using fallback collider dimensions.");
        }

        CacheVisualArrowVertices();

        arrowCastSize.x =
            Mathf.Max(0.001f, arrowCastSize.x);

        arrowCastSize.y =
            Mathf.Max(0.001f, arrowCastSize.y);
    }

    private void CacheVisualArrowVertices()
    {
        arrowVisualVerticesRootLocal.Clear();

        if (predictionArrowRoot == null ||
            predictionArrowSprite == null ||
            predictionArrowSprite.sprite == null)
        {
            return;
        }

        Vector2[] vertices =
            predictionArrowSprite.sprite.vertices;

        if (vertices == null)
            return;

        bool flipX = predictionArrowSprite.flipX;
        bool flipY = predictionArrowSprite.flipY;
        Transform spriteTransform = predictionArrowSprite.transform;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 localVertex = vertices[i];

            if (flipX)
                localVertex.x = -localVertex.x;

            if (flipY)
                localVertex.y = -localVertex.y;

            Vector3 worldVertex =
                spriteTransform.TransformPoint(localVertex);

            Vector3 rootLocal =
                predictionArrowRoot.InverseTransformPoint(worldVertex);

            arrowVisualVerticesRootLocal.Add(
                new Vector2(rootLocal.x, rootLocal.y));
        }
    }

    private Vector2 GetVisualTipOffset(
        Vector2 travelDirection)
    {
        Vector2 direction =
            travelDirection.sqrMagnitude > DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        if (arrowVisualVerticesRootLocal.Count == 0)
        {
            // Fallback: place the visual tip at the front edge of the cached
            // arrowhead box. This path is only used if the sprite mesh cannot
            // be read.
            float fallbackDistance =
                arrowColliderOffset.x +
                arrowCastSize.x * 0.5f;

            return direction * fallbackDistance;
        }

        float angleDegrees =
            GetArrowRotationAngle(direction);

        float bestProjection =
            float.NegativeInfinity;

        Vector2 bestOffset = Vector2.zero;

        for (int i = 0;
             i < arrowVisualVerticesRootLocal.Count;
             i++)
        {
            Vector2 rootLocal =
                arrowVisualVerticesRootLocal[i];

            Vector2 scaledOffset =
                new Vector2(
                    rootLocal.x * arrowRootWorldScale.x,
                    rootLocal.y * arrowRootWorldScale.y);

            Vector2 worldOffset =
                RotateVector(
                    scaledOffset,
                    angleDegrees);

            float projection =
                Vector2.Dot(
                    worldOffset,
                    direction);

            if (projection <= bestProjection)
                continue;

            bestProjection = projection;
            bestOffset = worldOffset;
        }

        return bestOffset;
    }

    private float GetVisualArrowLength(
        Vector2 travelDirection)
    {
        if (arrowVisualVerticesRootLocal.Count < 2)
        {
            return Mathf.Max(
                arrowCastSize.x,
                arrowCastSize.y,
                0.75f);
        }

        Vector2 direction =
            travelDirection.sqrMagnitude > DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        float angleDegrees =
            GetArrowRotationAngle(direction);

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0;
             i < arrowVisualVerticesRootLocal.Count;
             i++)
        {
            Vector2 rootLocal =
                arrowVisualVerticesRootLocal[i];

            Vector2 scaledOffset =
                new Vector2(
                    rootLocal.x * arrowRootWorldScale.x,
                    rootLocal.y * arrowRootWorldScale.y);

            Vector2 worldOffset =
                RotateVector(
                    scaledOffset,
                    angleDegrees);

            minX = Mathf.Min(minX, worldOffset.x);
            maxX = Mathf.Max(maxX, worldOffset.x);
            minY = Mathf.Min(minY, worldOffset.y);
            maxY = Mathf.Max(maxY, worldOffset.y);
        }

        return Mathf.Max(
            maxX - minX,
            maxY - minY);
    }

    private bool TryFindMirrorSurfaceHit(
        Collider2D mirrorCollider,
        Vector2 arrowPivot,
        Vector2 incomingDirection,
        out Vector2 contactPoint,
        out Vector2 surfaceNormal)
    {
        contactPoint = default;
        surfaceNormal = default;

        if (mirrorCollider == null)
            return false;

        Vector2 direction =
            incomingDirection.normalized;

        Vector2 currentTip =
            arrowPivot +
            GetVisualTipOffset(direction);

        float visualLength =
            GetVisualArrowLength(direction);

        float arrowSpeed =
            config != null
                ? config.ArrowSpeed
                : 12f;

        float searchDistance =
            Mathf.Max(
                0.75f,
                visualLength * 1.5f +
                arrowSpeed * Time.fixedDeltaTime * 2f);

        Vector2 rayOrigin =
            currentTip -
            direction * searchDistance;

        float rayDistance =
            searchDistance * 2.5f;

        RaycastHit2D[] hits =
            Physics2D.RaycastAll(
                rayOrigin,
                direction,
                rayDistance);

        float bestDistance =
            float.PositiveInfinity;

        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit2D hit = hits[i];

            if (hit.collider != mirrorCollider)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            if (hit.normal.sqrMagnitude < DirectionEpsilon)
                continue;

            bestDistance = hit.distance;
            contactPoint = hit.point;
            surfaceNormal = hit.normal.normalized;
            found = true;
        }

        return found;
    }

    private Vector2 CalculateMirrorPlaneIntersection(
        Collider2D mirrorCollider,
        Vector2 arrowPivot,
        Vector2 incomingDirection,
        Vector2 surfaceNormal)
    {
        Vector2 direction =
            incomingDirection.normalized;

        Vector2 currentTip =
            arrowPivot +
            GetVisualTipOffset(direction);

        Vector2 planePoint =
            mirrorCollider != null
                ? (Vector2)mirrorCollider.bounds.center
                : arrowPivot;

        float denominator =
            Vector2.Dot(
                direction,
                surfaceNormal);

        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return mirrorCollider != null
                ? mirrorCollider.ClosestPoint(currentTip)
                : currentTip;
        }

        float distanceAlongRay =
            Vector2.Dot(
                planePoint - currentTip,
                surfaceNormal) /
            denominator;

        return
            currentTip +
            direction * distanceAlongRay;
    }

    private float GetArrowRotationAngle(
        Vector2 direction)
    {
        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x) *
            Mathf.Rad2Deg;

        if (config != null)
            angle += config.ArrowVisualAngleOffset;

        return angle;
    }

    private static Vector2 RotateVector(
        Vector2 value,
        float angleDegrees)
    {
        float radians =
            angleDegrees * Mathf.Deg2Rad;

        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return new Vector2(
            value.x * cos - value.y * sin,
            value.x * sin + value.y * cos);
    }

    private int PlaceDotsAlongPath(
        List<Vector2> points,
        int maxDots)
    {
        if (points == null ||
            points.Count < 2 ||
            maxDots <= 0)
        {
            return 0;
        }

        float spacing =
            Mathf.Max(0.05f, config.TrajectorySpacing);

        float distanceUntilNextDot =
            Mathf.Max(0f, config.TrajectoryStartOffset);

        int dotIndex = 0;
        float totalPlacedDistance = 0f;

        float totalPathLength = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            totalPathLength +=
                Vector2.Distance(
                    points[i],
                    points[i + 1]);
        }

        totalPathLength =
            Mathf.Max(
                0.001f,
                totalPathLength);

        for (int segmentIndex = 0;
             segmentIndex < points.Count - 1 &&
             dotIndex < maxDots;
             segmentIndex++)
        {
            Vector2 segmentStart =
                points[segmentIndex];

            Vector2 segmentEnd =
                points[segmentIndex + 1];

            Vector2 segment =
                segmentEnd - segmentStart;

            float segmentLength =
                segment.magnitude;

            if (segmentLength < DirectionEpsilon)
                continue;

            Vector2 segmentDirection =
                segment / segmentLength;

            float travelledOnSegment = 0f;

            while (dotIndex < maxDots)
            {
                float available =
                    segmentLength -
                    travelledOnSegment;

                if (distanceUntilNextDot > available)
                {
                    distanceUntilNextDot -= available;
                    totalPlacedDistance += available;
                    break;
                }

                travelledOnSegment +=
                    distanceUntilNextDot;

                totalPlacedDistance +=
                    distanceUntilNextDot;

                Vector2 position =
                    segmentStart +
                    segmentDirection *
                    travelledOnSegment;

                float normalized =
                    Mathf.Clamp01(
                        totalPlacedDistance /
                        totalPathLength);

                ApplyDotVisual(
                    dotIndex,
                    position,
                    normalized,
                    config.TrajectoryColor);

                dotIndex++;
                distanceUntilNextDot = spacing;

                if (travelledOnSegment >=
                    segmentLength - 0.0001f)
                {
                    break;
                }
            }
        }

        return dotIndex;
    }

    private void UpdateBounceMarkers(
        List<Vector2> bounces)
    {
        EnsureBounceMarkers();

        int count = Mathf.Min(
            bounces != null
                ? bounces.Count
                : 0,
            bounceMarkers.Count);

        for (int i = 0;
             i < bounceMarkers.Count;
             i++)
        {
            bool active = i < count;
            SpriteRenderer marker =
                bounceMarkers[i];

            marker.gameObject.SetActive(
                active && visible);

            if (!active)
                continue;

            marker.transform.position =
                new Vector3(
                    bounces[i].x,
                    bounces[i].y,
                    0f);

            marker.color =
                config.YellowColor;

            float scale =
                config.TrajectoryDotScale *
                1.55f;

            marker.transform.localScale =
                new Vector3(
                    scale,
                    scale,
                    1f);
        }
    }

    private void ApplyDotVisual(
        int index,
        Vector2 worldPosition,
        float normalized,
        Color baseColor)
    {
        SpriteRenderer dot =
            dots[index];

        SpriteRenderer shadow =
            dotShadows[index];

        Vector3 position =
            new Vector3(
                worldPosition.x,
                worldPosition.y,
                0f);

        dot.transform.position = position;
        shadow.transform.position = position;

        float alpha =
            Mathf.Lerp(
                config.TrajectoryStartAlpha,
                config.TrajectoryEndAlpha,
                normalized);

        Color color = baseColor;
        color.a *= alpha;
        dot.color = color;

        Color shadowColor =
            new Color(
                0.02f,
                0.05f,
                0.08f,
                Mathf.Clamp01(
                    alpha * 0.44f));

        shadow.color = shadowColor;

        float scale =
            config.TrajectoryDotScale *
            Mathf.Lerp(
                1f,
                0.80f,
                normalized);

        dot.transform.localScale =
            new Vector3(
                scale,
                scale,
                1f);

        float shadowScale =
            scale * 1.58f;

        shadow.transform.localScale =
            new Vector3(
                shadowScale,
                shadowScale,
                1f);
    }

    private void EnsureDots()
    {
        if (config == null)
            config = GameConfig.Load();

        if (dotSprite == null)
            dotSprite = CreateSoftDotSprite();

        int targetCount =
            Mathf.Max(
                1,
                Mathf.Max(
                    config.TrajectoryDotCount,
                    config.FullTrajectoryDotCount));

        while (dots.Count < targetCount)
        {
            int index = dots.Count;

            GameObject shadowObject =
                new GameObject(
                    $"DotShadow_{index:00}");

            shadowObject.transform.SetParent(
                transform,
                false);

            SpriteRenderer shadowRenderer =
                shadowObject.AddComponent<SpriteRenderer>();

            shadowRenderer.sprite = dotSprite;
            shadowRenderer.sortingOrder = 49;
            shadowRenderer.enabled = true;
            shadowRenderer.gameObject.SetActive(false);
            dotShadows.Add(shadowRenderer);

            GameObject dotObject =
                new GameObject(
                    $"Dot_{index:00}");

            dotObject.transform.SetParent(
                transform,
                false);

            SpriteRenderer renderer =
                dotObject.AddComponent<SpriteRenderer>();

            renderer.sprite = dotSprite;
            renderer.sortingOrder = 50;
            renderer.enabled = true;
            renderer.gameObject.SetActive(false);
            dots.Add(renderer);
        }
    }

    private void EnsureBounceMarkers()
    {
        if (config == null)
            config = GameConfig.Load();

        if (dotSprite == null)
            dotSprite = CreateSoftDotSprite();

        int targetCount =
            Mathf.Max(
                1,
                config.FullTrajectoryMaxBounces);

        while (bounceMarkers.Count < targetCount)
        {
            GameObject markerObject =
                new GameObject(
                    $"Bounce_{bounceMarkers.Count:00}");

            markerObject.transform.SetParent(
                transform,
                false);

            SpriteRenderer renderer =
                markerObject.AddComponent<SpriteRenderer>();

            renderer.sprite = dotSprite;
            renderer.sortingOrder = 51;
            renderer.enabled = true;
            renderer.gameObject.SetActive(false);
            bounceMarkers.Add(renderer);
        }
    }

    private void SetDotsActive(
        bool active,
        int activeCount)
    {
        int count =
            Mathf.Clamp(
                activeCount,
                0,
                dots.Count);

        for (int i = 0;
             i < dots.Count;
             i++)
        {
            bool shouldShow =
                active &&
                visible &&
                i < count;

            // During scene/level shutdown Unity can destroy child dot
            // GameObjects before LevelManager finishes calling BowController
            // cleanup. Unity's overloaded null check correctly treats those
            // destroyed SpriteRenderers as null, so never dereference them.
            SpriteRenderer dot = dots[i];
            if (dot != null &&
                dot.gameObject.activeSelf != shouldShow)
            {
                dot.gameObject.SetActive(shouldShow);
            }

            // Keep this independent from the main dot. Destruction order of
            // sibling GameObjects is not guaranteed when a scene closes.
            if (i >= dotShadows.Count)
                continue;

            SpriteRenderer shadow = dotShadows[i];
            if (shadow != null &&
                shadow.gameObject.activeSelf != shouldShow)
            {
                shadow.gameObject.SetActive(shouldShow);
            }
        }
    }

    private void SetBounceMarkersActive(
        bool active)
    {
        bool shouldShow = active && visible;

        for (int i = 0;
             i < bounceMarkers.Count;
             i++)
        {
            SpriteRenderer marker = bounceMarkers[i];
            if (marker == null)
                continue;

            if (marker.gameObject.activeSelf != shouldShow)
                marker.gameObject.SetActive(shouldShow);
        }
    }

    private static Sprite CreateSoftDotSprite()
    {
        const int size = 64;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "RuntimeTrajectoryDot";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        Vector2 center =
            new Vector2(
                (size - 1) * 0.5f,
                (size - 1) * 0.5f);

        float radius =
            size * 0.48f;

        for (int y = 0;
             y < size;
             y++)
        {
            for (int x = 0;
                 x < size;
                 x++)
            {
                float distance =
                    Vector2.Distance(
                        new Vector2(x, y),
                        center) /
                    radius;

                float alpha =
                    Mathf.Clamp01(
                        1f - distance);

                alpha =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        alpha);

                texture.SetPixel(
                    x,
                    y,
                    new Color(
                        1f,
                        1f,
                        1f,
                        alpha));
            }
        }

        texture.Apply(
            false,
            true);

        return Sprite.Create(
            texture,
            new Rect(
                0f,
                0f,
                size,
                size),
            new Vector2(
                0.5f,
                0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
    }
}
