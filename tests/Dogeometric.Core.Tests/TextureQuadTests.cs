using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class TextureQuadTests
{
    [Fact]
    public void A_quad_mapping_puts_the_tile_corners_on_the_four_pins()
    {
        var e = new Entities();
        var face = new Welder(e).Face([new(0, 0, 0), new(200, 0, 0), new(200, 200, 0), new(0, 200, 0)], []);
        if (face.Normal.Z < 0)
            FaceFinder.Reverse(face);
        var material = new Material { Name = "t", Texture = new TextureImage { FileName = "t.png", Data = [], WidthMm = 50, HeightMm = 40 } };
        (double, double)[] pins = [(10, 10), (90, 20), (120, 140), (5, 100)];
        face.FrontMapping = TextureMapping.FromQuad(pins[0], pins[1], pins[2], pins[3], 50, 40);
        (double U, double V)[] expected = [(0, 0), (1, 0), (1, 1), (0, 1)];
        for (var i = 0; i < 4; i++)
        {
            var (u, v) = Texturing.Uv(face, false, new Vec3(pins[i].Item1, pins[i].Item2, 0), material);
            Assert.Equal(expected[i].U, u, 9);
            Assert.Equal(expected[i].V, v, 9);
        }
    }
}
