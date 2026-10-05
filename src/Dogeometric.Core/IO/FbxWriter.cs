using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>FBX 7.5 ASCII export laid out as SketchUp writes it (Y up, a null per group holding a mesh and Phong materials),
/// but declaring millimetres, where SketchUp's reads ten times too big.</summary>
public static class FbxWriter
{
    /// <summary>Writes the model (or the selection) and returns how many meshes went out; textured materials hand their
    /// pictures to <paramref name="saveImage"/> under <paramref name="imageFolder"/> (left untextured without it).</summary>
    public static int Write(Model model, TextWriter w, ExportOptions? options = null, string? imageFolder = null, Action<string, byte[]>? saveImage = null)
    {
        options ??= new ExportOptions();
        var root = options.SelectionContext ?? model.Entities;
        var rootXf = options.SelectionContext != null ? options.SelectionContextTransform : Transform.Identity;
        bool Wanted(object e) => options.Selection == null || options.Selection.Contains(e);
        static string N(double v) => v.ToString("0.###############", CultureInfo.InvariantCulture);
        static string Text(string s) => s.Replace("\"", "'");

        var groups = new List<(string Name, List<Triangle> Triangles)>();
        foreach (var inst in root.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false } && Wanted(i)))
            groups.Add((inst.Name.Length > 0 ? inst.Name : inst.Definition.Name,
                MeshExtractor.ExtractInstance(inst, includeHidden: false).Select(t => t with { A = rootXf.ApplyPoint(t.A), B = rootXf.ApplyPoint(t.B), C = rootXf.ApplyPoint(t.C) }).ToList()));
        var loose = MeshExtractor.Extract(model, new ExportOptions
        {
            Selection = root.Faces.Where(f => Wanted(f)).Cast<object>().ToHashSet(),
            SelectionContext = root,
            SelectionContextTransform = rootXf,
        });

        var nextId = 1000L;
        var objects = new StringBuilder();
        var connections = new List<(long Child, long Parent, string? Property)>();
        var materialIds = new Dictionary<Material, long>();
        var imageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (models, geometries, textures) = (0, 0, 0);
        bool Textured(Material? m) => m?.Texture is { Data.Length: > 0 } && saveImage != null;
        // FBX is Y up: SketchUp's (x, y, z) goes out as (x, z, -y).
        static string P(Vec3 p) => $"{N(p.X)},{N(p.Z)},{N(-p.Y)}";

        long Null(string name, long parent)
        {
            var id = nextId++;
            objects.Append($"\tModel: {id}, \"Model::{Text(name)}\", \"Null\" {{\n\t\tVersion: 232\n\t\tShading: Y\n\t\tCulling: \"CullingOff\"\n\t}}\n");
            connections.Add((id, parent, null));
            models++;
            return id;
        }

        void Mesh(string name, List<Triangle> triangles, long parent)
        {
            if (triangles.Count == 0)
                return;
            var meshModel = nextId++;
            var geometry = nextId++;
            var used = triangles.Select(t => t.Material).Distinct().ToList();
            var vertices = string.Join(",", triangles.SelectMany(t => new[] { P(t.A), P(t.B), P(t.C) }));
            var indices = string.Join(",", Enumerable.Range(0, triangles.Count).Select(i => $"{3 * i},{3 * i + 1},{-(3 * i + 2) - 1}"));
            var normals = string.Join(",", triangles.SelectMany(t => Enumerable.Repeat(P(t.Normal), 3)));
            var materials = string.Join(",", triangles.Select(t => used.IndexOf(t.Material)));
            var withUv = triangles.Any(t => t.Uv != null && Textured(t.Material));
            objects.Append($"\tGeometry: {geometry}, \"Geometry::\", \"Mesh\" {{\n")
                .Append($"\t\tVertices: *{9 * triangles.Count} {{\n\t\t\ta: {vertices}\n\t\t}}\n")
                .Append($"\t\tPolygonVertexIndex: *{3 * triangles.Count} {{\n\t\t\ta: {indices}\n\t\t}}\n")
                .Append("\t\tGeometryVersion: 124\n")
                .Append("\t\tLayerElementNormal: 0 {\n\t\t\tVersion: 102\n\t\t\tName: \"\"\n\t\t\tMappingInformationType: \"ByPolygonVertex\"\n\t\t\tReferenceInformationType: \"Direct\"\n")
                .Append($"\t\t\tNormals: *{9 * triangles.Count} {{\n\t\t\t\ta: {normals}\n\t\t\t}}\n\t\t}}\n")
                .Append("\t\tLayerElementMaterial: 0 {\n\t\t\tVersion: 101\n\t\t\tName: \"\"\n\t\t\tMappingInformationType: \"ByPolygon\"\n\t\t\tReferenceInformationType: \"IndexToDirect\"\n")
                .Append($"\t\t\tMaterials: *{triangles.Count} {{\n\t\t\t\ta: {materials}\n\t\t\t}}\n\t\t}}\n");
            if (withUv)
            {
                var uvs = string.Join(",", triangles.SelectMany(t => t.Uv is { } uv && Textured(t.Material) ? new[] { uv.A, uv.B, uv.C } : new (double, double)[3])
                    .Select(q => $"{N(q.Item1)},{N(q.Item2)}"));
                objects.Append("\t\tLayerElementUV: 0 {\n\t\t\tVersion: 101\n\t\t\tName: \"UVSet0\"\n\t\t\tMappingInformationType: \"ByPolygonVertex\"\n\t\t\tReferenceInformationType: \"IndexToDirect\"\n")
                    .Append($"\t\t\tUV: *{6 * triangles.Count} {{\n\t\t\t\ta: {uvs}\n\t\t\t}}\n")
                    .Append($"\t\t\tUVIndex: *{3 * triangles.Count} {{\n\t\t\t\ta: {string.Join(",", Enumerable.Range(0, 3 * triangles.Count))}\n\t\t\t}}\n\t\t}}\n");
            }
            objects.Append("\t\tLayer: 0 {\n\t\t\tVersion: 100\n")
                .Append("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementNormal\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n")
                .Append("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementMaterial\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n")
                .Append(withUv ? "\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementUV\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n" : "")
                .Append("\t\t}\n\t}\n");
            objects.Append($"\tModel: {meshModel}, \"Model::{Text(name)}\", \"Mesh\" {{\n\t\tVersion: 232\n\t\tProperties70:  {{\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0\n\t\t}}\n\t\tShading: T\n\t\tCulling: \"CullingOff\"\n\t}}\n");
            connections.Add((meshModel, parent, null));
            connections.Add((geometry, meshModel, null));
            foreach (var m in used)
                connections.Add((MaterialId(m), meshModel, null));
            models++;
            geometries++;
        }

        long MaterialId(Material? m)
        {
            var key = m ?? Default;
            if (materialIds.TryGetValue(key, out var id))
                return id;
            id = materialIds[key] = nextId++;
            var c = key.Color;
            var (r, g, b) = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
            objects.Append($"\tMaterial: {id}, \"Material::{Text(key.Name)}\", \"\" {{\n\t\tVersion: 102\n\t\tShadingModel: \"phong\"\n\t\tMultiLayer: 0\n\t\tProperties70:  {{\n")
                .Append("\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0,0,0\n")
                .Append($"\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",{N(r)},{N(g)},{N(b)}\n")
                .Append("\t\t\tP: \"TransparentColor\", \"Color\", \"\", \"A\",1,1,1\n")
                .Append($"\t\t\tP: \"TransparencyFactor\", \"Number\", \"\", \"A\",{N(1 - key.Opacity)}\n")
                .Append("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.33,0.33,0.33\n")
                .Append($"\t\t\tP: \"Diffuse\", \"Vector3D\", \"Vector\", \"\",{N(r)},{N(g)},{N(b)}\n")
                .Append("\t\t\tP: \"Shininess\", \"double\", \"Number\", \"\",20\n")
                .Append($"\t\t\tP: \"Opacity\", \"double\", \"Number\", \"\",{N(key.Opacity)}\n\t\t}}\n\t}}\n");
            if (Textured(m))
            {
                var file = Path.GetFileName(m!.Texture!.FileName) is { Length: > 0 } f ? f : "texture.png";
                var (stem, ext) = (Path.GetFileNameWithoutExtension(file), Path.GetExtension(file));
                for (var i = 1; !imageFiles.Add(file); i++)
                    file = $"{stem}{i}{ext}";
                var relative = string.IsNullOrEmpty(imageFolder) ? file : $"{imageFolder}/{file}";
                saveImage!(relative, m.Texture.Data);
                var (texture, video) = (nextId++, nextId++);
                objects.Append($"\tVideo: {video}, \"Video::{Text(key.Name)}\", \"Clip\" {{\n\t\tType: \"Clip\"\n\t\tProperties70:  {{\n\t\t\tP: \"Path\", \"KString\", \"XRefUrl\", \"\", \"{Text(relative)}\"\n\t\t}}\n")
                    .Append($"\t\tUseMipMap: 0\n\t\tFilename: \"{Text(relative)}\"\n\t\tRelativeFilename: \"{Text(relative)}\"\n\t}}\n")
                    .Append($"\tTexture: {texture}, \"Texture::{Text(key.Name)}\", \"\" {{\n\t\tType: \"TextureVideoClip\"\n\t\tVersion: 202\n\t\tTextureName: \"Texture::{Text(key.Name)}\"\n")
                    .Append($"\t\tMedia: \"Video::{Text(key.Name)}\"\n\t\tFileName: \"{Text(relative)}\"\n\t\tRelativeFilename: \"{Text(relative)}\"\n")
                    .Append("\t\tModelUVTranslation: 0,0\n\t\tModelUVScaling: 1,1\n\t\tTexture_Alpha_Source: \"None\"\n\t\tCropping: 0,0,0,0\n\t}\n");
                connections.Add((video, texture, null));
                connections.Add((texture, id, "DiffuseColor"));
                textures++;
            }
            return id;
        }

        var top = Null("Model", 0);
        var meshes = 0;
        foreach (var (name, triangles) in groups.Where(g => g.Triangles.Count > 0))
        {
            Mesh(name + "-Mesh", triangles, Null(name, top));
            meshes++;
        }
        if (loose.Count > 0)
        {
            Mesh("Mesh", loose, top);
            meshes++;
        }

        var now = DateTime.Now;
        w.Write("; FBX 7.5.0 project file\n; Created by Dogeometric\n; ----------------------------------------------------\n\n");
        w.Write("FBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7500\n\tCreationTimeStamp:  {\n\t\tVersion: 1000\n");
        w.Write($"\t\tYear: {now.Year}\n\t\tMonth: {now.Month}\n\t\tDay: {now.Day}\n\t\tHour: {now.Hour}\n\t\tMinute: {now.Minute}\n\t\tSecond: {now.Second}\n\t\tMillisecond: {now.Millisecond}\n\t}}\n");
        w.Write("\tCreator: \"Dogeometric\"\n}\n");
        w.Write("GlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n");
        foreach (var (key, value) in new[] { ("UpAxis", 1), ("UpAxisSign", 1), ("FrontAxis", 2), ("FrontAxisSign", 1), ("CoordAxis", 0), ("CoordAxisSign", 1), ("OriginalUpAxis", 2), ("OriginalUpAxisSign", 1) })
            w.Write($"\t\tP: \"{key}\", \"int\", \"Integer\", \"\",{value}\n");
        // FBX counts in centimetres: millimetres are a tenth.
        w.Write("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",0.1\n\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",0.1\n\t}\n}\n");
        w.Write("Documents:  {\n\tCount: 1\n\tDocument: 1, \"\", \"Scene\" {\n\t\tRootNode: 0\n\t}\n}\nReferences:  {\n}\n");
        w.Write($"Definitions:  {{\n\tVersion: 100\n\tCount: {1 + models + geometries + materialIds.Count + 2 * textures}\n");
        w.Write($"\tObjectType: \"GlobalSettings\" {{\n\t\tCount: 1\n\t}}\n\tObjectType: \"Model\" {{\n\t\tCount: {models}\n\t}}\n");
        w.Write($"\tObjectType: \"Geometry\" {{\n\t\tCount: {geometries}\n\t}}\n\tObjectType: \"Material\" {{\n\t\tCount: {materialIds.Count}\n\t}}\n");
        if (textures > 0)
            w.Write($"\tObjectType: \"Texture\" {{\n\t\tCount: {textures}\n\t}}\n\tObjectType: \"Video\" {{\n\t\tCount: {textures}\n\t}}\n");
        w.Write("}\n");
        w.Write("Objects:  {\n");
        w.Write(objects.ToString());
        w.Write("}\nConnections:  {\n");
        foreach (var (child, parent, property) in connections)
            w.Write(property == null ? $"\tC: \"OO\",{child},{parent}\n" : $"\tC: \"OP\",{child},{parent}, \"{property}\"\n");
        w.Write("}\nTakes:  {\n\tCurrent: \"\"\n}\n");
        return meshes;
    }

    private static readonly Material Default = new() { Name = "Default", Color = new Rgba(255, 255, 255) };
}
