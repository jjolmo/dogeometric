using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Push/Pull: hover a face, click, move to set the distance (snapping to other geometry), click to
/// finish or type a distance. Ctrl toggles "create new starting face", Alt toggles Stretch Mode (the face moves and
/// the faces around it stretch); double-click repeats the last distance.
/// </summary>
public sealed class PushPullTool : DrawingTool
{
    private static double _lastDistance;
    private static bool _stretch;

    private Face? _hover;
    private Face? _face;
    private Vec3 _anchor;
    private Vec3 _normal;
    private Vec3 _localNormal;
    private double _distance;
    private ulong _lastClickMs;

    public override int CommandId => CommandIds.PushPull;
    public override string CursorImage => _face != null || _hover != null ? "pushpull" : "pushpullno";
    public override string VcbLabel => "Distance";
    public override Input.CursorShape Cursor => Input.CursorShape.PointingHand;

    public override string StatusText => (_face, _stretch) switch
    {
        (null, false) => "Click to select the face that you want to push or pull.  Alt = Toggle Stretch Mode.",
        (null, true) => "Click to select the face that you want to stretch.  Alt = Toggle Stretch Mode.",
        (_, false) => "Click to set face or enter distance.  Ctrl = Toggle Create New Starting Face.  Alt = Toggle Stretch Mode.",
        _ => "Click to set the faces you're stretching or enter distance.  Alt = Toggle Stretch Mode.",
    };

    /// <summary>Push/pulls the face, or in Stretch Mode moves it along its normal so the faces around it stretch.</summary>
    private static void Run(Entities e, Face face, double distance, bool keep)
    {
        if (_stretch)
            Transforming.Move(e, [face], face.Normal.Normalized() * distance);
        else
            PushPull.Apply(e, face, distance, keep);
    }

    public override string VcbValue => _face != null ? UI.Measure.Show(_distance) : "";

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
        ShowPreview();
    }

    /// <summary>The real push/pull at the current distance, redone on every move (SketchUp shows the solid live).</summary>
    private void ShowPreview()
    {
        if (_face is not { } face || View.Document is not { } doc)
            return;
        var scale = doc.Context.ToWorld.ApplyVector(_localNormal).Length;
        var keep = Input.IsKeyPressed(Key.Ctrl);
        var d = _distance / scale;
        try
        {
            doc.Preview(_stretch ? "Stretch" : "Push/Pull", e =>
            {
                if (Math.Abs(d) > Tolerance.Length)
                    Run(e, face, d, keep);
            });
        }
        catch (Exception)
        {
            // A distance the geometry can't take (e.g. pushing through itself mid-drag): keep the last good preview.
        }
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
            // Pre-pick: with one face selected beforehand, a click anywhere push/pulls that face.
            var picked = UI.AppPreferences.Current.DisablePushPullPrePick ? null
                : doc.Selection.Items.Count == 1 && doc.Selection.Items.First() is Face only && doc.Context.Entities.Faces.Contains(only) ? only : null;
            var face = picked ?? FaceUnderCursor(position);
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
            _localNormal = face.Normal;
            _normal = toWorld.ApplyNormal(face.Normal);
            _distance = 0;
            RefreshStatus();
            return;
        }
        Apply(doc, _face, _distance);
    }

    public override bool ApplyVcb(string text)
    {
        if (!UI.Measure.Read(text, out var mm) || View.Document is not { } doc)
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
        // Finishing a live drag: the preview already holds the result at this distance; typed values redo it.
        if (doc.Undo.IsPending)
        {
            _distance = distance;
            ShowPreview();
            if (Math.Abs(distance) > Tolerance.Length)
            {
                doc.CommitPreview();
                _lastDistance = distance;
            }
            else
            {
                doc.CancelPreview();
            }
            _face = null;
            _hover = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return;
        }
        if (Math.Abs(distance) > Tolerance.Length)
        {
            // World distance along the world normal → local distance along the local normal.
            var local = doc.Context.ToWorld;
            var scale = local.ApplyVector(face.Normal).Length;
            var keep = Input.IsKeyPressed(Key.Ctrl);
            doc.Operation(_stretch ? "Stretch" : "Push/Pull", e => Run(e, face, distance / scale, keep));
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
        if (Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.Center or InferenceKind.OnEdge or InferenceKind.Origin } snap)
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
        if (key.Keycode == Key.Ctrl && !key.Echo && _face != null)
        {
            ShowPreview(); // Ctrl toggles "create new starting face": redo with the new mode
            return false;
        }
        if (key.Keycode == Key.Alt && !key.Echo)
        {
            _stretch = !_stretch;
            ShowPreview();
            RefreshStatus();
            return true;
        }
        if (key.Keycode == Key.Escape && _face != null)
        {
            View.Document?.CancelPreview();
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
        if (_face != null)
        {
            // While dragging, the model itself shows the extrusion; only the inference marker goes on top.
            DrawInference(overlay);
            return;
        }
        var blue = new Color(0, 0, 1);
        foreach (var loop in target.Loops)
        {
            var pts = loop.Points.Select(toWorld.ApplyPoint).ToList();
            for (var i = 0; i < pts.Count; i++)
                DrawWorldLine(overlay, pts[i], pts[(i + 1) % pts.Count], blue, 1.5f);
        }
    }
}
