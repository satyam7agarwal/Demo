using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class GameUIBuilder
{
    private static Sprite roundedSprite;

    public static GameUIView Build(Canvas canvas, GameConfig config)
    {
        GameUIView view = new GameUIView();
        ConfigureCanvas(canvas, config);
        EnsureEventSystem();

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null)
        {
            Debug.LogError("GameUIBuilder: Canvas must use a RectTransform.");
            return view;
        }
        RectTransform root = CreateRect("GeneratedGameplayUI", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        view.ScreenFlash = CreateImage("ScreenFlash", root, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        view.ScreenFlash.raycastTarget = false;

        RectTransform safeArea = CreateRect("SafeArea", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safeArea.gameObject.AddComponent<SafeAreaFitter>();

        BuildHud(safeArea, config, view);
        BuildFeedback(root, config, view);
        BuildResultOverlay(root, config, view);
        BuildPauseOverlay(root, config, view);

        return view;
    }


    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule)
        );
    }

    private static void ConfigureCanvas(Canvas canvas, GameConfig config)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = config.UIReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = config.UIMatchWidthOrHeight;
        scaler.referencePixelsPerUnit = 100f;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();
    }

    private static void BuildHud(RectTransform safeArea, GameConfig config, GameUIView view)
    {
        GameObject hudObject = new GameObject("HUD", typeof(RectTransform), typeof(CanvasGroup));
        hudObject.transform.SetParent(safeArea, false);
        RectTransform hudRect = hudObject.GetComponent<RectTransform>();
        Stretch(hudRect);
        view.HudGroup = hudObject.GetComponent<CanvasGroup>();

        RectTransform levelPill = CreatePanel(
            "LevelPill",
            hudRect,
            config.PanelColor,
            config.PanelBorderColor,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -26f),
            new Vector2(286f, 72f)
        );
        view.LevelText = CreateText("LevelText", levelPill, "LEVEL 1", 30f, config.PrimaryTextColor, FontStyles.Bold);
        Stretch(view.LevelText.rectTransform, 18f, 12f, 18f, 12f);

        view.PauseButton = CreateButton(
            "PauseButton",
            hudRect,
            "II",
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-28f, -26f),
            new Vector2(96f, 84f),
            config.PanelColor,
            config.LimeColor,
            config.PrimaryTextColor,
            28f
        );

        // Optional assist: the normal short guide is always available while
        // aiming; this button toggles the complete mirror-aware prediction.
        view.FullTraceButton = CreateButton(
            "FullTraceButton",
            hudRect,
            "FULL PATH  OFF",
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-28f, -116f),
            new Vector2(220f, 64f),
            config.PanelColor,
            config.PanelBorderColor,
            config.SecondaryTextColor,
            20f
        );
        view.FullTraceButtonText =
            view.FullTraceButton.GetComponentInChildren<TMP_Text>();

        RectTransform shotsPill = CreatePanel(
            "ShotsPill",
            hudRect,
            config.PanelColor,
            config.LimeColor,
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(30f, 28f),
            new Vector2(286f, 78f)
        );
        view.ShotsText = CreateText("ShotsText", shotsPill, "ARROWS  3", 29f, config.LimeColor, FontStyles.Bold);
        Stretch(view.ShotsText.rectTransform, 24f, 12f, 20f, 12f);
        view.ShotsText.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform hintPill = CreatePanel(
            "AimHintPill",
            hudRect,
            new Color(config.PanelColor.r, config.PanelColor.g, config.PanelColor.b, 0.84f),
            config.PanelBorderColor,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-30f, 28f),
            new Vector2(360f, 78f)
        );
        view.AimHintGroup = hintPill.gameObject.AddComponent<CanvasGroup>();
        view.AimHintText = CreateText("AimHintText", hintPill, "DRAG TO AIM  >", 25f, config.LimeColor, FontStyles.Bold);
        Stretch(view.AimHintText.rectTransform, 20f, 10f, 20f, 10f);
    }

    private static void BuildFeedback(RectTransform root, GameConfig config, GameUIView view)
    {
        view.FeedbackText = CreateText("CenterFeedback", root, string.Empty, 58f, config.YellowColor, FontStyles.Bold);
        RectTransform rect = view.FeedbackText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.58f);
        rect.anchorMax = new Vector2(0.5f, 0.58f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1000f, 100f);
        view.FeedbackText.enableAutoSizing = true;
        view.FeedbackText.fontSizeMin = 34f;
        view.FeedbackText.fontSizeMax = 58f;
        AddTextShadow(view.FeedbackText.gameObject, new Color(0f, 0f, 0f, 0.5f), new Vector2(3f, -3f));

        view.RicochetText = CreateText("RicochetFeedback", root, string.Empty, 36f, config.YellowColor, FontStyles.Bold);
        RectTransform ricochetRect = view.RicochetText.rectTransform;
        ricochetRect.anchorMin = new Vector2(0.5f, 0.70f);
        ricochetRect.anchorMax = new Vector2(0.5f, 0.70f);
        ricochetRect.pivot = new Vector2(0.5f, 0.5f);
        ricochetRect.anchoredPosition = Vector2.zero;
        ricochetRect.sizeDelta = new Vector2(760f, 72f);
        view.RicochetText.enableAutoSizing = true;
        view.RicochetText.fontSizeMin = 24f;
        view.RicochetText.fontSizeMax = 36f;
        view.RicochetText.alpha = 0f;
        view.RicochetText.gameObject.SetActive(false);
        AddTextShadow(view.RicochetText.gameObject, new Color(0f, 0f, 0f, 0.55f), new Vector2(2f, -2f));
    }

    private static void BuildResultOverlay(RectTransform root, GameConfig config, GameUIView view)
    {
        Image overlayImage = CreateImage("ResultOverlay", root, new Color(0.025f, 0.015f, 0.08f, 0.68f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        view.ResultOverlay = overlayImage.gameObject.AddComponent<CanvasGroup>();

        view.ResultCard = CreatePanel(
            "ResultCard",
            overlayImage.rectTransform,
            config.PanelColor,
            config.PanelBorderColor,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(680f, 560f)
        );
        AddShadow(view.ResultCard.gameObject, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -14f));

        view.ResultTitle = CreateText("ResultTitle", view.ResultCard, "PERFECT SHOT!", 50f, config.YellowColor, FontStyles.Bold);
        Place(view.ResultTitle.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 80f));

        BuildResultStars(view.ResultCard, config, view);

        view.ResultInfo = CreateText("ResultInfo", view.ResultCard, "SHOTS SPENT  1 / 3", 25f, config.SecondaryTextColor, FontStyles.Bold);
        Place(view.ResultInfo.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 58f));

        view.ResultPrimaryButton = CreateButton(
            "PrimaryButton",
            view.ResultCard,
            "NEXT LEVEL  >>",
            new Vector2(0.5f, 0.31f),
            new Vector2(0.5f, 0.31f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(430f, 76f),
            config.LimeColor,
            config.LimeColor,
            new Color(0.035f, 0.03f, 0.08f, 1f),
            27f
        );
        view.ResultPrimaryButtonText = view.ResultPrimaryButton.GetComponentInChildren<TMP_Text>();

        view.ResultReplayButton = CreateButton(
            "ReplayButton",
            view.ResultCard,
            "REPLAY",
            new Vector2(0.5f, 0.17f),
            new Vector2(0.5f, 0.17f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(430f, 62f),
            new Color(config.PanelColor.r * 0.8f, config.PanelColor.g * 0.8f, config.PanelColor.b * 0.8f, 1f),
            config.PanelBorderColor,
            config.PrimaryTextColor,
            23f
        );

        view.ResultLevelsButton = CreateButton(
            "LevelsButton", view.ResultCard, "LEVELS",
            new Vector2(0.35f, 0.055f), new Vector2(0.35f, 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(245f, 54f), new Color(0.08f, 0.05f, 0.20f, 1f), config.PanelBorderColor, config.SecondaryTextColor, 19f);

        view.ResultHomeButton = CreateButton(
            "HomeButton", view.ResultCard, "HOME",
            new Vector2(0.65f, 0.055f), new Vector2(0.65f, 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(245f, 54f), new Color(0.08f, 0.05f, 0.20f, 1f), config.PanelBorderColor, config.SecondaryTextColor, 19f);
    }

    private static void BuildResultStars(
        RectTransform card,
        GameConfig config,
        GameUIView view)
    {
        view.ResultStarsContainer =
            CreateRect(
                "ResultStars",
                card,
                new Vector2(0.5f, 0.63f),
                new Vector2(0.5f, 0.63f),
                Vector2.zero,
                Vector2.zero);

        RectTransform row =
            view.ResultStarsContainer;

        row.pivot =
            new Vector2(0.5f, 0.5f);

        row.anchoredPosition =
            Vector2.zero;

        row.sizeDelta =
            new Vector2(300f, 72f);

        const float starSize = 58f;
        const float starSpacing = 84f;

        view.ResultStars =
            new StarGraphic[3];

        Color idleColor =
            new Color(
                config.SecondaryTextColor.r,
                config.SecondaryTextColor.g,
                config.SecondaryTextColor.b,
                0.28f);

        for (int index = 0;
             index < view.ResultStars.Length;
             index++)
        {
            GameObject starObject =
                new GameObject(
                    $"Star{index + 1}",
                    typeof(RectTransform),
                    typeof(StarGraphic));

            starObject.transform.SetParent(
                row,
                false);

            RectTransform starRect =
                starObject.GetComponent<RectTransform>();

            Place(
                starRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(
                    (index - 1) *
                    starSpacing,
                    0f),
                new Vector2(
                    starSize,
                    starSize));

            StarGraphic star =
                starObject.GetComponent<StarGraphic>();

            star.color =
                idleColor;

            star.raycastTarget =
                false;

            Shadow shadow =
                starObject.AddComponent<Shadow>();

            shadow.effectColor =
                new Color(
                    0f,
                    0f,
                    0f,
                    0.35f);

            shadow.effectDistance =
                new Vector2(
                    2f,
                    -2f);

            shadow.useGraphicAlpha =
                true;

            view.ResultStars[index] =
                star;
        }

        view.ResultStarsMessage =
            CreateText(
                "ResultStarsMessage",
                card,
                string.Empty,
                27f,
                config.PrimaryTextColor,
                FontStyles.Bold);

        Place(
            view.ResultStarsMessage.rectTransform,
            new Vector2(0.5f, 0.63f),
            new Vector2(0.5f, 0.63f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(560f, 70f));

        view.ResultStarsMessage
            .gameObject
            .SetActive(false);
    }

    private static void BuildPauseOverlay(RectTransform root, GameConfig config, GameUIView view)
    {
        Image overlayImage = CreateImage("PauseOverlay", root, new Color(0.02f, 0.012f, 0.07f, 0.76f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        view.PauseOverlay = overlayImage.gameObject.AddComponent<CanvasGroup>();

        view.PauseCard = CreatePanel(
            "PauseCard",
            overlayImage.rectTransform,
            config.PanelColor,
            config.PanelBorderColor,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(600f, 510f)
        );
        AddShadow(view.PauseCard.gameObject, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -12f));

        TMP_Text title = CreateText("PauseTitle", view.PauseCard, "PAUSED", 48f, config.YellowColor, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 0.77f), new Vector2(0.5f, 0.77f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 70f));

        view.ResumeButton = CreateButton(
            "ResumeButton", view.PauseCard, "RESUME",
            new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(400f, 74f), config.LimeColor, config.LimeColor,
            new Color(0.035f, 0.03f, 0.08f, 1f), 26f
        );

        view.RestartButton = CreateButton(
            "RestartButton", view.PauseCard, "RESTART LEVEL",
            new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(400f, 66f), new Color(0.10f, 0.06f, 0.24f, 1f), config.PanelBorderColor,
            config.PrimaryTextColor, 23f
        );

        view.PauseLevelsButton = CreateButton(
            "PauseLevelsButton", view.PauseCard, "LEVELS",
            new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(400f, 60f), new Color(0.08f, 0.05f, 0.20f, 1f), config.PanelBorderColor, config.SecondaryTextColor, 20f);

        view.PauseHomeButton = CreateButton(
            "PauseHomeButton", view.PauseCard, "HOME",
            new Vector2(0.5f, 0.09f), new Vector2(0.5f, 0.09f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(400f, 56f), new Color(0.08f, 0.05f, 0.20f, 1f), config.PanelBorderColor, config.SecondaryTextColor, 19f);
    }

    private static RectTransform CreatePanel(
        string name,
        Transform parent,
        Color fill,
        Color border,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size
    )
    {
        Image image = CreateImage(name, parent, fill, anchorMin, anchorMax, anchoredPosition, size, pivot);
        bool premiumCard = name == "ResultCard" || name == "PauseCard";
        if (premiumCard)
            ATSPremiumSkin.Apply(image, "panel_ornate", new Vector4(30f, 30f, 30f, 30f));
        else
        {
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
        }
        Outline outline = image.gameObject.AddComponent<Outline>();
        if (premiumCard)
            outline.enabled = false;
        outline.effectColor = border;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
        return image.rectTransform;
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        Color fill,
        Color border,
        Color textColor,
        float fontSize
    )
    {
        RectTransform rect = CreatePanel(name, parent, fill, border, anchorMin, anchorMax, pivot, anchoredPosition, size);
        string skin = (name == "PrimaryButton" || name == "ResumeButton") ? "button_primary" :
            ((name == "ReplayButton" || name == "LevelsButton" || name == "HomeButton" || name == "RestartButton" || name == "PauseLevelsButton" || name == "PauseHomeButton") ? "button_secondary" : null);
        if (!string.IsNullOrEmpty(skin))
            ATSPremiumSkin.Apply(rect.GetComponent<Image>(), skin, new Vector4(30f, 30f, 30f, 30f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.3f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TMP_Text text = CreateText("Text", rect, label, fontSize, textColor, FontStyles.Bold);
        Stretch(text.rectTransform, 16f, 8f, 16f, 8f);
        return button;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        Vector2? pivot = null
    )
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Place(rect, anchorMin, anchorMax, pivot ?? new Vector2(0.5f, 0.5f), anchoredPosition, size);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string text, float fontSize, Color color, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TMP_Text tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        return tmp;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
    }

    private static void AddShadow(GameObject go, Color color, Vector2 distance)
    {
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static void AddTextShadow(GameObject go, Color color, Vector2 distance)
    {
        AddShadow(go, color, distance);
    }

    private static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null)
            return roundedSprite;

        const int size = 64;
        const float radius = 15f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "RuntimeRoundedUI";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x, radius, size - 1 - radius);
                float cy = Mathf.Clamp(y, radius, size - 1 - radius);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
            }
        }

        texture.Apply(false, true);
        roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );
        roundedSprite.name = "RuntimeRoundedUISprite";
        return roundedSprite;
    }
}
