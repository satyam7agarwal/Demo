using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight authored UI skin loader. Visual artwork lives as imported PNGs in
/// Resources/UI/Premium; runtime code only chooses/reuses sprites and never draws
/// the premium frames procedurally.
/// </summary>
public static class ATSPremiumSkin
{
    private const string Root = "UI/Premium/";
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public static Sprite Sprite(string assetName, Vector4 border = default)
    {
        string key = assetName + "|" + border;
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        Texture2D texture = Resources.Load<Texture2D>(Root + assetName);
        if (texture == null)
            return null;

        Sprite sprite = UnityEngine.Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            border);
        sprite.name = "ATS_" + assetName;
        Cache[key] = sprite;
        return sprite;
    }

    public static bool Apply(Image image, string assetName, Vector4 border = default, bool sliced = true)
    {
        if (image == null)
            return false;

        Sprite sprite = Sprite(assetName, border);
        if (sprite == null)
            return false;

        image.sprite = sprite;
        image.type = sliced && border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = Color.white;
        image.preserveAspect = !sliced;

        Outline outline = image.GetComponent<Outline>();
        if (outline != null)
            outline.enabled = false;
        return true;
    }

    public static Image AddIcon(Transform parent, string assetName, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(assetName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        Apply(image, assetName, Vector4.zero, false);
        image.raycastTarget = false;
        return image;
    }
}
