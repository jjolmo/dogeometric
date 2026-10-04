using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's status bar: tool hint on the left, Measurements box on the right.</summary>
public partial class StatusBar : PanelContainer
{
    private Label _hint = null!;
    private Label _vcbLabel = null!;

    public LineEdit Vcb { get; private set; } = null!;

    public override void _Ready()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);

        _hint = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        row.AddChild(_hint);

        _vcbLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(120, 0) };
        row.AddChild(_vcbLabel);

        Vcb = new LineEdit { CustomMinimumSize = new Vector2(180, 0), Editable = true, FocusMode = FocusModeEnum.Click };
        row.AddChild(Vcb);
    }

    public void SetHint(string text) => _hint.Text = text;

    public void SetVcbLabel(string text, bool editable)
    {
        _vcbLabel.Text = text;
        Vcb.Editable = editable;
    }
}
