#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity2DJumpStart
{
    /// <summary>
    /// Finds audio clips in the project and applies a compact default import configuration.
    /// </summary>
    public sealed class AudioOptimizerWindow : EditorWindow
    {
        private sealed class AudioClipData
        {
            public string path;
            public AudioClip clip;
            public string clipName;
            public string folder;
            public long sizeBytes;
            public float length;
            public bool canOptimize;
            public string skipReason;
        }

        private const string MenuPath = "Tools/2DJumpStart/Audio Optimizer";
        private const string EmptyFolderMessage = "Leave empty to scan the entire project.";

        [SerializeField] private DefaultAsset targetFolder;
        [SerializeField, Min(1)] private int itemsPerPage = 20;

        private readonly List<AudioClipData> audioClips = new List<AudioClipData>();
        private Vector2 scrollPosition;
        private int currentPage;
        private string statusMessage = "No audio clips loaded. Click Populate Audio Clips to begin.";

        private GUIStyle centerLabelStyle;
        private GUIStyle leftLabelStyle;

        private static MethodInfo getSoundSizeMethod;
        private static bool getSoundSizeUsesBoolArgument;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<AudioOptimizerWindow>("Audio Optimizer");
        }

        private void OnEnable()
        {
            minSize = new Vector2(620f, 360f);
        }

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.Label("Audio Optimizer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Find imported audio clips and optimize their default Unity import settings. The tool skips ambisonic, PCM, ADPCM, and already-mono clips.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Target Folder", EmptyFolderMessage),
                targetFolder,
                typeof(DefaultAsset),
                false);

            if (targetFolder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(targetFolder)))
            {
                EditorGUILayout.HelpBox("The selected asset is not a valid folder. Clear it to scan the entire project.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            itemsPerPage = Mathf.Max(1, EditorGUILayout.IntField("Audio Clips Per Page", itemsPerPage));
            if (GUILayout.Button("Populate Audio Clips", GUILayout.Height(21f)))
            {
                ScanAudioClips();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedLabel);

            if (audioClips.Count == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("No audio clips loaded.", centerLabelStyle);
                GUILayout.FlexibleSpace();
                return;
            }

            int totalPages = Mathf.CeilToInt((float)audioClips.Count / itemsPerPage);
            currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(currentPage <= 0);
            if (GUILayout.Button("Previous", GUILayout.Width(100f)))
            {
                currentPage--;
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Page {currentPage + 1} of {totalPages} ({audioClips.Count} total clips)");
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(currentPage >= totalPages - 1);
            if (GUILayout.Button("Next", GUILayout.Width(100f)))
            {
                currentPage++;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            DrawHeader();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Mathf.Min(startIndex + itemsPerPage, audioClips.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                DrawAudioClipRow(i, audioClips[i]);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Eligible clips: {CountOptimizableClips()} | Skipped clips: {CountSkippedClips()}");
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(CountOptimizableClips() == 0);
            if (GUILayout.Button("Optimize All", GUILayout.Width(140f), GUILayout.Height(32f)))
            {
                ConfirmAndOptimize();
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

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("No.", EditorStyles.toolbarButton, GUILayout.Width(45f));
            GUILayout.Label("Clip Name", EditorStyles.toolbarButton, GUILayout.Width(190f));
            GUILayout.Label("Folder Belong", EditorStyles.toolbarButton, GUILayout.Width(180f));
            GUILayout.Label("Size", EditorStyles.toolbarButton, GUILayout.Width(95f));
            GUILayout.Label("Length", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAudioClipRow(int index, AudioClipData data)
        {
            float rowHeight = data.canOptimize ? 34f : 48f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight, GUILayout.ExpandWidth(true));
            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                EditorGUIUtility.PingObject(data.clip);
                Selection.activeObject = data.clip;
            }

            float x = rowRect.x;
            GUI.Label(new Rect(x, rowRect.y + 7f, 45f, 20f), (index + 1).ToString(), centerLabelStyle);
            x += 45f;
            GUI.Label(new Rect(x + 5f, rowRect.y + 7f, 185f, 20f), data.clipName, leftLabelStyle);
            x += 190f;
            GUI.Label(new Rect(x + 5f, rowRect.y + 7f, 175f, 20f), data.folder, leftLabelStyle);
            x += 180f;
            GUI.Label(new Rect(x, rowRect.y + 7f, 95f, 20f), FormatBytes(data.sizeBytes), centerLabelStyle);
            x += 95f;
            GUI.Label(new Rect(x, rowRect.y + 7f, rowRect.width - (x - rowRect.x), 20f), FormatLength(data.length), centerLabelStyle);

            if (!data.canOptimize && !string.IsNullOrEmpty(data.skipReason))
            {
                GUI.Label(new Rect(rowRect.x + 5f, rowRect.y + 31f, rowRect.width - 10f, 14f), data.skipReason, EditorStyles.miniLabel);
            }
        }

        private void ScanAudioClips()
        {
            audioClips.Clear();
            currentPage = 0;

            string[] searchFolders = GetSearchFolders();
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", searchFolders);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    continue;
                }

                AudioClipData data = CreateAudioClipData(path, clip);
                audioClips.Add(data);
            }

            audioClips.Sort((first, second) => second.sizeBytes.CompareTo(first.sizeBytes));
            statusMessage = $"Loaded {audioClips.Count} audio clip(s). Click a row to select it in the Project window.";
        }

        private string[] GetSearchFolders()
        {
            if (targetFolder == null)
            {
                return new[] { "Assets" };
            }

            string folderPath = AssetDatabase.GetAssetPath(targetFolder);
            return AssetDatabase.IsValidFolder(folderPath) ? new[] { folderPath } : new[] { "Assets" };
        }

        private AudioClipData CreateAudioClipData(string path, AudioClip clip)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            string skipReason = GetSkipReason(importer);

            return new AudioClipData
            {
                path = path,
                clip = clip,
                clipName = clip.name,
                folder = GetFolderName(path),
                sizeBytes = GetImportedSize(clip),
                length = clip.length,
                canOptimize = string.IsNullOrEmpty(skipReason),
                skipReason = skipReason
            };
        }

        private string GetSkipReason(AudioImporter importer)
        {
            if (importer == null)
            {
                return "Skipped: invalid importer";
            }

            if (importer.ambisonic)
            {
                return "Skipped: ambisonic";
            }

            if (importer.forceToMono)
            {
                return "Skipped: already mono";
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.compressionFormat == AudioCompressionFormat.PCM)
            {
                return "Skipped: PCM";
            }

            if (settings.compressionFormat == AudioCompressionFormat.ADPCM)
            {
                return "Skipped: ADPCM";
            }

            return string.Empty;
        }

        private void ConfirmAndOptimize()
        {
            int eligibleCount = CountOptimizableClips();
            string message = "This will change the import settings of all listed audio clips. Stereo clips may be converted to mono, and compressed clips may lose audio detail. Most sound effects and voice clips will have little noticeable quality loss, but music and stereo ambience may be affected. Continue?";

            if (EditorUtility.DisplayDialog("Optimize Audio Clips", message, "Okay", "Cancel"))
            {
                OptimizeAudioClips(eligibleCount);
                ScanAudioClips();
            }
        }

        private void OptimizeAudioClips(int eligibleCount)
        {
            int optimizedCount = 0;
            try
            {
                for (int i = 0; i < audioClips.Count; i++)
                {
                    AudioClipData data = audioClips[i];
                    if (!data.canOptimize)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Optimizing Audio Clips",
                        $"Processing {data.clipName} ({optimizedCount + 1}/{eligibleCount})",
                        (float)optimizedCount / eligibleCount);

                    AudioImporter importer = AssetImporter.GetAtPath(data.path) as AudioImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0f;
                    importer.defaultSampleSettings = settings;
                    importer.forceToMono = true;
                    importer.SaveAndReimport();
                    optimizedCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            statusMessage = $"Optimized {optimizedCount} audio clip(s). The table will now refresh with updated imported sizes.";
        }

        private int CountOptimizableClips()
        {
            int count = 0;
            for (int i = 0; i < audioClips.Count; i++)
            {
                if (audioClips[i].canOptimize)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountSkippedClips()
        {
            return audioClips.Count - CountOptimizableClips();
        }

        private long GetImportedSize(AudioClip clip)
        {
            if (clip == null)
            {
                return -1L;
            }

            FindSoundSizeMethod();

            long importedSize = InvokeSoundSizeMethod(clip);
            if (importedSize > 0L)
            {
                return importedSize;
            }

            // Audio data can be unloaded while the AudioClip metadata remains available.
            // Loading it gives Unity's editor audio utility a chance to report the imported data size.
            clip.LoadAudioData();
            importedSize = InvokeSoundSizeMethod(clip);
            if (importedSize > 0L)
            {
                return importedSize;
            }

            long runtimeSize = Profiler.GetRuntimeMemorySizeLong(clip);
            return runtimeSize > 0L ? runtimeSize : -1L;
        }

        private static void FindSoundSizeMethod()
        {
            if (getSoundSizeMethod != null)
            {
                return;
            }

            Type audioUtilType = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null)
            {
                return;
            }

            MethodInfo[] methods = audioUtilType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "GetSoundSize")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(AudioClip))
                {
                    getSoundSizeMethod = method;
                    getSoundSizeUsesBoolArgument = false;
                    return;
                }

                if (parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(AudioClip) &&
                    parameters[1].ParameterType == typeof(bool))
                {
                    getSoundSizeMethod = method;
                    getSoundSizeUsesBoolArgument = true;
                }
            }
        }

        private static long InvokeSoundSizeMethod(AudioClip clip)
        {
            if (getSoundSizeMethod == null)
            {
                return -1L;
            }

            try
            {
                object[] arguments = getSoundSizeUsesBoolArgument
                    ? new object[] { clip, false }
                    : new object[] { clip };
                object result = getSoundSizeMethod.Invoke(null, arguments);
                return result == null ? -1L : Convert.ToInt64(result);
            }
            catch
            {
                return -1L;
            }
        }

        private static string GetFolderName(string assetPath)
        {
            string folderPath = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(folderPath) ? "Assets" : folderPath.Replace("\\", "/");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0L)
            {
                return "Unavailable";
            }

            if (bytes >= 1048576L)
            {
                return (bytes / 1048576f).ToString("F2") + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024f).ToString("F2") + " KB";
            }

            return bytes + " Bytes";
        }

        private static string FormatLength(float seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1d
                ? time.ToString(@"h\:mm\:ss")
                : time.ToString(@"m\:ss");
        }
    }
}
#endif
