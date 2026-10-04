using System.IO.Compression;
using System.Text.Json;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Dogeometric.Core.View;

namespace Dogeometric.Core.IO;

/// <summary>
/// Dogeometric's native <c>.dog</c> format: a ZIP container holding <c>manifest.json</c>, <c>model.json</c> and
/// <c>textures/*</c>. Geometry is stored as compact index arrays (see docs/dog-format.md). Lengths are millimetres.
/// </summary>
public static class DogFile
{
    public const string Format = "dogeometric";
    public const int Version = 1;

    public static void Save(Model model, string path)
    {
        using var stream = File.Create(path);
        Save(model, stream);
    }

    public static void Save(Model model, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(zip, "manifest.json", w =>
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", Version);
            w.WriteString("generator", "Dogeometric");
            w.WriteEndObject();
        });

        var materialIndex = model.Materials.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        var tagIndex = model.Tags.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        var defIndex = model.Definitions.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        var textures = new List<(string Name, byte[] Data)>();
        WriteEntry(zip, "model.json", w =>
        {
            w.WriteStartObject();
            w.WriteString("units", model.Units.ToString());
            w.WriteNumber("unitPrecision", model.UnitPrecision);
            if (model.Axes != Transform.Identity)
            {
                w.WriteStartArray("axes");
                foreach (var v in model.Axes.ToColumnMajor())
                    w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            w.WriteString("sourceVersion", model.SourceVersion);

            w.WriteStartArray("materials");
            foreach (var m in model.Materials)
            {
                w.WriteStartObject();
                w.WriteString("name", m.Name);
                WriteColor(w, "color", m.Color);
                w.WriteNumber("opacity", m.Opacity);
                if (m.Texture is { } tex)
                {
                    var entryName = $"textures/{textures.Count}{Path.GetExtension(tex.FileName)}";
                    textures.Add((entryName, tex.Data));
                    w.WriteStartObject("texture");
                    w.WriteString("file", entryName);
                    w.WriteString("name", tex.FileName);
                    w.WriteNumber("width", tex.WidthMm);
                    w.WriteNumber("height", tex.HeightMm);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("tags");
            foreach (var t in model.Tags)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                WriteColor(w, "color", t.Color);
                w.WriteBoolean("visible", t.Visible);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("definitions");
            foreach (var d in model.Definitions)
            {
                w.WriteStartObject();
                w.WriteString("name", d.Name);
                if (d.Description.Length > 0)
                    w.WriteString("description", d.Description);
                w.WriteBoolean("group", d.IsGroup);
                if (d.IsImage)
                    w.WriteBoolean("image", true);
                if (d.AlwaysFaceCamera)
                    w.WriteBoolean("alwaysFaceCamera", true);
                if (d.ShadowsFaceSun)
                    w.WriteBoolean("shadowsFaceSun", true);
                WriteEntities(w, "entities", d.Entities, materialIndex, tagIndex, defIndex);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            WriteEntities(w, "entities", model.Entities, materialIndex, tagIndex, defIndex);

            w.WriteStartArray("scenes");
            foreach (var s in model.Scenes)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                if (s.Camera is { } c)
                {
                    w.WriteStartObject("camera");
                    WriteVec(w, "eye", c.Eye);
                    WriteVec(w, "target", c.Target);
                    WriteVec(w, "up", c.Up);
                    w.WriteBoolean("perspective", c.Perspective);
                    w.WriteNumber("fov", c.FovDegrees);
                    w.WriteNumber("orthoHeight", c.OrthoHeight);
                    w.WriteEndObject();
                }
                w.WriteStartArray("hiddenTags");
                foreach (var t in s.HiddenTags)
                    w.WriteStringValue(t);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        });

        foreach (var (name, data) in textures)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var es = entry.Open();
            es.Write(data);
        }
    }

    // Entities layout:
    //   vertices: [x, y, z, ...]
    //   edges:    [[start, end, flags, tag, material], ...]          (-1 = none)
    //   faces:    [[loops, front, back, tag, hidden], ...]  loops = [[signed edge refs], ...], ref = ±(edge + 1)
    //   instances:[{def, transform[16 column-major], name, tag, material, hidden, locked}, ...]
    private static void WriteEntities(Utf8JsonWriter w, string name, Entities e,
        Dictionary<Material, int> mats, Dictionary<Tag, int> tags, Dictionary<ComponentDefinition, int> defs)
    {
        var vIndex = new Dictionary<Vertex, int>(e.Vertices.Count);
        var eIndex = new Dictionary<Edge, int>(e.Edges.Count);

        w.WriteStartObject(name);
        w.WriteStartArray("vertices");
        foreach (var v in e.Vertices)
        {
            vIndex[v] = vIndex.Count;
            w.WriteNumberValue(v.Position.X);
            w.WriteNumberValue(v.Position.Y);
            w.WriteNumberValue(v.Position.Z);
        }
        w.WriteEndArray();

        w.WriteStartArray("edges");
        foreach (var edge in e.Edges)
        {
            eIndex[edge] = eIndex.Count;
            w.WriteStartArray();
            w.WriteNumberValue(vIndex[edge.Start]);
            w.WriteNumberValue(vIndex[edge.End]);
            w.WriteNumberValue((int)edge.Flags);
            w.WriteNumberValue(Ref(tags, edge.Tag));
            w.WriteNumberValue(Ref(mats, edge.Material));
            w.WriteEndArray();
        }
        w.WriteEndArray();

        w.WriteStartArray("faces");
        foreach (var f in e.Faces)
        {
            w.WriteStartArray();
            w.WriteStartArray();
            foreach (var loop in f.Loops)
            {
                w.WriteStartArray();
                foreach (var (edge, reversed) in loop.Edges)
                    w.WriteNumberValue(reversed ? -(eIndex[edge] + 1) : eIndex[edge] + 1);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteNumberValue(Ref(mats, f.FrontMaterial));
            w.WriteNumberValue(Ref(mats, f.BackMaterial));
            w.WriteNumberValue(Ref(tags, f.Tag));
            w.WriteNumberValue(f.Hidden ? 1 : 0);
            // Optional: positioned textures for the front and back (SketchUp's 3×3 UV matrices).
            if (f.FrontMapping != null || f.BackMapping != null)
            {
                foreach (var mapping in new[] { f.FrontMapping, f.BackMapping })
                {
                    if (mapping == null)
                    {
                        w.WriteNullValue();
                        continue;
                    }
                    w.WriteStartArray();
                    foreach (var x in mapping.Matrix)
                        w.WriteNumberValue(x);
                    w.WriteEndArray();
                }
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();

        w.WriteStartArray("instances");
        foreach (var inst in e.Instances)
        {
            w.WriteStartObject();
            w.WriteNumber("def", defs[inst.Definition]);
            w.WriteStartArray("transform");
            foreach (var x in inst.Transform.ToColumnMajor())
                w.WriteNumberValue(x);
            w.WriteEndArray();
            if (inst.Name.Length > 0)
                w.WriteString("name", inst.Name);
            w.WriteNumber("tag", Ref(tags, inst.Tag));
            w.WriteNumber("material", Ref(mats, inst.Material));
            if (inst.Hidden)
                w.WriteBoolean("hidden", true);
            if (inst.Locked)
                w.WriteBoolean("locked", true);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        if (e.GuideLines.Count > 0)
        {
            w.WriteStartArray("guideLines");
            foreach (var g in e.GuideLines)
            {
                w.WriteStartObject();
                WriteVec(w, "point", g.Point);
                WriteVec(w, "direction", g.Direction);
                if (g.Start is { } s && g.End is { } en)
                {
                    WriteVec(w, "start", s);
                    WriteVec(w, "end", en);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (e.Dimensions.Count > 0)
        {
            w.WriteStartArray("dimensions");
            foreach (var d in e.Dimensions)
            {
                w.WriteStartObject();
                WriteVec(w, "start", d.Start);
                WriteVec(w, "end", d.End);
                WriteVec(w, "offset", d.Offset);
                if (d.Text.Length > 0)
                    w.WriteString("text", d.Text);
                w.WriteNumber("tag", Ref(tags, d.Tag));
                if (d.Hidden)
                    w.WriteBoolean("hidden", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (e.SectionPlanes.Count > 0)
        {
            w.WriteStartArray("sectionPlanes");
            foreach (var s in e.SectionPlanes)
            {
                w.WriteStartObject();
                WriteVec(w, "point", s.Point);
                WriteVec(w, "normal", s.Normal);
                if (s.Name.Length > 0)
                    w.WriteString("name", s.Name);
                if (s == e.ActiveSection)
                    w.WriteBoolean("active", true);
                w.WriteNumber("tag", Ref(tags, s.Tag));
                if (s.Hidden)
                    w.WriteBoolean("hidden", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (e.Texts.Count > 0)
        {
            w.WriteStartArray("texts");
            foreach (var t in e.Texts)
            {
                w.WriteStartObject();
                w.WriteString("text", t.Text);
                if (t.ScreenPosition is { } sp)
                {
                    w.WriteStartArray("screen");
                    w.WriteNumberValue(sp.X);
                    w.WriteNumberValue(sp.Y);
                    w.WriteEndArray();
                }
                else
                {
                    WriteVec(w, "point", t.Point);
                    WriteVec(w, "offset", t.Offset);
                }
                w.WriteNumber("tag", Ref(tags, t.Tag));
                if (t.Hidden)
                    w.WriteBoolean("hidden", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (e.GuidePoints.Count > 0)
        {
            w.WriteStartArray("guidePoints");
            foreach (var g in e.GuidePoints)
            {
                w.WriteNumberValue(g.Position.X);
                w.WriteNumberValue(g.Position.Y);
                w.WriteNumberValue(g.Position.Z);
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static Model Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static Model Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        using (var manifest = ReadJson(zip, "manifest.json"))
        {
            var root = manifest.RootElement;
            if (root.GetProperty("format").GetString() != Format)
                throw new InvalidDataException("Not a Dogeometric file");
            var version = root.GetProperty("version").GetInt32();
            if (version > Version)
                throw new InvalidDataException($"File version {version} is newer than this Dogeometric ({Version})");
        }

        using var doc = ReadJson(zip, "model.json");
        var r = doc.RootElement;
        var model = new Model
        {
            Units = Enum.TryParse<LengthUnit>(r.GetProperty("units").GetString(), out var u) ? u : LengthUnit.Millimeters,
            UnitPrecision = r.GetProperty("unitPrecision").GetInt32(),
            SourceVersion = r.TryGetProperty("sourceVersion", out var sv) ? sv.GetString() ?? "" : "",
        };
        if (r.TryGetProperty("axes", out var axes))
            model.Axes = Transform.FromColumnMajor(axes.EnumerateArray().Select(x => x.GetDouble()).ToArray());

        foreach (var m in r.GetProperty("materials").EnumerateArray())
        {
            var mat = new Material
            {
                Name = m.GetProperty("name").GetString() ?? "",
                Color = ReadColor(m.GetProperty("color")),
                Opacity = m.GetProperty("opacity").GetDouble(),
            };
            if (m.TryGetProperty("texture", out var t))
            {
                var entry = zip.GetEntry(t.GetProperty("file").GetString()!)
                    ?? throw new InvalidDataException("Missing texture data");
                using var es = entry.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                mat.Texture = new TextureImage
                {
                    FileName = t.GetProperty("name").GetString() ?? "",
                    Data = ms.ToArray(),
                    WidthMm = t.GetProperty("width").GetDouble(),
                    HeightMm = t.GetProperty("height").GetDouble(),
                };
            }
            model.Materials.Add(mat);
        }

        model.Tags.Clear();
        foreach (var t in r.GetProperty("tags").EnumerateArray())
        {
            model.Tags.Add(new Tag
            {
                Name = t.GetProperty("name").GetString() ?? "",
                Color = ReadColor(t.GetProperty("color")),
                Visible = t.GetProperty("visible").GetBoolean(),
            });
        }
        if (model.Tags.Count == 0)
            model.Tags.Add(new Tag { Name = Tag.UntaggedName });

        // Definitions first (instances refer to them by index), then their contents.
        var defsJson = r.GetProperty("definitions").EnumerateArray().ToList();
        foreach (var d in defsJson)
        {
            model.Definitions.Add(new ComponentDefinition
            {
                Name = d.GetProperty("name").GetString() ?? "",
                Description = d.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                IsGroup = d.GetProperty("group").GetBoolean(),
                IsImage = d.TryGetProperty("image", out var img) && img.GetBoolean(),
                AlwaysFaceCamera = d.TryGetProperty("alwaysFaceCamera", out var fc) && fc.GetBoolean(),
                ShadowsFaceSun = d.TryGetProperty("shadowsFaceSun", out var sfs) && sfs.GetBoolean(),
            });
        }
        for (var i = 0; i < defsJson.Count; i++)
            ReadEntities(defsJson[i].GetProperty("entities"), model.Definitions[i].Entities, model);
        ReadEntities(r.GetProperty("entities"), model.Entities, model);

        foreach (var s in r.GetProperty("scenes").EnumerateArray())
        {
            var scene = new Scene { Name = s.GetProperty("name").GetString() ?? "" };
            if (s.TryGetProperty("camera", out var c))
            {
                scene.Camera = new CameraState(
                    ReadVec(c.GetProperty("eye")), ReadVec(c.GetProperty("target")), ReadVec(c.GetProperty("up")),
                    c.GetProperty("perspective").GetBoolean(), c.GetProperty("fov").GetDouble(), c.GetProperty("orthoHeight").GetDouble());
            }
            foreach (var t in s.GetProperty("hiddenTags").EnumerateArray())
                scene.HiddenTags.Add(t.GetString() ?? "");
            model.Scenes.Add(scene);
        }
        return model;
    }

    private static void ReadEntities(JsonElement j, Entities e, Model model)
    {
        var coords = j.GetProperty("vertices");
        var n = coords.GetArrayLength() / 3;
        var it = coords.EnumerateArray();
        for (var i = 0; i < n; i++)
        {
            it.MoveNext();
            var x = it.Current.GetDouble();
            it.MoveNext();
            var y = it.Current.GetDouble();
            it.MoveNext();
            var z = it.Current.GetDouble();
            e.AddVertex(new Vec3(x, y, z));
        }

        foreach (var ej in j.GetProperty("edges").EnumerateArray())
        {
            var a = ej.EnumerateArray().ToArray();
            var edge = e.AddEdge(e.Vertices[a[0].GetInt32()], e.Vertices[a[1].GetInt32()]);
            edge.Flags = (EdgeFlags)a[2].GetInt32();
            edge.Tag = TagAt(model, a[3].GetInt32());
            edge.Material = MaterialAt(model, a[4].GetInt32());
        }

        foreach (var fj in j.GetProperty("faces").EnumerateArray())
        {
            var a = fj.EnumerateArray().ToArray();
            var face = new Face
            {
                FrontMaterial = MaterialAt(model, a[1].GetInt32()),
                BackMaterial = MaterialAt(model, a[2].GetInt32()),
                Tag = TagAt(model, a[3].GetInt32()),
                Hidden = a[4].GetInt32() != 0,
                FrontMapping = a.Length > 5 ? ReadMapping(a[5]) : null,
                BackMapping = a.Length > 6 ? ReadMapping(a[6]) : null,
            };
            foreach (var lj in a[0].EnumerateArray())
            {
                var loop = new FaceLoop();
                foreach (var refJ in lj.EnumerateArray())
                {
                    var r = refJ.GetInt32();
                    loop.Edges.Add((e.Edges[Math.Abs(r) - 1], r < 0));
                }
                face.Loops.Add(loop);
            }
            e.Faces.Add(face);
        }

        foreach (var ij in j.GetProperty("instances").EnumerateArray())
        {
            var matrix = ij.GetProperty("transform").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var inst = e.AddInstance(model.Definitions[ij.GetProperty("def").GetInt32()], Transform.FromColumnMajor(matrix));
            inst.Name = ij.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            inst.Tag = TagAt(model, ij.GetProperty("tag").GetInt32());
            inst.Material = MaterialAt(model, ij.GetProperty("material").GetInt32());
            inst.Hidden = ij.TryGetProperty("hidden", out var h) && h.GetBoolean();
            inst.Locked = ij.TryGetProperty("locked", out var l) && l.GetBoolean();
        }

        if (j.TryGetProperty("guideLines", out var guides))
        {
            foreach (var g in guides.EnumerateArray())
            {
                var line = new GuideLine(ReadVec(g.GetProperty("point")), ReadVec(g.GetProperty("direction")));
                if (g.TryGetProperty("start", out var s) && g.TryGetProperty("end", out var en))
                {
                    line.Start = ReadVec(s);
                    line.End = ReadVec(en);
                }
                e.GuideLines.Add(line);
            }
        }
        if (j.TryGetProperty("dimensions", out var dims))
        {
            foreach (var d in dims.EnumerateArray())
            {
                e.Dimensions.Add(new LinearDimension(ReadVec(d.GetProperty("start")), ReadVec(d.GetProperty("end")), ReadVec(d.GetProperty("offset")))
                {
                    Text = d.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "",
                    Tag = TagAt(model, d.GetProperty("tag").GetInt32()),
                    Hidden = d.TryGetProperty("hidden", out var h) && h.GetBoolean(),
                });
            }
        }
        if (j.TryGetProperty("sectionPlanes", out var sections))
        {
            foreach (var s in sections.EnumerateArray())
            {
                var plane = new SectionPlane(ReadVec(s.GetProperty("point")), ReadVec(s.GetProperty("normal")))
                {
                    Name = s.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    Tag = TagAt(model, s.GetProperty("tag").GetInt32()),
                    Hidden = s.TryGetProperty("hidden", out var h) && h.GetBoolean(),
                };
                e.SectionPlanes.Add(plane);
                if (s.TryGetProperty("active", out var act) && act.GetBoolean())
                    e.ActiveSection = plane;
            }
        }
        if (j.TryGetProperty("texts", out var texts))
        {
            foreach (var t in texts.EnumerateArray())
            {
                var label = new TextLabel(t.GetProperty("text").GetString() ?? "")
                {
                    Tag = TagAt(model, t.GetProperty("tag").GetInt32()),
                    Hidden = t.TryGetProperty("hidden", out var h) && h.GetBoolean(),
                };
                if (t.TryGetProperty("screen", out var sp))
                {
                    label.ScreenPosition = (sp[0].GetDouble(), sp[1].GetDouble());
                }
                else
                {
                    label.Point = ReadVec(t.GetProperty("point"));
                    label.Offset = ReadVec(t.GetProperty("offset"));
                }
                e.Texts.Add(label);
            }
        }
        if (j.TryGetProperty("guidePoints", out var gp))
        {
            var it2 = gp.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            for (var i = 0; i + 2 < it2.Length; i += 3)
                e.GuidePoints.Add(new GuidePoint(new Vec3(it2[i], it2[i + 1], it2[i + 2])));
        }
    }

    private static int Ref<T>(Dictionary<T, int> index, T? item) where T : class =>
        item != null && index.TryGetValue(item, out var i) ? i : -1;

    private static Material? MaterialAt(Model m, int i) => i >= 0 && i < m.Materials.Count ? m.Materials[i] : null;

    private static TextureMapping? ReadMapping(JsonElement j) =>
        j.ValueKind == JsonValueKind.Array ? new TextureMapping(j.EnumerateArray().Select(x => x.GetDouble()).ToArray()) : null;

    private static Tag? TagAt(Model m, int i) => i >= 0 && i < m.Tags.Count ? m.Tags[i] : null;

    private static void WriteEntry(ZipArchive zip, string name, Action<Utf8JsonWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = new Utf8JsonWriter(s);
        write(w);
    }

    private static JsonDocument ReadJson(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Missing {name}");
        using var s = entry.Open();
        return JsonDocument.Parse(s, new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static void WriteColor(Utf8JsonWriter w, string name, Rgba c)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(c.R);
        w.WriteNumberValue(c.G);
        w.WriteNumberValue(c.B);
        w.WriteNumberValue(c.A);
        w.WriteEndArray();
    }

    private static Rgba ReadColor(JsonElement j)
    {
        var a = j.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
        return new Rgba(a[0], a[1], a[2], a.Length > 3 ? a[3] : (byte)255);
    }

    private static void WriteVec(Utf8JsonWriter w, string name, Vec3 v)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(v.X);
        w.WriteNumberValue(v.Y);
        w.WriteNumberValue(v.Z);
        w.WriteEndArray();
    }

    private static Vec3 ReadVec(JsonElement j)
    {
        var a = j.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        return new Vec3(a[0], a[1], a[2]);
    }
}
