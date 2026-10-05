using Dogeometric.Core.IO;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Help › Welcome: SketchUp's start window, with the templates to start from, Open and the recent files as
/// previews. It shows at start-up unless "Always show on startup" is cleared.</summary>
public partial class MainWindow
{
    private void ShowWelcome()
    {
        var d = new AcceptDialog { Title = "Welcome to Dogeometric", OkButtonText = "Close", Theme = LightTheme.Create() };
        var columns = new HBoxContainer { CustomMinimumSize = new Vector2(820, 440) };

        var start = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        start.AddChild(new Label { Text = "New model", ThemeTypeVariation = "HeaderSmall" });
        var templates = Templates.All();
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (var t in templates)
            list.SetItemTooltip(list.AddItem(t.Name), t.Description);
        list.Select(Math.Max(0, templates.FindIndex(t => t.Name == Templates.Default.Name)));
        start.AddChild(list);
        var description = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 40) };
        void Describe(long i) => description.Text = templates[(int)i].Description;
        Describe(list.GetSelectedItems().FirstOrDefault());
        list.ItemSelected += Describe;
        start.AddChild(description);
        var buttons = new HBoxContainer();
        var create = new Button { Text = "Start modeling" };
        create.Pressed += () =>
        {
            if (list.GetSelectedItems() is [var i, ..])
            {
                d.Hide();
                _document.ConfirmDiscard(() => _document.NewFrom(Templates.Create(templates[i])));
            }
        };
        list.ItemActivated += _ => create.EmitSignal(BaseButton.SignalName.Pressed);
        buttons.AddChild(create);
        var open = new Button { Text = "Open a file..." };
        open.Pressed += () =>
        {
            d.Hide();
            _document.ShowOpen();
        };
        buttons.AddChild(open);
        start.AddChild(buttons);
        columns.AddChild(start);

        var recent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        recent.AddChild(new Label { Text = "Recent files", ThemeTypeVariation = "HeaderSmall" });
        var files = AppPreferences.Current.RecentFiles.Where(File.Exists).ToList();
        var grid = new ItemList
        {
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(128, 96),
            FixedColumnWidth = 150,
            MaxColumns = 0,
            SameColumnWidth = true,
            MaxTextLines = 2,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        foreach (var path in files)
        {
            Texture2D? preview = ResourceLoader.Exists("res://icons/make_component.svg") ? GD.Load<Texture2D>("res://icons/make_component.svg") : null;
            if (ComponentCollection.Thumbnail(path) is { } png)
            {
                var image = new Image();
                if (image.LoadPngFromBuffer(png) == Error.Ok)
                    preview = ImageTexture.CreateFromImage(image);
            }
            grid.SetItemTooltip(grid.AddItem(System.IO.Path.GetFileName(path), preview), path);
        }
        grid.ItemActivated += i =>
        {
            d.Hide();
            _document.OpenRecent(files[(int)i]);
        };
        recent.AddChild(files.Count == 0 ? new Label { Text = "Files you open or save show here.", Modulate = new Color(1, 1, 1, 0.6f) } : grid);
        recent.AddChild(new Label
        {
            Text = "Dogeometric works like SketchUp 2021, with its extensions built in. Help › Search finds any command by name.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(500, 0),
        });
        columns.AddChild(recent);

        var box = new VBoxContainer();
        box.AddChild(columns);
        var always = new CheckBox { Text = "Always show on startup", ButtonPressed = AppPreferences.Current.ShowWelcome };
        always.Toggled += on =>
        {
            AppPreferences.Current.ShowWelcome = on;
            AppPreferences.Save();
        };
        box.AddChild(always);
        d.AddChild(box);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered(new Vector2I(880, 540));
    }
}
