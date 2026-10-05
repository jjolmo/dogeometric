using System.Globalization;
using System.Net;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>One group, component or image of a report, with sizes along its own axes in millimetres.</summary>
public sealed record ReportRow(string Path, string Kind, string Definition, string Name, string Tag, string Material,
    double? Volume, double LenX, double LenY, double LenZ);

/// <summary>File › Generate Report: the model's groups and components (nested ones too), as CSV or HTML.</summary>
public static class Reports
{
    public static readonly string[] Columns = ["Path", "Entity Description", "Definition Name", "Entity Name", "Tag", "Material", "Volume (mm³)", "LenX (mm)", "LenY (mm)", "LenZ (mm)"];

    /// <summary>The rows for <paramref name="scope"/> (the whole model when null) and everything inside them.</summary>
    public static List<ReportRow> Rows(Model model, IEnumerable<ComponentInstance>? scope = null)
    {
        var rows = new List<ReportRow>();
        void Walk(IEnumerable<ComponentInstance> instances, string parent)
        {
            foreach (var inst in instances)
            {
                var def = inst.Definition;
                var kind = def.IsImage ? "Image" : inst.IsGroup ? "Group" : "Component";
                var label = inst.Name.Length > 0 ? inst.Name : def.Name.Length > 0 ? def.Name : kind;
                var path = parent.Length > 0 ? $"{parent} > {label}" : label;
                var check = MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def) { Transform = Transform.Identity }));
                var box = def.Entities.Bounds();
                var t = inst.Transform;
                double Len(double size, Vec3 axis) => box.IsEmpty ? 0 : size * axis.Length;
                var scale = Math.Abs(t.Determinant);
                rows.Add(new ReportRow(path, kind, def.Name, inst.Name, inst.Tag?.Name ?? Tag.UntaggedName, inst.Material?.Name ?? "",
                    check.IsWatertight ? Math.Abs(check.Volume) * scale : null,
                    Len(box.Size.X, t.X), Len(box.Size.Y, t.Y), Len(box.Size.Z, t.Z)));
                Walk(def.Entities.Instances, path);
            }
        }
        Walk(scope ?? model.Entities.Instances, "");
        return rows;
    }

    private static string N(double? v) => v is { } x ? x.ToString("0.###", CultureInfo.InvariantCulture) : "";

    private static string[] Cells(ReportRow r) =>
        [r.Path, r.Kind, r.Definition, r.Name, r.Tag, r.Material, N(r.Volume), N(r.LenX), N(r.LenY), N(r.LenZ)];

    public static string ToCsv(IEnumerable<ReportRow> rows)
    {
        static string Quote(string s) => s.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns.Select(Quote)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Cells(r).Select(Quote)));
        return sb.ToString();
    }

    public static string ToHtml(IEnumerable<ReportRow> rows, string title)
    {
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title>");
        sb.Append("<style>body{font-family:sans-serif}table{border-collapse:collapse}th,td{border:1px solid #bbb;padding:3px 8px}th{background:#eee}td.n{text-align:right}</style></head><body>");
        sb.Append($"<h1>{WebUtility.HtmlEncode(title)}</h1><table><tr>");
        foreach (var c in Columns)
            sb.Append($"<th>{WebUtility.HtmlEncode(c)}</th>");
        sb.Append("</tr>");
        foreach (var r in rows)
        {
            sb.Append("<tr>");
            var cells = Cells(r);
            for (var i = 0; i < cells.Length; i++)
                sb.Append(i >= 6 ? $"<td class=\"n\">{cells[i]}</td>" : $"<td>{WebUtility.HtmlEncode(cells[i])}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></body></html>\n");
        return sb.ToString();
    }
}
