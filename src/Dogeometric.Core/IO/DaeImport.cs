using System.Globalization;
using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>File › Import of COLLADA (.dae) and KMZ: shared nodes (SketchUp's components) as components, other meshes as
/// groups, placed by their nodes, in the file's unit and turned to Z up, with material colours and opacity.</summary>
public static class DaeImport
{
    public static Model Load(string path) => Path.GetExtension(path).Equals(".kmz", StringComparison.OrdinalIgnoreCase)
        ? LoadKmz(path)
        : Read(XDocument.Load(path), Path.GetFileNameWithoutExtension(path), relative =>
        {
            var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Uri.UnescapeDataString(relative));
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        });

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
        var folder = Path.GetDirectoryName(dae.FullName.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        byte[]? ReadEntry(string relative)
        {
            var parts = new List<string>();
            foreach (var part in $"{folder}/{Uri.UnescapeDataString(relative)}".Split('/', StringSplitOptions.RemoveEmptyEntries))
                if (part == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                }
                else if (part != ".")
                    parts.Add(part);
            if (zip.GetEntry(string.Join('/', parts)) is not { } entry)
                return null;
            using var s = entry.Open();
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            return copy.ToArray();
        }
        XDocument xml;
        using (var stream = dae.Open())
            xml = XDocument.Load(stream);
        return Read(xml, Path.GetFileNameWithoutExtension(path), ReadEntry);
    }

    /// <summary>Reads a COLLADA document; <paramref name="readFile"/> fetches its textures by their relative paths.</summary>
    public static Model Read(XDocument doc, string name, Func<string, byte[]?>? readFile = null)
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
                Texture = TextureOf(shading?.Element(ns + "diffuse")?.Element(ns + "texture")),
            };
        }

        // diffuse <texture> → sampler newparam → surface newparam → image → its file.
        TextureImage? TextureOf(XElement? texture)
        {
            if (texture == null || readFile == null)
                return null;
            var profile = texture.Ancestors(ns + "profile_COMMON").FirstOrDefault();
            XElement? Param(string? sid) => profile?.Elements(ns + "newparam").FirstOrDefault(p => (string?)p.Attribute("sid") == sid);
            var target = (string?)texture.Attribute("texture");
            var surface = (string?)Param(target)?.Element(ns + "sampler2D")?.Element(ns + "source");
            var imageId = ((string?)Param(surface)?.Element(ns + "surface")?.Element(ns + "init_from") ?? target)?.Trim();
            var file = imageId != null ? ((string?)ids.GetValueOrDefault(imageId)?.Element(ns + "init_from"))?.Trim() : null;
            if (string.IsNullOrEmpty(file))
                return null;
            if (file.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                file = file[7..];
            return readFile(file) is { Length: > 0 } data ? new TextureImage { FileName = Path.GetFileName(file.Replace('\\', '/')), Data = data } : null;
        }

        var polygons = new List<List<Vec3>>();
        var painted = new List<object?>();

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
            var sourceIds = mesh.Elements(ns + "source").ToDictionary(e => "#" + (string)e.Attribute("id")!, e => (string)e.Attribute("id")!);

            foreach (var primitive in mesh.Elements().Where(e => e.Name.LocalName is "triangles" or "polylist" or "polygons"))
            {
                var inputs = primitive.Elements(ns + "input").ToList();
                var stride = inputs.Count == 0 ? 1 : inputs.Max(i => (int?)i.Attribute("offset") ?? 0) + 1;
                var vertexOffset = (int?)inputs.FirstOrDefault(i => (string?)i.Attribute("semantic") == "VERTEX")?.Attribute("offset") ?? 0;
                var material = MaterialFor(bindings.GetValueOrDefault((string?)primitive.Attribute("material") ?? ""));
                var texcoord = inputs.Where(i => (string?)i.Attribute("semantic") == "TEXCOORD").OrderBy(i => (int?)i.Attribute("set") ?? 0).FirstOrDefault();
                var uvs = material?.Texture != null && texcoord != null && sourceIds.TryGetValue((string?)texcoord.Attribute("source") ?? "", out var uvId)
                    ? sources[uvId] : default;
                var uvOffset = (int?)texcoord?.Attribute("offset") ?? 0;
                void Polygon(double[] p, int start, int corners)
                {
                    var polygon = new List<Vec3>(corners);
                    for (var c = 0; c < corners; c++)
                        polygon.Add(Point((int)p[(start + c) * stride + vertexOffset]));
                    polygons.Add(polygon);
                    if (uvs.Data is { Length: > 0 } && corners >= 3)
                    {
                        (double U, double V) Uv(int c) => ((int)p[(start + c) * stride + uvOffset] * uvs.Stride is var k && k + 1 < uvs.Data.Length ? (uvs.Data[k], uvs.Data[k + 1]) : (0, 0));
                        painted.Add(TexturedKey.From(material!, polygon[0], polygon[1], polygon[2], Uv(0), Uv(1), Uv(2)) ?? (object)material!);
                    }
                    else
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

        var unit = meter * 1000;
        var model = new Model();
        var geometryDefs = new Dictionary<string, ComponentDefinition?>();
        var nodeDefs = new Dictionary<XElement, ComponentDefinition>();
        Transform ToTransform(double[] m) =>
            new(new Vec3(m[0], m[4], m[8]), new Vec3(m[1], m[5], m[9]), new Vec3(m[2], m[6], m[10]), new Vec3(m[3], m[7], m[11]) * unit);

        // A geometry's faces in millimetres, with their material bindings.
        bool AddFaces(Entities target, XElement geometry, Dictionary<string, string> bindings)
        {
            polygons.Clear();
            painted.Clear();
            AddMesh(geometry, [unit, 0, 0, 0, 0, unit, 0, 0, 0, 0, unit, 0, 0, 0, 0, 1], bindings);
            foreach (var (face, front, back) in MeshImport.AddMerged(target, polygons.Select((p, i) => (p, painted[i])).ToList()))
            {
                (face.FrontMaterial, face.FrontMapping) = front is TexturedKey f ? (f.Material, f.Mapping(face.Normal)) : ((Material?)front, null);
                (face.BackMaterial, face.BackMapping) = back is TexturedKey b ? (b.Material, b.Mapping(face.Normal)) : ((Material?)back, null);
            }
            return polygons.Count > 0;
        }

        // A geometry (with its material bindings) as a group, made once.
        ComponentDefinition? GeometryDef(XElement geometry, Dictionary<string, string> bindings)
        {
            var key = (string?)geometry.Attribute("id") + "|" + string.Join(",", bindings.OrderBy(b => b.Key).Select(b => b.Key + "=" + b.Value));
            if (geometryDefs.TryGetValue(key, out var known))
                return known;
            var def = new ComponentDefinition { Name = (string?)geometry.Attribute("name") ?? (string?)geometry.Attribute("id") ?? "Geometry", IsGroup = true };
            if (!AddFaces(def.Entities, geometry, bindings))
                return geometryDefs[key] = null;
            model.Definitions.Add(def);
            return geometryDefs[key] = def;
        }

        // A node others instance (SketchUp's components) as a component, made once.
        ComponentDefinition NodeDef(XElement shared, int depth)
        {
            if (nodeDefs.TryGetValue(shared, out var known))
                return known;
            var def = new ComponentDefinition { Name = (string?)shared.Attribute("name") ?? (string?)shared.Attribute("id") ?? "Component" };
            nodeDefs[shared] = def;
            model.Definitions.Add(def);
            Visit(shared, def.Entities, Transform.Identity, depth + 1, inline: true);
            return def;
        }

        // Inline: the node is a component's own, so its untransformed meshes are the component's faces.
        void Visit(XElement node, Entities target, Transform parent, int depth, bool inline = false)
        {
            if (depth > 64)
                return;
            var local = Identity();
            foreach (var t in node.Elements())
            {
                var v = Numbers(t.Value);
                local = t.Name.LocalName switch
                {
                    "matrix" when v.Length == 16 => Multiply(local, v),
                    "translate" when v.Length == 3 => Multiply(local, [1, 0, 0, v[0], 0, 1, 0, v[1], 0, 0, 1, v[2], 0, 0, 0, 1]),
                    "scale" when v.Length == 3 => Multiply(local, [v[0], 0, 0, 0, 0, v[1], 0, 0, 0, 0, v[2], 0, 0, 0, 0, 1]),
                    "rotate" when v.Length == 4 => Multiply(local, Rotation(new Vec3(v[0], v[1], v[2]), v[3] * Math.PI / 180)),
                    _ => local,
                };
            }
            var xf = ToTransform(local).Then(parent);
            foreach (var instance in node.Elements(ns + "instance_geometry"))
            {
                if (ByUrl((string?)instance.Attribute("url")) is not { } geometry)
                    continue;
                var bindings = instance.Descendants(ns + "instance_material")
                    .GroupBy(i => (string)i.Attribute("symbol")!).ToDictionary(g => g.Key, g => (string)g.First().Attribute("target")!);
                if (inline && xf == Transform.Identity)
                    AddFaces(target, geometry, bindings);
                else if (GeometryDef(geometry, bindings) is { } def)
                    target.AddInstance(def, xf);
            }
            foreach (var instance in node.Elements(ns + "instance_node"))
                if (ByUrl((string?)instance.Attribute("url")) is { } shared)
                    target.AddInstance(NodeDef(shared, depth), xf).Name = (string?)node.Attribute("name") ?? "";
            foreach (var child in node.Elements(ns + "node"))
                Visit(child, target, xf, depth + 1);
        }

        var scene = ByUrl((string?)root.Element(ns + "scene")?.Element(ns + "instance_visual_scene")?.Attribute("url"))
            ?? root.Element(ns + "library_visual_scenes")?.Element(ns + "visual_scene");
        var top = new ComponentDefinition { Name = name, IsGroup = true };
        foreach (var node in scene?.Elements(ns + "node") ?? [])
            Visit(node, top.Entities, Transform.Identity, 0);
        if (top.Entities.Instances.Count == 0)
            throw new InvalidDataException("no geometry in the COLLADA scene");
        // A geometry placed more than once is a component, as SketchUp shows it.
        var uses = model.AllEntities.Append(top.Entities).SelectMany(e => e.Instances).GroupBy(i => i.Definition).ToDictionary(g => g.Key, g => g.Count());
        foreach (var def in geometryDefs.Values.OfType<ComponentDefinition>().Where(d => uses.GetValueOrDefault(d) > 1))
            def.IsGroup = false;
        foreach (var used in materials.Values)
            model.Materials.Add(used);
        model.Definitions.Add(top);
        // The file's up axis turns the whole model to SketchUp's Z up.
        model.Entities.AddInstance(top, ToTransform(up));
        return model;
    }

    /// <summary>A textured polygon's material and where its texture's (0,0), (1,0) and (0,1) land in space; triangles of
    /// one face share them, so they merge only with triangles textured the same way.</summary>
    private sealed class TexturedKey(Material material, Vec3 origin, Vec3 uEnd, Vec3 vEnd) : IEquatable<TexturedKey>
    {
        public Material Material => material;

        public static TexturedKey? From(Material material, Vec3 p0, Vec3 p1, Vec3 p2, (double U, double V) t0, (double U, double V) t1, (double U, double V) t2)
        {
            var (a, b, c, d) = (t1.U - t0.U, t1.V - t0.V, t2.U - t0.U, t2.V - t0.V);
            var det = a * d - b * c;
            if (Math.Abs(det) < 1e-12)
                return null;
            var du = ((p1 - p0) * d - (p2 - p0) * b) * (1 / det);
            var dv = ((p2 - p0) * a - (p1 - p0) * c) * (1 / det);
            var origin = p0 - du * t0.U - dv * t0.V;
            // The picture's size: one copy of it across the first face that uses it.
            if (material.Texture is { WidthMm: <= 0 } texture)
                (texture.WidthMm, texture.HeightMm) = (du.Length, dv.Length);
            return new TexturedKey(material, origin, origin + du, origin + dv);
        }

        public TextureMapping Mapping(Vec3 normal)
        {
            var (x, y) = Texturing.PlaneAxes(normal);
            (double, double) Flat(Vec3 p) => (p.Dot(x), p.Dot(y));
            return TextureMapping.FromPlanePoints(Flat(origin), Flat(uEnd), Flat(vEnd), material.Texture!.WidthMm, material.Texture.HeightMm);
        }

        public bool Equals(TexturedKey? other) => other != null && other.Material == material
            && other.Corners().Zip(Corners()).All(pair => pair.First.DistanceTo(pair.Second) <= 1e-6 * Math.Max(1, (uEnd - origin).Length + (vEnd - origin).Length));

        private Vec3[] Corners() => [origin, uEnd, vEnd];

        public override bool Equals(object? obj) => Equals(obj as TexturedKey);

        public override int GetHashCode() => material.GetHashCode();
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
