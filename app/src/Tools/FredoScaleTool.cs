using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>FredoScale's tapering, twisting, shearing and bending of the selection's box: arrows pick the axis; drag sideways
/// (live) and click, or type the amount.</summary>
public sealed class FredoScaleTool(Deformation kind) : Tool
{
    private static readonly Dictionary<Deformation, int> Axes = [];
    private bool _dragging;
    private Vector2 _start;
    private double _amount;

    public override int CommandId => ExtensionIds.FredoScale(kind);
    public override string CursorImage => "scale";
    public override string VcbLabel => kind == Deformation.Taper ? "Percent" : "Angle";
    public override string VcbValue => kind == Deformation.Taper ? $"{_amount:0.#}%" : $"{_amount:0.#}°";

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
        _ => "Radial Bending",
    };

    public override string StatusText =>
        $"{Name} along {(Axis == 0 ? "red" : Axis == 1 ? "green" : "blue")} (arrow keys change it). " +
        (_dragging ? "Move sideways, then click." : "Click and drag sideways, or type the amount.");

    public override void Activate()
    {
        _amount = kind == Deformation.Taper ? 100 : 0;
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

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (Targets(doc).Count == 0)
            return;
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
        if (!_dragging)
            return;
        var dx = position.X - _start.X;
        _amount = kind == Deformation.Taper ? Math.Max(0, 100 + dx / 2) : dx / 2;
        View.ShowVcbValue(VcbValue);
        Preview();
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().TrimEnd('%', '°');
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
            case Key.Escape when _dragging:
                View.Document?.CancelPreview();
                _dragging = false;
                RefreshStatus();
                return true;
        }
        return false;
    }
}
