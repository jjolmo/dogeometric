using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using CadColor = ACadSharp.Color;
using Transform = Dogeometric.Core.Geometry.Transform;
using Line = ACadSharp.Entities.Line;

namespace Dogeometric.Formats;

/// <summary>AutoCAD DWG through ACadSharp (MIT): import goes through the DXF importer (blocks as components); 3D export
/// writes DWG or DXF as SketchUp does: faces as 3D faces, edges as lines, tags as layers, in millimetres.</summary>
public static class CadFiles
{
    public static Model LoadDwg(string path) => DxfImport.Read(DwgToDxf(path), Path.GetFileNameWithoutExtension(path));

    /// <summary>The DWG as DXF text holding only model-space lines, curves and 3D faces.</summary>
    public static string DwgToDxf(string path)
    {
        var doc = DwgReader.Read(path);
        Flatten(doc);
        using var stream = new MemoryStream();
        DxfWriter.Write(stream, doc, false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Polyface meshes (SketchUp's faces) turned into 3D faces, in model space and in every block, which stay
    /// blocks so their references come in as components.</summary>
    private static void Flatten(CadDocument doc)
    {
        foreach (var record in doc.BlockRecords.ToList())
            foreach (var mesh in record.Entities.OfType<PolyfaceMesh>().ToList())
            {
                var corners = mesh.Vertices.Select(v => v.Location).ToList();
                var faces = new List<Face3D>();
                foreach (var f in mesh.Faces)
                {
                    var ids = new[] { f.Index1, f.Index2, f.Index3, f.Index4 }.Select(i => Math.Abs((int)i) - 1).Where(i => i >= 0 && i < corners.Count).ToList();
                    if (ids.Count >= 3)
                        faces.Add(new Face3D
                        {
                            FirstCorner = corners[ids[0]], SecondCorner = corners[ids[1]], ThirdCorner = corners[ids[2]],
                            FourthCorner = corners[ids[^1]], Layer = mesh.Layer,
                        });
                }
                record.Entities.Remove(mesh);
                foreach (var face in faces)
                    record.Entities.Add(face);
            }
    }

    /// <summary>Writes the visible model (or the selection) as a .dwg or .dxf by the path's extension; returns the
    /// numbers of 3D faces and lines written.</summary>
    public static (int Faces, int Lines) Write3D(Model model, string path, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var doc = new CadDocument();
        doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Millimeters;
        var layers = new Dictionary<Tag, Layer>();
        Layer LayerOf(Tag? tag)
        {
            if (tag == null)
                return doc.Layers[Layer.DefaultName];
            if (!layers.TryGetValue(tag, out var layer))
            {
                layer = new Layer(tag.Name.Length > 0 ? tag.Name : "Tag") { Color = new CadColor(tag.Color.R, tag.Color.G, tag.Color.B) };
                doc.Layers.Add(layer);
                layers[tag] = layer;
            }
            return layer;
        }
        static XYZ X(Vec3 p) => new(p.X, p.Y, p.Z);

        var (faces, lines) = (0, 0);
        void Walk(Entities entities, Transform xf, Tag? inherited, bool top)
        {
            bool Wanted(object e) => !top || options.Selection == null || options.Selection.Contains(e);
            foreach (var face in entities.Faces.Where(f => !f.Hidden && f.Tag is not { Visible: false } && Wanted(f)))
            {
                var layer = LayerOf(face.Tag ?? inherited);
                var outer = face.OuterLoop.Points.Select(xf.ApplyPoint).ToList();
                var holes = face.Loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Points.Select(xf.ApplyPoint).ToList()).ToList();
                // SketchUp writes a convex face of up to four corners as one 3D face, anything else as triangles.
                if (holes.Count == 0 && outer.Count is 3 or 4 && IsConvex(outer))
                {
                    doc.Entities.Add(new Face3D { FirstCorner = X(outer[0]), SecondCorner = X(outer[1]), ThirdCorner = X(outer[2]), FourthCorner = X(outer[^1]), Layer = layer });
                    faces++;
                    continue;
                }
                var all = outer.Concat(holes.SelectMany(h => h)).ToList();
                var tris = Polygon.Triangulate(outer, holes);
                for (var i = 0; i + 2 < tris.Count; i += 3)
                {
                    var (a, b, c) = (all[tris[i]], all[tris[i + 1]], all[tris[i + 2]]);
                    doc.Entities.Add(new Face3D { FirstCorner = X(a), SecondCorner = X(b), ThirdCorner = X(c), FourthCorner = X(c), Layer = layer });
                    faces++;
                }
            }
            foreach (var edge in entities.Edges.Where(e => !e.Flags.HasFlag(EdgeFlags.Hidden) && e.Tag is not { Visible: false } && Wanted(e)))
            {
                doc.Entities.Add(new Line(X(xf.ApplyPoint(edge.Start.Position)), X(xf.ApplyPoint(edge.End.Position))) { Layer = LayerOf(edge.Tag ?? inherited) });
                lines++;
            }
            foreach (var inst in entities.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
                Walk(inst.Definition.Entities, inst.Transform.Then(xf), inst.Tag ?? inherited, false);
        }
        Walk(options.SelectionContext ?? model.Entities, options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity, null, true);

        static bool IsConvex(List<Vec3> p)
        {
            var n = Polygon.Normal(p);
            return Enumerable.Range(0, p.Count).All(i => (p[(i + 1) % p.Count] - p[i]).Cross(p[(i + 2) % p.Count] - p[(i + 1) % p.Count]).Dot(n) > 0);
        }

        if (Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            DwgWriter.Write(path, doc);
        else
            DxfWriter.Write(path, doc, false);
        return (faces, lines);
    }
}
