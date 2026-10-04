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
        if (tool.GetType() == Active.GetType())
            return;
        Active.Deactivate();
        // Navigation tools remember the tool they interrupted, so Esc can go back to it.
        if (!Active.IsNavigation)
            _previous = Active;
        tool.Attach(_view, this);
        Active = tool;
        tool.Activate();
        NotifyChanged();
    }

    public void ActivatePrevious()
    {
        if (_previous != null)
            Activate(_previous);
    }

    public void NotifyChanged() => Changed?.Invoke();
}
