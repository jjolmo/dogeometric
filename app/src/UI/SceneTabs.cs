using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's scene tabs above the drawing area: one tab per scene; clicking one flies the camera there (the model's
/// transition time) and applies its tag visibility; right-click offers Update, Add and Delete. Play cycles through them.
/// </summary>
public partial class SceneTabs : HBoxContainer
{
    private Func<Document> _doc = null!;
    private ModelViewport _view = null!;
    private Action _tagsChanged = null!;
    private int _current = -1;

    /// <summary>View › Scene Tabs: off hides the tabs even when the model has scenes.</summary>
    public bool Enabled { get; set; } = true;
    private Tween? _tween;

    public static SceneTabs Create(Func<Document> doc, ModelViewport view, Action tagsChanged) =>
        new() { _doc = doc, _view = view, _tagsChanged = tagsChanged };

    /// <summary>Raised when scenes are added, removed, renamed, reordered or another becomes current.</summary>
    public event Action? Changed;

    public int CurrentIndex => _current;

    /// <summary>Scene › Move Left / Move Right.</summary>
    public void Move(int delta)
    {
        var scenes = _doc().Model.Scenes;
        var to = _current + delta;
        if (_current < 0 || to < 0 || to >= scenes.Count)
            return;
        (scenes[_current], scenes[to]) = (scenes[to], scenes[_current]);
        _current = to;
        Refresh();
    }

    public void Refresh()
    {
        foreach (var c in GetChildren().Where(c => c != _player))
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var scenes = _doc().Model.Scenes;
        Visible = Enabled && scenes.Count > 0;
        if (_current >= scenes.Count)
            _current = scenes.Count - 1;
        for (var i = 0; i < scenes.Count; i++)
        {
            var index = i;
            var tab = new Button { Text = scenes[i].Name, ToggleMode = true, ButtonPressed = i == _current, FocusMode = FocusModeEnum.None };
            tab.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb)
                    Menu(index, tab.GetScreenPosition() + mb.Position);
            };
            tab.Pressed += () => Go(index);
            AddChild(tab);
        }
        Changed?.Invoke();
    }

    public void Add()
    {
        var model = _doc().Model;
        var scene = new Scene { Name = $"Scene {model.Scenes.Count + 1}" };
        Capture(model, scene);
        model.Scenes.Insert(_current + 1, scene);
        _current++;
        Refresh();
    }

    public void UpdateCurrent()
    {
        var model = _doc().Model;
        if (_current >= 0 && _current < model.Scenes.Count)
            Capture(model, model.Scenes[_current]);
    }

    public void DeleteCurrent()
    {
        var model = _doc().Model;
        if (_current < 0 || _current >= model.Scenes.Count)
            return;
        model.Scenes.RemoveAt(_current);
        _current = Math.Min(_current, model.Scenes.Count - 1);
        Refresh();
    }

    public void Step(int delta)
    {
        var count = _doc().Model.Scenes.Count;
        if (count > 0)
            Go(((_current + delta) % count + count) % count);
    }

    private void Capture(Model model, Scene scene)
    {
        scene.Camera = _view.Camera.Save();
        scene.HiddenTags.Clear();
        foreach (var t in model.Tags.Where(t => !t.Visible))
            scene.HiddenTags.Add(t.Name);
    }

    public Scene? Current => _current >= 0 && _current < _doc().Model.Scenes.Count ? _doc().Model.Scenes[_current] : null;

    /// <summary>Flies to scene <paramref name="index"/>, interpolating eye, target, up and field of view; a scene with a
    /// matched photo shows it once there.</summary>
    public void Go(int index, bool instant = false)
    {
        var model = _doc().Model;
        if (index < 0 || index >= model.Scenes.Count)
            return;
        _current = index;
        var scene = model.Scenes[index];
        foreach (var t in model.Tags)
            t.Visible = !scene.HiddenTags.Contains(t.Name);
        _tagsChanged();
        Refresh();
        if (scene.Camera is not { } to)
        {
            _view.ShowPhoto(scene.Photo);
            return;
        }
        var from = _view.Camera.Save();
        _view.BeginNavigation();
        if (instant || !model.SceneTransitions || model.SceneTransitionSeconds <= 0)
        {
            _view.ChangeCamera(c => c.Restore(to));
            _view.ShowPhoto(scene.Photo);
            return;
        }
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<double>(t =>
        {
            var s = t * t * (3 - 2 * t); // ease in and out
            Vec3 L(Vec3 a, Vec3 b) => a + (b - a) * s;
            _view.ChangeCamera(c => c.Restore(new CameraState(L(from.Eye, to.Eye), L(from.Target, to.Target),
                L(from.Up, to.Up).Normalized(), to.Perspective, from.FovDegrees + (to.FovDegrees - from.FovDegrees) * s,
                from.OrthoHeight + (to.OrthoHeight - from.OrthoHeight) * s)));
        }), 0.0, 1.0, model.SceneTransitionSeconds);
        if (scene.Photo is { } photo)
            _tween.TweenCallback(Callable.From(() => _view.ShowPhoto(photo)));
    }

    private Godot.Timer? _player;

    public bool Playing => _player != null;

    /// <summary>View › Animation › Play: from scene to scene in a loop, each transition then the scene delay.</summary>
    public void Play()
    {
        var model = _doc().Model;
        if (model.Scenes.Count == 0 || Playing)
            return;
        _player = new Godot.Timer { OneShot = false };
        AddChild(_player);
        void Next()
        {
            var m = _doc().Model;
            if (m.Scenes.Count == 0)
            {
                Stop();
                return;
            }
            if (m.Scenes.All(s => !s.InAnimation))
            {
                Stop();
                return;
            }
            do
                Step(1);
            while (!m.Scenes[_current].InAnimation);
            _player!.WaitTime = Math.Max(0.05, (m.SceneTransitions ? m.SceneTransitionSeconds : 0) + m.SceneDelaySeconds);
            _player.Start();
        }
        _player.Timeout += Next;
        Next();
    }

    public void Stop()
    {
        if (_player != null && IsInstanceValid(_player))
            _player.QueueFree();
        _player = null;
    }

    /// <summary>Asks for the current scene's new name (Scene › Rename).</summary>
    public void RenameCurrent()
    {
        var scenes = _doc().Model.Scenes;
        if (_current < 0 || _current >= scenes.Count)
            return;
        var scene = scenes[_current];
        var d = new ConfirmationDialog { Title = "Rename Scene", Theme = UI.LightTheme.Create() };
        var name = new LineEdit { Text = scene.Name, CustomMinimumSize = new Vector2(260, 0) };
        d.AddChild(name);
        d.RegisterTextEnter(name);
        d.Confirmed += () =>
        {
            if (name.Text.Trim() is { Length: > 0 } t)
            {
                scene.Name = t;
                Refresh();
            }
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
        name.GrabFocus();
        name.SelectAll();
    }

    /// <summary>Scenes… in the tab menu: shows the Scenes panel.</summary>
    public Action ShowPanel { get; set; } = () => { };

    private void Menu(int index, Vector2 at)
    {
        var menu = new PopupMenu();
        var count = _doc().Model.Scenes.Count;
        (string Label, Action Run, bool Enabled)[] items =
        [
            ("Move Left", () => Move(-1), index > 0),
            ("Move Right", () => Move(1), index < count - 1),
            ("Add...", Add, true),
            ("Update", UpdateCurrent, true),
            ("Rename", RenameCurrent, true),
            ("Delete", DeleteCurrent, true),
            ("Play Animation", () =>
            {
                if (Playing)
                    Stop();
                else
                    Play();
            }, true),
            ("Scenes...", ShowPanel, true),
        ];
        for (var i = 0; i < items.Length; i++)
        {
            menu.AddItem(items[i].Label, i);
            menu.SetItemDisabled(i, !items[i].Enabled);
        }
        menu.IdPressed += id =>
        {
            _current = index;
            items[id].Run();
            menu.QueueFree();
        };
        AddChild(menu);
        menu.Position = (Vector2I)at;
        menu.Popup();
    }
}
