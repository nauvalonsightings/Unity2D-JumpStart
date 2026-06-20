#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity2DJumpStart
{
    public sealed class TexturePivotBulkWindow : EditorWindow
    {
        private enum TargetMode
        {
            SelectedAssets = 0,
            Folder = 1
        }

        private enum PivotPreset
        {
            Custom = 0,
            Center = 1,
            BottomCenter = 2,
            TopCenter = 3,
            LeftCenter = 4,
            RightCenter = 5,
            BottomLeft = 6,
            BottomRight = 7,
            TopLeft = 8,
            TopRight = 9
        }

        private const string MenuPath = "Tools/2DJumpStart/Texture Pivot Bulk";

        [SerializeField] private TargetMode targetMode = TargetMode.SelectedAssets;
        [SerializeField] private PivotPreset pivotPreset = PivotPreset.Custom;
        [SerializeField] private Vector2 customPivot = new Vector2(0.5f, 0.5f);
        [SerializeField] private bool includeSelectedSprites = true;
        [SerializeField] private bool includeSelectedTextures = true;
        [SerializeField] private bool includeFolderContents = true;
        [SerializeField] private DefaultAsset targetFolder;

        private Vector2 scrollPosition;
        private string statusMessage = "Select sprites, textures, or a folder, then apply a pivot in bulk.";
        private int processedCount;
        private int skippedCount;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<TexturePivotBulkWindow>("Texture Pivot Bulk");
        }

        private void OnGUI()
        {
            GUILayout.Label("Texture Pivot Bulk", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Batch-edit sprite pivot points without opening the Sprite Editor one asset at a time.", MessageType.Info);

            EditorGUILayout.Space(4f);

            targetMode = (TargetMode)EditorGUILayout.EnumPopup("Target Mode", targetMode);

            EditorGUI.BeginChangeCheck();
            pivotPreset = (PivotPreset)EditorGUILayout.EnumPopup("Pivot Preset", pivotPreset);
            if (EditorGUI.EndChangeCheck())
            {
                SyncCustomPivotFromPreset();
            }

            if (pivotPreset == PivotPreset.Custom)
            {
                customPivot = EditorGUILayout.Vector2Field("Custom Pivot", ClampPivot(customPivot));
            }
            else
            {
                EditorGUILayout.Vector2Field("Pivot Value", ClampPivot(GetPivotValue()));
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Selection Filters", EditorStyles.boldLabel);
            includeSelectedSprites = EditorGUILayout.ToggleLeft("Process selected Sprite assets", includeSelectedSprites);
            includeSelectedTextures = EditorGUILayout.ToggleLeft("Process selected Texture2D assets", includeSelectedTextures);
            includeFolderContents = EditorGUILayout.ToggleLeft("Process all sprite textures inside folder", includeFolderContents);

            if (targetMode == TargetMode.Folder)
            {
                targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder", targetFolder, typeof(DefaultAsset), false);
            }

            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Pivot", GUILayout.Height(30f)))
            {
                ApplyPivotBulk();
            }

            if (GUILayout.Button("Use Current Selection", GUILayout.Height(30f)))
            {
                LoadSelectionHint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Last Run", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(110f));
            EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField($"Processed: {processedCount}");
            EditorGUILayout.LabelField($"Skipped: {skippedCount}");
            EditorGUILayout.EndScrollView();
        }

        private void LoadSelectionHint()
        {
            Object[] selection = Selection.objects;
            if (selection == null || selection.Length == 0)
            {
                statusMessage = "Nothing selected.";
                return;
            }

            targetMode = TargetMode.SelectedAssets;
            statusMessage = $"Loaded {selection.Length} selected object(s) as the processing source.";
        }

        private void ApplyPivotBulk()
        {
            Vector2 pivot = ClampPivot(GetPivotValue());
            Object[] targets = CollectTargets();

            processedCount = 0;
            skippedCount = 0;

            if (targets.Length == 0)
            {
                statusMessage = "No matching sprite assets were found.";
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!ApplyPivotToTarget(targets[i], pivot))
                    {
                        skippedCount++;
                        continue;
                    }

                    processedCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            statusMessage = $"Done. Processed {processedCount} asset(s), skipped {skippedCount}.";
        }

        private Object[] CollectTargets()
        {
            List<Object> results = new List<Object>(32);

            if (targetMode == TargetMode.SelectedAssets)
            {
                Object[] selection = Selection.objects;
                if (selection == null || selection.Length == 0)
                {
                    return results.ToArray();
                }

                for (int i = 0; i < selection.Length; i++)
                {
                    Object asset = selection[i];
                    if (asset == null)
                    {
                        continue;
                    }

                    if (asset is Sprite)
                    {
                        if (includeSelectedSprites)
                        {
                            results.Add(asset);
                        }
                        continue;
                    }

                    if (asset is Texture2D)
                    {
                        if (includeSelectedTextures)
                        {
                            results.Add(asset);
                        }
                        continue;
                    }

                    DefaultAsset folder = asset as DefaultAsset;
                    if (folder != null && includeFolderContents)
                    {
                        AddFolderTextures(folder, results);
                    }
                }

                return results.ToArray();
            }

            if (targetFolder != null && includeFolderContents)
            {
                AddFolderTextures(targetFolder, results);
            }

            return results.ToArray();
        }

        private void AddFolderTextures(DefaultAsset folder, List<Object> results)
        {
            string folderPath = AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite)
                {
                    continue;
                }

                results.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
        }

        private bool ApplyPivotToTarget(Object target, Vector2 pivot)
        {
            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            if (target is Sprite sprite)
            {
                return ApplyPivotToSprite(importer, sprite.name, pivot);
            }

            if (target is Texture2D)
            {
                return ApplyPivotToTexture(importer, pivot);
            }

            return false;
        }

        private bool ApplyPivotToTexture(TextureImporter importer, Vector2 pivot)
        {
            if (importer.textureType != TextureImporterType.Sprite)
            {
                return false;
            }

            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                importer.spritePivot = pivot;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                return true;
            }

            SpriteMetaData[] sprites = importer.spritesheet;
            if (sprites == null || sprites.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteMetaData data = sprites[i];
                data.alignment = (int)SpriteAlignment.Custom;
                data.pivot = pivot;
                sprites[i] = data;
            }

            importer.spritesheet = sprites;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return true;
        }

        private bool ApplyPivotToSprite(TextureImporter importer, string spriteName, Vector2 pivot)
        {
            if (importer.textureType != TextureImporterType.Sprite)
            {
                return false;
            }

            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                importer.spritePivot = pivot;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                return true;
            }

            SpriteMetaData[] sprites = importer.spritesheet;
            if (sprites == null || sprites.Length == 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteMetaData data = sprites[i];
                if (data.name != spriteName)
                {
                    continue;
                }

                data.alignment = (int)SpriteAlignment.Custom;
                data.pivot = pivot;
                sprites[i] = data;
                changed = true;
                break;
            }

            if (!changed)
            {
                return false;
            }

            importer.spritesheet = sprites;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return true;
        }

        private void SyncCustomPivotFromPreset()
        {
            switch (pivotPreset)
            {
                case PivotPreset.Center:
                    customPivot = new Vector2(0.5f, 0.5f);
                    break;
                case PivotPreset.BottomCenter:
                    customPivot = new Vector2(0.5f, 0f);
                    break;
                case PivotPreset.TopCenter:
                    customPivot = new Vector2(0.5f, 1f);
                    break;
                case PivotPreset.LeftCenter:
                    customPivot = new Vector2(0f, 0.5f);
                    break;
                case PivotPreset.RightCenter:
                    customPivot = new Vector2(1f, 0.5f);
                    break;
                case PivotPreset.BottomLeft:
                    customPivot = new Vector2(0f, 0f);
                    break;
                case PivotPreset.BottomRight:
                    customPivot = new Vector2(1f, 0f);
                    break;
                case PivotPreset.TopLeft:
                    customPivot = new Vector2(0f, 1f);
                    break;
                case PivotPreset.TopRight:
                    customPivot = new Vector2(1f, 1f);
                    break;
            }
        }

        private Vector2 GetPivotValue()
        {
            if (pivotPreset == PivotPreset.Custom)
            {
                return ClampPivot(customPivot);
            }

            switch (pivotPreset)
            {
                case PivotPreset.Center:
                    return new Vector2(0.5f, 0.5f);
                case PivotPreset.BottomCenter:
                    return new Vector2(0.5f, 0f);
                case PivotPreset.TopCenter:
                    return new Vector2(0.5f, 1f);
                case PivotPreset.LeftCenter:
                    return new Vector2(0f, 0.5f);
                case PivotPreset.RightCenter:
                    return new Vector2(1f, 0.5f);
                case PivotPreset.BottomLeft:
                    return new Vector2(0f, 0f);
                case PivotPreset.BottomRight:
                    return new Vector2(1f, 0f);
                case PivotPreset.TopLeft:
                    return new Vector2(0f, 1f);
                case PivotPreset.TopRight:
                    return new Vector2(1f, 1f);
                default:
                    return ClampPivot(customPivot);
            }
        }

        private static Vector2 ClampPivot(Vector2 pivot)
        {
            return new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
        }
    }
}
#endif
