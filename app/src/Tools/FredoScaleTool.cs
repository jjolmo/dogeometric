using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>FredoScale's deformations of the selection's box: arrows pick the axis; drag sideways (live) and click, or
/// type the amount. "To Target" picks a point of the box, then where it should go.</summary>
public sealed class FredoScaleTool(Deformation kind, bool toTarget = false) : DrawingTool
{
    private static readonly Dictionary<Deformation, int> Axes = [];
    private bool _dragging;
    private Vector2 _start;
    private double _amount;
    private Vec3? _reference;

    public override int CommandId => !toTarget ? ExtensionIds.FredoScale(kind) : kind switch
    {
        Deformation.Scale => ExtensionIds.FredoScaleScaleTarget,
        Deformation.Taper => ExtensionIds.FredoScaleTaperTarget,
        Deformation.Shear => ExtensionIds.FredoScaleShearTarget,
        _ => ExtensionIds.FredoScaleStretchTarget,
    };
    protected override Vec3? From => _reference;
    public override string CursorImage => "scale";
    private bool Percent => kind is Deformation.Taper or Deformation.Scale;

    public override string VcbLabel => Percent ? "Percent" : kind == Deformation.Stretch ? "Length" : "Angle";
    public override string VcbValue => Percent ? $"{_amount:0.#}%" : kind == Deformation.Stretch ? $"{_amount:0.#} mm" : $"{_amount:0.#}°";

    private int Axis
    {
        get => Axes.GetValueOrDefault(kind, 2);
        set => Axes[kind] = value;
    }

    private string Name => kind switch
    {
        Deformation.Taper => "Box Tapering",
        Deformation.Twist => "Box Twisting",
        Deformation.Shear => "Planar Shearing",
        Deformation.Scale => "Box Scaling",
        Deformation.Stretch => "Box Stretching",
        Deformation.Rotate => "Box Rotation",
        _ => "Radial Bending",
    };

    public override string StatusText =>
        $"{Name}{(toTarget ? " to Target" : "")} along {(Axis == 0 ? "red" : Axis == 1 ? "green" : "blue")} (arrow keys change it). " +
        (toTarget ? _reference == null ? "Click the point of the box to move." : "Click where it should go."
            : _dragging ? "Move sideways, then click." : "Click and drag sideways, or type the amount.");

    public override void Activate()
    {
        _amount = Percent ? 100 : 0;
        View.ShowVcbValue(VcbValue);
    }

    public override void Deactivate() => View.Document?.CancelPreview();

    /// <summary>Where to deform: the selected geometry, or everything inside selected groups and components.</summary>
    private List<(Entities Entities, List<object> Items)> Targets(Document doc)
    {
        var sel = doc.Selection.Items.ToList();
        var list = new List<(Entities, List<object>)>();
        var geometry = sel.Where(x => x is Face or Edge).ToList();
        if (geometry.Count > 0)
            list.Add((doc.Context.Entities, geometry));
        foreach (var def in sel.OfType<ComponentInstance>().Select(i => i.Definition).Distinct())
            list.Add((def.Entities, def.Entities.Faces.Cast<object>().Concat(def.Entities.Edges).ToList()));
        return list;
    }

    private void Preview()
    {
        if (View.Document is not { } doc)
            return;
        var targets = Targets(doc);
        if (targets.Count == 0)
            return;
        var amount = _amount;
        var axis = Axis;
        doc.Preview(Name, _ =>
        {
            foreach (var (e, items) in targets)
            {
                doc.Undo.Touch(e);
                FredoScale.Apply(e, items, kind, axis, amount);
            }
        });
    }

    /// <summary>World to the coordinates of the first thing deformed (the open group, or a selected instance's definition).</summary>
    private static Transform ToLocal(Document doc, Entities e) => e == doc.Context.Entities
        ? doc.Context.ToWorld.Inverse()
        : doc.Selection.Items.OfType<ComponentInstance>().First(i => i.Definition.Entities == e).Transform.Then(doc.Context.ToWorld).Inverse();

    /// <summary>To Target: the amount that takes the picked point to the cursor's point.</summary>
    private void FollowTarget()
    {
        if (_reference is not { } from || Current is not { } c || View.Document is not { } doc || Targets(doc) is not [var (e, items), ..])
            return;
        var toLocal = ToLocal(doc, e);
        if (FredoScale.TargetAmount(kind, Axis, FredoScale.BoxOf(items), toLocal.ApplyPoint(from), toLocal.ApplyPoint(c.Point)) is { } amount)
        {
            _amount = amount;
            View.ShowVcbValue(VcbValue);
            Preview();
        }
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (Targets(doc).Count == 0)
            return;
        if (toTarget)
        {
            if (_reference == null)
                _reference = Current?.Point;
            else
            {
                doc.CommitPreview();
                _reference = null;
            }
            RefreshStatus();
            return;
        }
        if (!_dragging)
        {
            _dragging = true;
            _start = position;
            RefreshStatus();
            return;
        }
        doc.CommitPreview();
        _dragging = false;
        RefreshStatus();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (toTarget)
        {
            FollowTarget();
            return;
        }
        if (!_dragging)
            return;
        var dx = position.X - _start.X;
        _amount = Percent ? Math.Max(0, 100 + dx / 2) : dx / 2;
        View.ShowVcbValue(VcbValue);
        Preview();
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().TrimEnd('%', '°').Replace("mm", "").Trim();
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return false;
        _amount = v;
        Preview();
        View.Document?.CommitPreview();
        _dragging = false;
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Right or Key.Left or Key.Up:
                Axis = key.Keycode == Key.Right ? 0 : key.Keycode == Key.Left ? 1 : 2;
                if (_dragging)
                    Preview();
                RefreshStatus();
                return true;
            case Key.Escape when _dragging || _reference != null:
                View.Document?.CancelPreview();
                _dragging = false;
                _reference = null;
                RefreshStatus();
                return true;
        }
        return false;
    }

    public override void Draw(Control overlay)
    {
        if (!toTarget)
            return;
        if (_reference is { } r && Current is { } c)
            DrawWorldLine(overlay, r, c.Point, Colors.Black, 1, dashed: true);
        DrawInference(overlay);
    }
}
