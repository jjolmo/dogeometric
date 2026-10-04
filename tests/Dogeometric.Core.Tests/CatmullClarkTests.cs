using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CatmullClarkTests
{
    [Theory]
    [InlineData(1, 416.7)]
    [InlineData(2, 350.1)]
    public void A_cube_rounds_off_and_stays_a_solid(int levels, double volume)
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        CatmullClark.Apply(e, levels);
        Assert.Empty(SolidInspector.Find(e));
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        var check = MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
        Assert.True(check.IsWatertight);
        Assert.Equal(volume, check.Volume, 0);
    }

    [Fact]
    public void An_open_surface_keeps_its_border()
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        CatmullClark.Apply(e, 1);
        Assert.Equal(4, e.Faces.Count);
        Assert.Equal(100, e.Faces.Sum(f => f.Area), 6);
    }
}
