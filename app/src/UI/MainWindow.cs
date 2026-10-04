using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Dogeometric.App.Viewport;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Root of the application window: menu bar, drawing area and status bar.</summary>
public partial class MainWindow : Control
{
    private CommandRegistry _commands = null!;
    private ModelViewport _viewport = null!;
    private StatusBar _status = null!;

    public override void _Ready()
    {
        GetWindow().Title = "Untitled - Dogeometric";
        _commands = new CommandRegistry("res://data/sketchup_commands.json");
        Theme = LightTheme.Create();

        var layout = new VBoxContainer();
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 0);
        AddChild(layout);

        _viewport = new ModelViewport { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _status = new StatusBar();

        layout.AddChild(_viewport);
        layout.AddChild(_status);

        // Commands must be registered before the menu is built: item kinds (check/radio) depend on them.
        RegisterCommands();

        var menuPanel = new PanelContainer();
        menuPanel.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.MenuBackground));
        menuPanel.AddChild(MenuBuilder.Build(_commands, text => _status.SetHint(text), UpdateToolStatus));
        layout.AddChild(menuPanel);
        layout.MoveChild(menuPanel, 0);

        _viewport.Tools.Changed += UpdateToolStatus;
        UpdateToolStatus();
        _viewport.GrabFocus();
    }

    private void RegisterCommands()
    {
        var v = _viewport;
        _commands.Register(CommandIds.Exit, () => GetTree().Quit());
        _commands.Register(CommandIds.About, ShowAbout);

        _commands.Register(CommandIds.ToggleAxes, () => v.Axes.Visible = !v.Axes.Visible, () => v.Axes.Visible);

        _commands.Register(CommandIds.PreviousCamera, v.PreviousCamera);
        _commands.Register(CommandIds.NextCamera, v.NextCamera);
        _commands.Register(CommandIds.ViewTop, () => v.SetStandardView(StandardView.Top));
        _commands.Register(CommandIds.ViewBottom, () => v.SetStandardView(StandardView.Bottom));
        _commands.Register(CommandIds.ViewFront, () => v.SetStandardView(StandardView.Front));
        _commands.Register(CommandIds.ViewBack, () => v.SetStandardView(StandardView.Back));
        _commands.Register(CommandIds.ViewLeft, () => v.SetStandardView(StandardView.Left));
        _commands.Register(CommandIds.ViewRight, () => v.SetStandardView(StandardView.Right));
        _commands.Register(CommandIds.ViewIso, () => v.SetStandardView(StandardView.Iso));
        _commands.Register(CommandIds.ParallelProjection, () => v.SetPerspective(false), () => !v.Camera.Perspective, radio: true);
        _commands.Register(CommandIds.Perspective, () => v.SetPerspective(true), () => v.Camera.Perspective, radio: true);
        _commands.Register(CommandIds.ZoomExtents, v.ZoomExtents);

        RegisterTool(CommandIds.Orbit, () => new OrbitTool());
        RegisterTool(CommandIds.Pan, () => new PanTool());
        RegisterTool(CommandIds.Zoom, () => new ZoomTool());
        RegisterTool(CommandIds.Select, () => new SelectTool());
    }

    private void RegisterTool(int id, Func<Tool> create)
    {
        _commands.Register(id, () => _viewport.Tools.Activate(create()), () => _viewport.Tools.Active.CommandId == id);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // Secondary SketchUp shortcuts (e.g. Ctrl+Shift+E for Zoom Extents); primary ones are menu accelerators.
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var pressed = key.GetKeycodeWithModifiers();
        foreach (var (keys, id) in _commands.Aliases)
        {
            if (keys == pressed && _commands.Execute(id))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    private void UpdateToolStatus()
    {
        _status.SetHint(_viewport.Tools.Active.StatusText);
        var label = _viewport.Tools.Active.VcbLabel;
        _status.SetVcbLabel(label.Length > 0 ? label : "Measurements", editable: label.Length > 0);
    }

    private void ShowAbout()
    {
        var dialog = new AcceptDialog { Title = "About Dogeometric", DialogText = "Dogeometric\nA SketchUp-style modeller built with Godot." };
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
    }
}
