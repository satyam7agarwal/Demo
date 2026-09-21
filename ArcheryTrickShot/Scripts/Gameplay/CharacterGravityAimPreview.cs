using UnityEngine;

/// <summary>
/// Character-specific ballistic aim curve.
///
/// Nerissa gets this guide because her projectile is physically affected by
/// gravity. It does not modify the existing mirror-aware FULL PATH renderer.
/// </summary>
public sealed class CharacterGravityAimPreview : MonoBehaviour
{
    private BowController bow;
    private ArrowController arrow;
    private ArcherGameplayTraitProfile trait;
    private GameConfig config;

    private LineRenderer line;
    private Material lineMaterial;

    private bool aiming;
    private Vector2 aimDirection =
        Vector2.right;

    public void Bind(
        BowController bowController)
    {
        if (bow == bowController)
            return;

        Unsubscribe();

        bow =
            bowController;

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
        arrow =
            currentArrow;

        trait =
            gameplayTrait;

        config =
            gameConfig != null
                ? gameConfig
                : GameConfig.Load();

        aiming =
            false;

        EnsureLine();
        ApplyStyle();
        Hide();
    }

    public void ClearArrow()
    {
        arrow =
            null;

        aiming =
            false;

        Hide();
    }

    private void OnAimStarted()
    {
        aiming =
            true;
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

        aiming =
            true;
    }

    private void OnAimReleased(
        Vector2 ignoredDirection)
    {
        aiming =
            false;

        Hide();
    }

    private void OnAimCancelled()
    {
        aiming =
            false;

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

        int samples =
            Mathf.Clamp(
                trait.AimCurveSamples,
                8,
                64);

        line.positionCount =
            samples;

        line.enabled =
            true;

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
            aimDirection *
            speed;

        Vector2 gravity =
            Physics2D.gravity *
            trait.GravityScale;

        float duration =
            Mathf.Clamp(
                trait.AimCurveDuration,
                0.4f,
                3f);

        for (int i = 0;
             i < samples;
             i++)
        {
            float normalized =
                samples <= 1
                    ? 0f
                    : i /
                      (float)(
                          samples - 1);

            float t =
                duration *
                normalized;

            Vector2 position =
                origin +
                initialVelocity *
                    t +
                0.5f *
                gravity *
                t *
                t;

            line.SetPosition(
                i,
                new Vector3(
                    position.x,
                    position.y,
                    arrow.transform.position.z));
        }
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
                    new Material(
                        shader)
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

        line.useWorldSpace =
            true;

        line.numCornerVertices =
            2;

        line.numCapVertices =
            2;

        line.textureMode =
            LineTextureMode.Stretch;

        line.sortingOrder =
            20;

        line.enabled =
            false;
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

        line.startWidth =
            width;

        line.endWidth =
            width *
            0.58f;

        Color start =
            trait.AimCurveColor;

        Color end =
            trait.AimCurveColor;

        end.a *=
            0.18f;

        line.startColor =
            start;

        line.endColor =
            end;
    }

    private void Hide()
    {
        if (line != null)
        {
            line.enabled =
                false;
        }
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
            Destroy(
                lineMaterial);

            lineMaterial =
                null;
        }
    }
}
