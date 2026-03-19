using Bhaptics.SDK2.Glove;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bhaptics.SDK2
{
    [CustomEditor(typeof(BhapticsPhysicsGlove))]
    public class BhapticsPhysicsGloveEditor : UnityEditor.Editor
    {
        private const string SettingsFileName = "GloveHapticSettings";
        private const string SettingsExtension = ".asset";

        private static string sdkRoot;
        private static string SdkRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(sdkRoot))
                {
                    return sdkRoot;
                }
                
                var  temp   = ScriptableObject.CreateInstance<BhapticsPhysicsGloveEditor>();
                var  monoScr  = MonoScript.FromScriptableObject(temp);
                string scriptPath = AssetDatabase.GetAssetPath(monoScr);
                UnityEngine.Object.DestroyImmediate(temp);
                
                int idx = scriptPath.LastIndexOf("/SDK2/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    sdkRoot = scriptPath.Substring(0, idx + "/SDK2/".Length).TrimEnd('/');
                }
                else
                {
                    sdkRoot = "Assets/Bhaptics/SDK2";
                }

                return sdkRoot;
            }
        }

        private static string BuildAssetPath(int? suffix = null)
        {
            string name = SettingsFileName + (suffix.HasValue ? $" {suffix}" : "") + SettingsExtension;
            return Path.Combine(SdkRoot, "Resources", name).Replace("\\", "/");
        }

        private BhapticsPhysicsGloveSettings LoadOrCreateSettingsAsset()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(BhapticsPhysicsGloveSettings)}", new[] { Path.Combine(SdkRoot, "Resources") });

            if (guids.Length > 0)
            {
                string firstPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<BhapticsPhysicsGloveSettings>(firstPath);
            }

            int suffix = 0;
            string assetPath = BuildAssetPath();
            string osPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);

            while (File.Exists(osPath))
            {
                suffix++;
                assetPath = BuildAssetPath(suffix);
                osPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(osPath));

            var settings = ScriptableObject.CreateInstance<BhapticsPhysicsGloveSettings>();
            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return settings;
        }

        public override void OnInspectorGUI()
        {
            BhapticsPhysicsGlove controller = (BhapticsPhysicsGlove)target;

            serializedObject.Update();

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bhaptics Physics Glove Settings", EditorStyles.boldLabel);

            if (controller.HapticSettings == null)
            {
                if (GUILayout.Button(new GUIContent("Create New GloveHapticSettings", "Creates a new GloveHapticSettings asset in the path: SDK2/Resources")))
                {
                    controller.HapticSettings = LoadOrCreateSettingsAsset();
                    EditorUtility.SetDirty(controller);
                }
            }
            else
            {
                UnityEditor.Editor editor = CreateEditor(controller.HapticSettings);
                editor.OnInspectorGUI();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Save Changes"))
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (GUILayout.Button("Revert to Original"))
            {
                serializedObject.ApplyModifiedProperties();
                ReturnOriginalValue(controller.HapticSettings);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            serializedObject.ApplyModifiedProperties();
        }

        public void ReturnOriginalValue(BhapticsPhysicsGloveSettings gloveSettings)
        {
            gloveSettings.motorIntensityMax = 50;
            gloveSettings.motorIntensityMin = 1;
            gloveSettings.velocityChangeMax = 2.0f;
            gloveSettings.velocityChangeMin = 0.2f;
            gloveSettings.decayRate = 0.3f;
            gloveSettings.decayDelay = 0.5f;
            gloveSettings.masterSlaveDistanceMax = 20f;
        }
    }
}