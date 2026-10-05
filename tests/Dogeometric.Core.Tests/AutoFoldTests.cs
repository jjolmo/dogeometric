using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class AutoFoldTests
{
    private static Entities Cube(double a)
    {
        var e = new Entities();
        Vec3[] bottom = [new(0, 0, 0), new(a, 0, 0), new(a, a, 0), new(0, a, 0)];
        var top = bottom.Select(p => p + new Vec3(0, 0, a)).ToList();
        e.AddFace(top);
        e.AddFace(bottom.Reverse().ToList());
        for (var i = 0; i < 4; i++)
            e.AddFace([bottom[i], bottom[(i + 1) % 4], top[(i + 1) % 4], top[i]]);
        return e;
    }

    private static bool AllFlat(Entities e) => e.Faces.All(f =>
        f.Loops.SelectMany(l => l.Points).All(q => Math.Abs((q - f.OuterLoop.Points.First()).Dot(f.Normal)) <= Tolerance.Length));

    [Fact]
    public void Pulling_a_corner_out_folds_the_three_faces_around_it()
    {
        var e = Cube(100);
        var corner = e.Vertices.Single(v => v.Position == new Vec3(100, 100, 100));
        Transforming.Move(e, [corner], new Vec3(10, 10, 50));
        Assert.Equal(9, e.Faces.Count);
        Assert.True(AllFlat(e));
        // The top keeps its untouched half: the fold runs between the corner's neighbours.
        Assert.Contains(e.Edges, x => new[] { x.Start.Position, x.End.Position }.ToHashSet().SetEquals([new Vec3(100, 0, 100), new Vec3(0, 100, 100)]));
    }

    [Fact]
    public void Lifting_an_edge_folds_only_the_faces_it_bends()
    {
        var e = Cube(100);
        var edge = e.Edges.Single(x => x.Start.Position.Z == 100 && x.End.Position.Z == 100 && x.Start.Position.X == 100 && x.End.Position.X == 100);
        Transforming.Move(e, [edge], new Vec3(0, 30, 50));
        Assert.Equal(8, e.Faces.Count);
        Assert.True(AllFlat(e));

        // Straight up keeps every face flat (the sides it touches stay in their planes), so nothing folds.
        var lifted = Cube(100);
        var corner = lifted.Vertices.Single(v => v.Position == new Vec3(100, 100, 100));
        Transforming.Move(lifted, [corner], new Vec3(0, 0, 50));
        Assert.Equal(7, lifted.Faces.Count);
    }

    [Fact]
    public void Turning_the_top_folds_the_sides_and_moving_everything_folds_nothing()
    {
        var e = Cube(100);
        var top = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99);
        Transforming.Apply(e, [top], Transform.Rotation(Vec3.UnitZ, Math.PI / 6, new Vec3(50, 50, 100)));
        Assert.Equal(10, e.Faces.Count);
        Assert.True(AllFlat(e));

        var box = Cube(100);
        Transforming.Move(box, box.Faces.Cast<object>().ToList(), new Vec3(10, 20, 30));
        Assert.Equal(6, box.Faces.Count);
    }

    [Fact]
    public void A_move_that_keeps_faces_flat_needs_no_fold()
    {
        var e = Cube(100);
        var corner = e.Vertices.Single(v => v.Position == new Vec3(100, 100, 100));
        Assert.True(Transforming.WouldBend(e, [corner], Transform.Translation(new Vec3(0, 0, 50))));
        var edge = e.Edges.Single(x => x.Start.Position.Z == 100 && x.End.Position.Z == 100 && x.Start.Position.Y == 100 && x.End.Position.Y == 100);
        // Raised, the top just tilts into a ramp; pushed sideways as well, the sides would bend.
        Assert.False(Transforming.WouldBend(e, [edge], Transform.Translation(new Vec3(0, 0, 30))));
        Assert.True(Transforming.WouldBend(e, [edge], Transform.Translation(new Vec3(20, 0, 30))));
        var top = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99);
        Assert.False(Transforming.WouldBend(e, [top], Transform.Translation(new Vec3(0, 0, 30))));
    }
}
