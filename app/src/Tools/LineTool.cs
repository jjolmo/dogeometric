using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Line tool: click, click, click… draws a chain of edges; closing a loop makes a face and ends the
/// chain. Typing a length draws that long along the current direction. Esc cancels the chain. With Preferences ›
/// Drawing › Click Style, pressing and dragging draws one line ending where the button is released.
/// </summary>
public sealed class LineTool : DrawingTool
{
    // Kept for the session, as SketchUp keeps it.
    private static Core.Inference.LinearInferences _linear;

    private Vec3? _start;

    public override int CommandId => CommandIds.Line;
    public override string CursorImage => "pencil";
    protected override Vec3? From => _start;
    public override string VcbLabel => "Length";
    public override string StatusText => (_start == null ? "Click to set first endpoint." : "Click to set second endpoint or enter length.") + _linear switch
    {
        Core.Inference.LinearInferences.AllOff => "  Linear Inferencing (All Off)",
        Core.Inference.LinearInferences.ParallelPerpendicularOnly => "  Parallel/Perpendicular Inferencing Only",
        _ => "  Alt = Toggle Linear Inferences",
    };

    public override void Activate()
    {
        base.Activate();
        Inference.Linear = _linear;
    }

    public override string VcbValue => _start is { } s && Current is { } c ? UI.Measure.Show(s.DistanceTo(c.Point)) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_start is not { } start)
        {
            _start = inf.Point;
            Pressed(position);
            RefreshStatus();
            return;
        }
        Segment(start, inf.Point);
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (ReleaseFinishes(button, position) && _start is { } start && Current is { } inf)
            Segment(start, inf.Point);
    }

    public override bool StartsVcb(char c) => c is '[' or '<';

    public override bool ApplyVcb(string text)
    {
        // Typed coordinates: the first point, or the end of the next segment.
        if (TryCoordinates(text, _start, out var typed))
        {
            if (_start is { } from)
                Segment(from, typed);
            else
            {
                _start = typed;
                RefreshStatus();
                UpdateInference();
            }
            return true;
        }
        if (_start is not { } start || Current is not { } inf || !UI.Measure.Read(text, out var mm) || mm == 0)
            return false;
        var dir = (inf.Point - start).Normalized();
        if (dir.IsZero(1e-12))
            return false;
        Segment(start, start + dir * mm);
        return true;
    }

    private void Segment(Vec3 a, Vec3 b)
    {
        if (a.DistanceTo(b) <= Tolerance.Length || View.Document is not { } doc)
            return;
        var facesBefore = 0;
        var facesAfter = 0;
        doc.Operation("Line", e =>
        {
            facesBefore = e.Faces.Count;
            StickyGeometry.DrawEdges(e, [ToLocal(a), ToLocal(b)]);
            facesAfter = e.Faces.Count;
        });
        ResetLocks();
        // Closing a face ends the chain, as in SketchUp; so does every line with "Continue line drawing" off.
        _start = facesAfter > facesBefore || !UI.AppPreferences.Current.ContinueLineDrawing ? null : b;
        RefreshStatus();
        UpdateInference();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Alt && !key.Echo)
        {
            // All On, then All Off, then Parallel/Perpendicular Only, as SketchUp cycles them.
            _linear = (Core.Inference.LinearInferences)(((int)_linear + 1) % 3);
            Inference.Linear = _linear;
            RefreshStatus();
            UpdateInference();
            return true;
        }
        if (key.Keycode == Key.Escape && _start != null)
        {
            _start = null;
            ResetLocks();
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_start is { } s && Current is { } c)
            DrawWorldLine(overlay, s, c.Point, AxisColor(c.Kind == Core.Inference.InferenceKind.OnAxis ? c.AxisDirection : null), 2);
        DrawInference(overlay);
    }
}
