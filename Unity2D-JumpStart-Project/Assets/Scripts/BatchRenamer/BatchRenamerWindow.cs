#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity2DJumpStart
{
    public sealed class BatchRenamerWindow : EditorWindow
    {
        private enum Tab
        {
            BatchRenamer = 0,
            ParenthesesRemover = 1,
            NumberFormatter = 2
        }

        private enum RenameMode
        {
            Prefix = 0,
            Postfix = 1
        }

        private enum BracketPair
        {
            Parentheses = 0,
            Square = 1,
            Curly = 2
        }

        private enum NumberBridge
        {
            Hyphen = 0,
            Underscore = 1,
            Hash = 2
        }

        private enum AssetTypeFilter
        {
            Texture = 0,
            Sprite = 1,
            Script = 2,
            Audio = 3,
            Prefab = 4
        }

        private sealed class RenameEntry
        {
            public string AssetPath;
            public string CurrentName;
            public string NewName;
            public bool IsFolder;
            public bool IsValid;
            public string Error;
        }

        private const string MenuPath = "Tools/2DJumpStart/Batch Renamer";

        private Tab _tab;
        private DefaultAsset _folder;
        private RenameMode _renameMode = RenameMode.Prefix;
        private string _from = string.Empty;
        private string _changeInto = string.Empty;
        private bool _includeRootFolder;
        private bool _onlyCertainType;
        private AssetTypeFilter _assetTypeFilter = AssetTypeFilter.Texture;
        private bool _includeSubfolders = true;

        private BracketPair _bracketPair = BracketPair.Parentheses;
        private NumberBridge _numberBridge = NumberBridge.Hyphen;

        private readonly List<RenameEntry> _previewEntries = new List<RenameEntry>(128);
        private Vector2 _scroll;
        private string _statusMessage = "Select a folder to begin.";
        private MessageType _statusType = MessageType.Info;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<BatchRenamerWindow>("Batch Renamer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Batch Renamer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Switch between batch rename and bracket removal in the same window.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Batch Renamer", "Parentheses Remover", "Number Formatter" });
            if (EditorGUI.EndChangeCheck())
            {
                _scroll = Vector2.zero;
                RebuildPreview();
            }

            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            _folder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", _folder, typeof(DefaultAsset), false);
            _includeRootFolder = EditorGUILayout.Toggle("Including the root folder ?", _includeRootFolder);
            _includeSubfolders = EditorGUILayout.Toggle("Include subfolders", _includeSubfolders);
            _onlyCertainType = EditorGUILayout.Toggle("Only certain type", _onlyCertainType);
            if (_onlyCertainType)
            {
                _assetTypeFilter = (AssetTypeFilter)EditorGUILayout.EnumPopup("Asset Type", _assetTypeFilter);
            }

            if (_tab == Tab.BatchRenamer)
            {
                _renameMode = (RenameMode)EditorGUILayout.EnumPopup("Prefix / Postfix", _renameMode);
                _from = EditorGUILayout.TextField("From", _from);
                _changeInto = EditorGUILayout.TextField("Change Into", _changeInto);
            }
            else if (_tab == Tab.ParenthesesRemover)
            {
                _bracketPair = (BracketPair)EditorGUILayout.EnumPopup("Bracket Pair", _bracketPair);
            }
            else
            {
                _numberBridge = (NumberBridge)EditorGUILayout.EnumPopup("Bridge", _numberBridge);
            }

            if (EditorGUI.EndChangeCheck())
            {
                RebuildPreview();
            }

            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Preview", GUILayout.Height(24f)))
            {
                RebuildPreview();
            }

            EditorGUI.BeginDisabledGroup(!CanApply());
            if (GUILayout.Button("Apply Rename", GUILayout.Height(24f)))
            {
                ApplyRename();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            EditorGUILayout.Space(6f);

            DrawPreview();
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField($"Preview ({_previewEntries.Count})", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _previewEntries.Count; i++)
            {
                RenameEntry entry = _previewEntries[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(entry.AssetPath);
                EditorGUILayout.LabelField("Current", entry.CurrentName);
                EditorGUILayout.LabelField("New", entry.NewName);

                if (!entry.IsValid)
                {
                    EditorGUILayout.LabelField(entry.Error, EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private bool CanApply()
        {
            return _folder != null && _previewEntries.Count > 0 && HasAnyValidEntry();
        }

        private bool HasAnyValidEntry()
        {
            for (int i = 0; i < _previewEntries.Count; i++)
            {
                if (_previewEntries[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildPreview()
        {
            _previewEntries.Clear();

            if (_folder == null)
            {
                _statusMessage = "Select a folder to begin.";
                _statusType = MessageType.Info;
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_folder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                _statusMessage = "The selected object is not a valid project folder.";
                _statusType = MessageType.Error;
                return;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool canRenameFolder = _includeRootFolder && string.Equals(_folder.name, Path.GetFileName(folderPath), StringComparison.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                if (!_includeSubfolders && !IsDirectChildOfFolder(assetPath, folderPath))
                {
                    continue;
                }

                if (!_includeRootFolder && string.Equals(assetPath, folderPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesFilter(assetPath, folderPath))
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string extension = Path.GetExtension(assetPath);
                bool isFolder = string.IsNullOrEmpty(extension);

                if (isFolder)
                {
                    if (!string.Equals(assetPath, folderPath, StringComparison.Ordinal) || !canRenameFolder)
                    {
                        continue;
                    }
                }

                string newName;
                bool matchesRule = _tab == Tab.BatchRenamer
                    ? TryBuildBatchRename(fileName, out newName)
                    : _tab == Tab.ParenthesesRemover
                        ? TryBuildBracketRemoval(fileName, out newName)
                        : TryBuildNumberFormatter(fileName, out newName);

                if (!matchesRule || string.IsNullOrEmpty(newName))
                {
                    _previewEntries.Add(new RenameEntry
                    {
                        AssetPath = assetPath,
                        CurrentName = fileName,
                        NewName = string.Empty,
                        IsFolder = isFolder,
                        IsValid = false,
                        Error = matchesRule ? "Resulting name is empty." : _tab == Tab.BatchRenamer ? "Name does not match the selected prefix/postfix rule." : _tab == Tab.ParenthesesRemover ? "Name does not contain the selected bracket pair." : "Name does not end with a number."
                    });
                    continue;
                }

                string parentPath = Path.GetDirectoryName(assetPath) ?? string.Empty;
                string targetPath = isFolder ? Path.Combine(parentPath, newName) : Path.Combine(parentPath, newName + extension);
                bool isNoOp = string.Equals(targetPath, assetPath, StringComparison.Ordinal);
                bool duplicate = !usedNames.Add(targetPath);
                bool nameExists = !isNoOp && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null;

                _previewEntries.Add(new RenameEntry
                {
                    AssetPath = assetPath,
                    CurrentName = fileName,
                    NewName = newName,
                    IsFolder = isFolder,
                    IsValid = !isNoOp && !duplicate && !nameExists,
                    Error = isNoOp ? "This rename does not change the current name." : duplicate ? "Duplicate resulting path in this batch." : nameExists ? "Target name already exists in this folder." : string.Empty
                });
            }

            int validCount = 0;
            for (int i = 0; i < _previewEntries.Count; i++)
            {
                if (_previewEntries[i].IsValid)
                {
                    validCount++;
                }
            }

            if (_previewEntries.Count == 0)
            {
                _statusMessage = "No matching assets were found in the selected folder.";
                _statusType = MessageType.Warning;
                return;
            }

            _statusMessage = validCount > 0
                ? $"{validCount} valid rename(s) ready."
                : "No valid rename targets were found.";
            _statusType = validCount > 0 ? MessageType.Info : MessageType.Warning;
        }

        private void ApplyRename()
        {
            if (!CanApply())
            {
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_folder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                _statusMessage = "The selected folder is no longer valid.";
                _statusType = MessageType.Error;
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < _previewEntries.Count; i++)
                {
                    RenameEntry entry = _previewEntries[i];
                    if (!entry.IsValid || entry.IsFolder)
                    {
                        continue;
                    }

                    AssetDatabase.RenameAsset(entry.AssetPath, entry.NewName);
                }

                for (int i = 0; i < _previewEntries.Count; i++)
                {
                    RenameEntry entry = _previewEntries[i];
                    if (!entry.IsValid || !entry.IsFolder)
                    {
                        continue;
                    }

                    AssetDatabase.RenameAsset(entry.AssetPath, entry.NewName);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            RebuildPreview();
            _statusMessage = "Rename operation completed.";
            _statusType = MessageType.Info;
        }

        private bool TryBuildBatchRename(string currentName, out string result)
        {
            result = currentName;

            if (_renameMode == RenameMode.Prefix)
            {
                if (string.IsNullOrEmpty(_from))
                {
                    result = _changeInto + result;
                }
                else if (currentName.StartsWith(_from, StringComparison.Ordinal))
                {
                    result = _changeInto + currentName.Substring(_from.Length);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(_from))
                {
                    result = result + _changeInto;
                }
                else if (currentName.EndsWith(_from, StringComparison.Ordinal))
                {
                    result = currentName.Substring(0, currentName.Length - _from.Length) + _changeInto;
                }
                else
                {
                    return false;
                }
            }

            result = SanitizeName(result);
            return true;
        }

        private bool TryBuildBracketRemoval(string currentName, out string result)
        {
            char open;
            char close;
            switch (_bracketPair)
            {
                case BracketPair.Square:
                    open = '[';
                    close = ']';
                    break;
                case BracketPair.Curly:
                    open = '{';
                    close = '}';
                    break;
                default:
                    open = '(';
                    close = ')';
                    break;
            }

            int openIndex = currentName.IndexOf(open);
            if (openIndex < 0)
            {
                result = currentName;
                return false;
            }

            System.Text.StringBuilder builder = null;
            bool removed = false;
            bool sawContentInsidePair = false;

            for (int i = 0; i < currentName.Length; i++)
            {
                char c = currentName[i];
                if (c == open)
                {
                    removed = true;
                    continue;
                }

                if (c == close)
                {
                    removed = true;
                    continue;
                }

                if (builder == null)
                {
                    builder = new System.Text.StringBuilder(currentName.Length);
                }

                builder.Append(c);

                if (removed)
                {
                    sawContentInsidePair = true;
                }
            }

            if (!removed)
            {
                result = currentName;
                return false;
            }

            if (builder == null)
            {
                result = string.Empty;
                return true;
            }

            result = NormalizeWhitespace(SanitizeName(builder.ToString()));
            return sawContentInsidePair || !string.Equals(result, currentName, StringComparison.Ordinal);
        }

        private bool TryBuildNumberFormatter(string currentName, out string result)
        {
            int end = currentName.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(currentName[end]))
            {
                end--;
            }

            if (end < 0 || !char.IsDigit(currentName[end]))
            {
                result = currentName;
                return false;
            }

            int numberEnd = end;
            while (end >= 0 && char.IsDigit(currentName[end]))
            {
                end--;
            }

            int numberStart = end + 1;
            if (numberStart <= 0)
            {
                result = currentName;
                return false;
            }

            int separatorEnd = numberStart - 1;
            while (separatorEnd >= 0 && char.IsWhiteSpace(currentName[separatorEnd]))
            {
                separatorEnd--;
            }

            if (separatorEnd < 0)
            {
                result = currentName;
                return false;
            }

            int separatorStart = separatorEnd;
            while (separatorStart >= 0 && !char.IsLetterOrDigit(currentName[separatorStart]))
            {
                separatorStart--;
            }

            separatorStart++;

            char bridge;
            switch (_numberBridge)
            {
                case NumberBridge.Underscore:
                    bridge = '_';
                    break;
                case NumberBridge.Hash:
                    bridge = '#';
                    break;
                default:
                    bridge = '-';
                    break;
            }

            string prefix = currentName.Substring(0, separatorStart).TrimEnd();
            string suffix = currentName.Substring(numberStart, numberEnd - numberStart + 1);

            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(suffix))
            {
                result = currentName;
                return false;
            }

            result = SanitizeName(prefix) + bridge + suffix;
            return !string.Equals(result, currentName, StringComparison.Ordinal);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                value = value.Replace(invalid[i].ToString(), string.Empty);
            }

            return value.Trim();
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = null;
            bool previousWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (previousWasSpace)
                    {
                        continue;
                    }
                    previousWasSpace = true;
                }
                else
                {
                    previousWasSpace = false;
                }

                if (builder == null)
                {
                    builder = new System.Text.StringBuilder(value.Length);
                }

                builder.Append(isSpace ? ' ' : c);
            }

            return builder == null ? value.Trim() : builder.ToString().Trim();
        }

        private bool MatchesFilter(string assetPath, string folderPath)
        {
            if (!_onlyCertainType)
            {
                return true;
            }

            if (string.Equals(assetPath, folderPath, StringComparison.Ordinal))
            {
                return false;
            }

            switch (_assetTypeFilter)
            {
                case AssetTypeFilter.Texture:
                    return IsTextureAsset(assetPath) && !IsSpriteAsset(assetPath);
                case AssetTypeFilter.Sprite:
                    return IsSpriteAsset(assetPath);
                case AssetTypeFilter.Script:
                    return string.Equals(Path.GetExtension(assetPath), ".cs", StringComparison.OrdinalIgnoreCase);
                case AssetTypeFilter.Audio:
                    return IsAudioAsset(assetPath);
                case AssetTypeFilter.Prefab:
                    return string.Equals(Path.GetExtension(assetPath), ".prefab", StringComparison.OrdinalIgnoreCase);
                default:
                    return true;
            }
        }

        private static bool IsTextureAsset(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".tga", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".psd", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".tif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".tiff", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetExtension(assetPath), ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpriteAsset(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            return importer.textureType == TextureImporterType.Sprite;
        }

        private static bool IsAudioAsset(string assetPath)
        {
            string extension = Path.GetExtension(assetPath);
            return string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".aif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".aiff", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDirectChildOfFolder(string assetPath, string folderPath)
        {
            string parent = Path.GetDirectoryName(assetPath);
            return string.Equals(parent, folderPath, StringComparison.Ordinal);
        }
    }
}
#endif
