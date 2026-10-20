using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Enables the next authored campaign chapter through the existing generic
/// ATSCampaignChapterCoordinator.
///
/// Chapter 3 was intentionally compiled as a future/locked chapter in the
/// original coordinator. This adapter supplies its data/skin at runtime while
/// preserving all Chapter 1/2 behaviour. It can be removed later if the same
/// metadata is moved directly into a future coordinator refactor.
/// </summary>
[DefaultExecutionOrder(1750)]
[DisallowMultipleComponent]
public sealed class ATSCampaignChapterExtensionRuntime : MonoBehaviour
{
    private const int WildheartChapter = 3;
    private const int WildheartFirstLevel = 21;
    private const string WildheartName = "WILDHEART GROVE";
    private const string WildheartSkin = "campaign_world3_wildheart";
    private const string WildheartData =
        "UI/Campaign/CampaignMapData_Chapter3";

    private ATSCampaignChapterCoordinator coordinator;
    private bool definitionApplied;
    private bool coordinatorReloaded;
    private float nextBindAttempt;

    private static FieldInfo chaptersField;
    private static FieldInfo activeChapterField;
    private static MethodInfo loadChapterDataMethod;
    private static MethodInfo configureTabsMethod;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<
                ATSCampaignChapterExtensionRuntime>() != null)
        {
            return;
        }

        GameObject host =
            new GameObject(
                "ATSCampaignChapterExtensionRuntime");

        DontDestroyOnLoad(
            host);

        host.AddComponent<
            ATSCampaignChapterExtensionRuntime>();
    }

    private void Awake()
    {
        CacheReflection();
        ApplyChapterDefinition();
    }

    private void Update()
    {
        if (Time.unscaledTime <
            nextBindAttempt)
        {
            return;
        }

        nextBindAttempt =
            Time.unscaledTime +
            0.20f;

        if (coordinator == null)
        {
            coordinator =
                FindFirstObjectByType<
                    ATSCampaignChapterCoordinator>();
        }

        if (coordinator == null)
            return;

        if (!definitionApplied)
        {
            ApplyChapterDefinition();
        }

        if (!coordinatorReloaded)
        {
            ReloadCoordinatorData();
        }
    }

    private void LateUpdate()
    {
        if (coordinator == null)
            return;

        RefreshWildheartTabArtwork();
        RefreshWildheartPreviewArtwork();
    }

    private static void CacheReflection()
    {
        Type type =
            typeof(
                ATSCampaignChapterCoordinator);

        chaptersField ??=
            type.GetField(
                "Chapters",
                BindingFlags.Static |
                BindingFlags.NonPublic);

        activeChapterField ??=
            type.GetField(
                "activeChapter",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        loadChapterDataMethod ??=
            type.GetMethod(
                "LoadChapterData",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        configureTabsMethod ??=
            type.GetMethod(
                "ConfigureChapterTabs",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
    }

    private void ApplyChapterDefinition()
    {
        definitionApplied =
            false;

        if (chaptersField == null)
            return;

        Array chapters =
            chaptersField.GetValue(
                null) as Array;

        if (chapters == null ||
            chapters.Length <
            WildheartChapter)
        {
            return;
        }

        object definition =
            chapters.GetValue(
                WildheartChapter - 1);

        if (definition == null)
            return;

        Type definitionType =
            definition.GetType();

        SetDefinitionField(
            definitionType,
            definition,
            "Name",
            WildheartName);

        SetDefinitionField(
            definitionType,
            definition,
            "BackgroundSkin",
            WildheartSkin);

        SetDefinitionField(
            definitionType,
            definition,
            "DataResource",
            WildheartData);

        SetDefinitionField(
            definitionType,
            definition,
            "Implemented",
            true);

        definitionApplied =
            true;
    }

    private static void SetDefinitionField(
        Type definitionType,
        object definition,
        string fieldName,
        object value)
    {
        FieldInfo field =
            definitionType.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

        field?.SetValue(
            definition,
            value);
    }

    private void ReloadCoordinatorData()
    {
        if (coordinator == null ||
            loadChapterDataMethod == null)
        {
            return;
        }

        loadChapterDataMethod.Invoke(
            coordinator,
            null);

        configureTabsMethod?.Invoke(
            coordinator,
            null);

        coordinatorReloaded =
            true;
    }

    private void RefreshWildheartTabArtwork()
    {
        bool unlocked =
            ATSPlayerProgress.IsLevelUnlocked(
                WildheartFirstLevel);

        Transform tab =
            FindDeepChild(
                coordinator.transform,
                "ChapterTab3");

        if (tab == null)
            return;

        TMP_Text subtitle =
            FindDeepChild(
                tab,
                "Subtitle")?
                .GetComponent<TMP_Text>();

        if (subtitle != null)
        {
            subtitle.text =
                WildheartName;
        }

        Transform lockTransform =
            FindDeepChild(
                tab,
                "ChapterLock");

        Transform thumbTransform =
            FindDeepChild(
                tab,
                "ChapterThumb");

        if (!unlocked)
        {
            if (thumbTransform != null)
                thumbTransform.gameObject.SetActive(false);

            if (lockTransform != null)
                lockTransform.gameObject.SetActive(true);

            return;
        }

        if (lockTransform != null)
            lockTransform.gameObject.SetActive(false);

        Image thumb =
            EnsureChapterThumb(
                tab,
                thumbTransform);

        if (thumb == null)
            return;

        thumb.gameObject.SetActive(true);

        ATSPremiumSkin.Apply(
            thumb,
            WildheartSkin,
            Vector4.zero,
            false);

        thumb.preserveAspect =
            false;
    }

    private void RefreshWildheartPreviewArtwork()
    {
        if (activeChapterField == null)
            return;

        object value =
            activeChapterField.GetValue(
                coordinator);

        if (!(value is int activeChapter) ||
            activeChapter !=
            WildheartChapter)
        {
            return;
        }

        Transform previewTransform =
            FindDeepChild(
                coordinator.transform,
                "LevelPreview");

        Image preview =
            previewTransform != null
                ? previewTransform.GetComponent<Image>()
                : null;

        if (preview == null)
            return;

        ATSPremiumSkin.Apply(
            preview,
            WildheartSkin,
            Vector4.zero,
            false);

        preview.preserveAspect =
            false;

        preview.color =
            Color.white;
    }

    private static Image EnsureChapterThumb(
        Transform tab,
        Transform existing)
    {
        if (existing != null)
        {
            return existing.GetComponent<Image>();
        }

        GameObject go =
            new GameObject(
                "ChapterThumb",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

        go.transform.SetParent(
            tab,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            new Vector2(
                0.02f,
                0.08f);

        rect.anchorMax =
            new Vector2(
                0.35f,
                0.92f);

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        Image image =
            go.GetComponent<Image>();

        image.raycastTarget =
            false;

        return image;
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
}
