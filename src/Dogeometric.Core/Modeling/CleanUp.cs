using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>CleanUp³'s options, with its defaults.</summary>
public sealed record CleanUpOptions
{
    public enum Scopes { Model, Local, Selected }

    public Scopes Scope { get; init; } = Scopes.Model;
    public bool Purge { get; init; } = true;
    public bool EraseHidden { get; init; }
    public bool EraseDuplicateFaces { get; init; }
    public bool GeometryToUntagged { get; init; }
    public bool MergeMaterials { get; init; }
    public bool MergeFaces { get; init; } = true;
    public bool IgnoreNormals { get; init; }
    public bool IgnoreMaterials { get; init; }
    public bool IgnoreUv { get; init; } = true;
    public bool RepairSplitEdges { get; init; } = true;
    public bool EraseStrayEdges { get; init; } = true;
    public bool RemoveEdgeMaterials { get; init; }

    /// <summary>Edges between faces meeting at up to this many degrees become soft and smooth (0: off).</summary>
    public double SmoothAngle { get; init; }
}

/// <summary>
/// CleanUp³ (ThomThom): purges, merges coplanar faces, repairs split edges, erases stray edges and more, over the
/// whole model, the active context or the selection. Returns what was done, as its statistics.
/// </summary>
public static class CleanUp
{
    public static SortedDictionary<string, int> Run(Model model, Entities context, IReadOnlyCollection<object> selection, CleanUpOptions o)
    {
        var stats = new SortedDictionary<string, int>();
        void Count(string key, int n) => stats[key] = stats.GetValueOrDefault(key) + n;

        if (o.Purge)
            Count("Purged Components", Grouping.PurgeUnused(model));

        // Each collection in scope, with the items to work on in it (null: all of them).
        var scope = new List<(Entities Entities, HashSet<object>? Only)>();
        switch (o.Scope)
        {
            case CleanUpOptions.Scopes.Model:
                scope.AddRange(model.AllEntities.Select(e => (e, (HashSet<object>?)null)));
                break;
            case CleanUpOptions.Scopes.Local:
                scope.Add((context, null));
                break;
            default:
                var only = selection.ToHashSet();
                foreach (var f in selection.OfType<Face>())
                    foreach (var edge in Topology.EdgesOf(f))
                        only.Add(edge);
                scope.Add((context, only));
                var seen = new HashSet<ComponentDefinition>();
                var stack = new Stack<ComponentDefinition>(selection.OfType<ComponentInstance>().Select(i => i.Definition));
                while (stack.Count > 0)
                {
                    var def = stack.Pop();
                    if (!seen.Add(def))
                        continue;
                    scope.Add((def.Entities, null));
                    foreach (var inner in def.Entities.Instances)
                        stack.Push(inner.Definition);
                }
                break;
        }
        bool In(HashSet<object>? only, object item) => only == null || only.Contains(item);

        if (o.MergeMaterials)
            Count("Materials Merged", MergeIdenticalMaterials(model));

        foreach (var (e, only) in scope)
        {
            if (o.EraseHidden)
            {
                var hidden = e.Faces.Where(f => f.Hidden && In(only, f)).Cast<object>()
                    .Concat(e.Edges.Where(x => x.Flags.HasFlag(EdgeFlags.Hidden) && In(only, x)))
                    .Concat(e.Instances.Where(i => i.Hidden && In(only, i))).ToList();
                // Erasing a hidden edge would take its faces with it: hidden edges between visible faces stay.
                hidden.RemoveAll(x => x is Edge edge && Topology.FacesOf(e, edge).Any(f => !f.Hidden));
                Editing.Erase(e, hidden);
                Count("Hidden Entities Erased", hidden.Count);
            }
            if (o.EraseDuplicateFaces)
                Count("Duplicate Faces Erased", EraseDuplicateFaces(e, only));
            if (o.MergeFaces)
                Count("Edges Reduced", MergeCoplanarFaces(e, only, o));
            if (o.EraseStrayEdges)
            {
                var stray = e.Edges.Where(x => In(only, x) && !Topology.FacesOf(e, x).Any()).ToList();
                Editing.Erase(e, stray);
                Count("Edges Reduced", stray.Count);
            }
            if (o.RepairSplitEdges)
                Count("Edges Reduced", RepairSplitEdges(e, only));
            foreach (var f in e.Faces.Where(f => In(only, f)))
                if (o.GeometryToUntagged)
                    f.Tag = null;
            foreach (var edge in e.Edges.Where(x => In(only, x)))
            {
                if (o.GeometryToUntagged)
                    edge.Tag = null;
                if (o.RemoveEdgeMaterials)
                    edge.Material = null;
            }
            if (o.SmoothAngle > 0)
                Count("Edges Smoothed", Editing.SoftenByAngle(e, e.Edges.Where(x => In(only, x)).ToList(), o.SmoothAngle));
        }

        if (o.Purge)
        {
            Count("Purged Components", Grouping.PurgeUnused(model));
            Count("Purged Materials", PurgeMaterials(model));
            Count("Purged Tags", PurgeTags(model));
        }
        return stats;
    }

    /// <summary>Erases the edges between coplanar faces facing the same way (or either way) with the same materials.</summary>
    public static int MergeCoplanarFaces(Entities e, HashSet<object>? only, CleanUpOptions o)
    {
        var merged = 0;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in e.Edges.Where(x => only == null || only.Contains(x)).ToList())
            {
                if (!e.Edges.Contains(edge))
                    continue;
                var faces = Topology.FacesOf(e, edge).ToList();
                if (faces.Count != 2 || faces[0] == faces[1] || !Mergeable(faces[0], faces[1], o))
                    continue;
                if (faces[0].Normal.Dot(faces[1].Normal) < 0)
                    FaceFinder.Reverse(faces[1]);
                if (FaceFinder.Merge(e, faces[0], faces[1], edge) != null)
                {
                    merged++;
                    changed = true;
                }
            }
        }
        return merged;
    }

    private static bool Mergeable(Face a, Face b, CleanUpOptions o)
    {
        var na = a.Normal.Normalized();
        var nb = b.Normal.Normalized();
        var dot = na.Dot(nb);
        if (o.IgnoreNormals ? Math.Abs(dot) < 1 - 1e-9 : dot < 1 - 1e-9)
            return false;
        var origin = a.OuterLoop.Points.First();
        if (b.Loops.SelectMany(l => l.Points).Any(p => Math.Abs((p - origin).Dot(na)) > Tolerance.Length))
            return false;
        if (SameVertices(a, b))
            return false;
        if (o.IgnoreMaterials)
            return true;
        var flipped = dot < 0;
        var (bf, bb) = flipped ? (b.BackMaterial, b.FrontMaterial) : (b.FrontMaterial, b.BackMaterial);
        if (a.FrontMaterial != bf || a.BackMaterial != bb)
            return false;
        // Textures must carry on across the edge unless UV is ignored.
        return o.IgnoreUv || a.FrontMaterial?.Texture == null || a.FrontMapping == b.FrontMapping;
    }

    private static bool SameVertices(Face a, Face b) => a.OuterLoop.Vertices.ToHashSet().SetEquals(b.OuterLoop.Vertices);

    /// <summary>Faces lying on top of another face with the same corners.</summary>
    public static int EraseDuplicateFaces(Entities e, HashSet<object>? only)
    {
        var duplicates = new HashSet<Face>();
        var faces = e.Faces.Where(f => only == null || only.Contains(f)).ToList();
        for (var i = 0; i < faces.Count; i++)
            for (var j = i + 1; j < faces.Count; j++)
                if (!duplicates.Contains(faces[i]) && !duplicates.Contains(faces[j]) && SameVertices(faces[i], faces[j]))
                    duplicates.Add(faces[j]);
        e.Faces.RemoveAll(duplicates.Contains);
        return duplicates.Count;
    }

    /// <summary>Joins edges split in two by a vertex where nothing else meets and they run straight on.</summary>
    public static int RepairSplitEdges(Entities e, HashSet<object>? only)
    {
        var repaired = 0;
        var changed = true;
        while (changed)
        {
            changed = false;
            var at = new Dictionary<Vertex, List<Edge>>();
            foreach (var edge in e.Edges)
                foreach (var v in new[] { edge.Start, edge.End })
                {
                    if (!at.TryGetValue(v, out var list))
                        at[v] = list = [];
                    list.Add(edge);
                }
            foreach (var (v, edges) in at)
            {
                if (edges.Count != 2 || (only != null && !edges.All(only.Contains)))
                    continue;
                var (a, b) = (edges[0], edges[1]);
                var da = (a.Other(v).Position - v.Position).Normalized();
                var db = (b.Other(v).Position - v.Position).Normalized();
                if (da.Dot(db) > -1 + 1e-9 || a.Flags != b.Flags || a.Tag != b.Tag || a.Material != b.Material)
                    continue;
                var joined = new Edge(a.Other(v), b.Other(v)) { Flags = a.Flags, Tag = a.Tag, Material = a.Material, Curve = a.Curve };
                foreach (var f in e.Faces)
                    foreach (var loop in f.Loops)
                        Rejoin(loop, a, b, joined);
                e.Edges.Remove(a);
                e.Edges.Remove(b);
                e.Edges.Add(joined);
                e.Vertices.Remove(v);
                if (only != null)
                    only.Add(joined);
                repaired++;
                changed = true;
                break;
            }
        }
        return repaired;
    }

    /// <summary>Replaces the consecutive pair a, b (either order) in a face loop with the joined edge.</summary>
    private static void Rejoin(FaceLoop loop, Edge a, Edge b, Edge joined)
    {
        var n = loop.Edges.Count;
        for (var i = 0; i < n; i++)
        {
            var (x, xr) = loop.Edges[i];
            var (y, _) = loop.Edges[(i + 1) % n];
            if (!((x == a && y == b) || (x == b && y == a)))
                continue;
            var from = xr ? x.End : x.Start;
            loop.Edges[i] = (joined, joined.Start != from);
            loop.Edges.RemoveAt((i + 1) % n);
            return;
        }
    }

    /// <summary>Materials with the same colour, opacity and texture become one.</summary>
    public static int MergeIdenticalMaterials(Model model)
    {
        var merged = 0;
        var keep = new List<Material>();
        var map = new Dictionary<Material, Material>();
        foreach (var m in model.Materials)
        {
            var twin = keep.FirstOrDefault(k => k.Color == m.Color && Math.Abs(k.Opacity - m.Opacity) < 1e-9 && SameTexture(k.Texture, m.Texture));
            if (twin == null)
                keep.Add(m);
            else
            {
                map[m] = twin;
                merged++;
            }
        }
        if (merged == 0)
            return 0;
        Material? Map(Material? m) => m != null && map.TryGetValue(m, out var t) ? t : m;
        foreach (var e in model.AllEntities)
        {
            foreach (var f in e.Faces)
            {
                f.FrontMaterial = Map(f.FrontMaterial);
                f.BackMaterial = Map(f.BackMaterial);
            }
            foreach (var edge in e.Edges)
                edge.Material = Map(edge.Material);
            foreach (var i in e.Instances)
                i.Material = Map(i.Material);
        }
        model.Materials.RemoveAll(map.ContainsKey);
        return merged;
    }

    private static bool SameTexture(TextureImage? a, TextureImage? b) =>
        a == null ? b == null : b != null && Math.Abs(a.WidthMm - b.WidthMm) < 1e-9 && Math.Abs(a.HeightMm - b.HeightMm) < 1e-9
            && (a.Data ?? []).AsSpan().SequenceEqual(b.Data ?? []);

    private static int PurgeMaterials(Model model)
    {
        var used = new HashSet<Material>();
        foreach (var e in model.AllEntities)
        {
            foreach (var f in e.Faces)
            {
                if (f.FrontMaterial != null) used.Add(f.FrontMaterial);
                if (f.BackMaterial != null) used.Add(f.BackMaterial);
            }
            foreach (var edge in e.Edges)
                if (edge.Material != null) used.Add(edge.Material);
            foreach (var i in e.Instances)
                if (i.Material != null) used.Add(i.Material);
        }
        return model.Materials.RemoveAll(m => !used.Contains(m));
    }

    private static int PurgeTags(Model model)
    {
        var used = new HashSet<Tag>();
        foreach (var e in model.AllEntities)
        {
            foreach (var t in e.Faces.Select(f => f.Tag).Concat(e.Edges.Select(x => x.Tag)).Concat(e.Instances.Select(i => i.Tag))
                         .Concat(e.Texts.Select(x => x.Tag)).Concat(e.Dimensions.Select(x => x.Tag)).Concat(e.SectionPlanes.Select(x => x.Tag)))
                if (t != null)
                    used.Add(t);
        }
        return model.Tags.RemoveAll(t => t != model.Tags[0] && !used.Contains(t));
    }
}
