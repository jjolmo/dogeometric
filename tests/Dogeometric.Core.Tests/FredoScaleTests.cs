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
}
