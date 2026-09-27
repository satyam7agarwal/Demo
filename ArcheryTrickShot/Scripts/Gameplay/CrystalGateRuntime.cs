using System.Collections;
using UnityEngine;

/// <summary>
/// Two-piece Crystal Caverns gate.
/// The gate never flies away as one giant block: its authored stone halves
/// charge, then retract in opposite directions to open a readable passage.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalGateRuntime : MonoBehaviour
{
    private Transform topHalf;
    private Transform bottomHalf;
    private Vector3 topClosedLocalPosition;
    private Vector3 bottomClosedLocalPosition;
    private Vector2 openOffset;
    private float openDuration;
    private Coroutine routine;
    private bool opened;
    private LineRenderer[] runeFrames;
    private SpriteRenderer[] renderers;
    private Collider2D[] colliders;

    public Vector2 ClosedWorldPosition =>
        transform.position;

    public void Configure(
        Transform top,
        Transform bottom,
        Vector2 offset,
        float duration)
    {
        topHalf = top;
        bottomHalf = bottom;
        openOffset = offset;
        openDuration = Mathf.Max(0.16f, duration);

        if (topHalf != null)
            topClosedLocalPosition = topHalf.localPosition;

        if (bottomHalf != null)
            bottomClosedLocalPosition = bottomHalf.localPosition;

        runeFrames =
            GetComponentsInChildren<LineRenderer>(true);

        renderers =
            GetComponentsInChildren<SpriteRenderer>(true);

        colliders =
            GetComponentsInChildren<Collider2D>(true);
    }

    public void Open(float delay = 0.22f)
    {
        if (opened)
            return;

        opened = true;

        if (routine != null)
            StopCoroutine(routine);

        routine =
            StartCoroutine(
                OpenRoutine(
                    Mathf.Max(0f, delay)));
    }

    private IEnumerator OpenRoutine(
        float delay)
    {
        float chargeElapsed = 0f;

        while (chargeElapsed < delay)
        {
            chargeElapsed += Time.deltaTime;

            float t =
                delay <= 0.001f
                    ? 1f
                    : Mathf.Clamp01(
                        chargeElapsed / delay);

            float pulse =
                Mathf.Sin(t * Mathf.PI);

            ApplyChargeVisual(pulse);
            yield return null;
        }

        // The old implementation moved one complete rectangle upward. That
        // looked like a floating debug block. This gate is deliberately split
        // into two physical stone leaves and retracts them away from the path.
        Vector3 topStart =
            topHalf != null
                ? topHalf.localPosition
                : Vector3.zero;

        Vector3 bottomStart =
            bottomHalf != null
                ? bottomHalf.localPosition
                : Vector3.zero;

        float verticalTravel =
            Mathf.Max(
                1.15f,
                Mathf.Abs(openOffset.y) * 0.56f);

        float horizontalTravel =
            openOffset.x * 0.45f;

        Vector3 topEnd =
            topClosedLocalPosition +
            new Vector3(
                horizontalTravel,
                verticalTravel,
                0f);

        Vector3 bottomEnd =
            bottomClosedLocalPosition +
            new Vector3(
                -horizontalTravel,
                -verticalTravel,
                0f);

        float elapsed = 0f;

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / openDuration);

            float eased =
                1f -
                Mathf.Pow(
                    1f - t,
                    3f);

            if (topHalf != null)
            {
                topHalf.localPosition =
                    Vector3.LerpUnclamped(
                        topStart,
                        topEnd,
                        eased);
            }

            if (bottomHalf != null)
            {
                bottomHalf.localPosition =
                    Vector3.LerpUnclamped(
                        bottomStart,
                        bottomEnd,
                        eased);
            }

            ApplyOpeningVisual(t);
            yield return null;
        }

        if (topHalf != null)
            topHalf.localPosition = topEnd;

        if (bottomHalf != null)
            bottomHalf.localPosition = bottomEnd;

        // Once the two leaves are visually clear, disable their collision
        // entirely. This removes any edge-case grazing against a moving gate.
        if (colliders != null)
        {
            for (int i = 0;
                 i < colliders.Length;
                 i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }

        routine = null;
    }

    private void ApplyChargeVisual(
        float pulse)
    {
        if (runeFrames != null)
        {
            for (int i = 0;
                 i < runeFrames.Length;
                 i++)
            {
                LineRenderer frame =
                    runeFrames[i];

                if (frame == null)
                    continue;

                Color frameColor =
                    Color.Lerp(
                        new Color(
                            0.10f,
                            0.72f,
                            0.96f,
                            0.88f),
                        Color.white,
                        pulse * 0.80f);

                frame.startColor = frameColor;
                frame.endColor = frameColor;
                frame.startWidth =
                    0.040f + pulse * 0.060f;
                frame.endWidth =
                    frame.startWidth;
            }
        }

        if (renderers == null)
            return;

        for (int i = 0;
             i < renderers.Length;
             i++)
        {
            SpriteRenderer renderer =
                renderers[i];

            if (renderer == null)
                continue;

            if (renderer.gameObject.name.Contains("Glow"))
            {
                renderer.color =
                    new Color(
                        0.12f,
                        0.88f,
                        1f,
                        0.18f + pulse * 0.30f);
                continue;
            }

            renderer.color =
                Color.Lerp(
                    new Color(
                        0.20f,
                        0.31f,
                        0.44f,
                        1f),
                    new Color(
                        0.52f,
                        0.90f,
                        1f,
                        1f),
                    pulse * 0.38f);
        }
    }

    private void ApplyOpeningVisual(
        float t)
    {
        if (renderers == null)
            return;

        float glow =
            1f - Mathf.Clamp01(t);

        for (int i = 0;
             i < renderers.Length;
             i++)
        {
            SpriteRenderer renderer =
                renderers[i];

            if (renderer == null ||
                !renderer.gameObject.name.Contains("Glow"))
            {
                continue;
            }

            renderer.color =
                new Color(
                    0.14f,
                    0.90f,
                    1f,
                    0.12f + glow * 0.22f);
        }
    }
}
