using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class DynamicComponentsTests
{
    private static ComponentInstance Lid(string onClick, Transform at)
    {
        var def = new ComponentDefinition { Name = "Lid" };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(40, 30, 2));
        def.Attributes.Add(new ComponentAttribute { Name = "onClick", Value = onClick });
        return new ComponentInstance(def) { Transform = at };
    }

    [Fact]
    public void A_lid_opens_on_its_hinge_and_closes_on_the_next_click()
    {
        var lid = Lid("ANIMATE(\"rotx\",0,90)", Transform.Translation(new Vec3(0, 0, 20)));
        Assert.True(DynamicComponents.CanInteract(lid));
        Assert.True(DynamicComponents.Click(lid));
        // Turned a quarter about its own red axis through its origin: its far edge now points up.
        Assert.True(lid.Transform.ApplyPoint(new Vec3(0, 30, 0)).DistanceTo(new Vec3(0, 0, 50)) < 1e-9);
        Assert.True(lid.Transform.ApplyPoint(Vec3.Zero).DistanceTo(new Vec3(0, 0, 20)) < 1e-9);
        Assert.True(DynamicComponents.Click(lid));
        Assert.True(lid.Transform.ApplyPoint(new Vec3(0, 30, 0)).DistanceTo(new Vec3(0, 30, 20)) < 1e-9);
    }

    [Fact]
    public void Set_cycles_through_its_values_and_unknown_actions_do_nothing()
    {
        var drawer = Lid("SET(\"x\",0,25,50)", Transform.Identity);
        DynamicComponents.Click(drawer);
        DynamicComponents.Click(drawer);
        Assert.Equal(50, drawer.Transform.Origin.X, 9);
        DynamicComponents.Click(drawer);
        Assert.Equal(0, drawer.Transform.Origin.X, 9);
        Assert.False(DynamicComponents.Click(Lid("REDRAW()", Transform.Identity)));
    }
}
