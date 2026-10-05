using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Section Plane: the plane follows the face under the cursor (or the axis plane facing the viewer),
/// with its arrows pointing into the face; a click asks for its name and symbol, places it and makes it the active
/// cut of the context. Shift holds the current plane; arrow keys lock it to the red, green or blue axis.
/// </summary>
public sealed class SectionPlaneTool : DrawingTool
{
    private Vec3? _locked;
    private bool _shiftLocked;

    public override int CommandId => CommandIds.SectionPlane;
    public override string StatusText => "Place section plane on face.  Shift = Lock to plane.";

    /// <summary>Arrow direction (world) for the cursor: into the face under it.</summary>
    private Vec3 Normal(InferenceResult inf) =>
        _locked ?? (inf.Face is { } f ? -inf.EntityToWorld.ApplyNormal(f.Normal).Normalized() : MostFacingPlane());

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        var toLocal = doc.Context.ToWorld.Inverse();
        var plane = new SectionPlane(toLocal.ApplyPoint(inf.Point), toLocal.ApplyNormal(Normal(inf)).Normalized());
        var number = doc.Context.Entities.SectionPlanes.Count + 1;
        void Place(string name, string symbol) => doc.Operation("Section Plane", e =>
        {
            plane.Name = name;
            plane.Symbol = symbol;
            e.SectionPlanes.Add(plane);
            e.ActiveSection = plane;
        });
        if (UI.AppPreferences.Current.AskSectionName)
            UI.SectionNameDialog.Show(View, $"Section Plane {number}", number.ToString(), Place);
        else
            Place($"Section Plane {number}", number.ToString());
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Shift && !key.Echo && _locked == null && Current is { } inf)
        {
            _locked = Normal(inf);
            _shiftLocked = true;
            return true;
        }
        Vec3? axis = key.Keycode switch
        {
            Key.Right => Red,
            Key.Left => Green,
            Key.Up => Blue,
            _ => null,
        };
        if (axis is { } a)
        {
            _locked = _locked is { } l && Math.Abs(l.Dot(a)) > 0.99 ? null : a;
            _shiftLocked = false;
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override bool KeyUp(InputEventKey key)
    {
        if (key.Keycode == Key.Shift && _shiftLocked)
        {
            _locked = null;
            _shiftLocked = false;
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyUp(key);
    }

    public override void Draw(Control overlay)
    {
        if (Current is { } inf && View.Document is { } doc)
        {
            // Preview at a size that reads on screen.
            var half = 40 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(inf.Point));
            var preview = new SectionPlane(inf.Point, Normal(inf));
            foreach (var (a, b) in AnnotationOverlay.SectionPlaneLines(preview, Transform.Identity, half))
                DrawWorldLine(overlay, a, b, new Color("#f28c28"), 1.5f);
        }
        DrawInference(overlay);
    }
}
