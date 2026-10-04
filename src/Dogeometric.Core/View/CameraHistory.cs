namespace Dogeometric.Core.View;

/// <summary>Camera › Previous / Next. Each finished navigation gesture records the view it started from.</summary>
public sealed class CameraHistory
{
    private const int Capacity = 100;

    private readonly List<CameraState> _back = [];
    private readonly List<CameraState> _forward = [];

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Record(CameraState before)
    {
        if (_back.Count > 0 && _back[^1] == before)
            return;
        _back.Add(before);
        if (_back.Count > Capacity)
            _back.RemoveAt(0);
        _forward.Clear();
    }

    public CameraState? Back(CameraState current)
    {
        if (_back.Count == 0)
            return null;
        var state = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
        return state;
    }

    public CameraState? Forward(CameraState current)
    {
        if (_forward.Count == 0)
            return null;
        var state = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
        return state;
    }
}
