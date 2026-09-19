using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct CollectibleHitResult
{
    public readonly LevelData.CollectibleStyle Style;
    public readonly string CollectibleId;
    public readonly Vector2 WorldPoint;

    public CollectibleHitResult(
        LevelData.CollectibleStyle style,
        string collectibleId,
        Vector2 worldPoint)
    {
        Style = style;
        CollectibleId = collectibleId;
        WorldPoint = worldPoint;
    }
}

/// <summary>
/// Persistent mastery collectible. It is always a trigger and never changes
/// arrow velocity, collision, reflection, or the authored trajectory.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CircleCollider2D))]
public sealed class GoldenMedallionCollectible : MonoBehaviour
{
    public event Action<CollectibleHitResult> Collected;

    private SpriteRenderer medallionRenderer;
    private SpriteRenderer haloRenderer;
    private CircleCollider2D triggerCollider;
    private string collectibleId;
    private LevelData.CollectibleStyle style;
    private bool consumed;
    private bool previouslyCollected;
    private Vector3 authoredPosition;
    private Vector3 authoredScale;
    private float phase;
    private Coroutine collectRoutine;

    private static Sprite sparkSprite;

    public void Configure(
        LevelData.CollectibleStyle collectibleStyle,
        string stableId,
        bool alreadyCollected)
    {
        style = collectibleStyle;
        collectibleId =
            stableId != null
                ? stableId.Trim()
                : string.Empty;

        medallionRenderer ??=
            GetComponent<SpriteRenderer>();

        triggerCollider ??=
            GetComponent<CircleCollider2D>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        previouslyCollected =
            alreadyCollected;

        authoredPosition =
            transform.position;

        authoredScale =
            transform.localScale;

        phase =
            Mathf.Abs(
                GetInstanceID() *
                0.0137f) %
            (Mathf.PI * 2f);

        EnsureHalo();

        // Previously collected medals still appear on replay so the route can
        // be repeated, but use a slightly calmer halo.
        if (haloRenderer != null)
        {
            Color halo =
                haloRenderer.color;

            halo.a =
                alreadyCollected
                    ? 0.18f
                    : 0.46f;

            haloRenderer.color =
                halo;
        }
    }

    private void Update()
    {
        if (consumed)
            return;

        float time =
            Time.unscaledTime +
            phase;

        float hover =
            Mathf.Sin(
                time * 2.05f) *
            0.075f;

        transform.position =
            authoredPosition +
            Vector3.up *
            hover;

        // Gentle coin-turn illusion without a 3D mesh.
        float xScale =
            Mathf.Lerp(
                0.84f,
                1f,
                (Mathf.Sin(
                    time * 1.55f) +
                 1f) * 0.5f);

        transform.localScale =
            new Vector3(
                authoredScale.x *
                    xScale,
                authoredScale.y,
                authoredScale.z);

        transform.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                Mathf.Sin(
                    time * 1.25f) *
                4f);

        if (haloRenderer != null)
        {
            float pulseBase =
                previouslyCollected
                    ? 1.10f
                    : 1.22f;

            float pulseRange =
                previouslyCollected
                    ? 0.05f
                    : 0.085f;

            float pulse =
                pulseBase +
                (Mathf.Sin(
                    time * 2.65f) +
                 1f) *
                pulseRange;

            haloRenderer.transform.localScale =
                Vector3.one *
                pulse;
        }
    }

    private void OnTriggerEnter2D(
        Collider2D other)
    {
        if (consumed)
            return;

        ArrowController arrow =
            other.GetComponentInParent<ArrowController>();

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        consumed = true;

        if (triggerCollider != null)
            triggerCollider.enabled = false;

        Vector2 worldPoint =
            transform.position;

        Collected?.Invoke(
            new CollectibleHitResult(
                style,
                collectibleId,
                worldPoint));

        if (collectRoutine != null)
            StopCoroutine(collectRoutine);

        collectRoutine =
            StartCoroutine(
                CollectReaction());
    }

    private void EnsureHalo()
    {
        if (medallionRenderer == null ||
            medallionRenderer.sprite == null)
        {
            return;
        }

        Transform existing =
            transform.Find(
                "MedallionHalo");

        if (existing != null)
        {
            haloRenderer =
                existing.GetComponent<SpriteRenderer>();
        }

        if (haloRenderer == null)
        {
            GameObject halo =
                new GameObject(
                    "MedallionHalo");

            halo.transform.SetParent(
                transform,
                false);

            haloRenderer =
                halo.AddComponent<SpriteRenderer>();
        }

        haloRenderer.sprite =
            medallionRenderer.sprite;

        haloRenderer.sortingLayerID =
            medallionRenderer.sortingLayerID;

        haloRenderer.sortingOrder =
            medallionRenderer.sortingOrder - 1;

        haloRenderer.color =
            new Color(
                1f,
                0.76f,
                0.16f,
                0.34f);

        haloRenderer.transform.localPosition =
            Vector3.zero;

        haloRenderer.transform.localRotation =
            Quaternion.identity;

        haloRenderer.transform.localScale =
            Vector3.one *
            (previouslyCollected
                ? 1.16f
                : 1.26f);
    }

    private IEnumerator CollectReaction()
    {
        List<SparkPiece> sparks =
            SpawnSparks();

        Vector3 startScale =
            transform.localScale;

        Color startColor =
            medallionRenderer != null
                ? medallionRenderer.color
                : Color.white;

        Color haloStart =
            haloRenderer != null
                ? haloRenderer.color
                : Color.clear;

        float duration =
            previouslyCollected
                ? 0.50f
                : 0.78f;

        float elapsed =
            0f;

        while (elapsed < duration)
        {
            float dt =
                Time.unscaledDeltaTime;

            elapsed += dt;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float burst =
                Mathf.Sin(
                    Mathf.Clamp01(
                        t / 0.34f) *
                    Mathf.PI);

            float peakScale =
                previouslyCollected
                    ? 1.36f
                    : 1.72f;

            transform.localScale =
                startScale *
                Mathf.Lerp(
                    1f,
                    peakScale,
                    burst);

            if (medallionRenderer != null)
            {
                Color c =
                    startColor;

                c.a =
                    1f -
                    Mathf.Clamp01(
                        (t - 0.48f) /
                        0.52f);

                medallionRenderer.color =
                    c;
            }

            if (haloRenderer != null)
            {
                float haloStartScale =
                    previouslyCollected
                        ? 1.16f
                        : 1.26f;

                float haloEndScale =
                    previouslyCollected
                        ? 1.70f
                        : 2.35f;

                haloRenderer.transform.localScale =
                    Vector3.one *
                    Mathf.Lerp(
                        haloStartScale,
                        haloEndScale,
                        t);

                Color c =
                    haloStart;

                c.a =
                    Mathf.Lerp(
                        haloStart.a,
                        0f,
                        t);

                haloRenderer.color =
                    c;
            }

            for (int i = 0;
                 i < sparks.Count;
                 i++)
            {
                SparkPiece spark =
                    sparks[i];

                if (spark.Transform == null)
                    continue;

                spark.Transform.position +=
                    (Vector3)(
                        spark.Velocity *
                        dt);

                spark.Velocity *=
                    Mathf.Pow(
                        0.19f,
                        dt);

                if (spark.Renderer != null)
                {
                    Color c =
                        spark.Renderer.color;

                    c.a =
                        1f - t;

                    spark.Renderer.color =
                        c;
                }

                sparks[i] =
                    spark;
            }

            yield return null;
        }

        for (int i = 0;
             i < sparks.Count;
             i++)
        {
            if (sparks[i].Transform != null)
            {
                Destroy(
                    sparks[i]
                        .Transform
                        .gameObject);
            }
        }

        collectRoutine =
            null;

        Destroy(
            gameObject);
    }

    private struct SparkPiece
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector2 Velocity;
    }

    private List<SparkPiece> SpawnSparks()
    {
        EnsureSparkSprite();

        int count =
            previouslyCollected
                ? 10
                : 18;

        List<SparkPiece> result =
            new List<SparkPiece>(
                count);

        Transform parent =
            transform.parent;

        for (int i = 0;
             i < count;
             i++)
        {
            float angle =
                (Mathf.PI * 2f) *
                i /
                count;

            Vector2 direction =
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));

            GameObject spark =
                new GameObject(
                    $"MedallionSpark_{i + 1}");

            spark.transform.SetParent(
                parent,
                true);

            spark.transform.position =
                transform.position +
                (Vector3)(
                    direction *
                    0.08f);

            float sizeMultiplier =
                previouslyCollected
                    ? 1f
                    : 1.34f;

            float size =
                (i % 2 == 0
                    ? 1.20f
                    : 0.82f) *
                sizeMultiplier;

            spark.transform.localScale =
                Vector3.one *
                size;

            SpriteRenderer renderer =
                spark.AddComponent<SpriteRenderer>();

            renderer.sprite =
                sparkSprite;

            renderer.sortingLayerID =
                medallionRenderer != null
                    ? medallionRenderer.sortingLayerID
                    : 0;

            renderer.sortingOrder =
                medallionRenderer != null
                    ? medallionRenderer.sortingOrder + 2
                    : 4;

            renderer.color =
                i % 3 == 0
                    ? new Color(
                        1f,
                        0.98f,
                        0.72f,
                        1f)
                    : new Color(
                        1f,
                        0.69f,
                        0.09f,
                        1f);

            result.Add(
                new SparkPiece
                {
                    Transform =
                        spark.transform,
                    Renderer =
                        renderer,
                    Velocity =
                        direction *
                        Mathf.Lerp(
                            previouslyCollected
                                ? 1.25f
                                : 1.75f,
                            previouslyCollected
                                ? 2.00f
                                : 2.95f,
                            (i % 4) /
                            3f)
                });
        }

        return result;
    }

    private static void EnsureSparkSprite()
    {
        if (sparkSprite != null)
            return;

        const int size =
            16;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "RuntimeMedallionSpark";

        texture.hideFlags =
            HideFlags.HideAndDontSave;

        Color[] pixels =
            new Color[
                size *
                size];

        Vector2 center =
            new Vector2(
                7.5f,
                7.5f);

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
                        new Vector2(
                            x,
                            y),
                        center);

                float alpha =
                    Mathf.Clamp01(
                        1f -
                        distance /
                        7.5f);

                alpha *= alpha;

                pixels[
                    y * size +
                    x] =
                    new Color(
                        1f,
                        1f,
                        1f,
                        alpha);
            }
        }

        texture.SetPixels(
            pixels);

        texture.Apply();

        sparkSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    size,
                    size),
                new Vector2(
                    0.5f,
                    0.5f),
                100f);

        sparkSprite.name =
            "RuntimeMedallionSparkSprite";
    }

    private void OnDisable()
    {
        if (collectRoutine != null)
        {
            StopCoroutine(
                collectRoutine);

            collectRoutine =
                null;
        }
    }
}
