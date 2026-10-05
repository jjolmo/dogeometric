using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class CameraObjectsTests
{
    [Fact]
    public void A_camera_group_gives_back_the_view_it_was_made_from_and_survives_saving()
    {
        var model = new Model();
        var view = new CameraState(new Vec3(-500, -800, 300), new Vec3(100, 200, 50), Vec3.UnitZ, true, 40, 1000);
        var inst = CameraObjects.Create(model, model.Entities, "Camera 1", view, 16.0 / 9, 2000);
        Assert.Equal(CameraObjects.CamerasTag, inst.Tag!.Name);
        Assert.Contains(inst.Definition.Entities.Edges, x => x.Tag?.Name == CameraObjects.FrustumLinesTag);
        Assert.Equal(4, inst.Definition.Entities.Faces.Count(f => f.Tag?.Name == CameraObjects.FrustumVolumeTag));

        var path = Path.Combine(Path.GetTempPath(), $"camera-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Entities.Instances.Single();
        File.Delete(path);
        var seen = CameraObjects.ViewOf(back, Transform.Identity)!.Value;
        Assert.True(seen.Eye.DistanceTo(view.Eye) < 1e-9);
        Assert.True((seen.Target - seen.Eye).Normalized().Dot((view.Target - view.Eye).Normalized()) > 1 - 1e-9);
        Assert.Equal(40, seen.FovDegrees);
        Assert.Equal(16.0 / 9, back.Definition.Camera!.Aspect, 9);
        Assert.True(seen.Up.Z > 0.9);
    }
}
