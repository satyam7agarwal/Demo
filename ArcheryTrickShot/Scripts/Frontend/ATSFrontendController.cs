using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-facing frontend built entirely from reusable runtime UI primitives.
/// V2 focuses on presentation polish while keeping the existing roster, progression,
/// level data and v18 gameplay systems authoritative.
/// </summary>
public sealed class ATSFrontendController : MonoBehaviour
{
    private enum ScreenId { Main, Characters, Levels, Settings }

    private static readonly Color GlassPanel = new Color(0.045f, 0.020f, 0.115f, 0.90f);
    private static readonly Color InnerPanel = new Color(0.030f, 0.015f, 0.075f, 0.92f);
    private static readonly Color CardColor = new Color(0.055f, 0.030f, 0.125f, 0.96f);
    private static readonly Color CardSelectedColor = new Color(0.095f, 0.075f, 0.20f, 0.98f);
    private static readonly Color LockedCardColor = new Color(0.028f, 0.024f, 0.050f, 0.92f);

    private LevelManager levelManager;
    private GameConfig config;
    private BowController gameplayBow;
    private GameUIController gameplayUI;
    private CanvasGroup rootGroup;
    private RectTransform contentRoot;
    private readonly Dictionary<ScreenId, CanvasGroup> screens = new Dictionary<ScreenId, CanvasGroup>();
    private Coroutine transitionRoutine;
    private ArcherCharacterRoster roster;
    private LevelData[] levels;
    private TMP_Text selectedCharacterText;
    private TMP_Text characterPreviewName;
    private TMP_Text characterPreviewRole;
    private ATSCharacterPreview characterPreview;
    private readonly List<CharacterCard> characterCards = new List<CharacterCard>();
    private readonly List<LevelCard> levelCards = new List<LevelCard>();

    private sealed class CharacterCard
    {
        public Archer3DRuntimeProfile Profile;
        public Button Button;
        public Image Background;
        public Outline Border;
        public TMP_Text Badge;
    }

    private sealed class LevelCard
    {
        public LevelData Level;
        public Button Button;
        public Image Background;
        public Outline Border;
        public readonly List<Image> Stars = new List<Image>(3);
        public TMP_Text Score;
        public TMP_Text Lock;
        public Image LockOverlay;
    }

    public static ATSFrontendController Ensure(LevelManager manager, GameConfig gameConfig, BowController bow, GameUIController ui)
    {
        ATSFrontendController existing = FindFirstObjectByType<ATSFrontendController>();
        if (existing == null)
        {
            GameObject go = new GameObject("ATSFrontend");
            existing = go.AddComponent<ATSFrontendController>();
        }
        existing.Initialize(manager, gameConfig, bow, ui);
        return existing;
    }

    private void Initialize(LevelManager manager, GameConfig gameConfig, BowController bow, GameUIController ui)
    {
        levelManager = manager;
        config = gameConfig ?? GameConfig.Load();
        gameplayBow = bow;
        gameplayUI = ui;
        roster = ArcherCharacterRoster.LoadDefault();
        levels = Resources.LoadAll<LevelData>("Levels").Where(x => x != null).OrderBy(x => x.LevelNumber).ToArray();
        ATSPlayerProgress.ApplyPerformanceMode();

        if (rootGroup == null)
            BuildUI();

        RefreshCharacterCards();
        RefreshLevelCards();
        ApplyAudioSettings();
    }

    public void ShowMainMenu() => Show(ScreenId.Main);
    public void ShowCharacterSelect() => Show(ScreenId.Characters);
    public void ShowLevelSelect() { RefreshLevelCards(); Show(ScreenId.Levels); }
    public void ShowSettings() => Show(ScreenId.Settings);

    public void HideFrontendImmediate()
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        characterPreview?.SetVisible(false);
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;
        rootGroup.gameObject.SetActive(false);
    }

    private void Show(ScreenId id)
    {
        Time.timeScale = 1f;
        if (gameplayBow != null)
            gameplayBow.gameObject.SetActive(false);
        gameplayUI?.SetGameplayVisible(false);

        rootGroup.gameObject.SetActive(true);
        CanvasGroup activeGroup = null;
        foreach (KeyValuePair<ScreenId, CanvasGroup> item in screens)
        {
            bool active = item.Key == id;
            item.Value.gameObject.SetActive(active);
            item.Value.alpha = active ? 0f : 1f;
            item.Value.interactable = false;
            item.Value.blocksRaycasts = active;
            if (active)
                activeGroup = item.Value;
        }

        bool characterScreen = id == ScreenId.Characters;
        characterPreview?.SetVisible(characterScreen);
        if (characterScreen)
            RefreshCharacterPreview();

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionIn(activeGroup));
    }

    private IEnumerator TransitionIn(CanvasGroup screen)
    {
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = true;

        RectTransform rect = screen != null ? screen.transform as RectTransform : null;
        Vector2 finalPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
        Vector2 startPosition = finalPosition + new Vector2(24f, 0f);
        if (rect != null)
            rect.anchoredPosition = startPosition;

        const float duration = 0.20f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rootGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            if (screen != null)
                screen.alpha = eased;
            if (rect != null)
                rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, finalPosition, eased);
            yield return null;
        }

        rootGroup.alpha = 1f;
        rootGroup.interactable = true;
        if (screen != null)
        {
            screen.alpha = 1f;
            screen.interactable = true;
        }
        if (rect != null)
            rect.anchoredPosition = finalPosition;
        transitionRoutine = null;
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("FrontendCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = config.UIReferenceResolution;
        scaler.matchWidthOrHeight = config.UIMatchWidthOrHeight;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        Image dim = CreateImage("Backdrop", canvasRect, new Color(0.012f, 0.008f, 0.032f, 0.38f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        rootGroup = dim.gameObject.AddComponent<CanvasGroup>();
        contentRoot = CreateRect("Content", dim.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        contentRoot.gameObject.AddComponent<SafeAreaFitter>();

        BuildMainScreen();
        BuildCharacterScreen();
        BuildLevelScreen();
        BuildSettingsScreen();
    }

    private void BuildMainScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Main, "MainMenu");
        RectTransform panel = CreatePremiumPanel("MainCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420f, 0f), new Vector2(650f, 760f));

        Image accent = CreateImage("GoldAccent", panel, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.92f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(5f, 0f), new Vector2(6f, -18f), new Vector2(0f, 0.5f));
        accent.raycastTarget = false;

        Image crest = CreateImage("ArcheryCrest", panel, Color.white,
            new Vector2(0.5f, 0.955f), new Vector2(0.5f, 0.955f), Vector2.zero, new Vector2(52f, 52f));
        ATSPremiumSkin.Apply(crest, "archery_crest", Vector4.zero, false);
        crest.raycastTarget = false;

        TMP_Text eyebrow = CreateText("Eyebrow", panel, "ANCIENT TRIALS  •  PRECISION ARCHERY", 15f, config.YellowColor, FontStyles.Bold);
        Place(eyebrow.rectTransform, new Vector2(0.5f, 0.91f), new Vector2(0.5f, 0.91f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 34f));

        TMP_Text title = CreateText("Title", panel, "ARCHERY\nTRICK SHOT", 61f, config.PrimaryTextColor, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 0.77f), new Vector2(0.5f, 0.77f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 160f));

        TMP_Text subtitle = CreateText("Subtitle", panel, "MASTER THE ANGLE.  OWN THE SHOT.", 18f, config.SecondaryTextColor, FontStyles.Bold);
        Place(subtitle.rectTransform, new Vector2(0.5f, 0.655f), new Vector2(0.5f, 0.655f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 34f));

        Image divider = CreateImage("Divider", panel, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.55f),
            new Vector2(0.5f, 0.61f), new Vector2(0.5f, 0.61f), Vector2.zero, new Vector2(420f, 2f));
        divider.raycastTarget = false;

        TMP_Text currentLabel = CreateText("CurrentLabel", panel, "CURRENT ARCHER", 13f, config.SecondaryTextColor, FontStyles.Bold);
        Place(currentLabel.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 26f));

        selectedCharacterText = CreateText("SelectedCharacter", panel, "", 23f, config.YellowColor, FontStyles.Bold);
        Place(selectedCharacterText.rectTransform, new Vector2(0.5f, 0.525f), new Vector2(0.5f, 0.525f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 38f));

        Button play = CreateButton("Play", panel, "PLAY", new Vector2(0.5f, 0.405f), new Vector2(0.5f, 0.405f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(470f, 84f), config.LimeColor, config.LimeColor, new Color(0.96f, 0.98f, 0.92f, 1f), 29f, true, "button_primary");
        Image playIcon = CreateImage("PlayIcon", play.transform, Color.white, new Vector2(0.12f, 0.5f), new Vector2(0.12f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        ATSPremiumSkin.Apply(playIcon, "icon_play", Vector4.zero, false);
        playIcon.raycastTarget = false;
        play.onClick.AddListener(() =>
        {
            Click();
            int target = Mathf.Clamp(ATSPlayerProgress.LastPlayedLevel, 1, Mathf.Max(1, levels.Length));
            levelManager.StartLevelByNumber(target);
        });

        CreateMenuButton(panel, "CHARACTERS", 0.275f, ShowCharacterSelect);
        CreateMenuButton(panel, "LEVELS", 0.165f, ShowLevelSelect);
        CreateMenuButton(panel, "SETTINGS", 0.055f, ShowSettings);
    }

    private void BuildCharacterScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Characters, "Characters");
        RectTransform panel = CreatePremiumPanel("CharacterCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1450f, 830f));
        AddHeader(panel, "SELECT ARCHER", "CHOOSE YOUR STYLE. YOUR SELECTION IS SAVED.");

        RectTransform leftPanel = CreatePanel("CharacterListPanel", panel, InnerPanel, new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.65f),
            new Vector2(0.055f, 0.16f), new Vector2(0.365f, 0.73f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(leftPanel.GetComponent<Image>(), "panel_inner", new Vector4(26f, 26f, 26f, 26f));
        RectTransform listViewport = CreateRect("ListViewport", leftPanel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero);
        listViewport.gameObject.AddComponent<RectMask2D>();
        ScrollRect listScroll = leftPanel.gameObject.AddComponent<ScrollRect>();
        listScroll.viewport = listViewport;
        listScroll.horizontal = false;
        listScroll.vertical = true;
        listScroll.movementType = ScrollRect.MovementType.Clamped;
        listScroll.scrollSensitivity = 28f;

        RectTransform list = CreateRect("CharacterList", listViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        list.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 8, 8);
        ContentSizeFitter fitter = list.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        listScroll.content = list;

        characterCards.Clear();
        if (roster != null && roster.Profiles != null)
        {
            foreach (Archer3DRuntimeProfile profile in roster.Profiles)
            {
                if (profile == null || !profile.PlayerSelectable)
                    continue;

                RectTransform card = CreatePanel("Character_" + profile.CharacterId, list, CardColor,
                    new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.55f),
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 142f));
                ATSPremiumSkin.Apply(card.GetComponent<Image>(), "character_card", new Vector4(28f, 28f, 28f, 28f));
                LayoutElement cardLayout = card.gameObject.AddComponent<LayoutElement>();
                cardLayout.preferredHeight = 142f;
                cardLayout.minHeight = 142f;

                Image emblemPlate = CreateImage("EmblemPlate", card, new Color(0.02f, 0.07f, 0.14f, 0.88f),
                    new Vector2(0.06f, 0.20f), new Vector2(0.28f, 0.80f), Vector2.zero, Vector2.zero);
                ATSPremiumSkin.Apply(emblemPlate, "panel_inner", new Vector4(22f, 22f, 22f, 22f));
                string emblem = profile.CharacterId.Equals("khaem", System.StringComparison.OrdinalIgnoreCase) ? "icon_helmet" :
                    (profile.CharacterId.Equals("nerissa", System.StringComparison.OrdinalIgnoreCase) ? "icon_ranger" : "icon_characters");
                Image emblemImage = CreateImage("Emblem", emblemPlate.transform, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));
                ATSPremiumSkin.Apply(emblemImage, emblem, Vector4.zero, false);
                emblemImage.raycastTarget = false;

                TMP_Text name = CreateText("Name", card, profile.DisplayName.ToUpperInvariant(), 25f, config.PrimaryTextColor, FontStyles.Bold);
                name.alignment = TextAlignmentOptions.Left;
                Place(name.rectTransform, new Vector2(0.34f, 0.62f), new Vector2(0.93f, 0.82f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                TMP_Text role = CreateText("Role", card, GetRole(profile), 14f, config.SecondaryTextColor, FontStyles.Bold);
                role.alignment = TextAlignmentOptions.Left;
                Place(role.rectTransform, new Vector2(0.34f, 0.40f), new Vector2(0.93f, 0.58f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                TMP_Text badge = CreateText("Badge", card, "SELECT", 14f, config.LimeColor, FontStyles.Bold);
                badge.alignment = TextAlignmentOptions.Left;
                Place(badge.rectTransform, new Vector2(0.34f, 0.14f), new Vector2(0.93f, 0.34f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                Button button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                card.gameObject.AddComponent<ATSButtonMotion>();
                Archer3DRuntimeProfile captured = profile;
                button.onClick.AddListener(() => SelectCharacter(captured));
                characterCards.Add(new CharacterCard
                {
                    Profile = profile,
                    Button = button,
                    Background = card.GetComponent<Image>(),
                    Border = card.GetComponent<Outline>(),
                    Badge = badge
                });
            }
        }

        RectTransform previewFrame = CreatePanel("PreviewFrame", panel, InnerPanel,
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.55f),
            new Vector2(0.395f, 0.16f), new Vector2(0.945f, 0.73f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(previewFrame.GetComponent<Image>(), "panel_inner", new Vector4(26f, 26f, 26f, 26f));

        Image pedestal = CreateImage("PreviewPedestal", previewFrame, Color.white,
            new Vector2(0.10f, 0.13f), new Vector2(0.68f, 0.37f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(pedestal, "preview_pedestal", Vector4.zero, false);
        pedestal.raycastTarget = false;

        RawImage previewImage = CreateRawImage("CharacterPreview", previewFrame,
            new Vector2(0.08f, 0.16f), new Vector2(0.70f, 0.94f), Vector2.zero, Vector2.zero);
        previewImage.color = Color.white;

        RectTransform info = CreateRect("PreviewInfo", previewFrame, new Vector2(0.70f, 0.18f), new Vector2(0.96f, 0.91f), Vector2.zero, Vector2.zero);
        TMP_Text label = CreateText("PreviewLabel", info, "ACTIVE ARCHER", 13f, config.YellowColor, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Left;
        Place(label.rectTransform, new Vector2(0f, 0.82f), new Vector2(1f, 0.95f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreviewName = CreateText("PreviewName", info, "", 34f, config.PrimaryTextColor, FontStyles.Bold);
        characterPreviewName.alignment = TextAlignmentOptions.Left;
        characterPreviewName.enableWordWrapping = true;
        Place(characterPreviewName.rectTransform, new Vector2(0f, 0.61f), new Vector2(1f, 0.82f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreviewRole = CreateText("PreviewRole", info, "", 17f, config.SecondaryTextColor, FontStyles.Bold);
        characterPreviewRole.alignment = TextAlignmentOptions.Left;
        characterPreviewRole.enableWordWrapping = true;
        Place(characterPreviewRole.rectTransform, new Vector2(0f, 0.46f), new Vector2(1f, 0.61f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        TMP_Text note = CreateText("PreviewNote", info, "Your archer is remembered\nbetween sessions.", 14f, config.SecondaryTextColor, FontStyles.Normal);
        note.alignment = TextAlignmentOptions.Left;
        note.enableWordWrapping = true;
        Place(note.rectTransform, new Vector2(0f, 0.16f), new Vector2(1f, 0.40f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreview = gameObject.AddComponent<ATSCharacterPreview>();
        characterPreview.Initialize(previewImage);
        AddBackButton(panel, ShowMainMenu);
    }

    private void BuildLevelScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Levels, "Levels");
        RectTransform panel = CreatePremiumPanel("LevelsCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 860f));
        AddHeader(panel, "CHOOSE YOUR CHALLENGE", "NEW LEVELS UNLOCK AS YOU PROGRESS");

        RectTransform viewport = CreateRect("LevelViewport", panel, new Vector2(0.055f, 0.17f), new Vector2(0.945f, 0.72f), Vector2.zero, Vector2.zero);
        Image viewportFill = viewport.gameObject.AddComponent<Image>();
        viewportFill.color = new Color(0f, 0f, 0f, 0.06f);
        viewportFill.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.14f;
        scroll.scrollSensitivity = 42f;
        scroll.viewport = viewport;

        RectTransform content = CreateRect("LevelGrid", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(290f, 168f);
        grid.spacing = new Vector2(22f, 20f);
        grid.padding = new RectOffset(20, 20, 18, 18);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperCenter;
        scroll.content = content;

        levelCards.Clear();
        foreach (LevelData level in levels)
        {
            RectTransform card = CreatePanel("Level_" + level.LevelNumber, content, CardColor,
                new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.62f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ATSPremiumSkin.Apply(card.GetComponent<Image>(), "level_card", new Vector4(26f, 26f, 26f, 26f));

            TMP_Text number = CreateText("Number", card, "LEVEL " + level.LevelNumber, 25f, config.PrimaryTextColor, FontStyles.Bold);
            Place(number.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 38f));

            RectTransform starsRoot = CreateRect("Stars", card, new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.49f), Vector2.zero, Vector2.zero);
            starsRoot.pivot = new Vector2(0.5f, 0.5f);
            starsRoot.anchoredPosition = Vector2.zero;
            starsRoot.sizeDelta = new Vector2(114f, 34f);
            List<Image> stars = new List<Image>(3);
            for (int i = 0; i < 3; i++)
            {
                Image star = CreateImage("Star" + (i + 1), starsRoot, Color.white,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(19f + i * 38f, 0f), new Vector2(25f, 25f));
                ATSPremiumSkin.Apply(star, "star_empty", Vector4.zero, false);
                star.raycastTarget = false;
                stars.Add(star);
            }

            TMP_Text score = CreateText("Score", card, "BEST  0", 15f, config.SecondaryTextColor, FontStyles.Bold);
            Place(score.rectTransform, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 28f));

            Image lockOverlay = CreateImage("LockOverlay", card, new Color(0.012f, 0.010f, 0.022f, 0.58f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image lockIcon = CreateImage("LockIcon", lockOverlay.transform, Color.white,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f), Vector2.zero, new Vector2(46f, 46f));
            ATSPremiumSkin.Apply(lockIcon, "lock", Vector4.zero, false);
            lockIcon.raycastTarget = false;
            TMP_Text lockText = CreateText("Lock", lockOverlay.transform, "LOCKED", 15f, config.PinkColor, FontStyles.Bold);
            Place(lockText.rectTransform, new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 28f));

            Button button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            card.gameObject.AddComponent<ATSButtonMotion>();
            LevelData captured = level;
            button.onClick.AddListener(() =>
            {
                if (ATSPlayerProgress.IsLevelUnlocked(captured.LevelNumber))
                {
                    Click();
                    levelManager.StartLevelByNumber(captured.LevelNumber);
                }
            });

            LevelCard entry = new LevelCard
            {
                Level = level,
                Button = button,
                Background = card.GetComponent<Image>(),
                Border = card.GetComponent<Outline>(),
                Score = score,
                Lock = lockText,
                LockOverlay = lockOverlay
            };
            entry.Stars.AddRange(stars);
            levelCards.Add(entry);
        }

        int rows = Mathf.CeilToInt(levels.Length / 4f);
        float contentHeight = grid.padding.top + grid.padding.bottom + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);

        TMP_Text hint = CreateText("ScrollHint", panel, levels.Length > 8 ? "SCROLL TO EXPLORE MORE LEVELS" : "", 13f, config.SecondaryTextColor, FontStyles.Bold);
        Place(hint.rectTransform, new Vector2(0.5f, 0.135f), new Vector2(0.5f, 0.135f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 24f));

        AddBackButton(panel, ShowMainMenu);
    }

    private void BuildSettingsScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Settings, "Settings");
        RectTransform panel = CreatePremiumPanel("SettingsCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 740f));
        AddHeader(panel, "SETTINGS", "TUNE THE EXPERIENCE");

        CreateSettingSwitchRow(panel, "MUSIC", "Background soundtrack", 0.61f,
            () => ATSPlayerProgress.MusicEnabled,
            value => { ATSPlayerProgress.MusicEnabled = value; ApplyAudioSettings(); });

        CreateSettingSwitchRow(panel, "SOUND EFFECTS", "Impacts, UI and shot feedback", 0.48f,
            () => ATSPlayerProgress.SfxEnabled,
            value => { ATSPlayerProgress.SfxEnabled = value; ApplyAudioSettings(); });

        CreateSettingSwitchRow(panel, "HAPTICS", "Mobile vibration feedback", 0.35f,
            () => ATSPlayerProgress.HapticsEnabled,
            value => ATSPlayerProgress.HapticsEnabled = value);

        Image performanceIcon = CreateImage("PerformanceIcon", panel, Color.white, new Vector2(0.135f, 0.22f), new Vector2(0.135f, 0.22f), Vector2.zero, new Vector2(44f, 44f));
        ATSPremiumSkin.Apply(performanceIcon, "icon_performance", Vector4.zero, false);
        performanceIcon.raycastTarget = false;
        TMP_Text performanceLabel = CreateText("PerformanceLabel", panel, "PERFORMANCE", 20f, config.PrimaryTextColor, FontStyles.Bold);
        performanceLabel.alignment = TextAlignmentOptions.Left;
        Place(performanceLabel.rectTransform, new Vector2(0.18f, 0.215f), new Vector2(0.48f, 0.255f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text performanceSub = CreateText("PerformanceSub", panel, "Choose smoothness or longer battery life", 13f, config.SecondaryTextColor, FontStyles.Normal);
        performanceSub.alignment = TextAlignmentOptions.Left;
        Place(performanceSub.rectTransform, new Vector2(0.18f, 0.175f), new Vector2(0.55f, 0.215f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform segmented = CreatePanel("PerformanceSelector", panel, new Color(0.018f, 0.012f, 0.050f, 0.95f),
            new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.6f),
            new Vector2(0.58f, 0.185f), new Vector2(0.84f, 0.255f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(segmented.GetComponent<Image>(), "panel_inner", new Vector4(24f, 24f, 24f, 24f));

        Button smooth = CreateButton("Smooth", segmented, "SMOOTH 60", new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 13f, false);
        Button battery = CreateButton("Battery", segmented, "BATTERY 30", new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 13f, false);
        Image smoothFill = CreateImage("SmoothFill", smooth.transform, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        smoothFill.transform.SetAsFirstSibling(); smoothFill.raycastTarget = false;
        Image batteryFill = CreateImage("BatteryFill", battery.transform, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        batteryFill.transform.SetAsFirstSibling(); batteryFill.raycastTarget = false;

        TMP_Text smoothText = smooth.GetComponentInChildren<TMP_Text>();
        TMP_Text batteryText = battery.GetComponentInChildren<TMP_Text>();
        System.Action refreshPerformance = () =>
        {
            bool isSmooth = ATSPlayerProgress.PerformanceMode == 0;
            smoothFill.color = isSmooth ? new Color(config.LimeColor.r, config.LimeColor.g, config.LimeColor.b, 0.16f) : Color.clear;
            batteryFill.color = !isSmooth ? new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.13f) : Color.clear;
            smoothText.color = isSmooth ? config.LimeColor : config.SecondaryTextColor;
            batteryText.color = !isSmooth ? config.YellowColor : config.SecondaryTextColor;
        };
        smooth.onClick.AddListener(() => { Click(); ATSPlayerProgress.PerformanceMode = 0; refreshPerformance(); });
        battery.onClick.AddListener(() => { Click(); ATSPlayerProgress.PerformanceMode = 1; refreshPerformance(); });
        refreshPerformance();

        AddBackButton(panel, ShowMainMenu);
    }

    private void CreateSettingSwitchRow(RectTransform panel, string title, string subtitle, float y, System.Func<bool> getter, System.Action<bool> setter)
    {
        string iconName = title == "MUSIC" ? "icon_music" : (title == "SOUND EFFECTS" ? "icon_sfx" : "icon_haptics");
        Image rowIcon = CreateImage(title + "Icon", panel, Color.white, new Vector2(0.135f, y + 0.012f), new Vector2(0.135f, y + 0.012f), Vector2.zero, new Vector2(44f, 44f));
        ATSPremiumSkin.Apply(rowIcon, iconName, Vector4.zero, false);
        rowIcon.raycastTarget = false;
        TMP_Text label = CreateText(title + "Label", panel, title, 21f, config.PrimaryTextColor, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Left;
        Place(label.rectTransform, new Vector2(0.18f, y), new Vector2(0.50f, y + 0.05f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        TMP_Text sub = CreateText(title + "Sub", panel, subtitle, 13f, config.SecondaryTextColor, FontStyles.Normal);
        sub.alignment = TextAlignmentOptions.Left;
        Place(sub.rectTransform, new Vector2(0.18f, y - 0.045f), new Vector2(0.55f, y), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform switchRect = CreatePanel(title + "Switch", panel, new Color(0.018f, 0.012f, 0.05f, 0.96f),
            new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.60f),
            new Vector2(0.68f, y - 0.015f), new Vector2(0.82f, y + 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Button button = switchRect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        switchRect.gameObject.AddComponent<ATSButtonMotion>();
        Image track = switchRect.GetComponent<Image>();
        ATSPremiumSkin.Apply(track, "toggle_track", new Vector4(26f, 26f, 26f, 26f));
        Image knob = CreateImage("Knob", switchRect, Color.white, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(42f, 42f));
        ATSPremiumSkin.Apply(knob, "toggle_knob", Vector4.zero, false);
        knob.raycastTarget = false;

        System.Action refresh = () =>
        {
            bool on = getter();
            track.color = on ? new Color(0.72f, 1f, 0.74f, 1f) : Color.white;
            knob.rectTransform.anchorMin = new Vector2(on ? 1f : 0f, 0.5f);
            knob.rectTransform.anchorMax = knob.rectTransform.anchorMin;
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            knob.rectTransform.anchoredPosition = new Vector2(on ? -28f : 28f, 0f);
            Outline outline = switchRect.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = on ? config.LimeColor : new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.60f);
        };

        button.onClick.AddListener(() => { Click(); setter(!getter()); refresh(); });
        refresh();
    }

    private void RefreshCharacterCards()
    {
        if (roster == null)
            return;

        Archer3DRuntimeProfile selected = roster.ResolveSelectedProfile();
        foreach (CharacterCard card in characterCards)
        {
            bool active = card.Profile == selected;
            ATSPremiumSkin.Apply(card.Background, active ? "character_card_selected" : "character_card", new Vector4(28f, 28f, 28f, 28f));
            if (card.Border != null)
                card.Border.enabled = false;
            card.Badge.text = active ? "✓  SELECTED" : "SELECT";
            card.Badge.color = active ? config.LimeColor : config.SecondaryTextColor;
        }

        if (selectedCharacterText != null)
            selectedCharacterText.text = selected != null ? selected.DisplayName.ToUpperInvariant() : "ARCHER";

        UpdateCharacterPreviewLabels(selected);
    }

    private void RefreshCharacterPreview()
    {
        if (roster == null)
            return;
        Archer3DRuntimeProfile selected = roster.ResolveSelectedProfile();
        characterPreview?.Show(selected);
        UpdateCharacterPreviewLabels(selected);
    }

    private void UpdateCharacterPreviewLabels(Archer3DRuntimeProfile selected)
    {
        if (characterPreviewName != null)
            characterPreviewName.text = selected != null ? selected.DisplayName.ToUpperInvariant() : "ARCHER";
        if (characterPreviewRole != null)
            characterPreviewRole.text = selected != null ? GetRole(selected) : string.Empty;
    }

    private void RefreshLevelCards()
    {
        int latestUnlocked = 1;
        foreach (LevelCard card in levelCards)
        {
            if (ATSPlayerProgress.IsLevelUnlocked(card.Level.LevelNumber))
                latestUnlocked = Mathf.Max(latestUnlocked, card.Level.LevelNumber);
        }

        foreach (LevelCard card in levelCards)
        {
            bool unlocked = ATSPlayerProgress.IsLevelUnlocked(card.Level.LevelNumber);
            int stars = ATSPlayerProgress.GetBestStars(card.Level.LevelNumber);
            bool latest = unlocked && card.Level.LevelNumber == latestUnlocked;

            card.Button.interactable = unlocked;
            card.LockOverlay.gameObject.SetActive(!unlocked);
            card.Lock.gameObject.SetActive(!unlocked);
            card.Score.text = "BEST  " + ATSPlayerProgress.GetBestScore(card.Level.LevelNumber).ToString("N0");
            string levelSkin = !unlocked ? "level_card_locked" : (latest ? "level_card_latest" : "level_card");
            ATSPremiumSkin.Apply(card.Background, levelSkin, new Vector4(26f, 26f, 26f, 26f));
            if (card.Border != null)
                card.Border.enabled = false;

            for (int i = 0; i < card.Stars.Count; i++)
            {
                bool filled = i < stars;
                ATSPremiumSkin.Apply(card.Stars[i], filled ? "star_filled" : "star_empty", Vector4.zero, false);
                card.Stars[i].color = Color.white;
            }
        }
    }

    private void SelectCharacter(Archer3DRuntimeProfile profile)
    {
        if (roster == null || profile == null)
            return;
        Click();
        roster.SelectCharacter(profile.CharacterId);
        RefreshCharacterCards();
        characterPreview?.Show(profile);
    }

    private void ApplyAudioSettings()
    {
        GameAudioController.Instance?.SetMusicVolume(ATSPlayerProgress.MusicEnabled ? config.MusicVolume : 0f);
        GameAudioController.Instance?.SetSfxEnabled(ATSPlayerProgress.SfxEnabled);
    }

    private void AddHeader(RectTransform panel, string title, string subtitle)
    {
        Image ribbon = CreateImage("HeaderRibbon", panel, Color.white,
            new Vector2(0.5f, 0.86f), new Vector2(0.5f, 0.86f), Vector2.zero, new Vector2(820f, 118f));
        ATSPremiumSkin.Apply(ribbon, "header_ribbon", Vector4.zero, false);
        ribbon.raycastTarget = false;
        TMP_Text heading = CreateText("Heading", panel, title, 43f, config.PrimaryTextColor, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 70f));
        TMP_Text sub = CreateText("Subheading", panel, subtitle, 16f, config.SecondaryTextColor, FontStyles.Bold);
        Place(sub.rectTransform, new Vector2(0.5f, 0.81f), new Vector2(0.5f, 0.81f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 34f));
        Image line = CreateImage("HeaderLine", panel, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.42f),
            new Vector2(0.5f, 0.765f), new Vector2(0.5f, 0.765f), Vector2.zero, new Vector2(300f, 2f));
        line.raycastTarget = false;
    }

    private void CreateMenuButton(RectTransform panel, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton(label, panel, label, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(470f, 66f), new Color(0.035f, 0.018f, 0.085f, 0.96f), new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.72f), config.PrimaryTextColor, 21f, true, "button_secondary");
        Image accent = CreateImage("Accent", button.transform, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.72f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 0f), new Vector2(4f, -10f), new Vector2(0f, 0.5f));
        accent.raycastTarget = false;
        string iconName = label == "CHARACTERS" ? "icon_characters" : (label == "LEVELS" ? "icon_levels" : "icon_settings");
        Image icon = CreateImage(label + "Icon", button.transform, Color.white, new Vector2(0.12f, 0.5f), new Vector2(0.12f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
        ATSPremiumSkin.Apply(icon, iconName, Vector4.zero, false);
        icon.raycastTarget = false;
        button.onClick.AddListener(() => { Click(); action(); });
    }

    private void AddBackButton(RectTransform panel, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton("Back", panel, "<  BACK", new Vector2(0.055f, 0.055f), new Vector2(0.055f, 0.055f), new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(220f, 56f), new Color(0.030f, 0.016f, 0.075f, 0.98f), new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.70f), config.PrimaryTextColor, 18f, true, "button_back");
        Image backIcon = CreateImage("BackIcon", button.transform, Color.white, new Vector2(0.15f, 0.5f), new Vector2(0.15f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
        ATSPremiumSkin.Apply(backIcon, "icon_back", Vector4.zero, false);
        backIcon.raycastTarget = false;
        button.onClick.AddListener(() => { Click(); action(); });
    }

    private CanvasGroup CreateScreen(ScreenId id, string name)
    {
        RectTransform rect = CreateRect(name, contentRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        screens[id] = group;
        return group;
    }

    private RectTransform CreatePremiumPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        RectTransform panel = CreatePanel(name, parent, GlassPanel,
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.58f),
            anchorMin, anchorMax, pivot, position, size);
        ATSPremiumSkin.Apply(panel.GetComponent<Image>(), "panel_ornate", new Vector4(30f, 30f, 30f, 30f));
        Shadow shadow = panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -8f);
        shadow.useGraphicAlpha = true;
        return panel;
    }

    private static string GetRole(Archer3DRuntimeProfile profile)
    {
        if (profile == null)
            return "ARCHER";
        if (profile.CharacterId.Equals("khaem", System.StringComparison.OrdinalIgnoreCase))
            return "WARRIOR ARCHER";
        if (profile.CharacterId.Equals("nerissa", System.StringComparison.OrdinalIgnoreCase))
            return "RANGER ARCHER";
        return "ARCHER";
    }

    private static void Click() => GameAudioController.Instance?.PlayUIClick();

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

    private static RectTransform CreatePanel(string name, Transform parent, Color fill, Color border, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Image image = CreateImage(name, parent, fill, anchorMin, anchorMax, position, size, pivot);
        Outline outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = border;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;
        return image.rectTransform;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
        Vector2 size, Color fill, Color border, Color textColor, float fontSize, bool addMotion = true, string skinName = null)
    {
        RectTransform rect = CreatePanel(name, parent, fill, border, anchorMin, anchorMax, pivot, position, size);
        if (!string.IsNullOrEmpty(skinName))
            ATSPremiumSkin.Apply(rect.GetComponent<Image>(), skinName, new Vector4(30f, 30f, 30f, 30f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        if (addMotion)
            button.gameObject.AddComponent<ATSButtonMotion>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.94f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
        colors.fadeDuration = 0.07f;
        button.colors = colors;
        TMP_Text text = CreateText("Text", rect, label, fontSize, textColor, FontStyles.Bold);
        Stretch(text.rectTransform, 14f, 8f, 14f, 8f);
        return button;
    }

    private static Image CreateImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Place(rect, anchorMin, anchorMax, pivot ?? new Vector2(0.5f, 0.5f), position, size);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static RawImage CreateRawImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Place(rect, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), position, size);
        RawImage image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float fontSize, Color color, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TMP_Text text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
