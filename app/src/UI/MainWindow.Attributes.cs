using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Window › Component Attributes and Component Options (Dynamic Components): a component's own values.</summary>
public partial class MainWindow
{
    private void RegisterAttributes()
    {
        _commands.Register(ExtensionIds.ComponentAttributes, () => ShowAttributes(options: false));
        _commands.Register(ExtensionIds.ComponentOptions, () => ShowAttributes(options: true));
        _commands.Register(ExtensionIds.Interact, () => _viewport.Tools.Activate(new InteractTool()), () => _viewport.Tools.Active is InteractTool);
    }

    /// <summary>Attributes edits every attribute (adding and removing too); Options only those users may change.</summary>
    private void ShowAttributes(bool options)
    {
        var doc = _document.Document;
        if (doc.Selection.Items.OfType<ComponentInstance>().FirstOrDefault() is not { } inst)
        {
            _status.SetHint("Select a component to see its " + (options ? "options." : "attributes."));
            return;
        }
        var def = inst.Definition;
        var d = new AcceptDialog { Title = (options ? "Component Options - " : "Component Attributes - ") + def.Name, OkButtonText = "Close", Theme = LightTheme.Create() };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        d.AddChild(box);
        void Change(Action change)
        {
            doc.Undo.Begin(options ? "Component Options" : "Component Attributes");
            change();
            doc.Undo.Commit();
        }
        void Fill()
        {
            foreach (var c in box.GetChildren())
            {
                box.RemoveChild(c);
                c.QueueFree();
            }
            var grid = new GridContainer { Columns = options ? 2 : 4 };
            if (!options)
                foreach (var header in new[] { "Name", "Value", "Users can edit", "" })
                    grid.AddChild(new Label { Text = header, Modulate = new Color(0.4f, 0.4f, 0.4f) });
            var shown = def.Attributes.Where(a => !options || a.UserCanEdit).ToList();
            foreach (var a in shown)
            {
                if (options)
                    grid.AddChild(new Label { Text = a.Name });
                else
                {
                    var name = new LineEdit { Text = a.Name, CustomMinimumSize = new Vector2(140, 0) };
                    name.TextSubmitted += t => Change(() => a.Name = t.Trim());
                    name.FocusExited += () =>
                    {
                        if (name.Text.Trim() != a.Name)
                            Change(() => a.Name = name.Text.Trim());
                    };
                    grid.AddChild(name);
                }
                var value = new LineEdit { Text = a.Value, CustomMinimumSize = new Vector2(160, 0) };
                value.TextSubmitted += t => Change(() => a.Value = t);
                value.FocusExited += () =>
                {
                    if (value.Text != a.Value)
                        Change(() => a.Value = value.Text);
                };
                grid.AddChild(value);
                if (!options)
                {
                    var edit = new CheckBox { ButtonPressed = a.UserCanEdit };
                    edit.Toggled += on => Change(() => a.UserCanEdit = on);
                    grid.AddChild(edit);
                    var remove = new Button { Text = "✕", TooltipText = "Delete attribute" };
                    remove.Pressed += () =>
                    {
                        Change(() => def.Attributes.Remove(a));
                        Fill();
                    };
                    grid.AddChild(remove);
                }
            }
            box.AddChild(grid);
            if (shown.Count == 0)
                box.AddChild(new Label { Text = options ? "This component has no options to change." : "No attributes yet." });
            if (!options)
            {
                var add = new Button { Text = "+ Add attribute", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                add.Pressed += () =>
                {
                    Change(() => def.Attributes.Add(new ComponentAttribute { Name = $"Attribute{def.Attributes.Count + 1}" }));
                    Fill();
                };
                box.AddChild(add);
            }
            d.ResetSize();
        }
        Fill();
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
    }
}
