using System.Collections;
using UnityEngine;

/// <summary>
/// Camera, time-scale and lightweight world-space impact feedback.
/// Final-shot presentation is intentionally isolated from projectile physics:
/// it only changes Time.timeScale / camera presentation after LevelManager has
/// confirmed that the arrow's nearest upcoming contact is a valid scoring face.
/// </summary>
public sealed class GameFeelController : MonoBehaviour
{
    private const int ShockwaveSegments = 40;

    private GameConfig config;
    private Camera targetCamera;

    private Coroutine shakeRoutine;
    private Coroutine zoomRoutine;
    private Coroutine finalApproachRoutine;
    private Coroutine finalImpactRoutine;
    private Coroutine impactVfxRoutine;

    private Vector3 baseLocalPosition;
    private Vector3 shakeLocalOffset;
    private Vector3 cinematicLocalOffset;
    private float baseOrthographicSize;
    private float cinematicReturnOrthographicSize;

    private bool finalShotCinematicActive;
    private bool ownsTimeScale;

    // Deliberately simple runtime line VFX: a tiny white contact flash,
    // one soft expanding ring and a small fixed pool of directional sparks.
    // This avoids the static "star/spoke" look the v2 stretched particles had.
    private LineRenderer impactFlashRing;
    private LineRenderer shockwaveRing;
    private LineRenderer[] impactSparks;
    private Material impactLineMaterial;

    public bool IsFinalShotCinematicActive =>
        finalShotCinematicActive;

    public void Configure(GameConfig gameConfig)
    {
        config = gameConfig != null
            ? gameConfig
            : GameConfig.Load();

        targetCamera = GetComponent<Camera>();
        baseLocalPosition = transform.localPosition;
        shakeLocalOffset = Vector3.zero;
        cinematicLocalOffset = Vector3.zero;

        if (targetCamera != null && targetCamera.orthographic)
        {
            baseOrthographicSize = targetCamera.orthographicSize;
            cinematicReturnOrthographicSize = baseOrthographicSize;
        }

        EnsureImpactVfx();
    }

    private void Awake()
    {
        Configure(GameConfig.Load());
    }

    private void LateUpdate()
    {
        // Keep shake and cinematic target-focus additive instead of allowing
        // two coroutines to fight over Camera.transform.localPosition.
        transform.localPosition =
            baseLocalPosition +
            cinematicLocalOffset +
            shakeLocalOffset;
    }

    public void PlayHitFeedback(bool isBullseye = false)
    {
        float magnitude = isBullseye
            ? config.HitShakeMagnitude * 1.15f
            : config.HitShakeMagnitude;

        Shake(config.HitShakeDuration, magnitude);
        PunchZoom(
            isBullseye
                ? Mathf.Max(0.97f, config.HitCameraZoomFactor - 0.004f)
                : config.HitCameraZoomFactor,
            config.HitCameraZoomDuration);
    }

    /// <summary>
    /// Starts once LevelManager has verified the final scoring segment.
    /// predictedBullseye only affects presentation strength; Target remains the
    /// sole authority for the actual score when contact occurs.
    /// </summary>
    public void BeginFinalApproach(
        Vector2 predictedImpactPoint,
        bool trickShot,
        bool predictedBullseye)
    {
        if (config == null ||
            !config.FinalShotCinematicEnabled ||
            finalShotCinematicActive)
        {
            return;
        }

        finalShotCinematicActive = true;
        ownsTimeScale = true;

        if (targetCamera != null && targetCamera.orthographic)
        {
            cinematicReturnOrthographicSize =
                targetCamera.orthographicSize;
        }

        if (zoomRoutine != null)
        {
            StopCoroutine(zoomRoutine);
            zoomRoutine = null;
        }

        if (finalApproachRoutine != null)
            StopCoroutine(finalApproachRoutine);

        float targetTimeScale =
            GetApproachTimeScale(
                trickShot,
                predictedBullseye);

        float zoomFactor =
            config.FinalApproachZoomFactor -
            (trickShot ? 0.004f : 0f) -
            (predictedBullseye ? 0.004f : 0f);

        zoomFactor = Mathf.Clamp(
            zoomFactor,
            0.92f,
            1f);

        float focusStrength = trickShot
            ? config.TrickShotCameraFocusStrength
            : config.FinalApproachCameraFocusStrength;

        if (predictedBullseye)
            focusStrength += 0.035f;

        Vector3 targetFocusOffset =
            CalculateFocusLocalOffset(
                predictedImpactPoint,
                focusStrength);

        finalApproachRoutine = StartCoroutine(
            FinalApproachRoutine(
                targetTimeScale,
                zoomFactor,
                targetFocusOffset));
    }

    private float GetApproachTimeScale(
        bool trickShot,
        bool predictedBullseye)
    {
        if (trickShot && predictedBullseye)
            return config.BullseyeTrickShotApproachTimeScale;

        if (predictedBullseye)
            return config.BullseyeApproachTimeScale;

        if (trickShot)
            return config.TrickShotApproachTimeScale;

        return config.FinalApproachTimeScale;
    }

    private IEnumerator FinalApproachRoutine(
        float targetTimeScale,
        float zoomFactor,
        Vector3 targetFocusOffset)
    {
        float duration = Mathf.Max(
            0.01f,
            config.FinalApproachEaseInDuration);

        float startScale = Mathf.Max(
            0.01f,
            Time.timeScale);

        float startSize =
            targetCamera != null && targetCamera.orthographic
                ? targetCamera.orthographicSize
                : 0f;

        float targetSize = startSize > 0f
            ? cinematicReturnOrthographicSize * zoomFactor
            : 0f;

        Vector3 startFocusOffset =
            cinematicLocalOffset;

        float elapsed = 0f;
        while (elapsed < duration && finalShotCinematicActive)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);

            Time.timeScale = Mathf.Lerp(
                startScale,
                targetTimeScale,
                eased);

            float focusEase =
                t * t * (3f - 2f * t);

            cinematicLocalOffset = Vector3.Lerp(
                startFocusOffset,
                targetFocusOffset,
                focusEase);

            if (targetCamera != null &&
                targetCamera.orthographic &&
                startSize > 0f)
            {
                targetCamera.orthographicSize =
                    Mathf.Lerp(
                        startSize,
                        targetSize,
                        focusEase);
            }

            yield return null;
        }

        if (finalShotCinematicActive)
        {
            Time.timeScale = targetTimeScale;
            cinematicLocalOffset = targetFocusOffset;

            if (targetCamera != null &&
                targetCamera.orthographic &&
                targetSize > 0f)
            {
                targetCamera.orthographicSize =
                    targetSize;
            }
        }

        finalApproachRoutine = null;
    }

    /// <summary>
    /// Transitions the already-active approach directly into a crisp hit-stop,
    /// localized world-space burst, target-focused camera punch and recovery.
    /// </summary>
    public void PlayFinalImpact(
        bool isBullseye,
        Vector2 impactPoint,
        bool trickShot)
    {
        PlayImpactBurst(
            impactPoint,
            isBullseye);

        if (!finalShotCinematicActive)
        {
            PlayHitFeedback(isBullseye);
            return;
        }

        if (finalApproachRoutine != null)
        {
            StopCoroutine(finalApproachRoutine);
            finalApproachRoutine = null;
        }

        if (zoomRoutine != null)
        {
            StopCoroutine(zoomRoutine);
            zoomRoutine = null;
        }

        if (finalImpactRoutine != null)
            StopCoroutine(finalImpactRoutine);

        finalImpactRoutine = StartCoroutine(
            FinalImpactRoutine(
                isBullseye,
                impactPoint,
                trickShot));
    }

    private IEnumerator FinalImpactRoutine(
        bool isBullseye,
        Vector2 impactPoint,
        bool trickShot)
    {
        float freezeDuration = isBullseye
            ? config.BullseyeImpactFreezeDuration
            : config.FinalImpactFreezeDuration;

        if (trickShot)
            freezeDuration += isBullseye ? 0.010f : 0.005f;

        float holdDuration = isBullseye
            ? config.BullseyeImpactHoldDuration
            : config.FinalImpactHoldDuration;

        float startSize =
            targetCamera != null && targetCamera.orthographic
                ? targetCamera.orthographicSize
                : 0f;

        float impactZoomFactor = isBullseye
            ? config.BullseyeImpactZoomFactor
            : config.FinalImpactZoomFactor;

        if (trickShot)
            impactZoomFactor -= 0.003f;

        impactZoomFactor = Mathf.Clamp(
            impactZoomFactor,
            0.90f,
            1f);

        float punchedSize =
            cinematicReturnOrthographicSize > 0f
                ? cinematicReturnOrthographicSize * impactZoomFactor
                : startSize;

        float focusStrength =
            config.FinalImpactCameraFocusStrength +
            (trickShot ? 0.035f : 0f) +
            (isBullseye ? 0.040f : 0f);

        Vector3 impactFocusOffset =
            CalculateFocusLocalOffset(
                impactPoint,
                focusStrength);

        Vector3 startFocusOffset =
            cinematicLocalOffset;

        ownsTimeScale = true;
        Time.timeScale = 0f;

        float elapsed = 0f;
        while (elapsed < freezeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = freezeDuration > 0f
                ? Mathf.Clamp01(elapsed / freezeDuration)
                : 1f;
            float eased = EaseOutCubic(t);

            cinematicLocalOffset = Vector3.Lerp(
                startFocusOffset,
                impactFocusOffset,
                eased);

            if (targetCamera != null &&
                targetCamera.orthographic &&
                startSize > 0f)
            {
                targetCamera.orthographicSize =
                    Mathf.Lerp(
                        startSize,
                        punchedSize,
                        eased);
            }

            yield return null;
        }

        Time.timeScale = 1f;
        ownsTimeScale = false;
        cinematicLocalOffset = impactFocusOffset;

        // The camera stays clean during the approach and frozen contact frame.
        // A short, damped punch starts only AFTER the hit-stop.
        float magnitudeMultiplier = isBullseye
            ? 1.30f
            : trickShot
                ? 1.20f
                : 1.12f;

        Shake(
            config.HitShakeDuration *
                (isBullseye ? 0.92f : 0.82f),
            config.HitShakeMagnitude *
                magnitudeMultiplier);

        if (targetCamera != null &&
            targetCamera.orthographic &&
            punchedSize > 0f)
        {
            targetCamera.orthographicSize =
                punchedSize;
        }

        if (holdDuration > 0f)
            yield return new WaitForSecondsRealtime(holdDuration);

        float returnDuration = Mathf.Max(
            0.05f,
            config.FinalImpactCameraReturnDuration);

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(
                elapsed / returnDuration);
            float returnEase =
                t * t * (3f - 2f * t);

            cinematicLocalOffset = Vector3.Lerp(
                impactFocusOffset,
                Vector3.zero,
                returnEase);

            if (targetCamera != null &&
                targetCamera.orthographic &&
                punchedSize > 0f)
            {
                targetCamera.orthographicSize =
                    Mathf.Lerp(
                        punchedSize,
                        cinematicReturnOrthographicSize,
                        returnEase);
            }

            yield return null;
        }

        cinematicLocalOffset = Vector3.zero;

        if (targetCamera != null &&
            targetCamera.orthographic &&
            cinematicReturnOrthographicSize > 0f)
        {
            targetCamera.orthographicSize =
                cinematicReturnOrthographicSize;
        }

        finalShotCinematicActive = false;
        finalImpactRoutine = null;
    }

    public void CancelFinalShotCinematic()
    {
        if (finalApproachRoutine != null)
        {
            StopCoroutine(finalApproachRoutine);
            finalApproachRoutine = null;
        }

        if (finalImpactRoutine != null)
        {
            StopCoroutine(finalImpactRoutine);
            finalImpactRoutine = null;
        }

        if (finalShotCinematicActive || ownsTimeScale)
            Time.timeScale = 1f;

        cinematicLocalOffset = Vector3.zero;

        if (targetCamera != null && targetCamera.orthographic)
        {
            float restoreSize =
                cinematicReturnOrthographicSize > 0f
                    ? cinematicReturnOrthographicSize
                    : baseOrthographicSize;

            if (restoreSize > 0f)
                targetCamera.orthographicSize = restoreSize;
        }

        finalShotCinematicActive = false;
        ownsTimeScale = false;
    }

    public void PlayRicochetFeedback()
    {
        Shake(
            config.RicochetShakeDuration,
            config.RicochetShakeMagnitude);
    }

    public void PlayMissFeedback()
    {
        Shake(
            config.MissShakeDuration,
            config.MissShakeMagnitude);
    }

    private Vector3 CalculateFocusLocalOffset(
        Vector2 focusWorldPoint,
        float strength)
    {
        if (strength <= 0f)
            return Vector3.zero;

        Vector3 baseWorldPosition = transform.parent != null
            ? transform.parent.TransformPoint(baseLocalPosition)
            : baseLocalPosition;

        Vector2 worldDelta =
            focusWorldPoint -
            new Vector2(
                baseWorldPosition.x,
                baseWorldPosition.y);

        Vector2 focusedDelta =
            Vector2.ClampMagnitude(
                worldDelta * strength,
                config.FinalApproachMaxCameraShift);

        Vector3 worldOffset = new Vector3(
            focusedDelta.x,
            focusedDelta.y,
            0f);

        return transform.parent != null
            ? transform.parent.InverseTransformVector(worldOffset)
            : worldOffset;
    }

    private void Shake(float duration, float magnitude)
    {
        if (duration <= 0f || magnitude <= 0f)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        shakeRoutine = StartCoroutine(
            ShakeRoutine(duration, magnitude));
    }

    private IEnumerator ShakeRoutine(
        float duration,
        float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(
                elapsed / Mathf.Max(0.001f, duration));

            float damping = 1f - normalized;
            damping *= damping;

            Vector2 offset =
                Random.insideUnitCircle *
                magnitude *
                damping;

            shakeLocalOffset =
                new Vector3(offset.x, offset.y, 0f);

            yield return null;
        }

        shakeLocalOffset = Vector3.zero;
        shakeRoutine = null;
    }

    private void PunchZoom(
        float zoomFactor,
        float duration)
    {
        if (targetCamera == null ||
            !targetCamera.orthographic ||
            duration <= 0f)
        {
            return;
        }

        if (zoomRoutine != null)
            StopCoroutine(zoomRoutine);

        baseOrthographicSize =
            targetCamera.orthographicSize;

        zoomRoutine = StartCoroutine(
            ZoomPunchRoutine(
                Mathf.Clamp(zoomFactor, 0.95f, 1f),
                duration));
    }

    private IEnumerator ZoomPunchRoutine(
        float zoomFactor,
        float duration)
    {
        float punchDuration = duration * 0.42f;
        float returnDuration = Mathf.Max(
            0.001f,
            duration - punchDuration);
        float punchedSize =
            baseOrthographicSize * zoomFactor;

        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(
                elapsed /
                Mathf.Max(0.001f, punchDuration));
            targetCamera.orthographicSize =
                Mathf.Lerp(
                    baseOrthographicSize,
                    punchedSize,
                    EaseOutCubic(t));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(
                elapsed / returnDuration);
            targetCamera.orthographicSize =
                Mathf.Lerp(
                    punchedSize,
                    baseOrthographicSize,
                    EaseOutCubic(t));
            yield return null;
        }

        targetCamera.orthographicSize =
            baseOrthographicSize;
        zoomRoutine = null;
    }

    private void EnsureImpactVfx()
    {
        if (impactLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                impactLineMaterial =
                    new Material(shader)
                    {
                        name = "RuntimeFinalShotImpactLineMaterial",
                        hideFlags = HideFlags.DontSave
                    };
            }
        }

        if (impactFlashRing == null)
        {
            impactFlashRing =
                CreateCircleRenderer(
                    "FinalShotContactFlash",
                    160);
        }

        if (shockwaveRing == null)
        {
            shockwaveRing =
                CreateCircleRenderer(
                    "FinalShotImpactShockwave",
                    155);
        }

        int requiredSparkCount = 16;
        if (impactSparks == null ||
            impactSparks.Length != requiredSparkCount)
        {
            impactSparks =
                new LineRenderer[requiredSparkCount];

            for (int i = 0; i < requiredSparkCount; i++)
            {
                GameObject sparkObject =
                    new GameObject(
                        $"FinalShotSpark_{i:00}");

                sparkObject.transform.SetParent(
                    transform,
                    false);

                LineRenderer spark =
                    sparkObject.AddComponent<LineRenderer>();

                spark.useWorldSpace = true;
                spark.positionCount = 2;
                spark.numCornerVertices = 2;
                spark.numCapVertices = 3;
                spark.textureMode = LineTextureMode.Stretch;
                spark.sortingOrder = 165;
                spark.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                spark.receiveShadows = false;
                spark.enabled = false;

                if (impactLineMaterial != null)
                {
                    spark.sharedMaterial =
                        impactLineMaterial;
                }

                impactSparks[i] = spark;
            }
        }
    }

    private LineRenderer CreateCircleRenderer(
        string objectName,
        int sortingOrder)
    {
        GameObject ringObject =
            new GameObject(objectName);

        ringObject.transform.SetParent(
            transform,
            false);

        LineRenderer ring =
            ringObject.AddComponent<LineRenderer>();

        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = ShockwaveSegments;
        ring.numCornerVertices = 3;
        ring.numCapVertices = 3;
        ring.sortingOrder = sortingOrder;
        ring.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.enabled = false;

        if (impactLineMaterial != null)
            ring.sharedMaterial = impactLineMaterial;

        return ring;
    }

    private void PlayImpactBurst(
        Vector2 worldPoint,
        bool isBullseye)
    {
        EnsureImpactVfx();

        if (impactVfxRoutine != null)
            StopCoroutine(impactVfxRoutine);

        impactVfxRoutine = StartCoroutine(
            ImpactVfxRoutine(
                worldPoint,
                isBullseye));
    }

    private IEnumerator ImpactVfxRoutine(
        Vector2 worldPoint,
        bool isBullseye)
    {
        int requestedCount = isBullseye
            ? config.BullseyeImpactParticleCount
            : config.FinalImpactParticleCount;

        int sparkCount = impactSparks != null
            ? Mathf.Min(
                requestedCount,
                impactSparks.Length)
            : 0;

        Vector2[] sparkDirections =
            new Vector2[sparkCount];

        float[] sparkSpeed =
            new float[sparkCount];

        float[] sparkLength =
            new float[sparkCount];

        for (int i = 0; i < sparkCount; i++)
        {
            // Even angular distribution plus a small random offset feels
            // intentional while avoiding a perfectly geometric star.
            float baseAngle =
                (Mathf.PI * 2f * i) /
                Mathf.Max(1, sparkCount);

            float angle =
                baseAngle +
                Random.Range(-0.20f, 0.20f);

            sparkDirections[i] =
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));

            sparkSpeed[i] =
                config.FinalImpactParticleSpeed *
                Random.Range(0.72f, 1.18f) *
                (isBullseye ? 1.10f : 1f);

            sparkLength[i] =
                config.FinalImpactSparkLength *
                Random.Range(0.72f, 1.18f);

            LineRenderer spark = impactSparks[i];
            if (spark != null)
                spark.enabled = true;
        }

        if (impactFlashRing != null)
            impactFlashRing.enabled = true;

        if (shockwaveRing != null)
            shockwaveRing.enabled = true;

        float flashDuration = Mathf.Max(
            0.02f,
            config.FinalImpactFlashDuration);

        float shockDuration =
            isBullseye ? 0.26f : 0.22f;

        float sparkDuration =
            isBullseye ? 0.26f : 0.21f;

        float totalDuration =
            Mathf.Max(
                flashDuration,
                Mathf.Max(
                    shockDuration,
                    sparkDuration));

        float shockRadius = isBullseye
            ? config.BullseyeImpactShockwaveRadius
            : config.FinalImpactShockwaveRadius;

        Color gold = config.YellowColor;

        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            // 1) Very short bright contact flash. It expands only a little,
            // then disappears, so it reads as contact rather than a starburst.
            if (impactFlashRing != null)
            {
                float flashT =
                    Mathf.Clamp01(
                        elapsed / flashDuration);

                if (flashT < 1f)
                {
                    float flashRadius =
                        Mathf.Lerp(
                            0.025f,
                            isBullseye ? 0.20f : 0.15f,
                            EaseOutCubic(flashT));

                    float flashAlpha =
                        (1f - flashT) *
                        (isBullseye ? 0.98f : 0.86f);

                    float flashWidth =
                        Mathf.Lerp(
                            isBullseye ? 0.080f : 0.065f,
                            0.010f,
                            flashT);

                    SetRing(
                        impactFlashRing,
                        worldPoint,
                        flashRadius,
                        Color.white,
                        flashAlpha,
                        flashWidth);
                }
                else
                {
                    impactFlashRing.enabled = false;
                }
            }

            // 2) Softer gold shockwave, smaller and cleaner than v2.
            if (shockwaveRing != null)
            {
                float shockT =
                    Mathf.Clamp01(
                        elapsed / shockDuration);

                if (shockT < 1f)
                {
                    float radius =
                        Mathf.Lerp(
                            0.07f,
                            shockRadius,
                            EaseOutCubic(shockT));

                    float alpha =
                        (1f - shockT) *
                        (isBullseye ? 0.72f : 0.52f);

                    float width =
                        Mathf.Lerp(
                            isBullseye ? 0.034f : 0.027f,
                            0.005f,
                            shockT);

                    SetRing(
                        shockwaveRing,
                        worldPoint,
                        radius,
                        gold,
                        alpha,
                        width);
                }
                else
                {
                    shockwaveRing.enabled = false;
                }
            }

            // 3) Short directional sparks that visibly travel away from the
            // arrow tip. These replace v2's long stretched radial spokes.
            float sparkT =
                Mathf.Clamp01(
                    elapsed / sparkDuration);

            for (int i = 0; i < sparkCount; i++)
            {
                LineRenderer spark =
                    impactSparks[i];

                if (spark == null)
                    continue;

                if (sparkT >= 1f)
                {
                    spark.enabled = false;
                    continue;
                }

                float eased =
                    EaseOutCubic(sparkT);

                Vector2 direction =
                    sparkDirections[i];

                float distance =
                    sparkSpeed[i] *
                    elapsed *
                    0.34f;

                Vector2 head =
                    worldPoint +
                    direction * distance;

                float currentLength =
                    sparkLength[i] *
                    Mathf.Lerp(
                        0.35f,
                        1f,
                        Mathf.Clamp01(
                            sparkT * 3f));

                Vector2 tail =
                    head -
                    direction * currentLength;

                Color sparkColor =
                    Color.Lerp(
                        Color.white,
                        gold,
                        0.58f);

                float alpha =
                    (1f - sparkT) *
                    (isBullseye ? 0.95f : 0.78f);

                sparkColor.a = alpha;

                float width =
                    Mathf.Lerp(
                        isBullseye ? 0.030f : 0.024f,
                        0.004f,
                        sparkT);

                spark.startColor = sparkColor;
                spark.endColor = new Color(
                    sparkColor.r,
                    sparkColor.g,
                    sparkColor.b,
                    alpha * 0.16f);
                spark.startWidth = width;
                spark.endWidth = width * 0.45f;
                spark.SetPosition(
                    0,
                    new Vector3(
                        head.x,
                        head.y,
                        0f));
                spark.SetPosition(
                    1,
                    new Vector3(
                        tail.x,
                        tail.y,
                        0f));
            }

            yield return null;
        }

        DisableImpactVfx();
        impactVfxRoutine = null;
    }

    private void SetRing(
        LineRenderer ring,
        Vector2 center,
        float radius,
        Color color,
        float alpha,
        float width)
    {
        if (ring == null)
            return;

        color.a = Mathf.Clamp01(alpha);

        ring.startColor = color;
        ring.endColor = color;
        ring.startWidth = width;
        ring.endWidth = width;

        for (int i = 0; i < ShockwaveSegments; i++)
        {
            float angle =
                (Mathf.PI * 2f * i) /
                ShockwaveSegments;

            ring.SetPosition(
                i,
                new Vector3(
                    center.x +
                        Mathf.Cos(angle) * radius,
                    center.y +
                        Mathf.Sin(angle) * radius,
                    0f));
        }
    }

    private void DisableImpactVfx()
    {
        if (impactFlashRing != null)
            impactFlashRing.enabled = false;

        if (shockwaveRing != null)
            shockwaveRing.enabled = false;

        if (impactSparks == null)
            return;

        for (int i = 0; i < impactSparks.Length; i++)
        {
            if (impactSparks[i] != null)
                impactSparks[i].enabled = false;
        }
    }

    private static float EaseOutCubic(float t)
    {
        float x = 1f - Mathf.Clamp01(t);
        return 1f - x * x * x;
    }

    private void OnDisable()
    {
        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        if (zoomRoutine != null)
            StopCoroutine(zoomRoutine);

        if (finalApproachRoutine != null)
            StopCoroutine(finalApproachRoutine);

        if (finalImpactRoutine != null)
            StopCoroutine(finalImpactRoutine);

        if (impactVfxRoutine != null)
            StopCoroutine(impactVfxRoutine);

        if (ownsTimeScale || finalShotCinematicActive)
            Time.timeScale = 1f;

        shakeLocalOffset = Vector3.zero;
        cinematicLocalOffset = Vector3.zero;
        transform.localPosition = baseLocalPosition;

        if (targetCamera != null &&
            targetCamera.orthographic &&
            baseOrthographicSize > 0f)
        {
            targetCamera.orthographicSize =
                baseOrthographicSize;
        }

        DisableImpactVfx();

        shakeRoutine = null;
        zoomRoutine = null;
        finalApproachRoutine = null;
        finalImpactRoutine = null;
        impactVfxRoutine = null;
        finalShotCinematicActive = false;
        ownsTimeScale = false;
    }

    private void OnDestroy()
    {
        if (impactLineMaterial != null)
            Destroy(impactLineMaterial);
    }
}
