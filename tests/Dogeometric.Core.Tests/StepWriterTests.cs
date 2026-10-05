using System.Text.RegularExpressions;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class StepWriterTests
{
    private static string Export(Model m, out StepWriter.Result result)
    {
        using var w = new StringWriter();
        result = StepWriter.Write(m, w, "Test");
        return w.ToString();
    }

    private static int Count(string step, string entity) => Regex.Matches(step, $@"={entity}\(").Count;

    [Fact]
    public void A_box_in_a_group_is_one_closed_solid_with_shared_edges_and_corners()
    {
        var m = new Model();
        var def = new ComponentDefinition { Name = "Box", IsGroup = true };
        m.Definitions.Add(def);
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(100, 60, 40));
        m.Entities.AddInstance(def, Transform.Translation(new Vec3(10, 0, 0)));

        var step = Export(m, out var result);
        Assert.Equal((1, 0), (result.Solids, result.Surfaces));
        Assert.StartsWith("ISO-10303-21;", step);
        Assert.EndsWith("END-ISO-10303-21;\n", step);
        Assert.Equal(6, Count(step, "ADVANCED_FACE"));
        Assert.Equal(12, Count(step, "EDGE_CURVE"));
        Assert.Equal(8, Count(step, "VERTEX_POINT"));
        // Every edge is used by two faces, once each way.
        Assert.Equal(24, Count(step, "ORIENTED_EDGE"));
        // The group's placement is applied: the far corner is at x = 110.
        Assert.Contains("CARTESIAN_POINT('',(110.0,60.0,40.0))", step);
    }

    [Fact]
    public void A_lone_face_with_a_hole_is_an_open_surface_and_hidden_geometry_is_left_out()
    {
        var m = new Model();
        m.Entities.AddFace([Vec3.Zero, new Vec3(100, 0, 0), new Vec3(100, 100, 0), new Vec3(0, 100, 0)],
            [[new Vec3(40, 40, 0), new Vec3(40, 60, 0), new Vec3(60, 60, 0), new Vec3(60, 40, 0)]]);
        var hidden = m.Entities.AddFace([new Vec3(0, 0, 500), new Vec3(10, 0, 500), new Vec3(10, 10, 500)]);
        hidden.Hidden = true;

        var step = Export(m, out var result);
        Assert.Equal((0, 1), (result.Solids, result.Surfaces));
        Assert.Equal(1, Count(step, "FACE_OUTER_BOUND"));
        Assert.Equal(1, Count(step, "FACE_BOUND"));
        Assert.DoesNotContain("500.0", step);
    }
}
