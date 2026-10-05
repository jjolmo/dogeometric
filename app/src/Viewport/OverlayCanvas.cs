using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Draws the active tool's 2D feedback on top of the 3D view.</summary>
public partial class OverlayCanvas : Control
{
    /// <summary>Above this many centres, loose faces' markers are left out (they still snap) to keep drawing fast.</summary>
    private const int MaxCenterMarkers = 2000;

    private void DrawCenters(ModelViewport view)
    {
        var centers = view.CenterPoints();
        var crowded = centers.Count > MaxCenterMarkers;
        var area = GetRect().Grow(8);
        foreach (var c in centers)
        {
            if (crowded && c.Label == "Center of Face")
                continue;
            if (view.ToScreen(c.Point) is { } p && area.HasPoint(p))
                Tools.CenterMarker.Draw(this, p, Tools.CenterMarker.Color with { A = 0.8f }, 4);
        }
    }

    /// <summary>Model Info › Components › Show component axes: each component's axes at its origin.</summary>
    private void DrawComponentAxes(ModelViewport view)
    {
        if (view.Document is not { Model.Options.ShowComponentAxes: true } doc)
            return;
        var p = UI.AppPreferences.Current;
        Color[] colors = [p.RedAxis, p.GreenAxis, p.BlueAxis];
        void Walk(Dogeometric.Core.Modeling.Entities e, Dogeometric.Core.Geometry.Transform parent)
        {
            foreach (var inst in e.Instances.Where(i => !i.Hidden))
            {
                var xf = inst.Transform.Then(parent);
                if (!inst.IsGroup && view.ToScreen(xf.ApplyPoint(Dogeometric.Core.Geometry.Vec3.Zero)) is { } o)
                {
                    Dogeometric.Core.Geometry.Vec3[] axes = [xf.X, xf.Y, xf.Z];
                    for (var k = 0; k < 3; k++)
                        if (view.ToScreen(xf.ApplyPoint(Dogeometric.Core.Geometry.Vec3.Zero) + axes[k].Normalized()) is { } end && end.DistanceTo(o) > 1e-3)
                            DrawLine(o, o + (end - o).Normalized() * 30, colors[k], 2);
                }
                Walk(inst.Definition.Entities, xf);
            }
        }
        Walk(doc.Model.Entities, Dogeometric.Core.Geometry.Transform.Identity);
    }

    /// <summary>A camera's frame: what falls outside its proportions is darkened, as Advanced Camera Tools shows it.</summary>
    private void DrawFrame(ModelViewport view)
    {
        if (view.FrameAspect is not { } aspect)
            return;
        var size = Size;
        var (w, h) = ((float)(size.Y * aspect), size.Y);
        if (w > size.X)
            (w, h) = (size.X, (float)(size.X / aspect));
        var shade = new Color(0, 0, 0, 0.55f);
        var (x0, y0) = ((size.X - w) / 2, (size.Y - h) / 2);
        DrawRect(new Rect2(0, 0, size.X, y0), shade);
        DrawRect(new Rect2(0, y0 + h, size.X, size.Y - y0 - h), shade);
        DrawRect(new Rect2(0, y0, x0, h), shade);
        DrawRect(new Rect2(x0 + w, y0, size.X - x0 - w, h), shade);
    }

    public ModelViewport? View { get; set; }

    public override void _Draw()
    {
        if (View == null)
            return;
        DrawFrame(View);
        DrawCenters(View);
        DrawComponentAxes(View);
        View.Annotations.Draw(View, this);
        View.Tools?.Active.Draw(this);
    }
}
