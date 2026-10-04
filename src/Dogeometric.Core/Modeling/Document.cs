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
