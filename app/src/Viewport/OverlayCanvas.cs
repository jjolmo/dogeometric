using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Draws the active tool's 2D feedback on top of the 3D view.</summary>
public partial class OverlayCanvas : Control
{
    public ModelViewport? View { get; set; }

    public override void _Draw()
    {
        if (View == null)
            return;
        foreach (var c in View.CenterPoints())
            if (View.ToScreen(c.Point) is { } p)
                Tools.CenterMarker.Draw(this, p, Tools.CenterMarker.Color with { A = 0.8f }, 4);
        View.Annotations.Draw(View, this);
        View.Tools?.Active.Draw(this);
    }
}
