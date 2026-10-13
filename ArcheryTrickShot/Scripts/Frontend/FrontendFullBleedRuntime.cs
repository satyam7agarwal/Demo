using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Separates full-bleed campaign decoration from safe-area UI.
///
/// ATSFrontendController correctly keeps interactive UI inside Screen.safeArea.
/// On very wide/notched phones that means the physical pixels outside the safe
/// area would otherwise reveal the scene behind the frontend. This runtime
/// mirrors the active CampaignWorld artwork into a non-interactive image under
/// the safe-area content, covering the complete physical display.
///
/// Buttons, map nodes, top bar, detail panel and chapter controls remain safely
/// inset. Only decorative campaign artwork becomes full bleed.
/// </summary>
[DefaultExecutionOrder(1750)]
[DisallowMultipleComponent]
public sealed class FrontendFullBleedRuntime : MonoBehaviour
{
    private ATSFrontendController frontend;
    private RectTransform backdrop;
    private RectTransform levelsScreen;
    private Image campaignWorld;

    private RectTransform fullBleedRect;
    private Image fullBleedImage;

    private Sprite lastSprite;
    private Vector2 lastParentSize;

    private float nextBindAttempt;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        FrontendFullBleedRuntime existing =
            UnityEngine.Object.FindFirstObjectByType<
                FrontendFullBleedRuntime>();

        if (existing != null)
            return;

        GameObject host =
            new GameObject(
                "FrontendFullBleedRuntime");

        DontDestroyOnLoad(host);

        host.AddComponent<
            FrontendFullBleedRuntime>();
    }

    private void LateUpdate()
    {
        if (!IsBound())
        {
            if (Time.unscaledTime <
                nextBindAttempt)
            {
                return;
            }

            nextBindAttempt =
                Time.unscaledTime +
                0.20f;

            TryBind();

            if (!IsBound())
                return;
        }

        bool campaignVisible =
            levelsScreen != null &&
            levelsScreen.gameObject.activeInHierarchy;

        if (fullBleedRect != null &&
            fullBleedRect.gameObject.activeSelf !=
                campaignVisible)
        {
            fullBleedRect.gameObject.SetActive(
                campaignVisible);
        }

        if (!campaignVisible ||
            campaignWorld == null ||
            fullBleedImage == null ||
            backdrop == null)
        {
            return;
        }

        Sprite currentSprite =
            campaignWorld.sprite;

        Vector2 parentSize =
            backdrop.rect.size;

        bool spriteChanged =
            currentSprite !=
            lastSprite;

        bool sizeChanged =
            (parentSize -
             lastParentSize).sqrMagnitude >
            0.25f;

        if (!spriteChanged &&
            !sizeChanged)
        {
            return;
        }

        lastSprite =
            currentSprite;

        lastParentSize =
            parentSize;

        fullBleedImage.sprite =
            currentSprite;

        fullBleedImage.color =
            Color.white;

        FitCover(
            fullBleedRect,
            backdrop,
            currentSprite);
    }

    private bool IsBound()
    {
        return
            frontend != null &&
            backdrop != null &&
            levelsScreen != null &&
            campaignWorld != null &&
            fullBleedRect != null &&
            fullBleedImage != null;
    }

    private void TryBind()
    {
        frontend =
            UnityEngine.Object.FindFirstObjectByType<
                ATSFrontendController>();

        if (frontend == null)
            return;

        Transform root =
            frontend.transform;

        Transform backdropTransform =
            FindDeepChild(
                root,
                "Backdrop");

        Transform levelsTransform =
            FindDeepChild(
                root,
                "Levels");

        Transform campaignWorldTransform =
            FindDeepChild(
                root,
                "CampaignWorld");

        if (backdropTransform == null ||
            levelsTransform == null ||
            campaignWorldTransform == null)
        {
            return;
        }

        backdrop =
            backdropTransform as RectTransform;

        levelsScreen =
            levelsTransform as RectTransform;

        campaignWorld =
            campaignWorldTransform.GetComponent<
                Image>();

        if (backdrop == null ||
            levelsScreen == null ||
            campaignWorld == null)
        {
            return;
        }

        Transform existing =
            FindDirectChild(
                backdrop,
                "CampaignFullBleed");

        GameObject go;

        if (existing != null)
        {
            go =
                existing.gameObject;
        }
        else
        {
            go =
                new GameObject(
                    "CampaignFullBleed",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));

            go.transform.SetParent(
                backdrop,
                false);

            // Decorative fill must render behind SafeArea Content.
            go.transform.SetSiblingIndex(
                0);
        }

        fullBleedRect =
            go.GetComponent<
                RectTransform>();

        fullBleedImage =
            go.GetComponent<
                Image>();

        fullBleedImage.raycastTarget =
            false;

        fullBleedImage.preserveAspect =
            false;

        fullBleedImage.type =
            Image.Type.Simple;

        fullBleedRect.anchorMin =
            new Vector2(
                0.5f,
                0.5f);

        fullBleedRect.anchorMax =
            new Vector2(
                0.5f,
                0.5f);

        fullBleedRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        fullBleedRect.anchoredPosition =
            Vector2.zero;

        lastSprite =
            null;

        lastParentSize =
            Vector2.zero;
    }

    private static void FitCover(
        RectTransform rect,
        RectTransform parent,
        Sprite sprite)
    {
        if (rect == null ||
            parent == null)
        {
            return;
        }

        Vector2 parentSize =
            parent.rect.size;

        if (parentSize.x <= 0f ||
            parentSize.y <= 0f)
        {
            return;
        }

        float spriteAspect =
            parentSize.x /
            parentSize.y;

        if (sprite != null &&
            sprite.rect.height >
            0.001f)
        {
            spriteAspect =
                sprite.rect.width /
                sprite.rect.height;
        }

        float parentAspect =
            parentSize.x /
            parentSize.y;

        float width;
        float height;

        // "Cover" behaviour: preserve artwork aspect and crop only outside
        // the physical display instead of stretching the campaign art.
        if (spriteAspect >
            parentAspect)
        {
            height =
                parentSize.y;

            width =
                height *
                spriteAspect;
        }
        else
        {
            width =
                parentSize.x;

            height =
                width /
                Mathf.Max(
                    0.001f,
                    spriteAspect);
        }

        rect.sizeDelta =
            new Vector2(
                width,
                height);

        rect.anchoredPosition =
            Vector2.zero;
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
}
