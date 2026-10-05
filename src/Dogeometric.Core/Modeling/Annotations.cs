using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's linear dimension: the distance between <see cref="Start"/> and <see cref="End"/>, drawn on a line
/// moved by <see cref="Offset"/> from the measured points. The text is the measured length unless
/// <see cref="Text"/> overrides it ("&lt;&gt;" in the override stands for the length).
/// </summary>
/// <summary>What a dimension measures: a distance, or an arc's radius or a circle's diameter (leader to the text).</summary>
public enum DimensionKind { Linear, Radius, Diameter }

public sealed class LinearDimension(Vec3 start, Vec3 end, Vec3 offset)
{
    /// <summary>Radial kinds: <see cref="Start"/> is on the curve (the arrow), <see cref="End"/> its centre (radius) or the
    /// opposite point (diameter); the text sits at Start + Offset.</summary>
    public DimensionKind Kind { get; set; }

    public Vec3 Start { get; set; } = start;
    public Vec3 End { get; set; } = end;
    public Vec3 Offset { get; set; } = offset;
    public string Text { get; set; } = "";
    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }

    /// <summary>Its own look, set when it was made or last updated; null follows the model's.</summary>
    public DimensionStyle? Style { get; set; }

    public double Length => Start.DistanceTo(End);

    /// <summary>SketchUp's prefix for radial dimensions ("R", "DIA").</summary>
    public string Prefix => Kind switch { DimensionKind.Radius => "R", DimensionKind.Diameter => "DIA ", _ => "" };
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
    /// <summary>The short label (up to three characters) drawn in the plane's markers.</summary>
    public string Symbol { get; set; } = "";
    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }
}

public static class RadialDimensions
{
    /// <summary>
    /// A radial dimension on the arc or circle <paramref name="edge"/> belongs to, with the arrow where the curve is
    /// nearest <paramref name="near"/>: a full circle measures its diameter, an arc its radius (as SketchUp chooses).
    /// Null when the edge is not on an arc or circle.
    /// </summary>
    public static LinearDimension? For(Entities e, Edge edge, Vec3 near)
    {
        if (edge.Curve is not { Radius: > 0, Spline: null } c || c.IsPolygon)
            return null;
        var n = c.Normal.Normalized();
        var d = near - c.Center;
        d -= n * d.Dot(n);
        if (d.IsZero(1e-9))
            d = edge.Start.Position - c.Center;
        var onCurve = c.Center + d.Normalized() * c.Radius;
        var curveEdges = e.Edges.Where(x => x.Curve == c).ToList();
        var closed = curveEdges.Count >= 3 && curveEdges.SelectMany(x => new[] { x.Start, x.End }).GroupBy(v => v).All(g => g.Count() == 2);
        return closed
            ? new LinearDimension(onCurve, c.Center * 2 - onCurve, Vec3.Zero) { Kind = DimensionKind.Diameter }
            : new LinearDimension(onCurve, c.Center, Vec3.Zero) { Kind = DimensionKind.Radius };
    }

    /// <summary>Type › Radius / Diameter: switches a radial dimension, keeping its arrow point.</summary>
    public static void SetKind(LinearDimension d, DimensionKind kind)
    {
        if (d.Kind == kind || d.Kind == DimensionKind.Linear || kind == DimensionKind.Linear)
            return;
        var centre = d.Kind == DimensionKind.Radius ? d.End : (d.Start + d.End) * 0.5;
        d.End = kind == DimensionKind.Radius ? centre : centre * 2 - d.Start;
        d.Kind = kind;
    }
}
