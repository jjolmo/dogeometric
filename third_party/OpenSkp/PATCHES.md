# Vendored OpenSKP (.NET)

Source: https://github.com/iamahsanmehmood/openskp, tag `nuget-v1.3.0` (commit e8b5af6), `packages/dotnet/OpenSkp`.
Licence: MIT (see `LICENSE` in this folder). Copyright (c) 2024-2026 Ahsan Mehmood.

Vendored rather than referenced from NuGet so Dogeometric can carry fixes before they land upstream.
Every change is listed here and should be offered upstream.

## Patches

1. **Group definitions** (`Definition.IsGroup`). VFF (2021+): tag `8315` of a definition record is its behaviour
   type — 0 component, 1 group, 2 image (upstream already used 2 for images). Legacy (2013–2020): a definition placed
   by a `CGroup` entity is a group. Verified against SketchUp 2021's own Ruby API (`ComponentDefinition#group?`) on
   the whole test corpus: 54/54 VFF files and the legacy files agree.
2. **Planarity tolerance** (`Create.cs`). The writer accepted a face only if every point was within span × 1e-6" of the
   fitted plane, so faces SketchUp had saved as planar (off by up to SketchUp's own 0.001" tolerance) were
   fan-triangulated on export. The tolerance is now max(span × 1e-6, 0.004"): real SketchUp faces deviate up to
   ~0.0015" from the plane this writer fits, and 0.004" (0.1 mm) covers the whole test corpus.
3. **Root-level group placement** (`SkpBuilder.AddGroupInstance`). The root builder could only place groups inline
   (`AddGroup`), one definition per group, so copies of a group that SketchUp stores sharing one definition came back
   as separate definitions. The new method mirrors `AddInstance` but writes a `CGroup`, like the existing
   `ComponentDefinitionBuilder.AddGroupInstance`.
4. **Definition kind and behaviour in legacy files** (`Create.cs`, `Legacy.cs`). In the 43-byte gap before a
   definition's thumbnail, byte −9 holds the behaviour flags (1 = always faces camera, 2 = shadows face sun; upstream
   already read these) and byte −4 is 1 for group definitions. Found by comparing definitions SketchUp reports as
   groups/components in real files. The writer zeroed the gap, so groups came back as plain definitions in
   SketchUp and 2D figures stopped facing the camera; `ComponentDefinitionBuilder` now has `IsGroupDefinition`,
   `AlwaysFacesCamera` and `ShadowsFaceSun`, and the reader also takes the group byte into account.
5. **Self-touching loops** (`Create.cs`, `WriteEdgeChain`). Vertex slots were resolved once for the whole chain
   before writing, so a point that appears twice in one loop (common in 2D silhouettes) got two vertices and
   duplicate edges. Each point is now looked up again right before use.
6. **Big-object escape in the scaffold renumbering** (`Create.cs`, `ShiftRef`). When a shifted reference passed
   0x7FFF it was rewritten with the escape tag `FF FF` (MFC's *new class* tag) instead of `FF 7F` (0x7FFF, the
   big-object tag `NewOfKnownClass`/`Backref` already use). Only models with more than 32 767 archive objects hit it;
   SketchUp rejected those files ("Unexpected file format") and OpenSKP's own reader failed on them.
7. **Fan triangulation without partial writes** (`Create.cs`, `FaceWriting.WriteFaceOrTriangulate`). The
   autoTriangulate fallback wrote fan triangles one by one; a collinear triangle (common in concave faces) threw after
   earlier ones were already in the buffer but not in the entity count, corrupting the enclosing definition (SketchUp:
   "Unexpected file format"). All triangles are now validated first and collinear ones skipped. Dogeometric itself
   no longer uses autoTriangulate (it triangulates non-planar faces with its own ear clipping), so this only guards
   other callers.
8. **Definition entity count after a trailing layer separator** (`Legacy.cs`, `ReadDefinition`). SketchUp 2019 files
   can carry one more 2-byte null after a definition's layer list, so the header was read two bytes early and the
   entity count landed on its own high half (an "implausible def entity count"). When the count is implausible it is
   re-read two bytes on, and kept only if it is plausible and an object record follows. Found on SketchUp's own
   `Tutorial01.skp`.
9. **Null records inside definition entity lists** (`Legacy.cs`, `ReadEntityListInner`). The same files put null
   records (tag 0) among a definition's entities: they take a declared slot but carry no entity. They are skipped and
   counted, and once one is seen the definition tail signature (nrel 0, GUID, name marker) ends the list, as it
   already did for burned indices.
10. **Image entities inside definitions** (`Create.cs`, `ComponentDefinitionBuilder.AddImageInstance`,
    `SkpBuilder.AddImageInstance`). `AddImage` only placed Images at the top level, building a definition per call;
    SketchUp nests Images in groups and components and shares one definition between copies. The new methods place a
    `CImage` that references an already-written image definition, at any level. An image definition is marked with
    kind 2 in the definition tail's gap byte (patch 4's byte -4: 0 component, 1 group, 2 image), as SketchUp 2021
    writes when it saves a model with Images in the 2017 format; without it SketchUp counts it as a component.

11. **Plain edges** (`Create.cs`, `WriteEdge`, `ComponentDefinitionBuilder.AddEdge`, `SkpBuilder.AddEdge`). The only
    way to write a loose edge was `AddPolyline`, which groups even a single edge into a curve, so every stray edge came
    back as a one-segment curve. `AddEdge` writes one edge with no curve. Edges are also how Dogeometric gives each
    edge its own soft/smooth/hidden flags: a face's edges take the face's flags only when no earlier call declared them.

## Note on `_scaffold/blank_v17.skp`

Shipped unchanged from upstream: the empty-document template the writer splices geometry into. Upstream documents
that its bytes come from a bare `SUModelCreate` + save with Trimble's SDK (SketchUp's own empty-document
boilerplate, no user content). It is part of the upstream MIT package; Dogeometric does not use the SDK.

9. **Empty documents** (`Create.cs`, `SkpBuilder.ToBytes`). A builder with nothing added threw "no geometry added";
   it now returns the bundled blank document unchanged, which is SketchUp's own empty model (written by its SDK).
   Anything else without geometry (materials, layers or definitions only) still throws.

10. **Definition attribute dictionaries** (`Geometry.cs` `CollectDefs`, `Parser.cs`, `Model.cs`). `Definition` gains
    `AttributeDictionaries`, read with the existing `ExtractAttributeDictionaries` from the D007 a definition (7C15)
    carries in its 8813 child. SketchUp keeps a definition's classification there (`AppliedSchemaTypes`:
    `"IFC 2x3" → "IfcWall"`), and Dynamic Components their definition-level `dynamic_attributes`. Checked on a file
    SketchUp 2021 saved after `add_classification("IFC 2x3", "IfcWall")`. The legacy (2017) reader keeps the
    definition's `CAttributeContainer` from its preamble the same way (`Legacy.cs` `ReadDefinition`), which is how
    Dogeometric's own `.skp` files carry the classification back.
