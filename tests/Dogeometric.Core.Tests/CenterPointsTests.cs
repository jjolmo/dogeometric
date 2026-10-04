using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CenterPointsTests
{
    [Fact]
    public void Groups_faces_and_selection_have_their_box_middles()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        var e = model.Entities;
        e.AddFace([new Vec3(0, 0, 500), new Vec3(100, 0, 500), new Vec3(100, 40, 500)]);
        var centers = CenterPoints.Of(e, Transform.Identity, [a, b]);

        Assert.Contains(centers, c => c.Label == "Center of Group" && c.Point.DistanceTo(CenterPoints.InstanceCenter(a)!.Value) < 1e-9);
        var face = Assert.Single(centers, c => c.Label == "Center of Face");
        Assert.True(face.Point.DistanceTo(new Vec3(50, 20, 500)) < 1e-9);
        Assert.Single(centers, c => c.Label == "Center of Selection");
    }

    [Fact]
    public void A_rotated_group_is_centred_on_its_own_box()
    {
        var def = new ComponentDefinition { IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(100, 20, 10));
        var inst = new ComponentInstance(def) { Transform = Transform.Rotation(Vec3.UnitZ, Math.PI / 2).Then(Transform.Translation(new Vec3(1000, 0, 0))) };
        Assert.True(CenterPoints.InstanceCenter(inst)!.Value.DistanceTo(new Vec3(990, 50, 5)) < 1e-9);
    }
}
