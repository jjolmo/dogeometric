using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's toolbar docking: toolbars sit in docks along the top, bottom, left and right of the drawing area,
/// or float in small windows. Drag a toolbar by its grip: dropped on a dock it joins it where the cursor is
/// (lying down on top and bottom, standing on the sides); dropped anywhere else it floats there. A floating one
/// also docks when its window is dragged by the title onto a dock. View › Toolbars shows and hides them. The
/// layout is kept for the next session.
/// </summary>
public sealed partial class ToolbarDocks : Control
{
    public enum Dock { Top, Bottom, Left, Right, Float }

    // Renamed when the default layout changes, so it shows once instead of an older saved one.
    private const string LayoutPath = "user://toolbars-2.json";
    private const float EdgeZone = 28;

    private sealed class State
    {
        public required Toolbar Bar { get; init; }
        public Dock Dock { get; set; }
        public bool Visible { get; set; } = true;
        public Vector2I FloatPosition { get; set; }
        public Window? Window { get; set; }
        public required Dock DefaultDock { get; init; }
        public required bool DefaultVisible { get; init; }
    }

    private readonly List<State> _states = [];
    private Container _top = null!, _bottom = null!, _left = null!, _right = null!;
    private Control _drawingArea = null!;
    private Panel _indicator = null!;
    private State? _dragging;

    public IEnumerable<Toolbar> Toolbars => _states.Select(s => s.Bar);

    /// <summary>Sets up the docks. The containers are the four dock strips; the drawing area is what they surround.</summary>
    public static ToolbarDocks Create(Container top, Container bottom, Container left, Container right, Control drawingArea)
    {
        var docks = new ToolbarDocks { _top = top, _bottom = bottom, _left = left, _right = right, _drawingArea = drawingArea, MouseFilter = MouseFilterEnum.Ignore };
        docks.SetAnchorsPreset(LayoutPreset.FullRect);
        docks._indicator = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore, TopLevel = true, ZIndex = 100 };
        docks._indicator.AddThemeStyleboxOverride("panel", LightTheme.Box(new Color(0.2f, 0.45f, 0.9f, 0.35f), 0, 0));
        docks.AddChild(docks._indicator);
        return docks;
    }

    public void Add(Toolbar bar, Dock dock, bool visible = true)
    {
        var state = new State { Bar = bar, Dock = dock, Visible = visible, DefaultDock = dock, DefaultVisible = visible };
        _states.Add(state);
        bar.DragStarted += _ => BeginDrag(state);
        Place(state, -1);
    }

    public bool IsVisible(Toolbar bar) => _states.First(s => s.Bar == bar).Visible;

    public void SetVisible(Toolbar bar, bool visible)
    {
        var state = _states.First(s => s.Bar == bar);
        state.Visible = visible;
        Place(state, -1);
        Save();
    }

    /// <summary>Back to SketchUp's default layout.</summary>
    public void Reset()
    {
        foreach (var s in _states)
        {
            s.Dock = s.DefaultDock;
            s.Visible = s.DefaultVisible;
        }
        // Re-add in registration order so each dock gets its default order.
        foreach (var s in _states)
            Place(s, -1);
        Save();
    }

    private Container? ContainerOf(Dock dock) => dock switch
    {
        Dock.Top => _top,
        Dock.Bottom => _bottom,
        Dock.Left => _left,
        Dock.Right => _right,
        _ => null,
    };

    /// <summary>Puts the toolbar in its dock at <paramref name="index"/> (-1: the end), floating or hidden.</summary>
    private void Place(State s, int index)
    {
        var bar = s.Bar;
        bar.GetParent()?.RemoveChild(bar);
        if (s.Window != null && (s.Dock != Dock.Float || !s.Visible))
        {
            s.Window.QueueFree();
            s.Window = null;
        }
        if (!s.Visible)
            return;
        if (ContainerOf(s.Dock) is { } container)
        {
            bar.SetVertical(s.Dock is Dock.Left or Dock.Right);
            container.AddChild(bar);
            if (index >= 0)
                container.MoveChild(bar, Math.Min(index, container.GetChildCount() - 1));
            container.GetParent<Control>().Visible = container.GetChildCount() > 0 || container == _top;
            UpdateDockVisibility();
            return;
        }
        // Floating: a small tool window titled like the toolbar; closing it hides the toolbar.
        bar.SetVertical(false);
        s.Window ??= MakeWindow(s);
        s.Window.AddChild(bar);
        // It keeps its place from the dock row otherwise, and the window grows to reach it.
        bar.Position = Vector2.Zero;
        s.Window.Position = s.FloatPosition;
        // Sized once the toolbar has laid out (its minimum size is only known after a frame).
        var window = s.Window;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(window))
                return;
            // A side dock stretched it to the drawing area's height, with its icons in the middle of that.
            bar.Size = bar.GetCombinedMinimumSize();
            window.Size = (Vector2I)bar.GetCombinedMinimumSize() + new Vector2I(4, 4);
        }).CallDeferred();
        UpdateDockVisibility();
    }

    private Window MakeWindow(State s)
    {
        var window = new Window
        {
            Title = s.Bar.Title,
            Theme = LightTheme.Create(),
            Unresizable = true,
            // Clicking a toolbar must leave the keyboard to the view, or shortcuts stop working.
            Unfocusable = true,
            Transient = true,
            WrapControls = true,
            AlwaysOnTop = false,
        };
        // The window is its own viewport: a drag begun on its grip goes on through its events.
        window.WindowInput += e =>
        {
            if (_dragging != null && e is InputEventMouse mouse)
            {
                DragInput(e, window.Position + mouse.Position);
                window.SetInputAsHandled();
            }
        };
        window.CloseRequested += () =>
        {
            s.Visible = false;
            Place(s, -1);
            Save();
        };
        // Deferred: at start-up (restoring a floating toolbar) the tree is still busy adding the main window.
        Callable.From(() =>
        {
            GetTree().Root.AddChild(window);
            window.Show();
        }).CallDeferred();
        return window;
    }

    /// <summary>Empty side and bottom docks take no room.</summary>
    private void UpdateDockVisibility()
    {
        foreach (var c in new[] { _bottom, _left, _right })
            c.GetParent<Control>().Visible = c.GetChildCount() > 0;
    }

    // ------------------------------------------------------------------ dragging

    private void BeginDrag(State s)
    {
        _dragging = s;
        Input.SetDefaultCursorShape(Input.CursorShape.Move);
    }

    public override void _Input(InputEvent e)
    {
        if (_dragging != null && DragInput(e, GetGlobalMousePosition()))
            GetViewport().SetInputAsHandled();
    }

    /// <summary>A step of a grip drag, at <paramref name="p"/> in the main window; true when it used the event.</summary>
    private bool DragInput(InputEvent e, Vector2 p)
    {
        if (_dragging is not { } s)
            return false;
        switch (e)
        {
            case InputEventMouseMotion:
                ShowIndicator(Target(p));
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                var (dock, index, _) = Target(p);
                _dragging = null;
                _indicator.Visible = false;
                Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                s.Dock = dock;
                // Floating windows are embedded: their position is in the main window's coordinates.
                if (dock == Dock.Float)
                    s.FloatPosition = (Vector2I)p - new Vector2I(10, 10);
                Place(s, index);
                Save();
                return true;
            case InputEventKey { Keycode: Key.Escape, Pressed: true }:
                _dragging = null;
                _indicator.Visible = false;
                Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                return true;
        }
        return false;
    }

    private State? _moving;
    private readonly Dictionary<State, Vector2I> _settled = [];

    /// <summary>A floating toolbar's window dragged by its title: over a dock the strip lights up, and letting go
    /// there docks it, as SketchUp's floating toolbars do.</summary>
    public override void _Process(double delta)
    {
        var pressed = Input.IsMouseButtonPressed(MouseButton.Left);
        if (_moving == null)
        {
            // Where each window rests between presses; one that moves while the button is held is being dragged.
            foreach (var f in _states.Where(f => f.Window != null))
                if (!pressed || !_settled.ContainsKey(f))
                    _settled[f] = f.Window!.Position;
                else if (_dragging == null && f.Window!.Position != _settled[f])
                    _moving = f;
            if (_moving == null)
                return;
        }
        var s = _moving;
        if (s.Window == null)
        {
            _moving = null;
            return;
        }
        var target = Target(GetGlobalMousePosition());
        if (pressed)
        {
            _indicator.Visible = target.Dock != Dock.Float;
            if (_indicator.Visible)
                ShowIndicator(target);
            return;
        }
        _moving = null;
        _indicator.Visible = false;
        _settled.Remove(s);
        if (target.Dock == Dock.Float)
            s.FloatPosition = s.Window.Position;
        else
        {
            s.Dock = target.Dock;
            Place(s, target.Index);
        }
        Save();
    }

    /// <summary>Where a toolbar dropped at <paramref name="p"/> goes, and the strip to highlight.</summary>
    private (Dock Dock, int Index, Rect2 Highlight) Target(Vector2 p)
    {
        var area = _drawingArea.GetGlobalRect();
        var topRect = _top.GetParent<Control>().GetGlobalRect();
        if (topRect.Grow(6).HasPoint(p) || (p.Y >= area.Position.Y && p.Y < area.Position.Y + EdgeZone && p.X > area.Position.X && p.X < area.End.X))
            return (Dock.Top, InsertIndex(_top, p, horizontal: true), topRect);
        Rect2 Strip(Container c, Rect2 fallback) => c.GetParent<Control>() is { Visible: true } panel ? panel.GetGlobalRect() : fallback;
        var leftRect = Strip(_left, new Rect2(area.Position, new Vector2(EdgeZone, area.Size.Y)));
        if (leftRect.Grow(6).HasPoint(p) || (p.X < area.Position.X + EdgeZone && p.Y > area.Position.Y && p.Y < area.End.Y))
            return (Dock.Left, InsertIndex(_left, p, horizontal: true), leftRect);
        var rightRect = Strip(_right, new Rect2(new Vector2(area.End.X - EdgeZone, area.Position.Y), new Vector2(EdgeZone, area.Size.Y)));
        if (rightRect.Grow(6).HasPoint(p) || (p.X > area.End.X - EdgeZone && p.Y > area.Position.Y && p.Y < area.End.Y))
            return (Dock.Right, InsertIndex(_right, p, horizontal: true), rightRect);
        var bottomRect = Strip(_bottom, new Rect2(new Vector2(area.Position.X, area.End.Y - EdgeZone), new Vector2(area.Size.X, EdgeZone)));
        if (bottomRect.Grow(6).HasPoint(p) || (p.Y > area.End.Y - EdgeZone && p.X > area.Position.X && p.X < area.End.X))
            return (Dock.Bottom, InsertIndex(_bottom, p, horizontal: true), bottomRect);
        var size = _dragging?.Bar.GetCombinedMinimumSize() ?? new Vector2(100, 34);
        return (Dock.Float, -1, new Rect2(p - new Vector2(10, 10), _dragging?.Bar.Vertical == true ? new Vector2(size.Y, size.X) : size));
    }

    /// <summary>Position among the dock's toolbars: before the first one whose middle is past the cursor.</summary>
    private int InsertIndex(Container c, Vector2 p, bool horizontal)
    {
        var i = 0;
        foreach (var child in c.GetChildren().OfType<Control>())
        {
            if (child == _dragging?.Bar)
                continue;
            var r = child.GetGlobalRect();
            var before = horizontal ? p.X < r.GetCenter().X && p.Y < r.End.Y + 4 : p.Y < r.GetCenter().Y;
            if (before && (horizontal ? p.Y >= r.Position.Y - 4 : true))
                return i;
            i++;
        }
        return -1;
    }

    private void ShowIndicator((Dock Dock, int Index, Rect2 Highlight) target)
    {
        _indicator.Visible = true;
        _indicator.Position = target.Highlight.Position;
        _indicator.Size = target.Highlight.Size;
    }

    // ------------------------------------------------------------------ persistence

    private sealed record Saved(string Name, string Dock, bool Visible, int X, int Y);

    private void Save()
    {
        var list = new List<Saved>();
        foreach (var dock in new[] { Dock.Top, Dock.Bottom, Dock.Left, Dock.Right })
            foreach (var bar in ContainerOf(dock)!.GetChildren().OfType<Toolbar>())
                list.Add(new Saved(bar.Title, dock.ToString(), true, 0, 0));
        foreach (var s in _states.Where(s => s.Dock == Dock.Float || !s.Visible))
            list.Add(new Saved(s.Bar.Title, s.Dock.ToString(), s.Visible, s.FloatPosition.X, s.FloatPosition.Y));
        using var file = FileAccess.Open(LayoutPath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(list));
    }

    /// <summary>Restores the saved layout (dock, order, visibility, floating position) after all toolbars are added.</summary>
    public void Load()
    {
        if (!FileAccess.FileExists(LayoutPath))
            return;
        using var file = FileAccess.Open(LayoutPath, FileAccess.ModeFlags.Read);
        List<Saved>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<Saved>>(file?.GetAsText() ?? "");
        }
        catch (JsonException)
        {
            return;
        }
        if (list == null)
            return;
        foreach (var saved in list)
        {
            if (_states.FirstOrDefault(s => s.Bar.Title == saved.Name) is not { } s || !Enum.TryParse<Dock>(saved.Dock, out var dock))
                continue;
            s.Dock = dock;
            s.Visible = saved.Visible;
            s.FloatPosition = new Vector2I(saved.X, saved.Y);
            Place(s, -1); // saved in dock order, so appending restores it
        }
    }
}
