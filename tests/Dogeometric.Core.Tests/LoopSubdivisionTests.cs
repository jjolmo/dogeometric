using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class LoopSubdivisionTests
{
    private static MeshCheck.Report Check(Entities e)
    {
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        return MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
    }

    [Fact]
    public void A_whole_cube_rounds_off_and_stays_closed()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        Assert.Equal(96, LoopSubdivision.Apply(e, e.Faces.ToList(), 1, soften: true));
        var check = Check(e);
        Assert.True(check.IsWatertight);
        Assert.InRange(check.Volume, 700, 800);
        Assert.Empty(SolidInspector.Find(e));
        Assert.All(e.Edges, x => Assert.True(x.Flags.HasFlag(EdgeFlags.Smooth)));
    }

    [Fact]
    public void One_face_stays_flat_and_its_neighbours_get_the_same_splits()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        var top = e.Faces.Single(f => f.Normal.Z > 0.5);
        Assert.Equal(64, LoopSubdivision.Apply(e, [top], 2, soften: true));
        var check = Check(e);
        Assert.True(check.IsWatertight);
        Assert.Equal(1000, check.Volume, 6);
        Assert.Empty(SolidInspector.Find(e));
    }
}
