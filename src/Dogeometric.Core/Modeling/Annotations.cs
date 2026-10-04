using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's linear dimension: the distance between <see cref="Start"/> and <see cref="End"/>, drawn on a line
/// moved by <see cref="Offset"/> from the measured points. The text is the measured length unless
/// <see cref="Text"/> overrides it ("&lt;&gt;" in the override stands for the length).
/// </summary>
public sealed class LinearDimension(Vec3 start, Vec3 end, Vec3 offset)
{
    public Vec3 Start { get; set; } = start;
    public Vec3 End { get; set; } = end;
    public Vec3 Offset { get; set; } = offset;
    public string Text { get; set; } = "";
    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }

    public double Length => Start.DistanceTo(End);
}

/// <summary>
/// SketchUp's Text: a note with a leader from <see cref="Point"/> to the text at Point + <see cref="Offset"/>, or
/// a screen text (no leader) at <see cref="ScreenPosition"/>, a fraction of the view's width and height.
/// </summary>
public sealed class TextLabel(string text)
{
    public string Text { get; set; } = text;
    public Vec3 Point { get; set; }
    public Vec3 Offset { get; set; }

    /// <summary>Screen text position (0..1 of the view), or null for a text attached to the model.</summary>
    public (double X, double Y)? ScreenPosition { get; set; }

    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>
/// SketchUp's section plane. Its arrows (<see cref="Normal"/>) point the way the cut is viewed: while it is the
/// active plane of its context (<see cref="Entities.ActiveSection"/>), geometry behind the arrows is cut away.
/// </summary>
public sealed class SectionPlane(Vec3 point, Vec3 normal)
{
    public Vec3 Point { get; set; } = point;
    public Vec3 Normal { get; set; } = normal.Normalized();
    public string Name { get; set; } = "";
    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }
}
