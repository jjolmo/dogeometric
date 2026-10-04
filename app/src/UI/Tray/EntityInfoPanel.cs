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
            case ComponentInstance i:
                Title(i.IsGroup ? "Group" : $"Component ({i.Definition.Entities.Instances.Count} nested)");
                TagRow(doc, i.Tag, t => doc.Operation("Change Tag", _ => i.Tag = t));
                Edit("Instance", i.Name, v => doc.Operation("Rename", _ => i.Name = v));
                if (!i.IsGroup)
                    Edit("Definition", i.Definition.Name, v => doc.Operation("Rename", _ => i.Definition.Name = v));
                var b = i.Definition.Entities.Bounds();
                if (!b.IsEmpty)
                {
                    var s = b.Size;
                    Row("Size", $"{Length.Format(s.X, LengthUnit.Millimeters, 1)} × {Length.Format(s.Y, LengthUnit.Millimeters, 1)} × {Length.Format(s.Z, LengthUnit.Millimeters, 1)}");
                }
                Row("Material", i.Material?.Name ?? "Default");
                Check("Hidden", i.Hidden, v => doc.Operation("Hide", _ => i.Hidden = v));
                Check("Locked", i.Locked, v => doc.Operation("Lock", _ => i.Locked = v));
                break;
        }
    }

    private static string Area(double mm2) => mm2 >= 1e6 ? $"{mm2 / 1e6:0.###} m²" : mm2 >= 100 ? $"{mm2 / 100:0.##} cm²" : $"{mm2:0.##} mm²";

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
