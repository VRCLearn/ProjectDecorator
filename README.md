# ProjectDecorator

Lightweight Unity Project window enhancements by VRCLearn. ProjectDecorator uses the same palette, artwork, settings layout and language choices as HierarchyDecorator, and installs independently alongside it and InspectorDecorator.

## Requirements and installation

Unity **2022.3 or newer**. In Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/VRCLearn/ProjectDecorator.git
```

Alternatively copy the `Editor` folder and its `.meta` file into `Assets/VRCLearn/ProjectDecorator`. Install only one copy per project. This is an Editor-only tool with no external package or game-specific SDK dependency.

## Folder decorations

- **Alt-click a folder's row or thumbnail** to open the palette beside the clicked folder at the pointer position, constrained to the screen bounds. Selection stays unchanged; ordinary folder clicks, double-clicks and dragging retain Unity's native behavior.
- **Folder Color** uses HierarchyDecorator's eight swatches. In the content list, thumbnails and Favorites, it tints the native folder icon. Only the Project tree paints row backgrounds. Hover over a swatch to preview; click to apply and close the palette.
- **Folder Icon** uses the same grouped icon tiles. In every view, including the Project tree, the chosen icon is a small badge at the bottom-right of the native folder icon. The folder silhouette stays visible.
- The first tile in each group resets its color or icon independently. Undo/Redo restores each change and its saved personal settings.
- The **+** beside Folder Color opens Unity's color picker. Closing it registers a custom swatch; then click the swatch to apply it. Right-click a custom swatch to remove it, with confirmation before clearing matching folder rows.
- The **+** beside Folder Icon opens Unity's texture picker. Choosing a project texture applies it and closes the palette. Cancelling returns to the palette. Escape and clicking outside dismiss it.
- **Hold Alt while choosing a color** to color the folder's tree region. The tree shows a softer background on descendants; the nearest colored region wins, and an explicit color on a child overrides its own row. Ancestor regions remain continuous through the indentation; nested regions retain the outer region bands. Region inheritance does not color other content tiles or Favorites. A normal color choice switches that folder back to its own-color mode.
- **Selection** follows HierarchyDecorator: selected tree rows retain their own color and ancestor region bands. Normal mode blends these colors over the active/inactive native selection background; alternating-background mode uses its alternating base and adds the same final selection/hover highlight as HierarchyDecorator. Rename fields keep Unity's native drawing.
- Optional alternating backgrounds apply to tree rows only. Folder icon color and corner badges also appear in Favorites.

Changes are personal. No folder names, importers or resource `.meta` files are modified. Folder regions are stored on their root folder and follow the current subtree without copying decorations into children.

## Favorites

Open **Tools > ProjectDecorator > Favorites** and dock the window wherever convenient.

- Drag assets or folders into the list, use the small **+** to add the selection, click a star in Project, or use **Assets > ProjectDecorator > Toggle Favorite**.
- The compact search field filters by name or path. The filled star marks each saved favorite; click it to remove the favorite. Right-click a row to **Move Up**, **Move Down** or **Remove Favorite**. Reordering is available when search is empty.
- Click a favorite to select and locate it. Double-click to open it. Drag a favorite into another Unity window.
- Opening scenes uses Unity's unsaved-scene prompt and is unavailable in Play Mode and Prefab Mode.
- Favorites follow moves and renames through GUIDs. Missing entries remain removable. Folders and main asset files are supported; embedded sub-assets, such as individual clips inside an FBX, are not favorited as their parent file.

Favorites take inspiration from the v-series' fast-access interaction. No v-series code, artwork or dependency is included.

## Settings and data

Open **Tools > ProjectDecorator > Settings** or **Preferences > ProjectDecorator**. The page follows HierarchyDecorator's General/Visual tabs, section styling, GitHub link and Restore controls. Restoring defaults keeps saved decorations, custom colors and favorites.

The language selector has **Auto**, **English**, **日本語**, **한국어**, **繁體中文** and **简体中文**. Auto follows the operating system, using English when its language is not supported. The preference is independent from the other decorators.

Settings and favorites stay in **`UserSettings/ProjectDecorator.asset`**, separately for each Unity project and user checkout. No scene, prefab or runtime component is added. Unity's usual `UserSettings` Git exclusion keeps this data personal. Uninstalling the package does not delete this file.

Existing favorites and decoration values are retained. Saved colors tint the native folder icon in content views and the background in the tree; saved icons appear as corner badges. Existing colors default to own-folder mode. Earlier saved colors remain available as custom palette swatches.

## Validation

`Tests~/Editor/ProjectDecoratorSmokeProbe.cs` is a disposable-project validation entrypoint. Copy it into the disposable project's `Assets/Editor` and run `ProjectDecoratorValidation.SmokeProbe.Run` with `-executeMethod`. It checks actual asset GUID favorites, duplicate prevention, reordering, independent color/icon reset, persistence, move tracking and subasset exclusion. It restores existing settings and removes only its own generated assets. It returns normally; a combined validation driver can call it without terminating Unity.

`Tests~/Editor/ProjectDecoratorInputProbe.cs` provides the native Alt-click regression. In a disposable project only, copy it into `Assets/Editor`, create an empty `ProjectSettings/StyleParity.enabled` marker, and launch with `-executeMethod ProjectInputProbe.Run`. It sends real window events across the tree, content list and thumbnails, records `Logs/ProjectInput/final.json`, and exits the test Editor. Fixture assets remain in that disposable project.

`Tests~/Editor/ProjectDecoratorRegionProbe.cs` verifies native region rendering in a marked disposable project with ProjectDecorator installed as a package. Use the same marker, then launch with `-executeMethod ProjectRegionProbe.Run`. It covers nested indentation bands, ordinary row overrides, selected-row color blending, native rename protection, Packages ancestry and narrow-tree clipping; output is written under `Logs/ProjectRegions` before the test Editor exits.

`Tests~/Editor/ProjectDecoratorSelectionProbe.cs` compares the actual HierarchyDecorator and ProjectDecorator selected-row rendering. Install both packages in the marked disposable project and run `-executeMethod ProjectSelectionProbe.Run`; comparisons cover focus and background modes. This optional verification does not add a runtime dependency on HierarchyDecorator.

## License and credits

MIT. The settings style definitions in `Editor/ProjectDecoratorStyle.cs` and the settings header and tab layout in `Editor/ProjectDecoratorPreferences.cs` are adapted from [WooshiiDev/HierarchyDecorator](https://github.com/WooshiiDev/HierarchyDecorator), Copyright (c) 2020 - 2024 Damian Slocombe (Wooshii), under MIT. The original copyright and permission notices are retained in [LICENSE.md](LICENSE.md). Unity-provided icons remain Unity editor resources.
