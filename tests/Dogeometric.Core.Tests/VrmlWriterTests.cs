using System.Globalization;
using System.Text.RegularExpressions;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class VrmlWriterTests
{
    [Fact]
    public void A_group_goes_out_as_SketchUp_writes_it_in_inches_with_both_sides_and_edges()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Lid", IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(100, 50, 20));
        model.Definitions.Add(def);
        var lid = model.Entities.AddInstance(def, Transform.Identity);
        lid.Material = new Material { Name = "Red", Color = new Rgba(200, 30, 30), Opacity = 0.75 };

        using var w = new StringWriter();
        var faces = VrmlWriter.Write(model, w);
        var wrl = w.ToString();

        Assert.Equal(6, faces);
        Assert.StartsWith("#VRML V2.0 utf8", wrl);
        Assert.Contains("DEF GRP_Lid Group", wrl);
        Assert.Equal(12, Regex.Matches(wrl, "IndexedFaceSet").Count);
        Assert.Equal(12, Regex.Matches(wrl, "IndexedLineSet").Count);
        Assert.Contains("diffuseColor 0.784314 0.117647 0.117647 transparency 0.25", wrl);
        // 100 mm is 3.937008 inches.
        Assert.Contains("3.937008 1.968504 0.787402", wrl);
    }

    [Fact]
    public void The_viewpoint_turns_VRML_s_default_view_onto_the_camera()
    {
        var camera = new CameraState(new Vec3(1000, -2000, 800), new Vec3(0, 0, 100), Vec3.UnitZ, true, 35, 1000);
        using var w = new StringWriter();
        VrmlWriter.Write(new Model(), w, camera);
        var m = Regex.Match(w.ToString(), @"orientation (\S+) (\S+) (\S+) (\S+)");
        double G(int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        var (axis, angle) = (new Vec3(G(1), G(2), G(3)), G(4));

        // Rodrigues: the default looking direction -Z, turned, is the camera's.
        Vec3 Turn(Vec3 v) => v * Math.Cos(angle) + axis.Cross(v) * Math.Sin(angle) + axis * (axis.Dot(v) * (1 - Math.Cos(angle)));
        var forward = (camera.Target - camera.Eye).Normalized();
        Assert.True(Turn(-Vec3.UnitZ).DistanceTo(forward) < 1e-5);
        Assert.True(Turn(Vec3.UnitY).Dot(Vec3.UnitZ) > 0);
    }
}
