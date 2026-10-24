using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic, data-driven suspended-target puzzle. An actual arrow must break the
/// visible holding vine; the physical target and wooden cradle then swing
/// together. The target's EXISTING prefab scoring sensors move with the target.
///
/// The target is physically screened while latched, so the setup action cannot
/// be skipped. After release, the pendulum NEVER freezes for an airborne arrow:
/// the player must genuinely lead and intercept the moving target. The ordinary
/// green trajectory predicts the ARROW'S route, not the future position of a
/// moving target; it must not be treated as an intercept guarantee.
///
/// Opt-in through Resources/Levels/VineSwing_NNN.json. Nothing here depends on
/// a hardcoded level number or changes global arrow/scoring behavior.
/// </summary>
[DefaultExecutionOrder(1740)]
[DisallowMultipleComponent]
public sealed class WildheartVineSwingRuntime : MonoBehaviour
{
    private const string ConfigPrefix = "Levels/VineSwing_";
    private static readonly FieldInfo CurrentLevelField = typeof(LevelManager)
        .GetField("currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CurrentArrowField = typeof(LevelManager)
        .GetField("currentArrow", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ViewField = typeof(GameUIController)
        .GetField("view", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PredictorField = typeof(BowController)
        .GetField("trajectoryRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo InvalidateMethod = typeof(AimTrajectoryRenderer)
        .GetMethod("InvalidatePreviewCache", BindingFlags.Instance | BindingFlags.NonPublic);

    [Serializable]
    private sealed class Config
    {
        public string MechanismId = "jungle_pendulum";
        public Vector2 Pivot = new Vector2(3.7f, 3.2f);
        public float RopeLength = 5.28f;
        public float SwingAngleDegrees = 34f;
        public float SwingPeriodSeconds = 4.15f;
        public float TargetRiseAboveCradle = 0.88f;
        public float CradleWorldWidth = 3.1f;
        public float CradleWorldHeight = 1.2f;
        public Vector2 HoldingVinePosition = new Vector2(0.1f, 0.65f);
        public float HoldingVineWorldHeight = 3.45f;
        public float HoldingVineWorldWidth = 0.66f;
        public float HoldingVineColliderWidth = 0.63f;
        public float HoldingVineColliderHeight = 3.12f;
        public float HeldScreenWidth = 1.34f;
        public float HeldScreenHeight = 2.45f;
        public float HeldScreenOffsetX = -0.72f;
        public int LeafCount = 18;
        public string CradleSpritePath;
        public string ScreenSpritePath;
        public string AnchorSpritePath;
        public string HoldingVineSpritePath;
        public string LeafSpritePath;
        public string SplashSpritePath;
        public string SnapAudioPath;
        public string SwingAudioPath;
        public string SplashAudioPath;
        public string WaitingHint = "CUT THE HOLDING VINE";
        public string ReleasedHint = "TIME THE SWING";
    }

    private sealed class Leaf
    {
        public Transform Root;
        public SpriteRenderer Renderer;
        public Vector2 Velocity;
        public float RotationSpeed;
        public float Delay;
        public float Lifetime;
    }

    private LevelManager manager;
    private Transform observedObjects;
    private Config config;
    private Target carriedTarget;
    private Transform targetTransform;
    private BowController bow;
    private AimTrajectoryRenderer predictor;
    private GameUIView view;
    private string originalHint;
    private bool originalHintCaptured;
    private Transform cradleTransform;
    private SpriteRenderer cradleRenderer;
    private BoxCollider2D cradleCollider;
    private SpriteRenderer holdingVineRenderer;
    private BoxCollider2D holdingVineCollider;
    private SpriteRenderer screenRenderer;
    private BoxCollider2D screenCollider;
    private SpriteRenderer splashRenderer;
    private LineRenderer[] suspensionRopes;
    private LineRenderer retainingVine;
    private AudioSource sound;
    private AudioClip snapAudio;
    private AudioClip swingAudio;
    private AudioClip splashAudio;
    private readonly List<Leaf> leaves = new List<Leaf>(24);
    private float releasedAt;
    private bool released;
    private bool completed;
    private bool usedSplash;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager levelManager = FindFirstObjectByType<LevelManager>();
        if (levelManager != null &&
            levelManager.GetComponent<WildheartVineSwingRuntime>() == null)
            levelManager.gameObject.AddComponent<WildheartVineSwingRuntime>();
    }

    private void Awake() { manager = GetComponent<LevelManager>(); }

    private void LateUpdate()
    {
        Transform objects = transform.Find("LevelObjects");
        if (objects != observedObjects)
        {
            ResetRuntime();
            observedObjects = objects;
            if (objects != null)
                SpawnForLevel(objects);
        }
        if (config == null)
            return;

        if (!released && holdingVineRenderer != null)
        {
            float g = 0.82f + 0.18f * Mathf.Sin(Time.time * 4.4f);
            holdingVineRenderer.color = new Color(1f, g, 0.59f, 1f);
        }
        if (released)
        {
            AnimateLeaves();
            // Only relabel the intentional FIRST setup shot. The next shot
            // cannot occur until the curtain clears (0.68 s), so this window
            // cannot suppress an actual later miss.
            if (Time.time - releasedAt < 0.64f)
                ConvertSetupMissToSuccess();
            // The first arrow is already consumed, but the new shot must not
            // start while the decorative shutter is still flying away.
            if (bow != null && Time.time - releasedAt < 0.68f)
                bow.SetInputEnabled(false);
            else if (bow != null && Time.time - releasedAt >= 0.68f &&
                     Time.time - releasedAt < 0.75f)
            {
                ArrowController held = CurrentArrowField != null && manager != null
                    ? CurrentArrowField.GetValue(manager) as ArrowController : null;
                if (held != null && !held.HasFired)
                    bow.SetInputEnabled(true);
            }
        }
        UpdateHud();

        if (released && !completed && bow != null && bow.IsAiming &&
            bow.FullTrajectoryPreviewEnabled && predictor != null &&
            InvalidateMethod != null)
        {
            InvalidateMethod.Invoke(predictor, null);
        }
    }

    private void FixedUpdate()
    {
        if (config == null || cradleTransform == null || targetTransform == null || completed)
            return;
        if (released)
        {
            // Do NOT pause during flight. In a moving-target puzzle, freezing
            // the target on firing makes timing trivial and visibly unnatural.
            // The target's scoring colliders are moved with the artwork by
            // PositionCradle() and physics transforms are synchronized there.
            float elapsed = Time.time - releasedAt;
            float period = Mathf.Max(2.9f, config.SwingPeriodSeconds);
            float theta = config.SwingAngleDegrees *
                Mathf.Cos(elapsed * 2f * Mathf.PI / period);
            PositionCradle(theta);
            if (!usedSplash && elapsed >= period * 0.20f)
            {
                usedSplash = true;
                PlaySplash();
            }
        }
        else if (!released)
        {
            PositionCradle(config.SwingAngleDegrees);
        }
    }

    private void SpawnForLevel(Transform levelObjects)
    {
        LevelData level = CurrentLevelField != null && manager != null
            ? CurrentLevelField.GetValue(manager) as LevelData : null;
        if (level == null)
            return;
        TextAsset resource = Resources.Load<TextAsset>(
            ConfigPrefix + level.LevelNumber.ToString("000"));
        if (resource == null)
            return;
        Config cfg;
        try { cfg = JsonUtility.FromJson<Config>(resource.text); }
        catch (Exception exception)
        {
            Debug.LogError("[Vine Swing] Invalid JSON: " + exception.Message, levelObjects);
            return;
        }
        if (cfg == null || cfg.RopeLength < 3f ||
            cfg.SwingPeriodSeconds < 2f || cfg.SwingAngleDegrees <= 8f ||
            cfg.SwingAngleDegrees > 65f || cfg.HoldingVineColliderWidth < 0.3f)
        {
            Debug.LogError("[Vine Swing] Invalid pendulum configuration.", levelObjects);
            return;
        }
        Sprite cradle = Resources.Load<Sprite>(cfg.CradleSpritePath);
        Sprite screen = Resources.Load<Sprite>(cfg.ScreenSpritePath);
        Sprite anchor = Resources.Load<Sprite>(cfg.AnchorSpritePath);
        Sprite vine = Resources.Load<Sprite>(cfg.HoldingVineSpritePath);
        Sprite leaf = Resources.Load<Sprite>(cfg.LeafSpritePath);
        Sprite splash = string.IsNullOrWhiteSpace(cfg.SplashSpritePath) ? null :
            Resources.Load<Sprite>(cfg.SplashSpritePath);
        if (cradle == null || screen == null || anchor == null || vine == null || leaf == null)
        {
            Debug.LogError("[Vine Swing] Required art missing; no invisible blockers created.", levelObjects);
            return;
        }
        carriedTarget = levelObjects.GetComponentInChildren<Target>(true);
        if (carriedTarget == null)
        {
            Debug.LogError("[Vine Swing] Level has no target prefab to carry.", levelObjects);
            return;
        }
        targetTransform = carriedTarget.transform;
        carriedTarget.Hit += OnTargetHit;
        config = cfg;
        bow = FindFirstObjectByType<BowController>();
        predictor = bow != null && PredictorField != null
            ? PredictorField.GetValue(bow) as AimTrajectoryRenderer : null;
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        view = ui != null && ViewField != null ? ViewField.GetValue(ui) as GameUIView : null;
        if (view != null && view.AimHintText != null)
        {
            originalHint = view.AimHintText.text;
            originalHintCaptured = true;
        }

        GameObject root = new GameObject("VineSwing_" + cfg.MechanismId);
        root.transform.SetParent(levelObjects, false);

        GameObject anchorObject = MakeArt(root.transform, "PendulumMossyAnchor",
            anchor, cfg.Pivot + new Vector2(0f, 0.17f), 2.15f, 0f, 14);
        anchorObject.transform.rotation = Quaternion.identity;

        // An unscaled mechanical root owns collision. The authored artwork is
        // scaled on a separate child; collider world units remain predictable.
        GameObject cradleObject = new GameObject("SwingingWoodenCradle");
        cradleObject.transform.SetParent(root.transform, false);
        cradleTransform = cradleObject.transform;
        GameObject cradleArt = MakeArt(cradleTransform, "AuthoredWoodenCradle",
            cradle, Vector2.zero, cfg.CradleWorldWidth,
            cfg.CradleWorldHeight, 15);
        cradleArt.transform.localPosition = Vector3.zero;
        cradleRenderer = cradleArt.GetComponent<SpriteRenderer>();
        // Below the target's painted scoring strip; true physical support.
        cradleCollider = cradleObject.AddComponent<BoxCollider2D>();
        cradleCollider.size = new Vector2(cfg.CradleWorldWidth * 0.90f,
            cfg.CradleWorldHeight * 0.38f);
        cradleCollider.offset = new Vector2(0f, -cfg.CradleWorldHeight * 0.11f);
        cradleCollider.isTrigger = false;

        GameObject holding = new GameObject("EntireCuttableHoldingVine");
        holding.transform.SetParent(root.transform, false);
        holding.transform.position = new Vector3(cfg.HoldingVinePosition.x,
            cfg.HoldingVinePosition.y, -0.02f);
        GameObject holdingArt = MakeArt(holding.transform, "AuthoredCuttableVine",
            vine, cfg.HoldingVinePosition, cfg.HoldingVineWorldWidth,
            cfg.HoldingVineWorldHeight, 18);
        holdingArt.transform.localPosition = Vector3.zero;
        holdingVineRenderer = holdingArt.GetComponent<SpriteRenderer>();
        // Unscaled root: physical dimensions are world units, not reduced by
        // the imported sprite's pixels-per-unit fit.
        holdingVineCollider = holding.AddComponent<BoxCollider2D>();
        holdingVineCollider.isTrigger = false;
        holdingVineCollider.size = new Vector2(cfg.HoldingVineColliderWidth,
            cfg.HoldingVineColliderHeight);
        holding.AddComponent<WildheartVineSwingHitReceiver>().Configure(this);

        // A visibly closed leafy shutter makes the required SETUP action fair.
        // It physically blocks the target scoring plane before the vine is cut
        // and is removed by that same event; never disable targets invisibly.
        GameObject shutter = new GameObject("LeafScreenShieldBeforeRelease");
        shutter.transform.SetParent(root.transform, false);
        GameObject shutterArt = MakeArt(shutter.transform, "AuthoredLeafScreen",
            screen, Vector2.zero, cfg.HeldScreenWidth * 1.10f,
            cfg.HeldScreenHeight * 1.10f, 24);
        shutterArt.transform.localPosition = Vector3.zero;
        screenRenderer = shutterArt.GetComponent<SpriteRenderer>();
        screenCollider = shutter.AddComponent<BoxCollider2D>();
        screenCollider.isTrigger = false;
        screenCollider.size = new Vector2(cfg.HeldScreenWidth,
            cfg.HeldScreenHeight);

        sound = root.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.78f;
        snapAudio = LoadClip(cfg.SnapAudioPath);
        swingAudio = LoadClip(cfg.SwingAudioPath);
        splashAudio = LoadClip(cfg.SplashAudioPath);

        suspensionRopes = new LineRenderer[2];
        for (int i = 0; i < suspensionRopes.Length; i++)
        {
            GameObject strand = new GameObject("LiveSuspensionVine_" + i);
            strand.transform.SetParent(root.transform, false);
            suspensionRopes[i] = CreateRope(strand, 0.065f, 11);
        }
        GameObject retaining = new GameObject("RetainingVineToPlatform");
        retaining.transform.SetParent(root.transform, false);
        retainingVine = CreateRope(retaining, 0.10f, 17);

        SpawnLeaves(root.transform, leaf);
        if (splash != null)
        {
            GameObject foam = MakeArt(root.transform, "PendulumRiverTouchFoam",
                splash, new Vector2(cfg.Pivot.x, -3.63f), 3.0f, 0f, 20);
            splashRenderer = foam.GetComponent<SpriteRenderer>();
            splashRenderer.color = new Color(1f, 1f, 1f, 0f);
        }
        PositionCradle(cfg.SwingAngleDegrees);
        Physics2D.SyncTransforms();
    }

    private static GameObject MakeArt(Transform root, string name, Sprite sprite,
        Vector2 position, float width, float height, int sorting)
    {
        GameObject obj = new GameObject(name, typeof(SpriteRenderer));
        obj.transform.SetParent(root, false);
        obj.transform.position = new Vector3(position.x, position.y, -0.02f);
        SpriteRenderer renderer = obj.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sorting;
        float sx = width > 0f ? width / Mathf.Max(0.01f, sprite.bounds.size.x) : 1f;
        float sy = height > 0f ? height / Mathf.Max(0.01f, sprite.bounds.size.y) : sx;
        obj.transform.localScale = new Vector3(sx, sy, 1f);
        return obj;
    }

    private static LineRenderer CreateRope(GameObject obj, float width, int order)
    {
        LineRenderer rope = obj.AddComponent<LineRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new Material(shader)
            {
                name = "VineSwingPendulumRope",
                hideFlags = HideFlags.DontSave
            };
            rope.sharedMaterial = material;
        }
        rope.useWorldSpace = true;
        rope.positionCount = 2;
        rope.startWidth = width;
        rope.endWidth = width * 0.90f;
        rope.startColor = new Color(0.30f, 0.44f, 0.16f, 1f);
        rope.endColor = new Color(0.60f, 0.72f, 0.29f, 1f);
        rope.sortingOrder = order;
        rope.numCapVertices = 4;
        return rope;
    }

    private void PositionCradle(float angleDegrees)
    {
        if (config == null || cradleTransform == null || targetTransform == null)
            return;
        float a = angleDegrees * Mathf.Deg2Rad;
        Vector2 payload = config.Pivot + new Vector2(
            Mathf.Sin(a), -Mathf.Cos(a)) * config.RopeLength;
        cradleTransform.position = new Vector3(payload.x, payload.y, -0.02f);
        cradleTransform.rotation = Quaternion.Euler(0f, 0f,
            Mathf.Clamp(-angleDegrees * 0.12f, -6f, 6f));
        targetTransform.position = new Vector3(
            payload.x, payload.y + config.TargetRiseAboveCradle, -0.05f);
        if (screenCollider != null && screenCollider.enabled)
        {
            screenCollider.transform.position = new Vector3(
                targetTransform.position.x + config.HeldScreenOffsetX,
                targetTransform.position.y, -0.09f);
        }
        if (suspensionRopes != null)
        {
            for (int i = 0; i < suspensionRopes.Length; i++)
            {
                LineRenderer rope = suspensionRopes[i];
                if (rope == null) continue;
                float side = i == 0 ? -0.90f : 0.90f;
                Vector2 localEye = (Vector2)cradleTransform.TransformPoint(
                    new Vector3(side, 0.31f, 0f));
                rope.SetPosition(0, new Vector3(config.Pivot.x, config.Pivot.y, -0.04f));
                rope.SetPosition(1, new Vector3(localEye.x, localEye.y, -0.04f));
            }
        }
        if (retainingVine != null && retainingVine.enabled)
        {
            retainingVine.SetPosition(0, new Vector3(
                config.HoldingVinePosition.x,
                config.HoldingVinePosition.y + config.HoldingVineWorldHeight * 0.37f,
                -0.09f));
            retainingVine.SetPosition(1, new Vector3(
                payload.x - config.CradleWorldWidth * 0.40f,
                payload.y + 0.16f, -0.09f));
        }
        Physics2D.SyncTransforms();
    }

    internal void OnHoldingVineStruck(ArrowController arrow)
    {
        if (config == null || released || arrow == null || !arrow.HasFired)
            return;
        released = true;
        releasedAt = Time.time;
        if (holdingVineCollider != null)
            holdingVineCollider.enabled = false;
        if (retainingVine != null)
            retainingVine.enabled = false;
        if (screenCollider != null)
            screenCollider.enabled = false;
        if (screenRenderer != null)
            StartCoroutine(ReleaseScreenCurtain());
        if (sound != null && snapAudio != null)
            sound.PlayOneShot(snapAudio, 1f);
        ATSHaptics.Pulse();
        // The level manager also shows its main shot feedback here. Do not
        // spawn a competing world-space popup: the two messages overlap across
        // the playing field. ConvertSetupMissToSuccess() supplies one concise
        // "VINE CUT!" message on that existing feedback surface instead.
        Physics2D.SyncTransforms();
        InvalidatePrediction();
    }

    private IEnumerator ReleaseScreenCurtain()
    {
        if (screenRenderer == null) yield break;
        Transform root = screenRenderer.transform.parent;
        if (root == null) yield break;
        Vector3 start = root.position;
        float elapsed = 0f;
        const float duration = 0.62f;
        while (elapsed < duration && root != null && screenRenderer != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 2.5f);
            root.position = start + new Vector3(-0.62f * ease,
                -1.2f * ease - 0.10f * t * t, 0f);
            root.rotation = Quaternion.Euler(0f, 0f, -59f * ease);
            Color c = screenRenderer.color;
            c.a = 1f - Mathf.Clamp01((t - 0.27f) / 0.73f);
            screenRenderer.color = c;
            yield return null;
        }
        if (screenRenderer != null)
            screenRenderer.enabled = false;
    }

    private void OnTargetHit()
    {
        completed = true;
    }

    private void SpawnLeaves(Transform root, Sprite art)
    {
        leaves.Clear();
        int count = Mathf.Clamp(config.LeafCount, 8, 24);
        for (int i = 0; i < count; i++)
        {
            GameObject particle = MakeArt(root, "PendulumLeaf_" + i, art,
                config.HoldingVinePosition, 0.18f + 0.03f * (i % 5), 0f, 22);
            SpriteRenderer renderer = particle.GetComponent<SpriteRenderer>();
            renderer.enabled = false;
            leaves.Add(new Leaf {
                Root = particle.transform,
                Renderer = renderer,
                Velocity = new Vector2(-1.7f + 0.24f * i,
                    1.2f + (i % 4) * 0.33f),
                RotationSpeed = i % 2 == 0 ? 108f : -98f,
                Delay = (i % 6) * 0.04f,
                Lifetime = 1.20f + 0.11f * (i % 4)
            });
        }
    }

    private void AnimateLeaves()
    {
        float elapsed = Time.time - releasedAt;
        for (int i = 0; i < leaves.Count; i++)
        {
            Leaf leaf = leaves[i];
            if (leaf == null || leaf.Root == null || leaf.Renderer == null)
                continue;
            float t = elapsed - leaf.Delay;
            leaf.Renderer.enabled = t > 0f && t < leaf.Lifetime;
            if (!leaf.Renderer.enabled) continue;
            Vector2 position = config.HoldingVinePosition + new Vector2(
                leaf.Velocity.x * t + Mathf.Sin(t * 8f + i) * 0.16f,
                leaf.Velocity.y * t - 1.95f * t * t);
            leaf.Root.position = new Vector3(position.x, position.y, -0.16f);
            leaf.Root.rotation = Quaternion.Euler(0f, 0f, t * leaf.RotationSpeed);
            leaf.Renderer.color = new Color(1f, 1f, 1f,
                Mathf.Clamp01((leaf.Lifetime - t) / 0.30f));
        }
        if (splashRenderer != null)
        {
            float age = elapsed - config.SwingPeriodSeconds * 0.20f;
            float alpha = Mathf.Clamp01(age * 8f) *
                Mathf.Clamp01((1.18f - age) * 1.9f) * 0.83f;
            splashRenderer.color = new Color(1f, 1f, 1f, Mathf.Max(0f, alpha));
        }
    }

    private void PlaySplash()
    {
        if (sound != null && splashAudio != null)
            sound.PlayOneShot(splashAudio, 0.74f);
        if (sound != null && swingAudio != null)
            sound.PlayOneShot(swingAudio, 0.48f);
        ATSHaptics.Pulse();
    }

    private void ConvertSetupMissToSuccess()
    {
        if (view == null || view.FeedbackText == null) return;
        string content = view.FeedbackText.text;
        if (string.IsNullOrEmpty(content) ||
            !content.StartsWith("MISS", StringComparison.Ordinal)) return;
        // Keep it short: the HUD already explains the next objective.
        view.FeedbackText.text = "VINE CUT!";
        view.FeedbackText.color = new Color(1f, 0.90f, 0.38f, 1f);
        if (view.ScreenFlash != null && view.ScreenFlash.gameObject.activeSelf)
        {
            Color flash = view.ScreenFlash.color;
            view.ScreenFlash.color = new Color(0.70f, 0.96f, 0.40f, flash.a);
        }
    }

    private void UpdateHud()
    {
        if (view == null || view.AimHintText == null || config == null)
            return;
        view.AimHintText.text = released ? config.ReleasedHint : config.WaitingHint;
    }

    private void InvalidatePrediction()
    {
        if (predictor != null && InvalidateMethod != null)
            InvalidateMethod.Invoke(predictor, null);
    }

    private static AudioClip LoadClip(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Resources.Load<AudioClip>(path);
    }

    private void ResetRuntime()
    {
        if (carriedTarget != null)
            carriedTarget.Hit -= OnTargetHit;
        if (originalHintCaptured && view != null && view.AimHintText != null)
            view.AimHintText.text = originalHint;
        StopAllCoroutines();
        config = null;
        carriedTarget = null;
        targetTransform = null;
        cradleTransform = null;
        cradleRenderer = null;
        cradleCollider = null;
        holdingVineRenderer = null;
        holdingVineCollider = null;
        screenRenderer = null;
        screenCollider = null;
        splashRenderer = null;
        suspensionRopes = null;
        retainingVine = null;
        sound = null;
        snapAudio = null;
        swingAudio = null;
        splashAudio = null;
        bow = null;
        predictor = null;
        view = null;
        originalHint = null;
        originalHintCaptured = false;
        released = false;
        completed = false;
        usedSplash = false;
        releasedAt = 0f;
        leaves.Clear();
    }

    private void OnDisable()
    {
        ResetRuntime();
        observedObjects = null;
    }
}

/// <summary>True solid-hit handler: event ordering with ArrowController is irrelevant.</summary>
[DisallowMultipleComponent]
internal sealed class WildheartVineSwingHitReceiver : MonoBehaviour
{
    private WildheartVineSwingRuntime mechanism;
    public void Configure(WildheartVineSwingRuntime owner) { mechanism = owner; }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (mechanism == null || collision == null) return;
        ArrowController arrow = collision.collider != null
            ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null)
            arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.HasFired)
            mechanism.OnHoldingVineStruck(arrow);
    }
}
