using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Owns touch/mouse aiming and the real 2D ArrowController.
/// Character presentation is delegated to Archer3DVisualController.
///
/// The real projectile physics remains unchanged.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class BowController : MonoBehaviour
{
    [Header("Optional Stable References")]
    [SerializeField] private Transform arrowSpawnPoint;
    [SerializeField] private AimTrajectoryRenderer trajectoryRenderer;

    private GameConfig config;
    private Archer3DRuntimeProfile archerProfile;
    private Archer3DVisualController archerVisual;

    private ArrowController arrow;
    private SpriteRenderer arrowSpriteRenderer;
    private Renderer[] gameplayArrowRenderers;

    private Camera mainCamera;

    private bool inputEnabled;
    private bool isAiming;
    private bool releasePending;

    // A press only starts pointer tracking. A real aim begins only after the
    // pointer has moved far enough to qualify as a deliberate drag. This
    // prevents tap/click-to-shoot while preserving mouse and touch dragging.
    private bool pointerTracking;
    private Vector2 pointerDownScreenPosition;

    private Vector2 currentAimDirection = Vector2.right;
    private float currentDrawAmount;

    private readonly List<RaycastResult> uiRaycastResults =
        new List<RaycastResult>(8);

    public event Action AimStarted;
    public event Action<Vector2> AimChanged;
    public event Action<Vector2> AimReleased;
    public event Action AimCancelled;

    public Transform ArrowSpawnPoint =>
        arrowSpawnPoint != null
            ? arrowSpawnPoint
            : transform;

    public bool IsAiming => isAiming;

    public bool FullTrajectoryPreviewEnabled =>
        trajectoryRenderer != null &&
        trajectoryRenderer.FullPathEnabled;

    private void Awake()
    {
        mainCamera = Camera.main;
        Configure(GameConfig.Load());
    }

    public void Configure(GameConfig gameConfig)
    {
        config =
            gameConfig != null
                ? gameConfig
                : GameConfig.Load();

        EnsureRuntimeHelpers();

        archerProfile =
            Archer3DRuntimeProfile.LoadDefault();

        UnsubscribeFromArcher();

        archerVisual =
            Archer3DRuntimeFactory.Ensure(
                transform,
                archerProfile);

        SubscribeToArcher();

        trajectoryRenderer?.Configure(config);

        RefreshSpawnPointFromArcher();
    }

    private void SubscribeToArcher()
    {
        if (archerVisual == null)
            return;

        archerVisual.ReleaseFrame +=
            OnArcherReleaseFrame;

        archerVisual.PoseApplied +=
            OnArcherPoseApplied;
    }

    private void UnsubscribeFromArcher()
    {
        if (archerVisual == null)
            return;

        archerVisual.ReleaseFrame -=
            OnArcherReleaseFrame;

        archerVisual.PoseApplied -=
            OnArcherPoseApplied;
    }

    private void EnsureRuntimeHelpers()
    {
        if (arrowSpawnPoint == null)
        {
            Transform existing =
                transform.Find("ArrowSpawnPoint");

            if (existing != null)
            {
                arrowSpawnPoint = existing;
            }
            else
            {
                GameObject spawn =
                    new GameObject("ArrowSpawnPoint");

                spawn.transform.SetParent(
                    transform,
                    false);

                arrowSpawnPoint =
                    spawn.transform;
            }
        }

        if (trajectoryRenderer == null)
        {
            Transform trajectoryTransform =
                transform.Find("Trajectory");

            if (trajectoryTransform == null)
            {
                GameObject trajectory =
                    new GameObject("Trajectory");

                trajectory.transform.SetParent(
                    transform,
                    false);

                trajectoryTransform =
                    trajectory.transform;
            }

            trajectoryRenderer =
                trajectoryTransform
                    .GetComponent<AimTrajectoryRenderer>();

            if (trajectoryRenderer == null)
            {
                trajectoryRenderer =
                    trajectoryTransform.gameObject
                        .AddComponent<AimTrajectoryRenderer>();
            }
        }
    }

    public void SetArrow(ArrowController newArrow)
    {
        CancelAim();

        arrow = newArrow;
        releasePending = false;
        currentAimDirection = Vector2.right;
        currentDrawAmount = 0f;

        CacheGameplayArrowVisuals();
        trajectoryRenderer?.SetPredictionArrow(arrow);

        if (arrow == null)
            return;

        archerVisual?.SetReady();

        RefreshSpawnPointFromArcher();
        AlignGameplayArrowToFinalPose();

        // A freshly prepared gameplay arrow must stay hidden while the
        // archer is idle. It becomes visible only when the player actually
        // starts drawing (if no asset-held arrow is available), or on release
        // when it becomes the real flying projectile.
        SetGameplayArrowVisible(false);
    }

    public void ClearArrow(
        ArrowController arrowToClear)
    {
        if (arrow != arrowToClear)
            return;

        CancelAim();

        arrow = null;
        arrowSpriteRenderer = null;
        gameplayArrowRenderers = null;
        trajectoryRenderer?.SetPredictionArrow(null);
        releasePending = false;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (!enabled)
        {
            pointerTracking = false;

            if (isAiming)
                CancelAim();
        }
    }

    /// <summary>
    /// Enables/disables the optional full trajectory assist. When disabled,
    /// no trajectory overlay is shown.
    /// </summary>
    public void SetFullTrajectoryPreviewEnabled(bool enabled)
    {
        EnsureRuntimeHelpers();
        trajectoryRenderer?.SetFullPathEnabled(enabled);

        // FULL PATH is an opt-in assist. OFF means no trajectory overlay.
        // ON means the complete mirror-aware path is shown while aiming.
        if (!isAiming)
            return;

        if (!enabled)
        {
            trajectoryRenderer?.Hide();
            return;
        }

        trajectoryRenderer?.Show();
        UpdateTrajectoryPreview();
    }

    private void Update()
    {
        if (!inputEnabled ||
            releasePending ||
            arrow == null ||
            arrow.HasFired)
        {
            pointerTracking = false;
            return;
        }

        if (!TryReadPointer(
                out Vector2 screenPosition,
                out bool pressedThisFrame,
                out bool held,
                out bool releasedThisFrame))
        {
            return;
        }

        if (pressedThisFrame)
        {
            pointerTracking = false;

            if (IsScreenPositionOverUI(screenPosition))
                return;

            pointerDownScreenPosition = screenPosition;
            pointerTracking = true;
        }

        if (!pointerTracking)
            return;

        // Do not enter the aiming state on pointer-down. The player must
        // actually drag first. A simple click/tap therefore does nothing.
        if (!isAiming &&
            held &&
            HasReachedMinimumDrag(screenPosition))
        {
            BeginAim();
        }

        if (isAiming && held)
            UpdateAim(screenPosition);

        if (!releasedThisFrame)
            return;

        // Also accept a very quick swipe where the release event arrives
        // before an intermediate held frame crosses the threshold. It is
        // still a deliberate drag, not a click.
        if (!isAiming &&
            HasReachedMinimumDrag(screenPosition))
        {
            BeginAim();
            UpdateAim(screenPosition);
        }

        if (isAiming)
            ReleaseAim(screenPosition);

        pointerTracking = false;
    }

    private bool HasReachedMinimumDrag(Vector2 screenPosition)
    {
        if (config == null)
            return false;

        float referencePixels =
            Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));

        float minimumDragPixels =
            Mathf.Max(
                8f,
                referencePixels * config.MinimumDragScreenFraction);

        return (screenPosition - pointerDownScreenPosition).sqrMagnitude >=
               minimumDragPixels * minimumDragPixels;
    }

    // Fallback only if the 3D visual did not initialize.
    private void LateUpdate()
    {
        if (archerVisual != null)
            return;

        if (arrow == null ||
            arrow.HasFired)
        {
            return;
        }

        RefreshSpawnPointFromArcher();
        AlignGameplayArrowToFinalPose();

        if (isAiming)
        {
            UpdateTrajectoryPreview();
        }
    }

    /// <summary>
    /// Called AFTER Archer3DVisualController has applied the final direct-bone
    /// pose in LateUpdate. This is the critical ordering guarantee that keeps
    /// the arrow attached to the actual moving draw hand.
    /// </summary>
    private void OnArcherPoseApplied()
    {
        if (arrow == null ||
            arrow.HasFired)
        {
            return;
        }

        RefreshSpawnPointFromArcher();
        AlignGameplayArrowToFinalPose();

        if (isAiming)
        {
            UpdateTrajectoryPreview();
        }
    }

    /// <summary>
    /// The held arrow is aligned so its TAIL sits on ArrowSpawnPoint.
    /// Its Transform/Rigidbody pivot is therefore farther forward than the
    /// nock. Prediction must start from that real pivot so mirror contact
    /// happens at the same place and time as the real projectile.
    /// </summary>
    private void UpdateTrajectoryPreview()
    {
        if (trajectoryRenderer == null)
            return;

        Vector2 arrowPivot =
            arrow != null
                ? (Vector2)arrow.transform.position
                : (Vector2)ArrowSpawnPoint.position;

        trajectoryRenderer.UpdateTrajectory(
            arrowPivot,
            currentAimDirection);
    }

    private bool TryReadPointer(
        out Vector2 screenPosition,
        out bool pressedThisFrame,
        out bool held,
        out bool releasedThisFrame)
    {
        screenPosition = Vector2.zero;
        pressedThisFrame = false;
        held = false;
        releasedThisFrame = false;

        if (Touchscreen.current != null)
        {
            var touch =
                Touchscreen.current.primaryTouch;

            screenPosition =
                touch.position.ReadValue();

            pressedThisFrame =
                touch.press.wasPressedThisFrame;

            held =
                touch.press.isPressed;

            releasedThisFrame =
                touch.press.wasReleasedThisFrame;

            if (pressedThisFrame ||
                held ||
                releasedThisFrame)
            {
                return true;
            }
        }

        if (Mouse.current == null)
            return false;

        screenPosition =
            Mouse.current.position.ReadValue();

        pressedThisFrame =
            Mouse.current.leftButton
                .wasPressedThisFrame;

        held =
            Mouse.current.leftButton.isPressed;

        releasedThisFrame =
            Mouse.current.leftButton
                .wasReleasedThisFrame;

        return true;
    }

    private void BeginAim()
    {
        if (arrow == null ||
            arrow.HasFired ||
            releasePending)
        {
            return;
        }

        isAiming = true;
        currentDrawAmount = 0f;

        archerVisual?.BeginDraw();

        if (FullTrajectoryPreviewEnabled)
            trajectoryRenderer?.Show();
        else
            trajectoryRenderer?.Hide();

        bool useAssetHeldArrow =
            archerVisual != null &&
            archerVisual.HasHeldArrowVisual;

        SetGameplayArrowVisible(
            !useAssetHeldArrow);

        AimStarted?.Invoke();
    }

    private void UpdateAim(
        Vector2 screenPosition)
    {
        if (!TryGetAim(
                screenPosition,
                out Vector2 direction,
                out float dragDistance))
        {
            return;
        }

        currentAimDirection = direction;

        currentDrawAmount =
            Mathf.Clamp01(
                dragDistance /
                Mathf.Max(
                    0.01f,
                    config.FullDrawDistance));

        // Exact pull-back-derived direction goes to BOTH the visual controller
        // and the real projectile. The visual controller smooths character
        // presentation only; projectile physics keeps the exact direction.
        archerVisual?.UpdateAim(
            direction,
            currentDrawAmount);

        arrow.SetDirection(direction);

        AimChanged?.Invoke(direction);
    }

    private void ReleaseAim(
        Vector2 screenPosition)
    {
        if (!isAiming ||
            releasePending ||
            arrow == null ||
            arrow.HasFired)
        {
            return;
        }

        if (!TryGetAim(
                screenPosition,
                out Vector2 direction,
                out float dragDistance))
        {
            CancelAim();
            return;
        }

        if (dragDistance <
            config.MinimumAimDistance)
        {
            CancelAim();
            return;
        }

        currentAimDirection = direction;

        currentDrawAmount =
            Mathf.Clamp01(
                dragDistance /
                Mathf.Max(
                    0.01f,
                    config.FullDrawDistance));

        isAiming = false;
        releasePending = true;

        trajectoryRenderer?.Hide();

        archerVisual?.UpdateAim(
            direction,
            currentDrawAmount);

        AimReleased?.Invoke(direction);

        // Mobile feel: projectile launch must happen on the SAME input-release
        // frame. The archer's Release animation is presentation only and is
        // allowed to continue after the real projectile has already launched.
        //
        // Previously the shot waited for an Animation Event / fallback timer,
        // which is the delay visible on device.
        archerVisual?.Release();
        CommitShot();
    }

    private void OnArcherReleaseFrame()
    {
        CommitShot();
    }

    private void CommitShot()
    {
        if (!releasePending ||
            arrow == null ||
            arrow.HasFired)
        {
            releasePending = false;
            return;
        }

        // The ReleaseFrame callback originates from Archer LateUpdate,
        // after the final hand pose for the launch frame has been applied.
        RefreshSpawnPointFromArcher();
        AlignGameplayArrowToFinalPose();

        archerVisual?.SetHeldArrowVisible(false);
        SetGameplayArrowVisible(true);

        // Physics still uses the exact pull-back direction, not the smoothed visual angle.
        arrow.SetDirection(currentAimDirection);
        arrow.Fire(currentAimDirection);

        releasePending = false;
    }

    private bool TryGetAim(
        Vector2 screenPosition,
        out Vector2 clampedDirection,
        out float dragDistance)
    {
        clampedDirection = Vector2.right;
        dragDistance = 0f;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return false;

        Vector3 spawnPosition =
            ArrowSpawnPoint.position;

        float depth =
            Mathf.Abs(
                mainCamera.transform.position.z -
                spawnPosition.z);

        // Pull-back aiming:
        // the player's drag represents pulling the bow/string away from the
        // firing direction. Therefore the shot direction is the OPPOSITE of
        // the pointer movement from press -> current position.
        //
        // Drag down  -> aim up
        // Drag up    -> aim down
        // Drag left  -> aim right
        //
        // Converting both points at the same camera depth keeps the gesture
        // consistent with orthographic gameplay and independent of where on
        // the screen the drag started.
        Vector3 pointerDownWorld3 =
            mainCamera.ScreenToWorldPoint(
                new Vector3(
                    pointerDownScreenPosition.x,
                    pointerDownScreenPosition.y,
                    depth));

        Vector3 pointerWorld3 =
            mainCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    depth));

        Vector2 pullDirection =
            (Vector2)(pointerDownWorld3 - pointerWorld3);

        dragDistance =
            pullDirection.magnitude;

        if (pullDirection.sqrMagnitude < 0.0001f)
            return false;

        // Gameplay always fires into the forward/right hemisphere. Mirroring
        // only X keeps the pull-back vertical behaviour continuous while also
        // retaining the earlier protection against the +/-180 degree Atan2
        // branch-cut jump when the pointer crosses behind the archer.
        pullDirection.x =
            Mathf.Abs(
                pullDirection.x);

        float rawAngle =
            Mathf.Atan2(
                pullDirection.y,
                pullDirection.x) *
            Mathf.Rad2Deg;

        float clampedAngle =
            Mathf.Clamp(
                rawAngle,
                config.MinimumAimAngle,
                config.MaximumAimAngle);

        float radians =
            clampedAngle *
            Mathf.Deg2Rad;

        clampedDirection =
            new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians))
            .normalized;

        return true;
    }

    private bool IsScreenPositionOverUI(
        Vector2 screenPosition)
    {
        EventSystem eventSystem =
            EventSystem.current;

        if (eventSystem == null)
            return false;

        PointerEventData eventData =
            new PointerEventData(eventSystem)
            {
                position = screenPosition
            };

        uiRaycastResults.Clear();

        eventSystem.RaycastAll(
            eventData,
            uiRaycastResults);

        return uiRaycastResults.Count > 0;
    }

    private void RefreshSpawnPointFromArcher()
    {
        if (arrowSpawnPoint == null)
            return;

        Vector3 nock =
            archerVisual != null
                ? archerVisual.NockWorldPosition
                : transform.position;

        if (archerProfile != null)
        {
            nock.z +=
                archerProfile.GameplayArrowZOffset;
        }

        arrowSpawnPoint.position = nock;
    }

    private void AlignGameplayArrowToFinalPose()
    {
        if (arrow == null)
            return;

        // While held by a generic Humanoid/Mixamo archer, the visible arrow
        // MUST lie on the real draw-fingers -> bow-grip line. Previously it was
        // rotated directly from pointer/gameplay aim, so it passed above the
        // bow hand when aiming up and below it when aiming down.
        //
        // Archer3DVisualController now corrects that pose line toward the exact
        // gameplay direction, so this gives both natural contact AND gameplay
        // consistency. CommitShot still fires with currentAimDirection exactly
        // as before; projectile physics is unchanged.
        bool useHumanoidPoseLine =
            archerVisual != null &&
            archerProfile != null &&
            archerProfile.SocketBindingMode ==
                ArcherSocketBindingMode.HumanoidAutoFingerSockets;

        Vector2 visualDirection =
            useHumanoidPoseLine
                ? archerVisual.PoseDirection
                : isAiming
                    ? currentAimDirection
                    : archerVisual != null
                        ? archerVisual.PoseDirection
                        : currentAimDirection;

        if (visualDirection.sqrMagnitude < 0.0001f)
            visualDirection = currentAimDirection;

        arrow.SetDirection(visualDirection);

        Vector3 nock =
            ArrowSpawnPoint.position;

        if (arrowSpriteRenderer == null ||
            arrowSpriteRenderer.sprite == null)
        {
            arrow.transform.position = nock;
            return;
        }

        arrow.transform.position = nock;

        Bounds spriteBounds =
            arrowSpriteRenderer.sprite.bounds;

        // Align the TAIL of the sprite, not its center pivot, to the nock.
        Vector3 tailLocal =
            new Vector3(
                spriteBounds.min.x,
                spriteBounds.center.y,
                spriteBounds.center.z);

        Vector3 tailWorld =
            arrowSpriteRenderer.transform
                .TransformPoint(tailLocal);

        Vector3 correction =
            nock - tailWorld;

        arrow.transform.position += correction;
    }

    private void CacheGameplayArrowVisuals()
    {
        if (arrow == null)
        {
            gameplayArrowRenderers = null;
            arrowSpriteRenderer = null;
            return;
        }

        gameplayArrowRenderers =
            arrow.GetComponentsInChildren<Renderer>(true);

        arrowSpriteRenderer =
            arrow.GetComponentInChildren<SpriteRenderer>(true);
    }

    private void SetGameplayArrowVisible(
        bool visible)
    {
        if (gameplayArrowRenderers == null)
            return;

        foreach (Renderer renderer
                 in gameplayArrowRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    private void CancelAim()
    {
        pointerTracking = false;

        if (!isAiming)
        {
            trajectoryRenderer?.Hide();
            return;
        }

        isAiming = false;
        releasePending = false;
        currentDrawAmount = 0f;

        trajectoryRenderer?.Hide();
        archerVisual?.CancelDraw();

        // Cancel returns to idle, so no gameplay arrow should be hanging
        // in front of the character.
        SetGameplayArrowVisible(false);

        AimCancelled?.Invoke();
    }

    private void OnDisable()
    {
        UnsubscribeFromArcher();
        CancelAim();
        releasePending = false;
    }
}
