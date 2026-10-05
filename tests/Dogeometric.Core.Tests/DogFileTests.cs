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
        var a = new SectionPlane(new Vec3(0, 200, 0), new Vec3(0, 1, 0)) { Name = "Front", Symbol = "F1" };
        var b = new SectionPlane(new Vec3(0, 0, 100), new Vec3(0, 0, -1));
        model.Entities.SectionPlanes.AddRange([a, b]);
        model.Entities.ActiveSection = b;
        var back = RoundTrip(model);
        Assert.Equal(2, back.Entities.SectionPlanes.Count);
        Assert.Equal("Front", back.Entities.SectionPlanes[0].Name);
        Assert.Equal("F1", back.Entities.SectionPlanes[0].Symbol);
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

    [Fact]
    public void Animation_settings_are_saved()
    {
        var m = new Model { SceneTransitions = false, SceneTransitionSeconds = 3.5, SceneDelaySeconds = 1 };
        var path = Path.Combine(Path.GetTempPath(), $"anim-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(m, path);
            var back = DogFile.Load(path);
            Assert.Equal((false, 3.5, 1.0), (back.SceneTransitions, back.SceneTransitionSeconds, back.SceneDelaySeconds));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dimension_and_text_settings_are_saved_and_undone()
    {
        var m = new Model { Dimensions = new DimensionStyle { FontSize = 20, Endpoints = DimensionEndpoint.Dot, Font = "Serif", AlignToScreen = false, Position = DimensionTextPosition.Centered, ShowRadialPrefix = false }, LeaderText = new TextStyle { FontSize = 9, Leader = LeaderType.Hidden, Endpoint = DimensionEndpoint.OpenArrow } };
        m.DimensionDisplay = m.DimensionDisplay with { HideSmall = false, ForeshortenedLimit = 0.5 };
        var styled = new LinearDimension(Vec3.Zero, new Vec3(100, 0, 0), new Vec3(0, 50, 0)) { Style = new DimensionStyle { Color = new Rgba(200, 0, 0) } };
        m.Entities.Dimensions.Add(styled);
        m.Entities.Texts.Add(new TextLabel("Note") { Point = Vec3.Zero, Offset = new Vec3(10, 0, 0), LeaderPixels = (30, -20), Style = new TextStyle { Color = new Rgba(0, 0, 200) } });
        var path = Path.Combine(Path.GetTempPath(), $"ann-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(m, path);
            var back = DogFile.Load(path);
            Assert.Equal((m.Dimensions, m.DimensionDisplay, m.LeaderText, m.ScreenText), (back.Dimensions, back.DimensionDisplay, back.LeaderText, back.ScreenText));
            var note = back.Entities.Texts.Single();
            Assert.Equal((30.0, -20.0), note.LeaderPixels);
            Assert.Equal(new Rgba(0, 0, 200), note.Style!.Color);
            Assert.Equal(styled.Style, back.Entities.Dimensions.Single().Style);
        }
        finally
        {
            File.Delete(path);
        }
        var doc = new Document(m);
        doc.Undo.Begin("Dimensions");
        m.Dimensions = m.Dimensions with { Endpoints = DimensionEndpoint.Slash };
        doc.Undo.Commit();
        doc.Undo.Undo();
        Assert.Equal(DimensionEndpoint.Dot, m.Dimensions.Endpoints);
    }

    [Fact]
    public void Model_info_options_survive_saving_and_undo()
    {
        var model = new Model();
        var doc = new Document(model);
        var options = new ModelOptions { Author = "Ada", FadeRest = 0.2, FadeSimilar = 0.9, ShowComponentAxes = true, SmoothTextures = false,
            FogStart = 0.25, FogEnd = 0.8, FogColor = new Rgba(10, 20, 30) };
        doc.Undo.Begin("Model Info");
        model.Options = options;
        doc.Undo.Commit();

        var path = Path.Combine(Path.GetTempPath(), $"options-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Options;
        File.Delete(path);
        Assert.Equal(options, back);

        doc.Undo.Undo();
        Assert.Equal(new ModelOptions(), model.Options);
    }

    [Fact]
    public void Scene_descriptions_and_animation_flags_survive_saving()
    {
        var model = new Model();
        model.Scenes.Add(new Scene { Name = "Front", Description = "Lid closed" });
        model.Scenes.Add(new Scene { Name = "Inside", InAnimation = false });
        var path = Path.Combine(Path.GetTempPath(), $"scenes-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Scenes;
        File.Delete(path);
        Assert.Equal(("Lid closed", true), (back[0].Description, back[0].InAnimation));
        Assert.Equal(("", false), (back[1].Description, back[1].InAnimation));
    }

    [Fact]
    public void Purging_tags_keeps_those_in_use_and_untagged()
    {
        var model = new Model();
        var used = model.GetOrAddTag("Lid");
        model.GetOrAddTag("Unused");
        var def = new ComponentDefinition { IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(1, 1, 1));
        def.Entities.Faces[0].Tag = used;
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        Assert.Equal(1, Grouping.PurgeTags(model));
        Assert.Equal([Tag.UntaggedName, "Lid"], model.Tags.Select(t => t.Name));
    }

    [Fact]
    public void Undo_brings_back_a_tags_colour()
    {
        var model = new Model();
        var tag = model.GetOrAddTag("Lid");
        var before = tag.Color;
        var doc = new Document(model);
        doc.Undo.Begin("Tag Color");
        tag.Color = new Rgba(1, 2, 3);
        doc.Undo.Commit();
        doc.Undo.Undo();
        Assert.Equal(before, tag.Color);
    }

    [Fact]
    public void Component_attributes_survive_saving_and_undo()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Lid" };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(1, 1, 1));
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        var doc = new Document(model);
        doc.Undo.Begin("Attributes");
        def.Attributes.Add(new ComponentAttribute { Name = "Thickness", Value = "2", UserCanEdit = true });
        def.Attributes.Add(new ComponentAttribute { Name = "Supplier", Value = "ACME" });
        doc.Undo.Commit();

        var path = Path.Combine(Path.GetTempPath(), $"attrs-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Definitions.Single().Attributes;
        File.Delete(path);
        Assert.Equal([("Thickness", "2", true), ("Supplier", "ACME", false)], back.Select(a => (a.Name, a.Value, a.UserCanEdit)));

        doc.Undo.Undo();
        Assert.Empty(def.Attributes);
    }

    [Fact]
    public void An_imported_model_becomes_one_component_named_and_aligned_by_its_model_info()
    {
        var part = new Model { Options = new ModelOptions { Name = "Vent", GlueTo = GlueTo.Vertical, CutsOpening = true } };
        TestModels.Box(part.Entities, Vec3.Zero, new Vec3(20, 5, 10));
        var host = new Model();
        var def = Grouping.ImportAsComponent(host, part, "vent.dog");
        Assert.Equal(("Vent", GlueTo.Vertical, true), (def.Name, def.GlueTo, def.CutsOpening));
        Assert.Equal(6, def.Entities.Faces.Count);
        Assert.Contains(def, host.Definitions);
        Assert.Empty(host.Entities.Faces);

        var unnamed = Grouping.ImportAsComponent(host, new Model(), "lid.dog");
        Assert.Equal("lid.dog", unnamed.Name);
    }

    [Fact]
    public void A_component_saved_as_a_model_imports_back_the_same_and_replace_swaps_definitions()
    {
        var model = new Model();
        var vent = new ComponentDefinition { Name = "Vent", GlueTo = GlueTo.Vertical, CutsOpening = true };
        TestModels.Box(vent.Entities, Vec3.Zero, new Vec3(20, 5, 10));
        model.Definitions.Add(vent);
        var inst = model.Entities.AddInstance(vent, Transform.Translation(new Vec3(7, 0, 0)));

        var path = Path.Combine(Path.GetTempPath(), $"vent-{Guid.NewGuid()}.dog");
        DogFile.Save(Grouping.DefinitionAsModel(model, vent), path);
        var back = Grouping.ImportAsComponent(new Model(), DogFile.Load(path), "x");
        File.Delete(path);
        Assert.Equal(("Vent", GlueTo.Vertical, true, 6), (back.Name, back.GlueTo, back.CutsOpening, back.Entities.Faces.Count));

        var other = new ComponentDefinition { Name = "Other" };
        Assert.Equal(1, Grouping.ReplaceDefinition([inst], other));
        Assert.Same(other, inst.Definition);
        Assert.Equal(new Vec3(7, 0, 0), inst.Transform.Origin);
    }

    [Fact]
    public void The_style_survives_saving_and_undo()
    {
        var model = new Model();
        var doc = new Document(model);
        var style = new StyleSettings { Name = "Plans", ProfileWidth = 5, EdgeColorMode = EdgeColorMode.ByAxis, EdgeColor = new Rgba(10, 20, 30),
            Profiles = true, Edges = false, FaceStyle = FaceStyle.XRay, ModelAxes = false, SectionCuts = false, Description = "For plans",
            Endpoints = true, EndpointLength = 12, Jitter = true, XrayOpacity = 0.3, Transparency = false, TransparencyQuality = TransparencyQuality.Nicer,
            BackColor = new Rgba(1, 2, 3), Sky = false, GroundTransparency = 0.4, GroundFromBelow = true,
            LockedColor = new Rgba(9, 9, 9), SectionFillColor = new Rgba(4, 5, 6), SectionCutWidth = 7 };
        doc.Undo.Begin("Style");
        model.Style = style;
        doc.Undo.Commit();
        var path = Path.Combine(Path.GetTempPath(), $"style-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Style;
        File.Delete(path);
        Assert.Equal(style, back);
        doc.Undo.Undo();
        Assert.Equal(new StyleSettings().WithViewOf(style), model.Style);
    }

    [Fact]
    public void Tag_dashes_survive_saving_and_undo()
    {
        var model = new Model();
        var tag = model.GetOrAddTag("Hidden lines");
        var doc = new Document(model);
        doc.Undo.Begin("Dashes");
        tag.Dashes = LineStyle.DashDot;
        doc.Undo.Commit();
        var path = Path.Combine(Path.GetTempPath(), $"dashes-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path);
        File.Delete(path);
        Assert.Equal(LineStyle.DashDot, back.Tags.Single(t => t.Name == "Hidden lines").Dashes);
        Assert.True(back.Style.Dashes);
        doc.Undo.Undo();
        Assert.Equal(LineStyle.Solid, tag.Dashes);
        Assert.Equal([12, 6, 1, 6], LineStyles.Pattern(LineStyles.Parse("dash dot")));
    }

    [Fact]
    public void Watermarks_survive_saving()
    {
        var model = new Model();
        var mark = new Watermark
        {
            Name = "Paper", Image = new TextureImage { FileName = "paper.png", Data = [1, 2, 3, 4] }, Overlay = true, Opacity = 0.3,
            Mask = true, Layout = WatermarkLayout.Positioned, LockAspect = false, Scale = 2, Position = WatermarkPosition.TopLeft,
        };
        model.Style = model.Style with { ShowWatermarks = false, Watermarks = new([mark]) };
        var path = Path.Combine(Path.GetTempPath(), $"marks-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Style;
        File.Delete(path);
        Assert.False(back.ShowWatermarks);
        var read = Assert.Single(back.Watermarks);
        Assert.Equal([1, 2, 3, 4], read.Image.Data);
        Assert.Equal(mark with { Image = read.Image }, read);
        Assert.Equal(new ValueList<int>([1, 2]), new ValueList<int>([1, 2]));
    }

    [Fact]
    public void Undo_keeps_the_view_switches()
    {
        var model = new Model();
        var doc = new Document(model);
        doc.Undo.Begin("Rename");
        model.Options = model.Options with { Name = "Shed" };
        doc.Undo.Commit();
        model.Style = model.Style with { Profiles = true, FaceStyle = FaceStyle.Wireframe };
        doc.Undo.Undo();
        Assert.Equal("", model.Options.Name);
        Assert.True(model.Style.Profiles);
        Assert.Equal(FaceStyle.Wireframe, model.Style.FaceStyle);
    }

    [Fact]
    public void Scenes_remember_style_shadows_axes_and_section_cut()
    {
        var m = new Model();
        var cut = new SectionPlane(new Vec3(0, 50, 0), new Vec3(0, 1, 0));
        m.Entities.SectionPlanes.Add(cut);
        m.Entities.ActiveSection = cut;
        m.Style = m.Style with { FaceStyle = FaceStyle.HiddenLine, Name = "Plans" };
        m.Shadows = m.Shadows with { Enabled = true };
        m.Axes = Transform.Translation(new Vec3(10, 20, 0));
        var roofDef = new ComponentDefinition { Name = "Roof", IsGroup = true };
        m.Definitions.Add(roofDef);
        var roof = m.Entities.AddInstance(roofDef, Transform.Identity);
        roof.Hidden = true;
        var plan = new Scene { Name = "Plan" };
        plan.Capture(m, new CameraState(new Vec3(0, 0, 100), Vec3.Zero, Vec3.UnitY, false, 35, 100));
        var free = new Scene { Name = "Free", Saves = SceneProperties.Camera };
        free.Capture(m, new CameraState(new Vec3(100, 100, 100), Vec3.Zero, Vec3.UnitZ, true, 35, 100));
        m.Scenes.AddRange([plan, free]);

        var back = RoundTrip(m);
        // Change everything, then show each scene.
        back.Style = new StyleSettings();
        back.Shadows = back.Shadows with { Enabled = false };
        back.Axes = Transform.Identity;
        back.Entities.ActiveSection = null;
        back.Entities.Instances[0].Hidden = false;
        back.Scenes[1].Apply(back);
        Assert.False(back.Entities.Instances[0].Hidden);
        Assert.Equal(FaceStyle.ShadedWithTextures, back.Style.FaceStyle);
        Assert.Null(back.Entities.ActiveSection);
        back.Scenes[0].Apply(back);
        Assert.Equal("Plans", back.Style.Name);
        Assert.Equal(FaceStyle.HiddenLine, back.Style.FaceStyle);
        Assert.True(back.Shadows.Enabled);
        Assert.Equal(new Vec3(10, 20, 0), back.Axes.Origin);
        Assert.Same(back.Entities.SectionPlanes[0], back.Entities.ActiveSection);
        Assert.True(back.Entities.Instances[0].Hidden);
        Assert.Equal(SceneProperties.Camera, back.Scenes[1].Saves);
    }
}
