using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Sends native EditorWindow events. Never calls the product input handler.
public static class ProjectInputProbe
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
    private const string Root = "Assets/__ProjectInputProbe";
    private const string Child = Root + "/Child";
    private const string FilePath = Root + "/NativeFile.txt";
    [Serializable] private sealed class Row { public string path, context; public Rect rect; }
    [Serializable] private sealed class Trace
    {
        public string test, stage, path, context, type, rawType, pressed, activeGuid, folder;
        public int hotControl;
        public int instanceId;
        public int pressedControl;
        public string callbackGuid;
        public bool editingTextField, nativeRenaming;
        public int[] selection;
        public Vector2 mouse;
        public Rect callbackRect;
        public bool pointerInside;
    }
    [Serializable] private sealed class Result
    {
        public string name, expectedGuid, actualGuid, beforeFolder, afterFolder;
        public bool passed, popupOpened;
        public bool nativeChildSelected;
        public bool canceledSafely, recoveryOpened;
        public bool favoriteBefore, favoriteAfter, favoriteToggled;
        public int[] beforeSelection, afterSelection;
        public string failure;
    }
    [Serializable] private sealed class Report
    {
        public string version, generatedAt, mode;
        public bool passed;
        public Result[] cases;
        public Trace[] events;
        public string[] errors;
    }
    private sealed class Test { public string name, context, action; public int grid; }
    private static readonly List<Test> Tests = new List<Test>();
    private static readonly List<Result> Results = new List<Result>();
    private static readonly List<Trace> Traces = new List<Trace>();
    private static readonly List<string> Errors = new List<string>();
    private static readonly Dictionary<string, Row> Rows = new Dictionary<string, Row>();
    private static EditorWindow browser, inspector;
    private static Type browserType, paletteType, settingsType;
    private static Object settings;
    private static string settingsJson, mode;
    private static bool started, sending;
    private static int index, phase;
    private static double next;
    private static Result current;
    private static Vector2 recoveryPoint;

    [InitializeOnLoadMethod] private static void Bootstrap()
    {
        if (Environment.GetCommandLineArgs().Contains("ProjectInputProbe.Run")) Run();
    }
    public static void Run() { EditorApplication.update -= Begin; EditorApplication.update += Begin; }
    private static void Begin()
    {
        if (started || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < 3) return;
        EditorApplication.update -= Begin; started = true;
        try
        {
            if (!File.Exists("ProjectSettings/StyleParity.enabled")) throw new InvalidOperationException("Disposable marker required.");
            mode = Environment.GetCommandLineArgs().Contains("-inputBaseline") ? "baseline" : "final";
            Application.logMessageReceived += OnLog;
            browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser", true);
            paletteType = Type.GetType("VRCLearn.ProjectDecorator.Editor.FolderDecorationPopup, VRCLearn.ProjectDecorator.Editor", true);
            settingsType = Type.GetType("VRCLearn.ProjectDecorator.Editor.ProjectDecoratorSettings, VRCLearn.ProjectDecorator.Editor", true);
            settings = (Object)settingsType.GetProperty("instance", Any).GetValue(null); settingsJson = EditorJsonUtility.ToJson(settings);
            settingsType.GetField("decorationsEnabled", Any).SetValue(settings, true);
            settingsType.GetField("showFavoriteStars", Any).SetValue(settings, true);
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "__ProjectInputProbe");
            if (!AssetDatabase.IsValidFolder(Child)) AssetDatabase.CreateFolder(Root, "Child");
            File.WriteAllText(FilePath, "Native ProjectBrowser input fixture"); AssetDatabase.ImportAsset(FilePath);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(FilePath);
            inspector = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow", true));
            inspector.ShowUtility(); inspector.position = new Rect(30, 520, 380, 300);
            foreach (string context in new[] {"list", "grid", "tree"})
                foreach (string action in mode == "baseline" ? new[] {"alt"} : new[] {"alt", "alt-selected", "click", "double", "alt-file", "alt-blank", "alt-star", "cancel-blank", "cancel-pane"})
                    if (context != "tree" || action != "alt-file") Tests.Add(new Test {name = context + "/" + action, context = context, action = action, grid = context == "grid" ? 64 : 16});
            // This observer records the event after Unity native handling but before
            // the existing product projectWindowItemInstanceOnGUI delegate.
            var nativePrior = EditorApplication.projectWindowItemInstanceOnGUI;
            EditorApplication.projectWindowItemInstanceOnGUI = Before + nativePrior + After;
            EditorApplication.projectWindowItemOnGUI += GuidCallback;
            browser = (EditorWindow)ScriptableObject.CreateInstance(browserType); browser.ShowUtility(); browser.position = new Rect(80, 60, 1040, 700);
            phase = 0; next = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void Configure()
    {
        ClosePalette(); Rows.Clear();
        var modeField = browserType.GetField("m_ViewMode", Any);
        if (Convert.ToInt32(modeField.GetValue(browser)) != 1) browserType.GetMethod("InitViewMode", Any).Invoke(browser, new[] {Enum.ToObject(modeField.FieldType, 1)});
        browserType.GetMethod("ShowFolderContents", Any, null, new[] {typeof(int), typeof(bool)}, null).Invoke(browser, new object[] {AssetDatabase.LoadMainAssetAtPath(Root).GetInstanceID(), true});
        browserType.GetField("m_DirectoriesAreaWidth", Any).SetValue(browser, 360f);
        object list = browserType.GetField("m_ListArea", Any).GetValue(browser);
        PropertyInfo gridSize = list.GetType().GetProperty("gridSize", Any);
        gridSize.SetValue(list, Convert.ChangeType(Tests[index].grid, gridSize.PropertyType));
        object tree = browserType.GetField("m_FolderTree", Any).GetValue(browser);
        object data = tree.GetType().GetProperty("data", Any).GetValue(tree);
        MethodInfo expanded = data.GetType().GetMethod("SetExpanded", Any, null, new[] {typeof(int), typeof(bool)}, null);
        foreach (string path in new[] {"Assets", Root}) expanded.Invoke(data, new object[] {AssetDatabase.LoadMainAssetAtPath(path).GetInstanceID(), true});
        tree.GetType().GetMethod("ReloadData", Any).Invoke(tree, null);
        Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(FilePath);
        if (Tests[index].action == "alt-selected")
        {
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(Child);
            list.GetType().GetMethod("InitSelection", Any).Invoke(list, new object[] {Selection.instanceIDs});
        }
        browser.Focus(); browser.Repaint();
    }
    private static EditorWindow CurrentWindow()
    {
        object view = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView", true).GetProperty("current", Any).GetValue(null);
        return view?.GetType().GetProperty("actualView", Any)?.GetValue(view) as EditorWindow;
    }
    private static string Folder() => browser == null ? null : (string)browserType.GetMethod("GetActiveFolderPath", Any).Invoke(browser, null);
    private static string ActiveGuid()
    {
        var popup = paletteType.GetField("activePalette", Any).GetValue(null) as EditorWindow;
        return popup == null ? null : (string)paletteType.GetField("targetGuid", Any).GetValue(popup);
    }
    private static string Pressed() => (string)paletteType.GetField("pressedPaletteTarget", Any).GetValue(null);
    private static int PressedControl() => (int)(paletteType.GetField("pressedPaletteControl", Any)?.GetValue(null) ?? 0);
    private static void TraceEvent(string stage, string path, string context, int id, string guid, Rect rect)
    {
        Event evt = Event.current;
        object list = browserType.GetField("m_ListArea", Any).GetValue(browser);
        object rename = list.GetType().GetMethod("GetRenameOverlay", Any).Invoke(list, null);
        bool renaming = (bool)rename.GetType().GetMethod("IsRenaming", Any).Invoke(rename, null);
        Traces.Add(new Trace {test = Tests[index].name, stage = stage, path = path, context = context, instanceId = id, callbackGuid = guid, editingTextField = EditorGUIUtility.editingTextField, nativeRenaming = renaming, type = evt.type.ToString(), rawType = evt.rawType.ToString(), pressed = Pressed(), pressedControl = PressedControl(), activeGuid = ActiveGuid(), folder = Folder(), hotControl = GUIUtility.hotControl, selection = Selection.instanceIDs, mouse = evt.mousePosition, callbackRect = rect, pointerInside = rect.Contains(evt.mousePosition)});
    }
    private static void Before(int id, Rect rect) { Observe(id, rect, "before-product"); }
    private static void After(int id, Rect rect) { Observe(id, rect, "after-product"); }
    private static void GuidCallback(string guid, Rect rect) { Observe(0, rect, "native-guid-callback", guid); }
    private static void Observe(int id, Rect rect, string stage, string guid = null)
    {
        if (browser == null || CurrentWindow() != browser) return;
        string path = guid != null ? AssetDatabase.GUIDToAssetPath(guid) : AssetDatabase.GetAssetPath(id);
        bool fixture = path == Child || path == FilePath;
        if (!fixture && (!sending || id != 0 || guid != null)) return;
        Vector2 local = GUIUtility.GUIToScreenPoint(rect.position) - browser.position.position;
        Rect content = (Rect)browserType.GetField("m_ListAreaRect", Any).GetValue(browser);
        string context = local.x < content.x ? "tree" : rect.height > 24 ? "grid" : "list";
        if (fixture && Event.current.type == EventType.Repaint) Rows[context + "|" + path] = new Row {path = path, context = context, rect = new Rect(local, rect.size)};
        if (sending) TraceEvent(stage, path, context, id, guid, rect);
    }
    private static void Send(Vector2 point, EventType type, bool alt, int count)
    {
        var evt = new Event {type = type, mousePosition = point, button = 0, clickCount = count, modifiers = alt ? EventModifiers.Alt : EventModifiers.None};
        sending = true;
        try { browser.SendEvent(evt); }
        finally { sending = false; }
        Traces.Add(new Trace {test = Tests[index].name, stage = "after-SendEvent", type = evt.type.ToString(), rawType = evt.rawType.ToString(), pressed = Pressed(), pressedControl = PressedControl(), activeGuid = ActiveGuid(), folder = Folder(), selection = Selection.instanceIDs, mouse = point});
    }
    private static void Tick()
    {
        if (inspector != null) inspector.Repaint(); if (browser != null) browser.Repaint();
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (phase == 0) { Configure(); phase = 1; next = EditorApplication.timeSinceStartup + 1; return; }
            if (phase == 1)
            {
                Test test = Tests[index]; string path = test.action == "alt-file" ? FilePath : Child;
                Row row;
                if (!Rows.TryGetValue(test.context + "|" + path, out row)) throw new InvalidOperationException("Missing real native callback: " + test.name + " " + path);
                current = new Result {name = test.name, expectedGuid = AssetDatabase.AssetPathToGUID(Child), beforeFolder = Folder(), beforeSelection = Selection.instanceIDs};
                current.favoriteBefore = (bool)settingsType.GetMethod("IsFavorite", Any).Invoke(settings, new object[] {current.expectedGuid});
                Vector2 point = row.rect.center;
                if (test.context != "grid") point.x = row.rect.x + 26;
                if (test.action == "alt-star") point = new Vector2(row.rect.xMax - 9, row.rect.y + 8);
                if (test.action == "alt-blank") point = new Vector2(test.context == "tree" ? 200 : 900, 650);
                bool alt = test.action.StartsWith("alt", StringComparison.Ordinal) || test.action.StartsWith("cancel", StringComparison.Ordinal);
                recoveryPoint = point;
                Vector2 release = point;
                if (test.action == "cancel-blank") release = new Vector2(test.context == "tree" ? 200 : 900, 650);
                if (test.action == "cancel-pane")
                {
                    Row opposite = Rows[(test.context == "tree" ? "list" : "tree") + "|" + Child];
                    release = new Vector2(opposite.rect.x + 26, opposite.rect.center.y);
                }
                Send(point, EventType.MouseDown, alt, 1); Send(release, EventType.MouseUp, alt, 1);
                if (test.action == "double") { Send(point, EventType.MouseDown, false, 2); Send(point, EventType.MouseUp, false, 2); }
                phase = 2; next = EditorApplication.timeSinceStartup + 0.3; return;
            }
            Test item = Tests[index];
            current.actualGuid = ActiveGuid(); current.popupOpened = !string.IsNullOrEmpty(current.actualGuid); current.afterFolder = Folder(); current.afterSelection = Selection.instanceIDs;
            current.favoriteAfter = (bool)settingsType.GetMethod("IsFavorite", Any).Invoke(settings, new object[] {current.expectedGuid});
            current.favoriteToggled = current.favoriteBefore != current.favoriteAfter;
            object selectionOwner = browserType.GetField(item.context == "tree" ? "m_FolderTree" : "m_ListArea", Any).GetValue(browser);
            var nativeSelection = ((IEnumerable)selectionOwner.GetType().GetMethod("GetSelection", Any).Invoke(selectionOwner, null)).Cast<int>();
            current.nativeChildSelected = nativeSelection.Contains(AssetDatabase.LoadMainAssetAtPath(Child).GetInstanceID());
            bool sameSelection = current.beforeSelection.OrderBy(value => value).SequenceEqual(current.afterSelection.OrderBy(value => value));
            if (item.action.StartsWith("cancel", StringComparison.Ordinal))
            {
                if (phase == 2)
                {
                    current.canceledSafely = !current.popupOpened && sameSelection && current.afterFolder == current.beforeFolder;
                    ClosePalette();
                    Send(recoveryPoint, EventType.MouseDown, true, 1); Send(recoveryPoint, EventType.MouseUp, true, 1);
                    phase = 3; next = EditorApplication.timeSinceStartup + 0.3; return;
                }
                current.recoveryOpened = current.popupOpened && current.actualGuid == current.expectedGuid && sameSelection && current.afterFolder == current.beforeFolder;
                current.passed = current.canceledSafely && current.recoveryOpened;
            }
            else if (item.action == "alt" || item.action == "alt-selected") current.passed = current.popupOpened && current.actualGuid == current.expectedGuid && sameSelection && current.afterFolder == current.beforeFolder;
            else if (item.action == "double") current.passed = !current.popupOpened && current.afterFolder == Child;
            else if (item.action == "click") current.passed = !current.popupOpened && current.nativeChildSelected;
            else if (item.action == "alt-star") current.passed = !current.popupOpened && current.favoriteToggled;
            else current.passed = !current.popupOpened;
            if (!current.passed) current.failure = "Observed popup/target/selection/navigation violates native integration expectation; see event traces.";
            Results.Add(current); ClosePalette(); index++;
            if (index == Tests.Count) { Finish(null); return; }
            phase = 0; next = EditorApplication.timeSinceStartup + 0.1;
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void ClosePalette() { (paletteType?.GetField("activePalette", Any).GetValue(null) as EditorWindow)?.Close(); }
    private static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message + "\n" + stack); }
    private static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        EditorApplication.projectWindowItemInstanceOnGUI -= Before; EditorApplication.projectWindowItemInstanceOnGUI -= After;
        EditorApplication.projectWindowItemOnGUI -= GuidCallback;
        if (exception != null) Errors.Add(exception.ToString()); ClosePalette();
        if (browser != null) browser.Close(); if (inspector != null) inspector.Close();
        if (settings != null && settingsJson != null) { EditorJsonUtility.FromJsonOverwrite(settingsJson, settings); settingsType.GetField("lookup", Any).SetValue(settings, null); settingsType.GetMethod("Persist", Any).Invoke(settings, null); }
        Application.logMessageReceived -= OnLog;
        Directory.CreateDirectory("Logs/ProjectInput");
        bool passed = Errors.Count == 0 && Results.Count != 0 && Results.All(result => result.passed);
        File.WriteAllText("Logs/ProjectInput/" + (mode ?? "failed") + ".json", JsonUtility.ToJson(new Report {version = Application.unityVersion, generatedAt = DateTimeOffset.Now.ToString("o"), mode = mode, passed = passed, cases = Results.ToArray(), events = Traces.ToArray(), errors = Errors.ToArray()}, true));
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
