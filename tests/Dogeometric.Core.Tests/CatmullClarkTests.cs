using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CatmullClarkTests
{
    [Theory]
    [InlineData(1, 416.7)]
    [InlineData(2, 350.1)]
    public void A_cube_rounds_off_and_stays_a_solid(int levels, double volume)
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        CatmullClark.Apply(e, levels);
        Assert.Empty(SolidInspector.Find(e));
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        var check = MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
        Assert.True(check.IsWatertight);
        Assert.Equal(volume, check.Volume, 0);
    }

    [Fact]
    public void An_open_surface_keeps_its_border()
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        CatmullClark.Apply(e, 1);
        Assert.Equal(4, e.Faces.Count);
        Assert.Equal(100, e.Faces.Sum(f => f.Area), 6);
    }

    private static Entities Cube()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        return e;
    }

    private static Bounds3 BoundsOf(CatmullClark.Result r) => r.Points.Aggregate(Bounds3.Empty, (b, p) => b.Include(p));

    [Fact]
    public void Infinitely_sharp_edges_keep_the_cube_square()
    {
        var e = Cube();
        foreach (var edge in e.Edges)
            edge.Crease = double.PositiveInfinity;
        var r = CatmullClark.Mesh(e, 3);
        var used = r.Polygons.SelectMany(p => p.Corners).Distinct().Select(i => r.Points[i]).ToList();
        // Every point stays on the cube's surface and its corners stay put.
        Assert.All(used, p => Assert.Contains(new[] { p.X, p.Y, p.Z }, c => Math.Abs(c) < 1e-9 || Math.Abs(c - 10) < 1e-9));
        Assert.Contains(used, p => p.DistanceTo(new Vec3(10, 10, 10)) < 1e-9);
        Assert.Equal(12 * 8, r.Hard.Count);
    }

    [Fact]
    public void A_creased_loop_keeps_the_top_flat_while_the_rest_rounds_off()
    {
        var e = Cube();
        foreach (var edge in e.Edges.Where(x => x.Start.Position.Z == 10 && x.End.Position.Z == 10))
            edge.Crease = double.PositiveInfinity;
        var creased = BoundsOf(CatmullClark.Mesh(e, 3));
        var smooth = BoundsOf(CatmullClark.Mesh(Cube(), 3));
        Assert.Equal(10, creased.Max.Z, 9);
        Assert.True(smooth.Max.Z < 9.5);
        Assert.Equal(4 * 8, CatmullClark.Mesh(e, 3).Hard.Count);
    }

    [Fact]
    public void Fractional_sharpness_lands_between_smooth_and_sharp()
    {
        double Corner(double sharpness)
        {
            var e = Cube();
            foreach (var edge in e.Edges)
                edge.Crease = sharpness;
            return CatmullClark.Mesh(e, 1).Points.Max(p => p.X + p.Y + p.Z);
        }
        var (smooth, half, sharp) = (Corner(0), Corner(0.5), Corner(1));
        Assert.True(smooth < half && half < sharp, $"{smooth} {half} {sharp}");
        Assert.Equal(30, sharp, 9);
    }

    [Fact]
    public void Creases_and_subdivision_survive_saving_and_undo()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Cage", IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        var doc = new Document(model);
        doc.Undo.Begin("Crease", def.Entities);
        def.Entities.Subdivision = 2;
        def.Entities.Edges[0].Crease = double.PositiveInfinity;
        def.Entities.Edges[1].Crease = 1.5;
        def.Entities.Vertices[0].Crease = 2;
        doc.Undo.Commit();

        var path = Path.Combine(Path.GetTempPath(), $"crease-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Definitions[0].Entities;
        File.Delete(path);
        Assert.Equal(2, back.Subdivision);
        Assert.Equal(double.PositiveInfinity, back.Edges[0].Crease);
        Assert.Equal(1.5, back.Edges[1].Crease);
        Assert.Equal(2, back.Vertices[0].Crease);

        doc.Undo.Undo();
        Assert.Equal(0, def.Entities.Subdivision);
        Assert.All(def.Entities.Edges, x => Assert.Equal(0, x.Crease));
        Assert.Equal(0, def.Entities.Vertices[0].Crease);
    }

    [Fact]
    public void A_subdivided_group_exports_its_smooth_surface()
    {
        var def = new ComponentDefinition { IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        def.Entities.Subdivision = 2;
        var check = MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
        Assert.True(check.IsWatertight);
        Assert.Equal(350.1, check.Volume, 0);
        var shown = CatmullClark.ShownBounds(def.Entities);
        Assert.True(shown.Max.X < 10 && shown.Min.X > 0);
        Assert.Equal(8, def.Entities.Vertices.Count);
    }
}
