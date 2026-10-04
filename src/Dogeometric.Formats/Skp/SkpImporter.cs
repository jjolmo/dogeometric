using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Dogeometric.Core.View;
using Sk = OpenSkp;

namespace Dogeometric.Formats.Skp;

/// <summary>Reads SketchUp .skp files (2013–2020 and 2021+) through OpenSKP into a Dogeometric model.</summary>
public static class SkpImporter
{
    private const double MmPerInch = 25.4;

    public static Model Import(string path)
    {
        var model = Convert(Sk.SkpFile.Open(path));
        if (SkpShadows.Read(path) is { } shadows)
            model.Shadows = shadows;
        return model;
    }

    public static Model Convert(Sk.SkpModel skp)
    {
        var model = new Model { SourceVersion = $"SketchUp {skp.Version.Trim('{', '}')}", Units = UnitsFrom(skp.Units) };

        foreach (var layer in skp.Layers)
        {
            var tag = model.GetOrAddTag(layer.Name);
            tag.Color = new Rgba((byte)layer.ColorR, (byte)layer.ColorG, (byte)layer.ColorB);
            tag.Visible = !layer.Hidden;
        }

        var materials = new Dictionary<Sk.Material, Material>();
        var layerNames = skp.Layers.Select(l => l.Name).ToHashSet();
        foreach (var m in skp.Materials)
        {
            // VFF files carry each tag as a "Layer_<name>" pseudo-material; SketchUp doesn't list those as materials.
            if (m.Name.StartsWith("Layer_", StringComparison.Ordinal) && layerNames.Contains(m.Name["Layer_".Length..]))
                continue;
            var mat = new Material
            {
                Name = m.Name,
                Color = new Rgba((byte)m.Color.R, (byte)m.Color.G, (byte)m.Color.B, (byte)m.Color.A),
                Opacity = Math.Clamp(m.Transparency, 0, 1),
            };
            if (m.Texture?.Data is { } data)
            {
                mat.Texture = new TextureImage
                {
                    FileName = Path.GetFileName(m.Texture.Filename),
                    Data = data,
                    WidthMm = m.Texture.Width * MmPerInch,
                    HeightMm = m.Texture.Height * MmPerInch,
                };
            }
            materials[m] = mat;
            model.Materials.Add(mat);
        }
        Material? MaterialById(long? id) =>
            id is { } i && skp.MaterialsById.TryGetValue(i, out var m) && materials.TryGetValue(m, out var mat) ? mat : null;

        // Definitions first so instances can point at them; OpenSKP keys them by entity id.
        var definitions = new Dictionary<long, ComponentDefinition>();
        foreach (var (id, d) in skp.Definitions)
        {
            var def = new ComponentDefinition
            {
                Name = d.Name,
                IsGroup = d.IsGroup,
                IsImage = d.IsImage,
                AlwaysFaceCamera = d.AlwaysFacesCamera,
                ShadowsFaceSun = d.ShadowsFaceSun,
            };
            definitions[id] = def;
            model.Definitions.Add(def);
        }

        foreach (var (id, d) in skp.Definitions)
            FillEntities(d, definitions[id].Entities, definitions, MaterialById, model);
        FillEntities(skp.Root, model.Entities, definitions, MaterialById, model);

        foreach (var page in skp.Pages)
        {
            var scene = new Scene { Name = page.Name };
            if (page is { Eye: { } eye, Target: { } target })
            {
                var up = page.Up is { } u ? new Vec3(u.X, u.Y, u.Z) : Vec3.UnitZ;
                scene.Camera = new CameraState(ToMm(eye), ToMm(target), up, !page.Parallel, page.Fov, page.OrthoHeight * MmPerInch);
            }
            foreach (var hidden in page.HiddenLayers)
                scene.HiddenTags.Add(hidden);
            model.Scenes.Add(scene);
        }
        return model;
    }

    private static void FillEntities(Sk.Definition d, Entities target, Dictionary<long, ComponentDefinition> definitions,
        Func<long?, Material?> materialById, Model model)
    {
        var vertices = new Dictionary<long, Vertex>(d.Vertices.Count);
        foreach (var (id, v) in d.Vertices)
            vertices[id] = target.AddVertex(new Vec3(v.X, v.Y, v.Z) * MmPerInch);

        var edges = new Dictionary<long, Edge>(d.Edges.Count);
        foreach (var (id, e) in d.Edges)
        {
            if (!vertices.TryGetValue(e.V1Id, out var a) || !vertices.TryGetValue(e.V2Id, out var b))
                continue;
            var edge = target.AddEdge(a, b);
            edge.Flags = (e.Soft ? EdgeFlags.Soft : 0) | (e.Smooth ? EdgeFlags.Smooth : 0) | (e.Hidden ? EdgeFlags.Hidden : 0);
            edges[id] = edge;
        }

        foreach (var (_, f) in d.Faces)
        {
            var face = new Face
            {
                FrontMaterial = materialById(f.MaterialId),
                BackMaterial = materialById(f.BackMaterialId),
                // Positioned textures (SketchUp's pins); null keeps the default projection.
                FrontMapping = f.UvTransform is { Length: 9 } fm ? new TextureMapping(fm) : null,
                BackMapping = f.UvTransformBack is { Length: 9 } bm ? new TextureMapping(bm) : null,
                Hidden = f.Hidden,
            };
            foreach (var loop in f.Loops)
            {
                var fl = new FaceLoop();
                foreach (var (edgeId, orientation) in loop)
                {
                    if (edges.TryGetValue(edgeId, out var edge))
                        fl.Edges.Add((edge, orientation < 0));
                }
                if (fl.Edges.Count >= 3)
                    face.Loops.Add(fl);
            }
            if (face.Loops.Count == 0)
                continue;
            OrientLike(face, f.Normal);
            target.Faces.Add(face);
        }

        foreach (var cl in d.ConstructionLines)
        {
            var line = new GuideLine(ToMm(cl.Point), new Vec3(cl.Direction.X, cl.Direction.Y, cl.Direction.Z));
            if (cl.Start is { } s && cl.End is { } en)
            {
                line.Start = ToMm(s);
                line.End = ToMm(en);
            }
            target.GuideLines.Add(line);
        }
        foreach (var cp in d.ConstructionPoints)
            target.GuidePoints.Add(new GuidePoint(ToMm(cp.Position)));

        foreach (var inst in d.Instances)
        {
            if (inst.RefIdx is not { } refId || !definitions.TryGetValue(refId, out var def))
                continue;
            // OpenSKP hands over SketchUp's 13-element matrix (its doc comment says 16 column-major; it isn't).
            var xf = inst.Matrix.Count >= 16 ? Transform.FromColumnMajor(inst.Matrix) : Transform.FromSketchUp13(inst.Matrix);
            var ci = target.AddInstance(def, xf with { Origin = xf.Origin * MmPerInch });
            ci.Name = inst.Name;
            ci.Hidden = inst.Hidden;
            ci.Material = materialById(inst.MaterialId);
            if (inst.Layer.Length > 0)
                ci.Tag = model.GetOrAddTag(inst.Layer);
        }
    }

    /// <summary>
    /// Makes the outer loop's winding agree with the stored front normal. The loop direction OpenSKP reports is
    /// edge-use order; when it disagrees with the normal SketchUp saved, the face's front is the other side.
    /// </summary>
    private static void OrientLike(Face face, (double Nx, double Ny, double Nz)? stored)
    {
        if (stored is not { } n)
            return;
        var normal = new Vec3(n.Nx, n.Ny, n.Nz);
        if (face.Normal.Dot(normal) >= 0)
            return;
        foreach (var loop in face.Loops)
        {
            loop.Edges.Reverse();
            for (var i = 0; i < loop.Edges.Count; i++)
                loop.Edges[i] = (loop.Edges[i].Edge, !loop.Edges[i].Reversed);
        }
    }

    private static Vec3 ToMm((double X, double Y, double Z) p) => new Vec3(p.X, p.Y, p.Z) * MmPerInch;

    private static LengthUnit UnitsFrom(string? units) => units?.ToLowerInvariant() switch
    {
        { } u when u.StartsWith("centim") => LengthUnit.Centimeters,
        { } u when u.StartsWith("meter") || u.StartsWith("metre") => LengthUnit.Meters,
        _ => LengthUnit.Millimeters,
    };
}
