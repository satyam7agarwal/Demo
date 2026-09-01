using System.Collections;
using UnityEngine;

/// <summary>
/// Lightweight camera feedback. All effects use unscaled time so they remain
/// reliable around pause/result transitions. The values come from GameConfig.
/// </summary>
public sealed class GameFeelController : MonoBehaviour
{
    private GameConfig config;
    private Camera targetCamera;

    private Coroutine shakeRoutine;
    private Coroutine zoomRoutine;

    private Vector3 baseLocalPosition;
    private float baseOrthographicSize;

    public void Configure(GameConfig gameConfig)
    {
        config = gameConfig != null
            ? gameConfig
            : GameConfig.Load();

        targetCamera = GetComponent<Camera>();
        baseLocalPosition = transform.localPosition;

        if (targetCamera != null && targetCamera.orthographic)
            baseOrthographicSize = targetCamera.orthographicSize;
    }

    private void Awake()
    {
        Configure(GameConfig.Load());
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

    public void PlayRicochetFeedback()
    {
        // Ricochets should feel crisp without shaking the whole scene.
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

    private void Shake(float duration, float magnitude)
    {
        if (duration <= 0f || magnitude <= 0f)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        baseLocalPosition = transform.localPosition;
        shakeRoutine = StartCoroutine(
            ShakeRoutine(duration, magnitude));
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(
                elapsed / Mathf.Max(0.001f, duration));

            // Ease the shake out so it feels like an impact instead of jitter.
            float damping = 1f - normalized;
            damping *= damping;

            Vector2 offset =
                Random.insideUnitCircle * magnitude * damping;

            transform.localPosition =
                baseLocalPosition +
                new Vector3(offset.x, offset.y, 0f);

            yield return null;
        }

        transform.localPosition = baseLocalPosition;
        shakeRoutine = null;
    }

    private void PunchZoom(float zoomFactor, float duration)
    {
        if (targetCamera == null ||
            !targetCamera.orthographic ||
            duration <= 0f)
        {
            return;
        }

        if (zoomRoutine != null)
            StopCoroutine(zoomRoutine);

        baseOrthographicSize = targetCamera.orthographicSize;
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
        float returnDuration = Mathf.Max(0.001f, duration - punchDuration);
        float punchedSize = baseOrthographicSize * zoomFactor;

        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, punchDuration));
            targetCamera.orthographicSize =
                Mathf.Lerp(baseOrthographicSize, punchedSize, EaseOutCubic(t));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / returnDuration);
            targetCamera.orthographicSize =
                Mathf.Lerp(punchedSize, baseOrthographicSize, EaseOutCubic(t));
            yield return null;
        }

        targetCamera.orthographicSize = baseOrthographicSize;
        zoomRoutine = null;
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

        transform.localPosition = baseLocalPosition;

        if (targetCamera != null &&
            targetCamera.orthographic &&
            baseOrthographicSize > 0f)
        {
            targetCamera.orthographicSize = baseOrthographicSize;
        }

        shakeRoutine = null;
        zoomRoutine = null;
    }
}
