using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class MakeUniqueTextureTests
{
    [Fact]
    public void A_face_gets_its_own_copy_of_the_material()
    {
        var m = new Model();
        var brick = new Material { Name = "Brick", Color = new Rgba(150, 60, 40), Texture = new TextureImage { FileName = "b.png", Data = [1, 2], WidthMm = 200, HeightMm = 100 } };
        m.Materials.Add(brick);
        var a = m.Entities.AddFace([Vec3.Zero, new Vec3(100, 0, 0), new Vec3(100, 100, 0)]);
        var b = m.Entities.AddFace([new Vec3(0, 0, 50), new Vec3(100, 0, 50), new Vec3(100, 100, 50)]);
        a.FrontMaterial = b.FrontMaterial = brick;

        var copy = Texturing.MakeUnique(m, a, back: false);
        Assert.Equal("Brick1", copy.Name);
        Assert.Same(copy, a.FrontMaterial);
        Assert.Same(brick, b.FrontMaterial);
        Assert.NotSame(brick.Texture, copy.Texture);
        Assert.Equal(200, copy.Texture!.WidthMm);
    }
}
