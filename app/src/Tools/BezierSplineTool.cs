using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>BezierSpline's curve tools: click control points (Esc removes the last), double-click or Return to finish;
/// "Ns" sets the precision, a length the family's extra value; F8/F9 close the curve, F7 opens it.</summary>
public sealed class BezierSplineTool(SplineKind kind) : DrawingTool
{
    private static readonly Dictionary<SplineKind, (int Precision, double Parameter)> Settings = [];

    private readonly List<Vec3> _points = [];
    private bool _closed;
    private ulong _lastClickMs;
    private Vector2 _lastClickAt;

    private Splines.Family Info => Splines.Info(kind);

    private (int Precision, double Parameter) Setting
    {
        get => Settings.TryGetValue(kind, out var s) ? s : (Info.PrecisionDefault, kind switch
        {
            SplineKind.Segmentor => 10,
            SplineKind.Divider => 10,
            _ => 3,
        });
        set => Settings[kind] = value;
    }

    public override int CommandId => ExtensionIds.Spline(kind);
    public override string CursorImage => "pencil";
    protected override Vec3? From => _points.Count > 0 ? _points[^1] : null;
    public override string VcbLabel => Info.Parameter ?? "Precision";

    public override string VcbValue => Info.Parameter != null
        ? $"{(kind == SplineKind.Segmentor ? Setting.Parameter.ToString("0") : Length.Format(Setting.Parameter, LengthUnit.Millimeters, 2))};{Setting.Precision}s"
        : $"{Setting.Precision}s";

    public override string StatusText => _points.Count == 0
        ? $"{Info.Menu}: click the first control point."
        : $"{Info.Menu}: click the next point; double-click or Return to finish, Esc removes the last point. F8/F9 close, F7 opens.";

    public override void Activate() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 400 && position.DistanceTo(_lastClickAt) < 6;
        _lastClickMs = now;
        _lastClickAt = position;
        if (doubleClick)
        {
            Finish();
            return;
        }
        if (_points.Count == 0 || _points[^1].DistanceTo(inf.Point) > Tolerance.Length)
            _points.Add(inf.Point);
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    private List<Vec3> Curve(IReadOnlyList<Vec3> controls) =>
        Splines.Compute(kind, controls, Setting.Precision, Setting.Parameter, _closed && controls.Count > 2);

    private void Finish()
    {
        if (View.Document is not { } doc || _points.Count < Math.Max(2, Info.MinPoints))
            return;
        var toLocal = doc.Context.ToWorld.Inverse();
        var controls = _points.Select(toLocal.ApplyPoint).ToList();
        var pts = Curve(controls);
        // Closed curves come back with their first point repeated at the end.
        var closed = pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < Tolerance.Length;
        if (closed)
            pts.RemoveAt(pts.Count - 1);
        var curve = new Curve { Segments = pts.Count, Spline = new SplineData(kind, controls, Setting.Precision, Setting.Parameter, _closed) };
        doc.Operation(Info.Menu, e => StickyGeometry.DrawEdges(e, pts, closed, null, kind == SplineKind.Polyline ? null : curve));
        _points.Clear();
        _closed = false;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        var any = false;
        foreach (var part in text.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim().ToLowerInvariant();
            if (t.EndsWith('s') && int.TryParse(t[..^1], out var n))
            {
                if (n < Info.PrecisionMin || n > Info.PrecisionMax)
                    return false;
                Setting = (n, Setting.Parameter);
                any = true;
            }
            else if (Info.Parameter != null && kind == SplineKind.Segmentor && int.TryParse(t, out var count) && count > 0)
            {
                Setting = (Setting.Precision, count);
                any = true;
            }
            else if (Info.Parameter != null && Length.TryParse(t, LengthUnit.Millimeters, out var mm) && mm > 0)
            {
                Setting = (Setting.Precision, mm);
                any = true;
            }
            else
                return false;
        }
        View.ShowVcbValue(VcbValue);
        View.QueueOverlayRedraw();
        return any;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter when _points.Count > 0:
                Finish();
                return true;
            case Key.Escape when _points.Count > 0:
                _points.RemoveAt(_points.Count - 1);
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.F8 or Key.F9:
                _closed = true;
                View.QueueOverlayRedraw();
                return true;
            case Key.F7:
                _closed = false;
                View.QueueOverlayRedraw();
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        var controls = _points.ToList();
        if (Current is { } inf && controls.Count > 0)
            controls.Add(inf.Point);
        for (var i = 0; i + 1 < controls.Count; i++)
            DrawWorldLine(overlay, controls[i], controls[i + 1], new Color(0.55f, 0.55f, 0.55f), 1, dashed: true);
        if (controls.Count >= 2)
        {
            var pts = Curve(controls);
            for (var i = 0; i + 1 < pts.Count; i++)
                DrawWorldLine(overlay, pts[i], pts[i + 1], Colors.Black, 2);
        }
        foreach (var p in _points)
            if (View.ToScreen(p) is { } s)
                overlay.DrawRect(new Rect2(s - new Vector2(3, 3), new Vector2(6, 6)), new Color(0.85f, 0.1f, 0.1f));
        DrawInference(overlay);
    }
}
