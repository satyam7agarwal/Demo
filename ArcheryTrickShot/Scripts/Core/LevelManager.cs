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
    private TargetHitResult lastTargetHitResult;
    private bool fullTrajectoryPreviewEnabled;
    private int currentShotRicochets;

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
        lastTargetHitResult = default;
        currentShotRicochets = 0;

        bow.transform.position = new Vector3(
            currentLevel.ArcherPosition.x,
            currentLevel.ArcherPosition.y,
            bow.transform.position.z
        );
        bow.SetInputEnabled(false);

        CreateLevelParent();
        SpawnLevelObjects();

        gameUI.PrepareForLevel(currentLevel.LevelNumber, currentLevel.MaxShots);
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
        currentShotRicochets = 0;

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

        currentShotRicochets = 0;
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
        currentLevelScore += result.Score;

        gameUI.PlayHitFeedback(
            result.Label,
            result.Score,
            result.IsBullseye);

        Debug.Log(
            $"{result.Label} +{result.Score} " +
            $"(Level score: {currentLevelScore})");
    }

    private void OnTargetInvalidHit()
    {
        if (currentState != LevelState.Playing)
            return;

        // The arrow did contact the target, but not its valid scoring face
        // (for example rim/back/underside). Consume the shot as a miss while
        // using the target-impact sound rather than the wall-impact sound.
        audioController?.PlayTargetHit();
        ResolveFailedShot(false);
    }

    private void OnTargetHit()
    {
        if (currentState != LevelState.Playing)
            return;

        currentState = LevelState.ResolvingShot;
        bow.SetInputEnabled(false);
        currentArrow?.Stop();

        audioController?.PlayTargetHit();
        ATSHaptics.Pulse();
        gameFeel?.PlayHitFeedback(
            lastTargetHitResult.IsBullseye);

        StopResolutionRoutine();
        resolutionRoutine = StartCoroutine(CompleteLevelSequence());
    }

    private IEnumerator CompleteLevelSequence()
    {
        yield return new WaitForSecondsRealtime(config.HitResultDelay);

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
            hitLabel,
            lastTargetHitResult.IsBullseye,
            isLastLevel,
            currentShotRicochets);

        audioController?.PlayLevelComplete();
        resolutionRoutine = null;
    }

    private void OnArrowReflected()
    {
        if (currentState != LevelState.Playing)
            return;

        currentShotRicochets++;
        ATSHaptics.Pulse();
        audioController?.PlayMirror(currentShotRicochets);
        gameFeel?.PlayRicochetFeedback();
        gameUI?.PlayRicochetFeedback(currentShotRicochets);
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

        currentState = LevelState.ResolvingShot;
        bow.SetInputEnabled(false);
        currentArrow?.Stop();

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
        Time.timeScale = 1f;
        StopResolutionRoutine();
        ClearPreviousLevel();
    }
}
