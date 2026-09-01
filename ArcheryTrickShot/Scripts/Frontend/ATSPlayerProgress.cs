using UnityEngine;

public static class ATSPlayerProgress
{
    private const string Prefix = "ArcheryTrickShot.Progress.";
    private const string HighestUnlockedKey = Prefix + "HighestUnlockedLevel";
    private const string LastPlayedKey = Prefix + "LastPlayedLevel";
    private const string MusicKey = Prefix + "MusicEnabled";
    private const string SfxKey = Prefix + "SfxEnabled";
    private const string HapticsKey = Prefix + "HapticsEnabled";
    private const string PerformanceKey = Prefix + "PerformanceMode";

    public static int HighestUnlockedLevel => Mathf.Max(1, PlayerPrefs.GetInt(HighestUnlockedKey, 1));
    public static int LastPlayedLevel => Mathf.Max(1, PlayerPrefs.GetInt(LastPlayedKey, 1));

    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicKey, 1) != 0;
        set { PlayerPrefs.SetInt(MusicKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool SfxEnabled
    {
        get => PlayerPrefs.GetInt(SfxKey, 1) != 0;
        set { PlayerPrefs.SetInt(SfxKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool HapticsEnabled
    {
        get => PlayerPrefs.GetInt(HapticsKey, 1) != 0;
        set { PlayerPrefs.SetInt(HapticsKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // 0 = smooth/60fps, 1 = battery/30fps.
    public static int PerformanceMode
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PerformanceKey, 0), 0, 1);
        set { PlayerPrefs.SetInt(PerformanceKey, Mathf.Clamp(value, 0, 1)); PlayerPrefs.Save(); ApplyPerformanceMode(); }
    }

    public static bool IsLevelUnlocked(int levelNumber) => levelNumber <= HighestUnlockedLevel;

    public static int GetBestStars(int levelNumber) => Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Stars." + levelNumber, 0), 0, 3);
    public static int GetBestScore(int levelNumber) => Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "Score." + levelNumber, 0));

    public static void RecordLevelStarted(int levelNumber)
    {
        PlayerPrefs.SetInt(LastPlayedKey, Mathf.Max(1, levelNumber));
        PlayerPrefs.Save();
    }

    public static void RecordCompletion(int levelNumber, int stars, int score, int totalLevels)
    {
        stars = Mathf.Clamp(stars, 1, 3);
        if (stars > GetBestStars(levelNumber))
            PlayerPrefs.SetInt(Prefix + "Stars." + levelNumber, stars);

        if (score > GetBestScore(levelNumber))
            PlayerPrefs.SetInt(Prefix + "Score." + levelNumber, Mathf.Max(0, score));

        int nextLevel = Mathf.Clamp(levelNumber + 1, 1, Mathf.Max(1, totalLevels));
        if (levelNumber < totalLevels && nextLevel > HighestUnlockedLevel)
            PlayerPrefs.SetInt(HighestUnlockedKey, nextLevel);

        PlayerPrefs.SetInt(LastPlayedKey, levelNumber < totalLevels ? nextLevel : levelNumber);
        PlayerPrefs.Save();
    }

    public static void ApplyPerformanceMode()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = PerformanceMode == 0 ? 60 : 30;
    }
}
