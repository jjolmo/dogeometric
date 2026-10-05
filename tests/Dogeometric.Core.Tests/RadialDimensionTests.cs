using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class RadialDimensionTests
{
    private static Edge Curve(Entities e, double radius, bool closed)
    {
        var centre = new Vec3(10, 10, 0);
        var curve = new Curve { Center = centre, Normal = Vec3.UnitZ, Radius = radius, Segments = 24 };
        var pts = closed
            ? Shapes.RegularPolygon(centre, Vec3.UnitZ, Vec3.UnitX, radius, 24)
            : Shapes.CenterArc(centre, Vec3.UnitZ, centre + Vec3.UnitX * radius, Math.PI / 2, 6);
        StickyGeometry.DrawEdges(e, pts, closed, Vec3.UnitZ, curve);
        return e.Edges.First(x => x.Curve == curve);
    }

    [Fact]
    public void A_circle_measures_its_diameter_and_an_arc_its_radius()
    {
        var e = new Entities();
        var circle = RadialDimensions.For(e, Curve(e, 8, closed: true), new Vec3(30, 10, 0))!;
        Assert.Equal(DimensionKind.Diameter, circle.Kind);
        Assert.Equal(16, circle.Length, 9);
        Assert.Equal(new Vec3(18, 10, 0), circle.Start);

        var arcs = new Entities();
        var arc = RadialDimensions.For(arcs, Curve(arcs, 5, closed: false), new Vec3(20, 20, 0))!;
        Assert.Equal(DimensionKind.Radius, arc.Kind);
        Assert.Equal(5, arc.Length, 9);
        Assert.Equal("R", arc.Prefix);
    }

    [Fact]
    public void Switching_type_keeps_the_arrow_and_is_saved()
    {
        var m = new Model();
        var d = RadialDimensions.For(m.Entities, Curve(m.Entities, 8, closed: true), new Vec3(30, 10, 0))!;
        RadialDimensions.SetKind(d, DimensionKind.Radius);
        Assert.Equal(8, d.Length, 9);
        Assert.Equal(new Vec3(18, 10, 0), d.Start);
        m.Entities.Dimensions.Add(d);
        var path = Path.Combine(Path.GetTempPath(), $"radial-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(m, path);
            Assert.Equal(DimensionKind.Radius, Assert.Single(DogFile.Load(path).Entities.Dimensions).Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_straight_edge_has_no_radial_dimension()
    {
        var e = new Entities();
        StickyGeometry.DrawEdges(e, [Vec3.Zero, new Vec3(10, 0, 0)]);
        Assert.Null(RadialDimensions.For(e, e.Edges[0], Vec3.Zero));
    }
}
