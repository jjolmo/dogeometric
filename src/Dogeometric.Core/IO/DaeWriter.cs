using System.Globalization;
using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>COLLADA 1.4.1 (.dae) export, Z-up, in millimetres (unit meter="0.001"); textures go beside it, in a folder
/// named after the file, as SketchUp writes them.</summary>
public static class DaeWriter
{
    private static readonly XNamespace Ns = "http://www.collada.org/2005/11/COLLADASchema";

    /// <summary>Writes the .dae and its textures beside it (in "&lt;name&gt;/").</summary>
    public static void Write(IReadOnlyList<Triangle> triangles, string path)
    {
        var folder = Path.GetFileNameWithoutExtension(path);
        using var stream = File.Create(path);
        Write(triangles, stream, folder, (relative, data) =>
        {
            var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        });
    }

    /// <summary>Writes the .dae; textured materials hand their pictures to <paramref name="saveImage"/> under
    /// <paramref name="imageFolder"/> (left untextured without it).</summary>
    public static void Write(IReadOnlyList<Triangle> triangles, Stream stream, string? imageFolder = null, Action<string, byte[]>? saveImage = null)
    {
        var c = CultureInfo.InvariantCulture;
        var groups = triangles.GroupBy(t => t.Material).ToList();
        var images = new XElement(Ns + "library_images");
        var imageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uvs = new List<(double U, double V)>();
        var effects = new XElement(Ns + "library_effects");
        var materials = new XElement(Ns + "library_materials");
        var geometries = new XElement(Ns + "library_geometries");
        var node = new XElement(Ns + "node", new XAttribute("id", "Model"), new XAttribute("name", "Model"));
        // One mesh, its positions welded so it keeps its topology, with a triangle list per material.
        var index = new Dictionary<Vec3, int>();
        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var primitives = new List<XElement>();
        var bindings = new List<XElement>();

        for (var gi = 0; gi < groups.Count; gi++)
        {
            var m = groups[gi].Key;
            var col = m?.Color ?? Rgba.DefaultFront;
            var opacity = m?.Opacity ?? 1;
            var matId = $"material{gi}";

            var textured = m?.Texture is { Data.Length: > 0 } && saveImage != null && groups[gi].All(t => t.Uv != null);
            var profile = new XElement(Ns + "profile_COMMON");
            XElement diffuse;
            if (textured)
            {
                var file = UniqueFile(m!.Texture!.FileName, imageFiles);
                var relative = string.IsNullOrEmpty(imageFolder) ? file : $"{imageFolder}/{file}";
                saveImage!(relative, m.Texture.Data);
                images.Add(new XElement(Ns + "image", new XAttribute("id", $"{matId}-image"), new XElement(Ns + "init_from", relative)));
                profile.Add(new XElement(Ns + "newparam", new XAttribute("sid", $"{matId}-surface"),
                        new XElement(Ns + "surface", new XAttribute("type", "2D"), new XElement(Ns + "init_from", $"{matId}-image"))),
                    new XElement(Ns + "newparam", new XAttribute("sid", $"{matId}-sampler"),
                        new XElement(Ns + "sampler2D", new XElement(Ns + "source", $"{matId}-surface"))));
                diffuse = new XElement(Ns + "texture", new XAttribute("texture", $"{matId}-sampler"), new XAttribute("texcoord", "UVSET0"));
            }
            else
                diffuse = new XElement(Ns + "color", string.Create(c, $"{col.R / 255.0:0.####} {col.G / 255.0:0.####} {col.B / 255.0:0.####} 1"));
            profile.Add(new XElement(Ns + "technique", new XAttribute("sid", "common"),
                new XElement(Ns + "lambert",
                    new XElement(Ns + "diffuse", diffuse),
                    new XElement(Ns + "transparency", new XElement(Ns + "float", opacity.ToString("0.####", c))))));
            effects.Add(new XElement(Ns + "effect", new XAttribute("id", $"{matId}-effect"), profile));
            materials.Add(new XElement(Ns + "material", new XAttribute("id", matId), new XAttribute("name", m?.Name ?? "Default"),
                new XElement(Ns + "instance_effect", new XAttribute("url", $"#{matId}-effect"))));

            var tris = groups[gi].ToList();
            var pi = new List<int>();
            foreach (var t in tris)
            {
                var ni = normals.Count;
                normals.Add(t.Normal);
                var corners = t.Uv is { } uv ? new[] { (t.A, uv.A), (t.B, uv.B), (t.C, uv.C) } : [(t.A, default), (t.B, default), (t.C, default)];
                foreach (var (v, q) in corners)
                {
                    if (!index.TryGetValue(v, out var vi))
                    {
                        vi = positions.Count;
                        index[v] = vi;
                        positions.Add(v);
                    }
                    pi.Add(vi);
                    pi.Add(ni);
                    if (textured)
                    {
                        pi.Add(uvs.Count);
                        uvs.Add(q);
                    }
                }
            }
            primitives.Add(new XElement(Ns + "triangles", new XAttribute("material", matId), new XAttribute("count", tris.Count),
                new XElement(Ns + "input", new XAttribute("semantic", "VERTEX"), new XAttribute("source", "#geometry0-vertices"), new XAttribute("offset", 0)),
                new XElement(Ns + "input", new XAttribute("semantic", "NORMAL"), new XAttribute("source", "#geometry0-normals"), new XAttribute("offset", 1)),
                textured ? new XElement(Ns + "input", new XAttribute("semantic", "TEXCOORD"), new XAttribute("source", "#geometry0-uv"), new XAttribute("offset", 2), new XAttribute("set", 0)) : null,
                new XElement(Ns + "p", string.Join(' ', pi))));
            bindings.Add(new XElement(Ns + "instance_material", new XAttribute("symbol", matId), new XAttribute("target", $"#{matId}"),
                textured ? new XElement(Ns + "bind_vertex_input", new XAttribute("semantic", "UVSET0"), new XAttribute("input_semantic", "TEXCOORD"), new XAttribute("input_set", 0)) : null));
        }

        geometries.Add(new XElement(Ns + "geometry", new XAttribute("id", "geometry0"),
            new XElement(Ns + "mesh",
                Source("geometry0-positions", positions, c),
                Source("geometry0-normals", normals, c),
                uvs.Count > 0 ? UvSource("geometry0-uv", uvs, c) : null,
                new XElement(Ns + "vertices", new XAttribute("id", "geometry0-vertices"),
                    new XElement(Ns + "input", new XAttribute("semantic", "POSITION"), new XAttribute("source", "#geometry0-positions"))),
                primitives)));
        node.Add(new XElement(Ns + "instance_geometry", new XAttribute("url", "#geometry0"),
            new XElement(Ns + "bind_material", new XElement(Ns + "technique_common", bindings))));

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", c);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "COLLADA", new XAttribute("version", "1.4.1"),
                new XElement(Ns + "asset",
                    new XElement(Ns + "contributor", new XElement(Ns + "authoring_tool", "Dogeometric")),
                    new XElement(Ns + "created", now),
                    new XElement(Ns + "modified", now),
                    new XElement(Ns + "unit", new XAttribute("meter", "0.001"), new XAttribute("name", "millimeter")),
                    new XElement(Ns + "up_axis", "Z_UP")),
                images.HasElements ? images : null,
                effects.HasElements ? effects : null,
                materials.HasElements ? materials : null,
                geometries.HasElements ? geometries : null,
                new XElement(Ns + "library_visual_scenes",
                    new XElement(Ns + "visual_scene", new XAttribute("id", "Scene"), node)),
                new XElement(Ns + "scene", new XElement(Ns + "instance_visual_scene", new XAttribute("url", "#Scene")))));
        doc.Save(stream);
    }

    private static string UniqueFile(string name, HashSet<string> taken)
    {
        var file = Path.GetFileName(name);
        if (file.Length == 0)
            file = "texture.png";
        var (stem, ext) = (Path.GetFileNameWithoutExtension(file), Path.GetExtension(file));
        for (var i = 1; !taken.Add(file); i++)
            file = $"{stem}{i}{ext}";
        return file;
    }

    private static XElement UvSource(string id, List<(double U, double V)> values, CultureInfo c) =>
        new(Ns + "source", new XAttribute("id", id),
            new XElement(Ns + "float_array", new XAttribute("id", $"{id}-array"), new XAttribute("count", values.Count * 2),
                string.Join(' ', values.Select(v => string.Create(c, $"{v.U:G9} {v.V:G9}")))),
            new XElement(Ns + "technique_common",
                new XElement(Ns + "accessor", new XAttribute("source", $"#{id}-array"), new XAttribute("count", values.Count), new XAttribute("stride", 2),
                    new XElement(Ns + "param", new XAttribute("name", "S"), new XAttribute("type", "float")),
                    new XElement(Ns + "param", new XAttribute("name", "T"), new XAttribute("type", "float")))));

    private static XElement Source(string id, List<Vec3> values, CultureInfo c) =>
        new(Ns + "source", new XAttribute("id", id),
            new XElement(Ns + "float_array", new XAttribute("id", $"{id}-array"), new XAttribute("count", values.Count * 3),
                string.Join(' ', values.Select(v => string.Create(c, $"{v.X:G9} {v.Y:G9} {v.Z:G9}")))),
            new XElement(Ns + "technique_common",
                new XElement(Ns + "accessor", new XAttribute("source", $"#{id}-array"), new XAttribute("count", values.Count), new XAttribute("stride", 3),
                    new XElement(Ns + "param", new XAttribute("name", "X"), new XAttribute("type", "float")),
                    new XElement(Ns + "param", new XAttribute("name", "Y"), new XAttribute("type", "float")),
                    new XElement(Ns + "param", new XAttribute("name", "Z"), new XAttribute("type", "float")))));
}
