using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class JointPushPullTests
{
    private static Entities Box()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(40, 30, 20));
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
    public void Pulling_a_face_extends_the_solid()
    {
        var e = Box();
        JointPushPull.Apply(new Model(), e, [e.Faces.Single(f => f.Normal.Z > 0.5)], 10, new JointPushPull.Options());
        Assert.Equal(36000, Check(e).Volume, 6);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Thickening_every_face_inwards_hollows_the_box()
    {
        var e = Box();
        JointPushPull.Apply(new Model(), e, e.Faces.ToList(), -2, new JointPushPull.Options { Thicken = true, Borders = JointPushPullBorders.None });
        var check = Check(e);
        Assert.True(check.IsWatertight);
        Assert.Equal(40 * 30 * 20 - 36 * 26 * 16, check.Volume, 6);
        // The cavity's faces look into it: no reversed faces reported.
        Assert.Empty(SolidInspector.Find(e));
    }

    [Theory]
    [InlineData(2.0, 1980.0)]
    [InlineData(-2.0, 2220.0)]
    public void A_bent_surface_thickens_evenly(double offset, double volume)
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(30, 0, 0), new(30, 20, 0), new(0, 20, 0)]);
        e.AddFace([new(0, 20, 0), new(30, 20, 0), new(30, 20, 15), new(0, 20, 15)]);
        foreach (var f in e.Faces.Where(f => f.Normal.Z < -0.5 || f.Normal.Y > 0.5).ToList())
            FaceFinder.Reverse(f);
        JointPushPull.Apply(new Model(), e, e.Faces.ToList(), offset, new JointPushPull.Options { Thicken = true });
        Assert.Equal(volume, Check(e).Volume, 6);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Joint_displacement_keeps_every_face_at_the_offset()
    {
        Vec3[] normals = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ];
        var d = JointPushPull.JointDisplacement(normals, 3);
        foreach (var n in normals)
            Assert.Equal(3, d.Dot(n), 9);
    }

    [Fact]
    public void A_cavity_facing_outwards_is_reported_as_reversed()
    {
        var e = Box();
        JointPushPull.Apply(new Model(), e, e.Faces.ToList(), -2, new JointPushPull.Options { Thicken = true, Borders = JointPushPullBorders.None });
        var cavityTop = e.Faces.Single(f => Math.Abs(f.OuterLoop.Points.First().Z - 18) < 1e-9);
        FaceFinder.Reverse(cavityTop);
        Assert.Contains(SolidInspector.Find(e), x => x.Kind == SolidErrorKind.ReversedFace && x.Entities[0] == cavityTop);
    }
}
