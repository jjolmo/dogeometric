using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Soften Edges panel: angle between normals, "Smooth normals", "Soften coplanar" on the selected edges;
/// the slider shows the result live and records one step on release.</summary>
public partial class SoftenEdgesPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private HSlider _angle = null!;
    private Label _value = null!;
    private CheckBox _smooth = null!;
    private CheckBox _coplanar = null!;

    public static SoftenEdgesPanel Create(Func<Document> doc)
    {
        var p = new SoftenEdgesPanel { _doc = doc };
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Angle between Normals" });
        p._value = new Label { Text = "20.0", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(p._value);
        p.AddChild(row);
        p._angle = new HSlider { MinValue = 0, MaxValue = 180, Step = 0.5, Value = 20, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        p.AddChild(p._angle);
        p._smooth = new CheckBox { Text = "Smooth normals", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        p._coplanar = new CheckBox { Text = "Soften coplanar", ButtonPressed = false, FocusMode = FocusModeEnum.None };
        p.AddChild(p._smooth);
        p.AddChild(p._coplanar);

        p._angle.ValueChanged += v =>
        {
            p._value.Text = v.ToString("0.0");
            p.Apply(preview: true);
        };
        p._angle.DragEnded += _ => p._doc().CommitPreview();
        p._smooth.Toggled += _ => p.Apply(preview: false);
        p._coplanar.Toggled += _ => p.Apply(preview: false);
        return p;
    }

    private void Apply(bool preview)
    {
        var doc = _doc();
        var edges = doc.Selection.Items.OfType<Edge>()
            .Concat(doc.Selection.Items.OfType<Face>().SelectMany(Topology.EdgesOf)).Distinct().ToList();
        if (edges.Count == 0)
            return;
        var (angle, smooth, coplanar) = (_angle.Value, _smooth.ButtonPressed, _coplanar.ButtonPressed);
        if (preview)
            doc.Preview("Soften Edges", e => Editing.SoftenEdges(e, edges, angle, smooth, coplanar));
        else
        {
            doc.CommitPreview();
            doc.Operation("Soften Edges", e => Editing.SoftenEdges(e, edges, angle, smooth, coplanar));
        }
    }
}
