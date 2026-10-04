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

    public ModelViewport? View { get; set; }

    public override void _Draw()
    {
        if (View == null)
            return;
        DrawCenters(View);
        View.Annotations.Draw(View, this);
        View.Tools?.Active.Draw(this);
    }
}
