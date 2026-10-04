using System.Globalization;
using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>COLLADA 1.4.1 (.dae) export, Z-up, in millimetres (unit meter="0.001").</summary>
public static class DaeWriter
{
    private static readonly XNamespace Ns = "http://www.collada.org/2005/11/COLLADASchema";

    public static void Write(IReadOnlyList<Triangle> triangles, Stream stream)
    {
        var c = CultureInfo.InvariantCulture;
        var groups = triangles.GroupBy(t => t.Material).ToList();
        var effects = new XElement(Ns + "library_effects");
        var materials = new XElement(Ns + "library_materials");
        var geometries = new XElement(Ns + "library_geometries");
        var node = new XElement(Ns + "node", new XAttribute("id", "Model"), new XAttribute("name", "Model"));

        for (var gi = 0; gi < groups.Count; gi++)
        {
            var m = groups[gi].Key;
            var col = m?.Color ?? Rgba.DefaultFront;
            var opacity = m?.Opacity ?? 1;
            var matId = $"material{gi}";

            effects.Add(new XElement(Ns + "effect", new XAttribute("id", $"{matId}-effect"),
                new XElement(Ns + "profile_COMMON",
                    new XElement(Ns + "technique", new XAttribute("sid", "common"),
                        new XElement(Ns + "lambert",
                            new XElement(Ns + "diffuse",
                                new XElement(Ns + "color", string.Create(c, $"{col.R / 255.0:0.####} {col.G / 255.0:0.####} {col.B / 255.0:0.####} 1"))),
                            new XElement(Ns + "transparency", new XElement(Ns + "float", opacity.ToString("0.####", c))))))));
            materials.Add(new XElement(Ns + "material", new XAttribute("id", matId), new XAttribute("name", m?.Name ?? "Default"),
                new XElement(Ns + "instance_effect", new XAttribute("url", $"#{matId}-effect"))));

            // Weld identical positions so the mesh keeps its topology.
            var index = new Dictionary<Vec3, int>();
            var positions = new List<Vec3>();
            var normals = new List<Vec3>();
            var p = new List<int>();
            var tris = groups[gi].ToList();
            foreach (var t in tris)
            {
                var ni = normals.Count;
                normals.Add(t.Normal);
                foreach (var v in new[] { t.A, t.B, t.C })
                {
                    if (!index.TryGetValue(v, out var vi))
                    {
                        vi = positions.Count;
                        index[v] = vi;
                        positions.Add(v);
                    }
                    p.Add(vi);
                    p.Add(ni);
                }
            }

            var geoId = $"geometry{gi}";
            geometries.Add(new XElement(Ns + "geometry", new XAttribute("id", geoId),
                new XElement(Ns + "mesh",
                    Source($"{geoId}-positions", positions, c),
                    Source($"{geoId}-normals", normals, c),
                    new XElement(Ns + "vertices", new XAttribute("id", $"{geoId}-vertices"),
                        new XElement(Ns + "input", new XAttribute("semantic", "POSITION"), new XAttribute("source", $"#{geoId}-positions"))),
                    new XElement(Ns + "triangles", new XAttribute("material", matId), new XAttribute("count", tris.Count),
                        new XElement(Ns + "input", new XAttribute("semantic", "VERTEX"), new XAttribute("source", $"#{geoId}-vertices"), new XAttribute("offset", 0)),
                        new XElement(Ns + "input", new XAttribute("semantic", "NORMAL"), new XAttribute("source", $"#{geoId}-normals"), new XAttribute("offset", 1)),
                        new XElement(Ns + "p", string.Join(' ', p))))));

            node.Add(new XElement(Ns + "instance_geometry", new XAttribute("url", $"#{geoId}"),
                new XElement(Ns + "bind_material",
                    new XElement(Ns + "technique_common",
                        new XElement(Ns + "instance_material", new XAttribute("symbol", matId), new XAttribute("target", $"#{matId}"))))));
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", c);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "COLLADA", new XAttribute("version", "1.4.1"),
                new XElement(Ns + "asset",
                    new XElement(Ns + "contributor", new XElement(Ns + "authoring_tool", "Dogeometric")),
                    new XElement(Ns + "created", now),
                    new XElement(Ns + "modified", now),
                    new XElement(Ns + "unit", new XAttribute("meter", "0.001"), new XAttribute("name", "millimeter")),
                    new XElement(Ns + "up_axis", "Z_UP")),
                effects.HasElements ? effects : null,
                materials.HasElements ? materials : null,
                geometries.HasElements ? geometries : null,
                new XElement(Ns + "library_visual_scenes",
                    new XElement(Ns + "visual_scene", new XAttribute("id", "Scene"), node)),
                new XElement(Ns + "scene", new XElement(Ns + "instance_visual_scene", new XAttribute("url", "#Scene")))));
        doc.Save(stream);
    }

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
