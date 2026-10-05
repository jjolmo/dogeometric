using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>Styles › Watermark Settings › Create / Edit Watermark: name, background or overlay, mask, blend and layout.</summary>
public static class WatermarkDialog
{
    private static readonly string[] Positions = ["Top left", "Top", "Top right", "Left", "Center", "Right", "Bottom left", "Bottom", "Bottom right"];

    public static void Show(Node parent, Watermark mark, string title, Action<Watermark> done)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = "OK" };
        var grid = new GridContainer { Columns = 2, CustomMinimumSize = new Vector2(360, 0) };
        void Row(string label, Control field)
        {
            grid.AddChild(new Label { Text = label });
            field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(field);
        }
        var name = new LineEdit { Text = mark.Name };
        Row("Name", name);
        var place = new OptionButton();
        place.AddItem("Background");
        place.AddItem("Overlay");
        place.Selected = mark.Overlay ? 1 : 0;
        Row("Display", place);
        var mask = new CheckBox { Text = "Create mask", ButtonPressed = mark.Mask };
        Row("", mask);
        var blend = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = mark.Opacity };
        Row("Blend", blend);
        var layout = new OptionButton();
        foreach (var l in new[] { "Stretched to fit the screen", "Tiled across the screen", "Positioned on the screen" })
            layout.AddItem(l);
        layout.Selected = (int)mark.Layout;
        Row("Layout", layout);
        var aspect = new CheckBox { Text = "Lock aspect ratio", ButtonPressed = mark.LockAspect };
        Row("", aspect);
        var scale = new SpinBox { MinValue = 0.05, MaxValue = 20, Step = 0.05, Value = mark.Scale };
        Row("Scale", scale);
        var position = new OptionButton();
        foreach (var p in Positions)
            position.AddItem(p);
        position.Selected = (int)mark.Position;
        Row("Position", position);
        void Enable()
        {
            aspect.Disabled = layout.Selected != (int)WatermarkLayout.Stretched;
            scale.Editable = layout.Selected != (int)WatermarkLayout.Stretched;
            position.Disabled = layout.Selected != (int)WatermarkLayout.Positioned;
        }
        layout.ItemSelected += _ => Enable();
        Enable();
        dialog.AddChild(grid);
        dialog.Confirmed += () => done(mark with
        {
            Name = name.Text,
            Overlay = place.Selected == 1,
            Mask = mask.ButtonPressed,
            Opacity = blend.Value,
            Layout = (WatermarkLayout)layout.Selected,
            LockAspect = aspect.ButtonPressed,
            Scale = scale.Value,
            Position = (WatermarkPosition)position.Selected,
        });
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
                dialog.QueueFree();
        };
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }
}
