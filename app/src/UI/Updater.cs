using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace Dogeometric.App.UI;

/// <summary>Help › Check for Updates and its toolbar button: asks GitHub for the latest release and, when it is newer
/// than this one, downloads this platform's package, checks its SHA-256 and puts it in place of the running install.</summary>
public static class Updater
{
    private const string LatestUrl = "https://api.github.com/repos/jjolmo/dogeometric/releases/latest";
    private const string ReleasesPage = "https://github.com/jjolmo/dogeometric/releases/latest";
    // Files the running install held open are moved aside under this suffix and deleted on the next start.
    private const string OldSuffix = ".old-update";
    private static readonly HttpClient Http = CreateClient();
    // Read before an update renames the running program (Linux then reports the renamed file as this program).
    private static readonly string Executable = OS.GetExecutablePath();

    public static string CurrentVersion => (string)ProjectSettings.GetSetting("application/config/version");

    private enum Kind { Source, AppImage, LinuxFolder, WindowsFolder, MacApp }

    private sealed record Release(string Tag, Version Version, string Notes, Dictionary<string, string> Assets);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Dogeometric/{CurrentVersion}");
        return http;
    }

    /// <summary>Checks, asks, installs; <paramref name="restart"/> quits (asking to save) and starts the given program.</summary>
    public static async void Check(Node host, Action<string, string[]> restart)
    {
        Diagnostics.Journal.Log("update", $"checking (this is {CurrentVersion})");
        // GitHub can take a while to answer: say something meanwhile.
        var waiting = new AcceptDialog { Title = "Check for Updates", DialogText = "Looking for a newer version…", OkButtonText = "Close", Theme = LightTheme.Create() };
        host.AddChild(waiting);
        waiting.PopupCentered();
        Release release;
        try
        {
            release = await Latest();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            waiting.Hide();
            waiting.QueueFree();
            MessageDialog.Show(host, "Check for Updates", $"Could not reach GitHub to look for a new version:\n{ex.Message}");
            return;
        }
        waiting.Hide();
        waiting.QueueFree();
        if (!Version.TryParse(CurrentVersion, out var current) || release.Version <= current)
        {
            MessageDialog.Show(host, "Check for Updates", $"Dogeometric {CurrentVersion} is the latest version.");
            return;
        }

        var (kind, target) = Install();
        var asset = kind == Kind.Source ? null : release.Assets.Keys.FirstOrDefault(n => n.EndsWith(AssetSuffix(kind), StringComparison.Ordinal));
        var notes = release.Notes.Length > 600 ? release.Notes[..600] + "…" : release.Notes;
        var text = $"Dogeometric {release.Version} is available (you have {CurrentVersion}).\n\n{notes}".TrimEnd();
        if (asset == null)
        {
            var why = kind == Kind.Source ? "This copy runs from the source code: update it with git pull." : "The release has no package for this system.";
            var d = MessageDialog.Show(host, "Check for Updates", $"{text}\n\n{why}");
            d.AddButton("Open Release Page", false, "page");
            d.CustomAction += action =>
            {
                if (action == "page")
                    OS.ShellOpen(ReleasesPage);
            };
            return;
        }

        var ask = new ConfirmationDialog { Title = "Check for Updates", OkButtonText = "Download and Install", CancelButtonText = "Not Now", Theme = LightTheme.Create() };
        ask.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(460, 0) });
        ask.Confirmed += () => Download(host, release, asset, kind, target, restart);
        ask.VisibilityChanged += () =>
        {
            if (!ask.Visible)
                ask.QueueFree();
        };
        host.AddChild(ask);
        ask.PopupCentered();
    }

    /// <summary>Deletes what the last update moved aside (Windows cannot delete a running program's files).</summary>
    public static void CleanUp()
    {
        var (kind, target) = Install();
        try
        {
            if (kind is Kind.LinuxFolder or Kind.WindowsFolder)
                foreach (var old in Directory.EnumerateFiles(target, "*" + OldSuffix, SearchOption.AllDirectories))
                    File.Delete(old);
            else if (kind == Kind.MacApp && Directory.Exists(target + OldSuffix))
                Directory.Delete(target + OldSuffix, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Journal.Log("update", $"could not remove the old files: {ex.Message}");
        }
    }

    private static async Task<Release> Latest()
    {
        using var json = JsonDocument.Parse(await Http.GetStringAsync(LatestUrl, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString()!;
        var assets = root.GetProperty("assets").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("browser_download_url").GetString()!);
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new Release(tag, Version.Parse(tag.TrimStart('v')), notes.Trim(), assets);
    }

    /// <summary>How this copy was installed, and what an update replaces: the AppImage file, the folder holding the
    /// program, or the .app bundle.</summary>
    private static (Kind, string) Install()
    {
        if (!OS.HasFeature("template"))
            return (Kind.Source, "");
        if (System.Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
            return (Kind.AppImage, appImage);
        var exe = Executable;
        if (OS.GetName() == "macOS")
        {
            var dir = Path.GetDirectoryName(exe);
            while (dir != null && !dir.EndsWith(".app", StringComparison.Ordinal))
                dir = Path.GetDirectoryName(dir);
            return dir == null ? (Kind.Source, "") : (Kind.MacApp, dir);
        }
        return (OS.GetName() == "Windows" ? Kind.WindowsFolder : Kind.LinuxFolder, Path.GetDirectoryName(exe)!);
    }

    private static string AssetSuffix(Kind kind) => kind switch
    {
        Kind.AppImage => "-x86_64.AppImage",
        Kind.LinuxFolder => "-linux-x86_64.tar.gz",
        Kind.WindowsFolder => "-windows-x86_64.zip",
        _ => "-macos-universal.zip",
    };

    private static async void Download(Node host, Release release, string asset, Kind kind, string target, Action<string, string[]> restart)
    {
        var progress = new AcceptDialog { Title = "Updating Dogeometric", OkButtonText = "Cancel", Theme = LightTheme.Create() };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        var label = new Label { Text = $"Downloading {asset}…" };
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0 };
        box.AddChild(label);
        box.AddChild(bar);
        progress.AddChild(box);
        using var cancel = new CancellationTokenSource();
        progress.Confirmed += cancel.Cancel;
        progress.Canceled += cancel.Cancel;
        host.AddChild(progress);
        progress.PopupCentered();

        // Staged next to what it replaces, so putting it in place is a rename on the same disk.
        var staging = kind switch
        {
            Kind.AppImage => target + ".download",
            Kind.MacApp => Path.Combine(Path.GetDirectoryName(target)!, ".dogeometric-update"),
            _ => Path.Combine(target, ".update"),
        };
        var package = kind == Kind.AppImage ? staging : Path.Combine(staging, asset);
        string program;
        try
        {
            if (kind != Kind.AppImage)
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
            }
            await Fetch(release.Assets[asset], package, f => bar.Value = f, cancel.Token);
            label.Text = "Checking the download…";
            if (release.Assets.TryGetValue(asset + ".sha256", out var sumUrl))
            {
                var expected = (await Http.GetStringAsync(sumUrl, cancel.Token)).Split(' ', '\t', '\n')[0].Trim().ToLowerInvariant();
                await using var stream = File.OpenRead(package);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel.Token)).ToLowerInvariant();
                if (actual != expected)
                    throw new InvalidDataException("the download does not match the release's checksum");
            }
            label.Text = "Installing…";
            program = await Task.Run(() => Put(kind, target, staging, package), cancel.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or KeyNotFoundException)
        {
            if (GodotObject.IsInstanceValid(progress))
                progress.QueueFree();
            try
            {
                if (kind == Kind.AppImage)
                    File.Delete(staging);
                else if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
            Diagnostics.Journal.Log("update", $"failed: {ex.Message}");
            if (!cancel.IsCancellationRequested)
            {
                var d = MessageDialog.Show(host, "Check for Updates", $"Could not install Dogeometric {release.Version}:\n{ex.Message}\n\nYou can download it from the release page.");
                d.AddButton("Open Release Page", false, "page");
                d.CustomAction += action =>
                {
                    if (action == "page")
                        OS.ShellOpen(ReleasesPage);
                };
            }
            return;
        }

        progress.Hide();
        progress.QueueFree();
        Diagnostics.Journal.Log("update", $"installed {release.Version} in {target}");
        var done = new ConfirmationDialog
        {
            Title = "Check for Updates",
            DialogText = $"Dogeometric {release.Version} is installed. It starts the next time you open Dogeometric.",
            OkButtonText = "Restart Now",
            CancelButtonText = "Later",
            Theme = LightTheme.Create(),
        };
        done.Confirmed += () =>
        {
            if (kind == Kind.MacApp)
                restart("/usr/bin/open", ["-n", target]);
            else
                restart(program, []);
        };
        done.VisibilityChanged += () =>
        {
            if (!done.Visible)
                done.QueueFree();
        };
        host.AddChild(done);
        done.PopupCentered();
    }

    private static async Task Fetch(string url, string path, Action<double> progress, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var from = await response.Content.ReadAsStreamAsync(cancel);
        await using var to = File.Create(path);
        var buffer = new byte[1 << 16];
        long done = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancel)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancel);
            done += read;
            if (total > 0)
                progress((double)done / total);
        }
    }

    /// <summary>Puts the downloaded package in place and returns the program to start.</summary>
    private static string Put(Kind kind, string target, string staging, string package)
    {
        switch (kind)
        {
            case Kind.AppImage:
                File.SetUnixFileMode(staging, File.GetUnixFileMode(target));
                File.Move(staging, target, true);
                return target;

            case Kind.MacApp:
            {
                var unpacked = Path.Combine(staging, "unpacked");
                Directory.CreateDirectory(unpacked);
                // ditto keeps the bundle's symlinks and permissions, which .NET's zip reader drops.
                var output = new Godot.Collections.Array();
                if (OS.Execute("/usr/bin/ditto", ["-x", "-k", package, unpacked], output, true) != 0)
                    throw new IOException("could not unpack the download: " + string.Join("", output));
                var app = Directory.EnumerateDirectories(unpacked, "*.app").FirstOrDefault() ?? throw new InvalidDataException("the download holds no .app");
                Directory.Move(target, target + OldSuffix);
                Directory.Move(app, target);
                Directory.Delete(staging, true);
                return target;
            }

            default:
            {
                var unpacked = Path.Combine(staging, "unpacked");
                Directory.CreateDirectory(unpacked);
                if (kind == Kind.LinuxFolder)
                {
                    using var gz = new GZipStream(File.OpenRead(package), CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gz, unpacked, overwriteFiles: true);
                }
                else
                {
                    ZipFile.ExtractToDirectory(package, unpacked, overwriteFiles: true);
                }
                // The package holds one folder named after its version; its contents replace this install's.
                var dirs = Directory.GetDirectories(unpacked);
                var root = dirs.Length == 1 && Directory.GetFiles(unpacked).Length == 0 ? dirs[0] : unpacked;
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(target, Path.GetRelativePath(root, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (File.Exists(dest))
                        // A running program's files can be renamed but not overwritten on Windows.
                        File.Move(dest, dest + OldSuffix, true);
                    File.Move(file, dest);
                }
                Directory.Delete(staging, true);
                return Path.Combine(target, Path.GetFileName(Executable));
            }
        }
    }
}
