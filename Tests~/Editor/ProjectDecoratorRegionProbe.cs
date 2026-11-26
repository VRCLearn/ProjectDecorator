using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Disposable native ProjectBrowser verification. Never paints a synthetic row.
public static class ProjectRegionProbe
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
    private const string Root = "Assets/__ProjectRegionProbe";
    private const string Region = Root + "/01 Blue Region";
    private const string Inherited = Region + "/01 Inherited Child";
    private const string Own = Region + "/02 Own Red";
    private const string OwnChild = Own + "/Child Still Blue";
    private const string Nested = Region + "/03 Nested Green Region";
    private const string NestedChild = Nested + "/Child Now Green";
    private const string NestedOwn = Nested + "/02 Nested Own Red";
    private const string NestedOwnChild = NestedOwn + "/Child Still Green";
    private const string Ordinary = Root + "/02 Ordinary Purple";
    private const string OrdinaryChild = Ordinary + "/Child Stays Native";
    private const string Package = "Packages/com.vrclearn.projectdecorator";
    private static readonly Color Blue = new Color32(103, 151, 233, 255);
    private static readonly Color Red = new Color32(222, 104, 111, 255);
    private static readonly Color Green = new Color32(130, 181, 107, 255);
    private static readonly Color Purple = new Color32(173, 125, 232, 255);

    [Serializable] private sealed class Observation
    {
        public string path, context, nativeName;
        public Rect callback, screen, local;
        public bool selected;
        public bool renaming;
        public Rect viewport;
        public Rect renameRect;
        public Vector2 scroll;
        public float labelHeight, nativeTextureWidth, nativeTextureHeight;
    }
    private sealed class Case
    {
        public string name, folder;
        public int grid;
        public float sidebar;
        public bool packages;
        public bool selected, rename;
        public float scrollX;
    }
    [Serializable] private sealed class CaseResult
    {
        public string name, nativeScreenshot, decoratedScreenshot;
        public int nativeRows, treeRows, listRows, gridRows, contentLabelPixels, contentLabelDifferences;
        public string[] checks;
        public Observation[] observations;
    }
    [Serializable] private sealed class Report
    {
        public string version, generatedAt;
        public bool passed;
        public string[] checks, errors;
        public CaseResult[] cases;
    }
    private static readonly List<Case> Cases = new List<Case>();
    private static readonly List<CaseResult> Results = new List<CaseResult>();
    private static readonly List<string> Checks = new List<string>();
    private static readonly List<string> Errors = new List<string>();
    private static readonly Dictionary<string, Observation> Rows = new Dictionary<string, Observation>();
    private static EditorWindow browser, inspectorHost;
    private static Type browserType, settingsType;
    private static Object settings;
    private static string originalSettings;
    private static Dictionary<string, Observation> baselineRows;
    private static Texture2D native;
    private static int caseIndex, phase;
    private static double next;
    private static bool started;
    private static string output;

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        if (Environment.GetCommandLineArgs().Contains("ProjectRegionProbe.Run")) Run();
    }
    public static void Run()
    {
        if (!File.Exists("ProjectSettings/StyleParity.enabled")) throw new InvalidOperationException("Marked disposable project required.");
        EditorApplication.update -= Begin;
        EditorApplication.update += Begin;
    }
    private static void Begin()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < 3) return;
        EditorApplication.update -= Begin;
        if (started) return;
        started = true;
        try
        {
            output = Path.GetFullPath("Logs/ProjectRegions");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser", true);
            settingsType = Type.GetType("VRCLearn.ProjectDecorator.Editor.ProjectDecoratorSettings, VRCLearn.ProjectDecorator.Editor", true);
            settings = (Object)settingsType.GetProperty("instance", Any).GetValue(null);
            originalSettings = EditorJsonUtility.ToJson(settings);
            PrepareFixtures();
            SetEnabled(false);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(Root);
            inspectorHost = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow", true));
            inspectorHost.ShowUtility(); inspectorHost.position = new Rect(30, 520, 380, 300); inspectorHost.Repaint();
            Cases.Add(new Case {name = "regions", folder = Root, grid = 16, sidebar = 360});
            Cases.Add(new Case {name = "selected", folder = Own, grid = 16, sidebar = 360, selected = true});
            Cases.Add(new Case {name = "rename", folder = Own, grid = 16, sidebar = 360, selected = true, rename = true});
            Cases.Add(new Case {name = "narrow", folder = Root, grid = 16, sidebar = 100, scrollX = 52});
            Cases.Add(new Case {name = "packages", folder = Package, grid = 16, sidebar = 360, packages = true});
            if (Environment.GetCommandLineArgs().Contains("-selectionOnly")) Cases.RemoveAll(item => !item.selected);
            EditorApplication.projectWindowItemInstanceOnGUI += Observe;
            OpenCase();
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static void PrepareFixtures()
    {
        foreach (string path in new[] {Root, Region, Inherited, Inherited + "/Grandchild Still Blue", Own, OwnChild, Nested, NestedChild, NestedOwn, NestedOwnChild, Ordinary, OrdinaryChild}) EnsureFolder(path);
        File.WriteAllText(Region + "/NativeTextMustRemainSingle.txt", "Native filename, no overlay redraw.");
        AssetDatabase.ImportAsset(Region + "/NativeTextMustRemainSingle.txt");
        string badgePath = Root + "/MagentaBadge.asset";
        var badge = AssetDatabase.LoadAssetAtPath<Texture2D>(badgePath);
        if (badge == null)
        {
            badge = new Texture2D(8, 8, TextureFormat.RGBA32, false) {name = "MagentaBadge"};
            badge.SetPixels(Enumerable.Repeat(Color.magenta, 64).ToArray()); badge.Apply();
            AssetDatabase.CreateAsset(badge, badgePath);
        }
        string badgeGuid = AssetDatabase.AssetPathToGUID(badgePath);
        ((IList)settingsType.GetField("decorations", Any).GetValue(settings)).Clear();
        settingsType.GetField("lookup", Any).SetValue(settings, null);
        SetColor(Region, Blue, true);
        SetColor(Own, Red, false);
        SetColor(Nested, Green, true);
        SetColor(NestedOwn, Red, false);
        SetColor(Ordinary, Purple, false);
        SetColor(Package, Blue, true);
        SetColor(Package + "/Editor", Green, true);
        AssetDatabase.SaveAssets();
        Checks.Add("Native fixture covers parent Blue region, own ordinary Red, nested Green region, nested ordinary Red with Green descendants, and non-inheriting ordinary Purple.");
    }
    private static void SetColor(string path, Color color, bool region)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Missing fixture GUID: " + path);
        MethodInfo method = settingsType.GetMethod("SetColorRegion", Any);
        if (method == null) throw new MissingMethodException("The frozen SetColorRegion(guid, color, region) contract is required.");
        method.Invoke(settings, new object[] {guid, (Color?)color, region});
    }
    private static void SetEnabled(bool enabled)
    {
        settingsType.GetField("decorationsEnabled", Any).SetValue(settings, enabled);
        settingsType.GetField("showFavoriteStars", Any).SetValue(settings, false);
        settingsType.GetField("alternateRows", Any).SetValue(settings, false);
        settingsType.GetMethod("Persist", Any).Invoke(settings, null);
    }
    private static void OpenCase()
    {
        EndRename();
        if (browser != null) browser.Close();
        if (native != null) Object.DestroyImmediate(native);
        SetEnabled(false);
        browser = (EditorWindow)ScriptableObject.CreateInstance(browserType);
        browser.ShowUtility(); browser.position = new Rect(80, 60, 1040, 700); browser.Focus();
        browserType.GetField("m_DirectoriesAreaWidth", Any).SetValue(browser, Cases[caseIndex].sidebar);
        Rows.Clear(); phase = 0; next = EditorApplication.timeSinceStartup + 2;
    }
    private static void ConfigureBrowser()
    {
        Case item = Cases[caseIndex];
        FieldInfo viewMode = browserType.GetField("m_ViewMode", Any);
        if (Convert.ToInt32(viewMode.GetValue(browser)) != 1)
            browserType.GetMethod("InitViewMode", Any).Invoke(browser, new[] {Enum.ToObject(viewMode.FieldType, 1)});
        browserType.GetField("m_DirectoriesAreaWidth", Any).SetValue(browser, item.sidebar);
        Object folder = AssetDatabase.LoadMainAssetAtPath(item.folder);
        if (folder == null) throw new InvalidOperationException("Native folder fixture unavailable: " + item.folder);
        browserType.GetMethod("ShowFolderContents", Any, null, new[] {typeof(int), typeof(bool)}, null).Invoke(browser, new object[] {folder.GetInstanceID(), true});
        object list = browserType.GetField("m_ListArea", Any).GetValue(browser);
        PropertyInfo size = list.GetType().GetProperty("gridSize", Any);
        size.SetValue(list, Convert.ChangeType(item.grid, size.PropertyType));
        object tree = browserType.GetField("m_FolderTree", Any).GetValue(browser);
        object data = tree.GetType().GetProperty("data", Any).GetValue(tree);
        MethodInfo expanded = data.GetType().GetMethod("SetExpanded", Any, null, new[] {typeof(int), typeof(bool)}, null);
        expanded.Invoke(data, new object[] {AssetDatabase.LoadMainAssetAtPath("Assets").GetInstanceID(), !item.packages});
        expanded.Invoke(data, new object[] {int.MaxValue, item.packages});
        foreach (string path in item.packages ? new[] {Package, Package + "/Editor"} : new[] {Root, Region, Inherited, Own, Nested, NestedOwn, Ordinary})
            expanded.Invoke(data, new object[] {AssetDatabase.LoadMainAssetAtPath(path).GetInstanceID(), true});
        tree.GetType().GetMethod("ReloadData", Any).Invoke(tree, null);
        object state = tree.GetType().GetProperty("state", Any).GetValue(tree);
        state.GetType().GetField("scrollPos", Any).SetValue(state, new Vector2(item.scrollX, 0));
        if (item.rename)
        {
            tree.GetType().GetMethod("GrabKeyboardFocus", Any).Invoke(tree, null);
            bool renamed = (bool)tree.GetType().GetMethod("BeginNameEditing", Any).Invoke(tree, new object[] {0f});
            Require(renamed, "Native TreeView.BeginNameEditing must start the actual rename overlay.");
        }
        Rows.Clear(); browser.Repaint();
    }
    private static EditorWindow CurrentWindow()
    {
        Type viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView", true);
        object view = viewType.GetProperty("current", Any).GetValue(null);
        if (view == null) return null;
        return view.GetType().GetProperty("actualView", Any)?.GetValue(view) as EditorWindow;
    }
    private static void Observe(int instanceId, Rect row)
    {
        if (Event.current.type != EventType.Repaint || browser == null || CurrentWindow() != browser) return;
        string path = AssetDatabase.GetAssetPath(instanceId);
        if (!path.StartsWith(Root, StringComparison.Ordinal) && !path.StartsWith(Package, StringComparison.Ordinal)) return;
        Vector2 screen = GUIUtility.GUIToScreenPoint(row.position);
        Rect local = new Rect(screen - browser.position.position, row.size);
        Rect content = (Rect)browserType.GetField("m_ListAreaRect", Any).GetValue(browser);
        string context = local.x < content.xMin ? "tree" : row.height > 24 ? "grid" : "list";
        string nativeName = null;
        bool selected = Selection.Contains(instanceId);
        bool renaming = false;
        Rect viewport = default;
        Rect renameRect = default;
        Vector2 scroll = default;
        if (context == "tree")
        {
            object tree = browserType.GetField("m_FolderTree", Any).GetValue(browser);
            object item = tree.GetType().GetMethod("FindItem", Any).Invoke(tree, new object[] {instanceId});
            nativeName = item?.GetType().GetProperty("displayName", Any)?.GetValue(item) as string;
            if (item != null)
            {
                selected = (bool)tree.GetType().GetMethod("IsItemDragSelectedOrSelected", Any).Invoke(tree, new[] {item});
                object gui = tree.GetType().GetProperty("gui", Any).GetValue(tree);
                renaming = (bool)gui.GetType().GetMethod("IsRenaming", Any).Invoke(gui, new object[] {instanceId});
            }
            viewport = (Rect)browserType.GetField("m_TreeViewRect", Any).GetValue(browser);
            object state = tree.GetType().GetProperty("state", Any).GetValue(tree);
            scroll = (Vector2)state.GetType().GetField("scrollPos", Any).GetValue(state);
            if (renaming)
            {
                object overlay = state.GetType().GetProperty("renameOverlay", Any).GetValue(state);
                PropertyInfo fieldProperty = overlay.GetType().GetProperty("editFieldRect", Any);
                Rect field = (Rect)(fieldProperty != null ? fieldProperty.GetValue(overlay) : overlay.GetType().GetField("editFieldRect", Any).GetValue(overlay));
                renameRect = new Rect(GUIUtility.GUIToScreenPoint(field.position) - browser.position.position, field.size);
            }
        }
        Texture nativeTexture = null;
        if (context == "grid")
        {
            object area = browserType.GetField("m_ListArea", Any).GetValue(browser);
            object group = area.GetType().GetField("m_LocalAssets", Any).GetValue(area);
            nativeTexture = (group.GetType().GetField("m_Content", Any).GetValue(group) as GUIContent)?.image;
        }
        Rows[context + "|" + path] = new Observation {path = path, context = context, nativeName = nativeName, callback = row, screen = new Rect(screen, row.size), local = local, selected = selected, renaming = renaming, viewport = viewport, renameRect = renameRect, scroll = scroll,
            labelHeight = context == "grid" ? GUI.skin.FindStyle("ProjectBrowserGridLabel").fixedHeight : row.height,
            nativeTextureWidth = nativeTexture == null ? 0 : nativeTexture.width, nativeTextureHeight = nativeTexture == null ? 0 : nativeTexture.height};
    }
    private static void Tick()
    {
        if (inspectorHost != null) inspectorHost.Repaint();
        if (browser != null) browser.Repaint();
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (phase == 0) { ConfigureBrowser(); phase = 1; next = EditorApplication.timeSinceStartup + 2; return; }
            if (phase == 1)
            {
                native = Capture();
                File.WriteAllBytes(Path.Combine(output, "native-" + Cases[caseIndex].name + ".png"), native.EncodeToPNG());
                baselineRows = new Dictionary<string, Observation>(Rows);
                Rows.Clear(); SetEnabled(true); browser.Repaint();
                phase = 2; next = EditorApplication.timeSinceStartup + 1.5; return;
            }
            Texture2D decorated = Capture();
            Type decorator = Type.GetType("VRCLearn.ProjectDecorator.Editor.ProjectDecorator, VRCLearn.ProjectDecorator.Editor", true);
            Delegate[] delayed = EditorApplication.delayCall?.GetInvocationList() ?? new Delegate[0];
            Checks.Add("Native hooks: tree=" + (decorator.GetField("treeHook", Any).GetValue(null) != null) + ", content=" + (decorator.GetField("contentHook", Any).GetValue(null) != null) + "; pending decorator callbacks=" + string.Join(",", delayed.Where(callback => callback.Method.DeclaringType?.FullName?.Contains("ProjectDecorator") == true).Select(callback => callback.Method.ToString())));
            string name = Cases[caseIndex].name;
            string decoratedPath = Path.Combine(output, "decorated-" + name + ".png");
            File.WriteAllBytes(decoratedPath, decorated.EncodeToPNG());
            var result = new CaseResult {name = name, nativeScreenshot = Path.Combine(output, "native-" + name + ".png"), decoratedScreenshot = decoratedPath,
                nativeRows = baselineRows.Count, treeRows = Rows.Values.Count(row => row.context == "tree"), listRows = Rows.Values.Count(row => row.context == "list"), gridRows = Rows.Values.Count(row => row.context == "grid"), observations = Rows.Values.ToArray()};
            Results.Add(result);
            var notes = new List<string>();
            CompareContentLabels(native, decorated, result, notes);
            result.checks = notes.ToArray();
            CompareIndentRegions(native, decorated, result, notes);
            result.checks = notes.ToArray();
            Object.DestroyImmediate(decorated);
            caseIndex++;
            if (caseIndex == Cases.Count) { Finish(null); return; }
            OpenCase();
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void CompareContentLabels(Texture2D before, Texture2D after, CaseResult result, List<string> notes)
    {
        float scale = EditorGUIUtility.pixelsPerPoint;
        foreach (Observation row in Rows.Values.Where(item => item.context != "tree"))
        {
            if (!baselineRows.ContainsKey(row.context + "|" + row.path)) continue;
            Rect label = row.context == "grid" ? new Rect(row.local.x, row.local.yMax - row.labelHeight, row.local.width, row.labelHeight)
                : new Rect(row.local.x + 24, row.local.y, Mathf.Max(0, row.local.width - 24), row.local.height);
            RectInt pixels = PixelRect(label, scale, before);
            for (int y = pixels.yMin; y < pixels.yMax; y++)
                for (int x = pixels.xMin; x < pixels.xMax; x++)
                {
                    result.contentLabelPixels++;
                    if (before.GetPixel(x, before.height - y - 1) != after.GetPixel(x, after.height - y - 1)) result.contentLabelDifferences++;
                }
        }
        Require(result.contentLabelPixels > 0, "Content labels must be sampled from actual native callbacks.");
        Require(result.contentLabelDifferences == 0, "Native content labels/background changed outside folder icons: " + result.contentLabelDifferences + " pixels.");
        notes.Add("Native content label/background pixel identity outside icons: " + result.contentLabelPixels + " pixels, zero differences.");
    }
    private static void CompareTreeRegions(Texture2D before, Texture2D after, List<string> notes)
    {
        if (Cases[caseIndex].packages) return;
        foreach (var expected in new[] {Tuple.Create(Inherited, "blue"), Tuple.Create(Own, "red"), Tuple.Create(OwnChild, "blue"), Tuple.Create(NestedChild, "green"), Tuple.Create(OrdinaryChild, "native")})
        {
            Observation row;
            Require(Rows.TryGetValue("tree|" + expected.Item1, out row), "Expanded native tree row is missing: " + expected.Item1);
            Color color = SampleBackground(after, row);
            Color original = SampleBackground(before, baselineRows["tree|" + expected.Item1]);
            bool valid = expected.Item2 == "blue" ? color.b > color.r + 0.06f : expected.Item2 == "red" ? color.r > color.g + 0.06f : expected.Item2 == "green" ? color.g > color.r + 0.025f : MaxDifference(color, original) < 0.01f;
            Require(valid, "Tree region expectation " + expected.Item2 + " failed for " + expected.Item1 + ": " + color + "; native=" + original);
            notes.Add("Actual native tree pixel " + expected.Item1 + " -> " + expected.Item2 + " " + color);
        }
    }
    private static Observation Tree(string path)
    {
        Observation row;
        Require(Rows.TryGetValue("tree|" + path, out row), "Native expanded tree row required: " + path);
        return row;
    }
    private static void CompareIndentRegions(Texture2D before, Texture2D after, CaseResult result, List<string> notes)
    {
        if (Cases[caseIndex].packages)
        {
            string editor = Package + "/Editor", icons = editor + "/Icons";
            float packageX = Tree(Package).local.x, editorX = Tree(editor).local.x;
            Require(Tree(Package).nativeName != Path.GetFileName(Package), "Package native displayName must differ from directory ID.");
            CheckBand(before, after, editor, packageX, editorX, Blue, 0.24f, "Packages Editor ancestor Blue", notes, result);
            CheckBand(before, after, icons, packageX, editorX, Blue, 0.24f, "Packages Icons outer Blue", notes, result);
            CheckBand(before, after, icons, editorX, Tree(icons).local.x, Green, 0.24f, "Packages Icons inner Green", notes, result);
            CheckBody(before, after, editor, Green, 0.56f, notes, result);
            CheckBody(before, after, icons, Green, 0.24f, notes, result);
            return;
        }
        float blue = Tree(Region).local.x, green = Tree(Nested).local.x;
        if (Cases[caseIndex].scrollX > 0)
        {
            Require(Tree(Region).viewport.width <= 100.1f, "Actual narrow tree viewport is required.");
            notes.Add("Requested horizontal scroll=" + Cases[caseIndex].scrollX + "; observed native scroll=" + Tree(Region).scroll.x + "; viewport=" + Tree(Region).viewport + ". Native ProjectBrowser TreeViewGUI disables horizontal scrolling and clamps the request to zero; this case validates actual narrow clipping, not horizontal scrolling.");
        }
        CheckBand(before, after, Inherited, blue, Tree(Inherited).local.x, Blue, 0.24f, "Blue ancestor fills direct child indent", notes, result);
        CheckBand(before, after, Inherited + "/Grandchild Still Blue", blue, Tree(Inherited + "/Grandchild Still Blue").local.x, Blue, 0.24f, "Blue fills ordinary ancestor gap", notes, result);
        CheckBand(before, after, Nested, blue, green, Blue, 0.24f, "Nested Green owner retains outer Blue", notes, result);
        CheckBand(before, after, NestedChild, blue, green, Blue, 0.24f, "Nested child outer Blue", notes, result);
        CheckBand(before, after, NestedChild, green, Tree(NestedChild).local.x, Green, 0.24f, "Nested child inner Green", notes, result);
        CheckBand(before, after, NestedOwn, blue, green, Blue, 0.24f, "Ordinary Red under nested region retains outer Blue", notes, result);
        CheckBand(before, after, NestedOwn, green, Tree(NestedOwn).local.x, Green, 0.24f, "Ordinary Red under nested region retains inner Green", notes, result);
        CheckBand(before, after, NestedOwnChild, blue, green, Blue, 0.24f, "Nested ordinary descendant outer Blue", notes, result);
        CheckBand(before, after, NestedOwnChild, green, Tree(NestedOwnChild).local.x, Green, 0.24f, "Nested Green fills ordinary Red gap", notes, result);
        CheckBand(before, after, OwnChild, blue, Tree(OwnChild).local.x, Blue, 0.24f, "Blue fills ordinary Red parent indent", notes, result);
        CheckBand(before, after, OrdinaryChild, Tree(Ordinary).local.x, Tree(OrdinaryChild).local.x, null, 0f, "Ordinary Purple does not inherit into indent", notes, result);
        CheckBody(before, after, Inherited, Blue, 0.24f, notes, result);
        CheckBody(before, after, OwnChild, Blue, 0.24f, notes, result);
        CheckBody(before, after, Nested, Green, 0.56f, notes, result);
        CheckBody(before, after, NestedChild, Green, 0.24f, notes, result);
        CheckBody(before, after, NestedOwn, Red, 0.56f, notes, result);
        CheckBody(before, after, NestedOwnChild, Green, 0.24f, notes, result);
        CheckBody(before, after, Ordinary, Purple, 0.56f, notes, result);
        CheckBody(before, after, OrdinaryChild, null, 0f, notes, result);
        if (Cases[caseIndex].rename)
            CompareProtectedRow(before, after, Tree(Own), notes, result);
        else
        {
            if (Cases[caseIndex].selected)
                Require(Tree(Own).selected, "Native selected state must actually be active.");
            CheckBand(before, after, Own, blue, Tree(Own).local.x, Blue, 0.24f, "Ordinary Red retains left Blue ancestor band", notes, result);
            CheckBody(before, after, Own, Red, 0.56f, notes, result);
        }
        float outsideRight = Mathf.Min(blue, Tree(Region).viewport.xMax);
        if (outsideRight > Tree(Region).viewport.xMin + 2)
            CheckBand(before, after, NestedOwnChild, Tree(Region).viewport.xMin, outsideRight, null, 0f, "Outside ancestor region remains native", notes, result);
    }
    private static void CheckBody(Texture2D before, Texture2D after, string path, Color? tint, float alpha, List<string> notes, CaseResult result)
    {
        Observation row = Tree(path);
        CheckBand(before, after, path, row.viewport.xMax - 26, row.viewport.xMax - 20, tint, alpha, "Row body " + path, notes, result);
    }
    private static void CheckBand(Texture2D before, Texture2D after, string path, float left, float right, Color? tint, float alpha, string label, List<string> notes, CaseResult result)
    {
        Observation row = Tree(path);
        left = Mathf.Max(left, row.viewport.xMin); right = Mathf.Min(right, row.viewport.xMax - 16);
        if (right - left < 1.5f) { notes.Add("Fully clipped sample: " + label); result.checks = notes.ToArray(); return; }
        foreach (float fraction in new[] {0.25f, 0.75f})
            foreach (float offset in new[] {1.5f, row.local.height - 1.5f})
            {
                Vector2 point = new Vector2(Mathf.Lerp(left + 0.5f, right - 0.5f, fraction), row.local.y + offset);
                Color nativeColor = SamplePoint(before, point), actual = SamplePoint(after, point);
                Color expected = tint.HasValue ? Color.Lerp(nativeColor, new Color(tint.Value.r, tint.Value.g, tint.Value.b, 1f), alpha) : nativeColor;
                notes.Add(label + " @" + point + " expected=" + expected + " actual=" + actual);
                result.checks = notes.ToArray();
                Require(MaxDifference(actual, expected) < 0.012f, label + " failed at " + point + "; native=" + nativeColor + "; expected=" + expected + "; actual=" + actual);
            }
    }
    private static Color SamplePoint(Texture2D texture, Vector2 point)
    {
        float scale = EditorGUIUtility.pixelsPerPoint;
        int x = Mathf.Clamp(Mathf.FloorToInt(point.x * scale), 0, texture.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(point.y * scale), 0, texture.height - 1);
        return texture.GetPixel(x, texture.height - y - 1);
    }
    private static void CompareProtectedRow(Texture2D before, Texture2D after, Observation row, List<string> notes, CaseResult result)
    {
        Require(row.renaming, "Native rename state must actually be active: " + Cases[caseIndex].name);
        Require(AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(Own)) == Own, "Rename test must not rename the fixture.");
        Rect area = new Rect(row.viewport.x, row.local.y, row.viewport.width - 16, row.local.height);
        RectInt pixels = PixelRect(area, EditorGUIUtility.pixelsPerPoint, before);
        Rect edit = row.renameRect;
        edit.xMin -= 1; edit.xMax += 1;
        int compared = 0, differences = 0;
        for (int y = pixels.yMin; y < pixels.yMax; y++)
            for (int x = pixels.xMin; x < pixels.xMax; x++)
            {
                if (edit.Contains(new Vector2(x / EditorGUIUtility.pixelsPerPoint, y / EditorGUIUtility.pixelsPerPoint))) continue;
                compared++;
                if (before.GetPixel(x, before.height - y - 1) != after.GetPixel(x, after.height - y - 1)) differences++;
            }
        notes.Add("Native rename (caret/textfield masked) row: " + compared + " pixels; differences=" + differences + "; renameRect=" + row.renameRect);
        result.checks = notes.ToArray();
        Require(compared > 0 && differences == 0, "Native protected row changed: " + differences + " pixels.");
    }
    private static void EndRename()
    {
        if (browser == null || browserType == null) return;
        object tree = browserType.GetField("m_FolderTree", Any).GetValue(browser);
        tree?.GetType().GetMethod("EndNameEditing", Any).Invoke(tree, new object[] {false});
    }
    private static void VerifyBadges(Texture2D after, List<string> notes)
    {
        foreach (Observation row in Rows.Values.Where(item => item.path == Region || item.path == Own || item.path == Nested || item.path == Ordinary || item.path == Package))
        {
            Rect icon = row.local;
            icon.width = row.context == "grid" ? Mathf.Min(icon.width, icon.height - row.labelHeight) : 18;
            if (row.context == "grid") { icon.x = row.local.center.x - icon.width * 0.5f; icon.height = icon.width; }
            RectInt pixels = PixelRect(icon, EditorGUIUtility.pixelsPerPoint, after);
            int magenta = 0;
            for (int y = pixels.yMin; y < pixels.yMax; y++)
                for (int x = pixels.xMin; x < pixels.xMax; x++)
                {
                    Color color = after.GetPixel(x, after.height - y - 1);
                    if (color.r > 0.94f && color.g < 0.06f && color.b > 0.94f) magenta++;
                }
            Require(magenta > 0, "Original-color magenta badge missing/tinted: " + row.context + " " + row.path);
            notes.Add("Original-color badge visible: " + row.context + " " + row.path + " (" + magenta + " pixels)");
        }
    }
    private static Color SampleBackground(Texture2D image, Observation row)
    {
        float scale = EditorGUIUtility.pixelsPerPoint;
        int x = Mathf.Clamp(Mathf.RoundToInt((row.local.xMax - 7) * scale), 0, image.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(row.local.center.y * scale), 0, image.height - 1);
        return image.GetPixel(x, image.height - y - 1);
    }
    private static float MaxDifference(Color left, Color right) => Mathf.Max(Mathf.Abs(left.r - right.r), Mathf.Abs(left.g - right.g), Mathf.Abs(left.b - right.b));
    private static RectInt PixelRect(Rect rect, float scale, Texture2D image)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(rect.x * scale), 0, image.width), y = Mathf.Clamp(Mathf.FloorToInt(rect.y * scale), 0, image.height);
        return new RectInt(x, y, Mathf.Clamp(Mathf.CeilToInt(rect.xMax * scale), x, image.width) - x, Mathf.Clamp(Mathf.CeilToInt(rect.yMax * scale), y, image.height) - y);
    }
    private static Texture2D Capture()
    {
        float scale = EditorGUIUtility.pixelsPerPoint;
        int width = Mathf.RoundToInt(browser.position.width * scale), height = Mathf.RoundToInt(browser.position.height * scale);
        object view = typeof(EditorWindow).GetField("m_Parent", Any).GetValue(browser);
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView", true).GetMethod("GrabPixels", Any).Invoke(view, new object[] {target, new Rect(0, 0, width, height)});
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            Color32[] pixels = texture.GetPixels32(), flipped = new Color32[pixels.Length];
            for (int row = 0; row < height; row++) Array.Copy(pixels, row * width, flipped, (height - row - 1) * width, width);
            texture.SetPixels32(flipped); texture.Apply(); return texture;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && Errors.Count < 12 && !Errors.Contains(message + "\n" + stack)) Errors.Add(message + "\n" + stack);
    }
    private static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        EditorApplication.projectWindowItemInstanceOnGUI -= Observe;
        if (exception != null) Errors.Add(exception.ToString());
        EndRename();
        if (browser != null) browser.Close();
        if (inspectorHost != null) inspectorHost.Close();
        if (native != null) Object.DestroyImmediate(native);
        if (settings != null && originalSettings != null)
        {
            EditorJsonUtility.FromJsonOverwrite(originalSettings, settings);
            settingsType.GetField("lookup", Any).SetValue(settings, null);
            settingsType.GetMethod("Persist", Any).Invoke(settings, null);
        }
        Application.logMessageReceived -= OnLog;
        if (output == null) output = Path.GetFullPath("Logs/ProjectRegions");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "project-regions.json"), JsonUtility.ToJson(new Report {version = Application.unityVersion, generatedAt = DateTimeOffset.Now.ToString("o"), passed = Errors.Count == 0, checks = Checks.ToArray(), errors = Errors.ToArray(), cases = Results.ToArray()}, true));
        EditorApplication.Exit(Errors.Count == 0 ? 0 : 1);
    }
}
