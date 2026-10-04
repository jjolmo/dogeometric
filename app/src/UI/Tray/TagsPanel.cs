using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Tags panel: visibility per tag, its colour, and Add Tag.</summary>
public partial class TagsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action _visibilityChanged = null!;

    public static TagsPanel Create(Func<Document> doc, Action visibilityChanged) => new() { _doc = doc, _visibilityChanged = visibilityChanged };

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var doc = _doc();
        var add = new Button { Text = "+  Add Tag", FocusMode = FocusModeEnum.None };
        add.Pressed += () =>
        {
            var name = $"Tag{doc.Model.Tags.Count}";
            doc.Operation("Add Tag", _ => doc.Model.GetOrAddTag(name));
            Refresh();
        };
        AddChild(add);

        foreach (var tag in doc.Model.Tags)
        {
            var row = new HBoxContainer();
            var visible = new CheckBox { ButtonPressed = tag.Visible, FocusMode = FocusModeEnum.None, TooltipText = "Visible" };
            visible.Toggled += v =>
            {
                tag.Visible = v;
                _visibilityChanged();
            };
            row.AddChild(visible);
            if (tag == doc.Model.UntaggedTag)
            {
                row.AddChild(new Label { Text = tag.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            }
            else
            {
                var name = new LineEdit { Text = tag.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, Flat = true };
                name.TextSubmitted += t => tag.Name = t;
                row.AddChild(name);
            }
            row.AddChild(new ColorRect { Color = Color.Color8(tag.Color.R, tag.Color.G, tag.Color.B), CustomMinimumSize = new Vector2(18, 18) });
            AddChild(row);
        }
    }
}
