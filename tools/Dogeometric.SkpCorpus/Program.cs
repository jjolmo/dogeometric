// Corpus check for .skp support. For every .skp under a folder:
//   1. import it (OpenSKP → Dogeometric model) and count entities;
//   2. compare the counts with SketchUp's own (oracle.json, produced by tools/skp-oracle/oracle.rb);
//   3. round-trip it: model → .dog → model (must be identical) → legacy .skp → re-import (counts compared).
// The corpus is private (users' models) and never lives in the repo.
//
// Usage: dotnet run --project tools/Dogeometric.SkpCorpus -- <folder> [report.tsv] [roundtrip-output-folder]

using System.Diagnostics;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Formats.Skp;
using System.Text.Json;

var root = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("DOGEOMETRIC_SKP_CORPUS")
    ?? throw new ArgumentException("Pass the corpus folder or set DOGEOMETRIC_SKP_CORPUS");
var reportPath = args.Length > 1 ? args[1] : null;
var outDir = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "dogeometric-roundtrip");
Directory.CreateDirectory(outDir);

var oraclePath = Path.Combine(root, "oracle.json");
var oracle = File.Exists(oraclePath)
    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(oraclePath))!
    : [];

string[] fields = ["definitions", "group_definitions", "instances", "vertices", "edges", "faces", "materials", "layers", "pages"];
var rows = new List<string> { "file\tversion\tstage\tms\t" + string.Join('\t', fields) + "\toracle_diff\tdog_diff\tskp_diff\terror" };
int failures = 0, oracleMismatches = 0, dogMismatches = 0, skpMismatches = 0;

foreach (var file in Directory.EnumerateFiles(root, "*.skp", SearchOption.AllDirectories).OrderBy(f => f))
{
    var rel = Path.GetRelativePath(root, file);
    var sw = Stopwatch.StartNew();
    var stage = "import";
    try
    {
        var model = SkpImporter.Import(file);
        var counts = Count(model);
        var importMs = sw.ElapsedMilliseconds;

        var oracleDiff = oracle.TryGetValue(rel, out var o) && !o.ContainsKey("error")
            ? Diff(fields.Where(f => o.ContainsKey(f)).ToDictionary(f => f, f => o[f].GetInt64()), counts, skip: ["layers"])
            : "no-oracle";

        stage = "dog";
        var dogPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".dog");
        DogFile.Save(model, dogPath);
        var reloaded = DogFile.Load(dogPath);
        var dogDiff = Diff(counts, Count(reloaded));

        stage = "skp-write";
        var skpPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".roundtrip.skp");
        var warnings = SkpExporter.Export(reloaded, skpPath);
        stage = "skp-reimport";
        var again = SkpImporter.Import(skpPath);
        var skpDiff = Diff(counts, Count(again), skip: ["pages", "layers", "materials"]);

        if (oracleDiff is not ("" or "no-oracle")) oracleMismatches++;
        if (dogDiff != "") dogMismatches++;
        if (skpDiff != "") skpMismatches++;

        rows.Add(string.Join('\t', new[] { rel, model.SourceVersion, "ok", importMs.ToString() }
            .Concat(fields.Select(f => counts[f].ToString()))
            .Concat([oracleDiff, dogDiff, skpDiff, string.Join("; ", warnings.Take(3))])));
        Console.WriteLine($"ok   {importMs,6} ms  {Short(rel),-52} sketchup:{Show(oracleDiff)}  dog:{Show(dogDiff)}  skp:{Show(skpDiff)}");
    }
    catch (Exception ex)
    {
        failures++;
        var msg = $"{ex.GetType().Name}: {ex.Message}".Replace('\t', ' ').Replace('\n', ' ');
        rows.Add(string.Join('\t', new[] { rel, "?", "FAIL:" + stage, sw.ElapsedMilliseconds.ToString() }
            .Concat(fields.Select(_ => "")).Concat(["", "", "", msg])));
        Console.WriteLine($"FAIL {sw.ElapsedMilliseconds,6} ms  {Short(rel),-52} at {stage}: {msg[..Math.Min(msg.Length, 160)]}");
    }
}

if (reportPath != null)
    File.WriteAllLines(reportPath, rows);
Console.WriteLine($"\n{rows.Count - 1} files: {failures} failed, counts differ from SketchUp in {oracleMismatches}, " +
                  $".dog round-trip differs in {dogMismatches}, .skp round-trip differs in {skpMismatches}");
return failures == 0 ? 0 : 1;

static Dictionary<string, long> Count(Model m)
{
    var all = m.AllEntities.ToList();
    return new Dictionary<string, long>
    {
        ["definitions"] = m.Definitions.Count,
        ["group_definitions"] = m.Definitions.Count(d => d.IsGroup),
        ["instances"] = all.Sum(e => e.Instances.Count),
        ["vertices"] = all.Sum(e => e.Edges.SelectMany(x => new[] { x.Start, x.End }).Distinct().Count()),
        ["edges"] = all.Sum(e => e.Edges.Count),
        ["faces"] = all.Sum(e => e.Faces.Count),
        ["materials"] = m.Materials.Count,
        ["layers"] = m.Tags.Count,
        ["pages"] = m.Scenes.Count,
    };
}

static string Diff(Dictionary<string, long> expected, Dictionary<string, long> actual, string[]? skip = null)
{
    var parts = expected.Where(kv => skip?.Contains(kv.Key) != true && actual.TryGetValue(kv.Key, out var a) && a != kv.Value)
        .Select(kv => $"{kv.Key} {kv.Value}→{actual[kv.Key]}");
    return string.Join(", ", parts);
}

static string Show(string diff) => diff == "" ? "=" : diff;

static string Short(string rel) => rel.Length > 52 ? "…" + rel[^51..] : rel;
