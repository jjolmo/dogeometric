using Dogeometric.Core.Picking;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// An open model with its editing state: selection, active context, undo history and pick acceleration.
/// Tools change geometry only through <see cref="Operation"/> so undo and redraw stay in sync.
/// </summary>
public sealed class Document
{
    public Model Model { get; }
    public Selection Selection { get; } = new();
    public EditContext Context { get; }
    public UndoStack Undo { get; }
    public Picker Picker { get; } = new();

    /// <summary>
    /// Raised after geometry changed (operation, undo, redo) with the entity collections that changed, so renderers
    /// and pickers refresh only those.
    /// </summary>
    public event Action<IReadOnlyCollection<Entities>>? GeometryChanged;

    public Document(Model model)
    {
        Model = model;
        Context = new EditContext(model);
        Undo = new UndoStack(model);
        Undo.Changed += () =>
        {
            foreach (var e in Undo.LastTouched)
                Picker.Invalidate(e);
            // Undo can bring back or remove entities; drop selected items that are no longer in the context.
            var present = new HashSet<object>(Context.Entities.Faces.Cast<object>()
                .Concat(Context.Entities.Edges).Concat(Context.Entities.Instances));
            if (Selection.Items.Any(i => !present.Contains(i)))
                Selection.Set(Selection.Items.Where(present.Contains).ToList());
            GeometryChanged?.Invoke(Undo.LastTouched);
        };
    }

    /// <summary>Runs <paramref name="change"/> as one undoable step touching the active context.</summary>
    public void Operation(string name, Action<Entities> change)
    {
        var entities = Context.Entities;
        Undo.Begin(name, entities);
        try
        {
            change(entities);
            Undo.Commit();
        }
        catch
        {
            Undo.Abort();
            throw;
        }
    }

    /// <summary>Edit › Delete: erases the selection the way SketchUp does.</summary>
    public void EraseSelection()
    {
        if (Selection.IsEmpty)
            return;
        var items = Selection.Items.ToList();
        Operation("Erase", e => Editing.Erase(e, items));
        Selection.Clear();
    }
}

/// <summary>Geometry edits shared by tools and commands.</summary>
public static class Editing
{
    /// <summary>
    /// Removes instances, faces and edges. Erasing an edge also erases the faces it bounds; erasing a face keeps its
    /// edges. Vertices no edge uses any more are dropped.
    /// </summary>
    public static void Erase(Entities e, IEnumerable<object> items)
    {
        var set = items.ToHashSet();
        var edges = set.OfType<Edge>().ToHashSet();
        e.Instances.RemoveAll(set.Contains);
        e.Faces.RemoveAll(f => set.Contains(f) || f.Loops.Any(l => l.Edges.Any(x => edges.Contains(x.Edge))));
        e.Edges.RemoveAll(edges.Contains);
        RemoveOrphanVertices(e);
    }

    public static void RemoveOrphanVertices(Entities e)
    {
        var used = new HashSet<Vertex>();
        foreach (var edge in e.Edges)
        {
            used.Add(edge.Start);
            used.Add(edge.End);
        }
        e.Vertices.RemoveAll(v => !used.Contains(v));
    }
}

/// <summary>Move/copy of entities within one collection (Move tool).</summary>
public static class Transforming
{
    /// <summary>
    /// Moves the given faces, edges and instances by <paramref name="offset"/>. Faces and edges move their
    /// vertices, so connected geometry stretches, as SketchUp's Move does.
    /// </summary>
    public static void Move(Entities e, IEnumerable<object> items, Geometry.Vec3 offset) =>
        Apply(e, items, Geometry.Transform.Translation(offset));

    public static void Apply(Entities e, IEnumerable<object> items, Geometry.Transform t)
    {
        var vertices = new HashSet<Vertex>();
        foreach (var item in items)
        {
            switch (item)
            {
                case Face f:
                    foreach (var v in f.Loops.SelectMany(l => l.Vertices))
                        vertices.Add(v);
                    break;
                case Edge edge:
                    vertices.Add(edge.Start);
                    vertices.Add(edge.End);
                    break;
                case ComponentInstance inst:
                    inst.Transform = inst.Transform.Then(t);
                    break;
            }
        }
        foreach (var v in vertices)
            v.Position = t.ApplyPoint(v.Position);
    }

    /// <summary>
    /// Copies the given entities transformed by <paramref name="t"/> into the same collection; copied loose
    /// geometry sticks to what it lands on. Returns the copies (for selection).
    /// </summary>
    public static List<object> Copy(Entities e, IEnumerable<object> items, Geometry.Transform t)
    {
        var list = items.ToList();
        var copies = new List<object>();
        foreach (var inst in list.OfType<ComponentInstance>())
        {
            var c = e.AddInstance(inst.Definition, inst.Transform.Then(t));
            c.Name = inst.Name;
            c.Tag = inst.Tag;
            c.Material = inst.Material;
            copies.Add(c);
        }

        var faces = list.OfType<Face>().ToList();
        var edges = list.OfType<Edge>().Concat(faces.SelectMany(Topology.EdgesOf)).Distinct().ToList();
        var before = e.Faces.ToHashSet();
        var newEdges = new List<Edge>();
        foreach (var edge in edges)
            newEdges.AddRange(StickyGeometry.AddSegment(e, t.ApplyPoint(edge.Start.Position), t.ApplyPoint(edge.End.Position)));
        // Faces come back where the copied faces were: their loops close, and FaceFinder fills them.
        FaceFinder.Update(e, newEdges);
        foreach (var f in e.Faces.Where(f => !before.Contains(f)))
        {
            // Copy materials from the face it came from (same shape, moved).
            var centre = f.OuterLoop.Points.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / f.OuterLoop.Edges.Count;
            var source = faces.FirstOrDefault(s => t.ApplyPoint(s.OuterLoop.Points.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / s.OuterLoop.Edges.Count).DistanceTo(centre) < 1e-3);
            if (source != null)
            {
                if (f.Normal.Dot(t.ApplyNormal(source.Normal)) < 0)
                    FaceFinder.Reverse(f);
                f.FrontMaterial = source.FrontMaterial;
                f.BackMaterial = source.BackMaterial;
                f.Tag = source.Tag;
            }
            copies.Add(f);
        }
        copies.AddRange(newEdges);
        return copies;
    }
}

/// <summary>Make Group, Make Component and Explode.</summary>
public static class Grouping
{
    /// <summary>
    /// Moves the given entities into a new group or component placed where they were. Edges also used by faces
    /// left outside stay outside too (copied into the group), as in SketchUp. Components get their axes at the
    /// lower corner of their bounding box; groups keep the context's axes.
    /// </summary>
    public static ComponentInstance Make(Model model, Entities e, IReadOnlyCollection<object> items, bool asGroup, string? name = null)
    {
        var faces = items.OfType<Face>().ToHashSet();
        var looseEdges = items.OfType<Edge>().ToHashSet();
        var instances = items.OfType<ComponentInstance>().ToList();
        var allEdges = looseEdges.Concat(faces.SelectMany(Topology.EdgesOf)).ToHashSet();

        var points = allEdges.SelectMany(x => new[] { x.Start.Position, x.End.Position }).ToList();
        var bounds = Geometry.Bounds3.FromPoints(points);
        foreach (var inst in instances)
        {
            var b = inst.Definition.Entities.Bounds();
            if (!b.IsEmpty)
                bounds = bounds.Include(TransformedBounds(b, inst.Transform));
        }
        var origin = asGroup || bounds.IsEmpty ? Geometry.Vec3.Zero : bounds.Min;
        var toLocal = Geometry.Transform.Translation(-origin);

        var index = model.Definitions.Count(d => d.IsGroup == asGroup) + 1;
        var def = new ComponentDefinition
        {
            Name = name ?? (asGroup ? $"Group#{index}" : $"Component#{index}"),
            IsGroup = asGroup,
        };
        model.Definitions.Add(def);

        // Copy geometry into the definition, then take from the context what nobody outside still uses.
        Clone(def.Entities, faces, allEdges, toLocal);
        foreach (var inst in instances)
        {
            var moved = def.Entities.AddInstance(inst.Definition, inst.Transform.Then(toLocal));
            moved.Name = inst.Name;
            moved.Material = inst.Material;
            moved.Tag = inst.Tag;
            moved.Hidden = inst.Hidden;
        }

        e.Instances.RemoveAll(instances.Contains);
        e.Faces.RemoveAll(faces.Contains);
        var stillUsed = e.Faces.SelectMany(Topology.EdgesOf).ToHashSet();
        e.Edges.RemoveAll(x => allEdges.Contains(x) && !stillUsed.Contains(x));
        Editing.RemoveOrphanVertices(e);

        return e.AddInstance(def, Geometry.Transform.Translation(origin));
    }

    /// <summary>Explode: the instance's contents return to the context, transformed, sticking to what is there.</summary>
    public static List<object> Explode(Entities e, ComponentInstance inst)
    {
        var src = inst.Definition.Entities;
        var t = inst.Transform;
        var created = new List<object>();
        var before = e.Faces.ToHashSet();

        var newEdges = new List<Edge>();
        foreach (var edge in src.Edges)
        {
            var segs = StickyGeometry.AddSegment(e, t.ApplyPoint(edge.Start.Position), t.ApplyPoint(edge.End.Position));
            foreach (var s in segs)
                s.Flags = edge.Flags;
            newEdges.AddRange(segs);
        }
        FaceFinder.Update(e, newEdges);
        foreach (var f in e.Faces.Where(f => !before.Contains(f)))
        {
            // Match each new face to the source face it reproduces, for orientation and materials.
            var c = Centroid(f);
            var source = src.Faces.FirstOrDefault(s => t.ApplyPoint(Centroid(s)).DistanceTo(c) < 1e-3);
            if (source != null)
            {
                if (f.Normal.Dot(t.ApplyNormal(source.Normal)) < 0)
                    FaceFinder.Reverse(f);
                f.FrontMaterial = source.FrontMaterial ?? inst.Material;
                f.BackMaterial = source.BackMaterial ?? inst.Material;
                f.Tag = source.Tag;
            }
            created.Add(f);
        }
        foreach (var child in src.Instances)
        {
            var c = e.AddInstance(child.Definition, child.Transform.Then(t));
            c.Name = child.Name;
            c.Material = child.Material ?? inst.Material;
            c.Tag = child.Tag;
            created.Add(c);
        }
        created.AddRange(newEdges);
        e.Instances.Remove(inst);
        return created;
    }

    private static void Clone(Entities target, IEnumerable<Face> faces, IEnumerable<Edge> edges, Geometry.Transform t)
    {
        var vmap = new Dictionary<Vertex, Vertex>();
        var emap = new Dictionary<Edge, Edge>();
        Vertex V(Vertex v)
        {
            if (!vmap.TryGetValue(v, out var c))
                vmap[v] = c = target.AddVertex(t.ApplyPoint(v.Position));
            return c;
        }
        foreach (var edge in edges)
        {
            var c = target.AddEdge(V(edge.Start), V(edge.End));
            c.Flags = edge.Flags;
            c.Tag = edge.Tag;
            c.Material = edge.Material;
            emap[edge] = c;
        }
        foreach (var f in faces)
        {
            var c = new Face { FrontMaterial = f.FrontMaterial, BackMaterial = f.BackMaterial, Tag = f.Tag, Hidden = f.Hidden };
            foreach (var loop in f.Loops)
            {
                var l = new FaceLoop();
                l.Edges.AddRange(loop.Edges.Select(x => (emap[x.Edge], x.Reversed)));
                c.Loops.Add(l);
            }
            target.Faces.Add(c);
        }
    }

    private static Geometry.Vec3 Centroid(Face f)
    {
        var pts = f.OuterLoop.Points.ToList();
        return pts.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / pts.Count;
    }

    private static Geometry.Bounds3 TransformedBounds(Geometry.Bounds3 b, Geometry.Transform t) =>
        Geometry.Bounds3.FromPoints(Enumerable.Range(0, 8).Select(i => t.ApplyPoint(new Geometry.Vec3(
            (i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z))));
}
