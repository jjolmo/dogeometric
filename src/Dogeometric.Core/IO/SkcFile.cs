using System.IO.Compression;
using System.Xml.Linq;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>SketchUp classification files (.skc): a zip with document.xml naming the schema's XSD, documentProperties.xml
/// with its title, and the XSD with an optional .filter listing the types offered and their attributes.</summary>
public static class SkcFile
{
    public static ClassificationSchema Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFileNameWithoutExtension(path));
    }

    public static ClassificationSchema Read(Stream stream, string fallbackName)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        XDocument? Xml(string name) => zip.GetEntry(name) is { } e ? XDocument.Load(e.Open()) : null;
        string? Text(string name)
        {
            if (zip.GetEntry(name) is not { } e)
                return null;
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        }

        var xsd = Xml("document.xml")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Classification")?.Attribute("xsdFile")?.Value
            ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase) && !e.FullName.StartsWith("__MACOSX"))?.FullName
            ?? throw new InvalidDataException("the classification file names no schema");
        var properties = Xml("documentProperties.xml")?.Descendants().ToList() ?? [];
        string? Property(string name) => properties.FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        var schema = new ClassificationSchema
        {
            Name = Property("title") is { Length: > 0 } title ? title : fallbackName,
            Description = Property("description") ?? "",
        };

        if (Text(xsd + ".filter") is { } filter)
            foreach (var (type, attributes) in Filter(filter))
                schema.Types.Add((type, attributes));
        else if (Xml(xsd) is { } document)
            // Without a filter every element the schema declares at its top level is a type.
            foreach (var element in document.Root?.Elements().Where(e => e.Name.LocalName == "element") ?? [])
                if (element.Attribute("name")?.Value is { Length: > 0 } name)
                    schema.Types.Add((name, []));
        if (schema.Types.Count == 0)
            throw new InvalidDataException($"{schema.Name} has no types");
        return schema;
    }

    /// <summary>A .filter's "Type { Attribute … }" blocks; "// Blacklist Attributes" blocks list attributes to hide.</summary>
    private static IEnumerable<(string Type, List<string> Attributes)> Filter(string text)
    {
        string? type = null;
        List<string>? attributes = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line == "{")
                attributes = [];
            else if (line == "}")
            {
                if (type != null && attributes != null)
                    yield return (type, attributes);
                (type, attributes) = (null, null);
            }
            else if (attributes != null)
            {
                if (type != null)
                    attributes.Add(line);
            }
            else
                type = line.StartsWith("//") ? null : line;
        }
    }
}
