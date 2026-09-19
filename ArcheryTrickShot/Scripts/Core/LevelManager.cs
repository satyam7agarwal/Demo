using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class LevelManager : MonoBehaviour
{
    private enum LevelState
    {
        Loading,
        Playing,
        ResolvingShot,
        Completed,
        Failed,
        Paused
    }

    [Header("Optional Inspector Overrides")]
    [Tooltip("Leave empty to auto-load all LevelData assets from Resources/Levels.")]
    [SerializeField] private LevelData[] levels;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject mirrorPrefab;
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private BowController bow;
    [SerializeField] private GameUIController gameUI;

    private GameConfig config;
    private GameAudioController audioController;
    private GameFeelController gameFeel;
    private ATSFrontendController frontend;

    private LevelState currentState = LevelState.Loading;
    private LevelState stateBeforePause = LevelState.Playing;
    private int currentLevelIndex;
    private LevelData currentLevel;
    private int shotsUsed;
    private int currentLevelScore;
    private int currentLevelTargetScore;
    private int currentLevelStyleScore;
    private TargetHitResult lastTargetHitResult;
    private bool fullTrajectoryPreviewEnabled;
    private int currentShotRicochets;
    private int currentShotStyleBonus;
    private bool currentShotStyleBonusCommitted;
    private int currentLevelMaxRewardedRicochetMirrors;
    private readonly HashSet<int> currentShotUniqueMirrorIds =
        new HashSet<int>();

    private const int FinalApproachRaycastBufferSize = 24;
    private readonly RaycastHit2D[] finalApproachRaycastHits =
        new RaycastHit2D[FinalApproachRaycastBufferSize];
    private bool finalApproachActive;
    private bool finalApproachWasTrickShot;
    private bool finalApproachPredictedBullseye;
    private Vector2 finalApproachPredictedImpactPoint;

    public int CurrentLevelScore => currentLevelScore;

    private Transform levelObjectsParent;
    private ArrowController currentArrow;
    private readonly List<Target> activeTargets = new List<Target>();
    private readonly Stack<ArrowController> arrowPool =
        new Stack<ArrowController>(2);
    private Transform arrowPoolParent;
    private Coroutine resolutionRoutine;

    private void Start()
    {
        config = GameConfig.Load();

        if (!ResolveDependencies())
            return;

        ConfigureCamera();
        gameUI.Initialize(this, config);
        currentLevelIndex = 0;
        frontend = ATSFrontendController.Ensure(this, config, bow, gameUI);
        OpenMainMenu();
    }

    private void Update()
    {
        if (currentState != LevelState.Playing ||
            finalApproachActive)
        {
            return;
        }

        TryBeginFinalApproach();
    }

    private void TryBeginFinalApproach()
    {
        if (config == null ||
            !config.FinalShotCinematicEnabled ||
            currentArrow == null ||
            !currentArrow.HasFired ||
            currentArrow.IsStopped)
        {
            return;
        }

        Vector2 velocity = currentArrow.GetVelocity();
        if (velocity.sqrMagnitude < 0.0001f)
            return;

        Vector2 direction = velocity.normalized;
        Vector2 origin =
            currentArrow.GetVisualTipWorldPosition(direction) +
            direction * 0.01f;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        filter.useNormalAngle = false;

        int hitCount = Physics2D.Raycast(
            origin,
            direction,
            filter,
            finalApproachRaycastHits,
            config.FinalApproachTriggerDistance);

        Collider2D nearestCollider = null;
        Vector2 nearestPoint = default;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = finalApproachRaycastHits[i];
            Collider2D hitCollider = hit.collider;

            if (hitCollider == null)
                continue;

            if (hitCollider.GetComponentInParent<ArrowController>() ==
                currentArrow)
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestCollider = hitCollider;
            nearestPoint = hit.point;
        }

        if (nearestCollider == null)
            return;

        TargetContactSensor sensor =
            nearestCollider.GetComponent<TargetContactSensor>();

        if (sensor == null ||
            sensor.Kind != TargetContactKind.ScoringFace)
        {
            // A mirror, wall or physical target part is still ahead.
            return;
        }

        Target target =
            sensor.GetComponentInParent<Target>();

        if (target == null ||
            !target.CanScoreApproach(direction))
        {
            return;
        }

        TargetHitZone predictedZone =
            target.PreviewHitZone(nearestPoint);

        if (predictedZone == TargetHitZone.Invalid)
            return;

        finalApproachActive = true;
        finalApproachWasTrickShot =
            currentShotRicochets > 0;
        finalApproachPredictedBullseye =
            predictedZone == TargetHitZone.Bullseye;
        finalApproachPredictedImpactPoint =
            nearestPoint;

        currentArrow.SetCinematicTrail(true);

        // The ricochet HUD now lives in the bottom-center safe area, so it no
        // longer competes with Update-A's target cinematic. Let the latest
        // ricochet label finish its normal short display instead of cancelling
        // DOUBLE RICOCHET / TRICK SHOT immediately after the final mirror.
        audioController?.BeginFinalApproach(
            finalApproachWasTrickShot,
            finalApproachPredictedBullseye);

        gameFeel?.BeginFinalApproach(
            finalApproachPredictedImpactPoint,
            finalApproachWasTrickShot,
            finalApproachPredictedBullseye);
    }

    private void CancelFinalApproach()
    {
        if (currentArrow != null)
            currentArrow.SetCinematicTrail(false);

        finalApproachActive = false;
        finalApproachWasTrickShot = false;
        finalApproachPredictedBullseye = false;
        finalApproachPredictedImpactPoint = default;

        gameFeel?.CancelFinalShotCinematic();
        audioController?.CancelTransientDuck();
    }

    private bool ResolveDependencies()
    {
        LevelData[] resourceLevels = Resources.LoadAll<LevelData>("Levels")
            .Where(level => level != null)
            .OrderBy(level => level.LevelNumber)
            .ToArray();

        // Resources/Levels is authoritative so adding a LevelData asset does not require scene edits.
        if (resourceLevels.Length > 0)
            levels = resourceLevels;

        if (levels == null || levels.Length == 0)
        {
            Debug.LogError("LevelManager: No LevelData assets were found. Add them under Resources/Levels.");
            return false;
        }

        targetPrefab = targetPrefab != null
            ? targetPrefab
            : Resources.Load<GameObject>("Prefabs/Gameplay/Target");
        wallPrefab = wallPrefab != null
            ? wallPrefab
            : Resources.Load<GameObject>("Prefabs/Gameplay/Wall");
        mirrorPrefab = mirrorPrefab != null
            ? mirrorPrefab
            : Resources.Load<GameObject>("Prefabs/Gameplay/Mirror");
        arrowPrefab = arrowPrefab != null
            ? arrowPrefab
            : Resources.Load<GameObject>("Prefabs/Gameplay/Arrow");

        if (targetPrefab == null || arrowPrefab == null)
        {
            Debug.LogError("LevelManager: Required Target/Arrow prefabs are missing from Resources/Prefabs/Gameplay.");
            return false;
        }

        if (bow == null)
            bow = FindFirstObjectByType<BowController>();

        if (bow == null)
        {
            GameObject bowPrefab = Resources.Load<GameObject>("Prefabs/Gameplay/Bow");
            if (bowPrefab != null)
            {
                GameObject instance = Instantiate(bowPrefab);
                bow = instance.GetComponent<BowController>();
            }
        }

        if (bow == null)
        {
            Debug.LogError("LevelManager: BowController could not be found or created.");
            return false;
        }

        bow.Configure(config);

        if (gameUI == null)
            gameUI = FindFirstObjectByType<GameUIController>();

        if (gameUI == null)
        {
            GameObject canvasObject = new GameObject(
                "GameplayCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(GameUIController)
            );
            canvasObject.layer = LayerMask.NameToLayer("UI");
            gameUI = canvasObject.GetComponent<GameUIController>();
        }

        audioController = FindFirstObjectByType<GameAudioController>();
        if (audioController == null)
        {
            GameObject audioObject = new GameObject("GameAudio");
            audioController = audioObject.AddComponent<GameAudioController>();
        }
        audioController.Configure(config);

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            gameFeel = mainCamera.GetComponent<GameFeelController>();
            if (gameFeel == null)
                gameFeel = mainCamera.gameObject.AddComponent<GameFeelController>();
            gameFeel.Configure(config);
        }

        return true;
    }

    private void ConfigureCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null || !mainCamera.orthographic)
            return;

        mainCamera.orthographicSize = config.BaseOrthographicSize;
    }

    public void LoadLevel()
    {
        if (levels == null || levels.Length == 0)
            return;

        frontend?.HideFrontendImmediate();
        if (bow != null)
            bow.gameObject.SetActive(true);
        gameUI?.SetGameplayVisible(true);
        gameFeel?.CancelFinalShotCinematic();
        audioController?.CancelTransientDuck();
        finalApproachActive = false;
        finalApproachWasTrickShot = false;
        finalApproachPredictedBullseye = false;
        finalApproachPredictedImpactPoint = default;
        Time.timeScale = 1f;
        StopResolutionRoutine();
        ClearPreviousLevel();

        currentLevelIndex = Mathf.Clamp(currentLevelIndex, 0, levels.Length - 1);
        currentLevel = levels[currentLevelIndex];

        if (currentLevel == null)
        {
            Debug.LogError($"LevelManager: Level index {currentLevelIndex} is null.");
            return;
        }

        currentState = LevelState.Loading;
        shotsUsed = 0;
        currentLevelScore = 0;
        currentLevelTargetScore = 0;
        currentLevelStyleScore = 0;
        lastTargetHitResult = default;
        currentLevelMaxRewardedRicochetMirrors =
            ResolveCurrentLevelRicochetGoal();
        ResetShotComboState();

        bow.transform.position = new Vector3(
            currentLevel.ArcherPosition.x,
            currentLevel.ArcherPosition.y,
            bow.transform.position.z
        );
        bow.SetInputEnabled(false);

        CreateLevelParent();
        SpawnLevelObjects();

        gameUI.PrepareForLevel(
            currentLevel.LevelNumber,
            currentLevel.MaxShots,
            currentLevelMaxRewardedRicochetMirrors);
        gameUI.SetFullTrajectoryPreviewEnabled(fullTrajectoryPreviewEnabled);
        bow.SetFullTrajectoryPreviewEnabled(fullTrajectoryPreviewEnabled);

        currentState = LevelState.Playing;
        CreateArrow();
    }

    private void SpawnLevelObjects()
    {
        if (currentLevel.Objects == null)
            return;

        foreach (LevelData.LevelObjectData data in currentLevel.Objects)
        {
            GameObject prefab = GetPrefab(data.Type);
            if (prefab == null)
            {
                Debug.LogWarning($"LevelManager: No prefab is available for {data.Type}. Skipping object.");
                continue;
            }

            GameObject instance = Instantiate(
                prefab,
                new Vector3(data.Position.x, data.Position.y, 0f),
                Quaternion.Euler(0f, 0f, data.Rotation),
                levelObjectsParent
            );

            instance.transform.localScale = new Vector3(data.Scale.x, data.Scale.y, 1f);

            if (data.Type == LevelData.ObjectType.Wall)
            {
                Wall wall = instance.GetComponent<Wall>();
                if (wall == null)
                    wall = instance.AddComponent<Wall>();

                wall.ApplyPresentation();
            }

            if (data.Type == LevelData.ObjectType.Target && instance.TryGetComponent(out Target target))
            {
                target.ScoredHit += OnTargetScoredHit;
                target.Hit += OnTargetHit;
                target.InvalidHit += OnTargetInvalidHit;
                activeTargets.Add(target);

                if (instance.TryGetComponent(
                        out TargetVisualFacing visualFacing))
                {
                    visualFacing.ApplyVisual(
                        data.Style,
                        data.Facing,
                        currentLevel.ArcherPosition.x);
                }
            }
        }
    }

    private void CreateArrow()
    {
        if (arrowPrefab == null ||
            bow == null ||
            currentState != LevelState.Playing)
        {
            return;
        }

        Transform spawnPoint = bow.ArrowSpawnPoint;
        ResetShotComboState();

        if (arrowPool.Count > 0)
        {
            currentArrow = arrowPool.Pop();
            currentArrow.transform.SetParent(null, false);
            currentArrow.transform.SetPositionAndRotation(
                spawnPoint.position,
                spawnPoint.rotation);
            currentArrow.gameObject.SetActive(true);
        }
        else
        {
            GameObject arrowObject = Instantiate(
                arrowPrefab,
                spawnPoint.position,
                spawnPoint.rotation);

            if (!arrowObject.TryGetComponent(out currentArrow))
            {
                Debug.LogError(
                    "LevelManager: Arrow prefab does not contain ArrowController.");
                Destroy(arrowObject);
                return;
            }
        }

        currentArrow.Configure(config);
        currentArrow.SolidCollision += OnSolidCollision;
        currentArrow.Missed += OnMissed;
        currentArrow.Shot += OnShot;
        currentArrow.Reflected += OnArrowReflected;
        currentArrow.ResetArrow();

        bow.SetArrow(currentArrow);
        bow.SetInputEnabled(true);
        gameUI?.SetAimHintVisible(true);
    }

    private GameObject GetPrefab(LevelData.ObjectType type)
    {
        switch (type)
        {
            case LevelData.ObjectType.Target:
                return targetPrefab;
            case LevelData.ObjectType.Wall:
                return wallPrefab;
            case LevelData.ObjectType.Mirror:
                return mirrorPrefab;
            default:
                return null;
        }
    }

    private void OnShot()
    {
        if (currentState != LevelState.Playing)
            return;

        // CreateArrow resets combo state. Keep UI explicitly clean on the
        // release frame so a previous failed-shot combo can never leak forward.
        gameUI?.ResetRicochetCombo();
        finalApproachActive = false;
        finalApproachWasTrickShot = false;
        finalApproachPredictedBullseye = false;
        finalApproachPredictedImpactPoint = default;
        shotsUsed++;
        int remaining = Mathf.Max(0, currentLevel.MaxShots - shotsUsed);
        gameUI.UpdateShots(remaining, currentLevel.MaxShots, true);
        gameUI.SetAimHintVisible(false);
        audioController?.PlayShot();
    }

    private void OnTargetScoredHit(
        TargetHitResult result)
    {
        if (currentState != LevelState.Playing)
            return;

        lastTargetHitResult = result;

        currentLevelTargetScore +=
            result.Score;

        int committedStyleBonus = 0;

        // Style is earned only when the shot actually reaches a scoring face.
        // Repeated target callbacks (or future multi-target logic) cannot award
        // the same shot's ricochet bonus more than once.
        if (!currentShotStyleBonusCommitted)
        {
            currentShotStyleBonusCommitted = true;
            committedStyleBonus =
                currentShotStyleBonus;

            currentLevelStyleScore +=
                committedStyleBonus;
        }

        currentLevelScore +=
            result.Score +
            committedStyleBonus;

        gameUI.PlayHitFeedback(
            result.Label,
            result.Score,
            result.IsBullseye,
            finalApproachActive);

        Debug.Log(
            $"{result.Label} +{result.Score} " +
            $"+ STYLE {committedStyleBonus} " +
            $"(Level score: {currentLevelScore})");
    }

    private void OnTargetInvalidHit()
    {
        if (currentState != LevelState.Playing)
            return;

        // The arrow did contact the target, but not its valid scoring face
        // (for example rim/back/underside). Consume the shot as a miss while
        // using the target-impact sound rather than the wall-impact sound.
        CancelFinalApproach();
        audioController?.PlayTargetHit();
        ResolveFailedShot(false);
    }

    private void OnTargetHit()
    {
        if (currentState != LevelState.Playing)
            return;

        // IMPORTANT: capture cinematic state BEFORE clearing it. The first
        // Update-A version cancelled here too early, which bypassed the strong
        // impact path even though the approach slow-motion had started.
        bool cinematicHit = finalApproachActive;
        bool trickShot = finalApproachWasTrickShot;
        bool isBullseye = lastTargetHitResult.IsBullseye;
        Vector2 impactPoint = lastTargetHitResult.WorldPoint;

        currentState = LevelState.ResolvingShot;
        bow.SetInputEnabled(false);
        currentArrow?.Stop();

        // Do not call CancelFinalApproach here. GameFeel must transition the
        // active approach directly into hit-stop / impact focus.
        finalApproachActive = false;
        finalApproachWasTrickShot = false;
        finalApproachPredictedBullseye = false;
        finalApproachPredictedImpactPoint = default;

        audioController?.PlayTargetHit(
            cinematicHit,
            isBullseye);

        ATSHaptics.Pulse();

        if (cinematicHit)
        {
            gameFeel?.PlayFinalImpact(
                isBullseye,
                impactPoint,
                trickShot);
        }
        else
        {
            gameFeel?.PlayHitFeedback(isBullseye);
        }

        StopResolutionRoutine();
        resolutionRoutine = StartCoroutine(
            CompleteLevelSequence(
                cinematicHit,
                trickShot,
                isBullseye));
    }

    private IEnumerator CompleteLevelSequence(
        bool cinematicHit,
        bool trickShot,
        bool isBullseye)
    {
        float resultDelay = config.HitResultDelay;

        if (cinematicHit)
        {
            if (isBullseye && trickShot)
                resultDelay = config.CinematicBullseyeTrickShotResultDelay;
            else if (isBullseye)
                resultDelay = config.CinematicBullseyeResultDelay;
            else if (trickShot)
                resultDelay = config.CinematicTrickShotResultDelay;
            else
                resultDelay = config.CinematicHitResultDelay;
        }

        // Keep the arrow visibly embedded while the impact VFX and camera
        // settle. The result card appears only after the shot has had time to
        // register visually.
        yield return new WaitForSecondsRealtime(resultDelay);

        DestroyCurrentArrow();
        currentState = LevelState.Completed;

        bool isLastLevel = currentLevelIndex >= levels.Length - 1;

        string hitLabel =
            lastTargetHitResult.Zone == TargetHitZone.Invalid
                ? "TARGET HIT!"
                : lastTargetHitResult.Label;

        int earnedStars = CalculateStars(shotsUsed, currentLevel.MaxShots);
        ATSPlayerProgress.RecordCompletion(
            currentLevel.LevelNumber,
            earnedStars,
            currentLevelScore,
            levels.Length);

        gameUI.ShowComplete(
            shotsUsed,
            currentLevel.MaxShots,
            currentLevelScore,
            currentLevelTargetScore,
            currentLevelStyleScore,
            hitLabel,
            isBullseye,
            isLastLevel,
            currentShotRicochets,
            GetRewardedUniqueMirrorCount());

        audioController?.PlayLevelComplete();
        resolutionRoutine = null;
    }

    private void OnArrowReflected(
        int chainCount,
        Collider2D mirrorCollider,
        Vector2 contactPoint)
    {
        if (currentState != LevelState.Playing)
            return;

        // Defensive reset: if a future moving object enters the predicted final
        // segment, a reflection must immediately return gameplay to normal speed.
        if (finalApproachActive)
            CancelFinalApproach();

        currentShotRicochets =
            Mathf.Max(
                currentShotRicochets,
                chainCount);

        bool newUniqueMirror = false;

        if (mirrorCollider != null)
        {
            newUniqueMirror =
                currentShotUniqueMirrorIds.Add(
                    mirrorCollider.GetInstanceID());
        }

        int uniqueMirrorCount =
            GetRewardedUniqueMirrorCount();

        currentShotStyleBonus =
            CalculateRicochetStyleBonus(
                uniqueMirrorCount);

        ATSHaptics.Pulse();

        audioController?.PlayMirror(
            currentShotRicochets);

        gameFeel?.PlayRicochetFeedback(
            currentShotRicochets,
            contactPoint);

        gameUI?.PlayRicochetFeedback(
            currentShotRicochets,
            uniqueMirrorCount,
            currentShotStyleBonus,
            newUniqueMirror);
    }

    private void OnSolidCollision()
    {
        audioController?.PlayWallHit();
        ResolveFailedShot(false);
    }

    private void OnMissed()
    {
        ResolveFailedShot(true);
    }

    private void ResolveFailedShot(
        bool playMissSound)
    {
        if (currentState != LevelState.Playing)
            return;

        CancelFinalApproach();
        currentState = LevelState.ResolvingShot;
        bow.SetInputEnabled(false);
        currentArrow?.Stop();
        gameUI?.ResetRicochetCombo();

        int shotsRemaining = Mathf.Max(0, currentLevel.MaxShots - shotsUsed);
        gameUI.PlayMissFeedback(shotsRemaining);

        if (playMissSound)
            audioController?.PlayMiss();

        gameFeel?.PlayMissFeedback();

        StopResolutionRoutine();
        resolutionRoutine = StartCoroutine(FailedShotSequence(shotsRemaining));
    }

    private IEnumerator FailedShotSequence(int shotsRemaining)
    {
        yield return new WaitForSecondsRealtime(config.MissFeedbackDelay);
        DestroyCurrentArrow();

        if (shotsRemaining > 0)
        {
            yield return new WaitForSecondsRealtime(config.NextArrowDelay);
            currentState = LevelState.Playing;
            CreateArrow();
            resolutionRoutine = null;
            yield break;
        }

        currentState = LevelState.Failed;
        gameUI.ShowFailed();
        audioController?.PlayLevelFailed();
        resolutionRoutine = null;
    }

    public int LevelCount => levels != null ? levels.Length : 0;

    public void StartLevelByNumber(int levelNumber)
    {
        if (levels == null || levels.Length == 0)
            return;

        int index = System.Array.FindIndex(
            levels,
            level => level != null && level.LevelNumber == levelNumber);

        if (index < 0)
            index = Mathf.Clamp(levelNumber - 1, 0, levels.Length - 1);

        currentLevelIndex = index;
        ATSPlayerProgress.RecordLevelStarted(levels[currentLevelIndex].LevelNumber);

        // Re-resolve the roster selection before every frontend-driven start so
        // changing Khaem/Nerissa takes effect without restarting Unity or the app.
        if (bow != null)
        {
            bow.gameObject.SetActive(true);
            bow.Configure(config);
        }

        LoadLevel();
    }

    public void OpenMainMenu()
    {
        PrepareForFrontend();
        frontend?.ShowMainMenu();
    }

    public void OpenLevelSelect()
    {
        PrepareForFrontend();
        frontend?.ShowLevelSelect();
    }

    public void OpenCharacterSelect()
    {
        PrepareForFrontend();
        frontend?.ShowCharacterSelect();
    }

    public void OpenSettings()
    {
        PrepareForFrontend();
        frontend?.ShowSettings();
    }

    private void PrepareForFrontend()
    {
        CancelFinalApproach();
        Time.timeScale = 1f;
        StopResolutionRoutine();
        ClearPreviousLevel();
        currentState = LevelState.Loading;
        bow?.SetInputEnabled(false);
        if (bow != null)
            bow.gameObject.SetActive(false);
        gameUI?.SetGameplayVisible(false);
        audioController?.ResumeMusic();
    }

    private int ResolveCurrentLevelRicochetGoal()
    {
        if (currentLevel == null)
            return 0;

        int globalCap =
            config != null
                ? Mathf.Max(
                    0,
                    config.RicochetMaxRewardedUniqueMirrors)
                : 4;

        if (globalCap <= 0)
            return 0;

        // Explicit authoring wins. This is important for future levels that
        // contain decorative/optional/unreachable mirrors.
        if (currentLevel.MaxRewardedRicochetMirrors > 0)
        {
            return Mathf.Clamp(
                currentLevel.MaxRewardedRicochetMirrors,
                1,
                globalCap);
        }

        // Existing levels need no manual edits: auto-detect their mirror count.
        int authoredMirrorCount = 0;

        if (currentLevel.Objects != null)
        {
            for (int i = 0;
                 i < currentLevel.Objects.Length;
                 i++)
            {
                LevelData.LevelObjectData data =
                    currentLevel.Objects[i];

                if (data != null &&
                    data.Type ==
                        LevelData.ObjectType.Mirror)
                {
                    authoredMirrorCount++;
                }
            }
        }

        return Mathf.Clamp(
            authoredMirrorCount,
            0,
            globalCap);
    }

    private int GetRewardedUniqueMirrorCount()
    {
        return Mathf.Clamp(
            currentShotUniqueMirrorIds.Count,
            0,
            Mathf.Max(
                0,
                currentLevelMaxRewardedRicochetMirrors));
    }

    private void ResetShotComboState()
    {
        currentShotRicochets = 0;
        currentShotStyleBonus = 0;
        currentShotStyleBonusCommitted = false;
        currentShotUniqueMirrorIds.Clear();
        gameUI?.ResetRicochetCombo();
    }

    private int CalculateRicochetStyleBonus(
        int uniqueMirrorCount)
    {
        if (config == null ||
            uniqueMirrorCount <= 0)
        {
            return 0;
        }

        int rewardedMirrors =
            Mathf.Clamp(
                uniqueMirrorCount,
                0,
                Mathf.Max(
                    0,
                    currentLevelMaxRewardedRicochetMirrors));

        return rewardedMirrors switch
        {
            1 => config.RicochetStyleBonus1,
            2 => config.RicochetStyleBonus2,
            3 => config.RicochetStyleBonus3,
            _ => config.RicochetStyleBonus4
        };
    }

    private static int CalculateStars(int usedShots, int maxShots)
    {
        if (usedShots <= 1)
            return 3;
        if (usedShots < maxShots)
            return 2;
        return 1;
    }

    public void ToggleFullTrajectoryPreview()
    {
        if (currentState != LevelState.Playing)
            return;

        fullTrajectoryPreviewEnabled =
            !fullTrajectoryPreviewEnabled;

        bow?.SetFullTrajectoryPreviewEnabled(
            fullTrajectoryPreviewEnabled);

        gameUI?.SetFullTrajectoryPreviewEnabled(
            fullTrajectoryPreviewEnabled);
    }

    public void PauseGame()
    {
        if (currentState != LevelState.Playing)
            return;

        CancelFinalApproach();
        stateBeforePause = currentState;
        currentState = LevelState.Paused;
        bow.SetInputEnabled(false);
        Time.timeScale = 0f;
        audioController?.PauseMusic();
        gameUI.ShowPause();
    }

    public void ResumeGame()
    {
        if (currentState != LevelState.Paused)
            return;

        Time.timeScale = 1f;
        audioController?.ResumeMusic();
        currentState = stateBeforePause;
        bow.SetInputEnabled(currentState == LevelState.Playing);
        gameUI.HidePause();
    }

    public void RetryLevel()
    {
        Time.timeScale = 1f;
        LoadLevel();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        currentLevelIndex = 0;
        LoadLevel();
    }

    public void OnResultPrimaryClicked()
    {
        if (currentState != LevelState.Completed)
            return;

        if (currentLevelIndex >= levels.Length - 1)
        {
            OpenLevelSelect();
            return;
        }

        currentLevelIndex++;
        LoadLevel();
    }

    private void CreateLevelParent()
    {
        GameObject parent = new GameObject("LevelObjects");
        parent.transform.SetParent(transform, false);
        levelObjectsParent = parent.transform;
    }

    private void ClearPreviousLevel()
    {
        foreach (Target target in activeTargets)
        {
            if (target != null)
            {
                target.ScoredHit -= OnTargetScoredHit;
                target.Hit -= OnTargetHit;
                target.InvalidHit -= OnTargetInvalidHit;
            }
        }
        activeTargets.Clear();

        DestroyCurrentArrow();

        if (levelObjectsParent != null)
        {
            Destroy(levelObjectsParent.gameObject);
            levelObjectsParent = null;
        }
    }

    private void DestroyCurrentArrow()
    {
        if (currentArrow == null)
            return;

        currentArrow.SolidCollision -= OnSolidCollision;
        currentArrow.Missed -= OnMissed;
        currentArrow.Shot -= OnShot;
        currentArrow.Reflected -= OnArrowReflected;
        bow?.ClearArrow(currentArrow);

        currentArrow.ResetArrow();
        currentArrow.gameObject.SetActive(false);

        EnsureArrowPoolParent();
        currentArrow.transform.SetParent(
            arrowPoolParent,
            false);

        arrowPool.Push(currentArrow);
        currentArrow = null;
    }

    private void EnsureArrowPoolParent()
    {
        if (arrowPoolParent != null)
            return;

        GameObject poolObject = new GameObject("ArrowPool");
        poolObject.transform.SetParent(transform, false);
        arrowPoolParent = poolObject.transform;
    }

    private void StopResolutionRoutine()
    {
        if (resolutionRoutine == null)
            return;

        StopCoroutine(resolutionRoutine);
        resolutionRoutine = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && currentState == LevelState.Playing)
            PauseGame();
    }

    private void OnDestroy()
    {
        CancelFinalApproach();
        Time.timeScale = 1f;
        StopResolutionRoutine();
        ClearPreviousLevel();
    }
}
