using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-facing frontend using a fixed premium 1920x1080 authored composition.
/// Dynamic data (characters, levels, stars, locks, settings and progression) is still
/// populated at runtime, but hero-screen geometry is deliberately fixed so Unity layout
/// groups cannot compress or distort the approved visual composition.
/// </summary>
public sealed class ATSFrontendController : MonoBehaviour
{
    private enum ScreenId { Main, Characters, Levels, Settings }

    [System.Serializable]
    private sealed class CampaignMapData
    {
        public string chapterTitle = "ANCIENT RUINS";
        public string chapterSubtitle = "MASTER THE ANGLE. OWN THE SHOT.";
        public float mapWidth = 3000f;
        public float mapHeight = 820f;
        public CampaignMapNodeData[] nodes = new CampaignMapNodeData[0];
    }

    [System.Serializable]
    private sealed class CampaignMapNodeData
    {
        public int level;
        public float x;
        public float y;
        public string title;
        public string subtitle;
    }

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
    private RawImage mainPreviewImage;
    private RawImage characterPreviewImage;
    private readonly List<CharacterCard> characterCards = new List<CharacterCard>();
    private readonly List<LevelCard> levelCards = new List<LevelCard>();

    // Campaign map state. This replaces the old grid visually while preserving the
    // same progression/save data and LevelManager gameplay entry points.
    private CampaignMapData campaignData;
    private ScrollRect campaignScroll;
    private RectTransform campaignContent;
    private RectTransform campaignViewport;
    private LevelData campaignSelectedLevel;
    private TMP_Text campaignDetailLevel;
    private TMP_Text campaignDetailTitle;
    private TMP_Text campaignDetailDescription;
    private TMP_Text campaignDetailShots;
    private TMP_Text campaignDetailScore;
    private TMP_Text campaignDetailLock;
    private TMP_Text campaignProgress;
    private readonly List<Image> campaignDetailStars = new List<Image>(3);
    private Button campaignPlayButton;
    private Image campaignPreviewImage;

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
        public Image Thumbnail;
        public RectTransform NodeRoot;
        public TMP_Text Number;
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
        campaignData = LoadCampaignMapData();
        ATSPlayerProgress.ApplyPerformanceMode();

        if (rootGroup == null)
            BuildUI();

        RefreshCharacterCards();
        RefreshLevelCards();
        ApplyAudioSettings();
    }

    public void ShowMainMenu() => Show(ScreenId.Main);
    public void ShowCharacterSelect() => Show(ScreenId.Characters);
    public void ShowLevelSelect()
    {
        EnsureCampaignSelection();
        RefreshLevelCards();
        Show(ScreenId.Levels);
        StartCoroutine(ScrollCampaignToSelection(false));
    }
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

        bool previewScreen = id == ScreenId.Main || id == ScreenId.Characters;
        if (characterPreview != null)
        {
            characterPreview.SetTarget(id == ScreenId.Main ? mainPreviewImage : characterPreviewImage);
            characterPreview.SetVisible(previewScreen);
        }
        if (previewScreen)
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
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        // Keep the authored ruins visible. The premium frames provide contrast instead of a heavy full-screen tint.
        Image dim = CreateImage("Backdrop", canvasRect, new Color(0.008f, 0.006f, 0.020f, 0.18f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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

        // Fixed 1920x1080 composition: authored menu frame on the left, living selected archer on the right.
        RectTransform panel = CreatePremiumPanel("MainCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-480f, 0f), new Vector2(760f, 900f));

        Image crest = CreateImage("ArcheryCrest", panel, Color.white,
            new Vector2(0.5f, 0.935f), new Vector2(0.5f, 0.935f), Vector2.zero, new Vector2(68f, 68f));
        ATSPremiumSkin.Apply(crest, "archery_crest", Vector4.zero, false);
        crest.raycastTarget = false;

        TMP_Text eyebrow = CreateText("Eyebrow", panel, "ANCIENT TRIALS  •  PRECISION ARCHERY", 17f, config.YellowColor, FontStyles.Bold);
        Place(eyebrow.rectTransform, new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 34f));

        TMP_Text title = CreateText("Title", panel, "ARCHERY\nTRICK SHOT", 76f, config.PrimaryTextColor, FontStyles.Bold);
        title.characterSpacing = 2f;
        Place(title.rectTransform, new Vector2(0.5f, 0.735f), new Vector2(0.5f, 0.735f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650f, 190f));

        TMP_Text subtitle = CreateText("Subtitle", panel, "MASTER THE ANGLE.  OWN THE SHOT.", 19f, config.SecondaryTextColor, FontStyles.Bold);
        Place(subtitle.rectTransform, new Vector2(0.5f, 0.615f), new Vector2(0.5f, 0.615f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(610f, 34f));

        Image divider = CreateImage("Divider", panel, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.70f),
            new Vector2(0.5f, 0.575f), new Vector2(0.5f, 0.575f), Vector2.zero, new Vector2(470f, 2f));
        divider.raycastTarget = false;

        TMP_Text currentLabel = CreateText("CurrentLabel", panel, "CURRENT ARCHER", 14f, config.SecondaryTextColor, FontStyles.Bold);
        Place(currentLabel.rectTransform, new Vector2(0.5f, 0.535f), new Vector2(0.5f, 0.535f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 26f));

        selectedCharacterText = CreateText("SelectedCharacter", panel, "", 28f, config.YellowColor, FontStyles.Bold);
        Place(selectedCharacterText.rectTransform, new Vector2(0.5f, 0.495f), new Vector2(0.5f, 0.495f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 38f));

        Button play = CreateButton("Play", panel, "PLAY", new Vector2(0.5f, 0.365f), new Vector2(0.5f, 0.365f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(560f, 92f), config.LimeColor, config.LimeColor, new Color(0.96f, 0.98f, 0.92f, 1f), 34f, true, "button_primary");
        Image playIcon = CreateImage("PlayIcon", play.transform, Color.white, new Vector2(0.12f, 0.5f), new Vector2(0.12f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
        ATSPremiumSkin.Apply(playIcon, "icon_play", Vector4.zero, false);
        playIcon.raycastTarget = false;
        // Premium campaign flow: Home PLAY opens the campaign map first.
        // The player can inspect progress/select a level there, and only the
        // campaign detail PLAY button starts gameplay.
        play.onClick.AddListener(() =>
        {
            Click();
            ShowLevelSelect();
        });

        CreateMenuButton(panel, "CHARACTERS", 0.245f, ShowCharacterSelect);
        CreateMenuButton(panel, "LEVELS", 0.145f, ShowLevelSelect);
        CreateMenuButton(panel, "SETTINGS", 0.045f, ShowSettings);

        // Main-menu hero presentation uses the actual selected runtime character, not baked artwork.
        Image heroGlow = CreateImage("HeroGlow", group.transform, Color.white,
            new Vector2(0.73f, 0.19f), new Vector2(0.73f, 0.19f), Vector2.zero, new Vector2(650f, 210f));
        ATSPremiumSkin.Apply(heroGlow, "preview_pedestal", Vector4.zero, false);
        heroGlow.color = new Color(1f, 1f, 1f, 0.86f);
        heroGlow.raycastTarget = false;

        mainPreviewImage = CreateRawImage("MainCharacterPreview", group.transform,
            new Vector2(0.52f, 0.08f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
        mainPreviewImage.color = Color.white;

        RectTransform heroLabel = CreatePanel("HeroLabel", group.transform, new Color(0.008f, 0.02f, 0.05f, 0.84f),
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.45f),
            new Vector2(0.68f, 0.08f), new Vector2(0.91f, 0.19f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(heroLabel.GetComponent<Image>(), "panel_inner", new Vector4(24f, 24f, 24f, 24f));
        TMP_Text heroHint = CreateText("HeroHint", heroLabel, "YOUR SELECTED ARCHER", 15f, config.YellowColor, FontStyles.Bold);
        Place(heroHint.rectTransform, new Vector2(0.5f, 0.67f), new Vector2(0.5f, 0.67f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330f, 28f));
        TMP_Text heroSub = CreateText("HeroSub", heroLabel, "READY FOR THE NEXT TRIAL", 13f, config.SecondaryTextColor, FontStyles.Bold);
        Place(heroSub.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330f, 26f));
    }

    private void BuildCharacterScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Characters, "Characters");
        RectTransform panel = CreatePremiumPanel("CharacterCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1740f, 940f));
        AddHeader(panel, "SELECT ARCHER", "CHOOSE YOUR STYLE.  YOUR SELECTION IS SAVED.");

        // Left rail: large, readable selectable hero cards.
        RectTransform leftPanel = CreatePanel("CharacterListPanel", panel, InnerPanel,
            new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.65f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-535f, -55f), new Vector2(540f, 600f));
        ATSPremiumSkin.Apply(leftPanel.GetComponent<Image>(), "panel_inner", new Vector4(30f, 30f, 30f, 30f));

        RectTransform listViewport = CreateRect("ListViewport", leftPanel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero);
        listViewport.gameObject.AddComponent<RectMask2D>();
        ScrollRect listScroll = leftPanel.gameObject.AddComponent<ScrollRect>();
        listScroll.viewport = listViewport;
        listScroll.horizontal = false;
        listScroll.vertical = true;
        listScroll.movementType = ScrollRect.MovementType.Clamped;
        listScroll.scrollSensitivity = 34f;

        RectTransform list = CreateRect("CharacterList", listViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        list.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 10, 10);
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
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 178f));
                ATSPremiumSkin.Apply(card.GetComponent<Image>(), "character_card", new Vector4(30f, 30f, 30f, 30f));
                LayoutElement cardLayout = card.gameObject.AddComponent<LayoutElement>();
                cardLayout.preferredHeight = 178f;
                cardLayout.minHeight = 178f;

                Image emblemPlate = CreateImage("EmblemPlate", card, new Color(0.02f, 0.07f, 0.14f, 0.88f),
                    new Vector2(0.055f, 0.16f), new Vector2(0.285f, 0.84f), Vector2.zero, Vector2.zero);
                ATSPremiumSkin.Apply(emblemPlate, "panel_inner", new Vector4(22f, 22f, 22f, 22f));
                string emblem = profile.CharacterId.Equals("khaem", System.StringComparison.OrdinalIgnoreCase) ? "icon_helmet" :
                    (profile.CharacterId.Equals("nerissa", System.StringComparison.OrdinalIgnoreCase) ? "icon_ranger" : "icon_characters");
                Image emblemImage = CreateImage("Emblem", emblemPlate.transform, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78f, 78f));
                ATSPremiumSkin.Apply(emblemImage, emblem, Vector4.zero, false);
                emblemImage.raycastTarget = false;

                TMP_Text name = CreateText("Name", card, profile.DisplayName.ToUpperInvariant(), 29f, config.PrimaryTextColor, FontStyles.Bold);
                name.alignment = TextAlignmentOptions.Left;
                Place(name.rectTransform, new Vector2(0.33f, 0.62f), new Vector2(0.94f, 0.84f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                TMP_Text role = CreateText("Role", card, GetRole(profile), 16f, config.SecondaryTextColor, FontStyles.Bold);
                role.alignment = TextAlignmentOptions.Left;
                Place(role.rectTransform, new Vector2(0.33f, 0.40f), new Vector2(0.94f, 0.60f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                TMP_Text badge = CreateText("Badge", card, "SELECT", 16f, config.LimeColor, FontStyles.Bold);
                badge.alignment = TextAlignmentOptions.Left;
                Place(badge.rectTransform, new Vector2(0.33f, 0.13f), new Vector2(0.94f, 0.34f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

                Button button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                card.gameObject.AddComponent<ATSButtonMotion>();
                Archer3DRuntimeProfile captured = profile;
                button.onClick.AddListener(() => SelectCharacter(captured));
                characterCards.Add(new CharacterCard { Profile = profile, Button = button, Background = card.GetComponent<Image>(), Border = card.GetComponent<Outline>(), Badge = badge });
            }
        }

        // Right hero stage: intentionally fixed, no layout groups, so it matches the authored composition at all 16:9 sizes.
        RectTransform previewFrame = CreatePanel("PreviewFrame", panel, InnerPanel,
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.55f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(315f, -55f), new Vector2(1020f, 600f));
        ATSPremiumSkin.Apply(previewFrame.GetComponent<Image>(), "panel_inner", new Vector4(30f, 30f, 30f, 30f));

        Image pedestal = CreateImage("PreviewPedestal", previewFrame, Color.white,
            new Vector2(0.34f, 0.19f), new Vector2(0.34f, 0.19f), Vector2.zero, new Vector2(510f, 155f));
        ATSPremiumSkin.Apply(pedestal, "preview_pedestal", Vector4.zero, false);
        pedestal.raycastTarget = false;

        characterPreviewImage = CreateRawImage("CharacterPreview", previewFrame,
            new Vector2(0.04f, 0.07f), new Vector2(0.66f, 0.96f), Vector2.zero, Vector2.zero);
        characterPreviewImage.color = Color.white;

        RectTransform info = CreateRect("PreviewInfo", previewFrame, new Vector2(0.66f, 0.11f), new Vector2(0.96f, 0.91f), Vector2.zero, Vector2.zero);
        TMP_Text label = CreateText("PreviewLabel", info, "ACTIVE ARCHER", 14f, config.YellowColor, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Left;
        Place(label.rectTransform, new Vector2(0f, 0.82f), new Vector2(1f, 0.94f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreviewName = CreateText("PreviewName", info, "", 44f, config.PrimaryTextColor, FontStyles.Bold);
        characterPreviewName.alignment = TextAlignmentOptions.Left;
        characterPreviewName.enableWordWrapping = true;
        Place(characterPreviewName.rectTransform, new Vector2(0f, 0.61f), new Vector2(1f, 0.82f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreviewRole = CreateText("PreviewRole", info, "", 21f, config.SecondaryTextColor, FontStyles.Bold);
        characterPreviewRole.alignment = TextAlignmentOptions.Left;
        Place(characterPreviewRole.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.61f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        Image infoLine = CreateImage("InfoLine", info, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.62f),
            new Vector2(0f, 0.43f), new Vector2(0.86f, 0.43f), Vector2.zero, new Vector2(0f, 2f), new Vector2(0f, 0.5f));
        infoLine.raycastTarget = false;

        TMP_Text note = CreateText("PreviewNote", info, "Your archer is remembered\nbetween sessions.", 17f, config.SecondaryTextColor, FontStyles.Normal);
        note.alignment = TextAlignmentOptions.Left;
        note.enableWordWrapping = true;
        Place(note.rectTransform, new Vector2(0f, 0.18f), new Vector2(1f, 0.38f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        characterPreview = gameObject.AddComponent<ATSCharacterPreview>();
        characterPreview.Initialize(characterPreviewImage);
        AddBackButton(panel, ShowMainMenu);
    }

    private void BuildLevelScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Levels, "Levels");
        RectTransform root = CreateRect("CampaignRoot", group.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // V6: full-screen campaign world. The authored map contains environment only.
        // All mutable UI (route, nodes, stars, locks, top bar, detail panel, chapter rail)
        // is rendered exactly once by Unity on top of it.

        // ---------- WORLD MAP ----------
        Image world = CreateImage("CampaignWorld", root, Color.white,
            new Vector2(0.010f, 0.165f), new Vector2(0.748f, 0.905f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(world, "campaign_world_v6", Vector4.zero, false);
        world.preserveAspect = false;
        world.raycastTarget = false;

        Image worldShade = CreateImage("CampaignWorldShade", root,
            new Color(0.005f, 0.012f, 0.025f, 0.10f),
            new Vector2(0.010f, 0.165f), new Vector2(0.748f, 0.905f), Vector2.zero, Vector2.zero);
        worldShade.raycastTarget = false;

        // One coordinate system for route + nodes. Nothing in this layer overlaps the inspector.
        campaignViewport = CreateRect("CampaignMapArea", root,
            new Vector2(0.025f, 0.190f), new Vector2(0.730f, 0.885f), Vector2.zero, Vector2.zero);
        campaignContent = CreateRect("CampaignContent", campaignViewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        campaignScroll = null;

        // Chapter identity plate floats over the map like the reference composition.
        RectTransform chapterPlate = CreatePanel("CampaignChapterPlate", campaignContent,
            new Color(0.030f, 0.028f, 0.025f, 0.84f),
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.78f),
            new Vector2(0.030f, 0.785f), new Vector2(0.320f, 0.955f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(chapterPlate.GetComponent<Image>(), "panel_inner", new Vector4(18f, 18f, 18f, 18f));
        TMP_Text chapterSmall = CreateText("ChapterSmall", chapterPlate, "CHAPTER 1", 18f, config.YellowColor, FontStyles.Bold);
        Place(chapterSmall.rectTransform, new Vector2(0.08f, 0.57f), new Vector2(0.92f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text chapterName = CreateText("ChapterName", chapterPlate, "ANCIENT RUINS", 31f, config.PrimaryTextColor, FontStyles.Bold);
        Place(chapterName.rectTransform, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.60f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // ---------- ROUTE ----------
        float sourceWidth = campaignData != null && campaignData.mapWidth > 1f ? campaignData.mapWidth : 1000f;
        float sourceHeight = campaignData != null && campaignData.mapHeight > 1f ? campaignData.mapHeight : 600f;
        List<Vector2> routePoints = new List<Vector2>();
        foreach (LevelData level in levels)
        {
            CampaignMapNodeData routeNode = ResolveCampaignNode(level.LevelNumber);
            if (routeNode == null) continue;
            routePoints.Add(new Vector2(Mathf.Clamp01(routeNode.x / sourceWidth), 1f - Mathf.Clamp01(routeNode.y / sourceHeight)));
        }

        RectTransform routeShadowRect = CreateRect("CampaignRouteShadow", campaignContent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ATSCampaignRouteGraphic routeShadow = routeShadowRect.gameObject.AddComponent<ATSCampaignRouteGraphic>();
        routeShadow.Configure(routePoints, 15f, 22f, 12f, new Color(0.08f, 0.035f, 0.005f, 0.80f));

        RectTransform routeGoldRect = CreateRect("CampaignRouteGold", campaignContent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ATSCampaignRouteGraphic routeGold = routeGoldRect.gameObject.AddComponent<ATSCampaignRouteGraphic>();
        routeGold.Configure(routePoints, 6f, 22f, 12f, new Color(1.00f, 0.78f, 0.20f, 0.98f));

        // ---------- DYNAMIC LEVEL NODES ----------
        levelCards.Clear();
        foreach (LevelData level in levels)
        {
            CampaignMapNodeData nodeData = ResolveCampaignNode(level.LevelNumber);
            float rawX = nodeData != null ? nodeData.x : 100f + (level.LevelNumber - 1) * 90f;
            float rawY = nodeData != null ? nodeData.y : 360f;
            float nx = Mathf.Clamp01(rawX / sourceWidth);
            float ny = 1f - Mathf.Clamp01(rawY / sourceHeight);

            RectTransform nodeRoot = CreateRect("CampaignLevel_" + level.LevelNumber, campaignContent,
                new Vector2(nx, ny), new Vector2(nx, ny), Vector2.zero, Vector2.zero);
            nodeRoot.pivot = new Vector2(0.5f, 0.5f);
            nodeRoot.sizeDelta = new Vector2(148f, 160f);

            Image node = CreateImage("Node", nodeRoot, Color.white,
                new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(106f, 106f));
            ATSPremiumSkin.Apply(node, "campaign_node", Vector4.zero, false);
            Button button = node.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            node.gameObject.AddComponent<ATSButtonMotion>();
            LevelData captured = level;
            button.onClick.AddListener(() => { Click(); SelectCampaignLevel(captured, false); });

            TMP_Text number = CreateText("Number", node.transform, level.LevelNumber.ToString(), 36f, Color.white, FontStyles.Bold);
            Stretch(number.rectTransform, 8f, 8f, 8f, 8f);

            RectTransform starsRoot = CreateRect("Stars", nodeRoot,
                new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.18f), Vector2.zero, Vector2.zero);
            starsRoot.pivot = new Vector2(0.5f, 0.5f);
            starsRoot.sizeDelta = new Vector2(132f, 34f);
            List<Image> stars = new List<Image>(3);
            for (int i = 0; i < 3; i++)
            {
                Image star = CreateImage("Star" + (i + 1), starsRoot, Color.white,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f + i * 44f, 0f), new Vector2(32f, 32f));
                ATSPremiumSkin.Apply(star, "star_empty", Vector4.zero, false);
                star.raycastTarget = false;
                stars.Add(star);
            }

            Image lockOverlay = CreateImage("LockOverlay", node.transform, new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image lockIcon = CreateImage("LockIcon", lockOverlay.transform, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
            ATSPremiumSkin.Apply(lockIcon, "lock", Vector4.zero, false);
            lockIcon.raycastTarget = false;

            LevelCard entry = new LevelCard
            {
                Level = level,
                Button = button,
                Background = node,
                Border = node.GetComponent<Outline>(),
                Score = null,
                Lock = null,
                LockOverlay = lockOverlay,
                Thumbnail = null,
                NodeRoot = nodeRoot,
                Number = number
            };
            entry.Stars.AddRange(stars);
            levelCards.Add(entry);
        }

        // ---------- TOP BAR ----------
        RectTransform topBar = CreatePanel("CampaignTopBar", root,
            new Color(0.008f, 0.020f, 0.040f, 0.985f),
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.72f),
            new Vector2(0.010f, 0.905f), new Vector2(0.990f, 0.995f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(topBar.GetComponent<Image>(), "panel_inner", new Vector4(10f, 10f, 10f, 10f));

        Button back = CreateButton("CampaignBack", topBar, "BACK",
            new Vector2(0.012f, 0.16f), new Vector2(0.145f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 23f, true, "button_back");
        Image backIcon = CreateImage("BackIcon", back.transform, Color.white,
            new Vector2(0.15f, 0.5f), new Vector2(0.15f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
        ATSPremiumSkin.Apply(backIcon, "icon_back", Vector4.zero, false);
        backIcon.raycastTarget = false;
        back.onClick.AddListener(() => { Click(); ShowMainMenu(); });

        TMP_Text title = CreateText("CampaignTitle", topBar, "CAMPAIGN MAP", 48f, config.PrimaryTextColor, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.30f, 0.34f), new Vector2(0.70f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text sub = CreateText("CampaignSubtitle", topBar, "ANCIENT TRIALS  •  PRECISION ARCHERY", 16f, config.SecondaryTextColor, FontStyles.Normal);
        Place(sub.rectTransform, new Vector2(0.32f, 0.05f), new Vector2(0.68f, 0.38f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform progressPlate = CreatePanel("CampaignProgressPlate", topBar,
            new Color(0.012f, 0.028f, 0.055f, 0.96f), config.YellowColor,
            new Vector2(0.755f, 0.16f), new Vector2(0.895f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image progressStar = CreateImage("ProgressStar", progressPlate, Color.white,
            new Vector2(0.18f, 0.5f), new Vector2(0.18f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        ATSPremiumSkin.Apply(progressStar, "star_filled", Vector4.zero, false);
        progressStar.raycastTarget = false;
        campaignProgress = CreateText("CampaignProgress", progressPlate, "", 25f, config.PrimaryTextColor, FontStyles.Bold);
        Place(campaignProgress.rectTransform, new Vector2(0.32f, 0.12f), new Vector2(0.92f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        Button settings = CreateButton("CampaignSettings", topBar, "",
            new Vector2(0.925f, 0.16f), new Vector2(0.982f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 20f, true, "button_secondary");
        Image settingsIcon = CreateImage("SettingsIcon", settings.transform, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        ATSPremiumSkin.Apply(settingsIcon, "icon_settings", Vector4.zero, false);
        settingsIcon.raycastTarget = false;
        settings.onClick.AddListener(() => { Click(); ShowSettings(); });

        // ---------- LEVEL INSPECTOR ----------
        RectTransform detail = CreatePanel("CampaignDetail", root,
            new Color(0.006f, 0.018f, 0.040f, 0.985f), config.YellowColor,
            new Vector2(0.755f, 0.190f), new Vector2(0.985f, 0.885f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(detail.GetComponent<Image>(), "panel_ornate", new Vector4(24f, 24f, 24f, 24f));

        campaignDetailLevel = CreateText("DetailLevel", detail, "LEVEL 1", 34f, config.PrimaryTextColor, FontStyles.Bold);
        Place(campaignDetailLevel.rectTransform, new Vector2(0.07f, 0.855f), new Vector2(0.93f, 0.955f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        campaignDetailTitle = CreateText("DetailTitle", detail, "FIRST SHOT", 18f, config.SecondaryTextColor, FontStyles.Bold);
        Place(campaignDetailTitle.rectTransform, new Vector2(0.07f, 0.795f), new Vector2(0.93f, 0.865f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        campaignPreviewImage = CreateImage("LevelPreview", detail, Color.white,
            new Vector2(0.08f, 0.525f), new Vector2(0.92f, 0.775f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(campaignPreviewImage, "level_thumb_ruins", Vector4.zero, false);
        campaignPreviewImage.raycastTarget = false;

        RectTransform detailStarsRoot = CreateRect("DetailStars", detail,
            new Vector2(0.5f, 0.465f), new Vector2(0.5f, 0.465f), Vector2.zero, Vector2.zero);
        detailStarsRoot.pivot = new Vector2(0.5f, 0.5f);
        detailStarsRoot.sizeDelta = new Vector2(190f, 46f);
        campaignDetailStars.Clear();
        for (int i = 0; i < 3; i++)
        {
            Image star = CreateImage("Star" + (i + 1), detailStarsRoot, Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(31f + i * 64f, 0f), new Vector2(46f, 46f));
            ATSPremiumSkin.Apply(star, "star_empty", Vector4.zero, false);
            star.raycastTarget = false;
            campaignDetailStars.Add(star);
        }

        campaignDetailDescription = CreateText("DetailDescription", detail, "", 17f, config.PrimaryTextColor, FontStyles.Normal);
        campaignDetailDescription.alignment = TextAlignmentOptions.Center;
        campaignDetailDescription.enableWordWrapping = true;
        Place(campaignDetailDescription.rectTransform, new Vector2(0.09f, 0.315f), new Vector2(0.91f, 0.425f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        Image detailDivider = CreateImage("DetailDivider", detail,
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.62f),
            new Vector2(0.08f, 0.300f), new Vector2(0.92f, 0.300f), Vector2.zero, new Vector2(0f, 2f));
        detailDivider.raycastTarget = false;

        campaignDetailShots = CreateText("DetailShots", detail, "ARROWS  3", 15f, config.PrimaryTextColor, FontStyles.Bold);
        campaignDetailShots.alignment = TextAlignmentOptions.Left;
        Place(campaignDetailShots.rectTransform, new Vector2(0.09f, 0.215f), new Vector2(0.48f, 0.285f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        campaignDetailScore = CreateText("DetailScore", detail, "BEST  0", 15f, config.PrimaryTextColor, FontStyles.Bold);
        campaignDetailScore.alignment = TextAlignmentOptions.Right;
        Place(campaignDetailScore.rectTransform, new Vector2(0.52f, 0.215f), new Vector2(0.91f, 0.285f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);

        campaignDetailLock = CreateText("DetailLock", detail, "", 12f, config.YellowColor, FontStyles.Bold);
        campaignDetailLock.enableWordWrapping = true;
        Place(campaignDetailLock.rectTransform, new Vector2(0.08f, 0.145f), new Vector2(0.92f, 0.205f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        campaignPlayButton = CreateButton("CampaignPlay", detail, "PLAY",
            new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.135f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            config.LimeColor, config.LimeColor, Color.white, 30f, true, "button_primary");
        campaignPlayButton.onClick.AddListener(StartSelectedCampaignLevel);

        // ---------- CHAPTER RAIL ----------
        RectTransform chapterRail = CreatePanel("CampaignChapterRail", root,
            new Color(0.006f, 0.015f, 0.032f, 0.99f),
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.48f),
            new Vector2(0.010f, 0.018f), new Vector2(0.990f, 0.155f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(chapterRail.GetComponent<Image>(), "panel_inner", new Vector4(12f, 12f, 12f, 12f));
        CreateCampaignChapterTab(chapterRail, 0, "CHAPTER 1", "ANCIENT RUINS", true);
        CreateCampaignChapterTab(chapterRail, 1, "CHAPTER 2", "MYSTIC FOREST", false);
        CreateCampaignChapterTab(chapterRail, 2, "CHAPTER 3", "FROZEN PEAKS", false);
        CreateCampaignChapterTab(chapterRail, 3, "CHAPTER 4", "DRAGON'S LAIR", false);

        EnsureCampaignSelection();
        UpdateCampaignDetail();
    }

    private void CreateCampaignChapterTab(RectTransform parent, int index, string heading, string subtitle, bool active)
    {
        const float left = 0.018f;
        const float gap = 0.012f;
        const float width = 0.232f;
        float x0 = left + index * (width + gap);
        float x1 = x0 + width;

        RectTransform tab = CreatePanel("ChapterTab" + (index + 1), parent,
            active ? new Color(0.065f, 0.050f, 0.025f, 0.98f) : new Color(0.012f, 0.028f, 0.055f, 0.96f),
            active ? config.YellowColor : new Color(0.30f, 0.38f, 0.50f, 0.72f),
            new Vector2(x0, 0.16f), new Vector2(x1, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(tab.GetComponent<Image>(), "panel_inner", new Vector4(18f, 18f, 18f, 18f));

        if (index == 0)
        {
            Image thumb = CreateImage("ChapterThumb", tab, Color.white,
                new Vector2(0.02f, 0.08f), new Vector2(0.35f, 0.92f), Vector2.zero, Vector2.zero);
            ATSPremiumSkin.Apply(thumb, "level_thumb_ruins", Vector4.zero, false);
            thumb.raycastTarget = false;
        }
        else
        {
            Image lockIcon = CreateImage("ChapterLock", tab, Color.white,
                new Vector2(0.15f, 0.5f), new Vector2(0.15f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            ATSPremiumSkin.Apply(lockIcon, "lock", Vector4.zero, false);
            lockIcon.raycastTarget = false;
        }

        TMP_Text h = CreateText("Heading", tab, heading, 18f, active ? config.PrimaryTextColor : new Color(0.80f, 0.82f, 0.86f, 1f), FontStyles.Bold);
        h.alignment = TextAlignmentOptions.Left;
        Place(h.rectTransform, new Vector2(0.39f, 0.48f), new Vector2(0.94f, 0.82f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text st = CreateText("Subtitle", tab, subtitle, 14f, active ? config.PrimaryTextColor : config.SecondaryTextColor, FontStyles.Normal);
        st.alignment = TextAlignmentOptions.Left;
        Place(st.rectTransform, new Vector2(0.39f, 0.14f), new Vector2(0.94f, 0.50f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void BuildSettingsScreen()
    {
        CanvasGroup group = CreateScreen(ScreenId.Settings, "Settings");
        RectTransform panel = CreatePremiumPanel("SettingsCard", group.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 880f));
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

        Image performanceIcon = CreateImage("PerformanceIcon", panel, Color.white, new Vector2(0.13f, 0.22f), new Vector2(0.13f, 0.22f), Vector2.zero, new Vector2(62f, 62f));
        ATSPremiumSkin.Apply(performanceIcon, "icon_performance", Vector4.zero, false);
        performanceIcon.raycastTarget = false;
        TMP_Text performanceLabel = CreateText("PerformanceLabel", panel, "PERFORMANCE", 29f, config.PrimaryTextColor, FontStyles.Bold);
        performanceLabel.alignment = TextAlignmentOptions.Left;
        Place(performanceLabel.rectTransform, new Vector2(0.18f, 0.205f), new Vector2(0.48f, 0.255f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text performanceSub = CreateText("PerformanceSub", panel, "Smooth play or longer battery life", 16f, config.SecondaryTextColor, FontStyles.Normal);
        performanceSub.alignment = TextAlignmentOptions.Left;
        Place(performanceSub.rectTransform, new Vector2(0.18f, 0.165f), new Vector2(0.52f, 0.205f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform segmented = CreatePanel("PerformanceSelector", panel, new Color(0.018f, 0.012f, 0.050f, 0.95f),
            new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.6f),
            new Vector2(0.59f, 0.17f), new Vector2(0.88f, 0.255f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(segmented.GetComponent<Image>(), "panel_inner", new Vector4(24f, 24f, 24f, 24f));

        Button smooth = CreateButton("Smooth", segmented, "SMOOTH 60 FPS", new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 16f, false);
        Button battery = CreateButton("Battery", segmented, "BATTERY 30", new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            Color.clear, Color.clear, config.PrimaryTextColor, 16f, false);
        Image smoothFill = CreateImage("SmoothFill", smooth.transform, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        smoothFill.transform.SetAsFirstSibling(); smoothFill.raycastTarget = false;
        Image batteryFill = CreateImage("BatteryFill", battery.transform, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        batteryFill.transform.SetAsFirstSibling(); batteryFill.raycastTarget = false;
        TMP_Text smoothText = smooth.GetComponentInChildren<TMP_Text>();
        TMP_Text batteryText = battery.GetComponentInChildren<TMP_Text>();
        System.Action refreshPerformance = () =>
        {
            bool isSmooth = ATSPlayerProgress.PerformanceMode == 0;
            smoothFill.color = isSmooth ? new Color(config.LimeColor.r, config.LimeColor.g, config.LimeColor.b, 0.18f) : Color.clear;
            batteryFill.color = !isSmooth ? new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.16f) : Color.clear;
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
        RectTransform row = CreatePanel(title + "Row", panel, new Color(0.01f, 0.025f, 0.065f, 0.90f),
            new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.35f),
            new Vector2(0.10f, y - 0.055f), new Vector2(0.90f, y + 0.065f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ATSPremiumSkin.Apply(row.GetComponent<Image>(), "panel_inner", new Vector4(26f, 26f, 26f, 26f));

        string iconName = title == "MUSIC" ? "icon_music" : (title == "SOUND EFFECTS" ? "icon_sfx" : "icon_haptics");
        Image rowIcon = CreateImage(title + "Icon", row, Color.white, new Vector2(0.07f, 0.5f), new Vector2(0.07f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
        ATSPremiumSkin.Apply(rowIcon, iconName, Vector4.zero, false);
        rowIcon.raycastTarget = false;

        TMP_Text label = CreateText(title + "Label", row, title, 29f, config.PrimaryTextColor, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Left;
        Place(label.rectTransform, new Vector2(0.14f, 0.50f), new Vector2(0.55f, 0.84f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        TMP_Text sub = CreateText(title + "Sub", row, subtitle, 16f, config.SecondaryTextColor, FontStyles.Normal);
        sub.alignment = TextAlignmentOptions.Left;
        Place(sub.rectTransform, new Vector2(0.14f, 0.16f), new Vector2(0.58f, 0.49f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform switchRect = CreatePanel(title + "Switch", row, new Color(0.018f, 0.012f, 0.05f, 0.96f),
            new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.60f),
            new Vector2(0.76f, 0.24f), new Vector2(0.94f, 0.76f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Button button = switchRect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        switchRect.gameObject.AddComponent<ATSButtonMotion>();
        Image track = switchRect.GetComponent<Image>();
        ATSPremiumSkin.Apply(track, "toggle_track", new Vector4(26f, 26f, 26f, 26f));
        Image knob = CreateImage("Knob", switchRect, Color.white, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(35f, 0f), new Vector2(48f, 48f));
        ATSPremiumSkin.Apply(knob, "toggle_knob", Vector4.zero, false);
        knob.raycastTarget = false;

        System.Action refresh = () =>
        {
            bool on = getter();
            track.color = on ? new Color(0.72f, 1f, 0.74f, 1f) : Color.white;
            knob.rectTransform.anchorMin = new Vector2(on ? 1f : 0f, 0.5f);
            knob.rectTransform.anchorMax = knob.rectTransform.anchorMin;
            knob.rectTransform.anchoredPosition = new Vector2(on ? -35f : 35f, 0f);
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
        EnsureCampaignSelection();

        int latestUnlocked = 1;
        int totalStars = 0;
        foreach (LevelCard card in levelCards)
        {
            if (ATSPlayerProgress.IsLevelUnlocked(card.Level.LevelNumber))
                latestUnlocked = Mathf.Max(latestUnlocked, card.Level.LevelNumber);
            totalStars += ATSPlayerProgress.GetBestStars(card.Level.LevelNumber);
        }

        foreach (LevelCard card in levelCards)
        {
            bool unlocked = ATSPlayerProgress.IsLevelUnlocked(card.Level.LevelNumber);
            int stars = ATSPlayerProgress.GetBestStars(card.Level.LevelNumber);
            bool completed = stars > 0;
            bool selected = campaignSelectedLevel != null && card.Level.LevelNumber == campaignSelectedLevel.LevelNumber;
            bool boss = card.Level.LevelNumber > 0 && card.Level.LevelNumber % 10 == 0;

            // Keep locked nodes inspectable; only PLAY is disabled. This is friendlier on touch devices.
            card.Button.interactable = true;
            if (card.LockOverlay != null)
                card.LockOverlay.gameObject.SetActive(!unlocked);
            if (card.Lock != null)
                card.Lock.gameObject.SetActive(!unlocked);
            if (card.Score != null)
                card.Score.text = unlocked ? (completed ? "BEST  " + ATSPlayerProgress.GetBestScore(card.Level.LevelNumber).ToString("N0") : "NEW TRIAL") : string.Empty;

            string nodeSkin;
            if (boss) nodeSkin = "campaign_node_boss";
            else if (!unlocked) nodeSkin = "campaign_node_locked";
            else if (selected) nodeSkin = "campaign_node_selected";
            else if (completed) nodeSkin = "campaign_node_completed";
            else nodeSkin = "campaign_node";
            ATSPremiumSkin.Apply(card.Background, nodeSkin, Vector4.zero, false);

            if (card.Number != null)
            {
                card.Number.text = unlocked || boss ? card.Level.LevelNumber.ToString() : string.Empty;
                card.Number.color = unlocked ? Color.white : new Color(0.78f, 0.68f, 0.46f, 0.92f);
            }

            for (int i = 0; i < card.Stars.Count; i++)
            {
                bool filled = i < stars;
                card.Stars[i].gameObject.SetActive(unlocked);
                ATSPremiumSkin.Apply(card.Stars[i], filled ? "star_filled" : "star_empty", Vector4.zero, false);
                card.Stars[i].color = Color.white;
            }
        }

        if (campaignProgress != null)
            campaignProgress.text = totalStars + " / " + (levels.Length * 3);

        UpdateCampaignDetail();
    }

    private CampaignMapData LoadCampaignMapData()
    {
        TextAsset json = Resources.Load<TextAsset>("UI/Campaign/CampaignMapData");
        if (json != null && !string.IsNullOrWhiteSpace(json.text))
        {
            try
            {
                CampaignMapData parsed = JsonUtility.FromJson<CampaignMapData>(json.text);
                if (parsed != null)
                    return parsed;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Campaign Map] Could not parse CampaignMapData.json: " + ex.Message);
            }
        }
        return new CampaignMapData();
    }

    private CampaignMapNodeData ResolveCampaignNode(int levelNumber)
    {
        if (campaignData != null && campaignData.nodes != null)
        {
            for (int i = 0; i < campaignData.nodes.Length; i++)
            {
                CampaignMapNodeData node = campaignData.nodes[i];
                if (node != null && node.level == levelNumber)
                    return node;
            }
        }

        // Future levels remain data-compatible even before an artist positions them.
        // They are clamped into the current chapter map until a new chapter asset/data is added.
        int extra = Mathf.Max(0, levelNumber - 10);
        return new CampaignMapNodeData
        {
            level = levelNumber,
            x = 940f,
            y = Mathf.Clamp(420f + Mathf.Sin(extra * 1.45f) * 70f, 120f, 520f),
            title = "TRIAL " + levelNumber,
            subtitle = "A new challenge awaits."
        };
    }

    private void EnsureCampaignSelection()
    {
        if (levels == null || levels.Length == 0)
        {
            campaignSelectedLevel = null;
            return;
        }

        if (campaignSelectedLevel != null && levels.Contains(campaignSelectedLevel))
            return;

        int preferred = Mathf.Clamp(ATSPlayerProgress.LastPlayedLevel, 1, levels[levels.Length - 1].LevelNumber);
        campaignSelectedLevel = levels.FirstOrDefault(x => x.LevelNumber == preferred);
        if (campaignSelectedLevel == null || !ATSPlayerProgress.IsLevelUnlocked(campaignSelectedLevel.LevelNumber))
        {
            int highest = ATSPlayerProgress.HighestUnlockedLevel;
            campaignSelectedLevel = levels.Where(x => x.LevelNumber <= highest).OrderByDescending(x => x.LevelNumber).FirstOrDefault() ?? levels[0];
        }
    }

    private void SelectCampaignLevel(LevelData level, bool centerOnNode)
    {
        if (level == null)
            return;
        campaignSelectedLevel = level;
        RefreshLevelCards();
        if (centerOnNode)
            StartCoroutine(ScrollCampaignToSelection(true));
    }

    private void UpdateCampaignDetail()
    {
        if (campaignDetailLevel == null || campaignSelectedLevel == null)
            return;

        int levelNumber = campaignSelectedLevel.LevelNumber;
        bool unlocked = ATSPlayerProgress.IsLevelUnlocked(levelNumber);
        int stars = ATSPlayerProgress.GetBestStars(levelNumber);
        CampaignMapNodeData data = ResolveCampaignNode(levelNumber);

        campaignDetailLevel.text = "LEVEL " + levelNumber;
        campaignDetailTitle.text = data != null && !string.IsNullOrWhiteSpace(data.title) ? data.title : "TRIAL " + levelNumber;
        campaignDetailDescription.text = data != null && !string.IsNullOrWhiteSpace(data.subtitle) ? data.subtitle : DescribeLevel(campaignSelectedLevel);
        campaignDetailShots.text = "ARROWS  " + campaignSelectedLevel.MaxShots;
        campaignDetailScore.text = "BEST  " + ATSPlayerProgress.GetBestScore(levelNumber).ToString("N0");
        campaignDetailLock.text = unlocked ? string.Empty : "COMPLETE THE PREVIOUS TRIAL TO UNLOCK";

        for (int i = 0; i < campaignDetailStars.Count; i++)
        {
            ATSPremiumSkin.Apply(campaignDetailStars[i], i < stars ? "star_filled" : "star_empty", Vector4.zero, false);
            campaignDetailStars[i].color = Color.white;
        }

        if (campaignPlayButton != null)
        {
            campaignPlayButton.interactable = unlocked;
            TMP_Text label = campaignPlayButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = unlocked ? "PLAY" : "LOCKED";
        }

        if (campaignPreviewImage != null)
        {
            float tint = 0.88f + (levelNumber % 3) * 0.04f;
            campaignPreviewImage.color = unlocked ? new Color(tint, tint * 0.97f, tint * 0.90f, 1f) : new Color(0.42f, 0.44f, 0.48f, 0.82f);
        }
    }

    private static string DescribeLevel(LevelData level)
    {
        if (level == null || level.Objects == null)
            return "Reach the target.";
        int mirrors = level.Objects.Count(x => x != null && x.Type == LevelData.ObjectType.Mirror);
        int walls = level.Objects.Count(x => x != null && x.Type == LevelData.ObjectType.Wall);
        if (mirrors > 0 && walls > 0)
            return "Use mirrors and obstacles to find the winning angle.";
        if (mirrors > 0)
            return "Use reflection to reach the target.";
        if (walls > 0)
            return "Find a clean path around the ruins.";
        return "Hit the target with precision.";
    }

    private void StartSelectedCampaignLevel()
    {
        if (campaignSelectedLevel == null || !ATSPlayerProgress.IsLevelUnlocked(campaignSelectedLevel.LevelNumber))
            return;
        Click();
        levelManager.StartLevelByNumber(campaignSelectedLevel.LevelNumber);
    }

    private void NudgeCampaignScroll(float amount)
    {
        if (campaignScroll == null)
            return;
        campaignScroll.StopMovement();
        campaignScroll.horizontalNormalizedPosition = Mathf.Clamp01(campaignScroll.horizontalNormalizedPosition + amount);
    }

    private IEnumerator ScrollCampaignToSelection(bool animate)
    {
        yield return null;
        if (campaignScroll == null || campaignViewport == null || campaignContent == null || campaignSelectedLevel == null)
            yield break;
        Canvas.ForceUpdateCanvases();
        CampaignMapNodeData node = ResolveCampaignNode(campaignSelectedLevel.LevelNumber);
        if (node == null)
            yield break;

        float scrollable = Mathf.Max(1f, campaignContent.rect.width - campaignViewport.rect.width);
        float target = Mathf.Clamp01((node.x - campaignViewport.rect.width * 0.50f) / scrollable);
        if (!animate)
        {
            campaignScroll.horizontalNormalizedPosition = target;
            yield break;
        }

        float start = campaignScroll.horizontalNormalizedPosition;
        float elapsed = 0f;
        const float duration = 0.32f;
        campaignScroll.StopMovement();
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1f - Mathf.Pow(1f - t, 3f);
            campaignScroll.horizontalNormalizedPosition = Mathf.Lerp(start, target, t);
            yield return null;
        }
        campaignScroll.horizontalNormalizedPosition = target;
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
            new Vector2(0.5f, 0.865f), new Vector2(0.5f, 0.865f), Vector2.zero, new Vector2(1180f, 150f));
        ATSPremiumSkin.Apply(ribbon, "header_ribbon", Vector4.zero, false);
        ribbon.raycastTarget = false;
        TMP_Text heading = CreateText("Heading", panel, title, 50f, config.PrimaryTextColor, FontStyles.Bold);
        heading.characterSpacing = 1.2f;
        Place(heading.rectTransform, new Vector2(0.5f, 0.895f), new Vector2(0.5f, 0.895f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 74f));
        TMP_Text sub = CreateText("Subheading", panel, subtitle, 17f, config.SecondaryTextColor, FontStyles.Bold);
        Place(sub.rectTransform, new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 34f));
        Image line = CreateImage("HeaderLine", panel, new Color(config.YellowColor.r, config.YellowColor.g, config.YellowColor.b, 0.52f),
            new Vector2(0.5f, 0.77f), new Vector2(0.5f, 0.77f), Vector2.zero, new Vector2(420f, 2f));
        line.raycastTarget = false;
    }

    private void CreateMenuButton(RectTransform panel, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton(label, panel, label, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(560f, 72f), new Color(0.035f, 0.018f, 0.085f, 0.96f), new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.72f), config.PrimaryTextColor, 24f, true, "button_secondary");
        string iconName = label == "CHARACTERS" ? "icon_characters" : (label == "LEVELS" ? "icon_levels" : "icon_settings");
        Image icon = CreateImage(label + "Icon", button.transform, Color.white, new Vector2(0.10f, 0.5f), new Vector2(0.10f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        ATSPremiumSkin.Apply(icon, iconName, Vector4.zero, false);
        icon.raycastTarget = false;
        button.onClick.AddListener(() => { Click(); action(); });
    }

    private void AddBackButton(RectTransform panel, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton("Back", panel, "BACK", new Vector2(0.065f, 0.055f), new Vector2(0.065f, 0.055f), new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(260f, 64f), new Color(0.030f, 0.016f, 0.075f, 0.98f), new Color(config.PanelBorderColor.r, config.PanelBorderColor.g, config.PanelBorderColor.b, 0.70f), config.PrimaryTextColor, 20f, true, "button_back");
        Image backIcon = CreateImage("BackIcon", button.transform, Color.white, new Vector2(0.13f, 0.5f), new Vector2(0.13f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
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
