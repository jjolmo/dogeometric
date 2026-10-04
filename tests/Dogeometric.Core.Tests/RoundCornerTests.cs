using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class RoundCornerTests
{
    /// <summary>An extruded prism, its profile counter-clockwise seen from above.</summary>
    private static Entities Prism(IReadOnlyList<Vec3> profile, double h)
    {
        var e = new Entities();
        var top = profile.Select(p => p + new Vec3(0, 0, h)).ToList();
        e.AddFace(top);
        e.AddFace(profile.Reverse().ToList());
        for (var i = 0; i < profile.Count; i++)
        {
            var j = (i + 1) % profile.Count;
            e.AddFace([profile[i], profile[j], top[j], top[i]]);
        }
        return e;
    }

    private static Entities Cube(double a) => Prism([new(0, 0, 0), new(a, 0, 0), new(a, a, 0), new(0, a, 0)], a);

    private static MeshCheck.Report Check(Entities e)
    {
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        return MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
    }

    private static void AssertSolid(Entities e)
    {
        Assert.Empty(SolidInspector.Find(e));
        Assert.True(Check(e).IsWatertight);
    }

    [Theory]
    [InlineData(RoundCornerMode.Round)]
    [InlineData(RoundCornerMode.Sharp)]
    [InlineData(RoundCornerMode.Bevel)]
    public void A_cube_with_every_edge_done_stays_a_solid(RoundCornerMode mode)
    {
        var e = Cube(10);
        var result = RoundCorner.Apply(e, e.Edges.ToList(), 2, 6, mode);
        Assert.Equal(12, result.Edges);
        Assert.Equal(8, result.Corners);
        Assert.Empty(result.Problems);
        AssertSolid(e);
    }

    [Fact]
    public void Bevelled_cube_volume_is_exact()
    {
        var e = Cube(10);
        RoundCorner.Apply(e, e.Edges.ToList(), 2, 6, RoundCornerMode.Bevel);
        // 1000 minus 12 prisms of 2 x (10 - 4) and 8 corners of 8 - 4/3.
        Assert.Equal(1000 - 12 * 2 * 6 - 8 * (8 - 4.0 / 3), Check(e).Volume, 6);
    }

    [Fact]
    public void Rounded_cube_volume_approaches_the_exact_one()
    {
        var e = Cube(10);
        RoundCorner.Apply(e, e.Edges.ToList(), 2, 12, RoundCornerMode.Round);
        double a = 10, r = 2, core = a - 2 * r;
        var exact = core * core * core + 6 * core * core * r + 3 * Math.PI * r * r * core + 4.0 / 3 * Math.PI * r * r * r;
        Assert.InRange(Check(e).Volume, exact * 0.99, exact);
    }

    [Fact]
    public void One_edge_ends_on_the_caps()
    {
        var e = Cube(10);
        var edge = e.Edges.First(x => x.Start.Position.Z > 9 && x.End.Position.Z > 9);
        RoundCorner.Apply(e, [edge], 2, 6, RoundCornerMode.Round);
        AssertSolid(e);
        // Two caps took the arc in; no extra corner faces.
        Assert.Equal(6 + 6, e.Faces.Count);
    }

    [Fact]
    public void The_curved_rim_of_a_cylinder_rounds_smoothly()
    {
        var e = Prism(Enumerable.Range(0, 24).Select(i => new Vec3(5 * Math.Cos(i * Math.PI / 12), 5 * Math.Sin(i * Math.PI / 12), 0)).ToList(), 8);
        var rim = e.Edges.Where(x => x.Start.Position.Z > 7.9 && x.End.Position.Z > 7.9).ToList();
        var result = RoundCorner.Apply(e, rim, 1.5, 6, RoundCornerMode.Round);
        Assert.Equal(24, result.Edges);
        AssertSolid(e);
        Assert.All(e.Edges.Where(x => x.Start.Position.Z > 6.5 && x.End.Position.Z > 6.5 && x.Start.Position.Z < 7.99),
            x => Assert.True(x.Flags.HasFlag(EdgeFlags.Soft)));
    }

    [Theory]
    [InlineData(RoundCornerMode.Round)]
    [InlineData(RoundCornerMode.Sharp)]
    [InlineData(RoundCornerMode.Bevel)]
    public void Concave_edges_and_mixed_corners_stay_solid(RoundCornerMode mode)
    {
        var e = Prism([new(0, 0, 0), new(20, 0, 0), new(20, 5, 0), new(5, 5, 0), new(5, 20, 0), new(0, 20, 0)], 10);
        var result = RoundCorner.Apply(e, e.Edges.ToList(), 1, 6, mode);
        Assert.Empty(result.Problems);
        AssertSolid(e);
    }

    [Fact]
    public void Open_corners_are_reported_and_left_out()
    {
        var e = Cube(10);
        e.Faces.Remove(e.Faces.Single(f => f.Normal.Z > 0.5));
        var result = RoundCorner.Apply(e, e.Edges.ToList(), 1, 6, RoundCornerMode.Round);
        Assert.NotEmpty(result.Problems);
    }
}
