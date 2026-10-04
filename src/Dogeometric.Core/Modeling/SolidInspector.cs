using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>The kinds of problem that keep a group from being a solid, as the Solid Inspector² extension reports them.</summary>
public enum SolidErrorKind
{
    HiddenFace,
    StrayEdge,
    SurfaceBorder,
    FaceHole,
    InternalFaceEdge,
    InternalFace,
    ExternalFace,
    ReversedFace,
    NestedInstance,
    ImageEntity,
    ShortEdge,
}

/// <summary>One problem found by <see cref="SolidInspector"/> and the entities (in the inspected collection) it concerns.</summary>
public sealed record SolidError(SolidErrorKind Kind, IReadOnlyList<object> Entities)
{
    public bool Fixable => SolidInspector.IsFixable(Kind);
}

/// <summary>
/// Solid Inspector² (ThomThom's extension): finds why a group or component is not a solid — holes, stray edges,
/// internal and external faces, reversed faces, nested instances… — and repairs what can be repaired automatically.
/// Internal, external and reversed faces come from walking the outer shell of each connected mesh from its topmost
/// face, choosing at edges shared by more than two faces the one turning outward the least.
/// </summary>
public static class SolidInspector
{
    /// <summary>The extension's default "short edge" threshold.</summary>
    public const double DefaultShortEdge = 3;

    public static string DisplayName(SolidErrorKind kind) => kind switch
    {
        SolidErrorKind.HiddenFace => "Hidden Faces",
        SolidErrorKind.StrayEdge => "Stray Edges",
        SolidErrorKind.SurfaceBorder => "Surface Borders",
        SolidErrorKind.FaceHole => "Face Holes",
        SolidErrorKind.InternalFaceEdge => "Internal Face Edges",
        SolidErrorKind.InternalFace => "Internal Faces",
        SolidErrorKind.ExternalFace => "External Faces",
        SolidErrorKind.ReversedFace => "Reversed Faces",
        SolidErrorKind.NestedInstance => "Nested Instances",
        SolidErrorKind.ImageEntity => "Image Entity",
        SolidErrorKind.ShortEdge => "Short Edges",
        _ => kind.ToString(),
    };

    public static string Description(SolidErrorKind kind) => kind switch
    {
        SolidErrorKind.HiddenFace => "Hidden faces are left out of STL files and may leave holes in the mesh. They are fixed by unhiding them.",
        SolidErrorKind.StrayEdge => "Stray edges are not connected to any face and are no part of a solid. They are fixed by erasing them.",
        SolidErrorKind.SurfaceBorder => "Edges that form the border of a surface or a hole in the mesh. These cannot be fixed automatically: close the mesh by hand and run the tool again.",
        SolidErrorKind.FaceHole => "Edges that form a hole in a face. They are fixed by erasing the hole.",
        SolidErrorKind.InternalFaceEdge => "Edges connected to internal faces. While the mesh has holes it is not possible to tell reliably which faces are internal: fix the holes and run the tool again.",
        SolidErrorKind.InternalFace => "Faces inside a mesh that should be a solid. They are fixed by erasing them.",
        SolidErrorKind.ExternalFace => "Faces sticking out of a mesh that should be a solid. They are fixed by erasing them.",
        SolidErrorKind.ReversedFace => "The front of every face of a solid must face outwards, or many applications will not treat it as a solid. They are fixed by reversing the faces.",
        SolidErrorKind.NestedInstance => "Groups and components inside the object are exported to STL, but Solid Tools and this inspector do not treat the object as a solid.",
        SolidErrorKind.ImageEntity => "Images are left out of STL files, but they keep Solid Tools from working on the object.",
        SolidErrorKind.ShortEdge => "Very small geometry may give unpredictable results through precision errors. This cannot be fixed automatically; scaling the model up by factors of 10 may help.",
        _ => "",
    };

    public static bool IsFixable(SolidErrorKind kind) => kind is SolidErrorKind.HiddenFace or SolidErrorKind.StrayEdge
        or SolidErrorKind.FaceHole or SolidErrorKind.InternalFace or SolidErrorKind.ExternalFace or SolidErrorKind.ReversedFace;

    /// <summary>
    /// Everything in <paramref name="e"/> that keeps it from being a solid. Short edges are only reported when a
    /// threshold (mm) is given, as the extension only checks them when asked to.
    /// </summary>
    public static List<SolidError> Find(Entities e, double? shortEdge = null)
    {
        var errors = new List<SolidError>();
        foreach (var face in e.Faces.Where(f => f.Hidden))
            errors.Add(new SolidError(SolidErrorKind.HiddenFace, [face]));

        var facesOf = FacesOfEdges(e);
        var borders = new List<Edge>();
        var holes = new List<Edge>();
        var crowded = new List<Edge>();
        foreach (var edge in e.Edges)
        {
            var faces = facesOf.GetValueOrDefault(edge) ?? [];
            switch (faces.Count)
            {
                case 0:
                    errors.Add(new SolidError(SolidErrorKind.StrayEdge, [edge]));
                    break;
                case 1:
                    (faces[0].OuterLoop.Edges.Any(x => x.Edge == edge) ? borders : holes).Add(edge);
                    break;
                case > 2:
                    crowded.Add(edge);
                    break;
            }
        }

        var shell = new Shell(e, facesOf);
        shell.Resolve();
        if (shell.Valid)
        {
            errors.AddRange(e.Faces.Where(shell.Internal.Contains).Select(f => new SolidError(SolidErrorKind.InternalFace, [f])));
            errors.AddRange(e.Faces.Where(shell.External.Contains).Select(f => new SolidError(SolidErrorKind.ExternalFace, [f])));
            errors.AddRange(e.Faces.Where(shell.Reversed.Contains).Select(f => new SolidError(SolidErrorKind.ReversedFace, [f])));
        }
        else
        {
            errors.AddRange(ConnectedRuns(borders).Select(run => new SolidError(SolidErrorKind.SurfaceBorder, run)));
            errors.AddRange(ConnectedRuns(holes).Select(run => new SolidError(SolidErrorKind.FaceHole, run)));
            errors.AddRange(crowded.Select(edge => new SolidError(SolidErrorKind.InternalFaceEdge, [edge])));
        }

        foreach (var inst in e.Instances)
            errors.Add(new SolidError(inst.Definition.IsImage ? SolidErrorKind.ImageEntity : SolidErrorKind.NestedInstance, [inst]));

        if (shortEdge is { } threshold)
            errors.AddRange(e.Edges.Where(edge => edge.Length < threshold).Select(edge => new SolidError(SolidErrorKind.ShortEdge, [edge])));
        return errors;
    }

    /// <summary>
    /// Repairs <paramref name="errors"/> in <paramref name="e"/>: erases internal and external faces, stray edges and
    /// face holes (with the edges left between erased faces), unhides hidden faces and reverses reversed ones.
    /// Returns false when some of them cannot be fixed automatically.
    /// </summary>
    public static bool Fix(Entities e, IEnumerable<SolidError> errors)
    {
        var allFixed = true;
        var eraseFaces = new HashSet<Face>();
        var eraseEdges = new HashSet<Edge>();
        var holeEdges = new HashSet<Edge>();
        var rest = new List<SolidError>();
        foreach (var error in errors)
        {
            switch (error.Kind)
            {
                case SolidErrorKind.InternalFace or SolidErrorKind.ExternalFace:
                    eraseFaces.UnionWith(error.Entities.OfType<Face>());
                    break;
                case SolidErrorKind.StrayEdge:
                    eraseEdges.UnionWith(error.Entities.OfType<Edge>());
                    break;
                case SolidErrorKind.FaceHole:
                    holeEdges.UnionWith(error.Entities.OfType<Edge>());
                    break;
                default:
                    rest.Add(error);
                    break;
            }
        }

        // Edges between faces that all go would be left behind as stray edges.
        var facesOf = FacesOfEdges(e);
        foreach (var face in eraseFaces)
            foreach (var edge in Topology.EdgesOf(face))
                if (facesOf.GetValueOrDefault(edge)?.All(eraseFaces.Contains) == true)
                    eraseEdges.Add(edge);

        // Erasing a hole's edges heals the face around it: its inner loop goes, the face stays.
        foreach (var face in e.Faces)
            face.Loops.RemoveAll(loop => loop != face.OuterLoop && loop.Edges.Any(x => holeEdges.Contains(x.Edge)));
        eraseEdges.UnionWith(holeEdges);
        Editing.Erase(e, eraseFaces.Cast<object>().Concat(eraseEdges));

        foreach (var error in rest)
        {
            switch (error.Kind)
            {
                case SolidErrorKind.HiddenFace:
                    foreach (var face in error.Entities.OfType<Face>())
                        face.Hidden = false;
                    break;
                case SolidErrorKind.ReversedFace:
                    foreach (var face in error.Entities.OfType<Face>().Where(e.Faces.Contains))
                        FaceFinder.Reverse(face);
                    break;
                default:
                    allFixed = false;
                    break;
            }
        }
        return allFixed;
    }

    /// <summary>The faces using each edge (each face once).</summary>
    private static Dictionary<Edge, List<Face>> FacesOfEdges(Entities e)
    {
        var map = new Dictionary<Edge, List<Face>>();
        foreach (var face in e.Faces)
            foreach (var edge in Topology.EdgesOf(face))
            {
                if (!map.TryGetValue(edge, out var list))
                    map[edge] = list = [];
                list.Add(face);
            }
        return map;
    }

    /// <summary>Splits edges into runs joined end to end.</summary>
    private static List<List<object>> ConnectedRuns(List<Edge> edges)
    {
        var at = new Dictionary<Vertex, List<Edge>>();
        foreach (var edge in edges)
            foreach (var v in new[] { edge.Start, edge.End })
            {
                if (!at.TryGetValue(v, out var list))
                    at[v] = list = [];
                list.Add(edge);
            }
        var runs = new List<List<object>>();
        var seen = new HashSet<Edge>();
        foreach (var first in edges)
        {
            if (!seen.Add(first))
                continue;
            var run = new List<object>();
            var stack = new Stack<Edge>([first]);
            while (stack.Count > 0)
            {
                var edge = stack.Pop();
                run.Add(edge);
                foreach (var next in at[edge.Start].Concat(at[edge.End]))
                    if (seen.Add(next))
                        stack.Push(next);
            }
            runs.Add(run);
        }
        return runs;
    }

    /// <summary>The outer shell of the meshes in an entity collection, as Solid Inspector² resolves it.</summary>
    private sealed class Shell(Entities e, Dictionary<Edge, List<Face>> facesOf)
    {
        private readonly Dictionary<Vertex, List<Edge>> _edgesAt = EdgesAt(e);
        private readonly Dictionary<(Edge, Face), bool> _reversedIn = ReversedIn(e);
        private readonly Dictionary<Face, Vec3> _normals = e.Faces.ToDictionary(f => f, f => f.Normal);
        private HashSet<Face> _internal = [];
        private HashSet<Face> _reversed = [];
        private HashSet<Face> _shell = [];

        public HashSet<Face> Internal => _internal;
        public HashSet<Face> External { get; private set; } = [];
        public HashSet<Face> Reversed { get; private set; } = [];

        public void Resolve()
        {
            var groups = ConnectedEdgeGroups();
            // A shell inside another one (a cavity) faces inwards: its walk starts from the other side.
            var cavity = groups.ToDictionary(g => g, g => Nested(g, groups));
            var front = new HashSet<Face>();
            foreach (var group in groups)
                if (StartFace(group, outside: !cavity[group]) is { } start)
                    front.UnionWith(FindShell(start));
            _internal = e.Faces.Where(f => !front.Contains(f)).ToHashSet();
            var reversedFromOutside = new HashSet<Face>(_reversed);

            // Walk again starting from the inside: faces only one walk reaches stick out of the solid.
            var back = new HashSet<Face>();
            foreach (var group in groups)
                if (StartFace(group, outside: cavity[group]) is { } start)
                    back.UnionWith(FindShell(start));
            _shell = front.Intersect(back).ToHashSet();
            External = e.Faces.Where(f => !_internal.Contains(f) && !_shell.Contains(f)).ToHashSet();
            Reversed = _shell.Intersect(reversedFromOutside).ToHashSet();
        }

        /// <summary>Whether <paramref name="group"/> lies inside an odd number of the other pieces (a ray from it crosses them oddly).</summary>
        private bool Nested(List<Edge> group, List<List<Edge>> groups)
        {
            var p = group[0].Start.Position;
            // A skewed ray, so it does not run along edges of axis-aligned models.
            var dir = new Vec3(1, 0.3141, 0.2718).Normalized();
            var own = group.ToHashSet();
            var crossings = 0;
            foreach (var f in e.Faces)
            {
                if (Topology.EdgesOf(f).Any(own.Contains))
                    continue;
                var n = _normals[f].Normalized();
                var origin = f.OuterLoop.Points.First();
                var denom = n.Dot(dir);
                if (Math.Abs(denom) < 1e-12)
                    continue;
                var t = (origin - p).Dot(n) / denom;
                if (t <= Tolerance.Length)
                    continue;
                if (Inside(f, p + dir * t, n))
                    crossings++;
            }
            return crossings % 2 == 1;
        }

        private static bool Inside(Face f, Vec3 point, Vec3 n)
        {
            var (u, v) = Polygon.PlaneAxes(n);
            var inside = false;
            foreach (var loop in f.Loops)
            {
                var pts = loop.Points.ToList();
                for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                {
                    double xi = pts[i].Dot(u), yi = pts[i].Dot(v), xj = pts[j].Dot(u), yj = pts[j].Dot(v);
                    double px = point.Dot(u), py = point.Dot(v);
                    if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>A closed shell: every edge of every shell face is shared by at least two shell faces.</summary>
        public bool Valid => _shell.Count > 0 && _shell.All(face =>
            Topology.EdgesOf(face).All(edge => facesOf[edge].Count(_shell.Contains) > 1));

        private List<Face> Faces(Edge edge) => (facesOf.GetValueOrDefault(edge) ?? []).Where(f => !_internal.Contains(f)).ToList();

        private Vec3 Normal(Face f) => _reversed.Contains(f) ? -_normals[f] : _normals[f];

        private bool IsReversedIn(Edge edge, Face f) => _reversedIn[(edge, f)] ^ _reversed.Contains(f);

        private void Toggle(Face f)
        {
            if (!_reversed.Remove(f))
                _reversed.Add(f);
        }

        /// <summary>The edges of each connected piece of geometry.</summary>
        private List<List<Edge>> ConnectedEdgeGroups()
        {
            var groups = new List<List<Edge>>();
            var seen = new HashSet<Edge>();
            foreach (var first in e.Edges)
            {
                if (!seen.Add(first))
                    continue;
                var group = new List<Edge>();
                var stack = new Stack<Edge>([first]);
                while (stack.Count > 0)
                {
                    var edge = stack.Pop();
                    group.Add(edge);
                    foreach (var next in _edgesAt[edge.Start].Concat(_edgesAt[edge.End]))
                        if (seen.Add(next))
                            stack.Push(next);
                }
                groups.Add(group);
            }
            return groups;
        }

        /// <summary>
        /// A face certainly on the outside: at the topmost vertex, the flattest edge's steepest face, oriented up (or
        /// down for the walk from the inside).
        /// </summary>
        private Face? StartFace(List<Edge> group, bool outside)
        {
            var vertices = group.SelectMany(edge => new[] { edge.Start, edge.End }).Distinct()
                .Where(v => _edgesAt[v].Any(edge => Faces(edge).Count > 0)).ToList();
            if (vertices.Count == 0)
                return null;
            var top = vertices.MaxBy(v => v.Position.Z)!;
            var edge = _edgesAt[top].Where(x => Faces(x).Count > 0)
                .MinBy(x => Math.Abs((x.End.Position - x.Start.Position).Normalized().Z))!;
            var face = Faces(edge).MaxBy(f => Math.Abs(Normal(f).Z))!;
            if (outside ? Normal(face).Z < 0 : Normal(face).Z > 0)
                Toggle(face);
            return face;
        }

        private List<Face> FindShell(Face start)
        {
            var stack = new Stack<Face>([start]);
            var processedFaces = new HashSet<Face> { start };
            var processedEdges = new HashSet<Edge>();
            var shell = new List<Face>();
            while (stack.Count > 0)
            {
                var face = stack.Pop();
                shell.Add(face);
                foreach (var loop in face.Loops)
                    foreach (var (edge, _) in loop.Edges)
                    {
                        if (processedEdges.Contains(edge) || Faces(edge).Count < 2)
                            continue;
                        processedEdges.Add(edge);
                        if (OtherShellFace(edge, face) is not { } other || processedFaces.Contains(other))
                            continue;
                        stack.Push(other);
                        processedFaces.Add(other);
                    }
            }
            return shell;
        }

        /// <summary>
        /// The face across <paramref name="edge"/> that continues the shell, oriented consistently with
        /// <paramref name="face"/>; with more than two faces, the one turning least from the outside.
        /// </summary>
        private Face? OtherShellFace(Edge edge, Face face)
        {
            var faces = Faces(edge);
            if (faces.Count == 1)
                return null;
            var reversed = IsReversedIn(edge, face);
            Face? next;
            if (faces.Count == 2)
            {
                next = faces.FirstOrDefault(f => f != face);
            }
            else
            {
                var edgeDir = reversed ? edge.Start.Position - edge.End.Position : edge.End.Position - edge.Start.Position;
                var faceDir = Normal(face);
                var product = faceDir.Cross(edgeDir);
                var minimum = Math.PI * 2;
                next = null;
                foreach (var other in faces.Where(f => f != face))
                {
                    var otherDir = Normal(other);
                    if (IsReversedIn(edge, other) == reversed)
                        otherDir = -otherDir;
                    var otherProduct = edgeDir.Cross(otherDir);
                    var angle = AngleBetween(product, otherProduct);
                    if (otherProduct.Dot(faceDir) < 0)
                        angle = Math.PI * 2 - angle;
                    if (angle < minimum)
                    {
                        minimum = angle;
                        next = other;
                    }
                }
            }
            if (next == null)
                return null;
            // Neighbours of a consistently oriented shell run along their shared edge in opposite directions.
            if (IsReversedIn(edge, next) == reversed)
                Toggle(next);
            return next;
        }

        private static double AngleBetween(Vec3 a, Vec3 b)
        {
            var d = a.Length * b.Length;
            return d < 1e-30 ? 0 : Math.Acos(Math.Clamp(a.Dot(b) / d, -1, 1));
        }

        private static Dictionary<Vertex, List<Edge>> EdgesAt(Entities e)
        {
            var map = new Dictionary<Vertex, List<Edge>>();
            foreach (var edge in e.Edges)
                foreach (var v in new[] { edge.Start, edge.End })
                {
                    if (!map.TryGetValue(v, out var list))
                        map[v] = list = [];
                    list.Add(edge);
                }
            return map;
        }

        private static Dictionary<(Edge, Face), bool> ReversedIn(Entities e)
        {
            var map = new Dictionary<(Edge, Face), bool>();
            foreach (var face in e.Faces)
                foreach (var loop in face.Loops)
                    foreach (var (edge, rev) in loop.Edges)
                        map.TryAdd((edge, face), rev);
            return map;
        }
    }
}
