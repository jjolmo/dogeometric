using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class EraseTests
{
    [Fact]
    public void Erasing_the_edge_between_coplanar_faces_heals_them_into_one()
    {
        var e = new Entities();
        var w = new Welder(e);
        w.Face([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], []);
        w.Face([new(10, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0)], []);
        var shared = e.Edges.Single(x => x.Start.Position.X == 10 && x.End.Position.X == 10);
        Editing.Erase(e, [shared]);
        var face = Assert.Single(e.Faces);
        Assert.Equal(200, Polygon.Area(face.OuterLoop.Points.ToList()), 6);
    }

    [Fact]
    public void Erasing_an_edge_between_faces_at_an_angle_takes_both_faces()
    {
        var e = new Entities();
        var w = new Welder(e);
        w.Face([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], []);
        w.Face([new(10, 0, 0), new(10, 0, 10), new(10, 10, 10), new(10, 10, 0)], []);
        var shared = e.Edges.Single(x => x.Start.Position.X == 10 && x.End.Position.X == 10 && x.Start.Position.Z == 0 && x.End.Position.Z == 0);
        Editing.Erase(e, [shared]);
        Assert.Empty(e.Faces);
    }
}
