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
            public Texture2D texture;
            public string name;
            public Vector2Int resolution;
            public long sizeBytes;
            public string formattedSize;
            public string compression;
        }

        private List<TextureData> textureList = new List<TextureData>();
        private Vector2 scrollPosition;
        
        // Pagination and Sorting
        private int itemsPerPage = 20;
        private int currentPage = 0;

        // Column Resizing State
        private float[] colWidths = { 40f, 50f, 200f, 100f, 100f, 120f };
        private int resizingColumnIndex = -1;

        // Reflection for accurate size
        private static MethodInfo getStorageMemorySizeMethod;

        // Cached GUI Styles for performance
        private GUIStyle centerLabelStyle;
        private GUIStyle leftLabelStyle;

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

            GUILayout.Label("Texture Observer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scan your project to find large textures. Click an entry to find it in the Project tab.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            itemsPerPage = Mathf.Max(1, EditorGUILayout.IntField("Items Per Page", itemsPerPage));
            
            if (GUILayout.Button("Scan Project Textures", GUILayout.Height(21)))
            {
                ScanTextures();
                currentPage = 0;
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (textureList.Count > 0)
            {
                int totalPages = Mathf.CeilToInt((float)textureList.Count / itemsPerPage);
                currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

                // Pagination Navigation
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUI.BeginDisabledGroup(currentPage <= 0);
                if (GUILayout.Button("Previous", GUILayout.Width(100))) currentPage--;
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"Page {currentPage + 1} of {totalPages} ({textureList.Count} total textures)");
                GUILayout.FlexibleSpace();

                EditorGUI.BeginDisabledGroup(currentPage >= totalPages - 1);
                if (GUILayout.Button("Next", GUILayout.Width(100))) currentPage++;
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                DrawHeader();
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                
                int startIndex = currentPage * itemsPerPage;
                int endIndex = Mathf.Min(startIndex + itemsPerPage, textureList.Count);

                for (int i = startIndex; i < endIndex; i++)
                {
                    DrawTextureRow(i, textureList[i]);
                }
                EditorGUILayout.EndScrollView();
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

        private void DrawHeader()
        {
            // Use ExpandWidth to ensure the header matches the window width
            Rect headerRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.ExpandWidth(true));
            string[] headers = { "No.", "Thumb", "Name", "Resolution", "Size", "Format" };
            
            float currentX = headerRect.x;
            for (int i = 0; i < colWidths.Length; i++)
            {
                // If it's the last column, calculate remaining width instead of using colWidths[i]
                float width = (i == colWidths.Length - 1) 
                    ? Mathf.Max(50, headerRect.width - (currentX - headerRect.x)) 
                    : colWidths[i];

                Rect colRect = new Rect(currentX, headerRect.y, width, headerRect.height);
                
                // Draw header box and label
                GUI.Box(colRect, headers[i], EditorStyles.toolbarButton);
                
                // Handle Resizing (Only for columns 0 to 4)
                if (i < colWidths.Length - 1)
                {
                    Rect resizeHandleRect = new Rect(currentX + width - 2, headerRect.y, 4, headerRect.height);
                    EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

                    if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
                    {
                        resizingColumnIndex = i;
                    }
                }

                currentX += width;
            }

            // Global mouse events for resizing
            if (resizingColumnIndex != -1)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    colWidths[resizingColumnIndex] += Event.current.delta.x;
                    colWidths[resizingColumnIndex] = Mathf.Max(20, colWidths[resizingColumnIndex]);
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    resizingColumnIndex = -1;
                }
            }
        }

        private void DrawTextureRow(int index, TextureData data)
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
            GUI.DrawTexture(new Rect(x + (colWidths[1]-30)/2, rowRect.y + 5, 30, 30), data.texture, ScaleMode.ScaleToFit); x += colWidths[1];
            GUI.Label(new Rect(x + 5, rowRect.y + 12, colWidths[2] - 5, 20), data.name, leftLabelStyle); x += colWidths[2];
            GUI.Label(new Rect(x, rowRect.y + 12, colWidths[3], 20), $"{data.resolution.x}x{data.resolution.y}", centerLabelStyle); x += colWidths[3];
            GUI.Label(new Rect(x, rowRect.y + 12, colWidths[4], 20), data.formattedSize, centerLabelStyle); x += colWidths[4];
            
            // The last column fills the remaining width of the rowRect
            float lastColWidth = rowRect.width - (x - rowRect.x);
            GUI.Label(new Rect(x, rowRect.y + 12, lastColWidth, 20), data.compression, centerLabelStyle);
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

            // Find all texture assets in the Assets folder
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (tex != null)
                {
                    long bytes = GetStorageSize(tex);
                    
                    textureList.Add(new TextureData
                    {
                        texture = tex,
                        name = tex.name,
                        resolution = new Vector2Int(tex.width, tex.height),
                        sizeBytes = bytes,
                        formattedSize = FormatBytes(bytes),
                        compression = tex.format.ToString()
                    });
                }
            }

            // Sort by size descending (largest first)
            textureList.Sort((a, b) => b.sizeBytes.CompareTo(a.sizeBytes));
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
