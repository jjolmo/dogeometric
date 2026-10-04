using Dogeometric.Core.Modeling;
using Dogeometric.Solids;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// The Solid Tools used as tools, as in SketchUp when nothing is selected: click the first solid group or
/// component, then the second; the operation runs in that order (Subtract keeps second minus first). The cursor
/// shows ① / ② over solids and a "no" sign over anything else.
/// </summary>
public sealed class SolidPickTool(int commandId, string name, Action<List<ComponentInstance>> run) : Tool
{
    private ComponentInstance? _first;
    private ComponentInstance? _hover;

    public override int CommandId => commandId;
    public override string StatusText => _first == null
        ? $"{name}: click the first solid group or component."
        : $"{name}: click the second solid group or component.";

    public override string CursorImage => _hover == null ? "solidnoselect" : _first == null ? "solid1select" : "solid2select";

    public override void MouseMove(Vector2 position, Vector2 relative) => _hover = SolidUnder(position);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || SolidUnder(position) is not { } solid)
            return;
        if (_first == null)
        {
            _first = solid;
            doc.Selection.Set([solid]);
        }
        else if (solid != _first)
        {
            var pair = new List<ComponentInstance> { _first, solid };
            _first = null;
            run(pair);
        }
        RefreshStatus();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode != Key.Escape || _first == null)
            return false;
        _first = null;
        View.Document?.Selection.Clear();
        RefreshStatus();
        return true;
    }

    /// <summary>The group or component of the active context under the cursor, if it is a solid.</summary>
    private ComponentInstance? SolidUnder(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { } hit)
            return null;
        var depth = doc.Context.Path.Count;
        if (hit.Path.Count <= depth || !hit.Path.Take(depth).SequenceEqual(doc.Context.Path))
            return null;
        var inst = hit.Path[depth];
        return SolidTools.IsSolid(inst) ? inst : null;
    }
}
