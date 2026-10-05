# Dogeometric — SketchUp feature inventory

Reference: **SketchUp Pro 2021** (21.1.332).

## Sources

| Source | What it gave us |
|---|---|
| `SketchUp.exe` resources (`research/extract_resources.py` → `research/sketchup_resources.json`) | Full menu tree with command IDs, command descriptions, accelerator tables, dialog titles |
| `resources/en-US/helpcontent/tool/<id>/index.html` (Instructor panel) | Step-by-step operation and **modifier keys** of every tool |
| `ShippedExtensions/*/Resources/en-US/*.strings` | Sandbox, Advanced Camera Tools, Dynamic Components |
| `AppData/Roaming/SketchUp/SketchUp 2021/SketchUp/Plugins` | Third-party extensions in the reference user's setup |
| Live session of the reference install (screenshots) | Context menu, UI layout |

Items marked *(verify)* come from general SketchUp knowledge and still need checking against the running app.

## Priorities

- **P0**: MVP. Without it the app isn't a SketchUp-like modeller.
- **P1**: Everyday modelling. Needed for real use.
- **P2**: Presentation and organisation features.
- **P3**: Niche, cloud or proprietary features. Last, or never.

---

## 1. Core model and engine (no menu, but everything depends on it)

| Feature | Notes | Prio |
|---|---|---|
| Edge/face topology | Vertices, edges and faces share vertices. A closed coplanar loop of ≥3 edges creates a face automatically. A new edge across a face splits it. Deleting an edge removes the faces bounded by it. | P0 |
| Sticky geometry | Loose geometry that touches merges and splits (edge/edge and edge/face intersections at draw time). | P0 |
| Faces with front/back | Back face uses a different default material. Reverse Faces and Orient Faces commands. Holes (inner loops). | P0 |
| Inference engine | Snaps to endpoint, midpoint, on edge, on face, intersection, center, axis directions (red/green/blue), parallel/perpendicular, tangent, "from point" dotted inference. Colour coding plus tooltips. Shift locks the current inference. Arrow keys lock: → red, ← green, ↑ blue, ↓ parallel/perpendicular. | P0 |
| Measurements box (VCB) | Exact values typed during or after any operation: length, `w,h`, angles, `4:12` slopes, `5x` / `/5` arrays, `r`/`d` suffixes, units (`'`, `"`, `mm`, `cm`, `m`). Retyping right after an operation edits its result. | P0 |
| Units | Model units with precision, angle snapping (Model Info › Units). | P0 |
| Undo/redo | Every operation is one undoable step. Camera has its own Previous/Next history. | P0 |
| Drawing axes | Red/green/blue axes, movable and reorientable (Axes tool). Inference uses the current axes. | P0 |
| Groups | Isolated geometry container. Double-click to edit inside, with the rest of the model faded. Close via Esc/click outside. | P0 |
| Components | Shared definitions with instances; editing one edits all. Make Unique, glue-to/cut-opening behaviour, axes per definition, Components panel. | P1 |
| Nested contexts | Groups and components inside others. Outliner shows the tree. | P1 |
| Tags (layers) | Visibility tags on objects; Color by tag; Purge. Tag folders *(verify, 2021)*. | P1 |
| Hide / Lock | Hide and unhide (selected, last, all). Locked groups and components can't be edited. | P1 |
| Soft/smooth edges | Edge flags for curved-surface rendering; Soften Edges panel with angle slider. | P1 |
| Curves | Arcs, circles and polygons are kept as curve entities (one selectable unit, segment count editable). | P1 |
| Materials | Colours and textures, opacity, texture size and positioning, default vs painted. | P1 |
| Solids | Detects watertight groups and components ("Solid group", volume in Entity Info). | P2 |
| Attributes / dictionaries | Arbitrary key/value data on entities (needed for Dynamic Components and plugins). | P3 |

## 2. Camera and navigation

Mouse defaults *(verify)*: middle-drag orbits, Shift+middle-drag pans, the wheel zooms to the cursor, double-click with the middle button re-centres.

| Command | Shortcut | Modifiers / notes | Prio |
|---|---|---|---|
| Orbit (10508) | `O` | Hold Shift = pan; hold Ctrl = suspend gravity (free orbit). Esc returns to the previous tool. | P0 |
| Pan (10523) | `H` | — | P0 |
| Zoom (10509) | `Z` | Shift = change field of view | P0 |
| Zoom Extents (10527) | `Shift+Z`, `Ctrl+Shift+E` | | P0 |
| Zoom Window (10526) | `Ctrl+Shift+W` | Drag a rectangle | P1 |
| Zoom Selection | context menu | | P1 |
| Standard views (10501–10507) | Top / Bottom / Front / Back / Left / Right / Iso | Iso goes to the nearest isometric view | P0 |
| Parallel projection / Perspective (10630, 10519) | | | P0 |
| Two-Point Perspective (10627) | | | P2 |
| Field of View (21494) | | Drag up/down; Shift = zoom | P1 |
| Previous / Next camera (10529, 10629) | | Camera undo history | P1 |
| Position Camera (21169) | | Click puts the camera at eye height; drag sets camera and target | P2 |
| Walk (10520) | | Drag. Shift = up/down, Ctrl = run, Alt = walk through walls | P2 |
| Look Around (10525) | | Pivot from a fixed eye point | P2 |
| Align View (context menu) | | Camera perpendicular to a face | P1 |
| Match Photo / Image Igloo / Zoom to Photo (23006, 10631, 10625) | | Fit the camera to a photo, project textures | P3 |

## 3. Drawing tools

| Tool | Shortcut | Operation | Modifiers | Prio |
|---|---|---|---|---|
| Line (21020) | `L` | Click, click, … for a chained polyline. Faces appear when a loop closes. | Shift = lock inference; arrows = axis lock; Alt = cycle linear inference (all on / off / parallel-perpendicular only) | P0 |
| Freehand (21031) | — | Click and drag a curve | Shift = 3D polyline | P2 |
| Rectangle (21094) | `R` | Corner to corner on the inferred plane. VCB takes `w,h`. | Ctrl = from centre; Shift = lock plane; arrows = lock plane (red/green/blue/parallel) | P0 |
| Rotated Rectangle (24223) | — | Three corners, with a protractor | Shift, Alt (lock plane / protractor baseline), arrows | P2 |
| Circle (21096) | `C` | Centre and radius; VCB takes radius and segment count (`24s`) | Shift = lock; arrows lock the normal before the first click and the direction after it; Ctrl +/− changes segments | P0 |
| Polygon (21095) | — | Centre and radius, N sides | Ctrl = inscribed/circumscribed; Shift; arrows | P1 |
| Arc (21069) | — | Centre, start, angle | Shift; arrows; Ctrl +/− segments | P1 |
| 2-Point Arc (21065) | `A` | Start, end, bulge. Tangent inference (cyan) to continue curves. | Shift; arrows; Ctrl +/− segments | P0 |
| 3-Point Arc (21071) | — | Start, through-point, end | Shift; arrows | P2 |
| Pie (21070) | — | Like Arc, but closed to the centre | Shift; arrows | P2 |
| Text (21405) | — | Leader text attached to an entity (shows area or length by default) | — | P2 |
| 3D Text (21940) | — | Dialog: font, height, extrusion; creates a component | — | P2 |
| Dimensions (21410) | — | Linear, radius and diameter dimensions; pull out the offset | — | P1 |
| Section Plane (21337) | — | Click a face to place it; active cut; Section Planes / Cuts / Fill toggles | Shift = lock orientation; arrows | P2 |

## 4. Modification tools

| Tool | Shortcut | Operation | Modifiers | Prio |
|---|---|---|---|---|
| Select (21022) | `Space` | Click; left-to-right window selects what's fully inside, right-to-left crossing selects what it touches; double-click a face = face plus its edges; triple-click = all connected; double-click a group = edit it | Ctrl = add; Shift = toggle; Ctrl+Shift = subtract | P0 |
| Eraser (21019) | `E` | Click or drag over entities; deletes on release | Shift = hide; Ctrl = soften/smooth; Alt = unsmooth/unhide; Ctrl+Shift = deselect | P0 |
| Move (21048) | `M` | Pick up, then drop. Moving a vertex, edge or face stretches the connected geometry (autofold). VCB takes distance and `Nx` / `/N` arrays after a copy. Rotation grips on group and component bounding boxes. | Ctrl = cycle move/copy/stamp; Alt = autofold; Shift = lock; arrows | P0 |
| Push/Pull (21041) | `P` | Extrude a face; double-click repeats the last distance; pushing to an opposite face cuts a hole | Ctrl = create a new starting face; Alt = stretch mode | P0 |
| Rotate (21129) | `Q` | Protractor: centre, then start and end angle. VCB takes degrees or `rise:run`. `Nx` arrays. | Ctrl = copy; Shift = lock protractor; arrows; drag to set the axis | P0 |
| Scale (21236) | `S` | Bounding-box grips: corner (3-way), edge (2-way), face (1-way). VCB takes a factor or an absolute size. Negative factors mirror. | Ctrl = about centre; Shift = toggle uniform | P1 |
| Offset (21100) | `F` | Offset a face's or a set of edges' outline in its plane | Alt = allow/trim overlap | P1 |
| Follow Me (21525) | — | Sweep a profile face along a path (edges, or the perimeter of a face) | Alt = use the face perimeter as the path | P1 |
| Paint Bucket (21074) | `B` | Paint faces, groups or components with the current material | Alt = sample material; Shift = all matching faces; Ctrl = connected matching faces; Ctrl+Shift = matching faces in the object | P1 |
| Intersect Faces (21524/21527/21526) | — | With Model / With Selection / With Context: creates edges where faces intersect | — | P1 |
| Position Texture (21515) | context menu | Pins: move, scale/rotate, shear, distort; fixed and free pin modes | Shift = pin mode; Ctrl = no inference | P2 |
| Make Unique Texture | context menu | | | P3 |

## 5. Construction and measurement tools

| Tool | Shortcut | Operation | Modifiers | Prio |
|---|---|---|---|---|
| Tape Measure (21024) | `T` | Measures distances. Creates guide lines (infinite when started from an edge) and guide points. Resize-model mode: measure, then type a new length to scale the whole model. | Ctrl = toggle guide creation; Shift = lock; arrows | P0 |
| Protractor (21057) | — | Measures angles and creates angled guides | Ctrl = toggle guides; Shift and arrows | P1 |
| Axes (21126) | — | Origin, then red axis, then green axis | Alt = alternate orientation | P1 |
| Delete Guides (21044) | — | | | P1 |
| Guides visibility (21980) | — | | | P1 |

## 6. Solid tools (Pro) — Tools menu

| Tool | Behaviour | Prio |
|---|---|---|
| Outer Shell (24198) | Merges solids and removes all interior geometry | P2 |
| Union (24201) | Merges solids but keeps interior voids | P2 |
| Intersect (24200) | Keeps only the overlap | P2 |
| Subtract (24202) | First solid is removed from the second; only the result remains | P2 |
| Trim (24203) | Trims the first against the second and keeps both | P2 |
| Split (24204) | Keeps every piece | P2 |

All of them need robust mesh booleans, which are the hardest part of the engine. Candidate library: Manifold.

## 7. Sandbox (terrain) — shipped extension

| Tool | Behaviour | Prio |
|---|---|---|
| From Contours | Builds a TIN from selected contour curves | P2 |
| From Scratch | Grid with a given spacing; three clicks | P2 |
| Smoove | Raises or lowers the mesh with radius falloff (`10'r`, `10'd` in the VCB). Shift = perpendicular. | P2 |
| Stamp | Imprints a footprint face onto the mesh, with an offset | P3 |
| Drape | Projects edges down onto the mesh | P3 |
| Add Detail | Subdivides the mesh; Ctrl = without offset | P3 |
| Flip Edge | Flips the diagonal of a triangle pair | P3 |

## 8. Edit menu and selection commands

| Command | Shortcut | Prio |
|---|---|---|
| Undo / Redo | `Ctrl+Z`, `Alt+Backspace` / `Ctrl+Y` | P0 |
| Cut / Copy / Paste | `Ctrl+X`, `Shift+Del` / `Ctrl+C`, `Ctrl+Ins` / `Ctrl+V`, `Shift+Ins` | P0 |
| Paste In Place | — | P1 |
| Delete | `Del` | P0 |
| Select All / None / Invert | `Ctrl+A` / `Ctrl+T` / `Ctrl+Shift+I` | P0 |
| Hide, Unhide (Selected / Last / All) | — | P1 |
| Lock, Unlock (Selected / All) | — | P2 |
| Make Component… | `G` | P1 |
| Make Group | — (context menu) | P0 |
| Close Group/Component | Esc / click outside | P0 |
| Explode | context menu | P0 |
| Context menu on a face | Entity Info, Erase, Hide, Select › and Area › submenus *(verify contents)*, Intersect Faces ›, Align View, Align Axes, Reverse Faces, Orient Faces, Zoom Selection | P1 |

## 9. View menu and styles

| Feature | Notes | Prio |
|---|---|---|
| Face styles | X-ray, Wireframe, Hidden Line, Shaded, Shaded With Textures, Monochrome | P0 (Shaded, Wireframe, X-ray), P1 (the rest) |
| Edge styles | Edges, Back Edges (`K`), Profiles, Depth Cue, Extension | P1 |
| Hidden Geometry / Hidden Objects | Show hidden items ghosted | P1 |
| Axes / Guides toggles | | P0 |
| Section Planes / Cuts / Fill toggles | | P2 |
| Shadows | Time and date sun, geo-location, on faces / ground; Shadows panel | P2 |
| Fog | Distance-based fog | P3 |
| Component Edit › Hide Rest Of Model / Hide Similar Components | | P1 |
| Styles panel | Background and sky, ground, edge and face settings, watermarks, saved style presets | P2 |
| Scene tabs and animation | Add, Update and Delete Scene; Previous/Next (`PgUp` / `PgDn`); Play; transition timing; scenes store camera, styles, visibility | P2 |

## 10. Panels (Default Tray)

| Panel | Contents | Prio |
|---|---|---|
| Entity Info | Type-specific properties: tag, material, length, area, volume, name, instance and definition, soft/smooth flags, segments… | P0 |
| Outliner | Group and component tree, rename, visibility | P1 |
| Materials | Library browser, create and edit material (colour, texture, opacity), sample | P1 |
| Components | Model components and libraries, insert by drag | P1 |
| Tags | Add, delete, visibility, colour, dashes *(verify)* | P1 |
| Styles | see §9 | P2 |
| Scenes | see §9 | P2 |
| Shadows | see §9 | P2 |
| Soften Edges | angle slider, smooth normals, soften coplanar | P1 |
| Instructor | Per-tool help (the same texts as above) | P3 |
| Fog / Match Photo | | P3 |
| Tray management (New / Manage / Hide Tray) | Dockable panel groups | P2 |

## 11. File and I/O

| Feature | Notes | Prio |
|---|---|---|
| New / New From Template / Open / Save / Save As / Save A Copy / Revert / Recent | Own native format | P0 |
| Templates | Templates on the welcome screen (Simple, Architectural, 3D printing; each in mm, cm, m or inches) | P1 |
| Welcome screen | Templates and recent files with thumbnails | P2 |
| Import | `.skp` (see open questions), DAE, 3DS, DWG/DXF, IFC, KMZ, STL, OBJ*(verify)*, DEM, images (as image / texture / match photo) | P1: OBJ/STL/images; P2: DAE/DXF; P3: the rest |
| Export 3D | DAE, 3DS, DWG/DXF, FBX, IFC, KMZ, OBJ, STL, VRML, XSI | P1: OBJ/STL/glTF; P2: DAE/FBX/DXF |
| Export 2D | PNG/JPG (resolution, transparent background), PDF/EPS, DWG/DXF hidden line | P1: PNG; P2: the rest |
| Export section slice, animation (video / image sequence) | | P3 |
| Print / Print Preview | | P3 |
| Auto-save and crash recovery | ("Recovered" files on the welcome screen) | P1 |

## 12. Model Info and Preferences

- **Model Info pages** *(verify)*: Animation, Classifications, Components (fade rest of model and similar components while editing), Credits, Dimensions, File, Geo-location, Rendering, Statistics (entity counts, Purge Unused, Fix Problems), Text, Units. Priority: P1 for Units and Statistics/Purge, P2 for the rest.
- **Preferences pages** *(verify)*: Accessibility, Applications, Compatibility (mouse wheel direction, highlight), Drawing (click style, "continue line drawing"), Files, General (auto-save, check problems), Graphics, Shortcuts (fully rebindable: dialog "Customize Keyboard"), Template, Workspace. Rebindable shortcuts are P1; the rest P2.

## 13. Third-party extensions in the reference user's setup

The reference user relies on these, so they matter if Dogeometric is to replace a real SketchUp workflow.

| Extension | What it does | Prio |
|---|---|---|
| tt_selection_toys 2.5.1 | Select / Select Only / Deselect by entity type, select connected, etc. | P1 (cheap to build natively) |
| MakeFaces 1.0.0 | Creates the missing faces from edges | P1 (cheap) |
| CircleByDiameter | Circle from two diameter points | P2 |
| tt_select_curve | Selects connected edges | P2 |
| tt_cleanup 3.4.3 | Merge coplanar faces, remove stray edges and duplicates | P2 |
| tt_solid_inspector2 | Finds why a group isn't solid (holes, internal faces, reversed faces) | P2 |
| Fredo6 JointPushPull | Push/pull many faces along their normals with joint, vector and normal modes | P2 |
| Fredo6 RoundCorner | Fillet and chamfer edges and corners | P2 |
| Fredo6 FredoScale | Box scale, tapering, twisting, bending, shearing… | P3 |
| Fredo6 Curviloft | Loft and skin between curves | P3 |
| Fredo6 ToolsOnSurface | Draw lines, shapes and offsets on curved surfaces | P3 |
| bezierspline 2.2a | Bezier, B-spline, catenary and polyline curves | P2 |
| TT_SUbD | Subdivision surfaces (quad mesh) | P3 |
| loop_subdiv, rp_sphere, JHS (Powerbar) | Loop subdivision, sphere primitive, misc. toolbar | P3 |

Before building them, confirm which ones are actually used.

## 14. Explicitly out of scope (unless asked)

3D Warehouse, Extension Warehouse, Trimble Connect, Sign-in and licensing, Send to LayOut, LayOut itself, Style Builder, Start PreDesign, Geo-location terrain download, Classifier/IFC types (21075), Ruby API and Ruby Console (Dogeometric would have its own scripting, if any), Dynamic Components (P3 at most), Advanced Camera Tools (P3).

---

## Decisions (answered open questions)

1. **`.skp` compatibility:** Dogeometric must open `.skp`; saving `.skp` too if feasible. Native format is `.dog`, plus open formats (STL, OBJ, glTF…).
2. **Target use:** 3D modelling of electronic enclosures and similar printable parts. Solids, precision, components and STL export (selection or whole model) take priority over architectural features.
3. **Units:** metric only for now.
4. **Platforms:** Windows, macOS, Linux, Android and iOS.

## Suggested MVP (P0) order

1. Viewport, camera (orbit / pan / zoom, standard views, perspective / parallel), axes, ground.
2. Topology core: vertices, edges and faces, automatic face creation, splitting.
3. Inference engine and the VCB.
4. Select, Line, Rectangle, Circle, 2-Point Arc.
5. Push/Pull, Move (with copy and arrays), Rotate, Eraser, Tape Measure with guides.
6. Groups (create, edit in context, explode), undo/redo, save and load.
7. Entity Info panel, Shaded / Wireframe / X-ray.
