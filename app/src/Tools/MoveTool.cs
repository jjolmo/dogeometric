using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Dogeometric.App.Viewport;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Move: pick up a point, drop it on another; moves the selection, or what is under the cursor when
/// nothing is selected (a corner when on an endpoint). Faces bent by the move fold into flat pieces (auto-fold).
/// Ctrl copies; after a copy, typing "5x" makes an array of 5, "/5" divides the distance.
/// Typed lengths move along the current direction.
/// </summary>
public sealed class MoveTool : DrawingTool
{
    private Vec3? _from;
    private List<object> _items = [];
    private bool _copy;
    // Stamp (Ctrl's third step): each click leaves a copy and the items stay in hand.
    private bool _stamp;
    private bool _autofold;

    // Rotation grips of the group or component under the cursor (Move's red crosshairs): centre and normal (world).
    private ComponentInstance? _gripsOf;
    private List<(Vec3 Center, Vec3 Normal)> _grips = [];
    private int _hotGrip = -1;
    // Alt over an object cycles the grips: rotation crosshairs, or points on its box to pick it up by.
    private bool _pointGrips;

    // Groups and components follow the cursor: their drawn nodes (or copies of them) and where those started.
    private readonly List<(Node3D Node, Transform3D Start, bool Ghost)> _following = [];

    // Last copy, for "Nx" and "/N" arrays typed right after it.
    private (List<object> Source, Vec3 Offset)? _lastCopy;

    public override int CommandId => CommandIds.Move;
    public override string CursorImage => _stamp ? "movestamp" : _copy ? "movecopy" : "move";
    protected override Vec3? From => _from;
    public override string VcbLabel => "Distance";
    public override Input.CursorShape Cursor => Input.CursorShape.Move;

    public override string StatusText => (_from, _copy) switch
    {
        _ when _stamp => _from == null ? "Click something to begin stamping it.  Ctrl = Cycle Copy/Stamp/Move." : "Click to stamp a copy, or enter a distance.  Ctrl = Cycle Copy/Stamp/Move.",
        _ when _autofold && !_copy => _from == null ? "Click an item to auto-fold." : "Click to place the item you are auto-folding or enter distance.",
        (null, false) => "Click something to begin moving it.  Ctrl = Cycle Copy/Stamp/Move.  Alt = Toggle Autofold.",
        (null, true) => "Click something to begin copying it.  Ctrl = Cycle Copy/Stamp/Move.",
        (_, false) => "Click to place the items you're moving or enter a distance.  Alt = Toggle Autofold.",
        _ => "Click to place the items you're copying or enter a distance.",
    };

    public override string VcbValue => _from is { } f && Current is { } c ? UI.Measure.Show(f.DistanceTo(c.Point)) : "";

    protected override void OnInferenceChanged()
    {
        View.ShowVcbValue(VcbValue);
        if (_from is not { } f || Current is not { } c || View.Document is not { } doc)
            return;
        var offset = Slide(c.Point - f);
        // Cutting components carry their opening along: the wall is redrawn (which renews the drawn nodes).
        var cutters = _copy ? [] : _items.OfType<ComponentInstance>().Where(i => i is { GluedTo: not null, Definition.CutsOpening: true }).ToList();
        if (cutters.Count > 0)
        {
            var local = Transform.Translation(doc.Context.ToWorld.Inverse().ApplyVector(offset));
            View.PreviewOpenings(doc.Context.Entities, cutters.ToDictionary(i => i, i => i.Transform.Then(local)));
            _previewingOpenings = false; // so Follow's reset leaves this preview in place
            Follow();
            _previewingOpenings = true;
        }
        foreach (var (node, start, _) in _following)
            node.GlobalTransform = start with { Origin = start.Origin + Space.ToGodot(offset) };
    }

    private bool _previewingOpenings;

    /// <summary>Glued components only slide along their face: the offset loses its part along the face's normal.</summary>
    private Vec3 Slide(Vec3 worldOffset)
    {
        if (_items.Count == 0 || View.Document is not { } doc || _items.Any(i => i is not ComponentInstance { GluedTo: not null } g || _items.Contains(g.GluedTo!)))
            return worldOffset;
        var n = doc.Context.ToWorld.ApplyNormal(((ComponentInstance)_items[0]).GluedTo!.Normal).Normalized();
        return _items.Cast<ComponentInstance>().All(i => doc.Context.ToWorld.ApplyNormal(i.GluedTo!.Normal).Normalized().Dot(n) > 0.999)
            ? worldOffset - n * worldOffset.Dot(n)
            : worldOffset;
    }

    /// <summary>Picks up the drawn groups and components being moved, or copies of them when copying.</summary>
    private void Follow()
    {
        Unfollow();
        var keys = _items.OfType<ComponentInstance>().Select(i => (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(i)).ToHashSet();
        if (keys.Count == 0)
            return;
        // The selection's highlight goes along with what it marks.
        if (!_copy)
            _following.Add((View.SelectionRoot, View.SelectionRoot.GlobalTransform, false));
        var found = new List<Node3D>();
        void Walk(Node node)
        {
            foreach (var child in node.GetChildren())
                if (child is Node3D n && n.HasMeta("instance"))
                {
                    if (keys.Contains(n.GetMeta("instance").AsUInt64()))
                        found.Add(n);
                    else
                        Walk(n);
                }
        }
        Walk(View.ModelRoot);
        foreach (var n in found)
        {
            var node = n;
            if (_copy)
            {
                node = (Node3D)n.Duplicate();
                n.GetParent().AddChild(node);
            }
            _following.Add((node, n.GlobalTransform, _copy));
        }
    }

    /// <summary>Puts the drawn nodes back (the model is redrawn after a move anyway).</summary>
    private void Unfollow()
    {
        if (_previewingOpenings && View.Document is { } doc)
        {
            _previewingOpenings = false;
            View.PreviewOpenings(doc.Context.Entities, null);
        }
        foreach (var (node, start, ghost) in _following)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            if (ghost)
                node.QueueFree();
            else
                node.GlobalTransform = start;
        }
        _following.Clear();
    }

    public override void Deactivate() => Unfollow();

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_from == null && _hotGrip >= 0 && _gripsOf is { } gripped)
        {
            var (center, normal) = _grips[_hotGrip];
            if (!_pointGrips)
            {
                // A rotation grip: rotate that object about the box face the grip is on.
                Manager.Activate(new RotateTool([gripped], center, normal));
                return;
            }
            // A point grip: pick the object up by that point of its box.
            _items = [gripped];
            _from = center;
            _lastCopy = null;
            _gripsOf = null;
            Follow();
            OnInferenceChanged();
            RefreshStatus();
            return;
        }
        if (_from == null)
        {
            _items = doc.Selection.IsEmpty ? VertexUnderCursor(doc, inf) ?? ItemUnderCursor(doc, position) : doc.Selection.Items.ToList();
            if (_items.Count == 0)
                return;
            // Components glued to a moving face go with it.
            var faces = _items.OfType<Face>().ToHashSet();
            _items.AddRange(doc.Context.Entities.Instances.Where(i => i.GluedTo is { } g && faces.Contains(g) && !_items.Contains(i)));
            _from = inf.Point;
            _lastCopy = null;
            Follow();
            OnInferenceChanged();
            RefreshStatus();
            return;
        }
        Finish(doc, Slide(inf.Point - _from.Value));
    }

    /// <summary>Rotation crosshairs at the box's face centres, or (point grips) its corners and edge midpoints.</summary>
    private List<(Vec3 Center, Vec3 Normal)> BuildGrips(Bounds3 b, Transform xf)
    {
        var grips = new List<(Vec3, Vec3)>();
        if (b.IsEmpty)
            return grips;
        Vec3 Corner(int i) => new((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z);
        if (_pointGrips)
        {
            for (var i = 0; i < 8; i++)
                grips.Add((xf.ApplyPoint(Corner(i)), Vec3.Zero));
            foreach (var (i, j) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
                grips.Add((xf.ApplyPoint((Corner(i) + Corner(j)) * 0.5), Vec3.Zero));
            return grips;
        }
        var c = b.Center;
        foreach (var (axis, half) in new[] { (Vec3.UnitX, b.Size.X / 2), (Vec3.UnitY, b.Size.Y / 2), (Vec3.UnitZ, b.Size.Z / 2) })
            foreach (var sign in new[] { 1, -1 })
                grips.Add((xf.ApplyPoint(c + axis * (sign * half)), xf.ApplyNormal(axis * sign).Normalized()));
        return grips;
    }

    /// <summary>The grips of the group or component under the cursor, and which one it is on.</summary>
    private void UpdateGrips(Vector2 position)
    {
        _hotGrip = -1;
        if (_from != null || View.Document is not { } doc)
        {
            _gripsOf = null;
            return;
        }
        var hit = View.Pick(position);
        var context = doc.Context.Path;
        var over = hit != null && hit.Path.Count > context.Count && hit.Path.Take(context.Count).SequenceEqual(context) ? hit.Path[context.Count] : null;
        // Staying near the grips of the object last hovered keeps them, so they can be reached from outside it.
        if (over != null && over != _gripsOf)
        {
            _gripsOf = over;
            var b = over.Definition.Entities.Bounds();
            var xf = over.Transform.Then(doc.Context.ToWorld);
            _grips = BuildGrips(b, xf);
        }
        if (_gripsOf == null)
            return;
        for (var i = 0; i < _grips.Count; i++)
            if (View.ToScreen(_grips[i].Center) is { } s && s.DistanceTo(position) < 9)
                _hotGrip = i;
        if (over == null && _hotGrip < 0)
            _gripsOf = null;
    }

    /// <summary>With nothing selected, Move picks up a corner of the geometry being edited by its endpoint.</summary>
    private static List<object>? VertexUnderCursor(Document doc, InferenceResult inf)
    {
        if (inf.Kind != InferenceKind.Endpoint)
            return null;
        var local = doc.Context.ToWorld.Inverse().ApplyPoint(inf.Point);
        var vertex = doc.Context.Entities.Vertices.FirstOrDefault(v => v.Position.DistanceTo(local) <= Tolerance.Length);
        return vertex != null && doc.Context.Entities.Edges.Any(e => e.Start == vertex || e.End == vertex) ? [vertex] : null;
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

    private void Finish(Document doc, Vec3 worldOffset)
    {
        var offset = doc.Context.ToWorld.Inverse().ApplyVector(worldOffset);
        if (offset.IsZero())
        {
            Cancel();
            return;
        }
        Unfollow();
        var items = _items;
        if (_stamp)
        {
            // A copy is left where it lands; the original stays in hand, measured from there for the next stamp.
            doc.Operation("Stamp", e => Transforming.Copy(e, items, Transform.Translation(offset)));
            _from = _from!.Value + worldOffset;
            Follow();
            OnInferenceChanged();
            RefreshStatus();
            return;
        }
        if (_copy)
        {
            List<object> copies = [];
            doc.Operation("Copy", e => copies = Transforming.Copy(e, items, Transform.Translation(offset)));
            doc.Selection.Set(copies.Where(c => c is not Edge || doc.Context.Entities.Edges.Contains(c)));
            _lastCopy = (items, offset);
        }
        else
        {
            // Faces moved so that faces around them would bend need Autofold (Alt), as in SketchUp; corners and edges fold freely.
            if (!_autofold && items.OfType<Face>().Any() && Transforming.WouldBend(doc.Context.Entities, items, Transform.Translation(offset)))
            {
                View.ShowHint("Moving these faces would bend others: press Alt to toggle Autofold.");
                return;
            }
            doc.Operation("Move", e => Transforming.Move(e, items, offset));
        }
        _from = null;
        ResetLocks();
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool StartsVcb(char c) => c is '[' or '<' || _lastCopy != null && c is 'x' or 'X' or '*' or '/';

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc)
            return false;
        var t = text.Trim().ToLowerInvariant();
        // Arrays after a copy: "5x" or "x5" repeats it, "/5" or "5/" divides it.
        if (_lastCopy is { } last && _from == null && (t.EndsWith('x') || t.StartsWith('x') || t.StartsWith('*') || t.EndsWith('*') || t.StartsWith('/') || t.EndsWith('/')))
        {
            if (!int.TryParse(t.Trim('x', '/', '*'), out var n) || n < 2)
                return false;
            var divide = t.Contains('/');
            doc.Undo.Undo(); // replace the single copy by the array, as SketchUp does
            doc.Operation("Copy", e =>
            {
                var step = divide ? last.Offset / n : last.Offset;
                for (var i = 1; i <= n; i++)
                    Transforming.Copy(e, last.Source, Transform.Translation(step * i));
            });
            return true;
        }
        // Typed coordinates: "[x,y,z]" moves the picked point there, "<x,y,z>" moves by that much.
        if (_from is { } picked && TryCoordinates(text, picked, out var target))
        {
            Finish(doc, Slide(target - picked));
            return true;
        }
        if (_from is not { } from || Current is not { } c || !UI.Measure.Read(text, out var mm))
            return false;
        var dir = (c.Point - from).Normalized();
        if (dir.IsZero(1e-12))
            return false;
        Finish(doc, Slide(dir * mm));
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Ctrl && !key.Echo)
        {
            // Move, Copy, Stamp, then Move again.
            (_copy, _stamp) = (_copy, _stamp) switch { (false, false) => (true, false), (true, false) => (true, true), _ => (false, false) };
            if (_from != null)
            {
                Follow();
                OnInferenceChanged();
            }
            RefreshStatus();
            return true;
        }
        if (key.Keycode == Key.Alt && !key.Echo && _from == null && _gripsOf is { } hovered && View.Document is { } d)
        {
            _pointGrips = !_pointGrips;
            _grips = BuildGrips(hovered.Definition.Entities.Bounds(), hovered.Transform.Then(d.Context.ToWorld));
            _hotGrip = -1;
            View.QueueOverlayRedraw();
            return true;
        }
        if (key.Keycode == Key.Alt && !key.Echo)
        {
            _autofold = !_autofold;
            RefreshStatus();
            return true;
        }
        if (key.Keycode == Key.Escape && _from != null)
        {
            Cancel();
            return true;
        }
        return base.KeyDown(key);
    }

    private void Cancel()
    {
        Unfollow();
        _from = null;
        ResetLocks();
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        if (_from is { } f && Current is { } c && View.Document is { } doc)
        {
            DrawWorldLine(overlay, f, c.Point, AxisColor(c.Kind == InferenceKind.OnAxis ? c.AxisDirection : null), 1.5f, dashed: true);
            // Ghost of what moves: its edges at the new position.
            var xf = doc.Context.ToWorld;
            var offset = c.Point - f;
            foreach (var edge in _items.SelectMany(EdgesOf).Distinct())
                DrawWorldLine(overlay, xf.ApplyPoint(edge.Start.Position) + offset, xf.ApplyPoint(edge.End.Position) + offset, new Color(0, 0, 1), 1);
        }
        if (_from == null && _gripsOf != null)
            foreach (var (i, (center, normal)) in _grips.Select((g, i) => (i, g)))
            {
                if (View.ToScreen(center) is not { } at)
                    continue;
                var color = i == _hotGrip ? new Color(1, 0.1f, 0.1f) : new Color(0.85f, 0.2f, 0.2f, 0.85f);
                if (_pointGrips)
                {
                    overlay.DrawCircle(at, i == _hotGrip ? 5 : 3.5f, color);
                    continue;
                }
                // A small cross in the box face's plane, brighter under the cursor.
                var (u, v) = Polygon.PlaneAxes(normal);
                var size = 9 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(center));
                DrawWorldLine(overlay, center - u * size, center + u * size, color, i == _hotGrip ? 3 : 2);
                DrawWorldLine(overlay, center - v * size, center + v * size, color, i == _hotGrip ? 3 : 2);
            }
        if (_hotGrip >= 0 && View.ToScreen(_grips[_hotGrip].Center) is { } tip)
        {
            DrawTooltip(overlay, tip, _pointGrips ? "Move" : "Rotate");
            return;
        }
        DrawInference(overlay);
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        UpdateGrips(position);
    }

    private static IEnumerable<Edge> EdgesOf(object item) => item switch
    {
        Edge e => [e],
        Face f => Topology.EdgesOf(f),
        _ => [],
    };
}
