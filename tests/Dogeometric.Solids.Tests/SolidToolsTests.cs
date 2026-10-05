using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Solids.Tests;

public class SolidToolsTests
{
    static SolidToolsTests() => SolidsNative.AddSearchDirectory(Path.Combine(AppContext.BaseDirectory, "native"));

    private static void Box(Entities e, Vec3 min, Vec3 size)
    {
        Vec3 P(double x, double y, double z) => min + new Vec3(x * size.X, y * size.Y, z * size.Z);
        e.AddFace([P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)]);
        e.AddFace([P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)]);
        e.AddFace([P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]);
        e.AddFace([P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)]);
        e.AddFace([P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)]);
        e.AddFace([P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]);
    }

    private static ComponentInstance Group(Model m, Vec3 min, Vec3 size)
    {
        var def = new ComponentDefinition { Name = $"Group#{m.Definitions.Count + 1}", IsGroup = true };
        Box(def.Entities, min, size);
        m.Definitions.Add(def);
        return m.Entities.AddInstance(def, Transform.Identity);
    }

    /// <summary>Volume by the divergence theorem over the instance's triangles.</summary>
    private static double Volume(ComponentInstance i) =>
        MeshExtractor.ExtractInstance(i).Sum(t => t.A.Dot(t.B.Cross(t.C)) / 6);

    [Fact]
    public void Union_intersect_and_subtract_of_two_overlapping_boxes()
    {
        // 100³ at the origin and 100³ shifted 50 along x: overlap 50×100×100.
        var m = new Model();
        var u = SolidTools.Union(m, m.Entities, [Group(m, Vec3.Zero, new Vec3(100, 100, 100)), Group(m, new Vec3(50, 0, 0), new Vec3(100, 100, 100))]);
        Assert.Equal(1_500_000, Volume(u), 0);
        Assert.True(SolidTools.IsSolid(u));

        m = new Model();
        var x = SolidTools.Intersect(m, m.Entities, [Group(m, Vec3.Zero, new Vec3(100, 100, 100)), Group(m, new Vec3(50, 0, 0), new Vec3(100, 100, 100))]);
        Assert.Equal(500_000, Volume(x), 0);

        m = new Model();
        var cutter = Group(m, new Vec3(50, 0, 0), new Vec3(100, 100, 100));
        var body = Group(m, Vec3.Zero, new Vec3(100, 100, 100));
        var s = SolidTools.Subtract(m, m.Entities, cutter, body)!;
        Assert.Equal(500_000, Volume(s), 0);
        Assert.Single(m.Entities.Instances);
    }

    [Fact]
    public void Trim_keeps_the_cutter_and_split_makes_three_parts()
    {
        var m = new Model();
        var cutter = Group(m, new Vec3(50, 0, 0), new Vec3(100, 100, 100));
        var body = Group(m, Vec3.Zero, new Vec3(100, 100, 100));
        var trimmed = SolidTools.Trim(m, m.Entities, cutter, body)!;
        Assert.Equal(500_000, Volume(trimmed), 0);
        Assert.Contains(cutter, m.Entities.Instances);
        Assert.Equal(2, m.Entities.Instances.Count);

        m = new Model();
        var parts = SolidTools.Split(m, m.Entities, Group(m, Vec3.Zero, new Vec3(100, 100, 100)), Group(m, new Vec3(50, 0, 0), new Vec3(100, 100, 100)));
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.Equal(500_000, Volume(p), 0));
    }

    [Fact]
    public void Outer_shell_fills_a_hollow_box_that_union_keeps_hollow()
    {
        // A 100³ box with a 60³ cavity (an inner shell facing in), next to a 20³ box touching it.
        Model Hollow(out ComponentInstance hollow, out ComponentInstance other)
        {
            var m = new Model();
            var def = new ComponentDefinition { Name = "Hollow", IsGroup = true };
            Box(def.Entities, Vec3.Zero, new Vec3(100, 100, 100));
            var inner = new Entities();
            Box(inner, new Vec3(20, 20, 20), new Vec3(60, 60, 60));
            foreach (var f in inner.Faces)
                def.Entities.AddFace(f.OuterLoop.Points.Reverse().ToList());
            m.Definitions.Add(def);
            hollow = m.Entities.AddInstance(def, Transform.Identity);
            other = Group(m, new Vec3(100, 0, 0), new Vec3(20, 20, 20));
            return m;
        }
        var m1 = Hollow(out var h1, out var o1);
        var union = SolidTools.Union(m1, m1.Entities, [h1, o1]);
        Assert.Equal(1_000_000 - 216_000 + 8_000, Volume(union), 0);

        var m2 = Hollow(out var h2, out var o2);
        var shell = SolidTools.OuterShell(m2, m2.Entities, [h2, o2]);
        Assert.Equal(1_000_000 + 8_000, Volume(shell), 0);
    }
}
