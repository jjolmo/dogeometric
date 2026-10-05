using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Core.IO;

/// <summary>VRML 2.0 (.wrl) export as SketchUp writes it: inches, Z up, a shape per face side, named groups, edges as
/// line sets and the camera as the viewpoint; textures go in a folder named after the file.</summary>
public static class VrmlWriter
{
    private const double MmPerInch = 25.4;

    /// <summary>Writes the model (or the selection) and returns how many faces went out; textured materials hand their
    /// pictures to <paramref name="saveImage"/> under <paramref name="imageFolder"/> (left untextured without it).</summary>
    public static int Write(Model model, TextWriter w, CameraState? camera = null, ExportOptions? options = null,
        string? imageFolder = null, Action<string, byte[]>? saveImage = null)
    {
        options ??= new ExportOptions();
        var appearances = new HashSet<string>();
        var imageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Textured(Material? m) => m?.Texture is { Data.Length: > 0 } && saveImage != null;
        var faces = 0;
        static string N(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
        static string P(Vec3 p) => $"{N(p.X / MmPerInch)} {N(p.Y / MmPerInch)} {N(p.Z / MmPerInch)}";
        static string Name(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        string Appearance(Material? m)
        {
            var name = "COL_" + Name(m?.Name ?? "Default");
            if (!appearances.Add(name))
                return $"USE {name}";
            var colour = m?.Color ?? new Rgba(255, 255, 255);
            var transparency = m is { Opacity: < 1 } ? $" transparency {N(1 - m.Opacity)}" : "";
            var texture = "";
            if (Textured(m))
            {
                var file = Path.GetFileName(m!.Texture!.FileName) is { Length: > 0 } f ? f : "texture.png";
                var (stem, ext) = (Path.GetFileNameWithoutExtension(file), Path.GetExtension(file));
                for (var i = 1; !imageFiles.Add(file); i++)
                    file = $"{stem}{i}{ext}";
                var relative = string.IsNullOrEmpty(imageFolder) ? file : $"{imageFolder}/{file}";
                saveImage!(relative, m.Texture.Data);
                texture = $"\ntexture ImageTexture {{ url \"{relative}\" }}";
            }
            return $"DEF {name} Appearance {{\nmaterial Material {{ diffuseColor {N(colour.R / 255.0)} {N(colour.G / 255.0)} {N(colour.B / 255.0)}{transparency} }}{texture}\n}}";
        }

        void Shape(StringBuilder sb, Material? m, List<List<Vec3>> polygons, List<List<(double U, double V)>>? uvs)
        {
            sb.Append("Shape {\nappearance ").Append(Appearance(m)).Append("\ngeometry IndexedFaceSet {\nsolid TRUE\ncoord Coordinate {\npoint [\n");
            foreach (var p in polygons.SelectMany(x => x))
                sb.Append(P(p)).Append(",\n");
            sb.Append("]\n}\ncoordIndex [\n");
            var at = 0;
            foreach (var polygon in polygons)
            {
                sb.Append(string.Join(" ", Enumerable.Range(at, polygon.Count))).Append(" -1\n");
                at += polygon.Count;
            }
            sb.Append("]\n");
            if (uvs != null)
            {
                // Without a texCoordIndex, VRML indexes the texture points with coordIndex.
                sb.Append("texCoord TextureCoordinate {\npoint [\n");
                foreach (var (u, v) in uvs.SelectMany(x => x))
                    sb.Append($"{N(u)} {N(v)},\n");
                sb.Append("]\n}\n");
            }
            sb.Append("}\n}\n");
        }

        void Walk(StringBuilder sb, Entities entities, Transform xf, Material? inherited, bool top)
        {
            bool Wanted(object e) => !top || options.Selection == null || options.Selection.Contains(e);
            foreach (var face in entities.Faces.Where(f => !f.Hidden && f.Tag is not { Visible: false } && Wanted(f)))
            {
                var outer = face.OuterLoop.Points.ToList();
                var holes = face.Loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
                // A face with holes goes out as triangles; VRML polygons have one outline.
                List<List<Vec3>> local;
                if (holes.Count == 0)
                    local = [outer];
                else
                {
                    var all = outer.Concat(holes.SelectMany(h => h)).ToList();
                    var tris = Polygon.Triangulate(outer, holes);
                    local = Enumerable.Range(0, tris.Count / 3).Select(i => new List<Vec3> { all[tris[3 * i]], all[tris[3 * i + 1]], all[tris[3 * i + 2]] }).ToList();
                }
                if (xf.IsMirroring)
                    local = local.Select(p => Enumerable.Reverse(p).ToList()).ToList();
                var reversed = local.Select(p => Enumerable.Reverse(p).ToList()).ToList();
                List<List<(double, double)>>? Uvs(Material? m, List<List<Vec3>> polygons, bool back) =>
                    Textured(m) ? polygons.Select(p => p.Select(q => Texturing.Uv(face, back, q, m!)).ToList()).ToList() : null;
                var (frontMaterial, backMaterial) = (face.FrontMaterial ?? inherited, face.BackMaterial ?? inherited);
                Shape(sb, frontMaterial, local.Select(p => p.Select(xf.ApplyPoint).ToList()).ToList(), Uvs(frontMaterial, local, false));
                Shape(sb, backMaterial, reversed.Select(p => p.Select(xf.ApplyPoint).ToList()).ToList(), Uvs(backMaterial, reversed, true));
                faces++;
            }
            foreach (var edge in entities.Edges.Where(e => !e.Flags.HasFlag(EdgeFlags.Hidden) && !e.Flags.HasFlag(EdgeFlags.Soft) && e.Tag is not { Visible: false } && Wanted(e)))
            {
                var foreground = appearances.Add("COL_ForegroundColor")
                    ? "DEF COL_ForegroundColor Appearance {\nmaterial Material { diffuseColor 0 0 0 }\n}"
                    : "USE COL_ForegroundColor";
                sb.Append("Shape {\nappearance ").Append(foreground).Append("\ngeometry IndexedLineSet {\ncoord Coordinate {\npoint [ ")
                    .Append(P(xf.ApplyPoint(edge.Start.Position))).Append(",\n").Append(P(xf.ApplyPoint(edge.End.Position)))
                    .Append(",\n]\n}\ncoordIndex [ 0 1 -1]\n}\n}\n");
            }
            foreach (var inst in entities.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
            {
                var name = inst.Name.Length > 0 ? inst.Name : inst.Definition.Name;
                sb.Append($"DEF GRP_{Name(name)} Group {{\nchildren [\n");
                Walk(sb, inst.Definition.Entities, inst.Transform.Then(xf), inst.Material ?? inherited, false);
                sb.Append("]\n}\n");
            }
        }

        var body = new StringBuilder();
        Walk(body, options.SelectionContext ?? model.Entities, options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity, null, true);
        w.Write("#VRML V2.0 utf8\nGroup {\nchildren [\n");
        w.Write(body.ToString());
        w.Write("]\n}\n");
        if (camera is { } c)
        {
            // VRML cameras look down -Z with +Y up; the orientation turns that onto the view.
            var back = (c.Eye - c.Target).Normalized();
            var right = c.Up.Cross(back).Normalized();
            var up = back.Cross(right);
            var (axis, angle) = AxisAngle(right, up, back);
            w.Write($"Viewpoint {{\nposition {P(c.Eye)}\norientation {N(axis.X)} {N(axis.Y)} {N(axis.Z)} {N(angle)}\n" +
                $"fieldOfView {N(c.FovDegrees * Math.PI / 180)}\ndescription \"Camera\"\njump FALSE\n}}\n");
        }
        return faces;
    }

    /// <summary>The rotation whose matrix has columns <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/>.</summary>
    private static (Vec3 Axis, double Angle) AxisAngle(Vec3 x, Vec3 y, Vec3 z)
    {
        var trace = x.X + y.Y + z.Z;
        var angle = Math.Acos(Math.Clamp((trace - 1) / 2, -1, 1));
        if (angle < 1e-9)
            return (Vec3.UnitZ, 0);
        if (Math.PI - angle < 1e-6)
        {
            // Half turns: the axis is the column of the largest diagonal entry, from (R + I) / 2.
            var d = new[] { x.X, y.Y, z.Z };
            var i = Array.IndexOf(d, d.Max());
            var axis = i switch
            {
                0 => new Vec3(x.X + 1, x.Y, x.Z),
                1 => new Vec3(y.X, y.Y + 1, y.Z),
                _ => new Vec3(z.X, z.Y, z.Z + 1),
            };
            return (axis.Normalized(), angle);
        }
        return (new Vec3(y.Z - z.Y, z.X - x.Z, x.Y - y.X).Normalized(), angle);
    }
}
