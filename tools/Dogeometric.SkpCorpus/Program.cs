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

// oracle.json (one object keyed by relative path) or oracle.jsonl (one object per line with "file").
var oracle = new Dictionary<string, Dictionary<string, JsonElement>>();
var oraclePath = Path.Combine(root, "oracle.json");
if (File.Exists(oraclePath))
    oracle = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(oraclePath))!;
var oracleLines = Path.Combine(root, "oracle.jsonl");
if (File.Exists(oracleLines))
{
    foreach (var line in File.ReadLines(oracleLines).Where(l => l.Trim().Length > 0))
    {
        var entry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!;
        var winPath = entry["file"].GetString()!.Replace('\\', '/');
        var key = Directory.EnumerateFiles(root, "*.skp", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f)).FirstOrDefault(r => winPath.EndsWith(r.Replace('\\', '/'), StringComparison.Ordinal));
        if (key != null)
            oracle[key] = entry;
    }
}

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

        var oracleDiff = "no-oracle";
        if (oracle.TryGetValue(rel, out var o) && !o.ContainsKey("error"))
        {
            // Absent keys are zero: the oracle only writes counts it saw.
            oracleDiff = Diff(fields.ToDictionary(f => f, f => o.TryGetValue(f, out var v) ? v.GetInt64() : 0), counts, skip: ["layers"]);
            if (o.TryGetValue("bounds_mm", out var bj) && bj.GetArrayLength() == 6)
            {
                var b = model.Entities.Bounds();
                var expected = bj.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                double[] actual = [b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z];
                var worst = expected.Zip(actual).Max(p => Math.Abs(p.First - p.Second));
                if (worst > 0.5)
                    oracleDiff = (oracleDiff == "" ? "" : oracleDiff + ", ") + $"bounds off by {worst:0.#}mm";
            }
        }

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

// Counted like SketchUp's API (tools/skp-oracle/oracle.rb): images are neither definitions nor instances.
static Dictionary<string, long> Count(Model m)
{
    var defs = m.Definitions.Where(d => !d.IsImage).ToList();
    var all = defs.Select(d => d.Entities).Prepend(m.Entities).ToList();
    return new Dictionary<string, long>
    {
        ["definitions"] = defs.Count,
        ["group_definitions"] = defs.Count(d => d.IsGroup),
        ["instances"] = all.Sum(e => e.Instances.Count(i => !i.Definition.IsImage)),
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
