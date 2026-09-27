using UnityEngine;

/// <summary>
/// Character-specific ballistic aim curve.
///
/// Nerissa gets this guide because her projectile is physically affected by
/// gravity. In Crystal Caverns the guide also understands the first Prism
/// Crystal it intersects: the ordinary green arc stops at the prism and a
/// purple/cyan continuation shows the redirected ballistic path.
/// </summary>
public sealed class CharacterGravityAimPreview : MonoBehaviour
{
    private BowController bow;
    private ArrowController arrow;
    private ArcherGameplayTraitProfile trait;
    private GameConfig config;

    private LineRenderer line;
    private Material lineMaterial;

    private LineRenderer prismLine;
    private Material prismLineMaterial;

    private bool aiming;
    private Vector2 aimDirection =
        Vector2.right;

    private readonly Vector3[] basePoints =
        new Vector3[64];

    private readonly Vector3[] prismPoints =
        new Vector3[64];

    public void Bind(
        BowController bowController)
    {
        if (bow == bowController)
            return;

        Unsubscribe();

        bow = bowController;

        if (bow == null)
            return;

        bow.AimStarted +=
            OnAimStarted;

        bow.AimChanged +=
            OnAimChanged;

        bow.AimReleased +=
            OnAimReleased;

        bow.AimCancelled +=
            OnAimCancelled;
    }

    public void Configure(
        ArrowController currentArrow,
        ArcherGameplayTraitProfile gameplayTrait,
        GameConfig gameConfig)
    {
        arrow = currentArrow;
        trait = gameplayTrait;

        config =
            gameConfig != null
                ? gameConfig
                : GameConfig.Load();

        aiming = false;

        EnsureLine();
        EnsurePrismLine();
        ApplyStyle();
        Hide();
    }

    public void ClearArrow()
    {
        arrow = null;
        aiming = false;
        Hide();
    }

    private void OnAimStarted()
    {
        aiming = true;
    }

    private void OnAimChanged(
        Vector2 direction)
    {
        if (direction.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        aimDirection =
            direction.normalized;

        aiming = true;
    }

    private void OnAimReleased(
        Vector2 ignoredDirection)
    {
        aiming = false;
        Hide();
    }

    private void OnAimCancelled()
    {
        aiming = false;
        Hide();
    }

    private void LateUpdate()
    {
        if (!ShouldRender())
        {
            Hide();
            return;
        }

        RenderCurve();
    }

    private bool ShouldRender()
    {
        return
            aiming &&
            arrow != null &&
            !arrow.HasFired &&
            trait != null &&
            trait.UsesGravityArc &&
            trait.ShowAimCurve &&
            config != null;
    }

    private void RenderCurve()
    {
        EnsureLine();
        EnsurePrismLine();

        int samples =
            Mathf.Clamp(
                trait.AimCurveSamples,
                8,
                64);

        Vector2 origin =
            arrow.transform.position;

        float fallbackSpeed =
            Mathf.Max(
                0.01f,
                config.ArrowSpeed);

        float drawAmount =
            bow != null
                ? bow.CurrentDrawAmount
                : 1f;

        float speed =
            trait.EvaluateLaunchSpeed(
                drawAmount,
                fallbackSpeed);

        Vector2 initialVelocity =
            aimDirection * speed;

        Vector2 gravity =
            Physics2D.gravity *
            trait.GravityScale;

        float duration =
            Mathf.Clamp(
                trait.AimCurveDuration,
                0.4f,
                3f);

        int baseCount = 0;
        bool prismHit = false;
        PrismCrystalRuntime hitPrism = null;
        Vector2 prismHitPoint = default;
        float prismHitTime = 0f;
        Vector2 prismIncomingVelocity = default;

        Vector2 previousPosition = origin;
        float previousTime = 0f;

        basePoints[baseCount++] =
            new Vector3(
                origin.x,
                origin.y,
                arrow.transform.position.z);

        for (int i = 1;
             i < samples;
             i++)
        {
            float normalized =
                i /
                (float)(samples - 1);

            float t =
                duration * normalized;

            Vector2 position =
                origin +
                initialVelocity * t +
                0.5f * gravity * t * t;

            if (PrismCrystalRuntime
                .TryFindFirstPreviewHit(
                    previousPosition,
                    position,
                    out PrismCrystalRuntime prism,
                    out Vector2 hitPoint,
                    out float segmentT))
            {
                float hitTime =
                    Mathf.Lerp(
                        previousTime,
                        t,
                        segmentT);

                basePoints[baseCount++] =
                    new Vector3(
                        hitPoint.x,
                        hitPoint.y,
                        arrow.transform.position.z);

                prismHit = true;
                hitPrism = prism;
                prismHitPoint = hitPoint;
                prismHitTime = hitTime;
                prismIncomingVelocity =
                    initialVelocity +
                    gravity * hitTime;

                break;
            }

            basePoints[baseCount++] =
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z);

            previousPosition = position;
            previousTime = t;
        }

        line.positionCount =
            baseCount;

        for (int i = 0;
             i < baseCount;
             i++)
        {
            line.SetPosition(
                i,
                basePoints[i]);
        }

        line.enabled =
            baseCount > 1;

        if (!prismHit ||
            hitPrism == null)
        {
            HidePrismContinuation();
            PrismCrystalRuntime.ClearPreviewFocus();
            return;
        }

        PrismCrystalRuntime.SetPreviewFocus(
            hitPrism);

        Vector2 outgoingVelocity =
            hitPrism.GetPreviewOutgoingVelocity(
                prismIncomingVelocity);

        // Give the redirected preview its own readable time window. The old
        // version reused only the small amount of the original aim-duration
        // remaining after the prism contact, which often made the purple path
        // too short to reach the target on screen.
        float continuationDuration =
            Mathf.Clamp(
                duration * 0.92f,
                0.95f,
                1.45f);

        if (outgoingVelocity.sqrMagnitude < 0.0001f)
        {
            HidePrismContinuation();
            return;
        }

        int continuationSamples =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    samples * 0.86f),
                18,
                64);

        for (int i = 0;
             i < continuationSamples;
             i++)
        {
            float normalized =
                continuationSamples <= 1
                    ? 0f
                    : i /
                      (float)(continuationSamples - 1);

            float t =
                continuationDuration *
                normalized;

            Vector2 position =
                prismHitPoint +
                outgoingVelocity * t +
                0.5f * gravity * t * t;

            prismPoints[i] =
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z);
        }

        prismLine.positionCount =
            continuationSamples;

        for (int i = 0;
             i < continuationSamples;
             i++)
        {
            prismLine.SetPosition(
                i,
                prismPoints[i]);
        }

        prismLine.enabled = true;
    }

    private void EnsureLine()
    {
        if (line != null)
            return;

        Transform existing =
            transform.Find(
                "CharacterGravityAimCurve");

        GameObject lineObject;

        if (existing != null)
        {
            lineObject =
                existing.gameObject;
        }
        else
        {
            lineObject =
                new GameObject(
                    "CharacterGravityAimCurve");

            lineObject.transform.SetParent(
                transform,
                false);
        }

        line =
            lineObject.GetComponent<LineRenderer>();

        if (line == null)
        {
            line =
                lineObject.AddComponent<LineRenderer>();
        }

        if (lineMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "Sprites/Default");

            if (shader != null)
            {
                lineMaterial =
                    new Material(shader)
                    {
                        name =
                            "RuntimeCharacterGravityAimMaterial",
                        hideFlags =
                            HideFlags.DontSave
                    };
            }
        }

        if (lineMaterial != null)
        {
            line.sharedMaterial =
                lineMaterial;
        }

        line.useWorldSpace = true;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.textureMode =
            LineTextureMode.Stretch;
        line.sortingOrder = 20;
        line.enabled = false;
    }

    private void EnsurePrismLine()
    {
        if (prismLine != null)
            return;

        Transform existing =
            transform.Find(
                "CharacterPrismAimContinuation");

        GameObject lineObject;

        if (existing != null)
        {
            lineObject =
                existing.gameObject;
        }
        else
        {
            lineObject =
                new GameObject(
                    "CharacterPrismAimContinuation");

            lineObject.transform.SetParent(
                transform,
                false);
        }

        prismLine =
            lineObject.GetComponent<LineRenderer>();

        if (prismLine == null)
        {
            prismLine =
                lineObject.AddComponent<LineRenderer>();
        }

        if (prismLineMaterial == null)
        {
            Shader shader =
                Shader.Find("Sprites/Default");

            if (shader != null)
            {
                prismLineMaterial =
                    new Material(shader)
                    {
                        name =
                            "RuntimeCharacterPrismAimMaterial",
                        hideFlags =
                            HideFlags.DontSave
                    };
            }
        }

        if (prismLineMaterial != null)
        {
            prismLine.sharedMaterial =
                prismLineMaterial;
        }

        prismLine.useWorldSpace = true;
        prismLine.numCornerVertices = 3;
        prismLine.numCapVertices = 3;
        prismLine.textureMode =
            LineTextureMode.Stretch;
        prismLine.sortingOrder = 21;
        prismLine.enabled = false;
    }

    private void ApplyStyle()
    {
        if (line == null ||
            trait == null)
        {
            return;
        }

        float width =
            Mathf.Clamp(
                trait.AimCurveWidth,
                0.005f,
                0.12f);

        line.startWidth = width;
        line.endWidth =
            width * 0.58f;

        Color start =
            trait.AimCurveColor;

        Color end =
            trait.AimCurveColor;

        end.a *= 0.18f;

        line.startColor = start;
        line.endColor = end;

        if (prismLine != null)
        {
            // The post-prism path is intentionally much stronger than the
            // ordinary aim curve. Green means "before prism"; violet/cyan
            // means "after prism" and must remain obvious on a bright cavern
            // background at phone resolution.
            prismLine.startWidth =
                width * 2.35f;

            prismLine.endWidth =
                width * 1.10f;

            prismLine.startColor =
                new Color(
                    0.96f,
                    0.66f,
                    1f,
                    1f);

            prismLine.endColor =
                new Color(
                    0.24f,
                    0.98f,
                    1f,
                    0.62f);
        }
    }

    private void HidePrismContinuation()
    {
        if (prismLine != null)
        {
            prismLine.enabled = false;
            prismLine.positionCount = 0;
        }
    }

    private void Hide()
    {
        if (line != null)
            line.enabled = false;

        HidePrismContinuation();
        PrismCrystalRuntime.ClearPreviewFocus();
    }

    private void Unsubscribe()
    {
        if (bow == null)
            return;

        bow.AimStarted -=
            OnAimStarted;

        bow.AimChanged -=
            OnAimChanged;

        bow.AimReleased -=
            OnAimReleased;

        bow.AimCancelled -=
            OnAimCancelled;
    }

    private void OnDestroy()
    {
        Unsubscribe();

        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }

        if (prismLineMaterial != null)
        {
            Destroy(prismLineMaterial);
            prismLineMaterial = null;
        }
    }
}
