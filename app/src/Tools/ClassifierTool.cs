using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's Classifier: click a group or component to give its definition the chosen IFC type (right-click
/// picks it). Shift makes it unique first, Alt samples a type, Ctrl erases it.</summary>
public sealed class ClassifierTool : Tool
{
    /// <summary>The type the tool applies, kept between uses as SketchUp's drop-down keeps it.</summary>
    public static string Current { get; private set; } = "";

    private bool _over;

    public override int CommandId => CommandIds.Classifier;
    public override string CursorImage => _over ? "" : "select";
    public override Input.CursorShape Cursor => _over ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow;

    public override string StatusText => Current.Length == 0
        ? "Right-click to choose a type to classify with."
        : $"Select component instance or group to classify as {Current}.  Shift = Make Unique and apply Type, Alt = Sample Type, Ctrl = Erase Types.  Right-click = choose type.";

    public override void Activate()
    {
        if (Current.Length == 0)
            ShowTypes(View.Size / 2);
    }

    /// <summary>The group or component under the cursor in the context being edited.</summary>
    private ComponentInstance? Under(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { } hit)
            return null;
        var context = doc.Context.Path;
        if (hit.Path.Count <= context.Count || !hit.Path.Take(context.Count).SequenceEqual(context))
            return null;
        return hit.Path[context.Count] is ComponentInstance { Definition.IsImage: false } i ? i : null;
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        var over = Under(position) != null;
        if (over != _over)
        {
            _over = over;
            RefreshStatus();
        }
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Right)
        {
            ShowTypes(position);
            return;
        }
        if (button != MouseButton.Left || View.Document is not { } doc || Under(position) is not { } inst)
            return;
        if (Input.IsKeyPressed(Key.Alt))
        {
            if (inst.Definition.IfcType.Length > 0)
                Current = inst.Definition.IfcType;
            RefreshStatus();
            return;
        }
        if (Input.IsKeyPressed(Key.Ctrl))
        {
            doc.Operation("Erase Types", _ => inst.Definition.IfcType = "");
            return;
        }
        if (Current.Length == 0)
        {
            ShowTypes(position);
            return;
        }
        var unique = Input.IsKeyPressed(Key.Shift);
        doc.Operation("Classify", _ => Classification.Apply(doc.Model, inst, Current, unique));
    }

    /// <summary>The IFC types by category, as the Classifier toolbar's drop-down lists them.</summary>
    private void ShowTypes(Vector2 at)
    {
        var menu = new PopupMenu();
        var submenus = new List<PopupMenu>();
        var ids = new Dictionary<long, string>();
        foreach (var category in Classification.Types.GroupBy(t => t.Category))
        {
            var sub = new PopupMenu();
            foreach (var type in category.OrderBy(t => t.Name))
            {
                ids[ids.Count] = type.Name;
                sub.AddRadioCheckItem(type.Name, ids.Count - 1);
                sub.SetItemChecked(sub.ItemCount - 1, type.Name == Current);
            }
            sub.IdPressed += Pick;
            submenus.Add(sub);
            menu.AddSubmenuNodeItem(category.Key, sub);
        }
        void Pick(long id)
        {
            Current = ids[id];
            RefreshStatus();
            // Pre-selected groups and components take the type at once.
            if (View.Document is { } doc && doc.Selection.Items.OfType<ComponentInstance>().ToList() is { Count: > 0 } picked)
                doc.Operation("Classify", _ =>
                {
                    foreach (var i in picked)
                        Classification.Apply(doc.Model, i, Current);
                });
        }
        menu.PopupHide += menu.QueueFree;
        View.AddChild(menu);
        menu.Position = (Vector2I)(View.GetScreenPosition() + at);
        menu.Popup();
    }
}
