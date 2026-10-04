using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Where geometry goes when it is laid on a clicked face: that face's collection and its transform to world.</summary>
internal static class SurfaceTarget
{
    public static (Entities Entities, Transform ToWorld)? Of(Document doc, PickHit hit)
    {
        if (hit.Face == null)
            return null;
        var toWorld = hit.Path.Aggregate(Transform.Identity, (acc, inst) => inst.Transform.Then(acc));
        var entities = hit.Path.Count > 0 ? hit.Path[^1].Definition.Entities : doc.Model.Entities;
        return (entities, toWorld);
    }

    /// <summary>Lays a world-space path on <paramref name="target"/> along a world direction, as one undoable step.</summary>
    public static int Lay(Document doc, string name, (Entities Entities, Transform ToWorld) target, IReadOnlyList<Vec3> worldPath, Vec3 worldDirection, bool closed)
    {
        var toLocal = target.ToWorld.Inverse();
        var path = worldPath.Select(toLocal.ApplyPoint).ToList();
        var dir = toLocal.ApplyVector(worldDirection);
        var made = 0;
        doc.Undo.Begin(name, target.Entities);
        try
        {
            foreach (var run in Sandbox.DrapePath(target.Entities, path, dir, closed))
            {
                StickyGeometry.DrawEdges(target.Entities, run);
                made++;
            }
            doc.Undo.Commit();
        }
        catch
        {
            doc.Undo.Abort();
            throw;
        }
        return made;
    }
}

/// <summary>Sandbox › Drape: with edges selected, click a surface: the edges drop straight down onto it and split its faces.</summary>
public sealed class DrapeTool : Tool
{
    private List<List<Vec3>> _paths = [];

    public override int CommandId => ExtensionIds.SandboxDrape;
    public override string CursorImage => "select";
    public override string StatusText => _paths.Count == 0 ? "Select the edges to drape first." : "Click the surface to drape the edges onto.";

    public override void Activate()
    {
        if (View.Document is not { } doc)
            return;
        var xf = doc.Context.ToWorld;
        _paths = Curviloft.Chains(doc.Selection.Items.OfType<Edge>()).Select(c => c.Select(xf.ApplyPoint).ToList()).ToList();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _paths.Count == 0 || View.Document is not { } doc || View.Pick(position) is not { } hit)
            return;
        if (SurfaceTarget.Of(doc, hit) is not { } target)
            return;
        foreach (var path in _paths)
            SurfaceTarget.Lay(doc, "Drape", target, path, -Vec3.UnitZ, closed: path.Count > 2 && path[0].DistanceTo(path[^1]) < Tolerance.Length);
        Manager.Activate(new SelectTool());
    }
}

/// <summary>Tools on Surface's shapes.</summary>
public enum SurfaceShape { Line, Rectangle, Circle, Polygon }

/// <summary>
/// Tools on Surface (Fredo6): line, rectangle, circle and polygon drawn on a surface, flat or curved. The shape is laid
/// out in the plane touching the surface at the first click, then laid onto the faces, splitting them.
/// </summary>
public sealed class SurfaceShapeTool(SurfaceShape shape) : Tool
{
    private static int _sides = 24;
    private Vec3? _first;
    private Vec3 _normal;
    private (Entities, Transform)? _target;
    private Vec3? _hover;

    public override int CommandId => ExtensionIds.SurfaceShape(shape);
    public override string CursorImage => "pencil";
    public override string VcbLabel => shape is SurfaceShape.Circle or SurfaceShape.Polygon && _first == null ? "Sides" : "Length";
    public override string VcbValue => shape is SurfaceShape.Circle or SurfaceShape.Polygon ? _sides.ToString() : "";

    public override string StatusText => _first == null
        ? $"{shape} on Surface: click a point on the surface."
        : shape is SurfaceShape.Circle or SurfaceShape.Polygon ? "Click to set the radius." : "Click the other end / corner.";

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _hover = PointOnPlane(position);
        View.QueueOverlayRedraw();
    }

    private Vec3? PointOnPlane(Vector2 position)
    {
        if (_first is not { } f)
            return View.Pick(position)?.Point;
        var ray = View.ScreenRay(position);
        return Dogeometric.Core.Inference.InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, f);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_first == null)
        {
            if (View.Pick(position) is not { Face: { } face } hit || SurfaceTarget.Of(doc, hit) is not { } target)
                return;
            _first = hit.Point;
            _normal = target.ToWorld.ApplyNormal(face.Normal).Normalized();
            _target = target;
            RefreshStatus();
            return;
        }
        if (PointOnPlane(position) is not { } p || _target is not { } t)
            return;
        var outline = Outline(_first.Value, p);
        if (outline.Count >= 2)
            SurfaceTarget.Lay(doc, $"{shape} on Surface", t, outline, -_normal, closed: shape != SurfaceShape.Line);
        _first = null;
        _target = null;
        RefreshStatus();
    }

    /// <summary>The shape in the touching plane, from the first click to <paramref name="p"/>.</summary>
    private List<Vec3> Outline(Vec3 first, Vec3 p)
    {
        switch (shape)
        {
            case SurfaceShape.Line:
                return [first, p];
            case SurfaceShape.Rectangle:
            {
                var (u, v) = Polygon.PlaneAxes(_normal);
                var d = p - first;
                var du = u * d.Dot(u);
                var dv = v * d.Dot(v);
                return [first, first + du, first + du + dv, first + dv];
            }
            default:
                var r = first.DistanceTo(p);
                return r < Tolerance.Length ? [] : Shapes.RegularPolygon(first, _normal, p - first, r, _sides, false);
        }
    }

    public override bool ApplyVcb(string text)
    {
        if (shape is SurfaceShape.Circle or SurfaceShape.Polygon && int.TryParse(text.Trim().TrimEnd('s'), out var n) && n >= 3)
        {
            _sides = n;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        return false;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode != Key.Escape || _first == null)
            return false;
        _first = null;
        RefreshStatus();
        return true;
    }

    public override void Draw(Control overlay)
    {
        if (_first is not { } f || _hover is not { } h)
            return;
        var pts = Outline(f, h);
        var closed = shape != SurfaceShape.Line;
        for (var i = 0; i + 1 < pts.Count + (closed ? 1 : 0); i++)
            if (View.ToScreen(pts[i]) is { } a && View.ToScreen(pts[(i + 1) % pts.Count]) is { } b)
                overlay.DrawLine(a, b, new Color(0.85f, 0.1f, 0.1f), 1.5f, true);
    }
}
