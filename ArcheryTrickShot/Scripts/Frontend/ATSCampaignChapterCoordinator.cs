using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds chapter-aware campaign presentation on top of ATSFrontendController
/// without replacing its proven level/progression/gameplay flow.
///
/// Chapter ownership is deterministic: 10 levels per chapter.
///   Chapter 1 = Levels 1-10
///   Chapter 2 = Levels 11-20
///   Chapter 3 = Levels 21-30
///   Chapter 4 = Levels 31-40
///
/// Existing real level nodes remain owned by ATSFrontendController. This
/// coordinator only filters/repositions them per chapter, swaps authored map
/// artwork, reconfigures the existing route renderer, and supplies locked
/// placeholder nodes for future levels that do not exist yet.
/// </summary>
[DefaultExecutionOrder(1500)]
[DisallowMultipleComponent]
public sealed class ATSCampaignChapterCoordinator : MonoBehaviour
{
    private const int LevelsPerChapter = 10;
    private const int FirstChapter = 1;
    private const int LastChapter = 4;

    [Serializable]
    private sealed class CampaignMapData
    {
        public string chapterTitle = string.Empty;
        public string chapterSubtitle = string.Empty;
        public float mapWidth = 1000f;
        public float mapHeight = 600f;
        public CampaignMapNodeData[] nodes = Array.Empty<CampaignMapNodeData>();
    }

    [Serializable]
    private sealed class CampaignMapNodeData
    {
        public int level;
        public float x;
        public float y;
        public string title;
        public string subtitle;
    }

    private sealed class ChapterDefinition
    {
        public int Number;
        public string Name;
        public string BackgroundSkin;
        public string DataResource;
        public bool Implemented;
    }

    private static readonly ChapterDefinition[] Chapters =
    {
        new ChapterDefinition
        {
            Number = 1,
            Name = "ANCIENT RUINS",
            BackgroundSkin = "campaign_world_v6",
            DataResource = "UI/Campaign/CampaignMapData",
            Implemented = true
        },
        new ChapterDefinition
        {
            Number = 2,
            Name = "CRYSTAL CAVERNS",
            BackgroundSkin = "campaign_world2_crystal",
            DataResource = "UI/Campaign/CampaignMapData_Chapter2",
            Implemented = true
        },
        new ChapterDefinition
        {
            Number = 3,
            Name = "MYSTIC FOREST",
            BackgroundSkin = string.Empty,
            DataResource = string.Empty,
            Implemented = false
        },
        new ChapterDefinition
        {
            Number = 4,
            Name = "DRAGON'S LAIR",
            BackgroundSkin = string.Empty,
            DataResource = string.Empty,
            Implemented = false
        }
    };

    private readonly Dictionary<int, CampaignMapData> chapterData =
        new Dictionary<int, CampaignMapData>();

    private readonly Dictionary<int, int> lastSelectedByChapter =
        new Dictionary<int, int>();

    private ATSFrontendController frontend;
    private Transform campaignRoot;
    private Transform campaignContent;
    private Transform levelsScreen;
    private Image campaignWorld;
    private TMP_Text chapterSmall;
    private TMP_Text chapterName;
    private TMP_Text campaignProgress;
    private Image levelPreview;
    private ATSCampaignRouteGraphic routeShadow;
    private ATSCampaignRouteGraphic routeGold;

    private int activeChapter = 1;
    private bool bound;
    private bool wasCampaignVisible;
    private float nextBindAttempt;
    private float nextStateRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeCoordinator()
    {
        ATSCampaignChapterCoordinator existing =
            FindFirstObjectByType<ATSCampaignChapterCoordinator>();

        if (existing != null)
            return;

        GameObject host =
            new GameObject("ATSCampaignChapterCoordinator");

        DontDestroyOnLoad(host);
        host.AddComponent<ATSCampaignChapterCoordinator>();
    }

    private void Awake()
    {
        LoadChapterData();
    }

    private void Update()
    {
        if (!bound || frontend == null || campaignRoot == null)
        {
            if (Time.unscaledTime >= nextBindAttempt)
            {
                nextBindAttempt = Time.unscaledTime + 0.20f;
                TryBind();
            }
            return;
        }

        bool visible =
            levelsScreen != null &&
            levelsScreen.gameObject.activeInHierarchy;

        if (visible && !wasCampaignVisible)
        {
            int preferredChapter =
                Mathf.Clamp(
                    ChapterForLevel(ATSPlayerProgress.LastPlayedLevel),
                    FirstChapter,
                    HighestAvailableChapter());

            SwitchChapter(preferredChapter, true);
        }

        wasCampaignVisible = visible;

        if (!visible)
            return;

        if (Time.unscaledTime >= nextStateRefresh)
        {
            nextStateRefresh = Time.unscaledTime + 0.35f;
            RefreshChapterTabs();
            RefreshChapterProgress();
        }
    }

    private void LoadChapterData()
    {
        chapterData.Clear();

        for (int i = 0; i < Chapters.Length; i++)
        {
            ChapterDefinition definition = Chapters[i];

            if (!definition.Implemented ||
                string.IsNullOrWhiteSpace(definition.DataResource))
            {
                continue;
            }

            TextAsset json =
                Resources.Load<TextAsset>(definition.DataResource);

            if (json == null ||
                string.IsNullOrWhiteSpace(json.text))
            {
                Debug.LogWarning(
                    "[Campaign Chapters] Missing data for Chapter " +
                    definition.Number +
                    " at Resources/" +
                    definition.DataResource +
                    ".json");
                continue;
            }

            try
            {
                CampaignMapData parsed =
                    JsonUtility.FromJson<CampaignMapData>(json.text);

                if (parsed != null)
                {
                    chapterData[definition.Number] = parsed;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[Campaign Chapters] Could not parse Chapter " +
                    definition.Number +
                    ": " +
                    ex.Message);
            }
        }
    }

    private void TryBind()
    {
        frontend =
            FindFirstObjectByType<ATSFrontendController>();

        if (frontend == null)
            return;

        Transform root = frontend.transform;

        campaignRoot =
            FindDeepChild(root, "CampaignRoot");

        campaignContent =
            FindDeepChild(root, "CampaignContent");

        levelsScreen =
            FindDeepChild(root, "Levels");

        Transform worldTransform =
            FindDeepChild(root, "CampaignWorld");

        Transform chapterPlate =
            FindDeepChild(root, "CampaignChapterPlate");

        Transform previewTransform =
            FindDeepChild(root, "LevelPreview");

        Transform progressTransform =
            FindDeepChild(root, "CampaignProgress");

        Transform shadowTransform =
            FindDeepChild(root, "CampaignRouteShadow");

        Transform goldTransform =
            FindDeepChild(root, "CampaignRouteGold");

        if (campaignRoot == null ||
            campaignContent == null ||
            levelsScreen == null ||
            worldTransform == null ||
            chapterPlate == null)
        {
            return;
        }

        campaignWorld =
            worldTransform.GetComponent<Image>();

        chapterSmall =
            FindDirectOrDeepText(chapterPlate, "ChapterSmall");

        chapterName =
            FindDirectOrDeepText(chapterPlate, "ChapterName");

        levelPreview =
            previewTransform != null
                ? previewTransform.GetComponent<Image>()
                : null;

        campaignProgress =
            progressTransform != null
                ? progressTransform.GetComponent<TMP_Text>()
                : null;

        routeShadow =
            shadowTransform != null
                ? shadowTransform.GetComponent<ATSCampaignRouteGraphic>()
                : null;

        routeGold =
            goldTransform != null
                ? goldTransform.GetComponent<ATSCampaignRouteGraphic>()
                : null;

        BindRealLevelNodeSelectionMemory();
        EnsureFutureChapter2Placeholders();
        ConfigureChapterTabs();

        bound = true;

        int preferredChapter =
            Mathf.Clamp(
                ChapterForLevel(ATSPlayerProgress.LastPlayedLevel),
                FirstChapter,
                HighestAvailableChapter());

        SwitchChapter(preferredChapter, true);
    }

    private void BindRealLevelNodeSelectionMemory()
    {
        if (campaignContent == null)
            return;

        for (int level = 1; level <= 40; level++)
        {
            Transform nodeRoot =
                FindDirectChild(
                    campaignContent,
                    "CampaignLevel_" + level);

            if (nodeRoot == null)
                continue;

            Button button =
                nodeRoot.GetComponentInChildren<Button>(true);

            if (button == null)
                continue;

            int capturedLevel = level;
            button.onClick.AddListener(
                () =>
                {
                    int chapter = ChapterForLevel(capturedLevel);
                    lastSelectedByChapter[chapter] = capturedLevel;

                    // Frontend selection refreshes the legacy global star total.
                    // Our listener is appended after that handler, so restore the
                    // chapter-local total in the same click frame.
                    RefreshChapterProgress();
                });
        }
    }

    private void ConfigureChapterTabs()
    {
        for (int chapter = FirstChapter;
             chapter <= LastChapter;
             chapter++)
        {
            Transform tab =
                FindDeepChild(
                    campaignRoot,
                    "ChapterTab" + chapter);

            if (tab == null)
                continue;

            TMP_Text heading =
                FindDirectOrDeepText(tab, "Heading");

            TMP_Text subtitle =
                FindDirectOrDeepText(tab, "Subtitle");

            if (heading != null)
                heading.text = "CHAPTER " + chapter;

            if (subtitle != null)
                subtitle.text = Chapters[chapter - 1].Name;

            Button button =
                tab.GetComponent<Button>();

            if (button == null)
            {
                button =
                    tab.gameObject.AddComponent<Button>();

                button.transition =
                    Selectable.Transition.None;

                Image image =
                    tab.GetComponent<Image>();

                if (image != null)
                    button.targetGraphic = image;

                if (tab.GetComponent<ATSButtonMotion>() == null)
                {
                    tab.gameObject.AddComponent<ATSButtonMotion>();
                }
            }

            button.onClick.RemoveAllListeners();

            int capturedChapter = chapter;
            button.onClick.AddListener(
                () =>
                {
                    if (!IsChapterUnlocked(capturedChapter))
                        return;

                    GameAudioController.Instance?.PlayUIClick();
                    SwitchChapter(capturedChapter, true);
                });
        }

        RefreshChapterTabs();
    }

    private void RefreshChapterTabs()
    {
        if (campaignRoot == null)
            return;

        for (int chapter = FirstChapter;
             chapter <= LastChapter;
             chapter++)
        {
            Transform tab =
                FindDeepChild(
                    campaignRoot,
                    "ChapterTab" + chapter);

            if (tab == null)
                continue;

            bool unlocked =
                IsChapterUnlocked(chapter);

            bool active =
                chapter == activeChapter;

            Button button =
                tab.GetComponent<Button>();

            if (button != null)
                button.interactable = unlocked;

            Image background =
                tab.GetComponent<Image>();

            if (background != null)
            {
                background.color =
                    active
                        ? new Color(1f, 0.96f, 0.82f, 1f)
                        : unlocked
                            ? Color.white
                            : new Color(0.48f, 0.52f, 0.60f, 0.82f);
            }

            TMP_Text heading =
                FindDirectOrDeepText(tab, "Heading");

            TMP_Text subtitle =
                FindDirectOrDeepText(tab, "Subtitle");

            if (heading != null)
            {
                heading.color =
                    unlocked
                        ? Color.white
                        : new Color(0.62f, 0.65f, 0.70f, 1f);
            }

            if (subtitle != null)
            {
                subtitle.color =
                    active
                        ? Color.white
                        : unlocked
                            ? new Color(0.78f, 0.82f, 0.88f, 1f)
                            : new Color(0.48f, 0.52f, 0.58f, 1f);
            }

            UpdateChapterTabArtwork(
                tab,
                chapter,
                unlocked);
        }
    }

    private void UpdateChapterTabArtwork(
        Transform tab,
        int chapter,
        bool unlocked)
    {
        Transform lockTransform =
            FindDirectOrDeepChild(tab, "ChapterLock");

        Transform thumbTransform =
            FindDirectOrDeepChild(tab, "ChapterThumb");

        if (chapter == 1)
        {
            if (lockTransform != null)
                lockTransform.gameObject.SetActive(false);

            if (thumbTransform != null)
                thumbTransform.gameObject.SetActive(true);

            return;
        }

        if (chapter == 2 && unlocked)
        {
            if (lockTransform != null)
                lockTransform.gameObject.SetActive(false);

            Image thumb;

            if (thumbTransform == null)
            {
                GameObject go =
                    new GameObject(
                        "ChapterThumb",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image));

                go.transform.SetParent(tab, false);

                RectTransform rect =
                    go.GetComponent<RectTransform>();

                rect.anchorMin =
                    new Vector2(0.02f, 0.08f);

                rect.anchorMax =
                    new Vector2(0.35f, 0.92f);

                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                thumb = go.GetComponent<Image>();
                thumb.raycastTarget = false;
            }
            else
            {
                thumbTransform.gameObject.SetActive(true);
                thumb = thumbTransform.GetComponent<Image>();
            }

            if (thumb != null)
            {
                ATSPremiumSkin.Apply(
                    thumb,
                    "campaign_world2_crystal",
                    Vector4.zero,
                    false);

                thumb.preserveAspect = false;
            }

            return;
        }

        if (thumbTransform != null)
            thumbTransform.gameObject.SetActive(false);

        if (lockTransform != null)
            lockTransform.gameObject.SetActive(true);
    }

    private void SwitchChapter(
        int chapter,
        bool updateSelection)
    {
        chapter =
            Mathf.Clamp(
                chapter,
                FirstChapter,
                LastChapter);

        if (!IsChapterUnlocked(chapter))
            return;

        if (!chapterData.ContainsKey(chapter))
            return;

        activeChapter = chapter;

        ApplyChapterIdentity();
        ApplyChapterBackground();
        ApplyChapterRoute();
        ApplyChapterNodes();
        ApplyDetailPreviewSkin();
        RefreshChapterTabs();

        if (updateSelection)
        {
            SelectRememberedRealLevel();
        }

        // ATSFrontendController still computes a legacy all-level total whenever
        // a node selection refreshes. Re-apply the chapter-local total AFTER
        // selection so the top star plate is correct immediately.
        RefreshChapterProgress();
    }

    private void RefreshChapterProgress()
    {
        if (campaignProgress == null)
            return;

        int firstLevel =
            FirstLevelOfChapter(activeChapter);

        int lastLevel =
            LastLevelOfChapter(activeChapter);

        int earnedStars = 0;

        for (int level = firstLevel;
             level <= lastLevel;
             level++)
        {
            earnedStars +=
                ATSPlayerProgress.GetBestStars(level);
        }

        // Each chapter owns ten levels even while later LevelData assets are
        // still in production. This makes chapter mastery stable from day one:
        // Chapter 1 = /30, Chapter 2 = /30, etc.
        int maximumStars =
            LevelsPerChapter * 3;

        campaignProgress.text =
            earnedStars + " / " + maximumStars;
    }

    private void ApplyChapterIdentity()
    {
        CampaignMapData data =
            GetActiveData();

        if (chapterSmall != null)
        {
            chapterSmall.text =
                "CHAPTER " + activeChapter;
        }

        if (chapterName != null)
        {
            chapterName.text =
                data != null &&
                !string.IsNullOrWhiteSpace(data.chapterTitle)
                    ? data.chapterTitle.ToUpperInvariant()
                    : Chapters[activeChapter - 1].Name;
        }
    }

    private void ApplyChapterBackground()
    {
        if (campaignWorld == null)
            return;

        ChapterDefinition definition =
            Chapters[activeChapter - 1];

        if (string.IsNullOrWhiteSpace(definition.BackgroundSkin))
            return;

        ATSPremiumSkin.Apply(
            campaignWorld,
            definition.BackgroundSkin,
            Vector4.zero,
            false);

        campaignWorld.preserveAspect = false;
        campaignWorld.color = Color.white;
    }

    private void ApplyChapterRoute()
    {
        CampaignMapData data =
            GetActiveData();

        if (data == null ||
            data.nodes == null)
        {
            return;
        }

        float width =
            Mathf.Max(1f, data.mapWidth);

        float height =
            Mathf.Max(1f, data.mapHeight);

        List<Vector2> points =
            new List<Vector2>();

        for (int i = 0; i < data.nodes.Length; i++)
        {
            CampaignMapNodeData node =
                data.nodes[i];

            if (node == null ||
                ChapterForLevel(node.level) != activeChapter)
            {
                continue;
            }

            points.Add(
                new Vector2(
                    Mathf.Clamp01(node.x / width),
                    1f - Mathf.Clamp01(node.y / height)));
        }

        routeShadow?.Configure(
            points,
            15f,
            22f,
            12f,
            new Color(0.025f, 0.035f, 0.060f, 0.88f));

        Color routeColor =
            activeChapter == 2
                ? new Color(1.00f, 0.80f, 0.24f, 0.98f)
                : new Color(1.00f, 0.78f, 0.20f, 0.98f);

        routeGold?.Configure(
            points,
            6f,
            22f,
            12f,
            routeColor);
    }

    private void ApplyChapterNodes()
    {
        if (campaignContent == null)
            return;

        CampaignMapData data =
            GetActiveData();

        float width =
            data != null
                ? Mathf.Max(1f, data.mapWidth)
                : 1000f;

        float height =
            data != null
                ? Mathf.Max(1f, data.mapHeight)
                : 600f;

        for (int level = 1; level <= 40; level++)
        {
            Transform node =
                FindDirectChild(
                    campaignContent,
                    "CampaignLevel_" + level);

            if (node == null)
                continue;

            int chapter =
                ChapterForLevel(level);

            bool show =
                chapter == activeChapter;

            node.gameObject.SetActive(show);

            if (!show)
                continue;

            CampaignMapNodeData nodeData =
                FindNodeData(data, level);

            if (nodeData == null)
                continue;

            RectTransform rect =
                node as RectTransform;

            if (rect == null)
                continue;

            float nx =
                Mathf.Clamp01(nodeData.x / width);

            float ny =
                1f - Mathf.Clamp01(nodeData.y / height);

            rect.anchorMin =
                new Vector2(nx, ny);

            rect.anchorMax =
                new Vector2(nx, ny);

            rect.anchoredPosition =
                Vector2.zero;
        }
    }

    private void ApplyDetailPreviewSkin()
    {
        if (levelPreview == null)
            return;

        if (activeChapter == 2)
        {
            ATSPremiumSkin.Apply(
                levelPreview,
                "campaign_world2_crystal",
                Vector4.zero,
                false);

            levelPreview.preserveAspect = false;
            levelPreview.color = Color.white;
        }
        else
        {
            ATSPremiumSkin.Apply(
                levelPreview,
                "level_thumb_ruins",
                Vector4.zero,
                false);
        }
    }

    private void SelectRememberedRealLevel()
    {
        int minLevel =
            FirstLevelOfChapter(activeChapter);

        int maxLevel =
            LastLevelOfChapter(activeChapter);

        int preferred;

        if (!lastSelectedByChapter.TryGetValue(
                activeChapter,
                out preferred))
        {
            int lastPlayed =
                ATSPlayerProgress.LastPlayedLevel;

            preferred =
                lastPlayed >= minLevel &&
                lastPlayed <= maxLevel
                    ? lastPlayed
                    : minLevel;
        }

        Transform preferredNode =
            FindDirectChild(
                campaignContent,
                "CampaignLevel_" + preferred);

        Button preferredButton =
            preferredNode != null
                ? preferredNode.GetComponentInChildren<Button>(true)
                : null;

        if (preferredButton != null &&
            preferredButton.interactable)
        {
            lastSelectedByChapter[activeChapter] = preferred;
            preferredButton.onClick.Invoke();
            ApplyDetailPreviewSkin();
            return;
        }

        for (int level = minLevel;
             level <= maxLevel;
             level++)
        {
            Transform node =
                FindDirectChild(
                    campaignContent,
                    "CampaignLevel_" + level);

            Button button =
                node != null
                    ? node.GetComponentInChildren<Button>(true)
                    : null;

            if (button == null ||
                !button.interactable)
            {
                continue;
            }

            lastSelectedByChapter[activeChapter] = level;
            button.onClick.Invoke();
            ApplyDetailPreviewSkin();
            return;
        }
    }

    private void EnsureFutureChapter2Placeholders()
    {
        CampaignMapData data;

        if (!chapterData.TryGetValue(2, out data) ||
            data == null ||
            data.nodes == null ||
            campaignContent == null)
        {
            return;
        }

        for (int i = 0; i < data.nodes.Length; i++)
        {
            CampaignMapNodeData nodeData =
                data.nodes[i];

            if (nodeData == null ||
                nodeData.level <= 11 ||
                nodeData.level > 20)
            {
                continue;
            }

            Transform existing =
                FindDirectChild(
                    campaignContent,
                    "CampaignLevel_" + nodeData.level);

            if (existing != null)
                continue;

            CreateLockedPlaceholderNode(
                nodeData,
                data);
        }
    }

    private void CreateLockedPlaceholderNode(
        CampaignMapNodeData nodeData,
        CampaignMapData data)
    {
        float width =
            Mathf.Max(1f, data.mapWidth);

        float height =
            Mathf.Max(1f, data.mapHeight);

        float nx =
            Mathf.Clamp01(nodeData.x / width);

        float ny =
            1f - Mathf.Clamp01(nodeData.y / height);

        GameObject rootObject =
            new GameObject(
                "CampaignLevel_" + nodeData.level,
                typeof(RectTransform));

        rootObject.transform.SetParent(
            campaignContent,
            false);

        RectTransform nodeRoot =
            rootObject.GetComponent<RectTransform>();

        nodeRoot.anchorMin =
            new Vector2(nx, ny);

        nodeRoot.anchorMax =
            new Vector2(nx, ny);

        nodeRoot.pivot =
            new Vector2(0.5f, 0.5f);

        nodeRoot.anchoredPosition =
            Vector2.zero;

        nodeRoot.sizeDelta =
            new Vector2(148f, 160f);

        Image nodeImage =
            CreateImageChild(
                "Node",
                nodeRoot,
                new Vector2(0.5f, 0.62f),
                new Vector2(106f, 106f));

        ATSPremiumSkin.Apply(
            nodeImage,
            "campaign_node_locked",
            Vector4.zero,
            false);

        Button button =
            nodeImage.gameObject.AddComponent<Button>();

        button.transition =
            Selectable.Transition.None;

        button.interactable = false;

        TMP_Text number =
            CreateTextChild(
                "Number",
                nodeImage.transform,
                nodeData.level.ToString(),
                29f,
                new Color(0.80f, 0.70f, 0.50f, 0.90f));

        Stretch(
            number.rectTransform,
            8f,
            8f,
            8f,
            8f);

        Image lockIcon =
            CreateImageChild(
                "PlaceholderLock",
                nodeImage.transform,
                new Vector2(0.5f, 0.47f),
                new Vector2(38f, 38f));

        ATSPremiumSkin.Apply(
            lockIcon,
            "lock",
            Vector4.zero,
            false);

        lockIcon.raycastTarget = false;

        RectTransform starsRoot =
            CreateRectChild(
                "Stars",
                nodeRoot,
                new Vector2(0.5f, 0.18f),
                new Vector2(132f, 34f));

        for (int i = 0; i < 3; i++)
        {
            Image star =
                CreateImageChild(
                    "Star" + (i + 1),
                    starsRoot,
                    new Vector2(0f, 0.5f),
                    new Vector2(28f, 28f));

            star.rectTransform.anchoredPosition =
                new Vector2(22f + i * 44f, 0f);

            ATSPremiumSkin.Apply(
                star,
                "star_empty",
                Vector4.zero,
                false);

            star.color =
                new Color(1f, 1f, 1f, 0.40f);

            star.raycastTarget = false;
        }

        nodeRoot.gameObject.SetActive(false);
    }

    private CampaignMapData GetActiveData()
    {
        CampaignMapData data;

        return chapterData.TryGetValue(
                activeChapter,
                out data)
            ? data
            : null;
    }

    private bool IsChapterUnlocked(int chapter)
    {
        if (chapter <= 1)
            return true;

        if (chapter < FirstChapter ||
            chapter > LastChapter)
        {
            return false;
        }

        ChapterDefinition definition =
            Chapters[chapter - 1];

        if (!definition.Implemented ||
            !chapterData.ContainsKey(chapter))
        {
            return false;
        }

        int firstLevel =
            FirstLevelOfChapter(chapter);

        return
            ATSPlayerProgress.IsLevelUnlocked(
                firstLevel);
    }

    private int HighestAvailableChapter()
    {
        int highest = 1;

        for (int chapter = 2;
             chapter <= LastChapter;
             chapter++)
        {
            if (IsChapterUnlocked(chapter))
                highest = chapter;
        }

        return highest;
    }

    private static int ChapterForLevel(int level)
    {
        level = Mathf.Max(1, level);

        return
            ((level - 1) /
             LevelsPerChapter) +
            1;
    }

    private static int FirstLevelOfChapter(int chapter)
    {
        return
            (chapter - 1) *
            LevelsPerChapter +
            1;
    }

    private static int LastLevelOfChapter(int chapter)
    {
        return
            chapter *
            LevelsPerChapter;
    }

    private static CampaignMapNodeData FindNodeData(
        CampaignMapData data,
        int level)
    {
        if (data == null ||
            data.nodes == null)
        {
            return null;
        }

        for (int i = 0;
             i < data.nodes.Length;
             i++)
        {
            CampaignMapNodeData node =
                data.nodes[i];

            if (node != null &&
                node.level == level)
            {
                return node;
            }
        }

        return null;
    }

    private static Transform FindDeepChild(
        Transform root,
        string name)
    {
        if (root == null)
            return null;

        if (root.name == name)
            return root;

        for (int i = 0;
             i < root.childCount;
             i++)
        {
            Transform found =
                FindDeepChild(
                    root.GetChild(i),
                    name);

            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindDirectChild(
        Transform root,
        string name)
    {
        if (root == null)
            return null;

        for (int i = 0;
             i < root.childCount;
             i++)
        {
            Transform child =
                root.GetChild(i);

            if (child != null &&
                child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindDirectOrDeepChild(
        Transform root,
        string name)
    {
        Transform direct =
            FindDirectChild(root, name);

        return direct ??
               FindDeepChild(root, name);
    }

    private static TMP_Text FindDirectOrDeepText(
        Transform root,
        string name)
    {
        Transform transform =
            FindDirectOrDeepChild(root, name);

        return transform != null
            ? transform.GetComponent<TMP_Text>()
            : null;
    }

    private static RectTransform CreateRectChild(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 size)
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

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        return rect;
    }

    private static Image CreateImageChild(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 size)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        Image image =
            go.GetComponent<Image>();

        image.color = Color.white;

        return image;
    }

    private static TMP_Text CreateTextChild(
        string name,
        Transform parent,
        string value,
        float fontSize,
        Color color)
    {
        GameObject go =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));

        go.transform.SetParent(
            parent,
            false);

        TMP_Text text =
            go.GetComponent<TMP_Text>();

        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        return text;
    }

    private static void Stretch(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
