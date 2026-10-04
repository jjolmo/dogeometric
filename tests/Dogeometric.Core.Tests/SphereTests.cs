using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SphereTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(48)]
    public void A_sphere_is_a_closed_solid_near_the_exact_volume(int segments)
    {
        var e = new Entities();
        Sphere.Add(e, new Vec3(5, 5, 5), 10, segments);
        Assert.Empty(SolidInspector.Find(e));
        var def = new ComponentDefinition();
        def.Entities.Vertices.AddRange(e.Vertices);
        def.Entities.Edges.AddRange(e.Edges);
        def.Entities.Faces.AddRange(e.Faces);
        var check = MeshCheck.Analyze(MeshExtractor.ExtractInstance(new ComponentInstance(def)));
        Assert.True(check.IsWatertight);
        Assert.InRange(check.Volume, 0.75 * 4.0 / 3 * Math.PI * 1000, 4.0 / 3 * Math.PI * 1000);
    }
}
