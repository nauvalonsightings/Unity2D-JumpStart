#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;

namespace Unity2DJumpStart
{
    /// <summary>
    /// A utility to quickly find objects in the active scene based on names, tags, layers, or components
    /// without the hierarchy view collapsing or losing context.
    /// </summary>
    public class SceneScourer : EditorWindow
    {
        private class ScourResult
        {
            public GameObject gameObject;
            public string hierarchyPath;
        }

        private enum SearchMode
        {
            NamePrefix,
            Tag,
            Layer,
            Component
        }

        // Search Criteria
        private SearchMode currentMode = SearchMode.NamePrefix;
        private string nameSearch = "";
        private string tagSearch = "Untagged";
        private int layerSearch = 0;
        private int componentIndex = 0;
        private string customComponent = "";
        private bool requireSpecificComponent = false;

        private readonly string[] popularComponents = { 
            "Custom...", 
            "SpriteRenderer", 
            "Rigidbody2D", 
            "BoxCollider2D", 
            "CircleCollider2D", 
            "Animator", 
            "AudioSource", 
            "Canvas",
            "Image",
            "RawImage"
        };

        // Results and State
        private List<ScourResult> results = new List<ScourResult>();
        private Vector2 scrollPosition;
        private string targetComponentQuery = ""; // Cached before traversal

        // Pagination
        private int itemsPerPage = 50;
        private int currentPage = 0;

        // Cached GUI Styles
        private GUIStyle resultButtonStyle;
        private GUIStyle pathLabelStyle;
        private GUIStyle toolbarLabelStyle;

        [MenuItem("Tools/2DJumpStart/Scene Scourer")]
        public static void ShowWindow()
        {
            GetWindow<SceneScourer>("Scene Scourer");
        }

        private void OnGUI()
        {
            // Initialize Styles
            if (resultButtonStyle == null)
            {
                resultButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 22
                };
            }
            if (pathLabelStyle == null)
            {
                pathLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                    margin = new RectOffset(5, 0, 4, 0)
                };
            }
            if (toolbarLabelStyle == null)
            {
                toolbarLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            GUILayout.Label("Scene Scourer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Find and select objects in the current scene. Search by Name, Tag, Layer, or Component type.", MessageType.Info);

            EditorGUILayout.Space();

            // Filter Configuration
            EditorGUI.BeginChangeCheck();
            currentMode = (SearchMode)EditorGUILayout.EnumPopup("Search Mode", currentMode);
            if (EditorGUI.EndChangeCheck())
            {
                results.Clear();
            }

            switch (currentMode)
            {
                case SearchMode.NamePrefix:
                    nameSearch = EditorGUILayout.TextField("Name Contains", nameSearch);
                    break;
                case SearchMode.Tag:
                    tagSearch = EditorGUILayout.TagField("Target Tag", tagSearch);
                    break;
                case SearchMode.Layer:
                    layerSearch = EditorGUILayout.LayerField("Target Layer", layerSearch);
                    break;
                case SearchMode.Component:
                    componentIndex = EditorGUILayout.Popup("Component Type", componentIndex, popularComponents);
                    if (componentIndex == 0) // Custom...
                    {
                        customComponent = EditorGUILayout.TextField("Type Name", customComponent);
                    }
                    break;
            }

            // Secondary Filter (for Name, Tag, and Layer searches)
            if (currentMode != SearchMode.Component)
            {
                requireSpecificComponent = EditorGUILayout.Toggle("Require Component", requireSpecificComponent);
                if (requireSpecificComponent)
                {
                    EditorGUI.indentLevel++;
                    componentIndex = EditorGUILayout.Popup("Component Type", componentIndex, popularComponents);
                    if (componentIndex == 0) // Custom...
                        customComponent = EditorGUILayout.TextField("Type Name", customComponent);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Scour Active Scene", GUILayout.Height(30)))
            {
                ScourScene();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();

            // Draw Results
            if (results.Count > 0)
            {
                int totalPages = Math.Max(1, (int)Math.Ceiling((float)results.Count / itemsPerPage));
                currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);

                // Pagination Toolbar
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUI.BeginDisabledGroup(currentPage == 0);
                if (GUILayout.Button("<", EditorStyles.toolbarButton, GUILayout.Width(30))) currentPage--;
                EditorGUI.EndDisabledGroup();

                GUILayout.Label($"Page {currentPage + 1} of {totalPages} ({results.Count} matches)", toolbarLabelStyle);

                EditorGUI.BeginDisabledGroup(currentPage >= totalPages - 1);
                if (GUILayout.Button(">", EditorStyles.toolbarButton, GUILayout.Width(30))) currentPage++;
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                int startIndex = currentPage * itemsPerPage;
                int endIndex = Math.Min(startIndex + itemsPerPage, results.Count);

                for (int i = startIndex; i < endIndex; i++)
                {
                    var res = results[i];
                    if (res.gameObject == null) continue;

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(res.gameObject.name, resultButtonStyle, GUILayout.MaxWidth(250)))
                    {
                        EditorGUIUtility.PingObject(res.gameObject);
                        Selection.activeGameObject = res.gameObject;
                    }
                    GUILayout.Label(res.hierarchyPath, pathLabelStyle);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Perform a search to see results.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
        }

        private void ScourScene()
        {
            results.Clear();
            currentPage = 0;

            if (currentMode == SearchMode.Component || requireSpecificComponent)
                targetComponentQuery = componentIndex == 0 ? customComponent : popularComponents[componentIndex];
            else
                targetComponentQuery = "";

            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots) Traverse(root, "");
        }

        private void Traverse(GameObject obj, string parentPath)
        {
            string path = string.IsNullOrEmpty(parentPath) ? obj.name : $"{parentPath}/{obj.name}";
            bool isMatch = false;

            switch (currentMode)
            {
                case SearchMode.NamePrefix: isMatch = !string.IsNullOrEmpty(nameSearch) && obj.name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) >= 0; break;
                case SearchMode.Tag: isMatch = obj.CompareTag(tagSearch); break;
                case SearchMode.Layer: isMatch = obj.layer == layerSearch; break;
                case SearchMode.Component:
                    if (string.IsNullOrEmpty(targetComponentQuery)) break;
                    foreach (var c in obj.GetComponents<Component>())
                    {
                        if (c != null && c.GetType().Name.IndexOf(targetComponentQuery, StringComparison.OrdinalIgnoreCase) >= 0) { isMatch = true; break; }
                    }
                    break;
            }

            // Secondary Component Verification
            if (isMatch && currentMode != SearchMode.Component && requireSpecificComponent)
            {
                bool hasComponent = false;
                if (!string.IsNullOrEmpty(targetComponentQuery))
                {
                    foreach (var c in obj.GetComponents<Component>())
                    {
                        if (c != null && c.GetType().Name.IndexOf(targetComponentQuery, StringComparison.OrdinalIgnoreCase) >= 0) 
                            { hasComponent = true; break; }
                    }
                }
                isMatch = hasComponent;
            }

            if (isMatch) results.Add(new ScourResult { gameObject = obj, hierarchyPath = path });
            for (int i = 0; i < obj.transform.childCount; i++) Traverse(obj.transform.GetChild(i).gameObject, path);
        }
    }
}
#endif