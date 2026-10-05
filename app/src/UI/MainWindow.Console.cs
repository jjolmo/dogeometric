using Dogeometric.Scripting;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Extensions › Developer › Ruby Console: SketchUp's scripting console, here running C# against the open model
/// (Model, Entities, Selection, puts, Pt, AddFace); each line is one undoable step.</summary>
public partial class MainWindow
{
    private Window? _console;

    private void ShowConsole()
    {
        if (_console != null)
        {
            _console.GrabFocus();
            return;
        }
        if (ScriptConsole.AssemblyFolders.Count == 0)
        {
            // Where Godot keeps the game's assemblies: the editor's build output, or the exported game's data folder.
            ScriptConsole.AssemblyFolders.Add(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug"));
            var exe = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            ScriptConsole.AssemblyFolders.AddRange(System.IO.Directory.Exists(exe) ? System.IO.Directory.GetDirectories(exe, "data_*") : []);
        }
        var engine = new ScriptConsole(_document.Document);
        _document.DocumentReplaced += () => engine = new ScriptConsole(_document.Document);
        var window = new Window { Title = "Ruby Console", Size = new Vector2I(640, 360), Theme = LightTheme.Create(), Exclusive = false, Transient = true };
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var log = new TextEdit
        {
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Text = "Dogeometric runs C# here, not Ruby: Model, Entities, Selection, puts(…), Pt(x, y, z), AddFace(…).\n",
        };
        var mono = new SystemFont { FontNames = ["monospace"] };
        log.AddThemeFontOverride("font", mono);
        log.AddThemeColorOverride("font_readonly_color", new Color(0.1f, 0.1f, 0.1f));
        var input = new LineEdit { PlaceholderText = "AddFace(Pt(0, 0), Pt(100, 0), Pt(100, 50), Pt(0, 50))", KeepEditingOnTextSubmit = true };
        input.AddThemeFontOverride("font", mono);
        var history = new List<string>();
        var at = 0;
        input.TextSubmitted += code =>
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            history.Add(code);
            at = history.Count;
            input.Clear();
            var result = engine.Run(code);
            log.Text += "> " + code + "\n" + result.Output;
            log.ScrollVertical = log.GetLineCount();
            _document.RebuildAll();
        };
        input.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } key || history.Count == 0)
                return;
            if (key.Keycode == Key.Up)
                at = Math.Max(0, at - 1);
            else if (key.Keycode == Key.Down)
                at = Math.Min(history.Count, at + 1);
            else
                return;
            input.Text = at < history.Count ? history[at] : "";
            input.CaretColumn = input.Text.Length;
            input.AcceptEvent();
        };
        box.AddChild(log);
        box.AddChild(input);
        window.AddChild(box);
        window.CloseRequested += () =>
        {
            window.QueueFree();
            _console = null;
        };
        AddChild(window);
        window.PopupCentered();
        input.GrabFocus();
        _console = window;
    }
}
