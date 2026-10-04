using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Follow Me. With a path already selected (edges, or a face for its perimeter), clicking the profile
/// face sweeps it at once. Otherwise click the profile, then move along the edges it touches: the path grows to the
/// edge under the cursor (Alt: the whole perimeter of the face under the cursor) and a second click sweeps.
/// </summary>
public sealed class FollowMeTool : Tool
{
    private Face? _hover;
    private Face? _profile;
    private List<Edge> _path = [];

    public override int CommandId => CommandIds.FollowMe;
    public override Input.CursorShape Cursor => Input.CursorShape.PointingHand;
    public override string StatusText => "Drag face to extrude  Alt = face perimeter.";

    public override void Activate()
    {
        _profile = null;
        _path = [];
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (View.Document is not { } doc)
            return;
        if (_profile == null)
            _hover = FaceUnderCursor(doc, position);
        else
            _path = PathTo(doc, position);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_profile == null)
        {
            if (FaceUnderCursor(doc, position) is not { } face)
                return;
            if (PreselectedPath(doc, face) is { Count: > 0 } path)
            {
                Sweep(doc, face, path);
                return;
            }
            _profile = face;
            _path = [];
            return;
        }
        if (_path.Count > 0)
            Sweep(doc, _profile, _path);
    }

    private void Sweep(Document doc, Face profile, List<Edge> path)
    {
        doc.Operation("Follow Me", e => FollowMe.Apply(e, profile, path));
        doc.Selection.Clear();
        _profile = null;
        _hover = null;
        _path = [];
        View.QueueOverlayRedraw();
    }

    /// <summary>The selection as a path: its edges, or the outer edges of a selected face (not the profile).</summary>
    private static List<Edge> PreselectedPath(Document doc, Face profile)
    {
        var items = doc.Selection.Items.ToList();
        var edges = items.OfType<Edge>().ToList();
        if (edges.Count > 0)
            return edges;
        if (items.OfType<Face>().FirstOrDefault(f => f != profile) is { } face)
            return face.OuterLoop.Edges.Select(x => x.Edge).ToList();
        return [];
    }

    /// <summary>
    /// Edges from the profile to the edge under the cursor, through connected edges (shortest chain); with Alt,
    /// the perimeter of the face under the cursor.
    /// </summary>
    private List<Edge> PathTo(Document doc, Vector2 position)
    {
        if (View.Pick(position) is not { } hit || !hit.Path.SequenceEqual(doc.Context.Path))
            return _path;
        if (Input.IsKeyPressed(Key.Alt) && hit.Face is { } face && face != _profile)
            return face.OuterLoop.Edges.Select(x => x.Edge).ToList();
        if (hit.Edge is not { } target)
            return _path;

        var e = doc.Context.Entities;
        var profileEdges = Topology.EdgesOf(_profile!).ToHashSet();
        if (profileEdges.Contains(target))
            return [];
        var byVertex = new Dictionary<Vertex, List<Edge>>();
        foreach (var edge in e.Edges.Where(x => !profileEdges.Contains(x)))
        {
            foreach (var v in new[] { edge.Start, edge.End })
            {
                if (!byVertex.TryGetValue(v, out var list))
                    byVertex[v] = list = [];
                list.Add(edge);
            }
        }

        // Breadth-first from the profile's vertices; the chain ends with the hovered edge.
        var cameFrom = new Dictionary<Vertex, Edge?>();
        var queue = new Queue<Vertex>();
        // Start from the profile's vertices and from any vertex touching its outline (a path may start mid-edge).
        foreach (var v in byVertex.Keys.Where(v => TouchesProfile(v.Position)).Concat(_profile!.Loops.SelectMany(l => l.Vertices)).Distinct())
        {
            cameFrom[v] = null;
            queue.Enqueue(v);
        }
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            if (!byVertex.TryGetValue(v, out var list))
                continue;
            foreach (var edge in list)
            {
                if (edge == target)
                {
                    var chain = new List<Edge> { target };
                    for (var at = v; cameFrom[at] is { } back; at = back.Other(at))
                        chain.Add(back);
                    chain.Reverse();
                    return chain;
                }
                var next = edge.Other(v);
                if (cameFrom.ContainsKey(next))
                    continue;
                cameFrom[next] = edge;
                queue.Enqueue(next);
            }
        }
        // Not connected to the profile: the whole chain of edges under the cursor, as SketchUp then follows it.
        var chain2 = new HashSet<Edge> { target };
        var todo = new Stack<Vertex>([target.Start, target.End]);
        while (todo.Count > 0)
        {
            var v = todo.Pop();
            if (!byVertex.TryGetValue(v, out var list) || list.Count > 2)
                continue;
            foreach (var edge in list)
                if (chain2.Add(edge))
                    todo.Push(edge.Other(v));
        }
        return chain2.ToList();
    }

    private bool TouchesProfile(Vec3 p) => Topology.EdgesOf(_profile!).Any(edge =>
    {
        var a = edge.Start.Position;
        var d = edge.End.Position - a;
        var t = Math.Clamp((p - a).Dot(d) / d.LengthSquared, 0, 1);
        return (a + d * t).DistanceTo(p) <= Tolerance.Length;
    });

    /// <summary>A face of the active context under the cursor (Follow Me doesn't reach into groups).</summary>
    private Face? FaceUnderCursor(Document doc, Vector2 position) =>
        View.Pick(position) is { Face: { } f } hit && hit.Path.SequenceEqual(doc.Context.Path) ? f : null;

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _profile != null)
        {
            _profile = null;
            _path = [];
            View.QueueOverlayRedraw();
            return true;
        }
        return false;
    }

    public override void Draw(Control overlay)
    {
        if (View.Document is not { } doc)
            return;
        var toWorld = doc.Context.ToWorld;
        void Line(Vec3 a, Vec3 b, Color c, float w)
        {
            if (View.ToScreen(toWorld.ApplyPoint(a)) is { } sa && View.ToScreen(toWorld.ApplyPoint(b)) is { } sb)
                overlay.DrawLine(sa, sb, c, w, true);
        }
        if ((_profile ?? _hover) is { } face)
            foreach (var edge in Topology.EdgesOf(face))
                Line(edge.Start.Position, edge.End.Position, new Color(0, 0, 1), 2);
        // The path so far, in SketchUp's red.
        foreach (var edge in _path)
            Line(edge.Start.Position, edge.End.Position, new Color(0.9f, 0, 0), 2.5f);
    }
}
