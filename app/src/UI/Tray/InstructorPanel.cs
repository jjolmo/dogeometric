using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>SketchUp's Instructor: what the active tool does, what to do next, and its keys.</summary>
public partial class InstructorPanel : VBoxContainer
{
    private CommandRegistry _commands = null!;
    private Label _title = null!;
    private Label _description = null!;
    private Label _next = null!;
    private Label _input = null!;

    public static InstructorPanel Create(CommandRegistry commands)
    {
        var p = new InstructorPanel { _commands = commands };
        Label Text(bool header = false)
        {
            var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
            if (header)
                l.AddThemeFontSizeOverride("font_size", 16);
            p.AddChild(l);
            return l;
        }
        p._title = Text(true);
        p._description = Text();
        p.AddChild(new Label { Text = "Tool Operations", Modulate = new Color(0.4f, 0.4f, 0.4f) });
        p._next = Text();
        p._input = Text();
        return p;
    }

    public void Show(Tool tool)
    {
        var cmd = _commands.Get(tool.CommandId);
        _title.Text = cmd.Label.Length > 0 ? cmd.Label.Replace("...", "") : tool.GetType().Name.Replace("Tool", "");
        _description.Text = cmd.Description;
        _next.Text = tool.StatusText;
        var keys = new List<string>();
        if (tool.VcbLabel.Length > 0)
            keys.Add($"Type a value then Return to set the {tool.VcbLabel.ToLowerInvariant()} (Measurements box).");
        if (tool is DrawingTool)
            keys.Add("Arrow keys lock to an axis; Shift locks the current inference.");
        keys.Add("Esc cancels the operation.");
        _input.Text = string.Join("\n", keys);
    }
}
