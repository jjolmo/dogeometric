using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>Softimage dotXSI 3.0 export as SketchUp writes it: millimetres, Z up, a model per group or component holding
/// a mesh whose polygons are listed per material.</summary>
public static class XsiWriter
{
    /// <summary>Writes the model (or the selection) and returns how many meshes went out.</summary>
    public static int Write(Model model, TextWriter w, string path = "", ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var root = options.SelectionContext ?? model.Entities;
        var rootXf = options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity;
        bool Wanted(object e) => options.Selection == null || options.Selection.Contains(e);
        static string N(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
        static string Id(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

        var materials = new List<Material>();
        var names = new Dictionary<Material, string>();
        string MaterialName(Material? m)
        {
            var key = m ?? Default;
            if (!names.TryGetValue(key, out var name))
            {
                name = Id(key.Name);
                while (names.ContainsValue(name))
                    name += "_";
                names[key] = name;
                materials.Add(key);
            }
            return name;
        }

        // A mesh's polygons: each face's outline (faces with holes as triangles), with its material, in world space.
        static List<(List<Vec3> Points, Material? Material)> Polygons(Entities entities, Transform xf, Material? inherited, Func<object, bool> wanted, bool top)
        {
            var result = new List<(List<Vec3>, Material?)>();
            foreach (var face in entities.Faces.Where(f => !f.Hidden && f.Tag is not { Visible: false } && (!top || wanted(f))))
            {
                var outer = face.OuterLoop.Points.Select(xf.ApplyPoint).ToList();
                var holes = face.Loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Points.Select(xf.ApplyPoint).ToList()).ToList();
                if (xf.IsMirroring)
                    outer.Reverse();
                var material = face.FrontMaterial ?? inherited;
                if (holes.Count == 0)
                    result.Add((outer, material));
                else
                {
                    var all = outer.Concat(holes.SelectMany(h => h)).ToList();
                    var tris = Polygon.Triangulate(outer, holes);
                    for (var i = 0; i + 2 < tris.Count; i += 3)
                        result.Add(([all[tris[i]], all[tris[i + 1]], all[tris[i + 2]]], material));
                }
            }
            foreach (var inst in entities.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && !top))
                result.AddRange(Polygons(inst.Definition.Entities, inst.Transform.Then(xf), inst.Material ?? inherited, wanted, false));
            return result;
        }

        var body = new StringBuilder();
        var meshes = 0;
        void Header(string indent, string name)
        {
            body.Append($"{indent}SI_Visibility {{\n{indent}\t1,\n{indent}}}\n\n");
            body.Append($"{indent}SI_Transform SRT-{name} {{\n");
            foreach (var v in new[] { 1, 1, 1, 0, 0, 0, 0, 0, 0 })
                body.Append($"{indent}\t{v},\n");
            body.Append($"{indent}}}\n\n");
        }
        void Mesh(string indent, List<(List<Vec3> Points, Material? Material)> polygons)
        {
            if (polygons.Count == 0)
                return;
            meshes++;
            var name = $"Mesh{meshes}";
            var positions = polygons.SelectMany(p => p.Points).Distinct().ToList();
            var position = positions.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
            var normals = polygons.Select(p => Polygon.Normal(p.Points).Normalized()).Distinct().ToList();
            body.Append($"{indent}SI_Model MDL-{name} {{\n");
            Header(indent + "\t", name);
            body.Append($"{indent}\tSI_Mesh MSH-{name} {{\n{indent}\t\tSI_Shape SHP-{name}-ORG {{\n{indent}\t\t\t2,\n{indent}\t\t\t\"ORDERED\",\n{indent}\t\t\t\n");
            body.Append($"{indent}\t\t\t{positions.Count},\n{indent}\t\t\t\"POSITION\",\n");
            foreach (var p in positions)
                body.Append($"{indent}\t\t\t{N(p.X)},{N(p.Y)},{N(p.Z)},\n");
            body.Append($"{indent}\t\t\t\n{indent}\t\t\t{normals.Count},\n{indent}\t\t\t\"NORMAL\",\n");
            foreach (var n in normals)
                body.Append($"{indent}\t\t\t{N(n.X)},{N(n.Y)},{N(n.Z)},\n");
            body.Append($"{indent}\t\t\t\n{indent}\t\t}}\n\n");
            var list = 0;
            foreach (var group in polygons.GroupBy(p => p.Material))
            {
                var polys = group.ToList();
                body.Append($"{indent}\t\tSI_PolygonList Faces{++list} {{\n{indent}\t\t\t{polys.Count},\n{indent}\t\t\t\"NORMAL\",\n{indent}\t\t\t\"{MaterialName(group.Key)}\",\n");
                body.Append($"{indent}\t\t\t{polys.Sum(p => p.Points.Count)},\n");
                foreach (var p in polys)
                    body.Append($"{indent}\t\t\t{p.Points.Count},\n");
                body.Append($"{indent}\t\t\t\n");
                foreach (var p in polys)
                    body.Append($"{indent}\t\t\t{string.Join(",", p.Points.Select(x => position[x]))},\n\n");
                foreach (var p in polys)
                {
                    var n = normals.IndexOf(Polygon.Normal(p.Points).Normalized());
                    body.Append($"{indent}\t\t\t{string.Join(",", p.Points.Select(_ => n))},\n\n");
                }
                body.Append($"{indent}\t\t}}\n\n");
            }
            body.Append($"{indent}\t}}\n{indent}}}\n\n");
        }

        body.Append("SI_Model MDL-Model {\n");
        Header("\t", "Model");
        foreach (var inst in root.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
        {
            var name = Id(inst.Name.Length > 0 ? inst.Name : inst.Definition.Name);
            body.Append($"\tSI_Model MDL-{name} {{\n");
            Header("\t\t", name);
            Mesh("\t\t", Polygons(inst.Definition.Entities, inst.Transform.Then(rootXf), inst.Material, _ => true, false));
            body.Append("\t}\n\n");
        }
        Mesh("\t", Polygons(root, rootXf, null, Wanted, true));
        body.Append("}\n");

        w.Write("xsi 0300txt 0032\n\nSI_Scene {\n\t\"FRAMES\",\n\t1,\n\t100,\n\t30,\n}\n\n");
        w.Write($"SI_MaterialLibrary {{\n\t{materials.Count},\n");
        foreach (var m in materials)
        {
            w.Write($"\tSI_Material {names[m]} {{\n");
            foreach (var v in new[] { m.Color.R / 255.0, m.Color.G / 255.0, m.Color.B / 255.0, m.Opacity, 32, 0.33, 0.33, 0.33, 0, 0, 0, 2, 0, 0, 0 })
                w.Write($"\t\t{N(v)},\n");
            w.Write("\t}\n\n");
        }
        w.Write("}\n\nSI_CoordinateSystem {\n\t0,\n\t0,\n\t0,\n\t0,\n\t0,\n\t0,\n}\n\nSI_Ambience {\n\t0,\n\t0,\n\t0,\n}\n\nSI_Angle {\n\t0,\n}\n\n");
        w.Write($"SI_FileInfo {{\n\t\"{path}\",\n\t\"\",\n\t\"{DateTime.Now.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)}\",\n\t\"SoftImage dotXSI file, Exported from Dogeometric\",\n}}\n\n");
        w.Write(body.ToString());
        return meshes;
    }

    private static readonly Material Default = new() { Name = "Default", Color = new Rgba(255, 255, 255) };
}
