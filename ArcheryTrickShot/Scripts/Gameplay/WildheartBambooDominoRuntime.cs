using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Data-driven bamboo domino puzzle. A LevelData opts in by shipping a
/// Resources/Levels/BambooDomino_NNN JSON file (NNN = LevelData.LevelNumber).
/// No scene edits, per-level code, prefab modifications, or physics overrides.
///
/// Stage 1: hit the broad, SOLID first stalk with an actual fired arrow.
/// Stage 2: the stalks topple in an overlapping, deterministic chain.
/// Stage 3: only after the last stalk stops moving, its sloped upper shaft
/// becomes a Mirror trigger, driven by the EXISTING ArrowController reflection
/// and AimTrajectoryRenderer full-path cast. No custom bounce calculations.
/// </summary>
[DefaultExecutionOrder(1760)]
[DisallowMultipleComponent]
public sealed class WildheartBambooDominoRuntime : MonoBehaviour
{
    private const string ConfigPrefix = "Levels/BambooDomino_";
    private const string ThumpClipPath = "Audio/SFX/Wildheart_BambooThump";
    private const string CrackClipPath = "Audio/SFX/Wildheart_BambooCrack";
    private const string ChimeClipPath = "Audio/SFX/Wildheart_BambooReady";
    private static readonly FieldInfo CurrentLevelField = typeof(LevelManager)
        .GetField("currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    // Use LevelManager's live arrow; do not guess a shot from the aim guide.
    private static readonly FieldInfo CurrentArrowField = typeof(LevelManager)
        .GetField("currentArrow", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Color ReadyGold = new Color(1f, 0.85f, 0.33f, 1f);
    private static readonly FieldInfo UiViewField = typeof(GameUIController)
        .GetField("view", BindingFlags.Instance | BindingFlags.NonPublic);

    [Serializable]
    private sealed class StageConfiguration
    {
        public string MechanismId = "bamboo_domino";
        public int FirstShootableIndex = 0;
        public int ReflectorIndex = 2;
        public float HandoffSeconds = 0.31f;
        public float ReflectorThickness = 0.22f;
        public float ReflectorLengthFraction = 0.79f;
        public string ChainStartedFeedback = "BAMBOO DOMINO!";
        public string CompletionTitle = "BAMBOO DOMINO!";
        public string RicochetNoun = "BAMBOO BOUNCE";
        public string UniqueMirrorNoun = "BAMBOO REFLECTOR";
        public StalkConfiguration[] Stalks;
    }

    [Serializable]
    private sealed class StalkConfiguration
    {
        public string Id;
        public Vector2 Pivot;
        public float Length = 3.6f;
        public float Width = 0.68f;
        public float VisualWidthMultiplier = 1.6f;
        public float FinalAngleDegrees = -69f;
        public float FallDuration = 0.61f;
        public string SpriteResourcePath;
        public int SortingOrder = 9;
    }

    private sealed class Stalk
    {
        public StalkConfiguration Data;
        public Transform Root;
        public SpriteRenderer Artwork;
        public BoxCollider2D Solid;
        public GameObject Reflector;
        public LineRenderer Glint;
        public ParticleSystem Splinters;
        public bool Fallen;
    }

    private Transform observedLevelObjects;
    private StageConfiguration activeConfiguration;
    private readonly List<Stalk> stalks = new List<Stalk>(5);
    private AudioSource audioSource;
    private AudioClip crackClip;
    private AudioClip thumpClip;
    private AudioClip chimeClip;
    private bool chainStarted;
    private int animationsActive;
    private static Material lineMaterial;
    private LevelManager manager;
    private BowController stageBow;
    private GameUIView resultView;
    private bool resultLabelsApplied;
    private bool reflectorReady;
    private ArrowController observedArrow;
    private Coroutine bounceFlashRoutine;
    private float bounceFlashUntil;
    private bool savedHint;
    private string savedHintText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager levelManager = FindFirstObjectByType<LevelManager>();
        if (levelManager != null &&
            levelManager.GetComponent<WildheartBambooDominoRuntime>() == null)
        {
            levelManager.gameObject.AddComponent<WildheartBambooDominoRuntime>();
        }
    }

    private void Awake()
    {
        manager = GetComponent<LevelManager>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.72f;
        crackClip = Resources.Load<AudioClip>(CrackClipPath);
        thumpClip = Resources.Load<AudioClip>(ThumpClipPath);
        chimeClip = Resources.Load<AudioClip>(ChimeClipPath);
    }

    private void LateUpdate()
    {
        Transform objects = transform.Find("LevelObjects");
        if (objects == null)
        {
            observedLevelObjects = null;
            return;
        }

        if (observedLevelObjects != objects)
        {
            observedLevelObjects = objects;
            SpawnForCurrentLevel(objects);
        }

        // An early second shot would be unfair while the reflector is still
        // physically absent. Lock the next shot until the cascade finishes.
        // LevelManager may create the next arrow mid-animation and re-enable
        // input; keep the lock active until the final shaft is stable.
        if (chainStarted && animationsActive > 0 && stageBow != null)
            stageBow.SetInputEnabled(false);
        if (chainStarted && !reflectorReady)
            ShowPositiveChainFeedback();
        if (activeConfiguration != null)
            UpdateBambooHint();
        if (reflectorReady)
            ObserveActualArrow();
        if (chainStarted && !resultLabelsApplied)
            ApplyResultLabels();

        // Decoration only; the active reflective collider NEVER moves.
        if (activeConfiguration != null && !chainStarted)
            return;
        for (int i = 0; i < stalks.Count; i++)
        {
            LineRenderer glint = stalks[i].Glint;
            if (glint == null || !glint.enabled)
                continue;
            float alpha = 0.77f + 0.20f * Mathf.Sin(Time.unscaledTime * 4.6f);
            float hitFlash = Time.unscaledTime < bounceFlashUntil ? 1f : 0f;
            // Amber is intentionally distinct from the neighbouring jade shafts.
            glint.startColor = Color.Lerp(
                new Color(1f, 0.65f, 0.11f, alpha),
                new Color(1f, 0.98f, 0.69f, 1f), hitFlash);
            glint.endColor = Color.Lerp(
                new Color(1f, 0.91f, 0.27f, alpha * 0.88f),
                Color.white, hitFlash);
            if (activeConfiguration != null &&
                i == activeConfiguration.ReflectorIndex && stalks[i].Artwork != null)
            {
                stalks[i].Artwork.color = Color.Lerp(
                    ReadyGold, Color.white, hitFlash);
            }
        }
    }

    private void SpawnForCurrentLevel(Transform parent)
    {
        // Reset the HUD and live-arrow subscription before level replacement.
        RestoreBambooHint();
        UnobserveActualArrow();
        // The entire mechanic is scoped to a LevelObjects lifetime, not the scene.
        StopAllCoroutines();
        stalks.Clear();
        activeConfiguration = null;
        animationsActive = 0;
        chainStarted = false;
        stageBow = null;
        resultView = null;
        resultLabelsApplied = false;
        reflectorReady = false;
        bounceFlashRoutine = null;
        bounceFlashUntil = 0f;

        LevelData level = CurrentLevelField?.GetValue(manager) as LevelData;
        if (level == null)
            return;

        TextAsset authored = Resources.Load<TextAsset>(
            ConfigPrefix + level.LevelNumber.ToString("000"));
        if (authored == null)
            return;

        StageConfiguration stage;
        try { stage = JsonUtility.FromJson<StageConfiguration>(authored.text); }
        catch (Exception ex)
        {
            Debug.LogError("[Bamboo Domino] Invalid config: " + ex.Message, parent);
            return;
        }

        if (stage == null || stage.Stalks == null || stage.Stalks.Length < 2 ||
            stage.FirstShootableIndex < 0 || stage.FirstShootableIndex >= stage.Stalks.Length ||
            stage.ReflectorIndex < 0 || stage.ReflectorIndex >= stage.Stalks.Length ||
            stage.ReflectorIndex != stage.Stalks.Length - 1)
        {
            Debug.LogError("[Bamboo Domino] Invalid stalk indices or count in " +
                authored.name, parent);
            return;
        }

        // Load/validate ALL sprites before creating physics. An incomplete art
        // install should not spawn a game-breaking invisible collision obstacle.
        Sprite[] sprites = new Sprite[stage.Stalks.Length];
        for (int i = 0; i < stage.Stalks.Length; i++)
        {
            if (stage.Stalks[i] == null ||
                stage.Stalks[i].Length <= 0.5f || stage.Stalks[i].Width <= 0.2f ||
                string.IsNullOrEmpty(stage.Stalks[i].SpriteResourcePath))
            {
                Debug.LogError("[Bamboo Domino] Invalid stalk " + i, parent);
                return;
            }
            sprites[i] = Resources.Load<Sprite>(stage.Stalks[i].SpriteResourcePath);
            if (sprites[i] == null)
            {
                Debug.LogError("[Bamboo Domino] Missing sprite at Resources/" +
                    stage.Stalks[i].SpriteResourcePath, parent);
                return;
            }
        }

        activeConfiguration = stage;
        stageBow = FindFirstObjectByType<BowController>();
        GameUIController uiController = FindFirstObjectByType<GameUIController>();
        resultView = uiController != null && UiViewField != null
            ? UiViewField.GetValue(uiController) as GameUIView : null;
        if (resultView != null && resultView.AimHintText != null)
        {
            savedHintText = resultView.AimHintText.text;
            savedHint = true;
        }
        GameObject root = new GameObject("BambooDomino_" + stage.MechanismId);
        root.transform.SetParent(parent, false);
        for (int i = 0; i < stage.Stalks.Length; i++)
            stalks.Add(BuildStalk(root.transform, stage.Stalks[i], sprites[i],
                i, stage));

        // FULL PATH remains a player-controlled option. Never draw an example
        // solution or silently force the green trajectory assist ON.
        Physics2D.SyncTransforms();
    }

    private Stalk BuildStalk(Transform parent, StalkConfiguration data,
        Sprite sprite, int index, StageConfiguration stage)
    {
        GameObject stem = new GameObject("Bamboo_" + index + "_" + data.Id);
        stem.transform.SetParent(parent, false);
        stem.transform.position = new Vector3(data.Pivot.x, data.Pivot.y, 0f);
        stem.transform.rotation = Quaternion.identity;

        GameObject art = new GameObject("AuthoredBambooArtwork", typeof(SpriteRenderer));
        art.transform.SetParent(stem.transform, false);
        art.transform.localPosition = new Vector3(0f, data.Length * 0.5f, 0.025f);
        SpriteRenderer sr = art.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = Color.white;
        sr.sortingOrder = data.SortingOrder;
        float fit = data.Length / Mathf.Max(0.01f, sprite.bounds.size.y);
        // Artwork includes thin transparent leaf branches. The controllable
        // horizontal fit makes the visible main bamboo stalk match the real
        // solid hitbox, so aim dots do not collide with invisible air.
        art.transform.localScale = new Vector3(
            fit * Mathf.Clamp(data.VisualWidthMultiplier, 0.5f, 3f), fit, 1f);

        // SOLID full-height stalk. Physical hits and the green line use the
        // same BoxCollider2D. Stalks become nonblocking as they topple.
        BoxCollider2D solid = stem.AddComponent<BoxCollider2D>();
        solid.isTrigger = false;
        solid.size = new Vector2(data.Width, data.Length * 0.88f);
        solid.offset = new Vector2(0f, data.Length * 0.53f);
        BambooDominoHitReceiver hit = stem.AddComponent<BambooDominoHitReceiver>();
        hit.Configure(this, index);

        Stalk result = new Stalk
        {
            Data = data, Root = stem.transform, Artwork = sr, Solid = solid
        };
        result.Splinters = CreateParticleEffect(stem.transform);

        if (index == stage.ReflectorIndex)
        {
            GameObject surface = new GameObject("BambooReflectiveShaft");
            surface.SetActive(false); // Do not predict a bounce before it settles.
            surface.transform.SetParent(stem.transform, false);
            surface.transform.localPosition = new Vector3(0f,
                data.Length * 0.54f, 0f);
            BoxCollider2D trigger = surface.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(
                Mathf.Max(0.08f, stage.ReflectorThickness),
                data.Length * Mathf.Clamp(stage.ReflectorLengthFraction, 0.4f, 0.9f));
            surface.AddComponent<Mirror>();
            result.Reflector = surface;
            result.Glint = CreateReflectionGlint(stem.transform, data.Length);
        }
        return result;
    }

    private static LineRenderer CreateReflectionGlint(Transform parent, float length)
    {
        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;
            lineMaterial = new Material(shader) { name = "BambooReflectiveGlint",
                hideFlags = HideFlags.DontSave };
        }
        GameObject obj = new GameObject("BambooReflectionWake");
        obj.transform.SetParent(parent, false);
        LineRenderer r = obj.AddComponent<LineRenderer>();
        r.sharedMaterial = lineMaterial;
        r.useWorldSpace = false;
        r.positionCount = 2;
        r.SetPosition(0, new Vector3(0.03f, 0.5f, -0.1f));
        r.SetPosition(1, new Vector3(0.03f, length - 0.18f, -0.1f));
        // Broad gold highlight: players shoot ANYWHERE on the illuminated
        // upper shaft; it must not look like a tiny hidden ring target.
        r.startWidth = 0.24f;
        r.endWidth = 0.18f;
        r.numCapVertices = 3;
        r.sortingOrder = 15;
        r.enabled = false;
        return r;
    }

    private static ParticleSystem CreateParticleEffect(Transform parent)
    {
        // Configure on INACTIVE object: Unity 6 auto-plays a new ParticleSystem
        // attached to an active object, then rejects changes to main.duration.
        GameObject go = new GameObject("BambooWoodSplinters");
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0.25f, -0.12f);
        ParticleSystem effect = go.AddComponent<ParticleSystem>();
        var main = effect.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.75f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.83f, 0.35f, 0.92f),
            new Color(0.40f, 0.88f, 0.39f, 0.92f));
        main.gravityModifier = 0.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;
        var emission = effect.emission;
        emission.rateOverTime = 0f;
        var shape = effect.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.15f;
        ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.sortingOrder = 17;
        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.SetActive(true);
        return effect;
    }

    internal void ReportArrowHit(int index, ArrowController arrow)
    {
        if (activeConfiguration == null || chainStarted || arrow == null ||
            !arrow.HasFired || index != activeConfiguration.FirstShootableIndex)
            return;
        chainStarted = true;
        if (audioSource != null && crackClip != null)
            audioSource.PlayOneShot(crackClip, 0.95f);
        else
            GameAudioController.Instance?.PlayWallHit();
        ATSHaptics.Pulse();
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        Vector2 point = stalks[index].Root != null
            ? (Vector2)stalks[index].Root.position + Vector2.up *
                (stalks[index].Data.Length * 0.46f)
            : Vector2.zero;
        ui?.PlayBonusPropFeedback(LevelData.BonusPropStyle.Bell,
            activeConfiguration.ChainStartedFeedback, 0, point);
        StartCoroutine(PlayDominoCascade());
    }

    private IEnumerator PlayDominoCascade()
    {
        StageConfiguration stage = activeConfiguration;
        for (int i = 0; i < stalks.Count; i++)
        {
            if (stalks[i] == null || stalks[i].Root == null)
                break;
            StartCoroutine(ToppleOne(stalks[i], i));
            if (i < stalks.Count - 1)
                yield return new WaitForSeconds(
                    Mathf.Max(0.14f, stage.HandoffSeconds));
        }

        while (animationsActive > 0)
            yield return null;

        if (stage != activeConfiguration || stalks.Count <= stage.ReflectorIndex)
            yield break;
        Stalk final = stalks[stage.ReflectorIndex];
        // Collider is enabled only AFTER final transform is completely stable.
        if (final.Root != null && final.Reflector != null)
        {
            final.Reflector.SetActive(true);
            reflectorReady = true;
            if (final.Artwork != null)
                final.Artwork.color = ReadyGold;
            if (final.Glint != null)
                final.Glint.enabled = true;
            // Subscribe before the next shot is enabled so the first bounce
            // cannot be missed, including fast touch-release input.
            ObserveActualArrow();
            if (final.Splinters != null)
                final.Splinters.Emit(25);
            if (audioSource != null && chimeClip != null)
                audioSource.PlayOneShot(chimeClip, 0.82f);
            Physics2D.SyncTransforms();
            // A new collider must invalidate the green path's cache, even if
            // the player is holding their aim perfectly still.
            if (stageBow != null)
            {
                stageBow.SetInputEnabled(true);
                if (stageBow.FullTrajectoryPreviewEnabled)
                {
                    stageBow.SetFullTrajectoryPreviewEnabled(false);
                    stageBow.SetFullTrajectoryPreviewEnabled(true);
                }
            }
            Debug.Log("[Bamboo Domino] Final stalk is down; reflective bamboo ready.");
        }
    }

    private void ObserveActualArrow()
    {
        if (CurrentArrowField == null || manager == null)
            return;

        ArrowController liveArrow =
            CurrentArrowField.GetValue(manager) as ArrowController;
        if (ReferenceEquals(liveArrow, observedArrow))
            return;

        UnobserveActualArrow();
        observedArrow = liveArrow;
        if (!ReferenceEquals(observedArrow, null))
            observedArrow.Reflected += OnActualArrowReflected;
    }

    private void UnobserveActualArrow()
    {
        if (!ReferenceEquals(observedArrow, null))
            observedArrow.Reflected -= OnActualArrowReflected;
        observedArrow = null;
    }

    private void OnActualArrowReflected(
        int chainCount, Collider2D sourceCollider, Vector2 contactPoint)
    {
        if (!reflectorReady || activeConfiguration == null ||
            sourceCollider == null || stalks.Count <= activeConfiguration.ReflectorIndex)
            return;

        Stalk gold = stalks[activeConfiguration.ReflectorIndex];
        if (gold == null || gold.Reflector == null ||
            sourceCollider.gameObject != gold.Reflector)
            return;

        // Reflected fires ONLY after ArrowController accepts the real ricochet;
        // touching a stem, missing the shaft, or drawing a green guide cannot
        // trigger this confirmation.
        bounceFlashUntil = Time.unscaledTime + 0.40f;
        if (gold.Splinters != null)
        {
            gold.Splinters.transform.position = new Vector3(
                contactPoint.x, contactPoint.y, gold.Splinters.transform.position.z);
            gold.Splinters.Emit(33);
        }

        if (audioSource != null && chimeClip != null)
        {
            audioSource.pitch = 1.22f;
            audioSource.PlayOneShot(chimeClip, 0.98f);
            audioSource.pitch = 1f;
        }
        ATSHaptics.Pulse();

        GameUIController ui = FindFirstObjectByType<GameUIController>();
        ui?.PlayBonusPropFeedback(
            LevelData.BonusPropStyle.Bell,
            "BAMBOO BOUNCE!", 0, contactPoint);

        if (bounceFlashRoutine != null)
            StopCoroutine(bounceFlashRoutine);
        bounceFlashRoutine = StartCoroutine(FlashBounceShaft(gold));
    }

    private IEnumerator FlashBounceShaft(Stalk gold)
    {
        // Animation affects only artwork: never reposition or resize the
        // reflective collider, so physical arrow and green prediction agree.
        if (gold == null || gold.Artwork == null)
            yield break;
        Transform visual = gold.Artwork.transform;
        Vector3 baseScale = visual.localScale;
        float elapsed = 0f;
        const float duration = 0.35f;
        while (elapsed < duration && visual != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = 1f + 0.045f * Mathf.Sin(Mathf.PI * t);
            visual.localScale = baseScale * punch;
            yield return null;
        }
        if (visual != null)
            visual.localScale = baseScale;
        bounceFlashRoutine = null;
    }

    private void UpdateBambooHint()
    {
        if (resultView == null || resultView.AimHintText == null)
            return;
        if (!chainStarted)
            resultView.AimHintText.text = "HIT AMBER BAMBOO";
        else if (!reflectorReady)
            resultView.AimHintText.text = "WATCH BAMBOO FALL";
        else
            resultView.AimHintText.text = "GOLD BAMBOO READY!";
    }

    private void RestoreBambooHint()
    {
        if (savedHint && resultView != null && resultView.AimHintText != null)
            resultView.AimHintText.text = savedHintText;
        savedHint = false;
        savedHintText = null;
    }

    private IEnumerator ToppleOne(Stalk stalk, int index)
    {
        animationsActive++;
        // The domino reaction is visual/kinematic and deliberately repeatable.
        // Its upright physical collider is disabled BEFORE its pivot animates,
        // preventing moving-collider vs trajectory-preview disagreements.
        if (stalk.Solid != null)
            stalk.Solid.enabled = false;
        if (stalk.Splinters != null)
            stalk.Splinters.Emit(index == 0 ? 28 : 16);

        float end = stalk.Data.FinalAngleDegrees;
        float duration = Mathf.Max(0.22f, stalk.Data.FallDuration);
        float elapsed = 0f;
        while (elapsed < duration && stalk.Root != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            // Small angular overshoot settles the heavy stalk without jitter.
            float angle = Mathf.Lerp(0f, end, eased) - 2.5f * Mathf.Sin(t * Mathf.PI);
            stalk.Root.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        if (stalk.Root != null)
            stalk.Root.localRotation = Quaternion.Euler(0f, 0f, end);
        stalk.Fallen = true;
        if (audioSource != null && thumpClip != null)
        {
            audioSource.pitch = 1f - index * 0.08f;
            audioSource.PlayOneShot(thumpClip, 0.78f + index * 0.06f);
        }
        if (stalk.Splinters != null)
            stalk.Splinters.Emit(22);
        ATSHaptics.Pulse();
        animationsActive--;
    }

    private void ShowPositiveChainFeedback()
    {
        // The existing LevelManager correctly deducts the setup arrow and
        // prepares the next one through its solid-collision lifecycle. This
        // strike IS a successful puzzle action, however, not a missed aim.
        // Scope the text-only relabel to the short cascade window so a later
        // genuinely missed shot still says MISS as it should.
        if (resultView == null || resultView.FeedbackText == null ||
            activeConfiguration == null)
            return;
        string text = resultView.FeedbackText.text;
        // The setup shot is intentional. Prevent the old "MISS" pink flash
        // from making the successful bamboo cascade feel like a failure.
        if (resultView.ScreenFlash != null &&
            resultView.ScreenFlash.gameObject.activeSelf)
        {
            Color flash = resultView.ScreenFlash.color;
            resultView.ScreenFlash.color = new Color(0.52f, 0.95f, 0.42f, flash.a);
        }
        if (string.IsNullOrEmpty(text) ||
            !text.StartsWith("MISS", StringComparison.Ordinal))
            return;
        resultView.FeedbackText.text = activeConfiguration.ChainStartedFeedback +
            text.Substring(4);
        resultView.FeedbackText.color = new Color(1f, 0.86f, 0.42f, 1f);
    }

    private void ApplyResultLabels()
    {
        // Presentation-only adapter for the existing shared completion card.
        // No source rewrite, scene dependency or global UI state change. The
        // text comes from JSON, and the card changes only for this level's
        // active domino mechanism. Gameplay/scoring never depends on this.
        if (resultView == null || resultView.ResultOverlay == null ||
            !resultView.ResultOverlay.gameObject.activeInHierarchy ||
            resultView.ResultInfo == null || activeConfiguration == null)
            return;

        string original = resultView.ResultInfo.text;
        if (string.IsNullOrEmpty(original) ||
            original.IndexOf("UNIQUE MIRROR", StringComparison.Ordinal) < 0)
            return;

        string mirrorNoun = string.IsNullOrWhiteSpace(activeConfiguration.UniqueMirrorNoun)
            ? "BAMBOO REFLECTOR" : activeConfiguration.UniqueMirrorNoun;
        string bounceNoun = string.IsNullOrWhiteSpace(activeConfiguration.RicochetNoun)
            ? "BAMBOO BOUNCE" : activeConfiguration.RicochetNoun;
        string updated = Regex.Replace(original, @"(\d+) UNIQUE MIRRORS?",
            m => m.Groups[1].Value + " " + mirrorNoun +
                (m.Groups[1].Value == "1" ? "" : "S"));
        updated = Regex.Replace(updated, @"(\d+) RICOCHETS?",
            m => m.Groups[1].Value + " " + bounceNoun +
                (m.Groups[1].Value == "1" ? "" : "S"));
        resultView.ResultInfo.text = updated;
        if (resultView.ResultTitle != null &&
            !string.IsNullOrWhiteSpace(activeConfiguration.CompletionTitle))
            resultView.ResultTitle.text = activeConfiguration.CompletionTitle;
        resultLabelsApplied = true;
    }

    private void OnDisable()
    {
        RestoreBambooHint();
        UnobserveActualArrow();
        StopAllCoroutines();
        stalks.Clear();
        activeConfiguration = null;
        observedLevelObjects = null;
        stageBow = null;
        resultView = null;
        resultLabelsApplied = false;
        reflectorReady = false;
        bounceFlashRoutine = null;
        bounceFlashUntil = 0f;
        chainStarted = false;
        animationsActive = 0;
    }
}

