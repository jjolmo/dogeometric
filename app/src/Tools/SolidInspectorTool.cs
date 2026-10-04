using Dogeometric.App.Commands;
using Dogeometric.App.UI;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;
using Transform = Dogeometric.Core.Geometry.Transform;

namespace Dogeometric.App.Tools;

/// <summary>
/// Solid Inspector² (Tools menu): inspects the selected group or component — or the active context when none is
/// selected — and marks in red what keeps it from being a solid. Click another group to inspect it; Tab /
/// Shift+Tab and the arrow keys step through the errors, Return (and Tab) zooms to the current one. Its window lists
/// the errors by kind, fixes one kind or all of them, and picking a kind shows only those.
/// </summary>
public sealed class SolidInspectorTool : Tool
{
    private static readonly Color EdgeColor = new(1, 0, 0);
    private static readonly Color FaceColor = new(1, 0, 0, 0.5f);
    private static readonly Color CurrentColor = new(1, 0.6f, 0);

    private Entities? _entities;
    private ComponentInstance? _instance;
    private Transform _toWorld = Transform.Identity;
    private List<SolidError> _errors = [];
    private SolidErrorKind? _filter;
    private int? _current;
    private SolidInspectorWindow? _window;
    private Document? _watched;

    /// <summary>Short edges are checked only when turned on (the extension's context menu); the threshold is in mm.</summary>
    public static bool DetectShortEdges { get; set; }

    public static double ShortEdgeThreshold { get; set; } = SolidInspector.DefaultShortEdge;

    public override int CommandId => ExtensionIds.SolidInspector;
    public override string CursorImage => "select";

    public override string StatusText =>
        "Click on solids to inspect. Use arrow keys to cycle between errors. Press Return to zoom to error. Press Tab/Shift+Tab to cycle though errors and zoom.";

    private List<SolidError> Shown => _filter is { } kind ? _errors.Where(e => e.Kind == kind).ToList() : _errors;

    public override void Activate()
    {
        _window = SolidInspectorWindow.Open(View, FixAll, FixKind, kind =>
        {
            _filter = kind;
            _current = null;
            _window?.List(_errors, _filter);
            View.QueueOverlayRedraw();
        });
        _window.CloseRequested += () => Manager.Activate(new SelectTool());
        if (View.Document is { } doc)
        {
            _watched = doc;
            doc.GeometryChanged += OnGeometryChanged;
        }
        Analyze();
    }

    public override void Deactivate()
    {
        if (_watched != null)
            _watched.GeometryChanged -= OnGeometryChanged;
        _watched = null;
        _window?.QueueFree();
        _window = null;
        View.QueueOverlayRedraw();
    }

    // Undo, redo or edits elsewhere: look again (the errors may be gone or point at erased entities).
    private void OnGeometryChanged(IReadOnlyCollection<Entities> changed)
    {
        if (_entities != null && changed.Contains(_entities))
            Analyze(keepTarget: true);
    }

    /// <summary>What to inspect: the selected group or component, else the active context.</summary>
    private void Analyze(bool keepTarget = false)
    {
        if (View.Document is not { } doc)
            return;
        if (!keepTarget)
        {
            _instance = doc.Selection.Items.OfType<ComponentInstance>().FirstOrDefault(i => !i.Definition.IsImage);
            _entities = _instance?.Definition.Entities ?? doc.Context.Entities;
            _toWorld = _instance != null ? _instance.Transform.Then(doc.Context.ToWorld) : doc.Context.ToWorld;
        }
        _errors = _entities == null ? [] : SolidInspector.Find(_entities, DetectShortEdges ? ShortEdgeThreshold : null);
        _current = null;
        _window?.List(_errors, _filter);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        // A click picks the group or component of the active context under the cursor, or none (the context).
        ComponentInstance? picked = null;
        if (View.Pick(position) is { } hit)
        {
            var depth = doc.Context.Path.Count;
            if (hit.Path.Count > depth && hit.Path.Take(depth).SequenceEqual(doc.Context.Path))
                picked = hit.Path[depth];
        }
        doc.Selection.Set(picked != null ? [picked] : []);
        Analyze();
    }

    public override bool KeyDown(InputEventKey key)
    {
        var errors = Shown;
        switch (key.Keycode)
        {
            case Key.Escape:
                Manager.Activate(new SelectTool());
                return true;
            case Key.Tab or Key.Up or Key.Right or Key.Down or Key.Left when errors.Count > 0:
                var back = key.Keycode is Key.Down or Key.Left || (key.Keycode == Key.Tab && key.ShiftPressed);
                _current = _current is not { } i ? 0 : (i + (back ? -1 : 1) + errors.Count) % errors.Count;
                if (key.Keycode == Key.Tab)
                    ZoomToCurrent();
                View.QueueOverlayRedraw();
                return true;
            case Key.Enter or Key.KpEnter when _current != null:
                ZoomToCurrent();
                return true;
        }
        return false;
    }

    private void ZoomToCurrent()
    {
        if (_current is not { } i || i >= Shown.Count)
            return;
        var bounds = Bounds3.Empty;
        foreach (var p in Points(Shown[i]))
            bounds = bounds.Include(p);
        if (!bounds.IsEmpty)
            View.ZoomToBounds(bounds);
    }

    /// <summary>The error's corner points in world space.</summary>
    private IEnumerable<Vec3> Points(SolidError error)
    {
        foreach (var item in error.Entities)
        {
            switch (item)
            {
                case Edge edge:
                    yield return _toWorld.ApplyPoint(edge.Start.Position);
                    yield return _toWorld.ApplyPoint(edge.End.Position);
                    break;
                case Face face:
                    foreach (var p in face.OuterLoop.Points)
                        yield return _toWorld.ApplyPoint(p);
                    break;
                case ComponentInstance inst:
                    var b = inst.Definition.Entities.Bounds();
                    if (b.IsEmpty)
                        break;
                    var to = inst.Transform.Then(_toWorld);
                    foreach (var corner in b.Corners())
                        yield return to.ApplyPoint(corner);
                    break;
            }
        }
    }

    private void FixAll()
    {
        if (!Fix("Repair Solid", _errors))
            _window?.Message("Some errors could not be automatically fixed. Manually inspect and correct the errors, then run the tool again.");
    }

    private void FixKind(SolidErrorKind kind)
    {
        if (!Fix("Repair Solid", _errors.Where(e => e.Kind == kind).ToList()))
            _window?.Message(SolidInspector.Description(kind));
    }

    private bool Fix(string name, List<SolidError> errors)
    {
        if (View.Document is not { } doc || _entities == null || errors.Count == 0)
            return true;
        var allFixed = true;
        doc.Undo.Begin(name, _entities);
        try
        {
            allFixed = SolidInspector.Fix(_entities, errors);
            doc.Undo.Commit();
        }
        catch
        {
            doc.Undo.Abort();
            throw;
        }
        Analyze(keepTarget: true);
        return allFixed;
    }

    public override void Draw(Control overlay)
    {
        var errors = Shown;
        for (var i = 0; i < errors.Count; i++)
            DrawError(overlay, errors[i]);
        if (_current is { } c && c < errors.Count)
            DrawCurrent(overlay, errors[c]);
    }

    private void DrawError(Control overlay, SolidError error)
    {
        foreach (var item in error.Entities)
        {
            switch (item)
            {
                case Face face:
                    FillFace(overlay, face);
                    if (error.Kind is SolidErrorKind.HiddenFace or SolidErrorKind.ReversedFace)
                        foreach (var edge in Topology.EdgesOf(face))
                            Line(overlay, edge);
                    break;
                case Edge edge:
                    Line(overlay, edge);
                    break;
                case ComponentInstance inst:
                    DrawBox(overlay, inst);
                    break;
            }
        }
    }

    private void Line(Control overlay, Edge edge)
    {
        if (View.ToScreen(_toWorld.ApplyPoint(edge.Start.Position)) is { } a && View.ToScreen(_toWorld.ApplyPoint(edge.End.Position)) is { } b)
            overlay.DrawLine(a, b, EdgeColor, 3, antialiased: true);
    }

    private void FillFace(Control overlay, Face face)
    {
        var points = new List<Vector2>();
        foreach (var p in face.OuterLoop.Points)
        {
            if (View.ToScreen(_toWorld.ApplyPoint(p)) is not { } s)
                return;
            points.Add(s);
        }
        var polygon = points.ToArray();
        if (Geometry2D.TriangulatePolygon(polygon).Length > 0)
            overlay.DrawColoredPolygon(polygon, FaceColor);
    }

    private void DrawBox(Control overlay, ComponentInstance inst)
    {
        var b = inst.Definition.Entities.Bounds();
        if (b.IsEmpty)
            return;
        var to = inst.Transform.Then(_toWorld);
        var c = b.Corners().Select(p => View.ToScreen(to.ApplyPoint(p))).ToArray();
        // Corners are numbered by bits (x, y, z): edges join corners differing in one bit.
        for (var i = 0; i < 8; i++)
            foreach (var bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0 && c[i] is { } p && c[i | bit] is { } q)
                    overlay.DrawLine(p, q, EdgeColor, 2, antialiased: true);
    }

    /// <summary>The current error: a circle around it, as the extension marks it.</summary>
    private void DrawCurrent(Control overlay, SolidError error)
    {
        var screen = Points(error).Select(View.ToScreen).OfType<Vector2>().ToList();
        if (screen.Count == 0)
            return;
        var min = screen.Aggregate((a, b) => a.Min(b));
        var max = screen.Aggregate((a, b) => a.Max(b));
        var radius = Math.Max((max - min).Length() / 2 + 12, 20);
        overlay.DrawArc((min + max) / 2, radius, 0, Mathf.Tau, 48, CurrentColor, 3, antialiased: true);
    }
}
