using System.Globalization;
using System.Text;

namespace Dogeometric.Core.IO;

/// <summary>STL export, in millimetres and Z-up (what slicers expect).</summary>
public static class StlWriter
{
    public static void WriteBinary(IReadOnlyList<Triangle> triangles, Stream stream, string header = "Dogeometric")
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var head = new byte[80];
        Encoding.ASCII.GetBytes(header.Length > 80 ? header[..80] : header).CopyTo(head, 0);
        w.Write(head);
        w.Write((uint)triangles.Count);
        foreach (var t in triangles)
        {
            WriteVec(w, t.Normal);
            WriteVec(w, t.A);
            WriteVec(w, t.B);
            WriteVec(w, t.C);
            w.Write((ushort)0);
        }
    }

    public static void WriteAscii(IReadOnlyList<Triangle> triangles, TextWriter w, string name = "Dogeometric")
    {
        var c = CultureInfo.InvariantCulture;
        w.WriteLine($"solid {name}");
        foreach (var t in triangles)
        {
            var n = t.Normal;
            w.WriteLine(string.Create(c, $"  facet normal {n.X:G9} {n.Y:G9} {n.Z:G9}"));
            w.WriteLine("    outer loop");
            foreach (var p in new[] { t.A, t.B, t.C })
                w.WriteLine(string.Create(c, $"      vertex {p.X:G9} {p.Y:G9} {p.Z:G9}"));
            w.WriteLine("    endloop");
            w.WriteLine("  endfacet");
        }
        w.WriteLine($"endsolid {name}");
    }

    private static void WriteVec(BinaryWriter w, Geometry.Vec3 v)
    {
        w.Write((float)v.X);
        w.Write((float)v.Y);
        w.Write((float)v.Z);
    }
}
