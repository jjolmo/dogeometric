using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>FredoScale's Planar Shearing (Free): click the origin, then a point along the line to lean, then where it
/// should lean to (or type the angle). The selection shears live.</summary>
public sealed class ShearFreeTool : DrawingTool
{
    private Vec3? _origin;
    private Vec3? _reference;
    private double _angle;

    public override int CommandId => ExtensionIds.FredoScaleShearFree;
    public override string CursorImage => "scale";
    protected override Vec3? From => _origin;
    public override string VcbLabel => "Angle";
    public override string VcbValue => $"{_angle:0.#}°";

    public override string StatusText => _origin == null ? "Planar Shearing (Free): click the origin."
        : _reference == null ? "Click a point along the line that leans."
        : "Click where the line should lean to, or type the angle.";

    private List<(Entities Entities, List<object> Items, Transform ToLocal)> Targets(Document doc)
    {
        var sel = doc.Selection.Items.ToList();
        var list = new List<(Entities, List<object>, Transform)>();
        var geometry = sel.Where(x => x is Face or Edge).ToList();
        if (geometry.Count > 0)
            list.Add((doc.Context.Entities, geometry, doc.Context.ToWorld.Inverse()));
        foreach (var inst in sel.OfType<ComponentInstance>())
            list.Add((inst.Definition.Entities, inst.Definition.Entities.Faces.Cast<object>().Concat(inst.Definition.Entities.Edges).ToList(),
                inst.Transform.Then(doc.Context.ToWorld).Inverse()));
        return list;
    }

    private void Preview(Vec3 to)
    {
        if (_origin is not { } o || _reference is not { } r || View.Document is not { } doc)
            return;
        var targets = Targets(doc);
        var (from, toward) = (r - o, to - o);
        doc.Preview("Planar Shearing", _ =>
        {
            foreach (var (e, items, toLocal) in targets)
            {
                doc.Undo.Touch(e);
                FredoScale.ShearFree(e, items, toLocal.ApplyPoint(o), toLocal.ApplyVector(from), toLocal.ApplyVector(toward));
            }
        });
        var axis = from.Normalized();
        _angle = Math.Atan2((toward - axis * toward.Dot(axis)).Length, toward.Dot(axis)) * 180 / Math.PI;
        View.ShowVcbValue(VcbValue);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } c || View.Document is not { } doc || Targets(doc).Count == 0)
            return;
        if (_origin == null)
            _origin = c.Point;
        else if (_reference == null)
            _reference = c.Point.DistanceTo(_origin.Value) > Tolerance.Length ? c.Point : null;
        else
        {
            doc.CommitPreview();
            (_origin, _reference) = (null, null);
        }
        RefreshStatus();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_reference != null && Current is { } c)
            Preview(c.Point);
    }

    public override bool ApplyVcb(string text)
    {
        if (_origin is not { } o || _reference is not { } r || Current is not { } c
            || !double.TryParse(text.Trim().TrimEnd('°'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees)
            || Math.Abs(degrees) >= 89)
            return false;
        var axis = (r - o).Normalized();
        var side = (c.Point - o) - axis * (c.Point - o).Dot(axis);
        if (side.Length < 1e-9)
            return false;
        Preview(o + axis + side.Normalized() * Math.Tan(degrees * Math.PI / 180));
        View.Document?.CommitPreview();
        (_origin, _reference) = (null, null);
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _origin != null)
        {
            View.Document?.CancelPreview();
            (_origin, _reference) = (null, null);
            RefreshStatus();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Deactivate() => View.Document?.CancelPreview();

    public override void Draw(Control overlay)
    {
        if (_origin is { } o && Current is { } c)
        {
            if (_reference is { } r)
            {
                DrawWorldLine(overlay, o, r, Colors.Black, 1.5f);
                DrawWorldLine(overlay, o, c.Point, new Color(0.85f, 0.1f, 0.1f), 1.5f, dashed: true);
            }
            else
                DrawWorldLine(overlay, o, c.Point, Colors.Black, 1, dashed: true);
        }
        DrawInference(overlay);
    }
}
