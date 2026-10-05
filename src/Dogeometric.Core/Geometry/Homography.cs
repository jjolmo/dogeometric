namespace Dogeometric.Core.Geometry;

/// <summary>
/// The projective map taking four plane points onto four others (Position Texture's free pins), as a 3×3 matrix in
/// row order: (x, y) ↦ ((h0 x + h1 y + h2) / w, (h3 x + h4 y + h5) / w) with w = h6 x + h7 y + h8.
/// </summary>
public static class Homography
{
    /// <summary>The map from <paramref name="from"/> to <paramref name="to"/> (4 points each), or null when they are degenerate.</summary>
    public static double[]? Solve(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to)
    {
        // Eight equations in h0..h7, with h8 = 1.
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = from[i];
            var (u, v) = to[i];
            double[] r1 = [x, y, 1, 0, 0, 0, -u * x, -u * y, u];
            double[] r2 = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
            for (var k = 0; k < 9; k++)
            {
                a[2 * i, k] = r1[k];
                a[2 * i + 1, k] = r2[k];
            }
        }
        for (var col = 0; col < 8; col++)
        {
            var pivot = Enumerable.Range(col, 8 - col).MaxBy(r => Math.Abs(a[r, col]));
            if (Math.Abs(a[pivot, col]) < 1e-12)
                return null;
            for (var k = 0; k < 9; k++)
                (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
            for (var r = 0; r < 8; r++)
            {
                if (r == col)
                    continue;
                var f = a[r, col] / a[col, col];
                for (var k = col; k < 9; k++)
                    a[r, k] -= f * a[col, k];
            }
        }
        var h = new double[9];
        for (var i = 0; i < 8; i++)
            h[i] = a[i, 8] / a[i, i];
        h[8] = 1;
        // A singular map squashes the plane onto a line: the pins don't fix a placement.
        var scale = h.Max(Math.Abs);
        return Math.Abs(Determinant(h)) < 1e-12 * scale * scale * scale ? null : h;
    }

    private static double Determinant(double[] h) =>
        h[0] * (h[4] * h[8] - h[5] * h[7]) - h[1] * (h[3] * h[8] - h[5] * h[6]) + h[2] * (h[3] * h[7] - h[4] * h[6]);

    public static (double X, double Y) Apply(double[] h, (double X, double Y) p)
    {
        var w = h[6] * p.X + h[7] * p.Y + h[8];
        return ((h[0] * p.X + h[1] * p.Y + h[2]) / w, (h[3] * p.X + h[4] * p.Y + h[5]) / w);
    }

    public static double[]? Invert(double[] h)
    {
        var det = Determinant(h);
        if (Math.Abs(det) < 1e-15)
            return null;
        return
        [
            (h[4] * h[8] - h[5] * h[7]) / det, (h[2] * h[7] - h[1] * h[8]) / det, (h[1] * h[5] - h[2] * h[4]) / det,
            (h[5] * h[6] - h[3] * h[8]) / det, (h[0] * h[8] - h[2] * h[6]) / det, (h[2] * h[3] - h[0] * h[5]) / det,
            (h[3] * h[7] - h[4] * h[6]) / det, (h[1] * h[6] - h[0] * h[7]) / det, (h[0] * h[4] - h[1] * h[3]) / det,
        ];
    }
}
