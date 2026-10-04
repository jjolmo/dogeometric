namespace Dogeometric.App.UI;

/// <summary>The reference SketchUp's extensions that Dogeometric rebuilds, and which are available yet.</summary>
public static class ExtensionCatalog
{
    public sealed record Extension(string Name, string Creator, string Description, bool Available);

    public static readonly Extension[] All =
    [
        new("Solid Inspector²", "Thomas Thomassen", "Finds and repairs what keeps a group from being a solid (Tools menu).", true),
        new("Round Corner", "Fredo6", "Rounds, sharpens or bevels the edges and corners of a shape (Tools › Fredo6 Collection).", true),
        new("Make Faces", "The Sketchup Dude", "Creates the faces missing between closed loops of edges (Extensions menu).", true),
        new("Selection Toys", "Thomas Thomassen", "Filters the selection by kind, selects copies and related faces (context menu and toolbar).", true),
        new("CleanUp³", "Thomas Thomassen", "Merges coplanar faces, repairs split edges, erases stray edges, purges (Extensions menu).", true),
        new("JointPushPull", "Fredo6", "Push/Pull of many faces at once, joined or along their normals.", false),
        new("CircleByDiameter", "The Sketchup Dude", "Draws a circle from the two ends of its diameter (Extensions menu).", true),
        new("Select Curve", "Thomas Thomassen", "Selects runs of connected visible edges with one click (Tools menu).", true),
        new("BezierSpline", "Fredo6", "Bézier, B-spline and other curves.", false),
        new("FredoScale", "Fredo6", "Scales, tapers, twists, bends and stretches.", false),
        new("Curviloft", "Fredo6", "Skins surfaces between curves (loft, rails).", false),
        new("Tools on Surface", "Fredo6", "Draws lines, shapes and offsets on curved surfaces.", false),
        new("SUbD", "Thomas Thomassen", "Subdivision surfaces.", false),
        new("Loop subdivision smooth", "Nathan B", "Smooths faces by Loop subdivision (Tools menu).", true),
        new("Sandbox Tools", "SketchUp", "Terrain from contours and scratch, smoove, stamp, drape.", false),
    ];
}
