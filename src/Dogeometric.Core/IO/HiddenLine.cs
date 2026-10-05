using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;

namespace Dogeometric.Core.IO;

/// <summary>
/// A hidden-line drawing of the view, as SketchUp's 2D export writes it: the parts of edges no face hides, plus the
/// outlines of smooth surfaces (soft edges on the silhouette), in screen coordinates.
/// </summary>
public static class HiddenLine
{
    public sealed record Segment(double X1, double Y1, double X2, double Y2, bool Profile);

    /// <param name="toScreen">World point to screen pixels, null behind the eye.</param>
    /// <param name="rayTo">The view ray reaching a world point (from the eye, or from far back in parallel views).</param>
    /// <param name="stepPixels">Edges are tested this often along their length on screen.</param>
    public static List<Segment> Visible(Model model, Func<Vec3, (double X, double Y)?> toScreen, Func<Vec3, Ray> rayTo, Picker picker, double stepPixels = 2)
    {
        var result = new List<Segment>();
        foreach (var (a, b, faces) in Edges(model))
        {
            if (toScreen(a) is not { } sa || toScreen(b) is not { } sb)
                continue;
            var mid = (a + b) * 0.5;
            var toward = rayTo(mid).Direction;
            var profile = faces.Count < 2 || (faces[0].Dot(toward) < 0) != (faces[1].Dot(toward) < 0);
            if (faces.Count == 2 && faces.Soft && !profile)
                continue;
            var length = Math.Sqrt((sb.X - sa.X) * (sb.X - sa.X) + (sb.Y - sa.Y) * (sb.Y - sa.Y));
            var steps = Math.Max(1, (int)Math.Ceiling(length / stepPixels));
            // Each piece of the edge is shown when its middle is not behind a face.
            (double X, double Y)? runStart = null;
            for (var i = 0; i < steps; i++)
            {
                double t0 = (double)i / steps, t1 = (double)(i + 1) / steps;
                var shown = !Hidden(model, picker, rayTo, a + (b - a) * ((t0 + t1) / 2));
                (double X, double Y) p0 = (sa.X + (sb.X - sa.X) * t0, sa.Y + (sb.Y - sa.Y) * t0);
                (double X, double Y) p1 = (sa.X + (sb.X - sa.X) * t1, sa.Y + (sb.Y - sa.Y) * t1);
                if (shown)
                    runStart ??= p0;
                if ((!shown || i == steps - 1) && runStart is { } s)
                {
                    var end = shown ? p1 : p0;
                    result.Add(new Segment(s.X, s.Y, end.X, end.Y, faces.Soft || profile && faces.Count == 2));
                    runStart = null;
                }
            }
        }
        return result;
    }

    private static bool Hidden(Model model, Picker picker, Func<Vec3, Ray> rayTo, Vec3 p)
    {
        var ray = rayTo(p);
        var distance = (p - ray.Origin).Dot(ray.Direction);
        var hit = picker.Pick(model.Entities, ray, _ => 0, o => o switch
        {
            Edge => false,
            Face f => !f.Hidden && f.Tag is not { Visible: false },
            ComponentInstance i => !i.Hidden && i.Tag is not { Visible: false },
            _ => true,
        });
        return hit != null && hit.Distance < distance - Math.Max(1e-3, distance * 1e-6);
    }

    private sealed record FaceNormals(List<Vec3> Normals, bool Soft)
    {
        public int Count => Normals.Count;
        public Vec3 this[int i] => Normals[i];
    }

    /// <summary>Drawn edges in world space, with the normals of the faces on them (and whether the edge is soft).</summary>
    private static IEnumerable<(Vec3 A, Vec3 B, FaceNormals Faces)> Edges(Model model)
    {
        var stack = new Stack<(Entities, Transform)>();
        stack.Push((model.Entities, Transform.Identity));
        while (stack.Count > 0)
        {
            var (e, xf) = stack.Pop();
            var normals = new Dictionary<Edge, List<Vec3>>();
            foreach (var f in e.Faces.Where(f => !f.Hidden && f.Tag is not { Visible: false }))
            {
                var n = xf.ApplyNormal(f.Normal).Normalized();
                foreach (var edge in Topology.EdgesOf(f))
                {
                    if (!normals.TryGetValue(edge, out var list))
                        normals[edge] = list = [];
                    list.Add(n);
                }
            }
            foreach (var edge in e.Edges)
            {
                if ((edge.Flags & EdgeFlags.Hidden) != 0 || edge.Tag is { Visible: false })
                    continue;
                var soft = (edge.Flags & EdgeFlags.Soft) != 0;
                var list = normals.GetValueOrDefault(edge) ?? [];
                if (soft && list.Count != 2)
                    continue;
                yield return (xf.ApplyPoint(edge.Start.Position), xf.ApplyPoint(edge.End.Position), new FaceNormals(list, soft));
            }
            foreach (var inst in e.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false }))
                stack.Push((inst.Definition.Entities, inst.Transform.Then(xf)));
        }
    }

    // ------------------------------------------------------------------ writers

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>An SVG drawing <paramref name="width"/> × <paramref name="height"/> pixels; profiles drawn thicker.</summary>
    public static string ToSvg(IEnumerable<Segment> segments, double width, double height)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(width)}\" height=\"{N(height)}\" viewBox=\"0 0 {N(width)} {N(height)}\">\n");
        sb.Append("<g fill=\"none\" stroke=\"#000\" stroke-linecap=\"round\">\n");
        foreach (var s in segments)
            sb.Append($"<line x1=\"{N(s.X1)}\" y1=\"{N(s.Y1)}\" x2=\"{N(s.X2)}\" y2=\"{N(s.Y2)}\" stroke-width=\"{(s.Profile ? "2" : "1")}\"/>\n");
        sb.Append("</g>\n</svg>\n");
        return sb.ToString();
    }

    /// <summary>A one-page PDF of the drawing, one point per pixel; profiles drawn thicker.</summary>
    public static byte[] ToPdf(IEnumerable<Segment> segments, double width, double height)
    {
        var content = new StringBuilder("1 J 1 j 0 G\n");
        foreach (var group in segments.GroupBy(s => s.Profile))
        {
            content.Append(group.Key ? "2 w\n" : "1 w\n");
            // PDF's origin is the bottom left.
            foreach (var s in group)
                content.Append($"{N(s.X1)} {N(height - s.Y1)} m {N(s.X2)} {N(height - s.Y2)} l S\n");
        }
        var stream = content.ToString();
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(width)} {N(height)}] /Contents 4 0 R /Resources << >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
            pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// A DXF drawing of LINEs in millimetres: <paramref name="mmPerPixel"/> turns screen pixels into model size (a
    /// parallel view at full scale), with y turned upwards; profiles on their own layer.
    /// </summary>
    public static string ToDxf(IEnumerable<Segment> segments, double height, double mmPerPixel)
    {
        var sb = new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        foreach (var s in segments)
        {
            sb.Append($"0\nLINE\n8\n{(s.Profile ? "Profiles" : "Edges")}\n");
            sb.Append($"10\n{N(s.X1 * mmPerPixel)}\n20\n{N((height - s.Y1) * mmPerPixel)}\n30\n0\n");
            sb.Append($"11\n{N(s.X2 * mmPerPixel)}\n21\n{N((height - s.Y2) * mmPerPixel)}\n31\n0\n");
        }
        sb.Append("0\nENDSEC\n0\nEOF\n");
        return sb.ToString();
    }
}
