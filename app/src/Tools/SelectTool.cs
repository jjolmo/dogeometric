using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Select tool. Click picks the outermost object in the active context; double-click a face selects it
/// with its edges, an edge with its faces, a group/component opens it for editing; triple-click selects everything
/// connected. Dragging left-to-right selects what is fully inside (window), right-to-left what it touches
/// (crossing). Ctrl adds, Shift toggles, Ctrl+Shift removes.
/// </summary>
public sealed class SelectTool : Tool
{
    private const float DragThreshold = 4;
    private const ulong MultiClickMs = 450;

    private Vector2 _down;
    private Vector2 _current;
    private bool _pressed;
    private bool _dragging;
    private int _clicks;
    private ulong _lastClickMs;
    private Vector2 _lastClickPos;

    public override int CommandId => CommandIds.Select;

    public override string StatusText => _dragging
        ? "Drag to select objects. Left-to-right = window (inside), right-to-left = crossing (touching)."
        : "Click or drag to select objects. Shift = Add/Subtract. Ctrl = Add. Shift + Ctrl = Subtract.";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _pressed = true;
        _dragging = false;
        _down = _current = position;
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _current = position;
        if (_pressed && !_dragging && position.DistanceTo(_down) > DragThreshold)
        {
            _dragging = true;
            RefreshStatus();
        }
        if (_dragging)
            View.QueueOverlayRedraw();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || !_pressed)
            return;
        _pressed = false;
        if (View.Document is not { } doc)
            return;

        if (_dragging)
        {
            _dragging = false;
            SelectInRectangle(doc, _down, position);
            View.QueueOverlayRedraw();
            RefreshStatus();
            return;
        }

        var now = Time.GetTicksMsec();
        _clicks = now - _lastClickMs < MultiClickMs && position.DistanceTo(_lastClickPos) < DragThreshold ? _clicks + 1 : 1;
        _lastClickMs = now;
        _lastClickPos = position;
        Click(doc, View.Pick(position));
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode != Key.Escape || View.Document is not { } doc)
            return false;
        // Esc clears the selection; with nothing selected it closes the group being edited.
        if (!doc.Selection.IsEmpty)
            doc.Selection.Clear();
        else
            doc.Context.Exit();
        return true;
    }

    private void Click(Document doc, PickHit? hit)
    {
        var item = hit == null ? null : InContext(doc, hit);

        if (hit != null && item == null)
        {
            // Clicked something outside the group being edited: SketchUp closes the group.
            doc.Context.Reset();
            item = InContext(doc, hit);
        }

        if (item == null)
        {
            if (Modifiers() != SelectMode.Replace)
                return;
            doc.Selection.Clear();
            // Clicking empty space while editing a group closes it, as in SketchUp.
            if (hit == null)
                doc.Context.Reset();
            return;
        }

        if (_clicks == 2 && item is ComponentInstance inst)
        {
            doc.Selection.Clear();
            doc.Context.Enter(inst);
            return;
        }

        IEnumerable<object> items = _clicks switch
        {
            2 => Topology.DoubleClickSet(doc.Context.Entities, item),
            >= 3 => Topology.Connected(doc.Context.Entities, item),
            _ => [item],
        };
        Apply(doc, items.ToList());
    }

    /// <summary>The entity of the active context that contains the hit, or null when the hit is outside it.</summary>
    private static object? InContext(Document doc, PickHit hit)
    {
        var context = doc.Context.Path;
        if (hit.Path.Count < context.Count)
            return null;
        for (var i = 0; i < context.Count; i++)
        {
            if (hit.Path[i] != context[i])
                return null;
        }
        return hit.Path.Count > context.Count ? hit.Path[context.Count] : hit.Entity;
    }

    private void SelectInRectangle(Document doc, Vector2 a, Vector2 b)
    {
        var crossing = b.X < a.X;
        var min = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        var max = (Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var toWorld = doc.Context.ToWorld;
        var items = doc.Picker.InRectangle(doc.Context.Entities,
            p => View.ToScreen(toWorld.ApplyPoint(p)) is { } s ? (s.X, s.Y) : null,
            min, max, crossing).ToList();
        Apply(doc, items);
    }

    private enum SelectMode { Replace, Add, Toggle, Remove }

    private static SelectMode Modifiers()
    {
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        var shift = Input.IsKeyPressed(Key.Shift);
        return (ctrl, shift) switch
        {
            (true, true) => SelectMode.Remove,
            (true, false) => SelectMode.Add,
            (false, true) => SelectMode.Toggle,
            _ => SelectMode.Replace,
        };
    }

    private static void Apply(Document doc, List<object> items)
    {
        switch (Modifiers())
        {
            case SelectMode.Add:
                doc.Selection.Add(items);
                break;
            case SelectMode.Toggle:
                doc.Selection.Toggle(items);
                break;
            case SelectMode.Remove:
                doc.Selection.Remove(items);
                break;
            default:
                doc.Selection.Set(items);
                break;
        }
    }

    public override void Draw(Control overlay)
    {
        if (!_dragging)
            return;
        var rect = new Rect2(_down, _current - _down).Abs();
        var crossing = _current.X < _down.X;
        var color = new Color(0, 0, 0);
        if (!crossing)
        {
            overlay.DrawRect(rect, color, filled: false, width: 1);
            return;
        }
        // Crossing selection: dashed outline.
        var p = new[] { rect.Position, rect.Position + new Vector2(rect.Size.X, 0), rect.End, rect.Position + new Vector2(0, rect.Size.Y) };
        for (var i = 0; i < 4; i++)
            overlay.DrawDashedLine(p[i], p[(i + 1) % 4], color, 1, 4);
    }
}
