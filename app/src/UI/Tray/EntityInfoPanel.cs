using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Entity Info: properties of the selection, editable (tag, name, edge flags, …). Shows "No Selection"
/// or "N Entities" like SketchUp.
/// </summary>
public partial class EntityInfoPanel : VBoxContainer
{
    private Func<Document> _doc = null!;

    public static EntityInfoPanel Create(Func<Document> doc) => new() { _doc = doc, CustomMinimumSize = new Vector2(0, 90) };

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var doc = _doc();
        var items = doc.Selection.Items.ToList();
        switch (items.Count)
        {
            case 0:
                Title("No Selection");
                return;
            case > 1:
                Title($"{items.Count} Entities");
                return;
        }

        switch (items[0])
        {
            case Face f:
                Title("Face");
                TagRow(doc, f.Tag, t => doc.Operation("Change Tag", _ => f.Tag = t));
                Row("Area", Area(f.Area));
                Row("Front", f.FrontMaterial?.Name ?? "Default");
                Row("Back", f.BackMaterial?.Name ?? "Default");
                Check("Hidden", f.Hidden, v => doc.Operation("Hide", _ => f.Hidden = v));
                break;
            case Edge e:
                Title("Edge");
                TagRow(doc, e.Tag, t => doc.Operation("Change Tag", _ => e.Tag = t));
                Row("Length", Length.Format(e.Length, LengthUnit.Millimeters, 2));
                Check("Soft", e.Flags.HasFlag(EdgeFlags.Soft), v => doc.Operation("Soften", _ => e.Flags = v ? e.Flags | EdgeFlags.Soft : e.Flags & ~EdgeFlags.Soft));
                Check("Smooth", e.Flags.HasFlag(EdgeFlags.Smooth), v => doc.Operation("Smooth", _ => e.Flags = v ? e.Flags | EdgeFlags.Smooth : e.Flags & ~EdgeFlags.Smooth));
                Check("Hidden", e.Flags.HasFlag(EdgeFlags.Hidden), v => doc.Operation("Hide", _ => e.Flags = v ? e.Flags | EdgeFlags.Hidden : e.Flags & ~EdgeFlags.Hidden));
                break;
            case SectionPlane s:
                Title("Section Plane");
                TagRow(doc, s.Tag, t => doc.Operation("Change Tag", _ => s.Tag = t));
                Edit("Name", s.Name, v => doc.Operation("Rename", _ => s.Name = v));
                Check("Hidden", s.Hidden, v => doc.Operation("Hide", _ => s.Hidden = v));
                break;
            case LinearDimension d:
                Title("Dimension");
                TagRow(doc, d.Tag, t => doc.Operation("Change Tag", _ => d.Tag = t));
                Edit("Text", d.Text.Length == 0 ? "<>" : d.Text, v => doc.Operation("Edit Text", _ => d.Text = v == "<>" ? "" : v));
                Row("Length", Length.Format(d.Length, LengthUnit.Millimeters, 2));
                Check("Hidden", d.Hidden, v => doc.Operation("Hide", _ => d.Hidden = v));
                break;
            case TextLabel x:
                Title(x.ScreenPosition == null ? "Text" : "Screen Text");
                TagRow(doc, x.Tag, t => doc.Operation("Change Tag", _ => x.Tag = t));
                Edit("Text", x.Text, v => doc.Operation("Edit Text", _ => x.Text = v));
                Check("Hidden", x.Hidden, v => doc.Operation("Hide", _ => x.Hidden = v));
                break;
            case ComponentInstance i:
                // SketchUp calls a closed, consistently oriented mesh without nested instances a solid.
                var report = i.Definition.Entities.Instances.Count == 0
                    ? MeshCheck.Analyze(MeshExtractor.ExtractInstance(i))
                    : null;
                var solid = report is { IsWatertight: true };
                var kind = i.IsGroup ? "Group" : $"Component ({i.Definition.Entities.Instances.Count} nested)";
                Title(solid ? $"Solid {kind}" : kind);
                SolidBadge(i, solid);
                TagRow(doc, i.Tag, t => doc.Operation("Change Tag", _ => i.Tag = t));
                Edit("Instance", i.Name, v => doc.Operation("Rename", _ => i.Name = v));
                if (!i.IsGroup)
                    Edit("Definition", i.Definition.Name, v => doc.Operation("Rename", _ => i.Definition.Name = v));
                var b = i.Definition.Entities.Bounds();
                if (!b.IsEmpty)
                {
                    // Along the instance's own axes, including its scale.
                    var t = i.Transform;
                    var s = new Vec3(b.Size.X * t.X.Length, b.Size.Y * t.Y.Length, b.Size.Z * t.Z.Length);
                    Row("Size", $"{Length.Format(s.X, LengthUnit.Millimeters, 1)} × {Length.Format(s.Y, LengthUnit.Millimeters, 1)} × {Length.Format(s.Z, LengthUnit.Millimeters, 1)}");
                }
                if (solid)
                    Row("Volume", Volume(Math.Abs(report!.Volume)));
                Row("Material", i.Material?.Name ?? "Default");
                Check("Hidden", i.Hidden, v => doc.Operation("Hide", _ => i.Hidden = v));
                Check("Locked", i.Locked, v => doc.Operation("Lock", _ => i.Locked = v));
                break;
        }
    }

    private static string Area(double mm2) => mm2 >= 1e6 ? $"{mm2 / 1e6:0.###} m²" : mm2 >= 100 ? $"{mm2 / 100:0.##} cm²" : $"{mm2:0.##} mm²";

    private static string Volume(double mm3) => mm3 >= 1e9 ? $"{mm3 / 1e9:0.###} m³" : mm3 >= 1000 ? $"{mm3 / 1000:0.##} cm³" : $"{mm3:0.##} mm³";

    /// <summary>Opens Solid Inspector² on a group or component (set by the main window).</summary>
    public static Action<ComponentInstance>? InspectSolid { get; set; }

    /// <summary>A plain "is it a solid" indicator; when it is not, a link runs Solid Inspector² on it.</summary>
    private void SolidBadge(ComponentInstance instance, bool solid)
    {
        var row = new HBoxContainer();
        var dot = new Label { Text = solid ? "● Solid" : "● Not a solid" };
        dot.AddThemeColorOverride("font_color", solid ? Color.Color8(0, 140, 60) : Color.Color8(200, 30, 30));
        row.AddChild(dot);
        if (!solid && InspectSolid is { } inspect)
        {
            var link = new LinkButton { Text = "Inspect…", TooltipText = "Find out why with Solid Inspector²", FocusMode = FocusModeEnum.None };
            link.AddThemeColorOverride("font_color", Color.Color8(0, 90, 200));
            link.AddThemeColorOverride("font_hover_color", Color.Color8(0, 60, 160));
            link.Pressed += () => inspect(instance);
            row.AddChild(link);
        }
        AddChild(row);
    }

    private void Title(string text) => AddChild(new Label { Text = text, ThemeTypeVariation = "HeaderSmall" });

    private void Row(string label, string value)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(80, 0) });
        row.AddChild(new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
        AddChild(row);
    }

    private void Edit(string label, string value, Action<string> apply)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(80, 0) });
        var edit = new LineEdit { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        edit.TextSubmitted += t => apply(t);
        row.AddChild(edit);
        AddChild(row);
    }

    private void Check(string label, bool value, Action<bool> apply)
    {
        var box = new CheckBox { Text = label, ButtonPressed = value, FocusMode = FocusModeEnum.None };
        box.Toggled += apply.Invoke;
        AddChild(box);
    }

    private void TagRow(Document doc, Tag? current, Action<Tag?> apply)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Tag", CustomMinimumSize = new Vector2(80, 0) });
        var options = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        var tags = doc.Model.Tags;
        for (var i = 0; i < tags.Count; i++)
        {
            options.AddItem(tags[i].Name, i);
            if (tags[i] == current || (current == null && i == 0))
                options.Select(i);
        }
        options.ItemSelected += idx => apply(idx == 0 ? null : tags[(int)idx]);
        row.AddChild(options);
        AddChild(row);
    }
}
