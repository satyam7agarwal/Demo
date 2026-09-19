using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GameUIView
{
    public CanvasGroup HudGroup;
    public TMP_Text LevelText;
    public TMP_Text ShotsText;
    public TMP_Text AimHintText;
    public CanvasGroup AimHintGroup;
    public Button PauseButton;
    public Button FullTraceButton;
    public TMP_Text FullTraceButtonText;

    public TMP_Text FeedbackText;
    public Image RicochetBackdrop;
    public CanvasGroup RicochetBackdropGroup;
    public TMP_Text RicochetText;
    public Image[] RicochetComboSegments;
    // Legacy v1 fields kept for safe compatibility; v2 no longer builds them.
    public Image RicochetComboTrack;
    public Image RicochetComboFill;
    public Image ScreenFlash;

    public CanvasGroup ResultOverlay;
    public RectTransform ResultCard;
    public TMP_Text ResultTitle;
    public TMP_Text ResultInfo;
    public RectTransform ResultStarsContainer;
    public StarGraphic[] ResultStars;
    public TMP_Text ResultStarsMessage;
    public Button ResultPrimaryButton;
    public TMP_Text ResultPrimaryButtonText;
    public Button ResultReplayButton;
    public Button ResultLevelsButton;
    public Button ResultHomeButton;

    public CanvasGroup PauseOverlay;
    public RectTransform PauseCard;
    public Button ResumeButton;
    public Button RestartButton;
    public Button PauseLevelsButton;
    public Button PauseHomeButton;
}
