using System.Globalization;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Shadows panel: on/off, UTC offset, time, date, light, dark, sun shading and where shadows show.
/// Sliders show the result live and record one undo step on release.</summary>
public partial class ShadowsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action _apply = null!;
    private ShadowSettings? _start;
    private bool _updating;

    private CheckButton _enabled = null!;
    private OptionButton _utc = null!;
    private HSlider _time = null!;
    private LineEdit _timeText = null!;
    private HSlider _date = null!;
    private LineEdit _dateText = null!;
    private HSlider _light = null!;
    private SpinBox _lightValue = null!;
    private HSlider _dark = null!;
    private SpinBox _darkValue = null!;
    private CheckBox _sunShading = null!;
    private CheckBox _onFaces = null!;
    private CheckBox _onGround = null!;
    private CheckBox _fromEdges = null!;

    private static readonly double[] Offsets =
        [-12, -11, -10, -9.5, -9, -8, -7, -6, -5, -4, -3.5, -3, -2, -1, 0, 1, 2, 3, 3.5, 4, 4.5, 5, 5.5, 5.75, 6, 6.5, 7, 8, 9, 9.5, 10, 11, 12, 13, 14];

    /// <param name="apply">Shows the model's settings in the view.</param>
    public static ShadowsPanel Create(Func<Document> doc, Action apply)
    {
        var p = new ShadowsPanel { _doc = doc, _apply = apply };
        var top = new HBoxContainer();
        p._enabled = new CheckButton { Text = "Show shadows", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        top.AddChild(p._enabled);
        p._utc = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var o in Offsets)
            p._utc.AddItem(UtcLabel(o));
        top.AddChild(p._utc);
        p.AddChild(top);

        (p._time, p._timeText) = p.SliderRow("Time", 0, 24 * 60 - 1, 1);
        (p._date, p._dateText) = p.SliderRow("Date", 1, 365, 1);
        (p._light, p._lightValue) = p.PercentRow("Light");
        (p._dark, p._darkValue) = p.PercentRow("Dark");
        p._sunShading = p.Check("Use sun for shading");
        p.AddChild(new Label { Text = "Display:" });
        var display = new HBoxContainer();
        p.AddChild(display);
        p._onFaces = p.Check("On faces", display);
        p._onGround = p.Check("On ground", display);
        p._fromEdges = p.Check("From edges", display);

        p._enabled.Toggled += on => p.Change(s => s with { Enabled = on }, live: false);
        p._utc.ItemSelected += i => p.Change(s => s with { UtcOffset = Offsets[i] }, live: false);
        p._time.ValueChanged += v => p.Change(s => s with { Time = s.Time.Date.AddMinutes(v) }, live: true);
        p._time.DragEnded += _ => p.Finish();
        p._timeText.TextSubmitted += t => p.TypeTime(t);
        p._date.ValueChanged += v => p.Change(s => s with { Time = new DateTime(s.Time.Year, 1, 1).AddDays(Math.Min(v, 365) - 1) + s.Time.TimeOfDay }, live: true);
        p._date.DragEnded += _ => p.Finish();
        p._dateText.TextSubmitted += t => p.TypeDate(t);
        p._light.ValueChanged += v => p.Change(s => s with { Light = (int)v }, live: true);
        p._light.DragEnded += _ => p.Finish();
        p._lightValue.ValueChanged += v => p.Change(s => s with { Light = (int)v }, live: false);
        p._dark.ValueChanged += v => p.Change(s => s with { Dark = (int)v }, live: true);
        p._dark.DragEnded += _ => p.Finish();
        p._darkValue.ValueChanged += v => p.Change(s => s with { Dark = (int)v }, live: false);
        p._sunShading.Toggled += on => p.Change(s => s with { UseSunForShading = on }, live: false);
        p._onFaces.Toggled += on => p.Change(s => s with { OnFaces = on }, live: false);
        p._onGround.Toggled += on => p.Change(s => s with { OnGround = on }, live: false);
        p._fromEdges.Toggled += on => p.Change(s => s with { FromEdges = on }, live: false);
        p.Refresh();
        return p;
    }

    private static string UtcLabel(double o)
    {
        var span = TimeSpan.FromHours(Math.Abs(o));
        return $"UTC{(o < 0 ? "-" : "+")}{span.Hours:00}:{span.Minutes:00}";
    }

    private (HSlider, LineEdit) SliderRow(string name, double min, double max, double step)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(44, 0) });
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(slider);
        var text = new LineEdit { CustomMinimumSize = new Vector2(78, 0) };
        row.AddChild(text);
        AddChild(row);
        return (slider, text);
    }

    private (HSlider, SpinBox) PercentRow(string name)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(44, 0) });
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(slider);
        var spin = new SpinBox { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(78, 0) };
        row.AddChild(spin);
        AddChild(row);
        return (slider, spin);
    }

    private CheckBox Check(string text, Container? parent = null)
    {
        var c = new CheckBox { Text = text, FocusMode = FocusModeEnum.None };
        (parent ?? this).AddChild(c);
        return c;
    }

    /// <summary>Changes the model's settings; a live change waits for <see cref="Finish"/> to become one undo step.</summary>
    private void Change(Func<ShadowSettings, ShadowSettings> edit, bool live)
    {
        if (_updating)
            return;
        var model = _doc().Model;
        _start ??= model.Shadows;
        model.Shadows = edit(model.Shadows);
        if (!live)
            Finish();
        _apply();
    }

    private void Finish()
    {
        if (_start is not { } start)
            return;
        _start = null;
        var doc = _doc();
        var end = doc.Model.Shadows;
        if (end == start)
            return;
        doc.Model.Shadows = start;
        doc.Undo.Begin("Shadow Settings");
        doc.Model.Shadows = end;
        doc.Undo.Commit();
    }

    private void TypeTime(string text)
    {
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var t))
            Change(s => s with { Time = s.Time.Date + t.TimeOfDay }, live: false);
        else
            Refresh();
    }

    private void TypeDate(string text)
    {
        var parts = text.Split('/', '-', '.');
        if (parts.Length == 2 && int.TryParse(parts[0], out var month) && int.TryParse(parts[1], out var day) && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2021, month))
            Change(s => s with { Time = new DateTime(s.Time.Year, month, day) + s.Time.TimeOfDay }, live: false);
        else
            Refresh();
    }

    /// <summary>Shows the current model's settings.</summary>
    public void Refresh()
    {
        var s = _doc().Model.Shadows;
        _updating = true;
        _enabled.ButtonPressed = s.Enabled;
        var utc = Array.IndexOf(Offsets, s.UtcOffset);
        _utc.Selected = utc;
        _time.Value = s.Time.TimeOfDay.TotalMinutes;
        _timeText.Text = s.Time.ToString("hh:mm tt", CultureInfo.InvariantCulture);
        _date.Value = s.Time.DayOfYear;
        _dateText.Text = s.Time.ToString("MM/dd", CultureInfo.InvariantCulture);
        _light.Value = _lightValue.Value = s.Light;
        _dark.Value = _darkValue.Value = s.Dark;
        _sunShading.ButtonPressed = s.UseSunForShading;
        _sunShading.Disabled = s.Enabled;
        _onFaces.ButtonPressed = s.OnFaces;
        _onGround.ButtonPressed = s.OnGround;
        _fromEdges.ButtonPressed = s.FromEdges;
        _updating = false;
    }
}
