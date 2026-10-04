using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// SketchUp edits a text in place: a text box over the view at the text's position. Enter or clicking away
/// confirms, Esc cancels.
/// </summary>
public static class InlineTextEditor
{
    public static void Show(Control view, Vector2 at, string initial, Action<string> done, Action? cancelled = null)
    {
        var edit = new LineEdit { Text = initial, Position = at, CustomMinimumSize = new Vector2(160, 0), SelectAllOnFocus = true };
        var finished = false;
        void Finish(bool accept)
        {
            if (finished)
                return;
            finished = true;
            var text = edit.Text;
            edit.QueueFree();
            if (accept && text.Length > 0)
                done(text);
            else
                cancelled?.Invoke();
        }
        edit.TextSubmitted += _ => Finish(true);
        edit.FocusExited += () => Finish(true);
        edit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                edit.AcceptEvent();
                Finish(false);
            }
        };
        view.AddChild(edit);
        edit.GrabFocus();
        edit.SelectAll();
    }
}
