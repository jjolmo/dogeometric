using Dogeometric.App.Viewport;

namespace Dogeometric.App.Tools;

public sealed class ToolManager
{
    private readonly ModelViewport _view;
    private Tool? _previous;

    public Tool Active { get; private set; }

    /// <summary>Raised when the active tool or its status/VCB text changes.</summary>
    public event Action? Changed;

    public ToolManager(ModelViewport view, Tool initial)
    {
        _view = view;
        initial.Attach(view, this);
        Active = initial;
        initial.Activate();
    }

    public void Activate(Tool tool)
    {
        // Picking the active tool's button again keeps it; tools sharing a class (each spline, surface shape or
        // component placement) are different tools.
        if (tool.GetType() == Active.GetType() && tool.CommandId == Active.CommandId && tool.CommandId != 0)
            return;
        Active.Deactivate();
        // Navigation tools remember the tool they interrupted, so Esc can go back to it.
        if (!Active.IsNavigation)
            _previous = Active;
        tool.Attach(_view, this);
        Active = tool;
        // The Measurements box starts from the new tool's value, not the last one's (activation may set its own).
        _view.ShowVcbValue(tool.VcbValue);
        tool.Activate();
        Diagnostics.Journal.Log("tool", tool.GetType().Name);
        NotifyChanged();
    }

    public void ActivatePrevious()
    {
        if (_previous != null)
            Activate(_previous);
    }

    public void NotifyChanged() => Changed?.Invoke();
}
