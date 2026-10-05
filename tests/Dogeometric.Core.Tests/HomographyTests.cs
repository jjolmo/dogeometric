using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Tests;

public class HomographyTests
{
    [Fact]
    public void Four_pins_map_onto_their_targets_and_back()
    {
        (double, double)[] from = [(0, 0), (1, 0), (0, 1), (1, 1)];
        (double, double)[] to = [(10, 10), (110, 20), (5, 90), (130, 140)];
        var h = Homography.Solve(from, to)!;
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = Homography.Apply(h, from[i]);
            Assert.Equal(to[i].Item1, x, 9);
            Assert.Equal(to[i].Item2, y, 9);
        }
        var back = Homography.Apply(Homography.Invert(h)!, Homography.Apply(h, (0.3, 0.7)));
        Assert.Equal(0.3, back.X, 9);
        Assert.Equal(0.7, back.Y, 9);
        // Three of the four in a line can't fix a map.
        Assert.Null(Homography.Solve(from, [(0, 0), (1, 0), (2, 0), (5, 5)]));
    }
}
