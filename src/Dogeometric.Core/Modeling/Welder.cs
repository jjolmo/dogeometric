using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>Adds faces sharing vertices and edges by position, without scanning the whole collection each time.</summary>
public sealed class Welder
{
    private const double Cell = Tolerance.Length * 4;
    private readonly Entities _e;
    private readonly Dictionary<(long, long, long), List<Vertex>> _grid = [];
    private readonly Dictionary<(Vertex, Vertex), Edge> _edges = [];

    public Welder(Entities e)
    {
        _e = e;
        foreach (var v in e.Vertices)
            Add(v);
        foreach (var edge in e.Edges)
            _edges[Key(edge.Start, edge.End)] = edge;
    }

    private static (long, long, long) CellOf(Vec3 p) => ((long)Math.Floor(p.X / Cell), (long)Math.Floor(p.Y / Cell), (long)Math.Floor(p.Z / Cell));

    private void Add(Vertex v)
    {
        var key = CellOf(v.Position);
        if (!_grid.TryGetValue(key, out var list))
            _grid[key] = list = [];
        list.Add(v);
    }

    private static (Vertex, Vertex) Key(Vertex a, Vertex b) => a.GetHashCode() <= b.GetHashCode() ? (a, b) : (b, a);

    public Vertex VertexAt(Vec3 p)
    {
        var (cx, cy, cz) = CellOf(p);
        for (var x = cx - 1; x <= cx + 1; x++)
            for (var y = cy - 1; y <= cy + 1; y++)
                for (var z = cz - 1; z <= cz + 1; z++)
                    if (_grid.TryGetValue((x, y, z), out var list))
                        foreach (var v in list)
                            if (v.Position.DistanceTo(p) <= Tolerance.Length)
                                return v;
        var nv = _e.AddVertex(p);
        Add(nv);
        return nv;
    }

    public Edge EdgeBetween(Vertex a, Vertex b)
    {
        var key = Key(a, b);
        if (!_edges.TryGetValue(key, out var edge))
            _edges[key] = edge = _e.AddEdge(a, b);
        return edge;
    }

    public Face Face(IReadOnlyList<Vec3> outer, IReadOnlyList<List<Vec3>> holes)
    {
        var face = new Face();
        face.Loops.Add(Loop(outer));
        foreach (var h in holes)
            face.Loops.Add(Loop(h));
        _e.Faces.Add(face);
        return face;
    }

    private FaceLoop Loop(IReadOnlyList<Vec3> pts)
    {
        var loop = new FaceLoop();
        var verts = pts.Select(VertexAt).ToList();
        for (var i = 0; i < verts.Count; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % verts.Count];
            var edge = EdgeBetween(a, b);
            loop.Edges.Add((edge, edge.Start != a));
        }
        return loop;
    }
}
