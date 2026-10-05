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
| STEP import | Vendor enclosure and connector models come as STEP. | Export is done (AP214, solids validated in FreeCAD). Import needs curved-surface tessellation. |
| Component libraries (Components panel › Open or create a local collection) | Standoffs, screws, connectors and PCBs reused from a folder of `.skp` files. | The panel only shows In Model definitions; File › Import of one `.skp` works. |
| Remaining file formats | Import: DWG (mechanical drawings), 3DS, DAE, IFC, KMZ. Export: DWG, 3DS, FBX, IFC, KMZ, VRML, XSI. | OpenSKP already has an IFC writer (`third_party/OpenSkp/IfcExport.cs`) to wire to File › Export. DWG needs a reader/writer (DXF is done both ways). |

## Medium impact

| Gap | Notes |
|---|---|
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
options, scene properties to save, Orient Faces, Colorize, Cast/Receive Shadows, Make Unique Texture, STEP export,
Solid Tools tests (Outer Shell now fills cavities), Transparency quality Nicer, Rotate about a dragged axis and Image
Igloo Shift/Ctrl + arrows (all checked on screen), Export selection only for STEP.
