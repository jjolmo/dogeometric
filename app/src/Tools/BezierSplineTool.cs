using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>BezierSpline's curve tools: click control points (Esc removes the last), double-click or Return to finish;
/// "Ns" sets the precision, a length the family's extra value; F9 closes nicely, F8 with a line, F7 opens, F5 toggles
/// vertex marks, TAB opens the extra parameters.</summary>
public sealed class BezierSplineTool(SplineKind kind) : DrawingTool
{
    private static readonly Dictionary<SplineKind, (int Precision, double Parameter)> Settings = [];

    public enum Closure { Open, Nice, Line }

    /// <summary>The BZ toolbar's toggles, shared by every curve tool for the session.</summary>
    public static Closure CloseMode { get; set; }
    public static bool VertexMarks { get; set; }

    private readonly List<Vec3> _points = [];
    private bool _closed => CloseMode == Closure.Nice;
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
        doc.Operation(Info.Menu, e => Splines.Draw(e, new SplineData(kind, controls, Setting.Precision, Setting.Parameter, _closed, CloseMode == Closure.Line)));
        _points.Clear();
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>TAB: the family's precision and extra value in a dialog.</summary>
    public void ShowExtras()
    {
        var d = new ConfirmationDialog { Title = $"{Info.Menu} - Parameters", Theme = UI.LightTheme.Create() };
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Precision (segments)" });
        var precision = new SpinBox { MinValue = Info.PrecisionMin, MaxValue = Info.PrecisionMax, Value = Setting.Precision, Editable = Info.PrecisionMin != Info.PrecisionMax };
        grid.AddChild(precision);
        SpinBox? parameter = null;
        if (Info.Parameter != null)
        {
            grid.AddChild(new Label { Text = Info.Parameter });
            parameter = new SpinBox { MinValue = 0.01, MaxValue = 100000, Step = kind == SplineKind.Segmentor ? 1 : 0.1, Value = Setting.Parameter, Suffix = kind == SplineKind.Segmentor ? "" : "mm" };
            grid.AddChild(parameter);
        }
        d.AddChild(grid);
        d.Confirmed += () =>
        {
            Setting = ((int)precision.Value, parameter?.Value ?? Setting.Parameter);
            View.ShowVcbValue(VcbValue);
            View.QueueOverlayRedraw();
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        View.AddChild(d);
        d.PopupCentered();
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
            case Key.F9:
                CloseMode = CloseMode == Closure.Nice ? Closure.Open : Closure.Nice;
                View.QueueOverlayRedraw();
                return true;
            case Key.F8:
                CloseMode = CloseMode == Closure.Line ? Closure.Open : Closure.Line;
                View.QueueOverlayRedraw();
                return true;
            case Key.F7:
                CloseMode = Closure.Open;
                View.QueueOverlayRedraw();
                return true;
            case Key.F5:
                VertexMarks = !VertexMarks;
                View.QueueOverlayRedraw();
                return true;
            case Key.Tab:
                ShowExtras();
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
            if (CloseMode == Closure.Line && pts.Count > 2)
                DrawWorldLine(overlay, pts[^1], pts[0], new Color(0.85f, 0.1f, 0.1f), 2);
            if (VertexMarks)
                foreach (var p in pts)
                    if (View.ToScreen(p) is { } s)
                        overlay.DrawRect(new Rect2(s - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), new Color(0, 0.85f, 0.9f));
        }
        foreach (var p in _points)
            if (View.ToScreen(p) is { } s)
                overlay.DrawRect(new Rect2(s - new Vector2(3, 3), new Vector2(6, 6)), new Color(0.85f, 0.1f, 0.1f));
        DrawInference(overlay);
    }
}

/// <summary>BezierSpline's Edit: click a curve to show its control points, drag one to move it, double-click a point
/// to remove it or the control polygon to add one; "Ns" changes the precision. Each change is one undoable step.</summary>
public sealed class BezierEditTool : DrawingTool
{
    private const float Grab = 8;
    private Curve? _curve;
    private List<Vec3> _controls = [];
    private int _dragging = -1;
    private ulong _lastClickMs;
    private Vector2 _lastClickAt;

    public override int CommandId => ExtensionIds.SplineEdit;
    public override string CursorImage => "select";
    public override string VcbLabel => "Precision";
    public override string VcbValue => _curve?.Spline is { } s ? $"{s.Precision}s" : "";

    public override string StatusText => _curve == null
        ? "Click a BezierSpline curve to edit it."
        : "Drag a control point to move it. Double-click a point to remove it, or the dashed polygon to add one.";

    public override void Activate()
    {
        if (View.Document is not { } doc)
            return;
        doc.Undo.Changed += Forget;
        if (doc.Selection.Items.OfType<Edge>().FirstOrDefault(e => e.Curve?.Spline != null) is { } edge)
            Load(edge.Curve!);
    }

    public override void Deactivate()
    {
        if (View.Document is { } doc)
            doc.Undo.Changed -= Forget;
    }

    /// <summary>After Undo or Redo the curve being edited may be gone.</summary>
    private void Forget()
    {
        if (_curve == null || View.Document?.Context.Entities.Edges.Any(e => e.Curve == _curve) == true)
            return;
        _curve = null;
        _dragging = -1;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    private void Load(Curve curve)
    {
        _curve = curve;
        var xf = View.Document!.Context.ToWorld;
        _controls = curve.Spline!.ControlPoints.Select(xf.ApplyPoint).ToList();
        View.ShowVcbValue(VcbValue);
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    private int PointAt(Vector2 position)
    {
        for (var i = 0; i < _controls.Count; i++)
            if (View.ToScreen(_controls[i]) is { } s && s.DistanceTo(position) <= Grab)
                return i;
        return -1;
    }

    /// <summary>The control polygon's side under the cursor, as the index of its first point.</summary>
    private int SideAt(Vector2 position)
    {
        var closed = _curve?.Spline?.Closed == true;
        var sides = closed ? _controls.Count : _controls.Count - 1;
        for (var i = 0; i < sides; i++)
            if (View.ToScreen(_controls[i]) is { } a && View.ToScreen(_controls[(i + 1) % _controls.Count]) is { } b
                && Geometry2D.GetClosestPointToSegment(position, a, b).DistanceTo(position) <= Grab / 2)
                return i;
        return -1;
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 400 && position.DistanceTo(_lastClickAt) < 6;
        _lastClickMs = now;
        _lastClickAt = position;
        if (_curve?.Spline is { } data)
        {
            var point = PointAt(position);
            if (doubleClick && point >= 0)
            {
                if (_controls.Count > Math.Max(2, Splines.Info(data.Kind).MinPoints))
                {
                    _controls.RemoveAt(point);
                    Apply("Remove Control Point");
                }
                return;
            }
            if (doubleClick && SideAt(position) is >= 0 and var side && Current is { } inf)
            {
                _controls.Insert(side + 1, inf.Point);
                Apply("Add Control Point");
                return;
            }
            if (point >= 0)
            {
                _dragging = point;
                return;
            }
        }
        if (View.Pick(position) is { Edge: { Curve.Spline: not null } edge } && doc.Context.Entities.Edges.Contains(edge))
            Load(edge.Curve!);
        else if (SideAt(position) < 0)
        {
            _curve = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
        }
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_dragging >= 0 && Current is { } inf)
            _controls[_dragging] = inf.Point;
        View.QueueOverlayRedraw();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _dragging < 0)
            return;
        _dragging = -1;
        Apply("Edit Bezier Curve");
    }

    private SplineData? Edited(int? precision = null)
    {
        if (_curve?.Spline is not { } data || View.Document is not { } doc)
            return null;
        var toLocal = doc.Context.ToWorld.Inverse();
        return data with { ControlPoints = _controls.Select(toLocal.ApplyPoint).ToList(), Precision = precision ?? data.Precision };
    }

    private void Apply(string name, int? precision = null)
    {
        if (_curve is not { } curve || Edited(precision) is not { } data || View.Document is not { } doc)
            return;
        List<Edge> edges = [];
        doc.Operation(name, e => edges = Splines.Edit(e, curve, data));
        doc.Selection.Set(edges);
        if (edges.FirstOrDefault()?.Curve is { } fresh)
            Load(fresh);
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (_curve?.Spline is not { } data || !t.EndsWith('s') || !int.TryParse(t[..^1], out var n))
            return false;
        var info = Splines.Info(data.Kind);
        if (n < info.PrecisionMin || n > info.PrecisionMax)
            return false;
        Apply("Edit Bezier Curve", n);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _dragging >= 0)
        {
            _dragging = -1;
            if (_curve != null)
                Load(_curve);
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_curve?.Spline is { } data)
        {
            var closed = data.Closed && _controls.Count > 2;
            for (var i = 0; i + 1 < _controls.Count + (closed ? 1 : 0); i++)
                DrawWorldLine(overlay, _controls[i], _controls[(i + 1) % _controls.Count], new Color(0.55f, 0.55f, 0.55f), 1, dashed: true);
            var pts = Splines.Compute(data.Kind, _controls, data.Precision, data.Parameter, closed);
            for (var i = 0; i + 1 < pts.Count; i++)
                DrawWorldLine(overlay, pts[i], pts[i + 1], new Color(0.1f, 0.2f, 0.9f), 2);
            for (var i = 0; i < _controls.Count; i++)
                if (View.ToScreen(_controls[i]) is { } s)
                    overlay.DrawRect(new Rect2(s - new Vector2(4, 4), new Vector2(8, 8)), i == _dragging ? new Color(1, 0.6f, 0) : new Color(0.85f, 0.1f, 0.1f));
        }
        DrawInference(overlay);
    }
}
