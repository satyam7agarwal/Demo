using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class Mirror : MonoBehaviour
{
    private Collider2D mirrorCollider;
    private SpriteRenderer[] visualRenderers;
    private Color[] baseColors;
    private SpriteRenderer glowRenderer;
    private Coroutine pulseRoutine;
    private GameConfig config;

    private void Awake()
    {
        mirrorCollider = GetComponent<Collider2D>();
        config = GameConfig.Load();

        visualRenderers =
            GetComponentsInChildren<SpriteRenderer>(true);

        baseColors =
            new Color[visualRenderers.Length];

        for (int i = 0; i < visualRenderers.Length; i++)
            baseColors[i] = visualRenderers[i].color;

        EnsureGlowRenderer();
    }

    /// <summary>
    /// Current project path: mirrors are trigger colliders.
    /// ArrowController resolves the actual surface point and keeps the visible
    /// arrow tip at that exact incidence point.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        ArrowController arrow =
            other.GetComponentInParent<ArrowController>();

        if (arrow == null)
            return;

        if (mirrorCollider == null)
            mirrorCollider = GetComponent<Collider2D>();

        if (mirrorCollider == null)
            return;

        bool reflected = arrow.ReflectFromMirror(
            mirrorCollider,
            mirrorCollider.transform.right);

        if (reflected)
            PlayRicochetPulse();
    }

    /// <summary>
    /// Also supports a non-trigger mirror collider. The contact point comes
    /// from physics, while reflection always uses the mirror's authored plane
    /// normal so end-cap hits do not bounce in a strange 90-degree direction.
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        ArrowController arrow =
            collision.collider
                .GetComponentInParent<ArrowController>();

        if (arrow == null ||
            collision.contactCount == 0)
        {
            return;
        }

        if (mirrorCollider == null)
            mirrorCollider = GetComponent<Collider2D>();

        ContactPoint2D contact =
            collision.GetContact(0);

        Vector2 incomingDirection =
            collision.relativeVelocity;

        if (incomingDirection.sqrMagnitude < 0.0001f)
            incomingDirection = arrow.GetVelocity();

        bool reflected = arrow.ReflectAtContact(
            incomingDirection,
            contact.point,
            mirrorCollider.transform.right,
            mirrorCollider);

        if (reflected)
            PlayRicochetPulse();
    }

    private void EnsureGlowRenderer()
    {
        if (visualRenderers == null || visualRenderers.Length == 0)
            return;

        SpriteRenderer source = visualRenderers[0];
        if (source == null || source.sprite == null)
            return;

        Transform existing = transform.Find("RicochetGlow");
        if (existing != null)
        {
            glowRenderer = existing.GetComponent<SpriteRenderer>();
        }
        else
        {
            GameObject glow = new GameObject("RicochetGlow");
            glow.transform.SetParent(transform, false);
            glowRenderer = glow.AddComponent<SpriteRenderer>();
        }

        if (glowRenderer == null)
            return;

        glowRenderer.sprite = source.sprite;
        glowRenderer.sharedMaterial = source.sharedMaterial;
        glowRenderer.sortingLayerID = source.sortingLayerID;
        glowRenderer.sortingOrder = source.sortingOrder + 1;
        glowRenderer.flipX = source.flipX;
        glowRenderer.flipY = source.flipY;
        glowRenderer.maskInteraction = source.maskInteraction;
        glowRenderer.color = new Color(0.55f, 0.95f, 1f, 0f);
        glowRenderer.transform.localPosition = Vector3.zero;
        glowRenderer.transform.localRotation = Quaternion.identity;
        glowRenderer.transform.localScale = Vector3.one;
    }

    private void PlayRicochetPulse()
    {
        if (!isActiveAndEnabled)
            return;

        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            RestoreVisualState();
        }

        pulseRoutine = StartCoroutine(RicochetPulseRoutine());
    }

    private IEnumerator RicochetPulseRoutine()
    {
        float duration = Mathf.Max(
            0.05f,
            config != null
                ? config.MirrorPulseDuration
                : 0.12f);

        float peakScale = config != null
            ? config.MirrorPulseScale
            : 1.055f;

        Color flashColor =
            new Color(0.72f, 0.98f, 1f, 1f);

        float half = duration * 0.45f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, half));
            float eased = 1f - (1f - t) * (1f - t);

            ApplyColorBlend(
                flashColor,
                Mathf.Lerp(0f, 0.34f, eased));

            if (glowRenderer != null)
            {
                glowRenderer.transform.localScale =
                    Vector3.one * Mathf.Lerp(1f, peakScale, eased);

                Color glow = flashColor;
                glow.a = Mathf.Lerp(0f, 0.42f, eased);
                glowRenderer.color = glow;
            }

            yield return null;
        }

        float returnDuration = Mathf.Max(0.001f, duration - half);
        elapsed = 0f;

        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / returnDuration);

            ApplyColorBlend(
                flashColor,
                Mathf.Lerp(0.34f, 0f, t));

            if (glowRenderer != null)
            {
                glowRenderer.transform.localScale =
                    Vector3.one * Mathf.Lerp(peakScale, 1f, t);

                Color glow = flashColor;
                glow.a = Mathf.Lerp(0.42f, 0f, t);
                glowRenderer.color = glow;
            }

            yield return null;
        }

        RestoreVisualState();
        pulseRoutine = null;
    }

    private void ApplyColorBlend(Color target, float amount)
    {
        if (visualRenderers == null || baseColors == null)
            return;

        int count = Mathf.Min(
            visualRenderers.Length,
            baseColors.Length);

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = visualRenderers[i];
            if (renderer == null || renderer == glowRenderer)
                continue;

            Color baseColor = baseColors[i];
            Color blended = Color.Lerp(baseColor, target, amount);
            blended.a = baseColor.a;
            renderer.color = blended;
        }
    }

    private void RestoreVisualState()
    {
        if (visualRenderers != null && baseColors != null)
        {
            int count = Mathf.Min(
                visualRenderers.Length,
                baseColors.Length);

            for (int i = 0; i < count; i++)
            {
                if (visualRenderers[i] != null &&
                    visualRenderers[i] != glowRenderer)
                {
                    visualRenderers[i].color = baseColors[i];
                }
            }
        }

        if (glowRenderer != null)
        {
            glowRenderer.transform.localScale = Vector3.one;
            glowRenderer.color = new Color(0.55f, 0.95f, 1f, 0f);
        }
    }

    private void OnDisable()
    {
        if (pulseRoutine != null)
            StopCoroutine(pulseRoutine);

        RestoreVisualState();
        pulseRoutine = null;
    }
}
