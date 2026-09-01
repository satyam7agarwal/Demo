using UnityEngine;

public static class MobilePlatformBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigurePlatform()
    {
        Input.multiTouchEnabled = true;

#if UNITY_ANDROID || UNITY_IOS
        GameConfig config = GameConfig.Load();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = config.MobileTargetFrameRate;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.backgroundLoadingPriority = ThreadPriority.Low;

        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
#endif
    }
}
