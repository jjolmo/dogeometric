using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Eraser: click or drag over edges, groups and components; everything touched is highlighted and
/// erased on release (an edge takes the faces it bounds with it). Ctrl softens/smooths edges, Alt unsmooths and
/// unhides, Shift hides, Ctrl+Shift only deselects.
/// </summary>
public sealed class EraserTool : Tool
{
    private readonly HashSet<object> _marked = [];
    private bool _down;

    public override int CommandId => CommandIds.Eraser;
    public override string CursorImage => "eraser";
    public override Input.CursorShape Cursor => Input.CursorShape.Cross;

    private enum Mode { Erase, Soften, Unsoften, Hide, Deselect }

    private static Mode Current => (Input.IsKeyPressed(Key.Ctrl), Input.IsKeyPressed(Key.Shift), Input.IsKeyPressed(Key.Alt)) switch
    {
        (true, true, _) => Mode.Deselect,
        (true, false, _) => Mode.Soften,
        (false, true, _) => Mode.Hide,
        (false, false, true) => Mode.Unsoften,
        _ => Mode.Erase,
    };

    public override string StatusText => Current switch
    {
        Mode.Soften => "Click or drag to soften/smooth edges.",
        Mode.Unsoften => "Click or drag to unsmooth/unhide items.",
        Mode.Hide => "Click or drag to hide items.",
        Mode.Deselect => "Click or drag to deselect items.",
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
        var mode = Current;
        switch (mode)
        {
            case Mode.Erase:
                doc.Selection.Remove(items);
                doc.Operation("Erase", e => Editing.Erase(e, items));
                break;
            case Mode.Deselect:
                doc.Selection.Remove(items);
                break;
            default:
                doc.Operation(mode switch { Mode.Soften => "Soften Edges", Mode.Hide => "Hide", _ => "Unsoften" }, _ =>
                {
                    foreach (var item in items)
                    {
                        switch (item, mode)
                        {
                            case (Edge e, Mode.Soften):
                                e.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                                break;
                            case (Edge e, Mode.Unsoften):
                                e.Flags &= ~(EdgeFlags.Soft | EdgeFlags.Smooth | EdgeFlags.Hidden);
                                break;
                            case (Edge e, Mode.Hide):
                                e.Flags |= EdgeFlags.Hidden;
                                break;
                            case (ComponentInstance i, Mode.Hide):
                                i.Hidden = true;
                                break;
                            case (ComponentInstance i, Mode.Unsoften):
                                i.Hidden = false;
                                break;
                        }
                    }
                });
                break;
        }
        View.QueueOverlayRedraw();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt)
            RefreshStatus();
        return base.KeyDown(key);
    }

    public override bool KeyUp(InputEventKey key)
    {
        if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt)
            RefreshStatus();
        return base.KeyUp(key);
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
