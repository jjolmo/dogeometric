using System.Globalization;
using Godot;

namespace Dogeometric.App.Diagnostics;

/// <summary>Local flight recorder for Report a Problem: user steps and errors, one timestamped line each. Each session
/// writes its own file as it goes, so a crash still leaves it behind.</summary>
public static partial class Journal
{
    private const int KeptSessions = 20;
    private const int RecentLines = 3000;
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static StreamWriter? _file;
    private static DateTime _start;

    public static string? SessionFile { get; private set; }

    public static string Folder => Path.Combine(OS.GetUserDataDir(), "journal");

    public static void Start()
    {
        if (_file != null)
            return;
        _start = DateTime.Now;
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.GetFiles(Folder, "*.log").Order().SkipLast(KeptSessions - 1))
            File.Delete(old);
        SessionFile = Path.Combine(Folder, $"{_start:yyyyMMdd-HHmmss}.log");
        _file = new StreamWriter(SessionFile, append: true) { AutoFlush = true };
        Log("session", $"Dogeometric {ProjectSettings.GetSetting("application/config/version")} on {OS.GetName()} {OS.GetVersion()}, " +
            $"{RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterApiVersion()}), started {_start:yyyy-MM-dd HH:mm:ss}");
        OS.AddLogger(new EngineLogger());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("crash", e.ExceptionObject.ToString() ?? "");
        TaskScheduler.UnobservedTaskException += (_, e) => Log("exception", e.Exception.ToString());
    }

    /// <summary>One line: seconds since the session started, what kind of event, and what happened.</summary>
    public static void Log(string kind, string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{(DateTime.Now - _start).TotalSeconds,9:0.000}s {kind,-9} {text.Replace('\n', ' ')}");
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > RecentLines)
                Recent.Dequeue();
            _file?.WriteLine(line);
        }
    }

    /// <summary>The session so far (its last lines when it is long).</summary>
    public static string[] Lines()
    {
        lock (Gate)
            return [.. Recent];
    }

    /// <summary>Godot's own errors and warnings, which otherwise only reach a console nobody sees.</summary>
    private sealed partial class EngineLogger : Logger
    {
        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
            Godot.Collections.Array<ScriptBacktrace> scriptBacktraces) =>
            Log(errorType == 1 ? "warning" : "error", $"{(rationale.Length > 0 ? rationale : code)} ({Path.GetFileName(file)}:{line} {function})");

        public override void _LogMessage(string message, bool error)
        {
            if (error)
                Log("stderr", message.TrimEnd());
        }
    }
}
