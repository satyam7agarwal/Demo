using System;
using System.Collections;
using UnityEngine;

public readonly struct FlyingBirdHitResult
{
    public readonly Vector2 WorldPoint;
    public readonly string UnlockGroupId;
    public readonly bool ConsumesArrow;

    public FlyingBirdHitResult(
        Vector2 worldPoint,
        string unlockGroupId,
        bool consumesArrow)
    {
        WorldPoint = worldPoint;
        UnlockGroupId =
            unlockGroupId ?? string.Empty;
        ConsumesArrow =
            consumesArrow;
    }
}

/// <summary>
/// Deterministic moving guardian target. It never uses random movement.
/// Its unlock group and arrow-consumption behavior are fully data-driven.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CapsuleCollider2D))]
public sealed class FlyingBirdTarget : MonoBehaviour
{
    public event Action<FlyingBirdHitResult> Hit;

    private SpriteRenderer birdRenderer;
    private CapsuleCollider2D triggerCollider;

    private Sprite wingUpSprite;
    private Sprite wingDownSprite;

    private Vector3 authoredPosition;
    private Vector3 authoredScale;

    private float patrolWidth;
    private float patrolSpeed;
    private float bobAmplitude;
    private float bobSpeed;
    private float movementStartTime;

    private string unlockGroupId;
    private bool consumeArrowOnHit;

    private bool consumed;
    private Coroutine hitRoutine;

    public void Configure(
        Sprite wingUp,
        Sprite wingDown,
        float configuredPatrolWidth,
        float configuredPatrolSpeed,
        float configuredBobAmplitude,
        float configuredBobSpeed,
        string configuredUnlockGroupId,
        bool configuredConsumeArrowOnHit)
    {
        birdRenderer ??=
            GetComponent<SpriteRenderer>();

        triggerCollider ??=
            GetComponent<CapsuleCollider2D>();

        wingUpSprite =
            wingUp;

        wingDownSprite =
            wingDown != null
                ? wingDown
                : wingUp;

        authoredPosition =
            transform.position;

        authoredScale =
            transform.localScale;

        patrolWidth =
            Mathf.Max(
                0f,
                configuredPatrolWidth);

        patrolSpeed =
            Mathf.Max(
                0.1f,
                configuredPatrolSpeed);

        bobAmplitude =
            Mathf.Max(
                0f,
                configuredBobAmplitude);

        bobSpeed =
            Mathf.Max(
                0.1f,
                configuredBobSpeed);

        unlockGroupId =
            configuredUnlockGroupId != null
                ? configuredUnlockGroupId.Trim()
                : string.Empty;

        consumeArrowOnHit =
            configuredConsumeArrowOnHit;

        movementStartTime =
            Time.unscaledTime;

        if (birdRenderer != null)
        {
            birdRenderer.sprite =
                wingUpSprite;

            birdRenderer.sortingOrder =
                12;
        }

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger =
                true;

            triggerCollider.direction =
                CapsuleDirection2D.Horizontal;

            triggerCollider.size =
                new Vector2(
                    1.48f,
                    0.72f);
        }
    }

    private void Update()
    {
        if (consumed)
            return;

        float t =
            Time.unscaledTime -
            movementStartTime;

        float horizontal =
            Mathf.Sin(
                t *
                patrolSpeed) *
            patrolWidth *
            0.5f;

        float vertical =
            Mathf.Sin(
                t *
                bobSpeed) *
            bobAmplitude;

        transform.position =
            authoredPosition +
            new Vector3(
                horizontal,
                vertical,
                0f);

        if (birdRenderer != null &&
            wingUpSprite != null)
        {
            bool wingsUp =
                Mathf.Sin(
                    t * 8.5f) >= 0f;

            birdRenderer.sprite =
                wingsUp
                    ? wingUpSprite
                    : wingDownSprite;

            birdRenderer.flipX =
                Mathf.Cos(
                    t *
                    patrolSpeed) < 0f;
        }

        float breathe =
            1f +
            Mathf.Sin(
                t * 3.1f) *
            0.025f;

        transform.localScale =
            authoredScale *
            breathe;
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

        consumed =
            true;

        if (triggerCollider != null)
            triggerCollider.enabled = false;

        if (consumeArrowOnHit)
            arrow.Stop();

        Vector2 point =
            transform.position;

        Hit?.Invoke(
            new FlyingBirdHitResult(
                point,
                unlockGroupId,
                consumeArrowOnHit));

        if (hitRoutine != null)
            StopCoroutine(hitRoutine);

        hitRoutine =
            StartCoroutine(
                HitReaction());
    }

    private IEnumerator HitReaction()
    {
        Vector3 startPosition =
            transform.position;

        Vector3 startScale =
            transform.localScale;

        Color startColor =
            birdRenderer != null
                ? birdRenderer.color
                : Color.white;

        float elapsed = 0f;
        const float duration = 0.72f;

        while (elapsed < duration)
        {
            float dt =
                Time.unscaledDeltaTime;

            elapsed += dt;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            transform.position =
                startPosition +
                new Vector3(
                    0.55f * t,
                    -1.35f *
                    t *
                    t,
                    0f);

            transform.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(
                        0f,
                        -42f,
                        t));

            float punch =
                1f +
                Mathf.Sin(
                    Mathf.Min(
                        1f,
                        t / 0.24f) *
                    Mathf.PI) *
                0.30f;

            transform.localScale =
                startScale *
                punch;

            if (birdRenderer != null)
            {
                Color c =
                    startColor;

                c.a =
                    1f -
                    Mathf.Clamp01(
                        (t - 0.45f) /
                        0.55f);

                birdRenderer.color =
                    c;
            }

            yield return null;
        }

        hitRoutine = null;
        Destroy(gameObject);
    }

    private void OnDisable()
    {
        if (hitRoutine != null)
        {
            StopCoroutine(hitRoutine);
            hitRoutine = null;
        }
    }
}
