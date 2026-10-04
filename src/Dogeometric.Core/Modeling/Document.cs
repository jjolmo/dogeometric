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
