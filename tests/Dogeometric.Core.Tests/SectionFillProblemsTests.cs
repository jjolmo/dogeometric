using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SectionFillProblemsTests
{
    [Fact]
    public void A_closed_box_cuts_clean_and_one_with_a_missing_face_shows_where_the_cut_breaks()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(100, 100, 100));
        var plane = new SectionPlane(new Vec3(50, 0, 0), Vec3.UnitX);
        Assert.Empty(SectionFill.Problems(Intersect.Slice(m, plane)));

        // Without its top the box's cut is an open U: its two loose ends are the problems.
        m.Entities.Faces.Remove(m.Entities.Faces.Single(f => f.Normal.Dot(Vec3.UnitZ) > 0.99));
        var problems = SectionFill.Problems(Intersect.Slice(m, plane));
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(100, p.Z, 6));
    }
}
