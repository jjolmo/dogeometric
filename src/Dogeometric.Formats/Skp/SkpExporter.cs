using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Sk = OpenSkp;

namespace Dogeometric.Formats.Skp;

/// <summary>
/// Writes a Dogeometric model as a SketchUp .skp through OpenSKP's writer. The result uses the pre-2021 (v17)
/// format, which SketchUp 2017 and later open. Returns what could not be reproduced exactly.
/// </summary>
public static class SkpExporter
{
    private const double InchPerMm = 1 / 25.4;

    public static List<string> Export(Model model, string path)
    {
        var warnings = new List<string>();
        var b = Sk.SkpCreate.NewFile();
        var tempFiles = new List<string>();
        try
        {
            var materials = WriteMaterials(model, b, tempFiles, warnings);
            var tags = new Dictionary<Tag, int>();
            foreach (var tag in model.Tags.Where(t => t != model.UntaggedTag))
                tags[tag] = b.AddLayer(tag.Name, (tag.Color.R, tag.Color.G, tag.Color.B, tag.Color.A), hidden: !tag.Visible);

            var ctx = new Context(materials, tags, warnings);

            // An empty model is written as SketchUp's own empty document; with materials or tags the writer needs one
            // entity, so a guide point marks the origin.
            var entities = model.AllEntities.Sum(e => e.Faces.Count + e.Edges.Count + e.GuidePoints.Count + e.GuideLines.Count + e.SectionPlanes.Count
                + e.Dimensions.Count + e.Texts.Count + e.Instances.Count);
            if (entities == 0 && (materials.Count > 0 || tags.Count > 0))
            {
                b.AddConstructionPoint((0, 0, 0));
                warnings.Add("The model has no geometry: a guide point was added at the origin so the .skp can hold its materials and tags.");
            }

            // Definitions are declared children-first (a definition can only nest already-closed ones), then placed.
            foreach (var def in PostOrder(model))
            {
                if (ctx.IsEmpty(def))
                    continue;
                var cb = b.AddComponentDefinition(string.IsNullOrEmpty(def.Name) ? "Component" : def.Name);
                cb.IsGroupDefinition = def.IsGroup;
                cb.IsImageDefinition = def.IsImage;
                cb.AlwaysFacesCamera = def.AlwaysFaceCamera;
                cb.ShadowsFaceSun = def.ShadowsFaceSun;
                using (cb)
                {
                    if (def.IsImage && WriteImageQuad(def, cb, ctx))
                        ctx.Images.Add(def);
                    else
                        WriteEntities(Baked(def.Entities), Target.For(cb), ctx);
                }
                ctx.Builders[def] = cb;
            }

            WriteEntities(model.Entities, Target.For(b), ctx);

            // Guides: OpenSKP writes them at the top level only.
            foreach (var g in model.Entities.GuideLines)
            {
                try
                {
                    if (g.Start is { } s && g.End is { } en)
                        b.AddConstructionLine(Inches(s), Inches(en));
                    else
                        b.AddConstructionLine(Inches(g.Point), direction: (g.Direction.X, g.Direction.Y, g.Direction.Z));
                }
                catch (Sk.SkpWriteException ex)
                {
                    ctx.Warn($"guide skipped: {ex.Message}");
                }
            }
            foreach (var g in model.Entities.GuidePoints)
                b.AddConstructionPoint(Inches(g.Position));
            var nestedGuides = model.Definitions.Sum(d => d.Entities.GuideLines.Count + d.Entities.GuidePoints.Count);
            if (nestedGuides > 0)
                warnings.Add($"{nestedGuides} guide(s) inside groups/components not written (OpenSKP writes top-level guides only)");
            if (model.Scenes.Count > 0)
                warnings.Add($"{model.Scenes.Count} scene(s) not written (OpenSKP writer has no scenes)");
            b.Save(path);
        }
        finally
        {
            foreach (var f in tempFiles)
                File.Delete(f);
        }
        return warnings;
    }

    private static string? SniffImage(byte[] data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [(byte)'B', (byte)'M', ..] => ".bmp",
        _ => null,
    };

    private static Dictionary<Material, int> WriteMaterials(Model model, Sk.SkpBuilder b, List<string> tempFiles, List<string> warnings)
    {
        var map = new Dictionary<Material, int>();
        var names = new HashSet<string>();
        foreach (var m in model.Materials)
        {
            var name = m.Name;
            for (var i = 2; !names.Add(name); i++)
                name = $"{m.Name} {i}";
            if (m.Texture is { Data.Length: > 0 } tex)
            {
                // The bytes say what the image is; the stored file name can be anything (even ".skp").
                var ext = SniffImage(tex.Data) ?? Path.GetExtension(tex.FileName).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg")
                {
                    var tmp = Path.Combine(Path.GetTempPath(), $"dogeometric-tex-{Guid.NewGuid():N}{ext}");
                    File.WriteAllBytes(tmp, tex.Data);
                    tempFiles.Add(tmp);
                    try
                    {
                        map[m] = b.AddTextureMaterial(name, tmp, tex.HeightMm * InchPerMm, tex.WidthMm * InchPerMm, m.Opacity);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"texture of '{m.Name}' written as colour: {ex.Message}");
                    }
                }
                else
                {
                    warnings.Add($"texture of '{m.Name}' ({ext}) written as colour");
                }
            }
            map[m] = b.AddMaterial(name, (m.Color.R, m.Color.G, m.Color.B, m.Color.A), m.Opacity);
        }
        return map;
    }

    /// <summary>SketchUp stores what SUbD shows: a subdivided group's smooth surface, not its control mesh.</summary>
    private static Entities Baked(Entities e)
    {
        if (e.Subdivision <= 0)
            return e;
        var copy = new Entities();
        Grouping.CopyEntities(e, copy, Transform.Identity);
        CatmullClark.Apply(copy, e.Subdivision);
        return copy;
    }

    private static void WriteEntities(Entities e, Target target, Context ctx)
    {
        foreach (var inst in e.Instances)
        {
            if (!ctx.Builders.TryGetValue(inst.Definition, out var cb))
                continue;
            var (t, m3) = Placement(inst.Transform);
            if (ctx.Images.Contains(inst.Definition))
            {
                // SketchUp Image entity: placed like an instance, drawn by its definition's textured quad.
                target.AddImage(cb, t, m3, ctx.Layer(inst.Tag), inst.Hidden);
                continue;
            }
            target.AddInstance(cb, inst.IsGroup, string.IsNullOrEmpty(inst.Name) ? null : inst.Name, t, m3,
                ctx.Material(inst.Material), ctx.Layer(inst.Tag), inst.Hidden);
        }

        // Distinct vertices at the same place (unwelded meshes, common in imported models) would be merged by the
        // writer, which identifies vertices by position: each extra one is nudged by a billionth of an inch, far
        // below SketchUp's tolerance, so the topology comes back as it was.
        var at = WrittenPositions(e);

        var usedByFaces = new HashSet<Edge>();
        foreach (var face in e.Faces)
        {
            foreach (var loop in face.Loops)
                foreach (var (edge, _) in loop.Edges)
                    usedByFaces.Add(edge);

            var edges = face.OuterLoop.Edges.Select(x => x.Edge).ToList();
            var soft = edges.All(x => x.Flags.HasFlag(EdgeFlags.Soft));
            var smooth = edges.All(x => x.Flags.HasFlag(EdgeFlags.Smooth));
            var hiddenEdges = edges.All(x => x.Flags.HasFlag(EdgeFlags.Hidden));
            foreach (var (outerMm, holesMm) in WritablePolygons(face, at))
            {
                var outer = outerMm.Select(Inches).ToList();
                var holes = holesMm.Select(h => (IReadOnlyList<(double, double, double)>)h.Select(Inches).ToList()).ToList();
                var (frontPins, backPins) = (Pins(face, false, outerMm), Pins(face, true, outerMm));
                try
                {
                    target.AddFace(outer, ctx.Material(face.FrontMaterial), ctx.Layer(face.Tag), ctx.Material(face.BackMaterial),
                        face.Hidden, soft, smooth, hiddenEdges, holes.Count > 0 ? holes : null, frontPins, backPins);
                }
                catch (Sk.SkpWriteException ex) when (frontPins != null || backPins != null)
                {
                    // A texture placement the pins can't express (a projected texture seen edge-on): keep the face
                    // with the default projection rather than lose it.
                    ctx.Warn($"texture placement not kept on a face: {ex.Message}");
                    try
                    {
                        target.AddFace(outer, ctx.Material(face.FrontMaterial), ctx.Layer(face.Tag), ctx.Material(face.BackMaterial),
                            face.Hidden, soft, smooth, hiddenEdges, holes.Count > 0 ? holes : null);
                    }
                    catch (Sk.SkpWriteException ex2)
                    {
                        ctx.Warn($"face skipped: {ex2.Message}");
                    }
                }
                catch (Sk.SkpWriteException ex)
                {
                    ctx.Warn($"face skipped: {ex.Message}");
                }
            }
        }

        foreach (var edge in e.Edges.Where(x => !usedByFaces.Contains(x)))
        {
            try
            {
                target.AddEdge(Inches(at(edge.Start)), Inches(at(edge.End)), edge.Flags);
            }
            catch (Sk.SkpWriteException ex)
            {
                ctx.Warn($"edge skipped: {ex.Message}");
            }
        }
    }

    /// <summary>Faces may drift this far (mm) off their plane and still be written as one face.</summary>
    private const double PlaneToleranceMm = 0.1;

    /// <summary>
    /// The polygons to write for a face: the face itself when planar (within <see cref="PlaneToleranceMm"/>), or its
    /// ear-clipped triangles otherwise — correct for concave faces, unlike a fan from the first corner.
    /// </summary>
    private static IEnumerable<(List<Vec3> Outer, List<List<Vec3>> Holes)> WritablePolygons(Face face, Func<Vertex, Vec3> at)
    {
        var outer = face.OuterLoop.Vertices.Select(at).ToList();
        var holes = face.InnerLoops.Select(l => l.Vertices.Select(at).ToList()).ToList();
        var all = outer.Concat(holes.SelectMany(h => h)).ToList();
        var normal = Polygon.Normal(outer);
        var origin = new Vec3(outer.Average(p => p.X), outer.Average(p => p.Y), outer.Average(p => p.Z));
        if (normal.IsZero(1e-12) || all.Max(p => Math.Abs((p - origin).Dot(normal))) <= PlaneToleranceMm)
        {
            yield return (outer, holes);
            yield break;
        }
        var idx = Polygon.Triangulate(outer, holes.Select(h => (IReadOnlyList<Vec3>)h).ToList());
        for (var i = 0; i < idx.Count; i += 3)
            yield return ([all[idx[i]], all[idx[i + 1]], all[idx[i + 2]]], []);
    }

    /// <summary>
    /// Every definition, each after the definitions it nests. Unused ones are kept too: SketchUp keeps them in the
    /// model's component library ("In Model") until purged.
    /// </summary>
    private static List<ComponentDefinition> PostOrder(Model model)
    {
        var order = new List<ComponentDefinition>();
        var state = new Dictionary<ComponentDefinition, bool>(); // false = visiting, true = done
        void Visit(ComponentDefinition d)
        {
            if (state.TryGetValue(d, out var done))
            {
                if (!done)
                    throw new InvalidOperationException($"Component '{d.Name}' contains itself");
                return;
            }
            state[d] = false;
            foreach (var child in d.Entities.Instances)
                Visit(child.Definition);
            state[d] = true;
            order.Add(d);
        }
        foreach (var inst in model.Entities.Instances)
            Visit(inst.Definition);
        foreach (var d in model.Definitions)
            Visit(d);
        return order;
    }

    /// <summary>
    /// An Image's definition: its one textured face, with the picture pinned to the quad's corners (SketchUp
    /// stretches an Image's picture over it). False when the definition isn't a plain textured quad.
    /// </summary>
    private static bool WriteImageQuad(ComponentDefinition def, Sk.ComponentDefinitionBuilder cb, Context ctx)
    {
        if (def.Entities.Faces is not [var face] || face.OuterLoop.Edges.Count != 4 || face.FrontMaterial is not { Texture: not null } mat)
            return false;
        var pts = face.OuterLoop.Points.ToList();
        var b = Bounds3.FromPoints(pts);
        var size = b.Size;
        if (size.X <= Tolerance.Length || size.Y <= Tolerance.Length || size.Z > Tolerance.Length)
            return false;
        // In texture inches, as OpenSKP's pins take them (see Pins).
        var (tw, th) = (mat.Texture.WidthMm * InchPerMm, mat.Texture.HeightMm * InchPerMm);
        (double, double) Uv(Vec3 p) => ((p.X - b.Min.X) / size.X * tw, (p.Y - b.Min.Y) / size.Y * th);
        cb.AddFace(pts.Select(Inches).ToList(), material: ctx.Material(mat), layer: ctx.Layer(face.Tag),
            frontUv: pts.Take(3).Select(p => new Sk.UvCorrespondence(Inches(p), Uv(p))).ToList());
        return true;
    }

    /// <summary>
    /// A positioned texture as three point ↔ (u, v) pins, which OpenSKP turns back into SketchUp's matrix; null for
    /// the default projection (written as is).
    /// </summary>
    private static List<Sk.UvCorrespondence>? Pins(Face face, bool back, List<Vec3> pointsMm)
    {
        var mapping = back ? face.BackMapping : face.FrontMapping;
        var material = back ? face.BackMaterial : face.FrontMaterial;
        if (mapping == null || material?.Texture == null || pointsMm.Count < 3)
            return null;
        // Three points that span the plane.
        var a = pointsMm[0];
        var b = pointsMm.Skip(1).FirstOrDefault(p => p.DistanceTo(a) > Tolerance.Length);
        var c = pointsMm.Skip(2).FirstOrDefault(p => (b - a).Cross(p - a).Length > 1e-6);
        if (b == default || c == default)
            return null;
        // OpenSKP pairs points with texture inches (tiles × tile size), which its reader divides back into tiles.
        var (tw, th) = (material.Texture.WidthMm * InchPerMm, material.Texture.HeightMm * InchPerMm);
        return [.. new[] { a, b, c }.Select(p =>
        {
            var (u, v) = Texturing.Uv(face, back, p, material);
            return new Sk.UvCorrespondence(Inches(p), (u * tw, v * th));
        })];
    }

    /// <summary>Where each vertex is written: its position, nudged when an earlier vertex already sits there.</summary>
    private static Func<Vertex, Vec3> WrittenPositions(Entities e)
    {
        const double nudgeMm = 2.54e-8; // 1e-9 inch
        var taken = new Dictionary<(long, long, long), int>();
        Dictionary<Vertex, Vec3>? moved = null;
        foreach (var v in e.Vertices)
        {
            var p = v.Position;
            var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
            if (taken.TryGetValue(key, out var n))
            {
                (moved ??= [])[v] = p + new Vec3(nudgeMm * (n + 1), 0, 0);
                taken[key] = n + 1;
            }
            else
            {
                taken[key] = 0;
            }
        }
        return moved == null ? v => v.Position : v => moved.TryGetValue(v, out var q) ? q : v.Position;
    }

    private static ((double, double, double) Translation, double[] Matrix3x3) Placement(Transform t) =>
        (Inches(t.Origin), [t.X.X, t.Y.X, t.Z.X, t.X.Y, t.Y.Y, t.Z.Y, t.X.Z, t.Y.Z, t.Z.Z]);

    private static (double, double, double) Inches(Vec3 p) => (p.X * InchPerMm, p.Y * InchPerMm, p.Z * InchPerMm);

    private sealed class Context(Dictionary<Material, int> materials, Dictionary<Tag, int> tags, List<string> warnings)
    {
        private const int MaxWarnings = 50;
        private readonly Dictionary<ComponentDefinition, bool> _empty = [];

        public Dictionary<ComponentDefinition, Sk.ComponentDefinitionBuilder> Builders { get; } = [];

        /// <summary>Image definitions written as textured quads; their instances become Image entities.</summary>
        public HashSet<ComponentDefinition> Images { get; } = [];

        public int? Material(Material? m) => m != null && materials.TryGetValue(m, out var i) ? i : null;

        public int? Layer(Tag? t) => t != null && tags.TryGetValue(t, out var i) ? i : null;

        public void Warn(string message)
        {
            if (warnings.Count < MaxWarnings)
                warnings.Add(message);
        }

        /// <summary>OpenSKP rejects definitions without geometry, so empty ones (recursively) are dropped.</summary>
        public bool IsEmpty(ComponentDefinition d)
        {
            if (_empty.TryGetValue(d, out var e))
                return e;
            _empty[d] = true; // guards against cycles
            e = d.Entities.Faces.Count == 0 && d.Entities.Edges.Count == 0 && d.Entities.Instances.All(i => IsEmpty(i.Definition));
            _empty[d] = e;
            return e;
        }
    }

    /// <summary>Uniform view over OpenSKP's root builder and definition builders.</summary>
    private sealed class Target
    {
        public required Action<IReadOnlyList<(double, double, double)>, int?, int?, int?, bool, bool, bool, bool, IReadOnlyList<IReadOnlyList<(double, double, double)>>?, IReadOnlyList<Sk.UvCorrespondence>?, IReadOnlyList<Sk.UvCorrespondence>?> Face { get; init; }
        public required Action<(double, double, double), (double, double, double), EdgeFlags> Edge { get; init; }
        public required Action<Sk.ComponentDefinitionBuilder, bool, string?, (double, double, double), double[], int?, int?, bool> Instance { get; init; }
        public required Action<Sk.ComponentDefinitionBuilder, (double, double, double), double[], int?, bool> Image { get; init; }

        public void AddImage(Sk.ComponentDefinitionBuilder def, (double, double, double) t, double[] m3, int? layer, bool hidden) =>
            Image(def, t, m3, layer, hidden);

        public void AddFace(IReadOnlyList<(double, double, double)> pts, int? mat, int? layer, int? back, bool hidden,
            bool soft, bool smooth, bool hiddenEdges, IReadOnlyList<IReadOnlyList<(double, double, double)>>? holes,
            IReadOnlyList<Sk.UvCorrespondence>? frontUv = null, IReadOnlyList<Sk.UvCorrespondence>? backUv = null) =>
            Face(pts, mat, layer, back, hidden, soft, smooth, hiddenEdges, holes, frontUv, backUv);

        public void AddEdge((double, double, double) a, (double, double, double) b, EdgeFlags flags) => Edge(a, b, flags);

        public void AddInstance(Sk.ComponentDefinitionBuilder def, bool group, string? name, (double, double, double) t, double[] m3,
            int? mat, int? layer, bool hidden) => Instance(def, group, name, t, m3, mat, layer, hidden);

        public static Target For(Sk.SkpBuilder b) => new()
        {
            Face = (p, m, l, bk, h, s, sm, he, holes, fu, bu) => b.AddFace(p, m, l, bk, h, s, sm, he, frontUv: fu, backUv: bu, holes: holes),
            Edge = (a, c, f) => b.AddPolyline([a, c], hiddenEdges: f.HasFlag(EdgeFlags.Hidden), softEdges: f.HasFlag(EdgeFlags.Soft), smoothEdges: f.HasFlag(EdgeFlags.Smooth)),
            Image = (d, t, m3, l, h) => b.AddImageInstance(d, t, m3, l, h),
            Instance = (d, group, n, t, m3, m, l, h) =>
            {
                if (group)
                    b.AddGroupInstance(d, n, t, m3, material: m, layer: l, hidden: h);
                else
                    b.AddInstance(d, n, t, m3, material: m, layer: l, hidden: h);
            },
        };

        public static Target For(Sk.ComponentDefinitionBuilder b) => new()
        {
            Face = (p, m, l, bk, h, s, sm, he, holes, fu, bu) => b.AddFace(p, m, l, bk, h, s, sm, he, frontUv: fu, backUv: bu, holes: holes),
            Edge = (a, c, f) => b.AddPolyline([a, c], hiddenEdges: f.HasFlag(EdgeFlags.Hidden), softEdges: f.HasFlag(EdgeFlags.Soft), smoothEdges: f.HasFlag(EdgeFlags.Smooth)),
            Image = (d, t, m3, l, h) => b.AddImageInstance(d, t, m3, l, h),
            Instance = (d, group, n, t, m3, m, l, h) =>
            {
                if (group)
                    b.AddGroupInstance(d, n, t, m3, material: m, layer: l, hidden: h);
                else
                    b.AddInstance(d, n, t, m3, material: m, layer: l, hidden: h);
            },
        };
    }
}
