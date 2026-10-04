using System.IO.Compression;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class DogFileTests
{
    private static Model RoundTrip(Model m)
    {
        using var ms = new MemoryStream();
        DogFile.Save(m, ms);
        ms.Position = 0;
        return DogFile.Load(ms);
    }

    [Fact]
    public void Round_trip_keeps_geometry_hierarchy_and_attributes()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        var red = new Material { Name = "Red", Color = new Rgba(255, 0, 0), Opacity = 0.5 };
        var tex = new Material { Name = "Wood", Texture = new TextureImage { FileName = "wood.png", Data = [1, 2, 3, 4], WidthMm = 100, HeightMm = 50 } };
        model.Materials.AddRange([red, tex]);
        var pcb = model.GetOrAddTag("PCB");
        pcb.Visible = false;
        a.Material = red;
        b.Tag = pcb;
        b.Hidden = true;
        b.Name = "Lid";
        b.Transform = Transform.Rotation(Vec3.UnitZ, 0.25, new Vec3(1, 2, 3)).Then(Transform.Translation(new Vec3(100, 0, 0)));
        var face = model.Definitions[0].Entities.Faces[0];
        face.FrontMaterial = tex;
        face.BackMaterial = red;
        model.Definitions[0].Entities.Edges[0].Flags = EdgeFlags.Soft | EdgeFlags.Smooth;
        model.Entities.AddFace(
            [new(0, 0, -1), new(50, 0, -1), new(50, 50, -1), new(0, 50, -1)],
            [[new(10, 10, -1), new(20, 10, -1), new(20, 20, -1), new(10, 20, -1)]]);
        model.Scenes.Add(new Scene { Name = "Top", Camera = new CameraState(new Vec3(0, 0, 500), Vec3.Zero, Vec3.UnitY, false, 35, 300) });
        model.Scenes[0].HiddenTags.Add("PCB");

        var back = RoundTrip(model);

        Assert.Equal(2, back.Definitions.Count);
        Assert.True(back.Definitions.All(d => d.IsGroup));
        Assert.Equal(6, back.Definitions[0].Entities.Faces.Count);
        Assert.Equal(12, back.Definitions[0].Entities.Edges.Count);
        Assert.Equal(EdgeFlags.Soft | EdgeFlags.Smooth, back.Definitions[0].Entities.Edges[0].Flags);

        var backFace = back.Definitions[0].Entities.Faces[0];
        Assert.Equal("Wood", backFace.FrontMaterial?.Name);
        Assert.Equal("Red", backFace.BackMaterial?.Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, back.Materials[1].Texture!.Data);
        Assert.Equal(0.5, back.Materials[0].Opacity);

        var lid = back.Entities.Instances[1];
        Assert.Equal("Lid", lid.Name);
        Assert.True(lid.Hidden);
        Assert.Equal("PCB", lid.Tag?.Name);
        Assert.False(lid.Tag!.Visible);
        Assert.True(lid.Transform.ApplyPoint(new Vec3(3, 4, 5)).DistanceTo(b.Transform.ApplyPoint(new Vec3(3, 4, 5))) < 1e-9);
        Assert.Same(back.Definitions[1], lid.Definition);

        var holed = back.Entities.Faces.Single();
        Assert.Equal(2, holed.Loops.Count);
        Assert.Equal(2500 - 100, holed.Area, 9);

        Assert.Equal("Top", back.Scenes[0].Name);
        Assert.False(back.Scenes[0].Camera!.Value.Perspective);
        Assert.Contains("PCB", back.Scenes[0].HiddenTags);
    }

    [Fact]
    public void Faces_keep_their_orientation()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        var back = RoundTrip(model);
        var before = model.Definitions[0].Entities.Faces.Select(f => f.Normal).ToList();
        var after = back.Definitions[0].Entities.Faces.Select(f => f.Normal).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Rejects_files_from_a_newer_format_version()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write("""{"format":"dogeometric","version":999}""");
        }
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => DogFile.Load(ms));
    }

    [Fact]
    public void Dimensions_and_texts_round_trip()
    {
        var model = new Model();
        model.Entities.Dimensions.Add(new LinearDimension(new Vec3(0, 0, 0), new Vec3(600, 0, 0), new Vec3(0, -100, 0)) { Text = "<> wide" });
        model.Entities.Texts.Add(new TextLabel("Lid") { Point = new Vec3(1, 2, 3), Offset = new Vec3(10, 0, 50) });
        model.Entities.Texts.Add(new TextLabel("Title") { ScreenPosition = (0.1, 0.2) });
        var back = RoundTrip(model);
        var d = Assert.Single(back.Entities.Dimensions);
        Assert.Equal(new Vec3(600, 0, 0), d.End);
        Assert.Equal(new Vec3(0, -100, 0), d.Offset);
        Assert.Equal("<> wide", d.Text);
        Assert.Equal(new Vec3(10, 0, 50), back.Entities.Texts[0].Offset);
        Assert.Equal((0.1, 0.2), back.Entities.Texts[1].ScreenPosition);
    }

    [Fact]
    public void Section_planes_round_trip_with_the_active_one()
    {
        var model = new Model();
        var a = new SectionPlane(new Vec3(0, 200, 0), new Vec3(0, 1, 0)) { Name = "Front" };
        var b = new SectionPlane(new Vec3(0, 0, 100), new Vec3(0, 0, -1));
        model.Entities.SectionPlanes.AddRange([a, b]);
        model.Entities.ActiveSection = b;
        var back = RoundTrip(model);
        Assert.Equal(2, back.Entities.SectionPlanes.Count);
        Assert.Equal("Front", back.Entities.SectionPlanes[0].Name);
        Assert.Same(back.Entities.SectionPlanes[1], back.Entities.ActiveSection);
        Assert.Equal(new Vec3(0, 0, -1), back.Entities.ActiveSection!.Normal);
    }

    [Fact]
    public void Drawing_axes_round_trip_and_undo()
    {
        var model = new Model();
        var axes = new Transform(Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ, new Vec3(10, 20, 30));
        var doc = new Document(model);
        doc.Operation("Place Axes", _ => model.Axes = axes);
        Assert.Equal(axes, RoundTrip(model).Axes);
        doc.Undo.Undo();
        Assert.Equal(Transform.Identity, model.Axes);
    }

    [Fact]
    public void Texture_mappings_round_trip()
    {
        var model = new Model();
        var face = model.Entities.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        face.FrontMapping = new TextureMapping([2, 0, 0, 0, 3, 0, 1, 1, 1]);
        var back = RoundTrip(model);
        var f = Assert.Single(back.Entities.Faces);
        Assert.Equal(face.FrontMapping.Matrix, f.FrontMapping!.Matrix);
        Assert.Null(f.BackMapping);
    }

    [Fact]
    public void Curves_and_their_spline_data_survive_saving()
    {
        var m = new Model();
        var circle = new Curve { Center = new Vec3(1, 2, 3), Normal = Vec3.UnitZ, Radius = 5, Segments = 24 };
        var pts = Shapes.RegularPolygon(circle.Center, Vec3.UnitZ, Vec3.UnitX, 5, 24, false);
        StickyGeometry.DrawEdges(m.Entities, pts, closed: true, Vec3.UnitZ, circle);
        var spline = new Curve { Segments = 3, Spline = new SplineData(SplineKind.DogBone, [new(0, 0, 0), new(9, 0, 0), new(9, 9, 0)], 24, 1.5, false) };
        StickyGeometry.DrawEdges(m.Entities, [new(20, 0, 0), new(25, 1, 0), new(30, 0, 0)], closed: false, null, spline);

        var back = RoundTrip(m);
        var curves = back.Entities.Edges.Select(x => x.Curve).Where(c => c != null).Distinct().ToList();
        Assert.Equal(2, curves.Count);
        var c1 = curves.Single(c => c!.Spline == null)!;
        Assert.Equal(5, c1.Radius);
        Assert.Equal(24, back.Entities.Edges.Count(x => x.Curve == c1));
        var s1 = curves.Single(c => c!.Spline != null)!.Spline!;
        Assert.Equal(SplineKind.DogBone, s1.Kind);
        Assert.Equal(3, s1.ControlPoints.Count);
        Assert.Equal(1.5, s1.Parameter);
    }
}
