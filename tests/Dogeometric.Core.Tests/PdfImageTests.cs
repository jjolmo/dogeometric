using System.Text;
using Dogeometric.Core.IO;

namespace Dogeometric.Core.Tests;

public class PdfImageTests
{
    // A 40 × 20 JPEG, red on the left and blue on the right.
    private static readonly byte[] Jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAAUACgDAREAAhEBAxEB/8QAFwABAQEBAAAAAAAAAAAAAAAAAAYHCP/EABgQAQADAQAAAAAAAAAAAAAAAAAHRYPC/8QAFwEBAQEBAAAAAAAAAAAAAAAAAAgJB//EABkRAQADAQEAAAAAAAAAAAAAAAAIRYTDxP/aAAwDAQACEQMRAD8A50cMapgIGVavfhccMrvN6Ecyzp9HBALjR0A31hy2LAQMq1e/C44ZXeb0I5lnT6OCAXGjoBvrDlsWAgZVq9+Fxwyu83oRzLOn0cEAuNHQD//Z");

    [Fact]
    public void The_picture_is_embedded_as_a_jpeg_filling_the_page()
    {
        var pdf = PdfImage.FromJpeg(Jpeg, 40, 20, 400, 200);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/MediaBox [0 0 400 200]", text);
        Assert.Contains("/Width 40 /Height 20", text);
        Assert.Contains("/Filter /DCTDecode /Length " + Jpeg.Length, text);
        Assert.Contains("q 400 0 0 200 0 0 cm /Im0 Do Q", text);
        // The cross-reference table points at each object.
        var xref = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[xref..]);
    }
}
