using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PrismCrystalRuntime : MonoBehaviour
{
    private static readonly List<PrismCrystalRuntime> ActivePrisms =
        new List<PrismCrystalRuntime>();

    private float exitAngle;
    private float speedMultiplier;
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D trigger;
    private int lastArrowId = int.MinValue;
    private float lastRedirectTime = -999f;

    private LineRenderer halo;
    private LineRenderer exitGuide;
    private LineRenderer exitArrowHead;
    private SpriteRenderer glowRenderer;
    private Material lineMaterial;
    private bool previewFocus;
    private Coroutine pulseRoutine;

    public void Configure(
        float worldExitAngle,
        float multiplier,
        SpriteRenderer renderer)
    {
        exitAngle = worldExitAngle;
        speedMultiplier = Mathf.Clamp(multiplier, 0.5f, 1.5f);
        spriteRenderer = renderer;
        trigger = GetComponent<CircleCollider2D>();

        EnsureVisualLanguage();
        ApplyVisualState();
    }

    private void OnEnable()
    {
        if (!ActivePrisms.Contains(this))
            ActivePrisms.Add(this);
    }

    private void OnDisable()
    {
        ActivePrisms.Remove(this);
    }

    private void Update()
    {
        if (halo == null)
            return;

        float wave =
            0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f);

        Color haloColor = previewFocus
            ? new Color(0.82f, 0.44f, 1f, 0.96f)
            : new Color(0.54f, 0.30f, 1f, 0.52f + wave * 0.10f);

        halo.startColor = haloColor;
        halo.endColor = haloColor;

        if (exitGuide != null)
        {
            exitGuide.startColor = previewFocus
                ? new Color(0.88f, 0.46f, 1f, 0.96f)
                : new Color(0.74f, 0.38f, 1f, 0.64f);

            exitGuide.endColor = previewFocus
                ? new Color(0.30f, 0.96f, 1f, 0.34f)
                : new Color(0.24f, 0.86f, 1f, 0.12f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        ArrowController arrow =
            other.GetComponentInParent<ArrowController>();

        if (arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        int id = arrow.GetInstanceID();

        if (id == lastArrowId &&
            Time.time - lastRedirectTime < 0.18f)
        {
            return;
        }

        lastArrowId = id;
        lastRedirectTime = Time.time;

        Vector2 incoming = arrow.GetVelocity();
        Vector2 outgoing = GetPreviewOutgoingVelocity(incoming);

        if (outgoing.sqrMagnitude < 0.0001f)
            return;

        // The prism redirects only the velocity vector. Rigidbody gravityScale
        // is deliberately untouched, so Nerissa resumes her natural ballistic
        // drop immediately after leaving the prism.
        arrow.Reflect(outgoing.normalized);

        Rigidbody2D body =
            arrow.GetComponent<Rigidbody2D>();

        if (body != null)
            body.linearVelocity = outgoing;

        GameAudioController.Instance?.PlayTargetHit();
        ATSHaptics.Pulse();

        if (pulseRoutine != null)
            StopCoroutine(pulseRoutine);

        pulseRoutine = StartCoroutine(PulseRoutine());
    }

    public Vector2 GetPreviewOutgoingVelocity(
        Vector2 incomingVelocity)
    {
        float speed =
            Mathf.Max(
                0.01f,
                incomingVelocity.magnitude) *
            speedMultiplier;

        float radians =
            exitAngle * Mathf.Deg2Rad;

        Vector2 direction =
            new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians));

        return direction.normalized * speed;
    }

    public static bool TryFindFirstPreviewHit(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out PrismCrystalRuntime prism,
        out Vector2 hitPoint,
        out float segmentT)
    {
        prism = null;
        hitPoint = default;
        segmentT = float.PositiveInfinity;

        for (int i = ActivePrisms.Count - 1;
             i >= 0;
             i--)
        {
            PrismCrystalRuntime candidate =
                ActivePrisms[i];

            if (candidate == null ||
                !candidate.isActiveAndEnabled)
            {
                ActivePrisms.RemoveAt(i);
                continue;
            }

            if (!candidate.TryGetPreviewHit(
                    segmentStart,
                    segmentEnd,
                    out Vector2 candidatePoint,
                    out float candidateT))
            {
                continue;
            }

            if (candidateT >= segmentT)
                continue;

            prism = candidate;
            hitPoint = candidatePoint;
            segmentT = candidateT;
        }

        return prism != null;
    }

    public static void SetPreviewFocus(
        PrismCrystalRuntime focused)
    {
        for (int i = ActivePrisms.Count - 1;
             i >= 0;
             i--)
        {
            PrismCrystalRuntime prism =
                ActivePrisms[i];

            if (prism == null)
            {
                ActivePrisms.RemoveAt(i);
                continue;
            }

            prism.previewFocus =
                prism == focused;
        }
    }

    public static void ClearPreviewFocus()
    {
        SetPreviewFocus(null);
    }

    private bool TryGetPreviewHit(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        out Vector2 hitPoint,
        out float segmentT)
    {
        hitPoint = default;
        segmentT = 0f;

        Vector2 d =
            segmentEnd - segmentStart;

        float a =
            Vector2.Dot(d, d);

        if (a < 0.000001f)
            return false;

        Vector2 center =
            transform.position;

        float radius =
            ResolveWorldTriggerRadius();

        Vector2 f =
            segmentStart - center;

        float c =
            Vector2.Dot(f, f) -
            radius * radius;

        if (c <= 0f)
        {
            hitPoint = segmentStart;
            segmentT = 0f;
            return true;
        }

        float b =
            2f * Vector2.Dot(f, d);

        float discriminant =
            b * b -
            4f * a * c;

        if (discriminant < 0f)
            return false;

        float root =
            Mathf.Sqrt(discriminant);

        float t0 =
            (-b - root) /
            (2f * a);

        float t1 =
            (-b + root) /
            (2f * a);

        float resolved =
            t0 >= 0f && t0 <= 1f
                ? t0
                : t1 >= 0f && t1 <= 1f
                    ? t1
                    : -1f;

        if (resolved < 0f)
            return false;

        segmentT = resolved;
        hitPoint =
            Vector2.Lerp(
                segmentStart,
                segmentEnd,
                resolved);

        return true;
    }

    private float ResolveWorldTriggerRadius()
    {
        if (trigger == null)
            trigger = GetComponent<CircleCollider2D>();

        float scale =
            Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y));

        float radius =
            trigger != null
                ? trigger.radius * scale
                : 0.52f;

        return Mathf.Max(0.28f, radius);
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
                        name = "RuntimePrismCrystalLines",
                        hideFlags = HideFlags.DontSave
                    };
            }
        }

        EnsureGlow();
        EnsureHalo();
        EnsureExitGuide();
    }

    private void EnsureGlow()
    {
        if (spriteRenderer == null ||
            spriteRenderer.sprite == null)
        {
            return;
        }

        Transform existing =
            transform.Find("PrismGlow");

        if (existing != null)
        {
            glowRenderer =
                existing.GetComponent<SpriteRenderer>();
        }

        if (glowRenderer == null)
        {
            GameObject glow =
                new GameObject(
                    "PrismGlow",
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

        glowRenderer.color =
            new Color(
                0.55f,
                0.32f,
                1f,
                0.22f);

        glowRenderer.transform.localScale =
            Vector3.one * 1.28f;
    }

    private void EnsureHalo()
    {
        if (halo != null)
            return;

        GameObject haloObject =
            new GameObject("PrismInteractiveHalo");

        haloObject.transform.SetParent(
            transform,
            false);

        halo =
            haloObject.AddComponent<LineRenderer>();

        halo.sharedMaterial = lineMaterial;
        halo.useWorldSpace = false;
        halo.loop = true;
        halo.positionCount = 40;
        halo.startWidth = 0.055f;
        halo.endWidth = 0.055f;
        halo.sortingOrder =
            spriteRenderer != null
                ? spriteRenderer.sortingOrder - 1
                : 7;

        const float radius = 1.18f;

        for (int i = 0; i < 40; i++)
        {
            float angle =
                i /
                40f *
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

    private void EnsureExitGuide()
    {
        if (exitGuide != null)
            return;

        GameObject guide =
            new GameObject("PrismExitDirection");

        guide.transform.SetParent(
            transform.parent,
            false);

        exitGuide =
            guide.AddComponent<LineRenderer>();

        exitGuide.sharedMaterial = lineMaterial;
        exitGuide.useWorldSpace = true;
        exitGuide.positionCount = 2;
        exitGuide.startWidth = 0.095f;
        exitGuide.endWidth = 0.030f;
        exitGuide.numCapVertices = 3;
        exitGuide.sortingOrder = 9;

        GameObject head =
            new GameObject("PrismExitArrowHead");

        head.transform.SetParent(
            transform.parent,
            false);

        exitArrowHead =
            head.AddComponent<LineRenderer>();

        exitArrowHead.sharedMaterial =
            lineMaterial;

        exitArrowHead.useWorldSpace = true;
        exitArrowHead.positionCount = 3;
        exitArrowHead.startWidth = 0.060f;
        exitArrowHead.endWidth = 0.060f;
        exitArrowHead.sortingOrder = 9;
        exitArrowHead.startColor =
            new Color(0.38f, 0.93f, 1f, 0.82f);
        exitArrowHead.endColor =
            new Color(0.38f, 0.93f, 1f, 0.82f);

        RebuildExitGuideGeometry();
    }

    private void RebuildExitGuideGeometry()
    {
        if (exitGuide == null)
            return;

        float radians =
            exitAngle * Mathf.Deg2Rad;

        Vector2 direction =
            new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians)).normalized;

        Vector2 normal =
            new Vector2(
                -direction.y,
                direction.x);

        Vector2 origin =
            transform.position;

        Vector2 start =
            origin + direction * 0.46f;

        Vector2 end =
            origin + direction * 1.72f;

        exitGuide.SetPosition(
            0,
            new Vector3(start.x, start.y, -0.14f));

        exitGuide.SetPosition(
            1,
            new Vector3(end.x, end.y, -0.14f));

        if (exitArrowHead == null)
            return;

        Vector2 headBase =
            end - direction * 0.28f;

        exitArrowHead.SetPosition(
            0,
            new Vector3(
                headBase.x + normal.x * 0.16f,
                headBase.y + normal.y * 0.16f,
                -0.14f));

        exitArrowHead.SetPosition(
            1,
            new Vector3(end.x, end.y, -0.14f));

        exitArrowHead.SetPosition(
            2,
            new Vector3(
                headBase.x - normal.x * 0.16f,
                headBase.y - normal.y * 0.16f,
                -0.14f));
    }

    private void ApplyVisualState()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color =
                new Color(
                    1f,
                    1f,
                    1f,
                    1f);
        }

        RebuildExitGuideGeometry();
    }

    private IEnumerator PulseRoutine()
    {
        if (spriteRenderer == null)
            yield break;

        Vector3 originalScale =
            transform.localScale;

        Color original =
            spriteRenderer.color;

        const float duration = 0.30f;
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
                (1f + pulse * 0.20f);

            spriteRenderer.color =
                Color.Lerp(
                    original,
                    Color.white,
                    pulse);

            if (halo != null)
            {
                halo.startWidth =
                    0.055f + pulse * 0.085f;

                halo.endWidth =
                    halo.startWidth;
            }

            yield return null;
        }

        transform.localScale =
            originalScale;

        spriteRenderer.color =
            original;

        if (halo != null)
        {
            halo.startWidth = 0.055f;
            halo.endWidth = 0.055f;
        }

        pulseRoutine = null;
    }

    private void OnDestroy()
    {
        ActivePrisms.Remove(this);

        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }

        if (exitGuide != null)
            Destroy(exitGuide.gameObject);

        if (exitArrowHead != null)
            Destroy(exitArrowHead.gameObject);
    }
}
