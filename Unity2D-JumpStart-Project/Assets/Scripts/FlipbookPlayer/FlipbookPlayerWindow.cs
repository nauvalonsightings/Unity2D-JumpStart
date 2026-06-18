#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity2DJumpStart
{
    public sealed class FlipbookPlayerWindow : EditorWindow
    {
        private enum SourceMode
        {
            SpriteSequence = 0,
            Folder = 1,
            Spritesheet = 2
        }

        private const string MenuPath = "Tools/2DJumpStart/Flipbook Player";

        private SourceMode _sourceMode = SourceMode.SpriteSequence;
        private readonly List<Sprite> _sequenceSprites = new List<Sprite>();
        private DefaultAsset _folderAsset;
        private Texture2D _spritesheetTexture;

        private readonly List<Sprite> _cachedFrames = new List<Sprite>();
        private readonly List<string> _cachedFrameKeys = new List<string>();

        private Vector2 _sequenceScroll;
        private bool _isPlaying;
        private bool _loop = true;
        private float _fps = 12f;
        private double _lastFrameTime;
        private int _currentFrameIndex;

        private GUIStyle _previewBoxStyle;
        private GUIStyle _centerLabelStyle;
        private GUIStyle _frameLabelStyle;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<FlipbookPlayerWindow>("Flipbook Player");
        }

        private void OnEnable()
        {
            RefreshFrames();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _isPlaying = false;
        }

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.Label("Flipbook Player", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Preview a sprite animation from a sequence, folder, or sliced spritesheet.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _sourceMode = (SourceMode)EditorGUILayout.EnumPopup("Source Mode", _sourceMode);
            if (EditorGUI.EndChangeCheck())
            {
                StopPlayback();
                RefreshFrames();
            }

            EditorGUILayout.Space(4f);

            switch (_sourceMode)
            {
                case SourceMode.SpriteSequence:
                    DrawSpriteSequenceSource();
                    break;
                case SourceMode.Folder:
                    DrawFolderSource();
                    break;
                case SourceMode.Spritesheet:
                    DrawSpritesheetSource();
                    break;
            }

            EditorGUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            _fps = EditorGUILayout.Slider("FPS", _fps, 1f, 60f);
            _loop = EditorGUILayout.Toggle("Loop", _loop);
            if (EditorGUI.EndChangeCheck() && _isPlaying)
            {
                _lastFrameTime = EditorApplication.timeSinceStartup;
            }

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_isPlaying ? "Stop" : "Play", GUILayout.Height(28f)))
            {
                if (_isPlaying)
                {
                    StopPlayback();
                }
                else
                {
                    StartPlayback();
                }
            }

            if (GUILayout.Button("Refresh Frames", GUILayout.Height(28f)))
            {
                RefreshFrames();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);

            DrawPreviewArea();
        }

        private void DrawSpriteSequenceSource()
        {
            EditorGUILayout.LabelField("Sprite Sequence", EditorStyles.boldLabel);
            DrawSpriteDropArea();

            bool changed = false;
            int removeIndex = -1;
            _sequenceScroll = EditorGUILayout.BeginScrollView(_sequenceScroll, GUILayout.MinHeight(120f));
            for (int i = 0; i < _sequenceSprites.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                _sequenceSprites[i] = (Sprite)EditorGUILayout.ObjectField(_sequenceSprites[i], typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                {
                    changed = true;
                }
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (removeIndex >= 0)
            {
                _sequenceSprites.RemoveAt(removeIndex);
                changed = true;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Sprite Slot", GUILayout.Width(120f)))
            {
                _sequenceSprites.Add(null);
                changed = true;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                RefreshFrames();
            }
        }

        private void DrawSpriteDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
            Event currentEvent = Event.current;

            bool hover = dropRect.Contains(currentEvent.mousePosition);
            Color fill = hover ? new Color(0.24f, 0.32f, 0.42f) : new Color(0.19f, 0.19f, 0.19f);
            Color border = hover ? new Color(0.42f, 0.65f, 0.85f) : new Color(0.33f, 0.33f, 0.33f);

            EditorGUI.DrawRect(dropRect, fill);
            Handles.BeginGUI();
            Handles.color = border;
            Handles.DrawAAPolyLine(2f,
                new Vector3(dropRect.x, dropRect.y),
                new Vector3(dropRect.xMax, dropRect.y),
                new Vector3(dropRect.xMax, dropRect.yMax),
                new Vector3(dropRect.x, dropRect.yMax),
                new Vector3(dropRect.x, dropRect.y));
            Handles.EndGUI();

            GUI.Label(dropRect, "Drag and drop Sprite assets here\nMulti-select supported", _centerLabelStyle);

            if (!hover)
            {
                return;
            }

            EventType type = currentEvent.type;
            if (type != EventType.DragUpdated && type != EventType.DragPerform)
            {
                return;
            }

            bool hasSprite = false;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                if (DragAndDrop.objectReferences[i] is Sprite)
                {
                    hasSprite = true;
                    break;
                }
            }

            if (!hasSprite)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AppendDraggedSprites(DragAndDrop.objectReferences);
                currentEvent.Use();
            }
            else
            {
                currentEvent.Use();
            }
        }

        private void AppendDraggedSprites(UnityEngine.Object[] draggedObjects)
        {
            bool changed = false;
            for (int i = 0; i < draggedObjects.Length; i++)
            {
                if (draggedObjects[i] is Sprite sprite)
                {
                    _sequenceSprites.Add(sprite);
                    changed = true;
                }
            }

            if (changed)
            {
                RefreshFrames();
            }
        }

        private void DrawFolderSource()
        {
            EditorGUI.BeginChangeCheck();
            _folderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Folder", _folderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshFrames();
            }

            EditorGUILayout.HelpBox("This scans the selected folder for Sprite assets and sliced sub-sprites.", MessageType.None);
        }

        private void DrawSpritesheetSource()
        {
            EditorGUI.BeginChangeCheck();
            _spritesheetTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", _spritesheetTexture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshFrames();
            }

            EditorGUILayout.HelpBox("The texture must be imported as Multiple and sliced into sprite sub-assets.", MessageType.None);
        }

        private void DrawPreviewArea()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(10f, 260f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.17f, 0.17f, 0.17f));
            GUI.Box(previewRect, GUIContent.none, _previewBoxStyle);

            if (_cachedFrames.Count == 0)
            {
                GUI.Label(previewRect, "No frames loaded.", _centerLabelStyle);
                return;
            }

            Sprite sprite = _cachedFrames[Mathf.Clamp(_currentFrameIndex, 0, _cachedFrames.Count - 1)];
            if (sprite == null)
            {
                GUI.Label(previewRect, "Current frame is missing.", _centerLabelStyle);
                return;
            }

            Texture2D texture = sprite.texture;
            Rect spriteRect = sprite.textureRect;
            Rect drawRect = FitRect(previewRect, spriteRect.width, spriteRect.height);

            GUI.DrawTextureWithTexCoords(drawRect, texture, GetNormalizedUV(spriteRect, texture));
            GUI.Label(
                new Rect(previewRect.x + 8f, previewRect.y + 8f, previewRect.width - 16f, 20f),
                $"{_currentFrameIndex + 1}/{_cachedFrames.Count}  {sprite.name}",
                _frameLabelStyle
            );

        }

        private void RefreshFrames()
        {
            _cachedFrames.Clear();
            _cachedFrameKeys.Clear();
            _currentFrameIndex = 0;

            switch (_sourceMode)
            {
                case SourceMode.SpriteSequence:
                    for (int i = 0; i < _sequenceSprites.Count; i++)
                    {
                        AddFrame(_sequenceSprites[i], false);
                    }
                    break;
                case SourceMode.Folder:
                    LoadFramesFromFolder(_folderAsset);
                    break;
                case SourceMode.Spritesheet:
                    LoadFramesFromSpritesheet(_spritesheetTexture);
                    break;
            }

            Repaint();
        }

        private void LoadFramesFromFolder(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                UnityEngine.Object[] representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                for (int j = 0; j < representations.Length; j++)
                {
                    if (representations[j] is Sprite sprite)
                    {
                        AddFrame(sprite, true);
                    }
                }

                Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                AddFrame(mainSprite, true);
            }
        }

        private void LoadFramesFromSpritesheet(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            UnityEngine.Object[] representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            if (representations == null)
            {
                return;
            }

            for (int i = 0; i < representations.Length; i++)
            {
                if (representations[i] is Sprite sprite)
                {
                    AddFrame(sprite, true);
                }
            }
        }

        private void AddFrame(Sprite sprite, bool dedupe)
        {
            if (sprite == null)
            {
                return;
            }

            if (dedupe)
            {
                string key = AssetDatabase.GetAssetPath(sprite) + "/" + sprite.name;
                for (int i = 0; i < _cachedFrameKeys.Count; i++)
                {
                    if (string.Equals(_cachedFrameKeys[i], key, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                _cachedFrameKeys.Add(key);
            }

            _cachedFrames.Add(sprite);
        }

        private void StartPlayback()
        {
            if (_cachedFrames.Count == 0)
            {
                RefreshFrames();
                if (_cachedFrames.Count == 0)
                {
                    return;
                }
            }

            _isPlaying = true;
            _lastFrameTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_isPlaying || _cachedFrames.Count <= 1)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double frameDuration = 1.0 / Math.Max(1f, _fps);
            if (now - _lastFrameTime < frameDuration)
            {
                return;
            }

            _lastFrameTime = now;
            _currentFrameIndex++;

            if (_currentFrameIndex >= _cachedFrames.Count)
            {
                if (_loop)
                {
                    _currentFrameIndex = 0;
                }
                else
                {
                    _currentFrameIndex = _cachedFrames.Count - 1;
                    _isPlaying = false;
                }
            }

            Repaint();
        }

        private void EnsureStyles()
        {
            if (_previewBoxStyle == null)
            {
                _previewBoxStyle = new GUIStyle(GUI.skin.box);
            }

            if (_centerLabelStyle == null)
            {
                _centerLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
                };
            }

            if (_frameLabelStyle == null)
            {
                _frameLabelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
                {
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private static Rect FitRect(Rect outer, float contentWidth, float contentHeight)
        {
            if (contentWidth <= 0f || contentHeight <= 0f)
            {
                return outer;
            }

            float scale = Mathf.Min(outer.width / contentWidth, outer.height / contentHeight);
            float width = contentWidth * scale;
            float height = contentHeight * scale;
            float x = outer.x + (outer.width - width) * 0.5f;
            float y = outer.y + (outer.height - height) * 0.5f;
            return new Rect(x, y, width, height);
        }

        private static Rect GetNormalizedUV(Rect textureRect, Texture2D texture)
        {
            float width = texture.width;
            float height = texture.height;
            return new Rect(textureRect.x / width, textureRect.y / height, textureRect.width / width, textureRect.height / height);
        }
    }
}
#endif
