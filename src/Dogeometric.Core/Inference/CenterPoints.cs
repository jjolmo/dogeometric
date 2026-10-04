using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Inference;

/// <summary>A centre the cursor can snap to, in world coordinates.</summary>
public sealed record CenterPoint(Vec3 Point, string Label);

/// <summary>
/// Dogeometric's centre points (not in SketchUp): bounding-box middles of the context's groups, components and
/// faces, and of the selection, to snap and align things centre to centre.
/// </summary>
public static class CenterPoints
{
    public static List<CenterPoint> Of(Entities context, Transform toWorld, IReadOnlyCollection<object> selection)
    {
        var result = new List<CenterPoint>();
        // Copies share their definition's box: measure each definition once.
        var boxes = new Dictionary<ComponentDefinition, Bounds3>();
        foreach (var inst in context.Instances.Where(i => !i.Hidden))
            if (InstanceCenter(inst, boxes) is { } c)
                result.Add(new CenterPoint(toWorld.ApplyPoint(c), inst.IsGroup ? "Center of Group" : "Center of Component"));
        foreach (var face in context.Faces.Where(f => !f.Hidden))
            result.Add(new CenterPoint(toWorld.ApplyPoint(FaceCenter(face)), "Center of Face"));
        // A single group or face already has its centre; the selection's own centre matters for several things.
        if (selection.Count > 1 || selection.Any(x => x is Edge))
        {
            var b = SelectionBounds(selection);
            if (!b.IsEmpty)
                result.Add(new CenterPoint(toWorld.ApplyPoint(b.Center), "Center of Selection"));
        }
        return result;
    }

    /// <summary>The middle of a group or component's box, in the coordinates of the collection holding it.</summary>
    public static Vec3? InstanceCenter(ComponentInstance inst, Dictionary<ComponentDefinition, Bounds3>? boxes = null)
    {
        Bounds3 b;
        if (boxes == null)
            b = inst.Definition.Entities.Bounds();
        else if (!boxes.TryGetValue(inst.Definition, out b))
            boxes[inst.Definition] = b = inst.Definition.Entities.Bounds();
        return b.IsEmpty ? null : inst.Transform.ApplyPoint(b.Center);
    }

    /// <summary>The middle of a face's extent within its own plane.</summary>
    public static Vec3 FaceCenter(Face face)
    {
        var points = face.OuterLoop.Points.ToList();
        var (x, y) = Texturing.PlaneAxes(face.Normal.Normalized());
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            var px = p.Dot(x);
            var py = p.Dot(y);
            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }
        var n = face.Normal.Normalized();
        var height = points[0].Dot(n);
        return x * ((minX + maxX) / 2) + y * ((minY + maxY) / 2) + n * height;
    }

    private static Bounds3 SelectionBounds(IEnumerable<object> selection)
    {
        var b = Bounds3.Empty;
        foreach (var item in selection)
        {
            switch (item)
            {
                case Edge e:
                    b = b.Include(e.Start.Position).Include(e.End.Position);
                    break;
                case Face f:
                    foreach (var p in f.OuterLoop.Points)
                        b = b.Include(p);
                    break;
                case ComponentInstance i:
                    var inner = i.Definition.Entities.Bounds();
                    if (!inner.IsEmpty)
                        foreach (var corner in inner.Corners())
                            b = b.Include(i.Transform.ApplyPoint(corner));
                    break;
            }
        }
        return b;
    }
}
