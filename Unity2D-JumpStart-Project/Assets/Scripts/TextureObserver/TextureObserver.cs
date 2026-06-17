#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System.Reflection;

namespace Unity2DJumpStart
{
    /// <summary>
    /// A simple editor tool to find and sort textures by their imported memory size.
    /// This helps developers identify textures that are too large or uncompressed.
    /// </summary>
    public class TextureObserver : EditorWindow
    {
        // Simple data class to hold texture information for the list
        private class TextureData
        {
            public string path;
            public Texture2D texture;
            public string name;
            public Vector2Int resolution;
            public long sizeBytes;
            public string formattedSize;
            public string compression;
            public bool isDivisibleBy4;
            public bool isOptimized;
        }

        private List<TextureData> textureList = new List<TextureData>();
        private Vector2 scrollPosition;

        // Search and Target Folders
        private string searchQuery = "";
        private List<TextureData> filteredTextureList = new List<TextureData>();
        private List<DefaultAsset> targetFolders = new List<DefaultAsset>();
        
        // Pagination and Sorting
        private int itemsPerPage = 20;
        private int currentPage = 0;

        // Tabs and Layout
        private int currentTab = 0;

        // Column Resizing State
        private float[] colWidths = { 40f, 50f, 200f, 100f, 100f };
        private float[] bulkColWidths = { 40f, 40f, 150f, 80f, 80f, 130f, 180f };
        private int resizingColumnIndex = -1;

        // Reflection for accurate size
        private static MethodInfo getStorageMemorySizeMethod;

        // Cached GUI Styles for performance
        private GUIStyle centerLabelStyle;
        private GUIStyle leftLabelStyle;
        private GUIStyle wrappedButtonStyle;

        // Configurable Compression Settings
        private bool showCompressionConfig = false;
        private TextureImporterFormat optAndroid = TextureImporterFormat.ETC2_RGBA8;
        private TextureImporterFormat optIOS = TextureImporterFormat.ASTC_10x10;
        private TextureImporterFormat optStandalone = TextureImporterFormat.DXT5;
        private TextureImporterFormat optWebGL = TextureImporterFormat.ASTC_10x10;

        private bool balancedUsesDefault = true;
        private TextureImporterFormat balAndroid = TextureImporterFormat.ASTC_4x4;
        private TextureImporterFormat balIOS = TextureImporterFormat.ASTC_4x4;
        private TextureImporterFormat balStandalone = TextureImporterFormat.DXT1;
        private TextureImporterFormat balWebGL = TextureImporterFormat.ASTC_4x4;

        [MenuItem("Tools/2DJumpStart/TextureObserver")]
        public static void ShowWindow()
        {
            GetWindow<TextureObserver>("Texture Observer");
        }

        private void OnGUI()
        {
            if (centerLabelStyle == null)
            {
                centerLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
            }
            if (leftLabelStyle == null)
            {
                leftLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
            }
            if (wrappedButtonStyle == null)
            {
                wrappedButtonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true };
            }

            GUILayout.Label("Texture Observer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scan your project to find large textures. Click an entry to find it in the Project tab.", MessageType.Info);

            EditorGUILayout.Space();

            // Tabs
            int previousTab = currentTab;
            currentTab = GUILayout.Toolbar(currentTab, new string[] { "Texture Observer", "Compression Bulk Change" });
            if (currentTab != previousTab)
            {
                // Reset resize state when switching tabs to prevent state corruption
                resizingColumnIndex = -1;
            }
            
            if (currentTab == 1)
            {
                EditorGUILayout.Space();
                showCompressionConfig = EditorGUILayout.Foldout(showCompressionConfig, "Configure Compression Formats");
                if (showCompressionConfig)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("Optimized Formats", EditorStyles.boldLabel);
                    optAndroid = (TextureImporterFormat)EditorGUILayout.EnumPopup("Android", optAndroid);
                    optIOS = (TextureImporterFormat)EditorGUILayout.EnumPopup("iOS", optIOS);
                    optStandalone = (TextureImporterFormat)EditorGUILayout.EnumPopup("Standalone", optStandalone);
                    optWebGL = (TextureImporterFormat)EditorGUILayout.EnumPopup("WebGL", optWebGL);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Balanced Formats", EditorStyles.boldLabel);
                    balancedUsesDefault = EditorGUILayout.Toggle("Clear Override (Use Unity Default)", balancedUsesDefault);
                    if (!balancedUsesDefault)
                    {
                        balAndroid = (TextureImporterFormat)EditorGUILayout.EnumPopup("Android", balAndroid);
                        balIOS = (TextureImporterFormat)EditorGUILayout.EnumPopup("iOS", balIOS);
                        balStandalone = (TextureImporterFormat)EditorGUILayout.EnumPopup("Standalone", balStandalone);
                        balWebGL = (TextureImporterFormat)EditorGUILayout.EnumPopup("WebGL", balWebGL);
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();

            // Search Bar
            EditorGUI.BeginChangeCheck();
            searchQuery = EditorGUILayout.TextField("Search Name", searchQuery);
            if (EditorGUI.EndChangeCheck())
            {
                ApplySearchFilter();
            }

            EditorGUILayout.Space();

            // Target Folders Layout
            EditorGUILayout.LabelField("Target Folders (Leave empty to scan entire project):");
            for (int i = 0; i < targetFolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                targetFolders[i] = (DefaultAsset)EditorGUILayout.ObjectField(targetFolders[i], typeof(DefaultAsset), false);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    targetFolders.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Folder Slot", GUILayout.Width(120)))
            {
                targetFolders.Add(null);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            itemsPerPage = Mathf.Max(1, EditorGUILayout.IntField("Items Per Page", itemsPerPage));
            
            if (GUILayout.Button("Populate Textures", GUILayout.Height(21)))
            {
                ScanTextures();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (filteredTextureList.Count > 0)
            {
                int totalPages = Mathf.CeilToInt((float)filteredTextureList.Count / itemsPerPage);
                currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

                // Pagination Navigation
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUI.BeginDisabledGroup(currentPage <= 0);
                if (GUILayout.Button("Previous", GUILayout.Width(100))) currentPage--;
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"Page {currentPage + 1} of {totalPages} ({filteredTextureList.Count} total textures)");
                GUILayout.FlexibleSpace();

                EditorGUI.BeginDisabledGroup(currentPage >= totalPages - 1);
                if (GUILayout.Button("Next", GUILayout.Width(100))) currentPage++;
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                if (currentTab == 0)
                {
                    string[] headers = { "No.", "Thumb", "Name", "Resolution", "Size", "Format" };
                    DrawHeader(headers, colWidths);
                }
                else
                {
                    string[] headers = { "No.", "Thumb", "Name", "Resolution", "Size", "Divisible by 4?", "Action", "Highlight" };
                    DrawHeader(headers, bulkColWidths);
                }
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                
                int startIndex = currentPage * itemsPerPage;
                int endIndex = Mathf.Min(startIndex + itemsPerPage, filteredTextureList.Count);

                for (int i = startIndex; i < endIndex; i++)
                {
                    if (currentTab == 0) DrawTextureRowObserver(i, filteredTextureList[i]);
                    else DrawTextureRowBulk(i, filteredTextureList[i]);
                }
                EditorGUILayout.EndScrollView();

                // Giant Bulk Buttons at the bottom of the Bulk Tab
                if (currentTab == 1)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Bulk Optimized", GUILayout.Height(40))) 
                    {
                        BulkApplyOptimization(true);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Bulk Balanced Compression", GUILayout.Height(40))) 
                    {
                        BulkApplyOptimization(false);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("No data. Click Scan to start.");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawHeader(string[] headers, float[] widths)
        {
            // Use ExpandWidth to ensure the header matches the window width
            Rect headerRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.ExpandWidth(true));
            
            float currentX = headerRect.x;

            // Draw the fixed-width headers (all except the last one)
            for (int i = 0; i < widths.Length; i++)
            {
                Rect colRect = new Rect(currentX, headerRect.y, widths[i], headerRect.height);
                GUI.Box(colRect, headers[i], EditorStyles.toolbarButton);

                // Draw resize handle for this column
                Rect resizeHandleRect = new Rect(currentX + widths[i] - 2, headerRect.y, 4, headerRect.height);
                EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);
                if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
                {
                    resizingColumnIndex = i;
                }
                currentX += widths[i];
            }

            // Draw the last, stretchy header
            float lastColWidth = Mathf.Max(50, headerRect.width - currentX);
            Rect lastColRect = new Rect(currentX, headerRect.y, lastColWidth, headerRect.height);
            GUI.Box(lastColRect, headers[headers.Length - 1], EditorStyles.toolbarButton);

            // Global mouse events for resizing
            if (resizingColumnIndex != -1)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    // Check bounds before accessing, just in case
                    if (resizingColumnIndex < widths.Length)
                    {
                        widths[resizingColumnIndex] += Event.current.delta.x;
                        widths[resizingColumnIndex] = Mathf.Max(20, widths[resizingColumnIndex]);
                        Repaint();
                    }
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    resizingColumnIndex = -1;
                }
            }
        }

        private void DrawTextureRowObserver(int index, TextureData data)
        {
            // Use a button style for the whole row to make it interactive
            Rect rowRect = EditorGUILayout.GetControlRect(false, 40, GUILayout.ExpandWidth(true));
            
            if (GUI.Button(rowRect, ""))
            {
                EditorGUIUtility.PingObject(data.texture);
                Selection.activeObject = data.texture;
            }

            float x = rowRect.x;
            GUI.Label(new Rect(x, rowRect.y + 12, colWidths[0], 20), (index + 1).ToString(), centerLabelStyle); x += colWidths[0];
            
            Texture2D thumb = AssetPreview.GetAssetPreview(data.texture);
            if (thumb == null) thumb = AssetPreview.GetMiniThumbnail(data.texture);
            if (thumb == null) thumb = data.texture;
            
            GUI.DrawTexture(new Rect(x + (colWidths[1]-30)/2, rowRect.y + 5, 30, 30), thumb, ScaleMode.ScaleToFit); x += colWidths[1];
            GUI.Label(new Rect(x + 5, rowRect.y + 12, colWidths[2] - 5, 20), data.name, leftLabelStyle); x += colWidths[2];
            GUI.Label(new Rect(x, rowRect.y + 12, colWidths[3], 20), $"{data.resolution.x}x{data.resolution.y}", centerLabelStyle); x += colWidths[3];
            GUI.Label(new Rect(x, rowRect.y + 12, colWidths[4], 20), data.formattedSize, centerLabelStyle); x += colWidths[4];
            
            // The last column fills the remaining width of the rowRect
            float lastColWidth = rowRect.width - (x - rowRect.x);
            GUI.Label(new Rect(x, rowRect.y + 12, lastColWidth, 20), data.compression, centerLabelStyle);
        }

        private void DrawTextureRowBulk(int index, TextureData data)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, 40, GUILayout.ExpandWidth(true));
            
            float x = rowRect.x;
            GUI.Label(new Rect(x, rowRect.y + 12, bulkColWidths[0], 20), (index + 1).ToString(), centerLabelStyle); x += bulkColWidths[0];
            
            Texture2D thumb = AssetPreview.GetAssetPreview(data.texture);
            if (thumb == null) thumb = AssetPreview.GetMiniThumbnail(data.texture);
            if (thumb == null) thumb = data.texture;
            
            GUI.DrawTexture(new Rect(x + (bulkColWidths[1]-30)/2, rowRect.y + 5, 30, 30), thumb, ScaleMode.ScaleToFit); x += bulkColWidths[1];
            GUI.Label(new Rect(x + 5, rowRect.y + 12, bulkColWidths[2] - 5, 20), data.name, leftLabelStyle); x += bulkColWidths[2];
            GUI.Label(new Rect(x, rowRect.y + 12, bulkColWidths[3], 20), $"{data.resolution.x}x{data.resolution.y}", centerLabelStyle); x += bulkColWidths[3];
            GUI.Label(new Rect(x, rowRect.y + 12, bulkColWidths[4], 20), data.formattedSize, centerLabelStyle); x += bulkColWidths[4];
            
            string divisibleText = data.isDivisibleBy4 ? "Yes" : "No, Not dividable by 4";
            GUI.Label(new Rect(x, rowRect.y + 12, bulkColWidths[5], 20), divisibleText, centerLabelStyle); x += bulkColWidths[5];
            
            string btnText = data.isOptimized ? "Restore to the balanced settings" : "Set To Lightest Settings";
            
            // Fallback to 180f if the array is missing the 7th element
            float actionWidth = bulkColWidths.Length > 6 ? bulkColWidths[6] : 180f;
            if (GUI.Button(new Rect(x + 5, rowRect.y + 2, actionWidth - 10, 36), btnText, wrappedButtonStyle))
            {
                ApplyOptimization(data, !data.isOptimized);
                GUIUtility.ExitGUI();
            }
            x += actionWidth;

            float lastColWidth = rowRect.width - (x - rowRect.x);
            if (GUI.Button(new Rect(x + 5, rowRect.y + 10, lastColWidth - 10, 20), "Go-To"))
            {
                EditorGUIUtility.PingObject(data.texture);
                Selection.activeObject = data.texture;
            }
        }

        private long GetStorageSize(Texture2D tex)
        {
            if (getStorageMemorySizeMethod == null)
            {
                var type = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil");
                if (type != null)
                {
                    // Unity 2020+ uses GetStorageMemorySizeLong
                    getStorageMemorySizeMethod = type.GetMethod("GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new System.Type[] { typeof(Texture) }, null);
                    
                    if (getStorageMemorySizeMethod == null)
                    {
                        // Fallback to older Unity versions
                        getStorageMemorySizeMethod = type.GetMethod("GetStorageMemorySize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new System.Type[] { typeof(Texture) }, null);
                    }
                }
            }

            if (getStorageMemorySizeMethod != null && tex != null)
            {
                try
                {
                    var result = getStorageMemorySizeMethod.Invoke(null, new object[] { tex });
                    return System.Convert.ToInt64(result);
                }
                catch
                {
                    // Silently fall through to fallback if invocation fails
                }
            }
            
            // Fallback if reflection fails
            return tex != null ? Profiler.GetRuntimeMemorySizeLong(tex) : 0;
        }

        private void ScanTextures()
        {
            textureList.Clear();

            List<string> searchPaths = new List<string>();
            foreach (var folder in targetFolders)
            {
                if (folder != null)
                {
                    string path = AssetDatabase.GetAssetPath(folder);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        searchPaths.Add(path);
                    }
                }
            }

            if (searchPaths.Count == 0)
            {
                searchPaths.Add("Assets");
            }

            // Find all texture assets in the targeted paths
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", searchPaths.ToArray());

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (tex != null)
                {
                    long bytes = GetStorageSize(tex);
                    
                    textureList.Add(new TextureData
                    {
                        path = path,
                        texture = tex,
                        name = tex.name,
                        resolution = new Vector2Int(tex.width, tex.height),
                        sizeBytes = bytes,
                        formattedSize = FormatBytes(bytes),
                        compression = tex.format.ToString(),
                        isDivisibleBy4 = (tex.width % 4 == 0) && (tex.height % 4 == 0),
                        isOptimized = IsTextureOptimized(path)
                    });
                }
            }

            // Sort by size descending (largest first)
            textureList.Sort((a, b) => b.sizeBytes.CompareTo(a.sizeBytes));
            
            // Apply the filter instantly so the UI populates
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            filteredTextureList.Clear();
            if (string.IsNullOrEmpty(searchQuery))
            {
                filteredTextureList.AddRange(textureList);
            }
            else
            {
                string lowerQuery = searchQuery.ToLower();
                foreach (var tex in textureList)
                {
                    if (tex.name.ToLower().Contains(lowerQuery)) filteredTextureList.Add(tex);
                }
            }
            currentPage = 0;
        }

        private bool IsTextureOptimized(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;

            string activePlatform = EditorUserBuildSettings.activeBuildTarget.ToString();
            string platformKey = "Standalone";
            TextureImporterFormat targetFormat = optStandalone;

            if (activePlatform == "Android") { platformKey = "Android"; targetFormat = optAndroid; }
            else if (activePlatform == "iPhone" || activePlatform == "iOS") { platformKey = "iPhone"; targetFormat = optIOS; }
            else if (activePlatform == "WebGL") { platformKey = "WebGL"; targetFormat = optWebGL; }

            var settings = importer.GetPlatformTextureSettings(platformKey);
            
            // If overridden, check if it matches our configured optimized format exactly
            if (settings.overridden)
            {
                return settings.format == targetFormat;
            }
            
            return false;
        }

        private void ApplyOptimization(TextureData data, bool optimize)
        {
            TextureImporter importer = AssetImporter.GetAtPath(data.path) as TextureImporter;
            if (importer == null) return;

            if (optimize)
            {
                SetPlatformOptimized(importer, "Android", optAndroid);
                SetPlatformOptimized(importer, "iPhone", optIOS);
                SetPlatformOptimized(importer, "Standalone", optStandalone);
                SetPlatformOptimized(importer, "WebGL", optWebGL);
            }
            else
            {
                if (balancedUsesDefault)
                {
                    importer.ClearPlatformTextureSettings("Android");
                    importer.ClearPlatformTextureSettings("iPhone");
                    importer.ClearPlatformTextureSettings("Standalone");
                    importer.ClearPlatformTextureSettings("WebGL");
                }
                else
                {
                    SetPlatformOptimized(importer, "Android", balAndroid);
                    SetPlatformOptimized(importer, "iPhone", balIOS);
                    SetPlatformOptimized(importer, "Standalone", balStandalone);
                    SetPlatformOptimized(importer, "WebGL", balWebGL);
                }
            }

            importer.SaveAndReimport();
            data.isOptimized = optimize;
            
            // Refresh data fields
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(data.path);
            if (tex != null)
            {
                data.sizeBytes = GetStorageSize(tex);
                data.formattedSize = FormatBytes(data.sizeBytes);
                data.compression = tex.format.ToString();
            }
        }

        private void SetPlatformOptimized(TextureImporter importer, string platform, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.format = format;
            importer.SetPlatformTextureSettings(settings);
        }

        private void BulkApplyOptimization(bool optimize)
        {
            int count = filteredTextureList.Count;
            for (int i = 0; i < count; i++)
            {
                EditorUtility.DisplayProgressBar("Bulk Updating Textures", $"Processing {filteredTextureList[i].name} ({i + 1}/{count})", (float)i / count);
                
                if (filteredTextureList[i].isOptimized != optimize)
                {
                    ApplyOptimization(filteredTextureList[i], optimize);
                }
            }
            EditorUtility.ClearProgressBar();
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576f).ToString("F2") + " MB";
            if (bytes >= 1024) return (bytes / 1024f).ToString("F2") + " KB";
            return bytes + " Bytes";
        }
    }
}
#endif
