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

    [Fact]
    public void Follow_stretches_the_sides_instead_of_adding_walls()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(20, 20, 20));
        var e = m.Entities;
        var top = e.Faces.Single(f => f.Normal.Normalized().Z > 0.99);
        JointPushPull.Apply(m, e, [top], 10, new JointPushPull.Options { Mode = JointPushPullMode.Follow });
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(new Vec3(20, 20, 30), e.Bounds().Size);
        Assert.Empty(SolidInspector.Find(e));

        var side = e.Faces.Single(f => f.Normal.Normalized().X > 0.99);
        var lid = e.Faces.Single(f => f.Normal.Normalized().Z > 0.99);
        JointPushPull.Apply(m, e, [side, lid], 5, new JointPushPull.Options { Mode = JointPushPullMode.Follow });
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(new Vec3(25, 20, 35), e.Bounds().Size);
        Assert.Equal(25 * 20 * 35, Check(e).Volume, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Round_gives_a_box_a_rounded_skin(bool thicken)
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(20, 20, 20));
        var e = m.Entities;
        JointPushPull.Apply(m, e, [.. e.Faces], 2, new JointPushPull.Options { Mode = JointPushPullMode.Round, Thicken = thicken, Segments = 12 });
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(new Vec3(24, 24, 24), e.Bounds().Size);
        var minkowski = 8000 + 2400 * 2 + 240 * Math.PI + 4.0 / 3 * Math.PI * 8;
        var volume = Check(e).Volume;
        var expected = thicken ? minkowski - 8000 : minkowski;
        Assert.InRange(volume, expected * 0.995, expected);
    }

    [Fact]
    public void Round_falls_back_to_mitres_where_faces_would_cut_into_each_other()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(20, 20, 20));
        var e = m.Entities;
        // Inwards every edge of a box closes up: no rounding, a smaller box.
        JointPushPull.Apply(m, e, [.. e.Faces], -2, new JointPushPull.Options { Mode = JointPushPullMode.Round, Thicken = true });
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(8000 - 16 * 16 * 16, Check(e).Volume, 6);
    }

    [Fact]
    public void Round_thickens_an_open_l_sheet_into_a_solid()
    {
        var m = new Model();
        var e = m.Entities;
        var w = new Welder(e);
        // Floor and wall meeting at a convex edge seen from outside (fronts facing out).
        var floor = w.Face([new(0, 0, 0), new(0, 20, 0), new(30, 20, 0), new(30, 0, 0)], []);
        var wall = w.Face([new(0, 0, 0), new(0, 0, 15), new(0, 20, 15), new(0, 20, 0)], []);
        if (floor.Normal.Z > 0)
            FaceFinder.Reverse(floor);
        if (wall.Normal.X > 0)
            FaceFinder.Reverse(wall);
        JointPushPull.Apply(m, e, [floor, wall], 2, new JointPushPull.Options { Mode = JointPushPullMode.Round, Thicken = true, Segments = 8 });
        Assert.Empty(SolidInspector.Find(e));
        // Two slabs plus a quarter-cylinder of radius 2 along the 20 mm edge.
        var expected = 30 * 20 * 2 + 15 * 20 * 2 + Math.PI * 4 / 4 * 20;
        Assert.InRange(Check(e).Volume, expected * 0.99, expected);
    }
}
