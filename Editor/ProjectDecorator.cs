using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.ProjectDecorator.Editor
{
    [InitializeOnLoad]
    internal static class ProjectDecorator
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Type BrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
        private static readonly Type TreeGUIType = typeof(EditorWindow).Assembly.GetType("UnityEditor.AssetsTreeViewGUI");
        private static readonly Type ListAreaType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ObjectListArea");
        private static readonly PropertyInfo CurrentView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView")?.GetProperty("current", Any);
        private static readonly PropertyInfo ActualView = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView")?.GetProperty("actualView", Any);
        private static readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();
        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>();
        private static readonly Dictionary<string, string> PathGuids = new Dictionary<string, string>();
        private static readonly Dictionary<string, Texture> Icons = new Dictionary<string, Texture>();
        private static Delegate treeHook;
        private static Delegate contentHook;

        private sealed class AssetInfo
        {
            internal string path;
            internal bool folder;
            internal int instanceId;
        }

        private sealed class BrowserContext
        {
            internal EditorWindow browser;
            internal object tree;
            internal object list;
            internal bool treeArea;
            internal bool listMode;
        }

        static ProjectDecorator()
        {
            EditorApplication.projectWindowItemInstanceOnGUI += DrawItem;
            EditorApplication.projectChanged += Invalidate;
            Selection.selectionChanged += Repaint;
            ProjectDecoratorSettings.Changed += Repaint;
            Undo.undoRedoPerformed += () => ProjectDecoratorSettings.instance.RefreshAfterUndo();
            SettingsLocalization.LanguageChanged += Repaint;
            InstallNativeHooks();
            AssemblyReloadEvents.beforeAssemblyReload += RemoveNativeHooks;
        }

        private static void InstallNativeHooks()
        {
            if (treeHook != null || contentHook != null)
                return;
            // These distinct native hooks identify tree vs content directly. In both
            // supported Editors, the tree hook runs before its label and foldout.
            treeHook = AddNativeHook(TreeGUIType, nameof(DrawNativeTreeIcon));
            contentHook = AddNativeHook(ListAreaType, nameof(DrawNativeContentIcon));
            Repaint();
        }

        private static Delegate AddNativeHook(Type owner, string method)
        {
            EventInfo hook = owner?.GetEvent("postAssetIconDrawCallback", Any);
            if (hook == null)
                throw new MissingMemberException(owner?.FullName, "postAssetIconDrawCallback");
            Delegate callback = Delegate.CreateDelegate(hook.EventHandlerType, typeof(ProjectDecorator).GetMethod(method, Any));
            hook.GetAddMethod(true).Invoke(null, new object[] { callback });
            return callback;
        }

        private static void RemoveNativeHooks()
        {
            if (treeHook != null)
                TreeGUIType.GetEvent("postAssetIconDrawCallback", Any).GetRemoveMethod(true).Invoke(null, new object[] { treeHook });
            if (contentHook != null)
                ListAreaType.GetEvent("postAssetIconDrawCallback", Any).GetRemoveMethod(true).Invoke(null, new object[] { contentHook });
            treeHook = contentHook = null;
        }

        private static void Invalidate()
        {
            Assets.Clear();
            PathGuids.Clear();
            Icons.Clear();
            Repaint();
        }

        private static void Repaint() => EditorApplication.RepaintProjectWindow();

        internal static Texture ResolveIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon))
                return null;
            if (!Icons.TryGetValue(icon, out Texture texture))
            {
                texture = icon.StartsWith("builtin:") ? EditorGUIUtility.IconContent(icon.Substring(8)).image
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(icon));
                Icons[icon] = texture;
            }
            return texture;
        }

        private static AssetInfo GetAsset(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;
            if (!Assets.TryGetValue(guid, out AssetInfo asset))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object main = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
                asset = new AssetInfo { path = path, folder = AssetDatabase.IsValidFolder(path), instanceId = main != null ? main.GetInstanceID() : 0 };
                Assets[guid] = asset;
                if (!string.IsNullOrEmpty(path))
                    PathGuids[path] = guid;
            }
            return asset;
        }

        internal static bool TryGetItemGuid(int instanceId, out string guid) =>
            ProjectDecoratorSettings.TryGetAssetGuid(EditorUtility.InstanceIDToObject(instanceId), out guid);

        internal static Color RowTint(Color color)
        {
            color.a = EditorGUIUtility.isProSkin ? 0.56f : 0.40f;
            return color;
        }

        private static bool TryGetExplicitTreeColor(string path, out Color color, out bool region)
        {
            color = default;
            region = false;
            if (string.IsNullOrEmpty(path))
                return false;
            if (!PathGuids.TryGetValue(path, out string guid))
            {
                guid = AssetDatabase.AssetPathToGUID(path);
                PathGuids[path] = guid;
            }
            FolderDecorationPopup.RowPreview preview = FolderDecorationPopup.GetRowPreview(guid);
            if (preview.HasColor)
            {
                color = preview.Color.GetValueOrDefault();
                region = preview.ColorRegion;
                return preview.Color.HasValue;
            }
            FolderDecoration decoration = ProjectDecoratorSettings.instance.GetDecoration(guid);
            if (decoration == null || !decoration.hasColor)
                return false;
            color = decoration.color;
            region = decoration.colorRegion;
            return true;
        }

        internal static Color? ResolveTreeColor(string path, out bool inherited)
        {
            inherited = false;
            string current = path;
            bool self = true;
            while (!string.IsNullOrEmpty(current))
            {
                if (TryGetExplicitTreeColor(current, out Color color, out bool region) && (self || region))
                {
                    inherited = !self;
                    return color;
                }
                self = false;
                int separator = current.LastIndexOf('/');
                current = separator >= 0 ? current.Substring(0, separator) : null;
            }
            return null;
        }

        private static EditorWindow CurrentBrowser()
        {
            object host = CurrentView?.GetValue(null);
            if (host == null || ActualView == null || !ActualView.DeclaringType.IsInstanceOfType(host))
                return null;
            var window = ActualView.GetValue(host) as EditorWindow;
            return window != null && BrowserType.IsInstanceOfType(window) ? window : null;
        }

        private static BrowserContext Context(Rect row, bool? nativeTree = null, bool? nativeListMode = null)
        {
            EditorWindow browser = CurrentBrowser();
            if (browser == null)
                return null;
            bool twoColumns = Convert.ToInt32(Read(browser, "m_ViewMode")) == 1;
            object list = Read(browser, "m_ListArea");
            bool tree;
            if (nativeTree.HasValue)
                tree = nativeTree.Value;
            else if (twoColumns)
            {
                Rect treeRect = (Rect)Read(browser, "m_TreeViewRect");
                Vector2 point = GUIUtility.GUIToScreenPoint(row.center) - browser.position.position;
                tree = treeRect.Contains(point);
            }
            else
                tree = !(bool)Call(Read(browser, "m_SearchFilter"), "IsSearching");
            return new BrowserContext
            {
                browser = browser,
                tree = Read(browser, twoColumns ? "m_FolderTree" : "m_AssetTree"),
                list = list,
                treeArea = tree,
                listMode = nativeListMode ?? (tree || list == null || (bool)Call(list, "IsListMode"))
            };
        }

        internal static Rect GetIconRect(Rect row)
        {
            BrowserContext context = Context(row);
            if (context == null || context.treeArea || context.listMode)
                return new Rect(row.x, row.y, Mathf.Min(16f, row.width), row.height);
            float height = GUI.skin.FindStyle("ProjectBrowserGridLabel")?.fixedHeight ?? 14f;
            return new Rect(row.x, row.y, row.width, Mathf.Max(0f, row.height - height));
        }

        private static Rect FavoriteStarRect(Rect row, bool listMode) => listMode
            ? new Rect(row.xMax - 18f, row.y, 18f, row.height)
            : new Rect(row.xMax - 18f, row.y, 18f, 18f);

        internal static bool IsFavoriteStar(Rect row, Vector2 pointer)
        {
            if (!ProjectDecoratorSettings.instance.showFavoriteStars)
                return false;
            BrowserContext context = Context(row);
            return context != null && FavoriteStarRect(row, context.listMode).Contains(pointer);
        }

        internal static void DrawItem(int instanceId, Rect rect)
        {
            if (!TryGetItemGuid(instanceId, out string guid) || Event.current == null)
                return;
            BrowserContext context = Context(rect);
            if (context == null)
                return;
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            AssetInfo asset = GetAsset(guid);
            if (asset == null || string.IsNullOrEmpty(asset.path) || (Selection.Contains(instanceId) && EditorGUIUtility.editingTextField))
                return;
            if (settings.decorationsEnabled && asset.folder)
                FolderDecorationPopup.HandleRowInput(rect, guid);
            // Decoration painting happens in the precise native icon hooks, never
            // after this callback has already received a rendered label.
            if (settings.showFavoriteStars)
            {
                Rect star = FavoriteStarRect(rect, context.listMode);
                bool favorite = settings.IsFavorite(guid);
                if (favorite || rect.Contains(Event.current.mousePosition))
                {
                    if (GUI.Button(star, SettingsLocalization.Content(string.Empty, favorite ? "Remove Favorite" : "Add Favorite"), GUIStyle.none))
                        settings.ToggleFavorite(guid);
                    Color tint = EditorStyles.label.normal.textColor;
                    if (!favorite)
                        tint.a *= 0.5f;
                    FolderDecorationPopup.DrawArtwork(star, FolderDecorationPopup.StarArtwork, 10f, tint);
                }
            }
        }

        private static void DrawNativeTreeIcon(Rect iconRect, string guid)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            BrowserContext context = Context(iconRect, true);
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            AssetInfo asset = GetAsset(guid);
            if (context == null || asset == null || context.tree == null || (!settings.decorationsEnabled && !settings.alternateRows))
                return;
            object item = Call(context.tree, "FindItem", asset.instanceId);
            object gui = Read(context.tree, "gui");
            if (item == null || (bool)Call(gui, "IsRenaming", asset.instanceId))
                return;
            bool selected = (bool)Call(context.tree, "IsItemDragSelectedOrSelected", item);
            bool focused = (bool)Call(context.tree, "HasFocus");
            bool inherited = false;
            Color? color = settings.decorationsEnabled ? ResolveTreeColor(asset.path, out inherited) : null;
            bool paintBackground = settings.alternateRows || color.HasValue;
            if (paintBackground)
            {
                Rect viewport = TreeViewport(context);
                Rect background = new Rect(iconRect.x, iconRect.y, Mathf.Max(0f, viewport.xMax - iconRect.x), iconRect.height);
                Color baseColor = TreeBaseColor(iconRect, settings.alternateRows);
                Rect visibleRow = new Rect(viewport.xMin, iconRect.y, viewport.width, iconRect.height);
                Color stateWash = Color.clear;
                if (settings.alternateRows)
                {
                    // Match Hierarchy's two-tone ordering, including its overlay
                    // state layer. The full gutter must lose the native blue too.
                    EditorGUI.DrawRect(visibleRow, baseColor);
                    if (selected)
                        stateWash = new Color(0.214f, 0.42f, 0.76f, 0.2f);
                    else if (visibleRow.Contains(Event.current.mousePosition))
                        stateWash = new Color(0.3f, 0.3f, 0.3f, 0.2f);
                }
                if (settings.decorationsEnabled)
                    DrawParentRegions(context, gui, item, iconRect);
                Color tint = Color.clear;
                if (color.HasValue)
                {
                    tint = color.Value;
                    tint.a = inherited ? (EditorGUIUtility.isProSkin ? 0.24f : 0.16f) : (EditorGUIUtility.isProSkin ? 0.56f : 0.40f);
                    EditorGUI.DrawRect(background, tint);
                }
                if (stateWash.a > 0f)
                    EditorGUI.DrawRect(visibleRow, stateWash);
                // The native label/foldout have not been drawn yet. Restore only
                // the native icon that the earlier icon pass already painted.
                EditorGUI.DrawRect(iconRect, baseColor);
                if (!settings.alternateRows)
                    RestoreTreeBackground(context, gui, item, iconRect, selected, focused);
                if (tint.a > 0f)
                    EditorGUI.DrawRect(iconRect, tint);
                if (stateWash.a > 0f)
                    EditorGUI.DrawRect(iconRect, stateWash);
                Texture nativeIcon = Call(gui, "GetEffectiveIcon", item, selected, focused) as Texture;
                if (nativeIcon != null)
                    GUI.DrawTexture(iconRect, nativeIcon, ScaleMode.ScaleToFit);
            }
            if (settings.decorationsEnabled && asset.folder)
            {
                FolderDecoration decoration = settings.GetDecoration(guid);
                FolderDecorationPopup.RowPreview preview = FolderDecorationPopup.GetRowPreview(guid);
                DrawFolderBadge(iconRect, preview.HasIcon ? preview.Icon : ResolveIcon(decoration?.icon));
            }
        }

        private static Rect TreeViewport(BrowserContext context)
        {
            Rect viewport = (Rect)Read(context.browser, "m_TreeViewRect");
            Vector2 scroll = (Vector2)Read(Read(context.tree, "state"), "scrollPos");
            // Tree callbacks use scroll-content coordinates, not window coordinates.
            return new Rect(scroll.x, 0f, viewport.width, viewport.height);
        }

        private static void DrawParentRegions(BrowserContext context, object gui, object item, Rect iconRect)
        {
            Rect viewport = TreeViewport(context);
            float selfIndent = (float)Call(gui, "GetContentIndent", item);
            float right = iconRect.x;
            for (object parent = Read(item, "parent"); parent != null && right > viewport.xMin; parent = Read(parent, "parent"))
            {
                int parentId = (int)Read(parent, "id");
                string path = parentId == 0 ? null : AssetDatabase.GetAssetPath(parentId);
                if (!TryGetExplicitTreeColor(path, out Color tint, out bool region) || !region)
                    continue;
                float indent = (float)Call(gui, "GetContentIndent", parent);
                float left = Mathf.Max(viewport.xMin, iconRect.x + indent - selfIndent);
                float visibleRight = Mathf.Min(right, viewport.xMax);
                if (visibleRight > left)
                {
                    Rect band = Rect.MinMaxRect(left, iconRect.yMin, visibleRight, iconRect.yMax);
                    tint.a = EditorGUIUtility.isProSkin ? 0.24f : 0.16f;
                    EditorGUI.DrawRect(band, tint);
                }
                // Ordinary colored ancestors do not stop region inheritance. Each
                // actual region owns the gap up to the next inner region's origin.
                right = left;
            }
        }

        private static void RestoreTreeBackground(BrowserContext context, object gui, object item, Rect clip, bool selected, bool focused)
        {
            Rect viewport = TreeViewport(context);
            object data = Read(context.tree, "data");
            int row = (int)Call(data, "GetRow", (int)Read(item, "id"));
            GUI.BeginClip(clip, -clip.position, Vector2.zero, false);
            try
            {
                // Match the native row ordering: hover, then focused/unfocused
                // selection. Restore the base under cleared icon slots before
                // their single color overlay. Alternating rows use their own base.
                Rect nativeRow = new Rect(0f, clip.y, viewport.xMax, clip.height);
                Call(gui, "DrawItemBackground", nativeRow, row, item, selected, focused);
                if (selected && (bool)Read(gui, "drawSelection"))
                {
                    GUIStyle selection = Read(gui, "selectionStyle") as GUIStyle;
                    selection?.Draw(nativeRow, false, false, true, focused);
                }
            }
            finally { GUI.EndClip(); }
        }

        private static void DrawNativeContentIcon(Rect frame, string guid, bool isListMode)
        {
            if (Event.current.type != EventType.Repaint || !ProjectDecoratorSettings.instance.decorationsEnabled)
                return;
            BrowserContext context = Context(frame, false, isListMode);
            AssetInfo asset = GetAsset(guid);
            if (context == null || asset == null || !asset.folder)
                return;
            FolderDecoration decoration = ProjectDecoratorSettings.instance.GetDecoration(guid);
            FolderDecorationPopup.RowPreview preview = FolderDecorationPopup.GetRowPreview(guid);
            Color? color = preview.HasColor ? preview.Color : decoration != null && decoration.hasColor ? decoration.color : (Color?)null;
            Texture badge = preview.HasIcon ? preview.Icon : ResolveIcon(decoration?.icon);
            Rect icon = frame;
            if (isListMode)
            {
                icon.x += Mathf.Max(0f, frame.width - 16f) * 0.5f;
                icon.width = 16f;
            }
            if (color.HasValue)
            {
                bool selected = Contains(Call(context.list, "GetSelection") as IEnumerable, asset.instanceId);
                bool focused = (bool)Call(context.list, "HasFocus");
                object group = Read(context.list, "m_LocalAssets");
                Texture nativeIcon = isListMode ? null : (Read(group, "m_Content") as GUIContent)?.image;
                if (nativeIcon == null)
                    nativeIcon = AssetDatabase.GetCachedIcon(asset.path);
                // Clear exactly the icon frame, leaving native text, foldouts,
                // grid labels, and rename controls completely untouched.
                GUIStyle panel = GUI.skin.FindStyle("ProjectBrowserIconAreaBg");
                if (panel != null)
                    panel.Draw(icon, GUIContent.none, false, false, false, false);
                else
                    EditorGUI.DrawRect(icon, EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.8f, 0.8f, 0.8f));
                if (selected && isListMode)
                {
                    GUIStyle selection = GUI.skin.FindStyle("OL ResultLabel");
                    GUI.BeginClip(icon, -icon.position, Vector2.zero, false);
                    try { selection?.Draw(new Rect(0f, icon.y, (float)Call(context.list, "GetVisibleWidth"), icon.height), GUIContent.none, false, false, true, focused); }
                    finally { GUI.EndClip(); }
                }
                Color previous = GUI.color;
                GUI.color *= color.Value;
                if (nativeIcon != null)
                    GUI.DrawTexture(icon, nativeIcon, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }
            DrawFolderBadge(icon, badge);
        }

        internal static void DrawFolderBadge(Rect icon, Texture badge)
        {
            if (badge == null || Event.current.type != EventType.Repaint)
                return;
            float size = icon.width <= 20f ? Mathf.Min(10f, icon.width) : Mathf.Min(24f, icon.width * 0.42f);
            Rect rect = new Rect(icon.xMax - size, icon.yMax - size, size, size);
            GUI.DrawTexture(rect, badge, ScaleMode.ScaleToFit);
        }

        private static Color TreeBaseColor(Rect row, bool alternate)
        {
            if (alternate)
            {
                bool even = Mathf.FloorToInt(row.y / Mathf.Max(1f, row.height)) % 2 == 0;
                float value = EditorGUIUtility.isProSkin ? (even ? 0.25f : 0.225f) : (even ? 0.8f : 0.765f);
                return new Color(value, value, value, 1f);
            }
            return EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.8f, 0.8f, 0.8f);
        }

        private static bool Contains(IEnumerable values, int id)
        {
            if (values != null)
                foreach (object value in values)
                    if (Convert.ToInt32(value) == id)
                        return true;
            return false;
        }

        private static object Read(object owner, string name)
        {
            if (owner == null)
                return null;
            Type type = owner.GetType();
            string key = type.FullName + ":" + name;
            if (!Members.TryGetValue(key, out MemberInfo member))
            {
                for (Type current = type; current != null && member == null; current = current.BaseType)
                    member = (MemberInfo)current.GetProperty(name, Any) ?? current.GetField(name, Any);
                Members[key] = member;
            }
            if (member is PropertyInfo property)
                return property.GetValue(owner);
            return (member as FieldInfo)?.GetValue(owner);
        }

        private static object Call(object owner, string name, params object[] args)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            Type type = owner.GetType();
            string key = type.FullName + ":" + name + ":" + args.Length;
            if (!Methods.TryGetValue(key, out MethodInfo method))
            {
                for (Type current = type; current != null && method == null; current = current.BaseType)
                    foreach (MethodInfo candidate in current.GetMethods(Any | BindingFlags.DeclaredOnly))
                        if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
                        {
                            method = candidate;
                            break;
                        }
                Methods[key] = method ?? throw new MissingMethodException(type.FullName, name);
            }
            return method.Invoke(owner, args);
        }
    }
}
