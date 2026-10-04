using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// Select Curve (ThomThom): click an edge to select the run of visible edges it belongs to, through vertices where
/// only two visible edges meet. Ctrl adds, Shift toggles, Ctrl+Shift removes.
/// </summary>
public sealed class SelectCurveTool : Tool
{
    public override int CommandId => ExtensionIds.SelectCurve;
    public override string StatusText => "Pick an edge to select its visible connected edges.";

    public override string CursorImage => (Input.IsKeyPressed(Key.Ctrl), Input.IsKeyPressed(Key.Shift)) switch
    {
        (true, true) => "selectsubtract",
        (true, false) => "selectadd",
        (false, true) => "selectinvert",
        _ => "select",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var context = doc.Context.Entities;
        List<object> curve = [];
        if (View.Pick(position) is { Entity: Edge edge } hit && hit.Path.SequenceEqual(doc.Context.Path))
            curve = Visible(edge) ? Run(context, edge).Cast<object>().ToList() : [edge];
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        var shift = Input.IsKeyPressed(Key.Shift);
        var sel = doc.Selection.Items.ToList();
        if (ctrl && shift)
            doc.Selection.Set(sel.Where(x => !curve.Contains(x)).ToList());
        else if (ctrl)
            doc.Selection.Set(sel.Concat(curve.Where(x => !sel.Contains(x))).ToList());
        else if (shift)
            doc.Selection.Set(sel.Where(x => !curve.Contains(x)).Concat(curve.Where(x => !sel.Contains(x))).ToList());
        else
            doc.Selection.Set(curve);
    }

    private static bool Visible(Edge e) => (e.Flags & (EdgeFlags.Soft | EdgeFlags.Hidden)) == 0;

    /// <summary>The edge and every visible edge chained to it through vertices with exactly two visible edges.</summary>
    public static List<Edge> Run(Entities e, Edge source)
    {
        var at = new Dictionary<Vertex, List<Edge>>();
        foreach (var edge in e.Edges.Where(Visible))
            foreach (var v in new[] { edge.Start, edge.End })
            {
                if (!at.TryGetValue(v, out var list))
                    at[v] = list = [];
                list.Add(edge);
            }
        var curve = new List<Edge>();
        var seen = new HashSet<Edge> { source };
        var queue = new Queue<Edge>([source]);
        while (queue.Count > 0)
        {
            var edge = queue.Dequeue();
            curve.Add(edge);
            foreach (var v in new[] { edge.Start, edge.End })
                if (at.TryGetValue(v, out var list) && list.Count == 2)
                    foreach (var next in list)
                        if (seen.Add(next))
                            queue.Enqueue(next);
        }
        return curve;
    }
}
