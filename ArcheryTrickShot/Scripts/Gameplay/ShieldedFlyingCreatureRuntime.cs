using System.Collections;
using UnityEngine;

/// <summary>
/// Reusable protective shield presentation + collision for FlyingBirdTarget.
///
/// The visual is a real authored transparent sprite. The collider is independent
/// from that artwork, so gameplay radius can be tuned without distorting art.
/// No LineRenderer/procedural circle is used as the primary shield visual.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShieldedFlyingCreatureRuntime : MonoBehaviour
{
    private CircleCollider2D shieldCollider;

    private Transform visualRoot;
    private SpriteRenderer shieldRenderer;
    private SpriteRenderer glowRenderer;

    private Vector3 baseVisualScale =
        Vector3.one;

    private Color baseShieldColor =
        Color.white;

    private float pulseSpeed;
    private float rotationSpeed;

    private float impactStrength;
    private Coroutine impactRoutine;

    public void Configure(
        float worldRadius,
        int sortingOrder,
        float configuredPulseSpeed,
        float configuredRotationSpeed,
        string visualResourcePath,
        float visualWorldWidth)
    {
        float safeWorldRadius =
            Mathf.Max(
                0.25f,
                worldRadius);

        pulseSpeed =
            Mathf.Max(
                0.1f,
                configuredPulseSpeed);

        rotationSpeed =
            configuredRotationSpeed;

        float parentScale =
            Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(
                        transform.lossyScale.x),
                    Mathf.Abs(
                        transform.lossyScale.y)));

        shieldCollider =
            gameObject.AddComponent<
                CircleCollider2D>();

        shieldCollider.isTrigger =
            false;

        shieldCollider.radius =
            safeWorldRadius /
            parentScale;

        Sprite shieldSprite =
            string.IsNullOrWhiteSpace(
                visualResourcePath)
                ? null
                : Resources.Load<Sprite>(
                    visualResourcePath.Trim());

        if (shieldSprite == null)
        {
            Debug.LogWarning(
                "[Flying Shield] Missing authored shield sprite at Resources/" +
                visualResourcePath,
                this);

            return;
        }

        BuildVisual(
            shieldSprite,
            sortingOrder,
            parentScale,
            Mathf.Max(
                0.25f,
                visualWorldWidth));
    }

    private void BuildVisual(
        Sprite sprite,
        int sortingOrder,
        float parentScale,
        float visualWorldWidth)
    {
        GameObject rootObject =
            new GameObject(
                "ProtectiveShieldVisual");

        rootObject.transform.SetParent(
            transform,
            false);

        visualRoot =
            rootObject.transform;

        float fitScale =
            1f;

        if (sprite.bounds.size.x >
            0.001f)
        {
            fitScale =
                visualWorldWidth /
                sprite.bounds.size.x /
                parentScale;
        }

        baseVisualScale =
            Vector3.one *
            fitScale;

        visualRoot.localScale =
            baseVisualScale;

        GameObject glowObject =
            new GameObject(
                "ShieldSoftGlow");

        glowObject.transform.SetParent(
            visualRoot,
            false);

        glowRenderer =
            glowObject.AddComponent<
                SpriteRenderer>();

        glowRenderer.sprite =
            sprite;

        glowRenderer.sortingOrder =
            sortingOrder - 1;

        glowRenderer.color =
            new Color(
                0.72f,
                0.76f,
                1f,
                0.17f);

        glowObject.transform.localScale =
            Vector3.one *
            1.035f;

        GameObject shieldObject =
            new GameObject(
                "ShieldShell");

        shieldObject.transform.SetParent(
            visualRoot,
            false);

        shieldRenderer =
            shieldObject.AddComponent<
                SpriteRenderer>();

        shieldRenderer.sprite =
            sprite;

        shieldRenderer.sortingOrder =
            sortingOrder;

        // Keep the centre of the authored bubble highly transparent so the
        // creature remains readable during real gameplay.
        baseShieldColor =
            new Color(
                1f,
                1f,
                1f,
                0.72f);

        shieldRenderer.color =
            baseShieldColor;
    }

    private void Update()
    {
        if (visualRoot == null)
            return;

        float t =
            Time.unscaledTime;

        float wave =
            0.5f +
            0.5f *
            Mathf.Sin(
                t *
                pulseSpeed);

        float scale =
            1f +
            wave *
            0.018f +
            impactStrength *
            0.12f;

        visualRoot.localScale =
            baseVisualScale *
            scale;

        visualRoot.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                t *
                rotationSpeed);

        if (shieldRenderer != null)
        {
            Color c =
                baseShieldColor;

            c.r =
                Mathf.Lerp(
                    0.95f,
                    1f,
                    wave);

            c.g =
                Mathf.Lerp(
                    0.94f,
                    1f,
                    impactStrength);

            c.b =
                1f;

            c.a =
                Mathf.Lerp(
                    0.66f,
                    0.78f,
                    wave);

            c.a =
                Mathf.Clamp01(
                    c.a +
                    impactStrength *
                    0.20f);

            shieldRenderer.color =
                c;
        }

        if (glowRenderer != null)
        {
            Color c =
                glowRenderer.color;

            c.a =
                Mathf.Lerp(
                    0.11f,
                    0.20f,
                    wave) +
                impactStrength *
                0.18f;

            glowRenderer.color =
                c;
        }
    }

    private void OnCollisionEnter2D(
        Collision2D collision)
    {
        if (collision == null)
            return;

        ArrowController arrow =
            collision.collider != null
                ? collision.collider
                    .GetComponentInParent<
                        ArrowController>()
                : null;

        if (arrow == null &&
            collision.otherCollider != null)
        {
            arrow =
                collision.otherCollider
                    .GetComponentInParent<
                        ArrowController>();
        }

        if (arrow == null ||
            !arrow.HasFired)
        {
            return;
        }

        ATSHaptics.Pulse();

        if (impactRoutine != null)
            StopCoroutine(
                impactRoutine);

        impactRoutine =
            StartCoroutine(
                ImpactPulse());
    }

    private IEnumerator ImpactPulse()
    {
        const float duration =
            0.26f;

        float elapsed =
            0f;

        while (elapsed <
               duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            impactStrength =
                Mathf.Sin(
                    t *
                    Mathf.PI);

            yield return null;
        }

        impactStrength =
            0f;

        impactRoutine =
            null;
    }

    private void OnDisable()
    {
        if (impactRoutine != null)
        {
            StopCoroutine(
                impactRoutine);

            impactRoutine =
                null;
        }

        impactStrength =
            0f;
    }
}
