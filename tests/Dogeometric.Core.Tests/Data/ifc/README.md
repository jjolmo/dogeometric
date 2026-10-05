`building.ifc` was made with ifcopenshell 0.8 (`ifcopenshell.api`): a wall, a round column and a slab as extrusions, and a
box as a polygonal face set, each placed and turned, to check the IFC importer against ifcopenshell's own geometry.

`openings.ifc` is a 4 m wall turned 30° with a window opening and a door opening reaching below its base (`IfcOpeningElement`
voiding it through `IfcRelVoidsElement`, `ifcopenshell.api.feature.add_feature`); ifcopenshell gives the cut wall 1.382 m³.
