using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Create Material dialog: name, colour, an optional texture image with its real-world size (aspect
/// locked by default) and opacity.
/// </summary>
public partial class CreateMaterialDialog : ConfirmationDialog
{
    private LineEdit _name = null!;
    private ColorPickerButton _color = null!;
    private CheckBox _useTexture = null!;
    private Label _file = null!;
    private LineEdit _width = null!, _height = null!;
    private CheckBox _lockAspect = null!;
    private HSlider _opacity = null!;
    private byte[]? _image;
    private string _imageName = "";
    private double _aspect = 1;

    public static void Show(Node parent, int index, Action<Material> created)
    {
        var d = new CreateMaterialDialog { Title = "Create Material", OkButtonText = "OK" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        d._name = new LineEdit { Text = $"Material{index}" };
        box.AddChild(d._name);

        var colorRow = new HBoxContainer();
        colorRow.AddChild(new Label { Text = "Color", CustomMinimumSize = new Vector2(80, 0) });
        d._color = new ColorPickerButton { Color = new Color(0.8f, 0.8f, 0.8f), CustomMinimumSize = new Vector2(80, 26), EditAlpha = false };
        colorRow.AddChild(d._color);
        box.AddChild(colorRow);

        box.AddChild(new HSeparator());
        var texRow = new HBoxContainer();
        d._useTexture = new CheckBox { Text = "Use texture image" };
        texRow.AddChild(d._useTexture);
        var browse = new Button { Text = "Browse…" };
        texRow.AddChild(browse);
        box.AddChild(texRow);
        d._file = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        box.AddChild(d._file);

        var size = new GridContainer { Columns = 3 };
        size.AddChild(new Label { Text = "Width" });
        d._width = new LineEdit { Text = "100mm", CustomMinimumSize = new Vector2(110, 0) };
        size.AddChild(d._width);
        d._lockAspect = new CheckBox { Text = "Lock aspect", ButtonPressed = true };
        size.AddChild(d._lockAspect);
        size.AddChild(new Label { Text = "Height" });
        d._height = new LineEdit { Text = "100mm", CustomMinimumSize = new Vector2(110, 0) };
        size.AddChild(d._height);
        box.AddChild(size);
        d._width.TextSubmitted += _ => d.KeepAspect(fromWidth: true);
        d._height.TextSubmitted += _ => d.KeepAspect(fromWidth: false);

        box.AddChild(new HSeparator());
        var opacityRow = new HBoxContainer();
        opacityRow.AddChild(new Label { Text = "Opacity", CustomMinimumSize = new Vector2(80, 0) });
        d._opacity = new HSlider { MinValue = 0, MaxValue = 100, Value = 100, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        opacityRow.AddChild(d._opacity);
        box.AddChild(opacityRow);
        d.AddChild(box);

        browse.Pressed += () =>
        {
            var picker = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = ["*.png, *.jpg, *.jpeg, *.bmp, *.webp ; Images"],
                Title = "Choose Image",
                UseNativeDialog = OS.GetEnvironment("DOGEOMETRIC_NO_NATIVE_DIALOGS") == "",
            };
            picker.FileSelected += path =>
            {
                d.LoadImage(path);
                picker.QueueFree();
            };
            picker.Canceled += picker.QueueFree;
            d.AddChild(picker);
            picker.PopupCentered(new Vector2I(800, 520));
        };

        d.Confirmed += () =>
        {
            d.KeepAspect(fromWidth: true);
            var c = d._color.Color;
            var m = new Material
            {
                Name = d._name.Text.Trim().Length > 0 ? d._name.Text.Trim() : $"Material{index}",
                Color = new Rgba((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255)),
                Opacity = d._opacity.Value / 100,
            };
            if (d._useTexture.ButtonPressed && d._image != null)
            {
                Length.TryParse(d._width.Text, LengthUnit.Millimeters, out var w);
                Length.TryParse(d._height.Text, LengthUnit.Millimeters, out var h);
                m.Texture = new TextureImage { FileName = d._imageName, Data = d._image, WidthMm = w > 0 ? w : 100, HeightMm = h > 0 ? h : 100 };
            }
            created(m);
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.PopupCentered();
        d._name.GrabFocus();
        d._name.SelectAll();
    }

    /// <summary>Takes an image file as the texture; the colour becomes its average, as SketchUp does.</summary>
    public void LoadImage(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var image = Image.LoadFromFile(path);
        if (image == null || image.IsEmpty())
            return;
        _image = bytes;
        _imageName = System.IO.Path.GetFileName(path);
        _file.Text = _imageName;
        _useTexture.ButtonPressed = true;
        _aspect = image.GetHeight() / (double)Math.Max(image.GetWidth(), 1);
        if (_name.Text.StartsWith("Material"))
            _name.Text = System.IO.Path.GetFileNameWithoutExtension(path);
        _color.Color = AverageColor(image);
        KeepAspect(fromWidth: true);
    }

    private void KeepAspect(bool fromWidth)
    {
        if (!_lockAspect.ButtonPressed || _image == null)
            return;
        if (fromWidth && Length.TryParse(_width.Text, LengthUnit.Millimeters, out var w))
            _height.Text = Length.Format(w * _aspect, LengthUnit.Millimeters, 1);
        else if (!fromWidth && Length.TryParse(_height.Text, LengthUnit.Millimeters, out var h))
            _width.Text = Length.Format(h / _aspect, LengthUnit.Millimeters, 1);
    }

    public static Color AverageColor(Image image)
    {
        var small = (Image)image.Duplicate();
        small.Resize(8, 8, Image.Interpolation.Bilinear);
        float r = 0, g = 0, b = 0;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var c = small.GetPixel(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
            }
        return new Color(r / 64, g / 64, b / 64);
    }
}
