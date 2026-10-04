using Godot;

namespace Dogeometric.App.UI;

/// <summary>Lays toolbars out in rows like SketchUp's top dock: wrapping when full, and starting a new row at a
/// toolbar marked <see cref="Toolbar.RowStart"/>.</summary>
public partial class ToolbarRows : Container
{
    private const int Gap = 6;
    private const int RowGap = 2;

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren)
            Arrange(fit: true);
    }

    public override Vector2 _GetMinimumSize() => Arrange(fit: false);

    /// <summary>Places the children (when <paramref name="fit"/>) and returns the size they take.</summary>
    private Vector2 Arrange(bool fit)
    {
        var width = Size.X > 0 ? Size.X : float.MaxValue;
        float x = 0, y = 0, rowHeight = 0;
        foreach (var child in GetChildren().OfType<Control>().Where(c => c.Visible))
        {
            var size = child.GetCombinedMinimumSize();
            var breakRow = x > 0 && (x + size.X > width || child is Toolbar { RowStart: true });
            if (breakRow)
            {
                y += rowHeight + RowGap;
                x = rowHeight = 0;
            }
            if (fit)
                FitChildInRect(child, new Rect2(x, y, size.X, size.Y));
            x += size.X + Gap;
            rowHeight = Math.Max(rowHeight, size.Y);
        }
        return new Vector2(0, y + rowHeight);
    }
}
