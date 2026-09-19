using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds Golden Medallion mastery state to the existing runtime campaign UI
/// without duplicating or replacing ATSFrontendController.
/// </summary>
public sealed class ATSMedallionCampaignDecorator : MonoBehaviour
{
    private const string DetailSuffix =
        "\nGOLDEN MEDALLION";

    private LevelData[] levels;
    private Sprite medallionSprite;
    private float nextRefreshTime;

    public static ATSMedallionCampaignDecorator Ensure(
        GameObject host)
    {
        if (host == null)
            return null;

        ATSMedallionCampaignDecorator existing =
            host.GetComponent<
                ATSMedallionCampaignDecorator>();

        if (existing != null)
            return existing;

        return host.AddComponent<
            ATSMedallionCampaignDecorator>();
    }

    private void Awake()
    {
        LoadData();
    }

    private void Update()
    {
        if (Time.unscaledTime <
            nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.unscaledTime +
            0.30f;

        RefreshNow();
    }

    public void RefreshNow()
    {
        LoadData();
        RefreshNodeBadges();
        RefreshSelectedLevelDetail();
    }

    private void LoadData()
    {
        if (levels == null ||
            levels.Length == 0)
        {
            levels =
                Resources
                    .LoadAll<LevelData>(
                        "Levels")
                    .Where(
                        level =>
                            level != null)
                    .OrderBy(
                        level =>
                            level.LevelNumber)
                    .ToArray();
        }

        medallionSprite ??=
            Resources.Load<Sprite>(
                "Art/Collectibles/GoldenMedallion");
    }

    private void RefreshNodeBadges()
    {
        if (levels == null)
            return;

        Transform[] transforms =
            GetComponentsInChildren<
                Transform>(true);

        foreach (LevelData level in levels)
        {
            List<string> ids =
                GetMedallionIds(
                    level);

            if (ids.Count == 0)
                continue;

            Transform node =
                FindByName(
                    transforms,
                    $"CampaignLevel_{level.LevelNumber}");

            if (node == null)
                continue;

            Image badge =
                EnsureNodeBadge(
                    node);

            if (badge == null)
                continue;

            int collected =
                CountCollected(
                    ids);

            bool complete =
                collected >=
                ids.Count;

            badge.color =
                complete
                    ? Color.white
                    : new Color(
                        0.74f,
                        0.74f,
                        0.76f,
                        0.34f);
        }
    }

    private Image EnsureNodeBadge(
        Transform node)
    {
        Transform existing =
            node.Find(
                "MedallionBadge");

        Image image =
            existing != null
                ? existing.GetComponent<Image>()
                : null;

        if (image == null)
        {
            GameObject badge =
                new GameObject(
                    "MedallionBadge",
                    typeof(RectTransform),
                    typeof(Image));

            badge.transform.SetParent(
                node,
                false);

            RectTransform rect =
                badge.GetComponent<RectTransform>();

            rect.anchorMin =
                new Vector2(
                    0.82f,
                    0.77f);

            rect.anchorMax =
                rect.anchorMin;

            rect.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            rect.anchoredPosition =
                Vector2.zero;

            rect.sizeDelta =
                new Vector2(
                    38f,
                    38f);

            image =
                badge.GetComponent<Image>();

            image.raycastTarget =
                false;

            image.preserveAspect =
                true;
        }

        if (medallionSprite != null)
            image.sprite =
                medallionSprite;

        image.transform.SetAsLastSibling();

        return image;
    }

    private void RefreshSelectedLevelDetail()
    {
        TMP_Text[] texts =
            GetComponentsInChildren<
                TMP_Text>(true);

        TMP_Text levelText =
            texts.FirstOrDefault(
                text =>
                    text != null &&
                    text.name ==
                    "DetailLevel");

        TMP_Text description =
            texts.FirstOrDefault(
                text =>
                    text != null &&
                    text.name ==
                    "DetailDescription");

        if (levelText == null ||
            description == null)
        {
            return;
        }

        int levelNumber =
            ParseLevelNumber(
                levelText.text);

        LevelData selected =
            levels != null
                ? levels.FirstOrDefault(
                    level =>
                        level != null &&
                        level.LevelNumber ==
                        levelNumber)
                : null;

        string baseText =
            StripMedallionSuffix(
                description.text);

        if (selected == null)
        {
            description.text =
                baseText;
            return;
        }

        List<string> ids =
            GetMedallionIds(
                selected);

        if (ids.Count == 0)
        {
            description.text =
                baseText;
            return;
        }

        int collected =
            CountCollected(
                ids);

        string label =
            ids.Count == 1
                ? "GOLDEN MEDALLION"
                : "GOLDEN MEDALLIONS";

        description.text =
            baseText +
            $"\n{label}  {collected} / {ids.Count}";
    }

    private static List<string> GetMedallionIds(
        LevelData level)
    {
        List<string> result =
            new List<string>();

        if (level == null ||
            level.Objects == null)
        {
            return result;
        }

        for (int i = 0;
             i < level.Objects.Length;
             i++)
        {
            LevelData.LevelObjectData data =
                level.Objects[i];

            if (data == null ||
                data.Type !=
                    LevelData.ObjectType.Collectible ||
                data.CollectibleStyle !=
                    LevelData.CollectibleStyle.GoldenMedallion ||
                string.IsNullOrWhiteSpace(
                    data.CollectibleId))
            {
                continue;
            }

            string id =
                data.CollectibleId.Trim();

            if (!result.Contains(id))
                result.Add(id);
        }

        return result;
    }

    private static int CountCollected(
        List<string> ids)
    {
        int count = 0;

        for (int i = 0;
             i < ids.Count;
             i++)
        {
            if (ATSPlayerProgress
                    .IsCollectibleCollected(
                        ids[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static Transform FindByName(
        Transform[] transforms,
        string objectName)
    {
        for (int i = 0;
             i < transforms.Length;
             i++)
        {
            Transform candidate =
                transforms[i];

            if (candidate != null &&
                candidate.name ==
                objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static int ParseLevelNumber(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return -1;
        }

        string[] parts =
            value.Trim()
                .Split(' ');

        for (int i =
                 parts.Length - 1;
             i >= 0;
             i--)
        {
            if (int.TryParse(
                    parts[i],
                    out int result))
            {
                return result;
            }
        }

        return -1;
    }

    private static string StripMedallionSuffix(
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        int index =
            value.IndexOf(
                DetailSuffix,
                System.StringComparison.Ordinal);

        return index >= 0
            ? value.Substring(
                0,
                index)
            : value;
    }
}
