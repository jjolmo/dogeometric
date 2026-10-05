using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// How a texture lies on one side of a face, as SketchUp stores it: a 3×3 matrix (row-vector convention,
/// [u v 1]·M = [x y w]) taking texture space to the face plane, both in inches as in .skp files. Faces without one
/// use SketchUp's default projection: the texture tiles at its real-world size from the plane's own axes.
/// </summary>
public sealed record TextureMapping(double[] Matrix)
{
    /// <summary>A mapping that places the texture's (u, v) = (0,0), (1,0), (0,1) corners at three plane points.</summary>
    public static TextureMapping FromPlanePoints((double X, double Y) origin, (double X, double Y) uEnd, (double X, double Y) vEnd, double tileWmm, double tileHmm)
    {
        // Texture space here is in inches of the tile, so a whole tile spans (tileW, tileH) inches.
        const double inch = 25.4;
        var (tw, th) = (tileWmm / inch, tileHmm / inch);
        var ox = origin.X / inch;
        var oy = origin.Y / inch;
        return new TextureMapping([
            (uEnd.X / inch - ox) / tw, (uEnd.Y / inch - oy) / tw, 0,
            (vEnd.X / inch - ox) / th, (vEnd.Y / inch - oy) / th, 0,
            ox, oy, 1]);
    }

    /// <summary>
    /// A perspective mapping that places the tile's four corners (u, v) = (0,0), (1,0), (1,1), (0,1) at four plane
    /// points (mm), as Texture › Position's yellow pin distorts it.
    /// </summary>
    public static TextureMapping FromQuad((double X, double Y) p00, (double X, double Y) p10, (double X, double Y) p11, (double X, double Y) p01,
        double tileWmm, double tileHmm)
    {
        const double inch = 25.4;
        double x0 = p00.X / inch, y0 = p00.Y / inch, x1 = p10.X / inch, y1 = p10.Y / inch;
        double x2 = p11.X / inch, y2 = p11.Y / inch, x3 = p01.X / inch, y3 = p01.Y / inch;
        double sx = x0 - x1 + x2 - x3, sy = y0 - y1 + y2 - y3;
        double g = 0, h = 0;
        if (Math.Abs(sx) > 1e-12 || Math.Abs(sy) > 1e-12)
        {
            double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
            var den = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(den) > 1e-12)
            {
                g = (sx * dy2 - dx2 * sy) / den;
                h = (dx1 * sy - sx * dy1) / den;
            }
        }
        // [s t 1]·M with s, t in tiles; the rows for u and v divide by the tile size in inches.
        var (tw, th) = (tileWmm / inch, tileHmm / inch);
        return new TextureMapping([
            (x1 - x0 + g * x1) / tw, (y1 - y0 + g * y1) / tw, g / tw,
            (x3 - x0 + h * x3) / th, (y3 - y0 + h * y3) / th, h / th,
            x0, y0, 1]);
    }
}

/// <summary>Texture coordinates of faces, the way SketchUp computes them (and OpenSKP reproduces).</summary>
public static class Texturing
{
    private const double MmPerInch = 25.4;

    /// <summary>
    /// The face plane's axes SketchUp projects textures along: x horizontal (perpendicular to the normal and blue),
    /// y completing the frame; a horizontal face uses red and ±green.
    /// </summary>
    public static (Vec3 X, Vec3 Y) PlaneAxes(Vec3 n)
    {
        var c = new Vec3(-n.Y, n.X, 0);
        if (c.Length < 1e-9)
            return (Vec3.UnitX, new Vec3(0, n.Z >= 0 ? 1 : -1, 0));
        var x = c.Normalized();
        return (x, n.Cross(x));
    }

    /// <summary>Point → (u, v) in tiles of the texture for one side of <paramref name="face"/>.</summary>
    public static (double U, double V) Uv(Face face, bool back, Vec3 point, Material material)
    {
        var tex = material.Texture;
        var tileW = tex is { WidthMm: > 1e-9 } ? tex.WidthMm : MmPerInch;
        var tileH = tex is { HeightMm: > 1e-9 } ? tex.HeightMm : MmPerInch;
        var (xr, yr) = PlaneAxes(face.Normal);
        var mapping = back ? face.BackMapping : face.FrontMapping;
        var px = point.Dot(xr) / MmPerInch;
        var py = point.Dot(yr) / MmPerInch;
        if (mapping == null)
            return (px / (tileW / MmPerInch), py / (tileH / MmPerInch));
        var inv = Invert(mapping.Matrix);
        var u = px * inv[0] + py * inv[3] + inv[6];
        var v = px * inv[1] + py * inv[4] + inv[7];
        var q = px * inv[2] + py * inv[5] + inv[8];
        if (Math.Abs(q) < 1e-12)
            q = 1;
        return (u / q / (tileW / MmPerInch), v / q / (tileH / MmPerInch));
    }

    /// <summary>A face point's coordinates (mm) in the projection plane frame, as <see cref="TextureMapping"/> uses.</summary>
    public static (double X, double Y) PlanePoint(Face face, Vec3 point)
    {
        var (xr, yr) = PlaneAxes(face.Normal);
        return (point.Dot(xr), point.Dot(yr));
    }

    /// <summary>
    /// The same placement seen from a face whose normal is reversed: the plane axes of -n are (-x, y), or (x, -y)
    /// for a horizontal face, so that coordinate of the mapping changes sign.
    /// </summary>
    public static TextureMapping? Flipped(TextureMapping? mapping, Vec3 normal)
    {
        if (mapping == null)
            return null;
        var m = (double[])mapping.Matrix.Clone();
        var column = new Vec3(-normal.Y, normal.X, 0).Length < 1e-9 ? 1 : 0;
        for (var row = 0; row < 3; row++)
            m[row * 3 + column] = -m[row * 3 + column];
        return new TextureMapping(m);
    }

    private static double[] Invert(double[] m)
    {
        var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-18)
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        var d = 1 / det;
        return
        [
            (m[4] * m[8] - m[5] * m[7]) * d, (m[2] * m[7] - m[1] * m[8]) * d, (m[1] * m[5] - m[2] * m[4]) * d,
            (m[5] * m[6] - m[3] * m[8]) * d, (m[0] * m[8] - m[2] * m[6]) * d, (m[2] * m[3] - m[0] * m[5]) * d,
            (m[3] * m[7] - m[4] * m[6]) * d, (m[1] * m[6] - m[0] * m[7]) * d, (m[0] * m[4] - m[1] * m[3]) * d,
        ];
    }

    /// <summary>
    /// Texture › Make Unique Texture: the face's side gets its own copy of the material (picture included), named
    /// like SketchUp's ("Brick1"), so editing it leaves the other faces alone. Returns the copy.
    /// </summary>
    public static Material MakeUnique(Model model, Face face, bool back)
    {
        var source = (back ? face.BackMaterial : face.FrontMaterial) ?? throw new InvalidOperationException("The face has no material on that side");
        var name = source.Name;
        for (var i = 1; model.Materials.Any(m => m.Name == name); i++)
            name = source.Name + i;
        var copy = new Material
        {
            Name = name,
            Color = source.Color,
            Opacity = source.Opacity,
            Colorize = source.Colorize,
            Texture = source.Texture is { } t ? new TextureImage { FileName = t.FileName, Data = t.Data, WidthMm = t.WidthMm, HeightMm = t.HeightMm } : null,
        };
        model.Materials.Add(copy);
        if (back)
            face.BackMaterial = copy;
        else
            face.FrontMaterial = copy;
        return copy;
    }
}
