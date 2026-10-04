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
        View.Annotations.Draw(View, this);
        View.Tools?.Active.Draw(this);
    }
}
