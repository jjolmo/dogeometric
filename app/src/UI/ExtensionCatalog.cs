namespace Dogeometric.App.UI;

/// <summary>The reference SketchUp's extensions that Dogeometric rebuilds, and which are available yet.</summary>
public static class ExtensionCatalog
{
    public sealed record Extension(string Name, string Creator, string Description, bool Available);

    public static readonly Extension[] All =
    [
        new("Solid Inspector²", "Thomas Thomassen", "Finds and repairs what keeps a group from being a solid (Tools menu).", true),
        new("Round Corner", "Fredo6", "Rounds, sharpens or bevels the edges and corners of a shape (Tools › Fredo6 Collection).", true),
        new("Make Faces", "The Sketchup Dude", "Creates the faces missing between closed loops of edges.", false),
        new("Selection Toys", "Thomas Thomassen", "Selects and filters the selection by kind of entity.", false),
        new("CleanUp³", "Thomas Thomassen", "Merges coplanar faces, erases stray edges and other cleaning.", false),
        new("JointPushPull", "Fredo6", "Push/Pull of many faces at once, joined or along their normals.", false),
        new("CircleByDiameter", "The Sketchup Dude", "Draws a circle from two points of its diameter.", false),
        new("Select Curve", "Thomas Thomassen", "Selects whole curves with one click.", false),
        new("BezierSpline", "Fredo6", "Bézier, B-spline and other curves.", false),
        new("FredoScale", "Fredo6", "Scales, tapers, twists, bends and stretches.", false),
        new("Curviloft", "Fredo6", "Skins surfaces between curves (loft, rails).", false),
        new("Tools on Surface", "Fredo6", "Draws lines, shapes and offsets on curved surfaces.", false),
        new("SUbD", "Thomas Thomassen", "Subdivision surfaces.", false),
        new("Sandbox Tools", "SketchUp", "Terrain from contours and scratch, smoove, stamp, drape.", false),
    ];
}
