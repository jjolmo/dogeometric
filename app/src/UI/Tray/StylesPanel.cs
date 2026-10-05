using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Styles panel, Edit tab: edge, face, background and modeling settings. The on/off ones are the
/// View menu's commands, so the two always agree; the rest are the model's style.</summary>
public partial class StylesPanel : VBoxContainer
{
    private CommandRegistry _commands = null!;
    private Func<StyleSettings> _style = null!;
    private Action<Func<StyleSettings, StyleSettings>> _setStyle = null!;
    private readonly List<(BaseButton Button, int Id)> _switches = [];
    private readonly List<Action> _readers = [];
    private bool _updating;

    public static StylesPanel Create(CommandRegistry commands, Func<StyleSettings> style, Action<Func<StyleSettings, StyleSettings>> setStyle)
    {
        var p = new StylesPanel { _commands = commands, _style = style, _setStyle = setStyle };
        var group = new ButtonGroup();
        p.Section("Edge Settings");
        p.Switch("Edges", CommandIds.Edges);
        p.Switch("Back Edges", CommandIds.BackEdges);
        p.Switch("Profiles", CommandIds.Profiles);
        p.Switch("Depth Cue", CommandIds.DepthCue);
        p.Switch("Extension", CommandIds.EdgeExtension);
        p.Number("Profile width", 1, 20, s => s.ProfileWidth, (s, v) => s with { ProfileWidth = v });
        p.Number("Depth cue width", 1, 20, s => s.DepthCueWidth, (s, v) => s with { DepthCueWidth = v });
        p.Number("Extension length", 1, 50, s => s.ExtensionLength, (s, v) => s with { ExtensionLength = v });
        p.Flag("Endpoints", s => s.Endpoints, (s, v) => s with { Endpoints = v });
        p.Number("Endpoint length", 1, 50, s => s.EndpointLength, (s, v) => s with { EndpointLength = v });
        p.Flag("Jitter", s => s.Jitter, (s, v) => s with { Jitter = v });
        p.Flag("Dashes", s => s.Dashes, (s, v) => s with { Dashes = v });
        p.Choice("Color", ["All same", "By material", "By axis"], s => (int)s.EdgeColorMode, (s, v) => s with { EdgeColorMode = (EdgeColorMode)v });
        p.Colour("Edge color", s => s.EdgeColor, (s, c) => s with { EdgeColor = c });
        p.Section("Face Settings");
        p.Colour("Front color", s => s.FrontColor, (s, c) => s with { FrontColor = c });
        p.Colour("Back color", s => s.BackColor, (s, c) => s with { BackColor = c });
        foreach (var (label, id) in new[]
        {
            ("Wireframe", CommandIds.StyleWireframe), ("Hidden Line", CommandIds.StyleHiddenLine), ("Shaded", CommandIds.StyleShaded),
            ("Shaded With Textures", CommandIds.StyleShadedTextures), ("Monochrome", CommandIds.StyleMonochrome), ("X-ray", CommandIds.StyleXRay),
        })
            p.Switch(label, id, group);
        p.Slider("X-ray opacity", s => s.XrayOpacity, (s, v) => s with { XrayOpacity = v });
        p.Flag("Enable transparency", s => s.Transparency, (s, v) => s with { Transparency = v });
        p.Choice("Transparency quality", ["Faster", "Nicer"], s => (int)s.TransparencyQuality, (s, v) => s with { TransparencyQuality = (TransparencyQuality)v });
        p.Section("Background Settings");
        p.Colour("Background", s => s.BackgroundColor, (s, c) => s with { BackgroundColor = c });
        p.Flag("Sky", s => s.Sky, (s, v) => s with { Sky = v });
        p.Colour("Sky color", s => s.SkyColor, (s, c) => s with { SkyColor = c });
        p.Flag("Ground", s => s.Ground, (s, v) => s with { Ground = v });
        p.Colour("Ground color", s => s.GroundColor, (s, c) => s with { GroundColor = c });
        p.Slider("Transparency", s => s.GroundTransparency, (s, v) => s with { GroundTransparency = v });
        p.Flag("Show ground from below", s => s.GroundFromBelow, (s, v) => s with { GroundFromBelow = v });
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

    private Control Row(string label, Control field)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(field);
        AddChild(row);
        return row;
    }

    private void Number(string label, int min, int max, Func<StyleSettings, int> get, Func<StyleSettings, int, StyleSettings> set)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Suffix = "px" };
        spin.ValueChanged += v =>
        {
            if (!_updating)
                _setStyle(s => set(s, (int)v));
        };
        Row(label, spin);
        _readers.Add(() => spin.Value = get(_style()));
    }

    private void Choice(string label, string[] options, Func<StyleSettings, int> get, Func<StyleSettings, int, StyleSettings> set)
    {
        var list = new OptionButton();
        foreach (var o in options)
            list.AddItem(o);
        list.ItemSelected += i =>
        {
            if (!_updating)
                _setStyle(s => set(s, (int)i));
        };
        Row(label, list);
        _readers.Add(() => list.Select(get(_style())));
    }

    private void Colour(string label, Func<StyleSettings, Rgba> get, Func<StyleSettings, Rgba, StyleSettings> set)
    {
        var pick = new ColorPickerButton { EditAlpha = false, CustomMinimumSize = new Vector2(48, 22) };
        pick.PopupClosed += () =>
        {
            var c = pick.Color;
            var value = new Rgba((byte)c.R8, (byte)c.G8, (byte)c.B8);
            if (value != get(_style()))
                _setStyle(s => set(s, value));
        };
        Row(label, pick);
        _readers.Add(() =>
        {
            var c = get(_style());
            pick.Color = Color.Color8(c.R, c.G, c.B);
        });
    }

    private void Flag(string label, Func<StyleSettings, bool> get, Func<StyleSettings, bool, StyleSettings> set)
    {
        var check = new CheckBox { Text = label, FocusMode = FocusModeEnum.None };
        check.Toggled += on =>
        {
            if (!_updating)
                _setStyle(s => set(s, on));
        };
        AddChild(check);
        _readers.Add(() => check.ButtonPressed = get(_style()));
    }

    private void Slider(string label, Func<StyleSettings, double> get, Func<StyleSettings, double, StyleSettings> set)
    {
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05 };
        slider.DragEnded += changed =>
        {
            if (changed)
                _setStyle(s => set(s, slider.Value));
        };
        Row(label, slider);
        _readers.Add(() => slider.Value = get(_style()));
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
        foreach (var read in _readers)
            read();
        _updating = false;
    }
}
