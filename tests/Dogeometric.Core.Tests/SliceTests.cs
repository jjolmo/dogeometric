using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SliceTests
{
    [Fact]
    public void A_slice_through_a_box_is_a_group_of_four_edges_round_its_middle()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(100, 60, 40));
        var plane = new SectionPlane(new Vec3(30, 0, 0), new Vec3(1, 0, 0));
        m.Entities.SectionPlanes.Add(plane);
        var group = Intersect.GroupFromSlice(m, plane, m.Entities, Transform.Identity)!;
        var edges = group.Definition.Entities.Edges;
        Assert.Equal(4, edges.Count);
        Assert.All(edges, e => Assert.Equal(30, e.Start.Position.X, 6));
        Assert.Equal(2 * (60 + 40), edges.Sum(e => e.Length), 6);
        // Nothing to slice beside the box.
        Assert.Null(Intersect.GroupFromSlice(m, new SectionPlane(new Vec3(500, 0, 0), Vec3.UnitX), m.Entities, Transform.Identity));
    }
}
