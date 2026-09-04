#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies stable mobile build defaults for Archery Trick Shot.
///
/// Android intentionally uses OpenGLES3 only. The game does not require Vulkan,
/// and GLES3 is the more conservative choice for the menu's off-screen character
/// preview across a wide range of Android GPU/driver combinations.
/// </summary>
public sealed class MobileBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS)
            return;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        if (report.summary.platform == BuildTarget.Android)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3 });

            Debug.Log("Archery Trick Shot: Android build configured for OpenGLES3 and landscape-left/right only.");
        }
        else
        {
            Debug.Log("Archery Trick Shot: iOS build configured for landscape-left/right only.");
        }
    }
}
#endif
