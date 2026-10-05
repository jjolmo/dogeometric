using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Scenes panel: the scenes in order, buttons to add, remove, update and reorder them, and the
/// selected one's name, description, whether animations include it and the properties it saves.</summary>
public partial class ScenesPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private SceneTabs _tabs = null!;
    private bool _filling;

    public static ScenesPanel Create(Func<Document> doc, SceneTabs tabs)
    {
        var p = new ScenesPanel { _doc = doc, _tabs = tabs };
        tabs.Changed += () => Callable.From(p.Refresh).CallDeferred();
        p.Refresh();
        return p;
    }

    public void Refresh()
    {
        if (_filling)
            return;
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var buttons = new HBoxContainer();
        Button Action(string text, string tip, Action run)
        {
            var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None };
            b.Pressed += run;
            buttons.AddChild(b);
            return b;
        }
        Action("+", "Add Scene", _tabs.Add);
        Action("−", "Remove Scene", _tabs.DeleteCurrent);
        Action("Update", "Update the selected scene", _tabs.UpdateCurrent);
        Action("▲", "Move Scene Up", () => _tabs.Move(-1));
        Action("▼", "Move Scene Down", () => _tabs.Move(1));
        AddChild(buttons);

        var scenes = _doc().Model.Scenes;
        var list = new ItemList { CustomMinimumSize = new Vector2(0, 110) };
        foreach (var s in scenes)
            list.AddItem(s.Name);
        if (_tabs.CurrentIndex >= 0 && _tabs.CurrentIndex < scenes.Count)
            list.Select(_tabs.CurrentIndex);
        list.ItemSelected += i => _tabs.Go((int)i);
        AddChild(list);
        if (_tabs.CurrentIndex < 0 || _tabs.CurrentIndex >= scenes.Count)
            return;

        var scene = scenes[_tabs.CurrentIndex];
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = scene.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.TextSubmitted += t => Rename(scene, t);
        name.FocusExited += () => Rename(scene, name.Text);
        grid.AddChild(name);
        grid.AddChild(new Label { Text = "Description" });
        var description = new LineEdit { Text = scene.Description, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        description.TextChanged += t => scene.Description = t;
        grid.AddChild(description);
        AddChild(grid);
        var animation = new CheckBox { Text = "Include in animation", ButtonPressed = scene.InAnimation };
        animation.Toggled += on => scene.InAnimation = on;
        AddChild(animation);
        AddChild(new Label { Text = "Properties to save:" });
        foreach (var (label, flag) in new[]
        {
            ("Camera Location", SceneProperties.Camera), ("Visible Tags", SceneProperties.VisibleTags),
            ("Active Section Planes", SceneProperties.ActiveSections), ("Style and Fog", SceneProperties.StyleAndFog),
            ("Shadow Settings", SceneProperties.Shadows), ("Axes Location", SceneProperties.Axes),
        })
        {
            var check = new CheckBox { Text = label, ButtonPressed = scene.Saves.HasFlag(flag), FocusMode = FocusModeEnum.None };
            check.Toggled += on => scene.Saves = on ? scene.Saves | flag : scene.Saves & ~flag;
            AddChild(check);
        }
    }

    private void Rename(Scene scene, string text)
    {
        if (text.Trim() is not { Length: > 0 } t || t == scene.Name)
            return;
        scene.Name = t;
        _filling = true;
        _tabs.Refresh();
        _filling = false;
    }
}
