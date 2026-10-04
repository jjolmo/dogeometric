using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>The kinds Selection Toys' "Select Only" and "Deselect" filter the selection by.</summary>
public enum SelectionKind
{
    Edges, Faces, Groups, Components, Guides, GuidePoints, Text, Images, SectionPlanes,
    Curves, Arcs, Circles, Polygons, LinearDimensions,
    FrontDefaultMaterial, BackDefaultMaterial, Hidden, SoftEdges, SmoothEdges, BorderEdges, SelectionBorder,
}

/// <summary>
/// Selection Toys (ThomThom): filter the selection by kind, select copies of a group or component, entities on the
/// same tags or with the same materials, and faces related to the selected ones (coplanar, parallel, connected…).
/// </summary>
public static class SelectionToys
{
    public static string Label(SelectionKind kind) => kind switch
    {
        SelectionKind.GuidePoints => "Guide Points",
        SelectionKind.SectionPlanes => "Section Planes",
        SelectionKind.LinearDimensions => "Linear Dimensions",
        SelectionKind.FrontDefaultMaterial => "Front Default Material",
        SelectionKind.BackDefaultMaterial => "Back Default Material",
        SelectionKind.SoftEdges => "Soft Edges",
        SelectionKind.SmoothEdges => "Smooth Edges",
        SelectionKind.BorderEdges => "Border Edges",
        SelectionKind.SelectionBorder => "Selection Border",
        _ => kind.ToString(),
    };

    /// <summary>Whether <paramref name="item"/> is of <paramref name="kind"/>, within <paramref name="context"/> and the whole selection.</summary>
    public static bool Is(SelectionKind kind, object item, Entities context, IReadOnlyCollection<object> selection)
    {
        switch (kind)
        {
            case SelectionKind.Edges: return item is Edge;
            case SelectionKind.Faces: return item is Face;
            case SelectionKind.Groups: return item is ComponentInstance { IsGroup: true, Definition.IsImage: false };
            case SelectionKind.Components: return item is ComponentInstance { IsGroup: false, Definition.IsImage: false };
            case SelectionKind.Images: return item is ComponentInstance { Definition.IsImage: true };
            case SelectionKind.Guides: return item is GuideLine;
            case SelectionKind.GuidePoints: return item is GuidePoint;
            case SelectionKind.Text: return item is TextLabel;
            case SelectionKind.SectionPlanes: return item is SectionPlane;
            case SelectionKind.LinearDimensions: return item is LinearDimension;
            case SelectionKind.Curves: return item is Edge { Curve: not null };
            case SelectionKind.Polygons: return item is Edge { Curve.IsPolygon: true };
            case SelectionKind.Arcs or SelectionKind.Circles:
                if (item is not Edge { Curve: { IsPolygon: false } curve })
                    return false;
                return Closed(context, curve) == (kind == SelectionKind.Circles);
            case SelectionKind.FrontDefaultMaterial:
                return item switch { Face f => f.FrontMaterial == null, ComponentInstance i => i.Material == null, Edge e => e.Material == null, _ => false };
            case SelectionKind.BackDefaultMaterial: return item is Face { BackMaterial: null };
            case SelectionKind.Hidden:
                return item switch
                {
                    Face f => f.Hidden,
                    Edge e => e.Flags.HasFlag(EdgeFlags.Hidden),
                    ComponentInstance i => i.Hidden,
                    _ => false,
                };
            case SelectionKind.SoftEdges: return item is Edge e1 && e1.Flags.HasFlag(EdgeFlags.Soft);
            case SelectionKind.SmoothEdges: return item is Edge e2 && e2.Flags.HasFlag(EdgeFlags.Smooth);
            case SelectionKind.BorderEdges: return item is Edge e3 && Topology.FacesOf(context, e3).Count() == 1;
            case SelectionKind.SelectionBorder:
                // An edge of the selected faces that has a face on one side only within the selection.
                return item is Edge e4 && Topology.FacesOf(context, e4).Count(selection.Contains) == 1;
            default: return false;
        }
    }

    private static bool Closed(Entities context, Curve curve)
    {
        var edges = context.Edges.Where(e => e.Curve == curve).ToList();
        var ends = new Dictionary<Vertex, int>();
        foreach (var e in edges)
            foreach (var v in new[] { e.Start, e.End })
                ends[v] = ends.GetValueOrDefault(v) + 1;
        return edges.Count > 2 && ends.Values.All(n => n == 2);
    }

    /// <summary>Select Only: the selected items of that kind.</summary>
    public static List<object> Only(SelectionKind kind, Entities context, IReadOnlyCollection<object> selection) =>
        selection.Where(x => Is(kind, x, context, selection)).ToList();

    /// <summary>Deselect: the selection without the items of that kind.</summary>
    public static List<object> Without(SelectionKind kind, Entities context, IReadOnlyCollection<object> selection) =>
        selection.Where(x => !Is(kind, x, context, selection)).ToList();

    /// <summary>Copies (same definition) of the selected groups or components in the context, optionally on the same tags.</summary>
    public static List<object> Copies(Entities context, IReadOnlyCollection<object> selection, bool groups, bool sameTag)
    {
        var picked = selection.OfType<ComponentInstance>().Where(i => i.IsGroup == groups).ToList();
        var defs = picked.Select(i => i.Definition).ToHashSet();
        var tags = picked.Select(i => i.Tag).ToHashSet();
        return context.Instances.Where(i => defs.Contains(i.Definition) && (!sameTag || tags.Contains(i.Tag))).Cast<object>().ToList();
    }

    /// <summary>Group Copies › Convert into Components: the selected groups' definitions become components (copies included).</summary>
    public static int GroupsToComponents(Model model, IReadOnlyCollection<object> selection)
    {
        var defs = selection.OfType<ComponentInstance>().Where(i => i.IsGroup).Select(i => i.Definition).Distinct().ToList();
        var n = 1;
        foreach (var def in defs)
        {
            def.IsGroup = false;
            if (def.Name.Length == 0 || def.Name.StartsWith("Group#"))
            {
                while (model.Definitions.Any(d => d.Name == $"Component#{n}"))
                    n++;
                def.Name = $"Component#{n}";
            }
        }
        return defs.Count;
    }

    private static Tag? TagOf(object item) => item switch
    {
        Face f => f.Tag,
        Edge e => e.Tag,
        ComponentInstance i => i.Tag,
        TextLabel t => t.Tag,
        LinearDimension d => d.Tag,
        SectionPlane s => s.Tag,
        _ => null,
    };

    private static Material? MaterialOf(object item) => item switch
    {
        Face f => f.FrontMaterial,
        Edge e => e.Material,
        ComponentInstance i => i.Material,
        _ => null,
    };

    private static IEnumerable<object> All(Entities e) =>
        e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances).Concat(e.GuideLines).Concat(e.GuidePoints)
            .Concat(e.Texts).Concat(e.Dimensions).Concat(e.SectionPlanes);

    /// <summary>Everything in the context on the tags of the selected items.</summary>
    public static List<object> OnTags(Entities context, IReadOnlyCollection<object> selection)
    {
        var tags = selection.Select(TagOf).ToHashSet();
        return All(context).Where(x => tags.Contains(TagOf(x))).ToList();
    }

    /// <summary>Faces, edges and instances in the context with the materials of the selected ones.</summary>
    public static List<object> WithMaterials(Entities context, IReadOnlyCollection<object> selection)
    {
        var mats = selection.Select(MaterialOf).ToHashSet();
        return context.Faces.Cast<object>().Concat(context.Edges).Concat(context.Instances).Where(x => mats.Contains(MaterialOf(x))).ToList();
    }

    /// <summary>How a face relates to one of the selected faces.</summary>
    public enum FaceRelation { Coplanar, SameDirection, Parallel, Perpendicular, SameArea }

    private static bool Related(FaceRelation relation, Face a, Face b)
    {
        var na = a.Normal.Normalized();
        var nb = b.Normal.Normalized();
        var dot = na.Dot(nb);
        return relation switch
        {
            FaceRelation.SameDirection => dot > 1 - 1e-6,
            FaceRelation.Parallel => Math.Abs(dot) > 1 - 1e-6,
            FaceRelation.Perpendicular => Math.Abs(dot) < 1e-6,
            FaceRelation.Coplanar => Math.Abs(dot) > 1 - 1e-6 && Math.Abs((b.OuterLoop.Points.First() - a.OuterLoop.Points.First()).Dot(na)) < Tolerance.Length,
            FaceRelation.SameArea => Math.Abs(a.Area - b.Area) < Math.Max(1e-6, a.Area * 1e-6),
            _ => false,
        };
    }

    /// <summary>Faces in the context related to any of the selected faces.</summary>
    public static List<object> Faces(Entities context, IReadOnlyCollection<object> selection, FaceRelation relation)
    {
        var seeds = selection.OfType<Face>().ToList();
        return context.Faces.Where(f => seeds.Any(s => s == f || Related(relation, s, f))).Cast<object>().ToList();
    }

    /// <summary>Faces reachable from the selected ones through shared edges, crossing only into faces related to where they started.</summary>
    public static List<object> ConnectedFaces(Entities context, IReadOnlyCollection<object> selection, Func<Face, Face, bool> keep)
    {
        var facesOf = new Dictionary<Edge, List<Face>>();
        foreach (var f in context.Faces)
            foreach (var e in Topology.EdgesOf(f))
            {
                if (!facesOf.TryGetValue(e, out var list))
                    facesOf[e] = list = [];
                list.Add(f);
            }
        var result = new HashSet<Face>();
        foreach (var seed in selection.OfType<Face>())
        {
            var stack = new Stack<Face>([seed]);
            result.Add(seed);
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                foreach (var e in Topology.EdgesOf(f))
                    foreach (var g in facesOf[e])
                        if (!result.Contains(g) && keep(seed, g))
                        {
                            result.Add(g);
                            stack.Push(g);
                        }
            }
        }
        return result.Cast<object>().ToList();
    }

    public static List<object> ConnectedFaces(Entities context, IReadOnlyCollection<object> selection, FaceRelation relation) =>
        ConnectedFaces(context, selection, (seed, g) => Related(relation, seed, g));

    /// <summary>Connected faces sharing the material (or back material) of where they started.</summary>
    public static List<object> ConnectedByMaterial(Entities context, IReadOnlyCollection<object> selection, bool back)
    {
        Material? M(Face f) => back ? f.BackMaterial : f.FrontMaterial;
        return ConnectedFaces(context, selection, (seed, g) => M(seed) == M(g));
    }

    /// <summary>Connected geometry on the same tag as where it started.</summary>
    public static List<object> ConnectedByTag(Entities context, IReadOnlyCollection<object> selection)
    {
        var tags = selection.Select(TagOf).ToHashSet();
        var start = selection.FirstOrDefault(x => x is Face or Edge);
        if (start == null)
            return [];
        return Topology.Connected(context, start).Where(x => tags.Contains(TagOf(x))).ToList();
    }

    /// <summary>For each selected face, the closest face straight behind it facing the other way (the opposite wall).</summary>
    public static List<object> OppositeFaces(Entities context, IReadOnlyCollection<object> selection)
    {
        var result = new List<object>();
        foreach (var f in selection.OfType<Face>())
        {
            var n = f.Normal.Normalized();
            var pts = f.OuterLoop.Points.ToList();
            var origin = pts.Aggregate(Vec3.Zero, (a, p) => a + p) / pts.Count;
            Face? best = null;
            var bestDist = double.MaxValue;
            foreach (var g in context.Faces)
            {
                if (g == f || g.Normal.Normalized().Dot(n) > -1 + 1e-6)
                    continue;
                var gp = g.OuterLoop.Points.ToList();
                var dist = (origin - gp[0]).Dot(n);
                if (dist <= Tolerance.Length || dist >= bestDist)
                    continue;
                var hit = origin - n * dist;
                if (Contains(gp, g.Normal.Normalized(), hit))
                {
                    best = g;
                    bestDist = dist;
                }
            }
            if (best != null)
                result.Add(best);
        }
        return result;
    }

    private static bool Contains(List<Vec3> polygon, Vec3 normal, Vec3 p)
    {
        var (u, v) = Polygon.PlaneAxes(normal);
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            double xi = polygon[i].Dot(u), yi = polygon[i].Dot(v), xj = polygon[j].Dot(u), yj = polygon[j].Dot(v);
            double px = p.Dot(u), py = p.Dot(v);
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
