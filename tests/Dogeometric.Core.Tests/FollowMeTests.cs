using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class FollowMeTests
{
    private static MeshCheck.Report Check(Entities e)
    {
        var m = new Model();
        m.Entities.Faces.AddRange(e.Faces);
        return MeshCheck.Analyze(MeshExtractor.Extract(m));
    }

    [Fact]
    public void Square_along_an_L_path_gives_a_mitred_closed_solid()
    {
        var e = new Entities();
        var profile = e.AddFace([new(-5, 0, 0), new(5, 0, 0), new(5, 0, 10), new(-5, 0, 10)]);
        List<Edge> path = [e.EdgeBetween(e.VertexAt(Vec3.Zero), e.VertexAt(new(0, 100, 0))), e.EdgeBetween(e.VertexAt(new(0, 100, 0)), e.VertexAt(new(100, 100, 0)))];
        FollowMe.Apply(e, profile, path);
        var r = Check(e);
        Assert.True(r.IsWatertight);
        Assert.Equal(10 * 10 * 200, r.Volume, 6);
        Assert.Equal(10, e.Faces.Count);
    }

    [Fact]
    public void Profile_around_a_circle_makes_a_smooth_ring()
    {
        var e = new Entities();
        var profile = e.AddFace([new(50, 0, 0), new(60, 0, 0), new(60, 0, 20), new(50, 0, 20)]);
        var curve = new Curve { Radius = 50, Segments = 24 };
        var path = new List<Edge>();
        for (var i = 0; i < 24; i++)
        {
            double a0 = 2 * Math.PI * i / 24, a1 = 2 * Math.PI * (i + 1) / 24;
            var edge = e.EdgeBetween(e.VertexAt(new(50 * Math.Cos(a0), 50 * Math.Sin(a0), 0)), e.VertexAt(new(50 * Math.Cos(a1), 50 * Math.Sin(a1), 0)));
            edge.Curve = curve;
            path.Add(edge);
        }
        FollowMe.Apply(e, profile, path);
        Assert.True(Check(e).IsWatertight);
        Assert.DoesNotContain(profile, e.Faces);
        Assert.Equal(4 * 24, e.Faces.Count);
        Assert.Equal(4 * 24, e.Edges.Count(x => x.Flags.HasFlag(EdgeFlags.Soft)));
    }

    [Fact]
    public void Branching_edges_are_not_a_path()
    {
        var e = new Entities();
        var c = e.VertexAt(Vec3.Zero);
        List<Edge> edges = [e.EdgeBetween(c, e.VertexAt(new(1, 0, 0))), e.EdgeBetween(c, e.VertexAt(new(0, 1, 0))), e.EdgeBetween(c, e.VertexAt(new(0, 0, 1)))];
        Assert.Null(FollowMe.OrderPath(edges, Vec3.Zero));
    }

    [Fact]
    public void Text_outline_with_a_counter_extrudes_to_a_ring()
    {
        var e = new Entities();
        IReadOnlyList<Vec3> outer = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)];
        IReadOnlyList<Vec3> counter = [new(3, 3, 0), new(7, 3, 0), new(7, 7, 0), new(3, 7, 0)];
        Text3D.Build(e, [outer, counter], filled: true, extrude: 2);
        var r = Check(e);
        Assert.True(r.IsWatertight);
        Assert.Equal((100 - 16) * 2, r.Volume, 6);
    }
}
