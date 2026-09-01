#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TargetVisualSetupTool
{
    private const string TargetPrefabPath =
        "Assets/ArcheryTrickShot/Resources/Prefabs/Gameplay/Target.prefab";

    private const string WoodPath =
        "Assets/ArcheryTrickShot/Art/Targets/Target_Wood.png";

    private const string RuinsPath =
        "Assets/ArcheryTrickShot/Art/Targets/Target_Ruins.png";

    private const string CrystalPath =
        "Assets/ArcheryTrickShot/Art/Targets/Target_Crystal.png";

    private const string MoltenPath =
        "Assets/ArcheryTrickShot/Art/Targets/Target_Molten.png";

    private const string ClockworkPath =
        "Assets/ArcheryTrickShot/Art/Targets/Target_Clockwork.png";

    [MenuItem(
        "Tools/Archery Trick Shot/Setup Premium Target Visual")]
    public static void SetupPremiumTargetVisual()
    {
        string[] spritePaths =
        {
            WoodPath,
            RuinsPath,
            CrystalPath,
            MoltenPath,
            ClockworkPath
        };

        foreach (string path in spritePaths)
            ConfigureTextureImporter(path);

        Sprite wood =
            AssetDatabase.LoadAssetAtPath<Sprite>(WoodPath);

        Sprite ruins =
            AssetDatabase.LoadAssetAtPath<Sprite>(RuinsPath);

        Sprite crystal =
            AssetDatabase.LoadAssetAtPath<Sprite>(CrystalPath);

        Sprite molten =
            AssetDatabase.LoadAssetAtPath<Sprite>(MoltenPath);

        Sprite clockwork =
            AssetDatabase.LoadAssetAtPath<Sprite>(ClockworkPath);

        if (wood == null)
        {
            Debug.LogError(
                "[Target Visual] Target_Wood.png was not found. " +
                "Expected: " + WoodPath);

            return;
        }

        GameObject prefabRoot =
            PrefabUtility.LoadPrefabContents(
                TargetPrefabPath);

        if (prefabRoot == null)
        {
            Debug.LogError(
                "[Target Visual] Could not load Target.prefab.");

            return;
        }

        try
        {
            Target target =
                prefabRoot.GetComponent<Target>();

            if (target == null)
            {
                Debug.LogError(
                    "[Target Visual] Target.cs is missing. " +
                    "Nothing was changed.");

                return;
            }

            Transform visualRoot =
                GetOrCreateChild(
                    prefabRoot.transform,
                    "VisualRoot");

            Transform body =
                GetOrCreateChild(
                    visualRoot,
                    "Body");

            SpriteRenderer bodyRenderer =
                body.GetComponent<SpriteRenderer>();

            if (bodyRenderer == null)
            {
                bodyRenderer =
                    body.gameObject
                        .AddComponent<SpriteRenderer>();
            }

            // Preserve the manually calibrated Body transform. The setup tool
            // only wires sprites; it never resets scoring geometry or physics.
            bodyRenderer.sprite = wood;
            bodyRenderer.flipX = false;

            TargetVisualFacing visual =
                prefabRoot.GetComponent<TargetVisualFacing>();

            if (visual == null)
            {
                visual =
                    prefabRoot
                        .AddComponent<TargetVisualFacing>();
            }

            visual.Configure(
                bodyRenderer,
                wood,
                ruins,
                crystal,
                molten,
                clockwork);

            PrefabUtility.SaveAsPrefabAsset(
                prefabRoot,
                TargetPrefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Object prefabAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    TargetPrefabPath);

            Selection.activeObject = prefabAsset;
            EditorGUIUtility.PingObject(prefabAsset);

            Debug.Log(
                "[Target Visual] Premium target variants wired. " +
                "Wood / Ruins / Crystal / Molten / Clockwork. " +
                "CollisionRoot / ScoringFace / PhysicalParts were left unchanged.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(
                prefabRoot);
        }
    }

    private static void ConfigureTextureImporter(string path)
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(path)
                as TextureImporter;

        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.spritePixelsPerUnit = 100f;
        importer.maxTextureSize = 2048;
        importer.textureCompression =
            TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }

    private static Transform GetOrCreateChild(
        Transform parent,
        string name)
    {
        Transform existing = parent.Find(name);

        if (existing != null)
            return existing;

        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        return child.transform;
    }
}
#endif
