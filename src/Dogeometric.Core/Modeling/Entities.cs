using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

public sealed class Vertex(Vec3 position)
{
    public Vec3 Position { get; set; } = position;
}

[Flags]
public enum EdgeFlags
{
    None = 0,
    Soft = 1,
    Smooth = 2,
    Hidden = 4,
}

/// <summary>
/// The curve an edge belongs to (circle, arc, polygon): SketchUp selects and edits it as one entity, and the edges
/// that extrude from it come out soft and smooth.
/// </summary>
public sealed class Curve
{
    public Vec3 Center { get; set; }
    public Vec3 Normal { get; set; } = Vec3.UnitZ;
    public double Radius { get; set; }
    public int Segments { get; set; }

    /// <summary>Polygons are curves with straight-edged semantics: their extrusions keep hard edges.</summary>
    public bool IsPolygon { get; set; }
}

public sealed class Edge(Vertex start, Vertex end)
{
    public Vertex Start { get; set; } = start;
    public Vertex End { get; set; } = end;
    public EdgeFlags Flags { get; set; }
    public Tag? Tag { get; set; }
    public Material? Material { get; set; }
    public Curve? Curve { get; set; }

    public double Length => Start.Position.DistanceTo(End.Position);

    public Vertex Other(Vertex v) => ReferenceEquals(v, Start) ? End : Start;
}

/// <summary>One boundary loop of a face: its edges in order, each traversed forwards or reversed.</summary>
public sealed class FaceLoop
{
    public List<(Edge Edge, bool Reversed)> Edges { get; } = [];

    /// <summary>Loop corners in traversal order.</summary>
    public IEnumerable<Vertex> Vertices => Edges.Select(e => e.Reversed ? e.Edge.End : e.Edge.Start);

    public IEnumerable<Vec3> Points => Vertices.Select(v => v.Position);
}

/// <summary>
/// A planar face: one outer loop (counter-clockwise seen from the front) plus optional inner loops (holes),
/// like SketchUp's faces.
/// </summary>
public sealed class Face
{
    public List<FaceLoop> Loops { get; } = [];
    public FaceLoop OuterLoop => Loops[0];
    public IEnumerable<FaceLoop> InnerLoops => Loops.Skip(1);

    public Material? FrontMaterial { get; set; }
    public Material? BackMaterial { get; set; }
    public Tag? Tag { get; set; }
    public bool Hidden { get; set; }

    /// <summary>Front-side normal from the outer loop's winding (Newell's method, robust for any polygon).</summary>
    public Vec3 Normal => Polygon.Normal(OuterLoop.Points.ToList());

    public double Area => Polygon.Area(OuterLoop.Points.ToList()) - InnerLoops.Sum(l => Polygon.Area(l.Points.ToList()));
}

/// <summary>
/// A guide line (Tape Measure / Protractor): through <see cref="Point"/> along <see cref="Direction"/>. Infinite
/// unless <see cref="Start"/>/<see cref="End"/> bound it.
/// </summary>
public sealed class GuideLine(Vec3 point, Vec3 direction)
{
    public Vec3 Point { get; set; } = point;
    public Vec3 Direction { get; set; } = direction.Normalized();
    public Vec3? Start { get; set; }
    public Vec3? End { get; set; }
    public bool IsInfinite => Start == null || End == null;
}

/// <summary>A guide point (Tape Measure from a point).</summary>
public sealed class GuidePoint(Vec3 position)
{
    public Vec3 Position { get; set; } = position;
}

/// <summary>A placed group or component: a definition plus a transform.</summary>
public sealed class ComponentInstance(ComponentDefinition definition)
{
    public ComponentDefinition Definition { get; set; } = definition;
    public Transform Transform { get; set; } = Transform.Identity;
    public string Name { get; set; } = "";
    public Tag? Tag { get; set; }

    /// <summary>Material inherited by default-material faces inside, as SketchUp paints groups/components.</summary>
    public Material? Material { get; set; }

    public bool Hidden { get; set; }
    public bool Locked { get; set; }

    public bool IsGroup => Definition.IsGroup;
}

/// <summary>
/// A set of entities: the model's top level, or the contents of a group/component definition.
/// </summary>
public sealed class Entities
{
    public List<Vertex> Vertices { get; } = [];
    public List<Edge> Edges { get; } = [];
    public List<Face> Faces { get; } = [];
    public List<ComponentInstance> Instances { get; } = [];
    public List<GuideLine> GuideLines { get; } = [];
    public List<GuidePoint> GuidePoints { get; } = [];
    public List<LinearDimension> Dimensions { get; } = [];
    public List<TextLabel> Texts { get; } = [];

    public bool IsEmpty => Edges.Count == 0 && Faces.Count == 0 && Instances.Count == 0;

    public Vertex AddVertex(Vec3 p)
    {
        var v = new Vertex(p);
        Vertices.Add(v);
        return v;
    }

    public Edge AddEdge(Vertex a, Vertex b)
    {
        var e = new Edge(a, b);
        Edges.Add(e);
        return e;
    }

    /// <summary>Existing vertex within <see cref="Tolerance.Length"/> of <paramref name="p"/>, or a new one.</summary>
    public Vertex VertexAt(Vec3 p)
    {
        foreach (var v in Vertices)
        {
            if (v.Position.DistanceTo(p) <= Tolerance.Length)
                return v;
        }
        return AddVertex(p);
    }

    /// <summary>Existing edge between the two vertices (either direction), or a new one.</summary>
    public Edge EdgeBetween(Vertex a, Vertex b)
    {
        foreach (var e in Edges)
        {
            if ((e.Start == a && e.End == b) || (e.Start == b && e.End == a))
                return e;
        }
        return AddEdge(a, b);
    }

    /// <summary>
    /// Adds a face from its outer boundary (counter-clockwise seen from the front) and optional holes, reusing
    /// vertices and edges already at those positions, like SketchUp's geometry sticks together.
    /// </summary>
    public Face AddFace(IReadOnlyList<Vec3> outer, IReadOnlyList<IReadOnlyList<Vec3>>? holes = null)
    {
        if (outer.Count < 3)
            throw new ArgumentException("A face needs at least 3 points", nameof(outer));
        var face = new Face();
        face.Loops.Add(Loop(outer));
        foreach (var h in holes ?? [])
            face.Loops.Add(Loop(h));
        Faces.Add(face);
        return face;

        FaceLoop Loop(IReadOnlyList<Vec3> pts)
        {
            var loop = new FaceLoop();
            var verts = pts.Select(VertexAt).ToList();
            for (var i = 0; i < verts.Count; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % verts.Count];
                var e = EdgeBetween(a, b);
                loop.Edges.Add((e, e.Start != a));
            }
            return loop;
        }
    }

    public ComponentInstance AddInstance(ComponentDefinition definition, Transform transform)
    {
        var i = new ComponentInstance(definition) { Transform = transform };
        Instances.Add(i);
        return i;
    }

    /// <summary>Local bounds, including nested instances transformed into this space.</summary>
    public Bounds3 Bounds()
    {
        var b = Bounds3.FromPoints(Vertices.Select(v => v.Position));
        foreach (var inst in Instances)
        {
            var inner = inst.Definition.Entities.Bounds();
            if (inner.IsEmpty)
                continue;
            foreach (var corner in Corners(inner))
                b = b.Include(inst.Transform.ApplyPoint(corner));
        }
        return b;
    }

    private static IEnumerable<Vec3> Corners(Bounds3 b)
    {
        for (var i = 0; i < 8; i++)
            yield return new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z);
    }
}

public sealed class ComponentDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Groups are definitions with a single instance, shown without a name in the Components panel.</summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// The definition behind an Image entity (a picture placed in the model): one textured rectangle. SketchUp
    /// lists images apart from components.
    /// </summary>
    public bool IsImage { get; set; }

    /// <summary>SketchUp's "Always face camera" behaviour (2D figures, billboards).</summary>
    public bool AlwaysFaceCamera { get; set; }

    /// <summary>SketchUp's "Shadows face sun" behaviour.</summary>
    public bool ShadowsFaceSun { get; set; }

    public Entities Entities { get; } = new();
}
