using System.Text.Json;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// Timed copies of a changed model into the backups folder (never its own file), and a session file whose presence
/// at start-up means the last session crashed.
/// </summary>
public sealed partial class Backups : Node
{
    private const string SessionFile = "session.json";

    private Func<Document> _document = null!;
    private Func<string?> _path = null!;
    private Godot.Timer _timer = null!;
    private Document? _watched;
    private int _backedUpRevision;

    /// <summary>Where backups go: one folder per model inside the user's data folder.</summary>
    public static string Folder => System.IO.Path.Combine(OS.GetUserDataDir(), "backups");

    private static string SessionPath => System.IO.Path.Combine(OS.GetUserDataDir(), SessionFile);

    /// <summary>The latest backup written this session, recorded in the session file.</summary>
    public string? LastBackup { get; private set; }

    public static Backups Create(Func<Document> document, Func<string?> path)
    {
        var b = new Backups { _document = document, _path = path, Name = "Backups" };
        b._timer = new Godot.Timer { OneShot = false };
        b._timer.Timeout += b.Tick;
        b.AddChild(b._timer);
        AppPreferences.Changed += b.Configure;
        b.Configure();
        return b;
    }

    private void Configure()
    {
        var p = AppPreferences.Current;
        _timer.Stop();
        if (!p.AutoBackup)
            return;
        _timer.WaitTime = Math.Max(1, p.AutoBackupMinutes) * 60;
        // Inside the tree only (the timer starts once the node is added).
        if (IsInsideTree())
            _timer.Start();
    }

    public override void _Ready()
    {
        Configure();
        WriteSession();
    }

    /// <summary>A new or opened model counts as backed up as it is.</summary>
    public void DocumentReplaced()
    {
        _watched = _document();
        _backedUpRevision = _watched.Undo.Revision;
        LastBackup = null;
        WriteSession();
    }

    private void Tick()
    {
        var doc = _document();
        if (doc != _watched)
            DocumentReplaced();
        // Nothing changed since the last copy, or an operation is half-way (a live preview).
        if (doc.Undo.Revision == _backedUpRevision || doc.Undo.IsPending)
            return;
        BackUpNow();
    }

    /// <summary>Writes a backup of the current model now; returns its path (null if it failed).</summary>
    public string? BackUpNow()
    {
        var doc = _document();
        var name = ModelName(_path());
        var dir = System.IO.Path.Combine(Folder, name);
        var file = System.IO.Path.Combine(dir, $"{name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.dog");
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            // Written under a temporary name and renamed, so a crash mid-write never leaves a half backup.
            using (var stream = System.IO.File.Create(file + ".part"))
                DogFile.Save(doc.Model, stream);
            System.IO.File.Move(file + ".part", file, overwrite: true);
            _backedUpRevision = doc.Undo.Revision;
            LastBackup = file;
            WriteSession();
            Prune(dir);
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Backup failed: {ex.Message}");
            return null;
        }
    }

    private static void Prune(string dir)
    {
        var keep = Math.Max(1, AppPreferences.Current.BackupsToKeep);
        foreach (var old in new System.IO.DirectoryInfo(dir).GetFiles("*.dog").OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
            old.Delete();
    }

    private static string ModelName(string? path)
    {
        var name = path == null ? "Untitled" : System.IO.Path.GetFileNameWithoutExtension(path);
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length == 0 ? "Untitled" : name;
    }

    // ------------------------------------------------------------------ session / crash recovery

    private sealed record Session(int Pid, string? Model, string? Backup, DateTime Started);

    /// <summary>The model's file changed (Save As, Open): the session file follows.</summary>
    public void UpdateSession() => WriteSession();

    private void WriteSession()
    {
        try
        {
            System.IO.Directory.CreateDirectory(OS.GetUserDataDir());
            System.IO.File.WriteAllText(SessionPath, JsonSerializer.Serialize(new Session(OS.GetProcessId(), _path(), LastBackup, DateTime.Now)));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Session file: {ex.Message}");
        }
    }

    /// <summary>A clean exit: the session file goes, so the next start offers no recovery.</summary>
    public static void EndSession()
    {
        try
        {
            System.IO.File.Delete(SessionPath);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The model and latest backup of a session that did not close properly, or null. Read before this
    /// session writes its own file.</summary>
    public static (string? Model, string Backup)? CrashedSession()
    {
        if (!System.IO.File.Exists(SessionPath))
            return null;
        try
        {
            var s = JsonSerializer.Deserialize<Session>(System.IO.File.ReadAllText(SessionPath));
            if (s == null || s.Pid == OS.GetProcessId())
                return null;
            var backup = s.Backup is { } b && System.IO.File.Exists(b) ? b : null;
            return backup == null ? null : (s.Model, backup);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Every backup, newest first.</summary>
    public static List<System.IO.FileInfo> All()
    {
        if (!System.IO.Directory.Exists(Folder))
            return [];
        return new System.IO.DirectoryInfo(Folder).GetFiles("*.dog", System.IO.SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
    }

    /// <summary>Before saving over <paramref name="path"/>, keeps its current version as name.dogb (SketchUp's .skb).</summary>
    public static void KeepPreviousVersion(string path)
    {
        if (!AppPreferences.Current.CreateBackup || !System.IO.File.Exists(path))
            return;
        try
        {
            System.IO.File.Copy(path, System.IO.Path.ChangeExtension(path, ".dogb"), overwrite: true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Backup of previous version failed: {ex.Message}");
        }
    }
}
