using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-time cinematic Nerissa reveal shown after the player's first Level 6 clear.
///
/// Design goals:
/// - uses the actual Nerissa runtime profile/model already shipped with the game;
/// - no scene/prefab setup is required;
/// - runs in unscaled time, so it is safe after gameplay slow-motion/pause states;
/// - stores only a small "seen" flag in PlayerPrefs;
/// - returns to the campaign map instead of auto-starting Level 7.
///
/// To replay the teaser while testing:
///     NerissaTeaserController.ResetForTesting();
/// or delete PlayerPrefs key:
///     ATS_NERISSA_TEASER_L6_V1
/// </summary>
public sealed class NerissaTeaserController : MonoBehaviour
{
    private const int TriggerLevel = 6;
    private const string SeenKey =
        "ATS_NERISSA_TEASER_L6_V1";

    private const string NerissaProfilePath =
        "Archer3D/Characters/nerissaArcher3D";

    private const string Quote =
        "I don't follow the path. I bend it.";

    private LevelManager levelManager;
    private GameConfig config;

    private CanvasGroup rootGroup;
    private CanvasGroup copyGroup;
    private CanvasGroup heroGroup;
    private CanvasGroup continueGroup;

    private RectTransform copyRect;
    private RectTransform heroRect;

    private TMP_Text quoteText;
    private Button continueButton;
    private RawImage previewImage;
    private ATSCharacterPreview preview;

    private Coroutine revealRoutine;
    private bool closing;

    public static bool ShouldShowAfterLevel(
        int levelNumber)
    {
        return levelNumber == TriggerLevel &&
               PlayerPrefs.GetInt(
                   SeenKey,
                   0) == 0;
    }

    public static void Show(
        LevelManager manager,
        GameConfig gameConfig)
    {
        if (manager == null)
            return;

        NerissaTeaserController existing =
            FindFirstObjectByType<NerissaTeaserController>();

        if (existing == null)
        {
            GameObject go =
                new GameObject(
                    "NerissaLevel6Teaser");

            existing =
                go.AddComponent<NerissaTeaserController>();
        }

        existing.Initialize(
            manager,
            gameConfig);
    }

    public static void ResetForTesting()
    {
        PlayerPrefs.DeleteKey(
            SeenKey);

        PlayerPrefs.Save();
    }

    private void Initialize(
        LevelManager manager,
        GameConfig gameConfig)
    {
        levelManager =
            manager;

        config =
            gameConfig != null
                ? gameConfig
                : GameConfig.Load();

        if (rootGroup == null)
            Build();

        if (revealRoutine != null)
            StopCoroutine(
                revealRoutine);

        closing =
            false;

        gameObject.SetActive(
            true);

        revealRoutine =
            StartCoroutine(
                RevealSequence());
    }

    private void Build()
    {
        GameObject canvasObject =
            new GameObject(
                "NerissaTeaserCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

        canvasObject.transform.SetParent(
            transform,
            false);

        Canvas canvas =
            canvasObject.GetComponent<Canvas>();

        canvas.renderMode =
            RenderMode.ScreenSpaceOverlay;

        // Above normal frontend (200) and gameplay UI.
        canvas.sortingOrder =
            320;

        CanvasScaler scaler =
            canvasObject.GetComponent<CanvasScaler>();

        scaler.uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution =
            new Vector2(
                1920f,
                1080f);

        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        scaler.matchWidthOrHeight =
            0.5f;

        scaler.referencePixelsPerUnit =
            100f;

        RectTransform canvasRect =
            canvasObject.transform as RectTransform;

        Image backdrop =
            CreateImage(
                "CinematicBackdrop",
                canvasRect,
                new Color(
                    0.012f,
                    0.008f,
                    0.030f,
                    0.91f),
                Vector2.zero,
                Vector2.one);

        rootGroup =
            backdrop.gameObject.AddComponent<CanvasGroup>();

        RectTransform safe =
            CreateRect(
                "SafeArea",
                backdrop.transform,
                Vector2.zero,
                Vector2.one);

        safe.gameObject.AddComponent<SafeAreaFitter>();

        BuildAtmosphere(
            safe);

        BuildHero(
            safe);

        BuildCopy(
            safe);

        BuildContinue(
            safe);

        rootGroup.alpha =
            0f;

        rootGroup.interactable =
            false;

        rootGroup.blocksRaycasts =
            true;

        copyGroup.alpha =
            0f;

        heroGroup.alpha =
            0f;

        continueGroup.alpha =
            0f;

        continueButton.interactable =
            false;
    }

    private void BuildAtmosphere(
        RectTransform root)
    {
        // Gold framing line.
        Image topLine =
            CreateImage(
                "TopGoldLine",
                root,
                new Color(
                    0.94f,
                    0.69f,
                    0.20f,
                    0.72f),
                new Vector2(
                    0.035f,
                    0.91f),
                new Vector2(
                    0.965f,
                    0.91f));

        topLine.rectTransform.sizeDelta =
            new Vector2(
                0f,
                2f);

        Image bottomLine =
            CreateImage(
                "BottomGoldLine",
                root,
                new Color(
                    0.94f,
                    0.69f,
                    0.20f,
                    0.38f),
                new Vector2(
                    0.035f,
                    0.09f),
                new Vector2(
                    0.965f,
                    0.09f));

        bottomLine.rectTransform.sizeDelta =
            new Vector2(
                0f,
                2f);

        // A restrained "bending path" motif behind Nerissa.
        // This is UI decoration only; no gameplay trajectory logic is involved.
        CreateArcSegment(
            root,
            new Vector2(
                1050f,
                315f),
            new Vector2(
                210f,
                5f),
            16f,
            0.28f);

        CreateArcSegment(
            root,
            new Vector2(
                1220f,
                375f),
            new Vector2(
                220f,
                5f),
            26f,
            0.34f);

        CreateArcSegment(
            root,
            new Vector2(
                1380f,
                465f),
            new Vector2(
                225f,
                5f),
            36f,
            0.40f);

        CreateArcSegment(
            root,
            new Vector2(
                1510f,
                585f),
            new Vector2(
                205f,
                5f),
            49f,
            0.34f);
    }

    private void BuildHero(
        RectTransform root)
    {
        RectTransform heroStage =
            CreateRect(
                "HeroStage",
                root,
                new Vector2(
                    0.50f,
                    0.10f),
                new Vector2(
                    0.96f,
                    0.90f));

        heroGroup =
            heroStage.gameObject.AddComponent<CanvasGroup>();

        heroRect =
            heroStage;

        Image glow =
            CreateImage(
                "HeroGlow",
                heroStage,
                new Color(
                    0.94f,
                    0.56f,
                    0.16f,
                    0.11f),
                new Vector2(
                    0.18f,
                    0.12f),
                new Vector2(
                    0.92f,
                    0.84f));

        glow.raycastTarget =
            false;

        previewImage =
            CreateRawImage(
                "NerissaPreview",
                heroStage,
                new Vector2(
                    0.00f,
                    0.00f),
                new Vector2(
                    1.00f,
                    1.00f));

        previewImage.color =
            Color.white;

        // Pedestal/identity plate.
        RectTransform plate =
            CreatePanel(
                "HeroIdentityPlate",
                heroStage,
                new Color(
                    0.020f,
                    0.013f,
                    0.050f,
                    0.88f),
                new Color(
                    0.92f,
                    0.66f,
                    0.18f,
                    0.55f),
                new Vector2(
                    0.41f,
                    0.03f),
                new Vector2(
                    0.92f,
                    0.15f));

        TMP_Text name =
            CreateText(
                "HeroName",
                plate,
                "NERISSA",
                31f,
                config != null
                    ? config.PrimaryTextColor
                    : Color.white,
                FontStyles.Bold);

        name.characterSpacing =
            3.2f;

        Stretch(
            name.rectTransform,
            16f,
            8f,
            16f,
            8f);

        preview =
            gameObject.AddComponent<ATSCharacterPreview>();

        preview.Initialize(
            previewImage);

        Archer3DRuntimeProfile profile =
            Resources.Load<Archer3DRuntimeProfile>(
                NerissaProfilePath);

        if (profile != null &&
            profile.ArcherPrefab != null)
        {
            preview.Show(
                profile);
        }
        else
        {
            Debug.LogWarning(
                "Nerissa teaser: runtime profile could not be loaded at " +
                $"Resources/{NerissaProfilePath}.");

            preview.SetVisible(
                false);
        }
    }

    private void BuildCopy(
        RectTransform root)
    {
        RectTransform copy =
            CreateRect(
                "TeaserCopy",
                root,
                new Vector2(
                    0.055f,
                    0.14f),
                new Vector2(
                    0.56f,
                    0.88f));

        copyRect =
            copy;

        copyGroup =
            copy.gameObject.AddComponent<CanvasGroup>();

        TMP_Text eyebrow =
            CreateText(
                "Eyebrow",
                copy,
                "A NEW ARCHER APPROACHES",
                20f,
                config != null
                    ? config.YellowColor
                    : new Color(
                        1f,
                        0.74f,
                        0.22f,
                        1f),
                FontStyles.Bold);

        eyebrow.alignment =
            TextAlignmentOptions.Left;

        eyebrow.characterSpacing =
            2.8f;

        Place(
            eyebrow.rectTransform,
            new Vector2(
                0f,
                0.84f),
            new Vector2(
                0.90f,
                0.94f));

        TMP_Text title =
            CreateText(
                "Title",
                copy,
                "NERISSA",
                84f,
                config != null
                    ? config.PrimaryTextColor
                    : Color.white,
                FontStyles.Bold);

        title.alignment =
            TextAlignmentOptions.Left;

        title.characterSpacing =
            5f;

        Place(
            title.rectTransform,
            new Vector2(
                0f,
                0.66f),
            new Vector2(
                0.95f,
                0.84f));

        TMP_Text teaser =
            CreateText(
                "LevelTeaser",
                copy,
                "LEVEL 6 TEASER",
                22f,
                config != null
                    ? config.SecondaryTextColor
                    : new Color(
                        0.78f,
                        0.78f,
                        0.84f,
                        1f),
                FontStyles.Bold);

        teaser.alignment =
            TextAlignmentOptions.Left;

        teaser.characterSpacing =
            2.2f;

        Place(
            teaser.rectTransform,
            new Vector2(
                0f,
                0.59f),
            new Vector2(
                0.90f,
                0.67f));

        Image divider =
            CreateImage(
                "Divider",
                copy,
                new Color(
                    0.94f,
                    0.68f,
                    0.18f,
                    0.70f),
                new Vector2(
                    0f,
                    0.555f),
                new Vector2(
                    0.52f,
                    0.555f));

        divider.rectTransform.sizeDelta =
            new Vector2(
                0f,
                3f);

        RectTransform quotePanel =
            CreatePanel(
                "QuotePanel",
                copy,
                new Color(
                    0.025f,
                    0.016f,
                    0.060f,
                    0.78f),
                new Color(
                    0.94f,
                    0.68f,
                    0.18f,
                    0.28f),
                new Vector2(
                    0f,
                    0.26f),
                new Vector2(
                    0.91f,
                    0.52f));

        TMP_Text speaker =
            CreateText(
                "Speaker",
                quotePanel,
                "NERISSA",
                17f,
                config != null
                    ? config.YellowColor
                    : new Color(
                        1f,
                        0.75f,
                        0.20f,
                        1f),
                FontStyles.Bold);

        speaker.alignment =
            TextAlignmentOptions.Left;

        speaker.characterSpacing =
            2f;

        Place(
            speaker.rectTransform,
            new Vector2(
                0.07f,
                0.71f),
            new Vector2(
                0.90f,
                0.91f));

        TMP_Text quoteMark =
            CreateText(
                "QuoteMark",
                quotePanel,
                "“",
                74f,
                new Color(
                    0.94f,
                    0.68f,
                    0.18f,
                    0.92f),
                FontStyles.Bold);

        quoteMark.alignment =
            TextAlignmentOptions.TopLeft;

        Place(
            quoteMark.rectTransform,
            new Vector2(
                0.03f,
                0.18f),
            new Vector2(
                0.16f,
                0.69f));

        quoteText =
            CreateText(
                "Quote",
                quotePanel,
                string.Empty,
                32f,
                config != null
                    ? config.PrimaryTextColor
                    : Color.white,
                FontStyles.Italic);

        quoteText.alignment =
            TextAlignmentOptions.Left;

        quoteText.textWrappingMode =
            TextWrappingModes.Normal;

        Place(
            quoteText.rectTransform,
            new Vector2(
                0.14f,
                0.17f),
            new Vector2(
                0.94f,
                0.69f));

        TMP_Text coming =
            CreateText(
                "Coming",
                copy,
                "COMING AT LEVEL 11",
                28f,
                config != null
                    ? config.LimeColor
                    : new Color(
                        0.70f,
                        1f,
                        0.18f,
                        1f),
                FontStyles.Bold);

        coming.alignment =
            TextAlignmentOptions.Left;

        coming.characterSpacing =
            2.4f;

        Place(
            coming.rectTransform,
            new Vector2(
                0f,
                0.12f),
            new Vector2(
                0.90f,
                0.22f));

        TMP_Text hint =
            CreateText(
                "Hint",
                copy,
                "SOME PATHS ARE MEANT TO BE CHANGED.",
                15f,
                config != null
                    ? config.SecondaryTextColor
                    : new Color(
                        0.72f,
                        0.72f,
                        0.78f,
                        1f),
                FontStyles.Bold);

        hint.alignment =
            TextAlignmentOptions.Left;

        hint.characterSpacing =
            1.2f;

        Place(
            hint.rectTransform,
            new Vector2(
                0f,
                0.04f),
            new Vector2(
                0.90f,
                0.11f));
    }

    private void BuildContinue(
        RectTransform root)
    {
        RectTransform container =
            CreateRect(
                "ContinueContainer",
                root,
                new Vector2(
                    0.055f,
                    0.055f),
                new Vector2(
                    0.32f,
                    0.135f));

        continueGroup =
            container.gameObject.AddComponent<CanvasGroup>();

        continueButton =
            CreateButton(
                "Continue",
                container,
                "CONTINUE",
                Vector2.zero,
                Vector2.one,
                config != null
                    ? config.LimeColor
                    : new Color(
                        0.48f,
                        0.92f,
                        0.16f,
                        1f),
                config != null
                    ? config.LimeColor
                    : new Color(
                        0.48f,
                        0.92f,
                        0.16f,
                        1f),
                Color.white,
                25f);

        continueButton.onClick.AddListener(
            ContinueToCampaign);
    }

    private IEnumerator RevealSequence()
    {
        rootGroup.alpha =
            0f;

        copyGroup.alpha =
            0f;

        heroGroup.alpha =
            0f;

        continueGroup.alpha =
            0f;

        continueButton.interactable =
            false;

        quoteText.text =
            string.Empty;

        Vector2 copyTarget =
            copyRect.anchoredPosition;

        Vector2 heroTarget =
            heroRect.anchoredPosition;

        copyRect.anchoredPosition =
            copyTarget +
            new Vector2(
                -55f,
                0f);

        heroRect.anchoredPosition =
            heroTarget +
            new Vector2(
                60f,
                0f);

        float elapsed =
            0f;

        const float fadeDuration =
            0.46f;

        while (elapsed < fadeDuration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    fadeDuration);

            rootGroup.alpha =
                EaseOutCubic(
                    t);

            yield return null;
        }

        rootGroup.alpha =
            1f;

        elapsed =
            0f;

        const float entranceDuration =
            0.72f;

        while (elapsed < entranceDuration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                EaseOutCubic(
                    Mathf.Clamp01(
                        elapsed /
                        entranceDuration));

            copyGroup.alpha =
                t;

            heroGroup.alpha =
                t;

            copyRect.anchoredPosition =
                Vector2.LerpUnclamped(
                    copyTarget +
                    new Vector2(
                        -55f,
                        0f),
                    copyTarget,
                    t);

            heroRect.anchoredPosition =
                Vector2.LerpUnclamped(
                    heroTarget +
                    new Vector2(
                        60f,
                        0f),
                    heroTarget,
                    t);

            yield return null;
        }

        copyRect.anchoredPosition =
            copyTarget;

        heroRect.anchoredPosition =
            heroTarget;

        copyGroup.alpha =
            1f;

        heroGroup.alpha =
            1f;

        ATSHaptics.Pulse();

        yield return
            new WaitForSecondsRealtime(
                0.22f);

        // Typewriter makes the quote read like Nerissa actually delivering it.
        const float charactersPerSecond =
            33f;

        for (int i = 1;
             i <= Quote.Length;
             i++)
        {
            quoteText.text =
                Quote.Substring(
                    0,
                    i);

            yield return
                new WaitForSecondsRealtime(
                    1f /
                    charactersPerSecond);
        }

        yield return
            new WaitForSecondsRealtime(
                0.20f);

        elapsed =
            0f;

        const float buttonFade =
            0.28f;

        while (elapsed < buttonFade)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            continueGroup.alpha =
                EaseOutCubic(
                    Mathf.Clamp01(
                        elapsed /
                        buttonFade));

            yield return null;
        }

        continueGroup.alpha =
            1f;

        continueButton.interactable =
            true;

        rootGroup.interactable =
            true;

        revealRoutine =
            null;
    }

    private void ContinueToCampaign()
    {
        if (closing)
            return;

        closing =
            true;

        Debug.Log(
            "Nerissa teaser: Continue clicked; returning to campaign map.");

        continueButton.interactable =
            false;

        GameAudioController.Instance?.PlayUIClick();

        PlayerPrefs.SetInt(
            SeenKey,
            1);

        PlayerPrefs.Save();

        StartCoroutine(
            CloseSequence());
    }

    private IEnumerator CloseSequence()
    {
        float startAlpha =
            rootGroup != null
                ? rootGroup.alpha
                : 1f;

        float elapsed =
            0f;

        const float duration =
            0.34f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            if (rootGroup != null)
            {
                rootGroup.alpha =
                    Mathf.Lerp(
                        startAlpha,
                        0f,
                        t);
            }

            yield return null;
        }

        preview?.SetVisible(
            false);

        LevelManager manager =
            levelManager;

        Destroy(
            gameObject);

        if (manager != null)
            manager.OpenLevelSelect();
    }

    private static float EaseOutCubic(
        float t)
    {
        float x =
            1f -
            Mathf.Clamp01(
                t);

        return 1f -
               x *
               x *
               x;
    }

    private static void CreateArcSegment(
        Transform parent,
        Vector2 anchoredPosition,
        Vector2 size,
        float rotation,
        float alpha)
    {
        GameObject go =
            new GameObject(
                "BentPathSegment",
                typeof(RectTransform),
                typeof(Image));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            new Vector2(
                0f,
                0f);

        rect.anchorMax =
            new Vector2(
                0f,
                0f);

        rect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        rect.anchoredPosition =
            anchoredPosition;

        rect.sizeDelta =
            size;

        rect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                rotation);

        Image image =
            go.GetComponent<Image>();

        image.color =
            new Color(
                1f,
                0.72f,
                0.20f,
                alpha);

        image.raycastTarget =
            false;
    }

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            anchorMin;

        rect.anchorMax =
            anchorMax;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        return rect;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            anchorMin;

        rect.anchorMax =
            anchorMax;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        Image image =
            go.GetComponent<Image>();

        image.color =
            color;

        image.raycastTarget =
            false;

        return image;
    }

    private static RawImage CreateRawImage(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(RawImage));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            anchorMin;

        rect.anchorMax =
            anchorMax;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        RawImage image =
            go.GetComponent<RawImage>();

        image.raycastTarget =
            false;

        return image;
    }

    private static RectTransform CreatePanel(
        string name,
        Transform parent,
        Color fill,
        Color border,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        Image image =
            CreateImage(
                name,
                parent,
                fill,
                anchorMin,
                anchorMax);

        Outline outline =
            image.gameObject.AddComponent<Outline>();

        outline.effectColor =
            border;

        outline.effectDistance =
            new Vector2(
                1.5f,
                -1.5f);

        outline.useGraphicAlpha =
            true;

        return image.rectTransform;
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color fill,
        Color border,
        Color textColor,
        float fontSize)
    {
        RectTransform rect =
            CreatePanel(
                name,
                parent,
                fill,
                border,
                anchorMin,
                anchorMax);

        // A Unity Button needs at least one raycastable Graphic under the
        // pointer. Decorative images in this teaser are intentionally
        // non-raycastable, so explicitly make the button panel interactive.
        Image buttonGraphic =
            rect.GetComponent<Image>();

        if (buttonGraphic != null)
            buttonGraphic.raycastTarget =
                true;

        Button button =
            rect.gameObject.AddComponent<Button>();

        button.targetGraphic =
            buttonGraphic;

        button.transition =
            Selectable.Transition.ColorTint;

        ColorBlock colors =
            button.colors;

        colors.normalColor =
            Color.white;

        colors.highlightedColor =
            new Color(
                1f,
                1f,
                1f,
                0.94f);

        colors.pressedColor =
            new Color(
                0.82f,
                0.82f,
                0.82f,
                1f);

        colors.disabledColor =
            new Color(
                0.46f,
                0.46f,
                0.46f,
                0.72f);

        colors.fadeDuration =
            0.08f;

        button.colors =
            colors;

        button.gameObject.AddComponent<ATSButtonMotion>();

        TMP_Text text =
            CreateText(
                "Text",
                rect,
                label,
                fontSize,
                textColor,
                FontStyles.Bold);

        Stretch(
            text.rectTransform,
            16f,
            8f,
            16f,
            8f);

        return button;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        Color color,
        FontStyles style)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(TextMeshProUGUI));

        go.transform.SetParent(
            parent,
            false);

        TMP_Text text =
            go.GetComponent<TextMeshProUGUI>();

        text.text =
            value;

        text.fontSize =
            fontSize;

        text.fontStyle =
            style;

        text.color =
            color;

        text.alignment =
            TextAlignmentOptions.Center;

        text.raycastTarget =
            false;

        text.textWrappingMode =
            TextWrappingModes.NoWrap;

        return text;
    }

    private static void Place(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        rect.anchorMin =
            anchorMin;

        rect.anchorMax =
            anchorMax;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        rect.localScale =
            Vector3.one;
    }

    private static void Stretch(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin =
            Vector2.zero;

        rect.anchorMax =
            Vector2.one;

        rect.offsetMin =
            new Vector2(
                left,
                bottom);

        rect.offsetMax =
            new Vector2(
                -right,
                -top);
    }

    private void OnDestroy()
    {
        if (revealRoutine != null)
        {
            StopCoroutine(
                revealRoutine);

            revealRoutine =
                null;
        }
    }
}
