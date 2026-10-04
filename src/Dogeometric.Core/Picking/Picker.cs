using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Picking;

public readonly record struct Ray(Vec3 Origin, Vec3 Direction)
{
    public Vec3 At(double t) => Origin + Direction * t;

    public Ray Transformed(Transform inverse) =>
        new(inverse.ApplyPoint(Origin), inverse.ApplyVector(Direction));
}

/// <summary>
/// What the cursor is over. <see cref="Path"/> is the chain of instances from the top level down to the entity, so
/// a click inside a group can select the group (SketchUp selects the outermost object of the active context).
/// </summary>
public sealed record PickHit(object Entity, IReadOnlyList<ComponentInstance> Path, Vec3 Point, double Distance)
{
    public Face? Face => Entity as Face;
    public Edge? Edge => Entity as Edge;
}

/// <summary>
/// Ray picking against faces and edges, through nested instances. Edges win over the face behind them when the
/// cursor is within the edge tolerance, as in SketchUp.
/// </summary>
public sealed class Picker
{
    private readonly Dictionary<Entities, Bvh> _bvhs = [];
    private readonly Dictionary<Entities, Bounds3> _nestedBounds = [];

    /// <summary>Forget cached acceleration data (call after the geometry changes).</summary>
    public void Invalidate(Entities? entities = null)
    {
        // Nested bounds depend on children, so any change clears them all.
        _nestedBounds.Clear();
        if (entities == null)
            _bvhs.Clear();
        else
            _bvhs.Remove(entities);
    }

    /// <param name="edgeTolerance">World-space edge pick radius at distance t along the ray.</param>
    public PickHit? Pick(Entities root, Ray ray, Func<double, double> edgeTolerance, Func<object, bool>? visible = null)
    {
        PickHit? best = null;
        Walk(root, Transform.Identity, [], ray, edgeTolerance, visible, ref best);
        return best;
    }

    private void Walk(Entities entities, Transform toWorld, List<ComponentInstance> path, Ray worldRay,
        Func<double, double> edgeTolerance, Func<object, bool>? visible, ref PickHit? best)
    {
        var inverse = toWorld.Inverse();
        var local = worldRay.Transformed(inverse);
        // Parameter t is shared between world and local rays because the local direction is not renormalised.
        var bvh = BvhFor(entities);

        // Inside a group or component, a face level with one already hit wins the tie (a component glued flat on a wall).
        var faceLimit = best?.Distance is { } d ? path.Count > 0 ? d + Math.Max(1e-6, d * 1e-9) : d : double.PositiveInfinity;
        if (bvh.Raycast(local, faceLimit, visible) is { } faceHit)
        {
            best = new PickHit(faceHit.Face, path.ToArray(), toWorld.ApplyPoint(local.At(faceHit.T)), faceHit.T);
        }

        foreach (var edge in entities.Edges)
        {
            if (visible != null && !visible(edge))
                continue;
            var a = toWorld.ApplyPoint(edge.Start.Position);
            var b = toWorld.ApplyPoint(edge.End.Position);
            var (t, distance, point) = RaySegment(worldRay, a, b);
            if (t <= 0)
                continue;
            // An edge on a face is the same distance away as the face; let it win within its tolerance.
            var limit = best?.Distance ?? double.PositiveInfinity;
            if (distance <= edgeTolerance(t) && t <= limit + edgeTolerance(t) * 2)
            {
                if (best?.Entity is Edge && t >= best.Distance)
                    continue;
                best = new PickHit(edge, path.ToArray(), point, t);
            }
        }

        foreach (var inst in entities.Instances)
        {
            if (visible != null && !visible(inst))
                continue;
            var childToWorld = inst.Transform.Then(toWorld);
            var nestedBounds = NestedBounds(inst.Definition.Entities);
            if (nestedBounds.IsEmpty)
                continue;
            // Inflate by the edge tolerance at the far end of the box so edges on the boundary stay pickable.
            var childRay = worldRay.Transformed(childToWorld.Inverse());
            var margin = edgeTolerance((nestedBounds.Center - childRay.Origin).Length + nestedBounds.Diagonal) * 2;
            if (!RayHitsBox(childRay, Inflate(nestedBounds, margin), best?.Distance))
                continue;
            path.Add(inst);
            Walk(inst.Definition.Entities, childToWorld, path, worldRay, edgeTolerance, visible, ref best);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>Entities whose geometry lies inside (window) or touches (crossing) a screen rectangle.</summary>
    public IEnumerable<object> InRectangle(Entities context, Func<Vec3, (double X, double Y)?> project,
        (double X, double Y) min, (double X, double Y) max, bool crossing)
    {
        bool Inside((double X, double Y)? p) => p is { } q && q.X >= min.X && q.X <= max.X && q.Y >= min.Y && q.Y <= max.Y;

        bool Matches(IEnumerable<Vec3> points, IEnumerable<(Vec3, Vec3)> segments)
        {
            var projected = points.Select(project).ToList();
            if (projected.Count == 0)
                return false;
            if (!crossing)
                return projected.All(Inside);
            if (projected.Any(Inside))
                return true;
            return segments.Any(s => SegmentCrossesRect(project(s.Item1), project(s.Item2), min, max));
        }

        foreach (var e in context.Edges)
        {
            if (Matches([e.Start.Position, e.End.Position], [(e.Start.Position, e.End.Position)]))
                yield return e;
        }
        foreach (var f in context.Faces)
        {
            var pts = f.OuterLoop.Points.ToList();
            var segs = pts.Select((p, i) => (p, pts[(i + 1) % pts.Count]));
            if (Matches(pts, segs))
                yield return f;
        }
        foreach (var inst in context.Instances)
        {
            var b = inst.Definition.Entities.Bounds();
            if (b.IsEmpty)
                continue;
            var corners = Corners(b).Select(inst.Transform.ApplyPoint).ToList();
            if (Matches(corners, BoxEdges(corners)))
                yield return inst;
        }
    }

    private Bvh BvhFor(Entities e)
    {
        if (!_bvhs.TryGetValue(e, out var bvh))
            _bvhs[e] = bvh = new Bvh(e);
        return bvh;
    }

    /// <summary>Closest approach between a ray and a segment: ray parameter, distance, closest point on the segment.</summary>
    public static (double T, double Distance, Vec3 Point) RaySegment(Ray ray, Vec3 a, Vec3 b)
    {
        var d1 = ray.Direction;
        var d2 = b - a;
        var r = ray.Origin - a;
        double aa = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), c = d1.Dot(r), bb = d1.Dot(d2);
        var denom = aa * e - bb * bb;
        double s;
        if (e < 1e-18)
            s = 0;
        else if (Math.Abs(denom) < 1e-18)
            s = Math.Clamp(f / e, 0, 1);
        else
            s = Math.Clamp((aa * f - bb * c) / denom, 0, 1);
        var onSeg = a + d2 * s;
        var t = (onSeg - ray.Origin).Dot(d1) / aa;
        var onRay = ray.At(t);
        return (t, onRay.DistanceTo(onSeg), onSeg);
    }

    private static bool RayHitsBox(Ray ray, Bounds3 b, double? maxT)
    {
        double tmin = 0, tmax = maxT ?? double.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
            var d = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
            var lo = axis == 0 ? b.Min.X : axis == 1 ? b.Min.Y : b.Min.Z;
            var hi = axis == 0 ? b.Max.X : axis == 1 ? b.Max.Y : b.Max.Z;
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

    private Bounds3 NestedBounds(Entities e)
    {
        if (!_nestedBounds.TryGetValue(e, out var b))
            _nestedBounds[e] = b = e.Bounds();
        return b;
    }

    private static Bounds3 Inflate(Bounds3 b, double by) => new(b.Min - new Vec3(by, by, by), b.Max + new Vec3(by, by, by));

    private static List<Vec3> Corners(Bounds3 b) =>
        Enumerable.Range(0, 8).Select(i => new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z)).ToList();

    private static IEnumerable<(Vec3, Vec3)> BoxEdges(List<Vec3> c)
    {
        int[][] pairs = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
        return pairs.Select(p => (c[p[0]], c[p[1]]));
    }

    private static bool SegmentCrossesRect((double X, double Y)? a, (double X, double Y)? b, (double X, double Y) min, (double X, double Y) max)
    {
        if (a is not { } p || b is not { } q)
            return false;
        (double, double)[] corners = [(min.X, min.Y), (max.X, min.Y), (max.X, max.Y), (min.X, max.Y)];
        for (var i = 0; i < 4; i++)
        {
            if (SegmentsIntersect(p, q, corners[i], corners[(i + 1) % 4]))
                return true;
        }
        return false;
    }

    private static bool SegmentsIntersect((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q1, (double X, double Y) q2)
    {
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var d1 = Cross(q1, q2, p1);
        var d2 = Cross(q1, q2, p2);
        var d3 = Cross(p1, p2, q1);
        var d4 = Cross(p1, p2, q2);
        return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
    }
}
