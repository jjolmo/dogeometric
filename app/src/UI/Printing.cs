using System.Diagnostics;
using Dogeometric.App.Viewport;
using Dogeometric.Core.IO;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>File › Print Setup, Print Preview and Print: the view becomes a one-page PDF (as drawn, or as a hidden-line
/// drawing) that the system's viewer shows or CUPS prints, fitted to the page.</summary>
public static class Printing
{
    /// <summary>The printers CUPS knows (<c>lpstat -a</c>); empty when there are none or CUPS is missing.</summary>
    public static List<string> Printers()
    {
        var output = Run("lpstat", ["-a"]);
        return output == null ? [] : output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(' ')[0]).ToList();
    }

    /// <summary>Writes the view as a PDF in the temporary folder and returns its path.</summary>
    public static string WritePdf(ModelViewport viewport)
    {
        var (w, h) = ((int)viewport.Size.X, (int)viewport.Size.Y);
        byte[] bytes;
        if (AppPreferences.Current.PrintAsDrawing)
            bytes = HiddenLine.ToPdf(viewport.HiddenLineDrawing(), w, h);
        else
        {
            var shot = viewport.Snapshot();
            bytes = PdfImage.FromJpeg(shot.SaveJpgToBuffer(0.95f), shot.GetWidth(), shot.GetHeight(), w, h);
        }
        var path = Path.Combine(Path.GetTempPath(), $"dogeometric-print-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Opens the page in the system's PDF viewer (which can print it too).</summary>
    public static string Preview(ModelViewport viewport)
    {
        var path = WritePdf(viewport);
        OS.ShellOpen(path);
        return $"Print preview: {Path.GetFileName(path)}";
    }

    /// <summary>Sends the page to the chosen printer; returns what to tell the user.</summary>
    public static string Print(ModelViewport viewport)
    {
        if (Printers().Count == 0)
            return "No printer is set up; File › Print Preview opens the page to save or print it.";
        var path = WritePdf(viewport);
        var printer = AppPreferences.Current.Printer;
        List<string> args = printer.Length > 0 ? ["-d", printer] : [];
        args.AddRange(["-o", "fit-to-page", path]);
        return Run("lp", args) is { } output ? output.Trim() : "Could not print (lp failed).";
    }

    /// <summary>File › Print Setup: printer and what to print.</summary>
    public static void ShowSetup(Node parent)
    {
        var dialog = new ConfirmationDialog { Title = "Print Setup", OkButtonText = "OK" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        var printers = Printers();
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Printer:" });
        var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        choice.AddItem("System default");
        foreach (var p in printers)
            choice.AddItem(p);
        choice.Selected = Math.Max(0, printers.IndexOf(AppPreferences.Current.Printer) + 1);
        row.AddChild(choice);
        box.AddChild(row);
        if (printers.Count == 0)
            box.AddChild(new Label { Text = "No printers found (CUPS).", Modulate = new Color(0.5f, 0.5f, 0.5f) });
        var drawing = new CheckBox { Text = "Print a hidden-line drawing", ButtonPressed = AppPreferences.Current.PrintAsDrawing };
        box.AddChild(drawing);
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            AppPreferences.Current.Printer = choice.Selected > 0 ? printers[choice.Selected - 1] : "";
            AppPreferences.Current.PrintAsDrawing = drawing.ButtonPressed;
            AppPreferences.Save();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }

    private static string? Run(string program, IEnumerable<string> args)
    {
        try
        {
            var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args)
                info.ArgumentList.Add(a);
            using var process = Process.Start(info);
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
