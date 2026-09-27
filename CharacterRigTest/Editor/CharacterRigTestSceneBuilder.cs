#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcheryTrickShot.CharacterTesting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheryTrickShot.CharacterTesting.Editor
{
    public static class CharacterRigTestSceneBuilder
    {
        private const string RootFolder = "Assets/CharacterRigTest";
        private const string CharacterFolder = RootFolder + "/Characters";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string ScenePath = SceneFolder + "/CharacterRigTest.unity";

        [MenuItem("Tools/Archery Trick Shot/Character Rig Test/Create or Refresh Test Scene")]
        public static void CreateOrRefreshScene()
        {
            EnsureFolders();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = CreateCamera();
            CreateLighting();
            CreateGround();

            var rootObject = new GameObject("CharacterRoot");
            rootObject.transform.position = Vector3.zero;

            var controllerObject = new GameObject("CharacterRigTestController");
            var controller = controllerObject.AddComponent<CharacterRigTestController>();
            controller.SetSceneReferences(rootObject.transform, camera);

            List<GameObject> characters = FindCharacterAssets();
            controller.SetCharacterPrefabs(characters);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = controllerObject;
            SceneView.lastActiveSceneView?.FrameSelected();

            if (characters.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Character Rig Test",
                    "Test scene created.\n\nNo character FBX/prefab was found yet.\n\nPut your Mixamo-rigged character FBX files into:\n" + CharacterFolder + "\n\nThen run this menu command again.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Character Rig Test",
                    "Test scene created with " + characters.Count + " character(s).\n\nOpen/Play:\n" + ScenePath,
                    "OK");
            }
        }

        [MenuItem("Tools/Archery Trick Shot/Character Rig Test/Configure Selected FBX as Humanoid")]
        public static void ConfigureSelectedAsHumanoid()
        {
            UnityEngine.Object[] selected = Selection.objects;
            int changed = 0;

            foreach (UnityEngine.Object obj in selected)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.SaveAndReimport();
                changed++;
            }

            EditorUtility.DisplayDialog(
                "Humanoid Setup",
                changed > 0
                    ? "Configured " + changed + " selected model(s) as Humanoid. Check the Rig tab for a green avatar/checkmark before testing."
                    : "No model/FBX asset was selected.",
                "OK");
        }

        [MenuItem("Tools/Archery Trick Shot/Character Rig Test/Open Test Scene")]
        public static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateOrRefreshScene();
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("TestCamera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.085f, 0.10f, 1f);
            cameraObject.transform.position = new Vector3(0f, 1.1f, 4.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 1f, 0f));
            return camera;
        }

        private static void CreateLighting()
        {
            GameObject keyObject = new GameObject("KeyLight");
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            keyObject.transform.rotation = Quaternion.Euler(40f, -35f, 0f);

            GameObject fillObject = new GameObject("FillLight");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fillObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.04f, 0f);
            ground.transform.localScale = new Vector3(8f, 0.08f, 8f);

            Collider collider = ground.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static List<GameObject> FindCharacterAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { CharacterFolder });
            var results = new List<GameObject>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && !results.Contains(asset))
                    results.Add(asset);
            }

            return results
                .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "CharacterRigTest");
            EnsureFolder(RootFolder, "Runtime");
            EnsureFolder(RootFolder, "Editor");
            EnsureFolder(RootFolder, "Characters");
            EnsureFolder(RootFolder, "Scenes");
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
