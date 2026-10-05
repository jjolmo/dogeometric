using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class OrientFacesTests
{
    [Fact]
    public void A_box_with_flipped_faces_ends_up_facing_out_like_the_chosen_face()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(100, 100, 100));
        var centre = new Vec3(50, 50, 50);
        bool Out(Face f) => f.Normal.Dot(f.OuterLoop.Points.First() - centre) > 0;
        Assert.All(e.Faces, f => Assert.True(Out(f)));

        // Two sides turned inside out; the paint on the outside goes inside with them.
        var paint = new Material { Name = "Paint" };
        var flipped = e.Faces.Where(f => Math.Abs(f.Normal.X) > 0.99).ToList();
        foreach (var f in flipped)
        {
            f.FrontMaterial = paint;
            FaceFinder.Reverse(f);
        }
        Assert.Equal(2, e.Faces.Count(f => !Out(f)));

        var top = e.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99);
        Assert.Equal(2, OrientFaces.Apply(e, top));
        Assert.All(e.Faces, f => Assert.True(Out(f)));
        // Turned back, the paint is on the outside again.
        Assert.All(flipped, f => Assert.Same(paint, f.FrontMaterial));
    }
}
