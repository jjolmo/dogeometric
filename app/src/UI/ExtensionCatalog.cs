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
        new("JointPushPull", "Fredo6", "Push/Pull or thicken many faces at once: Joint, Round, Vector, Normal, Extrude, Follow (Tools › Fredo6 Collection).", true),
        new("CircleByDiameter", "The Sketchup Dude", "Draws a circle from the two ends of its diameter (Draw menu).", true),
        new("rp_sphere", "", "Creates a sphere from its radius and segments (Draw menu).", true),
        new("Select Curve", "Thomas Thomassen", "Selects runs of connected visible edges with one click (Tools menu).", true),
        new("BezierSpline", "Fredo6", "Bézier, B-spline, Catmull, F-spline, Courbette, arc/chamfer/dog-bone corners, dividers, editing (Draw › BezierSpline curves).", true),
        new("FredoScale", "Fredo6", "Scales, stretches, rotates, tapers, twists, shears and bends the selection's box (Tools › Fredo6 Collection).", true),
        new("Curviloft", "Fredo6", "Loft by Spline, Loft along path and Skin Contours on selected curves (Tools › Fredo6 Collection).", true),
        new("Tools on Surface", "Fredo6", "Lines, shapes, arcs, offset, freehand and eraser drawn on surfaces (Tools › Fredo6 Collection).", true),
        new("SUbD", "Thomas Thomassen", "Catmull-Clark subdivision of groups with a live control cage and creases (Extensions › SUbD).", true),
        new("Loop subdivision smooth", "Nathan B", "Smooths faces by Loop subdivision (Tools menu).", true),
        new("Sandbox Tools", "SketchUp", "Terrain from contours and from scratch, Smoove, Stamp, Drape, Add Detail, Flip Edge.", true),
    ];
}
