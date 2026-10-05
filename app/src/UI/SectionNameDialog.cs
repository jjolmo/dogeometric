using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's "Name Section Plane" prompt shown when a section plane is placed: name, symbol, don't ask again.</summary>
public static class SectionNameDialog
{
    public static void Show(Node parent, string name, string symbol, Action<string, string> place)
    {
        var dialog = new ConfirmationDialog { Title = "Name Section Plane", OkButtonText = "Place" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        var grid = new GridContainer { Columns = 2 };
        var nameEdit = new LineEdit { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var symbolEdit = new LineEdit { Text = symbol, MaxLength = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(new Label { Text = "Name" });
        grid.AddChild(nameEdit);
        grid.AddChild(new Label { Text = "Symbol" });
        grid.AddChild(symbolEdit);
        box.AddChild(grid);
        var dontAsk = new CheckBox { Text = "Please don't ask again. Use default names." };
        box.AddChild(dontAsk);
        dialog.AddChild(box);
        // Enter in either field places it, as in SketchUp.
        dialog.RegisterTextEnter(nameEdit);
        dialog.RegisterTextEnter(symbolEdit);
        dialog.Confirmed += () =>
        {
            if (dontAsk.ButtonPressed)
            {
                AppPreferences.Current.AskSectionName = false;
                AppPreferences.Save();
            }
            place(nameEdit.Text, symbolEdit.Text);
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
                dialog.QueueFree();
        };
        parent.AddChild(dialog);
        dialog.PopupCentered();
        nameEdit.GrabFocus();
        nameEdit.SelectAll();
    }
}
