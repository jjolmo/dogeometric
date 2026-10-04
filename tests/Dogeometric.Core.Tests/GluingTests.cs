using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class GluingTests
{
    private static (Model Model, Face Front, ComponentInstance Vent) GluedVent()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(100, 60, 40));
        var vent = new ComponentDefinition { Name = "Vent", GlueTo = GlueTo.Vertical, CutsOpening = true };
        new Welder(vent.Entities).Face([new(-15, -8, 0), new(15, -8, 0), new(15, 8, 0), new(-15, 8, 0)],
            [[new(-12, -5, 0), new(12, -5, 0), new(12, 5, 0), new(-12, 5, 0)]]);
        m.Definitions.Add(vent);
        var front = m.Entities.Faces.First(f => f.Normal.Normalized().Dot(-Vec3.UnitY) > 0.99);
        var inst = m.Entities.AddInstance(vent, Gluing.OnFace(new Vec3(50, 0, 20), front.Normal.Normalized()));
        inst.GluedTo = front;
        return (m, front, inst);
    }

    [Fact]
    public void A_glued_component_stands_on_the_face_and_cuts_its_outline()
    {
        var (m, front, vent) = GluedVent();
        Assert.Equal(-1, vent.Transform.ApplyVector(Vec3.UnitZ).Normalized().Y, 9);
        Assert.Same(front, Gluing.FaceUnder(m.Entities, vent));
        var opening = Assert.Single(Gluing.Openings(m.Entities)[front]);
        Assert.Equal(30 * 16, Polygon.Area(opening), 6);
        Assert.All(opening, p => Assert.Equal(0, p.Y, 9));
    }

    [Fact]
    public void Glue_choices_follow_the_face_slope()
    {
        Assert.True(Gluing.Accepts(GlueTo.Horizontal, Vec3.UnitZ));
        Assert.False(Gluing.Accepts(GlueTo.Horizontal, Vec3.UnitX));
        Assert.True(Gluing.Accepts(GlueTo.Vertical, -Vec3.UnitY));
        Assert.True(Gluing.Accepts(GlueTo.Sloped, new Vec3(0, 1, 1).Normalized()));
        Assert.False(Gluing.Accepts(GlueTo.None, Vec3.UnitZ));
    }

    [Fact]
    public void Gluing_survives_saving_and_undo()
    {
        var (m, _, _) = GluedVent();
        var path = Path.Combine(Path.GetTempPath(), $"glue-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(m, path);
            var back = DogFile.Load(path);
            Assert.NotNull(back.Entities.Instances[0].GluedTo);
            Assert.True(back.Definitions[0].CutsOpening);
            Assert.Equal(GlueTo.Vertical, back.Definitions[0].GlueTo);
        }
        finally
        {
            File.Delete(path);
        }
        var doc = new Document(m);
        var inst = m.Entities.Instances[0];
        doc.Operation("Unglue", _ => inst.GluedTo = null);
        doc.Undo.Undo();
        Assert.NotNull(inst.GluedTo);
    }

    [Fact]
    public void Changing_a_components_axes_moves_nothing_on_screen()
    {
        var m = new Model();
        var def = new ComponentDefinition { Name = "Box" };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(10, 20, 30));
        m.Definitions.Add(def);
        var a = m.Entities.AddInstance(def, Transform.Translation(new Vec3(100, 0, 0)));
        var b = m.Entities.AddInstance(def, Transform.Rotation(Vec3.UnitZ, 0.5).Then(Transform.Translation(new Vec3(0, 50, 0))));
        List<Vec3> World(ComponentInstance i) => def.Entities.Vertices.Select(v => i.Transform.ApplyPoint(v.Position)).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();
        var (wa, wb) = (World(a), World(b));
        var axes = new Transform(Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ, new Vec3(105, 10, 0));
        Grouping.ChangeAxes(m, a, axes);
        Assert.Equal(new Vec3(105, 10, 0), a.Transform.Origin);
        foreach (var (p, q) in World(a).Zip(wa).Concat(World(b).Zip(wb)))
            Assert.True(p.DistanceTo(q) < 1e-9);
    }

    [Fact]
    public void A_copy_slid_along_the_face_is_glued_too()
    {
        var (m, front, vent) = GluedVent();
        var copy = (ComponentInstance)Transforming.Copy(m.Entities, [vent], Transform.Translation(new Vec3(-33, 0, 0))).Single();
        Assert.Same(front, copy.GluedTo);
        Assert.Equal(2, Gluing.Openings(m.Entities)[front].Count);
    }

    [Fact]
    public void A_component_made_on_a_wall_stands_on_it_and_glues()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(100, 60, 40));
        var wall = m.Entities.Faces.First(f => f.Normal.Normalized().Dot(-Vec3.UnitY) > 0.99);
        StickyGeometry.DrawEdges(m.Entities, [new(30, 0, 10), new(60, 0, 10), new(60, 0, 25), new(30, 0, 25)], closed: true, -Vec3.UnitY);
        var rect = m.Entities.Faces.Single(f => f.Normal.Normalized().Dot(-Vec3.UnitY) > 0.99 && Polygon.Area(f.OuterLoop.Points.ToList()) < 500);
        var before = rect.OuterLoop.Points.ToList();
        var inst = Grouping.Make(m, m.Entities, [rect, .. Topology.EdgesOf(rect)], asGroup: false);
        inst.Definition.GlueTo = GlueTo.Vertical;
        inst.Definition.CutsOpening = true;
        var face = Gluing.Settle(m, m.Entities, inst);
        Assert.NotNull(face);
        Assert.Same(face, inst.GluedTo);
        Assert.Equal(-1, inst.Transform.ApplyVector(Vec3.UnitZ).Normalized().Y, 9);
        // Nothing moved on screen, and the opening is the rectangle.
        var world = inst.Definition.Entities.Vertices.Select(v => inst.Transform.ApplyPoint(v.Position)).ToList();
        Assert.All(before, p => Assert.Contains(world, q => q.DistanceTo(p) < 1e-9));
        Assert.Equal(30 * 15, Polygon.Area(Assert.Single(Gluing.Openings(m.Entities)[face!])), 6);
        // The wall is whole again: the component makes the hole.
        Assert.Empty(face!.InnerLoops);
        Assert.DoesNotContain(SolidInspector.Find(m.Entities), x => x.Kind == SolidErrorKind.StrayEdge);
    }

    [Fact]
    public void Overlapping_openings_are_not_cut()
    {
        var (m, front, vent) = GluedVent();
        var twin = m.Entities.AddInstance(vent.Definition, vent.Transform.Then(Transform.Translation(new Vec3(10, 0, 0))));
        twin.GluedTo = front;
        Assert.Single(Gluing.Openings(m.Entities)[front]);
    }
}
