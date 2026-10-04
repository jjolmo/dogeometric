using System.Globalization;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>Wavefront OBJ + MTL export, in millimetres.</summary>
public static class ObjWriter
{
    /// <param name="swapYz">Write Y-up coordinates (SketchUp's "Swap YZ coordinates" option, on by default).</param>
    public static void Write(IReadOnlyList<Triangle> triangles, string objPath, bool swapYz = true)
    {
        var mtlPath = Path.ChangeExtension(objPath, ".mtl");
        var c = CultureInfo.InvariantCulture;
        var materials = triangles.Select(t => t.Material).Distinct().ToList();
        var names = UniqueNames(materials);

        using (var mtl = new StreamWriter(mtlPath))
        {
            foreach (var m in materials)
            {
                var col = m?.Color ?? Rgba.DefaultFront;
                mtl.WriteLine($"newmtl {names[m ?? NoMaterial]}");
                mtl.WriteLine(string.Create(c, $"Kd {col.R / 255.0:0.######} {col.G / 255.0:0.######} {col.B / 255.0:0.######}"));
                mtl.WriteLine(string.Create(c, $"d {(m?.Opacity ?? 1):0.######}"));
                mtl.WriteLine();
            }
        }

        using var w = new StreamWriter(objPath);
        w.WriteLine("# Exported by Dogeometric (units: millimetres)");
        w.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");

        // Shared vertices keep closed solids closed in tools that weld by index.
        var index = new Dictionary<Vec3, int>();
        var faces = new List<(Material? M, int A, int B, int C)>(triangles.Count);
        foreach (var t in triangles)
            faces.Add((t.Material, Index(t.A), Index(t.B), Index(t.C)));

        foreach (var g in faces.GroupBy(f => f.M ?? NoMaterial))
        {
            w.WriteLine($"usemtl {names[g.Key]}");
            foreach (var f in g)
                w.WriteLine($"f {f.A} {f.B} {f.C}");
        }
        return;

        int Index(Vec3 p)
        {
            if (index.TryGetValue(p, out var i))
                return i;
            i = index.Count + 1;
            index[p] = i;
            w.WriteLine(swapYz
                ? string.Create(c, $"v {p.X:G9} {p.Z:G9} {-p.Y:G9}")
                : string.Create(c, $"v {p.X:G9} {p.Y:G9} {p.Z:G9}"));
            return i;
        }
    }

    private static readonly Material NoMaterial = new() { Name = "Default" };

    private static Dictionary<Material, string> UniqueNames(IEnumerable<Material?> materials)
    {
        var names = new Dictionary<Material, string>();
        var used = new HashSet<string>();
        foreach (var m in materials.Select(m => m ?? NoMaterial))
        {
            var baseName = new string(m.Name.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
            if (baseName.Length == 0)
                baseName = "material";
            var name = baseName;
            for (var i = 2; !used.Add(name); i++)
                name = $"{baseName}_{i}";
            names[m] = name;
        }
        return names;
    }
}
