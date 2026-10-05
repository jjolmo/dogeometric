using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;

namespace Dogeometric.Core.Inference;

public enum InferenceKind
{
    None,
    Endpoint,
    Midpoint,
    OnEdge,
    OnFace,
    OnAxis,
    OnGuide,
    GuidePoint,
    Origin,
    InPlane,
    Center,
}

/// <summary>The snapped point and what it snapped to (drives the cursor marker colour and tooltip).</summary>
public sealed record InferenceResult(Vec3 Point, InferenceKind Kind, string Label, Vec3? AxisFrom = null, Vec3? AxisDirection = null)
{
    public Face? Face { get; init; }
    public Edge? Edge { get; init; }

    /// <summary>World transform of the entity hit (faces/edges inside groups).</summary>
    public Transform EntityToWorld { get; init; } = Transform.Identity;

    /// <summary>The point is on geometry inside a group or component other than the one being edited.</summary>
    public bool InGroup { get; init; }
}

/// <summary>The view, as the inference engine needs it: rays through pixels and pixels of model points.</summary>
public interface IViewProjection
{
    Ray RayAt(double x, double y);
    (double X, double Y)? ToScreen(Vec3 world);
    PickHit? Pick(double x, double y);

    /// <summary>Direction of view (from the eye into the scene).</summary>
    Vec3 ViewDirection { get; }
}

/// <summary>
/// SketchUp's inference engine: snaps the cursor to endpoints, midpoints, edges, faces and the drawing axes from
/// the previous point. Arrow keys lock an axis (→ red, ← green, ↑ blue).
/// </summary>
public sealed class InferenceEngine
{
    public const double SnapPixels = 8;

    /// <summary>The drawing axes (the model's, see Model.Axes).</summary>
    public Transform Axes { get; set; } = Transform.Identity;

    private (Vec3 Dir, string Name)[] AxisDirections =>
        [(Axes.X.Normalized(), "Red"), (Axes.Y.Normalized(), "Green"), (Axes.Z.Normalized(), "Blue")];

    /// <summary>Centre points to snap to (View › Center Points); empty when they are off.</summary>
    public IReadOnlyList<CenterPoint> Centers { get; set; } = [];

    /// <summary>Locked axis (arrow keys), or null.</summary>
    public Vec3? LockedAxis { get; set; }

    /// <summary>Locked inference (Shift): the line the point must stay on.</summary>
    public (Vec3 From, Vec3 Dir, string Label)? LockedLine { get; set; }

    public InferenceResult Infer(IViewProjection view, double x, double y, Vec3? from, Entities context, Transform contextToWorld)
    {
        var ray = view.RayAt(x, y);

        // A locked axis or Shift-locked inference constrains the point to a line from the previous point.
        if (from is { } f0 && (LockedAxis is { } || LockedLine is { }))
        {
            var (origin, dir, label) = LockedLine is { } l ? l : (f0, LockedAxis!.Value, AxisName(LockedAxis!.Value));
            var p = ClosestOnLine(ray, origin, dir);
            return new InferenceResult(p, InferenceKind.OnAxis, label, origin, dir);
        }

        var hit = view.Pick(x, y);
        var snap = SnapToPoints(view, x, y, hit, context, contextToWorld, Axes.Origin, Centers);
        if (snap != null)
            return snap;

        if (from is { } start && AxisInference(view, ray, x, y, start) is { } axis)
            return axis;

        if (OnGuide(view, ray, x, y, context, contextToWorld) is { } guide && (hit == null || guide.Point.DistanceTo(ray.Origin) <= hit.Distance + 1))
            return guide;

        if (hit != null)
        {
            var toWorld = hit.Path.Aggregate(Transform.Identity, (acc, inst) => inst.Transform.Then(acc));
            var inside = InsideSuffix(hit, context);
            return hit.Entity switch
            {
                Edge e => new InferenceResult(hit.Point, InferenceKind.OnEdge, "On Edge" + inside) { Edge = e, EntityToWorld = toWorld, InGroup = inside != "" },
                Face f => new InferenceResult(hit.Point, InferenceKind.OnFace, "On Face" + inside) { Face = f, EntityToWorld = toWorld, InGroup = inside != "" },
                _ => new InferenceResult(hit.Point, InferenceKind.None, ""),
            };
        }

        // Nothing under the cursor: from a previous point, stay in the axis plane most facing the viewer;
        // otherwise land on the ground.
        if (from is { } s)
        {
            var normal = MostFacing(view.ViewDirection, Axes);
            if (IntersectPlane(ray, normal, s) is { } q)
                return new InferenceResult(q, InferenceKind.InPlane, "");
        }
        if (IntersectPlane(ray, Axes.Z.Normalized(), Axes.Origin) is { } g)
            return new InferenceResult(g, InferenceKind.InPlane, "");
        return new InferenceResult(ray.At(1000), InferenceKind.None, "");
    }

    /// <summary>SketchUp's tooltip suffix for geometry inside another group or component ("Endpoint in Group").</summary>
    private static string InsideSuffix(PickHit hit, Entities context)
    {
        var mine = hit.Entity switch
        {
            Edge e => context.Edges.Contains(e),
            Face f => context.Faces.Contains(f),
            _ => true,
        };
        if (mine || hit.Path.Count == 0)
            return "";
        return hit.Path[^1].IsGroup ? " in Group" : " in Component";
    }

    /// <summary>Endpoints, midpoints and the origin within <see cref="SnapPixels"/> of the cursor.</summary>
    private static InferenceResult? SnapToPoints(IViewProjection view, double x, double y, PickHit? hit, Entities context, Transform contextToWorld, Vec3 origin,
        IReadOnlyList<CenterPoint> centers)
    {
        InferenceResult? best = null;
        var bestDist = SnapPixels;

        void Consider(Vec3 world, InferenceKind kind, string label, Edge? edge = null, string inside = "")
        {
            if (view.ToScreen(world) is not { } s)
                return;
            var d = Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));
            // Endpoints beat midpoints and centres at equal distance.
            if (d < bestDist || (d <= bestDist + 0.5 && kind == InferenceKind.Endpoint && best?.Kind is InferenceKind.Midpoint or InferenceKind.Center))
            {
                bestDist = d;
                best = new InferenceResult(world, kind, label + inside) { Edge = edge, InGroup = inside != "" };
            }
        }

        Consider(origin, InferenceKind.Origin, "Origin");
        foreach (var c in centers)
            Consider(c.Point, InferenceKind.Center, c.Label);

        // Inside an arc or circle only its ends are points to snap to, as in SketchUp: not each segment's ends and middle.
        var curveVertices = new Dictionary<(Curve, Vertex), int>();
        foreach (var e in context.Edges)
            if (e.Curve is { IsPolygon: false } c)
                foreach (var v in new[] { e.Start, e.End })
                    curveVertices[(c, v)] = curveVertices.GetValueOrDefault((c, v)) + 1;
        bool CurveEnd(Edge e, Vertex v) => e.Curve is not { IsPolygon: false } c || curveVertices.GetValueOrDefault((c, v)) < 2;

        void FromEdge(Edge e, Transform xf, string inside = "")
        {
            if (CurveEnd(e, e.Start))
                Consider(xf.ApplyPoint(e.Start.Position), InferenceKind.Endpoint, "Endpoint", e, inside);
            if (CurveEnd(e, e.End))
                Consider(xf.ApplyPoint(e.End.Position), InferenceKind.Endpoint, "Endpoint", e, inside);
            if (e.Curve is not { IsPolygon: false })
                Consider(xf.ApplyPoint((e.Start.Position + e.End.Position) * 0.5), InferenceKind.Midpoint, "Midpoint", e, inside);
        }

        foreach (var e in context.Edges)
        {
            if ((e.Flags & EdgeFlags.Hidden) != 0)
                continue;
            FromEdge(e, contextToWorld);
        }
        foreach (var g in context.GuidePoints)
            Consider(contextToWorld.ApplyPoint(g.Position), InferenceKind.GuidePoint, "Guide Point");
        foreach (var g in context.GuideLines.Where(g => !g.IsInfinite))
        {
            Consider(contextToWorld.ApplyPoint(g.Start!.Value), InferenceKind.Endpoint, "Endpoint");
            Consider(contextToWorld.ApplyPoint(g.End!.Value), InferenceKind.Endpoint, "Endpoint");
        }

        // Geometry under the cursor in other contexts (inside groups) snaps too.
        if (hit != null)
        {
            var toWorld = hit.Path.Aggregate(Transform.Identity, (acc, inst) => inst.Transform.Then(acc));
            var inside = InsideSuffix(hit, context);
            if (hit.Edge is { } he)
                FromEdge(he, toWorld, inside);
            if (hit.Face is { } hf)
            {
                foreach (var e in Topology.EdgesOf(hf))
                    FromEdge(e, toWorld, inside);
                if (centers.Count > 0 && inside != "")
                    Consider(toWorld.ApplyPoint(CenterPoints.FaceCenter(hf)), InferenceKind.Center, "Center of Face", null, inside);
            }
        }
        return best;
    }

    /// <summary>The red/green/blue axis through <paramref name="from"/> whose screen direction the cursor follows.</summary>
    private InferenceResult? AxisInference(IViewProjection view, Ray ray, double x, double y, Vec3 from)
    {
        if (view.ToScreen(from) is null)
            return null;
        InferenceResult? best = null;
        var bestDist = SnapPixels;
        foreach (var (dir, name) in AxisDirections)
        {
            var p = ClosestOnLine(ray, from, dir);
            if (view.ToScreen(p) is not { } s || p.DistanceTo(from) < Tolerance.Length)
                continue;
            // Distance from the cursor to the axis line on screen.
            var d = Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));
            if (d < bestDist)
            {
                bestDist = d;
                best = new InferenceResult(p, InferenceKind.OnAxis, $"On {name} Axis", from, dir);
            }
        }
        return best;
    }

    /// <summary>Closest point on a guide line within <see cref="SnapPixels"/> of the cursor.</summary>
    private static InferenceResult? OnGuide(IViewProjection view, Ray ray, double x, double y, Entities context, Transform toWorld)
    {
        InferenceResult? best = null;
        var bestDist = SnapPixels;
        foreach (var g in context.GuideLines)
        {
            var p = ClosestOnLine(ray, toWorld.ApplyPoint(g.Point), toWorld.ApplyVector(g.Direction));
            if (!g.IsInfinite)
            {
                // Bounded guides: clamp to the segment.
                var s = toWorld.ApplyPoint(g.Start!.Value);
                var e = toWorld.ApplyPoint(g.End!.Value);
                var t = Math.Clamp((p - s).Dot(e - s) / Math.Max((e - s).LengthSquared, 1e-12), 0, 1);
                p = s + (e - s) * t;
            }
            if (view.ToScreen(p) is not { } sp)
                continue;
            var d = Math.Sqrt((sp.X - x) * (sp.X - x) + (sp.Y - y) * (sp.Y - y));
            if (d < bestDist)
            {
                bestDist = d;
                best = new InferenceResult(p, InferenceKind.OnGuide, "On Guide");
            }
        }
        return best;
    }

    public string AxisName(Vec3 dir) => AxisName(dir, Axes);

    public static string AxisName(Vec3 dir, Transform axes) =>
        Math.Abs(dir.Dot(axes.X.Normalized())) > 0.99 ? "On Red Axis"
        : Math.Abs(dir.Dot(axes.Y.Normalized())) > 0.99 ? "On Green Axis"
        : Math.Abs(dir.Dot(axes.Z.Normalized())) > 0.99 ? "On Blue Axis"
        : "Parallel to Edge";

    /// <summary>Point on the line (origin, dir) closest to the ray.</summary>
    public static Vec3 ClosestOnLine(Ray ray, Vec3 origin, Vec3 dir)
    {
        var d1 = ray.Direction;
        var d2 = dir.Normalized();
        var r = ray.Origin - origin;
        double a = d1.Dot(d1), b = d1.Dot(d2), c = d1.Dot(r), f = d2.Dot(r);
        var denom = a - b * b;
        if (Math.Abs(denom) < 1e-12)
            return origin;
        var s = (a * f - b * c) / denom;
        return origin + d2 * s;
    }

    public static Vec3? IntersectPlane(Ray ray, Vec3 normal, Vec3 point)
    {
        var denom = ray.Direction.Dot(normal);
        if (Math.Abs(denom) < 1e-9)
            return null;
        var t = (point - ray.Origin).Dot(normal) / denom;
        return t > 0 ? ray.At(t) : null;
    }

    /// <summary>The red/green/blue plane normal most aligned with the view direction.</summary>
    public static Vec3 MostFacing(Vec3 viewDir) => MostFacing(viewDir, Transform.Identity);

    /// <summary>The plane normal among the given drawing axes most aligned with the view direction.</summary>
    public static Vec3 MostFacing(Vec3 viewDir, Transform axes)
    {
        var (x, y, z) = (axes.X.Normalized(), axes.Y.Normalized(), axes.Z.Normalized());
        var ax = Math.Abs(viewDir.Dot(x));
        var ay = Math.Abs(viewDir.Dot(y));
        var az = Math.Abs(viewDir.Dot(z));
        return az >= ax && az >= ay ? z : ax >= ay ? x : y;
    }
}
