using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class GameAudioController : MonoBehaviour
{
    private const string BackgroundMusicResourcePath =
        "Audio/Music/Temple_Archery_Mystic_Egyptian_Loop_v2";

    public static GameAudioController Instance { get; private set; }

    [Header("Optional Inspector Overrides")]
    [SerializeField] private AudioClip backgroundMusicClip;
    [SerializeField] private AudioClip shotClip;
    [SerializeField] private AudioClip mirrorClip;
    [SerializeField] private AudioClip targetHitClip;
    [SerializeField] private AudioClip wallHitClip;
    [SerializeField] private AudioClip missClip;
    [SerializeField] private AudioClip levelCompleteClip;
    [SerializeField] private AudioClip levelFailedClip;
    [SerializeField] private AudioClip uiClickClip;

    [Header("SFX Mix")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 0.90f;

    [Range(0f, 1f)]
    [SerializeField] private float shotVolume = 0.90f;

    [Range(0f, 1f)]
    [SerializeField] private float impactVolume = 0.90f;

    [Range(0f, 1f)]
    [SerializeField] private float resultVolume = 0.80f;

    [Range(0f, 1f)]
    [SerializeField] private float uiVolume = 0.55f;

    private AudioSource sfxSource;
    private AudioSource musicSource;
    private AudioSource ricochetSource;
    private Coroutine musicDuckRoutine;
    private float configuredMusicVolume;

    private void Awake()
    {
        if (Instance != null &&
            Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Configure(GameConfig.Load());
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Configure(GameConfig config)
    {
        config ??= GameConfig.Load();

        EnsureAudioSources();
        ConfigureSfxSource();
        ConfigureMusicSource(config);
        LoadMissingClips();
        StartBackgroundMusic(config);
    }

    private void EnsureAudioSources()
    {
        AudioSource[] sources =
            GetComponents<AudioSource>();

        if (sources.Length > 0)
        {
            sfxSource = sources[0];
        }
        else
        {
            sfxSource =
                gameObject.AddComponent<AudioSource>();
        }

        if (musicSource != null &&
            musicSource != sfxSource)
        {
            return;
        }

        sources = GetComponents<AudioSource>();

        if (sources.Length > 1)
        {
            musicSource = sources[1];
        }
        else
        {
            musicSource =
                gameObject.AddComponent<AudioSource>();
        }

        sources = GetComponents<AudioSource>();
        if (sources.Length > 2)
        {
            ricochetSource = sources[2];
        }
        else
        {
            ricochetSource =
                gameObject.AddComponent<AudioSource>();
        }

        ricochetSource.playOnAwake = false;
        ricochetSource.loop = false;
        ricochetSource.spatialBlend = 0f;
        ricochetSource.dopplerLevel = 0f;
        ricochetSource.volume = 1f;
        ricochetSource.priority = 48;
    }

    private void ConfigureSfxSource()
    {
        if (sfxSource == null)
            return;

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.dopplerLevel = 0f;
        sfxSource.volume = 1f;
        sfxSource.priority = 64;
    }

    private void ConfigureMusicSource(
        GameConfig config)
    {
        if (musicSource == null)
            return;

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.dopplerLevel = 0f;
        configuredMusicVolume = Mathf.Clamp01(config.MusicVolume);
        musicSource.volume = configuredMusicVolume;
        musicSource.priority = 128;
    }

    private void LoadMissingClips()
    {
        backgroundMusicClip ??=
            Resources.Load<AudioClip>(
                BackgroundMusicResourcePath);

        shotClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/bow_release");

        mirrorClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/mirror_ricochet");

        targetHitClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/target_hit");

        wallHitClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/wall_hit");

        missClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/miss");

        levelCompleteClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/level_complete");

        levelFailedClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/level_failed");

        uiClickClip ??=
            Resources.Load<AudioClip>(
                "Audio/SFX/ui_click");
    }

    private void StartBackgroundMusic(
        GameConfig config)
    {
        if (musicSource == null ||
            backgroundMusicClip == null)
        {
            if (backgroundMusicClip == null)
            {
                Debug.LogWarning(
                    $"Background music was not found at Resources/{BackgroundMusicResourcePath}.");
            }

            return;
        }

        configuredMusicVolume = Mathf.Clamp01(config.MusicVolume);
        musicSource.volume = configuredMusicVolume;

        if (musicSource.clip !=
            backgroundMusicClip)
        {
            musicSource.Stop();
            musicSource.clip =
                backgroundMusicClip;
        }

        if (!musicSource.isPlaying)
            musicSource.Play();
    }

    public void SetMusicVolume(float volume)
    {
        configuredMusicVolume = Mathf.Clamp01(volume);

        if (musicSource == null)
            return;

        if (musicDuckRoutine == null)
            musicSource.volume = configuredMusicVolume;
    }

    public void PauseMusic()
    {
        if (musicSource != null &&
            musicSource.isPlaying)
        {
            musicSource.Pause();
        }
    }

    public void ResumeMusic()
    {
        if (musicSource != null &&
            !musicSource.isPlaying &&
            musicSource.clip != null)
        {
            musicSource.UnPause();
        }
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }

    public void PlayShot()
    {
        Play(
            shotClip,
            shotVolume);
    }

    public void PlayMirror(int chainCount = 1)
    {
        if (mirrorClip == null)
            return;

        if (ricochetSource == null)
        {
            Play(mirrorClip, impactVolume);
            return;
        }

        // Each bounce rises slightly in pitch, turning a multi-ricochet shot
        // into a satisfying audible combo without needing extra audio assets.
        ricochetSource.pitch = Mathf.Clamp(
            0.96f + Mathf.Max(0, chainCount - 1) * 0.055f,
            0.96f,
            1.24f);

        ricochetSource.PlayOneShot(
            mirrorClip,
            Mathf.Clamp01(sfxVolume * impactVolume));
    }

    public void PlayTargetHit()
    {
        Play(
            targetHitClip,
            impactVolume);

        DuckMusic(0.68f, 0.34f);
    }

    // Backward-compatible alias for older LevelManager versions.
    public void PlayHit()
    {
        PlayTargetHit();
    }

    public void PlayWallHit()
    {
        Play(
            wallHitClip,
            impactVolume);
    }

    public void PlayMiss()
    {
        Play(
            missClip,
            impactVolume * 0.75f);
    }

    public void PlayLevelComplete()
    {
        Play(
            levelCompleteClip,
            resultVolume);

        DuckMusic(0.58f, 0.60f);
    }

    public void PlayLevelFailed()
    {
        Play(
            levelFailedClip,
            resultVolume);
    }

    public void PlayUIClick()
    {
        Play(
            uiClickClip,
            uiVolume);
    }

    private void DuckMusic(float multiplier, float duration)
    {
        if (musicSource == null ||
            !musicSource.isPlaying ||
            duration <= 0f)
        {
            return;
        }

        if (musicDuckRoutine != null)
            StopCoroutine(musicDuckRoutine);

        musicDuckRoutine = StartCoroutine(
            DuckMusicRoutine(
                Mathf.Clamp01(multiplier),
                duration));
    }

    private IEnumerator DuckMusicRoutine(
        float multiplier,
        float duration)
    {
        float normalVolume = configuredMusicVolume;
        float duckedVolume = normalVolume * multiplier;
        const float fadeTime = 0.07f;

        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            musicSource.volume = Mathf.Lerp(normalVolume, duckedVolume, t);
            yield return null;
        }

        float holdTime = Mathf.Max(0f, duration - fadeTime * 2f);
        if (holdTime > 0f)
            yield return new WaitForSecondsRealtime(holdTime);

        elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            musicSource.volume = Mathf.Lerp(duckedVolume, configuredMusicVolume, t);
            yield return null;
        }

        musicSource.volume = configuredMusicVolume;
        musicDuckRoutine = null;
    }

    private void Play(
        AudioClip clip,
        float categoryVolume)
    {
        if (clip == null ||
            sfxSource == null)
        {
            return;
        }

        sfxSource.PlayOneShot(
            clip,
            Mathf.Clamp01(
                sfxVolume *
                categoryVolume));
    }
}
