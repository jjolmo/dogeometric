using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Eraser: click or drag over edges, groups and components; everything touched is highlighted and
/// erased on release (an edge takes the faces it bounds with it). Shift hides instead, Ctrl softens/smooths
/// edges, Ctrl+Shift unsoftens.
/// </summary>
public sealed class EraserTool : Tool
{
    private readonly HashSet<object> _marked = [];
    private bool _down;

    public override int CommandId => CommandIds.Eraser;
    public override string CursorImage => "eraser";
    public override Input.CursorShape Cursor => Input.CursorShape.Cross;

    public override string StatusText => (Input.IsKeyPressed(Key.Ctrl), Input.IsKeyPressed(Key.Shift)) switch
    {
        (true, true) => "Click or drag to unsmooth/unhide items.",
        (true, false) => "Click or drag to soften/smooth edges.",
        (false, true) => "Click or drag to hide items.",
        _ => "Click or drag to erase items.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _down = true;
        _marked.Clear();
        Mark(position);
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_down)
            Mark(position);
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || !_down)
            return;
        _down = false;
        if (_marked.Count == 0 || View.Document is not { } doc)
            return;
        var items = _marked.ToList();
        _marked.Clear();
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        var shift = Input.IsKeyPressed(Key.Shift);
        if (ctrl || shift)
        {
            doc.Operation(ctrl && !shift ? "Soften Edges" : shift && !ctrl ? "Hide" : "Unsoften", _ =>
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case Edge e when ctrl && !shift:
                            e.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                            break;
                        case Edge e when ctrl && shift:
                            e.Flags &= ~(EdgeFlags.Soft | EdgeFlags.Smooth | EdgeFlags.Hidden);
                            break;
                        case Edge e:
                            e.Flags |= EdgeFlags.Hidden;
                            break;
                        case ComponentInstance i:
                            i.Hidden = !ctrl;
                            break;
                    }
                }
            });
        }
        else
        {
            doc.Selection.Remove(items);
            doc.Operation("Erase", e => Editing.Erase(e, items));
        }
        View.QueueOverlayRedraw();
    }

    private void Mark(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { } hit)
            return;
        var context = doc.Context.Path;
        if (hit.Path.Count < context.Count || !hit.Path.Take(context.Count).SequenceEqual(context))
            return;
        // Inside the context: groups/components as a whole, edges directly; faces are not erased by the Eraser.
        object? item = hit.Path.Count > context.Count ? hit.Path[context.Count] : hit.Entity as Edge;
        if (item != null && _marked.Add(item))
            View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        if (View.Document is not { } doc)
            return;
        var xf = doc.Context.ToWorld;
        var blue = new Color(0, 0, 1);
        foreach (var item in _marked)
        {
            if (item is Edge e && View.ToScreen(xf.ApplyPoint(e.Start.Position)) is { } a && View.ToScreen(xf.ApplyPoint(e.End.Position)) is { } b)
                overlay.DrawLine(a, b, blue, 3);
        }
    }
}
