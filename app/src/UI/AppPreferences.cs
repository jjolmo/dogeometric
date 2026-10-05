using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Dogeometric.App.UI;

/// <summary>
/// Window › Preferences, kept in user://preferences.json. SketchUp 2021's defaults, but auto-backup every 3 minutes.
/// </summary>
public sealed class AppPreferences
{
    private const string FilePath = "user://preferences.json";

    // General › Saving.
    /// <summary>Saving over a file first keeps its previous version beside it as name.dogb (SketchUp's .skb).</summary>
    public bool CreateBackup { get; set; } = true;

    /// <summary>Copies of the open model into the backups folder while it has changes; the model's own file is never touched.</summary>
    public bool AutoBackup { get; set; } = true;

    public int AutoBackupMinutes { get; set; } = 3;

    /// <summary>How many automatic backups to keep per model (the oldest go first).</summary>
    public int BackupsToKeep { get; set; } = 20;

    // General › Startup.
    public bool CheckForCrashRecovery { get; set; } = true;

    // Drawing.
    /// <summary>The Line tool keeps drawing from the end of the last line until Esc or a closed face.</summary>
    public bool ContinueLineDrawing { get; set; } = true;

    // Compatibility.
    public bool InvertWheelZoom { get; set; }

    public bool ShowCenterPoints { get; set; }

    // Graphics.
    /// <summary>Multisample anti-aliasing: 0, 2, 4 or 8 samples.</summary>
    public int Antialiasing { get; set; } = 4;

    // Printing (File › Print Setup).
    /// <summary>CUPS printer to print to; empty for the system's default.</summary>
    public string Printer { get; set; } = "";

    /// <summary>Print a hidden-line drawing instead of the view as drawn.</summary>
    public bool PrintAsDrawing { get; set; }

    // SUbD › Preferences.
    /// <summary>Check a mesh is manifold before subdividing it.</summary>
    public bool SubdFixManifolds { get; set; } = true;

    /// <summary>Push/Pull inside a subdivided group is SUbD's Quad Push/Pull.</summary>
    public bool SubdReplacePushPull { get; set; }

    public static AppPreferences Current { get; private set; } = Load();

    /// <summary>Raised after the preferences change, so the parts that use them pick the new values up.</summary>
    public static event Action? Changed;

    private static AppPreferences Load()
    {
        if (!FileAccess.FileExists(FilePath))
            return new AppPreferences();
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
        try
        {
            return JsonSerializer.Deserialize<AppPreferences>(file?.GetAsText() ?? "") ?? new AppPreferences();
        }
        catch (JsonException)
        {
            return new AppPreferences();
        }
    }

    public static void Save()
    {
        using (var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write))
            file?.StoreString(JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }

    /// <summary>Back to the defaults (General › Reset).</summary>
    public static void Reset()
    {
        Current = new AppPreferences();
        Save();
    }
}
