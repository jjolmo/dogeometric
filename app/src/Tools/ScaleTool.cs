using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using System.Globalization;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Scale: a box with grips around the selection (aligned with a lone group's or component's own axes).
/// Corner grips scale uniformly, edge grips along two axes, face grips along one; the opposite grip stays put.
/// Shift toggles uniform scaling, Ctrl scales about the centre. Type a factor ("2", "2,1,0.5", "-1" mirrors) or a
/// dimension with its unit ("120mm").
/// </summary>
public sealed class ScaleTool : DrawingTool
{
    private const float GripPixels = 7;

    private List<object> _items = [];
    private Transform _frame = Transform.Identity; // box coordinates → active context's local coordinates
    private Bounds3 _box = Bounds3.Empty;          // in box coordinates
    private (int X, int Y, int Z)? _hover;
    private (int X, int Y, int Z)? _grip;
    private bool _shift;
    private bool _aboutCenter;

    public override int CommandId => CommandIds.Scale;
    public override string VcbLabel => "Scale";

    public override string StatusText
    {
        get
        {
            var grip = _grip ?? _hover;
            var mode = (grip is { } g && Uniform(g), _aboutCenter) switch
            {
                (true, true) => " uniformly about center",
                (true, false) => " uniformly",
                (false, true) => " about center",
                _ => "",
            };
            if (_items.Count == 0)
                return $"Click the item or object you want to scale{(_aboutCenter ? " about center" : "")}.";
            return _grip == null
                ? $"Click a scale grip to begin scaling{mode}."
                : $"Click to finish scaling{mode}, or enter a scale factor or dimension.";
        }
    }

    public override string VcbValue => Factors() is { } f ? FormatFactors(f) : "";

    public override void Activate()
    {
        if (View.Document is { } doc && !doc.Selection.IsEmpty)
            SetItems(doc, doc.Selection.Items.ToList());
    }

    public override void Deactivate()
    {
        _items = [];
        _grip = null;
    }

    private void SetItems(Document doc, List<object> items)
    {
        _items = items;
        if (items is [ComponentInstance inst])
        {
            _frame = inst.Transform;
            _box = inst.Definition.Entities.Bounds();
        }
        else
        {
            _frame = Transform.Identity;
            var points = new List<Vec3>();
            foreach (var item in items)
            {
                switch (item)
                {
                    case Face f:
                        points.AddRange(f.OuterLoop.Points);
                        break;
                    case Edge e:
                        points.Add(e.Start.Position);
                        points.Add(e.End.Position);
                        break;
                }
            }
            var box = Bounds3.FromPoints(points);
            foreach (var inst2 in items.OfType<ComponentInstance>())
                box = box.Include(new Entities { Instances = { inst2 } }.Bounds());
            _box = box;
        }
        if (_box.IsEmpty)
            _items = [];
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    // ------------------------------------------------------------------ grips

    /// <summary>Grip (−1/0/1 per axis) → point in box coordinates.</summary>
    private Vec3 GripPoint((int X, int Y, int Z) g)
    {
        var c = _box.Center;
        var h = _box.Size * 0.5;
        return new Vec3(c.X + g.X * h.X, c.Y + g.Y * h.Y, c.Z + g.Z * h.Z);
    }

    private Vec3 ToWorld(Vec3 boxPoint) => View.Document!.Context.ToWorld.ApplyPoint(_frame.ApplyPoint(boxPoint));

    private IEnumerable<(int X, int Y, int Z)> Grips()
    {
        var size = _box.Size;
        // A flat box (a lone face) has no grips across its thickness.
        int[] Range(double extent) => extent > Tolerance.Length ? [-1, 0, 1] : [0];
        foreach (var x in Range(size.X))
            foreach (var y in Range(size.Y))
                foreach (var z in Range(size.Z))
                    if ((x, y, z) != (0, 0, 0))
                        yield return (x, y, z);
    }

    private (int X, int Y, int Z)? GripAt(Vector2 mouse)
    {
        (int, int, int)? best = null;
        var bestDistance = GripPixels;
        foreach (var g in Grips())
        {
            if (View.ToScreen(ToWorld(GripPoint(g))) is { } p && p.DistanceTo(mouse) <= bestDistance)
            {
                bestDistance = p.DistanceTo(mouse);
                best = g;
            }
        }
        return best;
    }

    private static int ActiveAxes((int X, int Y, int Z) g) => Math.Abs(g.X) + Math.Abs(g.Y) + Math.Abs(g.Z);

    /// <summary>Corner grips scale uniformly and the others don't; Shift swaps that.</summary>
    private bool Uniform((int X, int Y, int Z) g) => (ActiveAxes(g) == 3) != _shift;

    private Vec3 Anchor((int X, int Y, int Z) g) => _aboutCenter ? _box.Center : GripPoint((-g.X, -g.Y, -g.Z));

    // ------------------------------------------------------------------ factors from the cursor

    /// <summary>Scale factors along the box axes for the cursor, or null before a grip is picked.</summary>
    private Vec3? Factors()
    {
        if (_grip is not { } g || View.Document is not { } doc)
            return null;
        var toBox = doc.Context.ToWorld.Inverse();
        var boxFromWorld = _frame.Inverse();
        var anchor = Anchor(g);
        var grip = GripPoint(g);
        var ray = View.ScreenRay(Mouse);
        // Work in box coordinates: the ray, and a snapped point when the inference found one.
        var origin = boxFromWorld.ApplyPoint(toBox.ApplyPoint(ray.Origin));
        var direction = boxFromWorld.ApplyVector(toBox.ApplyVector(ray.Direction)).Normalized();
        Vec3? snapped = Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.OnEdge } inf
            ? boxFromWorld.ApplyPoint(toBox.ApplyPoint(inf.Point))
            : null;

        Vec3 target;
        if (Uniform(g))
        {
            // Along the line from the anchor through the grip.
            var axis = (grip - anchor).Normalized();
            target = snapped is { } s ? anchor + axis * (s - anchor).Dot(axis) : ClosestOnLine(anchor, axis, origin, direction);
        }
        else if (ActiveAxes(g) == 1)
        {
            var axis = new Vec3(g.X, g.Y, g.Z);
            target = snapped is { } s ? anchor + axis * (s - anchor).Dot(axis) : ClosestOnLine(grip, axis, origin, direction);
        }
        else if (ActiveAxes(g) == 2)
        {
            // In the plane through the grip spanned by its two axes.
            var normal = new Vec3(g.X == 0 ? 1 : 0, g.Y == 0 ? 1 : 0, g.Z == 0 ? 1 : 0);
            if (snapped is { } s)
                target = s;
            else if (InferenceEngine.IntersectPlane(new Ray(origin, direction), normal, grip) is { } hit)
                target = hit;
            else
                return null;
        }
        else
        {
            // Non-uniform corner (Shift): the inferred point decides all three factors.
            if (Current is not { } c)
                return null;
            target = boxFromWorld.ApplyPoint(toBox.ApplyPoint(c.Point));
        }

        double Factor(int sign, double a, double gripCoordinate, double t)
        {
            if (sign == 0 || Math.Abs(gripCoordinate - a) < 1e-9)
                return 1;
            return (t - a) / (gripCoordinate - a);
        }
        var factors = new Vec3(Factor(g.X, anchor.X, grip.X, target.X), Factor(g.Y, anchor.Y, grip.Y, target.Y), Factor(g.Z, anchor.Z, grip.Z, target.Z));
        if (Uniform(g))
        {
            // Every axis of the box gets the grip's factor (the active ones agree on the diagonal); flat axes stay flat.
            var f = g.X != 0 ? factors.X : g.Y != 0 ? factors.Y : factors.Z;
            var size = _box.Size;
            factors = new Vec3(size.X > Tolerance.Length ? f : 1, size.Y > Tolerance.Length ? f : 1, size.Z > Tolerance.Length ? f : 1);
        }
        return new Vec3(Round(factors.X), Round(factors.Y), Round(factors.Z));
    }

    /// <summary>Snaps factors to 0.01 steps, as SketchUp's drag shows two decimals.</summary>
    private static double Round(double f) => Math.Round(f, 2);

    private static Vec3 ClosestOnLine(Vec3 point, Vec3 axis, Vec3 rayOrigin, Vec3 rayDirection)
    {
        var w = point - rayOrigin;
        double b = axis.Dot(rayDirection), d = axis.Dot(w), e = rayDirection.Dot(w);
        var denom = 1 - b * b;
        if (Math.Abs(denom) < 1e-12)
            return point;
        var t = (b * e - d) / denom;
        return point + axis * t;
    }

    /// <summary>One factor for a uniform scale, else one per axis the grip moves (as SketchUp lists them).</summary>
    private string FormatFactors(Vec3 f)
    {
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        if (_grip is not { } g)
            return F(f.X);
        if (Uniform(g))
            return F(g.X != 0 ? f.X : g.Y != 0 ? f.Y : f.Z);
        var active = new List<double>();
        if (g.X != 0) active.Add(f.X);
        if (g.Y != 0) active.Add(f.Y);
        if (g.Z != 0) active.Add(f.Z);
        return string.Join(", ", active.Select(F));
    }

    // ------------------------------------------------------------------ input

    protected override void OnInferenceChanged()
    {
        if (_grip == null)
        {
            var hover = _items.Count > 0 ? GripAt(Mouse) : null;
            if (hover != _hover)
            {
                _hover = hover;
                RefreshStatus();
            }
        }
        View.ShowVcbValue(VcbValue);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_grip == null)
        {
            if (_items.Count > 0 && GripAt(position) is { } g)
            {
                _grip = g;
            }
            else if (ItemUnderCursor(doc, position) is { Count: > 0 } items)
            {
                doc.Selection.Set(items);
                SetItems(doc, items);
            }
        }
        else if (Factors() is { } f)
        {
            Finish(doc, f);
        }
        RefreshStatus();
    }

    private List<object> ItemUnderCursor(Document doc, Vector2 position)
    {
        if (View.Pick(position) is not { } hit)
            return [];
        var context = doc.Context.Path;
        if (hit.Path.Count < context.Count || !hit.Path.Take(context.Count).SequenceEqual(context))
            return [];
        return [hit.Path.Count > context.Count ? hit.Path[context.Count] : hit.Entity];
    }

    private void Finish(Document doc, Vec3 factors)
    {
        if (_grip is not { } g || factors.X == 0 || factors.Y == 0 || factors.Z == 0)
            return;
        var t = Transforming.Scale(_frame, Anchor(g), factors.X, factors.Y, factors.Z);
        var items = _items;
        doc.Operation("Scale", e => Transforming.Apply(e, items, t));
        _grip = null;
        SetItems(doc, items);
    }

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc || _grip is not { } g)
            return false;
        var parts = text.Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // "1,5" is a decimal factor in comma locales, not two factors; SketchUp separates with ';' there.
        if (text.Contains(',') && !text.Contains(';') && parts.Length == 2 && !parts.Any(p => p.Any(char.IsLetter)) && parts[1].Length > 0 && !text.Contains(", "))
            parts = [text.Replace(',', '.')];
        if (parts.Length is 0 or > 3)
            return false;

        // Which box axes the typed values apply to: the grip's active axes, or all for a uniform drag.
        var size = _box.Size;
        var axes = new List<int>();
        if (g.X != 0) axes.Add(0);
        if (g.Y != 0) axes.Add(1);
        if (g.Z != 0) axes.Add(2);
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            var axis = axes[Math.Min(i, axes.Count - 1)];
            if (p.Any(char.IsLetter))
            {
                // A dimension: the new size along that axis.
                if (!Length.TryParse(p, LengthUnit.Millimeters, out var mm))
                    return false;
                var extent = axis == 0 ? size.X : axis == 1 ? size.Y : size.Z;
                if (extent <= Tolerance.Length)
                    return false;
                values[i] = mm / extent;
            }
            else if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
            if (values[i] == 0)
                return false;
        }

        var factors = new[] { 1.0, 1.0, 1.0 };
        if (values.Length == 1 && Uniform(g))
        {
            for (var a = 0; a < 3; a++)
                if ((a == 0 ? size.X : a == 1 ? size.Y : size.Z) > Tolerance.Length)
                    factors[a] = values[0];
        }
        else
        {
            for (var i = 0; i < values.Length && i < axes.Count; i++)
                factors[axes[i]] = values[i];
            if (values.Length == 1)
                foreach (var a in axes)
                    factors[a] = values[0];
        }
        Finish(doc, new Vec3(factors[0], factors[1], factors[2]));
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Shift when !key.Echo:
                _shift = true;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Ctrl when !key.Echo:
                _aboutCenter = true;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Escape when _grip != null:
                _grip = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
        }
        return base.KeyDown(key);
    }

    public override bool KeyUp(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Shift:
                _shift = false;
                break;
            case Key.Ctrl:
                _aboutCenter = false;
                break;
            default:
                return base.KeyUp(key);
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
        return true;
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Control overlay)
    {
        if (_items.Count == 0 || View.Document == null)
            return;

        // The box as it would look after the drag: the original corners scaled by the current factors.
        var preview = Transform.Identity;
        if (_grip is { } g && Factors() is { } f && f.X != 0 && f.Y != 0 && f.Z != 0)
            preview = Transforming.Scale(Transform.Identity, Anchor(g), f.X, f.Y, f.Z);
        Vec3 Corner(int i) => ToWorld(preview.ApplyPoint(GripPoint(((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        var boxColor = new Color(0.85f, 0.65f, 0);
        foreach (var (a, b) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
            DrawWorldLine(overlay, Corner(a), Corner(b), boxColor, 1);

        foreach (var grip in Grips())
        {
            if (View.ToScreen(ToWorld(preview.ApplyPoint(GripPoint(grip)))) is not { } p)
                continue;
            var active = grip == (_grip ?? _hover);
            var color = active ? new Color(0.9f, 0, 0) : new Color(0, 0.6f, 0);
            overlay.DrawRect(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), color);
            overlay.DrawRect(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), Colors.Black, filled: false, width: 1);
        }

        if (_grip is { } dragged && View.ToScreen(ToWorld(Anchor(dragged))) is { } anchor)
        {
            // The fixed point, and the factor next to the cursor.
            overlay.DrawCircle(anchor, 4, new Color(0.9f, 0, 0));
            if (Factors() is { } factors)
            {
                var font = overlay.GetThemeDefaultFont();
                overlay.DrawString(font, Mouse + new Vector2(16, 24), FormatFactors(factors), HorizontalAlignment.Left, -1, 13, Colors.Black);
            }
        }
    }
}
