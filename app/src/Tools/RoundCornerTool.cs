using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>RoundCorner's Round, Sharp and Bevel tools: click edges, faces or vertices to add or remove edges; the result
/// shows live. Measurements box: offset, "6s" segments, or both; Return applies.</summary>
public sealed class RoundCornerTool(RoundCornerMode mode) : Tool
{
    private static readonly Color Picked = new(0.9f, 0, 0);
    private static readonly Color Candidate = new(1, 0.55f, 0);
    private const float VertexPixels = 8;

    // Kept between uses within the session, as the extension does.
    private static double _offset = 1;
    private static int _segments = 6;

    private readonly List<Edge> _edges = [];
    private List<Edge> _hover = [];
    private string _problem = "";

    /// <summary>The context's edges before the live result replaced them (vertex clicks need the original ones).</summary>
    private List<Edge>? _before;

    public override int CommandId => mode switch
    {
        RoundCornerMode.Round => ExtensionIds.RoundCornerRound,
        RoundCornerMode.Sharp => ExtensionIds.RoundCornerSharp,
        _ => ExtensionIds.RoundCornerBevel,
    };

    public override string CursorImage => "select";
    public override string VcbLabel => "Offset";
    public override string VcbValue => Length.Format(_offset, LengthUnit.Millimeters, 2) + (mode == RoundCornerMode.Bevel ? "" : $";{_segments}s");

    public override string StatusText => _problem.Length > 0
        ? _problem
        : $"Click edges, vertices and faces to add/remove edges ({_edges.Count} chosen). Return or click in empty space: apply. Offset and segments (\"6s\") in the Measurements box.";

    private int Segments => mode == RoundCornerMode.Bevel ? 1 : _segments;

    public override void Activate()
    {
        if (View.Document is not { } doc)
            return;
        // A selection made before picking the tool is taken as the edges to work on.
        var context = doc.Context.Entities;
        foreach (var item in doc.Selection.Items)
        {
            if (item is Edge edge && context.Edges.Contains(edge))
                Add(edge);
            else if (item is Face face && context.Faces.Contains(face))
                foreach (var e in Topology.EdgesOf(face))
                    Add(e);
        }
        if (_edges.Count > 0)
        {
            doc.Selection.Clear();
            Preview();
        }
    }

    public override void Deactivate()
    {
        View.Document?.CancelPreview();
        View.QueueOverlayRedraw();
    }

    private void Add(Edge edge)
    {
        if (!_edges.Contains(edge))
            _edges.Add(edge);
    }

    /// <summary>The edges a click at <paramref name="position"/> would add or remove.</summary>
    private List<Edge> Under(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { } hit || !hit.Path.SequenceEqual(doc.Context.Path))
            return [];
        var context = doc.Context.Entities;
        var toWorld = doc.Context.ToWorld;
        switch (hit.Entity)
        {
            case Edge edge:
                foreach (var v in new[] { edge.Start, edge.End })
                    if (View.ToScreen(toWorld.ApplyPoint(v.Position)) is { } s && s.DistanceTo(position) < VertexPixels)
                        return ((doc.Undo.IsPending ? _before : null) ?? context.Edges).Where(x => x.Start == v || x.End == v).ToList();
                return [edge];
            case Face face:
                return Topology.EdgesOf(face).ToList();
            default:
                return [];
        }
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _hover = Under(position);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        var under = Under(position);
        if (under.Count == 0)
        {
            Apply();
            return;
        }
        // A face or vertex whose edges are all chosen removes them; otherwise it adds the missing ones.
        if (under.All(_edges.Contains))
            _edges.RemoveAll(under.Contains);
        else
            foreach (var e in under)
                Add(e);
        Preview();
    }

    private void Preview()
    {
        _problem = "";
        if (View.Document is not { } doc)
            return;
        if (_edges.Count == 0)
        {
            doc.CancelPreview();
            RefreshStatus();
            return;
        }
        var edges = _edges.ToList();
        if (!doc.Undo.IsPending)
            _before = doc.Context.Entities.Edges.ToList();
        try
        {
            doc.Preview(Name, e =>
            {
                var result = RoundCorner.Apply(e, edges, _offset, Segments, mode);
                _problem = result.Problems.Count > 0 ? string.Join(" ", result.Problems.Distinct()) : "";
            });
        }
        catch (Exception ex)
        {
            doc.CancelPreview();
            _problem = $"Could not round these edges: {ex.Message}";
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    private string Name => mode switch
    {
        RoundCornerMode.Round => "Round Corner",
        RoundCornerMode.Sharp => "Sharp Corner",
        _ => "Bevel",
    };

    private void Apply()
    {
        if (View.Document is not { } doc)
            return;
        if (_edges.Count == 0)
        {
            Manager.Activate(new SelectTool());
            return;
        }
        doc.CommitPreview();
        _before = null;
        _edges.Clear();
        _hover = [];
        _problem = "";
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                Apply();
                return true;
            case Key.Escape when _edges.Count > 0:
                _edges.Clear();
                Preview();
                return true;
            case Key.Escape:
                Manager.Activate(new SelectTool());
                return true;
        }
        return false;
    }

    /// <summary>"2mm", "6s" or both ("2mm;6s", "2mm 6s").</summary>
    public override bool ApplyVcb(string text)
    {
        var any = false;
        foreach (var part in text.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith('s') && int.TryParse(part[..^1], out var segments))
            {
                if (segments < 1 || segments > RoundCorner.MaxSegments)
                    return false;
                _segments = segments;
                any = true;
            }
            else if (Length.TryParse(part, LengthUnit.Millimeters, out var mm) && mm > 0)
            {
                _offset = mm;
                any = true;
            }
            else
                return false;
        }
        if (any)
            Preview();
        return any;
    }

    public override void Draw(Control overlay)
    {
        if (View.Document is not { } doc)
            return;
        var toWorld = doc.Context.ToWorld;
        void Line(Edge e, Color color, float width)
        {
            if (View.ToScreen(toWorld.ApplyPoint(e.Start.Position)) is { } a && View.ToScreen(toWorld.ApplyPoint(e.End.Position)) is { } b)
                overlay.DrawLine(a, b, color, width, antialiased: true);
        }
        foreach (var e in _hover.Where(x => !_edges.Contains(x)))
            Line(e, Candidate, 2);
        foreach (var e in _edges)
            Line(e, Picked, 2.5f);
    }
}
