using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class FredoScaleTests
{
    private static Entities Bar()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 100));
        return e;
    }

    private static MeshCheck.Report Check(Entities e)
    {
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        return MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
    }

    [Fact]
    public void Tapering_to_half_gives_a_frustum()
    {
        var e = Bar();
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), Deformation.Taper, 2, 50);
        Assert.Equal(100.0 / 3 * (100 + 25 + 50), Check(e).Volume, 6);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Shearing_keeps_the_volume()
    {
        var e = Bar();
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), Deformation.Shear, 2, 45);
        Assert.Equal(10000, Check(e).Volume, 6);
        Assert.Equal(110, e.Vertices.Max(v => v.Position.X), 6);
    }

    [Theory]
    [InlineData(Deformation.Twist, 90.0)]
    [InlineData(Deformation.Bend, 60.0)]
    public void Twisting_and_bending_split_bent_faces_and_stay_solid(Deformation kind, double amount)
    {
        var e = Bar();
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), kind, 2, amount);
        Assert.True(Check(e).IsWatertight);
        Assert.Empty(SolidInspector.Find(e));
        Assert.All(e.Faces, f => Assert.All(f.OuterLoop.Points, p => Assert.True(Math.Abs((p - f.OuterLoop.Points.First()).Dot(f.Normal.Normalized())) < 1e-3)));
    }

    [Fact]
    public void Stretching_lengthens_the_middle_and_keeps_a_solid()
    {
        var e = Bar();
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), Deformation.Stretch, 2, 20);
        Assert.Equal(120, e.Vertices.Max(v => v.Position.Z), 6);
        Assert.Equal(0, e.Vertices.Min(v => v.Position.Z), 6);
        Assert.Equal(12000, Check(e).Volume, 6);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Box_scaling_and_rotation_keep_their_shape()
    {
        var e = Bar();
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), Deformation.Scale, 2, 50);
        Assert.Equal(5000, Check(e).Volume, 6);
        FredoScale.Apply(e, e.Faces.Cast<object>().ToList(), Deformation.Rotate, 2, 90);
        Assert.Equal(5000, Check(e).Volume, 6);
        Assert.Equal(0, e.Vertices.Min(v => v.Position.X), 6);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Theory]
    [InlineData(Deformation.Scale)]
    [InlineData(Deformation.Stretch)]
    [InlineData(Deformation.Shear)]
    [InlineData(Deformation.Taper)]
    public void To_target_moves_the_picked_point_onto_the_target(Deformation kind)
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(20, 10, 30));
        var items = e.Faces.Cast<object>().Concat(e.Edges).ToList();
        var corner = e.Vertices.First(v => v.Position.DistanceTo(new Vec3(20, 10, 30)) < 1e-9);
        var target = kind switch
        {
            Deformation.Shear => new Vec3(26, 10, 30),
            Deformation.Taper => new Vec3(25, 10, 30),
            _ => new Vec3(20, 10, 45),
        };
        var amount = FredoScale.TargetAmount(kind, 2, FredoScale.BoxOf(items), corner.Position, target)!.Value;
        FredoScale.Apply(e, items, kind, 2, amount);
        // Tapering scales the whole cross-section, so only the side the target was taken on is checked.
        if (kind == Deformation.Taper)
            Assert.Equal(target.X, corner.Position.X, 6);
        else
            Assert.True(corner.Position.DistanceTo(target) < 1e-6, $"{kind}: {corner.Position}");
    }
}
