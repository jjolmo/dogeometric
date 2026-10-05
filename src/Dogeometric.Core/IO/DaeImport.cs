using System.Globalization;
using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>File › Import of COLLADA (.dae) and KMZ: every placed mesh through its nodes, unit and up axis, with material
/// colours and opacity, as one group.</summary>
public static class DaeImport
{
    public static Model Load(string path) => Path.GetExtension(path).Equals(".kmz", StringComparison.OrdinalIgnoreCase)
        ? LoadKmz(path)
        : Read(XDocument.Load(path), Path.GetFileNameWithoutExtension(path));

    /// <summary>A KMZ (Google Earth) file: the COLLADA model its KML links to, or the first one inside.</summary>
    public static Model LoadKmz(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var kml = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        string? linked = null;
        if (kml != null)
            using (var s = kml.Open())
                linked = XDocument.Load(s).Descendants().FirstOrDefault(e => e.Name.LocalName == "href" && e.Value.EndsWith(".dae", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        var dae = (linked != null ? zip.GetEntry(linked.TrimStart('.', '/')) : null)
            ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".dae", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("the KMZ holds no COLLADA model");
        using var stream = dae.Open();
        return Read(XDocument.Load(stream), Path.GetFileNameWithoutExtension(path));
    }

    public static Model Read(XDocument doc, string name)
    {
        var root = doc.Root ?? throw new InvalidDataException("empty COLLADA file");
        var ns = root.Name.Namespace;
        var ids = root.Descendants().Where(e => e.Attribute("id") != null)
            .GroupBy(e => (string)e.Attribute("id")!).ToDictionary(g => g.Key, g => g.First());
        XElement? ByUrl(string? url) => url is ['#', .. var id] ? ids.GetValueOrDefault(id) : null;

        var asset = root.Element(ns + "asset");
        var meter = double.TryParse((string?)asset?.Element(ns + "unit")?.Attribute("meter"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 1;
        // Rows of the matrix that turns the file's up axis into Z.
        var up = ((string?)asset?.Element(ns + "up_axis"))?.Trim() switch
        {
            "Z_UP" => Identity(),
            "X_UP" => new double[] { 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 },
            _ => new double[] { 1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1 },
        };

        var materials = new Dictionary<string, Material>();
        Material? MaterialFor(string? url)
        {
            if (ByUrl(url) is not { } element)
                return null;
            var id = (string)element.Attribute("id")!;
            if (materials.TryGetValue(id, out var known))
                return known;
            var shading = ByUrl((string?)element.Element(ns + "instance_effect")?.Attribute("url"))
                ?.Descendants(ns + "technique").FirstOrDefault(t => t.Parent?.Name == ns + "profile_COMMON")?.Elements().FirstOrDefault();
            var colour = Numbers((string?)shading?.Element(ns + "diffuse")?.Element(ns + "color"));
            var opacity = Numbers((string?)shading?.Element(ns + "transparency")?.Element(ns + "float")) is [var t] ? t : 1;
            byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            return materials[id] = new Material
            {
                Name = (string?)element.Attribute("name") ?? id,
                Color = colour.Length >= 3 ? new Rgba(B(colour[0]), B(colour[1]), B(colour[2])) : new Rgba(200, 200, 200),
                Opacity = Math.Clamp(opacity * (colour.Length >= 4 ? colour[3] : 1), 0, 1),
            };
        }

        var polygons = new List<List<Vec3>>();
        var painted = new List<Material?>();

        void AddMesh(XElement geometry, double[] world, Dictionary<string, string> bindings)
        {
            if (geometry.Element(ns + "mesh") is not { } mesh)
                return;
            var sources = mesh.Elements(ns + "source").ToDictionary(s => (string)s.Attribute("id")!, s =>
                (Data: Numbers((string?)s.Element(ns + "float_array")),
                 Stride: (int?)s.Element(ns + "technique_common")?.Element(ns + "accessor")?.Attribute("stride") ?? 3));
            var positionSource = mesh.Element(ns + "vertices")?.Elements(ns + "input").FirstOrDefault(i => (string?)i.Attribute("semantic") == "POSITION");
            if (positionSource == null || ByUrl((string?)positionSource.Attribute("source")) is not { } src
                || !sources.TryGetValue((string)src.Attribute("id")!, out var positions))
                return;
            Vec3 Point(int i) => Apply(world, new Vec3(positions.Data[i * positions.Stride], positions.Data[i * positions.Stride + 1], positions.Data[i * positions.Stride + 2]));

            foreach (var primitive in mesh.Elements().Where(e => e.Name.LocalName is "triangles" or "polylist" or "polygons"))
            {
                var inputs = primitive.Elements(ns + "input").ToList();
                var stride = inputs.Count == 0 ? 1 : inputs.Max(i => (int?)i.Attribute("offset") ?? 0) + 1;
                var vertexOffset = (int?)inputs.FirstOrDefault(i => (string?)i.Attribute("semantic") == "VERTEX")?.Attribute("offset") ?? 0;
                var material = MaterialFor(bindings.GetValueOrDefault((string?)primitive.Attribute("material") ?? ""));
                void Polygon(double[] p, int start, int corners)
                {
                    var polygon = new List<Vec3>(corners);
                    for (var c = 0; c < corners; c++)
                        polygon.Add(Point((int)p[(start + c) * stride + vertexOffset]));
                    polygons.Add(polygon);
                    painted.Add(material);
                }
                if (primitive.Name.LocalName == "polygons")
                {
                    foreach (var p in primitive.Elements(ns + "p").Select(e => Numbers(e.Value)))
                        Polygon(p, 0, p.Length / stride);
                    continue;
                }
                var indices = Numbers((string?)primitive.Element(ns + "p"));
                var counts = primitive.Name.LocalName == "polylist"
                    ? Numbers((string?)primitive.Element(ns + "vcount")).Select(v => (int)v).ToArray()
                    : Enumerable.Repeat(3, indices.Length / stride / 3).ToArray();
                var at = 0;
                foreach (var n in counts)
                {
                    if ((at + n) * stride > indices.Length)
                        break;
                    Polygon(indices, at, n);
                    at += n;
                }
            }
        }

        void Visit(XElement node, double[] parent, int depth)
        {
            if (depth > 64)
                return;
            var world = parent;
            foreach (var t in node.Elements())
            {
                var v = Numbers(t.Value);
                world = t.Name.LocalName switch
                {
                    "matrix" when v.Length == 16 => Multiply(world, v),
                    "translate" when v.Length == 3 => Multiply(world, [1, 0, 0, v[0], 0, 1, 0, v[1], 0, 0, 1, v[2], 0, 0, 0, 1]),
                    "scale" when v.Length == 3 => Multiply(world, [v[0], 0, 0, 0, 0, v[1], 0, 0, 0, 0, v[2], 0, 0, 0, 0, 1]),
                    "rotate" when v.Length == 4 => Multiply(world, Rotation(new Vec3(v[0], v[1], v[2]), v[3] * Math.PI / 180)),
                    _ => world,
                };
            }
            foreach (var instance in node.Elements(ns + "instance_geometry"))
                if (ByUrl((string?)instance.Attribute("url")) is { } geometry)
                    AddMesh(geometry, world, instance.Descendants(ns + "instance_material")
                        .GroupBy(i => (string)i.Attribute("symbol")!).ToDictionary(g => g.Key, g => (string)g.First().Attribute("target")!));
            foreach (var instance in node.Elements(ns + "instance_node"))
                if (ByUrl((string?)instance.Attribute("url")) is { } shared)
                    Visit(shared, world, depth + 1);
            foreach (var child in node.Elements(ns + "node"))
                Visit(child, world, depth + 1);
        }

        var scene = ByUrl((string?)root.Element(ns + "scene")?.Element(ns + "instance_visual_scene")?.Attribute("url"))
            ?? root.Element(ns + "library_visual_scenes")?.Element(ns + "visual_scene");
        foreach (var node in scene?.Elements(ns + "node") ?? [])
            Visit(node, up, 0);
        if (polygons.Count == 0)
            throw new InvalidDataException("no geometry in the COLLADA scene");
        return MeshImport.Build(name, polygons, meter * 1000, painted);
    }

    private static double[] Numbers(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    private static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>Row-major 4×4 product, as COLLADA writes its matrices.</summary>
    private static double[] Multiply(double[] a, double[] b)
    {
        var r = new double[16];
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
                for (var k = 0; k < 4; k++)
                    r[i * 4 + j] += a[i * 4 + k] * b[k * 4 + j];
        return r;
    }

    private static double[] Rotation(Vec3 axis, double angle)
    {
        var (x, y, z) = (axis.Normalized().X, axis.Normalized().Y, axis.Normalized().Z);
        var (c, s) = (Math.Cos(angle), Math.Sin(angle));
        var t = 1 - c;
        return [t * x * x + c, t * x * y - s * z, t * x * z + s * y, 0,
                t * x * y + s * z, t * y * y + c, t * y * z - s * x, 0,
                t * x * z - s * y, t * y * z + s * x, t * z * z + c, 0,
                0, 0, 0, 1];
    }

    private static Vec3 Apply(double[] m, Vec3 p) => new(
        m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3],
        m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7],
        m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11]);
}
