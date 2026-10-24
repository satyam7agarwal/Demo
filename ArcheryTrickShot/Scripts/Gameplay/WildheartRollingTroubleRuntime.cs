using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Data-driven Runaway Log puzzle. Opt-in via Resources/Levels/RollingTrouble_NNN.json.
/// First arrow breaks the heavy retaining peg; the log rolls down a chute.
/// The second arrow must hit the red steering lever BEFORE the log reaches
/// the fork, raising an actual visible ramp and launching the log into the
/// barricade. The third arrow reaches the revealed target.
///
/// All interactive props have real solid colliders on unscaled roots.
/// Full Path remains player-controlled: the existing preview and arrow see
/// those same colliders. No pre-drawn solution or invisible route-blocker.
/// Moving log prediction is instantaneous rather than future-positional.
/// </summary>
[DefaultExecutionOrder(1730)]
[DisallowMultipleComponent]
public sealed class WildheartRollingTroubleRuntime : MonoBehaviour
{
    private const string ResourcePrefix = "Levels/RollingTrouble_";
    private static readonly FieldInfo LevelField = typeof(LevelManager).GetField(
        "currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ViewField = typeof(GameUIController).GetField(
        "view", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PredictorField = typeof(BowController).GetField(
        "trajectoryRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo InvalidatePrediction =
        typeof(AimTrajectoryRenderer).GetMethod("InvalidatePreviewCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

    [Serializable]
    private sealed class Config
    {
        public string MechanismId = "runaway_log";
        public string LogSpritePath;
        public string TrackSpritePath;
        public string GateSpritePath;
        public string BrokenGateSpritePath;
        public string SplinterSpritePath;
        public string PegSpritePath;
        public string LeverSpritePath;
        public string RampSpritePath;
        public string RampSupportSpritePath;
        public string RumbleAudioPath;
        public string CrashAudioPath;
        public string SplashAudioPath;
        public Vector2 LogStart = new Vector2(-1.75f, -0.30f);
        public Vector2 ForkPosition = new Vector2(2.12f, -1.72f);
        public Vector2 GateImpact = new Vector2(4.03f, -1.22f);
        public Vector2 RiverLanding = new Vector2(3.15f, -3.86f);
        public Vector2 PegPosition = new Vector2(-1.65f, 1.30f);
        public Vector2 LeverPosition = new Vector2(0.67f, 1.30f);
        public Vector2 RampPosition = new Vector2(2.22f, -2.31f);
        public Vector2 GatePosition = new Vector2(4.85f, -1.56f);
        public Vector2 TrackPosition = new Vector2(0.85f, -1.80f);
        public float TrackWidth = 8.65f;
        public float LogWidth = 2.04f;
        public float LogColliderRadius = 0.83f;
        public float PegWidth = 1.30f;
        public Vector2 PegColliderSize = new Vector2(1.05f, 1.17f);
        public float LeverWidth = 1.65f;
        public Vector2 LeverColliderSize = new Vector2(1.23f, 1.55f);
        public float RampWidth = 3.00f;
        public float RampSupportWidth = 1.55f;
        public float GateWidth = 1.42f;
        public float GateHeight = 3.83f;
        public Vector2 GateColliderSize = new Vector2(1.14f, 3.68f);
        public float ForkArrivalSeconds = 10.0f;
        public float AutoRetrySeconds = 2.3f;
        public float RampLiftSeconds = 0.42f;
        public float LeapSeconds = 0.95f;
        public float SplashSeconds = 0.88f;
        public float FinalLogDropSeconds = 0.88f;
        public float RampRaisedAngle = 28f;
        public string PegHint = "SHOOT GOLD PEG";
        public string LeverHint = "RED LEVER - ACTIVATE!";
        public string ArmedHint = "RAMP READY!";
        public string FlyingHint = "HERE IT COMES!";
        public string ClearHint = "GATE SMASHED!";
        public string MissedHint = "LOG LOST! RETRYING";
    }

    private enum Stage { Held, Rolling, Flying, Splash, Clear, Lost, Retrying }

    private sealed class Fragment
    {
        public Transform Root;
        public SpriteRenderer Renderer;
        public Vector2 Velocity;
        public float Spin;
        public float Age;
        public float Lifetime;
    }

    private readonly List<Fragment> fragments = new List<Fragment>(36);
    private Transform observedLevelObjects;
    private LevelManager manager;
    private Config config;
    private Stage stage;
    private Transform mechanismRoot;
    private Transform logRoot;
    private SpriteRenderer logRenderer;
    private CircleCollider2D logCollider;
    private BoxCollider2D pegCollider;
    private SpriteRenderer pegRenderer;
    private BoxCollider2D leverCollider;
    private SpriteRenderer leverRenderer;
    private Transform rampRoot;
    private SpriteRenderer rampRenderer;
    private BoxCollider2D gateCollider;
    private SpriteRenderer gateRenderer;
    private Sprite intactGate;
    private Sprite brokenGate;
    private Sprite splinterSprite;
    private LineRenderer tether;
    private LineRenderer pegSupport;
    private LineRenderer leverGlow;
    private AudioSource sound;
    private AudioClip rumble;
    private AudioClip crash;
    private AudioClip splash;
    private BowController bow;
    private AimTrajectoryRenderer preview;
    private GameUIView view;
    private string originalHint;
    private bool savedHint;
    private bool leverActivated;
    private bool gateBroken;
    private bool splashPlayed;
    private bool logResting;
    private float rollingStartedAt;
    private float phaseStartedAt;
    private float positiveHitAt = -99f;
    private string positiveFeedback;
    private Vector2 logLastPosition;
    private float rolledDistance;
    private Color leverBaseColor = Color.white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager lm = FindFirstObjectByType<LevelManager>();
        if (lm != null && lm.GetComponent<WildheartRollingTroubleRuntime>() == null)
            lm.gameObject.AddComponent<WildheartRollingTroubleRuntime>();
    }

    private void Awake() { manager = GetComponent<LevelManager>(); }

    private void LateUpdate()
    {
        Transform objects = transform.Find("LevelObjects");
        if (objects != observedLevelObjects)
        {
            ClearState();
            observedLevelObjects = objects;
            if (objects != null) Spawn(objects);
        }
        if (config == null) return;

        if (stage == Stage.Rolling || stage == Stage.Flying || stage == Stage.Splash || stage == Stage.Clear)
        {
            AdvanceLog();
            // A physical moving collider changes independently of aim input.
            // Invalidate the existing optional trajectory preview cache.
            if (bow != null && bow.IsAiming && bow.FullTrajectoryPreviewEnabled)
                RefreshPrediction();
        }
        AnimateFragments();
        UpdateVisualHighlights();
        UpdateHint();

        // When the log misses the redirect ramp, continuing is unwinnable:
        // the only log has gone and the barricade still stands. Give the
        // failure a short visible beat, then reload the same level cleanly
        // instead of consuming more arrows against an impossible barrier.
        if (stage == Stage.Lost &&
            Time.time - phaseStartedAt >= Mathf.Max(1f, config.AutoRetrySeconds))
        {
            stage = Stage.Retrying; // Prevent calling RetryLevel twice.
            if (manager != null)
                manager.RetryLevel();
            return;
        }

        // Don't let LevelManager's delayed arrow respawn enable a shot while
        // the log is airborne and the barricade is still closing the route.
        if (((stage == Stage.Rolling && leverActivated) || stage == Stage.Flying) &&
            !gateBroken && bow != null)
            bow.SetInputEnabled(false);

        // Successful setup shots use the same shared shot/arrow lifecycle as
        // a wall hit, but should not be presented as a pink 'MISS'.
        if (Time.time - positiveHitAt < 0.88f)
            RelabelSetupShot();
    }

    private void Spawn(Transform parent)
    {
        LevelData level = LevelField != null && manager != null
            ? LevelField.GetValue(manager) as LevelData : null;
        if (level == null) return;
        TextAsset json = Resources.Load<TextAsset>(
            ResourcePrefix + level.LevelNumber.ToString("000"));
        if (json == null) return;

        Config c;
        try { c = JsonUtility.FromJson<Config>(json.text); }
        catch (Exception e)
        {
            Debug.LogError("[Runaway Log] Invalid config JSON: " + e.Message, parent);
            return;
        }
        if (c == null || c.ForkArrivalSeconds < 2.5f ||
            c.LogColliderRadius < 0.35f || c.GateColliderSize.y < 2f ||
            c.PegColliderSize.x < 0.45f || c.LeverColliderSize.x < 0.45f)
        {
            Debug.LogError("[Runaway Log] Invalid physical dimensions/timing.", parent);
            return;
        }

        Sprite log = Resources.Load<Sprite>(c.LogSpritePath);
        Sprite track = Resources.Load<Sprite>(c.TrackSpritePath);
        Sprite gate = Resources.Load<Sprite>(c.GateSpritePath);
        Sprite broken = Resources.Load<Sprite>(c.BrokenGateSpritePath);
        Sprite peg = Resources.Load<Sprite>(c.PegSpritePath);
        Sprite lever = Resources.Load<Sprite>(c.LeverSpritePath);
        Sprite ramp = Resources.Load<Sprite>(c.RampSpritePath);
        Sprite support = Resources.Load<Sprite>(c.RampSupportSpritePath);
        Sprite splinters = Resources.Load<Sprite>(c.SplinterSpritePath);
        if (log == null || track == null || gate == null || broken == null ||
            peg == null || lever == null || ramp == null || support == null || splinters == null)
        {
            Debug.LogError("[Runaway Log] Missing required authored art; refusing invisible physics.", parent);
            return;
        }

        config = c;
        stage = Stage.Held;
        brokenGate = broken;
        intactGate = gate;
        splinterSprite = splinters;
        bow = FindFirstObjectByType<BowController>();
        preview = bow != null && PredictorField != null
            ? PredictorField.GetValue(bow) as AimTrajectoryRenderer : null;
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        view = ui != null && ViewField != null
            ? ViewField.GetValue(ui) as GameUIView : null;
        if (view != null && view.AimHintText != null)
        {
            originalHint = view.AimHintText.text;
            savedHint = true;
        }

        GameObject root = new GameObject("RunawayLog_" + c.MechanismId);
        root.transform.SetParent(parent, false);
        mechanismRoot = root.transform;
        MakeArt(mechanismRoot, "MossyRollingTrack", track,
            c.TrackPosition, c.TrackWidth, 0f, 4);

        gateRenderer = MakeArt(mechanismRoot, "RootBarricade", gate,
            c.GatePosition, c.GateWidth, c.GateHeight, 12);
        gateCollider = gateRenderer.transform.parent.gameObject.AddComponent<BoxCollider2D>();
        gateCollider.isTrigger = false;
        gateCollider.size = c.GateColliderSize;

        SpriteRenderer logArt = MakeArt(mechanismRoot, "RunawayHeavyLog", log,
            c.LogStart, c.LogWidth, 0f, 15);
        logRenderer = logArt;
        logRoot = logArt.transform.parent;
        logCollider = logRoot.gameObject.AddComponent<CircleCollider2D>();
        logCollider.isTrigger = false;
        logCollider.radius = c.LogColliderRadius;
        logLastPosition = c.LogStart;
        rolledDistance = 0f;

        pegRenderer = MakeArt(mechanismRoot, "HeavyRetainingPeg", peg,
            c.PegPosition, c.PegWidth, 0f, 17);
        pegCollider = pegRenderer.transform.parent.gameObject.AddComponent<BoxCollider2D>();
        pegCollider.size = c.PegColliderSize;
        pegCollider.isTrigger = false;
        pegRenderer.transform.parent.gameObject.AddComponent<RollingLogPegHitReceiver>()
            .Configure(this);

        leverRenderer = MakeArt(mechanismRoot, "RedSteeringLever", lever,
            c.LeverPosition, c.LeverWidth, 0f, 17);
        leverCollider = leverRenderer.transform.parent.gameObject.AddComponent<BoxCollider2D>();
        leverCollider.size = c.LeverColliderSize;
        leverCollider.isTrigger = false;
        // Visible solid wood must always remain physically solid. An early
        // arrow gets a clear "PEG FIRST!" response, never ghosts through it.
        leverCollider.enabled = true;
        leverRenderer.transform.parent.gameObject.AddComponent<RollingLogLeverHitReceiver>()
            .Configure(this);
        leverBaseColor = leverRenderer.color;
        leverRenderer.color = new Color(0.67f, 0.65f, 0.58f, 0.95f);

        // The bright red lever rests on a vine-suspended wooden perch instead
        // of floating in air. The perch/ropes are decoration above the arrow
        // corridor; they are NOT invisible gameplay blockers.
        MakeArt(mechanismRoot, "SuspendedSteeringPerch", track,
            c.LeverPosition + new Vector2(0f, -1.01f), 2.18f, 0f, 7);
        LineRenderer leftPerchRope = MakeLine("LeftPerchRope", mechanismRoot, 2,
            new Color(0.35f, 0.49f, 0.18f, 0.93f), 0.075f, 9);
        leftPerchRope.SetPosition(0, new Vector3(c.LeverPosition.x - 0.80f,
            c.LeverPosition.y - 0.45f, 0.04f));
        leftPerchRope.SetPosition(1, new Vector3(c.LeverPosition.x - 0.98f,
            5.15f, 0.04f));
        LineRenderer rightPerchRope = MakeLine("RightPerchRope", mechanismRoot, 2,
            new Color(0.37f, 0.49f, 0.21f, 0.93f), 0.075f, 9);
        rightPerchRope.SetPosition(0, new Vector3(c.LeverPosition.x + 0.83f,
            c.LeverPosition.y - 0.45f, 0.04f));
        rightPerchRope.SetPosition(1, new Vector3(c.LeverPosition.x + 0.99f,
            5.15f, 0.04f));

        // Visual ramp and support remain in-world; the ramp is tilted only by
        // a confirmed lever hit. The moving log and ramp are choreographed as
        // a deterministic gameplay mechanism; arrow blocking is not faked.
        SpriteRenderer supportArt = MakeArt(mechanismRoot, "FlipRampSupport", support,
            c.RampPosition + new Vector2(0f, -0.70f), c.RampSupportWidth, 0f, 6);
        supportArt.color = new Color(1f, 1f, 1f, 0.95f);
        rampRenderer = MakeArt(mechanismRoot, "RaisingLogRamp", ramp,
            c.RampPosition, c.RampWidth, 0f, 14);
        rampRoot = rampRenderer.transform.parent;
        rampRoot.rotation = Quaternion.Euler(0f, 0f, 0f);

        tether = MakeLine("PegLogTether", mechanismRoot, 2,
            new Color(0.81f, 0.62f, 0.28f, 0.95f), 0.105f, 13);
        tether.SetPosition(0, new Vector3(c.PegPosition.x, c.PegPosition.y - 0.29f, -0.12f));
        tether.SetPosition(1, new Vector3(c.LogStart.x, c.LogStart.y + 0.63f, -0.12f));
        pegSupport = MakeLine("PegAnchorPost", mechanismRoot, 2,
            new Color(0.37f, 0.24f, 0.12f, 0.92f), 0.19f, 11);
        pegSupport.SetPosition(0, new Vector3(c.PegPosition.x,
            c.PegPosition.y - 0.43f, 0.035f));
        pegSupport.SetPosition(1, new Vector3(c.PegPosition.x + 0.12f,
            c.LogStart.y - 1.05f, 0.035f));
        leverGlow = MakeLine("LeverWake", mechanismRoot, 4,
            new Color(1f, 0.40f, 0.19f, 0.95f), 0.12f, 19);
        Vector2 pos = c.LeverPosition;
        float half = c.LeverColliderSize.x * 0.53f;
        leverGlow.SetPosition(0, new Vector3(pos.x-half, pos.y-0.33f, -0.12f));
        leverGlow.SetPosition(1, new Vector3(pos.x-half, pos.y+0.35f, -0.12f));
        leverGlow.SetPosition(2, new Vector3(pos.x+half, pos.y+0.35f, -0.12f));
        leverGlow.SetPosition(3, new Vector3(pos.x+half, pos.y-0.33f, -0.12f));
        leverGlow.enabled = false;

        rumble = LoadSound(c.RumbleAudioPath);
        crash = LoadSound(c.CrashAudioPath);
        splash = LoadSound(c.SplashAudioPath);
        sound = root.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.75f;
        Physics2D.SyncTransforms();
        RefreshPrediction();
    }

    private static SpriteRenderer MakeArt(Transform parent, string name,
        Sprite sprite, Vector2 point, float width, float height, int order)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);
        container.transform.position = new Vector3(point.x, point.y, 0f);
        GameObject art = new GameObject("AuthoredSprite", typeof(SpriteRenderer));
        art.transform.SetParent(container.transform, false);
        art.transform.localPosition = new Vector3(0f, 0f, -0.025f);
        SpriteRenderer renderer = art.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        float sx = width / Mathf.Max(0.01f, sprite.bounds.size.x);
        float sy = height > 0f ? height / Mathf.Max(0.01f, sprite.bounds.size.y) : sx;
        art.transform.localScale = new Vector3(sx, sy, 1f);
        return renderer;
    }

    private static LineRenderer MakeLine(string name, Transform parent,
        int positions, Color color, float width, int order)
    {
        GameObject root = new GameObject(name, typeof(LineRenderer));
        root.transform.SetParent(parent, false);
        LineRenderer line = root.GetComponent<LineRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            line.sharedMaterial = new Material(shader)
            { name = name + "_RuntimeMaterial", hideFlags = HideFlags.DontSave };
        line.positionCount = positions;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = order;
        line.numCapVertices = 3;
        return line;
    }

    private static AudioClip LoadSound(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Resources.Load<AudioClip>(path);

    internal void OnPegStruck(ArrowController arrow)
    {
        if (stage != Stage.Held || config == null || arrow == null || !arrow.HasFired)
            return;
        stage = Stage.Rolling;
        leverActivated = false;
        rollingStartedAt = Time.time;
        phaseStartedAt = Time.time;
        positiveHitAt = Time.time;
        positiveFeedback = "LOG RELEASED!";
        if (pegCollider != null) pegCollider.enabled = false;
        if (pegRenderer != null) pegRenderer.enabled = false;
        if (tether != null) tether.enabled = false;
        if (pegSupport != null) pegSupport.enabled = false;
        if (leverCollider != null) leverCollider.enabled = true;
        if (leverRenderer != null) leverRenderer.color = leverBaseColor;
        if (leverGlow != null) leverGlow.enabled = true;
        SpawnFragments(config.PegPosition, 14, 0.55f);
        if (sound != null && rumble != null) sound.PlayOneShot(rumble, 0.76f);
        ATSHaptics.Pulse();
        Physics2D.SyncTransforms();
        RefreshPrediction();
    }

    internal void OnLeverStruck(ArrowController arrow)
    {
        if (config == null || arrow == null || !arrow.HasFired) return;
        if (stage == Stage.Held)
        {
            positiveHitAt = Time.time;
            positiveFeedback = "PEG FIRST!";
            return;
        }
        if (stage != Stage.Rolling || leverActivated) return;
        // A player who misses the red handle can't raise the ramp by hitting
        // the log: only this actual arrow collision activates the switch.
        float elapsed = Time.time - rollingStartedAt;
        if (elapsed > config.ForkArrivalSeconds - 0.06f)
        {
            positiveHitAt = Time.time;
            positiveFeedback = "TOO LATE!";
            return;
        }
        leverActivated = true;
        positiveHitAt = Time.time;
        positiveFeedback = "RAMP UP!";
        if (leverCollider != null) leverCollider.enabled = false;
        if (leverGlow != null) leverGlow.enabled = false;
        if (leverRenderer != null)
            leverRenderer.color = new Color(1f, 0.85f, 0.46f, 1f);
        SpawnFragments(config.LeverPosition, 12, 0.28f);
        if (sound != null && crash != null) sound.PlayOneShot(crash, 0.32f);
        ATSHaptics.Pulse();
        Physics2D.SyncTransforms();
        RefreshPrediction();
    }

    private void AdvanceLog()
    {
        Config c = config;
        if (c == null || logRoot == null) return;
        if (stage == Stage.Rolling)
        {
            float elapsed = Mathf.Max(0f, Time.time - rollingStartedAt);
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.5f, c.ForkArrivalSeconds));
            // Accelerating heavy body (gravity down the curved wooden chute).
            float accelerated = t * (0.62f + 0.38f * t);
            Vector2 middle = Vector2.Lerp(c.LogStart, c.ForkPosition, 0.51f) +
                new Vector2(0f, -0.14f);
            Vector2 position = Bezier(c.LogStart, middle, c.ForkPosition, accelerated);
            MoveAndSpinLog(position);
            if (leverActivated && rampRoot != null)
            {
                float rampT = Mathf.Clamp01((Time.time - positiveHitAt) /
                    Mathf.Max(0.15f, c.RampLiftSeconds));
                float ease = EaseOutBack(rampT);
                rampRoot.rotation = Quaternion.Euler(0f, 0f,
                    c.RampRaisedAngle * Mathf.Clamp(ease, 0f, 1.05f));
            }
            if (t >= 1f)
            {
                // A dramatic last-second lever hit is valid: let the wooden
                // ramp finish lifting before the log launches off the fork.
                // Never let the body fly through a visibly lowered ramp.
                if (leverActivated && Time.time - positiveHitAt < c.RampLiftSeconds)
                    return;
                stage = leverActivated ? Stage.Flying : Stage.Splash;
                phaseStartedAt = Time.time;
                if (!leverActivated && leverGlow != null) leverGlow.enabled = false;
                if (leverActivated && bow != null) bow.SetInputEnabled(false);
            }
        }
        else if (stage == Stage.Flying)
        {
            float t = Mathf.Clamp01((Time.time - phaseStartedAt) /
                Mathf.Max(0.3f, c.LeapSeconds));
            Vector2 highPoint = Vector2.Lerp(c.ForkPosition, c.GateImpact, 0.50f) +
                Vector2.up * 2.05f;
            Vector2 position = Bezier(c.ForkPosition, highPoint, c.GateImpact, t);
            MoveAndSpinLog(position);
            if (t >= 1f)
            {
                SmashBarrier();
                stage = Stage.Clear;
                phaseStartedAt = Time.time;
            }
        }
        else if (stage == Stage.Splash)
        {
            float t = Mathf.Clamp01((Time.time - phaseStartedAt) /
                Mathf.Max(0.3f, c.SplashSeconds));
            MoveAndSpinLog(Bezier(c.ForkPosition,
                c.ForkPosition + new Vector2(0.90f, -0.28f), c.RiverLanding,
                t));
            if (!splashPlayed && t >= 0.72f)
            {
                splashPlayed = true;
                if (sound != null && splash != null) sound.PlayOneShot(splash, 0.89f);
                SpawnFragments(c.RiverLanding, 8, 0.38f);
                ATSHaptics.Pulse();
            }
            if (t >= 1f)
            {
                stage = Stage.Lost;
                phaseStartedAt = Time.time;
                if (logCollider != null) logCollider.enabled = false;
                if (leverCollider != null) leverCollider.enabled = false;
                if (bow != null) bow.SetInputEnabled(false);
                Physics2D.SyncTransforms();
                RefreshPrediction();
            }
        }
        else if (stage == Stage.Clear && !logResting)
        {
            float t = Mathf.Clamp01((Time.time - phaseStartedAt) /
                Mathf.Max(0.3f, c.FinalLogDropSeconds));
            MoveAndSpinLog(Bezier(c.GateImpact,
                c.GateImpact + new Vector2(0.62f, -1.05f),
                c.RiverLanding + Vector2.right * 1.40f, t));
            if (t >= 1f)
            {
                logResting = true;
                if (sound != null && splash != null) sound.PlayOneShot(splash, 0.76f);
                if (logRenderer != null)
                    logRenderer.color = new Color(1f, 1f, 1f, 0.77f);
            }
        }
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return a * (u * u) + b * (2f * u * t) + c * (t * t);
    }

    private static float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float v = t - 1f;
        return 1f + c3 * v * v * v + c1 * v * v;
    }

    private void MoveAndSpinLog(Vector2 position)
    {
        if (logRoot == null || config == null) return;
        rolledDistance += Vector2.Distance(logLastPosition, position);
        logLastPosition = position;
        logRoot.position = new Vector3(position.x, position.y, -0.02f);
        logRoot.rotation = Quaternion.Euler(0f, 0f,
            -rolledDistance / Mathf.Max(0.15f, config.LogColliderRadius) * Mathf.Rad2Deg);
        Physics2D.SyncTransforms();
    }

    private void SmashBarrier()
    {
        if (gateBroken || config == null) return;
        gateBroken = true;
        if (gateCollider != null) gateCollider.enabled = false;
        if (logCollider != null) logCollider.enabled = false;
        if (gateRenderer != null)
        {
            gateRenderer.sprite = brokenGate;
            gateRenderer.color = Color.white;
        }
        SpawnFragments(config.GatePosition + Vector2.up * 0.30f, 40, 1f);
        if (sound != null && crash != null) sound.PlayOneShot(crash, 1f);
        ATSHaptics.Pulse();
        if (bow != null) bow.SetInputEnabled(true);
        Physics2D.SyncTransforms();
        RefreshPrediction();
    }

    private void SpawnFragments(Vector2 at, int count, float force)
    {
        if (splinterSprite == null || mechanismRoot == null) return;
        System.Random random = new System.Random(2700 + count +
            Mathf.RoundToInt(at.x * 100f) + Mathf.RoundToInt(Time.time * 10f));
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("FlyingWoodSplinter", typeof(SpriteRenderer));
            go.transform.SetParent(mechanismRoot, false);
            go.transform.position = new Vector3(at.x + (float)(random.NextDouble()-0.5)*0.48f,
                at.y + (float)(random.NextDouble()-0.5)*0.58f, -0.12f);
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = splinterSprite;
            sr.sortingOrder = 21;
            float width = 0.14f + (float)random.NextDouble()*0.20f;
            float scale = width / Mathf.Max(0.02f, splinterSprite.bounds.size.x);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            fragments.Add(new Fragment
            {
                Root = go.transform,
                Renderer = sr,
                Velocity = new Vector2(((float)random.NextDouble() - 0.5f)*
                    (3.5f + 5.1f*force), 1.1f+(float)random.NextDouble()*(2.4f+2.8f*force)),
                Spin = -310f+(float)random.NextDouble()*620f,
                Lifetime = 0.80f+(float)random.NextDouble()*0.82f
            });
        }
    }

    private void AnimateFragments()
    {
        float dt = Time.deltaTime;
        for (int i = fragments.Count - 1; i >= 0; i--)
        {
            Fragment f = fragments[i];
            if (f.Root == null || f.Renderer == null)
            {
                fragments.RemoveAt(i);
                continue;
            }
            f.Age += dt;
            if (f.Age >= f.Lifetime)
            {
                Destroy(f.Root.gameObject);
                fragments.RemoveAt(i);
                continue;
            }
            f.Root.position += (Vector3)(f.Velocity*dt);
            f.Velocity += Vector2.down * (7.5f*dt);
            f.Root.Rotate(0f,0f,f.Spin*dt);
            Color col = f.Renderer.color;
            col.a = Mathf.Clamp01((f.Lifetime-f.Age)*2.2f);
            f.Renderer.color = col;
        }
    }

    private void UpdateVisualHighlights()
    {
        if (leverGlow != null && leverGlow.enabled)
        {
            float elapsed = Mathf.Max(0f, Time.time - rollingStartedAt);
            float fraction = stage == Stage.Rolling && config != null
                ? Mathf.Clamp01(elapsed / Mathf.Max(1f, config.ForkArrivalSeconds))
                : 0f;
            float pulseRate = Mathf.Lerp(6f, 15f, fraction);
            Color c = new Color(1f, 0.38f, 0.16f,
                0.64f + 0.32f * Mathf.Sin(Time.time * pulseRate));
            leverGlow.startColor = c;
            leverGlow.endColor = c;
        }
        if (leverRenderer != null && leverCollider != null && leverCollider.enabled)
        {
            leverRenderer.color = Color.Lerp(leverBaseColor,
                new Color(1f, 0.83f, 0.67f, 1f),
                (Mathf.Sin(Time.time*5.3f) + 1f)*0.19f);
        }
    }

    private void UpdateHint()
    {
        if (view == null || view.AimHintText == null || config == null) return;
        switch(stage)
        {
            case Stage.Held: view.AimHintText.text = config.PegHint; break;
            case Stage.Rolling:
                if (leverActivated)
                    view.AimHintText.text = config.ArmedHint;
                else if (Time.time - rollingStartedAt >=
                    Mathf.Max(0f, config.ForkArrivalSeconds - 2.6f))
                    view.AimHintText.text = "LEVER - NOW!";
                else
                    view.AimHintText.text = config.LeverHint;
                break;
            case Stage.Flying: view.AimHintText.text = config.FlyingHint; break;
            case Stage.Splash: view.AimHintText.text = "LOG IN RIVER!"; break;
            case Stage.Clear: view.AimHintText.text = config.ClearHint; break;
            case Stage.Lost: view.AimHintText.text = config.MissedHint; break;
            case Stage.Retrying: view.AimHintText.text = config.MissedHint; break;
        }
    }

    private void RelabelSetupShot()
    {
        if (view == null || view.FeedbackText == null || string.IsNullOrEmpty(positiveFeedback))
            return;
        string message = view.FeedbackText.text;
        if (string.IsNullOrWhiteSpace(message) ||
            !message.StartsWith("MISS", StringComparison.Ordinal)) return;
        view.FeedbackText.text = positiveFeedback;
        view.FeedbackText.color = new Color(1f, 0.87f, 0.37f, 1f);
        if (view.ScreenFlash != null && view.ScreenFlash.gameObject.activeSelf)
        {
            Color previous = view.ScreenFlash.color;
            view.ScreenFlash.color = new Color(0.85f, 0.96f, 0.44f, previous.a);
        }
    }

    private void RefreshPrediction()
    {
        if (preview != null && InvalidatePrediction != null)
            InvalidatePrediction.Invoke(preview, null);
    }

    private void ClearState()
    {
        if (savedHint && view != null && view.AimHintText != null)
            view.AimHintText.text = originalHint;
        for(int i=0;i<fragments.Count;i++)
            if (fragments[i].Root != null) Destroy(fragments[i].Root.gameObject);
        fragments.Clear();
        config = null;
        stage = Stage.Held;
        mechanismRoot = null;
        logRoot = null;
        logRenderer = null;
        logCollider = null;
        pegCollider = null;
        pegRenderer = null;
        leverCollider = null;
        leverRenderer = null;
        rampRoot = null;
        rampRenderer = null;
        gateCollider = null;
        gateRenderer = null;
        intactGate = null;
        brokenGate = null;
        splinterSprite = null;
        tether = null;
        pegSupport = null;
        leverGlow = null;
        sound = null;
        rumble = null;
        crash = null;
        splash = null;
        bow = null;
        preview = null;
        view = null;
        savedHint = false;
        originalHint = null;
        leverActivated = false;
        gateBroken = false;
        splashPlayed = false;
        logResting = false;
        rollingStartedAt = 0f;
        phaseStartedAt = 0f;
        positiveHitAt = -99f;
        positiveFeedback = null;
        rolledDistance = 0f;
    }

    private void OnDisable()
    {
        ClearState();
        observedLevelObjects = null;
    }
}

/// <summary>Physical first-arrow impact on the big gold retaining peg.</summary>
[DisallowMultipleComponent]
internal sealed class RollingLogPegHitReceiver : MonoBehaviour
{
    private WildheartRollingTroubleRuntime owner;
    public void Configure(WildheartRollingTroubleRuntime runtime) { owner = runtime; }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (owner == null || collision == null) return;
        ArrowController arrow = collision.collider != null
            ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null)
            arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.HasFired) owner.OnPegStruck(arrow);
    }
}

/// <summary>Physical second-arrow impact on the large red steering lever.</summary>
[DisallowMultipleComponent]
internal sealed class RollingLogLeverHitReceiver : MonoBehaviour
{
    private WildheartRollingTroubleRuntime owner;
    public void Configure(WildheartRollingTroubleRuntime runtime) { owner = runtime; }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (owner == null || collision == null) return;
        ArrowController arrow = collision.collider != null
            ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null)
            arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.HasFired) owner.OnLeverStruck(arrow);
    }
}
