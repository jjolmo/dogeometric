using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>rp_sphere's sphere: a closed UV sphere of quads (triangles at the poles), faces out, edges smoothed.</summary>
public static class Sphere
{
    /// <summary>Adds the sphere to <paramref name="e"/>; <paramref name="segments"/> around the equator, half as many from pole to pole.</summary>
    public static void Add(Entities e, Vec3 centre, double radius, int segments)
    {
        var around = Math.Max(3, segments);
        var rings = Math.Max(2, around / 2);
        Vec3 P(int ring, int k)
        {
            var lat = Math.PI * ring / rings - Math.PI / 2;
            var lon = 2 * Math.PI * k / around;
            return centre + new Vec3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat)) * radius;
        }
        var weld = new Welder(e);
        var faces = new List<Face>();
        for (var ring = 0; ring < rings; ring++)
            for (var k = 0; k < around; k++)
            {
                var k1 = (k + 1) % around;
                List<Vec3> pts = ring == 0 ? [P(0, 0), P(1, k1), P(1, k)]
                    : ring == rings - 1 ? [P(ring, k), P(ring, k1), P(rings, 0)]
                    : [P(ring, k), P(ring, k1), P(ring + 1, k1), P(ring + 1, k)];
                // Counter-clockwise seen from outside, so fronts face out.
                if (Polygon.Normal(pts).Dot(pts.Aggregate(Vec3.Zero, (a, p) => a + p) / pts.Count - centre) < 0)
                    pts.Reverse();
                faces.Add(weld.Face(pts, []));
            }
        foreach (var edge in faces.SelectMany(Topology.EdgesOf).Distinct())
            edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
    }
}
