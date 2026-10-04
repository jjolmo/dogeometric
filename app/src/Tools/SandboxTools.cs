using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// Sandbox › From Scratch: type the grid spacing, click the grid's origin, its length and its width; the grid is
/// made as a group of triangles, flat, ready for Smoove.
/// </summary>
public sealed class SandboxGridTool : DrawingTool
{
    private static double _spacing = 1000;
    private readonly List<Vec3> _clicks = [];

    public override int CommandId => ExtensionIds.SandboxFromScratch;
    public override string CursorImage => "pencil";
    public override string VcbLabel => _clicks.Count == 0 ? "Grid Spacing" : "Length";
    public override string VcbValue => Length.Format(_spacing, LengthUnit.Millimeters, 0);
    protected override Vec3? From => _clicks.Count > 0 ? _clicks[^1] : null;

    public override string StatusText => _clicks.Count switch
    {
        0 => "Click to set the grid's starting point. Type the grid spacing in the Measurements box.",
        1 => "Click to set the grid's length.",
        _ => "Click to set the grid's width.",
    };

    public override void Activate() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        var p = _clicks.Count == 2 ? InPlane(inf.Point) : inf.Point;
        _clicks.Add(p);
        if (_clicks.Count < 3)
        {
            RefreshStatus();
            return;
        }
        var (o, x, w, y, d) = Frame();
        var toLocal = doc.Context.ToWorld.Inverse();
        doc.Operation("Grid", e =>
        {
            var def = new ComponentDefinition { Name = "Grid", IsGroup = true };
            Sandbox.Grid(def.Entities, toLocal.ApplyPoint(o), toLocal.ApplyVector(x), w, toLocal.ApplyVector(y), d, _spacing);
            doc.Model.Definitions.Add(def);
            e.AddInstance(def, Transform.Identity);
        });
        _clicks.Clear();
        RefreshStatus();
    }

    /// <summary>The third point dropped into the plane of the first two and the vertical (or the ground).</summary>
    private Vec3 InPlane(Vec3 p)
    {
        var x = (_clicks[1] - _clicks[0]).Normalized();
        var y = Vec3.UnitZ.Cross(x).Normalized();
        return _clicks[0] + x * (p - _clicks[0]).Dot(x) + y * (p - _clicks[0]).Dot(y);
    }

    private (Vec3 Origin, Vec3 X, double Width, Vec3 Y, double Depth) Frame()
    {
        var x = _clicks[1] - _clicks[0];
        var third = _clicks[2] - _clicks[1];
        var y = third - x.Normalized() * third.Dot(x.Normalized());
        var origin = _clicks[0];
        return (origin, x.Normalized(), x.Length, y.Normalized(), y.Length);
    }

    public override bool ApplyVcb(string text)
    {
        if (!Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm <= 0)
            return false;
        if (_clicks.Count == 0)
        {
            _spacing = mm;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        return false;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _clicks.Count > 0)
        {
            _clicks.Clear();
            RefreshStatus();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_clicks.Count > 0 && Current is { } inf)
        {
            var pts = _clicks.ToList();
            pts.Add(_clicks.Count == 2 ? InPlane(inf.Point) : inf.Point);
            if (pts.Count == 3)
            {
                var corner = pts[0] + (pts[2] - pts[1]);
                pts.Add(corner);
                pts.Add(pts[0]);
            }
            for (var i = 0; i + 1 < pts.Count; i++)
                DrawWorldLine(overlay, pts[i], pts[i + 1], Colors.Black, 1.5f);
        }
        DrawInference(overlay);
    }
}

/// <summary>
/// Sandbox › Smoove: type the radius, click a point on the terrain and move up or down; the surface rises or sinks
/// smoothly around it, live. Click (or type the height) to finish. Works on the geometry of the open group.
/// </summary>
public sealed class SmooveTool : DrawingTool
{
    private static double _radius = 1000;
    private Vec3? _centre;
    private double _height;

    public override int CommandId => ExtensionIds.SandboxSmoove;
    public override string CursorImage => "move";
    public override string VcbLabel => _centre == null ? "Radius" : "Offset";
    public override string VcbValue => Length.Format(_centre == null ? _radius : _height, LengthUnit.Millimeters, 0);

    public override string StatusText => _centre == null
        ? "Click on the terrain to start smooving. Type the radius in the Measurements box."
        : "Move up or down, then click (or type the offset).";

    public override void Activate() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_centre != null)
        {
            doc.CommitPreview();
            _centre = null;
            RefreshStatus();
            return;
        }
        // Any point on the terrain will do (edges and vertices too): Smoove moves the vertices around it.
        if (Current is not { Kind: not Dogeometric.Core.Inference.InferenceKind.None } inf)
            return;
        _centre = inf.Point;
        _height = 0;
        RefreshStatus();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_centre is not { } c)
        {
            View.QueueOverlayRedraw();
            return;
        }
        var ray = View.ScreenRay(Mouse);
        _height = (InferenceEngine.ClosestOnLine(new Ray(ray.Origin, ray.Direction), c, Vec3.UnitZ) - c).Z;
        View.ShowVcbValue(VcbValue);
        Preview();
    }

    private void Preview()
    {
        if (_centre is not { } c || View.Document is not { } doc)
            return;
        var toLocal = doc.Context.ToWorld.Inverse();
        var local = toLocal.ApplyPoint(c);
        var up = toLocal.ApplyVector(Vec3.UnitZ);
        var height = _height;
        doc.Preview("Smoove", e => Sandbox.Smoove(e, local, _radius, up, height / up.Length));
    }

    public override bool ApplyVcb(string text)
    {
        if (!Length.TryParse(text, LengthUnit.Millimeters, out var mm))
            return false;
        if (_centre == null)
        {
            if (mm <= 0)
                return false;
            _radius = mm;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        _height = mm;
        Preview();
        View.Document?.CommitPreview();
        _centre = null;
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _centre != null)
        {
            View.Document?.CancelPreview();
            _centre = null;
            RefreshStatus();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        // The area of influence, as SketchUp outlines it on the ground.
        var c = _centre ?? Current?.Point;
        if (c is { } centre)
        {
            const int n = 48;
            for (var i = 0; i < n; i++)
            {
                var a = Math.Tau * i / n;
                var b = Math.Tau * (i + 1) / n;
                DrawWorldLine(overlay, centre + new Vec3(Math.Cos(a), Math.Sin(a), 0) * _radius, centre + new Vec3(Math.Cos(b), Math.Sin(b), 0) * _radius, new Color(0.9f, 0.1f, 0.1f), 1.5f);
            }
        }
        DrawInference(overlay);
    }
}

/// <summary>Sandbox › Flip Edge: click the diagonal between two triangles to join their other corners instead.</summary>
public sealed class FlipEdgeTool : Tool
{
    public override int CommandId => ExtensionIds.SandboxFlipEdge;
    public override string StatusText => "Click an edge between two triangles to flip it.";
    public override string CursorImage => "select";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || View.Pick(position) is not { Entity: Edge edge } hit || !hit.Path.SequenceEqual(doc.Context.Path))
            return;
        doc.Operation("Flip Edge", e => Sandbox.FlipEdge(e, edge));
    }
}
