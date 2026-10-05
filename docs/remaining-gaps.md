# Remaining gaps against SketchUp 2021

Compared on 2026-10-05 against the reference SketchUp 2021 on undine: its menu tree (Win32 dump), toolbars,
Instructor pages (modifier keys of every tool), dialog strings and shipped extensions. Trimble services are left
out on purpose (3D Warehouse, Extension Warehouse, Send to LayOut, PreDesign, Share Model/Component, Manage
Licensing, Show Terrain): their menu entries exist and are disabled.

The menus now match SketchUp's item for item, apart from those services and the extensions' own About/Video/
Donation entries. What remains is depth, not entries. Ordered by impact on modelling **electronics enclosures**
(PCB outline in, shell and lid with bosses, cut-outs and fillets, out to a 3D printer or a CAD package).

## High impact

| Gap | Why it matters for enclosures | Notes |
|---|---|---|
| STEP export (and import) | Enclosures go on to FreeCAD/KiCad/Fusion for fit checks and manufacturing; STL loses the exact geometry. | Not in SketchUp itself (it needs a plugin), so beyond parity, but the most useful format here. Planar faces and polygonal curves map to an AP214 faceted B-rep. |
| Solid Tools test coverage | Shells, lids and holes are built with Union/Subtract/Trim/Split/Outer Shell. `Dogeometric.Solids` has no test project; Trim, Split and Outer Shell are only checked by hand. | Add `tests/Dogeometric.Solids.Tests` with volume and manifold checks, as the e2e harness does. |
| Component libraries (Components panel › Open or create a local collection) | Standoffs, screws, connectors and PCBs reused from a folder of `.skp` files. | The panel only shows In Model definitions; File › Import of one `.skp` works. |
| Remaining file formats | Import: DWG (mechanical drawings), 3DS, DAE, IFC, KMZ. Export: DWG, 3DS, FBX, IFC, KMZ, VRML, XSI. | OpenSKP already has an IFC writer (`third_party/OpenSkp/IfcExport.cs`) to wire to File › Export. DWG needs a reader/writer (DXF is done both ways). |

## Medium impact

| Gap | Notes |
|---|---|
| Transparency quality "Nicer" | The far-then-near second pass was committed but not confirmed on screen. |
| Rotate: axis set by dragging from the centre | The protractor turns along the drag; a full rotation about a dragged axis is not yet checked. |
| Image Igloo: Shift/Ctrl + arrows | Implemented, not checked on screen (needs a model with several matched photos from one spot). |
| Ruby Console and Ruby API | SketchUp's extension mechanism. Our extensions are rebuilt natively; no scripting yet. A console with a small command language (or an embedded scripting host) would cover macros. |
| Classifier tool and Model Info › Classifications | IFC types on components; only matters with IFC export. |

## Low impact

| Gap | Notes |
|---|---|
| Help › Welcome to SketchUp | A start dialog with templates and recent files (templates and recent files exist in File). |
| BezierSpline › About / Documentation, Fredo6 About/Video/Donation entries | Plugin chrome; no modelling function. |
| Advanced Camera Tools sub-categories | All 93 cameras are there; RED Mysterium sits under RED® (as in SketchUp's CSV). |

## Closed in this round (for reference)

Styles (edges, faces, background, modelling colours, watermarks, `.style` load/save), every tool's Ctrl/Alt/Shift
from the Instructor pages, VCB coordinates/arrays/offsets, imperial units and formats, Outliner editing, section
plane naming/symbol/fill troubleshooting/slice to group, Dimension and Text options, 2D image and vector export
options, scene properties to save, Orient Faces, Colorize, Cast/Receive Shadows, Make Unique Texture.
