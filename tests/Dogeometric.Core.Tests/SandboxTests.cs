using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SandboxTests
{
    private static int Borders(Entities e) => e.Edges.Count(x => Topology.FacesOf(e, x).Count() == 1);

    [Fact]
    public void From_scratch_makes_a_flat_grid_of_triangles_with_soft_diagonals()
    {
        var e = new Entities();
        Sandbox.Grid(e, Vec3.Zero, Vec3.UnitX, 100, Vec3.UnitY, 80, 10);
        Assert.Equal(160, e.Faces.Count);
        Assert.All(e.Faces, f => Assert.True(f.Normal.Z > 0.99));
        Assert.Equal(80, e.Edges.Count(x => x.Flags.HasFlag(EdgeFlags.Soft)));
        Assert.Equal(36, Borders(e));
    }

    [Fact]
    public void Concentric_contours_triangulate_cleanly()
    {
        var contours = new Entities();
        foreach (var (r, z) in new[] { (60.0, 0.0), (40.0, 10.0), (20.0, 20.0) })
            StickyGeometry.DrawEdges(contours, Shapes.RegularPolygon(Vec3.Zero + new Vec3(0, 0, z), Vec3.UnitZ, Vec3.UnitX, r, 24, false), closed: true, Vec3.UnitZ, null);
        var e = new Entities();
        // 72 points, 24 on the hull: 2n - h - 2 triangles.
        Assert.Equal(118, Sandbox.FromContours(e, contours.Edges));
        Assert.All(e.Faces, f => Assert.True(f.Normal.Z > 0));
        Assert.Equal(24, Borders(e));
        Assert.DoesNotContain(e.Edges, x => Topology.FacesOf(e, x).Count() > 2);
    }

    [Fact]
    public void Smoove_raises_the_centre_most_and_leaves_the_rim()
    {
        var e = new Entities();
        Sandbox.Grid(e, Vec3.Zero, Vec3.UnitX, 100, Vec3.UnitY, 100, 10);
        Sandbox.Smoove(e, new Vec3(50, 50, 0), 30, Vec3.UnitZ, 20);
        Assert.Equal(20, e.Vertices.Max(v => v.Position.Z), 9);
        Assert.All(e.Vertices.Where(v => new Vec3(v.Position.X - 50, v.Position.Y - 50, 0).Length >= 30), v => Assert.Equal(0, v.Position.Z));
    }

    [Fact]
    public void Add_detail_and_flip_edge_keep_the_surface_whole()
    {
        var e = new Entities();
        Sandbox.Grid(e, Vec3.Zero, Vec3.UnitX, 100, Vec3.UnitY, 80, 10);
        var interior = e.Faces.Where(f => f.OuterLoop.Points.All(p => p.X > 5 && p.X < 95 && p.Y > 5 && p.Y < 75)).Take(4).ToList();
        Assert.Equal(12, Sandbox.AddDetail(e, interior));
        Assert.Equal(36, Borders(e));
        var diagonal = e.Edges.First(x => x.Flags.HasFlag(EdgeFlags.Soft) && Topology.FacesOf(e, x).Count() == 2 && Topology.FacesOf(e, x).All(f => f.OuterLoop.Edges.Count == 3));
        Assert.True(Sandbox.FlipEdge(e, diagonal));
        Assert.Equal(36, Borders(e));
    }

    [Fact]
    public void Stamp_flattens_the_footprint_and_slopes_back_to_the_terrain()
    {
        var e = new Entities();
        Sandbox.Grid(e, Vec3.Zero, Vec3.UnitX, 100, Vec3.UnitY, 100, 10);
        Sandbox.Smoove(e, new Vec3(50, 50, 0), 45, Vec3.UnitZ, 20);
        var before = e.Vertices.ToDictionary(v => (Math.Round(v.Position.X, 6), Math.Round(v.Position.Y, 6)), v => v.Position.Z);
        Vec3[] square = [new(40, 40, 0), new(60, 40, 0), new(60, 60, 0), new(40, 60, 0)];
        Assert.True(Sandbox.Stamp(e, square, 10, 15) > 0);

        bool In(Vec3 p, double m) => p.X >= 40 - m && p.X <= 60 + m && p.Y >= 40 - m && p.Y <= 60 + m;
        Assert.All(e.Vertices.Where(v => In(v.Position, 1e-6)), v => Assert.Equal(10, v.Position.Z, 6));
        Assert.All(e.Vertices.Where(v => !In(v.Position, 15 + 1e-6) && before.ContainsKey((Math.Round(v.Position.X, 6), Math.Round(v.Position.Y, 6)))),
            v => Assert.Equal(before[(Math.Round(v.Position.X, 6), Math.Round(v.Position.Y, 6))], v.Position.Z, 6));
        Assert.Contains(e.Vertices, v => v.Position.DistanceTo(new Vec3(40, 40, 10)) < 1e-6);
        Assert.Equal(40, Borders(e));
        Assert.DoesNotContain(e.Edges, x => Topology.FacesOf(e, x).Count() > 2);
        Assert.All(e.Faces, f => Assert.True(f.Normal.Z > 0));
    }
}
