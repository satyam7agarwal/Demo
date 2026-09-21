using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Visual-only nock seating correction for standard Humanoid archers.
///
/// BowController already aligns the mathematical tail of the gameplay arrow to
/// the real bow-string nock. The authored 2D arrow sprite has a small visual
/// inset at its rear, so the visible tail can still appear a few pixels in
/// front of the string.
///
/// This component:
/// - runs only for HumanoidAutoFingerSockets characters (Nerissa/future Humanoids);
/// - moves only the SpriteRenderer visual while the arrow is HELD;
/// - does not move the ArrowController/Rigidbody/collider;
/// - does not alter gameplay aim, trajectory, draw strength or release physics;
/// - restores the sprite before BowController performs the next pose alignment,
///   preventing any frame-to-frame accumulation.
///
/// It installs itself automatically on BowController objects, so no Inspector
/// setup is required.
/// </summary>
[DefaultExecutionOrder(1300)]
[DisallowMultipleComponent]
public sealed class HumanoidArrowNockSeatVisual : MonoBehaviour
{
    // From the recorded Level01 result, the remaining visible separation is
    // approximately five percent of the rendered arrow length. Using a fraction
    // instead of a fixed world-space offset keeps the correction proportional
    // if the arrow art/scale changes.
    private const float NockSeatFraction = 0.055f;

    private BowController bowController;
    private Archer3DVisualController archerVisual;

    private ArrowController trackedArrow;
    private SpriteRenderer trackedSprite;

    private Vector3 spriteBaseLocalPosition;
    private bool visualOffsetApplied;

    private static bool sceneHookInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
            return;

        SceneManager.sceneLoaded +=
            OnSceneLoaded;

        sceneHookInstalled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallOnBowControllers();
    }

    private static void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        InstallOnBowControllers();
    }

    private static void InstallOnBowControllers()
    {
        BowController[] bows =
            Object.FindObjectsByType<BowController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (BowController bow in bows)
        {
            if (bow == null)
                continue;

            HumanoidArrowNockSeatVisual existing =
                bow.GetComponent<HumanoidArrowNockSeatVisual>();

            if (existing == null)
            {
                existing =
                    bow.gameObject.AddComponent<
                        HumanoidArrowNockSeatVisual>();
            }

            existing.Configure(
                bow);
        }
    }

    private void Configure(
        BowController owner)
    {
        bowController = owner;
        ResolveArcherVisual();
    }

    private void Awake()
    {
        if (bowController == null)
        {
            bowController =
                GetComponent<BowController>();
        }

        ResolveArcherVisual();
    }

    private void Update()
    {
        // BowController's next LateUpdate alignment must always see the arrow
        // in its canonical/unmodified visual position.
        RestoreSpritePosition();

        if (bowController == null)
        {
            bowController =
                GetComponent<BowController>();

            if (bowController == null)
                return;
        }

        if (!bowController.IsAiming)
        {
            if (trackedArrow != null &&
                trackedArrow.HasFired)
            {
                ClearTrackedArrow();
            }

            return;
        }

        if (!UsesHumanoidAutoSockets())
            return;

        if (trackedArrow == null ||
            trackedArrow.HasFired ||
            trackedSprite == null)
        {
            AcquireHeldArrow();
        }
    }

    private void LateUpdate()
    {
        if (bowController == null ||
            !bowController.IsAiming ||
            !UsesHumanoidAutoSockets())
        {
            return;
        }

        if (trackedArrow == null ||
            trackedArrow.HasFired ||
            trackedSprite == null ||
            trackedSprite.sprite == null)
        {
            AcquireHeldArrow();

            if (trackedArrow == null ||
                trackedSprite == null ||
                trackedSprite.sprite == null)
            {
                return;
            }
        }

        Bounds bounds =
            trackedSprite.sprite.bounds;

        if (bounds.size.x <= 0.0001f)
            return;

        // Convert the sprite's local arrow length to rendered world length.
        // This automatically respects the current transform scale.
        float renderedArrowLength =
            trackedSprite.transform
                .TransformVector(
                    Vector3.right *
                    bounds.size.x)
                .magnitude;

        float seatDistance =
            renderedArrowLength *
            NockSeatFraction;

        if (seatDistance <= 0.0001f)
            return;

        Vector3 shaftForward =
            trackedSprite.transform.right;

        if (shaftForward.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        shaftForward.Normalize();

        // Move only the rendered sprite slightly BACK toward the string.
        // ArrowController/Rigidbody position remains exactly where gameplay
        // expects it, so prediction and collision behaviour are unchanged.
        trackedSprite.transform.position -=
            shaftForward *
            seatDistance;

        visualOffsetApplied = true;
    }

    private bool UsesHumanoidAutoSockets()
    {
        if (archerVisual == null)
        {
            ResolveArcherVisual();
        }

        Archer3DRuntimeProfile profile =
            archerVisual != null
                ? archerVisual.Profile
                : null;

        return
            profile != null &&
            profile.SocketBindingMode ==
                ArcherSocketBindingMode
                    .HumanoidAutoFingerSockets;
    }

    private void ResolveArcherVisual()
    {
        if (bowController == null)
            return;

        archerVisual =
            bowController
                .GetComponentInChildren<
                    Archer3DVisualController>(
                    true);

        if (archerVisual != null)
            return;

        Archer3DVisualController[] visuals =
            Object.FindObjectsByType<
                Archer3DVisualController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        if (visuals.Length == 1)
        {
            archerVisual =
                visuals[0];
        }
    }

    private void AcquireHeldArrow()
    {
        RestoreSpritePosition();
        ClearTrackedArrow();

        ArrowController[] arrows =
            Object.FindObjectsByType<
                ArrowController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (ArrowController candidate
                 in arrows)
        {
            if (candidate == null ||
                candidate.HasFired)
            {
                continue;
            }

            SpriteRenderer sprite =
                candidate
                    .GetComponentInChildren<
                        SpriteRenderer>(
                        true);

            if (sprite == null ||
                sprite.sprite == null)
            {
                continue;
            }

            trackedArrow =
                candidate;

            trackedSprite =
                sprite;

            spriteBaseLocalPosition =
                trackedSprite.transform
                    .localPosition;

            visualOffsetApplied =
                false;

            return;
        }
    }

    private void RestoreSpritePosition()
    {
        if (!visualOffsetApplied ||
            trackedSprite == null)
        {
            return;
        }

        trackedSprite.transform.localPosition =
            spriteBaseLocalPosition;

        visualOffsetApplied =
            false;
    }

    private void ClearTrackedArrow()
    {
        RestoreSpritePosition();

        trackedArrow = null;
        trackedSprite = null;
        spriteBaseLocalPosition = Vector3.zero;
        visualOffsetApplied = false;
    }

    private void OnDisable()
    {
        RestoreSpritePosition();
    }

    private void OnDestroy()
    {
        RestoreSpritePosition();
    }
}
