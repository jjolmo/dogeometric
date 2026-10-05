using Godot;

namespace Dogeometric.App.UI;

/// <summary>A message (an error, a report) whose text can be selected and copied, with a Copy button for all of it.</summary>
public static class MessageDialog
{
    public static AcceptDialog Show(Node parent, string title, string text, Vector2I? size = null)
    {
        var d = new AcceptDialog { Title = title, Theme = LightTheme.Create() };
        var body = new RichTextLabel
        {
            Text = text,
            BbcodeEnabled = false,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(size?.X ?? 460, 0),
        };
        body.AddThemeColorOverride("default_color", new Color(0.1f, 0.1f, 0.1f));
        d.AddChild(body);
        d.AddButton("Copy", right: false, action: "copy");
        d.CustomAction += action =>
        {
            if (action == "copy")
                DisplayServer.ClipboardSet($"{title}\n{text}");
        };
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.PopupCentered();
        return d;
    }
}
