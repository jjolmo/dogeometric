using System.Text;
using System.Text.Json;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// glTF 2.0 binary (.glb) export. glTF is Y-up and in metres, so model (x, y, z) mm becomes (x, z, -y) / 1000.
/// One primitive per material, flat normals, double-sided materials (SketchUp faces show both sides).
/// </summary>
public static class GltfWriter
{
    public static void WriteGlb(IReadOnlyList<Triangle> triangles, Stream stream)
    {
        var groups = triangles.GroupBy(t => t.Material).ToList();
        var materials = groups.Select(g => g.Key).ToList();

        var bin = new MemoryStream();
        var bw = new BinaryWriter(bin);
        var accessors = new List<object>();
        var bufferViews = new List<object>();
        var primitives = new List<object>();

        foreach (var g in groups)
        {
            var tris = g.ToList();
            var count = tris.Count * 3;

            // Positions.
            var posOffset = (int)bin.Position;
            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            foreach (var t in tris)
            {
                foreach (var p in new[] { t.A, t.B, t.C })
                {
                    var v = ToGltf(p);
                    for (var k = 0; k < 3; k++)
                    {
                        min[k] = Math.Min(min[k], v[k]);
                        max[k] = Math.Max(max[k], v[k]);
                        bw.Write(v[k]);
                    }
                }
            }
            var posView = bufferViews.Count;
            bufferViews.Add(new { buffer = 0, byteOffset = posOffset, byteLength = count * 12, target = 34962 });

            // Normals.
            var nOffset = (int)bin.Position;
            foreach (var t in tris)
            {
                var n = t.Normal;
                var gn = new[] { (float)n.X, (float)n.Z, (float)-n.Y };
                if (n.IsZero(1e-12))
                    gn = [0, 1, 0];
                for (var i = 0; i < 3; i++)
                    foreach (var c in gn)
                        bw.Write(c);
            }
            var nView = bufferViews.Count;
            bufferViews.Add(new { buffer = 0, byteOffset = nOffset, byteLength = count * 12, target = 34962 });

            var posAccessor = accessors.Count;
            accessors.Add(new { bufferView = posView, componentType = 5126, count, type = "VEC3", min, max });
            var nAccessor = accessors.Count;
            accessors.Add(new { bufferView = nView, componentType = 5126, count, type = "VEC3" });

            primitives.Add(new
            {
                attributes = new Dictionary<string, int> { ["POSITION"] = posAccessor, ["NORMAL"] = nAccessor },
                material = materials.IndexOf(g.Key),
                mode = 4,
            });
        }

        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "Dogeometric" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = new[] { new { mesh = 0, name = "Model" } },
            ["meshes"] = new[] { new { primitives } },
            ["materials"] = materials.Select(MaterialJson).ToArray(),
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new[] { new { byteLength = (int)bin.Length } },
        };
        if (triangles.Count == 0)
        {
            // A mesh needs at least one primitive; an empty export is a scene with one empty node.
            gltf["nodes"] = new[] { new { name = "Model" } };
            gltf.Remove("meshes");
            gltf.Remove("accessors");
            gltf.Remove("bufferViews");
            gltf.Remove("buffers");
            gltf.Remove("materials");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(gltf);
        var jsonPadded = Pad(json, (byte)' ');
        var binPadded = Pad(bin.ToArray(), 0);
        var hasBin = triangles.Count > 0;

        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var total = 12 + 8 + jsonPadded.Length + (hasBin ? 8 + binPadded.Length : 0);
        w.Write(0x46546C67u); // "glTF"
        w.Write(2u);
        w.Write((uint)total);
        w.Write((uint)jsonPadded.Length);
        w.Write(0x4E4F534Au); // "JSON"
        w.Write(jsonPadded);
        if (hasBin)
        {
            w.Write((uint)binPadded.Length);
            w.Write(0x004E4942u); // "BIN\0"
            w.Write(binPadded);
        }
    }

    private static object MaterialJson(Material? m)
    {
        var c = m?.Color ?? Rgba.DefaultFront;
        var opacity = m?.Opacity ?? 1;
        return new Dictionary<string, object>
        {
            ["name"] = m?.Name ?? "Default",
            ["pbrMetallicRoughness"] = new
            {
                baseColorFactor = new[] { SrgbToLinear(c.R), SrgbToLinear(c.G), SrgbToLinear(c.B), opacity },
                metallicFactor = 0.0,
                roughnessFactor = 1.0,
            },
            ["alphaMode"] = opacity < 1 ? "BLEND" : "OPAQUE",
            ["doubleSided"] = true,
        };
    }

    private static double SrgbToLinear(byte v)
    {
        var c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static float[] ToGltf(Vec3 p) => [(float)(p.X / 1000), (float)(p.Z / 1000), (float)(-p.Y / 1000)];

    private static byte[] Pad(byte[] data, byte fill)
    {
        var len = (data.Length + 3) & ~3;
        if (len == data.Length)
            return data;
        var padded = new byte[len];
        data.CopyTo(padded, 0);
        for (var i = data.Length; i < len; i++)
            padded[i] = fill;
        return padded;
    }
}
