#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity2DJumpStart
{
    public sealed class WebGLChecklist : EditorWindow
    {
        private enum CheckState
        {
            Pass,
            Warn,
            Unknown
        }

        private const string MenuPath = "Tools/2DJumpStart/WebGL Checklist";

        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private GUIStyle _wrapLabel;
        private GUIStyle _passStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _unknownStyle;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<WebGLChecklist>("WebGL Checklist");
        }

        private void OnGUI()
        {
            if (_wrapLabel == null)
            {
                _wrapLabel = new GUIStyle(EditorStyles.label) { wordWrap = true };
                _passStyle = CreateStateStyle(new Color(0.85f, 1f, 0.85f));
                _warnStyle = CreateStateStyle(new Color(1f, 0.92f, 0.75f));
                _unknownStyle = CreateStateStyle(new Color(0.88f, 0.88f, 0.88f));
            }

            GUILayout.Label("WebGL Checklist", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool only shows WebGL checks when the active build target is WebGL. Use the button below to apply the recommended settings in one pass.", MessageType.Info);

            EditorGUILayout.Space(4f);

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                EditorGUILayout.HelpBox("Active build target is not WebGL. The rest of the checklist is intentionally hidden.", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open Build Settings", GUILayout.Height(24f)))
                {
                    EditorApplication.ExecuteMenuItem("File/Build Settings...");
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                return;
            }

            CheckItem("Compression", IsCompressionRecommended(), "Gzip is the most compatibility-friendly option. Brotli is smaller, but Gzip is usually safer for broader browser support.");
            CheckItem("IL2CPP Code Generation", IsIl2CppCodeGenRecommended(), "Recommended for faster WebGL builds unless you are preparing a final release candidate and want to reconsider tradeoffs.");
            CheckItem("Code Stripping", IsManagedStrippingRecommended(), "Minimal stripping is safer when using .jslib or platform bridges. Higher stripping can break integrations or create subtle behavior changes.");
            CheckItem("Decompression Fallback", IsDecompressionFallbackOn(), "Fallback is generally worth keeping on for WebGL delivery reliability.");
            CheckItem("WASM Growth Mode", IsLinearGrowthMode(), "Linear growth is the safer default for predictable WebGL memory behavior.");

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Recommended WebGL Settings", GUILayout.Height(30f)))
            {
                ApplyRecommendedSettings();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void CheckItem(string label, CheckState state, string explanation)
        {
            GUIStyle style = state == CheckState.Pass ? _passStyle : state == CheckState.Warn ? _warnStyle : _unknownStyle;
            string title = state == CheckState.Pass ? $"{label} (DONE)" : label;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, style);
            EditorGUILayout.LabelField(explanation, _wrapLabel);
            EditorGUILayout.EndVertical();
        }

        private CheckState IsCompressionRecommended()
        {
            object value = GetWebGLSettingValue("compressionFormat");
            if (value == null)
            {
                return CheckState.Unknown;
            }

            string name = value.ToString();
            return string.Equals(name, "Gzip", StringComparison.OrdinalIgnoreCase) ? CheckState.Pass : CheckState.Warn;
        }

        private CheckState IsDecompressionFallbackOn()
        {
            object value = GetWebGLSettingValue("decompressionFallback");
            if (value == null)
            {
                return CheckState.Unknown;
            }

            return value is bool b && b ? CheckState.Pass : CheckState.Warn;
        }

        private CheckState IsLinearGrowthMode()
        {
            object value = GetWebGLSettingValue("memoryGrowthMode");
            if (value == null)
            {
                return CheckState.Unknown;
            }

            string name = value.ToString();
            return string.Equals(name, "Linear", StringComparison.OrdinalIgnoreCase) ? CheckState.Pass : CheckState.Warn;
        }

        private CheckState IsIl2CppCodeGenRecommended()
        {
            int? serializedValue = GetSerializedWebGLProjectSettingInt("il2cppCodeGeneration");
            if (!serializedValue.HasValue)
            {
                return CheckState.Unknown;
            }

            return serializedValue.Value == 1 ? CheckState.Pass : CheckState.Warn;
        }

        private CheckState IsManagedStrippingRecommended()
        {
            object value = InvokePlayerSettingsGetter("GetManagedStrippingLevel", GetBuildTargetArg());
            if (value == null)
            {
                return CheckState.Unknown;
            }

            string name = value.ToString();
            if (name.IndexOf("Minimal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CheckState.Pass;
            }

            if (name.IndexOf("Low", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CheckState.Pass;
            }

            return CheckState.Warn;
        }

        private void ApplyRecommendedSettings()
        {
            SetWebGLSettingValue("compressionFormat", "Gzip");
            SetWebGLSettingValue("decompressionFallback", true);
            SetWebGLSettingValue("memoryGrowthMode", "Linear");

            object il2CppCodeGen = FindEnumValue("UnityEditor.Il2CppCodeGeneration", "Faster", "OptimizeSize", "Size", "BuildTime");
            if (il2CppCodeGen != null)
            {
                InvokePlayerSettingsSetter("SetIl2CppCodeGeneration", GetBuildTargetArg(), il2CppCodeGen);
            }

            object strippingLevel = FindEnumValue("UnityEditor.ManagedStrippingLevel", "Minimal", "Low");
            if (strippingLevel != null)
            {
                InvokePlayerSettingsSetter("SetManagedStrippingLevel", GetBuildTargetArg(), strippingLevel);
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("WebGL Checklist", "Recommended WebGL settings were applied where the Unity version exposed writable APIs.", "OK");
        }

        private static GUIStyle CreateStateStyle(Color textColor)
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = textColor }
            };
        }

        private static Type GetPlayerSettingsType()
        {
            return typeof(PlayerSettings);
        }

        private static Type GetWebGLSettingsType()
        {
            Type playerSettingsType = GetPlayerSettingsType();

            foreach (Type nested in playerSettingsType.GetNestedTypes(Flags))
            {
                if (nested.Name.IndexOf("WebGL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return nested;
                }
            }

            Assembly editorAssembly = typeof(Editor).Assembly;
            foreach (Type type in editorAssembly.GetTypes())
            {
                if (type.Name.IndexOf("WebGL", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (type.GetProperty("compressionFormat", Flags) != null ||
                    type.GetProperty("decompressionFallback", Flags) != null ||
                    type.GetProperty("memoryGrowthMode", Flags) != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static object GetWebGLSettingValue(string propertyName)
        {
            Type type = GetWebGLSettingsType();
            if (type == null)
            {
                return null;
            }

            PropertyInfo property = type.GetProperty(propertyName, Flags);
            if (property == null)
            {
                return null;
            }

            object target = property.GetGetMethod(true) != null && property.GetGetMethod(true).IsStatic ? null : Activator.CreateInstance(type);
            return property.GetValue(target, null);
        }

        private static void SetWebGLSettingValue(string propertyName, object value)
        {
            Type type = GetWebGLSettingsType();
            if (type == null)
            {
                return;
            }

            PropertyInfo property = type.GetProperty(propertyName, Flags);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            object converted = ConvertToType(property.PropertyType, value);
            if (converted == null && property.PropertyType.IsValueType)
            {
                return;
            }

            object target = property.GetSetMethod(true) != null && property.GetSetMethod(true).IsStatic ? null : Activator.CreateInstance(type);
            property.SetValue(target, converted, null);
        }

        private static object InvokePlayerSettingsGetter(string methodName, params object[] args)
        {
            MethodInfo method = FindPlayerSettingsMethod(methodName, args);
            if (method == null)
            {
                return null;
            }

            return method.Invoke(null, args);
        }

        private static void InvokePlayerSettingsSetter(string methodName, params object[] args)
        {
            MethodInfo method = FindPlayerSettingsMethod(methodName, args);
            if (method == null)
            {
                return;
            }

            method.Invoke(null, args);
        }

        private static MethodInfo FindPlayerSettingsMethod(string methodName, params object[] args)
        {
            Type type = GetPlayerSettingsType();
            MethodInfo[] methods = type.GetMethods(Flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                {
                    continue;
                }

                bool matches = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (args[p] == null)
                    {
                        continue;
                    }

                    if (!parameters[p].ParameterType.IsAssignableFrom(args[p].GetType()) &&
                        !parameters[p].ParameterType.IsEnum)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return method;
                }
            }

            return null;
        }

        private static object GetBuildTargetArg()
        {
            Type namedBuildTargetType = Type.GetType("UnityEditor.NamedBuildTarget, UnityEditor");
            if (namedBuildTargetType != null)
            {
                PropertyInfo webglProperty = namedBuildTargetType.GetProperty("WebGL", BindingFlags.Public | BindingFlags.Static);
                if (webglProperty != null)
                {
                    return webglProperty.GetValue(null, null);
                }
            }

            return BuildTargetGroup.WebGL;
        }

        private static object FindEnumValue(string enumTypeName, params string[] preferredNames)
        {
            Type enumType = Type.GetType(enumTypeName + ", UnityEditor");
            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            Array values = Enum.GetValues(enumType);
            for (int i = 0; i < preferredNames.Length; i++)
            {
                string desired = preferredNames[i];
                foreach (object item in values)
                {
                    string name = Enum.GetName(enumType, item);
                    if (name != null && name.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return item;
                    }
                }
            }

            return values.Length > 0 ? values.GetValue(0) : null;
        }

        private static object ConvertToType(Type targetType, object value)
        {
            if (value == null)
            {
                return null;
            }

            if (targetType.IsAssignableFrom(value.GetType()))
            {
                return value;
            }

            if (targetType.IsEnum)
            {
                if (value is string s)
                {
                    return Enum.Parse(targetType, s, true);
                }

                return Enum.ToObject(targetType, value);
            }

            if (targetType == typeof(bool))
            {
                if (value is bool b)
                {
                    return b;
                }

                return Convert.ToBoolean(value);
            }

            return Convert.ChangeType(value, targetType);
        }

        private static int? GetSerializedWebGLProjectSettingInt(string settingName)
        {
            string projectSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "ProjectSettings/ProjectSettings.asset");
            if (!File.Exists(projectSettingsPath))
            {
                return null;
            }

            string text = File.ReadAllText(projectSettingsPath);
            string marker = settingName + ":";
            int settingIndex = text.IndexOf(marker, StringComparison.Ordinal);
            if (settingIndex < 0)
            {
                return null;
            }

            int webglIndex = text.IndexOf("WebGL:", settingIndex, StringComparison.Ordinal);
            if (webglIndex < 0)
            {
                return null;
            }

            int colonIndex = text.IndexOf(':', webglIndex);
            if (colonIndex < 0)
            {
                return null;
            }

            int cursor = colonIndex + 1;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            int end = cursor;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            if (end <= cursor)
            {
                return null;
            }

            if (int.TryParse(text.Substring(cursor, end - cursor), out int parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
#endif
