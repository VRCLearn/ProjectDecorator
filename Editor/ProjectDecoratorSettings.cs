using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCLearn.ProjectDecorator.Editor
{
    [Serializable]
    internal sealed class FolderDecoration
    {
        public string guid;
        public bool hasColor;
        public bool colorRegion;
        public Color color = new Color(0.52f, 0.68f, 0.94f);
        public string icon = string.Empty;
    }

    [FilePath("UserSettings/ProjectDecorator.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ProjectDecoratorSettings : ScriptableSingleton<ProjectDecoratorSettings>
    {
        [SerializeField] internal bool decorationsEnabled = true;
        [SerializeField] internal bool showFavoriteStars = true;
        [SerializeField] internal bool alternateRows;
        [SerializeField] private List<FolderDecoration> decorations = new List<FolderDecoration>();
        [SerializeField] private List<string> favorites = new List<string>();
        [SerializeField] private List<Color> paletteColors = new List<Color>();
        [SerializeField] private Color paletteColor = new Color32(137, 149, 166, 255);

        internal List<Color> PaletteColors => paletteColors;
        internal Color PaletteColor { get => paletteColor; set => paletteColor = value; }

        private void OnEnable()
        {
            // Retain the early version's personal colors, exposing them as custom swatches.
            foreach (FolderDecoration decoration in decorations)
            {
                if (decoration != null && decoration.hasColor && !paletteColors.Contains(decoration.color) &&
                    !FolderDecorationPopup.IsPresetColor(decoration.color))
                    paletteColors.Add(decoration.color);
            }
            persistedState = EditorJsonUtility.ToJson(this);
        }

        internal void RemovePaletteColor(Color color)
        {
            if (!paletteColors.Remove(color))
                return;
            for (int index = decorations.Count - 1; index >= 0; index--)
            {
                FolderDecoration decoration = decorations[index];
                if (decoration.hasColor && decoration.color.Equals(color))
                {
                    decoration.hasColor = false;
                    decoration.colorRegion = false;
                    if (string.IsNullOrEmpty(decoration.icon))
                        decorations.RemoveAt(index);
                }
            }
            lookup = null;
            Persist();
        }

        internal void RestoreDefaults()
        {
            decorationsEnabled = true;
            showFavoriteStars = true;
            alternateRows = false;
            Persist();
        }
        [NonSerialized] private Dictionary<string, FolderDecoration> lookup;
        [NonSerialized] private string persistedState;

        internal static event Action Changed;
        internal IReadOnlyList<string> Favorites => favorites;

        internal FolderDecoration GetDecoration(string guid)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, FolderDecoration>(StringComparer.Ordinal);
                foreach (FolderDecoration decoration in decorations)
                {
                    if (decoration != null && !string.IsNullOrEmpty(decoration.guid))
                        lookup[decoration.guid] = decoration;
                }
            }
            return lookup.TryGetValue(guid, out FolderDecoration value) ? value : null;
        }

        internal void SetColor(string guid, Color? color)
        {
            SetColorRegion(guid, color, false);
        }

        internal void SetColorRegion(string guid, Color? color, bool region)
        {
            FolderDecoration decoration = GetOrCreate(guid);
            decoration.hasColor = color.HasValue;
            decoration.colorRegion = color.HasValue && region;
            if (color.HasValue)
                decoration.color = color.Value;
            PruneDecoration(decoration);
            Persist();
        }

        internal void SetIcon(string guid, string icon)
        {
            FolderDecoration decoration = GetOrCreate(guid);
            decoration.icon = icon ?? string.Empty;
            PruneDecoration(decoration);
            Persist();
        }

        private FolderDecoration GetOrCreate(string guid)
        {
            FolderDecoration decoration = GetDecoration(guid);
            if (decoration != null)
                return decoration;
            decoration = new FolderDecoration { guid = guid };
            decorations.Add(decoration);
            lookup[guid] = decoration;
            return decoration;
        }

        private void PruneDecoration(FolderDecoration decoration)
        {
            if (decoration.hasColor || !string.IsNullOrEmpty(decoration.icon))
                return;
            decorations.Remove(decoration);
            lookup.Remove(decoration.guid);
        }

        internal bool IsFavorite(string guid) => favorites.Contains(guid);

        internal bool AddFavorite(string guid)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)) || favorites.Contains(guid))
                return false;
            favorites.Add(guid);
            Persist();
            return true;
        }

        internal void RemoveFavorite(string guid)
        {
            if (favorites.Remove(guid))
                Persist();
        }

        internal void ToggleFavorite(string guid)
        {
            if (IsFavorite(guid))
                RemoveFavorite(guid);
            else
                AddFavorite(guid);
        }

        internal void MoveFavorite(string guid, int offset)
        {
            int current = favorites.IndexOf(guid);
            int next = current + offset;
            if (current < 0 || next < 0 || next >= favorites.Count)
                return;
            favorites.RemoveAt(current);
            favorites.Insert(next, guid);
            Persist();
        }

        internal void RefreshAfterUndo()
        {
            if (persistedState == EditorJsonUtility.ToJson(this))
                return;
            lookup = null;
            Persist();
        }

        internal void Persist()
        {
            Directory.CreateDirectory("UserSettings");
            Save(true);
            persistedState = EditorJsonUtility.ToJson(this);
            Changed?.Invoke();
        }

        internal static bool TryGetAssetGuid(Object asset, out string guid)
        {
            guid = string.Empty;
            if (asset == null || !EditorUtility.IsPersistent(asset) || !AssetDatabase.IsMainAsset(asset))
                return false;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return false;
            guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid);
        }
    }
}
