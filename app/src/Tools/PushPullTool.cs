using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Push/Pull: hover a face, click, move to set the distance (snapping to other geometry), click to
/// finish or type a distance. Ctrl toggles "create new starting face"; double-click repeats the last distance.
/// </summary>
public sealed class PushPullTool : DrawingTool
{
    private static double _lastDistance;

    private Face? _hover;
    private Face? _face;
    private Vec3 _anchor;
    private Vec3 _normal;
    private double _distance;
    private ulong _lastClickMs;

    public override int CommandId => CommandIds.PushPull;
    public override string CursorImage => _face != null || _hover != null ? "pushpull" : "pushpullno";
    public override string VcbLabel => "Distance";
    public override Input.CursorShape Cursor => Input.CursorShape.PointingHand;

    public override string StatusText => _face == null
        ? "Click to select the face that you want to push or pull."
        : "Click to set face or enter distance.  Ctrl = toggle create new starting face.";

    public override string VcbValue => _face != null ? Length.Format(_distance, LengthUnit.Millimeters, 1) : "";

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_face == null)
        {
            _hover = FaceUnderCursor(position);
            View.QueueOverlayRedraw();
            return;
        }
        _distance = DistanceFromCursor();
        View.ShowVcbValue(VcbValue);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 450;
        _lastClickMs = now;

        if (_face == null)
        {
            var face = FaceUnderCursor(position);
            if (face == null)
                return;
            if (doubleClick && _lastDistance != 0)
            {
                Apply(doc, face, _lastDistance);
                return;
            }
            _face = face;
            var hit = View.Pick(position);
            var toWorld = doc.Context.ToWorld;
            _anchor = hit?.Point ?? toWorld.ApplyPoint(face.OuterLoop.Points.First());
            _normal = toWorld.ApplyNormal(face.Normal);
            _distance = 0;
            RefreshStatus();
            return;
        }
        Apply(doc, _face, _distance);
    }

    public override bool ApplyVcb(string text)
    {
        if (!Length.TryParse(text, LengthUnit.Millimeters, out var mm) || View.Document is not { } doc)
            return false;
        // Typed right after a push/pull, SketchUp redoes the last one with the new distance.
        var face = _face ?? _hover;
        if (face == null)
            return false;
        Apply(doc, face, mm);
        return true;
    }

    private void Apply(Document doc, Face face, double distance)
    {
        if (Math.Abs(distance) > Tolerance.Length)
        {
            // World distance along the world normal → local distance along the local normal.
            var local = doc.Context.ToWorld;
            var scale = local.ApplyVector(face.Normal).Length;
            var keep = Input.IsKeyPressed(Key.Ctrl);
            doc.Operation("Push/Pull", e => PushPull.Apply(e, face, distance / scale, keep));
            _lastDistance = distance;
        }
        _face = null;
        _hover = null;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>Signed distance along the face normal: snapped geometry projects onto it, otherwise the cursor ray.</summary>
    private double DistanceFromCursor()
    {
        if (Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.OnEdge or InferenceKind.Origin } snap)
            return (snap.Point - _anchor).Dot(_normal);
        var ray = View.ScreenRay(Mouse);
        var p = InferenceEngine.ClosestOnLine(new Ray(ray.Origin, ray.Direction), _anchor, _normal);
        return (p - _anchor).Dot(_normal);
    }

    private Face? FaceUnderCursor(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { Face: { } f } hit)
            return null;
        // Push/Pull works on faces of the active context only, as in SketchUp.
        return hit.Path.Count == doc.Context.Path.Count && hit.Path.SequenceEqual(doc.Context.Path) ? f : null;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _face != null)
        {
            _face = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (View.Document is not { } doc)
            return;
        var toWorld = doc.Context.ToWorld;
        var target = _face ?? _hover;
        if (target == null)
            return;
        var blue = new Color(0, 0, 1);
        var offset = _face != null ? _normal * _distance : Vec3.Zero;
        foreach (var loop in target.Loops)
        {
            var pts = loop.Points.Select(toWorld.ApplyPoint).ToList();
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                DrawWorldLine(overlay, a, b, blue, 1.5f);
                if (_face != null)
                {
                    DrawWorldLine(overlay, a + offset, b + offset, Colors.Black, 1.5f);
                    DrawWorldLine(overlay, a, a + offset, Colors.Black, 1);
                }
            }
        }
        if (_face != null)
            DrawInference(overlay);
    }
}
