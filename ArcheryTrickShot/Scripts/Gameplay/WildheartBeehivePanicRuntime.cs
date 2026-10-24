using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(1730)]
[DisallowMultipleComponent]
public sealed class WildheartBeehivePanicRuntime : MonoBehaviour
{
    private const string ConfigPrefix = "Levels/BeehivePanic_";
    private static readonly FieldInfo CurrentLevelField =
        typeof(LevelManager).GetField("currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ViewField =
        typeof(GameUIController).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic);

    [Serializable]
    private sealed class HiveConfig
    {
        public string MechanismId = "wildheart_angry_bees";
        public Vector2 HivePosition = new Vector2(0.55f, -0.62f);
        public float HiveWorldWidth = 2.24f;
        public float HiveWorldHeight = 2.92f;
        public float VineWorldHeight = 3.38f;
        public float DropDistance = 3.30f;
        public float DropSeconds = 0.32f;
        public int BeeCount = 22;
        public float SwarmDuration = 4.2f;
        public string HiveSpritePath;
        public string CrackedHiveSpritePath;
        public string VineSpritePath;
        public string BeeSpritePath;
        public string PollenSpritePath;
        public string CrackAudioPath;
        public string BuzzAudioPath;
        public string SplashAudioPath;
        public string WaitingHint = "HIT THE HIVE";
        public string FallingHint = "BEES INCOMING!";
        public string SwarmHint = "SHOOT THE NECTAR!";
        public string DistractedHint = "BEES DISTRACTED!";
        public string ReadyHint = "TARGET EXPOSED!";
        public Vector2 NectarPodPosition = new Vector2(-2.85f, 1.18f);
        public float NectarPodWorldWidth = 1.18f;
        public Vector2 FlowerPosition = new Vector2(4.05f, 1.56f);
        public float FlowerWorldWidth = 2.35f;
        public Vector2 BeeShieldCenter = new Vector2(-2.95f, -0.28f);
        public float BeeShieldRadius = 1.20f;
        public float SwarmApproachSeconds = 1.35f;
        public float SwarmRedirectSeconds = 1.15f;
        public float NectarVineHeight = 2.0f;
        public string PetalParticleResourcePath;
        public string GlowParticleResourcePath;
        public string NectarPodSpritePath;
        public string FlowerSpritePath;
    }

    private enum Phase
    {
        WaitingForHive,
        HiveFalling,
        BeesThreatening,
        NectarTriggered,
        TargetClear
    }

    private sealed class Bee
    {
        public Transform Root;
        public SpriteRenderer Renderer;
        public float Phase;
        public Vector2 Offset;
    }

    private LevelManager manager;
    private Transform observedLevelObjects;
    private HiveConfig activeConfig;
    private GameUIView view;
    private string savedHint;
    private bool savedHintExists;

    private Transform hiveRoot;
    private SpriteRenderer hiveRenderer;
    private PolygonCollider2D hiveCollider;
    private SpriteRenderer vineRenderer;
    private SpriteRenderer hiveGlow;
    private SpriteRenderer nectarGlow;
    private SpriteRenderer flowerGlow;
    private Transform nectarPodRoot;
    private SpriteRenderer nectarRenderer;
    private Collider2D nectarCollider;
    private Transform flowerRoot;
    private SpriteRenderer flowerRenderer;
    private Transform beeThreatRoot;
    private CircleCollider2D beeThreatCollider;
    private SpriteRenderer beeThreatRenderer;
    private Bee[] bees;
    private AudioSource audioSource;
    private AudioClip crackAudio;
    private AudioClip buzzAudio;
    private AudioClip splashAudio;
    private Sprite beeSprite;
    private Sprite pollenSprite;
    private Sprite petalParticleSprite;
    private Sprite glowParticleSprite;
    private Sprite vineSprite;
    private float hiveDropAt;
    private static Material petalMaterial;
    private static Material orbMaterial;
    private ParticleSystem honeyBurst;
    private ParticleSystem riverSplash;
    private ParticleSystem nectarBurst;
    private ParticleSystem flowerPollen;
    private Phase phase;
    private float phaseAt;
    private float hiveHitAt;
    private float lastPositiveHitTime;
    private string lastPositiveLabel;
    private Coroutine fallRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager levelManager = FindFirstObjectByType<LevelManager>();
        if (levelManager != null && levelManager.GetComponent<WildheartBeehivePanicRuntime>() == null)
            levelManager.gameObject.AddComponent<WildheartBeehivePanicRuntime>();
    }

    private void Awake() => manager = GetComponent<LevelManager>();

    private void LateUpdate()
    {
        Transform currentObjects = transform.Find("LevelObjects");
        if (currentObjects != observedLevelObjects)
        {
            ResetRuntime();
            observedLevelObjects = currentObjects;
            if (currentObjects != null)
                TryCreateForLevel(currentObjects);
        }
        if (activeConfig == null)
            return;

        AnimateHighlights();
        AnimateBees();
        UpdateHint();
        if (Time.time - lastPositiveHitTime < 0.75f)
            RelabelIntentionalSetupShot();
    }

    private void TryCreateForLevel(Transform parent)
    {
        LevelData level = CurrentLevelField != null && manager != null ? CurrentLevelField.GetValue(manager) as LevelData : null;
        if (level == null)
            return;

        TextAsset configText = Resources.Load<TextAsset>(ConfigPrefix + level.LevelNumber.ToString("000"));
        if (configText == null)
            return;

        HiveConfig config;
        try { config = JsonUtility.FromJson<HiveConfig>(configText.text); }
        catch (Exception ex)
        {
            Debug.LogError("[Beehive Panic] Invalid JSON: " + ex.Message, parent);
            return;
        }
        if (config == null)
            return;

        Sprite intact = Resources.Load<Sprite>(config.HiveSpritePath);
        Sprite broken = Resources.Load<Sprite>(config.CrackedHiveSpritePath);
        Sprite vine = Resources.Load<Sprite>(config.VineSpritePath);
        Sprite nectarPod = Resources.Load<Sprite>(config.NectarPodSpritePath);
        Sprite flowers = Resources.Load<Sprite>(config.FlowerSpritePath);
        vineSprite = vine;
        petalParticleSprite = Resources.Load<Sprite>(config.PetalParticleResourcePath);
        glowParticleSprite = Resources.Load<Sprite>(config.GlowParticleResourcePath);
        beeSprite = Resources.Load<Sprite>(config.BeeSpritePath);
        pollenSprite = Resources.Load<Sprite>(config.PollenSpritePath);
        if (intact == null || broken == null || vine == null || nectarPod == null || flowers == null || beeSprite == null)
        {
            Debug.LogError("[Beehive Panic] Required authored sprite missing.", parent);
            return;
        }

        activeConfig = config;
        phase = Phase.WaitingForHive;
        phaseAt = Time.time;
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        view = ui != null && ViewField != null ? ViewField.GetValue(ui) as GameUIView : null;
        if (view != null && view.AimHintText != null)
        {
            savedHint = view.AimHintText.text;
            savedHintExists = true;
        }

        GameObject root = new GameObject("BeehivePanic_" + config.MechanismId);
        root.transform.SetParent(parent, false);

        // Hive vine
        GameObject vineObject = new GameObject("SuspendedVine", typeof(SpriteRenderer));
        vineObject.transform.SetParent(root.transform, false);
        vineObject.transform.position = new Vector3(config.HivePosition.x, config.HivePosition.y + config.HiveWorldHeight * 0.46f + config.VineWorldHeight * 0.48f, 0f);
        vineRenderer = vineObject.GetComponent<SpriteRenderer>();
        vineRenderer.sprite = vine;
        vineRenderer.sortingOrder = 10;
        ScaleSprite(vineObject.transform, vine, 0f, config.VineWorldHeight);

        // Hive root + glow
        GameObject hiveObject = new GameObject("EntireShootableHive");
        hiveObject.transform.SetParent(root.transform, false);
        hiveObject.transform.position = new Vector3(config.HivePosition.x, config.HivePosition.y, -0.015f);
        hiveRoot = hiveObject.transform;

        if (pollenSprite != null)
        {
            GameObject glow = new GameObject("SoftHiveAmberAura", typeof(SpriteRenderer));
            glow.transform.SetParent(hiveRoot, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            hiveGlow = glow.GetComponent<SpriteRenderer>();
            hiveGlow.sprite = pollenSprite;
            hiveGlow.sortingOrder = 14;
            hiveGlow.color = new Color(1f, 0.86f, 0.38f, 0.22f);
            ScaleSprite(glow.transform, pollenSprite, config.HiveWorldWidth * 1.6f, 0f);
        }
        GameObject hiveArt = new GameObject("AuthoredHive", typeof(SpriteRenderer));
        hiveArt.transform.SetParent(hiveRoot, false);
        hiveRenderer = hiveArt.GetComponent<SpriteRenderer>();
        hiveRenderer.sprite = intact;
        hiveRenderer.sortingOrder = 16;
        ScaleSprite(hiveArt.transform, intact, config.HiveWorldWidth, config.HiveWorldHeight);
        hiveCollider = hiveObject.AddComponent<PolygonCollider2D>();
        hiveCollider.isTrigger = false;
        float rx = config.HiveWorldWidth * 0.47f;
        float ry = config.HiveWorldHeight * 0.46f;
        hiveCollider.SetPath(0, new Vector2[] {
            new Vector2(0f, ry), new Vector2(-rx * 0.53f, ry * 0.87f), new Vector2(-rx * 0.88f, ry * 0.48f),
            new Vector2(-rx, 0f), new Vector2(-rx * 0.78f, -ry * 0.61f), new Vector2(0f, -ry),
            new Vector2(rx * 0.78f, -ry * 0.61f), new Vector2(rx, 0f), new Vector2(rx * 0.88f, ry * 0.48f), new Vector2(rx * 0.53f, ry * 0.87f)});
        hiveObject.AddComponent<WildheartHiveArrowHit>().Configure(this);

        // A visible vine connects the nectar to the overhead forest.
        // No tiny hidden latch: the entire pod is the shot target.
        GameObject nectarVine = new GameObject("NectarSuspensionVine", typeof(SpriteRenderer));
        nectarVine.transform.SetParent(root.transform, false);
        nectarVine.transform.position = new Vector3(
            config.NectarPodPosition.x, config.NectarPodPosition.y + config.NectarVineHeight * 0.60f, 0.02f);
        var nectarVineRenderer = nectarVine.GetComponent<SpriteRenderer>();
        nectarVineRenderer.sprite = vineSprite;
        nectarVineRenderer.sortingOrder = 12;
        ScaleSprite(nectarVine.transform, vineSprite, 0f, config.NectarVineHeight * 1.22f);

        // Nectar pod above-left / forward from Khaem.
        GameObject nectar = new GameObject("NectarPod");
        nectar.transform.SetParent(root.transform, false);
        nectar.transform.position = new Vector3(config.NectarPodPosition.x, config.NectarPodPosition.y, -0.01f);
        nectarPodRoot = nectar.transform;
        GameObject nectarGlowObj = new GameObject("NectarGlow", typeof(SpriteRenderer));
        nectarGlowObj.transform.SetParent(nectarPodRoot, false);
        nectarGlow = nectarGlowObj.GetComponent<SpriteRenderer>();
        nectarGlow.sprite = pollenSprite != null ? pollenSprite : beeSprite;
        nectarGlow.sortingOrder = 11;
        nectarGlow.color = new Color(1f, 0.6f, 0.9f, 0f);
        ScaleSprite(nectarGlowObj.transform, nectarGlow.sprite, config.NectarPodWorldWidth * 1.8f, 0f);
        GameObject nectarArt = new GameObject("NectarArt", typeof(SpriteRenderer));
        nectarArt.transform.SetParent(nectarPodRoot, false);
        nectarRenderer = nectarArt.GetComponent<SpriteRenderer>();
        nectarRenderer.sprite = nectarPod;
        nectarRenderer.sortingOrder = 13;
        ScaleSprite(nectarArt.transform, nectarPod, config.NectarPodWorldWidth, 0f);
        CircleCollider2D ncol = nectar.AddComponent<CircleCollider2D>();
        ncol.radius = config.NectarPodWorldWidth * 0.43f;
        nectarCollider = ncol;
        // Not shootable until the bee phase: prevent an accidental early
        // setup shot from irreversibly consuming the rescue prop.
        nectarCollider.enabled = false;
        nectarObjectAdd(nectar);

        // Flowers destination
        GameObject fl = new GameObject("DistractorFlowers");
        fl.transform.SetParent(root.transform, false);
        fl.transform.position = new Vector3(config.FlowerPosition.x, config.FlowerPosition.y, -0.02f);
        flowerRoot = fl.transform;
        GameObject flowerGlowObj = new GameObject("FlowerGlow", typeof(SpriteRenderer));
        flowerGlowObj.transform.SetParent(flowerRoot, false);
        flowerGlow = flowerGlowObj.GetComponent<SpriteRenderer>();
        flowerGlow.sprite = pollenSprite != null ? pollenSprite : beeSprite;
        flowerGlow.sortingOrder = 10;
        flowerGlow.color = new Color(1f, 0.76f, 0.2f, 0f);
        ScaleSprite(flowerGlowObj.transform, flowerGlow.sprite, config.FlowerWorldWidth * 1.9f, 0f);
        GameObject flowerArt = new GameObject("FlowerArt", typeof(SpriteRenderer));
        flowerArt.transform.SetParent(flowerRoot, false);
        flowerRenderer = flowerArt.GetComponent<SpriteRenderer>();
        flowerRenderer.sprite = flowers;
        flowerRenderer.sortingOrder = 12;
        flowerRenderer.color = Color.white;
        ScaleSprite(flowerArt.transform, flowers, config.FlowerWorldWidth, 0f);

        // Bee threat shield between player and direct line to target.
        GameObject shield = new GameObject("BeeThreatShield");
        shield.transform.SetParent(root.transform, false);
        shield.transform.position = new Vector3(config.BeeShieldCenter.x, config.BeeShieldCenter.y, -0.04f);
        beeThreatRoot = shield.transform;
        GameObject threatVisual = new GameObject("VisibleBeeCloud", typeof(SpriteRenderer));
        threatVisual.transform.SetParent(beeThreatRoot, false);
        beeThreatRenderer = threatVisual.GetComponent<SpriteRenderer>();
        beeThreatRenderer.sprite = pollenSprite != null ? pollenSprite : beeSprite;
        beeThreatRenderer.sortingOrder = 11;
        beeThreatRenderer.color = new Color(1f, 0.78f, 0.12f, 0f);
        // Only the visual child is scaled; the root collider remains in
        // WORLD-UNIT local coordinates and matches the swarm's visible radius.
        ScaleSprite(threatVisual.transform, beeThreatRenderer.sprite,
            config.BeeShieldRadius * 2.45f, 0f);
        beeThreatCollider = shield.AddComponent<CircleCollider2D>();
        beeThreatCollider.radius = config.BeeShieldRadius;
        beeThreatCollider.enabled = false;
        shield.AddComponent<WildheartBeeThreatBlocker>().Configure(this);

        crackAudio = LoadOptionalClip(config.CrackAudioPath);
        buzzAudio = LoadOptionalClip(config.BuzzAudioPath);
        splashAudio = LoadOptionalClip(config.SplashAudioPath);
        audioSource = root.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        honeyBurst = BuildBurst(root.transform, "HoneyBurst", new Color(1f, 0.72f, 0.1f), new Color(0.9f,0.55f,0.05f));
        riverSplash = BuildBurst(root.transform, "RiverSplash", new Color(0.90f, 0.97f, 1f), new Color(0.64f,0.87f,1f));
        nectarBurst = BuildBurst(root.transform, "NectarBurst", new Color(1f, 0.48f, 0.68f), new Color(1f, 0.9f, 0.2f));
        flowerPollen = BuildBurst(root.transform, "FlowerPollen", new Color(1f, 0.83f, 0.25f), new Color(1f,0.48f,0.70f));
        CreateBees(root.transform, config.BeeCount);
    }

    private void nectarObjectAdd(GameObject nectar)
    {
        var hit = nectar.AddComponent<WildheartNectarArrowHit>();
        hit.Configure(this);
    }

    private void CreateBees(Transform parent, int count)
    {
        bees = new Bee[Mathf.Clamp(count, 4, 32)];
        for (int i = 0; i < bees.Length; i++)
        {
            Bee b = new Bee();
            GameObject go = new GameObject("Bee_" + i, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            b.Root = go.transform;
            b.Renderer = go.GetComponent<SpriteRenderer>();
            b.Renderer.sprite = beeSprite;
            b.Renderer.sortingOrder = 18 + (i % 2);
            b.Renderer.enabled = false;
            float scale = 0.36f + (i % 3) * 0.06f;
            ScaleSprite(go.transform, beeSprite, scale, 0f);
            b.Phase = i * 0.53f;
            float ring = 0.15f + (i % 5) * 0.06f;
            b.Offset = new Vector2(Mathf.Cos(b.Phase) * ring, Mathf.Sin(b.Phase * 1.2f) * ring);
            bees[i] = b;
        }
    }

    private ParticleSystem BuildBurst(Transform parent, string name, Color start, Color end)
    {
        // Unity 6: configure while inactive, before first Play().
        GameObject go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.50f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.45f, 3.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startColor = new ParticleSystem.MinMaxGradient(start, end);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;
        main.startRotation = new ParticleSystem.MinMaxCurve(-1.9f, 1.9f);
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.18f;
        var gravity = ps.main;
        gravity.gravityModifier = 0.17f;
        ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 21;
            bool petal = name == "NectarBurst";
            Sprite particleSprite = petal ? petalParticleSprite : glowParticleSprite;
            if (particleSprite != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    Material shared = petal ? petalMaterial : orbMaterial;
                    if (shared == null)
                    {
                        shared = new Material(shader);
                        shared.name = petal ? "WildheartPetalParticles" : "WildheartPollenParticles";
                        shared.mainTexture = particleSprite.texture;
                        if (petal) petalMaterial = shared;
                        else orbMaterial = shared;
                    }
                    rend.sharedMaterial = shared;
                }
            }
        }
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.SetActive(true);
        return ps;
    }

    private AudioClip LoadOptionalClip(string path)
    {
        return string.IsNullOrEmpty(path) ? null : Resources.Load<AudioClip>(path);
    }

    private void ScaleSprite(Transform target, Sprite sprite, float width, float height)
    {
        if (sprite == null || target == null)
            return;
        Vector2 size = sprite.bounds.size;
        float sx = width > 0f ? width / Mathf.Max(0.001f, size.x) : 1f;
        float sy = height > 0f ? height / Mathf.Max(0.001f, size.y) : sx;
        target.localScale = new Vector3(sx, sy, 1f);
    }

    public void OnHiveStruck(ArrowController arrow)
    {
        if (phase != Phase.WaitingForHive || hiveRoot == null)
            return;
        phase = Phase.HiveFalling;
        phaseAt = Time.time;
        hiveHitAt = Time.time;
        hiveDropAt = Time.time;
        lastPositiveHitTime = Time.time;
        lastPositiveLabel = "HIVE DOWN!";
        if (hiveCollider != null)
            hiveCollider.enabled = false;
        if (hiveRenderer != null && activeConfig != null)
            hiveRenderer.sprite = Resources.Load<Sprite>(activeConfig.CrackedHiveSpritePath);
        if (honeyBurst != null)
        {
            honeyBurst.transform.position = hiveRoot.position;
            honeyBurst.Emit(32);
        }
        if (audioSource != null && crackAudio != null)
            audioSource.PlayOneShot(crackAudio, 0.95f);
        if (audioSource != null && buzzAudio != null)
            audioSource.PlayOneShot(buzzAudio, 0.7f);
        if (fallRoutine != null)
            StopCoroutine(fallRoutine);
        fallRoutine = StartCoroutine(DropHiveRoutine());
    }

    internal void OnSwarmBlockedArrow(ArrowController arrow)
    {
        if (arrow == null || !arrow.HasFired || phase != Phase.BeesThreatening)
            return;
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        ui?.PlayBonusPropFeedback(LevelData.BonusPropStyle.Bell,
            "BEES! HIT NECTAR!", 0, activeConfig.BeeShieldCenter);
        ATSHaptics.Pulse();
    }

    public void OnNectarStruck(ArrowController arrow)
    {
        if ((phase != Phase.BeesThreatening && phase != Phase.HiveFalling) || nectarPodRoot == null)
            return;
        phase = Phase.NectarTriggered;
        phaseAt = Time.time;
        lastPositiveHitTime = Time.time;
        lastPositiveLabel = "NECTAR BURST!";
        if (nectarCollider != null)
            nectarCollider.enabled = false;
        if (nectarRenderer != null)
            nectarRenderer.enabled = false;
        if (nectarGlow != null)
            nectarGlow.enabled = false;
        if (nectarBurst != null)
        {
            nectarBurst.transform.position = nectarPodRoot.position;
            nectarBurst.Emit(42);
        }
        if (flowerPollen != null && flowerRoot != null)
        {
            flowerPollen.transform.position = flowerRoot.position;
            flowerPollen.Emit(46);
        }
        // Keep collision tied to the still-visible swarm until the animated
        // diversion is complete. The next arrow cannot bypass an active cloud.
        if (audioSource != null && crackAudio != null)
            audioSource.PlayOneShot(crackAudio, 0.7f);
        ATSHaptics.Pulse();
        GameUIController ui = FindFirstObjectByType<GameUIController>();
        ui?.PlayBonusPropFeedback(LevelData.BonusPropStyle.Bell,
            "NECTAR BLOOM!", 0, activeConfig.NectarPodPosition);
        StartCoroutine(ClearAfterDistraction());
    }

    private IEnumerator DropHiveRoutine()
    {
        if (activeConfig == null || hiveRoot == null)
            yield break;
        Vector3 start = hiveRoot.position;
        Quaternion startRotation = hiveRoot.rotation;
        bool splashed = false;
        float elapsed = 0f;
        if (vineRenderer != null)
            vineRenderer.enabled = false;
        while (elapsed < activeConfig.DropSeconds && hiveRoot != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, activeConfig.DropSeconds));
            float progress = t * t * (3f - 2f * t);
            hiveRoot.position = start + new Vector3(t * 0.50f, -activeConfig.DropDistance * progress, 0f);
            hiveRoot.rotation = startRotation * Quaternion.Euler(0f, 0f, -35f * progress + 5f * Mathf.Sin(t * 21f) * (1f - t));
            if (!splashed && t > 0.78f)
            {
                splashed = true;
                if (riverSplash != null)
                {
                    riverSplash.transform.position = new Vector3(start.x + 0.44f, -3.48f, -0.05f);
                    riverSplash.Emit(40);
                }
                if (audioSource != null && splashAudio != null)
                    audioSource.PlayOneShot(splashAudio, 0.7f);
            }
            yield return null;
        }
        if (hiveRenderer != null)
            hiveRenderer.enabled = false;
        // Bees fly toward Khaem before becoming a physically blocking cloud.
        phase = Phase.BeesThreatening;
        phaseAt = Time.time;
        if (beeThreatCollider != null)
            beeThreatCollider.enabled = true;
        if (nectarCollider != null)
            nectarCollider.enabled = true;
        fallRoutine = null;
    }

    private IEnumerator ClearAfterDistraction()
    {
        yield return new WaitForSeconds(
            Mathf.Max(0.85f, activeConfig.SwarmRedirectSeconds));
        if (beeThreatCollider != null)
            beeThreatCollider.enabled = false;
        phase = Phase.TargetClear;
        phaseAt = Time.time;
    }

    private void AnimateHighlights()
    {
        if (hiveGlow != null && phase == Phase.WaitingForHive)
        {
            Color c = hiveGlow.color;
            c.a = 0.18f + 0.08f * Mathf.Sin(Time.time * 2.7f);
            hiveGlow.color = c;
        }
        if (nectarGlow != null)
        {
            bool on = phase == Phase.BeesThreatening || phase == Phase.HiveFalling;
            nectarGlow.enabled = on;
            if (on)
            {
                Color c = nectarGlow.color;
                c.a = 0.17f + 0.13f * Mathf.Sin(Time.time * 5.4f);
                nectarGlow.color = c;
            }
        }
        if (flowerGlow != null)
        {
            bool on = phase == Phase.NectarTriggered || phase == Phase.TargetClear;
            if (on)
            {
                flowerGlow.enabled = true;
                Color c = flowerGlow.color;
                c.a = phase == Phase.TargetClear ? 0.32f : (0.18f + 0.15f * Mathf.Sin(Time.time * 8.2f));
                flowerGlow.color = c;
            }
            else flowerGlow.enabled = false;
        }
        if (beeThreatRenderer != null)
        {
            if (phase == Phase.BeesThreatening || phase == Phase.HiveFalling)
            {
                beeThreatRenderer.enabled = true;
                Color c = beeThreatRenderer.color;
                c.a = 0.12f + 0.08f * Mathf.Sin(Time.time * 11f);
                beeThreatRenderer.color = c;
            }
            else beeThreatRenderer.enabled = false;
        }
    }

    private void AnimateBees()
    {
        if (bees == null || activeConfig == null)
            return;
        if (phase == Phase.WaitingForHive)
        {
            foreach (Bee bee in bees)
                if (bee != null && bee.Renderer != null)
                    bee.Renderer.enabled = false;
            return;
        }

        float time = Time.time;
        float sinceHive = Mathf.Max(0f, time - hiveHitAt);
        for (int i = 0; i < bees.Length; i++)
        {
            Bee b = bees[i];
            if (b == null || b.Root == null || b.Renderer == null)
                continue;
            b.Renderer.enabled = true;
            float fly = sinceHive * (5.9f + (i % 5) * 0.51f) + b.Phase;
            float orbitAngle = fly * 1.85f + b.Phase * 3.1f;
            Vector2 home = activeConfig.HivePosition + b.Offset;
            Vector2 hover = activeConfig.BeeShieldCenter +
                new Vector2(Mathf.Cos(orbitAngle) * activeConfig.BeeShieldRadius * 0.83f,
                    Mathf.Sin(orbitAngle * 0.92f) * activeConfig.BeeShieldRadius * 0.78f);
            Vector2 flower = activeConfig.FlowerPosition +
                new Vector2(Mathf.Cos(orbitAngle) * (0.50f + i % 4 * 0.12f),
                    Mathf.Sin(orbitAngle) * (0.36f + i % 3 * 0.1f));
            Vector2 position;
            if (phase == Phase.HiveFalling || phase == Phase.BeesThreatening)
            {
                // Approaching swarm: a readable outward burst then a fast
                // converging attack on Khaem's real firing corridor.
                float t = Mathf.Clamp01((time - hiveDropAt - (i % 5) * 0.04f) /
                    Mathf.Max(0.6f, activeConfig.SwarmApproachSeconds));
                float ease = t * t * (3f - 2f * t);
                Vector2 arc = Vector2.up * Mathf.Sin(t * Mathf.PI) *
                    (0.35f + i % 3 * 0.10f);
                position = Vector2.Lerp(home, hover, ease) + arc;
            }
            else if (phase == Phase.NectarTriggered)
            {
                // A longer synchronized peel-away instead of a near-instant
                // teleport to the flowers. Bees arc ABOVE the shot lane.
                float t = Mathf.Clamp01((time - phaseAt - (i % 4) * 0.035f) /
                    Mathf.Max(0.75f, activeConfig.SwarmRedirectSeconds));
                float ease = t * t * (3f - 2f * t);
                position = Vector2.Lerp(hover, flower, ease) +
                    Vector2.up * Mathf.Sin(t * Mathf.PI) * 2.05f;
            }
            else
                position = flower;

            b.Root.position = new Vector3(position.x, position.y, -0.11f);
            b.Root.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(fly * 1.3f) * 18f);
            Color tint = Color.white;
            tint.a = Mathf.Clamp01((time - hiveHitAt) * 6f);
            b.Renderer.color = tint;
            b.Renderer.flipX = Mathf.Sin(orbitAngle) < 0f;
        }
    }

    private void UpdateHint()
    {
        if (view == null || view.AimHintText == null || activeConfig == null)
            return;
        switch (phase)
        {
            case Phase.WaitingForHive:
                view.AimHintText.text = activeConfig.WaitingHint;
                break;
            case Phase.HiveFalling:
                view.AimHintText.text = activeConfig.FallingHint;
                break;
            case Phase.BeesThreatening:
                view.AimHintText.text = activeConfig.SwarmHint;
                break;
            case Phase.NectarTriggered:
                view.AimHintText.text = activeConfig.DistractedHint;
                break;
            case Phase.TargetClear:
                view.AimHintText.text = activeConfig.ReadyHint;
                break;
        }
    }

    private void RelabelIntentionalSetupShot()
    {
        if (view == null || view.FeedbackText == null || string.IsNullOrEmpty(lastPositiveLabel))
            return;
        string content = view.FeedbackText.text;
        if (string.IsNullOrEmpty(content) || !content.StartsWith("MISS", StringComparison.Ordinal))
            return;
        view.FeedbackText.text = lastPositiveLabel + content.Substring(4);
        view.FeedbackText.color = new Color(1f, 0.85f, 0.31f, 1f);
        if (view.ScreenFlash != null && view.ScreenFlash.gameObject.activeSelf)
        {
            Color flash = view.ScreenFlash.color;
            flash.r = 1f; flash.g = 0.71f; flash.b = 0.19f;
            view.ScreenFlash.color = flash;
        }
    }

    private void ResetRuntime()
    {
        if (savedHintExists && view != null && view.AimHintText != null)
            view.AimHintText.text = savedHint;
        StopAllCoroutines();
        activeConfig = null;
        view = null;
        savedHintExists = false;
        savedHint = null;
        hiveRoot = null; hiveRenderer = null; hiveCollider = null; vineRenderer = null; hiveGlow = null;
        nectarGlow = null; flowerGlow = null; nectarPodRoot = null; nectarRenderer = null; nectarCollider = null;
        flowerRoot = null; flowerRenderer = null; beeThreatRoot = null; beeThreatCollider = null; beeThreatRenderer = null;
        bees = null; petalParticleSprite = null; glowParticleSprite = null; vineSprite = null;
        hiveDropAt = 0f; audioSource = null; crackAudio = null; buzzAudio = null; splashAudio = null;
        honeyBurst = null; riverSplash = null; nectarBurst = null; flowerPollen = null;
        phase = Phase.WaitingForHive; phaseAt = 0f; hiveHitAt = 0f; lastPositiveHitTime = 0f; lastPositiveLabel = null; fallRoutine = null;
    }

    private void OnDisable()
    {
        ResetRuntime();
        observedLevelObjects = null;
    }
}

[DisallowMultipleComponent]
internal sealed class WildheartHiveArrowHit : MonoBehaviour
{
    private WildheartBeehivePanicRuntime mechanism;
    public void Configure(WildheartBeehivePanicRuntime owner) => mechanism = owner;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (mechanism == null || collision == null) return;
        ArrowController arrow = collision.collider != null ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null) arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.HasFired) mechanism.OnHiveStruck(arrow);
    }
}

[DisallowMultipleComponent]
internal sealed class WildheartNectarArrowHit : MonoBehaviour
{
    private WildheartBeehivePanicRuntime mechanism;
    public void Configure(WildheartBeehivePanicRuntime owner) => mechanism = owner;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (mechanism == null || collision == null) return;
        ArrowController arrow = collision.collider != null ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null) arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.HasFired) mechanism.OnNectarStruck(arrow);
    }
}

[DisallowMultipleComponent]
internal sealed class WildheartBeeThreatBlocker : MonoBehaviour
{
    private WildheartBeehivePanicRuntime mechanism;
    public void Configure(WildheartBeehivePanicRuntime owner) => mechanism = owner;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (mechanism == null || collision == null)
            return;
        ArrowController arrow = collision.collider != null
            ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.gameObject != null)
            arrow = collision.gameObject.GetComponentInParent<ArrowController>();
        if (arrow != null)
            mechanism.OnSwarmBlockedArrow(arrow);
    }
}
