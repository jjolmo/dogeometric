using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's Export 2D Graphic › Options for pictures: image size, anti-alias, transparent background, JPEG quality.</summary>
public static class ExportImageDialog
{
    public static void Show(Node parent, Vector2I viewSize, bool png, Action done)
    {
        var p = AppPreferences.Current;
        var dialog = new ConfirmationDialog { Title = "Export Image Options", OkButtonText = "Export" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        box.AddChild(new Label { Text = "Image Size", ThemeTypeVariation = "HeaderSmall" });
        var useView = new CheckBox { Text = "Use view size", ButtonPressed = p.ExportUseViewSize };
        box.AddChild(useView);
        var grid = new GridContainer { Columns = 2 };
        var width = new SpinBox { CustomMinimumSize = new Vector2(150, 0), MinValue = 16, MaxValue = 16384, Value = p.ExportUseViewSize ? viewSize.X : p.ExportWidth, Suffix = "pixels" };
        var height = new SpinBox { CustomMinimumSize = new Vector2(150, 0), MinValue = 16, MaxValue = 16384, Value = p.ExportUseViewSize ? viewSize.Y : p.ExportHeight, Suffix = "pixels" };
        dialog.RegisterTextEnter(width.GetLineEdit());
        dialog.RegisterTextEnter(height.GetLineEdit());
        grid.AddChild(new Label { Text = "Width" });
        grid.AddChild(width);
        grid.AddChild(new Label { Text = "Height" });
        grid.AddChild(height);
        box.AddChild(grid);
        // The picture keeps the view's proportions, as in SketchUp: one size sets the other.
        var aspect = (double)viewSize.X / Math.Max(viewSize.Y, 1);
        var linking = false;
        width.ValueChanged += v =>
        {
            if (linking)
                return;
            linking = true;
            height.Value = Math.Round(v / aspect);
            linking = false;
        };
        height.ValueChanged += v =>
        {
            if (linking)
                return;
            linking = true;
            width.Value = Math.Round(v * aspect);
            linking = false;
        };
        void Enable()
        {
            width.Editable = height.Editable = !useView.ButtonPressed;
            if (useView.ButtonPressed)
            {
                linking = true;
                (width.Value, height.Value) = (viewSize.X, viewSize.Y);
                linking = false;
            }
        }
        useView.Toggled += _ => Enable();
        Enable();
        box.AddChild(new Label { Text = "Rendering", ThemeTypeVariation = "HeaderSmall" });
        var antialias = new CheckBox { Text = "Anti-alias", ButtonPressed = p.ExportAntialias };
        box.AddChild(antialias);
        var transparent = new CheckBox { Text = "Transparent background", ButtonPressed = p.ExportTransparent, Visible = png };
        box.AddChild(transparent);
        var quality = new HSlider { MinValue = 0.1, MaxValue = 1, Step = 0.01, Value = p.ExportJpegQuality, Visible = !png };
        if (!png)
            box.AddChild(new Label { Text = "JPEG Compression (smaller file ↔ better quality)" });
        box.AddChild(quality);
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            p.ExportUseViewSize = useView.ButtonPressed;
            p.ExportWidth = (int)width.Value;
            p.ExportHeight = (int)height.Value;
            p.ExportAntialias = antialias.ButtonPressed;
            p.ExportTransparent = png && transparent.ButtonPressed;
            p.ExportJpegQuality = quality.Value;
            AppPreferences.Save();
            done();
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
                dialog.QueueFree();
        };
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }
}
