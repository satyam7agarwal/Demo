using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ResonanceCrystalRuntime : MonoBehaviour
{
    private CrystalWorldMechanicRuntime owner;
    private string unlockGroupId;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer glowRenderer;
    private CircleCollider2D trigger;
    private LineRenderer halo;
    private Material lineMaterial;
    private bool activated;
    private Coroutine pulseRoutine;

    public void Configure(
        CrystalWorldMechanicRuntime runtime,
        string groupId,
        SpriteRenderer renderer)
    {
        owner = runtime;
        unlockGroupId = groupId;
        spriteRenderer = renderer;
        trigger = GetComponent<CircleCollider2D>();

        EnsureVisualLanguage();
        ApplyInactiveState();
    }

    private void Update()
    {
        if (activated || halo == null)
            return;

        float wave =
            0.5f +
            0.5f *
            Mathf.Sin(Time.time * 2.1f);

        Color haloColor =
            new Color(
                0.25f,
                0.92f,
                1f,
                0.44f + wave * 0.12f);

        halo.startColor = haloColor;
        halo.endColor = haloColor;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (activated || other == null)
            return;

        ArrowController arrow =
            other.GetComponentInParent<ArrowController>();

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        activated = true;

        if (trigger != null)
            trigger.enabled = false;

        owner?.ActivateGroup(
            unlockGroupId,
            transform.position);

        GameAudioController.Instance?.PlayTargetHit();
        ATSHaptics.Pulse();

        if (pulseRoutine != null)
            StopCoroutine(pulseRoutine);

        pulseRoutine =
            StartCoroutine(PulseRoutine());
    }

    private void EnsureVisualLanguage()
    {
        if (lineMaterial == null)
        {
            Shader shader =
                Shader.Find("Sprites/Default");

            if (shader != null)
            {
                lineMaterial =
                    new Material(shader)
                    {
                        name = "RuntimeResonanceCrystalLines",
                        hideFlags = HideFlags.DontSave
                    };
            }
        }

        EnsureGlow();
        EnsureHalo();
    }

    private void EnsureGlow()
    {
        if (spriteRenderer == null ||
            spriteRenderer.sprite == null)
        {
            return;
        }

        Transform existing =
            transform.Find("ResonanceGlow");

        if (existing != null)
        {
            glowRenderer =
                existing.GetComponent<SpriteRenderer>();
        }

        if (glowRenderer == null)
        {
            GameObject glow =
                new GameObject(
                    "ResonanceGlow",
                    typeof(SpriteRenderer));

            glow.transform.SetParent(
                transform,
                false);

            glowRenderer =
                glow.GetComponent<SpriteRenderer>();
        }

        glowRenderer.sprite =
            spriteRenderer.sprite;

        glowRenderer.sortingLayerID =
            spriteRenderer.sortingLayerID;

        glowRenderer.sortingOrder =
            spriteRenderer.sortingOrder - 1;

        glowRenderer.transform.localScale =
            Vector3.one * 1.34f;
    }

    private void EnsureHalo()
    {
        if (halo != null)
            return;

        GameObject haloObject =
            new GameObject("ResonanceInteractiveHalo");

        haloObject.transform.SetParent(
            transform,
            false);

        halo =
            haloObject.AddComponent<LineRenderer>();

        halo.sharedMaterial = lineMaterial;
        halo.useWorldSpace = false;
        halo.loop = true;
        halo.positionCount = 40;
        halo.startWidth = 0.060f;
        halo.endWidth = 0.060f;
        halo.sortingOrder =
            spriteRenderer != null
                ? spriteRenderer.sortingOrder - 1
                : 7;

        const float radius = 1.18f;

        for (int i = 0; i < 40; i++)
        {
            float angle =
                i / 40f *
                Mathf.PI *
                2f;

            halo.SetPosition(
                i,
                new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
        }
    }

    private void ApplyInactiveState()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color =
                new Color(
                    0.68f,
                    0.90f,
                    0.94f,
                    1f);
        }

        if (glowRenderer != null)
        {
            glowRenderer.color =
                new Color(
                    0.18f,
                    0.88f,
                    1f,
                    0.18f);
        }
    }

    private IEnumerator PulseRoutine()
    {
        if (spriteRenderer == null)
            yield break;

        Vector3 originalScale =
            transform.localScale;

        Color original =
            spriteRenderer.color;

        const float duration = 0.44f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            float pulse =
                Mathf.Sin(t * Mathf.PI);

            transform.localScale =
                originalScale *
                (1f + pulse * 0.22f);

            spriteRenderer.color =
                Color.Lerp(
                    original,
                    Color.white,
                    pulse);

            if (halo != null)
            {
                Color energy =
                    Color.Lerp(
                        new Color(0.22f, 0.94f, 1f, 0.72f),
                        Color.white,
                        pulse * 0.72f);

                halo.startColor = energy;
                halo.endColor = energy;
                halo.startWidth =
                    0.060f + pulse * 0.10f;
                halo.endWidth =
                    halo.startWidth;
            }

            yield return null;
        }

        transform.localScale =
            originalScale;

        spriteRenderer.color =
            new Color(
                0.76f,
                1f,
                1f,
                1f);

        if (glowRenderer != null)
        {
            glowRenderer.color =
                new Color(
                    0.26f,
                    0.96f,
                    1f,
                    0.42f);
        }

        if (halo != null)
        {
            Color active =
                new Color(
                    0.36f,
                    1f,
                    1f,
                    0.92f);

            halo.startColor = active;
            halo.endColor = active;
            halo.startWidth = 0.070f;
            halo.endWidth = 0.070f;
        }


        pulseRoutine = null;
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }
    }
}
