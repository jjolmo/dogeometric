using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>Google Earth (.kmz) export as SketchUp writes it: a KML placing models/&lt;name&gt;.dae at the model's
/// geo-location (Shadows › latitude and longitude), turned to its north angle.</summary>
public static class KmzWriter
{
    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";

    public static void Write(Model model, IReadOnlyList<Triangle> triangles, string path, string name)
    {
        var file = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        if (file.Length == 0)
            file = "model";
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var shadows = model.Shadows;
        var kml = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Kml + "kml",
                new XElement(Kml + "Folder",
                    new XElement(Kml + "name", name),
                    new XElement(Kml + "Placemark",
                        new XElement(Kml + "name", "Model"),
                        new XElement(Kml + "Model",
                            new XElement(Kml + "altitudeMode", "relativeToGround"),
                            new XElement(Kml + "Location",
                                new XElement(Kml + "latitude", N(shadows.Latitude)),
                                new XElement(Kml + "longitude", N(shadows.Longitude)),
                                new XElement(Kml + "altitude", "0")),
                            new XElement(Kml + "Orientation",
                                new XElement(Kml + "heading", N(-shadows.NorthAngle)),
                                new XElement(Kml + "tilt", "0"),
                                new XElement(Kml + "roll", "0")),
                            new XElement(Kml + "Scale", new XElement(Kml + "x", "1"), new XElement(Kml + "y", "1"), new XElement(Kml + "z", "1")),
                            new XElement(Kml + "Link", new XElement(Kml + "href", $"models/{file}.dae")))))));
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var entry = zip.CreateEntry("doc.kml").Open())
            kml.Save(entry);
        using var dae = zip.CreateEntry($"models/{file}.dae").Open();
        DaeWriter.Write(triangles, dae);
    }
}
