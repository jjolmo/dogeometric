using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class DxfImportTests
{
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>A KiCad-like board: a 100 × 60 outline of lines on Edge.Cuts and four 3.2 mm holes, in millimetres.</summary>
    private static string Board(int insunits = 4)
    {
        var sb = new StringBuilder($"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n{insunits}\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        (double, double)[] c = [(0, 0), (100, 0), (100, 60), (0, 60)];
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (c[i], c[(i + 1) % 4]);
            sb.Append($"0\nLINE\n8\nEdge.Cuts\n10\n{N(a.Item1)}\n20\n{N(a.Item2)}\n30\n0\n11\n{N(b.Item1)}\n21\n{N(b.Item2)}\n31\n0\n");
        }
        foreach (var (x, y) in new[] { (4.0, 4.0), (96.0, 4.0), (96.0, 56.0), (4.0, 56.0) })
            sb.Append($"0\nCIRCLE\n8\nEdge.Cuts\n10\n{N(x)}\n20\n{N(y)}\n30\n0\n40\n1.6\n");
        sb.Append("0\nENDSEC\n0\nEOF\n");
        return sb.ToString();
    }

    [Fact]
    public void A_board_outline_becomes_a_face_with_its_holes()
    {
        var m = DxfImport.Read(Board());
        var e = m.Definitions[0].Entities;
        var board = e.Faces.Single(f => f.Loops.Count == 5);
        Assert.Equal(100 * 60 - 4 * Polygon.Area(Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 1.6, 24)), board.Area, 6);
        Assert.All(e.Edges.Where(x => x.Curve != null), x => Assert.Equal(1.6, x.Curve!.Radius, 9));
        Assert.Contains(m.Tags, t => t.Name == "Edge.Cuts");
    }

    [Fact]
    public void Inches_are_converted()
    {
        var e = DxfImport.Read(Board(insunits: 1)).Definitions[0].Entities;
        Assert.Equal(new Vec3(2540, 1524, 0), e.Bounds().Size);
    }

    [Fact]
    public void A_polyline_with_bulges_rounds_its_corners()
    {
        // A 20 × 10 rectangle with 2 mm corner arcs (bulge tan(90°/4)).
        var b = N(Math.Tan(Math.PI / 8));
        var pts = new[] { (2.0, 0.0, "0"), (18.0, 0.0, b), (20.0, 2.0, "0"), (20.0, 8.0, b), (18.0, 10.0, "0"), (2.0, 10.0, b), (0.0, 8.0, "0"), (0.0, 2.0, b) };
        var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\n0\n90\n8\n70\n1\n");
        foreach (var (x, y, bulge) in pts)
            sb.Append($"10\n{N(x)}\n20\n{N(y)}\n42\n{bulge}\n");
        sb.Append("0\nENDSEC\n0\nEOF\n");
        var e = DxfImport.Read(sb.ToString()).Definitions[0].Entities;
        var face = Assert.Single(e.Faces);
        // The arcs are drawn with 6 segments each, a little inside the true curve.
        Assert.InRange(face.Area, 200 - (4 - Math.PI) * 4 - 0.2, 200 - (4 - Math.PI) * 4);
        Assert.Equal(new Vec3(20, 10, 0), e.Bounds().Size);
    }

    [Fact]
    public void Blocks_become_components_placed_by_their_inserts()
    {
        // A 10 mm square block with its base point at (5, 0), inserted once turned 90° at (100, 0) and once doubled at the origin.
        string Pairs(params (int Code, string Value)[] pairs) => string.Concat(pairs.Select(p => $"{p.Code}\n{p.Value}\n"));
        var dxf = Pairs((0, "SECTION"), (2, "BLOCKS"),
                (0, "BLOCK"), (8, "0"), (2, "SQ"), (10, "5"), (20, "0"), (30, "0"),
                (0, "LWPOLYLINE"), (8, "0"), (70, "1"), (10, "0"), (20, "0"), (10, "10"), (20, "0"), (10, "10"), (20, "10"), (10, "0"), (20, "10"),
                (0, "ENDBLK"), (0, "ENDSEC"),
                (0, "SECTION"), (2, "ENTITIES"),
                (0, "INSERT"), (8, "Parts"), (2, "SQ"), (10, "100"), (20, "0"), (30, "0"), (50, "90"),
                (0, "INSERT"), (8, "0"), (2, "SQ"), (10, "0"), (20, "0"), (30, "0"), (41, "2"), (42, "2"), (43, "2"),
                (0, "ENDSEC"), (0, "EOF"));

        var model = DxfImport.Read(dxf, "blocks");

        var drawing = model.Entities.Instances.Single().Definition.Entities;
        Assert.Equal(2, drawing.Instances.Count);
        var square = Assert.Single(drawing.Instances.Select(i => i.Definition).Distinct());
        Assert.Equal("SQ", square.Name);
        Assert.False(square.IsGroup);
        Assert.Single(square.Entities.Faces);
        Assert.Equal("Parts", drawing.Instances[0].Tag?.Name);
        // The base point goes to the insertion point: turned 90°, the square spans x 100..90, y -5..5.
        var turned = square.Entities.Faces[0].OuterLoop.Points.Select(drawing.Instances[0].Transform.ApplyPoint).ToList();
        Assert.Equal(90, turned.Min(p => p.X), 6);
        Assert.Equal(-5, turned.Min(p => p.Y), 6);
        var doubled = square.Entities.Faces[0].OuterLoop.Points.Select(drawing.Instances[1].Transform.ApplyPoint).ToList();
        Assert.Equal(-10, doubled.Min(p => p.X), 6);
        Assert.Equal(20, doubled.Max(p => p.Y), 6);
    }
}
