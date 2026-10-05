using Dogeometric.Core.Modeling;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Materials panel: the model's materials and a colour palette as swatches. Picking one makes it the
/// Paint Bucket's current material; "Create" adds a colour material.
/// </summary>
public partial class MaterialsPanel : VBoxContainer
{
    private static readonly (string Name, Color Color)[] Palette =
    [
        ("White", Colors.White), ("Black", Color.Color8(30, 30, 30)), ("Grey", Color.Color8(128, 128, 128)),
        ("Red", Color.Color8(220, 40, 40)), ("Orange", Color.Color8(240, 140, 30)), ("Yellow", Color.Color8(250, 210, 40)),
        ("Green", Color.Color8(60, 170, 70)), ("Blue", Color.Color8(40, 100, 220)), ("Purple", Color.Color8(140, 70, 180)),
        ("Brown", Color.Color8(130, 85, 50)), ("Translucent Glass", new Color(0.7f, 0.85f, 1f, 0.35f)),
    ];

    private Func<Document> _doc = null!;
    private Label _current = null!;
    private GridContainer _grid = null!;

    /// <summary>The Paint Bucket's material (null = Default, which un-paints).</summary>
    public Material? CurrentMaterial { get; private set; }

    public event Action? CurrentChanged;

    public static MaterialsPanel Create(Func<Document> doc) => new() { _doc = doc };

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        _current = new Label { Text = "Current: " + (CurrentMaterial?.Name ?? "Default") };
        AddChild(_current);
        var create = new Button { Text = "Create Material…", FocusMode = FocusModeEnum.None };
        create.Pressed += () => CreateMaterialDialog.Show(this, _doc().Model.Materials.Count + 1, m =>
        {
            var doc = _doc();
            doc.Operation("Create Material", _ => doc.Model.Materials.Add(m));
            Refresh();
            Use(m);
        });
        AddChild(create);
        AddChild(new Label { Text = "In Model" });
        _grid = new GridContainer { Columns = 6 };
        AddChild(_grid);
        Swatch("Default", new Color(1, 1, 1), null);
        foreach (var m in _doc().Model.Materials)
            Swatch(m.Name, Color.Color8(m.Color.R, m.Color.G, m.Color.B, (byte)(m.Opacity * 255)), m);

        AddChild(new Label { Text = "Colors" });
        var palette = new GridContainer { Columns = 6 };
        AddChild(palette);
        foreach (var (name, color) in Palette)
        {
            var b = SwatchButton(name, color);
            b.Pressed += () => Use(FindOrCreate(name, color));
            palette.AddChild(b);
        }
    }

    private void Swatch(string name, Color color, Material? m)
    {
        var b = SwatchButton(name, color);
        // Textured materials show their picture, as SketchUp's swatches do.
        if (m?.Texture is { } tex && Viewport.TextureImages.Decode(tex.Data) is { } image)
        {
            image.Resize(32, 32, Image.Interpolation.Bilinear);
            var thumb = ImageTexture.CreateFromImage(image);
            b.AddThemeStyleboxOverride("normal", new StyleBoxTexture { Texture = thumb });
            b.AddThemeStyleboxOverride("hover", new StyleBoxTexture { Texture = thumb, ModulateColor = new Color(0.85f, 0.85f, 1f) });
        }
        b.Pressed += () => Use(m);
        if (m != null)
            b.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb)
                    MaterialMenu(m, b.GetScreenPosition() + mb.Position);
                else if (e is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
                    EditMaterial(m);
            };
        _grid.AddChild(b);
    }

    private static Button SwatchButton(string name, Color color)
    {
        var b = new Button { CustomMinimumSize = new Vector2(34, 34), TooltipText = name, FocusMode = FocusModeEnum.None };
        b.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = color, BorderColor = new Color(0.5f, 0.5f, 0.5f), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        b.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = color, BorderColor = new Color(0, 0, 1), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
        return b;
    }

    private Material FindOrCreate(string name, Color color)
    {
        var doc = _doc();
        var existing = doc.Model.Materials.FirstOrDefault(m => m.Name == name);
        if (existing != null)
            return existing;
        var m = new Material
        {
            Name = name,
            Color = new Rgba((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)),
            Opacity = color.A,
        };
        doc.Model.Materials.Add(m);
        Refresh();
        return m;
    }

    /// <summary>Raised when a material changed look, so the view redraws it.</summary>
    public event Action? MaterialEdited;

    private void EditMaterial(Material m) => CreateMaterialDialog.Edit(this, m, apply =>
    {
        var doc = _doc();
        doc.Undo.Begin("Edit Material");
        apply();
        doc.Undo.Commit();
        Refresh();
        MaterialEdited?.Invoke();
    });

    /// <summary>A material's right-click menu in the model's list, as SketchUp's: Edit, Delete, Select, Purge Unused.</summary>
    private void MaterialMenu(Material m, Vector2 at)
    {
        var doc = _doc();
        var menu = new PopupMenu();
        menu.AddItem("Edit...", 0);
        menu.AddItem("Delete", 1);
        menu.AddItem("Select", 2);
        menu.AddSeparator();
        menu.AddItem("Purge Unused", 3);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0:
                    EditMaterial(m);
                    break;
                case 1:
                    doc.Operation("Delete Material", _ => Painting.DeleteMaterial(doc.Model, m));
                    if (CurrentMaterial == m)
                        Use(null);
                    Refresh();
                    MaterialEdited?.Invoke();
                    break;
                case 2:
                    var ents = doc.Context.Entities;
                    doc.Selection.Set(ents.Faces.Where(f => f.FrontMaterial == m || f.BackMaterial == m).Cast<object>()
                        .Concat(ents.Instances.Where(i => i.Material == m)));
                    break;
                case 3:
                    doc.Operation("Purge Materials", _ => Painting.PurgeMaterials(doc.Model));
                    Refresh();
                    break;
            }
        };
        menu.PopupHide += menu.QueueFree;
        AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)at, Vector2I.Zero));
    }

    /// <summary>Sets the Paint Bucket's material (the Alt sampler uses this).</summary>
    public void SetCurrent(Material? m) => Use(m);

    private void Use(Material? m)
    {
        CurrentMaterial = m;
        _current.Text = "Current: " + (m?.Name ?? "Default");
        CurrentChanged?.Invoke();
    }
}
