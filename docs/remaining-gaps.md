# Remaining gaps against SketchUp 2021

Compared on 2026-10-05 against a reference SketchUp 2021 install: its menu tree (Win32 dump), toolbars,
Instructor pages (modifier keys of every tool), dialog strings and shipped extensions. Trimble services are left
out on purpose (3D Warehouse, Extension Warehouse, Send to LayOut, PreDesign, Share Model/Component, Manage
Licensing, Show Terrain): their menu entries exist and are disabled.

The menus now match SketchUp's item for item, apart from those services and the extensions' own About/Video/
Donation entries. What remains is depth, not entries. Ordered by impact on modelling **electronics enclosures**
(PCB outline in, shell and lid with bosses, cut-outs and fillets, out to a 3D printer or a CAD package).

## High impact

| Gap | Why it matters for enclosures | Notes |
|---|---|---|
| STEP import: offset surfaces | One offset surface sample (a loft's side offset 2 mm) still comes in with 5 non-manifold edges near its boundary, where points are projected back onto the offset surface approximately. | Curved faces are now Delaunay on the surface, seams included: areas within 0.4 % of FreeCAD's on the test parts and 0.04 % on FreeCAD's sample parts (Schenkel, 409 faces with spheres through their poles; EngineBlock; PartDesignExample), all closed. |
| Import details | IFC boolean cuts (openings clipped from walls) are not applied. Components come in as components from COLLADA, KMZ, DWG, DXF, 3DS (as groups), STEP and IFC. | |
| Textures in mesh formats | COLLADA and KMZ carry textures both ways (pictures beside the .dae or inside the .kmz, each face's placement kept); OBJ, glTF, FBX, 3DS and VRML exports and OBJ/3DS imports still leave them out. | Exported triangles now carry texture coordinates, so each writer only has to add its own image references. |

## Medium impact

| Gap | Notes |
|---|---|
| Ruby API | Extensions › Developer › Ruby Console runs C# against the model (Model, Entities, Selection, puts, Pt, AddFace; one undo step per line), but there is no Ruby: SketchUp's .rb extensions and scripts do not run. |
| Model Info › Classifications | Classifier types go to and from `.skp` (AppliedSchemaTypes, checked in SketchUp 2021) and `.dog`; importing other schemas (gbXML, .skc files) is missing. |

## Low impact

| Gap | Notes |
|---|---|
| Fredo6 Donation, Plugin Information, Check for Update and LibFredo6 Settings | Payment pages and plugin services with no use in a native rebuild. About, Video and Documentation entries are there. |
| Advanced Camera Tools sub-categories | All 93 cameras are there; RED Mysterium sits under RED® (as in SketchUp's CSV). |

## Closed in this round (for reference)

Styles (edges, faces, background, modelling colours, watermarks, `.style` load/save), every tool's Ctrl/Alt/Shift
from the Instructor pages, VCB coordinates/arrays/offsets, imperial units and formats, Outliner editing, section
plane naming/symbol/fill troubleshooting/slice to group, Dimension and Text options, 2D image and vector export
options, scene properties to save, Orient Faces, Colorize, Cast/Receive Shadows, Make Unique Texture, STEP export,
Solid Tools tests (Outer Shell now fills cavities), Transparency quality Nicer, Rotate about a dragged axis and Image
Igloo Shift/Ctrl + arrows (all checked on screen), Export selection only for STEP, local component collections (browse, search, place), IFC 4 export
(checked with ifcopenshell), Classifier tool (Shift/Alt/Ctrl, pre-selection, IFC types in the export), COLLADA and KMZ import
(checked with files exported by SketchUp), KMZ export (checked by importing it in SketchUp), DWG import and
DWG/DXF 3D export through ACadSharp (both checked against SketchUp 2021), DXF 3D faces, ellipses and circles off the XY plane, VRML export (read back with VTK), 3DS import and export (checked
against SketchUp both ways), FBX export (read by Godot's importer at true size; SketchUp's own FBX comes out 10× too
big there), dotXSI export (same structure as SketchUp's), `.dog` previews
saved with the file and shown in collections, Help › Welcome (templates, Open, recent files with previews), STEP import (analytic and
B-spline surfaces, extrusions, revolutions, assemblies) and STEP colours both ways (AP214 styled items; only
checked against our own reader, as FreeCAD loads colours only with its GUI), IFC import (extrusions, face sets,
faceted B-reps, mapped items, placements, colours, classified; checked against ifcopenshell).
