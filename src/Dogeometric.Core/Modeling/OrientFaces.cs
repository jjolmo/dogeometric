namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Orient Faces: the faces connected to <c>face</c> turn to face the same way it does. Across each edge
/// two faces agree when they run it in opposite directions, as a closed shell's faces do.
/// </summary>
public static class OrientFaces
{
    /// <summary>Reverses the connected faces that disagree with <paramref name="face"/>; returns how many it turned.</summary>
    public static int Apply(Entities e, Face face)
    {
        var byEdge = new Dictionary<Edge, List<Face>>();
        foreach (var f in e.Faces)
            foreach (var (edge, _) in f.Loops.SelectMany(l => l.Edges))
            {
                if (!byEdge.TryGetValue(edge, out var list))
                    byEdge[edge] = list = [];
                if (!list.Contains(f))
                    list.Add(f);
            }
        bool Runs(Face f, Edge edge) => f.Loops.SelectMany(l => l.Edges).First(x => x.Edge == edge).Reversed;

        var done = new HashSet<Face> { face };
        var queue = new Queue<Face>([face]);
        var turned = 0;
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var (edge, _) in f.Loops.SelectMany(l => l.Edges))
            {
                // Only edges shared by exactly two faces tell which way the other should face.
                if (byEdge[edge] is not [var a, var b])
                    continue;
                var other = a == f ? b : a;
                if (!done.Add(other))
                    continue;
                if (Runs(other, edge) == Runs(f, edge))
                {
                    FaceFinder.Reverse(other);
                    turned++;
                }
                queue.Enqueue(other);
            }
        }
        return turned;
    }
}
