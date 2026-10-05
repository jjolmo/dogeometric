using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Formats.Skp;

namespace Dogeometric.Formats.Tests;

/// <summary>
/// Writing a model as .skp and reading it back must give the same model. Each test pins a case that once came
/// back different from what SketchUp sees in the original file.
/// </summary>
public class SkpRoundTripTests
{
    // Smallest valid 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Model RoundTrip(Model model)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dogeometric-test-{Guid.NewGuid():N}.skp");
        try
        {
            SkpExporter.Export(model, path);
            return SkpImporter.Import(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A quad with vertices of its own, even where other geometry already has one (an unwelded mesh).</summary>
    private static Face LooseQuad(Entities e, params Vec3[] pts)
    {
        var verts = pts.Select(e.AddVertex).ToList();
        var loop = new FaceLoop();
        for (var i = 0; i < verts.Count; i++)
        {
            var edge = e.AddEdge(verts[i], verts[(i + 1) % verts.Count]);
            loop.Edges.Add((edge, false));
        }
        var face = new Face();
        face.Loops.Add(loop);
        e.Faces.Add(face);
        return face;
    }

    [Fact]
    public void A_subdivided_group_is_written_as_its_smooth_surface()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Cage", IsGroup = true };
        var e = def.Entities;
        var v = new[] { new Vec3(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        e.AddFace([v[3], v[2], v[1], v[0]]);
        e.AddFace(v.Select(p => p + new Vec3(0, 0, 10)).ToList());
        for (var i = 0; i < 4; i++)
            e.AddFace([v[i], v[(i + 1) % 4], v[(i + 1) % 4] + new Vec3(0, 0, 10), v[i] + new Vec3(0, 0, 10)]);
        e.Subdivision = 1;
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        var back = RoundTrip(model).Entities.Instances.Single().Definition.Entities;
        Assert.Equal(6, def.Entities.Faces.Count);
        Assert.True(back.Faces.Count > 24);
        Assert.True(back.Vertices.Max(p => p.Position.X) < 10);
    }

    [Fact]
    public void Each_edge_keeps_its_own_softness_and_loose_edges_are_not_curves()
    {
        var model = new Model();
        var e = model.Entities;
        var v = new[] { new Vec3(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        e.AddFace([v[3], v[2], v[1], v[0]]);
        e.AddFace(v.Select(p => p + new Vec3(0, 0, 10)).ToList());
        for (var i = 0; i < 4; i++)
            e.AddFace([v[i], v[(i + 1) % 4], v[(i + 1) % 4] + new Vec3(0, 0, 10), v[i] + new Vec3(0, 0, 10)]);
        var top = e.Edges.First(x => x.Start.Position.Z == 10 && x.End.Position.Z == 10);
        top.Flags = EdgeFlags.Soft | EdgeFlags.Smooth;
        StickyGeometry.DrawEdges(e, [new(20, 0, 0), new(30, 0, 0)]);

        var back = RoundTrip(model).Entities;
        var soft = back.Edges.Where(x => x.Flags.HasFlag(EdgeFlags.Soft)).ToList();
        var edge = Assert.Single(soft);
        Assert.Equal(top.Start.Position.Z, edge.Start.Position.Z, 6);
        Assert.Equal(top.Start.Position.Z, edge.End.Position.Z, 6);
        Assert.Equal(13, back.Edges.Count);
        Assert.All(back.Edges, x => Assert.Null(x.Curve));
    }

    [Fact]
    public void Unwelded_pieces_keep_their_own_vertices_and_edges()
    {
        // Two quads meeting along x = 10 with separate vertices there (CASOPLON's imported meshes are like this):
        // writing must not weld them by position, or edge and vertex counts drop.
        var model = new Model();
        LooseQuad(model.Entities, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0));
        LooseQuad(model.Entities, new(10, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0));
        var back = RoundTrip(model);
        Assert.Equal(2, back.Entities.Faces.Count);
        Assert.Equal(8, back.Entities.Edges.Count);
        Assert.Equal(8, back.Entities.Vertices.Count);
    }

    [Fact]
    public void Image_inside_a_group_stays_an_image()
    {
        // SketchUp nests Images in groups; they used to come back as plain components (more definitions and
        // instances than SketchUp counts).
        var model = new Model();
        var picture = new Material { Name = "Photo", Texture = new TextureImage { FileName = "photo.png", Data = Png, WidthMm = 100, HeightMm = 50 } };
        model.Materials.Add(picture);
        var image = new ComponentDefinition { Name = "Image", IsImage = true };
        image.Entities.AddFace([new(0, 0, 0), new(100, 0, 0), new(100, 50, 0), new(0, 50, 0)]).FrontMaterial = picture;
        var group = new ComponentDefinition { Name = "Group#1", IsGroup = true };
        TestBox(group.Entities);
        group.Entities.AddInstance(image, Transform.Translation(new Vec3(5, 5, 20)));
        model.Definitions.AddRange([image, group]);
        model.Entities.AddInstance(group, Transform.Translation(new Vec3(100, 0, 0)));

        var back = RoundTrip(model);
        var backImage = Assert.Single(back.Definitions, d => d.IsImage);
        var backGroup = Assert.Single(back.Definitions, d => d.IsGroup);
        var placed = Assert.Single(backGroup.Entities.Instances);
        Assert.Same(backImage, placed.Definition);
        Assert.True(placed.Transform.Origin.DistanceTo(new Vec3(5, 5, 20)) < 1e-9);
        Assert.Equal(new Vec3(100, 50, 0), backImage.Entities.Bounds().Size);
        // The picture still spans the whole quad, one tile corner to corner.
        var face = Assert.Single(backImage.Entities.Faces);
        var uvs = face.OuterLoop.Points.Select(p => Texturing.Uv(face, false, p, face.FrontMaterial!)).ToList();
        Assert.Equal(1, uvs.Max(uv => uv.U) - uvs.Min(uv => uv.U), 6);
        Assert.Equal(1, uvs.Max(uv => uv.V) - uvs.Min(uv => uv.V), 6);
    }

    [Fact]
    public void Texture_type_comes_from_its_bytes_not_its_name()
    {
        // A texture stored under a ".skp" name (seen in a real model) was written as a flat colour.
        var model = new Model();
        var mat = new Material { Name = "Label", Texture = new TextureImage { FileName = "Untitled(2).skp", Data = Png, WidthMm = 10, HeightMm = 10 } };
        model.Materials.Add(mat);
        model.Entities.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]).FrontMaterial = mat;
        var back = RoundTrip(model);
        var backMat = Assert.Single(back.Materials, m => m.Name == "Label");
        Assert.NotNull(backMat.Texture);
        Assert.Equal(Png, backMat.Texture!.Data);
    }

    [Fact]
    public void Instance_positions_survive()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Part" };
        TestBox(def.Entities);
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Rotation(Vec3.UnitZ, Math.PI / 2).Then(Transform.Translation(new Vec3(1234, -567, 89))));
        var back = RoundTrip(model);
        var inst = Assert.Single(back.Entities.Instances);
        Assert.Equal(1234, inst.Transform.Origin.X, 6);
        Assert.Equal(-567, inst.Transform.Origin.Y, 6);
        Assert.Equal(89, inst.Transform.Origin.Z, 6);
        Assert.Equal(1, inst.Transform.X.Y, 9);
    }

    [Fact]
    public void SketchUp_2019_tutorial_reads_when_the_private_corpus_is_present()
    {
        // Tutorial01.skp (SketchUp 2019 legacy format: a trailing layer-list null and null records in entity lists)
        // ships with SketchUp, so it can't live in this repository; the check runs where the corpus is.
        var root = Environment.GetEnvironmentVariable("DOGEOMETRIC_SKP_CORPUS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dogeometric-corpus");
        var file = Directory.Exists(root) ? Directory.EnumerateFiles(root, "Tutorial01.skp", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null)
            return;
        var model = SkpImporter.Import(file);
        // SketchUp 2021's own figures for this file: 8840 edges, 144 faces, 6 definitions.
        Assert.Equal(8840, model.AllEntities.Sum(e => e.Edges.Count));
        Assert.Equal(144, model.AllEntities.Sum(e => e.Faces.Count));
        Assert.Equal(6, model.Definitions.Count);
    }

    [Fact]
    public void Positioned_texture_keeps_its_placement()
    {
        // A label placed on a face with pins (rotated and scaled): the texture coordinates of every corner must
        // come back the same.
        var model = new Model();
        var mat = new Material { Name = "Logo", Texture = new TextureImage { FileName = "logo.png", Data = Png, WidthMm = 40, HeightMm = 20 } };
        model.Materials.Add(mat);
        var face = model.Entities.AddFace([new(0, 0, 0), new(100, 0, 0), new(100, 60, 0), new(0, 60, 0)]);
        face.FrontMaterial = mat;
        var (ox, oy) = Texturing.PlanePoint(face, new Vec3(10, 5, 0));
        var (ux, uy) = Texturing.PlanePoint(face, new Vec3(40, 25, 0));
        var (vx, vy) = Texturing.PlanePoint(face, new Vec3(-5, 25, 0));
        face.FrontMapping = TextureMapping.FromPlanePoints((ox, oy), (ux, uy), (vx, vy), 40, 20);
        var before = face.OuterLoop.Points.Select(p => Texturing.Uv(face, false, p, mat)).ToList();

        var back = RoundTrip(model);
        var f = Assert.Single(back.Entities.Faces);
        Assert.NotNull(f.FrontMapping);
        var after = f.OuterLoop.Points.Select(p => Texturing.Uv(f, false, p, f.FrontMaterial!)).ToList();
        foreach (var p in face.OuterLoop.Points)
        {
            var i = face.OuterLoop.Points.ToList().IndexOf(p);
            var j = f.OuterLoop.Points.ToList().FindIndex(q => q.DistanceTo(p) < 1e-3);
            Assert.Equal(before[i].U, after[j].U, 4);
            Assert.Equal(before[i].V, after[j].V, 4);
        }
    }

    private static void TestBox(Entities e)
    {
        e.AddFace([new(0, 0, 0), new(0, 10, 0), new(10, 10, 0), new(10, 0, 0)]);
        e.AddFace([new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10)]);
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 0, 10), new(0, 0, 10)]);
        e.AddFace([new(0, 10, 0), new(0, 10, 10), new(10, 10, 10), new(10, 10, 0)]);
        e.AddFace([new(0, 0, 0), new(0, 0, 10), new(0, 10, 10), new(0, 10, 0)]);
        e.AddFace([new(10, 0, 0), new(10, 10, 0), new(10, 10, 10), new(10, 0, 10)]);
    }

    [Fact]
    public void An_empty_model_saves_as_sketchups_empty_document()
    {
        var back = RoundTrip(new Model());
        Assert.True(back.AllEntities.All(e => e.Faces.Count == 0 && e.Edges.Count == 0 && e.Instances.Count == 0));
    }

    [Fact]
    public void A_model_with_only_tags_keeps_them_with_a_guide_point()
    {
        var model = new Model();
        model.GetOrAddTag("Lid");
        var back = RoundTrip(model);
        Assert.Contains(back.Tags, t => t.Name == "Lid");
    }
}
