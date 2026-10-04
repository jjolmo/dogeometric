using Godot;

namespace Dogeometric.App.Tools;

/// <summary>The centre point marker (View › Center Points): a ring with a cross, like a target.</summary>
public static class CenterMarker
{
    public static readonly Color Color = new("#e07b00");

    public static void Draw(Control overlay, Vector2 p, Color color, float r)
    {
        var rim = new Color("#f8f9fc", color.A);
        overlay.DrawArc(p, r, 0, Mathf.Tau, 24, rim, 3.5f, true);
        overlay.DrawArc(p, r, 0, Mathf.Tau, 24, color, 2, true);
        overlay.DrawLine(p - new Vector2(r + 3, 0), p + new Vector2(r + 3, 0), color, 1.5f, true);
        overlay.DrawLine(p - new Vector2(0, r + 3), p + new Vector2(0, r + 3), color, 1.5f, true);
    }
}
