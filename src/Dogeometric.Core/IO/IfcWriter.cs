using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// IFC4 export in millimetres, as SketchUp's File › Export › 3D Model › IFC: each top-level group or component is a
/// building element (loose geometry is one more), with its triangles, material colours and tag as the IFC layer.
/// </summary>
public static class IfcWriter
{
    public static int Write(Model model, string path, string name = "Dogeometric", ExportOptions? options = null)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        return Write(model, w, name, options);
    }

    /// <summary>Writes the model and returns how many elements it holds.</summary>
    public static int Write(Model model, TextWriter output, string name = "Dogeometric", ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var lines = new StringBuilder();
        var next = 1;
        int Add(string entity)
        {
            var id = next++;
            lines.Append('#').Append(id).Append('=').Append(entity).Append(";\n");
            return id;
        }

        var person = Add("IFCPERSON($,$,'',$,$,$,$,$)");
        var organisation = Add("IFCORGANIZATION($,'Dogeometric',$,$,$)");
        var owner = Add($"IFCPERSONANDORGANIZATION(#{person},#{organisation},$)");
        var application = Add($"IFCAPPLICATION(#{organisation},'1','Dogeometric','Dogeometric')");
        var history = Add($"IFCOWNERHISTORY(#{owner},#{application},$,.READWRITE.,$,$,$,{DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
        var units = Add($"IFCUNITASSIGNMENT((#{Add("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)")},#{Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)")}))");
        var origin = Add($"IFCAXIS2PLACEMENT3D(#{Add("IFCCARTESIANPOINT((0.,0.,0.))")},$,$)");
        var context = Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#{origin},$)");
        var bodyContext = Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{context},$,.MODEL_VIEW.,$)");
        var project = Add($"IFCPROJECT('{Guid()}',#{history},{Text(name)},$,$,$,$,(#{context}),#{units})");
        var sitePlacement = Add($"IFCLOCALPLACEMENT($,#{origin})");
        var site = Add($"IFCSITE('{Guid()}',#{history},'Site',$,$,#{sitePlacement},$,$,.ELEMENT.,$,$,$,$,$)");
        var buildingPlacement = Add($"IFCLOCALPLACEMENT(#{sitePlacement},#{origin})");
        var building = Add($"IFCBUILDING('{Guid()}',#{history},'Building',$,$,#{buildingPlacement},$,$,.ELEMENT.,$,$,$)");
        var storeyPlacement = Add($"IFCLOCALPLACEMENT(#{buildingPlacement},#{origin})");
        var storey = Add($"IFCBUILDINGSTOREY('{Guid()}',#{history},'Level 0',$,$,#{storeyPlacement},$,$,.ELEMENT.,0.)");
        Add($"IFCRELAGGREGATES('{Guid()}',#{history},$,$,#{project},(#{site}))");
        Add($"IFCRELAGGREGATES('{Guid()}',#{history},$,$,#{site},(#{building}))");
        Add($"IFCRELAGGREGATES('{Guid()}',#{history},$,$,#{building},(#{storey}))");

        var styles = new Dictionary<(Rgba, double), int>();
        int Style(Material? m)
        {
            var colour = m?.Color ?? new Rgba(255, 255, 255);
            var opacity = m?.Opacity ?? 1;
            if (styles.TryGetValue((colour, opacity), out var id))
                return id;
            var rgb = Add($"IFCCOLOURRGB($,{N(colour.R / 255.0)},{N(colour.G / 255.0)},{N(colour.B / 255.0)})");
            var rendering = Add($"IFCSURFACESTYLERENDERING(#{rgb},{N(1 - opacity)},$,$,$,$,$,$,.FLAT.)");
            return styles[(colour, opacity)] = Add($"IFCSURFACESTYLE({Text(m?.Name ?? "Default")},.BOTH.,(#{rendering}))");
        }

        var products = new List<int>();
        var layers = new Dictionary<string, List<int>>();
        var root = options.SelectionContext ?? model.Entities;
        var rootXf = options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity;
        bool Wanted(object e) => options.Selection == null || options.Selection.Contains(e);

        void Element(string elementName, string? description, Tag? tag, List<Triangle> triangles)
        {
            if (triangles.Count == 0)
                return;
            var items = new List<int>();
            foreach (var byMaterial in triangles.GroupBy(t => t.Material))
            {
                var index = new Dictionary<Vec3, int>();
                var points = new List<string>();
                var faces = new List<string>();
                int Point(Vec3 p)
                {
                    if (index.TryGetValue(p, out var i))
                        return i;
                    points.Add($"({N(p.X)},{N(p.Y)},{N(p.Z)})");
                    return index[p] = points.Count;
                }
                foreach (var t in byMaterial)
                    faces.Add($"({Point(t.A)},{Point(t.B)},{Point(t.C)})");
                var pointList = Add($"IFCCARTESIANPOINTLIST3D(({string.Join(",", points)}),$)");
                var faceSet = Add($"IFCTRIANGULATEDFACESET(#{pointList},$,.F.,({string.Join(",", faces)}),$)");
                Add($"IFCSTYLEDITEM(#{faceSet},(#{Style(byMaterial.Key)}),$)");
                items.Add(faceSet);
            }
            var shape = Add($"IFCSHAPEREPRESENTATION(#{bodyContext},'Body','Tessellation',({string.Join(",", items.Select(i => "#" + i))}))");
            var definition = Add($"IFCPRODUCTDEFINITIONSHAPE($,$,(#{shape}))");
            var placement = Add($"IFCLOCALPLACEMENT(#{storeyPlacement},#{origin})");
            products.Add(Add($"IFCBUILDINGELEMENTPROXY('{Guid()}',#{history},{Text(elementName)},{(string.IsNullOrEmpty(description) ? "$" : Text(description))}," +
                $"$,#{placement},#{definition},$,.NOTDEFINED.)"));
            var layer = tag?.Name ?? "Untagged";
            if (!layers.TryGetValue(layer, out var list))
                layers[layer] = list = [];
            list.Add(shape);
        }

        foreach (var inst in root.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
        {
            var triangles = MeshExtractor.ExtractInstance(inst, includeHidden: false).Select(t => Moved(t, rootXf)).ToList();
            Element(inst.Name.Length > 0 ? inst.Name : inst.Definition.Name, inst.Definition.Description, inst.Tag, triangles);
        }
        var loose = MeshExtractor.Extract(model, new ExportOptions
        {
            Selection = root.Faces.Where(f => Wanted(f)).Cast<object>().ToHashSet(),
            SelectionContext = root,
            SelectionContextTransform = rootXf,
        });
        Element("Ungrouped geometry", null, null, loose);

        foreach (var (layer, shapes) in layers.OrderBy(l => l.Key, StringComparer.Ordinal))
            Add($"IFCPRESENTATIONLAYERASSIGNMENT({Text(layer)},$,({string.Join(",", shapes.Select(s => "#" + s))}),$)");
        if (products.Count > 0)
            Add($"IFCRELCONTAINEDINSPATIALSTRUCTURE('{Guid()}',#{history},$,$,({string.Join(",", products.Select(p => "#" + p))}),#{storey})");

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        output.Write("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\n");
        output.Write($"FILE_NAME({Text(name)},'{stamp}',(''),(''),'Dogeometric','Dogeometric','');\n");
        output.Write("FILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n");
        output.Write(lines.ToString());
        output.Write("ENDSEC;\nEND-ISO-10303-21;\n");
        return products.Count;
    }

    private static Triangle Moved(Triangle t, Transform xf) =>
        t with { A = xf.ApplyPoint(t.A), B = xf.ApplyPoint(t.B), C = xf.ApplyPoint(t.C) };

    private static string N(double v)
    {
        var s = v.ToString("0.0#########", CultureInfo.InvariantCulture);
        return s == "-0.0" ? "0.0" : s;
    }

    /// <summary>IFC strings escape quotes by doubling and anything beyond ASCII as \X2\hex\X0\.</summary>
    private static string Text(string s)
    {
        var b = new StringBuilder("'");
        foreach (var c in s)
        {
            if (c == '\'')
                b.Append("''");
            else if (c == '\\')
                b.Append("\\\\");
            else if (c is >= ' ' and <= '~')
                b.Append(c);
            else
                b.Append($"\\X2\\{(int)c:X4}\\X0\\");
        }
        return b.Append('\'').ToString();
    }

    private const string GuidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>A new IFC GlobalId: a GUID's 128 bits in IFC's 22-character base-64.</summary>
    private static string Guid()
    {
        var bytes = System.Guid.NewGuid().ToByteArray();
        var value = new System.Numerics.BigInteger(bytes, isUnsigned: true);
        var chars = new char[22];
        for (var i = 21; i >= 0; i--)
        {
            chars[i] = GuidChars[(int)(value % 64)];
            value /= 64;
        }
        return new string(chars);
    }
}
