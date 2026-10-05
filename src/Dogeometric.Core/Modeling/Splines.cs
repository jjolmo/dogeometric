using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>BezierSpline's curve families (Draw › BezierSpline curves).</summary>
public enum SplineKind
{
    ClassicBezier, Polyline, CubicBezier, UniformBSpline, CatmullSpline, FSpline, Courbette,
    ArcCorners, Chamfer, DogBone, Divider, Segmentor, TBone, DividerAnimation,
}

/// <summary>Polyline Divider for Animation's step modes: equal steps (the shortest or the longest), speeding up,
/// slowing down, or both ways.</summary>
public enum AnimationSteps { EqualMinimum, EqualMaximum, Accelerate, Decelerate, AccelerateDecelerate, DecelerateAccelerate }

/// <summary>
/// The curves of Fredo6's BezierSpline, computed from their control points. Precision is what each family's
/// "Ns" means: segments of the whole curve, segments per span, or segments of a full circle for rounded corners.
/// </summary>
public static class Splines
{
    public sealed record Family(string Menu, int MinPoints, int PrecisionMin, int PrecisionMax, int PrecisionDefault, string? Parameter);

    public static Family Info(SplineKind kind) => kind switch
    {
        SplineKind.ClassicBezier => new("Classic Bezier curve", 2, 1, 200, 20, null),
        SplineKind.Polyline => new("Polyline", 2, 1, 1, 1, null),
        SplineKind.CubicBezier => new("Cubic Bezier curve", 2, 1, 20, 7, null),
        SplineKind.UniformBSpline => new("Uniform B-Spline", 3, 15, 400, 30, null),
        SplineKind.CatmullSpline => new("Catmull Spline", 2, 2, 30, 7, null),
        SplineKind.FSpline => new("F-Spline", 2, 7, 400, 30, null),
        SplineKind.Courbette => new("Courbette", 2, 1, 90, 24, null),
        SplineKind.ArcCorners => new("Polyline Arc Corners", 2, 1, 120, 24, "Offset"),
        SplineKind.Chamfer => new("Polyline Chamfer", 2, 1, 1, 1, "Offset"),
        SplineKind.DogBone => new("Polyline Dog-Bone Corners", 2, 1, 120, 24, "Radius"),
        SplineKind.Divider => new("Polyline Divider", 2, 1, 1, 1, "Length"),
        SplineKind.Segmentor => new("Polyline Segmentor", 2, 1, 1, 1, "Segments"),
        SplineKind.TBone => new("Polyline T-Bone Corners", 2, 1, 120, 24, "Radius"),
        SplineKind.DividerAnimation => new("Polyline Divider for Animation", 2, 1, 1, 1, "Min step"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The curve's points. <paramref name="parameter"/> is the family's extra value (offset, radius, length, count).</summary>
    public static List<Vec3> Compute(SplineKind kind, IReadOnlyList<Vec3> pts, int precision, double parameter, bool closed)
    {
        if (pts.Count < 2)
            return [.. pts];
        var p = closed ? pts.Append(pts[0]).ToList() : pts.ToList();
        return kind switch
        {
            SplineKind.ClassicBezier => Bezier(p, Math.Max(1, precision)),
            SplineKind.Polyline => p,
            SplineKind.CubicBezier => Cubic(p, Math.Max(1, precision), closed),
            SplineKind.CatmullSpline => Catmull(p, Math.Max(1, precision), closed),
            SplineKind.UniformBSpline => BSpline(p, Math.Max(2, precision)),
            SplineKind.FSpline => Fit(p, Math.Max(2, precision)),
            SplineKind.Courbette => Courbette(p, Math.Max(1, precision)),
            SplineKind.ArcCorners => Corners(p, closed, parameter, precision, Fillet),
            SplineKind.Chamfer => Corners(p, closed, parameter, precision, ChamferCorner),
            SplineKind.DogBone => Corners(p, closed, parameter, precision, DogBoneCorner),
            SplineKind.TBone => Corners(p, closed, parameter, precision, TBoneCorner),
            SplineKind.Divider => Resample(p, parameter > Tolerance.Length ? Math.Max(1, (int)Math.Round(Length(p) / parameter)) : 1),
            SplineKind.Segmentor => Resample(p, Math.Max(1, (int)Math.Round(parameter))),
            _ => p,
        };
    }

    /// <summary>
    /// Polyline Divider for Animation (BezierSpline): the polyline cut into steps between <paramref name="min"/> and
    /// <paramref name="max"/>, growing or shrinking evenly by <paramref name="mode"/>, so a camera moving one step per
    /// frame along it speeds up or slows down.
    /// </summary>
    public static List<Vec3> DivideForAnimation(IReadOnlyList<Vec3> pts, AnimationSteps mode, double min, double max)
    {
        var steps = AnimationStepLengths(Length(pts), mode, min, max);
        if (steps.Count == 0 || pts.Count < 2)
            return [.. pts];
        var result = new List<Vec3> { pts[0] };
        var (i, k, step, at) = (0, 0, steps[0], pts[0]);
        while (true)
        {
            if (i + 1 >= pts.Count || k >= steps.Count)
            {
                // The last piece joins the previous one when it is much shorter than a step.
                if (result[^1].DistanceTo(pts[^1]) < steps[^1] * 0.4 && result.Count > 1)
                    result[^1] = pts[^1];
                else
                    result.Add(pts[^1]);
                return result;
            }
            var next = pts[i + 1];
            var d = at.DistanceTo(next);
            if (d >= step)
            {
                at += (next - pts[i]).Normalized() * step;
                result.Add(at);
                if (Math.Abs(d - step) < 1e-12)
                    i++;
                step = ++k < steps.Count ? steps[k] : 0;
            }
            else
            {
                at = next;
                step -= d;
                i++;
            }
        }
    }

    /// <summary>The step lengths along a curve of <paramref name="length"/>, as BezierSpline computes them.</summary>
    public static List<double> AnimationStepLengths(double length, AnimationSteps mode, double min, double max)
    {
        var steps = new List<double>();
        var (first, last) = mode is AnimationSteps.EqualMaximum or AnimationSteps.Decelerate or AnimationSteps.DecelerateAccelerate ? (max, min) : (min, max);
        if (mode is AnimationSteps.EqualMinimum or AnimationSteps.EqualMaximum || Math.Abs(max - min) < 1e-12)
        {
            if (first <= 0)
                return steps;
            var count = Math.Max(1, (int)Math.Round(length / first));
            steps.AddRange(Enumerable.Repeat(length / count, count));
            return steps;
        }
        void Ramp(double span, double from, double to)
        {
            var accel = (max * max - min * min) / 2 / span;
            var count = (int)Math.Round((max - min) / accel);
            if (count < 2)
                return;
            // Steps growing evenly from the first, adjusted so they add up to the span exactly.
            var grow = (span - count * from) / (count * (count - 1) / 2.0);
            for (var i = 0; i < count; i++)
                steps.Add(from + grow * i);
        }
        if (mode is AnimationSteps.Accelerate or AnimationSteps.Decelerate)
            Ramp(length, first, last);
        else
        {
            Ramp(length / 2, first, last);
            Ramp(length / 2, last, first);
        }
        return steps;
    }

    /// <summary>The curve's points for its family and settings (closed curves repeat their first point at the end).</summary>
    public static List<Vec3> Points(SplineData data) => data.Kind == SplineKind.DividerAnimation
        ? DivideForAnimation(data.Closed && data.ControlPoints.Count > 2 ? [.. data.ControlPoints, data.ControlPoints[0]] : data.ControlPoints, data.Mode, data.Parameter, data.Maximum)
        : Compute(data.Kind, data.ControlPoints, data.Precision, data.Parameter, data.Closed && data.ControlPoints.Count > 2);

    /// <summary>Draws a BezierSpline curve as one curve entity; closed curves drop their repeated end point.</summary>
    public static List<Edge> Draw(Entities e, SplineData data)
    {
        var pts = Points(data);
        var closed = pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < Tolerance.Length;
        if (closed)
            pts.RemoveAt(pts.Count - 1);
        closed |= data.LineClosed && pts.Count > 2;
        var curve = data.Kind == SplineKind.Polyline ? null : new Curve { Segments = pts.Count, Spline = data };
        return StickyGeometry.DrawEdges(e, pts, closed, null, curve);
    }

    /// <summary>BezierSpline's Edit: the curve's edges are redrawn from <paramref name="data"/> (new control points or settings).</summary>
    public static List<Edge> Edit(Entities e, Curve curve, SplineData data)
    {
        Editing.Erase(e, e.Edges.Where(x => x.Curve == curve).Cast<object>().ToList());
        return Draw(e, data);
    }

    // ------------------------------------------------------------------ smooth curves

    /// <summary>One Bézier curve of degree n-1 through the ends, pulled by the others (de Casteljau).</summary>
    public static List<Vec3> Bezier(IReadOnlyList<Vec3> p, int segments)
    {
        var result = new List<Vec3>();
        for (var i = 0; i <= segments; i++)
        {
            var t = (double)i / segments;
            var q = p.ToArray();
            for (var k = q.Length - 1; k > 0; k--)
                for (var j = 0; j < k; j++)
                    q[j] = q[j] * (1 - t) + q[j + 1] * t;
            result.Add(q[0]);
        }
        return result;
    }

    /// <summary>Cubic pieces through every point, tangents from the neighbours (control points lie on the curve).</summary>
    private static List<Vec3> Cubic(List<Vec3> p, int perSpan, bool closed)
    {
        Vec3 Tangent(int i)
        {
            if (closed && (i == 0 || i == p.Count - 1))
                return (p[1] - p[^2]) / 2;
            if (i == 0)
                return p[1] - p[0];
            if (i == p.Count - 1)
                return p[^1] - p[^2];
            return (p[i + 1] - p[i - 1]) / 2;
        }
        var result = new List<Vec3> { p[0] };
        for (var i = 0; i < p.Count - 1; i++)
        {
            Vec3 a = p[i], b = p[i] + Tangent(i) / 3, c = p[i + 1] - Tangent(i + 1) / 3, d = p[i + 1];
            for (var s = 1; s <= perSpan; s++)
            {
                var t = (double)s / perSpan;
                var u = 1 - t;
                result.Add(a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t));
            }
        }
        return result;
    }

    /// <summary>Catmull-Rom through every point.</summary>
    private static List<Vec3> Catmull(List<Vec3> p, int perSpan, bool closed)
    {
        Vec3 At(int i) => i < 0 ? (closed ? p[^2] : p[0] * 2 - p[1]) : i >= p.Count ? (closed ? p[1] : p[^1] * 2 - p[^2]) : p[i];
        var result = new List<Vec3> { p[0] };
        for (var i = 0; i < p.Count - 1; i++)
        {
            Vec3 p0 = At(i - 1), p1 = p[i], p2 = p[i + 1], p3 = At(i + 2);
            for (var s = 1; s <= perSpan; s++)
            {
                var t = (double)s / perSpan;
                result.Add((p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * (t * t) + (p1 * 3 - p0 - p2 * 3 + p3) * (t * t * t)) * 0.5);
            }
        }
        return result;
    }

    /// <summary>Clamped uniform cubic B-spline: starts and ends on the end points, pulled towards the others.</summary>
    private static List<Vec3> BSpline(List<Vec3> p, int segments)
    {
        var n = p.Count;
        var degree = Math.Min(3, n - 1);
        var knots = new List<double>();
        for (var i = 0; i <= degree; i++)
            knots.Add(0);
        for (var i = 1; i < n - degree; i++)
            knots.Add(i);
        for (var i = 0; i <= degree; i++)
            knots.Add(n - degree);
        var result = new List<Vec3>();
        var end = n - degree;
        for (var s = 0; s <= segments; s++)
            result.Add(DeBoor(p, knots, degree, end * (double)s / segments));
        return result;
    }

    private static Vec3 DeBoor(List<Vec3> p, List<double> knots, int degree, double t)
    {
        var k = degree;
        while (k < p.Count - 1 && t >= knots[k + 1])
            k++;
        var d = new Vec3[degree + 1];
        for (var j = 0; j <= degree; j++)
            d[j] = p[j + k - degree];
        for (var r = 1; r <= degree; r++)
            for (var j = degree; j >= r; j--)
            {
                var i = j + k - degree;
                var den = knots[i + degree - r + 1] - knots[i];
                var alpha = den < 1e-12 ? 0 : (t - knots[i]) / den;
                d[j] = d[j - 1] * (1 - alpha) + d[j] * alpha;
            }
        return d[degree];
    }

    /// <summary>F-Spline: a smooth spline through every point (natural cubic, chord-length parameters).</summary>
    private static List<Vec3> Fit(List<Vec3> p, int segments)
    {
        var n = p.Count;
        if (n == 2)
            return Resample(p, segments);
        var t = new double[n];
        for (var i = 1; i < n; i++)
            t[i] = t[i - 1] + Math.Max(p[i].DistanceTo(p[i - 1]), 1e-9);
        // Second derivatives of a natural cubic spline per coordinate (tridiagonal solve).
        double[] Solve(Func<Vec3, double> c)
        {
            var m = new double[n];
            var a = new double[n];
            var b = new double[n];
            var r = new double[n];
            for (var i = 1; i < n - 1; i++)
            {
                double h0 = t[i] - t[i - 1], h1 = t[i + 1] - t[i];
                a[i] = h0;
                b[i] = 2 * (h0 + h1);
                r[i] = 6 * ((c(p[i + 1]) - c(p[i])) / h1 - (c(p[i]) - c(p[i - 1])) / h0);
            }
            var cp = new double[n];
            var dp = new double[n];
            for (var i = 1; i < n - 1; i++)
            {
                double h1 = t[i + 1] - t[i];
                var den = b[i] - a[i] * cp[i - 1];
                cp[i] = h1 / den;
                dp[i] = (r[i] - a[i] * dp[i - 1]) / den;
            }
            for (var i = n - 2; i >= 1; i--)
                m[i] = dp[i] - cp[i] * m[i + 1];
            return m;
        }
        var mx = Solve(v => v.X);
        var my = Solve(v => v.Y);
        var mz = Solve(v => v.Z);
        double Eval(double[] m, Func<Vec3, double> c, int i, double x)
        {
            var h = t[i + 1] - t[i];
            double A = (t[i + 1] - x) / h, B = (x - t[i]) / h;
            return A * c(p[i]) + B * c(p[i + 1]) + ((A * A * A - A) * m[i] + (B * B * B - B) * m[i + 1]) * h * h / 6;
        }
        // Each span gets its share of the segments by length, so the curve meets every control point.
        var result = new List<Vec3> { p[0] };
        for (var i = 0; i < n - 1; i++)
        {
            var k = Math.Max(1, (int)Math.Round(segments * (t[i + 1] - t[i]) / t[^1]));
            for (var s = 1; s <= k; s++)
            {
                var x = t[i] + (t[i + 1] - t[i]) * s / k;
                result.Add(s == k ? p[i + 1] : new Vec3(Eval(mx, v => v.X, i, x), Eval(my, v => v.Y, i, x), Eval(mz, v => v.Z, i, x)));
            }
        }
        return result;
    }

    /// <summary>Courbette: circular arcs through the points (two per span, a biarc), smooth like Spiro.</summary>
    private static List<Vec3> Courbette(List<Vec3> p, int perArc)
    {
        // Each point's tangent is the one of the circle through it and its neighbours.
        var n = p.Count;
        var tangents = new Vec3[n];
        for (var i = 0; i < n; i++)
        {
            if (n == 2)
                tangents[i] = (p[1] - p[0]).Normalized();
            else if (i == 0)
                tangents[i] = CircleTangentAt(p[0], p[1], p[2], p[0]);
            else if (i == n - 1)
                tangents[i] = CircleTangentAt(p[n - 3], p[n - 2], p[n - 1], p[n - 1]);
            else
                tangents[i] = CircleTangentAt(p[i - 1], p[i], p[i + 1], p[i]);
        }
        var result = new List<Vec3> { p[0] };
        for (var i = 0; i < n - 1; i++)
            result.AddRange(Biarc(p[i], tangents[i], p[i + 1], tangents[i + 1], perArc).Skip(1));
        return result;
    }

    /// <summary>The unit tangent, in the a→c direction, of the circle through a, b and c at <paramref name="at"/>.</summary>
    private static Vec3 CircleTangentAt(Vec3 a, Vec3 b, Vec3 c, Vec3 at)
    {
        var ab = b - a;
        var ac = c - a;
        var n = ab.Cross(ac);
        if (n.Length < 1e-12)
            return (c - a).Normalized();
        var centre = a + (n.Cross(ab) * ac.LengthSquared + ac.Cross(n) * ab.LengthSquared) / (2 * n.LengthSquared);
        var t = n.Cross(at - centre).Normalized();
        return t.Dot(c - a) < 0 ? -t : t;
    }

    /// <summary>Two arcs from p0 (leaving along t0) to p1 (arriving along t1), meeting with a common tangent.</summary>
    private static List<Vec3> Biarc(Vec3 p0, Vec3 t0, Vec3 p1, Vec3 t1, int perArc)
    {
        var v = p1 - p0;
        var t = t0 + t1;
        var denom = 2 * (1 - t0.Dot(t1));
        double d;
        if (Math.Abs(denom) < 1e-9)
            d = v.LengthSquared / Math.Max(4 * v.Dot(t1), 1e-12);
        else
            d = (-v.Dot(t) + Math.Sqrt(v.Dot(t) * v.Dot(t) + denom * v.LengthSquared)) / denom;
        var joint = (p0 + t0 * d + p1 - t1 * d) / 2;
        var first = TangentArc(p0, t0, joint, perArc);
        var second = TangentArc(p1, -t1, joint, perArc);
        second.Reverse();
        return [.. first, .. second.Skip(1)];
    }

    /// <summary>The arc from <paramref name="a"/> to <paramref name="b"/> leaving a along <paramref name="tangent"/>.</summary>
    private static List<Vec3> TangentArc(Vec3 a, Vec3 tangent, Vec3 b, int segments)
    {
        var chord = b - a;
        var normal = tangent.Cross(chord);
        if (normal.Length < 1e-9)
            return [a, b];
        var inward = normal.Cross(tangent).Normalized();
        var radius = chord.LengthSquared / (2 * chord.Dot(inward));
        var centre = a + inward * radius;
        var from = a - centre;
        var to = b - centre;
        var axis = normal.Normalized();
        var angle = Math.Atan2(from.Cross(to).Dot(axis), from.Dot(to));
        if (angle < 0)
            angle += 2 * Math.PI;
        var list = new List<Vec3>();
        for (var s = 0; s <= segments; s++)
            list.Add(centre + Rotate(from, axis, angle * s / segments));
        return list;
    }

    private static Vec3 Rotate(Vec3 v, Vec3 axis, double angle) =>
        v * Math.Cos(angle) + axis.Cross(v) * Math.Sin(angle) + axis * (axis.Dot(v) * (1 - Math.Cos(angle)));

    // ------------------------------------------------------------------ polyline corners

    private delegate List<Vec3> CornerShape(Vec3 before, Vec3 corner, Vec3 after, double value, int circleSegments);

    /// <summary>Applies a corner treatment to every inner corner (and the closing one of a loop).</summary>
    private static List<Vec3> Corners(List<Vec3> p, bool closed, double value, int circleSegments, CornerShape shape)
    {
        if (value <= Tolerance.Length || p.Count < 3)
            return p;
        var corners = closed ? p.Take(p.Count - 1).ToList() : p;
        var n = corners.Count;
        var result = new List<Vec3>();
        for (var i = 0; i < n; i++)
        {
            var isEnd = !closed && (i == 0 || i == n - 1);
            if (isEnd)
            {
                result.Add(corners[i]);
                continue;
            }
            var before = corners[(i - 1 + n) % n];
            var after = corners[(i + 1) % n];
            result.AddRange(shape(before, corners[i], after, value, circleSegments));
        }
        if (closed)
            result.Add(result[0]);
        return result;
    }

    /// <summary>A rounded corner: an arc of radius <paramref name="r"/> tangent to both sides.</summary>
    private static List<Vec3> Fillet(Vec3 before, Vec3 corner, Vec3 after, double r, int circleSegments)
    {
        var u = (before - corner).Normalized();
        var v = (after - corner).Normalized();
        var half = Math.Acos(Math.Clamp(u.Dot(v), -1, 1)) / 2;
        if (half < 1e-6 || Math.PI / 2 - half < 1e-6)
            return [corner];
        var setback = Math.Min(r / Math.Tan(half), Math.Min(before.DistanceTo(corner), after.DistanceTo(corner)) / 2);
        var radius = setback * Math.Tan(half);
        var a = corner + u * setback;
        var b = corner + v * setback;
        var centre = corner + (u + v).Normalized() * (radius / Math.Sin(half));
        var sweep = Math.PI - 2 * half;
        var steps = Math.Max(1, (int)Math.Ceiling(circleSegments * sweep / (2 * Math.PI)));
        var axis = (a - centre).Cross(b - centre).Normalized();
        var list = new List<Vec3>();
        for (var s = 0; s <= steps; s++)
            list.Add(centre + Rotate(a - centre, axis, sweep * s / steps));
        return list;
    }

    private static List<Vec3> ChamferCorner(Vec3 before, Vec3 corner, Vec3 after, double d, int _)
    {
        var u = (before - corner).Normalized();
        var v = (after - corner).Normalized();
        var cut = Math.Min(d, Math.Min(before.DistanceTo(corner), after.DistanceTo(corner)) / 2);
        return [corner + u * cut, corner + v * cut];
    }

    /// <summary>
    /// A dog-bone corner: the cut of a round cutter of radius <paramref name="r"/> pushed along the bisector until it
    /// touches the corner, so a square part fits in the pocket.
    /// </summary>
    /// <summary>A T-bone corner: a half circle of radius <paramref name="r"/> cut along the incoming side, past the corner
    /// on the outside, so a round cutter reaches it travelling along that side only.</summary>
    private static List<Vec3> TBoneCorner(Vec3 before, Vec3 corner, Vec3 after, double r, int circleSegments)
    {
        var u = (before - corner).Normalized();
        var v = (after - corner).Normalized();
        var w = v - u * u.Dot(v);
        if (w.Length < 1e-9)
            return [corner];
        w = w.Normalized();
        var centre = corner + u * r;
        var steps = Math.Max(2, (int)Math.Ceiling(circleSegments / 2.0));
        return Enumerable.Range(0, steps + 1)
            .Select(k => Math.PI * k / steps)
            .Select(t => centre + u * (Math.Cos(t) * r) - w * (Math.Sin(t) * r))
            .ToList();
    }

    private static List<Vec3> DogBoneCorner(Vec3 before, Vec3 corner, Vec3 after, double r, int circleSegments)
    {
        var u = (before - corner).Normalized();
        var v = (after - corner).Normalized();
        var bisector = (u + v);
        if (bisector.Length < 1e-9)
            return [corner];
        var centre = corner + bisector.Normalized() * r;
        var a = corner + u * (2 * (centre - corner).Dot(u));
        var b = corner + v * (2 * (centre - corner).Dot(v));
        // The arc from a to b through the corner itself: the notch reaching past both sides (the dog-bone's ears).
        var e1 = (a - centre).Normalized();
        // The corner's plane (a and b are opposite ends of a diameter at right-angled corners).
        var axis = u.Cross(v);
        if (axis.Length < 1e-12)
            return [corner];
        axis = axis.Normalized();
        var e2 = axis.Cross(e1);
        double AngleOf(Vec3 q) => Math.Atan2((q - centre).Dot(e2), (q - centre).Dot(e1));
        var toB = AngleOf(b);
        var toFar = AngleOf(corner);
        if (toB < 0)
            toB += 2 * Math.PI;
        if (toFar < 0)
            toFar += 2 * Math.PI;
        var sweep = toFar < toB ? toB : toB - 2 * Math.PI;
        var steps = Math.Max(2, (int)Math.Ceiling(circleSegments * Math.Abs(sweep) / (2 * Math.PI)));
        var list = new List<Vec3>();
        for (var k = 0; k <= steps; k++)
        {
            var angle = sweep * k / steps;
            list.Add(centre + (e1 * Math.Cos(angle) + e2 * Math.Sin(angle)) * r);
        }
        return list;
    }

    // ------------------------------------------------------------------ resampling

    public static double Length(IReadOnlyList<Vec3> p)
    {
        var total = 0.0;
        for (var i = 1; i < p.Count; i++)
            total += p[i].DistanceTo(p[i - 1]);
        return total;
    }

    /// <summary>Points at <paramref name="count"/> equal steps along the polyline.</summary>
    private static List<Vec3> Resample(IReadOnlyList<Vec3> p, int count)
    {
        var total = Length(p);
        var result = new List<Vec3> { p[0] };
        var seg = 1;
        var walked = 0.0;
        for (var k = 1; k < count; k++)
        {
            var target = total * k / count;
            while (seg < p.Count - 1 && walked + p[seg].DistanceTo(p[seg - 1]) < target)
            {
                walked += p[seg].DistanceTo(p[seg - 1]);
                seg++;
            }
            var len = p[seg].DistanceTo(p[seg - 1]);
            var t = len < 1e-12 ? 0 : (target - walked) / len;
            result.Add(p[seg - 1] + (p[seg] - p[seg - 1]) * t);
        }
        result.Add(p[^1]);
        return result;
    }
}
