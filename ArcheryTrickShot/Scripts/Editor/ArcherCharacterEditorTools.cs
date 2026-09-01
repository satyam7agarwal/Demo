#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only utilities for the scalable archer roster.
///
/// Character switching deliberately writes the same PlayerPrefs key that the
/// future Character Select screen will use. The roster default remains a true
/// fallback (Khaem in the current project) instead of being abused as a live
/// selection field.
///
/// Material setup is intentionally non-destructive: imported FBX/model assets
/// are never edited or replaced. The generated PBR material is stored on the
/// character profile and applied to the instantiated character at runtime.
/// </summary>
public static class ArcherCharacterEditorTools
{
    private const string RosterAssetPath =
        "Assets/ArcheryTrickShot/Resources/Archer3D/ArcherCharacterRoster.asset";

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Use Selected Archer Profile",
        true)]
    private static bool ValidateUseSelectedArcherProfile()
    {
        return Selection.activeObject is Archer3DRuntimeProfile;
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Use Selected Archer Profile")]
    private static void UseSelectedArcherProfile()
    {
        Archer3DRuntimeProfile profile =
            Selection.activeObject as Archer3DRuntimeProfile;

        ArcherCharacterRoster roster = LoadRoster();

        if (profile == null || roster == null)
            return;

        if (!roster.SelectCharacter(profile.CharacterId))
        {
            EditorUtility.DisplayDialog(
                "Select Archer",
                "The selected profile is not registered in ArcherCharacterRoster.",
                "OK");
            return;
        }

        Debug.Log(
            "[Archer Characters] Selected '" +
            profile.DisplayName +
            "' (" + profile.CharacterId + ").");

        EditorUtility.DisplayDialog(
            "Archer Selected",
            profile.DisplayName +
            " is now the saved active character.\n\n" +
            "If the game is already running, stop and enter Play Mode again " +
            "so the character presentation is rebuilt cleanly.",
            "OK");
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Use Roster Default (Clear Saved Selection)")]
    private static void UseRosterDefault()
    {
        ArcherCharacterRoster roster = LoadRoster();

        if (roster == null)
            return;

        roster.ClearSavedSelection();

        Archer3DRuntimeProfile defaultProfile =
            roster.ResolveRosterDefaultProfile();

        string label = defaultProfile != null
            ? defaultProfile.DisplayName + " (" + defaultProfile.CharacterId + ")"
            : roster.DefaultCharacterId;

        Debug.Log(
            "[Archer Characters] Cleared saved selection. Roster default is '" +
            label + "'.");

        EditorUtility.DisplayDialog(
            "Using Roster Default",
            "Saved character selection was cleared.\n\n" +
            "The next Play Mode uses: " + label +
            "\n\nThe roster's Default Character Id remains the fallback; " +
            "you no longer need to edit it just to test another character.",
            "OK");
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Log Current Character Selection")]
    private static void LogCurrentCharacterSelection()
    {
        ArcherCharacterRoster roster = LoadRoster();

        if (roster == null)
            return;

        string saved = roster.GetSavedSelectionId();
        Archer3DRuntimeProfile resolved = roster.ResolveSelectedProfile();

        Debug.Log(
            "[Archer Characters] saved=" +
            (string.IsNullOrWhiteSpace(saved) ? "<none>" : saved) +
            ", rosterDefault=" + roster.DefaultCharacterId +
            ", resolved=" +
            (resolved != null ? resolved.CharacterId : "<none>"));
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create-Update PBR Material For Selected Archer",
        true)]
    private static bool ValidateCreateOrUpdatePbrMaterial()
    {
        return Selection.activeObject is Archer3DRuntimeProfile;
    }

    [MenuItem(
        "Tools/Archery Trick Shot/Characters/Create-Update PBR Material For Selected Archer")]
    private static void CreateOrUpdatePbrMaterial()
    {
        Archer3DRuntimeProfile profile =
            Selection.activeObject as Archer3DRuntimeProfile;

        if (profile == null || profile.ArcherPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "Archer Material",
                "Select an Archer3DRuntimeProfile that has an Archer Prefab.",
                "OK");
            return;
        }

        if (!TryCreateOrUpdateMaterial(profile, true))
            return;

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
    }

    /// <summary>
    /// Used by the one-click Humanoid onboarding tool too. Returns false when
    /// no nearby diffuse texture exists, because in that state keeping the FBX
    /// untouched is safer than creating a misleading white material.
    /// </summary>
    public static bool TryCreateOrUpdateMaterial(
        Archer3DRuntimeProfile profile,
        bool showDialogs)
    {
        if (profile == null || profile.ArcherPrefab == null)
            return false;

        // V8.1 temporarily used a generated *Character.prefab wrapper for
        // materials. If a project still points at one, recover the original
        // model dependency before doing anything else. New material setup never
        // replaces ArcherPrefab.
        TryRestoreOriginalModelFromLegacyWrapper(profile);

        string sourcePath =
            AssetDatabase.GetAssetPath(profile.ArcherPrefab);

        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        string sourceFolder =
            Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        Texture2D diffuse = FindTexture(
            sourceFolder,
            "texture_diffuse",
            "basecolor",
            "base_color",
            "albedo",
            "diffuse",
            "shaded");

        if (diffuse == null)
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog(
                    "Archer Material - Textures Not Found",
                    "No diffuse/base-color texture was found near:\n" +
                    sourcePath +
                    "\n\nImport the Hyper3D texture files into the SAME character folder, for example:\n" +
                    "• texture_diffuse.png\n" +
                    "• texture_normal.png\n" +
                    "• texture_metallic.png\n" +
                    "• texture_roughness.png\n\n" +
                    "Then select this archer profile and run the command again.",
                    "OK");
            }

            return false;
        }

        Texture2D normal = FindTexture(
            sourceFolder,
            "texture_normal",
            "normal");

        Texture2D metallic = FindTexture(
            sourceFolder,
            "texture_metallic",
            "metallic",
            "metalness");

        Texture2D roughness = FindTexture(
            sourceFolder,
            "texture_roughness",
            "roughness");

        ConfigureTextureImport(diffuse, TextureRole.Color);

        if (normal != null)
            ConfigureTextureImport(normal, TextureRole.Normal);

        if (metallic != null)
            ConfigureTextureImport(metallic, TextureRole.LinearData);

        if (roughness != null)
            ConfigureTextureImport(roughness, TextureRole.LinearData);

        string safeName = SafeFileName(
            string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.CharacterId
                : profile.DisplayName);

        string materialPath =
            sourceFolder + "/" + safeName + "_PBR.mat";

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        // The imported Mixamo/Hyper model is already known to render correctly
        // in this project before we add textures. Use one of its existing body
        // materials as the rendering template instead of guessing a pipeline
        // shader. This preserves the shader, surface type, culling, render queue
        // and other project-specific settings that made the white source model
        // visible in the first place.
        Material sourceTemplate =
            FindSourceTemplateMaterial(profile.ArcherPrefab);

        if (material == null)
        {
            if (sourceTemplate != null)
            {
                material = new Material(sourceTemplate)
                {
                    name = safeName + "_PBR"
                };
            }
            else
            {
                Shader shader = FindProjectLitShader();

                if (shader == null)
                {
                    if (showDialogs)
                    {
                        EditorUtility.DisplayDialog(
                            "Archer Material",
                            "Could not find a working source material or a supported Lit/Standard shader in this project.",
                            "OK");
                    }
                    return false;
                }

                material = new Material(shader)
                {
                    name = safeName + "_PBR"
                };
            }

            AssetDatabase.CreateAsset(material, materialPath);
        }
        else if (sourceTemplate != null)
        {
            // Repair materials created by V8.1/V8.2 as well. Re-sync the
            // existing generated material from the source model every time the
            // command runs, then apply the PBR maps below.
            string generatedName = material.name;
            material.shader = sourceTemplate.shader;
            material.CopyPropertiesFromMaterial(sourceTemplate);
            material.name = generatedName;
        }

        Texture2D metallicSmoothness = null;

        if (metallic != null && roughness != null)
        {
            string packedPath =
                sourceFolder + "/" + safeName + "_MetallicSmoothness.png";

            metallicSmoothness =
                CreateMetallicSmoothnessMap(
                    metallic,
                    roughness,
                    packedPath);
        }

        ApplyPbrTextures(
            material,
            diffuse,
            normal,
            metallicSmoothness != null
                ? metallicSmoothness
                : metallic);

        // Character atlases from Hyper3D may contain an alpha channel even
        // though the body is intended to be opaque. Explicitly keep the runtime
        // character on an opaque/depth-writing path so an imported alpha channel
        // cannot make the entire SkinnedMeshRenderer disappear.
        ForceOpaqueSurface(material);

        EditorUtility.SetDirty(material);

        // Keep the original rigged FBX/prefab as ArcherPrefab. The profile owns
        // only the visual material override; Archer3DRuntimeFactory applies it
        // after instantiation and before the shared bow is created.
        profile.CharacterMaterialOverride = material;
        profile.ApplyCharacterMaterialToAllSlots = true;
        EditorUtility.SetDirty(profile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (showDialogs)
        {
            EditorUtility.DisplayDialog(
                "Archer PBR Material Ready",
                "Created/updated:\n" + materialPath +
                "\n\nRuntime material override assigned to profile:\n" +
                profile.DisplayName +
                "\n\nArcher Prefab remains the original rigged model:\n" +
                AssetDatabase.GetAssetPath(profile.ArcherPrefab) +
                "\n\nNo wrapper prefab was created and the Mixamo FBX was not modified.",
                "OK");
        }

        return true;
    }

    private static void TryRestoreOriginalModelFromLegacyWrapper(
        Archer3DRuntimeProfile profile)
    {
        if (profile == null || profile.ArcherPrefab == null)
            return;

        string wrapperPath =
            AssetDatabase.GetAssetPath(profile.ArcherPrefab);

        if (string.IsNullOrWhiteSpace(wrapperPath) ||
            !wrapperPath.EndsWith(
                "Character.prefab",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string[] dependencies =
            AssetDatabase.GetDependencies(wrapperPath, true);

        for (int i = 0; i < dependencies.Length; i++)
        {
            string dependency = dependencies[i];
            string extension =
                Path.GetExtension(dependency).ToLowerInvariant();

            if (extension != ".fbx" &&
                extension != ".obj" &&
                extension != ".glb" &&
                extension != ".gltf")
            {
                continue;
            }

            GameObject original =
                AssetDatabase.LoadAssetAtPath<GameObject>(dependency);

            if (original == null)
                continue;

            profile.ArcherPrefab = original;
            EditorUtility.SetDirty(profile);

            Debug.Log(
                "[Archer Characters] Restored original model from legacy " +
                "material wrapper: " + dependency);
            return;
        }
    }

    private enum TextureRole
    {
        Color,
        Normal,
        LinearData
    }

    private static ArcherCharacterRoster LoadRoster()
    {
        ArcherCharacterRoster roster =
            AssetDatabase.LoadAssetAtPath<ArcherCharacterRoster>(
                RosterAssetPath);

        if (roster == null)
        {
            EditorUtility.DisplayDialog(
                "Archer Characters",
                "ArcherCharacterRoster.asset was not found at:\n" +
                RosterAssetPath,
                "OK");
        }

        return roster;
    }

    private static Texture2D FindTexture(
        string folder,
        params string[] preferredTokens)
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { folder });

        for (int tokenIndex = 0;
             tokenIndex < preferredTokens.Length;
             tokenIndex++)
        {
            string token =
                preferredTokens[tokenIndex]
                    .ToLowerInvariant();

            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[i]);

                string file =
                    Path.GetFileNameWithoutExtension(path)
                        .ToLowerInvariant();

                if (file == token || file.Contains(token))
                {
                    return AssetDatabase
                        .LoadAssetAtPath<Texture2D>(path);
                }
            }
        }

        return null;
    }

    private static void ConfigureTextureImport(
        Texture2D texture,
        TextureRole role)
    {
        if (texture == null)
            return;

        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer =
            AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            return;

        bool changed = false;

        if (role == TextureRole.Normal)
        {
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
        }
        else
        {
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            bool shouldBeSrgb = role == TextureRole.Color;
            if (importer.sRGBTexture != shouldBeSrgb)
            {
                importer.sRGBTexture = shouldBeSrgb;
                changed = true;
            }
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static Material FindSourceTemplateMaterial(
        GameObject archerPrefab)
    {
        if (archerPrefab == null)
            return null;

        Renderer[] renderers =
            archerPrefab.GetComponentsInChildren<Renderer>(true);

        Material fallback = null;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                renderer is LineRenderer ||
                renderer is TrailRenderer)
            {
                continue;
            }

            string lowerName =
                renderer.name.ToLowerInvariant();

            if (lowerName.Contains("bow") ||
                lowerName.Contains("arrow") ||
                lowerName.Contains("trail"))
            {
                continue;
            }

            Material[] slots = renderer.sharedMaterials;

            if (slots == null)
                continue;

            foreach (Material slot in slots)
            {
                if (slot == null || slot.shader == null)
                    continue;

                if (renderer is SkinnedMeshRenderer)
                    return slot;

                if (fallback == null)
                    fallback = slot;
            }
        }

        return fallback;
    }

    private static void ForceOpaqueSurface(Material material)
    {
        if (material == null)
            return;

        // URP/HDRP-style surface controls. Only touch properties that exist so
        // the same code remains safe for Built-in Standard and custom shaders.
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 0f);

        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);

        if (material.HasProperty("_AlphaCutoffEnable"))
            material.SetFloat("_AlphaCutoffEnable", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);

        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 1f);

        // Built-in Standard rendering mode.
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 0f);

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = -1;
    }

    private static Shader FindProjectLitShader()
    {
        bool hasScriptablePipeline =
            GraphicsSettings.defaultRenderPipeline != null;

        if (hasScriptablePipeline)
        {
            Shader urp =
                Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null)
                return urp;

            Shader hdrp =
                Shader.Find("HDRP/Lit");
            if (hdrp != null)
                return hdrp;
        }

        Shader standard = Shader.Find("Standard");
        if (standard != null)
            return standard;

        return Shader.Find("Universal Render Pipeline/Lit");
    }

    private static void ApplyPbrTextures(
        Material material,
        Texture2D diffuse,
        Texture2D normal,
        Texture2D metallicSmoothness)
    {
        if (material == null)
            return;

        // Built-in Standard shader.
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", diffuse);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);

        // URP/HDRP style base map.
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", diffuse);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);

        if (normal != null)
        {
            if (material.HasProperty("_BumpMap"))
                material.SetTexture("_BumpMap", normal);

            material.EnableKeyword("_NORMALMAP");
        }

        if (metallicSmoothness != null)
        {
            if (material.HasProperty("_MetallicGlossMap"))
                material.SetTexture("_MetallicGlossMap", metallicSmoothness);

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 1f);

            if (material.HasProperty("_GlossMapScale"))
                material.SetFloat("_GlossMapScale", 1f);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 1f);

            material.EnableKeyword("_METALLICGLOSSMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
    }

    private static Texture2D CreateMetallicSmoothnessMap(
        Texture2D metallic,
        Texture2D roughness,
        string outputAssetPath)
    {
        string metallicPath = AssetDatabase.GetAssetPath(metallic);
        string roughnessPath = AssetDatabase.GetAssetPath(roughness);

        TextureImporter metallicImporter =
            AssetImporter.GetAtPath(metallicPath) as TextureImporter;
        TextureImporter roughnessImporter =
            AssetImporter.GetAtPath(roughnessPath) as TextureImporter;

        if (metallicImporter == null || roughnessImporter == null)
            return null;

        bool oldMetalReadable = metallicImporter.isReadable;
        bool oldRoughReadable = roughnessImporter.isReadable;
        bool oldMetalSrgb = metallicImporter.sRGBTexture;
        bool oldRoughSrgb = roughnessImporter.sRGBTexture;

        try
        {
            metallicImporter.isReadable = true;
            metallicImporter.sRGBTexture = false;
            metallicImporter.SaveAndReimport();

            roughnessImporter.isReadable = true;
            roughnessImporter.sRGBTexture = false;
            roughnessImporter.SaveAndReimport();

            metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
            roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughnessPath);

            int width = metallic.width;
            int height = metallic.height;

            Texture2D packed = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                true);

            Color32[] output = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;

                    Color m = metallic.GetPixelBilinear(u, v);
                    Color r = roughness.GetPixelBilinear(u, v);

                    float metal = Mathf.Clamp01(
                        (m.r + m.g + m.b) / 3f);

                    float rough = Mathf.Clamp01(
                        (r.r + r.g + r.b) / 3f);

                    byte metalByte =
                        (byte)Mathf.RoundToInt(metal * 255f);
                    byte smoothByte =
                        (byte)Mathf.RoundToInt((1f - rough) * 255f);

                    output[y * width + x] =
                        new Color32(
                            metalByte,
                            metalByte,
                            metalByte,
                            smoothByte);
                }
            }

            packed.SetPixels32(output);
            packed.Apply(false, false);

            string absolutePath =
                AssetPathToAbsolutePath(outputAssetPath);

            File.WriteAllBytes(
                absolutePath,
                packed.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(packed);

            AssetDatabase.ImportAsset(
                outputAssetPath,
                ImportAssetOptions.ForceUpdate);

            TextureImporter packedImporter =
                AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;

            if (packedImporter != null)
            {
                packedImporter.textureType = TextureImporterType.Default;
                packedImporter.sRGBTexture = false;
                packedImporter.mipmapEnabled = true;
                packedImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                packedImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
        }
        finally
        {
            metallicImporter.isReadable = oldMetalReadable;
            metallicImporter.sRGBTexture = oldMetalSrgb;
            metallicImporter.SaveAndReimport();

            roughnessImporter.isReadable = oldRoughReadable;
            roughnessImporter.sRGBTexture = oldRoughSrgb;
            roughnessImporter.SaveAndReimport();
        }
    }

    private static string SafeFileName(string value)
    {
        string result =
            string.IsNullOrWhiteSpace(value)
                ? "Archer"
                : value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');

        return result.Replace(' ', '_');
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        DirectoryInfo parent =
            Directory.GetParent(Application.dataPath);

        if (parent == null)
            throw new InvalidOperationException(
                "Could not resolve Unity project root.");

        return Path.GetFullPath(
            Path.Combine(
                parent.FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
#endif
