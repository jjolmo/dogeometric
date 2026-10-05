using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class OutlinerMoveTests
{
    [Fact]
    public void Moving_a_group_into_another_keeps_it_in_place_and_refuses_cycles()
    {
        var model = new Model();
        var outerDef = new ComponentDefinition { Name = "Outer", IsGroup = true };
        var innerDef = new ComponentDefinition { Name = "Inner", IsGroup = true };
        model.Definitions.AddRange([outerDef, innerDef]);
        var outer = model.Entities.AddInstance(outerDef, Transform.Translation(new Vec3(100, 0, 0)));
        var inner = model.Entities.AddInstance(innerDef, Transform.Translation(new Vec3(130, 20, 0)));
        var outerWorld = outer.Transform;

        Assert.True(Grouping.MoveInto(inner, model.Entities, Transform.Identity, outerDef.Entities, outerWorld));
        Assert.DoesNotContain(inner, model.Entities.Instances);
        Assert.Contains(inner, outerDef.Entities.Instances);
        Assert.Equal(new Vec3(130, 20, 0), inner.Transform.Then(outerWorld).ApplyPoint(Vec3.Zero));

        // The outer group can't go inside the group it now holds.
        Assert.False(Grouping.MoveInto(outer, model.Entities, Transform.Identity, outerDef.Entities, outerWorld));
    }
}
