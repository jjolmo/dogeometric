using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ScaleTests
{
    [Fact]
    public void Scaling_about_the_opposite_corner_keeps_it_fixed()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(100, 50, 20));
        var t = Transforming.Scale(Transform.Identity, new Vec3(100, 50, 20), 2, 1, 0.5);
        Transforming.Apply(e, e.Faces.Cast<object>().ToList(), t);
        var b = e.Bounds();
        Assert.Equal(new Vec3(-100, 0, 10), b.Min);
        Assert.Equal(new Vec3(100, 50, 20), b.Max);
    }

    [Fact]
    public void Scale_follows_a_rotated_group_axes()
    {
        var (model, a, _) = TestModels.TwoBoxGroups();
        a.Transform = Transform.Rotation(Vec3.UnitZ, Math.PI / 2);
        var size = a.Definition.Entities.Bounds().Size;
        // Double the group along its own red axis, which now points along world green.
        Transforming.Apply(model.Entities, [a], Transforming.Scale(a.Transform, Vec3.Zero, 2, 1, 1));
        var world = new Entities { Instances = { a } }.Bounds().Size;
        Assert.Equal(size.Y, world.X, 6);
        Assert.Equal(2 * size.X, world.Y, 6);
    }

    [Fact]
    public void Mirroring_loose_faces_keeps_them_facing_out()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        var outward = e.Faces.ToDictionary(f => f, f => f.Normal.Dot(Centroid(f) - new Vec3(5, 5, 5)) > 0);
        Transforming.Apply(e, e.Faces.Cast<object>().ToList(), Transforming.Scale(Transform.Identity, new Vec3(5, 5, 5), -1, 1, 1));
        foreach (var f in e.Faces)
            Assert.Equal(outward[f], f.Normal.Dot(Centroid(f) - new Vec3(5, 5, 5)) > 0);
    }

    [Fact]
    public void Flip_along_red_mirrors_a_group_in_place()
    {
        var (model, a, _) = TestModels.TwoBoxGroups();
        var before = new Entities { Instances = { a } }.Bounds();
        Transforming.Flip(model.Entities, [a], 0, Transform.Identity);
        Assert.True(a.Transform.IsMirroring);
        var after = new Entities { Instances = { a } }.Bounds();
        Assert.Equal(before.Min.X, after.Min.X, 6);
        Assert.Equal(before.Max.X, after.Max.X, 6);
    }

    private static Vec3 Centroid(Face f) => f.OuterLoop.Points.Aggregate(Vec3.Zero, (s, p) => s + p) / f.OuterLoop.Points.Count();
}
