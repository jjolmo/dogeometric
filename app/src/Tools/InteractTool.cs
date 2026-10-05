using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Tools › Interact (Dynamic Components): clicking a component runs its onClick action (opening a lid,
/// sliding a drawer); the cursor shows a hand over those that have one.</summary>
public sealed class InteractTool : Tool
{
    private bool _over;

    public override int CommandId => ExtensionIds.Interact;
    public override string CursorImage => _over ? "" : "select";
    public override Input.CursorShape Cursor => _over ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow;
    public override string StatusText => "Click on Dynamic Components to activate their onClick behavior.";

    /// <summary>The outermost instance under the cursor that has an onClick action, from the inside out.</summary>
    private ComponentInstance? Under(Vector2 position) =>
        View.Pick(position) is { } hit ? hit.Path.Reverse().FirstOrDefault(DynamicComponents.CanInteract) : null;

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
        if (button != MouseButton.Left || View.Document is not { } doc || Under(position) is not { } inst)
            return;
        var parent = doc.Model.AllEntities.FirstOrDefault(e => e.Instances.Contains(inst)) ?? doc.Model.Entities;
        doc.Undo.Begin("Interact", parent);
        DynamicComponents.Click(inst);
        doc.Undo.Commit();
    }
}
