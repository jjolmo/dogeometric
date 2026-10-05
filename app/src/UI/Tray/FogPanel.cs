using Dogeometric.App.Viewport;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Fog panel: fog on or off, where it starts and is full, and its colour.</summary>
public partial class FogPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private ModelViewport _view = null!;
    private Action _toggled = null!;

    public static FogPanel Create(Func<Document> doc, ModelViewport view, Action toggled)
    {
        var p = new FogPanel { _doc = doc, _view = view, _toggled = toggled };
        p.Refresh();
        return p;
    }

    /// <summary>The model's fog settings shown in the view.</summary>
    public void Apply()
    {
        var o = _doc().Model.Options;
        _view.SetFog(o.FogStart, o.FogEnd, o.FogColor is { } c ? Color.Color8(c.R, c.G, c.B) : null);
    }

    private void Set(Func<ModelOptions, ModelOptions> change)
    {
        var doc = _doc();
        doc.Undo.Begin("Fog");
        doc.Model.Options = change(doc.Model.Options);
        doc.Undo.Commit();
        Apply();
    }

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var o = _doc().Model.Options;
        var show = new CheckBox { Text = "Display Fog", ButtonPressed = _view.ShowFog };
        show.Toggled += on =>
        {
            _view.ShowFog = on;
            Apply();
            _toggled();
        };
        AddChild(show);
        AddChild(new Label { Text = "Distance" });
        HSlider Slider(string label, double value, Action<double> set)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(48, 0) });
            var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            slider.DragEnded += changed =>
            {
                if (changed)
                    set(slider.Value);
            };
            row.AddChild(slider);
            AddChild(row);
            return slider;
        }
        Slider("Start", o.FogStart, v => Set(m => m with { FogStart = Math.Min(v, m.FogEnd) }));
        Slider("End", o.FogEnd, v => Set(m => m with { FogEnd = Math.Max(v, m.FogStart) }));
        var colorRow = new HBoxContainer();
        var background = new CheckBox { Text = "Use background color", ButtonPressed = o.FogColor == null };
        var pick = new ColorPickerButton
        {
            Color = o.FogColor is { } fc ? Color.Color8(fc.R, fc.G, fc.B) : new Color(0.74f, 0.76f, 0.79f),
            CustomMinimumSize = new Vector2(48, 22),
            EditAlpha = false,
            Disabled = o.FogColor == null,
        };
        Rgba Rgb(Color c) => new((byte)c.R8, (byte)c.G8, (byte)c.B8);
        background.Toggled += on =>
        {
            pick.Disabled = on;
            Set(m => m with { FogColor = on ? null : Rgb(pick.Color) });
        };
        pick.PopupClosed += () => Set(m => m with { FogColor = Rgb(pick.Color) });
        colorRow.AddChild(background);
        colorRow.AddChild(pick);
        AddChild(colorRow);
    }
}
