using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Disposable real HierarchyDecorator vs real ProjectBrowser selection comparison.
public static class ProjectSelectionProbe
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
    private const string RootPath = "Assets/__ProjectSelectionProbe";
    private static readonly Color Blue = new Color32(103,151,233,255), Red = new Color32(222,104,111,255), Green = new Color32(130,181,107,255);
    private static readonly Color StateWash = new Color(0.214f,0.42f,0.76f,1f);
    private sealed class Node { public string name, path; public int parent; public Color? color; public bool region; public GameObject gameObject; public Object folder; }
    private sealed class Job { public string name, pane; public bool focused, alternate, rename; }
    [Serializable] private sealed class Row { public string path; public Rect rect, renameRect; public bool selected, focused, renaming; }
    [Serializable] private sealed class Sample { public string key; public Vector2 position; public Color baseline, actual, expected; }
    [Serializable] private sealed class Result { public string name, pane, nativeScreenshot, decoratedScreenshot; public bool focused, alternate; public Row[] rows; public List<Sample> samples = new List<Sample>(); public List<string> checks = new List<string>(); }
    [Serializable] private sealed class Report { public string version, generatedAt; public bool passed; public Result[] cases; public string[] errors, diagnostics; }
    private static readonly List<Node> Nodes = new List<Node>();
    private static readonly List<Job> Jobs = new List<Job>();
    private static readonly List<Result> Results = new List<Result>();
    private static readonly List<string> Errors = new List<string>();
    private static readonly List<string> Diagnostics = new List<string>();
    private static readonly Dictionary<string, Row> ProjectRows = new Dictionary<string, Row>(), HierarchyRows = new Dictionary<string, Row>();
    private static readonly int[] Selected = {2,3,5,6};
    private static Type projectType, hdOwnerType;
    private static Object projectSettings, hdSettings;
    private static string projectJson, hdJson, output;
    private static object hdColors;
    private static EditorWindow project, hierarchy, inspector;
    private static bool started;
    private static int index, phase;
    private static double next, readyDeadline;
    private static Texture2D native;
    private static Dictionary<string, Row> baselineRows;
    private static byte[] hdFile, projectFile;
    private static bool filesCaptured, referenceReady;
    private static object Read(object target, string name) { PropertyInfo property = target.GetType().GetProperty(name, Any); return property != null ? property.GetValue(target) : target.GetType().GetField(name, Any).GetValue(target); }
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(Any).First(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
    private static object Tree(string pane) => pane == "Hierarchy" ? Read(Read(hierarchy, "sceneHierarchy"), "treeView") : Read(project, "m_FolderTree");
    private static EditorWindow Window(string pane) => pane == "Hierarchy" ? hierarchy : project;
    private static Dictionary<string, Row> Rows(string pane) => pane == "Hierarchy" ? HierarchyRows : ProjectRows;
    private static Type Find(string name, string assembly) => Type.GetType(name + ", " + assembly, true);
    [InitializeOnLoadMethod] private static void Bootstrap() { if (Environment.GetCommandLineArgs().Contains("ProjectSelectionProbe.Run")) Run(); }
    public static void Run() { EditorApplication.update -= Begin; EditorApplication.update += Begin; }
    private static void Begin()
    {
        if (started || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < 3) return;
        started = true; EditorApplication.update -= Begin;
        try
        {
            if (!File.Exists("ProjectSettings/StyleParity.enabled")) throw new InvalidOperationException("Disposable marked project required.");
            hdFile = File.Exists("UserSettings/Settings.asset") ? File.ReadAllBytes("UserSettings/Settings.asset") : null;
            projectFile = File.Exists("UserSettings/ProjectDecorator.asset") ? File.ReadAllBytes("UserSettings/ProjectDecorator.asset") : null;
            filesCaptured = true;
            output = Path.GetFullPath("Logs/ProjectSelection"); Directory.CreateDirectory(output); Application.logMessageReceived += OnLog;
            projectType = Find("VRCLearn.ProjectDecorator.Editor.ProjectDecoratorSettings", "VRCLearn.ProjectDecorator.Editor");
            projectSettings = (Object)projectType.GetProperty("instance", Any).GetValue(null); projectJson = EditorJsonUtility.ToJson(projectSettings);
            hdOwnerType = Find("HierarchyDecorator.Editor.HierarchyDecorator", "VRCLearn.HierarchyDecorator.Editor");
            hdSettings = (Object)hdOwnerType.GetProperty("Settings", Any).GetValue(null); hdJson = EditorJsonUtility.ToJson(hdSettings);
            hdColors = Read(hdSettings, "RowColors");
            object global = Read(hdSettings, "globalData"), style = Read(hdSettings, "styleData");
            global.GetType().GetField("showActiveToggles", Any).SetValue(global, false); global.GetType().GetField("showBreadcrumbs", Any).SetValue(global, false);
            style.GetType().GetField("displayTags", Any).SetValue(style, false); style.GetType().GetField("displayLayers", Any).SetValue(style, false);
            ((IList)style.GetType().GetField("styles", Any).GetValue(style)).Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Add("__ProjectSelectionProbe", -1, null, false); Add("01 Blue Region", 0, Blue, true);
            Add("01 Inherited", 1, null, false); Add("02 Own Red", 1, Red, false); Add("03 Nested Green", 1, Green, true);
            Add("01 Nested Child", 4, null, false); Add("04 Plain", 0, null, false);
            EditorSceneManager.SaveScene(Nodes[0].gameObject.scene, "Assets/ProjectSelectionProbe.unity");
            Selection.activeGameObject = Nodes[0].gameObject;
            hierarchy = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow", true)); hierarchy.ShowUtility(); hierarchy.position = new Rect(30,60,420,500);
            project = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser", true)); project.ShowUtility(); project.position = new Rect(470,60,1040,500);
            inspector = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow", true)); inspector.ShowUtility(); inspector.position = new Rect(30,580,380,280);
            EditorApplication.hierarchyWindowItemOnGUI += ObserveHierarchy; EditorApplication.projectWindowItemInstanceOnGUI += ObserveProject;
            foreach (bool alternate in new[] {false,true}) foreach (bool focused in new[] {true,false}) foreach (string pane in new[] {"Hierarchy","Project"})
                Jobs.Add(new Job {name = (alternate ? "alternate" : "default") + "-" + (focused ? "focused" : "unfocused") + "-" + pane.ToLowerInvariant(), pane = pane, focused = focused, alternate = alternate});
            // Native rename is verified by the existing ProjectRegionProbe,
            // chained below; do not maintain a second rename fixture here.
            next = EditorApplication.timeSinceStartup + 3; EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }
    private static void Add(string name, int parent, Color? color, bool region)
    {
        string path = parent < 0 ? RootPath : Nodes[parent].path + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent < 0 ? "Assets" : Nodes[parent].path, parent < 0 ? "__ProjectSelectionProbe" : name);
        var gameObject = new GameObject(name); if (parent >= 0) gameObject.transform.SetParent(Nodes[parent].gameObject.transform);
        EditorGUIUtility.SetIconForObject(gameObject, EditorGUIUtility.IconContent("Folder Icon").image as Texture2D);
        Nodes.Add(new Node {name = name, parent = parent, path = path, color = color, region = region, gameObject = gameObject, folder = AssetDatabase.LoadMainAssetAtPath(path)});
    }
    private static void Colors(bool enabled, Job job)
    {
        projectType.GetField("decorationsEnabled", Any).SetValue(projectSettings, enabled); projectType.GetField("alternateRows", Any).SetValue(projectSettings, job.alternate); projectType.GetField("showFavoriteStars", Any).SetValue(projectSettings, false);
        object style = Read(hdSettings, "styleData"); style.GetType().GetField("twoToneBackground", Any).SetValue(style, job.alternate);
        foreach (Node node in Nodes.Where(node => node.color.HasValue))
        {
            Color? color = enabled ? node.color : null;
            hdColors.GetType().GetMethod("SetColor", Any).Invoke(hdColors, new object[] {node.gameObject, color, node.region});
            projectType.GetMethod("SetColorRegion", Any).Invoke(projectSettings, new object[] {AssetDatabase.AssetPathToGUID(node.path), color, node.region});
        }
    }
    private static void Configure(Job job)
    {
        if (!referenceReady) PrepareReference(job);
        EndRename();
        object mode = Read(project, "m_ViewMode"); if (Convert.ToInt32(mode) != 1) Call(project, "InitViewMode", Enum.ToObject(mode.GetType(), 1));
        project.GetType().GetField("m_DirectoriesAreaWidth", Any).SetValue(project, 420f);
        Call(project, "ShowFolderContents", Nodes[0].folder.GetInstanceID(), true);
        object list = Read(project, "m_ListArea"); PropertyInfo size = list.GetType().GetProperty("gridSize", Any); size.SetValue(list, Convert.ChangeType(16, size.PropertyType));
        foreach (string pane in new[] {"Hierarchy","Project"})
        {
            object tree = Tree(pane), data = Read(tree, "data");
            foreach (Node node in Nodes) Call(data, "SetExpanded", pane == "Hierarchy" ? node.gameObject.GetInstanceID() : node.folder.GetInstanceID(), true);
            Call(data, "SetExpanded", pane == "Hierarchy" ? Nodes[0].gameObject.scene.handle : AssetDatabase.LoadMainAssetAtPath("Assets").GetInstanceID(), true);
            Call(tree, "ReloadData");
        }
        Colors(false, job);
        Selection.objects = Selected.Select(i => (Object)Nodes[i].gameObject).Concat(Selected.Select(i => Nodes[i].folder)).ToArray();
        SetSelections(job);
        if (job.focused) { Window(job.pane).Focus(); Call(Tree(job.pane), "GrabKeyboardFocus"); } else inspector.Focus();
        if (job.rename)
        {
            Selection.objects = new[] {Nodes[3].folder};
            Call(Tree("Project"), "SetSelection", new[] {Nodes[3].folder.GetInstanceID()}, false);
            if (!(bool)Call(Tree("Project"), "BeginNameEditing", 0f)) throw new InvalidOperationException("Native rename did not start.");
        }
        ProjectRows.Clear(); HierarchyRows.Clear();
    }
    private static void PrepareReference(Job job)
    {
        bool registered = (EditorApplication.hierarchyWindowItemOnGUI?.GetInvocationList() ?? new Delegate[0]).Any(callback => callback.Method.DeclaringType?.FullName == "HierarchyDecorator.Editor.HierarchyManager");
        Diagnostics.Add("HD native handler initially registered=" + registered + "; same Settings instance=" + ReferenceEquals(hdSettings, hdOwnerType.GetProperty("Settings", Any).GetValue(null)) + "; pending=" + string.Join(",", (EditorApplication.delayCall?.GetInvocationList() ?? new Delegate[0]).Select(callback => callback.Method.DeclaringType?.Name + "." + callback.Method.Name)));
        Require(ReferenceEquals(hdSettings, hdOwnerType.GetProperty("Settings", Any).GetValue(null)), "HD reference Settings instance changed.");
        Colors(true, job);
        Type palette = Find("HierarchyDecorator.Editor.GameObjectIconWindow", "VRCLearn.HierarchyDecorator.Editor");
        foreach (Node node in Nodes.Where(node => node.color.HasValue))
        {
            object[] args = {node.gameObject, default(Color), false};
            bool found = (bool)hdColors.GetType().GetMethod("TryGetColor", Any).Invoke(hdColors, args);
            bool paletteColor = (bool)palette.GetMethod("IsPaletteColor", Any).Invoke(null, new object[] {hdSettings, node.color.Value});
            Diagnostics.Add("HD " + node.path + ": TryGetColor=" + found + " color=" + args[1] + " region=" + args[2] + "; palette=" + paletteColor + "; scene=" + node.gameObject.scene.path + "; flags=" + node.gameObject.hideFlags + "; icon=" + EditorGUIUtility.GetIconForObject(node.gameObject)?.name);
            Require(found && paletteColor && !string.IsNullOrEmpty(node.gameObject.scene.path) && node.gameObject.hideFlags == HideFlags.None, "HD reference fixture is not renderable.");
        }
        if (!registered)
        {
            Find("HierarchyDecorator.Editor.HierarchyManager", "VRCLearn.HierarchyDecorator.Editor").GetMethod("Initialize", Any).Invoke(null, null);
            Diagnostics.Add("Called original public HierarchyManager.Initialize to register its real native rendering callback in this marked CLI fixture. This is a rendering comparison, not an HD startup-lifecycle pass.");
        }
        referenceReady = true;
    }
    private static void SetSelections(Job job)
    {
        if (job.rename) return;
        foreach (string pane in new[] {"Hierarchy","Project"}) Call(Tree(pane), "SetSelection", Selected.Select(i => pane == "Hierarchy" ? Nodes[i].gameObject.GetInstanceID() : Nodes[i].folder.GetInstanceID()).ToArray(), false);
    }
    private static EditorWindow CurrentWindow()
    {
        object view = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView", true).GetProperty("current", Any).GetValue(null);
        return view?.GetType().GetProperty("actualView", Any)?.GetValue(view) as EditorWindow;
    }
    private static void ObserveHierarchy(int id, Rect rect) { Node node = Nodes.FirstOrDefault(item => item.gameObject.GetInstanceID() == id); if (node != null) Observe(node, "Hierarchy", rect); }
    private static void ObserveProject(int id, Rect rect) { Node node = Nodes.FirstOrDefault(item => item.folder.GetInstanceID() == id); if (node != null) Observe(node, "Project", rect); }
    private static void Observe(Node node, string pane, Rect rect)
    {
        if (Event.current.type != EventType.Repaint || CurrentWindow() != Window(pane)) return;
        EditorWindow window = Window(pane); Vector2 local = GUIUtility.GUIToScreenPoint(rect.position) - window.position.position;
        if (pane == "Project" && local.x >= ((Rect)Read(project, "m_ListAreaRect")).xMin) return;
        object tree = Tree(pane), gui = Read(tree, "gui"), item = Call(tree, "FindItem", pane == "Hierarchy" ? node.gameObject.GetInstanceID() : node.folder.GetInstanceID());
        int id = pane == "Hierarchy" ? node.gameObject.GetInstanceID() : node.folder.GetInstanceID();
        bool renaming = (bool)Call(gui, "IsRenaming", id);
        Rect edit = default;
        if (renaming) { object overlay = Read(Read(tree, "state"), "renameOverlay"); Rect field = (Rect)Read(overlay, "editFieldRect"); edit = new Rect(GUIUtility.GUIToScreenPoint(field.position) - window.position.position, field.size); }
        Rows(pane)[node.path] = new Row {path = node.path, rect = new Rect(local, rect.size), selected = (bool)Call(tree, "IsItemDragSelectedOrSelected", item), focused = (bool)Call(tree, "HasFocus"), renaming = renaming, renameRect = edit};
    }
    private static void Tick()
    {
        if (inspector != null) inspector.Repaint(); if (hierarchy != null) hierarchy.Repaint(); if (project != null) project.Repaint();
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            Job job = Jobs[index];
            if (phase == 0) { Configure(job); phase = 1; next = EditorApplication.timeSinceStartup + 1.2; readyDeadline = EditorApplication.timeSinceStartup + 10; return; }
            SetSelections(job);
            if (!NativeReady(job))
            {
                if (EditorApplication.timeSinceStartup > readyDeadline) throw new InvalidOperationException("Actual native selection/focus did not settle before capture: " + job.name);
                next = EditorApplication.timeSinceStartup + 0.1; return;
            }
            if (phase == 1)
            {
                if (!Rows(job.pane).ContainsKey(Nodes[3].path)) throw new InvalidOperationException("Actual native row callbacks missing.");
                native = Capture(Window(job.pane)); baselineRows = new Dictionary<string, Row>(Rows(job.pane));
                File.WriteAllBytes(Path.Combine(output, "native-" + job.name + ".png"), native.EncodeToPNG());
                Colors(true, job); phase = 2; next = EditorApplication.timeSinceStartup + 0.8; readyDeadline = EditorApplication.timeSinceStartup + 10; return;
            }
            Texture2D decorated = Capture(Window(job.pane)); string path = Path.Combine(output, "decorated-" + job.name + ".png"); File.WriteAllBytes(path, decorated.EncodeToPNG());
            var result = new Result {name = job.name, pane = job.pane, focused = job.focused, alternate = job.alternate, nativeScreenshot = Path.Combine(output, "native-" + job.name + ".png"), decoratedScreenshot = path, rows = Rows(job.pane).Values.ToArray()}; Results.Add(result);
            if (job.rename) CheckRename(native, decorated, result); else CheckSelected(native, decorated, result, job);
            Object.DestroyImmediate(native); native = null; Object.DestroyImmediate(decorated); EndRename(); index++; phase = 0;
            if (index == Jobs.Count) { Finish(null); return; }
            next = EditorApplication.timeSinceStartup + 0.1;
        }
        catch (Exception error) { Finish(error); }
    }
    private static bool NativeReady(Job job)
    {
        var rows=Rows(job.pane);
        foreach(int i in job.rename?new[]{3}:Selected)
        {
            Row row;
            if(!rows.TryGetValue(Nodes[i].path,out row)||!row.selected||(!job.rename&&row.focused!=job.focused)||(job.rename&&!row.renaming))return false;
        }
        return true;
    }
    private static void CheckSelected(Texture2D before, Texture2D after, Result result, Job job)
    {
        Dictionary<string, Row> rows = Rows(job.pane);
        foreach (int index in Selected)
        {
            Row row = rows[Nodes[index].path];
            Require(row.selected && baselineRows[row.path].selected, "Actual native selected row required: " + row.path);
            Require(row.focused == job.focused && baselineRows[row.path].focused == job.focused, "Actual native focus state mismatch: " + job.name + " " + row.focused);
            Color? color = index == 3 ? Red : index == 2 ? Blue : index == 5 ? Green : (Color?)null;
            float alpha = index == 3 ? 0.56f : color.HasValue ? 0.24f : 0f;
            Vector2 body = new Vector2(340, row.rect.y + 0.5f), corner = new Vector2(row.rect.x + 0.5f, row.rect.y + 0.5f);
            CheckSample(before, after, result, "body/" + index, body, color, alpha);
            CheckSample(before, after, result, "icon/" + index, corner, color, alpha);
            Require(Diff(Point(after, body), Point(after, corner)) < 0.015f, "Native icon background differs from body: " + job.name + " " + index);
            if (job.alternate) { Color basis = Point(before, body); Color unwashed=new Color((basis.r-StateWash.r*0.2f)/0.8f,(basis.g-StateWash.g*0.2f)/0.8f,(basis.b-StateWash.b*0.2f)/0.8f,1); Require(Mathf.Max(unwashed.r,unwashed.g,unwashed.b)-Mathf.Min(unwashed.r,unwashed.g,unwashed.b)<0.02f,"Two-tone baseline must contain the original HD StateDrawer wash over gray, without native blue selection."); }
            if (job.pane == "Project") CheckLabel(before, after, row, result);
        }
        float blueX = rows[Nodes[1].path].rect.x + 3, greenX = rows[Nodes[4].path].rect.x + 3;
        CheckSample(before, after, result, "band/inherited-blue", new Vector2(blueX, rows[Nodes[2].path].rect.y + 0.5f), Blue, 0.24f);
        CheckSample(before, after, result, "band/own-blue", new Vector2(blueX, rows[Nodes[3].path].rect.y + 0.5f), Blue, 0.24f);
        CheckSample(before, after, result, "band/nested-blue", new Vector2(blueX, rows[Nodes[5].path].rect.y + 0.5f), Blue, 0.24f);
        CheckSample(before, after, result, "band/nested-green", new Vector2(greenX, rows[Nodes[5].path].rect.y + 0.5f), Green, 0.24f);
        if (job.pane == "Project" && !job.alternate)
        {
            Result reference = Results.Last(item => item.pane == "Hierarchy" && item.focused == job.focused && !item.alternate);
            foreach (Sample sample in result.samples) Require(Diff(sample.actual, reference.samples.First(item => item.key == sample.key).actual) < 0.015f, "HD/Project selected background mismatch: " + sample.key);
            result.checks.Add("All 12 default body/icon/band samples match actual HierarchyDecorator reference within quantization tolerance.");
        }
    }
    private static void CheckSample(Texture2D before, Texture2D after, Result result, string key, Vector2 point, Color? tint, float alpha)
    {
        Color basis = Point(before, point), actual = Point(after, point);
        Color target=tint.HasValue?new Color(tint.Value.r,tint.Value.g,tint.Value.b,1):basis;
        if(result.alternate&&tint.HasValue)target=Color.Lerp(target,StateWash,0.2f);
        Color expected=tint.HasValue?Color.Lerp(basis,target,alpha):basis;
        result.samples.Add(new Sample {key = key, position = point, baseline = basis, actual = actual, expected = expected});
        Require(Diff(actual, expected) < 0.015f, result.name + " " + key + ": expected=" + expected + ", actual=" + actual + ", baseline=" + basis);
    }
    private static void CheckLabel(Texture2D before, Texture2D after, Row row, Result result)
    {
        Require(row.rect == baselineRows[row.path].rect,"Native row geometry changed: "+row.path);
        int x0 = Mathf.CeilToInt(row.rect.x + 20), x1 = Mathf.Min(320, Mathf.FloorToInt(row.rect.x + 190));
        int y0 = Mathf.CeilToInt(row.rect.y), y1 = Mathf.FloorToInt(row.rect.yMax); float max = 0; Color foreground=Color.white;
        for (int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) { Color c=Point(before,new Vector2(x,y)); float luminance=(c.r+c.g+c.b)/3f;if(luminance>max){max=luminance;foreground=c;} }
        int core=0,stable=0;
        for (int y=y0;y<y1;y++) for(int x=x0;x<x1;x++)
        {
            Vector2 p=new Vector2(x,y); Color a=Point(before,p), b=Point(after,p);
            bool brightA=(a.r+a.g+a.b)/3f>max-0.015f;
            if(brightA) {core++;if(Diff(a,b)<0.025f)stable++;}
        }
        result.checks.Add("Native row geometry unchanged " + row.path + ": before=" + baselineRows[row.path].rect + ", after=" + row.rect + ". Informational brightest-pixel RGB stability=" + stable + "/" + core + "; this threshold is not a reliable cross-version glyph-position metric. Native screenshots retained for manual ghosting review.");
    }
    private static void CheckRename(Texture2D before, Texture2D after, Result result)
    {
        Row row=ProjectRows[Nodes[3].path]; Require(row.renaming && baselineRows[row.path].renaming,"Real native rename overlay required.");
        int compared=0,different=0; Rect edit=row.renameRect;edit.xMin-=1;edit.xMax+=1;
        for(int y=(int)row.rect.y;y<(int)row.rect.yMax;y++)for(int x=0;x<404;x++){if(edit.Contains(new Vector2(x,y)))continue;compared++;if(Diff(Point(before,new Vector2(x,y)),Point(after,new Vector2(x,y)))>0.001f)different++;}
        result.checks.Add("Native rename outside editor/caret: " + compared + " pixels, differences=" + different);
        Require(compared>0&&different==0,"Native rename background changed.");
    }
    private static float Diff(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
    private static Color Point(Texture2D texture,Vector2 point){float scale=EditorGUIUtility.pixelsPerPoint;return texture.GetPixel(Mathf.Clamp(Mathf.FloorToInt(point.x*scale),0,texture.width-1),Mathf.Clamp(texture.height-Mathf.FloorToInt(point.y*scale)-1,0,texture.height-1));}
    private static Texture2D Capture(EditorWindow window)
    {
        float scale=EditorGUIUtility.pixelsPerPoint;int width=Mathf.RoundToInt(window.position.width*scale),height=Mathf.RoundToInt(window.position.height*scale);
        object view=typeof(EditorWindow).GetField("m_Parent",Any).GetValue(window);var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);RenderTexture previous=RenderTexture.active;
        try{typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView",true).GetMethod("GrabPixels",Any).Invoke(view,new object[]{target,new Rect(0,0,width,height)});RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);Color32[] pixels=texture.GetPixels32(),flipped=new Color32[pixels.Length];for(int row=0;row<height;row++)Array.Copy(pixels,row*width,flipped,(height-row-1)*width,width);texture.SetPixels32(flipped);texture.Apply();return texture;}finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
    }
    private static void EndRename(){if(project==null)return;object tree=Read(project,"m_FolderTree");if(tree!=null)Call(tree,"EndNameEditing",false);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Errors.Add(message+"\n"+stack);}
    private static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.hierarchyWindowItemOnGUI-=ObserveHierarchy;EditorApplication.projectWindowItemInstanceOnGUI-=ObserveProject;
        if(error!=null)Errors.Add(error.ToString());EndRename();foreach(EditorWindow window in new[]{project,hierarchy,inspector})if(window!=null)window.Close();if(native!=null)Object.DestroyImmediate(native);
        if(projectSettings!=null&&projectJson!=null){EditorJsonUtility.FromJsonOverwrite(projectJson,projectSettings);projectType.GetField("lookup",Any).SetValue(projectSettings,null);projectType.GetMethod("Persist",Any).Invoke(projectSettings,null);}
        if(hdSettings!=null&&hdJson!=null){EditorJsonUtility.FromJsonOverwrite(hdJson,hdSettings);hdOwnerType.GetMethod("SaveSettings",Any).Invoke(null,new object[]{hdSettings});}
        if(filesCaptured)
        {
            RestoreFile("UserSettings/Settings.asset",hdFile); RestoreFile("UserSettings/ProjectDecorator.asset",projectFile);
            if(hdSettings!=null)EditorUtility.ClearDirty(hdSettings);if(projectSettings!=null)EditorUtility.ClearDirty(projectSettings);
            Diagnostics.Add("Restored both original project-local UserSettings files byte-for-byte after restoring Settings JSON; no source package Settings asset was written.");
        }
        Application.logMessageReceived-=OnLog;if(output==null)output=Path.GetFullPath("Logs/ProjectSelection");Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"selection.json"),JsonUtility.ToJson(new Report{version=Application.unityVersion,generatedAt=DateTimeOffset.Now.ToString("o"),passed=Errors.Count==0,cases=Results.ToArray(),errors=Errors.ToArray(),diagnostics=Diagnostics.ToArray()},true));
        if(Errors.Count==0&&Environment.GetCommandLineArgs().Contains("-verifyRegionContract")){Find("ProjectRegionProbe","Assembly-CSharp-Editor").GetMethod("Run",Any).Invoke(null,null);return;}
        EditorApplication.Exit(Errors.Count==0?0:1);
    }
    private static void RestoreFile(string path,byte[] bytes){if(bytes==null){if(File.Exists(path))File.Delete(path);}else{File.WriteAllBytes(path,bytes);Require(File.ReadAllBytes(path).SequenceEqual(bytes),"Settings file restoration failed: "+path);}}
}
