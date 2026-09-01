using UnityEngine;

public static class ATSHaptics
{
    private static float lastPulseTime = -10f;

    public static void Pulse()
    {
        if (!ATSPlayerProgress.HapticsEnabled || !Application.isMobilePlatform)
            return;

        if (Time.unscaledTime - lastPulseTime < 0.08f)
            return;

        lastPulseTime = Time.unscaledTime;
        Handheld.Vibrate();
    }
}
