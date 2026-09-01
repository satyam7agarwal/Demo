using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ATSFrontendController : MonoBehaviour
{
    private enum ScreenId { Main, Characters, Levels, Settings }

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
    private readonly List<CharacterCard> characterCards = new List<CharacterCard>();
    private readonly List<LevelCard> levelCards = new List<LevelCard>();

    private sealed class CharacterCard
    {
        public Archer3DRuntimeProfile Profile;
        public Button Button;
        public Image Background;
        public TMP_Text Badge;
    }

    private sealed class LevelCard
    {
        public LevelData Level;
        public Button Button;
        public Image Background;
        public TMP_Text Stars;
        public TMP_Text Score;
        public TMP_Text Lock;
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
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;
        rootGroup.gameObject.SetActive(false);
    }

    private void Show(ScreenId id)
    {
        Time.timeScale = 1f;
        if (gameplayBow != null) gameplayBow.gameObject.SetActive(false);
        gameplayUI?.SetGameplayVisible(false);
        rootGroup.gameObject.SetActive(true);
        foreach (KeyValuePair<ScreenId, CanvasGroup> item in screens)
        {
            bool active = item.Key == id;
            item.Value.gameObject.SetActive(active);
            item.Value.alpha = active ? 1f : 0f;
            item.Value.interactable = active;
            item.Value.blocksRaycasts = active;
        }
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(FadeRootIn());
    }

    private IEnumerator FadeRootIn()
    {
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = true;
        rootGroup.alpha = 0f;
        float duration = 0.16f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            rootGroup.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        rootGroup.alpha = 1f;
        rootGroup.interactable = true;
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
        Image dim = CreateImage("Backdrop", canvasRect, new Color(0.018f, 0.012f, 0.045f, 0.50f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
        CanvasGroup group = CreateScreen("MainMenu");
        RectTransform panel = CreatePanel("MainCard", group.transform, new Color(config.PanelColor.r, config.PanelColor.g, config.PanelColor.b, 0.94f), config.PanelBorderColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420f, 0f), new Vector2(650f, 760f));

        TMP_Text title = CreateText("Title", panel, "ARCHERY\nTRICK SHOT", 64f, config.PrimaryTextColor, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 180f));
        TMP_Text subtitle = CreateText("Subtitle", panel, "MASTER THE ANGLE. OWN THE SHOT.", 21f, config.SecondaryTextColor, FontStyles.Bold);
        Place(subtitle.rectTransform, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 45f));

        selectedCharacterText = CreateText("SelectedCharacter", panel, "", 20f, config.YellowColor, FontStyles.Bold);
        Place(selectedCharacterText.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 42f));

        Button play = CreateButton("Play", panel, "PLAY", new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(470f, 88f), config.LimeColor, config.LimeColor, new Color(0.03f, 0.025f, 0.07f, 1f), 31f);
        play.onClick.AddListener(() => { Click(); int target = Mathf.Clamp(ATSPlayerProgress.LastPlayedLevel, 1, Mathf.Max(1, levels.Length)); levelManager.StartLevelByNumber(target); });

        CreateMenuButton(panel, "CHARACTERS", 0.33f, ShowCharacterSelect);
        CreateMenuButton(panel, "LEVELS", 0.21f, ShowLevelSelect);
        CreateMenuButton(panel, "SETTINGS", 0.09f, ShowSettings);
    }

    private void BuildCharacterScreen()
    {
        CanvasGroup group = CreateScreen("Characters");
        RectTransform panel = CreatePanel("CharacterCard", group.transform, new Color(config.PanelColor.r, config.PanelColor.g, config.PanelColor.b, 0.96f), config.PanelBorderColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 790f));
        AddHeader(panel, "SELECT ARCHER", "YOUR STYLE. YOUR SHOT.");

        RectTransform list = CreateRect("CharacterList", panel, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.73f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup layout = list.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 26f; layout.childAlignment = TextAnchor.MiddleCenter; layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
        layout.padding = new RectOffset(8, 8, 8, 8);

        characterCards.Clear();
        if (roster != null && roster.Profiles != null)
        {
            foreach (Archer3DRuntimeProfile profile in roster.Profiles)
            {
                if (profile == null || !profile.PlayerSelectable) continue;
                RectTransform card = CreatePanel("Character_" + profile.CharacterId, list, new Color(0.085f, 0.05f, 0.19f, 0.98f), config.PanelBorderColor,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                VerticalLayoutGroup v = card.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(24, 24, 32, 28); v.spacing = 10f; v.childAlignment = TextAnchor.MiddleCenter;
                TMP_Text icon = CreateText("Monogram", card, string.IsNullOrWhiteSpace(profile.DisplayName) ? "A" : profile.DisplayName.Substring(0, 1).ToUpperInvariant(), 82f, config.YellowColor, FontStyles.Bold);
                icon.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
                TMP_Text name = CreateText("Name", card, profile.DisplayName.ToUpperInvariant(), 31f, config.PrimaryTextColor, FontStyles.Bold);
                name.gameObject.AddComponent<LayoutElement>().preferredHeight = 55f;
                TMP_Text role = CreateText("Role", card, profile.CharacterId.Equals("khaem", System.StringComparison.OrdinalIgnoreCase) ? "WARRIOR ARCHER" : "RANGER ARCHER", 18f, config.SecondaryTextColor, FontStyles.Bold);
                role.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
                TMP_Text badge = CreateText("Badge", card, "SELECT", 20f, config.LimeColor, FontStyles.Bold);
                badge.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
                Button button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                card.gameObject.AddComponent<ATSButtonMotion>();
                Archer3DRuntimeProfile captured = profile;
                button.onClick.AddListener(() => SelectCharacter(captured));
                characterCards.Add(new CharacterCard { Profile = profile, Button = button, Background = card.GetComponent<Image>(), Badge = badge });
            }
        }
        AddBackButton(panel, ShowMainMenu);
    }

    private void BuildLevelScreen()
    {
        CanvasGroup group = CreateScreen("Levels");
        RectTransform panel = CreatePanel("LevelsCard", group.transform, new Color(config.PanelColor.r, config.PanelColor.g, config.PanelColor.b, 0.96f), config.PanelBorderColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1450f, 830f));
        AddHeader(panel, "CHOOSE YOUR CHALLENGE", "NEW LEVELS UNLOCK AS YOU PROGRESS");

        RectTransform viewport = CreateRect("LevelGrid", panel, new Vector2(0.06f, 0.16f), new Vector2(0.94f, 0.74f), Vector2.zero, Vector2.zero);
        GridLayoutGroup grid = viewport.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 180f); grid.spacing = new Vector2(20f, 18f); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4; grid.childAlignment = TextAnchor.UpperCenter;

        levelCards.Clear();
        foreach (LevelData level in levels)
        {
            RectTransform card = CreatePanel("Level_" + level.LevelNumber, viewport, new Color(0.085f, 0.05f, 0.19f, 0.98f), config.PanelBorderColor,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            TMP_Text number = CreateText("Number", card, "LEVEL " + level.LevelNumber, 27f, config.PrimaryTextColor, FontStyles.Bold);
            Place(number.rectTransform, new Vector2(0.5f, 0.76f), new Vector2(0.5f, 0.76f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 48f));
            TMP_Text stars = CreateText("Stars", card, "☆ ☆ ☆", 28f, config.YellowColor, FontStyles.Bold);
            Place(stars.rectTransform, new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 48f));
            TMP_Text score = CreateText("Score", card, "BEST  0", 17f, config.SecondaryTextColor, FontStyles.Bold);
            Place(score.rectTransform, new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 38f));
            TMP_Text lockText = CreateText("Lock", card, "LOCKED", 18f, config.PinkColor, FontStyles.Bold);
            Place(lockText.rectTransform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 30f));
            Button button = card.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None; card.gameObject.AddComponent<ATSButtonMotion>();
            LevelData captured = level; button.onClick.AddListener(() => { if (ATSPlayerProgress.IsLevelUnlocked(captured.LevelNumber)) { Click(); levelManager.StartLevelByNumber(captured.LevelNumber); } });
            levelCards.Add(new LevelCard { Level = level, Button = button, Background = card.GetComponent<Image>(), Stars = stars, Score = score, Lock = lockText });
        }
        AddBackButton(panel, ShowMainMenu);
    }

    private void BuildSettingsScreen()
    {
        CanvasGroup group = CreateScreen("Settings");
        RectTransform panel = CreatePanel("SettingsCard", group.transform, new Color(config.PanelColor.r, config.PanelColor.g, config.PanelColor.b, 0.96f), config.PanelBorderColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 760f));
        AddHeader(panel, "SETTINGS", "TUNE THE EXPERIENCE");
        CreateSettingRow(panel, "MUSIC", 0.60f, () => ATSPlayerProgress.MusicEnabled, value => { ATSPlayerProgress.MusicEnabled = value; ApplyAudioSettings(); });
        CreateSettingRow(panel, "SOUND EFFECTS", 0.48f, () => ATSPlayerProgress.SfxEnabled, value => { ATSPlayerProgress.SfxEnabled = value; ApplyAudioSettings(); });
        CreateSettingRow(panel, "HAPTICS", 0.36f, () => ATSPlayerProgress.HapticsEnabled, value => ATSPlayerProgress.HapticsEnabled = value);
        Button perf = CreateButton("Performance", panel, "PERFORMANCE: " + (ATSPlayerProgress.PerformanceMode == 0 ? "SMOOTH 60 FPS" : "BATTERY 30 FPS"),
            new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 70f), new Color(0.09f,0.055f,0.22f,1f), config.PanelBorderColor, config.PrimaryTextColor, 21f);
        TMP_Text perfText = perf.GetComponentInChildren<TMP_Text>();
        perf.onClick.AddListener(() => { Click(); ATSPlayerProgress.PerformanceMode = ATSPlayerProgress.PerformanceMode == 0 ? 1 : 0; perfText.text = "PERFORMANCE: " + (ATSPlayerProgress.PerformanceMode == 0 ? "SMOOTH 60 FPS" : "BATTERY 30 FPS"); });
        AddBackButton(panel, ShowMainMenu);
    }

    private void CreateSettingRow(RectTransform panel, string label, float y, System.Func<bool> getter, System.Action<bool> setter)
    {
        TMP_Text text = CreateText(label + "Label", panel, label, 24f, config.PrimaryTextColor, FontStyles.Bold);
        Place(text.rectTransform, new Vector2(0.30f, y), new Vector2(0.30f, y), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 60f));
        Button toggle = CreateButton(label + "Toggle", panel, getter() ? "ON" : "OFF", new Vector2(0.72f, y), new Vector2(0.72f, y), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(210f, 62f), new Color(0.09f,0.055f,0.22f,1f), getter() ? config.LimeColor : config.PanelBorderColor, getter() ? config.LimeColor : config.SecondaryTextColor, 22f);
        TMP_Text toggleText = toggle.GetComponentInChildren<TMP_Text>();
        toggle.onClick.AddListener(() => { Click(); bool next = !getter(); setter(next); toggleText.text = next ? "ON" : "OFF"; Outline outline = toggle.GetComponent<Outline>(); if (outline != null) outline.effectColor = next ? config.LimeColor : config.PanelBorderColor; toggleText.color = next ? config.LimeColor : config.SecondaryTextColor; });
    }

    private void RefreshCharacterCards()
    {
        if (roster == null) return;
        Archer3DRuntimeProfile selected = roster.ResolveSelectedProfile();
        foreach (CharacterCard card in characterCards)
        {
            bool active = card.Profile == selected;
            card.Background.color = active ? new Color(0.12f, 0.10f, 0.24f, 1f) : new Color(0.085f, 0.05f, 0.19f, 0.98f);
            card.Badge.text = active ? "✓ SELECTED" : "SELECT";
            card.Badge.color = active ? config.LimeColor : config.SecondaryTextColor;
        }
        if (selectedCharacterText != null)
            selectedCharacterText.text = selected != null ? "CURRENT ARCHER  •  " + selected.DisplayName.ToUpperInvariant() : "CURRENT ARCHER";
    }

    private void RefreshLevelCards()
    {
        foreach (LevelCard card in levelCards)
        {
            bool unlocked = ATSPlayerProgress.IsLevelUnlocked(card.Level.LevelNumber);
            int stars = ATSPlayerProgress.GetBestStars(card.Level.LevelNumber);
            card.Button.interactable = unlocked;
            card.Lock.gameObject.SetActive(!unlocked);
            card.Stars.text = stars <= 0 ? "☆ ☆ ☆" : string.Join(" ", Enumerable.Range(0, 3).Select(i => i < stars ? "★" : "☆"));
            card.Score.text = "BEST  " + ATSPlayerProgress.GetBestScore(card.Level.LevelNumber).ToString("N0");
            card.Background.color = unlocked ? new Color(0.085f, 0.05f, 0.19f, 0.98f) : new Color(0.055f, 0.045f, 0.085f, 0.86f);
        }
    }

    private void SelectCharacter(Archer3DRuntimeProfile profile)
    {
        if (roster == null || profile == null) return;
        Click(); roster.SelectCharacter(profile.CharacterId); RefreshCharacterCards();
    }

    private void ApplyAudioSettings()
    {
        GameAudioController.Instance?.SetMusicVolume(ATSPlayerProgress.MusicEnabled ? config.MusicVolume : 0f);
        GameAudioController.Instance?.SetSfxEnabled(ATSPlayerProgress.SfxEnabled);
    }

    private void AddHeader(RectTransform panel, string title, string subtitle)
    {
        TMP_Text heading = CreateText("Heading", panel, title, 45f, config.PrimaryTextColor, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120f, 80f));
        TMP_Text sub = CreateText("Subheading", panel, subtitle, 19f, config.SecondaryTextColor, FontStyles.Bold);
        Place(sub.rectTransform, new Vector2(0.5f, 0.79f), new Vector2(0.5f, 0.79f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120f, 44f));
    }

    private void CreateMenuButton(RectTransform panel, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton(label, panel, label, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(470f, 70f), new Color(0.085f, 0.05f, 0.19f, 0.98f), config.PanelBorderColor, config.PrimaryTextColor, 24f);
        button.onClick.AddListener(() => { Click(); action(); });
    }

    private void AddBackButton(RectTransform panel, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateButton("Back", panel, "<  BACK", new Vector2(0.12f, 0.07f), new Vector2(0.12f, 0.07f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(220f, 58f), new Color(0.075f, 0.045f, 0.17f, 1f), config.PanelBorderColor, config.SecondaryTextColor, 20f);
        button.onClick.AddListener(() => { Click(); action(); });
    }

    private CanvasGroup CreateScreen(string name)
    {
        RectTransform rect = CreateRect(name, contentRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        screens[(ScreenId)screens.Count] = group;
        return group;
    }

    private static void Click() => GameAudioController.Instance?.PlayUIClick();

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; return rect;
    }

    private static RectTransform CreatePanel(string name, Transform parent, Color fill, Color border, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Image image = CreateImage(name, parent, fill, anchorMin, anchorMax, position, size, pivot);
        Outline outline = image.gameObject.AddComponent<Outline>(); outline.effectColor = border; outline.effectDistance = new Vector2(2f, -2f); outline.useGraphicAlpha = true;
        return image.rectTransform;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size, Color fill, Color border, Color textColor, float fontSize)
    {
        RectTransform rect = CreatePanel(name, parent, fill, border, anchorMin, anchorMax, pivot, position, size);
        Button button = rect.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.ColorTint; button.gameObject.AddComponent<ATSButtonMotion>();
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(1f,1f,1f,0.92f); colors.pressedColor = new Color(0.82f,0.82f,0.82f,1f); colors.fadeDuration = 0.07f; button.colors = colors;
        TMP_Text text = CreateText("Text", rect, label, fontSize, textColor, FontStyles.Bold); Stretch(text.rectTransform, 14f, 8f, 14f, 8f); return button;
    }

    private static Image CreateImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); RectTransform rect = go.GetComponent<RectTransform>(); Place(rect, anchorMin, anchorMax, pivot ?? new Vector2(0.5f,0.5f), position, size); Image image = go.GetComponent<Image>(); image.color = color; return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float fontSize, Color color, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); TMP_Text text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = fontSize; text.fontStyle = style; text.color = color; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.enableWordWrapping = false; return text;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    { rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one; }
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left,bottom); rect.offsetMax = new Vector2(-right,-top); }
}
