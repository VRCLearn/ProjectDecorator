# Validation — 2026-11-26

Validated on Windows with Unity 2022.3.22f1 and 6000.0.67f1 in disposable projects, with HierarchyDecorator, ProjectDecorator and InspectorDecorator installed together.

- Both Editor-only assemblies compile against the actual APIs of both Unity versions.
- ProjectDecorator: main assets/folders, GUID move tracking, duplicate prevention, ordering, missing entries, independent color/icon reset, personal settings persistence, and rejection of subasset rows.
- InspectorDecorator: saved scene/object/component/prefab identity, unsaved changes rejection, duplicate prevention, ordering, GUID moves, closed/reopened scenes, missing entries, and a fixed window kept alive during scene unload/reload.
- Native graphical windows: Project list and thumbnail modes, folder palette, both favorites windows, GameObject header shortcuts and fixed component inspector.
- Editor restart preserves favorites. The fixed component inspector keeps its target when the main selection changes; its native serialized editor supports editing, Undo and Redo.
- Screenshots were inspected for both dark and light themes. Palette controls and the footer remain visible; the component Inspect button is not clipped.

The smoke probes are opt-in files under `Tests~/Editor`. Copy them into an isolated project's `Assets/Editor`; save an empty baseline scene before running the InspectorDecorator probe. Their public `Run()` methods throw on failure and return on success. Batch callers should use `-quit`, or call them from a driver that records the result and exits.

Unity 6000.0.67f1 has a separately reproduced native Chinese AudioSource-inspector font-atlas assertion (`kAllowDontSaveObjectsToBePersistent`). A project with none of these plugins reproduces the same assertion in `AudioSourceInspector`/TextCore. Final graphical checks used a Transform/BoxCollider fixture to isolate plugin behavior. No workaround or error suppression was added to the plugins.

Third-party custom inspectors were not individually tested. Fixed inspectors delegate rendering to Unity's native InspectorElement; asset importer settings are outside this version's scope.

## HierarchyDecorator style correction

- Compared the original HierarchyDecorator and both independent plugins in the same native Editor, theme and fixture. The palette uses the reference tile geometry and artwork; settings use the reference typography, tabs and compact section layout. Favorites use flat 18 px rows.
- Unity 2022.3 dark and light graphical checks cover the real GUI entrypoints, component popup, palette, both settings tabs, favorites, native Inspector header, and Project list/thumbnail views. The dark BoxCollider popup PNG is byte-identical to the original reference screenshot.
- Re-ran both native smoke probes on Unity 2022.3.22f1 and 6000.0.67f1 after the correction. Each version passes the Project probe's 39 checks, the Inspector probe and independent-assembly coexistence checks. Color/icon Apply and Undo/Redo restore both in-memory and saved settings.
- Headless execution explicitly reports `inputChecksRun=false`; those input assertions are verified separately inside an actual EditorWindow.OnGUI callback. Ordinary clicks/double-clicks preserve native input, and Alt-click arms the palette.
- Installed both embedded packages into an existing VRChat avatar test project. All 63 distribution files match the source, both editor assemblies are loaded, compilation is complete and Console has no errors. Existing scenes remain clean.

The Unity 6 graphical comparison captures all 16 windows but remains failed because it records two native `kDontSaveInEditor` GUI assertions: one in the unmodified HierarchyDecorator reference settings and one in InspectorDecorator settings. Both raw assertions are retained. The same assertion was also independently reproduced in native TextCore previously, but these two GUI stacks do not establish a font-related cause. This graphical run is not reported as a zero-error pass, and no suppression or workaround was added to product code.

## Project view rendering correction

- Content lists, thumbnails and Favorites tint only the native folder icon. Tree views paint row backgrounds. Every view keeps the native folder silhouette and draws the chosen icon as a bottom-right badge in its original color.
- Tree backgrounds and badges use Unity's native pre-label icon callbacks. The after-row callback never redraws labels. Native folder-open icons, captions, foldouts and rename drawing remain owned by Unity. Selection colors follow the layering contract below. Native callbacks register immediately during editor initialization and detach before assembly reload.
- Alt while choosing a color stores a tree-region flag. Descendants inherit the nearest region, own colors override their row, and ordinary colors do not become inherited regions. Hover previews use the same resolution. Color reset and custom-swatch removal clear region flags.
- Both Unity versions pass the expanded 48-check native smoke probe, including region persistence, same-color mode changes, independent reset and Undo/Redo. `inputChecksRun=false` remains explicit in non-GUI execution.
- Both versions pass four native ProjectBrowser before/after cases (8 screenshots each): root content list, region content list, region thumbnails, and Packages tree. All 208,896 tested non-tree caption/background pixels remain identical outside icon slots. Tree samples verify inherited, own, nested and non-inheriting colors; original-color badges are visible. Packages captions, arrows and indentation are native; that fixture used the actual 200 px sidebar and did not independently exercise truncation of its package name.
- A real EditorWindow GUI input probe verifies click-adjacent palette placement for a wide list row and a thumbnail tile, plus bottom/right desktop bounds. The palette is no longer anchored to the Project window's right edge.
- The final distribution is synchronized into the existing avatar test project and verified file by file. Compilation is complete, both native callbacks are installed, scenes are clean, and the final Console check has no errors. An unrelated AvatarOptimizer update-check TLS exception was observed during reimport; the agent issued no log-clearing command and did not change third-party code.

## Native Alt-click regression

- Reproduced the reported failure with actual ProjectBrowser.SendEvent input: content list/grid failed while the tree worked. The same folder appeared in both panes; an earlier tree `Ignore` event with raw MouseUp cleared the content pane's pending press.
- The press now belongs to its native GUI control, with IDs allocated consistently on all events. Only the initiating control can release or cancel it. Favorite buttons share a single hit-area definition with palette exclusion and retain their own click behavior.
- Unity 2022.3.22f1 and 6000.0.67f1 each pass 26 real-window input cases with zero errors: Alt-click with selected/unselected folders, native selection/double-click navigation, empty/file/star exclusion, and canceled gestures followed by recovery clicks. Favorite-star cases verify that the actual favorite value toggles. No direct input-handler invocation is counted as native integration coverage.
- The self-contained opt-in `Tests~/Editor/ProjectDecoratorInputProbe.cs` preserves the tested regression driver. Copy it into a disposable project's Assets/Editor, create an empty ProjectSettings/StyleParity.enabled marker, then launch with `-executeMethod ProjectInputProbe.Run`. It creates fixture assets only in that marked project, restores settings and event callbacks, writes Logs/ProjectInput/final.json, and exits the test Editor. Baseline and intermediate failures remain in the local QA artifacts.

## Tree region indentation regression

- Reproduced the missing ancestor bands in the avatar test project's Packages tree: descendant backgrounds began at their own icon rather than continuing through indentation.
- Region bands now use the native parent chain and GetContentIndent differences. A normal row color overrides only its own body; nested region bands retain outer colors. Hover previews and persisted colors share the same explicit-color lookup. That earlier run used native-only selection protection, superseded by the selection alignment below; native rename protection remains.
- Unity 2022.3.22f1 and 6000.0.67f1 each pass five native ProjectBrowser cases (10 screenshots, zero errors): region nesting/overrides, selection, active rename without accepting it, narrow-tree clipping, and Packages parent-chain regions. Per version, 146,016 content caption pixels, 5,504 selected-row pixels and 1,200 protected rename pixels are unchanged. Native edit-field/caret pixels are excluded from rename comparison because the caret blinks independently.
- The narrow fixture actually uses 100 px with native scroll.x=0. The stock FolderTree does not expose horizontal scrolling and clamps a requested scroll of 52 back to zero. This run verifies clipping, not horizontal-scroll behavior; the invalid initial test premise is retained in local artifacts.
- The exact passing driver is preserved as Tests~/Editor/ProjectDecoratorRegionProbe.cs. It requires a marked disposable project, writes native before/after screenshots and a report under Logs/ProjectRegions, restores settings and closes its own windows before exiting.
- Synchronized into the existing avatar test project. The actual Packages subtree was captured before and after; ancestor bands are continuous, ordinary child-row overrides retain the parent band, and the original scene remains clean. Capture uses a linear render target to avoid gamma conversion in the project's Linear color space.

## Hierarchy selection alignment

- Selected tree rows now keep their own row color and ancestor region bands, matching HierarchyDecorator. In normal mode the native focused/unfocused selection is the base beneath a single row tint. Cleared icon slots restore that same native base before tinting; folder foreground and corner badges keep their native/original color.
- Alternating mode matches the complete reference sequence: opaque alternating base across the visible row, ancestor/row tints, then StateDrawer's selected wash (0.214, 0.42, 0.76, 0.2) or nonselected hover wash (0.3, 0.3, 0.3, 0.2). That final state layer does not depend on window focus. Native folder foreground/badges draw afterward; native labels and foldouts are not redrawn by the plugin.
- Compared real HierarchyDecorator and ProjectDecorator windows on both Unity versions, covering own, inherited, nested and uncolored selected rows under focused/unfocused and normal/alternating modes. Default body/icon-background/band samples match the actual reference within quantization tolerance. Alternating mode is checked against each window's configured base and the reference's final state-wash composition.
- Unity 2022.3.22f1 passes nine comparison cases (18 screenshots), including native rename; Unity 6000.0.67f1 passes the eight color-comparison cases. Both then pass the updated RegionProbe selected/rename cases (two each). The old selected-row pixel-identity assertion is replaced by selected-row tint/band assertions; rename protection stays native.
- Native row geometry and screenshots confirm stable text placement. Bright-core RGB thresholds are not reliable across font versions and changing antialias backgrounds; raw metrics are informational, not claimed as pixel identity. Invalid initial QA assumptions and raw failures remain in local artifacts.
- The marked disposable reference setup explicitly calls HierarchyManager.Initialize when cold-start delayed initialization has not run. This is a rendering comparison, not startup verification. HD and Project settings JSON and persisted bytes are restored and verified before cleanup. All QA Editors exit normally.
- The exact optional reference driver is Tests~/Editor/ProjectDecoratorSelectionProbe.cs. It requires both packages in a disposable project with ProjectSettings/StyleParity.enabled; launch with -executeMethod ProjectSelectionProbe.Run. Add -verifyRegionContract -selectionOnly with the RegionProbe copied alongside it to run the focused selected/rename contract afterward. Reports and native comparisons are written under Logs/ProjectSelection and Logs/ProjectRegions.
