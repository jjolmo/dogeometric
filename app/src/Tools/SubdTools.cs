using System.Globalization;
using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>SUbD › Crease Tool: pick edges or vertices of the open group (Shift adds), drag up or down or type their
/// sharpness (Shift snaps to whole rounds); a double-click toggles infinitely sharp.</summary>
public sealed class CreaseTool : Tool
{
    private const float Grab = 8;
    private const double PerPixel = 0.02;
    private readonly List<object> _picked = [];
    private Vector2? _dragFrom;
    private Dictionary<object, double> _start = [];
    private ulong _lastClickMs;

    public override int CommandId => ExtensionIds.SubdCrease;
    public override string CursorImage => "select";
    public override string VcbLabel => "Sharpness";
    public override string VcbValue => _picked.Count == 0 ? "" : Format(Sharpness(_picked[0]));

    public override string StatusText => View.Document?.Context.Entities.Subdivision > 0
        ? "Select objects. Click on labels and drag mouse to adjust crease. Shift snaps to whole numbers; double-click toggles infinite sharpness."
        : "Open a subdivided group or component to crease its edges and vertices.";

    private static string Format(double s) => double.IsPositiveInfinity(s) ? "∞" : s.ToString("0.##", CultureInfo.InvariantCulture);

    private static double Sharpness(object o) => o switch { Edge e => e.Crease, Vertex v => v.Crease, _ => 0 };

    private static void SetSharpness(object o, double s)
    {
        if (o is Edge e)
            e.Crease = s;
        else if (o is Vertex v)
            v.Crease = s;
    }

    private Vec3 At(object o, Transform xf) => o switch
    {
        Edge e => xf.ApplyPoint((e.Start.Position + e.End.Position) * 0.5),
        Vertex v => xf.ApplyPoint(v.Position),
        _ => Vec3.Zero,
    };

    /// <summary>The vertex or edge of the open group under the cursor (vertices win near their edge's ends).</summary>
    private object? Under(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { } hit)
            return null;
        var entities = doc.Context.Entities;
        var xf = doc.Context.ToWorld;
        var edge = hit.Edge ?? (hit.Face is { } f ? Topology.EdgesOf(f).FirstOrDefault(x => Near(x)) : null);
        bool Near(Edge x) => View.ToScreen(xf.ApplyPoint(x.Start.Position)) is { } a && View.ToScreen(xf.ApplyPoint(x.End.Position)) is { } b
            && Geometry2D.GetClosestPointToSegment(position, a, b).DistanceTo(position) <= Grab;
        if (edge == null || !entities.Edges.Contains(edge))
            return null;
        foreach (var v in new[] { edge.Start, edge.End })
            if (View.ToScreen(xf.ApplyPoint(v.Position)) is { } s && s.DistanceTo(position) <= Grab)
                return v;
        return edge;
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 400;
        _lastClickMs = now;
        var item = Under(position);
        if (doubleClick && item != null)
        {
            var target = double.IsPositiveInfinity(Sharpness(item)) ? 0 : double.PositiveInfinity;
            var items = _picked.Contains(item) ? _picked.ToList() : [item];
            doc.Operation("Adjust Crease", _ => items.ForEach(o => SetSharpness(o, target)));
            Refresh();
            return;
        }
        if (item == null)
        {
            if (!Input.IsKeyPressed(Key.Shift))
                _picked.Clear();
        }
        else if (Input.IsKeyPressed(Key.Shift))
        {
            if (!_picked.Remove(item))
                _picked.Add(item);
        }
        else if (!_picked.Contains(item))
        {
            _picked.Clear();
            _picked.Add(item);
        }
        if (item != null && _picked.Contains(item))
        {
            _dragFrom = position;
            _start = _picked.ToDictionary(o => o, Sharpness);
        }
        Refresh();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_dragFrom is not { } from || View.Document is not { } doc)
            return;
        var delta = (from.Y - position.Y) * PerPixel;
        var snap = Input.IsKeyPressed(Key.Shift);
        var values = _start.ToDictionary(kv => kv.Key, kv =>
        {
            var start = double.IsPositiveInfinity(kv.Value) ? 10 : kv.Value;
            var s = Math.Max(0, start + delta);
            return snap ? Math.Round(s) : s;
        });
        doc.Preview("Adjust Crease", _ =>
        {
            foreach (var (o, s) in values)
                SetSharpness(o, s);
        });
        View.ShowVcbValue(VcbValue);
        View.QueueOverlayRedraw();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _dragFrom == null)
            return;
        _dragFrom = null;
        View.Document?.CommitPreview();
        Refresh();
    }

    public override bool ApplyVcb(string text)
    {
        if (_picked.Count == 0 || View.Document is not { } doc)
            return false;
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) || s < 0)
            return false;
        var items = _picked.ToList();
        doc.Operation("Adjust Crease", _ => items.ForEach(o => SetSharpness(o, s)));
        Refresh();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _dragFrom != null)
        {
            _dragFrom = null;
            View.Document?.CancelPreview();
            Refresh();
            return true;
        }
        return base.KeyDown(key);
    }

    private void Refresh()
    {
        View.ShowVcbValue(VcbValue);
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        if (View.Document is not { } doc)
            return;
        var xf = doc.Context.ToWorld;
        var font = overlay.GetThemeDefaultFont();
        var picked = new Color(0.1f, 0.3f, 1f);
        var creased = doc.Context.Entities.Edges.Where(e => e.Crease > 0).Cast<object>()
            .Concat(doc.Context.Entities.Vertices.Where(v => v.Crease > 0)).Concat(_picked).Distinct();
        foreach (var o in creased)
        {
            if (o is Edge e && View.ToScreen(xf.ApplyPoint(e.Start.Position)) is { } a && View.ToScreen(xf.ApplyPoint(e.End.Position)) is { } b)
                overlay.DrawLine(a, b, _picked.Contains(o) ? picked : new Color(0.8f, 0.1f, 0.6f), 3);
            if (o is Vertex && View.ToScreen(At(o, xf)) is { } p)
                overlay.DrawRect(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), _picked.Contains(o) ? picked : new Color(0.8f, 0.1f, 0.6f));
            if (View.ToScreen(At(o, xf)) is { } at)
            {
                var label = Format(Sharpness(o));
                var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 12);
                var box = new Rect2(at + new Vector2(6, -size.Y - 4), size + new Vector2(6, 4));
                overlay.DrawRect(box, _picked.Contains(o) ? picked : new Color(0.25f, 0.25f, 0.25f));
                overlay.DrawString(font, box.Position + new Vector2(3, size.Y - 1), label, HorizontalAlignment.Left, -1, 12, Colors.White);
            }
        }
    }
}
