#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ArcheryTrickShot.CharacterTesting.Editor
{
    /// <summary>
    /// Builds one reusable Ember material from the original Hyper3D texture set.
    /// It does not modify the Mixamo skeleton or the imported FBX mesh.
    /// </summary>
    public static class EmberMaterialSetup
    {
        private const string Root = "Assets/CharacterRigTest/Materials/Ember";
        private const string TextureRoot = Root + "/Textures";
        private const string DiffusePath = TextureRoot + "/texture_diffuse.png";
        private const string NormalPath = TextureRoot + "/texture_normal.png";
        private const string MetallicPath = TextureRoot + "/texture_metallic.png";
        private const string RoughnessPath = TextureRoot + "/texture_roughness.png";
        private const string PackedMetallicSmoothnessPath = Root + "/Ember_MetallicSmoothness.png";
        public const string MaterialPath = Root + "/Ember_Original_PBR.mat";

        [MenuItem("Tools/Archery Trick Shot/Character Rig Test/Setup Ember Original Materials")]
        public static void SetupFromMenu()
        {
            Material material = EnsureMaterial(true);
            if (material == null)
                return;

            EditorGUIUtility.PingObject(material);
            Selection.activeObject = material;

            EditorUtility.DisplayDialog(
                "Ember Materials Ready",
                "Ember's original Hyper3D PBR textures are ready.\n\n" +
                "Material:\n" + MaterialPath + "\n\n" +
                "Now run:\nTools > Archery Trick Shot > Character Rig Test > Create Ember Gameplay Test",
                "OK");
        }

        public static Material EnsureMaterial(bool forceRebuild = false)
        {
            EnsureFolders();

            if (!ValidateSourceTextures(out string error))
            {
                Debug.LogError("[EmberMaterialSetup] " + error);
                if (forceRebuild)
                    EditorUtility.DisplayDialog("Ember Material Setup", error, "OK");
                return null;
            }

            ConfigureTextureImporters();

            if (forceRebuild || AssetDatabase.LoadAssetAtPath<Texture2D>(PackedMetallicSmoothnessPath) == null)
                BuildMetallicSmoothnessTexture();

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader shader = ResolveShader();

            if (shader == null)
            {
                Debug.LogError("[EmberMaterialSetup] Could not find URP Lit, URP Simple Lit, or Standard shader.");
                return null;
            }

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Ember_Original_PBR"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            Texture2D metallicSmoothness = AssetDatabase.LoadAssetAtPath<Texture2D>(PackedMetallicSmoothnessPath);

            ApplyTextures(material, shader, diffuse, normal, metallicSmoothness);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return material;
        }

        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                return shader;

            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader != null)
                return shader;

            return Shader.Find("Standard");
        }

        private static void ApplyTextures(
            Material material,
            Shader shader,
            Texture2D diffuse,
            Texture2D normal,
            Texture2D metallicSmoothness)
        {
            string shaderName = shader.name;

            if (shaderName.IndexOf("Universal Render Pipeline", StringComparison.Ordinal) >= 0)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", diffuse);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", Color.white);

                if (material.HasProperty("_BumpMap"))
                    material.SetTexture("_BumpMap", normal);
                if (normal != null)
                    material.EnableKeyword("_NORMALMAP");

                if (material.HasProperty("_MetallicGlossMap"))
                    material.SetTexture("_MetallicGlossMap", metallicSmoothness);
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", 1f);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 1f);
                if (material.HasProperty("_SmoothnessTextureChannel"))
                    material.SetFloat("_SmoothnessTextureChannel", 0f);

                if (metallicSmoothness != null)
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");

                if (material.HasProperty("_Cull"))
                    material.SetFloat("_Cull", 2f);
            }
            else
            {
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", diffuse);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", Color.white);

                if (material.HasProperty("_BumpMap"))
                    material.SetTexture("_BumpMap", normal);
                if (normal != null)
                    material.EnableKeyword("_NORMALMAP");

                if (material.HasProperty("_MetallicGlossMap"))
                    material.SetTexture("_MetallicGlossMap", metallicSmoothness);
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", 1f);
                if (material.HasProperty("_GlossMapScale"))
                    material.SetFloat("_GlossMapScale", 1f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", 1f);

                if (metallicSmoothness != null)
                    material.EnableKeyword("_METALLICGLOSSMAP");
            }
        }

        private static bool ValidateSourceTextures(out string error)
        {
            string[] paths = { DiffusePath, NormalPath, MetallicPath, RoughnessPath };
            foreach (string path in paths)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                {
                    error = "Missing original Ember texture:\n" + path +
                            "\n\nRe-extract the v2.2 patch over the Assets folder.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void ConfigureTextureImporters()
        {
            ConfigureTexture(DiffusePath, false, true);
            ConfigureTexture(NormalPath, true, false);
            ConfigureTexture(MetallicPath, false, false);
            ConfigureTexture(RoughnessPath, false, false);
        }

        private static void ConfigureTexture(string path, bool normalMap, bool sRgb)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;

            TextureImporterType desiredType = normalMap
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;

            if (importer.textureType != desiredType)
            {
                importer.textureType = desiredType;
                changed = true;
            }

            if (importer.sRGBTexture != sRgb)
            {
                importer.sRGBTexture = sRgb;
                changed = true;
            }


            if (importer.maxTextureSize != 2048)
            {
                importer.maxTextureSize = 2048;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        private static void BuildMetallicSmoothnessTexture()
        {
            Color32[] metallicPixels = ReadLinearPixels(MetallicPath, out int width, out int height);
            Color32[] roughnessPixels = ReadLinearPixels(RoughnessPath, out int roughWidth, out int roughHeight);

            if (metallicPixels == null || roughnessPixels == null)
                return;

            if (width != roughWidth || height != roughHeight)
                throw new InvalidOperationException("Ember metallic and roughness textures have different dimensions.");

            Color32[] packed = new Color32[metallicPixels.Length];
            for (int i = 0; i < packed.Length; i++)
            {
                byte metallic = metallicPixels[i].r;
                byte smoothness = (byte)(255 - roughnessPixels[i].r);
                packed[i] = new Color32(metallic, metallic, metallic, smoothness);
            }

            Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                output.SetPixels32(packed);
                output.Apply(false, false);

                string absolute = AssetPathToAbsolute(PackedMetallicSmoothnessPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? string.Empty);
                File.WriteAllBytes(absolute, output.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            AssetDatabase.ImportAsset(PackedMetallicSmoothnessPath, ImportAssetOptions.ForceUpdate);

            TextureImporter packedImporter = AssetImporter.GetAtPath(PackedMetallicSmoothnessPath) as TextureImporter;
            if (packedImporter != null)
            {
                packedImporter.textureType = TextureImporterType.Default;
                packedImporter.sRGBTexture = false;
                packedImporter.mipmapEnabled = true;
                packedImporter.maxTextureSize = 2048;
                packedImporter.isReadable = false;
                packedImporter.SaveAndReimport();
            }
        }

        private static Color32[] ReadLinearPixels(string path, out int width, out int height)
        {
            width = 0;
            height = 0;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return null;

            bool oldReadable = importer.isReadable;
            bool oldSrgb = importer.sRGBTexture;

            try
            {
                if (!oldReadable || oldSrgb)
                {
                    importer.isReadable = true;
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                    return null;

                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                importer.isReadable = oldReadable;
                importer.sRGBTexture = oldSrgb;
                importer.SaveAndReimport();
            }
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Could not resolve Unity project root.");

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "CharacterRigTest");
            EnsureFolder("Assets/CharacterRigTest", "Materials");
            EnsureFolder("Assets/CharacterRigTest/Materials", "Ember");
            EnsureFolder(Root, "Textures");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
