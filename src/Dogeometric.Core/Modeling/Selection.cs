namespace Dogeometric.Core.Modeling;

/// <summary>The selection set: faces, edges and instances of the active editing context.</summary>
public sealed class Selection
{
    private readonly HashSet<object> _items = [];

    public event Action? Changed;

    public IReadOnlyCollection<object> Items => _items;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    public bool Contains(object item) => _items.Contains(item);

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        _items.Clear();
        Changed?.Invoke();
    }

    public void Set(IEnumerable<object> items)
    {
        _items.Clear();
        _items.UnionWith(items);
        Changed?.Invoke();
    }

    public void Add(IEnumerable<object> items)
    {
        _items.UnionWith(items);
        Changed?.Invoke();
    }

    public void Remove(IEnumerable<object> items)
    {
        _items.ExceptWith(items);
        Changed?.Invoke();
    }

    /// <summary>Shift-click: each item flips in or out of the selection.</summary>
    public void Toggle(IEnumerable<object> items)
    {
        foreach (var item in items)
        {
            if (!_items.Remove(item))
                _items.Add(item);
        }
        Changed?.Invoke();
    }
}

/// <summary>
/// The group/component being edited (SketchUp's active path). Empty path = the model's top level. Selection and
/// new geometry go into <see cref="Entities"/>.
/// </summary>
public sealed class EditContext(Model model)
{
    private readonly List<ComponentInstance> _path = [];

    public event Action? Changed;

    public IReadOnlyList<ComponentInstance> Path => _path;
    public Entities Entities => _path.Count == 0 ? model.Entities : _path[^1].Definition.Entities;

    /// <summary>Local-to-world transform of the active context.</summary>
    public Geometry.Transform ToWorld => _path.Aggregate(Geometry.Transform.Identity, (acc, inst) => inst.Transform.Then(acc));

    public void Enter(ComponentInstance instance)
    {
        _path.Add(instance);
        Changed?.Invoke();
    }

    /// <summary>Close Group/Component: step out one level. Returns false at the top level.</summary>
    public bool Exit()
    {
        if (_path.Count == 0)
            return false;
        _path.RemoveAt(_path.Count - 1);
        Changed?.Invoke();
        return true;
    }

    public void Reset()
    {
        _path.Clear();
        Changed?.Invoke();
    }
}

/// <summary>Connectivity queries SketchUp's selection gestures use.</summary>
public static class Topology
{
    /// <summary>Faces that use <paramref name="edge"/> in any of their loops.</summary>
    public static IEnumerable<Face> FacesOf(Entities e, Edge edge) =>
        e.Faces.Where(f => f.Loops.Any(l => l.Edges.Any(x => x.Edge == edge)));

    public static IEnumerable<Edge> EdgesOf(Face face) => face.Loops.SelectMany(l => l.Edges.Select(x => x.Edge)).Distinct();

    /// <summary>Double-click: a face with its edges, or an edge with the faces that share it.</summary>
    public static IEnumerable<object> DoubleClickSet(Entities e, object item) => item switch
    {
        Face f => EdgesOf(f).Cast<object>().Prepend(f),
        Edge edge => FacesOf(e, edge).Cast<object>().Prepend(edge),
        _ => [item],
    };

    /// <summary>Triple-click: everything connected through shared vertices.</summary>
    public static IEnumerable<object> Connected(Entities e, object item)
    {
        var edgesAt = new Dictionary<Vertex, List<Edge>>();
        foreach (var edge in e.Edges)
        {
            foreach (var v in new[] { edge.Start, edge.End })
            {
                if (!edgesAt.TryGetValue(v, out var list))
                    edgesAt[v] = list = [];
                list.Add(edge);
            }
        }
        var facesAt = new Dictionary<Edge, List<Face>>();
        foreach (var f in e.Faces)
        {
            foreach (var edge in EdgesOf(f))
            {
                if (!facesAt.TryGetValue(edge, out var list))
                    facesAt[edge] = list = [];
                list.Add(f);
            }
        }

        var seenEdges = new HashSet<Edge>();
        var seenFaces = new HashSet<Face>();
        var queue = new Queue<Edge>(item switch
        {
            Edge edge => [edge],
            Face f => EdgesOf(f),
            _ => [],
        });
        if (item is Face face)
            seenFaces.Add(face);
        while (queue.Count > 0)
        {
            var edge = queue.Dequeue();
            if (!seenEdges.Add(edge))
                continue;
            foreach (var f in facesAt.GetValueOrDefault(edge) ?? [])
                seenFaces.Add(f);
            foreach (var v in new[] { edge.Start, edge.End })
            {
                foreach (var next in edgesAt.GetValueOrDefault(v) ?? [])
                {
                    if (!seenEdges.Contains(next))
                        queue.Enqueue(next);
                }
            }
        }
        return seenFaces.Cast<object>().Concat(seenEdges);
    }
}
