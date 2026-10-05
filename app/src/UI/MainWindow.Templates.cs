using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>File › New From Template and Save As Template.</summary>
public partial class MainWindow
{
    private void RegisterTemplates()
    {
        _commands.Register(CommandIds.NewFromTemplate, () => _document.ConfirmDiscard(ShowNewFromTemplate));
        _commands.Register(CommandIds.SaveAsTemplate, ShowSaveAsTemplate);
    }

    private void ShowNewFromTemplate()
    {
        var d = new ConfirmationDialog { Title = "New From Template", OkButtonText = "Create", Theme = LightTheme.Create() };
        var list = new ItemList { CustomMinimumSize = new Vector2(460, 260) };
        var templates = Templates.All();
        foreach (var t in templates)
            list.AddItem(t.Description.Length > 0 ? $"{t.Name}  —  {t.Description}" : t.Name);

        list.Select(Math.Max(0, templates.IndexOf(Templates.Default)));
        d.AddChild(list);
        void Create()
        {
            if (list.GetSelectedItems() is [var i, ..])
                _document.NewFrom(Templates.Create(templates[i]));
            d.QueueFree();
        }
        d.Confirmed += Create;
        list.ItemActivated += _ =>
        {
            d.Hide();
            Create();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
    }

    private void ShowSaveAsTemplate()
    {
        var d = new ConfirmationDialog { Title = "Save As Template", OkButtonText = "Save", Theme = LightTheme.Create() };
        var grid = new GridContainer { Columns = 2, CustomMinimumSize = new Vector2(420, 0) };
        grid.AddChild(new Label { Text = "Name:" });
        var name = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(name);
        grid.AddChild(new Label { Text = "Description:" });
        var description = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(description);
        grid.AddChild(new Control());
        var makeDefault = new CheckBox { Text = "Set as default template", ButtonPressed = true };
        grid.AddChild(makeDefault);
        d.AddChild(grid);
        d.RegisterTextEnter(name);
        d.RegisterTextEnter(description);
        d.Confirmed += () =>
        {
            if (name.Text.Trim() is { Length: > 0 } n)
            {
                var saved = Templates.Save(_document.Model, n, description.Text.Trim());
                if (makeDefault.ButtonPressed)
                {
                    AppPreferences.Current.DefaultTemplate = saved;
                    AppPreferences.Save();
                }
                _status.SetHint($"Saved template {n}");
            }
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
        name.GrabFocus();
    }
}
