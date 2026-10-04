using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Picking;

/// <summary>Bounding-volume hierarchy over the triangulated faces of one entity collection (local space).</summary>
internal sealed class Bvh
{
    private const int LeafSize = 8;

    private readonly struct Tri(Vec3 a, Vec3 b, Vec3 c, Face face)
    {
        public readonly Vec3 A = a, B = b, C = c;
        public readonly Face Face = face;
        public Vec3 Center => (A + B + C) / 3;
    }

    private sealed class Node
    {
        public Bounds3 Box;
        public Node? Left, Right;
        public int Start, Count;
    }

    private readonly Tri[] _tris;
    private readonly Node? _root;

    public Bounds3 Bounds => _root?.Box ?? Bounds3.Empty;

    public Bvh(Entities e)
    {
        var list = new List<Tri>();
        foreach (var f in e.Faces)
        {
            var outer = f.OuterLoop.Points.ToList();
            var holes = f.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
            var all = outer.Concat(holes.SelectMany(h => h)).ToArray();
            var idx = Polygon.Triangulate(outer, holes);
            for (var i = 0; i < idx.Count; i += 3)
                list.Add(new Tri(all[idx[i]], all[idx[i + 1]], all[idx[i + 2]], f));
        }
        _tris = list.ToArray();
        if (_tris.Length > 0)
            _root = Build(0, _tris.Length);
    }

    private Node Build(int start, int count)
    {
        var box = Bounds3.Empty;
        for (var i = start; i < start + count; i++)
            box = box.Include(_tris[i].A).Include(_tris[i].B).Include(_tris[i].C);
        var node = new Node { Box = box, Start = start, Count = count };
        if (count <= LeafSize)
            return node;

        // Split on the longest axis at the median centre.
        var size = box.Size;
        Func<Tri, double> key = size.X >= size.Y && size.X >= size.Z ? t => t.Center.X : size.Y >= size.Z ? t => t.Center.Y : t => t.Center.Z;
        Array.Sort(_tris, start, count, Comparer<Tri>.Create((a, b) => key(a).CompareTo(key(b))));
        var half = count / 2;
        node.Left = Build(start, half);
        node.Right = Build(start + half, count - half);
        node.Count = 0;
        return node;
    }

    public (Face Face, double T)? Raycast(Ray ray, double maxT, Func<object, bool>? visible)
    {
        if (_root == null)
            return null;
        (Face, double)? best = null;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!HitsBox(ray, n.Box, maxT))
                continue;
            if (n.Left == null)
            {
                for (var i = n.Start; i < n.Start + n.Count; i++)
                {
                    ref readonly var tri = ref _tris[i];
                    if (visible != null && !visible(tri.Face))
                        continue;
                    if (Intersect(ray, tri.A, tri.B, tri.C) is { } t && t < maxT)
                    {
                        maxT = t;
                        best = (tri.Face, t);
                    }
                }
                continue;
            }
            stack.Push(n.Left);
            stack.Push(n.Right!);
        }
        return best;
    }

    /// <summary>Möller–Trumbore, both sides (SketchUp faces are two-sided).</summary>
    private static double? Intersect(Ray ray, Vec3 a, Vec3 b, Vec3 c)
    {
        var e1 = b - a;
        var e2 = c - a;
        var p = ray.Direction.Cross(e2);
        var det = e1.Dot(p);
        if (Math.Abs(det) < 1e-15)
            return null;
        var inv = 1 / det;
        var s = ray.Origin - a;
        var u = s.Dot(p) * inv;
        if (u < 0 || u > 1)
            return null;
        var q = s.Cross(e1);
        var v = ray.Direction.Dot(q) * inv;
        if (v < 0 || u + v > 1)
            return null;
        var t = e2.Dot(q) * inv;
        return t > 1e-9 ? t : null;
    }

    private static bool HitsBox(Ray ray, Bounds3 b, double maxT)
    {
        double tmin = 0, tmax = maxT;
        for (var axis = 0; axis < 3; axis++)
        {
            double o = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
            double d = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
            double lo = axis == 0 ? b.Min.X : axis == 1 ? b.Min.Y : b.Min.Z;
            double hi = axis == 0 ? b.Max.X : axis == 1 ? b.Max.Y : b.Max.Z;
            if (Math.Abs(d) < 1e-15)
            {
                if (o < lo || o > hi)
                    return false;
                continue;
            }
            var t1 = (lo - o) / d;
            var t2 = (hi - o) / d;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax)
                return false;
        }
        return true;
    }
}
