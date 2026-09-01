using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public sealed class GameUIController : MonoBehaviour
{
    private GameConfig config;
    private LevelManager levelManager;
    private GameUIView view;

    private Coroutine resultRoutine;
    private Coroutine pauseRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine ricochetRoutine;
    private Coroutine shotsPunchRoutine;
    private Coroutine hudRoutine;

    private void Awake()
    {
        config = GameConfig.Load();
        EnsureView();
        HideAllOverlaysImmediate();
    }

    public void Initialize(LevelManager manager, GameConfig gameConfig)
    {
        levelManager = manager;
        config = gameConfig != null ? gameConfig : GameConfig.Load();
        EnsureView();
        BindButtons();
        HideAllOverlaysImmediate();
    }

    private void EnsureView()
    {
        if (view != null)
            return;

        Canvas canvas = GetComponent<Canvas>();
        view = GameUIBuilder.Build(canvas, config != null ? config : GameConfig.Load());
    }

    private void BindButtons()
    {
        view.PauseButton.onClick.RemoveAllListeners();
        view.PauseButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.PauseGame(); });

        view.FullTraceButton.onClick.RemoveAllListeners();
        view.FullTraceButton.onClick.AddListener(() =>
        {
            PlayUIClick();
            levelManager?.ToggleFullTrajectoryPreview();
        });

        view.ResumeButton.onClick.RemoveAllListeners();
        view.ResumeButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.ResumeGame(); });

        view.RestartButton.onClick.RemoveAllListeners();
        view.RestartButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.RetryLevel(); });

        view.ResultPrimaryButton.onClick.RemoveAllListeners();
        view.ResultPrimaryButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OnResultPrimaryClicked(); });

        view.ResultReplayButton.onClick.RemoveAllListeners();
        view.ResultReplayButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.RetryLevel(); });

        if (view.ResultLevelsButton != null)
        {
            view.ResultLevelsButton.onClick.RemoveAllListeners();
            view.ResultLevelsButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OpenLevelSelect(); });
        }

        if (view.ResultHomeButton != null)
        {
            view.ResultHomeButton.onClick.RemoveAllListeners();
            view.ResultHomeButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OpenMainMenu(); });
        }

        if (view.PauseLevelsButton != null)
        {
            view.PauseLevelsButton.onClick.RemoveAllListeners();
            view.PauseLevelsButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OpenLevelSelect(); });
        }

        if (view.PauseHomeButton != null)
        {
            view.PauseHomeButton.onClick.RemoveAllListeners();
            view.PauseHomeButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OpenMainMenu(); });
        }
    }

    public void PrepareForLevel(int levelNumber, int maxShots)
    {
        BindButtons();
        StopPresentationCoroutines();
        HideAllOverlaysImmediate();

        view.LevelText.text = $"LEVEL {levelNumber}";
        UpdateShots(maxShots, maxShots, false);
        SetAimHintVisible(true);

        view.FeedbackText.alpha = 0f;
        view.FeedbackText.gameObject.SetActive(false);
        if (view.RicochetText != null)
        {
            view.RicochetText.alpha = 0f;
            view.RicochetText.gameObject.SetActive(false);
        }

        view.HudGroup.gameObject.SetActive(true);
        view.HudGroup.alpha = 0f;
        view.HudGroup.interactable = true;
        view.HudGroup.blocksRaycasts = true;
        hudRoutine = StartCoroutine(FadeCanvasGroup(view.HudGroup, 0f, 1f, 0.22f));
    }

    public void UpdateShots(int shotsRemaining, int maxShots, bool animate)
    {
        view.ShotsText.text = $"ARROWS  {shotsRemaining}";

        if (!animate)
        {
            view.ShotsText.rectTransform.localScale = Vector3.one;
            return;
        }

        if (shotsPunchRoutine != null)
            StopCoroutine(shotsPunchRoutine);
        shotsPunchRoutine = StartCoroutine(PunchScale(view.ShotsText.rectTransform, 1.12f, 0.18f));
    }

    public void SetAimHintVisible(bool visible)
    {
        if (view?.AimHintGroup == null)
            return;

        view.AimHintGroup.gameObject.SetActive(visible);
        view.AimHintGroup.alpha = visible ? 1f : 0f;
        view.AimHintGroup.interactable = false;
        view.AimHintGroup.blocksRaycasts = false;

        if (view.AimHintText != null)
            view.AimHintText.alpha = visible ? 1f : 0f;
    }

    public void SetFullTrajectoryPreviewEnabled(bool enabled)
    {
        if (view == null || view.FullTraceButton == null)
            return;

        Color assistTeal = new Color(0.20f, 0.92f, 0.94f, 1f);

        if (view.FullTraceButtonText != null)
        {
            view.FullTraceButtonText.text =
                enabled
                    ? "FULL PATH  ON"
                    : "FULL PATH  OFF";

            view.FullTraceButtonText.color =
                enabled
                    ? assistTeal
                    : config.SecondaryTextColor;
        }

        Image background =
            view.FullTraceButton.GetComponent<Image>();

        if (background != null)
        {
            background.color =
                enabled
                    ? new Color(
                        config.PanelColor.r * 0.82f,
                        config.PanelColor.g * 0.82f + 0.03f,
                        config.PanelColor.b * 0.82f + 0.05f,
                        0.94f)
                    : config.PanelColor;
        }

        Outline outline =
            view.FullTraceButton.GetComponent<Outline>();

        if (outline != null)
        {
            outline.effectColor =
                enabled
                    ? assistTeal
                    : config.PanelBorderColor;
        }
    }

    public void SetGameplayVisible(bool visible)
    {
        if (view == null)
            return;

        if (view.HudGroup != null)
        {
            view.HudGroup.gameObject.SetActive(visible);
            view.HudGroup.alpha = visible ? 1f : 0f;
            view.HudGroup.interactable = visible;
            view.HudGroup.blocksRaycasts = visible;
        }

        if (!visible)
            HideAllOverlaysImmediate();
    }

    public void PlayHitFeedback()
    {
        PlayHitFeedback(
            "TARGET HIT!",
            0,
            false);
    }

    public void PlayHitFeedback(
        string label,
        int score,
        bool isBullseye)
    {
        string message =
            score > 0
                ? $"{label}\n+{score:N0}"
                : label;

        Color accent =
            isBullseye
                ? config.YellowColor
                : config.LimeColor;

        PlayFeedback(
            message,
            accent,
            new Color(
                accent.r,
                accent.g,
                accent.b,
                isBullseye ? 0.10f : 0.065f));
    }

    public void PlayRicochetFeedback(int ricochetCount)
    {
        if (view?.RicochetText == null || ricochetCount <= 0)
            return;

        if (ricochetRoutine != null)
            StopCoroutine(ricochetRoutine);

        ricochetRoutine = StartCoroutine(
            RicochetSequence(ricochetCount));
    }

    private IEnumerator RicochetSequence(int ricochetCount)
    {
        string label = ricochetCount switch
        {
            1 => "RICOCHET!",
            2 => "DOUBLE RICOCHET!",
            3 => "TRIPLE RICOCHET!",
            _ => $"{ricochetCount}x RICOCHET!"
        };

        TMP_Text text = view.RicochetText;
        text.gameObject.SetActive(true);
        text.text = label;
        text.color = config.YellowColor;
        text.alpha = 0f;
        text.rectTransform.localScale = new Vector3(0.78f, 0.78f, 1f);

        const float enterDuration = 0.10f;
        float elapsed = 0f;
        while (elapsed < enterDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / enterDuration);
            float scale = Mathf.LerpUnclamped(0.78f, 1.06f, EaseOutBack(t));
            text.alpha = t;
            text.rectTransform.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        text.rectTransform.localScale = Vector3.one;
        yield return new WaitForSecondsRealtime(0.20f);

        const float exitDuration = 0.16f;
        elapsed = 0f;
        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            text.alpha = 1f - t;
            text.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, t);
            yield return null;
        }

        text.alpha = 0f;
        text.gameObject.SetActive(false);
        text.rectTransform.localScale = Vector3.one;
        ricochetRoutine = null;
    }

    public void PlayMissFeedback(int shotsRemaining)
    {
        string message = shotsRemaining > 0
            ? $"MISS  •  {shotsRemaining} SHOT{(shotsRemaining == 1 ? string.Empty : "S")} LEFT"
            : "MISS!";
        PlayFeedback(message, config.PinkColor, new Color(config.PinkColor.r, config.PinkColor.g, config.PinkColor.b, 0.11f));
    }

    public void ShowComplete(
        int shotsUsed,
        int maxShots,
        int score,
        string hitLabel,
        bool isBullseye,
        bool isLastLevel,
        int ricochetCount = 0)
    {
        view.ResultPrimaryButton.onClick.RemoveAllListeners();
        view.ResultPrimaryButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.OnResultPrimaryClicked(); });
        view.ResultReplayButton.onClick.RemoveAllListeners();
        view.ResultReplayButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.RetryLevel(); });
        StopOverlayCoroutine(ref resultRoutine);
        StopOverlayCoroutine(ref ricochetRoutine);
        if (view.RicochetText != null)
            view.RicochetText.gameObject.SetActive(false);

        int stars = CalculateStars(shotsUsed, maxShots);

        view.ResultTitle.text =
            GetCelebrationTitle(
                shotsUsed,
                isBullseye,
                ricochetCount,
                hitLabel,
                isLastLevel);

        view.ResultTitle.color =
            isBullseye
                ? config.YellowColor
                : config.LimeColor;

        SetResultStars(stars);
        view.ResultStarsContainer.gameObject.SetActive(true);
        view.ResultStarsMessage.gameObject.SetActive(false);
        string shotWord = shotsUsed == 1 ? "SHOT" : "SHOTS";
        string ricochetInfo = ricochetCount > 0
            ? $"  •  {ricochetCount} RICOCHET{(ricochetCount == 1 ? string.Empty : "S")}" 
            : string.Empty;

        view.ResultInfo.text =
            $"SCORE  {score:N0}\n" +
            $"{shotsUsed} {shotWord} USED{ricochetInfo}";
        view.ResultPrimaryButtonText.text = isLastLevel ? "PLAY AGAIN  >>" : "NEXT LEVEL  >>";
        view.ResultReplayButton.gameObject.SetActive(!isLastLevel);

        resultRoutine = StartCoroutine(
            ShowCompleteOverlay(
                view.ResultOverlay,
                view.ResultCard,
                stars));
    }

    // Kept for compatibility with any existing caller that still uses
    // the old completion signature.
    public void ShowComplete(
        int shotsUsed,
        int maxShots,
        bool isLastLevel)
    {
        ShowComplete(
            shotsUsed,
            maxShots,
            0,
            "TARGET HIT!",
            false,
            isLastLevel);
    }

    private static string GetCelebrationTitle(
        int shotsUsed,
        bool isBullseye,
        int ricochetCount,
        string hitLabel,
        bool isLastLevel)
    {
        if (isLastLevel)
            return "ALL LEVELS COMPLETE!";

        if (isBullseye && shotsUsed <= 1)
            return "PERFECT BULLSEYE!";

        if (ricochetCount >= 2 && shotsUsed <= 1)
            return "TRICK SHOT!";

        if (ricochetCount == 1 && shotsUsed <= 1)
            return "PERFECT RICOCHET!";

        if (shotsUsed <= 1)
            return "ONE SHOT!";

        return string.IsNullOrWhiteSpace(hitLabel)
            ? "TARGET HIT!"
            : hitLabel;
    }

    private IEnumerator ShowCompleteOverlay(
        CanvasGroup overlay,
        RectTransform card,
        int earnedStars)
    {
        if (view.ResultStars != null)
        {
            for (int i = 0; i < view.ResultStars.Length; i++)
            {
                StarGraphic star = view.ResultStars[i];
                if (star != null)
                    star.rectTransform.localScale = i < earnedStars
                        ? Vector3.one * 0.55f
                        : Vector3.one;
            }
        }

        yield return StartCoroutine(
            ShowOverlay(overlay, card));

        if (view.ResultStars != null)
        {
            for (int i = 0; i < earnedStars && i < view.ResultStars.Length; i++)
            {
                StarGraphic star = view.ResultStars[i];
                if (star == null)
                    continue;

                RectTransform rect = star.rectTransform;
                const float duration = 0.11f;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float scale = Mathf.LerpUnclamped(0.55f, 1.08f, EaseOutBack(t));
                    rect.localScale = Vector3.one * scale;
                    yield return null;
                }

                rect.localScale = Vector3.one;
                yield return new WaitForSecondsRealtime(0.035f);
            }
        }

        resultRoutine = null;
    }

    public void ShowFailed()
    {
        StopOverlayCoroutine(ref resultRoutine);
        StopOverlayCoroutine(ref ricochetRoutine);
        if (view.RicochetText != null)
            view.RicochetText.gameObject.SetActive(false);
        view.ResultTitle.text = "MISS!";
        view.ResultTitle.color = config.PinkColor;
        view.ResultStarsContainer.gameObject.SetActive(false);
        view.ResultStarsMessage.gameObject.SetActive(true);
        view.ResultStarsMessage.text = "TRY ANOTHER ANGLE";
        view.ResultStarsMessage.color = config.PrimaryTextColor;
        view.ResultInfo.text = "YOU USED ALL AVAILABLE SHOTS";
        view.ResultPrimaryButtonText.text = "RETRY LEVEL";
        view.ResultReplayButton.gameObject.SetActive(false);

        view.ResultPrimaryButton.onClick.RemoveAllListeners();
        view.ResultPrimaryButton.onClick.AddListener(() => { PlayUIClick(); levelManager?.RetryLevel(); });
        resultRoutine = StartCoroutine(ShowOverlay(view.ResultOverlay, view.ResultCard));
    }

    public void ShowPause()
    {
        StopOverlayCoroutine(ref pauseRoutine);
        pauseRoutine = StartCoroutine(ShowOverlay(view.PauseOverlay, view.PauseCard));
    }

    public void HidePause()
    {
        StopOverlayCoroutine(ref pauseRoutine);
        pauseRoutine = StartCoroutine(HideOverlay(view.PauseOverlay));
    }

    private static void PlayUIClick()
    {
        GameAudioController.Instance?.PlayUIClick();
    }

    private void PlayFeedback(string message, Color textColor, Color flashColor)
    {
        if (feedbackRoutine != null)
            StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(FeedbackSequence(message, textColor, flashColor));
    }

    private IEnumerator FeedbackSequence(string message, Color textColor, Color flashColor)
    {
        view.FeedbackText.gameObject.SetActive(true);
        view.FeedbackText.text = message;
        view.FeedbackText.color = textColor;
        view.FeedbackText.alpha = 0f;
        view.FeedbackText.rectTransform.localScale = new Vector3(0.82f, 0.82f, 1f);

        view.ScreenFlash.gameObject.SetActive(true);
        view.ScreenFlash.raycastTarget = false;
        Color flash = flashColor;
        flash.a = 0f;
        view.ScreenFlash.color = flash;

        float enterDuration = 0.12f;
        float elapsed = 0f;
        while (elapsed < enterDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / enterDuration);
            float eased = EaseOutBack(t);
            view.FeedbackText.alpha = t;
            float scale = Mathf.LerpUnclamped(0.82f, 1f, eased);
            view.FeedbackText.rectTransform.localScale = new Vector3(scale, scale, 1f);

            Color c = flashColor;
            c.a = Mathf.Lerp(0f, flashColor.a, t);
            view.ScreenFlash.color = c;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, config.UIFeedbackDuration - 0.24f));

        float exitDuration = 0.12f;
        elapsed = 0f;
        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            view.FeedbackText.alpha = 1f - t;
            Color c = flashColor;
            c.a = Mathf.Lerp(flashColor.a, 0f, t);
            view.ScreenFlash.color = c;
            yield return null;
        }

        view.FeedbackText.gameObject.SetActive(false);
        view.ScreenFlash.gameObject.SetActive(false);
        feedbackRoutine = null;
    }

    private IEnumerator ShowOverlay(CanvasGroup overlay, RectTransform card)
    {
        overlay.gameObject.SetActive(true);
        overlay.alpha = 0f;
        overlay.interactable = true;
        overlay.blocksRaycasts = true;
        card.localScale = new Vector3(0.82f, 0.82f, 1f);

        float duration = Mathf.Max(config.UIFadeDuration, config.UICardPopDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fadeT = Mathf.Clamp01(elapsed / config.UIFadeDuration);
            float popT = Mathf.Clamp01(elapsed / config.UICardPopDuration);
            overlay.alpha = fadeT;
            float scale = Mathf.LerpUnclamped(0.82f, 1f, EaseOutBack(popT));
            card.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        overlay.alpha = 1f;
        card.localScale = Vector3.one;
    }

    private IEnumerator HideOverlay(CanvasGroup overlay)
    {
        float startAlpha = overlay.alpha;
        float elapsed = 0f;
        while (elapsed < config.UIFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / config.UIFadeDuration);
            overlay.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        overlay.alpha = 0f;
        overlay.interactable = false;
        overlay.blocksRaycasts = false;
        overlay.gameObject.SetActive(false);
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        float elapsed = 0f;
        group.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        group.alpha = to;
    }

    private IEnumerator PunchScale(RectTransform rect, float peak, float duration)
    {
        float half = duration * 0.5f;
        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            float scale = Mathf.Lerp(1f, peak, t);
            rect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            float scale = Mathf.Lerp(peak, 1f, t);
            rect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        rect.localScale = Vector3.one;
    }

    private void HideAllOverlaysImmediate()
    {
        if (view == null)
            return;

        HideCanvasGroupImmediate(view.ResultOverlay);
        HideCanvasGroupImmediate(view.PauseOverlay);
        view.ScreenFlash.gameObject.SetActive(false);
        view.FeedbackText.gameObject.SetActive(false);
        if (view.RicochetText != null)
            view.RicochetText.gameObject.SetActive(false);
    }

    private static void HideCanvasGroupImmediate(CanvasGroup group)
    {
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        group.gameObject.SetActive(false);
    }

    private void StopPresentationCoroutines()
    {
        StopOverlayCoroutine(ref resultRoutine);
        StopOverlayCoroutine(ref pauseRoutine);
        StopOverlayCoroutine(ref feedbackRoutine);
        StopOverlayCoroutine(ref ricochetRoutine);
        StopOverlayCoroutine(ref shotsPunchRoutine);
        StopOverlayCoroutine(ref hudRoutine);
    }

    private void StopOverlayCoroutine(ref Coroutine routine)
    {
        if (routine == null)
            return;
        StopCoroutine(routine);
        routine = null;
    }

    private static int CalculateStars(int shotsUsed, int maxShots)
    {
        if (shotsUsed <= 1)
            return 3;
        if (shotsUsed == 2)
            return 2;
        return 1;
    }

    private void SetResultStars(
        int earnedStars)
    {
        if (view?.ResultStars == null)
            return;

        int count =
            view.ResultStars.Length;

        earnedStars =
            Mathf.Clamp(
                earnedStars,
                0,
                count);

        Color unearnedColor =
            new Color(
                config.SecondaryTextColor.r,
                config.SecondaryTextColor.g,
                config.SecondaryTextColor.b,
                0.28f);

        for (int index = 0;
             index < count;
             index++)
        {
            StarGraphic star =
                view.ResultStars[index];

            if (star == null)
                continue;

            star.color =
                index < earnedStars
                    ? config.YellowColor
                    : unearnedColor;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}
