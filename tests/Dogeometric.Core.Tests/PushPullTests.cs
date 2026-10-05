using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class PushPullTests
{
    [Fact]
    public void Pulling_a_square_makes_a_closed_box()
    {
        var e = new Entities();
        var face = e.AddFace([new Vec3(0, 0, 0), new Vec3(100, 0, 0), new Vec3(100, 100, 0), new Vec3(0, 100, 0)]);
        PushPull.Apply(e, face, face.Normal.Z > 0 ? 50 : -50);
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(12, e.Edges.Count);
        Assert.Equal(new Vec3(100, 100, 50), e.Bounds().Size);
    }

    [Fact]
    public void Pushing_an_inner_square_to_the_far_face_cuts_a_hole_through()
    {
        // A 100 box, then a 40 square drawn on its top, pushed down the whole height.
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(100, 100, 100));
        var top = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99);
        StickyGeometry.DrawEdges(e, [new Vec3(30, 30, 100), new Vec3(70, 30, 100), new Vec3(70, 70, 100), new Vec3(30, 70, 100)], closed: true, Vec3.UnitZ);
        var inner = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99 && f.Area < 2000);
        PushPull.Apply(e, inner, -100);

        // Neither a cap at the top nor a face across the hole at the bottom remains.
        Assert.DoesNotContain(e.Faces, f => Math.Abs(f.Normal.Z) > 0.99 && f.Area < 2000);
        var bottom = e.Faces.Single(f => f.Normal.Dot(-Vec3.UnitZ) > 0.99);
        Assert.Equal(100 * 100 - 40 * 40, bottom.Area, 6);
        Assert.Equal(2, bottom.Loops.Count);
        // Four walls line the hole.
        Assert.Equal(4, e.Faces.Count(f => Math.Abs(f.Normal.Z) < 1e-9 && f.Area == 40 * 100));
    }

    [Fact]
    public void Pushing_a_box_side_just_moves_it()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(100, 100, 100));
        var side = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitX) > 0.99);
        PushPull.Apply(e, side, 50);
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(new Vec3(150, 100, 100), e.Bounds().Size);
    }
}
