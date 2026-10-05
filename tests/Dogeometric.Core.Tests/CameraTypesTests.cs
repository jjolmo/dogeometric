using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class CameraTypesTests
{
    [Fact]
    public void The_list_matches_Advanced_Camera_Tools_and_gives_lens_angles()
    {
        Assert.Equal(93, CameraTypes.All.Length);
        Assert.Equal(22, CameraTypes.All.Count(c => c.Category == "Digital/Vision Research®"));
        var fullFrame = CameraTypes.All.Single(c => c.Name == "35mm SLR / Full Frame DSLR");
        // A 50mm lens on a 36mm-wide frame sees about 39.6° across.
        Assert.Equal(39.6, fullFrame.HorizontalFov(50), 1);
        Assert.True(fullFrame.VerticalFov(50) < fullFrame.HorizontalFov(50));
    }
}
