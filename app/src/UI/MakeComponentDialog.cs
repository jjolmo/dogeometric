using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's Create Component dialog: definition name and description, behaviours, and whether the
/// selection becomes an instance of the new component.</summary>
public partial class MakeComponentDialog : ConfirmationDialog
{
    public sealed record Result(string Name, string Description, GlueTo GlueTo, bool CutsOpening, bool AlwaysFaceCamera, bool ShadowsFaceSun, bool ReplaceSelection);

    private LineEdit _name = null!;
    private TextEdit _description = null!;
    private OptionButton _glue = null!;
    private CheckBox _cut = null!;
    private CheckBox _faceCamera = null!;
    private CheckBox _shadowsFaceSun = null!;
    private CheckBox _replace = null!;

    public static void Show(Node parent, string defaultName, Action<Result> create)
    {
        var dialog = new MakeComponentDialog { Title = "Create Component", OkButtonText = "Create" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };

        var general = new GridContainer { Columns = 2 };
        general.AddChild(new Label { Text = "Definition:" });
        dialog._name = new LineEdit { Text = defaultName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        general.AddChild(dialog._name);
        general.AddChild(new Label { Text = "Description:" });
        dialog._description = new TextEdit { CustomMinimumSize = new Vector2(0, 54), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        general.AddChild(dialog._description);
        box.AddChild(new Label { Text = "General" });
        box.AddChild(general);

        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Alignment" });
        var glueRow = new HBoxContainer();
        glueRow.AddChild(new Label { Text = "Glue to:" });
        dialog._glue = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var g in Enum.GetValues<GlueTo>())
            dialog._glue.AddItem(g.ToString());
        glueRow.AddChild(dialog._glue);
        box.AddChild(glueRow);
        dialog._cut = new CheckBox { Text = "Cut opening", Disabled = true };
        dialog._glue.ItemSelected += i => dialog._cut.Disabled = i == 0;
        box.AddChild(dialog._cut);
        dialog._faceCamera = new CheckBox { Text = "Always face camera" };
        dialog._shadowsFaceSun = new CheckBox { Text = "Shadows face sun", Disabled = true };
        dialog._faceCamera.Toggled += on => dialog._shadowsFaceSun.Disabled = !on;
        box.AddChild(dialog._faceCamera);
        box.AddChild(dialog._shadowsFaceSun);

        box.AddChild(new HSeparator());
        dialog._replace = new CheckBox { Text = "Replace selection with component", ButtonPressed = true };
        box.AddChild(dialog._replace);

        dialog.AddChild(box);
        dialog.RegisterTextEnter(dialog._name); // Enter in the name creates the component
        dialog.Confirmed += () =>
        {
            var name = dialog._name.Text.Trim();
            var glue = (GlueTo)dialog._glue.Selected;
            create(new Result(name.Length > 0 ? name : defaultName, dialog._description.Text, glue, glue != GlueTo.None && dialog._cut.ButtonPressed,
                dialog._faceCamera.ButtonPressed,
                dialog._faceCamera.ButtonPressed && dialog._shadowsFaceSun.ButtonPressed, dialog._replace.ButtonPressed));
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        parent.AddChild(dialog);
        dialog.PopupCentered();
        dialog._name.GrabFocus();
        dialog._name.SelectAll();
    }
}
