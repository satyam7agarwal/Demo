using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// World 3 waterwheel timing puzzle. Level-opt-in and all tuning comes from
/// Resources/Levels/RiverRun_NNN.json. No level-number branches in gameplay.
///
/// Actual BoxCollider2D paddles ride on the rotating kinematic Rigidbody2D.
/// The existing AimTrajectoryRenderer and fired ArrowController both see
/// those same colliders. We never draw a precomputed or example shot path.
///
/// To avoid a clear preview turning into an unfair collision after release,
/// the wheel holds its exact angle while the current arrow is airborne.
/// Rotation continues between shots.
/// </summary>
[DefaultExecutionOrder(1740)]
[DisallowMultipleComponent]
public sealed class WildheartRiverRunRuntime : MonoBehaviour
{
    private const string ConfigResourceFolder = "Levels/RiverRun_";

    private static readonly FieldInfo CurrentLevelField =
        typeof(LevelManager).GetField("currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CurrentArrowField =
        typeof(LevelManager).GetField("currentArrow", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ViewField =
        typeof(GameUIController).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PredictorField =
        typeof(BowController).GetField("trajectoryRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo AimDirectionField =
        typeof(BowController).GetField("currentAimDirection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo InvalidatePredictorMethod =
        typeof(AimTrajectoryRenderer).GetMethod("InvalidatePreviewCache", BindingFlags.Instance | BindingFlags.NonPublic);

    [Serializable]
    private sealed class WheelConfig
    {
        public string MechanismId = "wildheart_waterwheel";
        public string PaddleResourcePath;
        public string HubResourcePath;
        public string RimResourcePath;
        public string SupportResourcePath;
        public string FoamResourcePath;
        public string RushAudioResourcePath;
        public string SplashAudioResourcePath;
        public Vector2 WheelCenter = new Vector2(0.67f, -2.42f);
        public float AngularSpeedDegreesPerSecond = 17.5f;
        public float InitialAngleDegrees = 0f;
        public int PaddleCount = 3;
        public float PaddleDistanceFromHub = 0.9f;
        public float PaddleWorldWidth = 0.94f;
        public float PaddleWorldHeight = 1.7f;
        public float ShaftColliderWidth = 0.23f;
        public float ShaftColliderHeight = 1.05f;
        public float ShaftColliderYOffset = -0.17f;
        public float HeadColliderWidth = 0.85f;
        public float HeadColliderHeight = 0.5f;
        public float HeadColliderYOffset = 0.62f;
        public float HubWorldWidth = 0.75f;
        public float RimWorldWidth = 2.35f;
        public float SupportWorldWidth = 3f;
        public float SupportYOffset = -1.06f;
        public float FoamY = -3.68f;
        public float FoamStartX = -4.6f;
        public float FoamEndX = 4.8f;
        public float FoamTravelSeconds = 8.1f;
        public string TimingHint = "WAIT FOR THE GAP";
        public string ClearHint = "GAP OPEN!";
        public string AlignmentHint = "AIM AT TARGET";
    }

    private LevelManager levelManager;
    private Transform observedLevelObjects;
    private WheelConfig definition;
    private Transform wheelRoot;
    private Rigidbody2D wheelBody;
    private GameUIView gameView;
    private BowController bow;
    private AimTrajectoryRenderer predictor;
    private Target target;
    private SpriteRenderer[] foamRenderers;
    private float nextGapCheck;
    private bool wasClear;
    private bool hasClearMeasurement;
    private string previousHint;
    private bool previousHintCaptured;
    private AudioSource waterAudio;
    private AudioClip gapAudio;

    private readonly RaycastHit2D[] laneRaycastHits = new RaycastHit2D[64];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForActiveScene()
    {
        LevelManager manager = FindFirstObjectByType<LevelManager>();
        if (manager != null &&
            manager.GetComponent<WildheartRiverRunRuntime>() == null)
        {
            manager.gameObject.AddComponent<WildheartRiverRunRuntime>();
        }
    }

    private void Awake()
    {
        levelManager = GetComponent<LevelManager>();
    }

    private void LateUpdate()
    {
        Transform levelObjects = transform.Find("LevelObjects");
        if (levelObjects != observedLevelObjects)
        {
            ClearState();
            observedLevelObjects = levelObjects;
            if (levelObjects != null)
                SpawnForLevel(levelObjects);
        }

        if (wheelRoot == null || wheelBody == null || definition == null)
            return;

        // Important: the level's moving colliders must invalidate the normal
        // Full Path BoxCast cache even when the player holds aim perfectly still.
        if (!ArrowInFlight() && bow != null && bow.IsAiming &&
            bow.FullTrajectoryPreviewEnabled && predictor != null &&
            InvalidatePredictorMethod != null)
        {
            InvalidatePredictorMethod.Invoke(predictor, null);
        }

        AnimateFoam();

        // The hint reflects the REAL physical blocking paddles, not an angle-
        // based assumption. It never draws or reveals an example trajectory.
        if (Time.unscaledTime >= nextGapCheck && !ArrowInFlight())
        {
            nextGapCheck = Time.unscaledTime + 0.12f;
            // The general cue samples several reachable target points. When
            // the player is actually aiming, the cue instead evaluates the
            // SAME held arrowhead direction, footprint and wheel colliders.
            // This avoids announcing WAIT while a valid player shot is clear.
            bool clear = IsTargetLanePhysicallyClear();
            bool aimedAtTarget = true;
            if (bow != null && bow.IsAiming && AimDirectionField != null)
            {
                Vector2 aim = (Vector2)AimDirectionField.GetValue(bow);
                clear = IsPlayerShotClear(aim, out aimedAtTarget);
            }
            if (aimedAtTarget && (!hasClearMeasurement || wasClear != clear))
            {
                hasClearMeasurement = true;
                wasClear = clear;
                if (clear && waterAudio != null && gapAudio != null)
                    waterAudio.PlayOneShot(gapAudio, 0.18f);
            }
            UpdateHint(clear, aimedAtTarget);
        }
    }

    private void FixedUpdate()
    {
        if (wheelBody == null || definition == null || ArrowInFlight())
            return;

        wheelBody.MoveRotation(
            wheelBody.rotation +
            definition.AngularSpeedDegreesPerSecond * Time.fixedDeltaTime);
    }

    private void SpawnForLevel(Transform levelObjects)
    {
        LevelData level = levelManager != null && CurrentLevelField != null
            ? CurrentLevelField.GetValue(levelManager) as LevelData
            : null;
        if (level == null)
            return;

        TextAsset asset = Resources.Load<TextAsset>(
            ConfigResourceFolder + level.LevelNumber.ToString("000"));
        if (asset == null)
            return;

        WheelConfig cfg;
        try
        {
            cfg = JsonUtility.FromJson<WheelConfig>(asset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError("[River Run] Invalid waterwheel JSON: " + exception.Message);
            return;
        }

        if (cfg == null || cfg.PaddleCount < 2 || cfg.PaddleCount > 6 ||
            cfg.PaddleDistanceFromHub < 0.4f ||
            cfg.PaddleWorldHeight < 0.5f ||
            string.IsNullOrWhiteSpace(cfg.PaddleResourcePath))
        {
            Debug.LogError("[River Run] Missing or invalid waterwheel configuration in " + asset.name);
            return;
        }

        Sprite paddleSprite = Resources.Load<Sprite>(cfg.PaddleResourcePath);
        Sprite hubSprite = Resources.Load<Sprite>(cfg.HubResourcePath);
        Sprite rimSprite = Resources.Load<Sprite>(cfg.RimResourcePath);
        Sprite supportSprite = Resources.Load<Sprite>(cfg.SupportResourcePath);
        if (paddleSprite == null || hubSprite == null || rimSprite == null || supportSprite == null)
        {
            Debug.LogError("[River Run] Waterwheel authored Sprite missing; refusing invisible physics obstacle.");
            return;
        }

        definition = cfg;
        bow = FindFirstObjectByType<BowController>();
        predictor = bow != null && PredictorField != null
            ? PredictorField.GetValue(bow) as AimTrajectoryRenderer
            : null;
        target = levelObjects.GetComponentInChildren<Target>();
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        gameView = ui != null && ViewField != null
            ? ViewField.GetValue(ui) as GameUIView : null;
        if (gameView != null && gameView.AimHintText != null)
        {
            previousHint = gameView.AimHintText.text;
            previousHintCaptured = true;
        }

        GameObject root = new GameObject("RiverRun_Waterwheel_" + cfg.MechanismId);
        root.transform.SetParent(levelObjects, false);

        // Stable support is deliberately drawn behind the spinning wheel.
        GameObject support = new GameObject("WheelSupport");
        support.transform.SetParent(root.transform, false);
        support.transform.position = new Vector3(
            cfg.WheelCenter.x, cfg.WheelCenter.y + cfg.SupportYOffset, 0.03f);
        SpriteRenderer supportRenderer = support.AddComponent<SpriteRenderer>();
        supportRenderer.sprite = supportSprite;
        supportRenderer.sortingOrder = 9;
        float supportScale = cfg.SupportWorldWidth / Mathf.Max(0.01f, supportSprite.bounds.size.x);
        support.transform.localScale = new Vector3(supportScale, supportScale, 1f);

        GameObject wheel = new GameObject("RotatingWheel_ActualPaddleColliders");
        wheel.transform.SetParent(root.transform, false);
        wheel.transform.position = new Vector3(cfg.WheelCenter.x, cfg.WheelCenter.y, 0.02f);
        wheel.transform.rotation = Quaternion.Euler(0f, 0f, cfg.InitialAngleDegrees);
        wheelRoot = wheel.transform;

        wheelBody = wheel.AddComponent<Rigidbody2D>();
        wheelBody.bodyType = RigidbodyType2D.Kinematic;
        wheelBody.gravityScale = 0f;
        wheelBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        wheelBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        wheelBody.useFullKinematicContacts = true;

        // The rim stays BELOW the target shot lane; upper paddle tips are
        // the moving obstacles. Unlike a fake decoration, its footprint also
        // has matching physics for downward shots.
        GameObject rim = new GameObject("WheelWoodenRim");
        rim.transform.SetParent(wheelRoot, false);
        rim.transform.localPosition = Vector3.zero;
        SpriteRenderer rimRenderer = rim.AddComponent<SpriteRenderer>();
        rimRenderer.sprite = rimSprite;
        rimRenderer.sortingOrder = 13;
        float rimScale = cfg.RimWorldWidth / Mathf.Max(0.01f, rimSprite.bounds.size.x);
        rim.transform.localScale = new Vector3(rimScale, rimScale, 1f);
        // The authored rim sprite is a HOLLOW wooden ring. A solid CircleCollider
        // incorrectly fills the empty center; use a closed, thin perimeter.
        EdgeCollider2D rimCollider = wheel.AddComponent<EdgeCollider2D>();
        rimCollider.isTrigger = false;
        rimCollider.edgeRadius = Mathf.Max(0.025f, cfg.RimWorldWidth * 0.045f);
        const int rimSegments = 36;
        Vector2[] circumference = new Vector2[rimSegments + 1];
        float collisionRimRadius = cfg.RimWorldWidth * 0.452f;
        for (int i = 0; i <= rimSegments; i++)
        {
            float angle = (2f * Mathf.PI * i) / rimSegments;
            circumference[i] = new Vector2(
                Mathf.Cos(angle) * collisionRimRadius,
                Mathf.Sin(angle) * collisionRimRadius);
        }
        rimCollider.points = circumference;

        for (int i = 0; i < cfg.PaddleCount; i++)
        {
            GameObject blade = new GameObject("Paddle_" + (i + 1));
            blade.transform.SetParent(wheelRoot, false);
            blade.transform.localRotation = Quaternion.Euler(
                0f, 0f, i * 360f / cfg.PaddleCount);
            blade.transform.localPosition = blade.transform.localRotation *
                new Vector3(0f, cfg.PaddleDistanceFromHub, 0f);

            SpriteRenderer renderer = blade.AddComponent<SpriteRenderer>();
            renderer.sprite = paddleSprite;
            renderer.sortingOrder = 14;
            float sx = cfg.PaddleWorldWidth / Mathf.Max(0.001f, paddleSprite.bounds.size.x);
            float sy = cfg.PaddleWorldHeight / Mathf.Max(0.001f, paddleSprite.bounds.size.y);
            blade.transform.localScale = new Vector3(sx, sy, 1f);

            // Compound colliders approximate the actual wooden silhouette:
            // narrow spoke plus wider end paddle. They rotate WITH the sprite.
            BoxCollider2D spoke = blade.AddComponent<BoxCollider2D>();
            spoke.isTrigger = false;
            spoke.size = new Vector2(cfg.ShaftColliderWidth / sx,
                cfg.ShaftColliderHeight / sy);
            spoke.offset = new Vector2(0f, cfg.ShaftColliderYOffset / sy);

            BoxCollider2D cap = blade.AddComponent<BoxCollider2D>();
            cap.isTrigger = false;
            cap.size = new Vector2(cfg.HeadColliderWidth / sx,
                cfg.HeadColliderHeight / sy);
            cap.offset = new Vector2(0f, cfg.HeadColliderYOffset / sy);
        }

        GameObject hub = new GameObject("SpokedAxle");
        hub.transform.SetParent(wheelRoot, false);
        hub.transform.localPosition = Vector3.zero;
        SpriteRenderer hubRenderer = hub.AddComponent<SpriteRenderer>();
        hubRenderer.sprite = hubSprite;
        hubRenderer.sortingOrder = 16;
        float hubScale = cfg.HubWorldWidth / Mathf.Max(0.01f, hubSprite.bounds.size.x);
        hub.transform.localScale = new Vector3(hubScale, hubScale, 1f);
        // The central axle is below the shooting lane and cannot block a
        // valid straight shot through the upper open interval.
        CircleCollider2D hubCollider = wheel.AddComponent<CircleCollider2D>();
        hubCollider.isTrigger = false;
        hubCollider.radius = cfg.HubWorldWidth * 0.43f;

        CreateFoam(root.transform);
        waterAudio = root.AddComponent<AudioSource>();
        waterAudio.playOnAwake = false;
        waterAudio.loop = true;
        waterAudio.spatialBlend = 0f;
        waterAudio.volume = 0.20f;
        if (!string.IsNullOrWhiteSpace(cfg.RushAudioResourcePath))
        {
            waterAudio.clip = Resources.Load<AudioClip>(cfg.RushAudioResourcePath);
            if (waterAudio.clip != null)
                waterAudio.Play();
        }
        gapAudio = string.IsNullOrWhiteSpace(cfg.SplashAudioResourcePath)
            ? null
            : Resources.Load<AudioClip>(cfg.SplashAudioResourcePath);

        hasClearMeasurement = false;
        nextGapCheck = 0f;
        Physics2D.SyncTransforms();
        Debug.Log("[River Run] Waterwheel v3 installed with hollow rim, " +
                  cfg.PaddleCount + " physical paddles and aim-aware timing cues.");
    }

    private void CreateFoam(Transform parent)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.FoamResourcePath))
            return;
        Sprite sprite = Resources.Load<Sprite>(definition.FoamResourcePath);
        if (sprite == null)
            return;
        foamRenderers = new SpriteRenderer[5];
        for (int i = 0; i < foamRenderers.Length; i++)
        {
            GameObject obj = new GameObject("RiverCurrentFoam_" + i);
            obj.transform.SetParent(parent, false);
            SpriteRenderer r = obj.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = 8;
            float width = 0.85f + (i % 3) * 0.23f;
            float scale = width / Mathf.Max(0.001f, sprite.bounds.size.x);
            obj.transform.localScale = new Vector3(scale, scale, 1f);
            foamRenderers[i] = r;
        }
    }

    private void AnimateFoam()
    {
        if (foamRenderers == null || definition == null)
            return;
        float duration = Mathf.Max(3f, definition.FoamTravelSeconds);
        for (int i = 0; i < foamRenderers.Length; i++)
        {
            SpriteRenderer r = foamRenderers[i];
            if (r == null)
                continue;
            float time = Mathf.Repeat(Time.time / duration + i * 0.227f, 1f);
            float x = Mathf.Lerp(definition.FoamStartX, definition.FoamEndX, time);
            float y = definition.FoamY - (i % 3) * 0.16f;
            r.transform.position = new Vector3(x, y, -0.06f);
            r.color = new Color(1f, 1f, 1f, 0.27f +
                0.30f * Mathf.Sin(Mathf.PI * time));
        }
    }

    private bool IsPlayerShotClear(Vector2 aimDirection, out bool aimedAtTarget)
    {
        aimedAtTarget = false;
        ArrowController arrow = GetCurrentArrow();
        if (arrow == null || arrow.HasFired || arrow.IsStopped ||
            target == null || wheelRoot == null ||
            aimDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector2 direction = aimDirection.normalized;
        if (!target.CanScoreApproach(direction))
            return false;
        Transform faceTransform = target.transform.Find("CollisionRoot/ScoringFace");
        Collider2D face = faceTransform != null
            ? faceTransform.GetComponent<Collider2D>() : null;
        if (face == null)
            return false;

        // The bow's held arrow already has the final animated nock position.
        Vector2 tip = arrow.GetVisualTipWorldPosition(direction);
        float searchDistance = Mathf.Max(8f, Vector2.Distance(tip, face.bounds.center) + 2f);
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;

        Physics2D.SyncTransforms();
        float heading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        int hitCount = Physics2D.BoxCast(
            tip, new Vector2(0.195f, 0.084f), heading,
            direction, filter, laneRaycastHits, searchDistance);
        float targetDistance = float.PositiveInfinity;
        float wheelDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = laneRaycastHits[i];
            Collider2D collider = hit.collider;
            if (collider == null)
                continue;
            if (collider == face)
                targetDistance = Mathf.Min(targetDistance, hit.distance);
            if (collider.transform.IsChildOf(wheelRoot))
                wheelDistance = Mathf.Min(wheelDistance, hit.distance);
        }

        aimedAtTarget = !float.IsInfinity(targetDistance);
        if (!aimedAtTarget)
            return false;
        return wheelDistance >= targetDistance;
    }

    private bool IsTargetLanePhysicallyClear()
    {
        if (wheelRoot == null || target == null || bow == null)
            return false;
        ArrowController arrow = GetCurrentArrow();
        Vector2 launch = arrow != null && !arrow.HasFired
            ? (Vector2)arrow.transform.position
            : (Vector2)bow.ArrowSpawnPoint.position;
        Transform scoringRoot = target.transform.Find("CollisionRoot/ScoringFace");
        Collider2D scoringFace = scoringRoot != null
            ? scoringRoot.GetComponent<Collider2D>()
            : null;
        Bounds bounds = scoringFace != null ? scoringFace.bounds : new Bounds(target.transform.position, new Vector3(0.6f, 0.6f, 0f));

        Vector2[] goalPoints = new Vector2[]
        {
            (Vector2)bounds.center,
            new Vector2(bounds.min.x + bounds.size.x * 0.34f, bounds.center.y),
            new Vector2(bounds.min.x + bounds.size.x * 0.34f, bounds.center.y + bounds.size.y * 0.22f),
            new Vector2(bounds.min.x + bounds.size.x * 0.34f, bounds.center.y - bounds.size.y * 0.22f)
        };

        if (arrow != null && !arrow.HasFired)
        {
            Vector2 towardPreview = (goalPoints[0] - launch).normalized;
            launch = arrow.GetVisualTipWorldPosition(towardPreview);
        }

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;
        Physics2D.SyncTransforms();

        int clearCount = 0;
        for (int p = 0; p < goalPoints.Length; p++)
        {
            Vector2 goal = goalPoints[p];
            Vector2 toward = goal - launch;
            if (toward.sqrMagnitude < 0.25f)
                continue;
            Vector2 direction = toward.normalized;
            float distance = Vector2.Distance(launch, goal);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int count = Physics2D.BoxCast(launch, new Vector2(0.19f, 0.11f), angle,
                direction, filter, laneRaycastHits, distance);
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = laneRaycastHits[i].collider;
                if (collider != null && collider.transform.IsChildOf(wheelRoot))
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked)
                clearCount++;
        }
        // Require a meaningful opening, not just one razor-thin bypass line.
        return clearCount >= 2;
    }

    private void UpdateHint(bool clear, bool aimedAtTarget)
    {
        if (gameView == null || gameView.AimHintText == null || definition == null)
            return;
        gameView.AimHintText.text = !aimedAtTarget
            ? definition.AlignmentHint
            : clear ? definition.ClearHint : definition.TimingHint;
    }

    private ArrowController GetCurrentArrow()
    {
        return levelManager != null && CurrentArrowField != null
            ? CurrentArrowField.GetValue(levelManager) as ArrowController
            : null;
    }

    private bool ArrowInFlight()
    {
        ArrowController arrow = GetCurrentArrow();
        return arrow != null && arrow.HasFired && !arrow.IsStopped;
    }

    private void ClearState()
    {
        if (previousHintCaptured && gameView != null && gameView.AimHintText != null)
            gameView.AimHintText.text = previousHint;
        previousHint = null;
        previousHintCaptured = false;
        definition = null;
        wheelRoot = null;
        wheelBody = null;
        gameView = null;
        bow = null;
        predictor = null;
        target = null;
        foamRenderers = null;
        waterAudio = null;
        gapAudio = null;
        wasClear = false;
        hasClearMeasurement = false;
    }

    private void OnDisable()
    {
        ClearState();
        observedLevelObjects = null;
    }
}
