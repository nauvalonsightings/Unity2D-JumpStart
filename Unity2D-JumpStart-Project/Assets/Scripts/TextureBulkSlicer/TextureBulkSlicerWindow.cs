#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity2DJumpStart
{
    /// <summary>
    /// Applies the same 9-slice border values to multiple sprite textures.
    /// </summary>
    public sealed class TextureBulkSlicerWindow : EditorWindow
    {
        private enum TargetMode
        {
            Folder,
            Selection
        }

        private enum SliceTab
        {
            SingleSprite,
            MultipleSprites
        }

        private sealed class TextureData
        {
            public string path;
            public Texture2D texture;
            public string textureName;
            public string folder;
            public Vector2Int size;
            public Vector4 currentBorder;
            public int spriteCount;
            public bool hasMixedBorders;
            public bool canApply;
            public string skipReason;
        }

        private const string MenuPath = "Tools/2DJumpStart/Texture Bulk Slicer";

        [SerializeField] private SliceTab currentTab = SliceTab.SingleSprite;
        [SerializeField] private TargetMode targetMode = TargetMode.Folder;
        [SerializeField] private DefaultAsset targetFolder;
        [SerializeField] private float leftBorder;
        [SerializeField] private float rightBorder;
        [SerializeField] private float topBorder;
        [SerializeField] private float bottomBorder;

        private readonly List<TextureData> textures = new List<TextureData>();
        private readonly HashSet<string> discoveredPaths = new HashSet<string>();
        private Vector2 scrollPosition;
        private int currentPage;
        private int itemsPerPage = 20;
        private string statusMessage = "No textures loaded. Choose a target and click Populate Textures.";

        private GUIStyle centerLabelStyle;
        private GUIStyle leftLabelStyle;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<TextureBulkSlicerWindow>("Texture Bulk Slicer");
        }

        private void OnEnable()
        {
            minSize = new Vector2(760f, 430f);
        }

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.Label("Texture Bulk Slicer", EditorStyles.boldLabel);
            SliceTab previousTab = currentTab;
            currentTab = (SliceTab)GUILayout.Toolbar((int)currentTab, new[] { "Single Sprite", "Multiple Sprites" });
            if (previousTab != currentTab)
            {
                textures.Clear();
                currentPage = 0;
                statusMessage = "Tab changed. Click Populate Textures to scan the selected target.";
            }

            EditorGUILayout.HelpBox(
                currentTab == SliceTab.SingleSprite
                    ? "Apply the same 9-slice border values to multiple single-sprite textures. Values are measured in pixels."
                    : "Apply the same 9-slice border values to every sprite in multiple-sprite textures. Values are measured in pixels.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            targetMode = (TargetMode)EditorGUILayout.EnumPopup("Target Mode", targetMode);
            if (targetMode == TargetMode.Folder)
            {
                targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Target Folder", "All valid textures in this folder and its subfolders will be scanned."),
                    targetFolder,
                    typeof(DefaultAsset),
                    false);

                if (targetFolder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(targetFolder)))
                {
                    EditorGUILayout.HelpBox("The selected asset is not a valid folder.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("The tool will use selected texture or sprite assets from the Project window.", MessageType.None);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Border Values", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            leftBorder = Mathf.Max(0f, EditorGUILayout.FloatField("Left", leftBorder));
            rightBorder = Mathf.Max(0f, EditorGUILayout.FloatField("Right", rightBorder));
            topBorder = Mathf.Max(0f, EditorGUILayout.FloatField("Top", topBorder));
            bottomBorder = Mathf.Max(0f, EditorGUILayout.FloatField("Bottom", bottomBorder));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            itemsPerPage = Mathf.Max(1, EditorGUILayout.IntField("Textures Per Page", itemsPerPage));
            if (GUILayout.Button("Populate Textures", GUILayout.Height(21f)))
            {
                ScanTextures();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedLabel);

            if (textures.Count == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("No textures loaded.", centerLabelStyle);
                GUILayout.FlexibleSpace();
                return;
            }

            int totalPages = Mathf.CeilToInt((float)textures.Count / itemsPerPage);
            currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

            DrawPagination(totalPages);
            DrawHeader();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Mathf.Min(startIndex + itemsPerPage, textures.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                DrawTextureRow(i, textures[i]);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Eligible textures: {CountEligibleTextures()} | Skipped textures: {CountSkippedTextures()}");
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(CountEligibleTextures() == 0);
            if (GUILayout.Button("Apply Slice", GUILayout.Width(140f), GUILayout.Height(32f)))
            {
                ApplySliceToAll();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (centerLabelStyle == null)
            {
                centerLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (leftLabelStyle == null)
            {
                leftLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private void DrawPagination(int totalPages)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(currentPage <= 0);
            if (GUILayout.Button("Previous", GUILayout.Width(100f)))
            {
                currentPage--;
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Page {currentPage + 1} of {totalPages} ({textures.Count} total textures)");
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(currentPage >= totalPages - 1);
            if (GUILayout.Button("Next", GUILayout.Width(100f)))
            {
                currentPage++;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("No.", EditorStyles.toolbarButton, GUILayout.Width(45f));
            GUILayout.Label("Texture Name", EditorStyles.toolbarButton, GUILayout.Width(170f));
            GUILayout.Label("Folder Belong", EditorStyles.toolbarButton, GUILayout.Width(190f));
            GUILayout.Label("Resolution", EditorStyles.toolbarButton, GUILayout.Width(95f));
            GUILayout.Label(currentTab == SliceTab.SingleSprite ? "Current Border" : "Current Borders", EditorStyles.toolbarButton, GUILayout.Width(180f));
            GUILayout.Label("Status", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureRow(int index, TextureData data)
        {
            float rowHeight = data.canApply ? 34f : 48f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight, GUILayout.ExpandWidth(true));
            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                EditorGUIUtility.PingObject(data.texture);
                Selection.activeObject = data.texture;
            }

            float x = rowRect.x;
            GUI.Label(new Rect(x, rowRect.y + 7f, 45f, 20f), (index + 1).ToString(), centerLabelStyle);
            x += 45f;
            GUI.Label(new Rect(x + 5f, rowRect.y + 7f, 165f, 20f), data.textureName, leftLabelStyle);
            x += 170f;
            GUI.Label(new Rect(x + 5f, rowRect.y + 7f, 185f, 20f), data.folder, leftLabelStyle);
            x += 190f;
            GUI.Label(new Rect(x, rowRect.y + 7f, 95f, 20f), $"{data.size.x}x{data.size.y}", centerLabelStyle);
            x += 95f;
            string borderText = data.hasMixedBorders ? "Mixed" : FormatBorder(data.currentBorder);
            GUI.Label(new Rect(x, rowRect.y + 7f, 180f, 20f), borderText, centerLabelStyle);
            x += 180f;

            string status = data.canApply
                ? currentTab == SliceTab.MultipleSprites ? $"Ready ({data.spriteCount} sprites)" : "Ready"
                : data.skipReason;
            GUI.Label(new Rect(x + 5f, rowRect.y + 7f, rowRect.width - (x - rowRect.x) - 5f, 20f), status, leftLabelStyle);
        }

        private void ScanTextures()
        {
            textures.Clear();
            discoveredPaths.Clear();
            currentPage = 0;

            if (targetMode == TargetMode.Folder)
            {
                ScanFolder();
            }
            else
            {
                ScanSelection();
            }

            textures.Sort((first, second) => string.Compare(first.textureName, second.textureName, System.StringComparison.OrdinalIgnoreCase));
            statusMessage = $"Loaded {textures.Count} texture(s). Click a row to select it in the Project window.";
        }

        private void ScanFolder()
        {
            string folderPath = targetFolder == null ? "Assets" : AssetDatabase.GetAssetPath(targetFolder);
            if (targetFolder != null && !AssetDatabase.IsValidFolder(folderPath))
            {
                statusMessage = "The selected target is not a valid folder.";
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                AddTexture(AssetDatabase.GUIDToAssetPath(guids[i]));
            }
        }

        private void ScanSelection()
        {
            Object[] selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                statusMessage = "No assets are selected in the Project window.";
                return;
            }

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(selectedObjects[i]);
                AddTexture(path);
            }
        }

        private void AddTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !discoveredPaths.Add(path))
            {
                return;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            string skipReason = GetSkipReason(importer);
            Vector4 currentBorder = importer != null ? importer.spriteBorder : Vector4.zero;
            int spriteCount = 0;
            bool hasMixedBorders = false;

            if (importer != null && currentTab == SliceTab.MultipleSprites && string.IsNullOrEmpty(skipReason))
            {
                SpriteMetaData[] spriteSheet = importer.spritesheet;
                spriteCount = spriteSheet == null ? 0 : spriteSheet.Length;
                if (spriteCount > 0)
                {
                    currentBorder = spriteSheet[0].border;
                    for (int i = 1; i < spriteSheet.Length; i++)
                    {
                        if (spriteSheet[i].border != currentBorder)
                        {
                            hasMixedBorders = true;
                            break;
                        }
                    }
                }
            }

            textures.Add(new TextureData
            {
                path = path,
                texture = texture,
                textureName = texture.name,
                folder = GetFolderName(path),
                size = new Vector2Int(texture.width, texture.height),
                currentBorder = currentBorder,
                spriteCount = spriteCount,
                hasMixedBorders = hasMixedBorders,
                canApply = string.IsNullOrEmpty(skipReason),
                skipReason = skipReason
            });
        }

        private string GetSkipReason(TextureImporter importer)
        {
            if (importer == null)
            {
                return "Skipped: invalid importer";
            }

            if (importer.textureType != TextureImporterType.Sprite)
            {
                return "Skipped: not a Sprite texture";
            }

            SpriteImportMode expectedMode = currentTab == SliceTab.SingleSprite ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            if (importer.spriteImportMode != expectedMode)
            {
                return currentTab == SliceTab.SingleSprite
                    ? "Skipped: Sprite Mode is Multiple"
                    : "Skipped: Sprite Mode is Single";
            }

            if (currentTab == SliceTab.MultipleSprites &&
                (importer.spritesheet == null || importer.spritesheet.Length == 0))
            {
                return "Skipped: no sprite metadata";
            }

            return string.Empty;
        }

        private void ApplySliceToAll()
        {
            int eligibleCount = CountEligibleTextures();
            int processedCount = 0;
            int clampedCount = 0;
            int affectedSpriteCount = 0;

            if (!EditorUtility.DisplayDialog(
                "Apply Texture Borders",
                currentTab == SliceTab.SingleSprite
                    ? "This will change the sprite border values and reimport all eligible single-sprite textures in the current list."
                    : "This will change the border values of every sprite in the eligible multiple-sprite textures and reimport them.",
                "Apply",
                "Cancel"))
            {
                return;
            }

            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    TextureData data = textures[i];
                    if (!data.canApply)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Applying Texture Borders",
                        $"Processing {data.textureName} ({processedCount + 1}/{eligibleCount})",
                        (float)processedCount / eligibleCount);

                    TextureImporter importer = AssetImporter.GetAtPath(data.path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    if (currentTab == SliceTab.SingleSprite)
                    {
                        Vector4 border = GetClampedBorder(data.size, out bool wasClamped);
                        if (wasClamped)
                        {
                            clampedCount++;
                        }

                        importer.spriteBorder = border;
                        affectedSpriteCount++;
                    }
                    else
                    {
                        SpriteMetaData[] spriteSheet = importer.spritesheet;
                        if (spriteSheet == null || spriteSheet.Length == 0)
                        {
                            continue;
                        }
                        for (int spriteIndex = 0; spriteIndex < spriteSheet.Length; spriteIndex++)
                        {
                            Vector2Int spriteSize = new Vector2Int(
                                Mathf.RoundToInt(spriteSheet[spriteIndex].rect.width),
                                Mathf.RoundToInt(spriteSheet[spriteIndex].rect.height));
                            spriteSheet[spriteIndex].border = GetClampedBorder(spriteSize, out bool wasClamped);
                            if (wasClamped)
                            {
                                clampedCount++;
                            }

                            affectedSpriteCount++;
                        }

                        importer.spritesheet = spriteSheet;
                    }

                    importer.SaveAndReimport();
                    processedCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ScanTextures();
            statusMessage = $"Applied borders to {affectedSpriteCount} sprite(s) across {processedCount} texture(s). {clampedCount} border value(s) required clamping.";
        }

        private Vector4 GetClampedBorder(Vector2Int textureSize, out bool wasClamped)
        {
            float left = Mathf.Clamp(leftBorder, 0f, textureSize.x);
            float right = Mathf.Clamp(rightBorder, 0f, textureSize.x - left);
            float top = Mathf.Clamp(topBorder, 0f, textureSize.y);
            float bottom = Mathf.Clamp(bottomBorder, 0f, textureSize.y - top);

            wasClamped = !Mathf.Approximately(left, leftBorder) ||
                         !Mathf.Approximately(right, rightBorder) ||
                         !Mathf.Approximately(top, topBorder) ||
                         !Mathf.Approximately(bottom, bottomBorder);

            // Unity stores the border as Left, Bottom, Right, Top.
            return new Vector4(left, bottom, right, top);
        }

        private int CountEligibleTextures()
        {
            int count = 0;
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i].canApply)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountSkippedTextures()
        {
            return textures.Count - CountEligibleTextures();
        }

        private static string GetFolderName(string assetPath)
        {
            string folderPath = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(folderPath) ? "Assets" : folderPath.Replace("\\", "/");
        }

        private static string FormatBorder(Vector4 border)
        {
            return $"L {border.x:0}  B {border.y:0}  R {border.z:0}  T {border.w:0}";
        }
    }
}
#endif
