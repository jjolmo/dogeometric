using System.Globalization;
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

    /// <param name="thumbnail">A PNG preview of the view, shown for the file in component collections.</param>
    public static void Save(Model model, string path, byte[]? thumbnail = null)
    {
        using var stream = File.Create(path);
        Save(model, stream, thumbnail);
    }

    public static void Save(Model model, Stream stream) => Save(model, stream, null);

    public static void Save(Model model, Stream stream, byte[]? thumbnail)
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
            w.WriteString("unitFormat", model.UnitFormat.ToString());
            w.WriteBoolean("showUnitSymbol", model.ShowUnitSymbol);
            w.WriteBoolean("forceZeroFeet", model.ForceZeroFeet);
            if (model.Axes != Transform.Identity)
            {
                w.WriteStartArray("axes");
                foreach (var v in model.Axes.ToColumnMajor())
                    w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            w.WriteString("sourceVersion", model.SourceVersion);
            WriteShadows(w, model.Shadows);
            w.WriteBoolean("sectionFill", model.ShowSectionFill);
            WriteStyle(w, model.Style);
            w.WriteStartObject("options");
            w.WriteString("author", model.Options.Author);
            w.WriteString("name", model.Options.Name);
            w.WriteString("glueTo", model.Options.GlueTo.ToString());
            w.WriteBoolean("cutsOpening", model.Options.CutsOpening);
            w.WriteBoolean("alwaysFaceCamera", model.Options.AlwaysFaceCamera);
            w.WriteBoolean("shadowsFaceSun", model.Options.ShadowsFaceSun);
            w.WriteString("description", model.Options.Description);
            w.WriteNumber("fadeRest", model.Options.FadeRest);
            w.WriteNumber("fadeSimilar", model.Options.FadeSimilar);
            w.WriteBoolean("componentAxes", model.Options.ShowComponentAxes);
            w.WriteBoolean("smoothTextures", model.Options.SmoothTextures);
            w.WriteBoolean("colorByTag", model.Options.ColorByTag);
            w.WriteNumber("anglePrecision", model.Options.AnglePrecision);
            w.WriteBoolean("angleSnapping", model.Options.AngleSnapping);
            w.WriteNumber("angleSnap", model.Options.AngleSnap);
            w.WriteBoolean("lengthSnapping", model.Options.LengthSnapping);
            w.WriteNumber("lengthSnap", model.Options.LengthSnap);
            w.WriteNumber("fogStart", model.Options.FogStart);
            w.WriteNumber("fogEnd", model.Options.FogEnd);
            if (model.Options.FogColor is { } fog)
                w.WriteString("fogColor", $"{fog.R},{fog.G},{fog.B}");
            w.WriteEndObject();
            w.WriteStartObject("annotation");
            WriteDimensionStyle(w, "dimensionStyle", model.Dimensions);
            w.WriteBoolean("hideForeshortened", model.DimensionDisplay.HideForeshortened);
            w.WriteNumber("foreshortenedLimit", model.DimensionDisplay.ForeshortenedLimit);
            w.WriteBoolean("hideSmall", model.DimensionDisplay.HideSmall);
            w.WriteNumber("smallPixels", model.DimensionDisplay.SmallPixels);
            WriteTextStyle(w, "screenText", model.ScreenText);
            WriteTextStyle(w, "leaderText", model.LeaderText);
            w.WriteEndObject();
            w.WriteStartObject("animation");
            w.WriteBoolean("transitions", model.SceneTransitions);
            w.WriteNumber("transitionSeconds", model.SceneTransitionSeconds);
            w.WriteNumber("delaySeconds", model.SceneDelaySeconds);
            w.WriteEndObject();

            w.WriteStartArray("materials");
            foreach (var m in model.Materials)
            {
                w.WriteStartObject();
                w.WriteString("name", m.Name);
                WriteColor(w, "color", m.Color);
                w.WriteNumber("opacity", m.Opacity);
                if (m.Colorize)
                    w.WriteBoolean("colorize", true);
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
                w.WriteString("dashes", LineStyles.Name(t.Dashes));
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
                if (d.IfcType.Length > 0)
                    w.WriteString("ifcType", d.IfcType);
                if (d.SchemaTypes.Count > 0)
                {
                    w.WriteStartObject("schemaTypes");
                    foreach (var (schema, type) in d.SchemaTypes)
                        w.WriteString(schema, type);
                    w.WriteEndObject();
                }
                w.WriteBoolean("group", d.IsGroup);
                if (d.IsImage)
                    w.WriteBoolean("image", true);
                if (d.AlwaysFaceCamera)
                    w.WriteBoolean("alwaysFaceCamera", true);
                if (d.ShadowsFaceSun)
                    w.WriteBoolean("shadowsFaceSun", true);
                if (d.GlueTo != GlueTo.None)
                    w.WriteString("glueTo", d.GlueTo.ToString());
                if (d.Attributes.Count > 0)
                {
                    w.WriteStartArray("attributes");
                    foreach (var a in d.Attributes)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", a.Name);
                        w.WriteString("value", a.Value);
                        if (a.UserCanEdit)
                            w.WriteBoolean("userCanEdit", true);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                if (d.Camera is { } cam)
                {
                    w.WriteStartObject("camera");
                    w.WriteNumber("fov", cam.FovDegrees);
                    w.WriteNumber("aspect", cam.Aspect);
                    w.WriteEndObject();
                }
                if (d.CutsOpening)
                    w.WriteBoolean("cutsOpening", true);
                WriteEntities(w, "entities", d.Entities, materialIndex, tagIndex, defIndex);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            WriteEntities(w, "entities", model.Entities, materialIndex, tagIndex, defIndex);

            if (model.Schemas.Count > 0)
            {
                w.WriteStartArray("schemas");
                foreach (var schema in model.Schemas)
                {
                    w.WriteStartObject();
                    w.WriteString("name", schema.Name);
                    w.WriteString("description", schema.Description);
                    w.WriteStartObject("types");
                    foreach (var (type, attributes) in schema.Types)
                    {
                        w.WriteStartArray(type);
                        foreach (var a in attributes)
                            w.WriteStringValue(a);
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            w.WriteStartArray("scenes");
            foreach (var s in model.Scenes)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                if (s.Description.Length > 0)
                    w.WriteString("description", s.Description);
                if (!s.InAnimation)
                    w.WriteBoolean("inAnimation", false);
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
                if (s.Saves != SceneProperties.All)
                    w.WriteString("saves", s.Saves.ToString());
                if (s.Style is { } style)
                    WriteStyle(w, style);
                if (s.Shadows is { } shadows)
                    WriteShadows(w, shadows);
                if (s.Axes is { } axes)
                {
                    w.WriteStartArray("axes");
                    foreach (var v in axes.ToColumnMajor())
                        w.WriteNumberValue(v);
                    w.WriteEndArray();
                }
                w.WriteNumber("activeSection", s.ActiveSection is { } section ? model.Entities.SectionPlanes.IndexOf(section) : -1);
                if (s.Hidden.Count > 0)
                {
                    // Each hidden entity as [owner (-1 the model, else a definition), kind (0 instance, 1 face, 2 edge), index].
                    w.WriteStartArray("hidden");
                    for (var owner = -1; owner < model.Definitions.Count; owner++)
                    {
                        var e = owner < 0 ? model.Entities : model.Definitions[owner].Entities;
                        foreach (var (list, kind) in new (System.Collections.IList, int)[] { (e.Instances, 0), (e.Faces, 1), (e.Edges, 2) })
                            for (var i = 0; i < list.Count; i++)
                                if (s.Hidden.Contains(list[i]!))
                                {
                                    w.WriteStartArray();
                                    w.WriteNumberValue(owner);
                                    w.WriteNumberValue(kind);
                                    w.WriteNumberValue(i);
                                    w.WriteEndArray();
                                }
                    }
                    w.WriteEndArray();
                }
                if (s.Photo is { } p)
                {
                    w.WriteStartObject("photo");
                    w.WriteString("name", p.Name);
                    w.WriteBase64String("image", p.Image);
                    w.WriteNumber("width", p.Width);
                    w.WriteNumber("height", p.Height);
                    w.WriteStartArray("lines");
                    foreach (var (a, b) in p.Red.Concat(p.Green))
                        foreach (var v in new[] { a.X, a.Y, b.X, b.Y })
                            w.WriteNumberValue(v);
                    w.WriteEndArray();
                    w.WriteStartArray("origin");
                    w.WriteNumberValue(p.Origin.X);
                    w.WriteNumberValue(p.Origin.Y);
                    w.WriteEndArray();
                    w.WriteNumber("distance", p.Distance);
                    w.WriteEndObject();
                }
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
        if (thumbnail != null)
        {
            using var ts = zip.CreateEntry(ThumbnailEntry, CompressionLevel.NoCompression).Open();
            ts.Write(thumbnail);
        }
    }

    private const string ThumbnailEntry = "thumbnail.png";

    /// <summary>The PNG preview saved with the file, or null.</summary>
    public static byte[]? ReadThumbnail(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(ThumbnailEntry) is not { } entry)
                return null;
            using var s = entry.Open();
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            return copy.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Entities layout:
    //   vertices: [x, y, z, ...]
    //   edges:    [[start, end, flags, tag, material, curve?], ...]  (-1 = none)
    //   curves:   [{center, normal, radius, segments, polygon, spline?}, ...] (optional; edges refer to them)
    //   faces:    [[loops, front, back, tag, hidden], ...]  loops = [[signed edge refs], ...], ref = ±(edge + 1)
    //   instances:[{def, transform[16 column-major], name, tag, material, hidden, locked, gluedTo (face index)}, ...]
    private static string RgbaText(Rgba c) => $"{c.R},{c.G},{c.B}";

    private static Rgba ReadRgba(JsonElement e, string name, Rgba fallback) =>
        e.TryGetProperty(name, out var v) && v.GetString()?.Split(',') is [var r, var g, var b]
            ? new Rgba(byte.Parse(r), byte.Parse(g), byte.Parse(b)) : fallback;

    private static void WriteStyle(Utf8JsonWriter w, StyleSettings s)
    {
        w.WriteStartObject("style");
        w.WriteString("name", s.Name);
        w.WriteString("description", s.Description);
        w.WriteBoolean("edges", s.Edges);
        w.WriteBoolean("backEdges", s.BackEdges);
        w.WriteBoolean("profiles", s.Profiles);
        w.WriteBoolean("depthCue", s.DepthCue);
        w.WriteBoolean("extension", s.Extension);
        w.WriteString("faceStyle", s.FaceStyle.ToString());
        w.WriteBoolean("hiddenGeometry", s.HiddenGeometry);
        w.WriteBoolean("guides", s.Guides);
        w.WriteBoolean("modelAxes", s.ModelAxes);
        w.WriteBoolean("sectionPlanes", s.SectionPlanes);
        w.WriteBoolean("sectionCuts", s.SectionCuts);
        w.WriteNumber("profileWidth", s.ProfileWidth);
        w.WriteNumber("depthCueWidth", s.DepthCueWidth);
        w.WriteNumber("extensionLength", s.ExtensionLength);
        w.WriteBoolean("endpoints", s.Endpoints);
        w.WriteNumber("endpointLength", s.EndpointLength);
        w.WriteBoolean("jitter", s.Jitter);
        w.WriteBoolean("dashes", s.Dashes);
        w.WriteString("edgeColorMode", s.EdgeColorMode.ToString());
        w.WriteString("edgeColor", RgbaText(s.EdgeColor));
        w.WriteString("frontColor", RgbaText(s.FrontColor));
        w.WriteString("backColor", RgbaText(s.BackColor));
        w.WriteNumber("xrayOpacity", s.XrayOpacity);
        w.WriteBoolean("transparency", s.Transparency);
        w.WriteString("transparencyQuality", s.TransparencyQuality.ToString());
        w.WriteString("backgroundColor", RgbaText(s.BackgroundColor));
        w.WriteBoolean("sky", s.Sky);
        w.WriteString("skyColor", RgbaText(s.SkyColor));
        w.WriteBoolean("ground", s.Ground);
        w.WriteString("groundColor", RgbaText(s.GroundColor));
        w.WriteNumber("groundTransparency", s.GroundTransparency);
        w.WriteBoolean("groundFromBelow", s.GroundFromBelow);
        w.WriteString("selectedColor", RgbaText(s.SelectedColor));
        w.WriteString("lockedColor", RgbaText(s.LockedColor));
        w.WriteString("guideColor", RgbaText(s.GuideColor));
        w.WriteString("activeSectionColor", RgbaText(s.ActiveSectionColor));
        w.WriteString("inactiveSectionColor", RgbaText(s.InactiveSectionColor));
        w.WriteString("sectionCutColor", RgbaText(s.SectionCutColor));
        w.WriteString("sectionFillColor", RgbaText(s.SectionFillColor));
        w.WriteNumber("sectionCutWidth", s.SectionCutWidth);
        w.WriteBoolean("showWatermarks", s.ShowWatermarks);
        w.WriteStartArray("watermarks");
        foreach (var m in s.Watermarks)
        {
            w.WriteStartObject();
            w.WriteString("name", m.Name);
            w.WriteString("file", m.Image.FileName);
            w.WriteBase64String("image", m.Image.Data);
            w.WriteBoolean("overlay", m.Overlay);
            w.WriteBoolean("visible", m.Visible);
            w.WriteNumber("opacity", m.Opacity);
            w.WriteBoolean("mask", m.Mask);
            w.WriteString("layout", m.Layout.ToString());
            w.WriteBoolean("lockAspect", m.LockAspect);
            w.WriteNumber("scale", m.Scale);
            w.WriteString("position", m.Position.ToString());
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static StyleSettings ReadStyle(JsonElement j)
    {
        var d = new StyleSettings();
        int Int(string n, int f) => j.TryGetProperty(n, out var v) ? v.GetInt32() : f;
        bool Bool(string n, bool f) => j.TryGetProperty(n, out var v) ? v.GetBoolean() : f;
        return new StyleSettings
        {
            Name = j.TryGetProperty("name", out var n) ? n.GetString() ?? d.Name : d.Name,
            Description = j.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : d.Description,
            Edges = Bool("edges", d.Edges),
            BackEdges = Bool("backEdges", d.BackEdges),
            Profiles = Bool("profiles", d.Profiles),
            DepthCue = Bool("depthCue", d.DepthCue),
            Extension = Bool("extension", d.Extension),
            FaceStyle = j.TryGetProperty("faceStyle", out var fs) && Enum.TryParse<FaceStyle>(fs.GetString(), out var face) ? face : d.FaceStyle,
            HiddenGeometry = Bool("hiddenGeometry", d.HiddenGeometry),
            Guides = Bool("guides", d.Guides),
            ModelAxes = Bool("modelAxes", d.ModelAxes),
            SectionPlanes = Bool("sectionPlanes", d.SectionPlanes),
            SectionCuts = Bool("sectionCuts", d.SectionCuts),
            ProfileWidth = Int("profileWidth", d.ProfileWidth),
            DepthCueWidth = Int("depthCueWidth", d.DepthCueWidth),
            ExtensionLength = Int("extensionLength", d.ExtensionLength),
            Endpoints = Bool("endpoints", d.Endpoints),
            EndpointLength = Int("endpointLength", d.EndpointLength),
            Jitter = Bool("jitter", d.Jitter),
            Dashes = Bool("dashes", d.Dashes),
            EdgeColorMode = j.TryGetProperty("edgeColorMode", out var m) && Enum.TryParse<EdgeColorMode>(m.GetString(), out var mode) ? mode : d.EdgeColorMode,
            EdgeColor = ReadRgba(j, "edgeColor", d.EdgeColor),
            FrontColor = ReadRgba(j, "frontColor", d.FrontColor),
            BackColor = ReadRgba(j, "backColor", d.BackColor),
            XrayOpacity = j.TryGetProperty("xrayOpacity", out var x) ? x.GetDouble() : d.XrayOpacity,
            Transparency = Bool("transparency", d.Transparency),
            TransparencyQuality = j.TryGetProperty("transparencyQuality", out var q) && Enum.TryParse<TransparencyQuality>(q.GetString(), out var quality) ? quality : d.TransparencyQuality,
            BackgroundColor = ReadRgba(j, "backgroundColor", d.BackgroundColor),
            Sky = Bool("sky", d.Sky),
            SkyColor = ReadRgba(j, "skyColor", d.SkyColor),
            Ground = Bool("ground", d.Ground),
            GroundColor = ReadRgba(j, "groundColor", d.GroundColor),
            GroundTransparency = j.TryGetProperty("groundTransparency", out var t) ? t.GetDouble() : d.GroundTransparency,
            GroundFromBelow = Bool("groundFromBelow", d.GroundFromBelow),
            SelectedColor = ReadRgba(j, "selectedColor", d.SelectedColor),
            LockedColor = ReadRgba(j, "lockedColor", d.LockedColor),
            GuideColor = ReadRgba(j, "guideColor", d.GuideColor),
            ActiveSectionColor = ReadRgba(j, "activeSectionColor", d.ActiveSectionColor),
            InactiveSectionColor = ReadRgba(j, "inactiveSectionColor", d.InactiveSectionColor),
            SectionCutColor = ReadRgba(j, "sectionCutColor", d.SectionCutColor),
            SectionFillColor = ReadRgba(j, "sectionFillColor", d.SectionFillColor),
            SectionCutWidth = Int("sectionCutWidth", d.SectionCutWidth),
            ShowWatermarks = Bool("showWatermarks", d.ShowWatermarks),
            Watermarks = j.TryGetProperty("watermarks", out var marks) ? new ValueList<Watermark>(marks.EnumerateArray().Select(ReadWatermark)) : d.Watermarks,
        };
    }

    private static void WriteDimensionStyle(Utf8JsonWriter w, string name, DimensionStyle s)
    {
        w.WriteStartObject(name);
        w.WriteString("font", s.Font);
        w.WriteNumber("fontSize", s.FontSize);
        w.WriteString("color", RgbaText(s.Color));
        w.WriteString("endpoints", s.Endpoints.ToString());
        w.WriteBoolean("alignToScreen", s.AlignToScreen);
        w.WriteString("position", s.Position.ToString());
        w.WriteBoolean("radialPrefix", s.ShowRadialPrefix);
        w.WriteEndObject();
    }

    private static DimensionStyle ReadDimensionStyle(JsonElement j)
    {
        var d = new DimensionStyle();
        return new DimensionStyle
        {
            Font = j.TryGetProperty("font", out var f) ? f.GetString() ?? "" : d.Font,
            FontSize = j.TryGetProperty("fontSize", out var fs) ? fs.GetInt32() : d.FontSize,
            Color = ReadRgba(j, "color", d.Color),
            Endpoints = j.TryGetProperty("endpoints", out var e) && Enum.TryParse<DimensionEndpoint>(e.GetString(), out var end) ? end : d.Endpoints,
            AlignToScreen = j.TryGetProperty("alignToScreen", out var a) ? a.GetBoolean() : d.AlignToScreen,
            Position = j.TryGetProperty("position", out var p) && Enum.TryParse<DimensionTextPosition>(p.GetString(), out var pos) ? pos : d.Position,
            ShowRadialPrefix = j.TryGetProperty("radialPrefix", out var r) ? r.GetBoolean() : d.ShowRadialPrefix,
        };
    }

    private static void WriteTextStyle(Utf8JsonWriter w, string name, TextStyle s)
    {
        w.WriteStartObject(name);
        w.WriteString("font", s.Font);
        w.WriteNumber("fontSize", s.FontSize);
        w.WriteString("color", RgbaText(s.Color));
        w.WriteString("endpoint", s.Endpoint.ToString());
        w.WriteString("leader", s.Leader.ToString());
        w.WriteEndObject();
    }

    private static TextStyle ReadTextStyle(JsonElement j)
    {
        var d = new TextStyle();
        return new TextStyle
        {
            Font = j.TryGetProperty("font", out var f) ? f.GetString() ?? "" : d.Font,
            FontSize = j.TryGetProperty("fontSize", out var fs) ? fs.GetInt32() : d.FontSize,
            Color = ReadRgba(j, "color", d.Color),
            Endpoint = j.TryGetProperty("endpoint", out var e) && Enum.TryParse<DimensionEndpoint>(e.GetString(), out var end) ? end : d.Endpoint,
            Leader = j.TryGetProperty("leader", out var l) && Enum.TryParse<LeaderType>(l.GetString(), out var leader) ? leader : d.Leader,
        };
    }

    private static Watermark ReadWatermark(JsonElement j)
    {
        var d = new Watermark();
        T Enum<T>(string n, T f) where T : struct => j.TryGetProperty(n, out var v) && System.Enum.TryParse<T>(v.GetString(), out var x) ? x : f;
        return new Watermark
        {
            Name = j.GetProperty("name").GetString() ?? "",
            Image = new TextureImage { FileName = j.GetProperty("file").GetString() ?? "", Data = j.GetProperty("image").GetBytesFromBase64() },
            Overlay = j.GetProperty("overlay").GetBoolean(),
            Visible = j.GetProperty("visible").GetBoolean(),
            Opacity = j.GetProperty("opacity").GetDouble(),
            Mask = j.GetProperty("mask").GetBoolean(),
            Layout = Enum("layout", d.Layout),
            LockAspect = j.GetProperty("lockAspect").GetBoolean(),
            Scale = j.GetProperty("scale").GetDouble(),
            Position = Enum("position", d.Position),
        };
    }

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

        var curves = new Dictionary<Curve, int>();
        foreach (var edge in e.Edges)
            if (edge.Curve is { } c && !curves.ContainsKey(c))
                curves[c] = curves.Count;

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
            if (edge.Curve != null)
                w.WriteNumberValue(curves[edge.Curve]);
            w.WriteEndArray();
        }
        w.WriteEndArray();

        if (curves.Count > 0)
        {
            w.WriteStartArray("curves");
            foreach (var c in curves.Keys)
            {
                w.WriteStartObject();
                WriteVec(w, "center", c.Center);
                WriteVec(w, "normal", c.Normal);
                w.WriteNumber("radius", c.Radius);
                w.WriteNumber("segments", c.Segments);
                w.WriteBoolean("polygon", c.IsPolygon);
                if (c.Spline is { } sp)
                {
                    w.WriteStartObject("spline");
                    w.WriteString("kind", sp.Kind.ToString());
                    w.WriteStartArray("points");
                    foreach (var q in sp.ControlPoints)
                    {
                        w.WriteNumberValue(q.X);
                        w.WriteNumberValue(q.Y);
                        w.WriteNumberValue(q.Z);
                    }
                    w.WriteEndArray();
                    w.WriteNumber("precision", sp.Precision);
                    w.WriteNumber("parameter", sp.Parameter);
                    w.WriteBoolean("closed", sp.Closed);
                    if (sp.LineClosed)
                        w.WriteBoolean("lineClosed", true);
                    if (sp.Kind == SplineKind.DividerAnimation)
                    {
                        w.WriteNumber("maximum", sp.Maximum);
                        w.WriteString("mode", sp.Mode.ToString());
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        // SUbD's sharpness, infinity written as -1 (JSON has no infinity).
        static double Sharpness(double s) => double.IsPositiveInfinity(s) ? -1 : s;
        if (e.Subdivision > 0)
            w.WriteNumber("subdivision", e.Subdivision);
        if (e.SubdivisionSmoothCorners)
            w.WriteBoolean("subdivisionSmoothCorners", true);
        if (e.Edges.Any(x => x.Crease != 0))
        {
            w.WriteStartArray("creases");
            foreach (var x in e.Edges.Where(x => x.Crease != 0))
            {
                w.WriteNumberValue(eIndex[x]);
                w.WriteNumberValue(Sharpness(x.Crease));
            }
            w.WriteEndArray();
        }
        if (e.Vertices.Any(v => v.Crease != 0))
        {
            w.WriteStartArray("vertexCreases");
            foreach (var v in e.Vertices.Where(v => v.Crease != 0))
            {
                w.WriteNumberValue(vIndex[v]);
                w.WriteNumberValue(Sharpness(v.Crease));
            }
            w.WriteEndArray();
        }

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
            // Bits: 1 hidden, 2 casts no shadows, 4 receives none.
            w.WriteNumberValue((f.Hidden ? 1 : 0) | (f.CastShadows ? 0 : 2) | (f.ReceiveShadows ? 0 : 4));
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
            if (!inst.CastShadows)
                w.WriteBoolean("castShadows", false);
            if (!inst.ReceiveShadows)
                w.WriteBoolean("receiveShadows", false);
            if (inst.GluedTo is { } glued && e.Faces.IndexOf(glued) is var gi and >= 0)
                w.WriteNumber("gluedTo", gi);
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
                if (d.Kind != DimensionKind.Linear)
                    w.WriteString("kind", d.Kind.ToString());
                if (d.Style is { } ds)
                    WriteDimensionStyle(w, "style", ds);
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
                if (s.Symbol.Length > 0)
                    w.WriteString("symbol", s.Symbol);
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
                if (t.LeaderPixels is { } lp)
                {
                    w.WriteStartArray("leaderPixels");
                    w.WriteNumberValue(lp.X);
                    w.WriteNumberValue(lp.Y);
                    w.WriteEndArray();
                }
                if (t.Style is { } ts)
                    WriteTextStyle(w, "style", ts);
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
            UnitFormat = r.TryGetProperty("unitFormat", out var uf) && Enum.TryParse<UnitFormat>(uf.GetString(), out var format) ? format : UnitFormat.Decimal,
            ShowUnitSymbol = !r.TryGetProperty("showUnitSymbol", out var sus) || sus.GetBoolean(),
            ForceZeroFeet = r.TryGetProperty("forceZeroFeet", out var fzf) && fzf.GetBoolean(),
            SourceVersion = r.TryGetProperty("sourceVersion", out var sv) ? sv.GetString() ?? "" : "",
        };
        if (r.TryGetProperty("annotation", out var ann))
        {
            // Older files kept only the size and endpoints.
            var style = ann.TryGetProperty("dimensionStyle", out var ds) ? ReadDimensionStyle(ds) : new DimensionStyle();
            if (ann.TryGetProperty("dimensionFontSize", out var dfs))
                style = style with { FontSize = dfs.GetInt32() };
            if (ann.TryGetProperty("dimensionEndpoints", out var de) && Enum.TryParse<DimensionEndpoint>(de.GetString(), out var end))
                style = style with { Endpoints = end };
            model.Dimensions = style;
            var display = new DimensionDisplay();
            model.DimensionDisplay = display with
            {
                HideForeshortened = ann.TryGetProperty("hideForeshortened", out var hf) ? hf.GetBoolean() : display.HideForeshortened,
                ForeshortenedLimit = ann.TryGetProperty("foreshortenedLimit", out var fl) ? fl.GetDouble() : display.ForeshortenedLimit,
                HideSmall = ann.TryGetProperty("hideSmall", out var hs) ? hs.GetBoolean() : display.HideSmall,
                SmallPixels = ann.TryGetProperty("smallPixels", out var sp) ? sp.GetInt32() : display.SmallPixels,
            };
            model.ScreenText = ann.TryGetProperty("screenText", out var st) ? ReadTextStyle(st) : new TextStyle();
            model.LeaderText = ann.TryGetProperty("leaderText", out var lt) ? ReadTextStyle(lt) : new TextStyle();
            if (ann.TryGetProperty("textFontSize", out var tfs))
            {
                model.ScreenText = model.ScreenText with { FontSize = tfs.GetInt32() };
                model.LeaderText = model.LeaderText with { FontSize = tfs.GetInt32() };
            }
        }
        if (r.TryGetProperty("animation", out var anim))
        {
            model.SceneTransitions = !anim.TryGetProperty("transitions", out var tr) || tr.GetBoolean();
            model.SceneTransitionSeconds = anim.TryGetProperty("transitionSeconds", out var ts) ? ts.GetDouble() : 2;
            model.SceneDelaySeconds = anim.TryGetProperty("delaySeconds", out var ds) ? ds.GetDouble() : 0;
        }
        if (r.TryGetProperty("sectionFill", out var fill))
            model.ShowSectionFill = fill.GetBoolean();
        if (r.TryGetProperty("style", out var styleJson))
            model.Style = ReadStyle(styleJson);
        if (r.TryGetProperty("options", out var opts))
            model.Options = new ModelOptions
            {
                Author = opts.GetProperty("author").GetString() ?? "",
                Name = opts.TryGetProperty("name", out var mn) ? mn.GetString() ?? "" : "",
                GlueTo = opts.TryGetProperty("glueTo", out var gt) && Enum.TryParse<GlueTo>(gt.GetString(), out var glue) ? glue : GlueTo.None,
                CutsOpening = opts.TryGetProperty("cutsOpening", out var co) && co.GetBoolean(),
                AlwaysFaceCamera = opts.TryGetProperty("alwaysFaceCamera", out var afc) && afc.GetBoolean(),
                ShadowsFaceSun = opts.TryGetProperty("shadowsFaceSun", out var sfs) && sfs.GetBoolean(),
                Description = opts.TryGetProperty("description", out var md2) ? md2.GetString() ?? "" : "",
                FadeRest = opts.GetProperty("fadeRest").GetDouble(),
                FadeSimilar = opts.GetProperty("fadeSimilar").GetDouble(),
                ShowComponentAxes = opts.GetProperty("componentAxes").GetBoolean(),
                SmoothTextures = opts.GetProperty("smoothTextures").GetBoolean(),
                ColorByTag = opts.TryGetProperty("colorByTag", out var cbt) && cbt.GetBoolean(),
                AnglePrecision = opts.TryGetProperty("anglePrecision", out var ap) ? ap.GetInt32() : 1,
                AngleSnapping = !opts.TryGetProperty("angleSnapping", out var asn) || asn.GetBoolean(),
                AngleSnap = opts.TryGetProperty("angleSnap", out var asv) ? asv.GetDouble() : 15,
                LengthSnapping = opts.TryGetProperty("lengthSnapping", out var lsn) && lsn.GetBoolean(),
                LengthSnap = opts.TryGetProperty("lengthSnap", out var lsv) ? lsv.GetDouble() : 1,
                FogStart = opts.TryGetProperty("fogStart", out var fs) ? fs.GetDouble() : 0,
                FogEnd = opts.TryGetProperty("fogEnd", out var fe) ? fe.GetDouble() : 1,
                FogColor = opts.TryGetProperty("fogColor", out var fc) && fc.GetString()!.Split(',') is [var fr, var fg, var fb]
                    ? new Rgba(byte.Parse(fr), byte.Parse(fg), byte.Parse(fb)) : null,
            };
        if (r.TryGetProperty("shadows", out var shadows))
            model.Shadows = ReadShadows(shadows);
        if (r.TryGetProperty("axes", out var axes))
            model.Axes = Transform.FromColumnMajor(axes.EnumerateArray().Select(x => x.GetDouble()).ToArray());

        foreach (var m in r.GetProperty("materials").EnumerateArray())
        {
            var mat = new Material
            {
                Name = m.GetProperty("name").GetString() ?? "",
                Color = ReadColor(m.GetProperty("color")),
                Opacity = m.GetProperty("opacity").GetDouble(),
                Colorize = m.TryGetProperty("colorize", out var colorize) && colorize.GetBoolean(),
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
                Dashes = t.TryGetProperty("dashes", out var dashes) ? LineStyles.Parse(dashes.GetString()) : LineStyle.Solid,
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
                IfcType = d.TryGetProperty("ifcType", out var ifc) ? ifc.GetString() ?? "" : "",
                IsGroup = d.GetProperty("group").GetBoolean(),
                IsImage = d.TryGetProperty("image", out var img) && img.GetBoolean(),
                AlwaysFaceCamera = d.TryGetProperty("alwaysFaceCamera", out var fc) && fc.GetBoolean(),
                ShadowsFaceSun = d.TryGetProperty("shadowsFaceSun", out var sfs) && sfs.GetBoolean(),
                GlueTo = d.TryGetProperty("glueTo", out var glue) && Enum.TryParse<GlueTo>(glue.GetString(), out var g) ? g : GlueTo.None,
                CutsOpening = d.TryGetProperty("cutsOpening", out var cut) && cut.GetBoolean(),
                Camera = d.TryGetProperty("camera", out var cam)
                    ? new PhysicalCamera(cam.GetProperty("fov").GetDouble(), cam.GetProperty("aspect").GetDouble()) : null,
            });
        }
        for (var i = 0; i < defsJson.Count; i++)
        {
            if (defsJson[i].TryGetProperty("attributes", out var attrs))
                foreach (var a in attrs.EnumerateArray())
                    model.Definitions[i].Attributes.Add(new ComponentAttribute
                    {
                        Name = a.GetProperty("name").GetString() ?? "",
                        Value = a.GetProperty("value").GetString() ?? "",
                        UserCanEdit = a.TryGetProperty("userCanEdit", out var editable) && editable.GetBoolean(),
                    });
            ReadEntities(defsJson[i].GetProperty("entities"), model.Definitions[i].Entities, model);
        }
        ReadEntities(r.GetProperty("entities"), model.Entities, model);
        for (var i = 0; i < defsJson.Count; i++)
            if (defsJson[i].TryGetProperty("schemaTypes", out var applied))
                foreach (var kv in applied.EnumerateObject())
                    model.Definitions[i].SchemaTypes[kv.Name] = kv.Value.GetString() ?? "";
        if (r.TryGetProperty("schemas", out var schemas))
            foreach (var sj in schemas.EnumerateArray())
            {
                var schema = new ClassificationSchema
                {
                    Name = sj.GetProperty("name").GetString() ?? "",
                    Description = sj.TryGetProperty("description", out var sd) ? sd.GetString() ?? "" : "",
                };
                foreach (var t in sj.GetProperty("types").EnumerateObject())
                    schema.Types.Add((t.Name, t.Value.EnumerateArray().Select(a => a.GetString() ?? "").ToList()));
                model.Schemas.Add(schema);
            }

        foreach (var s in r.GetProperty("scenes").EnumerateArray())
        {
            var scene = new Scene
            {
                Name = s.GetProperty("name").GetString() ?? "",
                Description = s.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                InAnimation = !s.TryGetProperty("inAnimation", out var inAnimation) || inAnimation.GetBoolean(),
            };
            if (s.TryGetProperty("camera", out var c))
            {
                scene.Camera = new CameraState(
                    ReadVec(c.GetProperty("eye")), ReadVec(c.GetProperty("target")), ReadVec(c.GetProperty("up")),
                    c.GetProperty("perspective").GetBoolean(), c.GetProperty("fov").GetDouble(), c.GetProperty("orthoHeight").GetDouble());
            }
            foreach (var t in s.GetProperty("hiddenTags").EnumerateArray())
                scene.HiddenTags.Add(t.GetString() ?? "");
            // Older files saved the camera and tags only.
            scene.Saves = s.TryGetProperty("saves", out var saves) && Enum.TryParse<SceneProperties>(saves.GetString(), out var flags) ? flags
                : s.TryGetProperty("activeSection", out _) ? SceneProperties.All : SceneProperties.Camera | SceneProperties.VisibleTags;
            if (s.TryGetProperty("style", out var sceneStyle))
                scene.Style = ReadStyle(sceneStyle);
            if (s.TryGetProperty("shadows", out var sceneShadows))
                scene.Shadows = ReadShadows(sceneShadows);
            if (s.TryGetProperty("axes", out var sceneAxes))
                scene.Axes = Transform.FromColumnMajor(sceneAxes.EnumerateArray().Select(x => x.GetDouble()).ToArray());
            if (s.TryGetProperty("activeSection", out var sceneSection) && sceneSection.GetInt32() is var si && si >= 0 && si < model.Entities.SectionPlanes.Count)
                scene.ActiveSection = model.Entities.SectionPlanes[si];
            if (s.TryGetProperty("hidden", out var hidden))
                foreach (var h in hidden.EnumerateArray())
                {
                    var (owner, kind, index) = (h[0].GetInt32(), h[1].GetInt32(), h[2].GetInt32());
                    if (owner >= model.Definitions.Count)
                        continue;
                    var e = owner < 0 ? model.Entities : model.Definitions[owner].Entities;
                    System.Collections.IList list = kind switch { 0 => e.Instances, 1 => e.Faces, _ => e.Edges };
                    if (index >= 0 && index < list.Count)
                        scene.Hidden.Add(list[index]!);
                }
            if (s.TryGetProperty("photo", out var pj))
            {
                var l = pj.GetProperty("lines").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                (PhotoPoint, PhotoPoint) Line(int i) => (new(l[4 * i], l[4 * i + 1]), new(l[4 * i + 2], l[4 * i + 3]));
                var o = pj.GetProperty("origin").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                scene.Photo = new MatchedPhoto
                {
                    Name = pj.GetProperty("name").GetString() ?? "",
                    Image = pj.GetProperty("image").GetBytesFromBase64(),
                    Width = pj.GetProperty("width").GetInt32(),
                    Height = pj.GetProperty("height").GetInt32(),
                    Red = [Line(0), Line(1)],
                    Green = [Line(2), Line(3)],
                    Origin = new PhotoPoint(o[0], o[1]),
                    Distance = pj.GetProperty("distance").GetDouble(),
                };
            }
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

        var curves = new List<Curve>();
        if (j.TryGetProperty("curves", out var cj))
            foreach (var c in cj.EnumerateArray())
            {
                var curve = new Curve
                {
                    Center = ReadVec(c.GetProperty("center")),
                    Normal = ReadVec(c.GetProperty("normal")),
                    Radius = c.GetProperty("radius").GetDouble(),
                    Segments = c.GetProperty("segments").GetInt32(),
                    IsPolygon = c.GetProperty("polygon").GetBoolean(),
                };
                if (c.TryGetProperty("spline", out var sj) && Enum.TryParse<SplineKind>(sj.GetProperty("kind").GetString(), out var kind))
                {
                    var flat = sj.GetProperty("points").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                    var pts = Enumerable.Range(0, flat.Length / 3).Select(i => new Vec3(flat[3 * i], flat[3 * i + 1], flat[3 * i + 2])).ToList();
                    curve.Spline = new SplineData(kind, pts, sj.GetProperty("precision").GetInt32(), sj.GetProperty("parameter").GetDouble(), sj.GetProperty("closed").GetBoolean(),
                        sj.TryGetProperty("lineClosed", out var lc) && lc.GetBoolean(),
                        sj.TryGetProperty("maximum", out var mx) ? mx.GetDouble() : 0,
                        sj.TryGetProperty("mode", out var md) && Enum.TryParse<AnimationSteps>(md.GetString(), out var mode) ? mode : AnimationSteps.EqualMaximum);
                }
                curves.Add(curve);
            }

        foreach (var ej in j.GetProperty("edges").EnumerateArray())
        {
            var a = ej.EnumerateArray().ToArray();
            var edge = e.AddEdge(e.Vertices[a[0].GetInt32()], e.Vertices[a[1].GetInt32()]);
            edge.Flags = (EdgeFlags)a[2].GetInt32();
            edge.Tag = TagAt(model, a[3].GetInt32());
            edge.Material = MaterialAt(model, a[4].GetInt32());
            if (a.Length > 5 && a[5].GetInt32() is var ci && ci >= 0 && ci < curves.Count)
                edge.Curve = curves[ci];
        }

        static double Sharpness(double s) => s < 0 ? double.PositiveInfinity : s;
        if (j.TryGetProperty("subdivision", out var sub))
            e.Subdivision = sub.GetInt32();
        e.SubdivisionSmoothCorners = j.TryGetProperty("subdivisionSmoothCorners", out var corners) && corners.GetBoolean();
        if (j.TryGetProperty("creases", out var creases))
        {
            var a = creases.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            for (var i = 0; i + 1 < a.Length; i += 2)
                e.Edges[(int)a[i]].Crease = Sharpness(a[i + 1]);
        }
        if (j.TryGetProperty("vertexCreases", out var vj))
        {
            var a = vj.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            for (var i = 0; i + 1 < a.Length; i += 2)
                e.Vertices[(int)a[i]].Crease = Sharpness(a[i + 1]);
        }

        foreach (var fj in j.GetProperty("faces").EnumerateArray())
        {
            var a = fj.EnumerateArray().ToArray();
            var face = new Face
            {
                FrontMaterial = MaterialAt(model, a[1].GetInt32()),
                BackMaterial = MaterialAt(model, a[2].GetInt32()),
                Tag = TagAt(model, a[3].GetInt32()),
                Hidden = (a[4].GetInt32() & 1) != 0,
                CastShadows = (a[4].GetInt32() & 2) == 0,
                ReceiveShadows = (a[4].GetInt32() & 4) == 0,
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
            inst.CastShadows = !ij.TryGetProperty("castShadows", out var cs) || cs.GetBoolean();
            inst.ReceiveShadows = !ij.TryGetProperty("receiveShadows", out var rs) || rs.GetBoolean();
            if (ij.TryGetProperty("gluedTo", out var gt) && gt.GetInt32() is var gi && gi >= 0 && gi < e.Faces.Count)
                inst.GluedTo = e.Faces[gi];
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
                    Kind = d.TryGetProperty("kind", out var k) && Enum.TryParse<DimensionKind>(k.GetString(), out var kind) ? kind : DimensionKind.Linear,
                    Style = d.TryGetProperty("style", out var ds) ? ReadDimensionStyle(ds) : null,
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
                    Symbol = s.TryGetProperty("symbol", out var sy) ? sy.GetString() ?? "" : "",
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
                    Style = t.TryGetProperty("style", out var ts) ? ReadTextStyle(ts) : null,
                    LeaderPixels = t.TryGetProperty("leaderPixels", out var lp) ? (lp[0].GetDouble(), lp[1].GetDouble()) : null,
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

    private static void WriteShadows(Utf8JsonWriter w, ShadowSettings s)
    {
        w.WriteStartObject("shadows");
        w.WriteBoolean("enabled", s.Enabled);
        w.WriteString("time", s.Time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        w.WriteNumber("utcOffset", s.UtcOffset);
        w.WriteNumber("latitude", s.Latitude);
        w.WriteNumber("longitude", s.Longitude);
        w.WriteNumber("northAngle", s.NorthAngle);
        w.WriteNumber("light", s.Light);
        w.WriteNumber("dark", s.Dark);
        w.WriteBoolean("useSunForShading", s.UseSunForShading);
        w.WriteBoolean("onFaces", s.OnFaces);
        w.WriteBoolean("onGround", s.OnGround);
        w.WriteBoolean("fromEdges", s.FromEdges);
        w.WriteEndObject();
    }

    private static ShadowSettings ReadShadows(JsonElement e)
    {
        var d = new ShadowSettings();
        bool Flag(string name, bool fallback) => e.TryGetProperty(name, out var v) ? v.GetBoolean() : fallback;
        double Number(string name, double fallback) => e.TryGetProperty(name, out var v) ? v.GetDouble() : fallback;
        var time = e.TryGetProperty("time", out var t) && DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : d.Time;
        return new ShadowSettings
        {
            Enabled = Flag("enabled", d.Enabled),
            Time = time,
            UtcOffset = Number("utcOffset", d.UtcOffset),
            Latitude = Number("latitude", d.Latitude),
            Longitude = Number("longitude", d.Longitude),
            NorthAngle = Number("northAngle", d.NorthAngle),
            Light = (int)Number("light", d.Light),
            Dark = (int)Number("dark", d.Dark),
            UseSunForShading = Flag("useSunForShading", d.UseSunForShading),
            OnFaces = Flag("onFaces", d.OnFaces),
            OnGround = Flag("onGround", d.OnGround),
            FromEdges = Flag("fromEdges", d.FromEdges),
        };
    }
}
