using Dogeometric.App.Viewport;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// An interactive tool, like SketchUp's tools: it receives the viewport's mouse and keyboard input, owns the
/// status bar hint and the Measurements box label, and may draw a 2D overlay.
/// </summary>
public abstract class Tool
{
    protected ModelViewport View { get; private set; } = null!;
    protected ToolManager Manager { get; private set; } = null!;

    /// <summary>SketchUp command id that activates this tool (used to tick menu items).</summary>
    public abstract int CommandId { get; }

    /// <summary>Camera tools return to the previous tool on Esc instead of cancelling.</summary>
    public virtual bool IsNavigation => false;

    public virtual string StatusText => "";
    public virtual string VcbLabel => "";
    public virtual Input.CursorShape Cursor => Input.CursorShape.Arrow;

    internal void Attach(ModelViewport view, ToolManager manager)
    {
        View = view;
        Manager = manager;
    }

    public virtual void Activate() { }
    public virtual void Deactivate() { }

    public virtual void MouseDown(MouseButton button, Vector2 position) { }
    public virtual void MouseMove(Vector2 position, Vector2 relative) { }
    public virtual void MouseUp(MouseButton button, Vector2 position) { }

    /// <summary>Returns true when the key was consumed.</summary>
    public virtual bool KeyDown(InputEventKey key) => false;

    public virtual bool KeyUp(InputEventKey key) => false;

    public virtual void Draw(Control overlay) { }

    protected void RefreshStatus() => Manager.NotifyChanged();
}
