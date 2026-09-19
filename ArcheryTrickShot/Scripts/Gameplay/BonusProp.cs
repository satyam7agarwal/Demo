using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct BonusPropHitResult
{
    public readonly LevelData.BonusPropStyle Style;
    public readonly int Score;
    public readonly string Label;
    public readonly Vector2 WorldPoint;

    public BonusPropHitResult(
        LevelData.BonusPropStyle style,
        int score,
        string label,
        Vector2 worldPoint)
    {
        Style = style;
        Score = score;
        Label = label;
        WorldPoint = worldPoint;
    }
}

/// <summary>
/// Optional, non-blocking style object. The collider is a trigger so the real
/// projectile always continues on its existing physics path.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class BonusProp : MonoBehaviour
{
    private struct FragmentPiece
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector2 Velocity;
        public float Spin;
        public Vector3 StartScale;
    }

    public event Action<BonusPropHitResult> Hit;

    private SpriteRenderer visual;
    private BoxCollider2D triggerCollider;
    private LevelData.BonusPropStyle style;
    private int score;
    private bool consumed;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private Coroutine reactionRoutine;

    private static Sprite fragmentSprite;

    public void Configure(
        LevelData.BonusPropStyle bonusStyle,
        int scoreOverride)
    {
        style = bonusStyle;
        score = scoreOverride > 0
            ? scoreOverride
            : GetDefaultScore(style);

        visual ??= GetComponent<SpriteRenderer>();
        triggerCollider ??= GetComponent<BoxCollider2D>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        consumed = false;
        baseScale = transform.localScale;
        baseRotation = transform.localRotation;
    }

    private void OnTriggerEnter2D(Collider2D other)
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

        Vector2 velocity =
            arrow.GetVelocity();

        Vector2 direction =
            velocity.sqrMagnitude > 0.0001f
                ? velocity.normalized
                : Vector2.right;

        Vector2 hitPoint =
            arrow.GetVisualTipWorldPosition(direction);

        Hit?.Invoke(
            new BonusPropHitResult(
                style,
                score,
                GetLabel(style),
                hitPoint));

        if (reactionRoutine != null)
            StopCoroutine(reactionRoutine);

        reactionRoutine =
            StartCoroutine(
                style == LevelData.BonusPropStyle.Bell
                    ? BellReaction()
                    : BreakReaction(direction));
    }

    private IEnumerator BreakReaction(
        Vector2 incomingDirection)
    {
        Color fragmentColor =
            GetFragmentColor(style);

        List<FragmentPiece> fragments =
            SpawnFragments(
                fragmentColor,
                incomingDirection);

        if (visual != null)
            visual.enabled = false;

        const float lifetime = 0.84f;
        float elapsed = 0f;

        while (elapsed < lifetime)
        {
            float dt =
                Time.unscaledDeltaTime;

            elapsed += dt;

            float t =
                Mathf.Clamp01(
                    elapsed / lifetime);

            for (int i = 0;
                 i < fragments.Count;
                 i++)
            {
                FragmentPiece piece =
                    fragments[i];

                if (piece.Transform == null)
                    continue;

                piece.Velocity +=
                    Vector2.down *
                    (4.7f * dt);

                piece.Transform.position +=
                    (Vector3)(piece.Velocity * dt);

                piece.Transform.Rotate(
                    0f,
                    0f,
                    piece.Spin * dt);

                float fade =
                    1f -
                    Mathf.Clamp01(
                        (t - 0.64f) / 0.36f);

                if (piece.Renderer != null)
                {
                    Color c =
                        piece.Renderer.color;
                    c.a = fade;
                    piece.Renderer.color = c;
                }

                piece.Transform.localScale =
                    piece.StartScale *
                    Mathf.Lerp(
                        1f,
                        0.72f,
                        t);

                fragments[i] =
                    piece;
            }

            yield return null;
        }

        for (int i = 0;
             i < fragments.Count;
             i++)
        {
            if (fragments[i].Transform != null)
                Destroy(fragments[i].Transform.gameObject);
        }

        reactionRoutine = null;
    }

    private IEnumerator BellReaction()
    {
        if (visual == null)
        {
            reactionRoutine = null;
            yield break;
        }

        const float duration = 0.52f;
        float elapsed = 0f;

        Color original =
            visual.color;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            float envelope =
                1f - t;

            float angle =
                Mathf.Sin(
                    t *
                    Mathf.PI *
                    7f) *
                8f *
                envelope;

            transform.localRotation =
                baseRotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    angle);

            float pulse =
                1f +
                Mathf.Sin(
                    t *
                    Mathf.PI) *
                0.08f;

            transform.localScale =
                baseScale * pulse;

            visual.color =
                Color.Lerp(
                    original,
                    new Color(
                        1f,
                        0.92f,
                        0.38f,
                        original.a),
                    Mathf.Sin(
                        t *
                        Mathf.PI) *
                    0.42f);

            yield return null;
        }

        transform.localRotation =
            baseRotation;
        transform.localScale =
            baseScale;
        visual.color =
            original;

        reactionRoutine = null;
    }

    private List<FragmentPiece> SpawnFragments(
        Color baseColor,
        Vector2 incomingDirection)
    {
        EnsureFragmentSprite();

        int count =
            style == LevelData.BonusPropStyle.GlassBottle
                ? 10
                : 7;

        List<FragmentPiece> pieces =
            new List<FragmentPiece>(count);

        Transform parent =
            transform.parent;

        uint seed =
            (uint)(
                GetInstanceID() *
                2654435761u);

        for (int i = 0;
             i < count;
             i++)
        {
            GameObject shard =
                new GameObject(
                    $"BonusShard_{i + 1}");

            shard.transform.SetParent(
                parent,
                true);

            shard.transform.position =
                transform.position +
                new Vector3(
                    SignedRandom(
                        ref seed,
                        0.12f),
                    SignedRandom(
                        ref seed,
                        0.16f),
                    -0.01f);

            // These are desired WORLD sizes, not Transform scale factors.
            // RuntimeBonusShardSprite is only 0.08 x 0.08 world units at
            // 100 PPU, so applying 0.11–0.31 directly as localScale made
            // the fragments microscopic.
            float desiredWorldWidth =
                RandomRange(
                    ref seed,
                    0.11f,
                    0.22f);

            float desiredWorldHeight =
                RandomRange(
                    ref seed,
                    0.16f,
                    0.31f);

            Vector2 spriteWorldSize =
                fragmentSprite != null
                    ? fragmentSprite.bounds.size
                    : new Vector2(
                        0.08f,
                        0.08f);

            Vector3 parentLossyScale =
                parent != null
                    ? parent.lossyScale
                    : Vector3.one;

            float safeSpriteWidth =
                Mathf.Max(
                    0.0001f,
                    spriteWorldSize.x);

            float safeSpriteHeight =
                Mathf.Max(
                    0.0001f,
                    spriteWorldSize.y);

            float safeParentX =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(
                        parentLossyScale.x));

            float safeParentY =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(
                        parentLossyScale.y));

            shard.transform.localScale =
                new Vector3(
                    desiredWorldWidth /
                        safeSpriteWidth /
                        safeParentX,
                    desiredWorldHeight /
                        safeSpriteHeight /
                        safeParentY,
                    1f);

            shard.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    RandomRange(
                        ref seed,
                        0f,
                        360f));

            SpriteRenderer renderer =
                shard.AddComponent<SpriteRenderer>();

            renderer.sprite =
                fragmentSprite;

            renderer.sortingLayerID =
                visual != null
                    ? visual.sortingLayerID
                    : 0;

            renderer.sortingOrder =
                visual != null
                    ? visual.sortingOrder + 1
                    : 2;

            float tint =
                RandomRange(
                    ref seed,
                    0.78f,
                    1.12f);

            renderer.color =
                new Color(
                    Mathf.Clamp01(
                        baseColor.r * tint),
                    Mathf.Clamp01(
                        baseColor.g * tint),
                    Mathf.Clamp01(
                        baseColor.b * tint),
                    1f);

            Vector2 radial =
                new Vector2(
                    SignedRandom(
                        ref seed,
                        1f),
                    RandomRange(
                        ref seed,
                        0.25f,
                        1.2f))
                .normalized;

            Vector2 velocity =
                radial *
                    RandomRange(
                        ref seed,
                        1.9f,
                        3.6f) +
                incomingDirection *
                    RandomRange(
                        ref seed,
                        0.40f,
                        1.00f);

            pieces.Add(
                new FragmentPiece
                {
                    Transform =
                        shard.transform,
                    Renderer =
                        renderer,
                    Velocity =
                        velocity,
                    Spin =
                        SignedRandom(
                            ref seed,
                            690f),
                    StartScale =
                        shard.transform.localScale
                });
        }

        return pieces;
    }

    private static void EnsureFragmentSprite()
    {
        if (fragmentSprite != null)
            return;

        Texture2D texture =
            new Texture2D(
                8,
                8,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "RuntimeBonusShardTexture";

        texture.hideFlags =
            HideFlags.HideAndDontSave;

        Color[] pixels =
            new Color[64];

        for (int i = 0;
             i < pixels.Length;
             i++)
        {
            pixels[i] =
                Color.white;
        }

        texture.SetPixels(
            pixels);
        texture.Apply();

        fragmentSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    8f,
                    8f),
                new Vector2(
                    0.5f,
                    0.5f),
                100f);

        fragmentSprite.name =
            "RuntimeBonusShardSprite";
    }

    private static int GetDefaultScore(
        LevelData.BonusPropStyle propStyle)
    {
        return propStyle switch
        {
            LevelData.BonusPropStyle.ClayPot =>
                50,
            LevelData.BonusPropStyle.GlassBottle =>
                75,
            LevelData.BonusPropStyle.Bell =>
                100,
            _ =>
                50
        };
    }

    private static string GetLabel(
        LevelData.BonusPropStyle propStyle)
    {
        return propStyle switch
        {
            LevelData.BonusPropStyle.ClayPot =>
                "SMASH!",
            LevelData.BonusPropStyle.GlassBottle =>
                "SHATTER!",
            LevelData.BonusPropStyle.Bell =>
                "DING!",
            _ =>
                "BONUS!"
        };
    }

    private static Color GetFragmentColor(
        LevelData.BonusPropStyle propStyle)
    {
        return propStyle switch
        {
            LevelData.BonusPropStyle.ClayPot =>
                new Color(
                    0.78f,
                    0.27f,
                    0.10f,
                    1f),
            LevelData.BonusPropStyle.GlassBottle =>
                new Color(
                    0.20f,
                    0.86f,
                    0.92f,
                    1f),
            _ =>
                new Color(
                    0.95f,
                    0.66f,
                    0.14f,
                    1f)
        };
    }

    private static float RandomRange(
        ref uint seed,
        float min,
        float max)
    {
        seed =
            seed *
            1664525u +
            1013904223u;

        float normalized =
            (seed & 0x00FFFFFFu) /
            16777215f;

        return Mathf.Lerp(
            min,
            max,
            normalized);
    }

    private static float SignedRandom(
        ref uint seed,
        float magnitude)
    {
        return RandomRange(
            ref seed,
            -magnitude,
            magnitude);
    }

    private void OnDisable()
    {
        if (reactionRoutine != null)
        {
            StopCoroutine(
                reactionRoutine);
            reactionRoutine =
                null;
        }
    }
}
