using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Styles panel, Edit tab: edge, face and modeling settings, each the same switch as its View
/// menu command, so the two always agree.</summary>
public partial class StylesPanel : VBoxContainer
{
    private CommandRegistry _commands = null!;
    private readonly List<(BaseButton Button, int Id)> _switches = [];
    private bool _updating;

    public static StylesPanel Create(CommandRegistry commands)
    {
        var p = new StylesPanel { _commands = commands };
        var group = new ButtonGroup();
        p.Section("Edge Settings");
        p.Switch("Edges", CommandIds.Edges);
        p.Switch("Back Edges", CommandIds.BackEdges);
        p.Switch("Profiles", CommandIds.Profiles);
        p.Switch("Depth Cue", CommandIds.DepthCue);
        p.Switch("Extension", CommandIds.EdgeExtension);
        p.Section("Face Settings");
        foreach (var (label, id) in new[]
        {
            ("Wireframe", CommandIds.StyleWireframe), ("Hidden Line", CommandIds.StyleHiddenLine), ("Shaded", CommandIds.StyleShaded),
            ("Shaded With Textures", CommandIds.StyleShadedTextures), ("Monochrome", CommandIds.StyleMonochrome), ("X-ray", CommandIds.StyleXRay),
        })
            p.Switch(label, id, group);
        p.Section("Modeling Settings");
        p.Switch("Hidden Geometry", CommandIds.HiddenGeometry);
        p.Switch("Section Planes", CommandIds.DisplaySectionPlanes);
        p.Switch("Section Cuts", CommandIds.DisplaySectionCuts);
        p.Switch("Section Fill", CommandIds.DisplaySectionFill);
        p.Switch("Guides", CommandIds.ToggleGuides);
        p.Switch("Model Axes", CommandIds.ToggleAxes);
        p.Refresh();
        return p;
    }

    private void Section(string title) => AddChild(new Label { Text = title, ThemeTypeVariation = "HeaderSmall" });

    private void Switch(string label, int id, ButtonGroup? group = null)
    {
        BaseButton button = group == null
            ? new CheckBox { Text = label, FocusMode = FocusModeEnum.None }
            : new CheckBox { Text = label, FocusMode = FocusModeEnum.None, ButtonGroup = group };
        button.Toggled += on =>
        {
            // A radio button only acts when chosen (the one let go must not refresh the others back).
            if (_updating || (group != null && !on))
                return;
            _commands.Execute(id);
            Refresh();
        };
        AddChild(button);
        _switches.Add((button, id));
    }

    /// <summary>Shows the commands' current states.</summary>
    public void Refresh()
    {
        _updating = true;
        foreach (var (button, id) in _switches)
        {
            var command = _commands.Get(id);
            button.Disabled = !command.IsImplemented;
            button.ButtonPressed = command.IsChecked?.Invoke() ?? false;
        }
        _updating = false;
    }
}
