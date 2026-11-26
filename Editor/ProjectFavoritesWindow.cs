// List spacing and hover styling follow HierarchyDecorator's scene selector.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCLearn.ProjectDecorator.Editor
{
    internal sealed class ProjectFavoritesWindow : EditorWindow
    {
        private const float RowHeight = 18f;
        private string search = string.Empty;
        private string dragging;
        private Vector2 scroll;
        private readonly List<string> visible = new List<string>();
        private SearchField searchField;
        private GUIStyle rowStyle;

        [MenuItem("Tools/ProjectDecorator/Favorites", priority = 1)]
        internal static void OpenWindow()
        {
            var window = GetWindow<ProjectFavoritesWindow>();
            window.titleContent = new GUIContent(SettingsLocalization.Text("Project Favorites"), FolderDecorationPopup.StarArtwork);
            window.minSize = new Vector2(264f, 100f);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            ProjectDecoratorSettings.Changed += Repaint;
            EditorApplication.projectChanged += Repaint;
            SettingsLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            ProjectDecoratorSettings.Changed -= Repaint;
            EditorApplication.projectChanged -= Repaint;
            SettingsLocalization.LanguageChanged -= Repaint;
        }

        private void OnGUI()
        {
            if (searchField == null)
                searchField = new SearchField();
            HandleDrop();
            if (rowStyle == null)
                rowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(4, 4, 0, 0), alignment = TextAnchor.MiddleLeft };
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            string nextSearch = searchField.OnGUI(new Rect(6f, 6f, position.width - 40f, 20f), search);
            if (nextSearch != search)
            {
                search = nextSearch;
                scroll = Vector2.zero;
            }
            Rect add = new Rect(position.width - 29f, 4f, 24f, 24f);
            if (GUI.Button(add, SettingsLocalization.Content(string.Empty, "Add Selected Assets"), GUIStyle.none))
                AddAssets(Selection.objects);
            FolderDecorationPopup.DrawArtwork(add, FolderDecorationPopup.AddArtwork, 10f, EditorStyles.label.normal.textColor);

            visible.Clear();
            foreach (string guid in settings.Favorites)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (search.Length == 0 || path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (string.IsNullOrEmpty(path) && SettingsLocalization.Text("Missing asset").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    visible.Add(guid);
            }
            Rect viewport = new Rect(2f, 31f, position.width - 4f, position.height - 33f);
            if (visible.Count == 0)
            {
                GUI.Label(viewport, SettingsLocalization.Text(settings.Favorites.Count == 0 ? "Drag assets or folders here" : "No matching favorites"), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            bool needsScroll = visible.Count * RowHeight > viewport.height;
            Rect content = new Rect(0f, 0f, viewport.width - (needsScroll ? 15f : 0f), visible.Count * RowHeight);
            bool mouseInViewport = viewport.Contains(Event.current.mousePosition);
            scroll = GUI.BeginScrollView(viewport, scroll, content);
            try
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight));
                int last = Mathf.Min(visible.Count, Mathf.CeilToInt((scroll.y + viewport.height) / RowHeight));
                for (int index = first; index < last; index++)
                    DrawFavorite(new Rect(0f, index * RowHeight, content.width, RowHeight), visible[index], settings, mouseInViewport);
            }
            finally { GUI.EndScrollView(); }
            if (Event.current.type == EventType.MouseMove)
                Repaint();
        }

        private void DrawFavorite(Rect row, string guid, ProjectDecoratorSettings settings, bool mouseInViewport)
        {
            Event evt = Event.current;
            bool hovered = mouseInViewport && row.Contains(evt.mousePosition);
            if (evt.type == EventType.Repaint && hovered)
                EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.24f, 0.49f, 0.78f, 0.45f));
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Object asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
            string label = asset == null ? SettingsLocalization.Text("Missing asset") : AssetDatabase.IsValidFolder(path)
                ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            FolderDecoration decoration = settings.decorationsEnabled ? settings.GetDecoration(guid) : null;
            Texture badge = decoration == null ? null : ProjectDecorator.ResolveIcon(decoration.icon);
            Texture icon = asset == null ? EditorGUIUtility.IconContent("console.warnicon.sml").image : AssetDatabase.GetCachedIcon(path);
            Rect star = new Rect(row.xMax - RowHeight - 2f, row.y, RowHeight + 2f, row.height);
            Rect labelRect = row;
            labelRect.xMax = star.x;
            Rect iconRect = new Rect(labelRect.x + 4f, labelRect.y + (labelRect.height - 16f) * 0.5f, 16f, 16f);
            Color previous = GUI.color;
            if (decoration != null && decoration.hasColor)
                GUI.color *= decoration.color;
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
            ProjectDecorator.DrawFolderBadge(iconRect, badge);
            labelRect.xMin += 18f;
            GUI.Label(labelRect, new GUIContent(label, string.IsNullOrEmpty(path) ? guid : path), rowStyle);
            if (GUI.Button(star, SettingsLocalization.Content(string.Empty, "Remove Favorite"), GUIStyle.none))
                settings.RemoveFavorite(guid);
            FolderDecorationPopup.DrawArtwork(star, FolderDecorationPopup.StarArtwork, 10f, EditorStyles.label.normal.textColor);
            if (hovered && evt.type == EventType.ContextClick)
            {
                ShowContext(guid, settings);
                evt.Use();
            }
            if (asset == null || !labelRect.Contains(evt.mousePosition))
                return;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                dragging = guid;
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                if (evt.clickCount == 2)
                    OpenAsset(asset, path);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 0 && dragging == guid)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new[] { asset };
                DragAndDrop.StartDrag(asset.name);
                dragging = null;
                evt.Use();
            }
        }

        private void ShowContext(string guid, ProjectDecoratorSettings settings)
        {
            var menu = new GenericMenu();
            int index = -1;
            for (int i = 0; i < settings.Favorites.Count; i++)
                if (settings.Favorites[i] == guid)
                    index = i;
            if (index > 0 && search.Length == 0)
                menu.AddItem(SettingsLocalization.Content("Move Up"), false, () => settings.MoveFavorite(guid, -1));
            else
                menu.AddDisabledItem(SettingsLocalization.Content("Move Up"));
            if (index >= 0 && index < settings.Favorites.Count - 1 && search.Length == 0)
                menu.AddItem(SettingsLocalization.Content("Move Down"), false, () => settings.MoveFavorite(guid, 1));
            else
                menu.AddDisabledItem(SettingsLocalization.Content("Move Down"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(SettingsLocalization.Content("Remove Favorite"), false, () => settings.RemoveFavorite(guid));
            menu.ShowAsContext();
        }

        internal static void OpenAsset(Object asset, string path)
        {
            if (asset is SceneAsset)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    PrefabStageUtility.GetCurrentPrefabStage() != null)
                    return;
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            else
                AssetDatabase.OpenAsset(asset);
        }

        private void HandleDrop()
        {
            Event evt = Event.current;
            if ((evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) ||
                !new Rect(Vector2.zero, position.size).Contains(evt.mousePosition))
                return;
            bool supported = false;
            foreach (Object asset in DragAndDrop.objectReferences)
                supported |= ProjectDecoratorSettings.TryGetAssetGuid(asset, out _);
            if (!supported)
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddAssets(DragAndDrop.objectReferences);
            }
            evt.Use();
        }

        internal static void AddAssets(Object[] assets)
        {
            foreach (Object asset in assets)
                if (ProjectDecoratorSettings.TryGetAssetGuid(asset, out string guid))
                    ProjectDecoratorSettings.instance.AddFavorite(guid);
        }

        [MenuItem("Assets/ProjectDecorator/Toggle Favorite", priority = 2000)]
        private static void ToggleSelected()
        {
            var seen = new HashSet<string>();
            foreach (Object asset in Selection.objects)
                if (ProjectDecoratorSettings.TryGetAssetGuid(asset, out string guid) && seen.Add(guid))
                    ProjectDecoratorSettings.instance.ToggleFavorite(guid);
        }

        [MenuItem("Assets/ProjectDecorator/Toggle Favorite", true)]
        private static bool CanToggleSelected()
        {
            foreach (Object asset in Selection.objects)
                if (ProjectDecoratorSettings.TryGetAssetGuid(asset, out _))
                    return true;
            return false;
        }
    }
}
