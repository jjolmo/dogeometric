using System.Globalization;
using System.Text;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Test hook for scripted UI runs: DOGEOMETRIC_STATS_FILE gets the model's state whenever it changes, so a
/// driver can check what each tool did.</summary>
public partial class MainWindow
{
    private string? _statsPath;
    private string _lastStats = "";

    public override void _Process(double delta)
    {
        _statsPath ??= OS.GetEnvironment("DOGEOMETRIC_STATS_FILE");
        if (_statsPath.Length == 0 || Engine.GetProcessFrames() % 10 != 0)
            return;
        var stats = Stats(_document.Document.Model);
        if (stats == _lastStats)
            return;
        _lastStats = stats;
        System.IO.File.WriteAllText(_statsPath, stats);
    }

    private string Stats(Model model)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var all = model.AllEntities.ToList();
        var e = model.Entities;
        var sb = new StringBuilder();
        sb.Append($"tool={_viewport.Tools.Active.GetType().Name}\n");
        sb.Append($"top faces={e.Faces.Count} edges={e.Edges.Count} instances={e.Instances.Count} guides={e.GuideLines.Count + e.GuidePoints.Count} dimensions={e.Dimensions.Count} texts={e.Texts.Count} sections={e.SectionPlanes.Count}\n");
        sb.Append($"total faces={all.Sum(x => x.Faces.Count)} edges={all.Sum(x => x.Edges.Count)} definitions={model.Definitions.Count} materials={model.Materials.Count}\n");
        var tris = MeshExtractor.Extract(model);
        var points = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Concat(all.SelectMany(x => x.Edges).SelectMany(ed => new[] { ed.Start.Position, ed.End.Position })).ToList();
        if (points.Count > 0)
            sb.Append($"bounds {N(points.Min(p => p.X))},{N(points.Min(p => p.Y))},{N(points.Min(p => p.Z))} .. {N(points.Max(p => p.X))},{N(points.Max(p => p.Y))},{N(points.Max(p => p.Z))}\n");
        var loose = MeshCheck.Analyze(MeshExtractor.Extract(model, new ExportOptions { Selection = e.Faces.Cast<object>().ToHashSet() }));
        sb.Append($"loose solid={loose.IsWatertight} volume={N(loose.Volume)}\n");
        foreach (var i in e.Instances)
        {
            var c = MeshCheck.Analyze(MeshExtractor.ExtractInstance(i));
            sb.Append($"instance {(i.Definition.IsGroup ? "group" : "component")} '{i.Definition.Name}' faces={i.Definition.Entities.Faces.Count} solid={c.IsWatertight} volume={N(c.Volume)}\n");
        }
        foreach (var f in e.Faces.Where(f => f.FrontMaterial != null || f.BackMaterial != null))
            sb.Append($"painted front={f.FrontMaterial?.Name} back={f.BackMaterial?.Name}\n");
        return sb.ToString();
    }
}
