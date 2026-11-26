// Copyright (c) 2026 VRCLearn.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCLearn.ProjectDecorator.Editor
{
    internal sealed class FolderDecorationPopup : EditorWindow
    {
        private const string Title = "Decorate Folder";
        private const string ResetArtworkGuid = "2fb2771de014412b8bba1e9cc2a65d86";
        private const string AddArtworkGuid = "2e7b19fba99c48c8841c1c891bc537c5";
        private const string StarArtworkGuid = "b26618aa00e94618bd555787a1ee7bd0";
        private const int PaletteColumns = 9;
        private const float PaletteWidth = 264f;
        private const float ContentWidth = 248f;
        private const float MinPaletteHeight = 259f;
        private const float MaxPaletteHeight = 340f;
        private const float Left = 14f;
        private const float TileSize = 24f;
        private const float TileStep = 26f;
        private const float ArtworkSize = 14f;
        private const float ActionArtworkSize = 10f;
        private const float ColorTilesY = 47f;
        private const float PopupGap = 4f;
        private static readonly Rect FullTexture = new Rect(0f, 0f, 1f, 1f);
        private static readonly Dictionary<Texture2D, ArtworkMetrics> ArtworkCache = new Dictionary<Texture2D, ArtworkMetrics>();
        private static FolderDecorationPopup activePalette;
        private static string pressedPaletteTarget;
        private static int pressedPaletteControl;

        private static readonly Color[] RowColors =
        {
            new Color32(137, 149, 166, 255), new Color32(222, 104, 111, 255),
            new Color32(230, 154, 82, 255), new Color32(221, 196, 102, 255),
            new Color32(130, 181, 107, 255), new Color32(79, 184, 167, 255),
            new Color32(103, 151, 233, 255), new Color32(173, 125, 232, 255)
        };

        private static readonly string[][] IconGroups =
        {
            new[] { "Folder Icon", "Canvas Icon" },
            new[] { "Camera Icon", "Light Icon", "ParticleSystem Icon", "Terrain Icon", "MeshRenderer Icon",
                "SpriteRenderer Icon", "SkinnedMeshRenderer Icon", "ReflectionProbe Icon" },
            new[] { "BoxCollider Icon", "SphereCollider Icon", "CapsuleCollider Icon", "Rigidbody Icon",
                "CharacterController Icon", "Cloth Icon", "WheelCollider Icon" },
            new[] { "AudioSource Icon", "AudioClip Icon", "Animator Icon", "AnimationClip Icon", "Material Icon",
                "Texture2D Icon", "ScriptableObject Icon", "LODGroup Icon" }
        };

        private struct Symbol
        {
            public Texture2D icon;
            public GUIContent tooltip;
            public Rect tile;
        }

        private struct ArtworkMetrics
        {
            public Rect bounds;
            public Vector2 opticalCenter;
        }

        internal struct RowPreview
        {
            public bool HasColor;
            public Color? Color;
            public bool HasIcon;
            public Texture2D Icon;
            public bool ColorRegion;
        }

        private struct PaletteLayout
        {
            public float iconHeaderY;
            public float hintY;
            public float Height => hintY + 18f;

            public PaletteLayout(int colorCount)
            {
                iconHeaderY = ColorTilesY + RowCount(1 + RowColors.Length + colorCount) * TileStep;
                hintY = iconHeaderY + 20f + IconGroups.Length * TileStep;
            }
        }

        [SerializeField] private string targetGuid;
        private readonly List<Symbol> symbols = new List<Symbol>();
        private Texture2D resetArtwork;
        private Texture2D addArtwork;
        private Texture2D starArtwork;
        private Vector2 hierarchyRightEdge;
        private Vector2 paletteScroll;
        private Vector2 pointerPosition;
        private bool pointerInPalette;
        private bool pointerUsesAlt;
        private RowPreview rowPreview;
        private bool dismissOnFocusLoss;
        private bool choosingPaletteItem;
        private Action removePaletteOption;
        private Rect removeMenuRect;
        private bool pressingRemoveOption;

        internal static void HandleRowInput(Rect row, string target)
        {
            Event evt = Event.current;
            // Allocate on every event, including Ignore, so the same folder in
            // the tree and content pane keeps distinct, stable control IDs.
            int control = GUIUtility.GetControlID(target.GetHashCode(), FocusType.Passive, row);
            bool repaint = evt.type == EventType.Repaint;
            if (!repaint && evt.type != EventType.MouseDown && evt.rawType != EventType.MouseUp)
                return;

            if (!IsEditable(target))
            {
                return;
            }

            Rect clickArea = row;
            bool selectionModifier = !evt.alt || evt.shift || evt.control || evt.command;
            if (repaint)
            {
                if (!selectionModifier)
                    EditorGUIUtility.AddCursorRect(clickArea, MouseCursor.Link);
                return;
            }

            bool insideClickArea = clickArea.Contains(evt.mousePosition) &&
                !ProjectDecorator.IsFavoriteStar(clickArea, evt.mousePosition);
            if (evt.type == EventType.MouseDown && evt.button == 0 && !selectionModifier && insideClickArea)
            {
                pressedPaletteTarget = target;
                pressedPaletteControl = control;
                evt.Use();
                return;
            }

            if (evt.rawType != EventType.MouseUp || evt.button != 0 ||
                pressedPaletteTarget != target || pressedPaletteControl != control)
            {
                return;
            }

            pressedPaletteTarget = null;
            pressedPaletteControl = 0;
            if (evt.type != EventType.MouseUp || selectionModifier || !insideClickArea)
            {
                return;
            }

            Vector2 anchor = GUIUtility.GUIToScreenPoint(evt.mousePosition);
            anchor.x += PopupGap;
            evt.Use();
            RunOnNextUpdate(() =>
            {
                if (IsEditable(target))
                {
                    ShowPalette(target, anchor);
                }
            });
        }

        private static void RunOnNextUpdate(Action action)
        {
            EditorApplication.update += Run;

            void Run()
            {
                EditorApplication.update -= Run;
                action();
            }
        }

        internal static void ShowPalette(string target, Vector2 anchor)
        {
            if (activePalette != null)
            {
                activePalette.Close();
            }

            FolderDecorationPopup window = CreatePalette(target);
            window.hierarchyRightEdge = anchor;
            window.FitPaletteToAnchor(GetPaletteSize());
            activePalette = window;
            // ShowAsDropDown can be dismissed by Unity's auxiliary window manager before the next frame.
            window.ShowPopup();
            window.Focus();
            window.dismissOnFocusLoss = true;
        }

        private void FitPaletteToAnchor(Vector2 size)
        {
            minSize = maxSize = size;
            position = FitPopupToDesktop(hierarchyRightEdge, size);
        }

        internal static Rect FitPopupToDesktop(Vector2 anchor, Vector2 size)
        {
            Rect desktop = InternalEditorUtility.GetBoundsOfDesktopAtPoint(anchor);
            float x = Mathf.Clamp(anchor.x, desktop.xMin, Mathf.Max(desktop.xMin, desktop.xMax - size.x));
            float y = Mathf.Clamp(anchor.y, desktop.yMin, Mathf.Max(desktop.yMin, desktop.yMax - size.y));
            return new Rect(new Vector2(x, y), size);
        }

        private void PositionPicker(EditorWindow picker)
        {
            Vector2 size = picker.position.size;
            var besidePalette = new Vector2(position.xMax + PopupGap, position.y);
            Rect beside = FitPopupToDesktop(besidePalette, size);
            picker.position = beside.x >= besidePalette.x
                ? beside
                : FitPopupToDesktop(new Vector2(hierarchyRightEdge.x, position.y), size);
        }

        private static Vector2 GetPaletteSize()
        {
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            var layout = new PaletteLayout(settings.PaletteColors.Count);
            return new Vector2(PaletteWidth, Mathf.Clamp(layout.Height, MinPaletteHeight, MaxPaletteHeight));
        }

        private static int RowCount(int count)
        {
            return (count + PaletteColumns - 1) / PaletteColumns;
        }

        internal static FolderDecorationPopup CreatePalette(string target)
        {
            var window = CreateInstance<FolderDecorationPopup>();
            window.targetGuid = target;
            window.resetArtwork = LoadArtwork(ResetArtworkGuid);
            window.addArtwork = LoadArtwork(AddArtworkGuid);
            window.starArtwork = LoadArtwork(StarArtworkGuid);
            window.titleContent = SettingsLocalization.Content(Title);
            return window;
        }

        private static bool IsEditable(string target)
        {
            return !string.IsNullOrEmpty(target) && AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(target));
        }

        private static Texture2D LoadArtwork(string guid) => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));

        private static string GetFolderName(string guid) => System.IO.Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid));

        internal static void DrawArtwork(Rect tile, Texture2D texture, float size, Color tint)
        {
            if (texture == null || Event.current.type != EventType.Repaint)
                return;
            ArtworkMetrics metrics = GetArtworkMetrics(texture);
            Rect rect = FitArtwork(tile, metrics, texture.width, texture.height, size);
            Color previous = GUI.color;
            GUI.color *= tint;
            GUI.DrawTextureWithTexCoords(rect, texture, metrics.bounds, true);
            GUI.color = previous;
        }

        private static Texture2D sharedStarArtwork;
        private static Texture2D sharedAddArtwork;
        internal static Texture2D StarArtwork
        {
            get
            {
                if (sharedStarArtwork == null)
                    sharedStarArtwork = LoadArtwork(StarArtworkGuid);
                return sharedStarArtwork;
            }
        }
        internal static Texture2D AddArtwork
        {
            get
            {
                if (sharedAddArtwork == null)
                    sharedAddArtwork = LoadArtwork(AddArtworkGuid);
                return sharedAddArtwork;
            }
        }

        private void OnEnable()
        {
            hideFlags = HideFlags.DontSave;
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            SetRowPreview(default);
        }

        private void OnDestroy()
        {
            if (activePalette == this)
            {
                activePalette = null;
            }
        }

        private void OnLostFocus()
        {
            pointerInPalette = false;
            SetRowPreview(default);
            if (!dismissOnFocusLoss || choosingPaletteItem)
            {
                return;
            }

            RunOnNextUpdate(() =>
            {
                if (this != null && focusedWindow != this && !choosingPaletteItem)
                {
                    Close();
                }
            });
        }

        private void RequestClose()
        {
            RunOnNextUpdate(() =>
            {
                if (this != null)
                {
                    Close();
                }
            });
        }

        internal static RowPreview GetRowPreview(string target)
        {
            FolderDecorationPopup palette = activePalette;
            return palette != null && target == palette.targetGuid && IsEditable(target) ? palette.rowPreview : default;
        }


        private void SetRowPreview(RowPreview preview)
        {
            if (rowPreview.HasColor == preview.HasColor && rowPreview.Color == preview.Color &&
                rowPreview.HasIcon == preview.HasIcon && rowPreview.Icon == preview.Icon &&
                rowPreview.ColorRegion == preview.ColorRegion)
            {
                return;
            }

            rowPreview = preview;
            EditorApplication.RepaintProjectWindow();
        }

        private void OnGUI()
        {
            Event evt = Event.current;
            HandleRemoveMenuInput(evt);
            if (!IsEditable(targetGuid) || (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape))
            {
                if (evt.type == EventType.KeyDown)
                {
                    evt.Use();
                }
                SetRowPreview(default);
                RequestClose();
                return;
            }

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseEnterWindow || evt.type == EventType.ScrollWheel)
            {
                pointerPosition = evt.mousePosition;
                pointerInPalette = true;
                pointerUsesAlt = evt.alt;
                Repaint();
            }
            else if (evt.type == EventType.MouseLeaveWindow)
            {
                pointerInPalette = false;
                SetRowPreview(default);
                Repaint();
            }
            else if (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp)
            {
                pointerUsesAlt = evt.alt;
                Repaint();
            }

            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            FolderDecoration decoration = settings.GetDecoration(targetGuid);
            Texture2D icon = ProjectDecorator.ResolveIcon(decoration?.icon) as Texture2D;
            Color? selectedColor = decoration != null && decoration.hasColor ? decoration.color : (Color?)null;
            var layout = new PaletteLayout(settings.PaletteColors.Count);
            RowPreview hovered = default;

            paletteScroll = GUI.BeginScrollView(new Rect(0f, 0f, position.width, position.height), paletteScroll,
                new Rect(0f, 0f, ContentWidth, layout.Height));
            try
            {
                EditorGUI.LabelField(new Rect(Left, 5f, 219f, 18f),
                    new GUIContent(GetFolderName(targetGuid), GetFolderName(targetGuid)), EditorStyles.boldLabel);

                bool savedRegion = decoration != null && decoration.hasColor && decoration.colorRegion;
                DrawColors(settings, selectedColor, savedRegion, evt.alt, ref hovered);
                DrawIcons(icon, layout, ref hovered);
                hovered.ColorRegion = hovered.Color.HasValue && pointerUsesAlt;
                EditorGUI.LabelField(new Rect(Left, layout.hintY, 232f, 15f),
                    SettingsLocalization.Content(hovered.ColorRegion ? "Coloring the folder tree region"
                        : "Hold Alt to color the tree region"), hovered.ColorRegion ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            }
            finally
            {
                GUI.EndScrollView();
            }

            if (evt.type == EventType.Repaint)
            {
                SetRowPreview(hovered);
            }

            if (removePaletteOption != null && evt.type == EventType.Repaint)
            {
                EditorStyles.miniButton.Draw(removeMenuRect, SettingsLocalization.Content("Remove from Palette"),
                    removeMenuRect.Contains(pointerPosition), pressingRemoveOption, false, false);
            }
        }

        private void DrawColors(ProjectDecoratorSettings settings, Color? selectedColor, bool savedRegion, bool colorRegion,
            ref RowPreview hovered)
        {
            EditorGUI.LabelField(new Rect(Left, 24f, 176f, 15f),
                SettingsLocalization.Content("Folder Color"), EditorStyles.miniBoldLabel);
            if (DrawActionTile(IconTile(224f, 22f), addArtwork, "Add Color…"))
            {
                Color initial = IsPaletteColor(settings, settings.PaletteColor) ? settings.PaletteColor : RowColors[0];
                OpenColorPicker(initial);
            }

            Rect clearTile = IconTile(Left, ColorTilesY);
            if (CanPreview(clearTile))
            {
                hovered.HasColor = true;
            }
            if (DrawActionTile(clearTile, resetArtwork, "Clear Color", !selectedColor.HasValue))
            {
                ApplyColorRegion(targetGuid, null, false);
                RequestClose();
            }

            Rect? savedRegionTile = null;
            int colorCount = RowColors.Length + settings.PaletteColors.Count;
            for (int i = 0; i < colorCount; i++)
            {
                bool custom = i >= RowColors.Length;
                Color color = custom ? settings.PaletteColors[i - RowColors.Length] : RowColors[i];
                Rect tile = PaletteTile(i + 1, ColorTilesY);
                bool previewing = CanPreview(tile);
                if (previewing)
                {
                    hovered.HasColor = true;
                    hovered.Color = color;
                }
                if (custom)
                {
                    HandleRemoveColorInput(tile, settings, color);
                }

                bool selected = selectedColor.HasValue && selectedColor.Value.Equals(color);
                if (selected && savedRegion)
                {
                    savedRegionTile = tile;
                }
                if (DrawSwatch(tile, color, selected))
                {
                    ApplyColorRegion(targetGuid, color, colorRegion);
                    RequestClose();
                }

                if (previewing && pointerUsesAlt)
                {
                    DrawRegionIndicator(tile);
                }
            }

            if (Event.current.type == EventType.Repaint && !hovered.HasColor && savedRegionTile.HasValue)
            {
                DrawRegionIndicator(savedRegionTile.Value);
            }
        }

        private static void DrawRegionIndicator(Rect tile)
        {
            float x = tile.x + 1f;
            float y = tile.y - 3f;
            Color tint = EditorStyles.miniLabel.normal.textColor;

            EditorGUI.DrawRect(new Rect(x, y, 9f, 1f), tint);
            EditorGUI.DrawRect(new Rect(x + 1f, y + 1f, 1f, 7f), tint);
            EditorGUI.DrawRect(new Rect(x + 2f, y + 4f, 5f, 1f), tint);
            EditorGUI.DrawRect(new Rect(x + 2f, y + 7f, 5f, 1f), tint);
        }

        private void OpenColorPicker(Color initial)
        {
            // Unity exposes ColorField publicly, but opening its picker from a button uses this overload.
            Type pickerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ColorPicker", true);
            Type[] parameters = { typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool) };
            MethodInfo show = pickerType.GetMethod("Show", parameters);
            Color chosen = initial;
            Action<Color> onColorChanged = color => chosen = color;
            choosingPaletteItem = true;
            if (show != null)
            {
                show.Invoke(null, new object[] { onColorChanged, initial, false, false });
            }
            else
            {
                // Newer Unity versions add an option for a transparent starting color.
                show = pickerType.GetMethod("Show", new[] { parameters[0], parameters[1], parameters[2],
                    parameters[3], typeof(bool) });
                show.Invoke(null, new object[] { onColorChanged, initial, false, false, false });
            }

            var picker = (EditorWindow)pickerType.GetProperty("instance").GetValue(null);
            PositionPicker(picker);
            EditorApplication.update += FinishColorSelection;
            GUIUtility.ExitGUI();

            void FinishColorSelection()
            {
                if (picker != null)
                {
                    return;
                }

                EditorApplication.update -= FinishColorSelection;
                // Escape restores the original color through the same callback before closing.
                Color32 selected = chosen;
                if (!((Color32)initial).Equals(selected))
                {
                    AddPaletteColor(selected);
                }

                ResumeAfterPicker();
            }
        }

        private void ResumeAfterPicker()
        {
            if (this != null)
            {
                choosingPaletteItem = false;
                Vector2 size = GetPaletteSize();
                if (position.size != size)
                {
                    FitPaletteToAnchor(size);
                }
                Focus();
                Repaint();
            }
        }

        private static void AddPaletteColor(Color32 selected)
        {
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            int preset = Array.FindIndex(RowColors, entry => ((Color32)entry).Equals(selected));
            int saved = settings.PaletteColors.FindIndex(entry => ((Color32)entry).Equals(selected));
            Color color = preset >= 0 ? RowColors[preset] : saved >= 0 ? settings.PaletteColors[saved] : (Color)selected;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(settings, SettingsLocalization.Text("Add Color…"));
            if (preset < 0 && saved < 0)
                settings.PaletteColors.Add(color);
            settings.PaletteColor = color;
            settings.Persist();
        }

        private void DrawIcons(Texture2D current, PaletteLayout layout, ref RowPreview hovered)
        {
            EditorGUI.LabelField(new Rect(Left, layout.iconHeaderY, 176f, 16f),
                SettingsLocalization.Content("Folder Icon"), EditorStyles.miniBoldLabel);
            if (DrawActionTile(IconTile(224f, layout.iconHeaderY - 4f), addArtwork, "Choose Project Icon…"))
            {
                string target = targetGuid;
                // Open the picker outside this dropdown's IMGUI pass so it has no GUI delegate.
                RunOnNextUpdate(() => OpenTexturePicker(target));
            }

            float iconTilesY = layout.iconHeaderY + 17f;
            Rect defaultTile = IconTile(Left, iconTilesY);
            if (CanPreview(defaultTile))
            {
                hovered.HasIcon = true;
            }
            if (DrawActionTile(defaultTile, resetArtwork, "Default Icon", current == null))
            {
                ApplyIcon(targetGuid, null);
                RequestClose();
            }

            if (symbols.Count == 0)
            {
                LoadSymbols();
            }

            foreach (Symbol symbol in symbols)
            {
                Rect tile = symbol.tile;
                tile.y += iconTilesY;
                if (CanPreview(tile))
                {
                    hovered.HasIcon = true;
                    hovered.Icon = symbol.icon;
                }
                if (DrawIconTile(tile, symbol.icon, symbol.icon == current, symbol.tooltip))
                {
                    ApplyIcon(targetGuid, symbol.icon);
                    RequestClose();
                }
            }
        }

        internal static bool IsPresetColor(Color color) => Array.IndexOf(RowColors, color) >= 0;

        internal static bool IsPaletteColor(ProjectDecoratorSettings settings, Color color)
        {
            return Array.IndexOf(RowColors, color) >= 0 || settings.PaletteColors.Contains(color);
        }

        private void LoadSymbols()
        {
            for (int group = 0; group < IconGroups.Length; group++)
            {
                int slot = group == 0 ? 1 : 0;
                foreach (string name in IconGroups[group])
                {
                    var texture = EditorGUIUtility.IconContent(name).image as Texture2D;
                    if (texture != null)
                    {
                        symbols.Add(new Symbol
                        {
                            icon = texture,
                            tooltip = new GUIContent(string.Empty, ObjectNames.NicifyVariableName(name.Replace(" Icon", ""))),
                            tile = IconTile(Left + slot++ * TileStep, group * TileStep)
                        });
                    }
                }

                if (group == 0)
                {
                    symbols.Add(new Symbol
                    {
                        icon = starArtwork,
                        tooltip = SettingsLocalization.Content(string.Empty, "Star"),
                        tile = IconTile(Left + slot * TileStep, 0f)
                    });
                }
            }
        }

        private bool CanPreview(Rect tile)
        {
            return Event.current.type == EventType.Repaint && !choosingPaletteItem &&
                removePaletteOption == null && IsTileHovered(tile);
        }

        private bool IsTileHovered(Rect tile)
        {
            return pointerInPalette && tile.Contains(pointerPosition + paletteScroll);
        }

        private void HandleRemoveColorInput(Rect tile, ProjectDecoratorSettings settings, Color color)
        {
            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 1 || !tile.Contains(evt.mousePosition))
            {
                return;
            }

            Vector2 point = evt.mousePosition - paletteScroll;
            Vector2 size = EditorStyles.miniButton.CalcSize(SettingsLocalization.Content("Remove from Palette"));
            size.x += 16f;
            size.y = TileSize;
            float x = Mathf.Clamp(point.x, 0f, Mathf.Max(0f, position.width - size.x));
            float y = Mathf.Clamp(point.y + 4f, 0f, Mathf.Max(0f, position.height - size.y));
            removeMenuRect = new Rect(new Vector2(x, y), size);
            removePaletteOption = () => ConfirmRemoveColor(settings, color);
            evt.Use();
            Repaint();
        }

        private void ConfirmRemoveColor(ProjectDecoratorSettings settings, Color color)
        {
            if (this == null)
            {
                return;
            }

            choosingPaletteItem = true;
            try
            {
                string message = SettingsLocalization.Text("Remove this color and clear all folder and tree region colors that use it in this project?");
                if (EditorUtility.DisplayDialog(SettingsLocalization.Text("Remove from Palette"),
                    message, SettingsLocalization.Text("Remove"), SettingsLocalization.Text("Cancel")))
                {
                    RemovePaletteColor(settings, color);
                }
            }
            finally
            {
                ResumeAfterPicker();
            }
        }

        private static void RemovePaletteColor(ProjectDecoratorSettings settings, Color color)
        {
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(settings, SettingsLocalization.Text("Remove from Palette"));
            settings.RemovePaletteColor(color);
        }

        private void HandleRemoveMenuInput(Event evt)
        {
            if (removePaletteOption == null)
            {
                return;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                DismissRemoveMenu();
                evt.Use();
                return;
            }

            bool overMenu = removeMenuRect.Contains(evt.mousePosition);
            if (evt.type == EventType.MouseDown)
            {
                if (evt.button == 0 && overMenu)
                {
                    pressingRemoveOption = true;
                    evt.Use();
                    Repaint();
                }
                else
                {
                    DismissRemoveMenu();
                }
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && pressingRemoveOption)
            {
                Action remove = removePaletteOption;
                DismissRemoveMenu();
                if (overMenu)
                {
                    RunOnNextUpdate(remove);
                }
                evt.Use();
            }
        }

        private void DismissRemoveMenu()
        {
            removePaletteOption = null;
            pressingRemoveOption = false;
            Repaint();
        }

        private static Rect PaletteTile(int slot, float startY)
        {
            return IconTile(Left + (slot % PaletteColumns) * TileStep, startY + (slot / PaletteColumns) * TileStep);
        }

        private bool DrawSwatch(Rect tile, Color color, bool selected)
        {
            bool clicked = DrawTile(tile, selected, GUIContent.none);
            if (Event.current.type == EventType.Repaint)
            {
                Rect swatch = ArtworkRect(tile);
                Color edge = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.16f);
                DrawRoundedRect(swatch, edge, 4f);
                DrawRoundedRect(InsetTile(swatch, 1f), color, 3f);
            }
            return clicked;
        }

        private bool DrawIconTile(Rect tile, Texture2D texture, bool selected, GUIContent tooltip,
            float artworkSize = ArtworkSize, Color? tint = null)
        {
            bool clicked = DrawTile(tile, selected, tooltip);
            if (texture != null && Event.current.type == EventType.Repaint)
            {
                ArtworkMetrics metrics = GetArtworkMetrics(texture);
                Rect artwork = FitArtwork(tile, metrics, texture.width, texture.height, artworkSize);
                Color previous = GUI.color;
                try
                {
                    GUI.color *= tint ?? Color.white;
                    GUI.DrawTextureWithTexCoords(artwork, texture, metrics.bounds, true);
                }
                finally
                {
                    GUI.color = previous;
                }
            }
            return clicked;
        }

        private static Rect IconTile(float x, float y)
        {
            return new Rect(x, y, TileSize, TileSize);
        }

        private static Rect ArtworkRect(Rect tile)
        {
            float inset = (TileSize - ArtworkSize) / 2f;
            return new Rect(tile.x + inset, tile.y + inset, ArtworkSize, ArtworkSize);
        }

        private static Rect FitArtwork(Rect tile, ArtworkMetrics metrics, int width, int height, float artworkSize)
        {
            Rect uv = metrics.bounds;
            var size = new Vector2(uv.width * width, uv.height * height);
            float scale = artworkSize / Mathf.Max(size.x, size.y);
            size *= scale;
            Vector2 offset = metrics.opticalCenter - uv.center;
            var correction = new Vector2(offset.x * width, -offset.y * height) * scale;
            return new Rect(tile.center - size / 2f - correction, size);
        }

        private static ArtworkMetrics GetArtworkMetrics(Texture2D texture)
        {
            if (ArtworkCache.TryGetValue(texture, out ArtworkMetrics metrics))
            {
                return metrics;
            }

            metrics = new ArtworkMetrics { bounds = FullTexture, opticalCenter = FullTexture.center };
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                int width = Mathf.Min(texture.width, 40);
                int height = Mathf.Min(texture.height, 40);
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                var sample = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    sample.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    metrics = MeasureArtwork(sample.GetPixels32(), width, height);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    DestroyImmediate(sample);
                }
            }

            ArtworkCache[texture] = metrics;
            return metrics;
        }

        private static ArtworkMetrics MeasureArtwork(Color32[] pixels, int width, int height)
        {
            int xMin = width;
            int yMin = height;
            int xMax = -1;
            int yMax = -1;
            float weight = 0f;
            Vector2 weightedPosition = Vector2.zero;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int alpha = pixels[y * width + x].a;
                    if (alpha <= 8)
                    {
                        continue;
                    }

                    xMin = Mathf.Min(xMin, x);
                    yMin = Mathf.Min(yMin, y);
                    xMax = Mathf.Max(xMax, x);
                    yMax = Mathf.Max(yMax, y);

                    // Favor the solid glyph over faint antialiasing and drop shadows.
                    float sampleWeight = alpha * alpha;
                    weight += sampleWeight;
                    weightedPosition += new Vector2(x + 0.5f, y + 0.5f) * sampleWeight;
                }
            }

            if (weight == 0f)
            {
                return new ArtworkMetrics { bounds = FullTexture, opticalCenter = FullTexture.center };
            }

            return new ArtworkMetrics
            {
                bounds = Rect.MinMaxRect((float)xMin / width, (float)yMin / height,
                    (float)(xMax + 1) / width, (float)(yMax + 1) / height),
                opticalCenter = new Vector2(weightedPosition.x / (weight * width), weightedPosition.y / (weight * height))
            };
        }

        private bool DrawActionTile(Rect tile, Texture2D icon, string tooltip, bool selected = false)
        {
            return DrawIconTile(tile, icon, selected, SettingsLocalization.Content(string.Empty, tooltip),
                ActionArtworkSize, EditorStyles.label.normal.textColor);
        }

        private bool DrawTile(Rect tile, bool selected, GUIContent tooltip)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = IsTileHovered(tile);
                Rect stateRect = InsetTile(tile, 1f);
                if (selected || hovered)
                {
                    // The same selection style Unity uses in its GameObject icon selector.
                    GUIStyle selection = GUI.skin.FindStyle("sv_iconselector_selection");
                    if (selection != null)
                    {
                        Color previous = GUI.color;
                        if (!selected)
                        {
                            GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * 0.65f);
                        }

                        try
                        {
                            selection.Draw(stateRect, GUIContent.none, false, false, false, false);
                        }
                        finally
                        {
                            GUI.color = previous;
                        }
                    }
                    else
                    {
                        EditorStyles.miniButton.Draw(stateRect, GUIContent.none, hovered, false, selected, false);
                    }
                }
            }
            Event evt = Event.current;
            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseUp) && evt.button != 0)
            {
                return false;
            }
            return GUI.Button(tile, tooltip, GUIStyle.none);
        }

        private static Rect InsetTile(Rect rect, float inset)
        {
            return new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
        }

        private static void DrawRoundedRect(Rect rect, Color color, float radius)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private void OpenTexturePicker(string target)
        {
            if (this == null || !IsEditable(target))
            {
                return;
            }

            // The callback overload does not require the palette to survive until the picker closes.
            Type selectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ObjectSelector", true);
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            Type[] parameters =
            {
                typeof(Object), typeof(Type), typeof(Object), typeof(bool),
#if UNITY_6000_4_OR_NEWER
                typeof(List<EntityId>),
#else
                typeof(List<int>),
#endif
                typeof(Action<Object>), typeof(Action<Object>), typeof(bool),
#if UNITY_6000_5_OR_NEWER
                typeof(bool),
#endif
            };
            MethodInfo show = selectorType.GetMethod("Show", instance, null, parameters, null);
            Action<Object> onClosed = selection => RunOnNextUpdate(() =>
            {
                if (selection is Texture2D texture && IsEditable(target) && IsProjectTexture(texture))
                {
                    ApplyIcon(target, texture);
                    RequestClose();
                }
                else
                {
                    ResumeAfterPicker();
                }
            });
            object[] arguments =
            {
                null, typeof(Texture2D), AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(target)), false, null, onClosed, null, false,
#if UNITY_6000_5_OR_NEWER
                false, // Only project textures can be added to the palette.
#endif
            };

            if (show == null)
            {
                // Older editors omit showNoneItem, and Unity 2018.4 takes a SerializedProperty as the context.
                Array.Resize(ref parameters, parameters.Length - 1);
                Array.Resize(ref arguments, arguments.Length - 1);
                show = selectorType.GetMethod("Show", instance, null, parameters, null);
                if (show == null)
                {
                    parameters[2] = typeof(SerializedProperty);
                    show = selectorType.GetMethod("Show", instance, null, parameters, null);
                    arguments[2] = null;
                }
            }

            var selector = (EditorWindow)selectorType.GetProperty("get").GetValue(null);
            choosingPaletteItem = true;
            try
            {
                show.Invoke(selector, arguments);
                PositionPicker(selector);
            }
            catch
            {
                choosingPaletteItem = false;
                throw;
            }
        }

        private static bool IsProjectTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return false;
            }

            return ProjectDecoratorSettings.TryGetAssetGuid(texture, out _);
        }

        private static void ApplyColor(string target, Color? color)
        {
            ApplyColorRegion(target, color, false);
        }

        private static void ApplyColorRegion(string target, Color? color, bool region)
        {
            if (!IsEditable(target))
                return;
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            FolderDecoration existing = settings.GetDecoration(target);
            bool hasColor = existing != null && existing.hasColor;
            bool existingRegion = existing != null && existing.colorRegion;
            bool colorRegion = color.HasValue && region;
            if (hasColor == color.HasValue && existingRegion == colorRegion &&
                (!hasColor || existing.color.Equals(color.Value)))
                return;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(settings, SettingsLocalization.Text("Folder Color"));
            settings.SetColorRegion(target, color, colorRegion);
        }

        private static void ApplyIcon(string target, Texture2D texture)
        {
            if (!IsEditable(target))
                return;
            string key = string.Empty;
            if (texture != null)
            {
                if (ProjectDecoratorSettings.TryGetAssetGuid(texture, out string guid))
                    key = guid;
                else
                {
                    foreach (string[] group in IconGroups)
                    {
                        foreach (string name in group)
                        {
                            if (EditorGUIUtility.IconContent(name).image == texture)
                                key = "builtin:" + name;
                        }
                    }
                }
            }
            ProjectDecoratorSettings settings = ProjectDecoratorSettings.instance;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(settings, SettingsLocalization.Text("Folder Icon"));
            settings.SetIcon(target, key);
        }


    }
}
