using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class ArrowController : MonoBehaviour
{
    [Header("Fallback Defaults")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private float outOfBoundsMargin = 0.12f;
    [SerializeField] private float visualAngleOffset;

    private const float DirectionEpsilon = 0.0001f;
    private const float SameMirrorCooldownSeconds = 0.05f;

    private Rigidbody2D rb;
    private Camera mainCamera;
    private SpriteRenderer arrowSpriteRenderer;
    private Collider2D arrowCollider;
    private TrailRenderer trailRenderer;
    private TrailRenderer cinematicCoreTrail;
    private LineRenderer cinematicContrastLine;
    private LineRenderer cinematicOuterLine;
    private LineRenderer cinematicCoreLine;
    private SpriteRenderer cinematicArrowGlow;
    private static Material sharedTrailMaterial;
    private GameConfig config;
    private float baseTrailTime;
    private float baseTrailWidth;
    private float baseTrailStartAlpha;
    private int baseArrowSortingOrder;
    private bool baseArrowSortingOrderCaptured;
    private bool cinematicTrailActive;

    private bool fired;
    private bool stopped;

    private int lastMirrorInstanceId = int.MinValue;
    private float lastMirrorReflectionTime = -999f;

    // Reused reflection query buffer: avoids allocating RaycastHit2D[] on
    // every ricochet, which helps keep mobile frame times predictable.
    private readonly RaycastHit2D[] mirrorRaycastHits =
        new RaycastHit2D[16];

    // A ricochet changes both rotation and body position in one physics
    // callback so the visible tip stays at the incidence point. Rigidbody2D
    // interpolation must be suspended for that rendered frame; otherwise
    // Unity displays the new rotation with an interpolated old position,
    // producing the one-frame "detached" arrow visible in slow motion.
    private Coroutine restoreInterpolationRoutine;
    private RigidbodyInterpolation2D normalInterpolation =
        RigidbodyInterpolation2D.Interpolate;

    public event Action Shot;
    public event Action Reflected;
    public event Action SolidCollision;
    public event Action Missed;

    public bool HasFired => fired;
    public bool IsStopped => stopped;
    public float Speed => speed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        arrowCollider = GetComponent<Collider2D>();
        arrowSpriteRenderer =
            GetComponentInChildren<SpriteRenderer>(true);

        CaptureBaseArrowSortingOrder();

        mainCamera = Camera.main;
        config = GameConfig.Load();
        ApplyPhysicsDefaults();
        EnsureTrail(config);
    }

    public void Configure(GameConfig config)
    {
        if (config == null)
            return;

        this.config = config;

        if (!cinematicTrailActive)
            CaptureBaseArrowSortingOrder();

        speed = config.ArrowSpeed;
        outOfBoundsMargin = config.ArrowOutOfBoundsMargin;
        visualAngleOffset = config.ArrowVisualAngleOffset;

        ApplyPhysicsDefaults();
        EnsureTrail(config);
    }

    private void ApplyPhysicsDefaults()
    {
        if (rb == null)
            return;

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode =
            CollisionDetectionMode2D.Continuous;

        normalInterpolation =
            RigidbodyInterpolation2D.Interpolate;

        rb.interpolation =
            normalInterpolation;
    }

    public void SetDirection(Vector2 direction)
    {
        if (fired ||
            direction.sqrMagnitude < DirectionEpsilon)
        {
            return;
        }

        ApplyRotation(direction.normalized);
    }

    public void Fire(Vector2 direction)
    {
        if (fired ||
            stopped ||
            direction.sqrMagnitude < DirectionEpsilon)
        {
            return;
        }

        Vector2 normalizedDirection =
            direction.normalized;

        fired = true;
        stopped = false;

        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = true;
        }

        rb.bodyType = RigidbodyType2D.Dynamic;
        ApplyRotation(normalizedDirection);
        rb.linearVelocity =
            normalizedDirection * speed;

        Shot?.Invoke();
    }

    /// <summary>
    /// Legacy reflection API kept for compatibility.
    /// Prefer ReflectFromMirror / ReflectAtContact because they preserve the
    /// visible arrow tip at the true point of incidence.
    /// </summary>
    public void Reflect(Vector2 direction)
    {
        if (!CanReflect(direction))
            return;

        Vector2 reflectedDirection =
            direction.normalized;

        rb.bodyType = RigidbodyType2D.Dynamic;
        ApplyRotation(reflectedDirection);
        rb.linearVelocity =
            reflectedDirection * speed;
    }

    /// <summary>
    /// Trigger-mirror path.
    ///
    /// Finds the real surface point on the mirror collider with a short ray
    /// cast along the arrow's incoming trajectory. Reflection uses the
    /// mirror's authored plane normal so end-cap/corner contacts behave like
    /// the visible mirror face instead of an accidental box edge.
    ///
    /// If the ray cannot resolve the trigger surface, a deterministic mirror
    /// plane intersection is used as a fallback.
    /// </summary>
    public bool ReflectFromMirror(
        Collider2D mirrorCollider,
        Vector2 fallbackNormal)
    {
        if (!CanStartMirrorReflection(mirrorCollider))
            return false;

        Vector2 incomingVelocity =
            GetVelocity();

        if (incomingVelocity.sqrMagnitude <
            DirectionEpsilon)
        {
            return false;
        }

        Vector2 incomingDirection =
            incomingVelocity.normalized;

        Vector2 contactPoint;
        Vector2 ignoredPhysicsNormal;

        // A mirror is one reflective plane. BoxCollider2D end caps can return
        // a 90-degree corner normal, producing a visually strange ricochet at
        // the very end of the mirror. Always use the mirror's authored plane
        // normal; use physics only to recover the exact contact point.
        Vector2 surfaceNormal =
            fallbackNormal.sqrMagnitude > DirectionEpsilon
                ? fallbackNormal.normalized
                : (Vector2)mirrorCollider.transform.right;

        if (!TryFindMirrorSurfaceHit(
                mirrorCollider,
                incomingDirection,
                out contactPoint,
                out ignoredPhysicsNormal))
        {
            contactPoint =
                CalculateMirrorPlaneIntersection(
                    mirrorCollider,
                    incomingDirection,
                    surfaceNormal);
        }

        return ReflectAtContact(
            incomingDirection,
            contactPoint,
            surfaceNormal,
            mirrorCollider);
    }

    /// <summary>
    /// Exact collision-contact path.
    ///
    /// Rotates around the incidence point rather than around the arrow's
    /// transform pivot. After rotation, the visible arrow tip is translated
    /// back onto the same contact point, so the bounce has no visual teleport.
    /// </summary>
    public bool ReflectAtContact(
        Vector2 incomingDirection,
        Vector2 contactPoint,
        Vector2 surfaceNormal,
        Collider2D sourceMirror = null)
    {
        if (!CanReflect(incomingDirection) ||
            surfaceNormal.sqrMagnitude <
            DirectionEpsilon)
        {
            return false;
        }

        if (sourceMirror != null &&
            IsSameMirrorCoolingDown(sourceMirror))
        {
            return false;
        }

        incomingDirection =
            incomingDirection.normalized;

        surfaceNormal =
            surfaceNormal.normalized;

        Vector2 reflectedDirection =
            Vector2.Reflect(
                incomingDirection,
                surfaceNormal);

        if (reflectedDirection.sqrMagnitude <
            DirectionEpsilon)
        {
            return false;
        }

        reflectedDirection.Normalize();

        // Stop the old movement before changing pose.
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Dynamic;

        // IMPORTANT:
        // A correct tip-preserving ricochet necessarily changes BOTH the
        // arrow's rotation and its Rigidbody2D centre in the same physics
        // callback. With Interpolate enabled, Unity can render one frame using
        // the new rotation but an interpolated pre-correction position. That
        // is the visible gap/jump seen in slow-motion testing.
        //
        // Disable interpolation for this one rendered frame, apply the complete
        // corrected pose atomically, then restore interpolation after rendering.
        SuspendInterpolationForReflectionFrame();

        float reflectedAngle =
            GetRotationAngle(
                reflectedDirection);

        rb.rotation = reflectedAngle;

        // Keep Transform synchronized immediately because the rendered sprite
        // mesh vertices are queried again in this same callback.
        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                reflectedAngle);

        // Translate the BODY so the VISIBLE arrow tip remains at the exact
        // surface incidence point after its 90/angled rotation.
        Vector2 reflectedTip =
            GetVisualTipWorldPosition(
                reflectedDirection);

        // Keep the ACTUAL rendered arrowhead exactly at the physical
        // incidence point. No visual push-forward is applied here.
        //
        // Same-mirror cooldown already prevents duplicate ricochets, so a
        // separation offset is unnecessary and would make the bounce appear
        // to start away from the true contact point.
        Vector2 desiredTip =
            contactPoint;

        Vector2 correctedPosition =
            rb.position +
            (desiredTip - reflectedTip);

        rb.position =
            correctedPosition;

        // Interpolation is temporarily disabled, so force the render Transform
        // to the exact corrected physics pose now rather than one frame later.
        Vector3 currentTransformPosition =
            transform.position;

        transform.position =
            new Vector3(
                correctedPosition.x,
                correctedPosition.y,
                currentTransformPosition.z);

        // Continue with unchanged configured speed.
        rb.linearVelocity =
            reflectedDirection * speed;

        if (sourceMirror != null)
        {
            lastMirrorInstanceId =
                sourceMirror.GetInstanceID();

            lastMirrorReflectionTime =
                Time.time;
        }

        // Presentation systems (audio/VFX) can react without Mirror depending
        // directly on them. Fired once per successful ricochet.
        Reflected?.Invoke();

        return true;
    }

    private void Update()
    {
        if (!fired || stopped)
            return;

        CheckOutOfBounds();
    }

    private void LateUpdate()
    {
        UpdateSmoothCinematicComet();
    }

    private void OnCollisionEnter2D(
        Collision2D collision)
    {
        if (!fired || stopped)
            return;

        // Mirrors are handled by Mirror itself so this generic solid-hit path
        // never converts a valid ricochet into a stopped arrow.
        if (collision.collider != null &&
            collision.collider
                .GetComponentInParent<Mirror>() != null)
        {
            return;
        }

        StopAsSolidCollision();
    }

    private bool TryFindMirrorSurfaceHit(
        Collider2D mirrorCollider,
        Vector2 incomingDirection,
        out Vector2 contactPoint,
        out Vector2 surfaceNormal)
    {
        contactPoint = default;
        surfaceNormal = default;

        if (mirrorCollider == null)
            return false;

        Vector2 currentTip =
            GetVisualTipWorldPosition(
                incomingDirection);

        float visualLength =
            GetVisualArrowLength();

        float searchDistance =
            Mathf.Max(
                0.75f,
                visualLength * 1.5f +
                speed * Time.fixedDeltaTime * 2f);

        Vector2 rayOrigin =
            currentTip -
            incomingDirection * searchDistance;

        float rayDistance =
            searchDistance * 2.5f;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;

        int hitCount = Physics2D.Raycast(
            rayOrigin,
            incomingDirection,
            filter,
            mirrorRaycastHits,
            rayDistance);

        float bestDistance =
            float.PositiveInfinity;

        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = mirrorRaycastHits[i];

            if (hit.collider != mirrorCollider)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            if (hit.normal.sqrMagnitude <
                DirectionEpsilon)
            {
                continue;
            }

            bestDistance = hit.distance;
            contactPoint = hit.point;
            surfaceNormal = hit.normal.normalized;
            found = true;
        }

        return found;
    }

    private Vector2 CalculateMirrorPlaneIntersection(
        Collider2D mirrorCollider,
        Vector2 incomingDirection,
        Vector2 surfaceNormal)
    {
        Vector2 currentTip =
            GetVisualTipWorldPosition(
                incomingDirection);

        Vector2 planePoint =
            mirrorCollider != null
                ? (Vector2)mirrorCollider.bounds.center
                : (Vector2)transform.position;

        float denominator =
            Vector2.Dot(
                incomingDirection,
                surfaceNormal);

        if (Mathf.Abs(denominator) <
            0.0001f)
        {
            return mirrorCollider != null
                ? mirrorCollider.ClosestPoint(
                    currentTip)
                : currentTip;
        }

        float distanceAlongRay =
            Vector2.Dot(
                planePoint - currentTip,
                surfaceNormal) /
            denominator;

        return
            currentTip +
            incomingDirection * distanceAlongRay;
    }

    /// <summary>
    /// Returns the centre of the visible sprite's leading edge.
    /// It does not assume that the sprite's native forward axis is +X.
    /// </summary>
    /// <summary>
    /// Returns the ACTUAL rendered arrowhead point.
    ///
    /// IMPORTANT:
    /// Sprite.bounds describes the sprite rectangle, which can include
    /// transparent padding. The Arrow PNG contains transparent pixels beyond
    /// the visible arrowhead, so using bounds.max places the mathematical tip
    /// in empty space. That is why the v13/v14 reflected arrow looked a little
    /// behind the incidence point even though the physics contact was correct.
    ///
    /// Sprite.vertices follows the sprite's rendered mesh (Tight mesh in this
    /// project), so the vertex farthest along the travel direction is the real
    /// visible arrow tip.
    /// </summary>
    public Vector2 GetVisualTipWorldPosition(
        Vector2 travelDirection)
    {
        if (arrowSpriteRenderer == null ||
            arrowSpriteRenderer.sprite == null)
        {
            return GetColliderTipFallback(
                travelDirection);
        }

        Vector2 direction =
            travelDirection.sqrMagnitude >
            DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        Vector2[] vertices =
            arrowSpriteRenderer.sprite.vertices;

        if (vertices == null ||
            vertices.Length == 0)
        {
            return GetColliderTipFallback(
                direction);
        }

        bool flipX =
            arrowSpriteRenderer.flipX;

        bool flipY =
            arrowSpriteRenderer.flipY;

        float bestProjection =
            float.NegativeInfinity;

        Vector2 bestWorldPoint =
            transform.position;

        Transform spriteTransform =
            arrowSpriteRenderer.transform;

        for (int i = 0;
             i < vertices.Length;
             i++)
        {
            Vector3 localVertex =
                vertices[i];

            if (flipX)
                localVertex.x =
                    -localVertex.x;

            if (flipY)
                localVertex.y =
                    -localVertex.y;

            Vector3 worldVertex =
                spriteTransform.TransformPoint(
                    localVertex);

            float projection =
                Vector2.Dot(
                    (Vector2)worldVertex,
                    direction);

            if (projection <=
                bestProjection)
            {
                continue;
            }

            bestProjection =
                projection;

            bestWorldPoint =
                worldVertex;
        }

        return bestWorldPoint;
    }

    private Vector2 GetVisualTailWorldPosition(
        Vector2 travelDirection)
    {
        if (arrowSpriteRenderer == null ||
            arrowSpriteRenderer.sprite == null)
        {
            return GetColliderTailFallback(
                travelDirection);
        }

        Vector2 direction =
            travelDirection.sqrMagnitude >
            DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        Vector2[] vertices =
            arrowSpriteRenderer.sprite.vertices;

        if (vertices == null ||
            vertices.Length == 0)
        {
            return GetColliderTailFallback(
                direction);
        }

        bool flipX =
            arrowSpriteRenderer.flipX;

        bool flipY =
            arrowSpriteRenderer.flipY;

        float smallestProjection =
            float.PositiveInfinity;

        Vector2 bestWorldPoint =
            transform.position;

        Transform spriteTransform =
            arrowSpriteRenderer.transform;

        for (int i = 0;
             i < vertices.Length;
             i++)
        {
            Vector3 localVertex =
                vertices[i];

            if (flipX)
                localVertex.x =
                    -localVertex.x;

            if (flipY)
                localVertex.y =
                    -localVertex.y;

            Vector3 worldVertex =
                spriteTransform.TransformPoint(
                    localVertex);

            float projection =
                Vector2.Dot(
                    (Vector2)worldVertex,
                    direction);

            if (projection >=
                smallestProjection)
            {
                continue;
            }

            smallestProjection =
                projection;

            bestWorldPoint =
                worldVertex;
        }

        return bestWorldPoint;
    }

    private Vector2 GetColliderTailFallback(
        Vector2 travelDirection)
    {
        Vector2 direction =
            travelDirection.sqrMagnitude >
            DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        if (arrowCollider == null)
        {
            return
                (Vector2)transform.position -
                direction * 0.5f;
        }

        Bounds bounds =
            arrowCollider.bounds;

        float supportDistance =
            Mathf.Abs(direction.x) *
                bounds.extents.x +
            Mathf.Abs(direction.y) *
                bounds.extents.y;

        return
            (Vector2)bounds.center -
            direction *
            supportDistance;
    }

    private Vector2 GetColliderTipFallback(
        Vector2 travelDirection)
    {
        Vector2 direction =
            travelDirection.sqrMagnitude >
            DirectionEpsilon
                ? travelDirection.normalized
                : Vector2.right;

        if (arrowCollider == null)
        {
            return
                (Vector2)transform.position +
                direction * 0.5f;
        }

        Bounds bounds =
            arrowCollider.bounds;

        float supportDistance =
            Mathf.Abs(direction.x) *
                bounds.extents.x +
            Mathf.Abs(direction.y) *
                bounds.extents.y;

        return
            (Vector2)bounds.center +
            direction * supportDistance;
    }

    private float GetVisualArrowLength()
    {
        if (arrowSpriteRenderer == null ||
            arrowSpriteRenderer.sprite == null)
        {
            if (arrowCollider != null)
            {
                Bounds colliderBounds =
                    arrowCollider.bounds;

                return Mathf.Max(
                    colliderBounds.size.x,
                    colliderBounds.size.y);
            }

            return 1f;
        }

        Vector2[] vertices =
            arrowSpriteRenderer.sprite.vertices;

        if (vertices == null ||
            vertices.Length < 2)
        {
            return Mathf.Max(
                arrowSpriteRenderer.bounds.size.x,
                arrowSpriteRenderer.bounds.size.y);
        }

        bool flipX =
            arrowSpriteRenderer.flipX;

        bool flipY =
            arrowSpriteRenderer.flipY;

        Transform spriteTransform =
            arrowSpriteRenderer.transform;

        float minX =
            float.PositiveInfinity;

        float maxX =
            float.NegativeInfinity;

        float minY =
            float.PositiveInfinity;

        float maxY =
            float.NegativeInfinity;

        for (int i = 0;
             i < vertices.Length;
             i++)
        {
            Vector3 localVertex =
                vertices[i];

            if (flipX)
                localVertex.x =
                    -localVertex.x;

            if (flipY)
                localVertex.y =
                    -localVertex.y;

            Vector3 worldVertex =
                spriteTransform.TransformPoint(
                    localVertex);

            minX =
                Mathf.Min(
                    minX,
                    worldVertex.x);

            maxX =
                Mathf.Max(
                    maxX,
                    worldVertex.x);

            minY =
                Mathf.Min(
                    minY,
                    worldVertex.y);

            maxY =
                Mathf.Max(
                    maxY,
                    worldVertex.y);
        }

        return Mathf.Max(
            maxX - minX,
            maxY - minY);
    }

    private bool CanStartMirrorReflection(
        Collider2D mirrorCollider)
    {
        return
            mirrorCollider != null &&
            fired &&
            !stopped &&
            rb != null &&
            !IsSameMirrorCoolingDown(
                mirrorCollider);
    }

    private bool CanReflect(
        Vector2 direction)
    {
        return
            fired &&
            !stopped &&
            rb != null &&
            direction.sqrMagnitude >=
            DirectionEpsilon;
    }

    private bool IsSameMirrorCoolingDown(
        Collider2D mirrorCollider)
    {
        if (mirrorCollider == null)
            return false;

        return
            lastMirrorInstanceId ==
                mirrorCollider.GetInstanceID() &&
            Time.time -
                lastMirrorReflectionTime <
                SameMirrorCooldownSeconds;
    }

    private void SuspendInterpolationForReflectionFrame()
    {
        if (rb == null)
            return;

        if (restoreInterpolationRoutine != null)
        {
            StopCoroutine(
                restoreInterpolationRoutine);

            restoreInterpolationRoutine = null;
        }

        normalInterpolation =
            RigidbodyInterpolation2D.Interpolate;

        rb.interpolation =
            RigidbodyInterpolation2D.None;

        restoreInterpolationRoutine =
            StartCoroutine(
                RestoreInterpolationAfterRenderedFrame());
    }

    private IEnumerator RestoreInterpolationAfterRenderedFrame()
    {
        // Keep interpolation OFF through the current render. This makes the
        // first reflected frame use the exact corrected contact pose.
        yield return new WaitForEndOfFrame();

        if (rb != null &&
            fired &&
            !stopped)
        {
            rb.interpolation =
                normalInterpolation;
        }

        restoreInterpolationRoutine = null;
    }

    private void CheckOutOfBounds()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        Vector3 viewport =
            mainCamera.WorldToViewportPoint(
                transform.position);

        if (
            viewport.x < -outOfBoundsMargin ||
            viewport.x >
                1f + outOfBoundsMargin ||
            viewport.y < -outOfBoundsMargin ||
            viewport.y >
                1f + outOfBoundsMargin
        )
        {
            stopped = true;
            SetTrailEmitting(false);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType =
                RigidbodyType2D.Static;

            Missed?.Invoke();
        }
    }

    public Vector2 GetVelocity()
    {
        return
            rb != null
                ? rb.linearVelocity
                : Vector2.zero;
    }

    /// <summary>
    /// Stops the arrow with its ACTUAL rendered arrowhead exactly at the
    /// supplied world-space contact point. This is used by target scoring so
    /// the score position and the final visible arrow position are identical.
    /// </summary>
    public void StopAtVisualTipContact(
        Vector2 contactPoint,
        Vector2 incomingDirection)
    {
        if (rb == null || stopped)
            return;

        Vector2 direction =
            incomingDirection.sqrMagnitude >=
            DirectionEpsilon
                ? incomingDirection.normalized
                : GetVelocity().normalized;

        if (direction.sqrMagnitude < DirectionEpsilon)
        {
            Stop();
            return;
        }

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // Prevent Rigidbody2D interpolation from rendering the old position
        // for one frame after the exact contact correction.
        if (restoreInterpolationRoutine != null)
        {
            StopCoroutine(restoreInterpolationRoutine);
            restoreInterpolationRoutine = null;
        }

        rb.interpolation = RigidbodyInterpolation2D.None;

        Vector2 currentTip =
            GetVisualTipWorldPosition(direction);

        Vector2 correctedPosition =
            rb.position +
            (contactPoint - currentTip);

        rb.position = correctedPosition;

        Vector3 transformPosition =
            transform.position;

        transform.position = new Vector3(
            correctedPosition.x,
            correctedPosition.y,
            transformPosition.z);

        stopped = true;
        SetTrailEmitting(false);
        rb.bodyType = RigidbodyType2D.Static;
    }

    public void Stop()
    {
        if (rb == null || stopped)
            return;

        stopped = true;
        SetTrailEmitting(false);
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Static;
    }

    public void ResetArrow()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (arrowCollider == null)
            arrowCollider =
                GetComponent<Collider2D>();

        if (arrowSpriteRenderer == null)
        {
            arrowSpriteRenderer =
                GetComponentInChildren<SpriteRenderer>(
                    true);
        }

        fired = false;
        stopped = false;
        SetCinematicTrail(false);
        ApplyCinematicSorting(false);
        SetSmoothCinematicCometVisible(false);

        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
            trailRenderer.Clear();
        }

        if (cinematicCoreTrail != null)
        {
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.Clear();
        }

        lastMirrorInstanceId =
            int.MinValue;

        lastMirrorReflectionTime =
            -999f;

        rb.bodyType =
            RigidbodyType2D.Kinematic;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        rb.collisionDetectionMode =
            CollisionDetectionMode2D.Continuous;

        if (restoreInterpolationRoutine != null)
        {
            StopCoroutine(
                restoreInterpolationRoutine);

            restoreInterpolationRoutine = null;
        }

        normalInterpolation =
            RigidbodyInterpolation2D.Interpolate;

        rb.interpolation =
            normalInterpolation;

        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                visualAngleOffset);
    }

    private void StopAsSolidCollision()
    {
        stopped = true;
        SetTrailEmitting(false);
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Static;
        SolidCollision?.Invoke();
    }

    private void EnsureTrail(GameConfig gameConfig)
    {
        config = gameConfig != null
            ? gameConfig
            : config != null
                ? config
                : GameConfig.Load();

        if (trailRenderer == null)
            trailRenderer = GetComponent<TrailRenderer>();

        if (trailRenderer == null)
            trailRenderer = gameObject.AddComponent<TrailRenderer>();

        if (sharedTrailMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                sharedTrailMaterial = new Material(shader)
                {
                    name = "RuntimeArrowTrailMaterial",
                    hideFlags = HideFlags.DontSave
                };
            }
        }

        if (sharedTrailMaterial != null)
            trailRenderer.sharedMaterial = sharedTrailMaterial;

        if (cinematicCoreTrail == null)
        {
            Transform existingCore =
                transform.Find("CinematicCoreTrail");

            if (existingCore != null)
            {
                cinematicCoreTrail =
                    existingCore.GetComponent<TrailRenderer>();
            }

            if (cinematicCoreTrail == null)
            {
                GameObject coreObject =
                    new GameObject("CinematicCoreTrail");

                coreObject.transform.SetParent(
                    transform,
                    false);

                cinematicCoreTrail =
                    coreObject.AddComponent<TrailRenderer>();
            }
        }

        if (cinematicCoreTrail != null &&
            sharedTrailMaterial != null)
        {
            cinematicCoreTrail.sharedMaterial =
                sharedTrailMaterial;
        }

        EnsureCinematicArrowGlow();
        EnsureSmoothCinematicComet();

        baseTrailTime = config != null
            ? config.ArrowTrailTime
            : 0.11f;

        baseTrailWidth = config != null
            ? config.ArrowTrailWidth
            : 0.028f;

        baseTrailStartAlpha = config != null
            ? config.ArrowTrailStartAlpha
            : 0.55f;

        trailRenderer.minVertexDistance = 0.045f;
        trailRenderer.numCornerVertices = 2;
        trailRenderer.numCapVertices = 2;
        trailRenderer.textureMode = LineTextureMode.Stretch;
        trailRenderer.alignment = LineAlignment.View;
        trailRenderer.autodestruct = false;
        trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trailRenderer.receiveShadows = false;
        trailRenderer.emitting = false;

        if (arrowSpriteRenderer != null)
        {
            trailRenderer.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;
            trailRenderer.sortingOrder =
                arrowSpriteRenderer.sortingOrder - 3;
        }

        if (cinematicCoreTrail != null)
        {
            // Legacy cinematic TrailRenderer from the earlier Update A pass.
            // Keep it disabled: the final winning segment now uses two
            // continuous LineRenderers, eliminating visible frame segments.
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.Clear();
            cinematicCoreTrail.enabled = false;
        }

        cinematicTrailActive = false;
        ApplyTrailStyle(false);
        UpdateCinematicArrowGlow();
    }

    private void CaptureBaseArrowSortingOrder()
    {
        if (arrowSpriteRenderer == null)
            return;

        // Never capture the temporary cinematic +4 value as the new baseline.
        if (cinematicTrailActive &&
            baseArrowSortingOrderCaptured)
        {
            return;
        }

        baseArrowSortingOrder =
            arrowSpriteRenderer.sortingOrder;

        baseArrowSortingOrderCaptured =
            true;
    }

    private void ApplyCinematicSorting(bool enabled)
    {
        if (arrowSpriteRenderer == null)
            return;

        if (!baseArrowSortingOrderCaptured)
            CaptureBaseArrowSortingOrder();

        int baseOrder =
            baseArrowSortingOrder;

        if (cinematicContrastLine != null)
        {
            cinematicContrastLine.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;
            cinematicContrastLine.sortingOrder =
                baseOrder + 1;
        }

        if (cinematicOuterLine != null)
        {
            cinematicOuterLine.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;
            cinematicOuterLine.sortingOrder =
                baseOrder + 2;
        }

        if (cinematicCoreLine != null)
        {
            cinematicCoreLine.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;
            cinematicCoreLine.sortingOrder =
                baseOrder + 3;
        }

        if (cinematicArrowGlow != null)
        {
            cinematicArrowGlow.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;
            cinematicArrowGlow.sortingOrder =
                baseOrder + 3;
        }

        // Arrow itself remains on top of all winning-shot VFX layers.
        arrowSpriteRenderer.sortingOrder =
            enabled
                ? baseOrder + 4
                : baseOrder;
    }

    private void EnsureSmoothCinematicComet()
    {
        cinematicContrastLine =
            EnsureCinematicLine(
                cinematicContrastLine,
                "CinematicCometContrast",
                1);

        cinematicOuterLine =
            EnsureCinematicLine(
                cinematicOuterLine,
                "CinematicCometOuter",
                2);

        cinematicCoreLine =
            EnsureCinematicLine(
                cinematicCoreLine,
                "CinematicCometCore",
                3);

        ConfigureSmoothCinematicComet();
        SetSmoothCinematicCometVisible(false);
    }

    private LineRenderer EnsureCinematicLine(
        LineRenderer existing,
        string objectName,
        int sortingOffset)
    {
        if (existing == null)
        {
            Transform existingTransform =
                transform.Find(objectName);

            if (existingTransform != null)
            {
                existing =
                    existingTransform.GetComponent<LineRenderer>();
            }
        }

        if (existing == null)
        {
            GameObject lineObject =
                new GameObject(objectName);

            lineObject.transform.SetParent(
                transform,
                false);

            existing =
                lineObject.AddComponent<LineRenderer>();
        }

        if (sharedTrailMaterial != null)
        {
            existing.sharedMaterial =
                sharedTrailMaterial;
        }

        existing.useWorldSpace = true;
        existing.positionCount = 2;
        existing.alignment = LineAlignment.View;
        existing.textureMode = LineTextureMode.Stretch;
        existing.numCornerVertices = 0;
        existing.numCapVertices = 3;
        existing.loop = false;
        existing.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        existing.receiveShadows = false;
        existing.enabled = false;

        if (arrowSpriteRenderer != null)
        {
            CaptureBaseArrowSortingOrder();

            existing.sortingLayerID =
                arrowSpriteRenderer.sortingLayerID;

            existing.sortingOrder =
                baseArrowSortingOrder +
                sortingOffset;
        }

        return existing;
    }

    private void ConfigureSmoothCinematicComet()
    {
        if (cinematicContrastLine != null)
        {
            float contrastWidthMultiplier =
                config != null
                    ? config.FinalApproachContrastTrailWidthMultiplier
                    : 8.0f;

            float contrastAlpha =
                config != null
                    ? config.FinalApproachContrastTrailAlpha
                    : 0.30f;

            cinematicContrastLine.widthMultiplier =
                baseTrailWidth *
                contrastWidthMultiplier;

            cinematicContrastLine.widthCurve =
                new AnimationCurve(
                    new Keyframe(0f, 0.01f),
                    new Keyframe(0.26f, 0.30f),
                    new Keyframe(0.66f, 0.82f),
                    new Keyframe(1f, 1f));

            Gradient contrastGradient =
                new Gradient();

            contrastGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(
                        new Color(0.10f, 0.035f, 0.008f),
                        0f),
                    new GradientColorKey(
                        new Color(0.18f, 0.055f, 0.006f),
                        0.58f),
                    new GradientColorKey(
                        new Color(0.24f, 0.075f, 0.008f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(
                        contrastAlpha * 0.34f,
                        0.30f),
                    new GradientAlphaKey(
                        contrastAlpha * 0.80f,
                        0.72f),
                    new GradientAlphaKey(
                        contrastAlpha,
                        1f)
                });

            cinematicContrastLine.colorGradient =
                contrastGradient;
        }

        if (cinematicOuterLine != null)
        {
            float outerWidthMultiplier =
                config != null
                    ? config.FinalApproachTrailWidthMultiplier
                    : 2.65f;

            cinematicOuterLine.widthMultiplier =
                baseTrailWidth *
                outerWidthMultiplier;

            cinematicOuterLine.widthCurve =
                new AnimationCurve(
                    new Keyframe(0f, 0.01f),
                    new Keyframe(0.25f, 0.28f),
                    new Keyframe(0.65f, 0.76f),
                    new Keyframe(1f, 1f));

            float outerAlphaMultiplier =
                config != null
                    ? config.FinalApproachTrailAlphaMultiplier
                    : 1.35f;

            Gradient outerGradient =
                new Gradient();

            outerGradient.SetKeys(
                new[]
                {
                    // Position 0 is the distant tail; position 1 meets arrow tail.
                    new GradientColorKey(
                        new Color(1f, 0.30f, 0.005f),
                        0f),
                    new GradientColorKey(
                        new Color(1f, 0.64f, 0.015f),
                        0.50f),
                    new GradientColorKey(
                        new Color(1f, 0.96f, 0.20f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(
                        Mathf.Clamp01(0.24f * outerAlphaMultiplier),
                        0.26f),
                    new GradientAlphaKey(
                        Mathf.Clamp01(0.56f * outerAlphaMultiplier),
                        0.68f),
                    new GradientAlphaKey(
                        Mathf.Clamp01(0.74f * outerAlphaMultiplier),
                        1f)
                });

            cinematicOuterLine.colorGradient =
                outerGradient;
        }

        if (cinematicCoreLine != null)
        {
            float coreWidthMultiplier =
                config != null
                    ? config.FinalApproachCoreTrailWidthMultiplier
                    : 0.95f;

            cinematicCoreLine.widthMultiplier =
                baseTrailWidth *
                coreWidthMultiplier;

            cinematicCoreLine.widthCurve =
                new AnimationCurve(
                    new Keyframe(0f, 0.01f),
                    new Keyframe(0.32f, 0.18f),
                    new Keyframe(0.70f, 0.62f),
                    new Keyframe(1f, 1f));

            Gradient coreGradient =
                new Gradient();

            coreGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(
                        new Color(1f, 0.70f, 0.05f),
                        0f),
                    new GradientColorKey(
                        new Color(1f, 0.93f, 0.52f),
                        0.55f),
                    new GradientColorKey(
                        new Color(1f, 0.99f, 0.92f),
                        0.82f),
                    new GradientColorKey(
                        Color.white,
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.36f, 0.32f),
                    new GradientAlphaKey(0.88f, 0.72f),
                    new GradientAlphaKey(1f, 1f)
                });

            cinematicCoreLine.colorGradient =
                coreGradient;
        }
    }

    private void UpdateSmoothCinematicComet()
    {
        bool visible =
            cinematicTrailActive &&
            fired &&
            !stopped &&
            rb != null;

        if (!visible)
        {
            SetSmoothCinematicCometVisible(false);
            return;
        }

        Vector2 velocity =
            GetVelocity();

        if (velocity.sqrMagnitude <
            DirectionEpsilon)
        {
            SetSmoothCinematicCometVisible(false);
            return;
        }

        ConfigureSmoothCinematicComet();

        Vector2 direction =
            velocity.normalized;

        Vector2 arrowTail =
            GetVisualTailWorldPosition(
                direction);

        // The luminous winning streak must read as a separate effect BEHIND
        // the patterned arrow artwork. FINAL-2/3 ended the line at the visible
        // arrow TIP, so the effect visually overlapped the full shaft.
        //
        // End just inside the actual rendered tail/nock to avoid a seam, then
        // extend backward in one perfectly continuous world-space segment.
        float overlap =
            config != null
                ? config.FinalApproachWinningStreakOverlap
                : 0.035f;

        Vector2 streakJoin =
            arrowTail +
            direction *
            overlap;

        float outerLength =
            config != null
                ? config.FinalApproachWinningStreakLength
                : 0.95f;

        float coreRatio =
            config != null
                ? config.FinalApproachCoreTrailTimeRatio
                : 0.54f;

        float coreLength =
            outerLength *
            Mathf.Clamp(
                coreRatio,
                0.40f,
                0.80f);

        Vector2 outerTail =
            streakJoin -
            direction *
            outerLength;

        Vector2 coreTail =
            streakJoin -
            direction *
            coreLength;

        if (cinematicContrastLine != null)
        {
            cinematicContrastLine.enabled = true;
            cinematicContrastLine.SetPosition(
                0,
                new Vector3(
                    outerTail.x,
                    outerTail.y,
                    transform.position.z));

            cinematicContrastLine.SetPosition(
                1,
                new Vector3(
                    streakJoin.x,
                    streakJoin.y,
                    transform.position.z));
        }

        if (cinematicOuterLine != null)
        {
            cinematicOuterLine.enabled = true;
            cinematicOuterLine.SetPosition(
                0,
                new Vector3(
                    outerTail.x,
                    outerTail.y,
                    transform.position.z));

            cinematicOuterLine.SetPosition(
                1,
                new Vector3(
                    streakJoin.x,
                    streakJoin.y,
                    transform.position.z));
        }

        if (cinematicCoreLine != null)
        {
            cinematicCoreLine.enabled = true;
            cinematicCoreLine.SetPosition(
                0,
                new Vector3(
                    coreTail.x,
                    coreTail.y,
                    transform.position.z));

            cinematicCoreLine.SetPosition(
                1,
                new Vector3(
                    streakJoin.x,
                    streakJoin.y,
                    transform.position.z));
        }
    }

    private void SetSmoothCinematicCometVisible(
        bool visible)
    {
        if (cinematicContrastLine != null)
            cinematicContrastLine.enabled = visible;

        if (cinematicOuterLine != null)
            cinematicOuterLine.enabled = visible;

        if (cinematicCoreLine != null)
            cinematicCoreLine.enabled = visible;
    }

    private void EnsureCinematicArrowGlow()
    {
        if (arrowSpriteRenderer == null)
            return;

        if (cinematicArrowGlow == null)
        {
            Transform existing =
                arrowSpriteRenderer.transform.Find(
                    "CinematicArrowGlow");

            if (existing != null)
            {
                cinematicArrowGlow =
                    existing.GetComponent<SpriteRenderer>();
            }

            if (cinematicArrowGlow == null)
            {
                GameObject glowObject =
                    new GameObject("CinematicArrowGlow");

                glowObject.transform.SetParent(
                    arrowSpriteRenderer.transform,
                    false);

                cinematicArrowGlow =
                    glowObject.AddComponent<SpriteRenderer>();
            }
        }

        cinematicArrowGlow.sprite =
            arrowSpriteRenderer.sprite;

        cinematicArrowGlow.flipX =
            arrowSpriteRenderer.flipX;

        cinematicArrowGlow.flipY =
            arrowSpriteRenderer.flipY;

        cinematicArrowGlow.sortingLayerID =
            arrowSpriteRenderer.sortingLayerID;

        cinematicArrowGlow.sortingOrder =
            baseArrowSortingOrderCaptured
                ? baseArrowSortingOrder + 3
                : arrowSpriteRenderer.sortingOrder + 3;

        cinematicArrowGlow.transform.localPosition =
            Vector3.zero;

        cinematicArrowGlow.transform.localRotation =
            Quaternion.identity;

        float glowScale =
            config != null
                ? config.FinalApproachArrowGlowScale
                : 1.09f;

        cinematicArrowGlow.transform.localScale =
            Vector3.one * glowScale;

        cinematicArrowGlow.color =
            new Color(
                1f,
                0.83f,
                0.20f,
                cinematicTrailActive && config != null
                    ? config.FinalApproachArrowGlowAlpha
                    : 0f);
    }

    /// <summary>
    /// Presentation-only trail boost used after the LevelManager has confirmed
    /// that the arrow's nearest upcoming contact is a valid target scoring face.
    /// It never changes velocity, collision geometry or reflection behaviour.
    /// </summary>
    public void SetCinematicTrail(bool enabled)
    {
        if (cinematicTrailActive == enabled)
            return;

        cinematicTrailActive = enabled;
        ApplyCinematicSorting(enabled);
        ApplyTrailStyle(enabled);
        UpdateCinematicArrowGlow();

        if (cinematicCoreTrail != null)
        {
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.Clear();
            cinematicCoreTrail.enabled = false;
        }

        if (enabled)
        {
            // The normal TrailRenderer is frame-sampled and was still visible
            // underneath the smooth cinematic comet in FINAL-2. At mobile
            // resolution that overlap reads as repeating blocks/stripes.
            //
            // Once a winning final segment is confirmed, remove the sampled
            // trail completely and render ONLY the analytical gold + white
            // comet. This is presentation-only and never changes movement.
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
                trailRenderer.Clear();
            }

            UpdateSmoothCinematicComet();
            return;
        }

        SetSmoothCinematicCometVisible(false);

        // If a cinematic prediction is ever cancelled while the arrow is
        // still flying, restore the ordinary trail cleanly. In the normal
        // success path the arrow has already stopped, so this does nothing.
        if (trailRenderer != null &&
            fired &&
            !stopped)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = true;
        }
    }

    private void UpdateCinematicArrowGlow()
    {
        if (cinematicArrowGlow == null)
            return;

        if (arrowSpriteRenderer != null)
        {
            cinematicArrowGlow.sprite =
                arrowSpriteRenderer.sprite;
            cinematicArrowGlow.flipX =
                arrowSpriteRenderer.flipX;
            cinematicArrowGlow.flipY =
                arrowSpriteRenderer.flipY;
        }

        float glowScale =
            config != null
                ? config.FinalApproachArrowGlowScale
                : 1.09f;

        float glowAlpha =
            cinematicTrailActive && config != null
                ? config.FinalApproachArrowGlowAlpha
                : 0f;

        cinematicArrowGlow.transform.localScale =
            Vector3.one * glowScale;

        cinematicArrowGlow.color =
            new Color(
                1f,
                0.86f,
                0.24f,
                glowAlpha);
    }

    private void ApplyTrailStyle(bool cinematic)
    {
        if (trailRenderer == null)
            return;

        // Keep the normal sampled arrow trail stable. The special winning
        // appearance is drawn by the smooth two-line comet instead.
        trailRenderer.time = baseTrailTime;
        trailRenderer.widthMultiplier = baseTrailWidth;

        float startAlpha =
            Mathf.Clamp01(
                baseTrailStartAlpha);

        Color startColor =
            new Color(1f, 0.86f, 0.25f);

        Color endColor =
            new Color(1f, 0.97f, 0.78f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(startColor, 0f),
                new GradientColorKey(endColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(startAlpha, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        trailRenderer.colorGradient = gradient;
        trailRenderer.widthCurve =
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.18f));

        if (cinematicCoreTrail != null)
        {
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.Clear();
            cinematicCoreTrail.enabled = false;
        }

        ConfigureSmoothCinematicComet();

    }

    private void SetTrailEmitting(bool emitting)
    {
        if (trailRenderer != null)
        {
            // Never allow the ordinary sampled trail to reappear while the
            // smooth final-shot comet owns the presentation.
            trailRenderer.emitting =
                emitting &&
                !cinematicTrailActive;

            if (cinematicTrailActive)
                trailRenderer.Clear();
        }

        if (cinematicCoreTrail != null)
        {
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.enabled = false;
        }

        if (!emitting)
            SetSmoothCinematicCometVisible(false);
    }

    private void ApplyRotation(
        Vector2 direction)
    {
        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                GetRotationAngle(
                    direction));
    }

    private float GetRotationAngle(
        Vector2 direction)
    {
        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x) *
            Mathf.Rad2Deg;

        return
            angle +
            visualAngleOffset;
    }
    private void OnDisable()
    {
        SetTrailEmitting(false);
        ApplyCinematicSorting(false);

        if (trailRenderer != null)
            trailRenderer.Clear();

        if (cinematicCoreTrail != null)
        {
            cinematicCoreTrail.emitting = false;
            cinematicCoreTrail.Clear();
            cinematicCoreTrail.enabled = false;
        }

        SetSmoothCinematicCometVisible(false);

        if (cinematicArrowGlow != null)
        {
            Color glowColor =
                cinematicArrowGlow.color;
            glowColor.a = 0f;
            cinematicArrowGlow.color =
                glowColor;
        }
    }

}
