// Copyright (c) 2026 VRCLearn.
// Settings header and tab layout adapted from WooshiiDev/HierarchyDecorator.
// Original portions: Copyright (c) 2020 - 2024 Damian Slocombe. MIT License.
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VRCLearn.ProjectDecorator.Editor
{
    internal static class ProjectDecoratorPreferences
    {
        private static int selectedTabIndex;

        [MenuItem("Tools/ProjectDecorator/Settings", priority = 20)]
        private static void OpenSettings() => SettingsService.OpenUserPreferences("Preferences/ProjectDecorator");

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/ProjectDecorator", SettingsScope.User)
            {
                label = "ProjectDecorator",
                guiHandler = _ => DrawSettings()
            };
        }

        private static void DrawSettings()
        {
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            bool restore;
            using (new EditorGUILayout.VerticalScope(ProjectDecoratorStyle.InspectorPadding))
            {
                SettingsLocalization.DrawLanguageSelector();
                using (new EditorGUILayout.VerticalScope(ProjectDecoratorStyle.TabBackground, GUILayout.MinHeight(32f)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(SettingsLocalization.Text("Project Settings"), ProjectDecoratorStyle.Title);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(SettingsLocalization.Text("GitHub Repository"), EditorStyles.miniButtonLeft))
                            Application.OpenURL("https://github.com/VRCLearn/ProjectDecorator/");
                        restore = GUILayout.Button(SettingsLocalization.Text("Restore Default Settings"), EditorStyles.miniButtonRight);
                    }
                    EditorGUILayout.Space(9f);
                    GUIContent general = SettingsLocalization.Content("General");
                    general.image = EditorGUIUtility.IconContent("d_CustomTool").image;
                    GUIContent visual = SettingsLocalization.Content("Visual");
                    visual.image = EditorGUIUtility.IconContent("d_InputField Icon").image;
                    selectedTabIndex = GUILayout.SelectionGrid(selectedTabIndex, new[] { general, visual }, 2, ProjectDecoratorStyle.TabButton);
                }
                using (new EditorGUILayout.VerticalScope(ProjectDecoratorStyle.TabBackground, GUILayout.MinHeight(32f)))
                {
                    EditorGUI.BeginChangeCheck();
                    if (selectedTabIndex == 0)
                    {
                        EditorGUILayout.LabelField(SettingsLocalization.Text("Favorites"), ProjectDecoratorStyle.BoxHeader);
                        settings.showFavoriteStars = EditorGUILayout.Toggle(SettingsLocalization.Content("Favorite Stars"), settings.showFavoriteStars);
                        if (GUILayout.Button(SettingsLocalization.Text("Open Favorites"), EditorStyles.miniButton))
                            ProjectFavoritesWindow.OpenWindow();
                    }
                    else
                    {
                        EditorGUILayout.LabelField(SettingsLocalization.Text("Folder Decorations"), ProjectDecoratorStyle.BoxHeader);
                        settings.decorationsEnabled = EditorGUILayout.Toggle(SettingsLocalization.Content("Enable Folder Decorations"), settings.decorationsEnabled);
                        EditorGUILayout.Space(9f);
                        EditorGUILayout.LabelField(SettingsLocalization.Text("Background"), ProjectDecoratorStyle.BoxHeader);
                        settings.alternateRows = EditorGUILayout.Toggle(SettingsLocalization.Content("Alternating Tree Row Backgrounds"), settings.alternateRows);
                    }
                    if (EditorGUI.EndChangeCheck())
                        settings.Persist();
                }
            }
            if (restore)
            {
                RestoreDefaults();
                GUIUtility.ExitGUI();
            }
        }

        [MenuItem("Tools/ProjectDecorator/Restore Default Settings…", priority = 40)]
        private static void RestoreDefaults()
        {
            if (EditorUtility.DisplayDialog(SettingsLocalization.Text("Restore Default Settings"),
                SettingsLocalization.Text("Restore the default Project settings?"),
                SettingsLocalization.Text("Restore"), SettingsLocalization.Text("Cancel")))
                ProjectDecoratorSettings.instance.RestoreDefaults();
        }

        private static void SelectLanguage(SettingsLanguage language)
        {
            SettingsLocalization.Language = language;
            InternalEditorUtility.RepaintAllViews();
        }

        [MenuItem("Tools/ProjectDecorator/Language/Auto")]
        private static void AutoLanguage() => SelectLanguage(SettingsLanguage.Auto);
        [MenuItem("Tools/ProjectDecorator/Language/English")]
        private static void EnglishLanguage() => SelectLanguage(SettingsLanguage.English);
        [MenuItem("Tools/ProjectDecorator/Language/日本語")]
        private static void JapaneseLanguage() => SelectLanguage(SettingsLanguage.Japanese);
        [MenuItem("Tools/ProjectDecorator/Language/한국어")]
        private static void KoreanLanguage() => SelectLanguage(SettingsLanguage.Korean);
        [MenuItem("Tools/ProjectDecorator/Language/繁體中文")]
        private static void TraditionalLanguage() => SelectLanguage(SettingsLanguage.TraditionalChinese);
        [MenuItem("Tools/ProjectDecorator/Language/简体中文")]
        private static void SimplifiedLanguage() => SelectLanguage(SettingsLanguage.SimplifiedChinese);
    }
}
