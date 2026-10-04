using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Line tool: click, click, click… draws a chain of edges; closing a loop makes a face and ends the
/// chain. Typing a length draws that long along the current direction. Esc cancels the chain.
/// </summary>
public sealed class LineTool : DrawingTool
{
    private Vec3? _start;

    public override int CommandId => CommandIds.Line;
    public override string CursorImage => "pencil";
    protected override Vec3? From => _start;
    public override string VcbLabel => "Length";
    public override string StatusText => _start == null ? "Select start point." : "Click to set second endpoint or enter length.";

    public override string VcbValue => _start is { } s && Current is { } c ? Length.Format(s.DistanceTo(c.Point), LengthUnit.Millimeters, 1) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_start is not { } start)
        {
            _start = inf.Point;
            RefreshStatus();
            return;
        }
        Segment(start, inf.Point);
    }

    public override bool ApplyVcb(string text)
    {
        if (_start is not { } start || Current is not { } inf || !Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm == 0)
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
        // Closing a face ends the chain, as in SketchUp.
        _start = facesAfter > facesBefore ? null : b;
        RefreshStatus();
        UpdateInference();
    }

    public override bool KeyDown(InputEventKey key)
    {
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
