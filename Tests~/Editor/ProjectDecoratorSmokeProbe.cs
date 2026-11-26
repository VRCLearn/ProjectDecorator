using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectDecoratorValidation
{
    public static class SmokeProbe
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type settingsType;
        private static Object settings;
        private static int checks;
        private static bool inputChecksRun;

        public static void Run()
        {
            settingsType = Type.GetType("VRCLearn.ProjectDecorator.Editor.ProjectDecoratorSettings, VRCLearn.ProjectDecorator.Editor", true);
            settings = (Object)settingsType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            string originalJson = EditorJsonUtility.ToJson(settings);
            string folderName = "__ProjectDecoratorSmoke_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            GameObject sceneObject = null;
            checks = 0;
            inputChecksRun = false;
            try
            {
                settingsType.GetField("favorites", InstanceFlags).SetValue(settings, new List<string>());
                settingsType.GetField("decorations", InstanceFlags).SetValue(settings, Activator.CreateInstance(settingsType.GetField("decorations", InstanceFlags).FieldType));
                settingsType.GetField("lookup", InstanceFlags).SetValue(settings, null);
                string folderGuid = AssetDatabase.CreateFolder("Assets", folderName);
                var texture = new Texture2D(2, 2) { name = "Favorite Asset" };
                AssetDatabase.CreateAsset(texture, folderPath + "/Favorite.asset");
                string assetGuid = AssetDatabase.AssetPathToGUID(folderPath + "/Favorite.asset");
                Require((bool)Call("AddFavorite", folderGuid), "Folder can be favorited");
                Require((bool)Call("AddFavorite", assetGuid), "Main asset can be favorited");
                Require(!(bool)Call("AddFavorite", assetGuid), "Duplicate favorite rejected");
                Require(!(bool)Call("AddFavorite", string.Empty), "Empty favorite rejected");
                Require(!(bool)Call("AddFavorite", Guid.NewGuid().ToString("N")), "Unknown favorite rejected");
                Call("MoveFavorite", assetGuid, -1);
                Require(Favorites()[0] == assetGuid && Favorites()[1] == folderGuid, "Favorite ordering persists");
                Call("MoveFavorite", assetGuid, -1);
                Require(Favorites()[0] == assetGuid, "Ordering boundary is safe");
                Call("SetColor", folderGuid, (Color?)Color.cyan);
                Call("SetIcon", folderGuid, "builtin:Favorite");
                Call("SetColor", folderGuid, null);
                object decoration = Call("GetDecoration", folderGuid);
                Require(!(bool)decoration.GetType().GetField("hasColor").GetValue(decoration), "Color reset independent");
                Require((string)decoration.GetType().GetField("icon").GetValue(decoration) == "builtin:Favorite", "Icon retained after color reset");
                Call("SetColor", folderGuid, (Color?)Color.green);
                Call("SetIcon", folderGuid, string.Empty);
                decoration = Call("GetDecoration", folderGuid);
                Require((bool)decoration.GetType().GetField("hasColor").GetValue(decoration), "Color retained after icon reset");
                Call("SetColor", folderGuid, null);
                Require(Call("GetDecoration", folderGuid) == null, "Empty decoration pruned");
                CheckUserDecorationUndo(folderGuid, texture, assetGuid);
                CheckRegionUndo(folderGuid);
                CheckFolderInput(folderGuid);
                Require(File.Exists("UserSettings/ProjectDecorator.asset"), "Settings file created");
                Require(File.ReadAllText("UserSettings/ProjectDecorator.asset").Contains(assetGuid), "Favorite GUID serialized to personal settings");
                string movedPath = folderPath + "/Moved.asset";
                Require(string.IsNullOrEmpty(AssetDatabase.MoveAsset(folderPath + "/Favorite.asset", movedPath)), "Fixture rename succeeds");
                Require(AssetDatabase.GUIDToAssetPath(assetGuid) == movedPath && (bool)Call("IsFavorite", assetGuid), "Favorite follows asset rename");
                object[] arguments = { texture, null };
                Require((bool)settingsType.GetMethod("TryGetAssetGuid", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments) && (string)arguments[1] == assetGuid, "Drag accepts main asset GUID");
                Type decoratorType = Type.GetType("VRCLearn.ProjectDecorator.Editor.ProjectDecorator, VRCLearn.ProjectDecorator.Editor", true);
                MethodInfo resolveItem = decoratorType.GetMethod("TryGetItemGuid", BindingFlags.Static | BindingFlags.NonPublic);
                arguments = new object[] { texture.GetInstanceID(), null };
                Require((bool)resolveItem.Invoke(null, arguments) && (string)arguments[1] == assetGuid, "Project callback accepts actual main asset instance");
                Object folder = AssetDatabase.LoadMainAssetAtPath(folderPath);
                arguments = new object[] { folder.GetInstanceID(), null };
                Require((bool)resolveItem.Invoke(null, arguments) && (string)arguments[1] == folderGuid, "Project callback accepts actual folder instance");
                sceneObject = new GameObject("Unsupported scene object");
                arguments = new object[] { sceneObject, null };
                Require(!(bool)settingsType.GetMethod("TryGetAssetGuid", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments), "Drag rejects scene object");
                var embedded = new Texture2D(1, 1) { name = "Embedded" };
                AssetDatabase.AddObjectToAsset(embedded, texture);
                AssetDatabase.SaveAssets();
                arguments = new object[] { embedded, null };
                Require(!(bool)settingsType.GetMethod("TryGetAssetGuid", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments), "Drag rejects embedded subasset");
                arguments = new object[] { embedded.GetInstanceID(), null };
                Require(!(bool)resolveItem.Invoke(null, arguments), "Project callback rejects subasset instance sharing parent GUID");
                int favoriteCount = Favorites().Count;
                decoratorType.GetMethod("DrawItem", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                    new object[] { embedded.GetInstanceID(), new Rect(0f, 0f, 200f, 16f) });
                Require(Favorites().Count == favoriteCount, "Subasset row returns before GUI or favorite handling");
                AssetDatabase.DeleteAsset(movedPath);
                Require((bool)Call("IsFavorite", assetGuid), "Missing favorites remain removable");
                Call("RemoveFavorite", assetGuid);
                Require(!(bool)Call("IsFavorite", assetGuid), "Missing favorite removed");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/ProjectDecoratorSmokeProbe.json", "{\"passed\":true,\"checks\":" + checks + ",\"inputChecksRun\":" + (inputChecksRun ? "true" : "false") + "}");
                Debug.Log("ProjectDecorator smoke probe passed: " + checks + " checks.");
            }
            finally
            {
                if (sceneObject != null)
                    Object.DestroyImmediate(sceneObject);
                if (AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.DeleteAsset(folderPath);
                EditorJsonUtility.FromJsonOverwrite(originalJson, settings);
                settingsType.GetField("lookup", InstanceFlags).SetValue(settings, null);
                Call("Persist");
            }
        }

        private static void CheckUserDecorationUndo(string folderGuid, Texture2D texture, string textureGuid)
        {
            Type palette = Type.GetType("VRCLearn.ProjectDecorator.Editor.FolderDecorationPopup, VRCLearn.ProjectDecorator.Editor", true);
            Color color = new Color32(103, 151, 233, 255);
            Call("Persist");
            string beforeJson = EditorJsonUtility.ToJson(settings);
            string beforeFile = File.ReadAllText("UserSettings/ProjectDecorator.asset");
            try
            {
                PaletteCall(palette, "ApplyColor", folderGuid, (Color?)color);
                Require(HasColor(folderGuid, color) && string.IsNullOrEmpty(IconKey(folderGuid)), "User color apply leaves icon unchanged");
                string colorJson = EditorJsonUtility.ToJson(settings);
                string colorFile = File.ReadAllText("UserSettings/ProjectDecorator.asset");
                PaletteCall(palette, "ApplyIcon", folderGuid, texture);
                Require(HasColor(folderGuid, color) && IconKey(folderGuid) == textureGuid, "User icon apply retains row color");
                string bothJson = EditorJsonUtility.ToJson(settings);
                string bothFile = File.ReadAllText("UserSettings/ProjectDecorator.asset");

                Undo.PerformUndo();
                Require(EditorJsonUtility.ToJson(settings) == colorJson && HasColor(folderGuid, color), "Undo icon restores prior color-only state");
                Require(File.ReadAllText("UserSettings/ProjectDecorator.asset") == colorFile, "Undo icon persists restored personal settings");
                Undo.PerformRedo();
                Require(EditorJsonUtility.ToJson(settings) == bothJson && IconKey(folderGuid) == textureGuid, "Redo icon restores full decoration");
                Require(File.ReadAllText("UserSettings/ProjectDecorator.asset") == bothFile, "Redo icon persists restored personal settings");

                Undo.PerformUndo();
                Undo.PerformUndo();
                Require(EditorJsonUtility.ToJson(settings) == beforeJson && Call("GetDecoration", folderGuid) == null,
                    "Undo color independently restores original undecorated state");
                Require(File.ReadAllText("UserSettings/ProjectDecorator.asset") == beforeFile,
                    "Undo color persists original settings without stale decoration cache");
                Undo.PerformRedo();
                Require(EditorJsonUtility.ToJson(settings) == colorJson && HasColor(folderGuid, color), "Redo color independently restores color-only state");
                Undo.PerformRedo();
                Require(EditorJsonUtility.ToJson(settings) == bothJson, "Redo both preserves favorites and other settings");

                PaletteCall(palette, "ApplyColor", folderGuid, null);
                Require(!HasColor(folderGuid, color) && IconKey(folderGuid) == textureGuid, "User color reset retains selected icon");
                PaletteCall(palette, "ApplyIcon", folderGuid, null);
                Require(Call("GetDecoration", folderGuid) == null, "User icon reset clears final decoration");
                Undo.PerformUndo();
                Require(!HasColor(folderGuid, color) && IconKey(folderGuid) == textureGuid, "Undo icon reset restores icon independently");
                Undo.PerformUndo();
                Require(EditorJsonUtility.ToJson(settings) == bothJson, "Undo color reset restores both selected values");
                Require(File.ReadAllText("UserSettings/ProjectDecorator.asset") == bothFile, "Undo resets serialize the restored state");
            }
            finally
            {
                // This probe runs only in disposable projects. Clear its own singleton
                // history so later checks cannot restore fixtures after cleanup.
                Undo.ClearUndo(settings);
                EditorJsonUtility.FromJsonOverwrite(beforeJson, settings);
                settingsType.GetField("lookup", InstanceFlags).SetValue(settings, null);
                Call("Persist");
            }
        }

        private static void CheckRegionUndo(string folderGuid)
        {
            Type palette = Type.GetType("VRCLearn.ProjectDecorator.Editor.FolderDecorationPopup, VRCLearn.ProjectDecorator.Editor", true);
            string beforeJson = EditorJsonUtility.ToJson(settings);
            Color color = new Color32(103, 151, 233, 255);
            try
            {
                Call("Persist");
                PaletteCall(palette, "ApplyColorRegion", folderGuid, (Color?)color, true);
                Require(HasColor(folderGuid, color) && IsRegion(folderGuid), "Alt color stores a region on its folder");
                string regionFile = File.ReadAllText("UserSettings/ProjectDecorator.asset");
                Require(regionFile.Contains("colorRegion: 1"), "Region flag persists in personal settings");
                Undo.PerformUndo();
                Require(EditorJsonUtility.ToJson(settings) == beforeJson, "Undo region restores original settings");
                Undo.PerformRedo();
                Require(IsRegion(folderGuid) && File.ReadAllText("UserSettings/ProjectDecorator.asset") == regionFile,
                    "Redo region restores both data and disk state");

                PaletteCall(palette, "ApplyColor", folderGuid, (Color?)color);
                Require(HasColor(folderGuid, color) && !IsRegion(folderGuid), "Choosing same color without Alt changes region to own-folder mode");
                Undo.PerformUndo();
                Require(IsRegion(folderGuid), "Undo same-color mode change restores the region flag");
                Call("SetIcon", folderGuid, "builtin:Favorite");
                Require(IsRegion(folderGuid), "Changing an icon retains the folder region");
                PaletteCall(palette, "ApplyColor", folderGuid, null);
                Require(!IsRegion(folderGuid) && IconKey(folderGuid) == "builtin:Favorite", "Color reset clears the region independently of its icon");
                Undo.PerformUndo();
                Require(IsRegion(folderGuid) && HasColor(folderGuid, color), "Undo color reset restores region inheritance data");
            }
            finally
            {
                Undo.ClearUndo(settings);
                EditorJsonUtility.FromJsonOverwrite(beforeJson, settings);
                settingsType.GetField("lookup", InstanceFlags).SetValue(settings, null);
                Call("Persist");
            }
        }

        private static bool IsRegion(string guid)
        {
            object decoration = Call("GetDecoration", guid);
            return decoration != null && (bool)decoration.GetType().GetField("colorRegion").GetValue(decoration);
        }

        private static void CheckFolderInput(string folderGuid)
        {
            // -executeMethod has no native IMGUI context. Assigning Event.current
            // alone cannot create one. The graphical driver calls this same method
            // from a real EditorWindow.OnGUI; headless reports the check as unrun.
            if (Event.current == null)
                return;
            inputChecksRun = true;
            Type palette = Type.GetType("VRCLearn.ProjectDecorator.Editor.FolderDecorationPopup, VRCLearn.ProjectDecorator.Editor", true);
            MethodInfo handle = palette.GetMethod("HandleRowInput", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo pressed = palette.GetField("pressedPaletteTarget", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo pressedControl = palette.GetField("pressedPaletteControl", BindingFlags.Static | BindingFlags.NonPublic);
            object previousPressed = pressed.GetValue(null);
            object previousControl = pressedControl.GetValue(null);
            Event previousEvent = Event.current;
            try
            {
                pressed.SetValue(null, null);
                foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
                {
                    foreach (int clickCount in new[] { 1, 2 })
                    {
                        var evt = new Event { type = type, button = 0, clickCount = clickCount, mousePosition = new Vector2(8f, 8f) };
                        Event.current = evt;
                        handle.Invoke(null, new object[] { new Rect(0f, 0f, 200f, 18f), folderGuid });
                        Require(evt.type == type, "Ordinary folder " + type + " with click count " + clickCount + " stays native");
                    }
                }
                var altDown = new Event { type = EventType.MouseDown, button = 0, alt = true, mousePosition = new Vector2(8f, 8f) };
                Event.current = altDown;
                handle.Invoke(null, new object[] { new Rect(0f, 0f, 200f, 18f), folderGuid });
                Require(altDown.type == EventType.Used && (string)pressed.GetValue(null) == folderGuid,
                    "Alt mouse down arms decoration and consumes only that gesture");
                // Deliberately do not send Alt mouse up: no popup is scheduled in headless validation.
            }
            finally
            {
                pressed.SetValue(null, previousPressed);
                pressedControl.SetValue(null, previousControl);
                Event.current = previousEvent;
            }
        }

        private static bool HasColor(string folderGuid, Color color)
        {
            object decoration = Call("GetDecoration", folderGuid);
            return decoration != null && (bool)decoration.GetType().GetField("hasColor").GetValue(decoration) &&
                ((Color)decoration.GetType().GetField("color").GetValue(decoration)).Equals(color);
        }

        private static string IconKey(string folderGuid)
        {
            object decoration = Call("GetDecoration", folderGuid);
            return decoration == null ? string.Empty : (string)decoration.GetType().GetField("icon").GetValue(decoration);
        }

        private static object PaletteCall(Type palette, string method, params object[] args) =>
            palette.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        private static object Call(string method, params object[] args) => settingsType.GetMethod(method, InstanceFlags).Invoke(settings, args);
        private static IReadOnlyList<string> Favorites() => (IReadOnlyList<string>)settingsType.GetProperty("Favorites", InstanceFlags).GetValue(settings);
        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("ProjectDecorator: " + message);
            checks++;
        }
    }
}
