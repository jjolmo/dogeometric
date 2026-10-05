using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>The About, Video and Documentation entries of the extensions Dogeometric rebuilds: credit to their authors
/// and a search for their videos and guides.</summary>
public partial class MainWindow
{
    private void RegisterExtensionInfo()
    {
        void Info(int about, int? video, string name, string search)
        {
            _commands.Register(about, () => Alert($"About {name}",
                $"{name} is a SketchUp extension by Fredo6, published on Sketchucation. Dogeometric rebuilds its tools natively, " +
                "following how the extension behaves in SketchUp 2021; it contains none of the extension's code.\n\n" +
                "Its videos and guides apply to these tools too."));
            if (video is { } v)
                _commands.Register(v, () => OS.ShellOpen("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(search)));
        }
        Info(ExtensionInfoIds.BezierSplineAbout, null, "BezierSpline", "");
        _commands.Register(ExtensionInfoIds.BezierSplineDocumentation, () =>
            OS.ShellOpen("https://duckduckgo.com/?q=" + Uri.EscapeDataString("Fredo6 BezierSpline quickcard tutorial")));
        Info(ExtensionInfoIds.CurviloftAbout, ExtensionInfoIds.CurviloftVideo, "Curviloft", "Fredo6 Curviloft SketchUp");
        Info(ExtensionInfoIds.FredoScaleAbout, ExtensionInfoIds.FredoScaleVideo, "FredoScale", "Fredo6 FredoScale SketchUp");
        Info(ExtensionInfoIds.JointPushPullAbout, ExtensionInfoIds.JointPushPullVideo, "JointPushPull", "Fredo6 JointPushPull SketchUp");
        Info(ExtensionInfoIds.RoundCornerAbout, ExtensionInfoIds.RoundCornerVideo, "Round Corner", "Fredo6 RoundCorner SketchUp");
        Info(ExtensionInfoIds.ToolsOnSurfaceAbout, ExtensionInfoIds.ToolsOnSurfaceVideo, "Tools on Surface", "Fredo6 Tools on Surface SketchUp");
    }
}
