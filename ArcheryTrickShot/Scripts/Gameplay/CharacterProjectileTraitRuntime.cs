using UnityEngine;

/// <summary>
/// Applies character-owned projectile behaviour to the existing ArrowController.
///
/// ArrowController remains authoritative for firing, collision and reflection.
/// This component layers character traits on top:
/// - draw strength changes launch speed;
/// - gravity begins at Shot;
/// - chosen launch speed is preserved through mirror ricochets;
/// - the visible arrow follows its instantaneous velocity.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class CharacterProjectileTraitRuntime : MonoBehaviour
{
    private ArrowController arrow;
    private Rigidbody2D body;
    private BowController bow;
    private ArcherGameplayTraitProfile trait;

    private float activeLaunchSpeed;
    private bool subscribed;

    public bool UsesGravityArc =>
        trait != null &&
        trait.UsesGravityArc;

    public void Configure(
        ArrowController arrowController,
        ArcherGameplayTraitProfile gameplayTrait,
        BowController bowController)
    {
        DetachListeners();

        arrow =
            arrowController;

        trait =
            gameplayTrait;

        bow =
            bowController;

        activeLaunchSpeed =
            0f;

        if (body == null)
        {
            body =
                GetComponent<Rigidbody2D>();
        }

        if (body != null)
        {
            // A held/pooled arrow must never sag before release.
            body.gravityScale =
                0f;
        }

        bool active =
            arrow != null &&
            trait != null &&
            (trait.UsesGravityArc ||
             trait.UsesDrawStrength);

        enabled =
            active;

        if (!active)
            return;

        arrow.Shot +=
            OnArrowShot;

        arrow.Reflected +=
            OnArrowReflected;

        subscribed =
            true;
    }

    private void OnArrowShot()
    {
        if (body == null ||
            trait == null)
        {
            return;
        }

        Vector2 currentVelocity =
            body.linearVelocity;

        float fallbackSpeed =
            currentVelocity.magnitude;

        if (fallbackSpeed < 0.01f)
        {
            fallbackSpeed =
                12f;
        }

        float drawAmount =
            bow != null
                ? bow.CurrentDrawAmount
                : 1f;

        activeLaunchSpeed =
            trait.EvaluateLaunchSpeed(
                drawAmount,
                fallbackSpeed);

        if (currentVelocity.sqrMagnitude >
            0.0001f)
        {
            body.linearVelocity =
                currentVelocity.normalized *
                activeLaunchSpeed;
        }

        body.gravityScale =
            trait.UsesGravityArc
                ? trait.GravityScale
                : 0f;
    }

    private void OnArrowReflected(
        int ignoredChainCount,
        Collider2D ignoredMirror,
        Vector2 ignoredContactPoint)
    {
        if (body == null ||
            trait == null ||
            activeLaunchSpeed <= 0.01f)
        {
            return;
        }

        // ArrowController performs the correct mirror geometry first.
        // It historically restores its configured base speed after reflection.
        // Re-apply this character's actual draw-derived speed immediately so
        // Nerissa does not magically gain/lose power at a mirror.
        Vector2 velocity =
            body.linearVelocity;

        if (velocity.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        body.linearVelocity =
            velocity.normalized *
            activeLaunchSpeed;
    }

    private void LateUpdate()
    {
        if (arrow == null ||
            body == null ||
            trait == null ||
            !trait.AlignArrowToVelocity ||
            !arrow.HasFired ||
            arrow.IsStopped)
        {
            return;
        }

        Vector2 velocity =
            body.linearVelocity;

        if (velocity.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        float angle =
            Mathf.Atan2(
                velocity.y,
                velocity.x) *
            Mathf.Rad2Deg;

        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                angle);
    }

    private void OnDisable()
    {
        DetachListeners();
    }

    private void OnDestroy()
    {
        DetachListeners();
    }

    private void DetachListeners()
    {
        if (!subscribed ||
            arrow == null)
        {
            subscribed =
                false;

            return;
        }

        arrow.Shot -=
            OnArrowShot;

        arrow.Reflected -=
            OnArrowReflected;

        subscribed =
            false;
    }
}
