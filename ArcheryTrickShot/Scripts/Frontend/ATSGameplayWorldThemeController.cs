using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Data-driven gameplay background selector.
///
/// The scene keeps one reusable Background + BackgroundScaler. This controller
/// only swaps the authored Sprite for the currently playing level; no scene or
/// Inspector configuration is required.
///
/// Level -> visual-phase mapping lives in Resources/UI/Campaign/GameplayWorldThemes.json,
/// so future worlds/background phases can be added without changing level scenes.
/// </summary>
[DefaultExecutionOrder(1400)]
[DisallowMultipleComponent]
public sealed class ATSGameplayWorldThemeController : MonoBehaviour
{
    private const string ThemeDataResource =
        "UI/Campaign/GameplayWorldThemes";

    [Serializable]
    private sealed class ThemeData
    {
        public ThemeRange[] themes =
            Array.Empty<ThemeRange>();
    }

    [Serializable]
    private sealed class ThemeRange
    {
        public int minLevel = 1;
        public int maxLevel = 10;
        public string backgroundResource = string.Empty;
        public string fallbackResource = string.Empty;
    }

    private ThemeData themeData;
    private SpriteRenderer backgroundRenderer;
    private Sprite originalBackground;
    private TMP_Text levelText;
    private int appliedLevel = -1;
    private float nextRefreshTime;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        ATSGameplayWorldThemeController existing =
            FindFirstObjectByType<
                ATSGameplayWorldThemeController>();

        if (existing != null)
            return;

        GameObject host =
            new GameObject(
                "ATSGameplayWorldThemeController");

        host.AddComponent<
            ATSGameplayWorldThemeController>();
    }

    private void Awake()
    {
        LoadThemeData();
        ResolveBackground();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.unscaledTime + 0.12f;

        ResolveBackground();
        ResolveLevelText();

        int level = ResolveCurrentLevelNumber();

        if (level <= 0 ||
            level == appliedLevel ||
            backgroundRenderer == null)
        {
            return;
        }

        ApplyBackgroundForLevel(level);
    }

    private void LoadThemeData()
    {
        TextAsset json =
            Resources.Load<TextAsset>(
                ThemeDataResource);

        if (json == null ||
            string.IsNullOrWhiteSpace(json.text))
        {
            themeData = new ThemeData();
            return;
        }

        try
        {
            ThemeData parsed =
                JsonUtility.FromJson<ThemeData>(
                    json.text);

            themeData =
                parsed ??
                new ThemeData();
        }
        catch (Exception ex)
        {
            themeData = new ThemeData();

            Debug.LogWarning(
                "[Gameplay Theme] Could not parse " +
                ThemeDataResource +
                ": " +
                ex.Message);
        }
    }

    private void ResolveBackground()
    {
        if (backgroundRenderer != null)
            return;

        BackgroundScaler scaler =
            FindFirstObjectByType<BackgroundScaler>();

        if (scaler == null)
            return;

        backgroundRenderer =
            scaler.GetComponent<SpriteRenderer>();

        if (backgroundRenderer != null &&
            originalBackground == null)
        {
            // The scene-authored Ancient Ruins sprite remains the source of
            // truth for Chapter 1 and as the ultimate fallback.
            originalBackground =
                backgroundRenderer.sprite;
        }
    }

    private void ResolveLevelText()
    {
        if (levelText != null)
            return;

        TMP_Text[] texts =
            FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text candidate = texts[i];

            if (candidate != null &&
                candidate.name == "LevelText")
            {
                levelText = candidate;
                return;
            }
        }
    }

    private int ResolveCurrentLevelNumber()
    {
        // The generated gameplay HUD is the most authoritative runtime signal:
        // PrepareForLevel writes LEVEL <n> after LevelManager resolves the real
        // current LevelData. This also handles Retry, RestartGame and NEXT LEVEL.
        if (levelText != null &&
            TryParseLevelNumber(
                levelText.text,
                out int hudLevel))
        {
            return hudLevel;
        }

        // Fallback covers the brief frame before generated UI exists.
        return Mathf.Max(
            1,
            ATSPlayerProgress.LastPlayedLevel);
    }

    private void ApplyBackgroundForLevel(
        int level)
    {
        Sprite nextSprite =
            ResolveThemeSprite(level);

        if (nextSprite == null)
        {
            nextSprite =
                originalBackground;
        }

        if (nextSprite == null)
            return;

        backgroundRenderer.sprite =
            nextSprite;

        backgroundRenderer.color =
            Color.white;

        appliedLevel = level;
    }

    private Sprite ResolveThemeSprite(
        int level)
    {
        ThemeRange range =
            FindThemeRange(level);

        if (range == null ||
            string.IsNullOrWhiteSpace(
                range.backgroundResource))
        {
            return originalBackground;
        }

        Sprite sprite =
            Resources.Load<Sprite>(
                range.backgroundResource);

        if (sprite != null)
            return sprite;

        if (!string.IsNullOrWhiteSpace(
                range.fallbackResource))
        {
            sprite =
                Resources.Load<Sprite>(
                    range.fallbackResource);

            if (sprite != null)
                return sprite;
        }

        return originalBackground;
    }

    private ThemeRange FindThemeRange(
        int level)
    {
        if (themeData == null ||
            themeData.themes == null)
        {
            return null;
        }

        for (int i = 0;
             i < themeData.themes.Length;
             i++)
        {
            ThemeRange range =
                themeData.themes[i];

            if (range == null)
                continue;

            if (level >= range.minLevel &&
                level <= range.maxLevel)
            {
                return range;
            }
        }

        return null;
    }

    private static bool TryParseLevelNumber(
        string value,
        out int level)
    {
        level = -1;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string[] parts =
            value.Trim()
                .Split(' ');

        for (int i =
                 parts.Length - 1;
             i >= 0;
             i--)
        {
            if (int.TryParse(
                    parts[i],
                    out level))
            {
                return level > 0;
            }
        }

        level = -1;
        return false;
    }
}
