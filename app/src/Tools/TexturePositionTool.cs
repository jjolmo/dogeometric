using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's Texture › Position, live. Fixed pins: red moves, green scales and turns about red, blue scales or
/// shears the other side, yellow distorts in perspective. Shift toggles free pins: four pins that can be lifted (click)
/// and dropped on any point of the picture, then dragged, the texture following all four. Pins snap to the face's
/// corners unless Ctrl is held. Return or a click away keeps it, Esc puts it back.</summary>
public sealed class TexturePositionTool(Face face, bool back) : Tool
{
    private static readonly Color Red = new(0.9f, 0.1f, 0.1f);
    private static readonly Color Green = new(0.1f, 0.65f, 0.15f);
    private static readonly Color Blue = new(0.15f, 0.3f, 0.95f);
    private static readonly Color Yellow = new(0.95f, 0.85f, 0.1f);

    private const double Inch = 25.4;
    private (double X, double Y) _o, _u, _v, _w;
    private bool _distorted;
    private int _dragging = -1;
    private Material _material = null!;

    // Free pins: where each sits on the picture (fractions of a tile) and on the face's plane.
    private bool _free;
    private readonly (double S, double T, (double X, double Y) At)[] _freePins = new (double, double, (double, double))[4];
    private int _lifted = -1;
    private Vector2 _pressedAt;
    private bool _moved;

    public override int CommandId => 0;
    public override string StatusText => _lifted >= 0 ? "Click to place pin."
        : "Drag pins to position texture.  Click=Lift pin, Shift=Toggle Fixed, Ctrl=No snapping.";

    public override void Activate()
    {
        _material = (back ? face.BackMaterial : face.FrontMaterial)!;
        var tex = _material.Texture!;
        var (tw, th) = (tex.WidthMm, tex.HeightMm);
        if ((back ? face.BackMapping : face.FrontMapping) is { } m)
        {
            // The tile's corners in the face's plane frame: [u v 1]·M with u, v in tile inches, divided by w.
            var k = m.Matrix;
            (double, double) Map(double ui, double vi)
            {
                var w = ui * k[2] + vi * k[5] + k[8];
                if (Math.Abs(w) < 1e-12)
                    w = 1;
                return ((ui * k[0] + vi * k[3] + k[6]) / w * Inch, (ui * k[1] + vi * k[4] + k[7]) / w * Inch);
            }
            double twi = tw / Inch, thi = th / Inch;
            _o = Map(0, 0);
            _u = Map(twi, 0);
            _v = Map(0, thi);
            _w = Map(twi, thi);
            _distorted = Math.Abs(k[2]) > 1e-12 || Math.Abs(k[5]) > 1e-12;
        }
        else
        {
            _o = (0, 0);
            _u = (tw, 0);
            _v = (0, th);
            _w = (tw, th);
        }
        View.QueueOverlayRedraw();
    }

    private Vec3 Normal => face.Normal.Normalized();

    private Transform ToWorld => View.Document!.Context.ToWorld;

    /// <summary>A point of the face's plane frame in world space.</summary>
    private Vec3 World((double X, double Y) p)
    {
        var (xr, yr) = Texturing.PlaneAxes(Normal);
        var height = face.OuterLoop.Points.First().Dot(Normal);
        return ToWorld.ApplyPoint(xr * p.X + yr * p.Y + Normal * height);
    }

    /// <summary>Where the cursor meets the face's plane, in the plane frame.</summary>
    private (double X, double Y)? Plane(Vector2 screen)
    {
        var ray = View.ScreenRay(screen);
        var n = ToWorld.ApplyNormal(Normal).Normalized();
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), n, ToWorld.ApplyPoint(face.OuterLoop.Points.First())) is not { } hit)
            return null;
        return Texturing.PlanePoint(face, ToWorld.Inverse().ApplyPoint(hit));
    }

    private (double X, double Y)[] Pins => [_o, _u, _v, _w];

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Right && View.Document is { } d)
        {
            ShowMenu(d, position);
            return;
        }
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_lifted >= 0)
        {
            // Dropping a lifted pin: it now holds the point of the picture under it.
            if (Plane(position) is { } at && Unmap(at) is var (ps, pt))
                _freePins[_lifted] = (ps, pt, at);
            _lifted = -1;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return;
        }
        var pins = _free ? _freePins.Select(f => f.At).ToArray() : Pins;
        for (var i = 0; i < 4; i++)
            if (View.ToScreen(World(pins[i])) is { } s && s.DistanceTo(position) < 10)
            {
                _dragging = i;
                (_pressedAt, _moved) = (position, false);
                return;
            }
        Finish(doc);
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        // A click on a free pin without dragging lifts it.
        if (_free && _dragging >= 0 && !_moved)
        {
            _lifted = _dragging;
            RefreshStatus();
        }
        _dragging = -1;
    }

    /// <summary>A plane point snapped to the face's corners within a few pixels, unless Ctrl is held.</summary>
    private (double X, double Y) Snapped((double X, double Y) p, Vector2 screen)
    {
        if (Input.IsKeyPressed(Key.Ctrl))
            return p;
        foreach (var v in face.Loops.SelectMany(l => l.Points))
        {
            var corner = Texturing.PlanePoint(face, v);
            if (View.ToScreen(World(corner)) is { } c && c.DistanceTo(screen) < 10)
                return corner;
        }
        return p;
    }

    /// <summary>The picture point (tile fractions) shown at a plane point, through the current corners.</summary>
    private (double S, double T)? Unmap((double X, double Y) p) =>
        Homography.Solve([(0, 0), (1, 0), (0, 1), (1, 1)], [_o, _u, _v, _w]) is { } h && Homography.Invert(h) is { } inv ? Homography.Apply(inv, p) : null;

    /// <summary>Position Texture's context menu, as SketchUp's: Done, Reset, Flip, Rotate, Fixed Pins.</summary>
    private void ShowMenu(Document doc, Vector2 at)
    {
        var menu = new PopupMenu();
        menu.AddItem("Done", 0);
        menu.AddItem("Reset", 1);
        menu.AddSeparator();
        var flip = new PopupMenu();
        flip.AddItem("Left/Right", 10);
        flip.AddItem("Up/Down", 11);
        menu.AddSubmenuNodeItem("Flip", flip);
        var rotate = new PopupMenu();
        rotate.AddItem("90", 20);
        rotate.AddItem("180", 21);
        rotate.AddItem("270", 22);
        menu.AddSubmenuNodeItem("Rotate", rotate);
        menu.AddSeparator();
        menu.AddCheckItem("Fixed Pins", 30);
        menu.SetItemChecked(menu.GetItemIndex(30), !_free);
        void Run(long id)
        {
            switch (id)
            {
                case 0:
                    Finish(doc);
                    return;
                case 1:
                    var tex = _material.Texture!;
                    (_o, _u, _v, _w, _distorted) = ((0, 0), (tex.WidthMm, 0), (0, tex.HeightMm), (tex.WidthMm, tex.HeightMm), false);
                    break;
                case 10:
                    (_o, _u, _v, _w) = (_u, _o, _w, _v);
                    break;
                case 11:
                    (_o, _u, _v, _w) = (_v, _w, _o, _u);
                    break;
                case 20 or 21 or 22:
                    // Turns the picture a quarter at a time, counter-clockwise, about its centre.
                    for (var i = 0; i <= id - 20; i++)
                        (_o, _u, _w, _v) = (_u, _w, _v, _o);
                    break;
                case 30:
                    ToggleFree();
                    return;
            }
            // Free pins follow the reshaped picture from its corners.
            if (_free)
            {
                _freePins[0] = (0, 0, _o);
                _freePins[1] = (1, 0, _u);
                _freePins[2] = (0, 1, _v);
                _freePins[3] = (1, 1, _w);
            }
            Preview();
        }
        menu.IdPressed += Run;
        flip.IdPressed += Run;
        rotate.IdPressed += Run;
        menu.PopupHide += menu.QueueFree;
        View.AddChild(menu);
        menu.Position = (Vector2I)(View.GetScreenPosition() + at);
        menu.Popup();
    }

    private void ToggleFree()
    {
        _free = !_free;
        _lifted = -1;
        if (_free)
        {
            _freePins[0] = (0, 0, _o);
            _freePins[1] = (1, 0, _u);
            _freePins[2] = (0, 1, _v);
            _freePins[3] = (1, 1, _w);
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_lifted >= 0)
        {
            View.QueueOverlayRedraw();
            return;
        }
        if (_dragging < 0 || Plane(position) is not { } raw)
            return;
        if (position.DistanceTo(_pressedAt) > 3)
            _moved = true;
        if (!_moved)
            return;
        var p = Snapped(raw, position);
        if (_free)
        {
            _freePins[_dragging] = _freePins[_dragging] with { At = p };
            if (Homography.Solve(_freePins.Select(f => (f.S, f.T)).ToArray(), _freePins.Select(f => f.At).ToArray()) is { } h)
            {
                (_o, _u, _v, _w) = (Homography.Apply(h, (0, 0)), Homography.Apply(h, (1, 0)), Homography.Apply(h, (0, 1)), Homography.Apply(h, (1, 1)));
                _distorted = true;
                Preview();
            }
            return;
        }
        switch (_dragging)
        {
            case 0:
                var (dx, dy) = (p.X - _o.X, p.Y - _o.Y);
                _o = p;
                _u = (_u.X + dx, _u.Y + dy);
                _v = (_v.X + dx, _v.Y + dy);
                _w = (_w.X + dx, _w.Y + dy);
                break;
            case 1:
            {
                // Scale and turn about the red pin; the other side keeps square and in proportion.
                var (ux, uy) = (_u.X - _o.X, _u.Y - _o.Y);
                var (vx, vy) = (_v.X - _o.X, _v.Y - _o.Y);
                var (nx, ny) = (p.X - _o.X, p.Y - _o.Y);
                var lu = Math.Sqrt(ux * ux + uy * uy);
                var ln = Math.Sqrt(nx * nx + ny * ny);
                if (ln < 1e-6 || lu < 1e-9)
                    return;
                var scale = ln / lu;
                var angle = Math.Atan2(ny, nx) - Math.Atan2(uy, ux);
                var (c, s) = (Math.Cos(angle), Math.Sin(angle));
                _u = p;
                _v = (_o.X + (vx * c - vy * s) * scale, _o.Y + (vx * s + vy * c) * scale);
                var (wx, wy) = (_w.X - _o.X, _w.Y - _o.Y);
                _w = (_o.X + (wx * c - wy * s) * scale, _o.Y + (wx * s + wy * c) * scale);
                break;
            }
            case 2:
                _v = p;
                break;
            case 3:
                _w = p;
                _distorted = true;
                break;
        }
        if (!_distorted)
            _w = (_u.X + _v.X - _o.X, _u.Y + _v.Y - _o.Y);
        Preview();
    }

    private void Preview()
    {
        if (View.Document is not { } doc)
            return;
        var tex = _material.Texture!;
        var mapping = _distorted
            ? TextureMapping.FromQuad(_o, _u, _w, _v, tex.WidthMm, tex.HeightMm)
            : TextureMapping.FromPlanePoints(_o, _u, _v, tex.WidthMm, tex.HeightMm);
        doc.Preview("Position Texture", _ =>
        {
            if (back)
                face.BackMapping = mapping;
            else
                face.FrontMapping = mapping;
        });
        View.QueueOverlayRedraw();
    }

    private void Finish(Document doc)
    {
        doc.CommitPreview();
        Manager.Activate(new SelectTool());
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (View.Document is not { } doc)
            return false;
        switch (key.Keycode)
        {
            case Key.Shift when !key.Echo:
                ToggleFree();
                return true;
            case Key.Enter or Key.KpEnter:
                Finish(doc);
                return true;
            case Key.Escape:
                doc.CancelPreview();
                Manager.Activate(new SelectTool());
                return true;
        }
        return false;
    }

    public override void Deactivate() => View.Document?.CommitPreview();

    public override void Draw(Control overlay)
    {
        (double, double)[] outline = [_o, _u, _w, _v];
        for (var i = 0; i < 4; i++)
            if (View.ToScreen(World(outline[i])) is { } a && View.ToScreen(World(outline[(i + 1) % 4])) is { } b)
                overlay.DrawDashedLine(a, b, new Color(0.2f, 0.2f, 0.2f), 1, 4);
        if (_free)
        {
            for (var i = 0; i < 4; i++)
            {
                var at = i == _lifted && Plane(View.GetLocalMousePosition()) is { } m ? m : _freePins[i].At;
                if (View.ToScreen(World(at)) is { } s)
                {
                    overlay.DrawCircle(s, 7, Colors.White);
                    overlay.DrawCircle(s, 5.5f, Yellow);
                    if (i == _lifted)
                        overlay.DrawArc(s, 10, 0, Mathf.Tau, 24, Colors.Black, 1);
                }
            }
            return;
        }
        Color[] colors = [Red, Green, Blue, Yellow];
        for (var i = 0; i < 4; i++)
            if (View.ToScreen(World(Pins[i])) is { } s)
            {
                overlay.DrawCircle(s, 7, Colors.White);
                overlay.DrawCircle(s, 5.5f, colors[i]);
            }
    }
}
