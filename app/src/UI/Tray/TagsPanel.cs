using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Tags panel: visibility, name and colour per tag; Add and Delete Tag; and its details menu
/// (Select All, Purge, Color by tag).</summary>
public partial class TagsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action _visibilityChanged = null!;
    private Tag? _picked;

    public static TagsPanel Create(Func<Document> doc, Action visibilityChanged) => new() { _doc = doc, _visibilityChanged = visibilityChanged };

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var doc = _doc();
        var bar = new HBoxContainer();
        var add = new Button { Text = "+  Add Tag", FocusMode = FocusModeEnum.None };
        add.Pressed += () =>
        {
            var name = $"Tag{doc.Model.Tags.Count}";
            doc.Operation("Add Tag", _ => doc.Model.GetOrAddTag(name));
            Refresh();
        };
        bar.AddChild(add);
        var delete = new Button { Text = "−  Delete Tag", FocusMode = FocusModeEnum.None, Disabled = _picked == null };
        delete.Pressed += () =>
        {
            if (_picked is not { } tag || tag == doc.Model.UntaggedTag)
                return;
            // As in SketchUp, what used it goes back to Untagged.
            doc.Operation("Delete Tag", _ =>
            {
                foreach (var e in doc.Model.AllEntities)
                {
                    e.Edges.Where(x => x.Tag == tag).ToList().ForEach(x => x.Tag = null);
                    e.Faces.Where(x => x.Tag == tag).ToList().ForEach(x => x.Tag = null);
                    e.Instances.Where(x => x.Tag == tag).ToList().ForEach(x => x.Tag = null);
                }
                doc.Model.Tags.Remove(tag);
            });
            _picked = null;
            _visibilityChanged();
            Refresh();
        };
        bar.AddChild(delete);
        var details = new MenuButton { Text = "≡", TooltipText = "Details", FocusMode = FocusModeEnum.None, Flat = false };
        var menu = details.GetPopup();
        menu.AddItem("Select All", 0);
        menu.SetItemDisabled(0, _picked == null);
        menu.AddItem("Purge", 1);
        menu.AddCheckItem("Color by tag", 2);
        menu.SetItemChecked(2, doc.Model.Options.ColorByTag);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0 when _picked is { } tag:
                    var context = doc.Context.Entities;
                    doc.Selection.Set(context.Edges.Where(x => x.Tag == tag).Cast<object>()
                        .Concat(context.Faces.Where(x => x.Tag == tag)).Concat(context.Instances.Where(x => x.Tag == tag)));
                    break;
                case 1:
                    doc.Operation("Purge Tags", _ => Grouping.PurgeTags(doc.Model));
                    Refresh();
                    break;
                case 2:
                    doc.Undo.Begin("Color by tag");
                    doc.Model.Options = doc.Model.Options with { ColorByTag = !doc.Model.Options.ColorByTag };
                    doc.Undo.Commit();
                    _visibilityChanged();
                    Refresh();
                    break;
            }
        };
        bar.AddChild(details);
        AddChild(bar);

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
                var name = new LineEdit { Text = tag.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, Flat = true, CustomMinimumSize = new Vector2(60, 0) };
                name.TextSubmitted += t => tag.Name = t;
                row.AddChild(name);
            }
            var pick = new Button { Text = "•", ToggleMode = true, ButtonPressed = tag == _picked, FocusMode = FocusModeEnum.None, TooltipText = "Select this tag" };
            pick.Pressed += () =>
            {
                _picked = tag == _picked ? null : tag;
                Refresh();
            };
            row.AddChild(pick);
            var color = new ColorPickerButton { Color = Color.Color8(tag.Color.R, tag.Color.G, tag.Color.B), CustomMinimumSize = new Vector2(30, 20), EditAlpha = false, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            color.PopupClosed += () =>
            {
                var c = color.Color;
                doc.Undo.Begin("Tag Color");
                tag.Color = new Rgba((byte)c.R8, (byte)c.G8, (byte)c.B8);
                doc.Undo.Commit();
                if (doc.Model.Options.ColorByTag)
                    _visibilityChanged();
            };
            row.AddChild(color);
            var dashes = new MenuButton { Icon = DashSample(tag.Dashes), FocusMode = FocusModeEnum.None, TooltipText = "Dashes: " + LineStyles.Name(tag.Dashes), Flat = false };
            var popup = dashes.GetPopup();
            for (var i = 0; i < LineStyles.Names.Length; i++)
                popup.AddIconItem(DashSample((LineStyle)i), i == 0 ? "Default" : LineStyles.Names[i], i);
            popup.IdPressed += id =>
            {
                doc.Undo.Begin("Tag Dashes");
                tag.Dashes = (LineStyle)id;
                doc.Undo.Commit();
                _visibilityChanged();
                Refresh();
            };
            row.AddChild(dashes);
            AddChild(row);
        }
    }

    private static readonly Dictionary<LineStyle, Texture2D> Samples = [];

    /// <summary>A short stretch of the line style, at half its on-screen size, for the Dashes column.</summary>
    private static Texture2D DashSample(LineStyle style)
    {
        if (Samples.TryGetValue(style, out var cached))
            return cached;
        const int width = 44;
        var image = Image.CreateEmpty(width, 6, false, Image.Format.Rgba8);
        var pattern = LineStyles.Pattern(style);
        for (int x = 0, i = 0, left = pattern.Length > 0 ? pattern[0] : width; x < width; x++)
        {
            if (pattern.Length == 0 || i % 2 == 0)
                image.SetPixel(x, 2, Colors.Black);
            left -= 2;
            while (pattern.Length > 0 && left <= 0)
                left += pattern[i = (i + 1) % pattern.Length];
        }
        return Samples[style] = ImageTexture.CreateFromImage(image);
    }
}
