using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirportBaggageSimulator.EditorTools
{
    /// <summary>Generates the M4 assets (sprites, prefabs, VisualSandbox scene). Never touches MainSimulation.unity.
    /// Menu: BAGFLOW/M4/Build Prefabs and VisualSandbox. Batch: -executeMethod AirportBaggageSimulator.EditorTools.BagflowUiBuilder.BuildAllBatch</summary>
    public static class BagflowUiBuilder
    {
        public const string SpriteFolder = "Assets/Sprites";
        public const string PrefabFolder = "Assets/Prefabs";
        public const string RoundedPath = SpriteFolder + "/ui_rounded.png";
        public const string CirclePath = SpriteFolder + "/ui_circle.png";
        public const string BagPrefabPath = PrefabFolder + "/BaggageSpriteView.prefab";
        public const string UiPrefabPath = PrefabFolder + "/BAGFLOW_UI.prefab";
        public const string SandboxScenePath = "Assets/Scenes/VisualSandbox.unity";

        [MenuItem("BAGFLOW/M4/Build Prefabs and VisualSandbox")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(SpriteFolder);
            Directory.CreateDirectory(PrefabFolder);
            UiTheme.Rounded = SaveSprite(RoundedPath, UiTheme.CreateTexture(32, 12), 12);
            UiTheme.Circle = SaveSprite(CirclePath, UiTheme.CreateTexture(32, 16), 0);

            var temp = new GameObject("BuildTemp");
            var bagTemplate = BagflowUiFactory.BuildBagTemplate(temp.transform);
            bagTemplate.transform.SetParent(null, false);
            var bagPrefab = PrefabUtility.SaveAsPrefabAsset(bagTemplate.gameObject, BagPrefabPath).GetComponent<BaggageSpriteView>();
            UnityEngine.Object.DestroyImmediate(bagTemplate.gameObject);

            var ui = BagflowUiFactory.Build(temp.transform, bagPrefab);
            ui.transform.SetParent(null, false);
            var uiPrefab = PrefabUtility.SaveAsPrefabAsset(ui.gameObject, UiPrefabPath);
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
            UnityEngine.Object.DestroyImmediate(temp);

            BuildSandboxScene(uiPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("BAGFLOW M4: prefabs and VisualSandbox built.");
        }

        public static void BuildAllBatch()
        {
            try { BuildAll(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static Sprite SaveSprite(string path, Texture2D texture, int border)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildSandboxScene(GameObject uiPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UiTheme.Background;
            camera.orthographic = true;

            var ui = ((GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, scene)).GetComponent<BagflowUi>();

            // Sandbox-local integration, mirroring the documented MainSimulation wiring (no second one in the main scene).
            var integration = new GameObject("BAGFLOW Integration (sandbox)");
            var controller = integration.AddComponent<SimulationController>();
            var composition = integration.AddComponent<SimulationComposition>();
            ConnectComposition(composition, ui);

            var demo = new GameObject("VisualSandboxDemo").AddComponent<VisualSandboxDemo>();
            var demoObject = new SerializedObject(demo);
            demoObject.FindProperty("ui").objectReferenceValue = ui;
            demoObject.FindProperty("controller").objectReferenceValue = controller;
            demoObject.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, SandboxScenePath);
        }

        /// <summary>Assigns the three view components of a BAGFLOW_UI instance to a SimulationComposition.</summary>
        public static void ConnectComposition(SimulationComposition composition, BagflowUi ui)
        {
            var so = new SerializedObject(composition);
            so.FindProperty("worldViewComponent").objectReferenceValue = ui.World;
            so.FindProperty("dashboardViewComponent").objectReferenceValue = ui.Dashboard;
            so.FindProperty("controlViewComponent").objectReferenceValue = ui.Controls;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(composition);
        }

        [MenuItem("BAGFLOW/M4/Connect selected BAGFLOW_UI to SimulationComposition")]
        public static void ConnectSelected()
        {
            var ui = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<BagflowUi>();
            var composition = UnityEngine.Object.FindFirstObjectByType<SimulationComposition>();
            if (ui == null || composition == null)
            {
                EditorUtility.DisplayDialog("BAGFLOW M4", "Select a BAGFLOW_UI instance in the scene; the scene must contain one SimulationComposition.", "OK");
                return;
            }
            ConnectComposition(composition, ui);
            EditorSceneManager.MarkSceneDirty(composition.gameObject.scene);
            Debug.Log("BAGFLOW M4: connected " + ui.name + " to " + composition.name + ". Save the scene.");
        }
    }
}
