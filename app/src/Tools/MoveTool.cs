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

    // Groups and components follow the cursor: their drawn nodes (or copies of them) and where those started.
    private readonly List<(Node3D Node, Transform3D Start, bool Ghost)> _following = [];

    // Last copy, for "Nx" and "/N" arrays typed right after it.
    private (List<object> Source, Vec3 Offset)? _lastCopy;

    public override int CommandId => CommandIds.Move;
    public override string CursorImage => _copy ? "movecopy" : "move";
    protected override Vec3? From => _from;
    public override string VcbLabel => "Distance";
    public override Input.CursorShape Cursor => Input.CursorShape.Move;

    public override string StatusText => (_from, _copy) switch
    {
        (null, false) => "Click something to begin moving it.",
        (null, true) => "Click something to begin copying it.",
        (_, false) => "Click to place the items you're moving or enter a distance.",
        _ => "Click to place the items you're copying or enter a distance.",
    };

    public override string VcbValue => _from is { } f && Current is { } c ? Length.Format(f.DistanceTo(c.Point), LengthUnit.Millimeters, 1) : "";

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
        if (_copy)
        {
            List<object> copies = [];
            doc.Operation("Copy", e => copies = Transforming.Copy(e, items, Transform.Translation(offset)));
            doc.Selection.Set(copies.Where(c => c is not Edge || doc.Context.Entities.Edges.Contains(c)));
            _lastCopy = (items, offset);
        }
        else
        {
            doc.Operation("Move", e => Transforming.Move(e, items, offset));
        }
        _from = null;
        ResetLocks();
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc)
            return false;
        var t = text.Trim().ToLowerInvariant();
        // Arrays after a copy: "5x" or "x5" repeats it, "/5" or "5/" divides it.
        if (_lastCopy is { } last && _from == null && (t.EndsWith('x') || t.StartsWith('x') || t.StartsWith('/') || t.EndsWith('/')))
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
        if (_from is not { } from || Current is not { } c || !Length.TryParse(text, LengthUnit.Millimeters, out var mm))
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
            _copy = !_copy;
            if (_from != null)
            {
                Follow();
                OnInferenceChanged();
            }
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
        DrawInference(overlay);
    }

    private static IEnumerable<Edge> EdgesOf(object item) => item switch
    {
        Edge e => [e],
        Face f => Topology.EdgesOf(f),
        _ => [],
    };
}
