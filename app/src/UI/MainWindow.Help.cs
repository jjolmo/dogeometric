using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>The Help menu: links to the project, a system report and SketchUp 2021's command search.</summary>
public partial class MainWindow
{
    private const string Project = "https://github.com/jjolmo/dogeometric";

    private void RegisterHelp()
    {
        _commands.Register(HelpIds.Welcome, ShowWelcome);
        _commands.Register(HelpIds.HelpCenter, () => OS.ShellOpen(Project + "#readme"));
        _commands.Register(HelpIds.ContactUs, () => OS.ShellOpen(Project + "/issues"));
        _commands.Register(HelpIds.CheckForUpdate, () => OS.ShellOpen(Project + "/releases"));
        _commands.Register(HelpIds.CheckYourSystem, () => Alert("Check Your System",
            $"Operating system: {OS.GetName()} {OS.GetVersion()}\n" +
            $"Processor: {OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)\n" +
            $"Graphics: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()})\n" +
            $"Graphics API: {RenderingServer.GetVideoAdapterApiVersion()}\n" +
            $"Engine: Godot {Engine.GetVersionInfo()["string"]}"));
        _commands.Register(HelpIds.Search, ShowCommandSearch);
    }

    /// <summary>Help › Search: type part of a command's name, pick it from the list (or press Return for the first).</summary>
    private void ShowCommandSearch()
    {
        var d = new AcceptDialog { Title = "Search Dogeometric", OkButtonText = "Close", Theme = LightTheme.Create() };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 320) };
        var query = new LineEdit { PlaceholderText = "Search commands" };
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(query);
        box.AddChild(list);
        d.AddChild(box);
        var found = new List<Command>();
        void Filter(string text)
        {
            list.Clear();
            found.Clear();
            var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var cmd in _commands.All.Where(c => c.IsImplemented && c.MenuPath.Length > 0)
                         .Where(c => words.All(w => c.MenuPath.ToLowerInvariant().Contains(w) || c.Description.ToLowerInvariant().Contains(w)))
                         .OrderBy(c => c.MenuPath).Take(60))
            {
                found.Add(cmd);
                list.AddItem($"{cmd.Label}    —  {cmd.MenuPath.Replace("/", " › ")}");
            }
        }
        void Run(long index)
        {
            if (index < 0 || index >= found.Count)
                return;
            d.Hide();
            d.QueueFree();
            _commands.Execute(found[(int)index].Id);
        }
        query.TextChanged += Filter;
        query.TextSubmitted += _ => Run(0);
        list.ItemActivated += Run;
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
        query.GrabFocus();
        Filter("");
    }
}
