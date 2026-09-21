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
    private ATSMedallionCampaignDecorator medallionCampaignDecorator;

    private LevelState currentState = LevelState.Loading;
    private LevelState stateBeforePause = LevelState.Playing;
    private int currentLevelIndex;
    private LevelData currentLevel;
    private ArcherGameplayTraitProfile currentArcherGameplayTrait;
    private CharacterGravityAimPreview gravityAimPreview;
    private int shotsUsed;
    private int currentLevelScore;
    private int currentLevelTargetScore;
    private int currentLevelStyleScore;
    private int currentLevelBonusScore;
    private TargetHitResult lastTargetHitResult;
    private bool fullTrajectoryPreviewEnabled;
    private int currentShotRicochets;
    private int currentShotStyleBonus;
    private bool currentShotStyleBonusCommitted;
    private int currentLevelMaxRewardedRicochetMirrors;
    private readonly HashSet<int> currentShotUniqueMirrorIds =
        new HashSet<int>();
    private readonly HashSet<string> currentLevelMedallionIds =
        new HashSet<string>();
    private readonly HashSet<string> currentLevelNewMedallionIds =
        new HashSet<string>();

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
    private readonly List<BonusProp> activeBonusProps =
        new List<BonusProp>();
    private readonly List<GoldenMedallionCollectible> activeCollectibles =
        new List<GoldenMedallionCollectible>();
    private readonly List<TargetGateSegment> activeGateSegments =
        new List<TargetGateSegment>();
    private readonly List<FlyingBirdTarget> activeFlyingBirds =
        new List<FlyingBirdTarget>();

    private readonly Dictionary<string, List<TargetGateSegment>>
        activeGatesByUnlockGroup =
            new Dictionary<string, List<TargetGateSegment>>();

    private readonly Dictionary<string, int>
        remainingBirdObjectivesByUnlockGroup =
            new Dictionary<string, int>();
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

        if (frontend != null)
        {
            medallionCampaignDecorator =
                ATSMedallionCampaignDecorator.Ensure(
                    frontend.gameObject);
        }

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

        if (levels != null &&
            levels.Length > 0)
        {
            ATSPlayerProgress.ReconcileUnlockedLevels(
                levels.Length);
        }

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

        ApplyRequiredCharacterForCurrentLevel();

        if (bow != null)
        {
            bow.Configure(
                config);
        }

        currentArcherGameplayTrait =
            ArcherGameplayTraitResolver
                .ResolveSelected();

        // Nerissa owns her ballistic curve, so the old straight/mirror FULL
        // PATH guide stays off while her gravity guide is active.
        if (currentArcherGameplayTrait != null &&
            currentArcherGameplayTrait.UsesGravityArc)
        {
            fullTrajectoryPreviewEnabled =
                false;
        }

        currentState = LevelState.Loading;
        shotsUsed = 0;
        currentLevelScore = 0;
        currentLevelTargetScore = 0;
        currentLevelStyleScore = 0;
        currentLevelBonusScore = 0;
        currentLevelMedallionIds.Clear();
        currentLevelNewMedallionIds.Clear();
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

    private void ApplyRequiredCharacterForCurrentLevel()
    {
        if (currentLevel == null ||
            string.IsNullOrWhiteSpace(
                currentLevel.RequiredCharacterId))
        {
            return;
        }

        ArcherCharacterRoster roster =
            ArcherCharacterRoster.LoadDefault();

        if (roster == null)
        {
            Debug.LogWarning(
                "LevelManager: Character roster is missing; " +
                $"cannot apply required character '{currentLevel.RequiredCharacterId}'.");
            return;
        }

        if (!roster.SelectCharacter(
                currentLevel.RequiredCharacterId))
        {
            Debug.LogWarning(
                "LevelManager: Required character '" +
                currentLevel.RequiredCharacterId +
                "' is not present in ArcherCharacterRoster.");
            return;
        }

        ArcherGameplayTraitResolver
            .InvalidateCache();
    }

    private void SpawnLevelObjects()
    {
        SpawnTargets();
        SpawnWalls();
        SpawnMirrors();
        SpawnBonusProps();
        SpawnCollectibles();
        SpawnGates();
        SpawnFlyingBirds();
    }

    private void SpawnTargets()
    {
        if (currentLevel.Targets == null)
            return;

        foreach (LevelData.TargetData data in
                 currentLevel.Targets)
        {
            if (data == null ||
                targetPrefab == null)
            {
                continue;
            }

            GameObject instance =
                InstantiateLevelPrefab(
                    targetPrefab,
                    data.Position,
                    data.Rotation,
                    data.Scale);

            if (instance == null ||
                !instance.TryGetComponent(
                    out Target target))
            {
                continue;
            }

            RegisterTarget(
                target,
                data.Style,
                data.Facing);
        }
    }

    private void SpawnWalls()
    {
        if (currentLevel.Walls == null)
            return;

        foreach (LevelData.WallData data in
                 currentLevel.Walls)
        {
            if (data == null ||
                wallPrefab == null)
            {
                continue;
            }

            GameObject instance =
                InstantiateLevelPrefab(
                    wallPrefab,
                    data.Position,
                    data.Rotation,
                    data.Scale);

            ApplyWallPresentation(
                instance);
        }
    }

    private void SpawnMirrors()
    {
        if (currentLevel.Mirrors == null)
            return;

        foreach (LevelData.MirrorData data in
                 currentLevel.Mirrors)
        {
            if (data == null ||
                mirrorPrefab == null)
            {
                continue;
            }

            InstantiateLevelPrefab(
                mirrorPrefab,
                data.Position,
                data.Rotation,
                data.Scale);
        }
    }

    private void SpawnBonusProps()
    {
        if (currentLevel.BonusProps == null)
            return;

        foreach (LevelData.BonusPropData data in
                 currentLevel.BonusProps)
        {
            if (data == null)
                continue;

            BonusProp bonusProp =
                BonusPropFactory.Create(
                    data,
                    levelObjectsParent);

            RegisterBonusProp(
                bonusProp);
        }
    }

    private void SpawnCollectibles()
    {
        if (currentLevel.Collectibles == null)
            return;

        foreach (LevelData.CollectibleData data in
                 currentLevel.Collectibles)
        {
            if (data == null)
                continue;

            RegisterCollectibleId(
                data.CollectibleId);

            GoldenMedallionCollectible collectible =
                CollectibleFactory.Create(
                    data,
                    levelObjectsParent);

            RegisterCollectible(
                collectible);
        }
    }

    private void SpawnGates()
    {
        if (currentLevel.Gates == null ||
            wallPrefab == null)
        {
            return;
        }

        foreach (LevelData.GateData data in
                 currentLevel.Gates)
        {
            if (data == null)
                continue;

            GameObject instance =
                InstantiateLevelPrefab(
                    wallPrefab,
                    data.Position,
                    data.Rotation,
                    data.Scale);

            if (instance == null)
                continue;

            instance.name =
                "TargetGate";

            ApplyWallPresentation(
                instance);

            TargetGateSegment gate =
                instance.GetComponent<TargetGateSegment>();

            if (gate == null)
            {
                gate =
                    instance.AddComponent<TargetGateSegment>();
            }

            gate.Configure(
                data.OpenOffset,
                data.OpenDuration);

            activeGateSegments.Add(
                gate);

            string unlockGroup =
                NormalizeUnlockGroupId(
                    data.UnlockGroupId);

            if (!activeGatesByUnlockGroup.TryGetValue(
                    unlockGroup,
                    out List<TargetGateSegment> gates))
            {
                gates =
                    new List<TargetGateSegment>();

                activeGatesByUnlockGroup.Add(
                    unlockGroup,
                    gates);
            }

            gates.Add(
                gate);
        }
    }

    private void SpawnFlyingBirds()
    {
        if (currentLevel.FlyingBirds == null)
            return;

        foreach (LevelData.FlyingBirdData data in
                 currentLevel.FlyingBirds)
        {
            if (data == null)
                continue;

            FlyingBirdTarget bird =
                FlyingBirdFactory.Create(
                    data,
                    levelObjectsParent);

            if (bird == null)
                continue;

            bird.Hit +=
                OnFlyingBirdHit;

            activeFlyingBirds.Add(
                bird);

            string unlockGroup =
                NormalizeUnlockGroupId(
                    data.UnlockGroupId);

            if (remainingBirdObjectivesByUnlockGroup
                    .TryGetValue(
                        unlockGroup,
                        out int currentCount))
            {
                remainingBirdObjectivesByUnlockGroup[
                    unlockGroup] =
                    currentCount + 1;
            }
            else
            {
                remainingBirdObjectivesByUnlockGroup.Add(
                    unlockGroup,
                    1);
            }
        }
    }

    private GameObject InstantiateLevelPrefab(
        GameObject prefab,
        Vector2 position,
        float rotation,
        Vector2 scale)
    {
        if (prefab == null)
            return null;

        GameObject instance =
            Instantiate(
                prefab,
                new Vector3(
                    position.x,
                    position.y,
                    0f),
                Quaternion.Euler(
                    0f,
                    0f,
                    rotation),
                levelObjectsParent);

        instance.transform.localScale =
            new Vector3(
                scale.x,
                scale.y,
                1f);

        return instance;
    }

    private void ApplyWallPresentation(
        GameObject instance)
    {
        if (instance == null)
            return;

        Wall wall =
            instance.GetComponent<Wall>();

        if (wall == null)
        {
            wall =
                instance.AddComponent<Wall>();
        }

        wall.ApplyPresentation();
    }

    private void RegisterTarget(
        Target target,
        LevelData.TargetStyle style,
        LevelData.TargetFacing facing)
    {
        if (target == null)
            return;

        target.ScoredHit +=
            OnTargetScoredHit;

        target.Hit +=
            OnTargetHit;

        target.InvalidHit +=
            OnTargetInvalidHit;

        activeTargets.Add(
            target);

        if (target.TryGetComponent(
                out TargetVisualFacing visualFacing))
        {
            visualFacing.ApplyVisual(
                style,
                facing,
                currentLevel.ArcherPosition.x);
        }
    }

    private void RegisterBonusProp(
        BonusProp bonusProp)
    {
        if (bonusProp == null)
            return;

        bonusProp.Hit +=
            OnBonusPropHit;

        activeBonusProps.Add(
            bonusProp);
    }

    private void RegisterCollectibleId(
        string collectibleId)
    {
        if (string.IsNullOrWhiteSpace(
                collectibleId))
        {
            return;
        }

        currentLevelMedallionIds.Add(
            collectibleId.Trim());
    }

    private void RegisterCollectible(
        GoldenMedallionCollectible collectible)
    {
        if (collectible == null)
            return;

        collectible.Collected +=
            OnCollectibleCollected;

        activeCollectibles.Add(
            collectible);
    }

    private static string NormalizeUnlockGroupId(
        string unlockGroupId)
    {
        return string.IsNullOrWhiteSpace(
                unlockGroupId)
            ? "default"
            : unlockGroupId.Trim();
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
        currentArrow.ResetArrow();
        ConfigureCurrentArrowGameplayTrait(
            currentArrow);

        currentArrow.SolidCollision += OnSolidCollision;
        currentArrow.Missed += OnMissed;
        currentArrow.Shot += OnShot;
        currentArrow.Reflected += OnArrowReflected;

        bow.SetArrow(currentArrow);
        ConfigureCharacterAimPreview(
            currentArrow);
        bow.SetInputEnabled(true);
        gameUI?.SetAimHintVisible(true);
    }

    private void ConfigureCurrentArrowGameplayTrait(
        ArrowController arrow)
    {
        if (arrow == null)
            return;

        CharacterProjectileTraitRuntime runtime =
            arrow.GetComponent<CharacterProjectileTraitRuntime>();

        if (runtime == null)
        {
            runtime =
                arrow.gameObject
                    .AddComponent<CharacterProjectileTraitRuntime>();
        }

        runtime.Configure(
            arrow,
            currentArcherGameplayTrait,
            bow);
    }

    private void ConfigureCharacterAimPreview(
        ArrowController arrow)
    {
        if (bow == null)
            return;

        if (gravityAimPreview == null)
        {
            gravityAimPreview =
                bow.GetComponent<CharacterGravityAimPreview>();

            if (gravityAimPreview == null)
            {
                gravityAimPreview =
                    bow.gameObject
                        .AddComponent<CharacterGravityAimPreview>();
            }

            gravityAimPreview.Bind(
                bow);
        }

        gravityAimPreview.Configure(
            arrow,
            currentArcherGameplayTrait,
            config);
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

        int earnedStars = CalculateStars(
            shotsUsed,
            currentLevel.MaxShots,
            currentLevel.ThreeStarShotLimit > 0
                ? currentLevel.ThreeStarShotLimit
                : 1,
            currentLevelMaxRewardedRicochetMirrors,
            GetRewardedUniqueMirrorCount());

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
            currentLevelBonusScore,
            hitLabel,
            isBullseye,
            isLastLevel,
            currentShotRicochets,
            GetRewardedUniqueMirrorCount(),
            GetCurrentLevelCollectedMedallionCount(),
            currentLevelMedallionIds.Count,
            currentLevelNewMedallionIds.Count);

        audioController?.PlayLevelComplete();
        resolutionRoutine = null;
    }

    private void OnCollectibleCollected(
        CollectibleHitResult result)
    {
        if (currentState != LevelState.Playing)
            return;

        string collectibleId =
            result.CollectibleId != null
                ? result.CollectibleId.Trim()
                : string.Empty;

        if (string.IsNullOrWhiteSpace(
                collectibleId))
        {
            return;
        }

        currentLevelMedallionIds.Add(
            collectibleId);

        bool newlyCollected =
            ATSPlayerProgress.RecordCollectible(
                collectibleId);

        if (newlyCollected)
        {
            currentLevelNewMedallionIds.Add(
                collectibleId);
        }

        audioController?.PlayCollectible(
            result.Style,
            newlyCollected);

        gameUI?.PlayCollectibleFeedback(
            result.Style,
            result.WorldPoint,
            newlyCollected);

        if (newlyCollected)
        {
            StartCoroutine(
                PremiumMedallionHapticSequence());
        }
        else
        {
            ATSHaptics.Pulse();
        }

        medallionCampaignDecorator?.RefreshNow();

        Debug.Log(
            newlyCollected
                ? $"GOLDEN MEDALLION COLLECTED: {collectibleId}"
                : $"GOLDEN MEDALLION ALREADY OWNED: {collectibleId}");
    }

    private IEnumerator PremiumMedallionHapticSequence()
    {
        ATSHaptics.Pulse();

        yield return
            new WaitForSecondsRealtime(
                0.14f);

        ATSHaptics.Pulse();
    }

    private int GetCurrentLevelCollectedMedallionCount()
    {
        int count = 0;

        foreach (string collectibleId in
                 currentLevelMedallionIds)
        {
            if (ATSPlayerProgress
                    .IsCollectibleCollected(
                        collectibleId))
            {
                count++;
            }
        }

        return count;
    }

    private void OnBonusPropHit(
        BonusPropHitResult result)
    {
        if (currentState != LevelState.Playing)
            return;

        currentLevelBonusScore +=
            result.Score;

        currentLevelScore +=
            result.Score;

        audioController?.PlayBonusProp(
            result.Style);

        gameUI?.PlayBonusPropFeedback(
            result.Style,
            result.Label,
            result.Score,
            result.WorldPoint);

        ATSHaptics.Pulse();

        Debug.Log(
            $"{result.Label} +{result.Score} BONUS " +
            $"(Level score: {currentLevelScore})");
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

    private void OnFlyingBirdHit(
        FlyingBirdHitResult result)
    {
        if (currentState != LevelState.Playing)
            return;

        string unlockGroup =
            NormalizeUnlockGroupId(
                result.UnlockGroupId);

        int remaining = 0;

        if (remainingBirdObjectivesByUnlockGroup
                .TryGetValue(
                    unlockGroup,
                    out int currentCount))
        {
            remaining =
                Mathf.Max(
                    0,
                    currentCount - 1);

            remainingBirdObjectivesByUnlockGroup[
                unlockGroup] =
                remaining;
        }

        bool gatesUnlocked =
            remaining <= 0;

        if (gatesUnlocked &&
            activeGatesByUnlockGroup.TryGetValue(
                unlockGroup,
                out List<TargetGateSegment> gates))
        {
            for (int i = 0;
                 i < gates.Count;
                 i++)
            {
                TargetGateSegment gate =
                    gates[i];

                if (gate != null)
                    gate.Open();
            }
        }

        gameUI?.PlayHitFeedback(
            gatesUnlocked
                ? "GATES OPEN!"
                : "GUARDIAN HIT!",
            0,
            false,
            false);

        audioController?.PlayTargetHit();
        ATSHaptics.Pulse();

        if (!result.ConsumesArrow)
            return;

        currentState =
            LevelState.ResolvingShot;

        bow.SetInputEnabled(false);
        currentArrow?.Stop();

        StopResolutionRoutine();

        resolutionRoutine =
            StartCoroutine(
                GuardianUnlockSequence());
    }

    private IEnumerator GuardianUnlockSequence()
    {
        // Let the bird reaction and the stone doors visibly finish before the
        // second-phase arrow is handed back to the player.
        yield return
            new WaitForSecondsRealtime(
                0.82f);

        DestroyCurrentArrow();

        int shotsRemaining =
            Mathf.Max(
                0,
                currentLevel.MaxShots -
                shotsUsed);

        if (shotsRemaining <= 0)
        {
            currentState =
                LevelState.Failed;

            gameUI.ShowFailed();
            audioController?.PlayLevelFailed();
            resolutionRoutine = null;
            yield break;
        }

        currentState =
            LevelState.Playing;

        CreateArrow();
        resolutionRoutine = null;
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

        if (bow != null)
        {
            bow.gameObject.SetActive(true);
        }

        // LoadLevel resolves any required level character first, then configures
        // the bow/runtime exactly once with the final character selection.
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

        if (currentLevel.MaxRewardedRicochetMirrors > 0)
        {
            return Mathf.Clamp(
                currentLevel.MaxRewardedRicochetMirrors,
                1,
                globalCap);
        }

        int authoredMirrorCount =
            currentLevel.Mirrors != null
                ? currentLevel.Mirrors.Length
                : 0;

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

    private static int CalculateStars(
        int usedShots,
        int maxShots,
        int threeStarShotLimit,
        int requiredUniqueMirrors,
        int usedUniqueMirrors)
    {
        // Completion on the final allowed arrow earns one star.
        if (usedShots >= maxShots)
            return 1;

        // The perfect-shot budget is fully data-driven per level.
        if (usedShots > threeStarShotLimit)
            return 2;

        bool masteryRouteSatisfied =
            requiredUniqueMirrors <= 0 ||
            usedUniqueMirrors >= requiredUniqueMirrors;

        return masteryRouteSatisfied
            ? 3
            : 2;
    }

    public void ToggleFullTrajectoryPreview()
    {
        if (currentState != LevelState.Playing)
            return;

        if (currentArcherGameplayTrait != null &&
            currentArcherGameplayTrait.UsesGravityArc)
        {
            // Nerissa's character-specific ballistic guide is already accurate
            // for her gravity motion. Do not display the old straight FULL PATH.
            fullTrajectoryPreviewEnabled =
                false;

            bow?.SetFullTrajectoryPreviewEnabled(
                false);

            gameUI?.SetFullTrajectoryPreviewEnabled(
                false);

            return;
        }

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

        // First completion of Level 6 gets a one-time Nerissa reveal before
        // returning to the campaign map. The teaser itself owns its persistence
        // key, presentation and transition back to the map.
        if (currentLevel != null &&
            NerissaTeaserController.ShouldShowAfterLevel(
                currentLevel.LevelNumber))
        {
            PrepareForFrontend();

            NerissaTeaserController.Show(
                this,
                config);

            return;
        }

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

        foreach (BonusProp bonusProp in
                 activeBonusProps)
        {
            if (bonusProp != null)
            {
                bonusProp.Hit -=
                    OnBonusPropHit;
            }
        }

        activeBonusProps.Clear();

        foreach (GoldenMedallionCollectible collectible in
                 activeCollectibles)
        {
            if (collectible != null)
            {
                collectible.Collected -=
                    OnCollectibleCollected;
            }
        }

        activeCollectibles.Clear();

        foreach (FlyingBirdTarget bird in
                 activeFlyingBirds)
        {
            if (bird != null)
            {
                bird.Hit -=
                    OnFlyingBirdHit;
            }
        }

        activeFlyingBirds.Clear();
        activeGateSegments.Clear();
        activeGatesByUnlockGroup.Clear();
        remainingBirdObjectivesByUnlockGroup.Clear();

        gravityAimPreview?.ClearArrow();

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
