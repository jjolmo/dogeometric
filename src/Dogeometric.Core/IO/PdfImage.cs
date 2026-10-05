using System.Text;

namespace Dogeometric.Core.IO;

/// <summary>A one-page PDF showing a JPEG picture (printing the view as it is drawn).</summary>
public static class PdfImage
{
    /// <summary>The picture fills a page of <paramref name="width"/> × <paramref name="height"/> points.</summary>
    public static byte[] FromJpeg(byte[] jpeg, int pixelsWide, int pixelsHigh, double width, double height)
    {
        string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var content = $"q {N(width)} 0 0 {N(height)} 0 0 cm /Im0 Do Q\n";
        using var pdf = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) => pdf.Write(Encoding.ASCII.GetBytes(s));
        void Object(int id, string body, byte[]? stream = null)
        {
            offsets.Add(pdf.Position);
            Write($"{id} 0 obj\n{body}\n");
            if (stream != null)
            {
                Write("stream\n");
                pdf.Write(stream);
                Write("\nendstream\n");
            }
            Write("endobj\n");
        }
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(width)} {N(height)}] /Contents 4 0 R /Resources << /XObject << /Im0 5 0 R >> >> >>");
        Object(4, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>", Encoding.ASCII.GetBytes(content));
        Object(5, $"<< /Type /XObject /Subtype /Image /Width {pixelsWide} /Height {pixelsHigh} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>", jpeg);
        var xref = pdf.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
            Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
}
