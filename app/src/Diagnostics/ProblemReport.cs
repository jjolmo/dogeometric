using System.Globalization;
using System.IO.Compression;
using System.Text;
using Dogeometric.App.UI;
using Dogeometric.App.Viewport;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Diagnostics;

/// <summary>Help › Report a Problem (F12): a screenshot with the user's marks and words, what lies under each mark, the
/// app's state, the journal and the model, saved as a folder and a zip under Documents › Dogeometric Reports.</summary>
public static partial class ProblemReport
{
    public static async void Show(Control host, ModelViewport viewport, Document doc, string? modelPath)
    {
        // Two frames let the menu that opened this close, so the picture shows the model, not the menu.
        for (var i = 0; i < 2; i++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var shot = host.GetViewport().GetTexture().GetImage();
        Journal.Log("report", "opened");
        var dialog = new ConfirmationDialog { Title = "Report a Problem", OkButtonText = "Save Report", Theme = LightTheme.Create() };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = "Drag on the picture to mark what looks wrong (right-click a mark to remove it)." });
        var canvas = new MarkCanvas(shot);
        box.AddChild(canvas);
        box.AddChild(new Label { Text = "What were you doing, and what did you expect instead?" });
        var words = new TextEdit { CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary };
        box.AddChild(words);
        var withModel = new CheckBox { Text = "Include the model", ButtonPressed = true };
        box.AddChild(withModel);
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            try
            {
                var folder = Save(shot, canvas.Marks, words.Text.Trim(), withModel.ButtonPressed, viewport, doc, modelPath);
                Journal.Log("report", $"saved to {folder}");
                var done = MessageDialog.Show(host, "Report a Problem", $"Saved to:\n{folder}\n\nand as {Path.GetFileName(folder)}.zip beside it, to send.");
                done.AddButton("Open Folder", false, "open");
                done.CustomAction += action =>
                {
                    if (action == "open")
                        OS.ShellOpen(folder);
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageDialog.Show(host, "Report a Problem", $"Could not save the report:\n{ex.Message}");
            }
        };
        dialog.Canceled += () => Journal.Log("report", "cancelled");
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
                dialog.QueueFree();
        };
        host.AddChild(dialog);
        dialog.PopupCentered();
        words.GrabFocus();
    }

    private static string Save(Image shot, IReadOnlyList<Rect2I> marks, string words, bool withModel, ModelViewport viewport, Document doc, string? modelPath)
    {
        var root = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), "Dogeometric Reports");
        var folder = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        shot.SavePng(Path.Combine(folder, "screenshot.png"));
        var marked = (Image)shot.Duplicate();
        foreach (var m in marks)
            Outline(marked, m, new Color(1, 0, 0));
        marked.SavePng(Path.Combine(folder, "marked.png"));
        if (withModel)
            DogFile.Save(doc.Model, Path.Combine(folder, "model.dog"));
        var steps = Journal.Lines();
        File.WriteAllLines(Path.Combine(folder, "journal.log"), steps);
        var engineLog = ProjectSettings.GlobalizePath((string)ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log"));
        if (File.Exists(engineLog))
            File.Copy(engineLog, Path.Combine(folder, "godot.log"), true);
        File.WriteAllText(Path.Combine(folder, "report.md"), Markdown(marks, words, withModel, viewport, doc, modelPath, shot.GetSize(), steps));
        var zip = folder + ".zip";
        if (File.Exists(zip))
            File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip);
        return folder;
    }

    private static string Markdown(IReadOnlyList<Rect2I> marks, string words, bool withModel, ModelViewport viewport, Document doc,
        string? modelPath, Vector2I size, string[] steps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dogeometric problem report").AppendLine();
        sb.AppendLine($"- When: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Version: {ProjectSettings.GetSetting("application/config/version")} on {OS.GetName()} {OS.GetVersion()}");
        sb.AppendLine($"- Graphics: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterApiVersion()}), window {size.X}×{size.Y}");
        sb.AppendLine($"- Model: {modelPath ?? "(not saved)"}{(withModel ? ", copy in model.dog" : "")}");
        sb.AppendLine().AppendLine("## What went wrong").AppendLine().AppendLine(words.Length > 0 ? words : "(no description)");

        sb.AppendLine().AppendLine("## Marked on the screenshot (window pixels; see marked.png)").AppendLine();
        if (marks.Count == 0)
            sb.AppendLine("(nothing marked)");
        var origin = viewport.GetGlobalRect().Position;
        for (var i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            sb.AppendLine($"{i + 1}. ({m.Position.X}, {m.Position.Y}) {m.Size.X}×{m.Size.Y}:");
            var found = new List<string>();
            for (var gy = 0; gy < 5; gy++)
                for (var gx = 0; gx < 5; gx++)
                {
                    var p = new Vector2(m.Position.X + m.Size.X * (gx + 0.5f) / 5, m.Position.Y + m.Size.Y * (gy + 0.5f) / 5) - origin;
                    var text = Describe.Hit(viewport.Pick(p));
                    // The same entity hit at several grid points is listed once.
                    var at = text.LastIndexOf(" at ", StringComparison.Ordinal);
                    var key = at > 0 ? text[..at] : text;
                    if (!found.Any(f => f.StartsWith(key, StringComparison.Ordinal)))
                        found.Add(text);
                }
            foreach (var f in found.Take(15))
                sb.AppendLine($"   - {f}");
        }

        sb.AppendLine().AppendLine("## Selection").AppendLine();
        var selected = doc.Selection.Items.ToList();
        if (selected.Count == 0)
            sb.AppendLine("(nothing selected)");
        foreach (var item in selected.Take(30))
            sb.AppendLine($"- {Describe.Entity(item, doc.Context.Path)}");
        if (selected.Count > 30)
            sb.AppendLine($"- … {selected.Count - 30} more");

        var cam = viewport.Camera;
        sb.AppendLine().AppendLine("## State").AppendLine();
        sb.AppendLine($"- Tool: {viewport.Tools.Active.GetType().Name} — \"{viewport.Tools.Active.StatusText}\"");
        sb.AppendLine($"- Measurements: {viewport.Tools.Active.VcbLabel} {viewport.Tools.Active.VcbValue}");
        sb.AppendLine($"- Camera: eye {Describe.P(cam.Eye)}, target {Describe.P(cam.Target)}, " +
                      $"{(cam.Perspective ? $"perspective {cam.FovDegrees:0.#}°" : $"parallel, height {cam.OrthoHeight:0.#} mm")}");
        sb.AppendLine($"- Editing: {(doc.Context.Path.Count == 0 ? "the model" : string.Join(" › ", doc.Context.Path.Select(i => i.Definition.Name)))}");
        sb.AppendLine($"- Model: {doc.Model.Entities.Faces.Count} faces, {doc.Model.Entities.Edges.Count} edges, " +
                      $"{doc.Model.Entities.Instances.Count} groups/components at the top, {doc.Model.Definitions.Count} definitions, units {doc.Model.Units}");
        sb.AppendLine($"- Undo: last '{doc.Undo.UndoName ?? "-"}', revision {doc.Undo.Revision}");

        sb.AppendLine().AppendLine("## Last steps (journal.log has the whole session)").AppendLine().AppendLine("```");
        foreach (var line in steps.TakeLast(200))
            sb.AppendLine(line);
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static void Outline(Image image, Rect2I r, Color colour)
    {
        const int width = 3;
        var bounds = new Rect2I(Vector2I.Zero, image.GetSize());
        foreach (var side in new[]
                 {
                     new Rect2I(r.Position, new Vector2I(r.Size.X, width)),
                     new Rect2I(r.Position + new Vector2I(0, r.Size.Y - width), new Vector2I(r.Size.X, width)),
                     new Rect2I(r.Position, new Vector2I(width, r.Size.Y)),
                     new Rect2I(r.Position + new Vector2I(r.Size.X - width, 0), new Vector2I(width, r.Size.Y)),
                 })
            if (side.Intersection(bounds) is var clipped && clipped.HasArea())
                image.FillRect(clipped, colour);
    }

    /// <summary>The screenshot, shrunk to fit, on which the user drags rectangles; marks are kept in window pixels.</summary>
    private sealed partial class MarkCanvas : Control
    {
        private readonly ImageTexture _texture;
        private readonly float _scale;
        private Vector2? _dragFrom;
        private Vector2 _dragTo;

        public List<Rect2I> Marks { get; } = [];

        public MarkCanvas(Image shot)
        {
            _texture = ImageTexture.CreateFromImage(shot);
            _scale = Math.Min(1f, Math.Min(960f / shot.GetWidth(), 560f / shot.GetHeight()));
            CustomMinimumSize = new Vector2(shot.GetWidth(), shot.GetHeight()) * _scale;
            MouseDefaultCursorShape = CursorShape.Cross;
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                    _dragFrom = _dragTo = press.Position;
                    break;
                case InputEventMouseMotion motion when _dragFrom != null:
                    _dragTo = motion.Position;
                    QueueRedraw();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _dragFrom is { } from:
                    var r = new Rect2(from, Vector2.Zero).Expand(_dragTo);
                    if (r.Size.X > 4 && r.Size.Y > 4)
                        Marks.Add(new Rect2I((Vector2I)(r.Position / _scale), (Vector2I)(r.Size / _scale)));
                    _dragFrom = null;
                    QueueRedraw();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click:
                    var hit = Marks.FindLastIndex(m => new Rect2(m.Position, m.Size).HasPoint(click.Position / _scale));
                    if (hit >= 0)
                        Marks.RemoveAt(hit);
                    QueueRedraw();
                    break;
            }
        }

        public override void _Draw()
        {
            DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
            var red = new Color(1, 0.1f, 0.1f);
            for (var i = 0; i < Marks.Count; i++)
            {
                var r = new Rect2((Vector2)Marks[i].Position * _scale, (Vector2)Marks[i].Size * _scale);
                DrawRect(r, red, false, 2);
                DrawString(ThemeDB.FallbackFont, r.Position + new Vector2(4, 16), (i + 1).ToString(), modulate: red);
            }
            if (_dragFrom is { } from)
                DrawRect(new Rect2(from, Vector2.Zero).Expand(_dragTo), red, false, 2);
        }
    }
}
