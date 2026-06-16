#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Profiling;

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
        }

        private List<TextureData> textureList = new List<TextureData>();
        private Vector2 scrollPosition;
        private int itemsPerPage = 20;
        private int currentPage = 0;

        [MenuItem("Tools/2DJumpStart/TextureObserver")]
        public static void ShowWindow()
        {
            GetWindow<TextureObserver>("Texture Observer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Texture Observer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scan your project to find large textures. Click an entry to find it in the Project tab.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            itemsPerPage = Mathf.Max(1, EditorGUILayout.IntField("Items Per Page", itemsPerPage));
            
            if (GUILayout.Button("Scan Project Textures", GUILayout.Height(21)))
            {
                ScanTextures();
                currentPage = 0;
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
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("No.", GUILayout.Width(30));
            GUILayout.Label("Thumb", GUILayout.Width(40));
            GUILayout.Label("Name", GUILayout.ExpandWidth(true));
            GUILayout.Label("Resolution", GUILayout.Width(100));
            GUILayout.Label("Imported Size", GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureRow(int index, TextureData data)
        {
            // Use a button style for the whole row to make it interactive
            if (GUILayout.Button("", GUILayout.Height(40)))
            {
                EditorGUIUtility.PingObject(data.texture);
                Selection.activeObject = data.texture;
            }

            // Move the cursor back up so we can draw our custom content over the button area
            Rect lastRect = GUILayoutUtility.GetLastRect();
            GUI.BeginGroup(lastRect);

            float yOffset = 12; // Vertical centering for text
            GUI.Label(new Rect(5, yOffset, 30, 20), (index + 1).ToString());
            GUI.DrawTexture(new Rect(40, 5, 30, 30), data.texture, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(80, yOffset, lastRect.width - 290, 20), data.name);
            GUI.Label(new Rect(lastRect.width - 200, yOffset, 90, 20), $"{data.resolution.x}x{data.resolution.y}");
            GUI.Label(new Rect(lastRect.width - 100, yOffset, 90, 20), data.formattedSize);

            GUI.EndGroup();
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
                    long bytes = Profiler.GetRuntimeMemorySizeLong(tex);
                    
                    textureList.Add(new TextureData
                    {
                        texture = tex,
                        name = tex.name,
                        resolution = new Vector2Int(tex.width, tex.height),
                        sizeBytes = bytes,
                        formattedSize = FormatBytes(bytes)
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
