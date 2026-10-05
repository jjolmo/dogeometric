using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>3D Studio (.3ds) export and import as SketchUp does: a mesh object per top-level group or component, material
/// colours and transparency, millimetres with the master scale saying so.</summary>
public static class ThreeDs
{
    private const ushort Main = 0x4D4D, Version = 0x0002, Editor = 0x3D3D, MeshVersion = 0x3D3E, MasterScale = 0x0100;
    private const ushort MaterialBlock = 0xAFFF, MaterialName = 0xA000, Ambient = 0xA010, Diffuse = 0xA020, Specular = 0xA030, Transparency = 0xA050;
    private const ushort Rgb24 = 0x0011, RgbFloat = 0x0010, Percent = 0x0030;
    private const ushort Object = 0x4000, TriMesh = 0x4100, Vertices = 0x4110, Faces = 0x4120, FaceMaterials = 0x4130, Smoothing = 0x4150, Matrix = 0x4160;
    private const int MaxVertices = 65535;

    /// <summary>Writes the model (or the selection) and returns how many mesh objects it holds; 3DS indexes vertices with
    /// 16 bits, so bigger meshes go out as several objects.</summary>
    public static int Write(Model model, Stream stream, ExportOptions? options = null, int maxVertices = MaxVertices)
    {
        options ??= new ExportOptions();
        var root = options.SelectionContext ?? model.Entities;
        var rootXf = options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity;
        bool Wanted(object e) => options.Selection == null || options.Selection.Contains(e);

        var objects = new List<(string Name, List<Triangle> Triangles)>();
        foreach (var inst in root.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
            objects.Add((inst.Name.Length > 0 ? inst.Name : inst.Definition.Name,
                MeshExtractor.ExtractInstance(inst, includeHidden: false).Select(t => t with { A = rootXf.ApplyPoint(t.A), B = rootXf.ApplyPoint(t.B), C = rootXf.ApplyPoint(t.C) }).ToList()));
        objects.Add(("Model", MeshExtractor.Extract(model, new ExportOptions
        {
            Selection = root.Faces.Where(f => Wanted(f)).Cast<object>().ToHashSet(),
            SelectionContext = root,
            SelectionContextTransform = rootXf,
        })));

        var materials = objects.SelectMany(o => o.Triangles).Select(t => t.Material).OfType<Material>().Distinct().ToList();
        var materialNames = new Dictionary<Material, string>();
        var usedNames = new HashSet<string>();
        string Unique(string name, int length)
        {
            var baseName = new string(name.Where(c => c >= ' ' && c < 127).Take(length).ToArray());
            if (baseName.Length == 0)
                baseName = "Object";
            var candidate = baseName;
            for (var i = 2; !usedNames.Add(candidate); i++)
                candidate = baseName[..Math.Min(baseName.Length, length - 1 - i.ToString().Length)] + "_" + i;
            return candidate;
        }

        var editor = new List<byte[]> { Chunk(MeshVersion, BitConverter.GetBytes(3u)), Chunk(MasterScale, BitConverter.GetBytes((float)(1 / 25.4))) };
        foreach (var m in materials)
        {
            var name = materialNames[m] = Unique(m.Name, 16);
            var colour = m.Color;
            editor.Add(Chunk(MaterialBlock,
                Chunk(MaterialName, CString(name)),
                Chunk(Ambient, Chunk(Rgb24, [0, 0, 0])),
                Chunk(Diffuse, Chunk(Rgb24, [colour.R, colour.G, colour.B])),
                Chunk(Specular, Chunk(Rgb24, [84, 84, 84])),
                Chunk(Transparency, Chunk(Percent, BitConverter.GetBytes((ushort)Math.Round((1 - m.Opacity) * 100))))));
        }
        usedNames.Clear();
        var count = 0;
        foreach (var (name, triangles) in objects.Where(o => o.Triangles.Count > 0))
            foreach (var part in triangles.Chunk(maxVertices / 3))
            {
                var vertices = new BinaryWriter(new MemoryStream());
                vertices.Write((ushort)(part.Length * 3));
                foreach (var t in part)
                    foreach (var p in new[] { t.A, t.B, t.C })
                    {
                        vertices.Write((float)p.X);
                        vertices.Write((float)p.Y);
                        vertices.Write((float)p.Z);
                    }
                var faces = new BinaryWriter(new MemoryStream());
                faces.Write((ushort)part.Length);
                for (var i = 0; i < part.Length; i++)
                {
                    faces.Write((ushort)(3 * i));
                    faces.Write((ushort)(3 * i + 1));
                    faces.Write((ushort)(3 * i + 2));
                    faces.Write((ushort)7);
                }
                var faceChunks = new List<byte[]> { Bytes(faces) };
                // Faces in the default material are in no material list.
                foreach (var group in part.Select((t, i) => (t.Material, i)).Where(x => x.Material != null).GroupBy(x => x.Material!))
                {
                    var list = new BinaryWriter(new MemoryStream());
                    list.Write(CString(materialNames[group.Key]));
                    list.Write((ushort)group.Count());
                    foreach (var (_, i) in group)
                        list.Write((ushort)i);
                    faceChunks.Add(Chunk(FaceMaterials, Bytes(list)));
                }
                faceChunks.Add(Chunk(Smoothing, new byte[4 * part.Length]));
                var matrix = new BinaryWriter(new MemoryStream());
                foreach (var v in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 })
                    matrix.Write(v);
                editor.Add(Chunk(Object, CString(Unique(name, 10)),
                    Chunk(TriMesh, Chunk(Vertices, Bytes(vertices)), Chunk(Matrix, Bytes(matrix)), Chunk(Faces, [.. faceChunks.SelectMany(b => b)]))));
                count++;
            }
        var file = Chunk(Main, Chunk(Version, BitConverter.GetBytes(3u)), Chunk(Editor, [.. editor.SelectMany(b => b)]));
        stream.Write(file);
        return count;
    }

    /// <summary>Reads a .3ds: each mesh object becomes a group; <paramref name="mmPerUnit"/> applies when the file's master
    /// scale is the neutral 1 (otherwise the master scale, in inches, sets the unit).</summary>
    public static Model Read(byte[] data, string name, double mmPerUnit = 1)
    {
        var model = new Model();
        var materials = new Dictionary<string, Material>();
        var scale = mmPerUnit;
        var objects = new List<(string Name, List<Vec3> Vertices, List<(int A, int B, int C)> Faces, Dictionary<int, string> FaceMaterial)>();

        void Walk(int start, int end)
        {
            var at = start;
            while (at + 6 <= end)
            {
                var id = BitConverter.ToUInt16(data, at);
                var length = (int)BitConverter.ToUInt32(data, at + 2);
                if (length < 6 || at + length > end)
                    return;
                var body = at + 6;
                var next = at + length;
                switch (id)
                {
                    case Main or Editor:
                        Walk(body, next);
                        break;
                    case MasterScale:
                        var master = BitConverter.ToSingle(data, body);
                        if (master > 0 && Math.Abs(master - 1) > 1e-6)
                            scale = master * 25.4;
                        break;
                    case MaterialBlock:
                        ReadMaterial(body, next);
                        break;
                    case Object:
                    {
                        var (objectName, after) = ReadCString(body);
                        ReadObject(objectName, after, next);
                        break;
                    }
                }
                at = next;
            }
        }

        void ReadMaterial(int start, int end)
        {
            string? materialName = null;
            var colour = new Rgba(200, 200, 200);
            var opacity = 1.0;
            for (var at = start; at + 6 <= end;)
            {
                var id = BitConverter.ToUInt16(data, at);
                var length = (int)BitConverter.ToUInt32(data, at + 2);
                if (length < 6)
                    break;
                if (id == MaterialName)
                    materialName = ReadCString(at + 6).Text;
                else if (id == Diffuse && ReadColour(at + 6, at + length) is { } c)
                    colour = c;
                else if (id == Transparency && BitConverter.ToUInt16(data, at + 6) == Percent)
                    opacity = 1 - BitConverter.ToUInt16(data, at + 12) / 100.0;
                at += length;
            }
            if (materialName != null)
                materials[materialName] = new Material { Name = materialName, Color = colour, Opacity = Math.Clamp(opacity, 0, 1) };
        }

        Rgba? ReadColour(int start, int end)
        {
            for (var at = start; at + 6 <= end;)
            {
                var id = BitConverter.ToUInt16(data, at);
                var length = (int)BitConverter.ToUInt32(data, at + 2);
                if (length < 6)
                    break;
                if (id == Rgb24)
                    return new Rgba(data[at + 6], data[at + 7], data[at + 8]);
                if (id == RgbFloat)
                {
                    byte B(int o) => (byte)Math.Round(Math.Clamp(BitConverter.ToSingle(data, at + 6 + 4 * o), 0, 1) * 255);
                    return new Rgba(B(0), B(1), B(2));
                }
                at += length;
            }
            return null;
        }

        void ReadObject(string objectName, int start, int end)
        {
            for (var at = start; at + 6 <= end;)
            {
                var id = BitConverter.ToUInt16(data, at);
                var length = (int)BitConverter.ToUInt32(data, at + 2);
                if (length < 6)
                    break;
                if (id == TriMesh)
                {
                    var vertices = new List<Vec3>();
                    var faces = new List<(int, int, int)>();
                    var faceMaterial = new Dictionary<int, string>();
                    for (var m = at + 6; m + 6 <= at + length;)
                    {
                        var sub = BitConverter.ToUInt16(data, m);
                        var subLength = (int)BitConverter.ToUInt32(data, m + 2);
                        if (subLength < 6)
                            break;
                        if (sub == Vertices)
                        {
                            int n = BitConverter.ToUInt16(data, m + 6);
                            for (var i = 0; i < n; i++)
                                vertices.Add(new Vec3(BitConverter.ToSingle(data, m + 8 + 12 * i), BitConverter.ToSingle(data, m + 12 + 12 * i), BitConverter.ToSingle(data, m + 16 + 12 * i)));
                        }
                        else if (sub == Faces)
                        {
                            int n = BitConverter.ToUInt16(data, m + 6);
                            for (var i = 0; i < n; i++)
                                faces.Add((BitConverter.ToUInt16(data, m + 8 + 8 * i), BitConverter.ToUInt16(data, m + 10 + 8 * i), BitConverter.ToUInt16(data, m + 12 + 8 * i)));
                            for (var f = m + 8 + 8 * n; f + 6 <= m + subLength;)
                            {
                                var fid = BitConverter.ToUInt16(data, f);
                                var fLength = (int)BitConverter.ToUInt32(data, f + 2);
                                if (fLength < 6)
                                    break;
                                if (fid == FaceMaterials)
                                {
                                    var (materialName, after) = ReadCString(f + 6);
                                    int count = BitConverter.ToUInt16(data, after);
                                    for (var i = 0; i < count; i++)
                                        faceMaterial[BitConverter.ToUInt16(data, after + 2 + 2 * i)] = materialName;
                                }
                                f += fLength;
                            }
                        }
                        m += subLength;
                    }
                    objects.Add((objectName, vertices, faces, faceMaterial));
                }
                at += length;
            }
        }

        (string Text, int After) ReadCString(int at)
        {
            var end = Array.IndexOf(data, (byte)0, at);
            if (end < 0)
                end = data.Length;
            return (Encoding.Latin1.GetString(data, at, end - at), end + 1);
        }

        Walk(0, data.Length);
        foreach (var (objectName, vertices, faces, faceMaterial) in objects.Where(o => o.Faces.Count > 0))
        {
            var def = new ComponentDefinition { Name = objectName, IsGroup = true };
            var polygons = faces.Select((f, i) => (new List<Vec3> { vertices[f.A] * scale, vertices[f.B] * scale, vertices[f.C] * scale },
                (object?)(faceMaterial.TryGetValue(i, out var mn) && materials.TryGetValue(mn, out var m) ? m : null))).ToList();
            foreach (var (face, front, back) in MeshImport.AddMerged(def.Entities, polygons))
                (face.FrontMaterial, face.BackMaterial) = ((Material?)front, (Material?)back);
            model.Definitions.Add(def);
            model.Entities.AddInstance(def, Transform.Identity).Name = objectName;
        }
        foreach (var m in model.Definitions.SelectMany(d => d.Entities.Faces).SelectMany(f => new[] { f.FrontMaterial, f.BackMaterial }).OfType<Material>().Distinct())
            model.Materials.Add(m);
        if (model.Definitions.Count == 0)
            throw new InvalidDataException($"no meshes in {name}");
        return model;
    }

    private static byte[] Chunk(ushort id, params byte[][] parts)
    {
        var body = parts.SelectMany(p => p).ToArray();
        var chunk = new byte[6 + body.Length];
        BitConverter.GetBytes(id).CopyTo(chunk, 0);
        BitConverter.GetBytes((uint)chunk.Length).CopyTo(chunk, 2);
        body.CopyTo(chunk, 6);
        return chunk;
    }

    private static byte[] CString(string s) => [.. Encoding.Latin1.GetBytes(s), 0];

    private static byte[] Bytes(BinaryWriter w)
    {
        w.Flush();
        return ((MemoryStream)w.BaseStream).ToArray();
    }
}
